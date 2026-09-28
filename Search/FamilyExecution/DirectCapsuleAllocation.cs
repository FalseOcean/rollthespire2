using System.Diagnostics;
using Godot;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// Bounded direct Capsule allocations and explicit oracle controls. Ordinals only;
// the existing R-owned mode-2 body serves both fused and serial execution.
internal static class DirectCapsuleAllocation
{
    // Complete bounded allocation quote, calibrated from the accepted clean
    // direct-capsule-v1 confrontation. Rows describe source/rarity work, never
    // target identity. Existing numerical implementations are reused unchanged.
    internal static string? CostKey(NeowReplayPlan n, NeowCapsuleComposite composite, string mode)
    {
        bool small=n.Selected==Beta110FastRelicCatalog.SmallCapsule;
        if(n.Bones || composite.Metadata[2]!=(small?1u:2u) || mode is not ("fused" or "rarity-serial" or "entry-rarity-serial")) return null;
        uint a=composite.Metadata[5],b=small?0:composite.Metadata[8];
        if(!small && a>b)(a,b)=(b,a);
        return $"DirectCapsule|{(small?"Small":"Large")}|{a}:{b}|{mode}|{composite.Revision(false,false)}|{DirectCapsuleInvocation.PhysicalRevision(mode)}|DirectNr.CanonicalFullWindow.20260912.v1";
    }

    internal static bool TrySelectPriced(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered, out FamilyExecutionPlan? selected)
    {
        selected = null;
        if (registered.OfType<NeowFamily>().SingleOrDefault() is not { } nf ||
            registered.OfType<CapsuleRelicFamily>().SingleOrDefault() is null ||
            registered.Any(f => f.FamilyId is not ("N.Neow" or "R.Relic" or "W.World"))) return false;
        if (!FamilyPhysicalQuote.HasReferenceBackend()) return false;
        var n = NeowReplayPlan.Compile(request, false);
        if (n.Bones || n.RequireBones || !n.DirectNestedVanilla111 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.Authority.NoRunModifiers != true ||
            request.Authority.AllCharacterCardPoolsUnlocked != true ||
            !NeowCapsuleComposite.TryCreate(request, n, out var composite) || composite is null ||
            !NeowFamilyGpuPlan.TryCreate(n, out var gpu, out _) || gpu is null) return false;
        bool small = n.Selected == Beta110FastRelicCatalog.SmallCapsule;
        if (composite.Metadata[2] != (small ? 1u : 2u)) return false;
        uint first = composite.Metadata[5], second = small ? 0 : composite.Metadata[8];
        if (!small && first > second) (first, second) = (second, first);
        (double fusedNs, double serialNs) = (small, first, second) switch {
            (true, 1, 0) => (2.8415, 1.0610), (true, 2, 0) => (2.3871, .8796),
            (true, 3, 0) => (2.0353, .6580), (false, 1, 1) => (1.7658, .5084),
            (false, 1, 2) => (1.9689, .5206), (false, 1, 3) => (1.6708, .4929),
            (false, 2, 3) => (1.2277, .4681), (false, 3, 3) => (.7614, .3897),
            _ => (0, 0) };
        if (serialNs == 0) return false; // Unmeasured rarity composition is not a guessed midpoint.
        double? sn = nf.Survival.SurvivalProbability;
        double? sr = composite.Projection.Survival(new HashSet<string> { "N.Neow" }).SurvivalProbability;
        if (sn is not (>= 0 and <= 1) || sr is not (>= 0 and <= 1)) return false;
        double nrSurvival = sn.Value * sr.Value;
        PrivateSerialChainPricing.Node[] tail = [];
        var w = registered.OfType<WorldFamily>().SingleOrDefault();
        if (w is not null) {
            if (!WorldFamilyGpuPlan.Supports(w.Replay)) return false;
            var wp = new WorldFamilyGpuPlan(w.Replay);
            double? sw = PrivateSerialChainPricing.ProjectWorldSurvival(request, 1);
            if (wp.VariantOnly || w.Replay.Plan.MaxRequiredAct is not (2 or 3) || sw is null) return false;
            tail = [WorldPhysicalPricing.LegacyDirectCapsuleTail(w.Replay, wp, sw.Value)];
        }
        double roots = request.ScanCount;
        var joint = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request));
        if (joint.JointlyPriced && joint.Probability is > 0 and <= 1)
            roots = Math.Min(roots, Math.Max(1, request.TargetMatchCount) / joint.Probability.Value);
        if (roots < PrivateOrdinalBuffer.Capacity) return false; // Frontier geometry only measured.
        var costSnapshot = GpuCostCalibration.Capture();
        double Recurring(double count, double ns, string mode)
        {
            string key=CostKey(n,composite,mode)!;
            double referenceNs=GpuReferenceCostAtlas.Peak(key) is double peak ? 1e9/peak : ns;
            // Complete NR canonical reference already includes its submits,
            // headers and final ABI1. The former per-batch detail-log surcharge
            // no longer exists in default execution; do not add generic NR edges.
            return count * referenceNs * costSnapshot.Ratio(key) / 1e6 +
            (tail.Length == 0 ? 0 : count * nrSurvival * 5 / 1e6 +
                PrivateSerialChainPricing.Price(count, tail, false, true, nrSurvival, true)) * costSnapshot.GlobalRatio;
        }
        string serialMode=small ? "rarity-serial" : "entry-rarity-serial";
        double commonSetup = 100 + tail.Sum(node => node.SetupMs);
        double fusedMs = commonSetup + 195 + Recurring(roots, fusedNs, "fused");
        double serialSetup = small ? 260 : 200;
        double serialMs = commonSetup + serialSetup + Recurring(roots, serialNs, serialMode);
        double fusedRecurringMs = Recurring(roots, fusedNs, "fused"), serialRecurringMs = Recurring(roots, serialNs, serialMode);
        bool useSerial = serialRecurringMs * 1.1 + PrivateOrdinalAllocationPricing.DecisionUncertaintyMs < fusedRecurringMs;
        string mode = useSerial ? small ? "rarity-serial" : "entry-rarity-serial" : "fused";
        var invocation = new DirectCapsuleInvocation(request, n, gpu, composite, mode, false);
        var result = FamilyPlanner.InOrder([invocation, ..registered.Where(f => f is WorldFamily)]);
        if (!result.Stages.SelectMany(s => s.Coverage).ToHashSet().SetEquals(registered.SelectMany(f => f.Coverage)))
            throw new InvalidOperationException("DirectCapsule.PricedCoverageMismatch");
        selected = result with {
            SelectionPolicyId = "FamilyPlanner.DirectCapsule.WorkShape.20260910.v2",
            RankingAuthority = FamilyPlannerRankingAuthority.OfflineAllocationWorkShape,
            DecisionEvidence = [$"SourceRarityWorkShape={small}:{first}:{second}",
                $"GpuConditionRatio={costSnapshot.Ratio(CostKey(n,composite,mode)!):G17};GlobalGpuCostRatio={costSnapshot.GlobalRatio:G17};SameRunFrozen=true",
                $"FusedCompleteMs={fusedMs:G17};SerialCompleteMs={serialMs:G17};ModeledNrSurvival={nrSurvival:G17}",
                "OfflineCleanDirectCapsule20260909;CanonicalIncludesInternalEdges;NoObservedSurvival;Objective=SteadyStateWallToKExact;StartupExcludedFromDecision",
                $"FusedRecurringMs={fusedRecurringMs:G17};SerialRecurringMs={serialRecurringMs:G17}"],
            EstimateCanonicalMilliseconds = count => Recurring(count, useSerial ? serialNs : fusedNs, mode),
            EstimatedSetupMilliseconds = commonSetup + (useSerial ? serialSetup : 195),
            EstimatedTerminalSurvival = nrSurvival * (tail.Length == 0 ? 1 : tail[0].Survival!.Value),
            CompleteQuoteEvidence = "DirectCapsule.Complete.20260910.v1;NoDefaultDiagnosticFee.20260913;RTX4060Laptop.D3D12;OfflineFrontier" };
        Bootstrap.RuntimeLog.TryBackgroundInfo($"directCapsuleSelected=true;mode={mode};normalSelection=true;automaticPlannerPromotion={useSerial};source={n.Selected};coverage=N.Neow,R.Relic;Rowner=R;transport=ordinalOnly");
        return true;
    }

    internal static bool TrySelect(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> registered,
        out FamilyExecutionPlan? selected, string? explicitMode=null)
    {
        selected=null;
        string mode=explicitMode ?? System.Environment.GetEnvironmentVariable("RT2_DIRECT_CAPSULE_EXPERIMENT") ?? "";
        if(mode.Length==0)return false;
        bool verify=mode.StartsWith("verify-",StringComparison.Ordinal);
        string path=verify?mode[7..]:mode;
        if(path is not ("keep" or "fused" or "serial" or "pregate-serial" or "pregate-fused" or "serial-direct" or "rarity-serial" or "entry-rarity-serial"))
            throw new InvalidOperationException("DirectCapsule.UnknownExperiment");
        var n=NeowReplayPlan.Compile(request,false);
        if(n.Bones || n.RequireBones || !n.DirectNestedVanilla111 || !request.Authority.CanUseCurrentModel || request.Authority.PlayersCount!=1 ||
            request.Authority.NoRunModifiers!=true || request.Authority.AllCharacterCardPoolsUnlocked!=true ||
            !NeowCapsuleComposite.TryCreate(request,n,out var composite) || composite is null ||
            !NeowFamilyGpuPlan.TryCreate(n,out var gpu,out _) || gpu is null ||
            registered.Any(f=>f.FamilyId is not ("N.Neow" or "R.Relic" or "W.World")))
        {
            if(explicitMode is not null) {
                Bootstrap.RuntimeLog.TryBackgroundInfo("directCapsuleRequested=true;applicable=false;normalAllocationRetained=true");
                return false;
            }
            throw new InvalidOperationException("DirectCapsule.UnsupportedExperimentShape");
        }
        // Explicit Owner calibration preset, resolved only after direct admission.
        // This selects measured physical controls, not automatic Planner pricing.
        if(path=="keep") {
            path=n.Selected==Beta110FastRelicCatalog.SmallCapsule?"rarity-serial":"entry-rarity-serial";
            mode=(verify?"verify-":"")+path;
            Bootstrap.RuntimeLog.TryBackgroundInfo($"directCapsulePreset=keep;resolvedMode={mode};source={n.Selected};automaticPlannerPromotion=false");
        }
        IFamilyInvocation[] ordered=[new DirectCapsuleInvocation(request,n,gpu,composite,path,verify),
            ..registered.Where(f=>f.FamilyId is not ("N.Neow" or "R.Relic"))];
        var stages=ordered.Select((f,i)=>{
            var condition=f.ResolveConditionPerformance(i!=0);
            return new FamilyExecutionPlanStage(f,f.Coverage,i!=0,i==0?1:double.NaN,null,null,null,condition,
                GpuCostCalibration.ResolveReference(condition),null,null,null,null,i);
        }).ToArray();
        selected=new("DirectCapsule."+mode,"DirectCapsule.Explicit.20260909",FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
            stages,null,["ExplicitCapsulePhysicalConfrontation"],["NoPricingChange"]);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"directCapsuleSelected=true;mode={mode};source={n.Selected};coverage=N.Neow,R.Relic;Rowner=R;transport=ordinalOnly;modelChanged=false");
        return true;
    }
}

internal sealed class DirectCapsuleInvocation(ExactSearchExecutionRequest request, NeowReplayPlan replay,
    NeowFamilyGpuPlan plan, NeowCapsuleComposite composite, string mode, bool verify) : IFamilyInvocation
{
    internal static string PhysicalRevision(string mode) => "N.Neow.Composite.RCapsule.DirectExperiment."+mode+".CanonicalAbi1Ready.20260909.v1";
    private readonly GpuCostSamples _costSamples=new();
    IEnumerable<GpuLocalPeak> IFamilyInvocation.CostPeaks=>_costSamples.Peaks;
    void IFamilyInvocation.LogExecutionEvidence() { _costSamples.WriteSummary(); _gpu?.LogEvidence(); }
    private DirectCapsuleGpu? _gpu;
    private long _input,_output;
    private int _batches;
    private double _ms;
    private bool _faulted;
    public string FamilyId=>"N.Neow";
    public IReadOnlyList<string> Coverage=>["N.Neow","R.Relic"];
    public FamilyAnalyticalCostProjection AnalyticalCost=>new(FamilyId,PrivateOrdinalBuffer.Capacity,[],"DirectCapsule.Unpriced");
    public FamilySurvivalProjection Survival=>FamilySurvivalProjection.Unresolved(FamilyId,"DirectCapsule.Experiment");
    public FamilyConditionPerformanceProjection ConditionPerformance=>new(FamilyId,
        PhysicalRevision(mode),"Neutral",1,"BoundedDirectCapsule",usesGpu:true);
    public FamilyPerformanceObservation CapturePerformanceObservation()=>new(ConditionPerformance,_gpu?.Device??"",_gpu?.SetupMs??0,
        0,0,_batches,0,_input,_output,_ms,false,_faulted?"PhysicalFailureOrRecovery":"BoundedCompositeCompleteQuoteOnly");
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context,FamilyObservationWindow observation,FamilyCandidateSet input,CancellationToken token)
    {
        if(_faulted||request.SnapshotFingerprint!=observation.ExactRequest.SnapshotFingerprint)throw new InvalidOperationException("DirectCapsule.InvocationInvariant");
        try {
            _gpu??=await context.ExecuteGpuAsync(rd=>new DirectCapsuleGpu(rd,plan,replay,composite,mode,verify),token).ConfigureAwait(false);
            var result=await context.ExecuteGpuAsync(_=>{var output=_gpu.Execute(input,token,out var ms);return(output,ms);},token).ConfigureAwait(false);
            _costSamples.Record(DirectCapsuleAllocation.CostKey(replay,composite,mode) ?? PhysicalRevision(mode), PrivateOrdinalBuffer.Capacity, input.Count, result.output.Count, -1, result.ms, -1, -1);
            if(!verify && input.Count==PrivateOrdinalBuffer.Capacity && DirectCapsuleAllocation.CostKey(replay,composite,mode) is string key)
                _costSamples.Observe(key,input.Count,result.ms,GpuCostCalibration.DeviceKey());
            _batches++;_input+=input.Count;_output+=result.output.Count;_ms+=result.ms;return result.output;
        } catch(OperationCanceledException){throw;} catch{_faulted=true;throw;}
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if(_gpu is not null)await context.ExecuteGpuAsync(_=>{_gpu.Dispose();return true;},CancellationToken.None).ConfigureAwait(false);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"directCapsuleSummary=true;mode={mode};batches={_batches};input={_input};output={_output};canonicalMs={_ms};faulted={_faulted};recovery=None");
    }
}

internal sealed class DirectCapsuleGpu : IDisposable
{
    private readonly PrivateOrdinalBuffer? _ordinals,_gateOrdinals;
    private readonly NeowFamilyGpuExecutor? _gate;
    private readonly NeowFamilyGpuExecutor _first;
    private readonly NeowFamilyGpuExecutor? _r,_reference,_entry,_rarity,_gatedEntry;
    private readonly NeowReplayPlan _replay;
    private readonly NeowCapsuleComposite _composite;
    private readonly string _mode;
    private bool _disposed,_checked;
    private readonly GpuCostSamples _physicalEvidence = new();
    internal void LogEvidence() => _physicalEvidence.WriteSummary(calibration: false);
    private readonly double _setupBaseMs;
    private double NeowSetupMs => (_gate?.SetupMs ?? 0) + _first.SetupMs + (_r?.SetupMs ?? 0) +
        (_reference?.SetupMs ?? 0) + (_entry?.SetupMs ?? 0) + (_rarity?.SetupMs ?? 0) + (_gatedEntry?.SetupMs ?? 0);
    internal double SetupMs=>_setupBaseMs+NeowSetupMs;
    internal string Device=>_first.Device;
    internal DirectCapsuleGpu(RenderingDevice rd,NeowFamilyGpuPlan plan,NeowReplayPlan replay,NeowCapsuleComposite composite,string mode,bool verify)
    {
        _replay=replay;_composite=composite;_mode=mode;var timer=Stopwatch.StartNew();
        try {
            if(mode=="rarity-serial") {
                _gateOrdinals=new(rd);_ordinals=new(rd);
                _gate=new(rd,plan,1,composite,privateOutput:_gateOrdinals,capsulePhysicalMode:4);
                _first=new(rd,plan,1,composite,privateInput:_gateOrdinals,privateOutput:_ordinals,capsulePhysicalMode:5);
                _r=new(rd,plan,1,composite,privateInput:_ordinals,capsulePhysicalMode:1);
            } else if(mode.EndsWith("serial",StringComparison.Ordinal) || mode=="serial-direct") {
                _ordinals=new(rd);
                _first=mode=="serial"?new(rd,plan,1,privateOutput:_ordinals):new(rd,plan,1,composite,privateOutput:_ordinals,capsulePhysicalMode:mode=="serial-direct"?5:mode=="entry-rarity-serial"?6:2);
                _r=new(rd,plan,1,composite,privateInput:_ordinals,capsulePhysicalMode:1);
            } else _first=new(rd,plan,1,composite,capsulePhysicalMode:mode=="fused"?0:3);
            if(verify) {
                _reference=new(rd,plan,1,composite);
                _entry=new(rd,plan,1);
                _rarity=new(rd,plan,1,composite,capsulePhysicalMode:4);
                _gatedEntry=new(rd,plan,1,composite,capsulePhysicalMode:2);
            }
            _setupBaseMs=timer.Elapsed.TotalMilliseconds-NeowSetupMs;
        }catch (Exception failure)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => PrivateOrdinalBuffer.DisposeAll(_gatedEntry,_rarity,_entry,_reference,_r,_first,_gate,_ordinals,_gateOrdinals), "DirectCapsule");
            throw;
        }
    }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input,CancellationToken token,out double ms)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!input.IsDense)throw new InvalidOperationException("DirectCapsule.DenseAdmissionRequired");
        double setupBefore=NeowSetupMs;
        var timer=Stopwatch.StartNew();NeowFamilyGpuMetrics nm,rm=default,pgm=default;
        int rarityCount=-1;
        int intermediate=-1;FamilyCandidateSet output;
        if(_r is not null){
            if(_gate is not null){rarityCount=_gate.ExecutePrivateOutput(input,token,out pgm);_gateOrdinals!.CheckPopulation(input.Batch,rarityCount);intermediate=_first.ExecutePrivateStage(input.Batch,rarityCount,token,out nm);}
            else intermediate=_first.ExecutePrivateOutput(input,token,out nm);_ordinals!.CheckPopulation(input.Batch,intermediate);output=_r.ExecutePrivate(input.Batch,intermediate,token,out rm);}
        else output=_first.Execute(input,token,out nm);
        ms=Math.Max(0,timer.Elapsed.TotalMilliseconds-(NeowSetupMs-setupBefore));
        void Record(string stage,int count,int pass,NeowFamilyGpuMetrics m) => _physicalEvidence.Record("DirectCapsule."+_mode+"|"+stage+"|"+DirectCapsuleInvocation.PhysicalRevision(_mode),NeowFamilyGpuExecutor.Capacity,count,pass,m.DispatchSyncMs,m.CanonicalMs,m.ReadbackMs,m.ReadbackBytes);
        if(_gate is not null)Record("Rarity.PrivateOutput",input.Count,rarityCount,pgm);
        Record(_r is null?"Fused.PublicOutput":"N.PrivateOutput",_gate is null?input.Count:rarityCount,_r is null?output.Count:intermediate,nm);
        if(_r is not null)Record("R.PrivateInput.PublicOutput",intermediate,output.Count,rm);
        if(_reference is not null) {
            var expected=_reference.Execute(input,token,out _);
            Equal(expected,output,"FusedParity");
            var entry=_entry!.Execute(input,token,out var em);
            var rarity=_rarity!.Execute(input,token,out var gm);
            var gated=_gatedEntry!.Execute(input,token,out _);
            var raritySet=rarity.EnumerateLogicalOrdinals().ToHashSet();
            var joint=FamilyCandidateSet.FromSortedAbi1(input.Batch,entry.EnumerateLogicalOrdinals().Where(raritySet.Contains).ToArray());
            Equal(joint,gated,"RarityEntryParity");
            if(_gateOrdinals is not null)_gateOrdinals.VerifyPopulation(input.Batch,rarityCount,rarity);
            if(_ordinals is not null)_ordinals.VerifyPopulation(input.Batch,intermediate,_mode is "serial" or "serial-direct"?entry:gated);
            // Existing independent CPU reference, both positive witnesses and a bounded dense prefix.
            var actual=output.EnumerateLogicalOrdinals().ToHashSet();
            foreach(var ordinal in input.EnumerateLogicalOrdinals().Take(2048).Concat(actual.Take(256)).Distinct()) {
                ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(input.Batch.BatchBase+ordinal));
                bool truth=NeowFamilyReplay.Matches(root,_replay)&&_composite.Reference.Matches(root);
                if(truth!=actual.Contains(ordinal))throw new InvalidDataException("DirectCapsule.CpuPopulationMismatch");
            }
            Bootstrap.RuntimeLog.TryBackgroundInfo($"directCapsuleDiagnostic=true;mode={_mode};input={input.Count};entry={entry.Count};rarity={rarity.Count};heavyEntered={gated.Count};entryCanonicalMs={em.CanonicalMs};entryDispatchMs={em.DispatchSyncMs};rarityDispatchMs={gm.DispatchSyncMs};fullPopulationParity=true");
            if(!_checked){_checked=true;CheckLifecycle(input.Batch);}
        }
        if (Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"directCapsuleBatch=true;mode={_mode};input={input.Count};rarityPopulation={rarityCount};rarityDispatchMs={pgm.DispatchSyncMs};intermediate={intermediate};output={output.Count};firstDispatchMs={nm.DispatchSyncMs};rDispatchMs={rm.DispatchSyncMs};firstCanonicalMs={nm.CanonicalMs};rCanonicalMs={rm.CanonicalMs};dispatches={(pgm.Groups==0?0:pgm.Dispatches)+(nm.Groups==0?0:nm.Dispatches)+(rm.Groups==0?0:rm.Dispatches)};readbackBytes={pgm.ReadbackBytes+nm.ReadbackBytes+rm.ReadbackBytes};readbackMs={pgm.ReadbackMs+nm.ReadbackMs+rm.ReadbackMs};canonicalMs={ms};intermediatePayloadBytes=0;verification={_reference is not null}");
        return output;
    }
    private static void Equal(FamilyCandidateSet expected,FamilyCandidateSet actual,string why){if(!expected.EnumerateLogicalOrdinals().SequenceEqual(actual.EnumerateLogicalOrdinals()))throw new InvalidDataException("DirectCapsule."+why);}
    private void CheckLifecycle(SearchBatch batch)
    {
        var tail=FamilyCandidateSet.Dense(new SearchBatch(batch.BatchBase+16777200,67));
        Execute(tail,CancellationToken.None,out _);
        Execute(FamilyCandidateSet.Dense(new SearchBatch(batch.BatchBase,0)),CancellationToken.None,out _);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        try{Execute(tail,cancelled.Token,out _);throw new InvalidDataException("DirectCapsule.CancelIgnored");}catch(OperationCanceledException){}
        Execute(tail,CancellationToken.None,out _);
        Bootstrap.RuntimeLog.TryBackgroundInfo("directCapsuleLifecycle=true;tail=true;zero=true;preCancellation=true;reuse=true");
    }
    public void Dispose(){if(_disposed)return;_disposed=true;PrivateOrdinalBuffer.DisposeAll(_gatedEntry,_rarity,_entry,_reference,_r,_first,_gate,_ordinals,_gateOrdinals);}
}
