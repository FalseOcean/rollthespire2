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
            var authority = Beta111MerchantColorlessAuthority.From(_plan.Authority);
            bool slotWorkers = !sequence && authority.UncommonPool.Count==32 && plan is not null && _plan.Evaluation.MerchantColorlessConditions.Count == 3 &&
                _plan.Evaluation.MerchantColorlessConditions.All(c => c.Slot == MerchantColorlessSlot.Uncommon) &&
                _plan.Evaluation.MerchantColorlessConditions.Select(c => c.MerchantOrdinal).Order().SequenceEqual(new[]{1,2,3});
            bool priced = _plan.Authority.PlayersCount == 1 && slotWorkers;
            foreach(int workers in FamilyCpuExecution.WorkerConfigurations(_plan))
                yield return new FamilyCpuExecution(_plan,this,plan is null ? "S.MerchantShopColorless.Cpu.Reference.20260912.v1" : sequence ? "S.MerchantShopColorless.Cpu.Sequence.20261013.v2" : "S.MerchantShopColorless.Cpu.Slot.20261013.v2",
                    plan is null ? root => MerchantShopColorlessQueryEvaluator.Evaluate(_plan.Evaluation,root,_plan.Authority).Disposition != SearchDisposition.NoMatch : plan.Matches,
                    workers,g => (priced && workers is 1 or 16 ? FamilyCpuReferenceCost.Quote(_plan,g,FamilyId,"S.ThreeUncommonSlots",workers,workers==1?"23.197-23.656":"4.976-5.592",equivalentIdentityWork:true) : null) ??
                    (plan is not null && workers==1 ? sequence ? MerchantShopColorlessPhysicalPricing.QuoteCpuSequence(_plan,g,ExpectedFilteringCost) :
                        MerchantShopColorlessPhysicalPricing.QuoteCpuSlots(_plan,g) : null));
        }
    }
}
