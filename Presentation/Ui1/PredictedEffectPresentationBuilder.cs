using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Presentation.Ui1;

internal static class PredictedEffectPresentationBuilder
{
    public static IReadOnlyList<PredictedEffectGroupViewModel> BuildGroups(
        IReadOnlyList<PredictedEffectGroup> groups,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(uiText);
        ArgumentNullException.ThrowIfNull(contentNames);

        PredictedEffectGroup[] ordered = groups.OrderBy(group => group.GroupOrder).ToArray();
        return ordered
            .Select((group, index) => BuildGroup(group, index, ordered.Length, uiText, contentNames, showInternalIds))
            .ToArray();
    }

    public static string BuildPlainText(
        PredictedEffect effect,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds) =>
        BuildItem(effect, null, uiText, contentNames, showInternalIds).DisplayText;

    private static PredictedEffectGroupViewModel BuildGroup(
        PredictedEffectGroup group,
        int index,
        int totalGroupCount,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        PredictedEffectItemViewModel[] items = group.OrderedItems
            .OrderBy(item => item.ItemOrder)
            .Select(item => BuildItem(item, group, uiText, contentNames, showInternalIds))
            .ToArray();
        PredictionPrecision precision = items.Length == 0
            ? PredictionPrecision.Unsupported
            : AnalysisPrecisionAggregator.AggregateEffects(group.OrderedItems);
        bool showInNormalMode = group.IsProductRelevant &&
                                !group.IsPlayerChoiceGroup &&
                                group.NormalViewDetailLevel != EffectPresentationDetailLevel.Hidden &&
                                (group.NormalViewDetailLevel == EffectPresentationDetailLevel.CompactSummary ||
                                 items.Any(item => item.ShowInNormalMode));

        return new PredictedEffectGroupViewModel(
            group.GroupId,
            group.GroupOrder,
            BuildGroupDisplayLabel(group, index, totalGroupCount, uiText),
            group.SelectionPolicy,
            uiText.Get(Ui1TextKey.EffectSelectionPolicyTextKey(group.SelectionPolicy)),
            group.Scope,
            group.RequiredSelectionCount,
            group.SelectionSetId,
            group.BundleId,
            group.BundleOrder,
            items,
            precision,
            uiText.Get(UiTextKey.Precision(precision)),
            group.SourceRelicKey,
            group.Phase,
            showInNormalMode,
            group.IsProductRelevant,
            group.IsPlayerChoiceGroup,
            group.NormalViewDetailLevel,
            group.AdvancedViewDetailLevel,
            group.DiagnosticViewDetailLevel,
            group.CompactSummaryKind,
            BuildCompactSummaryText(group.CompactSummaryKind, uiText));
    }

    private static PredictedEffectItemViewModel BuildItem(
        PredictedEffect effect,
        PredictedEffectGroup? group,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        GameContentKind? contentKind = TryGetContentKind(effect.TargetKey);
        string? targetName = effect.TargetKey.HasValue && contentKind.HasValue
            ? GameContentDisplayPresentationBuilder.Build(
                effect.TargetKey.Value,
                contentKind.Value,
                contentNames,
                showInternalIds).DisplayName
            : null;

        string multiplicity = effect.Multiplicity > 1
            ? uiText.Format(Ui1TextKey.EffectMultiplicity, effect.Multiplicity)
            : string.Empty;
        string signedAmount = effect.Amount.HasValue
            ? effect.Amount.Value.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        GameContentKind? sourceKind = TryGetContentKind(effect.SourceKey);
        string sourceName = effect.SourceKey.HasValue && sourceKind.HasValue
            ? GameContentDisplayPresentationBuilder.Build(
                effect.SourceKey.Value,
                sourceKind.Value,
                contentNames,
                showInternalIds).DisplayName
            : string.Empty;

        string displayText = effect.Kind switch
        {
            PredictedEffectKind.AddCard when targetName is not null => uiText.Format(Ui1TextKey.EffectAddCard, targetName, multiplicity),
            PredictedEffectKind.RemoveCard when targetName is not null => uiText.Format(Ui1TextKey.EffectRemoveCard, targetName, multiplicity),
            PredictedEffectKind.UpgradeCard when targetName is not null => uiText.Format(Ui1TextKey.EffectUpgradeCard, targetName, multiplicity),
            PredictedEffectKind.TransformCard when targetName is not null => uiText.Format(Ui1TextKey.EffectTransformCard, targetName, multiplicity),
            PredictedEffectKind.AddRelic when targetName is not null => uiText.Format(Ui1TextKey.EffectAddRelic, targetName, multiplicity),
            PredictedEffectKind.AddPotion when targetName is not null => uiText.Format(Ui1TextKey.EffectAddPotion, targetName, multiplicity),
            PredictedEffectKind.AttemptAddPotion when targetName is not null => uiText.Format(Ui1TextKey.EffectAttemptAddPotion, targetName, multiplicity),
            PredictedEffectKind.ChangeGold => uiText.Format(Ui1TextKey.EffectChangeGold, signedAmount),
            PredictedEffectKind.SetGold => uiText.Format(Ui1TextKey.EffectSetGold, effect.Amount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
            PredictedEffectKind.ChangeMaxHp => uiText.Format(Ui1TextKey.EffectChangeMaxHp, signedAmount),
            PredictedEffectKind.ChangeCurrentHp => uiText.Format(Ui1TextKey.EffectChangeCurrentHp, signedAmount),
            PredictedEffectKind.ChangePotionCapacity => uiText.Format(Ui1TextKey.EffectChangePotionCapacity, signedAmount),
            PredictedEffectKind.FutureRuntimeEffect when targetName is not null => uiText.Format(Ui1TextKey.EffectFutureRuntime, targetName),
            PredictedEffectKind.AutomaticEffect when effect.CompactSummaryKind == EffectCompactSummaryKind.Applied =>
                uiText.Get(Ui1TextKey.EffectCompactApplied),
            PredictedEffectKind.AutomaticEffect => uiText.Get(Ui1TextKey.EffectAutomatic),
            PredictedEffectKind.DescriptionOnly when effect.WarningCodes.Contains(PredictionWarningCode.ComplexResultNotEvaluatedByPolicy) =>
                uiText.Get(Ui1TextKey.ComplexNotEvaluatedByPolicy),
            PredictedEffectKind.DescriptionOnly => uiText.Get(Ui1TextKey.EffectDescriptionOnly),
            _ => uiText.Get(Ui1TextKey.EffectUnavailable)
        };
        if (!string.IsNullOrWhiteSpace(sourceName) && effect.Kind == PredictedEffectKind.TransformCard)
        {
            displayText = sourceName + " → " + displayText;
        }

        string glamSuffix = effect.HasGlam
            ? uiText.Get(Ui1TextKey.NormalCombatRewardCardGlamSuffix)
            : string.Empty;
        displayText += glamSuffix;
        string compact = targetName is null
            ? displayText
            : effect.Kind == PredictedEffectKind.TransformCard && !string.IsNullOrWhiteSpace(sourceName)
                ? $"{sourceName} → {targetName}{multiplicity}"
                : targetName + multiplicity + glamSuffix;
        GameContentDisplayViewModel? targetContent = effect.TargetKey.HasValue && contentKind.HasValue
            ? GameContentDisplayPresentationBuilder.Build(
                effect.TargetKey.Value,
                contentKind.Value,
                contentNames,
                showInternalIds,
                tooltipSummary: displayText,
                diagnosticLines: new[] { effect.EvidenceCode.ToString() })
            : null;
        string tooltip = targetContent?.Tooltip ?? displayText;
        bool showInNormalMode = ShouldShowItemInNormalMode(effect, group);

        return new PredictedEffectItemViewModel(
            effect.ItemOrder,
            effect.Kind,
            targetContent,
            displayText,
            compact,
            tooltip,
            effect.Multiplicity,
            effect.Amount,
            effect.Precision,
            uiText.Get(UiTextKey.Precision(effect.Precision)),
            effect.SourceKey,
            effect.OfferItemId,
            showInNormalMode,
            effect.EffectNodeId,
            effect.ParentEffectNodeId,
            effect.Relation,
            effect.IsProductRelevant,
            effect.IsPlayerChoiceEffect,
            effect.NormalViewDetailLevel,
            effect.AdvancedViewDetailLevel,
            effect.DiagnosticViewDetailLevel,
            effect.CompactSummaryKind);
    }

    private static string BuildGroupDisplayLabel(
        PredictedEffectGroup group,
        int index,
        int totalGroupCount,
        IUiTextProvider uiText)
    {
        if (group.Phase == PredictedEffectPhase.SharedContinuation)
        {
            return uiText.Get(Ui1TextKey.EffectContinuation);
        }
        if (group.IsPlayerChoiceGroup)
        {
            return uiText.Get(Ui1TextKey.PlayerChoiceRoute);
        }
        if (totalGroupCount > 1 || !string.IsNullOrWhiteSpace(group.SelectionSetId) || !string.IsNullOrWhiteSpace(group.BundleId))
        {
            return uiText.Format(Ui1TextKey.EffectGroupNumber, index + 1);
        }
        return string.Empty;
    }

    private static bool ShouldShowItemInNormalMode(PredictedEffect effect, PredictedEffectGroup? group)
    {
        if (!effect.IsProductRelevant || effect.IsPlayerChoiceEffect || group?.IsPlayerChoiceGroup == true ||
            effect.NormalViewDetailLevel == EffectPresentationDetailLevel.Hidden ||
            group?.NormalViewDetailLevel == EffectPresentationDetailLevel.Hidden)
        {
            return false;
        }
        if (effect.NormalViewDetailLevel == EffectPresentationDetailLevel.CompactSummary)
        {
            return true;
        }
        if (effect.Kind is PredictedEffectKind.ChangeGold or
            PredictedEffectKind.SetGold or
            PredictedEffectKind.ChangeMaxHp or
            PredictedEffectKind.ChangeCurrentHp or
            PredictedEffectKind.ChangePotionCapacity or
            PredictedEffectKind.FutureRuntimeEffect)
        {
            return false;
        }
        if (effect.Kind == PredictedEffectKind.DescriptionOnly &&
            !effect.WarningCodes.Contains(PredictionWarningCode.ComplexResultNotEvaluatedByPolicy))
        {
            return false;
        }
        if (effect.Phase == PredictedEffectPhase.RelicAcquisition &&
            effect.TargetKey == effect.SourceRelicKey)
        {
            return false;
        }
        return effect.Kind is PredictedEffectKind.AddCard or
            PredictedEffectKind.RemoveCard or
            PredictedEffectKind.UpgradeCard or
            PredictedEffectKind.TransformCard or
            PredictedEffectKind.AddRelic or
            PredictedEffectKind.AddPotion or
            PredictedEffectKind.AttemptAddPotion or
            PredictedEffectKind.AutomaticEffect or
            PredictedEffectKind.DescriptionOnly;
    }

    private static string BuildCompactSummaryText(
        EffectCompactSummaryKind kind,
        IUiTextProvider uiText) => kind switch
        {
            EffectCompactSummaryKind.Applied => uiText.Get(Ui1TextKey.EffectCompactApplied),
            EffectCompactSummaryKind.FixedStarterPair => uiText.Get(Ui1TextKey.EffectCompactFixedStarterPair),
            EffectCompactSummaryKind.PlayerChoiceDeckMutation => uiText.Get(Ui1TextKey.EffectCompactPlayerChoiceDeckMutation),
            _ => string.Empty
        };

    private static GameContentKind? TryGetContentKind(ModelKey? key)
    {
        if (!key.HasValue)
        {
            return null;
        }
        return key.Value.Category switch
        {
            "CARD" => GameContentKind.Card,
            "RELIC" => GameContentKind.Relic,
            "POTION" => GameContentKind.Potion,
            "CHARACTER" => GameContentKind.Character,
            "EVENT" => GameContentKind.Event,
            "ENCOUNTER" => GameContentKind.Encounter,
            "ANCIENT" => GameContentKind.Ancient,
            _ => null
        };
    }
}
