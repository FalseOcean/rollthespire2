using System.Diagnostics;
using Godot;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

// Bounded direct Capsule allocations and explicit oracle controls. Ordinals only;
// the existing R-owned mode-2 body serves both fused and serial execution.
internal static class DirectCapsuleAllocation
{
    // Complete coupled Dense N+R invocation, calibrated from the accepted clean
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

    // Only this Dense N+R invocation is compared; independent families, scan
    // length, target K and Exact work never participate in the physical choice.
    internal static bool TryCreateDefaultUnit(SearchModel request, FilterExecutionPreferences preferences,
        IReadOnlyList<FilterBodyDescription> registered, out FilterBodyDescription? unit)
    {
        unit = null;
        var nf = registered.SingleOrDefault(f => f.FamilyId == "N.Neow" && f.UsesGpu && f.Coverage.Count == 1);
        if (nf is null || !registered.Any(f => f.FamilyId == "R.Relic" && f.UsesGpu) ||
            !NeowReplayPlan.HasCapsule(request) || !FamilyPhysicalQuote.HasReferenceBackend()) return false;
        var n = NeowReplayPlan.Compile(request, false);
        if (n.Bones || n.RequireBones || !n.DirectNestedVanilla111 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.Authority.NoRunModifiers != true ||
            request.Authority.AllCharacterCardPoolsUnlocked != true ||
            !NeowCapsuleComposite.TryCreate(request, n, out var composite) || composite is null ||
            !NeowFamilyGpuPlan.TryCreate(n, out var gpu, out _) || gpu is null) return false;
        string serial = n.Selected == Beta110FastRelicCatalog.SmallCapsule ? "rarity-serial" : "entry-rarity-serial";
        double? fusedNs = DenseReferenceNanoseconds(n, composite, "fused");
        double? serialNs = DenseReferenceNanoseconds(n, composite, serial);
        if (!serialNs.HasValue || !fusedNs.HasValue) return false;
        string mode = serialNs.Value < fusedNs.Value ? serial : "fused";
        double? nSurvival = nf.Survival.SurvivalProbability;
        double? rSurvival = composite.Projection.Survival(new HashSet<string> { "N.Neow" }).SurvivalProbability;
        var survival = nSurvival.HasValue && rSurvival.HasValue
            ? FamilySurvivalProjection.Resolved("N.Neow", nSurvival.Value * rSurvival.Value,
                "CompositeCoverage=N.Neow,R.Relic;RConditionalOnN")
            : FamilySurvivalProjection.Unresolved("N.Neow", "CompositeJointSurvivalUnavailable");
        unit = DirectCapsuleInvocation.Prepare(request, n, gpu, composite, mode, false, survival);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"directCapsuleUnitSelected=true;mode={mode};fusedDenseNs={fusedNs:G17};serialDenseNs={serialNs:G17};" +
            "comparisonScope=OneCoupledDenseNrUnit;NoSearchSuffixOrExactTailPrice;transport=ordinalOnly");
        return true;
    }

    internal static double? DenseReferenceNanoseconds(NeowReplayPlan n, NeowCapsuleComposite composite, string mode)
    {
        string? key = CostKey(n, composite, mode);
        if (key is null || !FamilyPhysicalQuote.HasReferenceBackend()) return null;
        bool small = n.Selected == Beta110FastRelicCatalog.SmallCapsule;
        uint first = composite.Metadata[5], second = small ? 0 : composite.Metadata[8];
        if (!small && first > second) (first, second) = (second, first);
        (double fused, double serial) = (small, first, second) switch {
            (true, 1, 0) => (2.8415, 1.0610), (true, 2, 0) => (2.3871, .8796),
            (true, 3, 0) => (2.0353, .6580), (false, 1, 1) => (1.7658, .5084),
            (false, 1, 2) => (1.9689, .5206), (false, 1, 3) => (1.6708, .4929),
            (false, 2, 3) => (1.2277, .4681), (false, 3, 3) => (.7614, .3897),
            _ => (0, 0) };
        if (serial == 0) return null;
        double reference = GpuReferenceCostAtlas.Peak(key) is double peak ? 1e9 / peak : mode == "fused" ? fused : serial;
        // This is a whole public coupled-call reference. A numerical-only
        // device ratio cannot rescale its host transport/control component.
        return reference;
    }

    internal static bool TrySelect(PreparedSearch request, IReadOnlyList<FilterBodyDescription> registered,
        out FamilyExecutionPlan? selected, string? explicitMode=null)
    {
        selected=null;
        var model = request.Model;
        string mode=explicitMode ?? System.Environment.GetEnvironmentVariable("RT2_DIRECT_CAPSULE_EXPERIMENT") ?? "";
        if(mode.Length==0)return false;
        bool verify=mode.StartsWith("verify-",StringComparison.Ordinal);
        string path=verify?mode[7..]:mode;
        if(path is not ("keep" or "fused" or "serial" or "pregate-serial" or "pregate-fused" or "serial-direct" or "rarity-serial" or "entry-rarity-serial"))
            throw new InvalidOperationException("DirectCapsule.UnknownExperiment");
        var n=NeowReplayPlan.Compile(model,false);
        if(n.Bones || n.RequireBones || !n.DirectNestedVanilla111 || !model.Authority.CanUseCurrentModel || model.Authority.PlayersCount!=1 ||
            model.Authority.NoRunModifiers!=true || model.Authority.AllCharacterCardPoolsUnlocked!=true ||
            !NeowCapsuleComposite.TryCreate(model,n,out var composite) || composite is null ||
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
        FilterBodyDescription[] ordered=[DirectCapsuleInvocation.Prepare(model,n,gpu,composite,path,verify),
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

internal static class DirectCapsuleInvocation
{
    internal static string PhysicalRevision(string mode) => "N.Neow.Composite.RCapsule.DirectExperiment." + mode + ".CanonicalAbi1Ready.20260909.v1" + NeowIdentityOfferFacts.Revision + GpuRngRotation.Revision;
    internal static FilterBodyDescription Prepare(SearchModel model, NeowReplayPlan replay,
        NeowFamilyGpuPlan plan, NeowCapsuleComposite composite, string mode, bool verify,
        FamilySurvivalProjection? modeledSurvival = null)
    {
        const string id = "N.Neow";
        string revision = PhysicalRevision(mode);
        var preparation = new PreparedFilter(id) {
            Coverage = [id, "R.Relic"], AnalyticalCost = new(id, PrivateOrdinalBuffer.Capacity, [], "DirectCapsule.Unpriced"),
            Remainders = replay.FilterRemainders,
            Survival = modeledSurvival ?? FamilySurvivalProjection.Unresolved(id, "DirectCapsule.Experiment")
        };
        return new(preparation, revision, model.ProfileId) {
            UsesGpu = true, InputModes = FilterInputModes.Dense, InputIdentity = model.SnapshotFingerprint,
            PlayersCount = model.Authority.PlayersCount, WindowCapacity = PrivateOrdinalBuffer.Capacity,
            NumericRequirements = GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64,
            PerformanceProjection = _ => new(id, revision, "Neutral", 1, "BoundedDirectCapsule", true),
            Cost = new() {
                CompleteCallPeakKey = DirectCapsuleAllocation.CostKey(replay, composite, mode) ?? "",
                MinimumComparableInput = _ => int.MaxValue,
                Quote = (selected, geometry, _) => {
                    if (geometry.ComputationOnly || geometry.CompactInput || geometry.PrivateInput || geometry.PrivateOutput ||
                        !FamilyPhysicalQuote.AdmittedRequest(geometry) ||
                        DirectCapsuleAllocation.DenseReferenceNanoseconds(replay, composite, mode) is not double ns) return null;
                    return FamilyGpuReadback.Reference(selected, new(id, "DirectCapsule.CoupledDenseUnit", ns,
                        PrivateOrdinalBuffer.Capacity, null, "OfflineCleanDirectCapsule20260909;CanonicalNrIncludesInternalEdges;" + GpuReferenceCostAtlas.Provenance,
                        PublicTransportClass: "CompleteCanonicalAbi1"));
                }
            },
            CreateExecution = (body, _) => new GpuExperimentExecution<DirectCapsuleGpu>(body,
                device => new(device, plan, replay, composite, mode, verify), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var ms);
                    return new(output, ms);
                }, resource => { resource.LogEvidence(); resource.Dispose(); }, resource => (resource.Device, resource.SetupMs), verify)
        };
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
