using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private static void ValidateRewardEnchantmentMask(PredictorReward reward, ImmutableArray<int> indices)
    {
        RequirePlan(!indices.IsDefault && (indices.IsEmpty || reward.Kind == PredictorRewardKind.Card) &&
            indices.All(index => index >= 0 && index < reward.Cards.Length) &&
            indices.SequenceEqual(indices.Distinct().Order()));
        // Masked snapshots cannot carry a guessed enchantment as saved intent.
        RequirePlan(indices.All(index => reward.Cards[index] != null && reward.Cards[index].Enchantment == null &&
            reward.Cards[index].EnchantmentAmount == 0));
    }

    private static bool SameRouteRewardOffer(PredictorRun run, PredictorReward actual,
        PredictorPlannedRewardChoice choice, List<RouteDependency> dependencies)
    {
        var knowledge = ObserveRouteRewards(run, dependencies).ElementAtOrDefault(run.Rewards.IndexOf(actual));
        // A suspended parent's own retained observation may establish a mask.
        // Missing observations never imply known identity or enchantment scope.
        if (knowledge == null)
            foreach (var pair in run._preview.Rewards.SacrificeSourceRewards)
            {
                int index = pair.Value.FindIndex(reward => reward.Id == actual.Id);
                if (index < 0 || !run._preview.Rewards.SacrificeSourceKnowledge.TryGetValue(pair.Key, out var retained)) continue;
                knowledge = retained.ElementAtOrDefault(index);
                break;
            }
        return SameBoundRewardOffer(actual, choice, knowledge?.OfferBinding);
    }

    private static bool SameBoundRewardOffer(PredictorReward actual, PredictorPlannedRewardChoice choice,
        PredictorRewardOfferBinding? current)
    {
        var masked = choice.UnresolvedEnchantmentIndices.Concat(current?.UnresolvedEnchantmentIndices ?? [])
            .Distinct().ToHashSet();
        if (masked.Count == 0) return SameOffer(actual, choice.ExpectedReward);
        if (actual.Kind != PredictorRewardKind.Card || actual.Cards.Length != choice.ExpectedReward.Cards.Length ||
            masked.Any(index => index < 0 || index >= actual.Cards.Length)) return false;
        PredictorReward Mask(PredictorReward reward) => reward with
        {
            Cards = reward.Cards.Select((card, index) => masked.Contains(index)
                ? card with { Enchantment = null, EnchantmentAmount = 0 } : card).ToImmutableArray()
        };
        return SameOffer(Mask(actual), Mask(choice.ExpectedReward));
    }

    private static bool RouteRewardOperationKnown(PredictorRun run, PredictorCombatRewardKnowledge knowledge,
        PredictorPlannedRewardChoice choice) => knowledge.IsKnown || knowledge.OfferBinding != null &&
        (choice.Skip || choice.Reroll || choice.SacrificeRelicId != null ||
            choice.CardIndex is { } index && knowledge.CanChooseCard(index) &&
                (knowledge.CardOptions[index].IsKnown || run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults));

    private static ImmutableArray<PredictorCombatPendingCause> RewardOfferReadCauses(PredictorCombatRewardKnowledge knowledge) =>
        knowledge.OfferBinding != null ? [] : knowledge.PendingCauses;
}
