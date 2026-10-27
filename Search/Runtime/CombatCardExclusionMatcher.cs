using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Runtime;

/// <summary>Pure per-continuation predicate over all initial reward options, never chosen cards.</summary>
internal static class CombatCardExclusionMatcher
{
    internal static SearchQueryEvaluation Evaluate(CombatCardExclusionSearchCondition condition,
        NormalCombatRewardRoutePredictionResult route, IReadOnlyList<ModelKey>? availableCardIdentities)
    {
        var banned = condition.CardKeys.ToHashSet(ModelKeyComparer.Instance);
        bool unknown = false;
        for (int ordinal = 1; ordinal <= condition.BattleCount; ordinal++)
        {
            var battles = route.Battles.Where(b => b.BattleOrdinal == ordinal).ToArray();
            if (battles.Length != 1) { unknown = true; continue; }
            var battle = battles[0];
            var cards = battle.CardRewards.Count > 0
                ? battle.CardRewards.SelectMany(reward => reward.Cards).ToArray() : battle.Cards;
            // This search model always produces a nonempty initial CardReward
            // (three standard options, plus modeled additions). No source contract
            // represents a proven zero-card reward. Empty DTOs are missing evidence.
            if (cards.Count == 0 || battle.CardRewards.Any(reward => reward.Cards.Count == 0)) unknown = true;
            // A proven hit disproves the condition even if another member or
            // battle is unavailable. Uncertain options cannot prove absence.
            foreach (var card in cards)
            {
                if (card.Precision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported || !card.CardKey.IsValid)
                    unknown = true;
                else if (banned.Contains(card.CardKey))
                    return SearchQueryEvaluation.NoMatch("CombatCardExclusion.Hit:Battle" + ordinal + ":" + card.CardKey.Serialized);
            }
            // Known potion/gold limitations stay in their own precision/status
            // fields; the current producer emits evaluated battles as Partial.
            // Unknown at battle level does not certify the initial reward topology
            // as complete, even when individual observed identities are known.
            if (battle.Precision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported) unknown = true;
        }
        if (unknown) return SearchQueryEvaluation.Unknown("CombatCardExclusion.RewardsIncomplete");
        if (availableCardIdentities is null)
            return SearchQueryEvaluation.Unknown("CombatCardExclusion.IdentityAuthorityUnavailable");
        var available = availableCardIdentities.ToHashSet(ModelKeyComparer.Instance);
        if (condition.CardKeys.Any(key => !available.Contains(key)))
            return SearchQueryEvaluation.Unknown("CombatCardExclusion.MemberUnavailable");
        return SearchQueryEvaluation.Match();
    }
}
