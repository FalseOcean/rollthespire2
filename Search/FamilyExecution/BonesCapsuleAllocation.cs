using System.Diagnostics;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Owner-accepted bounded K+Large default; explicit A/B controls retain precedence.
// No new logical Family or automatic multi-stage pricing.
internal static class BonesCapsuleAllocation
{
    internal const string LocalRevision = "N.Neow.Composite.RCapsule.BonesK.LocalSameRoute.DensePreBones.CanonicalAbi1Ready.20260909.v1";
    internal static bool TrySelectDefault(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> registered,
        out FamilyExecutionPlan? selected) => TrySelect(request, registered, out selected, defaultSelection: true);

    internal static bool TrySelect(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> registered,
        out FamilyExecutionPlan? selected, bool defaultSelection = false)
    {
        selected = null;
        string mode = System.Environment.GetEnvironmentVariable("RT2_BONES_CAPSULE_BOUNDARY") ?? "";
        if (defaultSelection)
        {
            if (mode.Length != 0 && mode != "default") return false;
            // Do not compile/replay N or allocate Capsule metadata for other Families.
            if (!registered.Any(f => f.FamilyId == "N.Neow") || !registered.Any(f => f.FamilyId == "R.Relic")) return false;
            mode = "local";
        }
        else if (mode.Length == 0 || mode is "ordinary" or "default") return false;
        if (mode is not ("private" or "verify" or "mask" or "local" or "recompute" or "verify-mask" or "verify-local" or "verify-recompute")) throw new InvalidOperationException("BonesBoundary.UnknownMode");
        var n = NeowReplayPlan.Compile(request, false);
        var effects = request.Evaluation.StructuredNeowEffects.Where(c => !c.IsEmpty).ToArray();
        var capsule = effects.FirstOrDefault(c => c.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule);
        var leafy = effects.FirstOrDefault(c => c.SourceRelicKey == BaseGameModelKeys.Relics.LeafyPoultice);
        var kaleidoscope = effects.FirstOrDefault(c => c.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope);
        bool isK = kaleidoscope is not null && leafy is null;
        var companion = isK ? BaseGameModelKeys.Relics.Kaleidoscope : BaseGameModelKeys.Relics.LeafyPoultice;
        bool applicable = (!defaultSelection || isK) && (mode is "private" or "verify" || isK) && n.Bones && n.DirectNestedVanilla111 && !n.HasFinalCurseFastProjection && n.ExactOnly.Length == 0 &&
            request.Authority.CanUseCurrentModel && request.Authority.PlayersCount == 1 && request.Authority.NoRunModifiers == true &&
            request.Authority.AllCharacterCardPoolsUnlocked == true && request.Evaluation.RequiredBonesAcquisitionOrder.Count == 0 &&
            request.Evaluation.RequiredBonesCombination.Count == 2 && request.Evaluation.RequiredBonesCombination.ToHashSet().SetEquals([BaseGameModelKeys.Relics.LargeCapsule, companion]) &&
            effects.Length == 2 && n.StructuredConditions.Length == 1 && capsule is not null && (leafy is not null || kaleidoscope is not null) &&
            NeowReplayPlan.IsCapsule(capsule) && capsule.Kind == NeowStructuredConditionKind.ExactUnorderedPair &&
            capsule.OutputKeys.Count >= (isK ? 1 : 2) && capsule.OutputKeys.Count <= 2 && capsule.OutputKeys.Distinct().Count() == capsule.OutputKeys.Count && !capsule.AllowDuplicateOutputs &&
            (isK || !capsule.OutputKeys.Any(k => k.Serialized is "RELIC:WHETSTONE" or "RELIC:WAR_PAINT")) &&
            ((!isK && leafy!.Kind == NeowStructuredConditionKind.ExactUnorderedPair && leafy.OutputKeys.Count is 1 or 2) ||
             (isK && kaleidoscope!.Kind == NeowStructuredConditionKind.IndependentOfferGroupTargets && kaleidoscope.OutputKeys.Count is 1 or 2)) &&
            !request.Evaluation.RelicSequenceConditions.Any(c => !c.IsEmpty) && !request.Evaluation.RelicShopSequenceConditions.Any(c => !c.IsEmpty) &&
            request.Evaluation.CapsuleContainedRelics.IsEmpty && !request.Evaluation.RequireWhetstone && !request.Evaluation.RequireWarPaint &&
            registered.Any(f => f.FamilyId == "N.Neow") && registered.Any(f => f.FamilyId == "R.Relic") &&
            registered.All(f => f.FamilyId is "N.Neow" or "R.Relic" or "W.World" or "E.EventResult");
        if (!applicable || !NeowFamilyGpuPlan.TryCreate(n, out var np, out _) || np is null ||
            !CreateR(request, isK, out var rp) || rp is null)
        {
            if (!defaultSelection) Bootstrap.RuntimeLog.TryBackgroundInfo("bonesCapsuleBoundaryRequested=true;applicable=false;normalAllocationRetained=true");
            return false;
        }
        // This fixed allocation is unpriced; no observed survival repricing.
        IFamilyInvocation[] ordered = [new BonesCapsuleBoundaryInvocation(request, np, rp, mode, defaultSelection),
            ..registered.Where(f => f.FamilyId is not ("N.Neow" or "R.Relic"))];
        var stages = ordered.Select((f,i) => {
            var condition = f.ResolveConditionPerformance(i != 0);
            return new FamilyExecutionPlanStage(f, f.Coverage, i != 0, i == 0 ? 1 : double.NaN, null, null, null,
                condition, GpuCostCalibration.ResolveReference(condition), null, null, null, null, i);
        }).ToArray();
        selected = new(defaultSelection ? "FamilyAllocation.BonesKLarge.LocalDefault" : "BonesCapsuleBoundary." + mode,
            defaultSelection ? "FamilyAllocation.BonesKLarge.LocalDefault.20260910.v1" : "BonesCapsuleBoundary.Explicit.20260909.v1",
            defaultSelection ? FamilyPlannerRankingAuthority.RegistrationOrderFallback : FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
            stages, null, [defaultSelection ? "OwnerAcceptedBoundedLocalDefault" : "ExplicitBoundaryExperiment"],
            ["BoundedCompositeSurvivalAndPricingDeferredToGlobalPhysicalComposition"]);
        if(!selected.Stages.SelectMany(stage=>stage.Coverage).ToHashSet(StringComparer.Ordinal)
            .SetEquals(registered.SelectMany(family=>family.Coverage))) throw new InvalidOperationException("BonesK.CoverageMismatch");
        Bootstrap.RuntimeLog.TryBackgroundInfo($"bonesCapsuleBoundarySelected=true;mode={mode};coverage=N.Neow,R.Relic;order=N>R;intermediate={((mode.EndsWith("mask",StringComparison.Ordinal)) ? "PrivatePackedOrdinalRouteMask" : mode.EndsWith("local",StringComparison.Ordinal) ? "LocalCheckpoint" : "PrivateOrdinal")};normalPricingChanged=false;defaultSelection={defaultSelection};physicalRevision={(mode=="local" ? LocalRevision : "ExplicitControl")};pricingDeferred=true");
        return true;
    }
    private static bool CreateR(ExactSearchExecutionRequest request, bool isK, out RelicFullGpuPlan? plan)
    {
        var route = NeowReplayPlan.Compile(request, true);
        return isK ? RelicFullGpuPlan.TryCreateBonesKBoundary(request, route, out plan, out _)
            : RelicFullGpuPlan.TryCreate(request, route, out plan, out _);
    }

}

internal sealed class BonesCapsuleBoundaryInvocation(ExactSearchExecutionRequest request, NeowFamilyGpuPlan np,
    RelicFullGpuPlan rp, string mode, bool defaultSelection = false) : IFamilyInvocation
{
    private BonesCapsuleBoundaryGpu? _gpu;
    private long _input, _output;
    private int _batches;
    private double _ms;
    private bool _faulted;
    public string FamilyId => "N.Neow";
    public IReadOnlyList<string> Coverage => ["N.Neow", "R.Relic"];
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, PrivateOrdinalBuffer.Capacity, [], "BonesKLocalAllocation.Unpriced");
    public FamilySurvivalProjection Survival => FamilySurvivalProjection.Unresolved(FamilyId, "BoundedSameRouteProjectionUnpriced");
    public FamilyConditionPerformanceProjection ConditionPerformance => new(FamilyId,
        mode=="local" ? BonesCapsuleAllocation.LocalRevision :
        "N.Neow.Composite.RCapsule.Bones" + (rp.UsesBonesKBoundary ? "K" : "Leafy") + $".{mode}.CanonicalAbi1Ready.20260909.v2",
        "Neutral", 1, defaultSelection ? "OwnerAcceptedBoundedDefault_Unpriced" : "Explicit", usesGpu:true);
    public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance, _gpu?.Device ?? "", _gpu?.SetupMs ?? 0,
        0, 0, _batches, 0, _input, _output, _ms, false, _faulted ? "PhysicalFailure" : defaultSelection ? "BoundedDefaultPricingDeferred" : "ExplicitExperimentNoCalibration");
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow observation,
        FamilyCandidateSet input, CancellationToken token)
    {
        if (_faulted || request.SnapshotFingerprint != observation.ExactRequest.SnapshotFingerprint)
            throw new InvalidOperationException("BonesBoundary.InvocationInvariant");
        try {
            _gpu ??= await context.ExecuteGpuAsync(rd => new BonesCapsuleBoundaryGpu(rd, request, np, rp, mode), token).ConfigureAwait(false);
            var result = await context.ExecuteGpuAsync(_ => { var output = _gpu.Execute(input, token, out var ms); return (output, ms); }, token).ConfigureAwait(false);
            _batches++; _input += input.Count; _output += result.output.Count; _ms += result.ms;
            return result.output;
        } catch (OperationCanceledException) { throw; } catch { _faulted = true; throw; }
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if (_gpu is not null) await context.ExecuteGpuAsync(_ => { _gpu.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"bonesCapsuleBoundarySummary=true;batches={_batches};input={_input};output={_output};canonicalMs={_ms};faulted={_faulted};recovery=None");
    }
}

internal sealed class BonesCapsuleBoundaryGpu : IDisposable
{
    private readonly RenderingDevice _rd;
    private readonly ExactSearchExecutionRequest _request;
    private readonly string _mode;
    private readonly PrivateOrdinalBuffer? _ordinals;
    private readonly NeowFamilyGpuExecutor _n;
    private readonly RelicFamilyGpuExecutor? _r;
    private readonly NeowFamilyGpuExecutor? _recompute, _referenceN;
    private readonly RelicFamilyGpuExecutor? _referenceR;
    private readonly CapsuleRelicReplay _cpu;
    private readonly NeowReplayPlan? _nReplay;
    private readonly CapsuleRelicReplay? _earlyCpu, _lateCpu;
    private bool _disposed, _checked;
    private long _batches, _inputs, _middle, _outputs, _zero, _pairs;
    private long _aOnly, _bOnly, _both, _evalA, _evalB, _heavyA, _heavyB, _removed;
    private long _validA, _validB, _validBoth, _sameFailure, _unsafe;
    private double _nDispatch, _nReadback, _nCanonical, _rDispatch, _rReadback, _rCanonical, _canonical;
    private readonly double _setupBaseMs;
    private double NeowSetupMs => _n.SetupMs + (_recompute?.SetupMs ?? 0) + (_referenceN?.SetupMs ?? 0);
    internal double SetupMs => _setupBaseMs + NeowSetupMs;
    internal string Device => _n.Device;
    internal BonesCapsuleBoundaryGpu(RenderingDevice rd, ExactSearchExecutionRequest request, NeowFamilyGpuPlan np, RelicFullGpuPlan rp, string mode)
    {
        _rd=rd; _request=request;
        bool verify=mode.StartsWith("verify",StringComparison.Ordinal);
        _mode=mode.Replace("verify-", "", StringComparison.Ordinal); if (_mode=="verify") _mode="private";
        var timer=Stopwatch.StartNew();
        _cpu=new(request,NeowReplayPlan.Compile(request,true));
        try {
            if(_mode!="local") _ordinals=new(rd);
            _n = _mode=="local" ? new(rd,np,1,bonesKMode:2,bonesKMetadata:rp.CapsuleMetadata)
                : new(rd,np,1,privateOutput:_ordinals,bonesKMode:_mode=="mask" ? 1 : 0);
            if(_mode=="recompute") {
                uint[] meta=(uint[])np.Meta.Clone(); meta[66]=0; // Compact receiver has no Dense stages.
                _recompute=new(rd,np with { Meta=meta },2,privateInput:_ordinals,bonesKMode:2,bonesKMetadata:rp.CapsuleMetadata);
            } else if(_mode!="local") _r=RelicFamilyGpuExecutor.Create(rd,request,rp,_ordinals,_mode=="mask");
            if(verify) {
                _referenceN=new(rd,np,3); _referenceR=RelicFamilyGpuExecutor.Create(rd,request,rp);
                if(rp.UsesBonesKBoundary) {
                    _nReplay=NeowReplayPlan.Compile(request,false);
                    var r=NeowReplayPlan.Compile(request,true);
                    _earlyCpu=new(request,r with {First=Beta110FastRelicCatalog.LargeCapsule,Second=Beta110FastRelicCatalog.Kaleidoscope});
                    _lateCpu=new(request,r with {First=Beta110FastRelicCatalog.Kaleidoscope,Second=Beta110FastRelicCatalog.LargeCapsule});
                }
            }
            _setupBaseMs=timer.Elapsed.TotalMilliseconds-NeowSetupMs;
        } catch (Exception failure)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => PrivateOrdinalBuffer.DisposeAll(_referenceR,_referenceN,_recompute,_r,_n,_ordinals), "BonesCapsule");
            throw;
        }
    }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, out double ms)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!input.IsDense) throw new InvalidOperationException("BonesBoundary.DenseRequired");
        double setupBefore=NeowSetupMs;
        var timer=Stopwatch.StartNew();
        FamilyCandidateSet output;
        NeowFamilyGpuMetrics nm;
        int middle;
        double rDispatch=0,rReadback=0,rCanonical=0;
        uint[]? counts=null;
        if(_mode=="local") {
            output=_n.Execute(input,token,out nm); counts=_n.BonesKCounts;
            middle=checked((int)(counts[0]+counts[1]+counts[2]));
        } else {
            middle=_n.ExecutePrivateOutput(input,token,out nm);
            _ordinals!.CheckPopulation(input.Batch,middle);
            if(_recompute is not null) {
                output=_recompute.ExecutePrivate(input.Batch,middle,token,out var rm);
                rDispatch=rm.DispatchSyncMs; rReadback=rm.ReadbackMs; rCanonical=rm.CanonicalMs;
                counts=_recompute.BonesKCounts;
            } else {
                output=_r!.ExecutePrivate(input.Batch,middle,token,out var rm);
                rDispatch=rm.CommandMs+rm.SubmitMs+rm.SyncMs; rReadback=rm.ReadbackMs; rCanonical=rm.CanonicalAbi1ReadyMs;
                _evalA+=_r.LastRouteA; _evalB+=_r.LastRouteB;
                _heavyA+=_r.LastLateHeavy; _heavyB+=_r.LastEarlyHeavy;
                if(_mode=="mask") counts=_n.BonesKCounts;
            }
        }
        ms=Math.Max(0,timer.Elapsed.TotalMilliseconds-(NeowSetupMs-setupBefore));
        if(counts is not null) {
            _aOnly+=counts[0]; _bOnly+=counts[1]; _both+=counts[2];
            if(_mode!="mask") { _evalA+=counts[3]; _evalB+=counts[4]; _heavyA+=counts[5]; _heavyB+=counts[6]; }
        }
        _batches++; _inputs+=input.Count; _middle+=middle; _outputs+=output.Count; _pairs+=nm.PairSurvivors;
        if(output.Count==0) _zero++;
        _nDispatch+=nm.DispatchSyncMs; _nReadback+=nm.ReadbackMs; _nCanonical+=nm.CanonicalMs;
        _rDispatch+=rDispatch; _rReadback+=rReadback; _rCanonical+=rCanonical; _canonical+=ms;
        if(_referenceN is not null) {
            Verify(input,middle,output,token);
            if(!_checked) { _checked=true; CheckLifecycle(input.Batch); }
        }
        if (RolltheSpire2.Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"bonesCapsuleBoundaryBatch=true;mode={_mode};input={input.Count};entryPass={nm.CurseSurvivors};pairPass={nm.PairSurvivors};privatePopulation={middle};output={output.Count};nDispatchSyncMs={nm.DispatchSyncMs};nCanonicalMs={nm.CanonicalMs};rDispatchSyncMs={rDispatch};rReadbackMs={rReadback};canonicalMs={ms};intermediatePayloadBytes=0;publicPayloadBytes={output.Count*4L};verification={_referenceN is not null}");
        return output;
    }
    private void Verify(FamilyCandidateSet input,int middle,FamilyCandidateSet output,CancellationToken token)
    {
        var ordinaryN=_referenceN!.Execute(input,token,out _);
        Dictionary<uint,uint>? masks=null;
        if(_mode=="mask") {
            uint[] packed=middle==0 ? [] : FamilyGpuComputeUtility.FromUInt32Bytes(_rd.BufferGetData(_ordinals!.Buffer,0,checked((uint)middle*4)));
            masks=packed.ToDictionary(x=>x&0xffffffu,x=>x>>24);
            if(!ordinaryN.EnumerateLogicalOrdinals().SequenceEqual(masks.Keys.Order().Select(x=>(ulong)x))) throw new InvalidDataException("BonesK.MaskOrdinalMismatch");
        } else if(_mode!="local") _ordinals!.VerifyPopulation(input.Batch,middle,ordinaryN);
        if(ordinaryN.Count!=middle) throw new InvalidDataException("BonesK.NPopulationMismatch");
        var ordinaryR=_referenceR!.Execute(ordinaryN,token,out _);
        var old=ordinaryR.EnumerateLogicalOrdinals().ToHashSet();
        var actual=output.EnumerateLogicalOrdinals().ToHashSet();
        if(!actual.IsSubsetOf(old)) throw new InvalidDataException("BonesK.NotSubsetOfIndependent");
        foreach(var ordinal in ordinaryN.EnumerateLogicalOrdinals()) {
            string seed=Beta110SeedCodec.FormatOrdinal(input.Batch.BatchBase+ordinal);
            ulong root=Beta111Profile.Instance.ComputeRootSeed(seed);
            if(_cpu.Matches(root)!=old.Contains(ordinal)) throw new InvalidDataException("BonesBoundary.CpuMismatch");
            if(_nReplay is null) { if(actual.Contains(ordinal)!=old.Contains(ordinal)) throw new InvalidDataException("BonesBoundary.FullPopulationMismatch"); continue; }
            var observation=NeowFamilyReplay.Observe(root,_nReplay);
            bool na=NeowFamilyReplay.Route(root,_nReplay,Beta110FastRelicCatalog.Kaleidoscope,Beta110FastRelicCatalog.LargeCapsule,observation.RewardsAfterBones);
            bool nb=NeowFamilyReplay.Route(root,_nReplay,Beta110FastRelicCatalog.LargeCapsule,Beta110FastRelicCatalog.Kaleidoscope,observation.RewardsAfterBones);
            uint mask=(na?1u:0u)|(nb?2u:0u);
            if(mask==0 || masks is not null && masks[(uint)ordinal]!=mask) throw new InvalidDataException("BonesK.RouteMaskMismatch");
            if(_mode=="private") { if(mask==1)_aOnly++; else if(mask==2)_bOnly++; else _both++; }
            // Pinned CPU references reproduce actual arrivals independently of N.
            bool ra=_lateCpu!.Matches(root), rb=_earlyCpu!.Matches(root);
            bool same=(na&&ra)||(nb&&rb);
            bool expected=_mode=="private" ? old.Contains(ordinal) : same;
            if(actual.Contains(ordinal)!=expected) throw new InvalidDataException("BonesK.SameRouteCpuMismatch");
            if(!same) _sameFailure++;
            if(!old.Contains(ordinal)) continue;
            if(!TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance,root,seed,out var bound,out var issue)) throw new InvalidDataException(issue);
            var exact=ProductionExactSearchEvaluator.Evaluate(_request,bound);
            if(!same) {
                if(exact.Evaluation.Disposition==SearchDisposition.NoMatch) _removed++; else _unsafe++;
                if(_mode!="private" && (exact.IsMatch || exact.Evaluation.Disposition!=SearchDisposition.NoMatch)) {
                    Bootstrap.RuntimeLog.TryBackgroundInfo($"bonesKCounterexample=true;seed={seed};ordinal={input.Batch.BatchBase+ordinal};na={na};nb={nb};ra={ra};rb={rb};exactMatch={exact.IsMatch};disposition={exact.Evaluation.Disposition};failure={exact.FailureCode};evaluationJson={System.Text.Json.JsonSerializer.Serialize(exact.Evaluation)}");
                    bool excludedPickup=HasExcludedMatchedPickup(exact, _request);
                    // Diagnostic classification only, never a simulation/fallback or
                    // a same-run pricing input. Other Exact divergences still fail.
                    if(!excludedPickup) throw new InvalidDataException("BonesK.RemovedExactNotNoMatch:"+seed+":"+exact.Evaluation.Disposition);
                    Bootstrap.RuntimeLog.TryBackgroundInfo($"bonesKScopedDivergence=true;seed={seed};excludedPickupEvidence=true;knownProductLimitation=WWP;realExactMismatchUnresolved=true");
                }
            } else if(exact.IsMatch) {
                if(na&&ra&&nb&&rb) _validBoth++; else if(na&&ra) _validA++; else _validB++;
            }
        }
    }
    // This is evidence classification, not an effect adapter. Only an Exact
    // witness whose Large->K route actually contains the excluded automatic
    // pickup qualifies; a W/WP in an unrelated route cannot waive a mismatch.
    internal static bool HasExcludedMatchedPickup(ProductionExactSearchResult exact, ExactSearchExecutionRequest request)
    {
        if(!exact.IsMatch || exact.Document is null) return false;
        bool Authored(ModelKey key) => request.Evaluation.CapsuleContainedRelics.All.Contains(key) ||
            request.Evaluation.CapsuleContainedRelics.Any.Contains(key) ||
            (key == BaseGameModelKeys.OrdinaryRelics.Whetstone ? request.Evaluation.RequireWhetstone : request.Evaluation.RequireWarPaint) ||
            request.Evaluation.StructuredNeowEffects.Any(c => !c.IsEmpty && NeowReplayPlan.IsCapsule(c) && c.OutputKeys.Contains(key));
        foreach(var choice in exact.Document.Sections.SelectMany(section=>section.NeowChoices)) {
            if(choice.BonesOutcome is null) continue;
            foreach(var route in choice.BonesOutcome.OriginalRoutes) {
                if(route.AcquisitionOrder.Count!=2 || route.AcquisitionOrder[0]!=BaseGameModelKeys.Relics.LargeCapsule ||
                    route.AcquisitionOrder[1]!=BaseGameModelKeys.Relics.Kaleidoscope) continue;
                string routeId=$"choice.{choice.SlotIndex}.{route.OpeningRewardContinuation?.Route.RouteId ?? route.RouteId}";
                if(!exact.Evaluation.MatchedRouteIds.Contains(routeId)) continue;
                if(route.RelicScopedResults.Where(result=>result.SourceRelicKey==BaseGameModelKeys.Relics.LargeCapsule)
                    .SelectMany(result=>result.EffectGroups).SelectMany(group=>group.OrderedItems)
                    .Any(item => item.EvidenceCode.Value == "beta111.neow-effect.war-paint.nested-automatic-effect" && !Authored(BaseGameModelKeys.OrdinaryRelics.WarPaint) ||
                        item.EvidenceCode.Value == "beta111.neow-effect.whetstone.nested-automatic-effect" && !Authored(BaseGameModelKeys.OrdinaryRelics.Whetstone))) return true;
            }
        }
        return false;
    }
    private void CheckLifecycle(SearchBatch batch)
    {
        var tail=FamilyCandidateSet.Dense(new SearchBatch(batch.BatchBase+16777200,67));
        Execute(tail,CancellationToken.None,out _);
        Execute(FamilyCandidateSet.Dense(new SearchBatch(batch.BatchBase,0)),CancellationToken.None,out _);
        using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
        try { Execute(tail,cancelled.Token,out _); throw new InvalidDataException("BonesBoundary.CancelIgnored"); } catch(OperationCanceledException) {}
        Execute(tail,CancellationToken.None,out _);
        try {
            if(_mode=="local") _n.Execute(FamilyCandidateSet.Dense(new SearchBatch(batch.BatchBase,NeowFamilyGpuExecutor.Capacity+1)),CancellationToken.None,out _);
            else if(_r is not null) _r.ExecutePrivate(batch,batch.BatchCandidateCount+1,CancellationToken.None,out _);
            else _recompute!.ExecutePrivate(batch,batch.BatchCandidateCount+1,CancellationToken.None,out _);
            throw new InvalidDataException("BonesBoundary.BoundsIgnored");
        } catch(Exception ex) when(ex.Message is "PrivateOrdinal.CountOutsideBatch" or "PrivateOrdinal.CountOutsideCapacity" or "NFamilyGpuCapacityExceeded" or "N.PrivateCountInvalid") {}
        Bootstrap.RuntimeLog.TryBackgroundInfo("bonesCapsuleBoundaryLifecycle=true;tail=true;zero=true;cancelReuse=true;invalidCountThrows=true;orderedAbi1=true");
    }
    public void Dispose()
    {
        if(_disposed)return; _disposed=true;
        PrivateOrdinalBuffer.DisposeAll(_referenceR,_referenceN,_recompute,_r,_n,_ordinals);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"bonesCapsuleBoundaryTiming=true;mode={_mode};batches={_batches};input={_inputs};pairInputs={_pairs};privatePopulation={_middle};output={_outputs};zeroSurvivorBatches={_zero};routeAOnly={_aOnly};routeBOnly={_bOnly};routeBoth={_both};routeAEvaluations={_evalA};routeBEvaluations={_evalB};heavyA={_heavyA};heavyB={_heavyB};nDispatchSyncMs={_nDispatch};nReadbackMs={_nReadback};nCanonicalMs={_nCanonical};rDispatchSyncMs={_rDispatch};rReadbackMs={_rReadback};rCanonicalMs={_rCanonical};canonicalMs={_canonical};intermediatePayloadBytes=0;publicPayloadBytes={_outputs*4};dispatchesPerNonemptyBatch={(_mode=="local" ? 3 : 4)};submitsPerNonemptyBatch={(_mode=="local" ? 1 : 2)};verification={_referenceN is not null};removedExactRejected={_removed};unsafeSameRouteRoots={_unsafe};validA={_validA};validB={_validB};validBoth={_validBoth};sameRouteFailure={_sameFailure}");
    }
}
