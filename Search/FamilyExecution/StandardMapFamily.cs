using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.FamilyExecution;

// Full Final CPU replay. No lossy Assignment sieve and no change to ABI1.
internal sealed class StandardMapFamily : IFamilyInvocation
{
    private readonly ExactSearchExecutionRequest _request;
    private readonly FamilyCpuExecution _cpu;
    // Lease per active worker, not per ThreadPool thread: thread churn across
    // batches must not retain another multi-megabyte workspace every time.
    private readonly System.Collections.Concurrent.ConcurrentBag<BoundedActMap[]> _work = new();
    internal StandardMapFamily(ExactSearchExecutionRequest request)
    {
        _request = request;
        _cpu = new(request, FamilyId, "M.StandardMap.Cpu.Final.20260919.v1", workers: Math.Clamp(request.WorkerCount,1,32), numericalRoots:true);
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
    internal bool MatchesRoot(ulong root, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!_work.TryTake(out var maps)) maps=Enumerable.Range(1,3).Select(act=>new BoundedActMap(Context(act,_request.Ascension))).ToArray();
        var cached = new MapOutcomeSignature?[3];
        try { return Matches(_request.CompiledSearch.NormalizedQuery.StandardMaps, act =>
        {
            if (cached[act-1] is {} value) return value;
            maps[act-1].ReplayRoot(root,token);
            var result = maps[act-1].EvaluateFinalScalars(); cached[act-1]=result; return result;
        }); }
        finally { _work.Add(maps); }
    }
    public string FamilyId => "M.StandardMap";
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId,FamilyCpuExecution.Capacity,[],"FullFinalReplay;AbsoluteCostNotCalibrated");
    public FamilySurvivalProjection Survival
    {
        get
        {
            SearchQuery mapOnly = SearchQuery.Empty with
            { StandardMaps = _request.CompiledSearch.NormalizedQuery.StandardMaps };
            var compiled = SearchCompiler.Compile(mapOnly, _request.CompiledSearch.Context);
            JointSelectivityResult estimate = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(compiled));
            return estimate.JointlyPriced && estimate.Probability is double p
                ? FamilySurvivalProjection.Resolved(FamilyId, p,
                    "M.FinalMapFamilyPredicateProjection;" + estimate.EvidenceCode)
                : FamilySurvivalProjection.Unresolved(FamilyId,
                    "M.FinalMapProbabilityUnavailable;" + estimate.EvidenceCode);
        }
    }
    public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry)
    {
        if (_request.ProfileId != RuntimeProfileId.Beta111 || !_request.Authority.IsVanilla ||
            !FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput ||
            _request.WorkerCount is < 1 or > 8 ||
            !SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().CpuIdentity
                .Contains("Intel64 Family 6 Model 183", StringComparison.Ordinal)) return null;
        int acts = _request.CompiledSearch.NormalizedQuery.StandardMaps.Any(c => c.Scope == 0) ? 3 :
            _request.CompiledSearch.NormalizedQuery.StandardMaps.Select(c => c.Scope).Distinct().Count();
        if (acts is < 1 or > 3) return null;
        double ns = 185000 + 225000 * acts;
        return new(FamilyId, "M.FinalMapCpu.Acts" + acts + ".P" + _request.WorkerCount,
            ns, FamilyCpuExecution.Capacity, 0,
            "LocalMeasuredCPU;Beta111FinalMap;Disjoint64RootReplay;" +
            "OneAct26ms_ThreeActs60ms;1024RootCanonicalP1P4P8;" +
            "NoWorkerSpeedupAssumed;PoolAndMapGraph=VanillaCurrent;" +
            "CpuCanonicalIncludesOrderedAbi1;ProbabilityCorpusSeparate",
            OutputElementBytes: 8, OutputAlreadyOrdered: true, PublicTransportClass: "CpuOrderedAbi1")
        { FixedWindowMilliseconds = .002 };
    }
    public FamilyConditionPerformanceProjection ConditionPerformance => _cpu.Condition(false);
    public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact) => _cpu.Condition(compact);
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations => [this];
    public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window, FamilyCandidateSet input, CancellationToken token) =>
        new(_cpu.Execute(window,input,token,root => MatchesRoot(root,token)));
    public FamilyPerformanceObservation CapturePerformanceObservation() => _cpu.Observation();
    public FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => _cpu.Live();
    public ValueTask DisposeAsync(FamilyExecutionContext context) { _cpu.WriteSummary(); _work.Clear(); return ValueTask.CompletedTask; }
}
