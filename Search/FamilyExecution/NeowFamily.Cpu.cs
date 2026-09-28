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
            if (_composite is not null || _request.ProfileId != Compatibility.RuntimeProfileId.Beta111) yield break;
            var plan = NeowIdentityCpuPlan.TryCompile(_plan);
            var gate = plan is null ? NeowCpuPreGate.TryCompile(_plan) : null;
            Func<ulong,bool> matcher = plan is not null ? plan.Matches : gate is not null ? gate.Matches : root => NeowFamilyReplay.Matches(root, _plan);
            yield return new FamilyCpuExecution(_request, this, gate is not null ? "N.Neow.Cpu.IdentityPreGate.20260913.v1" : plan is null ? "N.Neow.Cpu.LocalReplay.20260905.v1" : "N.Neow.Cpu.IdentityPair.20260912.v1",
                matcher, 1, g => gate is not null ? NeowPhysicalPricing.QuoteCpuPreGate(this,gate,g) : (plan is null || _plan.Authority.BonesEligibleRelicIds.Length!=28 || _plan.Authority.EligibleCurseRelicIds.Length!=10 ? null : FamilyCpuReferenceCost.Quote(_request,g,FamilyId,"N.BonesIdentityTwoSource",1,"28.306-28.612",equivalentIdentityWork:true)) ?? NeowPhysicalPricing.QuoteCpuIdentity(this,g) ?? NeowPhysicalPricing.QuoteCpuLocal(this,g));
        }
    }
}
