using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunOperations;

// Display policy lives here; producers know neither layouts nor UI preferences.
internal static class OperationForecastProjection
{
    internal static OperationPreview Project(OperationForecast facts) => new(facts.Source,
        facts.ContainedRelics, facts.CurseCandidates, facts.CursesPossible,
        facts.Branches.Select(branch => new OperationPreviewBranch(branch.Order,
            Project(branch.Sections, facts), branch.PendingReasons, branch.PathsExamined, branch.FullyEnumerated)
            { Faulted = branch.Faulted, Label = branch.Kind,
                HuntOutcomes = branch.HuntOutcomes.Select(outcome => new OperationPreviewHuntOutcome(outcome.Triggers,
                    Project(outcome.Sections, facts))).ToImmutableArray() }).ToImmutableArray(), facts.PendingReasons)
    {
        Faults = facts.Faults.Select(f => new OperationPreviewFault(f.Stage, f.InputPath, f.Exception)).ToImmutableArray(),
        OptionKey = facts.OptionKey, Notices = facts.Notices,
        Style = Style(facts)
    };

    internal static ImmutableArray<OperationPreviewSection> Project(ImmutableArray<OperationOutcomeSource> sources,
        OperationForecast? forecast = null) =>
        sources.Select(source => new OperationPreviewSection(source.Source,
            source.Rows.Select(row => new OperationPreviewRow(Action(row.Action),
                row.Models, row.Cards, row.Possible) { Label = row.Kind, Price = row.Price }).ToImmutableArray(),
            source.CardGroups.Select(group => new OperationPreviewCardGroup(group.Id, group.Cards,
                group.MinimumSelect, group.MaximumSelect, group.CanSkip, group.Possible)
            {
                Label = group.Kind,
                SequenceOrdinal = group.SequenceOrdinal,
                EarliestFirst = group.OrderedAlternatives || group.SequenceOrdinal.HasValue,
                SuppressPossiblePrefix = group.OrderedAlternatives || group.ProposedTarget && forecast?.TargetTransformation == true,
                ForceSingleRow = group.ProposedTarget && forecast?.TargetTransformation == true,
                Highlighted = group.ProposedTarget && int.TryParse(group.Kind, out int ordinal) &&
                    ordinal - 1 == forecast?.ProposedTargetOrdinal,
                IconPaths = group.IconPaths
            }).ToImmutableArray())
        {
            Category = source.Category switch
            {
                OperationOutcomeCategory.CardTransform => OperationPredictionCategory.CardTransform,
                OperationOutcomeCategory.ShopRestock => OperationPredictionCategory.ShopRestock,
                OperationOutcomeCategory.RewardReroll => OperationPredictionCategory.RewardReroll,
                OperationOutcomeCategory.RewardSacrifice => OperationPredictionCategory.RewardSacrifice,
                null => null,
                _ => throw new ArgumentOutOfRangeException(nameof(source.Category))
            },
            Label = source.Kind, FeatureId = source.FeatureId, EmptyReason = source.EmptyReason,
            Style = SectionStyle(source, forecast)
        }).ToImmutableArray();

    private static OperationDisplayStyle Cards(bool grouped = false) =>
        new(OperationTextMode.None, grouped ? OperationCardLayout.Groups : OperationCardLayout.Parallel);

    private static OperationPreviewAction Action(OperationOutcomeAction action) => action switch
    {
        OperationOutcomeAction.ObtainRelic => OperationPreviewAction.ObtainRelic,
        OperationOutcomeAction.AddCard => OperationPreviewAction.AddCard,
        OperationOutcomeAction.UpgradeCard => OperationPreviewAction.UpgradeCard,
        OperationOutcomeAction.TransformCard => OperationPreviewAction.TransformCard,
        OperationOutcomeAction.GeneratePotion => OperationPreviewAction.GeneratePotion,
        OperationOutcomeAction.EnchantCard => OperationPreviewAction.EnchantCard,
        OperationOutcomeAction.RemoveCard => OperationPreviewAction.RemoveCard,
        OperationOutcomeAction.DowngradeCard => OperationPreviewAction.DowngradeCard,
        OperationOutcomeAction.Restock => OperationPreviewAction.Restock,
        OperationOutcomeAction.Choose => OperationPreviewAction.Choose,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static OperationDisplayStyle? Style(OperationForecast facts) => facts.Activity switch
    {
        OperationForecastActivity.ShopPurchase when facts.PurchasedEntryKind is PredictorShopEntryKind.CharacterCard or PredictorShopEntryKind.ColorlessCard => Cards(),
        OperationForecastActivity.ShopPurchase when facts.PurchasedEntryKind is { } kind && kind != PredictorShopEntryKind.Relic => OperationDisplayPolicy.A1,
        OperationForecastActivity.RestAction => facts.RestAction == PredictorRestAction.Dig || facts.Source.Entry == "TINY_MAILBOX" ? OperationDisplayPolicy.A1 : Cards(),
        OperationForecastActivity.Reroll => Cards(facts.Branches.SelectMany(b => b.Sections).Sum(s => s.CardGroups.Length) > 1),
        OperationForecastActivity.Sacrifice => OperationDisplayPolicy.A1,
        OperationForecastActivity.ProposedTargets when facts.TargetTransformation => Cards(),
        OperationForecastActivity.PotionUse => facts.Source.Entry == "ENTROPIC_BREW" ? OperationDisplayPolicy.A1 :
            Cards(facts.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups).Any(g => g.IconPaths.Length > 1 || g.IconPaths.Any(path => path.Segments.Sum(segment => segment.Length) > 1))),
        _ => facts.Source.Entry == "ROOM_FULL_OF_CHEESE" && facts.OptionKey == "GORGE"
            ? new(OperationTextMode.None, OperationCardLayout.Spread) : null
    };

    private static OperationDisplayStyle? SectionStyle(OperationOutcomeSource source, OperationForecast? facts)
    {
        if (source.Category == OperationOutcomeCategory.ShopRestock)
            return source.Source.Entry == "THE_COURIER" && source.Rows.Any(r => !r.Cards.IsEmpty) ? Cards() : OperationDisplayPolicy.A1;
        if (source.Category == OperationOutcomeCategory.RewardReroll) return Cards(source.CardGroups.Length > 1);
        if (source.Category == OperationOutcomeCategory.RewardSacrifice) return OperationDisplayPolicy.A1;
        if (facts?.TargetTransformation == true && source.Category == OperationOutcomeCategory.CardTransform) return Cards();
        return source.FeatureId switch
        {
            "relic:DREAM_CATCHER" => Cards(),
            "relic:TINY_MAILBOX" => OperationDisplayPolicy.A1,
            _ => null
        };
    }
}
