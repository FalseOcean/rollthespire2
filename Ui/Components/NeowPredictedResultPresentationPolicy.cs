using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.Ui1;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Predictor-only presentation filter for Opening. It removes deterministic relic-
/// inherent facts from the ordinary Neow prediction surface while preserving the
/// immutable prediction model and all Advanced/diagnostic evidence. This is not a
/// Neow semantic or RNG authority.
/// </summary>
internal static class NeowPredictedResultPresentationPolicy
{
    private static readonly HashSet<ModelKey> IdentitySufficientRelics = new()
    {
        BaseGameModelKeys.Relics.CursedPearl,
        BaseGameModelKeys.Relics.DowsingRod,
        BaseGameModelKeys.Relics.GoldenPearl,
        BaseGameModelKeys.Relics.NeowsTorment,
        BaseGameModelKeys.Relics.NutritiousOyster,
        BaseGameModelKeys.Relics.NeowsSacrifice,
        BaseGameModelKeys.Relics.NeowsTalisman,
        BaseGameModelKeys.Relics.PreciseScissors,
        BaseGameModelKeys.Relics.PrecariousShears,
        BaseGameModelKeys.Relics.Pomander,
        BaseGameModelKeys.Relics.SilkenTress
    };

    public static IReadOnlyList<PredictedEffectGroupViewModel> Filter(
        ModelKey sourceRelicKey,
        IReadOnlyList<PredictedEffectGroupViewModel> groups)
    {
        if (IdentitySufficientRelics.Contains(sourceRelicKey))
        {
            return Array.Empty<PredictedEffectGroupViewModel>();
        }

        return groups
            .OrderBy(group => group.GroupOrder)
            .Where(group => !IsFixedInherentGroup(sourceRelicKey, group))
            .Select(group => group with
            {
                OrderedItems = group.OrderedItems
                    .Where(item => item.ShowInNormalMode)
                    .ToArray()
            })
            .Where(group => group.ShowInNormalMode && group.OrderedItems.Count > 0)
            .ToArray();
    }

    public static bool HasVisibleResults(NeowChoiceViewModel choice)
    {
        ArgumentNullException.ThrowIfNull(choice);

        // Bones has a dedicated detail renderer, including its fail-soft route
        // state, so the middle column remains visible for the composite result.
        if (choice.BonesOutcome is not null ||
            choice.PredictionState == NeowPredictionPresentationState.PredictionUnavailable)
        {
            return true;
        }

        return Filter(choice.RelicKey, choice.EffectGroups).Count > 0;
    }

    private static bool IsFixedInherentGroup(
        ModelKey sourceRelicKey,
        PredictedEffectGroupViewModel group)
    {
        if (group.CompactSummaryKind is EffectCompactSummaryKind.FixedStarterPair or
            EffectCompactSummaryKind.PlayerChoiceDeckMutation)
        {
            return true;
        }

        // Hefty Tablet contains both a seed-dependent rare-card offer and a fixed
        // Injury obtain. Only the latter is inherent to the relic identity.
        bool heftyTabletSource = sourceRelicKey == BaseGameModelKeys.Relics.HeftyTablet ||
                                 group.SourceRelicKey == BaseGameModelKeys.Relics.HeftyTablet;
        return heftyTabletSource && IsCanonicalOrScopedGroup(group.GroupId, "hefty-tablet-fixed");
    }

    private static bool IsCanonicalOrScopedGroup(string groupId, string canonicalGroupId)
    {
        if (string.Equals(groupId, canonicalGroupId, StringComparison.Ordinal))
        {
            return true;
        }

        // Bones keeps the source effect semantics but namespaces the concrete scoped
        // GroupId as <routeId>.<sourceGroupId>. Match that provenance boundary rather
        // than hiding all Injury cards, so a genuinely predicted Final Curse Injury
        // remains visible.
        return groupId.EndsWith("." + canonicalGroupId, StringComparison.Ordinal);
    }
}
