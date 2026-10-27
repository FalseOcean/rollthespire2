from pathlib import Path
root = Path(__file__).resolve().parents[2]

def read(rel):
    return (root / rel).read_text(encoding='utf-8')

def req(name, ok):
    print(f'{name}={str(bool(ok)).lower()}')
    if not ok:
        raise AssertionError(name)

opening = read('Ui/Components/AnalysisOpeningGroupCard.cs')
analysis = read('Ui/Pages/Analysis/AnalysisPage.cs')
neow = read('Ui/Components/NeowChoiceDetailPanel.cs')
compact = read('Ui/Components/NeowCompactEffectList.cs')
reward = read('Ui/Components/NormalCombatRewardSequenceSummaryList.cs')
policy = read('Ui/Components/NeowPredictedResultPresentationPolicy.cs')
ancient = read('Ui/Components/AnalysisAncientGroupCard.cs')
shop = read('Ui/Components/AnalysisRelicShopGroupCard.cs')
act = read('Ui/Components/AnalysisActPredictionCard.cs')
keys = read('Presentation/Localization/Ui1TextKey.cs')
en = read('Localization/rt2_ui/en.json')
zh = read('Localization/rt2_ui/zh.json')

req('openingSharedTrackBridge', 'SetSharedColumnTracks' in opening and 'ChoiceDetail.SetColumnTrackWeights' in opening and 'CombatRewardList.SetColumnTrackWeights' in opening)
req('analysisTrackShapeFromSelectedReward', 'SelectedRewardRouteUsesWideMiddleTrack' in analysis and 'UsesWideMiddleTrack(route)' in analysis)
req('normalTracksDefault111', '_firstTrackWeight = 1f' in neow and '_secondTrackWeight = 1f' in neow and '_thirdTrackWeight = 1f' in neow)
req('candyTracks313831', 'CandyOuterBattleNormalWeight' in opening and 'CandyOuterBattleWideWeight' in opening)
req('bonesThreeColumnsConsumeSharedTracks', 'subcards.Count == 3' in neow and 'TrackWeight(index)' in neow)
req('combatConsumesSharedTracks', 'TrackWeightForBattle' in reward and 'battleCard.SizeFlagsStretchRatio = TrackWeightForBattle' in reward)
req('neowResponsiveMiniCards', 'ResponsiveSummaryCardRow' in compact and 'Math.Max(3, _cards.Count)' in compact and 'card.SetResponsiveWidth(perCard)' in compact)
req('sameMiniCardResponsiveLanguage', 'PredictorMiniCard.MinimumResponsiveWidth' in compact and 'SetResponsiveWidth' in compact)
req('kaleidoscopeGroupsPreserved', 'ShouldShowOfferGroupLabels' in neow and 'kaleidoscope-group-' in neow)
req('heftyScopedFixedPayloadSuppressed', 'IsCanonicalOrScopedGroup' in policy and 'EndsWith("." + canonicalGroupId' in policy and 'hefty-tablet-fixed' in policy)
req('randomInjuryNotGloballyHidden', 'BaseGameModelKeys.Cards.Injury' not in policy and 'FinalCurseDisplay' in neow and 'curseCard.Bind(curse' in neow)
req('ancientMeasuredSoftBorrowing', '_optionMeasureLabel.GetMinimumSize().X' in ancient and 'AdaptiveInlineWidthAllocator.Allocate' in ancient and 'OptionMinimumItemWidth' in ancient)
req('shopHintKey', 'AnalysisRelicShopHint' in keys and 'First five shop relics, shown in order' in en and '前五个商店售卖遗物，按序显示' in zh)
req('shopNaturalMeasuredWidths', 'ReflowShopWidths' in shop and '_measureLabel.GetMinimumSize().X' in shop and 'AdaptiveInlineWidthAllocator.Allocate' in shop and 'measuredText[index]' in shop)
req('shopSoftBorrowingBeforeEllipsis', 'AdaptiveInlineWidthAllocator.Allocate' in shop and 'natural content widths are used' in shop and 'ClipText = clipped' in shop)
req('shopLeftAlignedSingleRow', 'Name = "ShopSequence"' in shop and 'Alignment = BoxContainer.AlignmentMode.Begin' in shop and 'ShopResultCount = 5' in shop)
req('bossOrderLabels', 'AnalysisBossFirst' in keys and 'AnalysisBossSecond' in keys and 'BossOrderLabel' in act and 'First Boss' in en and '第一个 Boss' in zh)
req('futureActMapNotIntroducedHere', 'Weak Monster Pool' not in opening and 'Map overlay' not in opening)
print('PASS')
