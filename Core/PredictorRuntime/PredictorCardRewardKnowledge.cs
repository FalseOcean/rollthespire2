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
    public ImmutableArray<ImmutableArray<PredictorCardEnchantment>> EnchantmentAlternatives { get; init; } = [];
    public ImmutableArray<int> UnresolvedCardInstanceIndices { get; init; } = [];
    internal static PredictorCardRewardNicheScope? From(IEnumerable<ImmutableArray<int>> trace,
        PredictorCardRewardNicheScope? previous = null,
        List<ImmutableArray<PredictorCardEnchantment>>? alternatives = null)
    {
        var calls = trace.ToArray();
        if (calls.Length == 0 && previous == null) return null;
        return new((previous?.EligibleIndices ?? []).Concat(calls.SelectMany(indices => indices))
                .Distinct().Order().ToImmutableArray(),
            (previous?.DependentEnchantmentIndices ?? []).Concat(calls.Where(indices => indices.Length > 1)
                .SelectMany(indices => indices)).Distinct().Order().ToImmutableArray())
        {
            EnchantmentAlternatives = alternatives?.ToImmutableArray() ?? previous?.EnchantmentAlternatives ?? [],
            UnresolvedCardInstanceIndices = previous?.UnresolvedCardInstanceIndices ?? []
        };
    }

    internal static List<ImmutableArray<PredictorCardEnchantment>> BeginAlternatives(
        IReadOnlyList<PredictorCard> cards, PredictorCardRewardNicheScope? previous = null) =>
        cards.Select((card, index) => previous != null && index < previous.EnchantmentAlternatives.Length
            ? previous.EnchantmentAlternatives[index] : ImmutableArray.Create(PredictorCardEnchantment.From(card))).ToList();

    internal static (ImmutableArray<int> Indices, bool AllocationCountVaries) LateInstanceUncertainty(
        PredictorContext context, IReadOnlyList<PredictorCard> cards, string listener,
        IReadOnlyList<ImmutableArray<PredictorCardEnchantment>> alternatives, bool silkenAvailable)
    {
        bool Changes(int index, PredictorCardEnchantment value)
        {
            var card = value.Apply(cards[index]);
            bool enchantable = PredictorObtainSources.CanEnchant(context, card);
            return listener switch
            {
                "WING_CHARM" or "GLITTER" => enchantable,
                "FRESNEL_LENS" => enchantable && PredictorObtainSources.GainsBlockForFresnel(context, card),
                "SILKEN_TRESS" => silkenAvailable && enchantable,
                _ => false
            };
        }
        var canChange = Enumerable.Range(0, cards.Count).Select(index =>
            alternatives[index].Select(value => Changes(index, value)).Distinct().ToArray()).ToArray();
        bool wing = listener == "WING_CHARM";
        int possibleTargets = canChange.Count(values => values.Contains(true));
        var indices = Enumerable.Range(0, cards.Count).Where(index =>
            canChange[index].Length > 1 || wing && possibleTargets > 1 && canChange[index].Contains(true)).ToImmutableArray();
        return (indices, canChange.Any(values => values.Length > 1));
    }

    // Marginal payloads of this one offered card, before actual listeners run.
    // No RNG is drawn, no instances are allocated and no state is substituted.
    internal static void AdvanceAlternatives(PredictorContext context,
        IReadOnlyList<PredictorCard> cards, string listener,
        List<ImmutableArray<PredictorCardEnchantment>> alternatives, bool silkenAvailable = false)
    {
        bool CanEnchant(int index, PredictorCardEnchantment enchantment) =>
            PredictorObtainSources.CanEnchant(context, enchantment.Apply(cards[index]));
        var eligible = listener == "WING_CHARM" ? Enumerable.Range(0, cards.Count)
            .Where(index => alternatives[index].Any(value => CanEnchant(index, value))).ToArray() : [];
        for (int index = 0; index < cards.Count; index++)
        {
            var next = ImmutableArray.CreateBuilder<PredictorCardEnchantment>();
            foreach (var value in alternatives[index])
            {
                if (listener == "WING_CHARM" && CanEnchant(index, value))
                {
                    next.Add(new(new("ENCHANTMENT", "SWIFT"), 1));
                    if (eligible.Length > 1) next.Add(value);
                }
                else if (listener == "FRESNEL_LENS" && CanEnchant(index, value) &&
                    PredictorObtainSources.GainsBlockForFresnel(context, cards[index]))
                    next.Add(new(new("ENCHANTMENT", "NIMBLE"), 2));
                else if (listener == "GLITTER" && CanEnchant(index, value) ||
                    listener == "SILKEN_TRESS" && silkenAvailable && value.Key == null &&
                    cards[index].Type is RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack or
                        RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill or
                        RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Power)
                    next.Add(new(new("ENCHANTMENT", "GLAM"), 1));
                else next.Add(value);
            }
            alternatives[index] = next.Distinct().ToImmutableArray();
        }
    }
}

internal sealed record PredictorCardEnchantment(RolltheSpire2.Core.Identity.ModelKey? Key, int Amount)
{
    internal static PredictorCardEnchantment From(PredictorCard card) => new(card.Enchantment, card.EnchantmentAmount);
    internal PredictorCard Apply(PredictorCard card) => card with { Enchantment = Key, EnchantmentAmount = Amount };
}

// The binding proves an opportunity's metadata and every unmasked card field.
// It is deliberately distinct from an exact reward payload.
internal sealed record PredictorRewardOfferBinding(PredictorReward Value,
    ImmutableArray<int> UnresolvedEnchantmentIndices);

// An unknown enchantment is cleared from Value and retained in the explicit
// mask. Identity-based actions can use the binding without claiming payload truth.
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
        var masked = DisplayReward(reward) with { Cards = options.Select(option => option.Value).ToImmutableArray() };
        return new(reward.Kind, pending.IsEmpty ? DisplayReward(reward) : null, pending)
        {
            CardOptions = options,
            PartialOfferBinding = new(masked, options.Select((option, index) => (option, index))
                .Where(pair => !pair.option.IsKnown).Select(pair => pair.index).ToImmutableArray())
        };
    }
}
