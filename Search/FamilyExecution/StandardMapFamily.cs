using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// Full Final remains the Family truth predicate. Optional Assignment proposals
// run privately first and preserve the ordered batch-local ABI1 boundary.
internal sealed class StandardMapFamily : IFamilyInvocation
{
    private readonly ExactSearchExecutionRequest _request;
    private readonly FamilyCpuExecution _cpu;
    private readonly (int Act, MapRouteSequenceQuery Query)[] _routes;
    // Lease per active worker, not per ThreadPool thread: thread churn across
    // batches must not retain another multi-megabyte workspace every time.
    private readonly System.Collections.Concurrent.ConcurrentBag<BoundedActMap[]> _work = new();
    private readonly MapLossyPlan _lossy;
    private readonly bool _useGpu;
    private readonly string _gpuReason;
    private MapLossyGpuExecutor? _gpu;
    private bool _parity, _faulted;
    private int _gpuBatches;
    private long _gpuInputs, _gpuOutputs;
    private double _gpuMs;
    private long _actualProposalInputs, _actualProposalCalls;
    internal long ActualProposalInputs => Interlocked.Read(ref _actualProposalInputs);
    internal long ActualProposalCalls => Interlocked.Read(ref _actualProposalCalls);
    internal MapLossyPlan LossyPlan => _lossy;
    internal bool UsesLossy => _lossy.Enabled;
    internal MapLossyExecutionDescription LossyDescription => new(_request.MapLossy.Enabled, _lossy.Enabled,
        !_lossy.Enabled ? "FullFinal" : _useGpu ? "Gpu" : "Cpu", _lossy.Enabled ? MapLossyPlan.Strategy : "FullFinal",
        !_lossy.Enabled ? _lossy.Reason : _gpuReason, _lossy.Scope, _lossy.Remainders.ToArray());
    internal string LossyExecution => !_lossy.Enabled ? _lossy.Reason :
        (_useGpu ? "Gpu" : "Cpu") + ":" + MapLossyPlan.Strategy + ";scope=" + _lossy.Scope + ";gpu=" + _gpuReason;
    internal StandardMapFamily(ExactSearchExecutionRequest request, bool preferGpu = false, bool float64Available = true)
    {
        _request = request;
        _lossy = MapLossyPlan.Create(request);
        var numeric = FamilyDeviceProfileFoundation.Initialization.Capture();
        bool int64Available = numeric.VulkanCapabilities?.ShaderInt64 != false;
        _useGpu = _lossy.Enabled && preferGpu && float64Available && int64Available;
        _gpuReason = !preferGpu ? "CpuSelectedOrGpuUnavailable" : !float64Available ? "Float64Unavailable" :
            !int64Available ? "Int64Unavailable" : _lossy.Enabled ? "GpuSelected" : _lossy.Reason;
        _routes = request.CompiledSearch.NormalizedQuery.StandardMapRoutes.Select(c => (c.Act, c.ToSequenceQuery())).ToArray();
        _cpu = new(request, FamilyId, (_lossy.Enabled ? "M.StandardMap.Cpu.AssignmentRound1.Property9.Drawing5.Combined.ThenFinal.20261028.v3" : "M.StandardMap.Cpu.Final.EndpointReach.RouteSequence.20261028.v3"), workers: Math.Clamp(request.WorkerCount,1,32), numericalRoots:true);
    }
    internal static ActContext Context(int act, int ascension) => new(
        act switch { 1 => ActKind.Overgrowth, 2 => ActKind.Hive, 3 => ActKind.Glory, _ => throw new ArgumentOutOfRangeException(nameof(act)) },
        act-1, HasSwarmingElites: ascension>=1);
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
    internal bool MatchesLossyRoot(ulong root, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!_lossy.Enabled) return true;
        if (_lossy.Predicates.Any(p => p.Program.AlwaysReject)) return false;
        if (!_work.TryTake(out var maps)) maps = Enumerable.Range(1, 3).Select(act => new BoundedActMap(Context(act, _request.Ascension))).ToArray();
        try
        {
            Span<int> values = stackalloc int[3 * MapPropertyProgram.MetricCount];
            foreach (var act in _lossy.Acts)
            {
                token.ThrowIfCancellationRequested();
                var map = maps[act.Act - 1];
                map.ReplayAssignmentRound1Root(root, token);
                foreach (var predicate in act.Drawings)
                    if (!map.MatchAssignmentProgram(predicate.Program, token)) return false;
                foreach (int metric in act.PropertyMetrics)
                    values[(act.Act - 1) * MapPropertyProgram.MetricCount + metric] = map.EvaluateAssignmentProperty(metric, token);
                foreach (var property in _lossy.Properties)
                {
                    if (property.Scope != act.Act) continue;
                    int value = values[(act.Act - 1) * MapPropertyProgram.MetricCount + property.Metric];
                    if (property.Comparison == StandardMapComparison.AtLeast ? value < property.Value : value > property.Value) return false;
                }
            }
            foreach (var property in _lossy.Properties)
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
            Context(act,_request.Ascension) with { IsMultiplayer = _request.Authority.PlayersCount > 1 })).ToArray();
        var cached = new MapOutcomeSignature?[3];
        var replayed = new bool[3];
        void Replay(int act)
        {
            if (replayed[act-1]) return;
            maps[act-1].ReplayRoot(root,token);
            replayed[act-1] = true;
        }
        try
        {
            if (!Matches(_request.CompiledSearch.NormalizedQuery.StandardMaps, act =>
            {
                if (cached[act-1] is {} value) return value;
                Replay(act);
                var result = maps[act-1].EvaluateFinalScalars(); cached[act-1]=result; return result;
            })) return false;
            foreach (var (act, query) in _routes)
            {
                Replay(act);
                var graph = maps[act-1].SnapshotFinalRouteGraph(token);
                if (!MapRouteSequenceEvaluator.Evaluate(graph.Start, graph.Boss, query, token).Matches) return false;
            }
            return true;
        }
        finally { _work.Add(maps); }
    }
    GpuNumericRequirements IFamilyInvocation.GpuNumericRequirements => _useGpu
        ? GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64 : GpuNumericRequirements.None;
    bool IFamilyInvocation.ExecutionFaulted => _faulted;
    public string FamilyId => "M.StandardMap";
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId,_useGpu ? _request.RunOptions.MapLossy!.GpuBatchSize : FamilyCpuExecution.Capacity,[],"FullFinalReplay;MapPricingDeferredByOwner.20261026");
    public FamilySurvivalProjection Survival
    {
        get
        {
            if (_lossy.Enabled) return FamilySurvivalProjection.Unresolved(FamilyId, "M.LossyAssignmentSurvivalUnknown;FinalProbabilityIsNotProposalSurvival");
            SearchQuery mapOnly = SearchQuery.Empty with
            { StandardMaps = _request.CompiledSearch.NormalizedQuery.StandardMaps,
                StandardMapRoutes = _request.CompiledSearch.NormalizedQuery.StandardMapRoutes };
            var compiled = SearchCompiler.Compile(mapOnly, _request.CompiledSearch.Context);
            JointSelectivityResult estimate = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(compiled));
            return estimate.JointlyPriced && estimate.Probability is double p
                ? FamilySurvivalProjection.Resolved(FamilyId, p,
                    "M.FinalMapFamilyPredicateProjection;" + estimate.EvidenceCode)
                : FamilySurvivalProjection.Unresolved(FamilyId,
                    "M.FinalMapProbabilityUnavailable;" + estimate.EvidenceCode);
        }
    }
    // Owner defers map pricing while map work remains unfinished, including
    // existing references. See Search/README.md, performance exceptions.
    public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry) => null;
    public FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
    public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact) => !_useGpu ? _cpu.Condition(compact) :
        new(FamilyId, "M.StandardMap.Gpu.AssignmentRound1.Property9.Drawing5.Combined.Bitmap.Persistent.ThenCpuFinal.20261028.v3" +
            (compact ? ".CompactAbi1" : ".Dense") + GpuRngRotation.Revision + ".RollingDrawingRows.20261028.v1",
            MapLossyPlan.Strategy, 1, "Unpriced;LossySurvivalUnknown;CanonicalAbi1Ready", usesGpu: true);
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations => _useGpu ? [new StandardMapFamily(_request)] : [this];
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window, FamilyCandidateSet input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (window.ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint) throw new InvalidOperationException("M.ObservationContextMismatch");
        if (_faulted) throw new InvalidOperationException("M.GpuInvocationFaulted");
        if (!_useGpu)
        {
            var cpuResult = _cpu.Execute(window, input, token, root => (!_lossy.Enabled || MatchesLossyRoot(root, token)) && MatchesRoot(root, token), context);
            if (_lossy.Enabled && input.Count > 0) { Interlocked.Add(ref _actualProposalInputs, input.Count); Interlocked.Increment(ref _actualProposalCalls); }
            return cpuResult;
        }
        if (input.Count == 0) return input;
        var watch = Stopwatch.StartNew();
        try
        {
            _gpu ??= await context.ExecuteGpuAsync(rd => new MapLossyGpuExecutor(rd, _request.RunOptions.MapLossy!.GpuBatchSize), token).ConfigureAwait(false);
            if (!_parity)
            {
                var sample = FamilyCandidateSet.FromSortedAbi1(input.Batch, input.EnumerateLogicalOrdinals().Take(33).ToArray());
                var actual = await context.ExecuteGpuAsync(_ => ProposeGpu(sample, token), token).ConfigureAwait(false);
                var profile = RolltheSpire2.Compatibility.Beta111Profile.Instance;
                var expected = sample.EnumerateLogicalOrdinals().Where(i => MatchesLossyRoot(profile.ComputeRootSeed(
                    VisibleSeedCandidateCodec.FormatOrdinal(profile, input.Batch.GlobalCandidate(i))), token));
                if (!actual.EnumerateLogicalOrdinals().SequenceEqual(expected)) throw new InvalidDataException("M.LossyGpuCpuParityMismatch");
                _parity = true;
            }
            var proposed = await context.ExecuteGpuAsync(_ => ProposeGpu(input, token), token).ConfigureAwait(false);
            Interlocked.Add(ref _actualProposalInputs, input.Count); Interlocked.Increment(ref _actualProposalCalls);
            var result = _cpu.Execute(window, proposed, token, root => MatchesRoot(root, token), context);
            _gpuBatches++; _gpuInputs += input.Count; _gpuOutputs += result.Count; _gpuMs += watch.Elapsed.TotalMilliseconds;
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
    private FamilyCandidateSet ProposeGpu(FamilyCandidateSet input, CancellationToken token) =>
        _gpu!.Propose(input, _lossy, _request.Ascension, token);
    public FamilyPerformanceObservation CapturePerformanceObservation() => !_useGpu ? _cpu.Observation() :
        new(ConditionPerformance, "", 0, 0, 0, _gpuBatches, 0, _gpuInputs, _gpuOutputs, _gpuMs, false,
            "MapPricingDeferred;FullFinalIncluded;LossySurvivalUnknown;CpuProposalParity=" + _parity);
    public FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => !_useGpu ? _cpu.Live() : null;
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        _cpu.WriteSummary(); _work.Clear();
        RuntimeLog.TryBackgroundInfo($"mapLossyExecution=true;enabled={_lossy.Enabled};execution={LossyExecution};remainders={string.Join(',', _lossy.Remainders)};batches={_gpuBatches};input={_gpuInputs};output={_gpuOutputs};actualProposalCalls={ActualProposalCalls};actualProposalInputs={ActualProposalInputs};faulted={_faulted};fullFinalAuthority=true");
        if (_gpu is not null)
            await context.ExecuteGpuAsync(_ => { _gpu.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false);
    }
}
