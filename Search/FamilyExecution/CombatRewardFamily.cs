using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// One continuous ordinary-combat observation, independently replayed from root.
// Authored Neow mechanics are prerequisites, never N Coverage or opening truth.
// Unspecified grants/impacts remain neutral by the existing Owner-approved
// BestEffort policy; this is not a strict conservative-superset guarantee.
internal sealed partial class CombatRewardFamily : IFamilyInvocation
{
    private readonly GpuCostSamples _costSamples = new();
    IEnumerable<GpuLocalPeak> IFamilyInvocation.CostPeaks => _costSamples.Peaks;
    void IFamilyInvocation.LogExecutionEvidence() => _costSamples.WriteSummary();
    private readonly FamilyCpuExecution _cpu;
    private readonly ExactSearchExecutionRequest _request;
    private readonly CombatRewardGpuPlan? _gpuPlan;
    internal CombatRewardGpuPlan? GpuPlan => _gpuPlan;
    private readonly CombatRewardFamilyModels _models;
    private static readonly IReadOnlySet<string> EmptyCoverage = new HashSet<string>();
    private CombatRewardGpuExecutor? _gpu;
    private bool _compact, _parity, _faulted;
    private int _batches;
    private long _inputs, _outputs, _liveInputs;
    private double _canonicalMs, _liveMs, _peak;
    internal CombatRewardReplay Replay { get; }

    private CombatRewardFamily(ExactSearchExecutionRequest request)
    {
        _request = request;
        Replay = new(request);
        _gpuPlan = CombatRewardGpuPlan.TryCreate(request, Replay, out string gpuIssue);
        _models = new(request, Replay, _gpuPlan is null ? FamilyCpuExecution.Capacity : CombatRewardGpuExecutor.Capacity);
        _cpu = new(request, FamilyId, "C.CombatReward.Cpu.AuthoredPrefix.20260907.v1");
        RuntimeLog.TryBackgroundInfo($"combatRewardFamilyReady=true;family={FamilyId};" +
            $"bonesPrefix={Replay.Plan.OpeningConsumption.ReplayBonesOffer};authoredChildren={Replay.Plan.OpeningConsumption.OrderedRelicIds.Length};" +
            $"prefixRoutes={Replay.RouteCount};maximumBattle={Replay.Plan.MaximumBattleOrdinal};" +
            $"explicitImpacts={string.Join(',', Replay.Plan.ExplicitContext.SourceRelicKeys)};" +
            $"physical={(_gpuPlan is null ? "CPU" : _gpuPlan.UsesHotLoop ? "GpuNeutralHotLoop" : "GpuGenericStreaming")};physicalIssue={gpuIssue};" +
            $"model=AuthoredPlusNeutralBestEffort;unordered=IndependentExistential;survival={(Survival.IsResolved ? "Available" : "Unknown")};expectedCost=FullEnvelope;recovery=None");
    }

    public string FamilyId => "C.CombatReward";
    public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest request) =>
        CombatRewardPhysicalPricing.Quote(_request, Replay, _gpuPlan, request);
    bool IFamilyInvocation.CanBindPrivateSerial => _gpuPlan is not null;
    FamilyPhysicalQuote? IFamilyInvocation.QuoteCommonPrivateWorkCancellation(FamilyPhysicalQuoteRequest g) =>
        _gpuPlan is null || !FamilyPhysicalQuote.AdmittedRequest(g) || !FamilyPhysicalQuote.HasReferenceBackend() ? null :
        new(FamilyId, "C.SameCompiled" + (_gpuPlan.UsesHotLoop ? "HotLoop" : "GenericStreaming"), 0,
            CombatRewardGpuExecutor.Capacity, _gpuPlan.UsesHotLoop ? 120 : 1000,
            "ComparisonOnly;UnknownNumericalCancelled;SameRoutesMetaAndAppend32;SameBodyReferenceSetup;PublicReachAppliedByAllocation");
    FamilyPrivateGpuExecution IFamilyInvocation.BindPrivateSerial(Godot.RenderingDevice rd,
        PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output)
    {
        if (_gpuPlan is null) throw new InvalidOperationException("C.PrivateSerialNotAdmitted");
        var gpu = new CombatRewardGpuExecutor(rd, _gpuPlan, privateInput: input, privateOutput: output);
        return new(gpu, gpu.Device, gpu.SetupMs, input, output, (batch, count, token) =>
        {
            FamilyCandidateSet? result = null; CombatRewardGpuMetrics m;
            if (output is not null)
            {
                if (input is null) gpu.ExecutePrivate(FamilyCandidateSet.Dense(batch), token, out m);
                else gpu.ExecutePrivateStage(batch, count, token, out m);
            }
            else result = input is null ? gpu.Execute(FamilyCandidateSet.Dense(batch), token, out m) : gpu.ExecutePrivateInput(batch, count, token, out m);
            return new(m.Output, result, m.DispatchSyncMs, m.ReadbackMs, m.ReadbackBytes, m.CanonicalMs, m.Dispatches);
        });
    }
    public FamilyAnalyticalCostProjection AnalyticalCost => _models.Analytical;
    public FamilySurvivalProjection Survival => _models.Survival;
    public bool HasConditionalProjections => true;
    public FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passedCoverage) => _models.ResolveSurvival(passedCoverage);
    public FamilyExpectedFilteringCostProjection ExpectedFilteringCost => _models.Expected(EmptyCoverage);
    public FamilyExpectedFilteringCostProjection ResolveExpectedFilteringCost(IReadOnlySet<string> passedCoverage) => _models.Expected(passedCoverage);
    public FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
    public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compactAbi1Input) => _gpuPlan is null
        ? _cpu.Condition(compactAbi1Input) : new(FamilyId,_gpuPlan.Revision(compactAbi1Input),
            "C.Neutral.20260907.v1",1,"NoAcceptedWithinPathCurve;CanonicalAbi1Ready",usesGpu:true);
    public FamilyPerformanceObservation CapturePerformanceObservation() => _gpuPlan is null ? _cpu.Observation() :
        new(ResolveConditionPerformance(_compact),_gpu?.Device??"",_gpu?.SetupMs??0,_peak,_peak,_batches,Math.Max(0,_batches-1),
            _inputs,_outputs,_canonicalMs,_parity&&!_faulted&&_batches>1,"CanonicalAbi1Ready;FirstBatchExcluded;CpuReferenceParity="+_parity);
    public FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => _gpuPlan is null ? _cpu.Live() :
        _faulted||!_parity||_batches<2?null:new(FamilyId,_gpuPlan.Revision(_compact),Interlocked.Read(ref _liveInputs),Volatile.Read(ref _liveMs),_batches-1);
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext executionContext,
        FamilyObservationWindow observationWindow, FamilyCandidateSet input, CancellationToken cancellationToken)
    {
        if(_gpuPlan is null)return _cpu.Execute(observationWindow,input,cancellationToken,Replay.Matches);
        cancellationToken.ThrowIfCancellationRequested();
        if(_faulted)throw new InvalidOperationException("C.GpuInvocationFaulted");
        if(observationWindow.ExactRequest.SnapshotFingerprint!=_request.SnapshotFingerprint)throw new InvalidOperationException("C.ObservationMismatch");
        if(input.Count==0)return input;
        _compact=!input.IsDense;
        try
        {
            _gpu??=await executionContext.ExecuteGpuAsync(rd=>new CombatRewardGpuExecutor(rd,_gpuPlan),cancellationToken).ConfigureAwait(false);
            if(!_parity)
            {
                // Separate tiny GPU dispatch, compared in full before admitting the
                // large batch. Parity cost is not physical performance evidence.
                var sample=FamilyCandidateSet.FromSortedAbi1(input.Batch,input.EnumerateLogicalOrdinals().Take(4096).ToArray());
                var actual=await executionContext.ExecuteGpuAsync(rd=>_gpu.Execute(sample,cancellationToken,out _),cancellationToken).ConfigureAwait(false);
                var profile=RolltheSpire2.Compatibility.RuntimeProfileRegistry.Select(_request.Detection);
                var expected=sample.EnumerateLogicalOrdinals().Where(i=>Replay.Matches(profile.ComputeRootSeed(
                    VisibleSeedCandidateCodec.FormatOrdinal(profile,input.Batch.GlobalCandidate(i)))));
                if(!actual.EnumerateLogicalOrdinals().SequenceEqual(expected))throw new InvalidDataException("C.GpuCpuParityMismatch");
                _parity=true;
            }
            var result=await executionContext.ExecuteGpuAsync(_=>{
                var output=_gpu.Execute(input,cancellationToken,out var metrics);return (output,metrics);
            },cancellationToken).ConfigureAwait(false);
            _costSamples.Observe(this, new(!input.IsDense, false, input.Count), input.Count, result.metrics.DispatchSyncMs, outputs: result.output.Count, canonicalMs: result.metrics.CanonicalMs, readbackMs: result.metrics.ReadbackMs, readbackBytes: result.metrics.ReadbackBytes);
            var m=result.metrics;_batches++;_inputs+=m.Input;_outputs+=m.Output;_canonicalMs+=m.CanonicalMs;
            if(_batches>1){Interlocked.Add(ref _liveInputs,m.Input);_liveMs+=m.CanonicalMs;_peak=Math.Max(_peak,m.Input*1000d/m.CanonicalMs);}
            if (RolltheSpire2.Bootstrap.RuntimeLog.DetailEnabled)RuntimeLog.TryBackgroundDetail($"combatRewardGpuBatch=true;physicalRevision={_gpuPlan.Revision(_compact)};"+
                $"input={m.Input};output={m.Output};dispatches={m.Dispatches};uploadMs={m.UploadMs};dispatchSyncMs={m.DispatchSyncMs};readbackMs={m.ReadbackMs};"+
                $"readbackBytes={m.ReadbackBytes};canonicalAbi1ReadyMs={m.CanonicalMs};parity={_parity};recovery=False");
            return result.output;
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){_faulted=true;RuntimeLog.TryBackgroundWarning("combatRewardGpuFault=true;recovery=None;error="+ex.Message);throw;}
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext executionContext)
    {
        if(_gpuPlan is null){_cpu.WriteSummary();return;}
        RuntimeLog.TryBackgroundInfo($"combatRewardGpuSummary=true;batches={_batches};input={_inputs};output={_outputs};canonicalMs={_canonicalMs};parity={_parity};faulted={_faulted};recovery=False");
        if(_gpu is not null)await executionContext.ExecuteGpuAsync(_=>{_gpu.Dispose();return true;},CancellationToken.None).ConfigureAwait(false);
    }

    internal static bool TryCreate(ExactSearchExecutionRequest request, out IFamilyInvocation? family)
    {
        family = null;
        if (!request.Evaluation.NormalCombatRewardConditions.Any(c => !c.IsEmpty)) return false;
        family = new CombatRewardFamily(request);
        return true;
    }
}

internal sealed class CombatRewardReplay
{
    private static readonly ulong RewardsHash = XxHash64.Hash("rewards"u8, 0UL);
    private static readonly ulong NicheHash = XxHash64.Hash("niche"u8, 0UL);
    private readonly int _playerSlot, _ascension, _bonesPoolCount;
    private readonly bool _defect;
    internal Beta110FastEffectCatalog Catalog { get; }
    internal Beta110CombatRewardFastPlan Plan { get; }
    internal int RouteCount { get; }
    internal bool CanUsePotionPrefix { get; }

    internal CombatRewardReplay(ExactSearchExecutionRequest request)
    {
        var keys = request.Evaluation.NormalCombatRewardConditions.SelectMany(c => c.Cards.Any.Concat(c.Cards.All)
            .Concat(c.Cards.Ban).Concat(c.Potions.Any).Concat(c.Potions.All).Concat(c.Potions.Ban));
        Catalog = Beta110FastEffectCatalogCompiler.Compile(request.Authority.EffectAuthority, keys);
        Plan = Beta110CombatRewardFastPlanCompiler.CompileForFamily(request, Catalog);
        if (!Plan.Enabled) throw new InvalidOperationException("C.CombatReward.CompilationFailure:" + Plan.DisableReason);
        _playerSlot = request.Authority.PlayerSlotIndex;
        _ascension = request.Ascension;
        _defect = request.CharacterKey == BaseGameModelKeys.Characters.Defect;
        // Only the shuffle's draw count is needed; no actual pair observation.
        _bonesPoolCount = request.Authority.EffectAuthority?.BonesEligibleRelics?
            .Count(key => key != BaseGameModelKeys.Relics.NeowsBones) ?? 0;
        if (Plan.OpeningConsumption.ReplayBonesOffer && _bonesPoolCount < 2)
            throw new InvalidOperationException("C.CombatReward.BonesShufflePoolUnavailable");
        bool fixedOrder = request.CompiledSearch.NormalizedQuery.OpeningRouteRelicRequirement?.OrderMode == BonesRouteOrderMode.ExactOrder ||
            request.Evaluation.RequiredBonesAcquisitionOrder.Count == 2;
        RouteCount = Plan.OpeningConsumption.ReplayBonesOffer && Plan.OpeningConsumption.OrderedRelicIds.Length == 2 && !fixedOrder ? 2 : 1;
        CanUsePotionPrefix = request.Authority.CanUseCurrentModel && request.Authority.PlayersCount == 1 &&
            request.ProfileId == Compatibility.RuntimeProfileId.Beta111 && Plan.CardPoolAuthorityExact && Plan.PotionPoolAuthorityExact &&
            Plan.Predicates.Length > 0 && Plan.Predicates.All(p => !p.IsAnyBattle && !p.HasCardPredicate);
    }

    internal Beta110OpeningRewardState Opening(ulong root, int route)
    {
        if ((uint)route >= (uint)RouteCount) throw new ArgumentOutOfRangeException(nameof(route));
        var rewards = new Beta110FastRng(unchecked(root + (ulong)_playerSlot + RewardsHash));
        var niche = new Beta110FastRng(unchecked(root + NicheHash));
        if (Plan.OpeningConsumption.ReplayBonesOffer) rewards.ConsumeUnstableShuffle(_bonesPoolCount);
        byte[] children = Plan.OpeningConsumption.OrderedRelicIds;
        for (int i = 0; i < children.Length; i++)
        {
            byte id = children[route == 0 ? i : children.Length - 1 - i];
            if (!CombatRewardOpeningReplay.TryReplayQueryLiteralRelicRewardsConsumption(
                    id, Catalog, _ascension, _defect, ref rewards, ref niche, requireAuthority: false))
                throw new InvalidOperationException("C.CombatReward.OpeningConsumptionFailed:" + id);
        }
        var opening = new Beta110OpeningRewardState
        {
            Rewards = rewards, PotionOdds = 0.4f, CardRarityOffset = -0.05f,
            // Exact within this authored model, not a claim about unknown world state.
            RewardsContinuationAuthorityExact = true, RewardInfluenceAuthorityExact = true
        };
        Plan.ExplicitContext.ApplyTo(ref opening);
        return opening;
    }

    internal bool Matches(ulong root) => MatchesCore(root, false);

    private bool MatchesCore(ulong root, bool potionPrefix)
    {
        var first = Opening(root, 0);
        bool firstPass = Evaluate(first, potionPrefix);
        if (RouteCount == 1) return firstPass;
        var second = Opening(root, 1);
        // Evaluate every distinct authored predecessor, including error checking.
        return second.EquivalentTo(first) ? firstPass : (Evaluate(second, potionPrefix) | firstPass);
    }

    internal bool MatchesPotionPrefix(ulong root)
    {
        if (!CanUsePotionPrefix) throw new InvalidOperationException("C.PotionPrefixNotAdmitted");
        return MatchesCore(root, true);
    }

    private bool Evaluate(Beta110OpeningRewardState opening, bool potionPrefix) =>
        (potionPrefix ? Beta110CombatRewardFastStage.EvaluatePotionPrefixForFamily(opening, Plan, Catalog, _ascension) :
            Beta110CombatRewardFastStage.EvaluateForFamily(opening, Plan, Catalog, _ascension)).Status ==
            Beta110CombatRewardRouteProjectionStatus.ExactProjection;
}
