using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class MerchantShopColorlessFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            var plan = MerchantShopColorlessCpuPlan.TryCompile(_plan);
            bool sequence = _plan.Evaluation.MerchantColorlessSequenceConditions.Count != 0;
            bool priced = !sequence && Beta111MerchantColorlessAuthority.From(_plan.Authority).UncommonPool.Count==32 && plan is not null && _plan.Evaluation.MerchantColorlessConditions.Count == 3 &&
                _plan.Evaluation.MerchantColorlessConditions.All(c => c.Slot == MerchantColorlessSlot.Uncommon) &&
                _plan.Evaluation.MerchantColorlessConditions.Select(c => c.MerchantOrdinal).Order().SequenceEqual(new[]{1,2,3});
            foreach(int workers in (priced ? new[]{1,16} : new[]{1}).Where(w=>w<=_plan.WorkerCount))
                yield return new FamilyCpuExecution(_plan,this,plan is null ? "S.MerchantShopColorless.Cpu.Reference.20260912.v1" : sequence ? "S.MerchantShopColorless.Cpu.Sequence.20260913.v1" : "S.MerchantShopColorless.Cpu.Slot.20260912.v1",
                    plan is null ? root => MerchantShopColorlessQueryEvaluator.Evaluate(_plan.Evaluation,root,_plan.Authority).Disposition != SearchDisposition.NoMatch : plan.Matches,
                    workers,g => (priced ? FamilyCpuReferenceCost.Quote(_plan,g,FamilyId,"S.ThreeUncommonSlots",workers,workers==1?"53.103-54.299":"26.360-26.735",equivalentIdentityWork:true) : null) ??
                    (plan is not null && workers==1 ? sequence ? MerchantShopColorlessPhysicalPricing.QuoteCpuSequence(_plan,g,ExpectedFilteringCost) :
                        MerchantShopColorlessPhysicalPricing.QuoteCpuSlots(_plan,g) : null));
        }
    }
}
