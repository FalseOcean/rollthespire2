using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record StandardMapFilterPlan(IReadOnlyList<StandardMapSearchCondition> Conditions,
    (int Act, MapRouteSequenceQuery Query)[] Routes, int Ascension, int Players, MapLossyPlan Lossy,
    ElitePrefixResearchCondition? ElitePrefixResearch,
    RngSemanticProfileId RngSemantics = RngSemanticProfileId.Modern110);

internal static class StandardMapFamily
{
    internal const string Id = "M.StandardMap";
    internal static FilterBodyDescription Prepare(SearchModel model, FilterExecutionPreferences preferences,
        bool preferGpu = false, bool float64Available = true)
    {
        var lossy = MapLossyPlan.Create(model, preferences.MapLossy);
        bool int64Available = FamilyDeviceProfileFoundation.Initialization.Capture().VulkanCapabilities?.ShaderInt64 != false;
        bool gpu = lossy.Enabled && preferGpu && float64Available && int64Available;
        string reason = !preferGpu ? "CpuSelectedOrGpuUnavailable" : !float64Available ? "Float64Unavailable" :
            !int64Available ? "Int64Unavailable" : lossy.Enabled ? "GpuSelected" : lossy.Reason;
        var plan = new StandardMapFilterPlan(model.CompiledSearch.NormalizedQuery.StandardMaps,
            model.CompiledSearch.NormalizedQuery.StandardMapRoutes.Select(c => (c.Act, c.ToSequenceQuery())).ToArray(),
            model.Ascension, model.Authority.PlayersCount, lossy, model.CompiledSearch.NormalizedQuery.ElitePrefixResearch,
            model.Authority.VersionResolution.SemanticProfileId);
        var survival = FamilySurvivalProjection.Unresolved(Id, "M.LossyAssignmentSurvivalUnknown;FinalProbabilityIsNotProposalSurvival");
        if (!lossy.Enabled)
        {
            var mapOnly = SearchQuery.Empty with { StandardMaps = model.CompiledSearch.NormalizedQuery.StandardMaps,
                StandardMapRoutes = model.CompiledSearch.NormalizedQuery.StandardMapRoutes,
                ElitePrefixResearch = plan.ElitePrefixResearch };
            var compiled = SearchCompiler.Compile(mapOnly, model.CompiledSearch.Context);
            var estimate = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(compiled));
            survival = estimate.JointlyPriced && estimate.Probability is double probability
                ? FamilySurvivalProjection.Resolved(Id, probability, "M.FinalMapFamilyPredicateProjection;" + estimate.EvidenceCode)
                : FamilySurvivalProjection.Unresolved(Id, "M.FinalMapProbabilityUnavailable;" + estimate.EvidenceCode);
        }
        var prepared = new PreparedFilter(Id)
        {
            AnalyticalCost = new(Id, gpu ? preferences.MapLossy.GpuBatchSize : CpuFilterExecution.Capacity, [],
                "FullFinalReplay;MapPricingDeferredByOwner.20261026"), Survival = survival,
            Obligations = plan.ElitePrefixResearch is null ? [new(Id)] : [new(Id), new("ElitePrefixResearch")]
        };
        var description = new MapLossyExecutionDescription(preferences.MapLossy.Enabled, lossy.Enabled,
            !lossy.Enabled ? "FullFinal" : gpu ? "Gpu" : "Cpu", lossy.Enabled ? MapLossyPlan.Strategy : "FullFinal",
            !lossy.Enabled ? lossy.Reason : reason, lossy.Scope, lossy.Remainders.ToArray());
        return Describe(prepared, plan, RuntimeProfileRegistry.Select(model.Detection), model.SnapshotFingerprint,
            Math.Clamp(preferences.WorkerCount, 1, 32), preferences.MapLossy.GpuBatchSize, gpu, description);
    }

    private static FilterBodyDescription Describe(PreparedFilter prepared, StandardMapFilterPlan plan,
        IRuntimeProfile profile, string identity, int workers, int capacity, bool gpu, MapLossyExecutionDescription description)
    {
        string cpuRevision = plan.Lossy.Enabled ? "M.StandardMap.Cpu.AssignmentRound1.Property9.Drawing5.Combined.ThenFinal.20261028.v3" :
            "M.StandardMap.Cpu.Final.EndpointReach.RouteSequence.20261028.v3";
        string gpuRevision = "M.StandardMap.Gpu.AssignmentRound1.Property9.Drawing5.Combined.Bitmap.Persistent.ThenCpuFinal.20261028.v3";
        if (plan.RngSemantics == RngSemanticProfileId.Modern113)
        {
            cpuRevision += ".Modern113";
            gpuRevision += ".Modern113";
        }
        if (plan.ElitePrefixResearch is not null)
        {
            cpuRevision += ".ElitePrefixResearch.FullFinal.20261105.v1";
            gpuRevision += ".ElitePrefixResearch.FullFinal.20261105.v1";
            if (plan.ElitePrefixResearch.Mode == ElitePrefixResearchMode.FloorSeven)
            {
                cpuRevision += ".FloorSeven.v1";
                gpuRevision += ".FloorSeven.v1";
            }
        }
        FilterBodyDescription? cpu = gpu ? Describe(prepared, plan, profile, identity, workers, capacity, false,
            description with { Backend = "Cpu", Reason = "CpuSelectedOrGpuUnavailable" }) : null;
        return new(prepared, gpu ? gpuRevision : cpuRevision, profile.ProfileId)
        {
            UsesGpu = gpu, WindowCapacity = gpu ? capacity : CpuFilterExecution.Capacity,
            CpuWorkers = workers, PlayersCount = plan.Players, InputIdentity = identity,
            NumericRequirements = gpu ? GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64 : GpuNumericRequirements.None,
            CpuAlternatives = cpu is null ? [] : [cpu],
            // Map pricing remains deliberately unavailable under the current Owner decision.
            PerformanceProjection = compact => gpu ? new(Id, gpuRevision +
                (compact ? ".CompactAbi1" : ".Dense") + GpuRngRotation.Revision + ".RollingDrawingRows.20261028.v1",
                MapLossyPlan.Strategy, 1, "Unpriced;LossySurvivalUnknown;CanonicalAbi1Ready", true) :
                CpuFilterExecution.Describe(Id, cpuRevision, workers, CpuFilterExecution.Capacity, true, false, compact),
            CreateExecution = (self, _) => new StandardMapExecution(self, plan, profile, capacity, description)
        };
    }

    internal static ActContext Context(int act, int ascension, RngSemanticProfileId semantics = RngSemanticProfileId.Modern110) => new(
        act switch { 1 => ActKind.Overgrowth, 2 => ActKind.Hive, 3 => ActKind.Glory, _ => throw new ArgumentOutOfRangeException(nameof(act)) },
        act-1, HasSwarmingElites: ascension>=1, RngSemantics: semantics);
    internal static int Value(MapOutcomeSignature value, StandardMapMetric metric) => metric switch
    {
        StandardMapMetric.GuaranteedMonster => value.Guarantee(0), StandardMapMetric.GuaranteedElite => value.Guarantee(1),
        StandardMapMetric.GuaranteedRest => value.Guarantee(2), StandardMapMetric.GuaranteedUnknown => value.Guarantee(4),
        StandardMapMetric.ReachableMaxMonster => value.Maximum(0), StandardMapMetric.ReachableMaxElite => value.Maximum(1),
        StandardMapMetric.ReachableMaxRest => value.Maximum(2), StandardMapMetric.ReachableMaxUnknown => value.Maximum(4),
        StandardMapMetric.ForcedMonsterPrefix => value.Guarantee(5), _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };
    internal static bool Matches(IReadOnlyList<StandardMapSearchCondition> conditions, Func<int,MapOutcomeSignature> observe)
    {
        foreach (var c in conditions)
        {
            int value = c.Scope == 0 ? Enumerable.Range(1,3).Sum(a => Value(observe(a),c.Metric)) : Value(observe(c.Scope),c.Metric);
            if (c.Comparison == StandardMapComparison.AtLeast ? value < c.Value : value > c.Value) return false;
        }
        return true;
    }
}

// Map's asynchronous lookahead/grouped device pipeline remains domain-specific.
// Its leased workspaces belong to this selected execution, never to preparation.
internal sealed record MapProposalObservation(MapLossyExecutionDescription Description,
    long ActualProposalCalls, long ActualProposalInputs);

internal interface IMapProposalObservation
{
    MapProposalObservation CaptureMapProposal();
}

internal sealed class StandardMapExecution : IFilterExecution, IMapProposalObservation
{
    public FilterBodyDescription Body { get; }
    public bool ExecutionFaulted => _faulted;
    private readonly StandardMapFilterPlan _plan;
    private readonly IRuntimeProfile _profile;
    private readonly FilterBodyDescription _cpuFinalBody;
    private readonly int _capacity;
    private readonly MapLossyExecutionDescription _description;
    private readonly System.Collections.Concurrent.ConcurrentBag<BoundedActMap[]> _work = new();
    private MapLossyGpuExecutor? _gpu;
    private bool _parity, _faulted;
    private int _gpuBatches;
    private long _gpuInputs, _gpuOutputs, _actualProposalInputs, _actualProposalCalls;
    private double _gpuMs, _fullFinalCpuMs;
    internal double FullFinalCpuMs => _fullFinalCpuMs;
    internal MapLossyGpuMetrics GpuPhaseMetrics => _gpu?.CumulativeMetrics ?? MapLossyGpuMetrics.Empty;
    public MapProposalObservation CaptureMapProposal() => new(_description, ActualProposalCalls, ActualProposalInputs);
    internal long ActualProposalInputs => Interlocked.Read(ref _actualProposalInputs);
    internal long ActualProposalCalls => Interlocked.Read(ref _actualProposalCalls);
    internal MapLossyPlan LossyPlan => _plan.Lossy;
    internal bool UsesLossy => _plan.Lossy.Enabled;
    internal MapLossyExecutionDescription LossyDescription => _description;
    internal string LossyExecution => !_plan.Lossy.Enabled ? _plan.Lossy.Reason :
        (Body.UsesGpu ? "Gpu" : "Cpu") + ":" + MapLossyPlan.Strategy + ";scope=" + _plan.Lossy.Scope + ";gpu=" + _description.Reason;

    internal StandardMapExecution(FilterBodyDescription body, StandardMapFilterPlan plan, IRuntimeProfile profile,
        int capacity, MapLossyExecutionDescription description)
    { Body = body; _plan = plan; _profile = profile; _capacity = capacity; _description = description;
        _cpuFinalBody = DescribeCpuFinal(body, plan.ElitePrefixResearch, plan.RngSemantics); }

    private static FilterBodyDescription DescribeCpuFinal(FilterBodyDescription body, ElitePrefixResearchCondition? eliteResearch,
        RngSemanticProfileId semantics)
    {
        string family = body.FamilyId; int workers = body.CpuWorkers;
        string revision = "M.StandardMap.Cpu.FullFinalAfterProposal" +
            (eliteResearch is not null ? ".ElitePrefixResearch.20261105.v1" : "") +
            (eliteResearch?.Mode == ElitePrefixResearchMode.FloorSeven ? ".FloorSeven.v1" : "") +
            (semantics == RngSemanticProfileId.Modern113 ? ".Modern113" : "");
        return body with { UsesGpu = false, WindowCapacity = CpuFilterExecution.Capacity, Cost = new(),
            PerformanceProjection = compact => CpuFilterExecution.Describe(family,
                revision, workers, CpuFilterExecution.Capacity, true, false, compact) };
    }

    internal bool MatchesLossyRoot(ulong root, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!_plan.Lossy.Enabled) return true;
        if (_plan.Lossy.Predicates.Any(p => p.Program.AlwaysReject)) return false;
        if (!_work.TryTake(out var maps)) maps = Enumerable.Range(1, 3).Select(act => new BoundedActMap(
            StandardMapFamily.Context(act, _plan.Ascension, _plan.RngSemantics))).ToArray();
        try
        {
            Span<int> values = stackalloc int[3 * MapPropertyProgram.MetricCount];
            foreach (var act in _plan.Lossy.Acts)
            {
                token.ThrowIfCancellationRequested();
                var map = maps[act.Act - 1];
                map.ReplayAssignmentRound1Root(root, token);
                foreach (var predicate in act.Drawings)
                    if (!map.MatchAssignmentProgram(predicate.Program, token)) return false;
                foreach (int metric in act.PropertyMetrics)
                    values[(act.Act - 1) * MapPropertyProgram.MetricCount + metric] = map.EvaluateAssignmentProperty(metric, token);
                foreach (var property in _plan.Lossy.Properties)
                {
                    if (property.Scope != act.Act) continue;
                    int value = values[(act.Act - 1) * MapPropertyProgram.MetricCount + property.Metric];
                    if (property.Comparison == StandardMapComparison.AtLeast ? value < property.Value : value > property.Value) return false;
                }
            }
            foreach (var property in _plan.Lossy.Properties)
            {
                if (property.Scope != 0) continue;
                token.ThrowIfCancellationRequested();
                int value = values[property.Metric] + values[MapPropertyProgram.MetricCount + property.Metric]
                    + values[2 * MapPropertyProgram.MetricCount + property.Metric];
                if (property.Comparison == StandardMapComparison.AtLeast ? value < property.Value : value > property.Value) return false;
            }
            return true;
        }
        finally { _work.Add(maps); }
    }
    internal bool MatchesRoot(ulong root, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!_work.TryTake(out var maps)) maps=Enumerable.Range(1,3).Select(act=>new BoundedActMap(
            StandardMapFamily.Context(act,_plan.Ascension,_plan.RngSemantics) with { IsMultiplayer = _plan.Players > 1 })).ToArray();
        var cached = new MapOutcomeSignature?[3];
        var replayed = new bool[3];
        (MapNode Start, MapNode Boss)? firstActGraph = null;
        void Replay(int act)
        {
            if (replayed[act-1]) return;
            maps[act-1].ReplayRoot(root,token);
            replayed[act-1] = true;
        }
        try
        {
            if (_plan.ElitePrefixResearch is { } research)
            {
                Replay(1);
                firstActGraph = maps[0].SnapshotFinalRouteGraph(token);
                var result = research.Mode == ElitePrefixResearchMode.FloorSeven
                    ? ElitePrefixResearchEvaluator.EvaluateFloorSeven(firstActGraph.Value.Start, token)
                    : ElitePrefixResearchEvaluator.Evaluate(firstActGraph.Value.Start, research.ExtraMonsters, research.Unknowns, token);
                if (!result.Matches)
                    return false;
            }
            if (!StandardMapFamily.Matches(_plan.Conditions, act =>
            {
                if (cached[act-1] is {} value) return value;
                Replay(act);
                var result = maps[act-1].EvaluateFinalScalars(); cached[act-1]=result; return result;
            })) return false;
            foreach (var (act, query) in _plan.Routes)
            {
                Replay(act);
                var graph = act == 1 && firstActGraph.HasValue ? firstActGraph.Value : maps[act-1].SnapshotFinalRouteGraph(token);
                if (!MapRouteSequenceEvaluator.Evaluate(graph.Start, graph.Boss, query, token).Matches) return false;
            }
            return true;
        }
        finally { _work.Add(maps); }
    }

    public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_faulted) throw new InvalidOperationException("M.GpuInvocationFaulted");
        if (!Body.UsesGpu)
        {
            var cpu = new CpuFilterExecution(Body, _profile,
                root => (!_plan.Lossy.Enabled || MatchesLossyRoot(root, token)) && MatchesRoot(root, token), true);
            try
            {
                var output = await cpu.FilterAsync(services, input, token).ConfigureAwait(false);
                if (_plan.Lossy.Enabled && input.Count > 0)
                { Interlocked.Add(ref _actualProposalInputs, input.Count); Interlocked.Increment(ref _actualProposalCalls); }
                return output;
            }
            catch (OperationCanceledException) { throw; }
            catch { _faulted = true; throw; }
        }
        if (input.Count == 0) return input;
        var timer = Stopwatch.StartNew();
        try
        {
            _gpu ??= await services.ExecuteGpuAsync(device => new MapLossyGpuExecutor(device, _capacity, services), token).ConfigureAwait(false);
            if (!_parity)
            {
                var sample = FamilyCandidateSet.FromSortedAbi1(input.Batch, input.EnumerateLogicalOrdinals().Take(33).ToArray());
                var actual = await services.ExecuteGpuAsync(_ => ProposeGpu(sample, token), token).ConfigureAwait(false);
                var expected = sample.EnumerateLogicalOrdinals().Where(i => MatchesLossyRoot(_profile.ComputeRootSeed(
                    VisibleSeedCandidateCodec.FormatOrdinal(_profile, input.Batch.GlobalCandidate(i))), token));
                if (!actual.EnumerateLogicalOrdinals().SequenceEqual(expected)) throw new InvalidDataException("M.LossyGpuCpuParityMismatch");
                _parity = true;
            }
            var proposed = await services.ExecuteGpuAsync(_ => ProposeGpu(input, token), token).ConfigureAwait(false);
            Interlocked.Add(ref _actualProposalInputs, input.Count); Interlocked.Increment(ref _actualProposalCalls);
            // Full Final owns result truth after the proposal. Its CPU workspace
            // remains local and uses the same bounded session CPU leases.
            var cpu = new CpuFilterExecution(_cpuFinalBody, _profile, root => MatchesRoot(root, token), true);
            long finalStarted = Stopwatch.GetTimestamp();
            FamilyCandidateSet result;
            try { result = await cpu.FilterAsync(services, proposed, token).ConfigureAwait(false); }
            finally { _fullFinalCpuMs += Stopwatch.GetElapsedTime(finalStarted).TotalMilliseconds; }
            _gpuBatches++; _gpuInputs += input.Count; _gpuOutputs += result.Count; _gpuMs += timer.Elapsed.TotalMilliseconds;
            services.Costs.ObserveCompleted(Body, input, result.Count, timer.Elapsed.TotalMilliseconds, passedCoverage: services.CoverageFor(Body));
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            _faulted = true;
            RuntimeLog.TryBackgroundWarning("mapLossyExecutionFault=true;recovery=None;cause=" + error);
            throw;
        }
    }

    private FamilyCandidateSet ProposeGpu(FamilyCandidateSet input, CancellationToken token)
    {
        try { return _gpu!.Propose(input, _plan.Lossy, _plan.Ascension, token); }
        finally
        {
            FamilyAuxiliaryEvidence.Read(Body.FamilyId, "MapGpuPhaseTiming", () =>
            {
                RuntimeLog.TryBackgroundDetail("mapLossyGpuPhases=true;scope=Call;mode=" + _gpu!.PipelineMode +
                    ";batchBase=" + input.Batch.BatchBase + ";paritySample=" + !_parity +
                    ";syncMeaning=HostWait;phasesMayOverlap=true;metrics=" + System.Text.Json.JsonSerializer.Serialize(_gpu.LastMetrics));
                return true;
            }, false);
        }
    }

    public async ValueTask DisposeAsync(FilterRuntimeServices services)
    {
        _work.Clear();
        RuntimeLog.TryBackgroundInfo($"mapLossyExecution=true;enabled={_plan.Lossy.Enabled};execution={LossyExecution};remainders={string.Join(',', _plan.Lossy.Remainders)};batches={_gpuBatches};input={_gpuInputs};output={_gpuOutputs};actualProposalCalls={ActualProposalCalls};actualProposalInputs={ActualProposalInputs};faulted={_faulted};fullFinalAuthority=true");
        if (_gpu is null) return;
        FamilyAuxiliaryEvidence.Read(Body.FamilyId, "MapGpuPhaseSummary", () =>
        {
            RuntimeLog.TryBackgroundInfo("mapLossyGpuPhases=true;scope=Cumulative;mode=" + _gpu.PipelineMode +
                ";syncMeaning=HostWait;phasesMayOverlap=true;includesParitySample=true;fullFinalCpuMs=" +
                _fullFinalCpuMs.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                ";metrics=" + System.Text.Json.JsonSerializer.Serialize(_gpu.CumulativeMetrics));
            return true;
        }, false);
        await services.ExecuteGpuAsync(_ => { _gpu.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false);
    }
}
