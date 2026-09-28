using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// One continuous ordinary-combat observation, independently replayed from root.
// Authored Neow mechanics are prerequisites, never N Coverage or opening truth.
// Capsule latent obtains follow the authored-only policy. Multiplayer Bones
// recovers unspecified companions from the real root-local offered-pair shuffle.
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

    private CombatRewardFamily(ExactSearchExecutionRequest request, int? precedingNicheDraws = 0)
    {
        _request = request;
        Replay = new(request, precedingNicheDraws);
        _gpuPlan = CombatRewardGpuPlan.TryCreate(request, Replay, out string gpuIssue);
        _models = new(request, Replay, _gpuPlan is null ? FamilyCpuExecution.Capacity : CombatRewardGpuExecutor.Capacity);
        _cpu = new(request, FamilyId, "C.CombatReward.Cpu.AuthoredPrefix.20260907.v1");
        RuntimeLog.TryBackgroundInfo($"combatRewardFamilyReady=true;family={FamilyId};" +
            $"bonesPrefix={Replay.Plan.OpeningConsumption.ReplayBonesOffer};authoredChildren={Replay.Plan.OpeningConsumption.OrderedRelicIds.Length};" +
            $"prefixRoutes={Replay.RouteCount};precedingNicheDraws={Replay.PrecedingNicheDraws};maximumBattle={Replay.Plan.MaximumBattleOrdinal};" +
            $"continuationIssue={Replay.ConservativeOpeningReason};kaleidoscopeCountOnly={Replay.KaleidoscopeCountOnly};" +
            $"effectiveSieve={!Replay.ConservativelyKeeps};exactRemainder={Replay.ConservativelyKeeps};" +
            $"actualBonesPair={Replay.ReplayActualBonesPair};" +
            $"actualCapsuleHeld={Replay.CapsuleHeldReplay};unhandledCapsuleHeld=PerRootConservativeKeep;" +
            $"explicitImpacts={string.Join(',', Replay.Plan.ExplicitContext.SourceRelicKeys)};" +
            $"physical={(_gpuPlan is null ? "CPU" : _gpuPlan.UsesHotLoop ? "GpuNeutralHotLoop" : "GpuGenericStreaming")};physicalIssue={gpuIssue};" +
            $"model={(request.Authority.PlayersCount > 1 ? "PartyAuthoredOrNeutralContinuation" : "AuthoredPlusNeutralBestEffort")};unordered=IndependentExistential;survival={(Survival.IsResolved ? "Available" : "Unknown")};expectedCost=FullEnvelope;recovery=None");
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
                    VisibleSeedCandidateCodec.FormatOrdinal(profile,input.Batch.GlobalCandidate(i))))).ToArray();
                var actualOrdinals = actual.EnumerateLogicalOrdinals().ToArray();
                if(!actualOrdinals.SequenceEqual(expected))throw new InvalidDataException(
                    $"C.GpuCpuParityMismatch:batchBase={input.Batch.BatchBase};slot={_request.Authority.PlayerSlotIndex};" +
                    $"cpu={expected.Length};gpu={actualOrdinals.Length};cpuOnly={string.Join(',',expected.Except(actualOrdinals).Take(8))};" +
                    $"gpuOnly={string.Join(',',actualOrdinals.Except(expected).Take(8))}");
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
        // A personal multiplayer request must carry its shared arrival explicitly.
        if (request.Authority.PlayersCount != 1) return false;
        if (!request.Evaluation.NormalCombatRewardConditions.Any(c => !c.IsEmpty)) return false;
        family = new CombatRewardFamily(request);
        return true;
    }

    internal static bool TryCreateParty(ExactSearchExecutionRequest request,
        int? precedingNicheDraws, int? precedingPotionDraws, out IFamilyInvocation? family)
    {
        family = null;
        if (request.ProfileId != Compatibility.RuntimeProfileId.Beta111 ||
            !request.Authority.CanUseCurrentModel || request.Authority.PlayersCount is < 2 or > 4 ||
            request.Authority.PlayerSlotIndex < 0 || request.Authority.PlayerSlotIndex >= request.Authority.PlayersCount ||
            !request.Evaluation.NormalCombatRewardConditions.Any(c => !c.IsEmpty))
            return false;
        if (request.CompiledSearch.NormalizedQuery.OpeningRoute is { } route &&
            (!route.IsValid || !Beta110FastRelicCatalog.TryGetId(route.RouteRelicKey, out _))) return false;
        // Combat potion rewards use personal Rewards, never the shared opening
        // CombatPotionGeneration stream. Unknown Niche remains explicitly unknown.
        family = new CombatRewardFamily(request, precedingNicheDraws);
        return true;
    }
}

internal sealed class CombatRewardReplay
{
    private static readonly ulong RewardsHash = XxHash64.Hash("rewards"u8, 0UL);
    private static readonly ulong NicheHash = XxHash64.Hash("niche"u8, 0UL);
    private readonly int _playerSlot, _ascension, _bonesPoolCount, _players;
    private readonly bool _defect;
    internal Beta110FastEffectCatalog Catalog { get; }
    internal Beta110CombatRewardFastPlan Plan { get; }
    internal int RouteCount { get; }
    internal bool CanUsePotionPrefix { get; }
    internal int? PrecedingNicheDraws { get; }
    internal int[] CapsuleNicheAdvances { get; } = new int[4];
    internal bool KaleidoscopeCountOnly { get; }
    internal bool ReplayActualBonesPair { get; }
    internal byte[] BonesPool { get; } = [];
    internal bool DynamicCapsuleNicheUnknown { get; }
    internal bool CapsuleHeldReplay { get; }
    internal HashSet<ushort> ExplicitHeldIds { get; }
    internal string ConservativeOpeningReason { get; } = "";
    internal bool ConservativelyKeeps => ConservativeOpeningReason.Length != 0;

    internal CombatRewardReplay(ExactSearchExecutionRequest request, int? precedingNicheDraws = 0)
    {
        if (precedingNicheDraws < 0) throw new ArgumentOutOfRangeException(nameof(precedingNicheDraws));
        PrecedingNicheDraws = precedingNicheDraws;
        var keys = request.Evaluation.NormalCombatRewardConditions.SelectMany(c => c.Cards.Any.Concat(c.Cards.All)
            .Concat(c.Cards.Ban).Concat(c.Potions.Any).Concat(c.Potions.All).Concat(c.Potions.Ban));
        Catalog = Beta110FastEffectCatalogCompiler.Compile(request.Authority.EffectAuthority, keys);
        if (request.Authority.PlayersCount > 1) Catalog = CombatRewardPartyCatalog.Project(request, Catalog);
        Plan = Beta110CombatRewardFastPlanCompiler.CompileForFamily(request, Catalog);
        if (request.Authority.PlayersCount > 1 && request.CompiledSearch.NormalizedQuery.OpeningRoute is null)
            Plan = Plan with { OpeningConsumption = Beta110CombatRewardOpeningConsumptionProjection.Empty,
                ExplicitContext = Beta110CombatRewardExplicitContext.Empty(request.ProfileId),
                Fingerprint = Plan.Fingerprint + ":PartyNeutralOpening" };
        if (!Plan.Enabled) throw new InvalidOperationException("C.CombatReward.CompilationFailure:" + Plan.DisableReason);
        _playerSlot = request.Authority.PlayerSlotIndex;
        _players = request.Authority.PlayersCount;
        _ascension = request.Ascension;
        _defect = request.CharacterKey == BaseGameModelKeys.Characters.Defect;
        // Only the shuffle's draw count is needed; no actual pair observation.
        _bonesPoolCount = request.Authority.EffectAuthority?.BonesEligibleRelics?
            .Count(key => key != BaseGameModelKeys.Relics.NeowsBones) ?? 0;
        if (Plan.OpeningConsumption.ReplayBonesOffer && _bonesPoolCount < 2)
        {
            if (_players == 1) throw new InvalidOperationException("C.CombatReward.BonesShufflePoolUnavailable");
            ConservativeOpeningReason = "BonesShufflePoolUnavailable";
        }
        bool fixedOrder = request.CompiledSearch.NormalizedQuery.OpeningRouteRelicRequirement?.OrderMode == BonesRouteOrderMode.ExactOrder ||
            request.Evaluation.RequiredBonesAcquisitionOrder.Count == 2;
        ReplayActualBonesPair = _players > 1 && Plan.OpeningConsumption.ReplayBonesOffer &&
            Plan.OpeningConsumption.OrderedRelicIds.Length < 2;
        RouteCount = ReplayActualBonesPair || Plan.OpeningConsumption.ReplayBonesOffer && Plan.OpeningConsumption.OrderedRelicIds.Length == 2 && !fixedOrder ? 2 : 1;
        KaleidoscopeCountOnly = Catalog.OtherCharacterPools.Length >= 3 &&
            Catalog.OtherCharacterPools.All(p => p.TotalCount > 0);
        var upgrades = NeowAuthoredUpgradeContinuation.Compile(request);
        DynamicCapsuleNicheUnknown = upgrades is not null;
        byte[] children = Plan.OpeningConsumption.OrderedRelicIds;
        if (ReplayActualBonesPair)
        {
            BonesPool = (request.Authority.EffectAuthority!.BonesEligibleRelics ?? [])
                .Where(k => k != BaseGameModelKeys.Relics.NeowsBones)
                .Select(k => Beta110FastRelicCatalog.TryGetId(k, out byte id) ? id : Beta110FastRelicCatalog.InvalidId).ToArray();
            if (request.Authority.EffectAuthority.BonesEligibilityExact != true || BonesPool.Length > 32 ||
                BonesPool.Any(id => id == Beta110FastRelicCatalog.InvalidId))
                ConservativeOpeningReason = "ActualBonesPoolOutsideAuditedNumericReplay";
            else if (BonesPool.Any(id => !HasNeutralObservableHeldImpact(request.ProfileId, id)))
                ConservativeOpeningReason = "ActualBonesHeldRewardImpactRequiresRealRoute";
        }
        for (int route = 0; route < RouteCount; route++)
        for (int index = 0; index < children.Length; index++)
        {
            byte current = children[route == 0 ? index : children.Length - 1 - index];
            byte previous = index == 0 ? Beta110FastRelicCatalog.InvalidId : children[route == 0 ? index - 1 : children.Length - index];
            CapsuleNicheAdvances[route * 2 + index] = upgrades?.Advance(current, previous) ?? 0;
        }
        if (_players > 1 && request.CompiledSearch.NormalizedQuery.OpeningRoute is not null)
        {
            var nested = RolltheSpire2.Search.Semantics.PartyInitialQuery.CapsuleEffectPremise(
                request.CompiledSearch.NormalizedQuery).Values.SelectMany(keys => keys);
            foreach (var key in nested.Distinct())
                if (!VanillaRelicRewardEffects.TryGet(request.ProfileId, key, out var effects) ||
                    !effects.NestedOnObtainPreservesRewardContinuation ||
                    !effects.IsHeldNeutralForNormalCombatReward && !OpeningCombatRewardImpactAdapterRegistry.TryResolve(request.ProfileId, key, out _))
                { ConservativeOpeningReason = "AuthoredNestedRewardEffectUnresolved:" + key; break; }
        }
        CapsuleHeldReplay = _players > 1 && children.Concat(ReplayActualBonesPair ? BonesPool : [])
            .Any(id => id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule);
        ExplicitHeldIds = Plan.ExplicitContext.SourceRelicKeys.Select(k => Catalog.TryGetDenseId(k, out var id) ? id : ushort.MaxValue).ToHashSet();
        if (CapsuleHeldReplay && !Catalog.RelicBagAuthorityExact)
            ConservativeOpeningReason = "CapsulePersonalInitialBagAuthorityUnavailable";
        if (!KaleidoscopeCountOnly && (children.Contains(Beta110FastRelicCatalog.Kaleidoscope) ||
            ReplayActualBonesPair && BonesPool.Contains(Beta110FastRelicCatalog.Kaleidoscope)) &&
            (!PrecedingNicheDraws.HasValue || CapsuleNicheAdvances.Any(n => n < 0) ||
                ReplayActualBonesPair && DynamicCapsuleNicheUnknown))
            ConservativeOpeningReason = "KaleidoscopeUnknownArrivalWithoutFixedDrawProof";
        CanUsePotionPrefix = request.Authority.CanUseCurrentModel && request.Authority.PlayersCount == 1 &&
            request.ProfileId == Compatibility.RuntimeProfileId.Beta111 && Plan.CardPoolAuthorityExact && Plan.PotionPoolAuthorityExact &&
            Plan.Predicates.Length > 0 && Plan.Predicates.All(p => !p.IsAnyBattle && !p.HasCardPredicate);
    }

    internal static bool HasNeutralObservableHeldImpact(Compatibility.RuntimeProfileId profile, byte id)
    {
        var key = Beta110FastRelicCatalog.KeyOf(id);
        if (!VanillaRelicRewardEffects.TryGet(profile, key, out var effect)) return false;
        if (effect.IsHeldNeutralForNormalCombatReward) return true;
        // Among current Neow ids, only Silken Tress and Silver Crucible have
        // non-neutral held adapters. Core applies their enchant/upgrade mutations
        // after identity and natural upgrade rolls, without consuming more RNG.
        // C predicates observe identity, potion and gold, not these mutations.
        return OpeningCombatRewardImpactAdapterRegistry.TryResolve(profile, key, out var adapter) &&
            adapter.Operations.All(operation => operation.Kind is
                OpeningCombatRewardImpactOperationKind.ForceUpgradeCardType or
                OpeningCombatRewardImpactOperationKind.UpgradeNextCardRewards or
                OpeningCombatRewardImpactOperationKind.EnchantFirstCardRewardWithGlam);
    }

    internal Beta110OpeningRewardState Opening(ulong root, int route) => Opening(root, route, out _);

    internal Beta110OpeningRewardState Opening(ulong root, int route, out UpFrontRngCheckpoint? nicheState)
    {
        if (ConservativelyKeeps) throw new InvalidOperationException("C.CombatReward.OpeningUnresolved:" + ConservativeOpeningReason);
        if ((uint)route >= (uint)RouteCount) throw new ArgumentOutOfRangeException(nameof(route));
        var rewards = new Beta110FastRng(unchecked(root + (ulong)_playerSlot + RewardsHash));
        var niche = new Beta110FastRng(unchecked(root + NicheHash));
        bool nicheKnown = PrecedingNicheDraws.HasValue;
        for (int draw = 0; draw < PrecedingNicheDraws.GetValueOrDefault(); draw++) _ = niche.NextDouble();
        Span<byte> actualPool = stackalloc byte[ReplayActualBonesPair ? BonesPool.Length : 0];
        ReadOnlySpan<byte> children = Plan.OpeningConsumption.OrderedRelicIds;
        Span<ushort> bag = stackalloc ushort[CapsuleHeldReplay ? Beta110FastEffectCatalogCompiler.MaximumRelicBagEntries : 0];
        int bagCount = -1;
        var held = new Beta110OpeningRewardState { RewardInfluenceAuthorityExact = true };
        if (ReplayActualBonesPair)
        {
            BonesPool.CopyTo(actualPool);
            rewards.UnstableShuffle(actualPool);
        }
        else if (Plan.OpeningConsumption.ReplayBonesOffer) rewards.ConsumeUnstableShuffle(_bonesPoolCount);
        int childCount = ReplayActualBonesPair ? 2 : children.Length;
        for (int i = 0; i < childCount; i++)
        {
            int at = route == 0 ? i : childCount - 1 - i;
            byte id = ReplayActualBonesPair ? actualPool[at] : children[at];
            if (CapsuleHeldReplay && id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule)
            {
                if (bagCount < 0) bagCount = CombatRewardCapsuleReplay.Initialize(root, Catalog, bag);
                int pulls = id == Beta110FastRelicCatalog.SmallCapsule ? 1 : 2;
                for (int pull = 0; pull < pulls; pull++)
                {
                    ushort index = CombatRewardCapsuleReplay.Pull(Catalog, bag[..bagCount], ref rewards);
                    if (index == ushort.MaxValue) continue;
                    var relic = Catalog.OrdinaryRelics[index];
                    var impact = relic.RewardCapability;
                    held.RewardInfluenceAuthorityExact &= impact.InfluenceSupported;
                    if (ExplicitHeldIds.Contains(relic.DenseId)) continue;
                    held.InfluenceFlags |= impact.InfluenceFlags & ~Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation;
                    held.AdditionalCardRewardCount += impact.AdditionalCardRewardCount;
                    held.FixedGoldAmount += impact.FixedGoldAmount;
                }
            }
            else if (!CombatRewardOpeningReplay.TryReplayQueryLiteralRelicRewardsConsumption(
                    id, Catalog, _ascension, _defect, ref rewards, ref niche, requireAuthority: false, multiplayer: _players > 1,
                    nicheKnown: nicheKnown))
                throw new InvalidOperationException("C.CombatReward.OpeningConsumptionFailed:" + id);
            int advance = ReplayActualBonesPair ? DynamicCapsuleNicheUnknown &&
                id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule ? -1 : 0 :
                CapsuleNicheAdvances[route * 2 + i];
            if (advance < 0) nicheKnown = false;
            else if (nicheKnown) for (int draw = 0; draw < advance; draw++) _ = niche.NextDouble();
        }
        if (Plan.OpeningConsumption.ReplayBonesOffer && nicheKnown) _ = niche.NextDouble(); // Final generated curse, after both obtains.
        nicheState = nicheKnown ? niche.CaptureCheckpoint() : null;
        var opening = new Beta110OpeningRewardState
        {
            Rewards = rewards, PotionOdds = 0.4f, CardRarityOffset = -0.05f,
            InfluenceFlags = held.InfluenceFlags, AdditionalCardRewardCount = held.AdditionalCardRewardCount,
            FixedGoldAmount = held.FixedGoldAmount,
            // Exact within this authored model, not a claim about unknown world state.
            RewardsContinuationAuthorityExact = true, RewardInfluenceAuthorityExact = held.RewardInfluenceAuthorityExact
        };
        Plan.ExplicitContext.ApplyTo(ref opening);
        return opening;
    }

    internal bool Matches(ulong root) => MatchesCore(root, false);

    private bool MatchesCore(ulong root, bool potionPrefix)
    {
        if (ConservativelyKeeps) return true;
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
        !opening.RewardInfluenceAuthorityExact ||
        (potionPrefix ? Beta110CombatRewardFastStage.EvaluatePotionPrefixForFamily(opening, Plan, Catalog, _ascension) :
            Beta110CombatRewardFastStage.EvaluateForFamily(opening, Plan, Catalog, _ascension)).Status ==
            Beta110CombatRewardRouteProjectionStatus.ExactProjection;
}
