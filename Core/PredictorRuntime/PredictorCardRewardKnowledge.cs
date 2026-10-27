using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// Recorded at the actual listener boundary, before Wing Charm changes a card.
// Later supported listeners may fill other eligible cards' enchantments or
// upgrade cards, but neither operation makes Wing's selected target change the
// identity/level. They do not remove enchantments. Keep the uncertain target's
// whole enchantment field masked through those listeners.
internal sealed record PredictorCardRewardNicheScope(ImmutableArray<int> EligibleIndices,
    ImmutableArray<int> DependentEnchantmentIndices)
{
    internal static PredictorCardRewardNicheScope? From(IEnumerable<ImmutableArray<int>> trace,
        PredictorCardRewardNicheScope? previous = null)
    {
        var calls = trace.ToArray();
        if (calls.Length == 0) return previous;
        return new((previous?.EligibleIndices ?? []).Concat(calls.SelectMany(indices => indices))
                .Distinct().Order().ToImmutableArray(),
            (previous?.DependentEnchantmentIndices ?? []).Concat(calls.Where(indices => indices.Length > 1)
                .SelectMany(indices => indices)).Distinct().Order().ToImmutableArray());
    }
}

// Display-only marginal facts. An unknown enchantment is explicitly cleared
// from Value; that card must not become an actionable full reward snapshot.
internal sealed record PredictorCardRewardOptionKnowledge(PredictorCard Value, bool? WingCharmEligible,
    ImmutableArray<PredictorCombatPendingCause> EnchantmentPendingCauses)
{
    internal bool IsKnown => EnchantmentPendingCauses.IsEmpty;
}

internal sealed partial class PredictorRun
{
    internal static PredictorCombatRewardKnowledge CardRewardKnowledge(PredictorReward reward,
        ImmutableArray<PredictorCombatPendingCause> identityCauses,
        ImmutableArray<PredictorCombatPendingCause> nicheCauses)
    {
        identityCauses = PredictorRouteCauseEvidence.Normalize(identityCauses);
        if (!identityCauses.IsEmpty)
            return new(reward.Kind, null, identityCauses);
        var scope = reward.CardRewardNicheScope;
        var pending = scope?.DependentEnchantmentIndices.IsEmpty == false
            ? PredictorRouteCauseEvidence.Normalize(nicheCauses) : [];
        var options = reward.Cards.Select((card, index) =>
        {
            var causes = !pending.IsEmpty && scope!.DependentEnchantmentIndices.Contains(index) ? pending : [];
            return new PredictorCardRewardOptionKnowledge(card with
            {
                Id = 0,
                Enchantment = causes.IsEmpty ? card.Enchantment : null,
                EnchantmentAmount = causes.IsEmpty ? card.EnchantmentAmount : 0
            }, scope == null ? null : scope.EligibleIndices.Contains(index), causes);
        }).ToImmutableArray();
        return new(reward.Kind, pending.IsEmpty ? DisplayReward(reward) : null, pending) { CardOptions = options };
    }
}
