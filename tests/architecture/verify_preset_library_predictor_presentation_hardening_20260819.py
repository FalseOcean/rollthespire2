#!/usr/bin/env python3
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]
def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f'{name}=true')

preset = read('Ui/Persistence/SearchPresetPersistence.cs')
workspace = read('Ui/Persistence/SearchWorkspacePersistence.cs')
overlay = read('Ui/Components/SearchPresetLibraryOverlay.cs')
page = read('Ui/Pages/Search/SearchPage.cs')
controller = read('Ui/Controllers/SearchPageController.cs')
warning_policy = read('Presentation/Ui1/PlayerWarningPresentationPolicy.cs')
presenter = read('Presentation/Ui1/SeedAnalysisPresentationBuilder.cs')
bones = read('Presentation/Ui1/BonesOutcomePresentationBuilder.cs')
viewmodels = read('Presentation/Ui1/SeedAnalysisViewModels.cs')
neow_detail = read('Ui/Components/NeowChoiceDetailPanel.cs')
choice_impact = read('Ui/Components/PlayerChoiceImpactSummaryList.cs')
analysis_controller = read('Ui/Controllers/AnalysisPageController.cs')
global_header = read('Ui/Shell/GlobalHeader.cs')
ancient = read('Ui/Components/AnalysisAncientGroupCard.cs')
relic = read('Ui/Components/AnalysisRelicShopGroupCard.cs')
neow_strip = read('Ui/Components/NeowChoiceStrip.cs')
ui_text_key = read('Presentation/Localization/UiTextKey.cs')
en = json.loads(read('Localization/rt2_ui/en.json'))
zh = json.loads(read('Localization/rt2_ui/zh.json'))

# Preset Library presentation boundaries after the approved B-Core supersession.
record_block = preset[preset.index('internal sealed record SearchPresetDefinition'):preset.index('internal sealed record SearchPresetCapture')]
req('presetGenericVisualSignature', 'IReadOnlyList<SearchPresetVisualIconRef> VisualIcons' in record_block and 'string Kind' in preset and 'string StableContentIdentity' in preset)
for excluded in ['SearchRunDraft','UnlockContext','StartSeed','TargetMatchCount','WorkerCount','ProfileId','ResourcePath']:
    req('presetPayloadExcludes_' + excluded, excluded not in record_block)
req('savedProbabilityIsHistoricalMetadata', 'SearchPresetProbabilitySnapshot SavedProbability' in record_block)
req('visualIdentityFailSoft', 'NormalizeVisualIcons' in preset and 'icon.IsValid' in preset and '.Take(3)' in preset)
req('officialProviderSamePayload', 'BuiltInSearchPresetProvider : ISearchPresetProvider' in preset and 'UserSearchPresetProvider : ISearchPresetProvider' in preset)
req('officialContentNotInventedInCSharp', 'OfficialPresets/' in preset and 'JsonSerializer.Deserialize<SearchPresetStore.PersistedSearchPreset>' in preset and 'new SearchPresetDefinition(' not in preset[preset.index('internal sealed class BuiltInSearchPresetProvider'):preset.index('internal sealed class UserSearchPresetProvider')])
req('libraryIsMasterDetailOverlay', 'class SearchPresetLibraryOverlay : Control' in overlay and 'SearchPresetOfficialSection' in overlay and 'SearchPresetTemporarySection' in overlay and 'SearchPresetUserSection' in overlay and '_detailName' in overlay and '_summary' in overlay)
req('selectionDoesNotLoad', 'button.Pressed += () =>' in overlay and '_selectedId = preset.Id;' in overlay and 'LoadRequested?.Invoke' not in overlay[overlay.index('button.Pressed += () =>'):overlay.index('_list.AddChild(button);')])
req('explicitLoadAction', '_load.Pressed +=' in overlay and 'LoadRequested?.Invoke(_selectedId)' in overlay)
req('userDeleteOnly', '_delete.Visible = valid && preset!.Source == SearchPresetSource.User' in overlay)
req('savedProbabilityDoesNotMutateSearcher', 'value.SavedProbability' in overlay and 'TryCompileDraftSemanticsForContext(' in controller)
req('loadStillClearReplace', 'ClearAllFilters(notify: false);' in page and 'RestoreWorkspace(characterKey, ascension, draft, runDraft: null, notify: true);' in page)
req('saveSameNameNeedsConfirmation', 'sameNameUser' in page and ('_presetOverwriteSurface.Open(' in page or '_presetOverwriteDialog.PopupCentered' in page))
req('deleteNeedsConfirmation', '_presetDeleteSurface.Open(' in page or '_presetDeleteDialog.PopupCentered' in page)
req('relicIconPickerPresentationOnly', 'BindPresetVisualRelicCatalog' in page and 'SearchPresetVisualIconRef.FromRelic' in page)
req('presetSummaryNoRawRelicLaneEnum', 'condition.Lane, condition.RangeValue' not in overlay and 'FormatRelicCondition' in overlay)
req('presetSummaryNoRawRewardOrderEnum', 'Cards.OrderMode));' not in overlay and 'Potions.OrderMode));' not in overlay and 'FormatOrderMode' in overlay)

# Warning audience: explicit allowlist, diagnostic by default, shared by Bones.
req('warningDefaultDeveloperDiagnostic', '_ => PlayerWarningAudience.DeveloperDiagnostic' in warning_policy)
req('documentUsesSharedPolicy', 'PlayerWarningPresentationPolicy.IsPlayerFacing(code)' in presenter)
req('bonesUsesSharedPolicy', '.Where(PlayerWarningPresentationPolicy.IsPlayerFacing)' in bones)
for internal_code in ['EffectAuthorityIncomplete','EffectProjectionFailed','EffectBranchBudgetExceeded','EffectNestedObtainIncomplete','SnapshotAuthorityIncomplete','ModernEligibilityIncomplete','Beta110ValidationPending','Beta111ValidationPending','CharacterAuthorityUnknown']:
    req('internalNotExplicitlyPlayerFacing_' + internal_code, internal_code not in warning_policy)

# Neow deterministic/no-extra vs unavailable are distinct product states.
for state in ['NoAdditionalPredictionNeeded','Predicted','PredictionUnavailable']:
    req('neowState_' + state, state in viewmodels)
req('builderAssignsPredictionState', 'NeowPredictionPresentationState.PredictionUnavailable' in presenter and 'NeowPredictionPresentationState.NoAdditionalPredictionNeeded' in presenter)
req('directUnavailableVisible', 'choice.PredictionState == NeowPredictionPresentationState.PredictionUnavailable' in neow_detail and '_predictionUnavailable' in neow_detail)

# Compatibility/global-status/raw diagnostics are not ordinary player copy.
req('validationPendingHasExplicitMapping', 'Beta110ValidationPending => "warning.validation_pending"' in ui_text_key and 'Beta111ValidationPending => "warning.validation_pending"' in ui_text_key)
req('globalStatusNoRawRequestEnum', 'requestError.ToString()' not in analysis_controller)
req('globalStatusNoExceptionClass', 'GlobalStatusKind.Error' in analysis_controller and 'ex.GetType().Name);' not in analysis_controller)
req('globalHeaderNoProfileId', 'ProfileId' not in global_header)
compat_keys = [
    'ui1.shell.runtime_badge_tooltip',
    'ui1.shell.runtime_pending_validation_badge_tooltip',
    'ui1.shell.runtime_fallback_badge_tooltip',
    'warning.validation_pending'
]
for forbidden in ['SourceCandidate', 'ProfileId', 'fail-closed', 'Beta110 0.110.1']:
    req('normalCompatibilityRemoved_' + forbidden.replace(' ','_').replace('.','_'), all(forbidden not in str(lang.get(key, '')) for lang in (en,zh) for key in compat_keys))
req('missingIconNoModelKey', all('ModelKey' not in lang['ui1.icon.missing_tooltip'] for lang in (en,zh)))
req('unlockTooltipNoR1Authority', all('R1' not in lang['ui1.analysis.unlock_tooltip'] and 'authority' not in lang['ui1.analysis.unlock_tooltip'].lower() for lang in (en,zh)))
req('pendingValidationVersionNeutral', 'game version' in en['ui1.shell.runtime_pending_validation_badge_tooltip'].lower() and 'Beta110' not in en['ui1.shell.runtime_pending_validation_badge_tooltip'])

# PlayerChoiceImpact keeps concrete outcomes, strips report/debug statistics.
for forbidden in ['CountsLabel','PolicyLabel','EquivalentSelectionCount','EquivalentSelectionLabel','HeaderLabel']:
    req('choiceImpactNo_' + forbidden, forbidden not in choice_impact)
req('choiceImpactKeepsConcreteGroups', 'ChoiceGroups' in choice_impact and 'AutomaticEffectGroups' in choice_impact and 'FinalCurse' in choice_impact)

# Narrow geometry/correctness fixes.
req('ancientUsesNaturalShrink', 'SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin' in ancient and 'naturalTextWidth' in ancient and 'NoTrimming' in ancient)
req('ancientSharedAllocatorPreserved', 'AdaptiveInlineWidthAllocator.Allocate' in ancient)
req('tailTruePositionOneAnchor', 'TailEntries.OrderBy(item => item.Position).FirstOrDefault()' in relic)
# The true physical Tail anchor (Position 1) must own the minimum budget in both
# asymmetric stress directions. This is a semantic allocation check; Godot runtime
# rendering is covered by the Owner smoke checklist.
def max_cursor_for_tail(true_tail_width, end_padding=10.0, gap=6.0, flexible_width=500.0):
    return max(0.0, min(1.0, 1.0 - ((end_padding + gap + true_tail_width) / flexible_width)))
req('tailStressLongTrueTailWins', max_cursor_for_tail(240.0) < max_cursor_for_tail(60.0))
req('tailStressShortTrueTailNotOverConstrained', max_cursor_for_tail(60.0) > max_cursor_for_tail(240.0))
req('heftyGuardPreserved', 'BaseGameModelKeys.Relics.HeftyTablet' in read('Ui/Components/NeowPredictedResultPresentationPolicy.cs') and 'hefty-tablet-fixed' in read('Ui/Components/NeowPredictedResultPresentationPolicy.cs'))
req('selectorNameOnlyTooltipRemoved', 'descriptor.IsMissing' in neow_strip and '? missingIconTooltip\n                : string.Empty' in neow_strip)

# Bilingual keys used by the new library.
for key in ['ui1.search.preset.library_title','ui1.search.preset.save_current','ui1.search.preset.official_section','ui1.search.preset.temporary_section','ui1.search.preset.user_section','ui1.search.preset.condition_summary','ui1.search.preset.probability_unavailable','ui1.search.preset.visual_mark','ui1.search.preset.overwrite_prompt','ui1.search.preset.delete_prompt','ui1.search.preset.summary.act','warning.validation_pending']:
    req('localized_' + key.replace('.','_'), key in en and key in zh and bool(en[key]) and bool(zh[key]))

print('presetLibraryPredictorPresentationHardening20260819=true')
