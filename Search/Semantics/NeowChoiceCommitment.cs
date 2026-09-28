using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

// Product interpretation of existing optional-offer operators. Mandatory adds,
// transforms and bundle choices deliberately do not participate in Skip policy.
internal static class NeowChoiceCommitment
{
    internal const string PolicyId = "OptionalCardCommitment.AuthoredTakeOtherwiseSkip.20260911";
    internal static bool IsOptionalCardOffer(ModelKey source) =>
        source == BaseGameModelKeys.Relics.Kaleidoscope || source == BaseGameModelKeys.Relics.LeadPaperweight ||
        source == BaseGameModelKeys.Relics.HeftyTablet || source == BaseGameModelKeys.Relics.LostCoffer;

    internal static ModelKey[] Cards(IEnumerable<NeowStructuredEffectSearchCondition> conditions, ModelKey source) =>
        conditions.Where(c => !c.IsEmpty && c.SourceRelicKey == source && c.OutputKind == NeowStructuredOutputKind.Card)
            .SelectMany(c => c.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder && c.KaleidoscopePositionalSlots.Count == 2
                ? c.KaleidoscopePositionalSlots.Where(k => k.HasValue).Select(k => k!.Value) : c.OutputKeys).ToArray();

    internal static bool Matches(IEnumerable<NeowStructuredEffectSearchCondition> conditions, ModelKey source,
        PlayerChoicePolicyDescriptor policy, IReadOnlyList<PredictedEffect> effects)
    {
        if (!IsOptionalCardOffer(source)) return true;
        var rows = conditions.ToArray();
        ModelKey[] desired = Cards(rows, source);
        if (!desired.OrderBy(k => k.Serialized, StringComparer.Ordinal).SequenceEqual(
                policy.SourceCardKeys.OrderBy(k => k.Serialized, StringComparer.Ordinal))) return false;
        foreach (var row in rows.Where(c => c.SourceRelicKey == source &&
                     c.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder && c.KaleidoscopePositionalSlots.Count == 2))
        {
            for (int i = 0; i < 2; i++)
            {
                var picked = effects.Where(e => e.Kind == PredictedEffectKind.AddCard &&
                    e.OfferItemId?.StartsWith($"kaleidoscope.{i}.", StringComparison.Ordinal) == true).ToArray();
                if (row.KaleidoscopePositionalSlots[i] is ModelKey key
                    ? picked.Length != 1 || picked[0].TargetKey != key : picked.Length != 0) return false;
            }
        }
        return true;
    }
}
