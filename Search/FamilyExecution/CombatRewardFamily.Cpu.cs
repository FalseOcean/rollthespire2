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
            var potions=Replay.Catalog.CombatRewardPotionPool;
            bool priced = _request.Authority.PlayersCount == 1 && potions.Common.Length==16 && potions.Uncommon.Length==16 && potions.Rare.Length==16 && !p.OpeningConsumption.HasReplay && p.ExplicitContext.InfluenceFlags == Beta110CombatRewardInfluenceFlags.None &&
                p.MaximumBattleOrdinal == 3 && p.PredicateCount == 3 &&
                p.Predicates.Select(row => (int)row.BattleOrdinal).SequenceEqual(new[]{1,2,3}) &&
                p.Predicates.All(row => row.PotionRequirement==NormalCombatPotionRequirement.MustDrop && row.PotionAny.Concat(row.PotionAll).All(id=>potions.Common.Contains(id)) && !row.HasCardPredicate && row.PotionAny.Length + row.PotionAll.Length == 1 && row.PotionBan.Length == 0 && !row.HasGoldPredicate);
            if (Replay.CanUsePotionPrefix)
                foreach(int workers in FamilyCpuExecution.WorkerConfigurations(_request))
                    yield return new FamilyCpuExecution(_request,this,"C.CombatReward.Cpu.PotionPrefix.20261013.v2",
                        Replay.MatchesPotionPrefix,workers,g=>workers==1 ? CombatRewardPhysicalPricing.QuoteCpuPotionPrefix(_request,Replay,g) : null);
            foreach(int workers in FamilyCpuExecution.WorkerConfigurations(_request))
                yield return new FamilyCpuExecution(_request,this,Replay.CpuRevision,Replay.Matches,workers,
                    g => (priced ? FamilyCpuReferenceCost.Quote(_request,g,FamilyId,"C.ThreeOrderedCommonPotions",workers,workers==1?"169.972-170.826":"36.462-37.286") : null) ??
                    (workers==1 ? CombatRewardPhysicalPricing.QuoteSixBattleModel(_request,Replay,_gpuPlan,g,cpu:true) ??
                        CombatRewardPhysicalPricing.QuoteNeutralModel(_request,Replay,_gpuPlan,g,cpu:true) : null));
        }
    }
}
