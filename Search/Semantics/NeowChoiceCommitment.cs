using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

// Product interpretation of existing optional-offer operators. Mandatory adds,
// transforms and bundle choices deliberately do not participate in Skip policy.
internal static class NeowChoiceCommitment
{
    internal const string PolicyId = "OptionalCardCommitment.AuthoredConjunctionTakeOtherwiseSkip.20260923.v2";
    internal static bool IsOptionalCardOffer(ModelKey source) =>
        source == BaseGameModelKeys.Relics.Kaleidoscope || source == BaseGameModelKeys.Relics.LeadPaperweight ||
        source == BaseGameModelKeys.Relics.HeftyTablet || source == BaseGameModelKeys.Relics.LostCoffer ||
        source == BaseGameModelKeys.Relics.MassiveScroll;

    internal static ModelKey[] Cards(IEnumerable<NeowStructuredEffectSearchCondition> conditions, ModelKey source) =>
        conditions.Where(c => !c.IsEmpty && c.SourceRelicKey == source && c.OutputKind == NeowStructuredOutputKind.Card)
            .SelectMany(c => (c.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder && c.KaleidoscopePositionalSlots.Count == 2
                ? c.KaleidoscopePositionalSlots.Where(k => k.HasValue).Select(k => k!.Value) : c.OutputKeys)
                .GroupBy(k => k).Select(g => (Key: g.Key, Count: g.Count())))
            .GroupBy(g => g.Key).OrderBy(g => g.Key.Serialized, StringComparer.Ordinal)
            .SelectMany(g => Enumerable.Repeat(g.Key, g.Max(item => item.Count))).ToArray();

    internal static bool HasCompatibleRequirements(IEnumerable<NeowStructuredEffectSearchCondition> conditions, ModelKey source)
    {
        if (!IsOptionalCardOffer(source) && source != BaseGameModelKeys.Relics.ScrollBoxes) return true;
        var rows = conditions.Where(c => !c.IsEmpty && c.SourceRelicKey == source && c.OutputKind == NeowStructuredOutputKind.Card).ToArray();
        ModelKey[] desired = Cards(rows, source);
        if (source == BaseGameModelKeys.Relics.ScrollBoxes)
            return desired.Length <= 3 && !(rows.Any(c => c.Kind == NeowStructuredConditionKind.SpecialOffer) &&
                rows.Any(c => c.Kind == NeowStructuredConditionKind.StructuredCardComposition));
        if (desired.Length > (source == BaseGameModelKeys.Relics.Kaleidoscope ? 2 : 1)) return false;
        var ordered = rows.Where(c => c.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder).ToArray();
        if (ordered.Length == 0) return true;
        var slots = ordered[0].KaleidoscopePositionalSlots;
        return slots.Count == 2 && ordered.All(c => c.KaleidoscopePositionalSlots.SequenceEqual(slots)) &&
            slots.Where(k => k.HasValue).Select(k => k!.Value).OrderBy(k => k.Serialized, StringComparer.Ordinal).SequenceEqual(desired);
    }

    internal static IEnumerable<NeowStructuredEffectSearchCondition> CombinedConditions(
        IEnumerable<NeowStructuredEffectSearchCondition> conditions)
    {
        var rows = conditions.Where(c => c.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope &&
            c.Kind == NeowStructuredConditionKind.IndependentOfferGroupTargets).ToArray();
        if (rows.Length >= 2)
        {
            var representative = rows.FirstOrDefault(c => c.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder) ?? rows[0];
            yield return representative with { OutputKeys = Cards(rows, representative.SourceRelicKey) };
        }
        var scroll = conditions.Where(c => c.SourceRelicKey == BaseGameModelKeys.Relics.ScrollBoxes &&
            c.Kind == NeowStructuredConditionKind.StructuredCardComposition).ToArray();
        if (scroll.Length >= 2) yield return scroll[0] with { OutputKeys = Cards(scroll, scroll[0].SourceRelicKey) };
    }

    internal static bool Matches(IEnumerable<NeowStructuredEffectSearchCondition> conditions, ModelKey source,
        PlayerChoicePolicyDescriptor policy, IReadOnlyList<PredictedEffect> effects)
    {
        if (!IsOptionalCardOffer(source)) return true;
        var rows = conditions.ToArray();
        if (!HasCompatibleRequirements(rows, source)) return false;
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
