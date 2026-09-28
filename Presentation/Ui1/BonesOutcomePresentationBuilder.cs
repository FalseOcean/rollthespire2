using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Presentation.Ui1;

internal static class BonesOutcomePresentationBuilder
{
    public static BonesOutcomeViewModel? Build(
        BonesOutcomeAnalysis? outcome,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        if (outcome is null)
        {
            return null;
        }

        var impactedOrders = new HashSet<string>(
            outcome.PlayerChoiceImpact?.RouteSets
                .Select(routeSet => FormatAcquisitionOrder(routeSet.AcquisitionOrder))
                ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        BonesOutcomeGroupViewModel[] groups = outcome.OutcomeGroups
            .Select(group => BuildGroup(
                group,
                impactedOrders.Contains(FormatAcquisitionOrder(group.RepresentativeRoute.AcquisitionOrder)),
                uiText,
                contentNames,
                showInternalIds))
            .ToArray();
        string[] comparisonEvidence = outcome.EffectiveOrderComparisonEvidence
            .Select(code => uiText.Get(Ui1TextKey.BonesDifferenceTextKey(code)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new BonesOutcomeViewModel(
            outcome.OverallImpact,
            ImpactLabel(outcome.OverallImpact, uiText),
            outcome.OrderComparisonStatus,
            uiText.Get(Ui1TextKey.BonesOrderComparisonTextKey(outcome.OrderComparisonStatus)),
            comparisonEvidence,
            uiText.Format(Ui1TextKey.BonesResultsCount, groups.Length),
            groups,
            BuildPlayerChoiceImpact(outcome.PlayerChoiceImpact, uiText, contentNames, showInternalIds));
    }

    private static BonesOutcomeGroupViewModel BuildGroup(
        BonesOutcomeGroup group,
        bool hasPlayerChoiceImpact,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        BonesAcquisitionRouteResult route = group.RepresentativeRoute;
        IReadOnlyList<BonesRelicScopedResultViewModel> relics = route.RelicScopedResults
            .OrderBy(result => IndexOf(route.AcquisitionOrder, result.SourceRelicKey))
            .Select(result => new BonesRelicScopedResultViewModel(
                GameContentDisplayPresentationBuilder.BuildRelic(
                    result.SourceRelicKey,
                    contentNames,
                    showInternalIds),
                result.ProductRelevantProjectionPrecision,
                uiText.Get(UiTextKey.Precision(result.ProductRelevantProjectionPrecision)),
                result.FullEffectSemanticsCompleteness,
                PredictedEffectPresentationBuilder.BuildGroups(
                    BuildObjectiveRelicGroups(route, result, hasPlayerChoiceImpact),
                    uiText,
                    contentNames,
                    showInternalIds)))
            .ToArray();

        string[] orderLabels = group.EquivalentAcquisitionOrders
            .Select(order => string.Join(" → ", order.Select(key =>
                VisibleName(key, GameContentKind.Relic, contentNames, showInternalIds))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string title = group.Impact == AcquisitionOrderImpact.ProvenIndependent && route.AcquisitionOrder.Count == 2
            ? string.Join(" ↔ ", route.AcquisitionOrder.Select(key =>
                VisibleName(key, GameContentKind.Relic, contentNames, showInternalIds)))
            : string.Join(" → ", route.AcquisitionOrder.Select(key =>
                VisibleName(key, GameContentKind.Relic, contentNames, showInternalIds)));
        string[] differenceLabels = group.DifferenceEvidence
            .Select(code => uiText.Get(Ui1TextKey.BonesDifferenceTextKey(code)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        RouteProjectionStatus routeStatus = EffectiveRouteStatus(route);
        string[] approvedRouteWarnings = route.WarningCodes
            .Where(PlayerWarningPresentationPolicy.IsPlayerFacing)
            .Select(code => uiText.Get(UiTextKey.Warning(code)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] routeWarnings = routeStatus is RouteProjectionStatus.Exact or
            RouteProjectionStatus.NotEvaluatedByPolicy
            ? approvedRouteWarnings
            : new[] { uiText.Get(Ui1TextKey.AnalysisPredictionUnavailable) };

        return new BonesOutcomeGroupViewModel(
            group.GroupId,
            route.RouteId,
            group.OriginalRoutes
                .OrderBy(item => item.OpeningRewardContinuation?.Route.RouteOrder ?? int.MaxValue)
                .ThenBy(item => item.RouteId, StringComparer.Ordinal)
                .Select(item => item.RouteId)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            group.Impact,
            ImpactLabel(group.Impact, uiText),
            routeStatus,
            uiText.Get(Ui1TextKey.BonesRouteProjectionTextKey(routeStatus)),
            route.ProductRelevantProjectionPrecision,
            route.SharedContinuation.FinalCursePrecision,
            route.SharedContinuation.FinalCurseKey.HasValue
                ? GameContentDisplayPresentationBuilder.BuildCard(
                    route.SharedContinuation.FinalCurseKey.Value,
                    contentNames,
                    showInternalIds)
                : null,
            routeWarnings,
            title,
            orderLabels,
            relics,
            PredictedEffectPresentationBuilder.BuildGroups(
                hasPlayerChoiceImpact
                    ? Array.Empty<PredictedEffectGroup>()
                    : route.SharedContinuation.EffectGroups,
                uiText,
                contentNames,
                showInternalIds),
            uiText.Get(Ui1TextKey.BonesSharedContinuation),
            differenceLabels,
            group.OriginalRoutes.Count);
    }

    private static PlayerChoiceImpactViewModel? BuildPlayerChoiceImpact(
        PlayerChoiceImpactAnalysis? impact,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        if (impact is null || impact.RouteSets.Count == 0)
        {
            return null;
        }

        PlayerChoiceRouteSetViewModel[] routeSets = impact.RouteSets.Select(routeSet =>
        {
            string order = string.Join(" → ", routeSet.AcquisitionOrder.Select(key =>
                VisibleName(key, GameContentKind.Relic, contentNames, showInternalIds)));
            GameContentDisplayViewModel choiceRelic = GameContentDisplayPresentationBuilder.BuildRelic(
                routeSet.ChoiceRelicKey,
                contentNames,
                showInternalIds);
            PlayerChoiceOutcomeViewModel[] outcomes = routeSet.DistinctOutcomes.Select(outcome =>
            {
                string precisionLabel = uiText.Get(UiTextKey.Precision(outcome.FinalCursePrecision));
                PlayerChoiceFinalCurseViewModel? finalCurse = null;
                if (outcome.FinalCurseKey.HasValue)
                {
                    GameContentDisplayViewModel curseContent = GameContentDisplayPresentationBuilder.BuildCard(
                        outcome.FinalCurseKey.Value,
                        contentNames,
                        showInternalIds,
                        diagnosticLines: outcome.EvidenceCodes.Select(code => code.ToString()));
                    finalCurse = new PlayerChoiceFinalCurseViewModel(
                        curseContent,
                        outcome.FinalCursePrecision,
                        precisionLabel,
                        uiText.Format(Ui1TextKey.PlayerChoiceFinalCurseHeader, precisionLabel),
                        uiText.Format(Ui1TextKey.PlayerChoiceFinalCurse, curseContent.DisplayName, precisionLabel));
                }

                return new PlayerChoiceOutcomeViewModel(
                    outcome.OutcomeId,
                    PolicyLabel(outcome.ChoicePolicy, uiText),
                    choiceRelic,
                    PredictedEffectPresentationBuilder.BuildGroups(
                        outcome.ChoiceEffectGroups, uiText, contentNames, showInternalIds),
                    PredictedEffectPresentationBuilder.BuildGroups(
                        outcome.AutomaticEffectGroups, uiText, contentNames, showInternalIds),
                    finalCurse,
                    outcome.FinalCursePrecision,
                    precisionLabel,
                    uiText.Format(Ui1TextKey.PlayerChoiceEquivalentSelections, outcome.EquivalentSelectionCount),
                    outcome.ProductRelevantAuthority,
                    outcome.ProductRelevantCompleteness,
                    outcome.PredictionScope,
                    outcome.EquivalentSelectionCount,
                    outcome.RawLegalSelectionCount,
                    outcome.DistinctDeckStateCount);
            }).ToArray();
            return new PlayerChoiceRouteSetViewModel(
                routeSet.RouteSetId,
                order,
                choiceRelic,
                routeSet.RawLegalSelectionCount,
                routeSet.DistinctDeckStateCount,
                uiText.Format(Ui1TextKey.PlayerChoiceCounts,
                    routeSet.RawLegalSelectionCount,
                    routeSet.DistinctDeckStateCount,
                    outcomes.Length),
                outcomes);
        }).ToArray();

        return new PlayerChoiceImpactViewModel(
            uiText.Get(Ui1TextKey.PlayerChoiceImpactTitle),
            routeSets);
    }

    private static IReadOnlyList<PredictedEffectGroup> BuildObjectiveRelicGroups(
        BonesAcquisitionRouteResult route,
        BonesRelicScopedResult result,
        bool hasPlayerChoiceImpact)
    {
        PredictedEffectGroup[] groups = result.EffectGroups
            .Where(group => group.Phase != PredictedEffectPhase.RelicAcquisition &&
                            !group.IsPlayerChoiceGroup)
            .ToArray();
        if (!hasPlayerChoiceImpact)
        {
            return groups;
        }

        int relicStep = IndexOf(route.AcquisitionOrder, result.SourceRelicKey);
        bool followsPlayerChoice = (route.PlayerChoiceSelections ?? Array.Empty<PlayerChoiceSelectionTrace>())
            .Any(trace => trace.AcquisitionStepIndex < relicStep);
        if (!followsPlayerChoice)
        {
            return groups;
        }

        // The Capsule identity and nested relics are objective seed facts. The
        // automatic Whetstone/War Paint targets that depend on an earlier player
        // choice belong exclusively to PlayerChoiceImpactAnalysis.
        return groups
            .Select(group => group with
            {
                OrderedItems = group.OrderedItems
                    .Where(item => item.Relation is not
                        (PredictedEffectRelation.NestedAutomaticEffect or PredictedEffectRelation.AffectedTarget))
                    .ToArray()
            })
            .Where(group => group.OrderedItems.Count > 0)
            .ToArray();
    }

    private static string FormatAcquisitionOrder(IReadOnlyList<ModelKey> order) =>
        string.Join(">", order.Select(key => key.Serialized));

    private static string PolicyLabel(PlayerChoicePolicyDescriptor policy, IUiTextProvider uiText)
    {
        string label = uiText.Get(policy.Kind switch
        {
            PlayerChoicePolicyKind.RemoveStrike => Ui1TextKey.PlayerChoiceRemoveStrike,
            PlayerChoicePolicyKind.RemoveDefend => Ui1TextKey.PlayerChoiceRemoveDefend,
            PlayerChoicePolicyKind.RemoveStrikeStrike => Ui1TextKey.PlayerChoiceRemoveSS,
            PlayerChoicePolicyKind.RemoveStrikeDefend => Ui1TextKey.PlayerChoiceRemoveSD,
            PlayerChoicePolicyKind.RemoveDefendDefend => Ui1TextKey.PlayerChoiceRemoveDD,
            PlayerChoicePolicyKind.UpgradeStarterCard => Ui1TextKey.PlayerChoiceUpgradeStarter,
            PlayerChoicePolicyKind.TransformStrike => Ui1TextKey.PlayerChoiceTransformStrike,
            PlayerChoicePolicyKind.TransformDefend => Ui1TextKey.PlayerChoiceTransformDefend,
            PlayerChoicePolicyKind.ChooseOfferedCard => Ui1TextKey.PlayerChoiceOfferedCard,
            PlayerChoicePolicyKind.SkipOfferedCard => Ui1TextKey.PlayerChoiceSkipOffer,
            PlayerChoicePolicyKind.ChooseOfferedCardCombination => Ui1TextKey.PlayerChoiceOfferCombination,
            _ => Ui1TextKey.PlayerChoiceRoute
        });

        if (policy.SourceUpgradeLevels is not { Count: > 0 } levels || levels.All(level => level <= 0))
        {
            return label;
        }

        string pattern = string.Join(" + ", levels.Select(level => level > 0 ? $"+{level}" : "+0"));
        return label + " · " + uiText.Format(Ui1TextKey.PlayerChoiceUpgradedVariant, pattern);
    }

    private static RouteProjectionStatus EffectiveRouteStatus(BonesAcquisitionRouteResult route)
    {
        if (route.RouteProjectionStatus != RouteProjectionStatus.Unknown)
        {
            return route.RouteProjectionStatus;
        }

        return route.ProductRelevantProjectionStatus switch
        {
            ProductRelevantProjectionStatus.NotEvaluatedByPolicy => RouteProjectionStatus.NotEvaluatedByPolicy,
            ProductRelevantProjectionStatus.Unsupported => RouteProjectionStatus.Unsupported,
            ProductRelevantProjectionStatus.Unknown => RouteProjectionStatus.Unknown,
            ProductRelevantProjectionStatus.Evaluated
                when route.ProductRelevantProjectionPrecision == PredictionPrecision.Exact &&
                     route.SharedContinuation.FinalCursePrecision == PredictionPrecision.Exact =>
                RouteProjectionStatus.Exact,
            ProductRelevantProjectionStatus.Evaluated => RouteProjectionStatus.Partial,
            _ => RouteProjectionStatus.Unknown
        };
    }

    private static string ImpactLabel(AcquisitionOrderImpact impact, IUiTextProvider uiText) =>
        uiText.Get(impact switch
        {
            AcquisitionOrderImpact.ProvenIndependent => Ui1TextKey.BonesOrderIndependent,
            AcquisitionOrderImpact.ProvenSensitive => Ui1TextKey.BonesOrderSensitive,
            _ => Ui1TextKey.BonesOrderIndeterminate
        });

    private static string VisibleName(
        ModelKey key,
        GameContentKind kind,
        IGameContentNameResolver contentNames,
        bool showInternalIds) =>
        GameContentDisplayPresentationBuilder.Build(
            key,
            kind,
            contentNames,
            showInternalIds).DisplayName;

    private static int IndexOf(IReadOnlyList<ModelKey> order, ModelKey key)
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == key) return i;
        }
        return int.MaxValue;
    }
}
