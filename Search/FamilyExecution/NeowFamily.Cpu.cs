using RolltheSpire2.Compatibility;
namespace RolltheSpire2.Search.FamilyExecution;
internal sealed partial class NeowFamily
{
    private IEnumerable<FilterBodyDescription> DescribeCpu(PreparedFilter prepared,IRuntimeProfile profile,int workerBudget)
    {
        if(_composite is not null || _transformations is not null || _profile.ProfileId!=RuntimeProfileId.Beta111)yield break;
        bool capsuleNiche=HasCapsuleNicheContinuation;
        var pair=capsuleNiche ? null : NeowIdentityCpuPlan.TryCompile(_plan);
        var local=capsuleNiche || pair is not null ? null : NeowSingleplayerLocalCpuPlan.TryCompile(_plan,_cpuPositiveIdentity);
        var gate=pair is null && local is null ? NeowCpuPreGate.TryCompile(_plan) : null;
        var fourWay=NeowCpuFourWayPreGate.TryCompile(_plan);
        Func<ulong,bool> matcher=capsuleNiche ? _cpuMatcher : local is not null ? local.Matches : pair is not null ? pair.Matches :
            _cpuPositiveIdentity is not null ? _cpuPositiveIdentity.Matches : gate is not null ? gate.Matches : _cpuMatcher;
        string revision=capsuleNiche ? CpuRevision : (local is not null ? local.Revision :
            (gate is not null ? "N.Neow.Cpu.IdentityPreGate.20261015.v3" : pair is null ? "N.Neow.Cpu.LocalReplay.20261015.v3" : "N.Neow.Cpu.IdentityPair.20260912.v1")+
            (_cpuPositiveIdentity is null ? "" : NeowCpuPositiveIdentityPlan.Revision))+NeowIdentityOfferFacts.Revision;
        if (_plan.Authority.UsesChildShuffle) revision += ".Modern113";
        revision += _plan.CapsuleNonCombatRevision;
        int workers=Math.Clamp(workerBudget,1,32);
        if(capsuleNiche) prepared=prepared with {
            AnalyticalCost=new(FamilyId,65536,[],"SoloCapsuleNicheCpuFullWorkUnpriced"),
            Survival=FamilySurvivalProjection.Unresolved(FamilyId,"SoloCapsuleNicheJointProbabilityUnpriced"),
            ExpectedFilteringCost=new(FamilyId,0,null,null,1,[],"SoloCapsuleNicheCpuWorkAndJointProbabilityUnpriced"),
            SurvivalResolver=null,ExpectedCostResolver=null
        };
        Func<FamilyPhysicalQuoteRequest,FamilyPhysicalQuote?> raw=g=>capsuleNiche ? null : NeowWorkEstimate.Cpu(this,_workEstimate,g,workers,
            gate,fixedPair:pair is not null,directLocal:local is not null,fourWay:fourWay);
        var snapshot=FamilyAuxiliaryEvidence.Read<CpuCostSnapshot?>(nameof(NeowFamily),"CostSnapshot",CpuCostCalibration.Capture,null);
        yield return new(prepared,revision,_profile.ProfileId)
        {
            CpuWorkers=workers,WindowCapacity=65536,PreferredRootWindowSize=65536,CpuChunkCapacity=CpuFilterExecution.Capacity,InputIdentity=_inputIdentity,
            PlayersCount=_plan.Authority.PlayersCount,
            PerformanceProjection=compact=>CpuFilterExecution.Describe(FamilyId,revision,workers,65536,true,fourWay is not null,compact,fourWay is {Enabled:true}?fourWay:null),
            Cost=new(){CalibrationQuote=raw,CanonicalScope=_canonicalScope,
                CanonicalWorkShape=_=>FamilyCanonicalCostShape.DescribeNeow(_plan,_gpuPlan),
                OptimizationShape=_=>fourWay is not { Enabled:true } ? "" : $";N.FourWayGate.v1:bound={fourWay.Bound};allowed={System.Numerics.BitOperations.PopCount(fourWay.AllowedIndexes)}",
                Quote=(body,g,passed)=>FamilyCanonicalMeasuredCost.TryDenseQuote(body,g) ??
                    (raw(g) is {} quote ? snapshot?.Local(quote,body.ResolveConditionPerformance(g.CompactInput).PhysicalImplementationRevision,workers,g.CompactInput) ?? quote : null)},
            CreateExecution=BindCpu(profile,matcher,fourWay,_sparseRefinement)
        };
    }
    private static Func<FilterBodyDescription,FilterRuntimeServices,IFilterExecution> BindCpu(IRuntimeProfile profile,
        Func<ulong,bool> matcher,NeowCpuFourWayPreGate? fourWay,NeowCapsuleSparseRefinement? sparse)=>(body,services)=>
        NeowCapsuleSparseRefinementRun.WrapCpu(body,profile,matcher,true,fourWay is not null,fourWay is {Enabled:true}?fourWay:null,sparse);
}
