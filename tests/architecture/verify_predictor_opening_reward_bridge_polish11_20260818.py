#!/usr/bin/env python3
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]

def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f'{name}=true')

page = read('Ui/Pages/Analysis/AnalysisPage.cs')
detail = read('Ui/Components/NeowChoiceDetailPanel.cs')
compact = read('Ui/Components/NeowCompactEffectList.cs')
icon = read('Ui/Components/IconWithLabel.cs')
ancient = read('Ui/Components/AnalysisAncientGroupCard.cs')
reward_core = read('Core/Rewards/NormalCombatRewardSequencePredictor.cs')
search = ''.join(p.read_text(encoding='utf-8', errors='ignore') for p in (ROOT/'Ui/Pages/Search').rglob('*.cs'))
keys = read('Presentation/Localization/Ui1TextKey.cs')
zh = json.loads(read('Localization/rt2_ui/zh.json'))
en = json.loads(read('Localization/rt2_ui/en.json'))

# P0: preserve Core's globally-unique reward route IDs and bridge them in presentation.
req('rewardCoreStillPrefixesChoiceRoute', 'RouteId = $"choice.{choice.SlotIndex}.{route.Route.RouteId}"' in reward_core)
req('presentationBridgeExists', 'RewardBindingMatchesOpeningRoute' in page and 'binding.RootRelicKey == openingRoute.RootRelicKey' in page)
req('bridgeStripsChoicePrefixOnly', 'const string prefix = "choice."' in page and 'originalRouteSeparator' in page and 'rewardBindingRouteId[(originalRouteSeparator + 1)..]' in page)
req('selectedRouteDrivesRewardGroup', page.count('FindRewardRouteGroupId(selectedRoute)') == 1 and page.count('FindRewardRouteGroupId(requestedRoute)') == 1)
req('noRewardReanalysis', 'SubmitDraft' not in page[page.index('private void ApplyOpeningSelection'):page.index('private static OpeningRoutePresentationViewModel? ResolveUniqueSelectableRoute')])
req('ambiguousBonesPromptSpecific', 'AnalysisOpeningBonesRoutePrompt' in page and zh['ui1.analysis.opening.bones_route_prompt'] == '请选择骨骰拾取顺序' and en['ui1.analysis.opening.bones_route_prompt'] == 'Choose the Bones pickup order')

# P1: Bones result area uses lightweight groups, not table separators.
req('bonesLightSubcards', 'WrapBonesResult' in detail and 'BonesResultSubcardPadding = 8f' in detail and 'Ui1SurfaceRole.Input' in detail)
req('bonesNoVerticalDivider', 'NewVerticalSeparator' not in detail and 'VSeparator' not in detail)
req('finalCurseDistinctGroup', 'AnalysisNeowFinalCurse' in detail and zh['ui1.analysis.neow.final_curse'] == '最终诅咒')

# P1: game/content names are atomic; flow/grid owns item-level wrapping.
req('iconNamesNoWrap', 'wrap: false' in icon and '_name.ClipText = false' in icon and '_name.CustomMinimumSize = new Vector2(0f, iconSize)' in icon)
req('old88pxNameMinimumRemoved', 'new Vector2(88f, iconSize)' not in icon)
req('textEffectsNoWrap', 'Ui1Theme.Label(visibleText, _textRole, wrap: false)' in compact)

# P1: Ancient identity remains prominent while option presentation may narrow under later readability convergence.
req('ancientPortrait56', 'AncientIdentityIconSize = 56f' in ancient)
req('ancientOption36', 'OptionIconSize = 36f' in ancient)
req('ancientTarget28', 'TargetIconSize = 28f' in ancient)
req('ancientSideBySidePreserved', 'AncientTwoLayerResult' in ancient and 'AncientResultColumns' in ancient)
req('ancientOptionNamesAtomic', 'option.OptionDisplay.DisplayName' in ancient and 'wrap: false' in ancient and 'OptionOrdinal' not in ancient)

# Search authored UI is not reused/mutated by this work.
req('searchNoAnalysisOpeningBridge', 'RewardBindingMatchesOpeningRoute' not in search and 'AnalysisOpeningBonesRoutePrompt' not in search)
req('localizationKeyPresent', 'AnalysisNeowFinalCurse' in keys and 'AnalysisOpeningBonesRoutePrompt' in keys)

print('predictorOpeningRewardBridgePolish11=true')
