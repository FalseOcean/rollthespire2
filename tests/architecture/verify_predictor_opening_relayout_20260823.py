#!/usr/bin/env python3
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]


def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")


def req(name, ok):
    if not ok:
        raise AssertionError(name)
    print(f"PASS: {name}")


opening = read("Ui/Components/AnalysisOpeningGroupCard.cs")
strip = read("Ui/Components/NeowChoiceStrip.cs")
detail = read("Ui/Components/NeowChoiceDetailPanel.cs")
compact = read("Ui/Components/NeowCompactEffectList.cs")
reward = read("Ui/Components/NormalCombatRewardSequenceSummaryList.cs")
shop = read("Ui/Components/AnalysisRelicShopGroupCard.cs")
anchor = read("Ui/Components/AnalysisAnchorBar.cs")
tab_icons = read("Ui/Icons/SearchCategoryTabIconProvider.cs")
event = read("Ui/Pages/Search/Event/EventSequenceFilterPage.cs")
event_controls = read("Ui/Pages/Search/Event/EventAuthoringControlStrip.cs")
page = read("Ui/Pages/Analysis/AnalysisPage.cs")
keys = read("Presentation/Localization/Ui1TextKey.cs")
en = json.loads(read("Localization/rt2_ui/en.json"))
zh = json.loads(read("Localization/rt2_ui/zh.json"))

req("openingHasThreeColumnTrack", "OpeningColumnStrip" in opening and "ChoiceColumnRatio = 0.20f" in opening and "EffectColumnRatio = 0.37f" in opening and "RewardColumnRatio = 0.43f" in opening and "SizeFlagsStretchRatio = ChoiceColumnRatio" in opening and "SizeFlagsStretchRatio = EffectColumnRatio" in opening and "SizeFlagsStretchRatio = RewardColumnRatio" in opening)
req("openingColumnsShareHeight", "SizeFlagsVertical = Control.SizeFlags.ExpandFill" in opening and "columns.AddChild(_choiceSubcard)" in opening)
req("choiceStripIsVertical", "NeowChoiceStrip : VBoxContainer" in strip and "childColumn" in strip and "margin_left" in strip)
req("bonesOnlyExpandsWhenSensitive", "BonesOrderAffectsResults" in strip and "AcquisitionOrderImpact.ProvenSensitive" in strip and "OrderComparisonStatus.ProvenSensitive" in strip)
req("bonesRoutesRemainSelectable", "HasAmbiguousBonesRoutes" in strip and "firstPickupHeader" in strip and "route.Text = firstRelic.DisplayName" in strip)
req("bonesDetailUsesProjectionSource", "private Control BuildBones(" in detail and "BuildSourceHeader(choice.RelicDisplay" in detail and "BuildRelicColumn(relic" in detail)
req("sourceHeadersKeepStableNames", "name.CustomMinimumSize = new Vector2(120f, iconSize)" in detail and "name.TextOverrunBehavior" in detail)
req("compactEffectsUseIconRows", "new PredictorMiniCard" not in compact and "BuildAnchoredContentIdentity" in compact and "groupHeading" in compact)
req("openingRewardsAreVerticalBattles", 'Name = "CombatRewardBattleStrip"' in reward and "var battles = new VBoxContainer" in reward and "var battleColumn = new VBoxContainer" in reward and "var topInfoRow = new HBoxContainer" in reward and "var groupRow = new HBoxContainer" in reward and "BuildRewardItem" in reward)
req("rewardGroupItemsShareWidth", "SizeFlagsStretchRatio = 1f" in reward and "item.UseClippedName()" in reward)
req("rewardGroupsAlwaysHaveGroupLabel", "if (cardRewards.Count > 1)" not in reward and "rewardGroupShortFormat" in reward)
req("shopUsesDynamicEqualFiveCards", "MerchantShopCardWidth" not in shop and "MerchantShopCardGap = 8" in shop and "_row = new HBoxContainer" in shop and "SizeFlagsHorizontal = Control.SizeFlags.ExpandFill" in shop and "SizeFlagsStretchRatio = 1f" in shop and "MerchantShopCardHeight = 240f" in shop)
req("shopCardUsesSixLeftAlignedRows", "MerchantRelicIconSize = 48f" in shop and "MerchantColorlessIconSize = 44f" in shop and "_relicLabels" in shop and "body.AddChild(topRow)" in shop and "body.AddChild(relicLabel)" in shop and "body.AddChild(_relicCells[i])" in shop and "body.AddChild(cardLabel)" in shop and "body.AddChild(_uncommonCells[i])" in shop and "body.AddChild(_rareCells[i])" in shop and "colorlessItems" not in shop)
req("shopCardLabelIsRowOwned", "_cardLabels[i]" in shop and "merchant.UncommonColorless" in shop and "_cardLabel);" not in shop)
req("fakeMerchantSlotIsLocallyWide", "_fakeRelicSlot.UseCompactInlineWidth(300f)" in event)
req("openingAutoSelectsFirstChoice", "selectedChoice = _viewModel.NeowChoices.OrderBy(choice => choice.SlotIndex).FirstOrDefault();" in page and "selectedChoice.OpeningRoutes" in page and "AnalysisOpeningSelectRelicPrompt" in page)
req("openingHasPurposeHint", "AnalysisOpeningChoiceHint" in opening and "_choiceHint" in opening)
req("openingSeparatesNoEffectsFromFailure", "AnalysisNoEffectsToPredict" in page and "AnalysisPredictionFailed" in page and "NeowPredictionPresentationState.NoAdditionalPredictionNeeded" in page)
req("eventDefaultIsThree", "DefaultRangeValue = 3" in event_controls and "SelectNumber(DefaultRangeValue)" in event_controls and "_authoringControls.NeedsReset" in event)
nav = read("Ui/Pages/Search/SearchCategoryNavigationBar.cs")
req("clearButtonOwnsConditionCount", "SearchClearDraftCount" in nav and "_conditionSummary" not in nav and "_clearConditions.Disabled = _running || _enabledConditionCount == 0" in nav)
req("ancientOptionIconReduced", "OptionIconSize = 34f" in read("Ui/Components/AnalysisAncientGroupCard.cs"))
req("analysisAnchorHasCenteredPreviewIcons", "AnalysisAnchorPreviewItem" in anchor and "Alignment = BoxContainer.AlignmentMode.Center" in anchor and "CustomMinimumSize = new Vector2(22f, 22f)" in anchor and "content.AddThemeConstantOverride(\"separation\", 8)" in anchor and "preview.AddThemeConstantOverride(\"separation\", 4)" in anchor)
req("analysisAnchorPreviewsAreNonInteractive", "MouseFilter = Control.MouseFilterEnum.Ignore" in anchor and "_tooltipHost" not in anchor and "ShowFor" not in anchor)
req("openingEffectColumnHidesForNoEffects", "SyncEffectVisibility(string emptyPrompt, bool showEffectColumn)" in opening and "_effectSubcard.Visible = showEffectColumn" in opening and "1f - ChoiceColumnRatio" in opening and "ShouldShowNeowEffectColumn" in page)
req("openingEffectColumnRemainsForFailure", "choice is null ||" in page and "HasVisibleResults(choice)" in page and "AnalysisPredictionFailed" in page)
req("openingEffectColumnUsesSharedPresentationPolicy", "HasVisibleResults" in read("Ui/Components/NeowPredictedResultPresentationPolicy.cs") and "NeowPredictedResultPresentationPolicy.HasVisibleResults(choice)" in page)
req("openingFixedResultsDoNotLeaveEmptyColumn", all(key in read("Ui/Components/NeowPredictedResultPresentationPolicy.cs") for key in ["NeowsTorment", "NeowsSacrifice", "PreciseScissors", "PrecariousShears"]) and "Filter(choice.RelicKey, choice.EffectGroups).Count > 0" in read("Ui/Components/NeowPredictedResultPresentationPolicy.cs"))
req("openingSharedPolicyKeepsBonesAndFailure", "choice.BonesOutcome is not null" in read("Ui/Components/NeowPredictedResultPresentationPolicy.cs") and "NeowPredictionPresentationState.PredictionUnavailable" in read("Ui/Components/NeowPredictedResultPresentationPolicy.cs"))
req("analysisPreviewUsesExistingDomains", "BuildAnchorPreviews" in page and ".Take(3)" in page and "AncientDomain.Status == SeedDomainEvaluationStatus.Evaluated" in page and "BossDomain.Status == SeedDomainEvaluationStatus.Evaluated" in page)
boss_preview = page.split("if (_viewModel.BossDomain.Status == SeedDomainEvaluationStatus.Evaluated)", 1)[1].split("return previews;", 1)[0]
req("analysisBossPreviewKeepsAllOrdinals", ".Where(item => item.Act == act)" in boss_preview and ".OrderBy(item => item.Ordinal)" in boss_preview and ".Take(1)" not in boss_preview and "foreach (AnalysisAnchorPreviewItem item in items)" in anchor)
req("analysisPreviewClearsOnFailureOrLoading", "_anchorBar.BindPreviews(EmptyAnchorPreviews())" in page)
req("searchTabsUseRequestedDisplayOrder", nav.index("AddTab(tabs, SearchCategoryKey.Neow);") < nav.index("AddTab(tabs, SearchCategoryKey.Ancient);") < nav.index("AddTab(tabs, SearchCategoryKey.Shop);") < nav.index("AddTab(tabs, SearchCategoryKey.CombatReward);") < nav.index("AddTab(tabs, SearchCategoryKey.Event);") < nav.index("AddTab(tabs, SearchCategoryKey.BossAndMap);") < nav.index("AddTab(tabs, SearchCategoryKey.Relic);"))
req("searchTabRelicReusesRunHistoryTreasure", "RunHistoryTreasureIconPath" in tab_icons and "res://images/ui/run_history/treasure.png" in tab_icons and "SearchCategoryKey.Relic => LoadExactTexture" in tab_icons)
for key in [
    "ui1.analysis.opening.select_relic_prompt",
    "ui1.analysis.opening.choice_hint",
    "ui1.analysis.prediction_error",
    "ui1.analysis.no_effects_to_predict",
    "ui1.analysis.prediction_failed",
    "ui1.search.clear_draft_count",
]:
    req(f"localized_{key}", key in en and key in zh)

print("PredictorOpeningRelayout20260823=PASS")
