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
            bool prefixWorkers = Replay.CanUsePotionPrefix && !p.OpeningConsumption.HasReplay &&
                !p.ExplicitContext.ChangesCurrentFastObservables && p.ExplicitContext.InfluenceFlags == Beta110CombatRewardInfluenceFlags.None &&
                p.MaximumBattleOrdinal <= 3 && p.PredicateCount <= 3 && p.Predicates.All(row => !row.HasGoldPredicate) &&
                potions.Common.Length == 16 && potions.Uncommon.Length == 16 && potions.Rare.Length == 16;
            var cards = Replay.Catalog.CombatRewardCardPool;
            bool standardCards = cards.Common.Length == 20 && cards.Uncommon.Length == 35 && cards.Rare.Length == 25;
            bool selectivePrefix = p.Predicates.Length == 3 && p.Predicates.Select(row => (int)row.BattleOrdinal).SequenceEqual(new[]{1,2,3}) &&
                p.Predicates.All(row => row.PotionRequirement == NormalCombatPotionRequirement.MustDrop &&
                    row.PotionAny.Length + row.PotionAll.Length == 1 && row.PotionBan.Length == 0);
            bool rareLatePrefix = prefixWorkers && standardCards && p.Predicates.Length == 2 &&
                p.Predicates.Select(row => (int)row.BattleOrdinal).SequenceEqual(new[]{2,3}) &&
                p.Predicates.All(row => row.PotionRequirement == NormalCombatPotionRequirement.MustDrop &&
                    row.PotionAny.Length + row.PotionAll.Length == 1 && row.PotionBan.Length == 0 &&
                    row.PotionAny.Concat(row.PotionAll).All(id => potions.Rare.Contains(id)));
            if (Replay.CanUsePotionPrefix)
                foreach(int workers in (prefixWorkers && selectivePrefix && standardCards ? new[]{1,2,4,8} : rareLatePrefix ? new[]{1,8} : new[]{1}).Where(w=>w<=_request.WorkerCount))
                    yield return new FamilyCpuExecution(_request,this,"C.CombatReward.Cpu.PotionPrefix.20260913.v1",
                        Replay.MatchesPotionPrefix,workers,g=>workers==1 ? CombatRewardPhysicalPricing.QuoteCpuPotionPrefix(_request,Replay,g) : null);
            bool cardWorkers = standardCards && Replay.RouteCount == 1 && p.CardPoolAuthorityExact && p.PotionPoolAuthorityExact &&
                potions.Common.Length == 16 && potions.Uncommon.Length == 16 && potions.Rare.Length == 16 &&
                !p.OpeningConsumption.HasReplay && !p.ExplicitContext.ChangesCurrentFastObservables && p.ExplicitContext.InfluenceFlags == Beta110CombatRewardInfluenceFlags.None &&
                p.MaximumBattleOrdinal == 3 && p.PredicateCount == 3 && p.Predicates.Select(row => (int)row.BattleOrdinal).SequenceEqual(new[]{1,2,3}) &&
                p.Predicates.All(row => row.CardAny.Length + row.CardAll.Length == 1 && row.CardBan.Length == 0 &&
                    !row.HasPotionDropPredicate && !row.HasPotionIdentityPredicate && !row.HasGoldPredicate);
            foreach(int workers in (priced ? new[]{1,16} : cardWorkers ? new[]{1,2,4,8} : new[]{1}).Where(w=>w<=_request.WorkerCount))
                yield return new FamilyCpuExecution(_request,this,"C.CombatReward.Cpu.AuthoredPrefix.20260912.v1",Replay.Matches,workers,
                    g => (priced ? FamilyCpuReferenceCost.Quote(_request,g,FamilyId,"C.ThreeOrderedCommonPotions",workers,workers==1?"1290.730-1304.638":"147.877-152.369") : null) ??
                    (workers==1 ? CombatRewardPhysicalPricing.QuoteSixBattleModel(_request,Replay,_gpuPlan,g,cpu:true) ??
                        CombatRewardPhysicalPricing.QuoteNeutralModel(_request,Replay,_gpuPlan,g,cpu:true) : null));
        }
    }
}
