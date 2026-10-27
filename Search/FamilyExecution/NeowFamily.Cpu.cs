using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            if (_composite is not null || _transformations is not null || _request.ProfileId != Compatibility.RuntimeProfileId.Beta111) yield break;
            var plan = NeowIdentityCpuPlan.TryCompile(_plan);
            var local = plan is null ? NeowSingleplayerLocalCpuPlan.TryCompile(_plan) : null;
            var gate = plan is null && local is null ? NeowCpuPreGate.TryCompile(_plan) : null;
            // Eligible AVX2 bodies default to packed roots and a four-root necessary
            // gate. Explicit off keeps packed scalar; legacy retains byte roots.
            var fourWay = NeowCpuFourWayPreGate.TryCompile(_plan);
            Func<ulong,bool> matcher = local is not null ? local.Matches : plan is not null ? plan.Matches : gate is not null ? gate.Matches : _cpuMatcher;
            foreach (int workers in FamilyCpuExecution.WorkerConfigurations(_request))
                yield return new FamilyCpuExecution(_request, this, local is not null ? local.Revision : gate is not null ? "N.Neow.Cpu.IdentityPreGate.20261015.v3" : plan is null ? "N.Neow.Cpu.LocalReplay.20261015.v3" : "N.Neow.Cpu.IdentityPair.20260912.v1",
                    matcher, workers, g => fourWay is not null || workers != 1 ? null : gate is not null ? NeowPhysicalPricing.QuoteCpuPreGate(this,gate,g) : (plan is null || _plan.Authority.BonesEligibleRelicIds.Length!=28 || _plan.Authority.EligibleCurseRelicIds.Length!=10 ? null : FamilyCpuReferenceCost.Quote(_request,g,FamilyId,"N.BonesIdentityTwoSource",1,"28.306-28.612",equivalentIdentityWork:true)) ?? NeowPhysicalPricing.QuoteCpuIdentity(this,g) ?? NeowPhysicalPricing.QuoteCpuLocal(this,g),
                    packedSeedRoots: fourWay is not null, fourWayPreGate: fourWay is { Enabled: true } ? fourWay : null);
        }
    }
}
