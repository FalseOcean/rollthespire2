using System.Diagnostics;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Infrastructure.Diagnostics;

namespace RolltheSpire2.Ui.Pages.Search;

// Captured/compiled authority enters here on the main thread. Everything below
// is numerical: no model capture, Godot controls, GPU dispatch or live RunState.
internal sealed record WorkbenchProbabilityPreview(
    JointSelectivityResult Joint,
    SearchProbabilityQuickView? View,
    FamilySearchEtaProjectionV1? Eta,
    IReadOnlyDictionary<string, (double? Cpu, double? Gpu)> Costs,
    JointSelectivityResult? Map,
    IReadOnlyDictionary<EventResultSearchCondition, (SearchSelectivityEstimate Estimate, double? Cpu, double? Gpu)> Events,
    IReadOnlyList<string> Issues)
{
    internal IReadOnlyList<string> AutomaticFamilyOrder { get; init; } = [];
    internal IReadOnlyList<string> FamilyOrder { get; init; } = [];
    internal IReadOnlySet<string> DenseFirstOrderUnits { get; init; } = new HashSet<string>();
    internal string OrderIssue { get; init; } = "";

    internal static WorkbenchProbabilityPreview Compute(CompiledSearch compiled, int target, int workers, bool cpuOnly,
        IReadOnlyList<string>? manualFamilyOrder = null, MapLossyOptions? mapLossy = null)
    {
        var input = SearchSelectivityInput.From(compiled);
        var joint = JointSelectivityEstimator.EstimateQuery(input);
        var costs = new Dictionary<string, (double? Cpu, double? Gpu)>();
        var events = new Dictionary<EventResultSearchCondition, (SearchSelectivityEstimate, double?, double?)>();
        var issues = new List<string>();
        // Party probability rows have their own presentation. Cost and ETA use
        // the same registered/selected unit consumers for both request kinds.
        var view = compiled.Context.Party is null ? SearchProbabilityPresentationBuilder.Build(compiled, joint) : null;
        var q = compiled.NormalizedQuery;
        FamilySearchEtaProjectionV1? eta = null;
        string[] automaticOrder = [], order = [], denseFirst = [];
        string orderIssue = "";
        bool orderResolved = false;
        try
        {
            var execution = ExactSearchExecutionRequestFactory.Compile(compiled,
                new("000000000000", long.MaxValue, target, workers, ManualFamilyOrder: manualFamilyOrder, MapLossy: mapLossy));
            if (execution.Success && execution.Plan is { } request)
            {
                var selected = FamilyExecutionCoordinator.Plan(request, cpuOnly ? false : null);
                order = selected.ExecutionOrderUnits.Select(FamilyExecutionCoordinator.FamilyOrderKey).ToArray();
                denseFirst = selected.ExecutionOrderUnits.Where(FamilyExecutionCoordinator.IsDenseOnlyFamilyOrderUnit)
                    .Select(FamilyExecutionCoordinator.FamilyOrderKey).ToArray();
                orderResolved = true;
                if (manualFamilyOrder is not { Count: > 0 }) automaticOrder = order;
                else
                {
                    try
                    {
                        var automatic = FamilyExecutionCoordinator.Plan(request with
                        { RunOptions = request.RunOptions with { ManualFamilyOrder = null } }, cpuOnly ? false : null);
                        automaticOrder = automatic.ExecutionOrderUnits.Select(FamilyExecutionCoordinator.FamilyOrderKey).ToArray();
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        RuntimeIssueLedger.Current.Record(error, "WorkbenchAutomaticOrderEstimateFailed", "WorkbenchProbabilityPreview", "FamilyOrder",
                            "runtime.issue.workbench_cost_failed", RuntimeIssueSeverity.Warning);
                        issues.Add("workbenchAutomaticOrderUnavailable=" + error.Message);
                    }
                }
                eta = FamilySearchEtaProjectorV1.Project(request, selected);
                var calibration = GpuCostCalibration.Capture();
                var geometry = new FamilyPhysicalQuoteRequest(false, false, 1 << 20);
                foreach (var family in FamilyExecutionCoordinator.CreateRegisteredFamilies(request))
                {
                    var cpu = QuoteConfiguredCpu(family, calibration);
                    var gpu = !cpuOnly && family.ConditionPerformance.UsesGpu
                        ? FamilyPhysicalQuote.Computation(family, geometry, calibration) : null;
                    costs[family.FamilyId] = (cpu?.NanosecondsPerInput, gpu?.NanosecondsPerInput);
                }
            }
            else orderIssue = execution.Issue;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RuntimeIssueLedger.Current.Record(ex, "WorkbenchCostEstimateFailed", "WorkbenchProbabilityPreview", "FamilyCost",
                "runtime.issue.workbench_cost_failed", RuntimeIssueSeverity.Warning);
            issues.Add("workbenchCostUnavailable=" + ex.Message);
            if (!orderResolved) orderIssue = ex.Message;
        }
        JointSelectivityResult? map = q.StandardMaps.Count == 0 && q.StandardMapRoutes.Count == 0 ? null :
            compiled.Context.Party is not null ? StandardMapProbabilityEstimator.EstimateParty(input) :
                JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(SearchCompiler.Compile(
                    SearchQuery.Empty with { StandardMaps = q.StandardMaps, StandardMapRoutes = q.StandardMapRoutes }, compiled.Context)));
        foreach (var c in q.EventResultConditions)
        {
            var estimate = EventResultProbabilityEstimator.EstimateSingle(input, c);
            double? cpuNs = null, gpuNs = null;
            try
            {
                var single = SearchCompiler.Compile(q with { EventResultConditions = [c] }, compiled.Context);
                var request = ExactSearchExecutionRequestFactory.Compile(single, new("000000000000", 1 << 20, 1, workers));
                if (request.Plan is { } plan && EventResultFamily.TryCreate(plan, out var family) && family is not null)
                {
                    var calibration = GpuCostCalibration.Capture();
                    var cpu = QuoteConfiguredCpu(family, calibration);
                    var geometry = new FamilyPhysicalQuoteRequest(false, false, 1 << 20);
                    var gpu = !cpuOnly && family.ConditionPerformance.UsesGpu
                        ? FamilyPhysicalQuote.Computation(family, geometry, calibration) : null;
                    cpuNs = cpu?.NanosecondsPerInput; gpuNs = gpu?.NanosecondsPerInput;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RuntimeIssueLedger.Current.Record(ex, "WorkbenchEventQuoteFailed", "WorkbenchProbabilityPreview", "EventQuote",
                    "runtime.issue.workbench_event_quote_failed", RuntimeIssueSeverity.Warning);
                issues.Add("workbenchEventQuoteUnavailable=" + ex.Message);
            }
            events[c] = (estimate, cpuNs, gpuNs);
        }
        return new(joint, view, eta, costs, map, events, issues) {
            AutomaticFamilyOrder = automaticOrder, FamilyOrder = order,
            DenseFirstOrderUnits = denseFirst.ToHashSet(StringComparer.Ordinal), OrderIssue = orderIssue
        };
    }

    // Match the execution budget. A lower worker count's reference cannot
    // describe this configuration when its own applicable quote is missing.
    private static FamilyPhysicalQuote? QuoteConfiguredCpu(IFamilyInvocation family, GpuCostSnapshot costs) =>
        FamilyCpuExecution.ConfiguredRealizations(family)
            .Select(f => FamilyPhysicalQuote.Computation(f, new(false, false, 1 << 20), costs))
            .Where(c => c is { NanosecondsPerInput: > 0 })
            .OrderBy(c => c!.NanosecondsPerInput).FirstOrDefault();
}

// One running estimate plus the newest requested snapshot. Rapid edits replace
// the pending request; an old completion can never overwrite the current query.
internal sealed class WorkbenchProbabilityPreviewQueue
{
    internal sealed record Request(string Key, CompiledSearch Compiled, int Target, int Workers, bool CpuOnly,
        IReadOnlyList<string>? ManualFamilyOrder = null, MapLossyOptions? MapLossy = null);
    internal sealed record Completion(Request Request, WorkbenchProbabilityPreview? Preview, Exception? Error, double Milliseconds);
    private readonly Func<Request, WorkbenchProbabilityPreview> _compute;
    private Request? _latest;
    private Task<Completion>? _running;

    internal WorkbenchProbabilityPreviewQueue(Func<Request, WorkbenchProbabilityPreview>? compute = null) =>
        _compute = compute ?? (r => WorkbenchProbabilityPreview.Compute(r.Compiled, r.Target, r.Workers, r.CpuOnly, r.ManualFamilyOrder, r.MapLossy));

    internal void Submit(Request request)
    {
        _latest = request;
        if (_running is null) Start(request);
    }

    internal void Invalidate() => _latest = null;

    internal bool TryTake(out Completion? completion)
    {
        completion = null;
        if (_running is not { IsCompleted: true }) return false;
        var finished = _running.GetAwaiter().GetResult();
        _running = null;
        if (ReferenceEquals(finished.Request, _latest)) completion = finished;
        else if (_latest is { } next) Start(next);
        return completion is not null;
    }

    private void Start(Request request)
    {
        _running = Task.Run(() =>
        {
            long start = Stopwatch.GetTimestamp();
            try { return new Completion(request, _compute(request), null, Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException)
                    RuntimeIssueLedger.Current.Record(ex, "WorkbenchProbabilityPreviewFailed", "WorkbenchProbabilityPreviewQueue", "Worker",
                        "runtime.issue.workbench_probability_failed", correlationId: request.Key);
                return new Completion(request, null, ex, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
        });
    }
}
