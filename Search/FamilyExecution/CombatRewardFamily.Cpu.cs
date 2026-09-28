using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class CombatRewardFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            if (_request.ProfileId != Compatibility.RuntimeProfileId.Beta111) yield break;
            var p = Replay.Plan;
            if (Replay.CanUsePotionPrefix)
                yield return new FamilyCpuExecution(_request,this,"C.CombatReward.Cpu.PotionPrefix.20260913.v1",
                    Replay.MatchesPotionPrefix,1,g=>CombatRewardPhysicalPricing.QuoteCpuPotionPrefix(_request,Replay,g));
            var potions=Replay.Catalog.CombatRewardPotionPool;
            bool priced = potions.Common.Length==16 && potions.Uncommon.Length==16 && potions.Rare.Length==16 && !p.OpeningConsumption.HasReplay && p.ExplicitContext.InfluenceFlags == Beta110CombatRewardInfluenceFlags.None &&
                p.MaximumBattleOrdinal == 3 && p.PredicateCount == 3 &&
                p.Predicates.Select(row => (int)row.BattleOrdinal).SequenceEqual(new[]{1,2,3}) &&
                p.Predicates.All(row => row.PotionRequirement==NormalCombatPotionRequirement.MustDrop && row.PotionAny.Concat(row.PotionAll).All(id=>potions.Common.Contains(id)) && !row.HasCardPredicate && row.PotionAny.Length + row.PotionAll.Length == 1 && row.PotionBan.Length == 0 && !row.HasGoldPredicate);
            foreach(int workers in (priced ? new[]{1,16} : new[]{1}).Where(w=>w<=_request.WorkerCount))
                yield return new FamilyCpuExecution(_request,this,"C.CombatReward.Cpu.AuthoredPrefix.20260912.v1",Replay.Matches,workers,
                    g => (priced ? FamilyCpuReferenceCost.Quote(_request,g,FamilyId,"C.ThreeOrderedCommonPotions",workers,workers==1?"1290.730-1304.638":"147.877-152.369") : null) ??
                    (workers==1 ? CombatRewardPhysicalPricing.QuoteNeutralModel(_request,Replay,_gpuPlan,g,cpu:true) : null));
        }
    }
}
