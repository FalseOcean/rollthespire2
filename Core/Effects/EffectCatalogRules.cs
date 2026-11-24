using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects;

/// <summary>
/// Pool semantics, independent of native catalog access and completeness evidence.
/// Runtime adaptation selects these rules; consumers supply the observed facts.
/// </summary>
public sealed record EffectCatalogRules(
    bool GeneratedCursePoolRequiresModifierFlag,
    bool OtherCharacterPoolsUseModelIdOrder)
{
    internal static EffectCatalogRules Modern { get; } = new(true, true);

    public bool? IsGeneratedCurseEligible(bool? canBeGeneratedByModifiers) =>
        GeneratedCursePoolRequiresModifierFlag ? canBeGeneratedByModifiers : true;

    public IReadOnlyList<ModelKey>? BuildGeneratedCursePool(
        IReadOnlyList<NeowEffectCardSnapshot>? cards, bool sourceExact, out bool exact)
    {
        exact = sourceExact;
        if (cards is null)
        {
            exact = false;
            return null;
        }

        var output = new List<ModelKey>();
        foreach (var card in cards.OrderBy(card => card.CardKey.Category, StringComparer.Ordinal)
                     .ThenBy(card => card.CardKey.Entry, StringComparer.Ordinal))
        {
            bool? eligible = IsGeneratedCurseEligible(card.CanBeGeneratedByModifiers);
            if (!eligible.HasValue) exact = false;
            else if (eligible.Value) output.Add(card.CardKey);
        }
        exact &= output.Count > 0;
        return output;
    }
}
