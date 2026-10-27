#!/usr/bin/env python3
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]
def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f'{name}=true')

compat = read('Ui/Persistence/SearchPresetCompatibility.cs')
controller = read('Ui/Controllers/SearchPageController.cs')
overlay = read('Ui/Components/SearchPresetLibraryOverlay.cs')
page = read('Ui/Pages/Search/SearchPage.cs')
persist = read('Ui/Persistence/SearchPresetPersistence.cs')
workspace = read('Ui/Persistence/SearchWorkspacePersistence.cs')
app = read('Ui/Shell/AppShell.cs')
keys = read('Presentation/Localization/Ui1TextKey.cs')
en = json.loads(read('Localization/rt2_ui/en.json'))
zh = json.loads(read('Localization/rt2_ui/zh.json'))

# Resolution authority: current SemanticUniverse only, never current unlock/admission.
req('resolverExists', 'internal static class SearchPresetCompatibilityResolver' in compat)
req('resolverConsumesSemanticUniverse', 'authority.SemanticUniverse' in compat and 'StableIdentityMissingFromCurrentSemanticUniverse' in compat)
req('resolverDoesNotConsumeCurrentUnlockState', 'FindCurrentUnlockDomain' not in compat and '.CurrentUnlockState' not in compat)
req('domainIncompleteFailsClosedForReferenceResolution', 'CurrentSemanticDomainAuthorityIncomplete:' in compat)
req('domainIncompleteDoesNotBecomePartialMissingIdentity', 'CurrentSemanticAuthorityIncomplete' in compat and 'HasAuthorityIncomplete(unresolved)' in compat)
req('categoryAgnosticResolutionRespectsDomainCompleteness', 'candidateDomains = key.Category switch' in compat and 'BaseGameModelKeys.Categories.Event' in compat)
req('characterMustResolve', 'PresetCharacterUnavailable' in compat)
req('rawShapeCanRemainUnavailable', 'PresetQueryShapeUnresolved' in compat)
req('threeLoadOutcomes', all(x in compat for x in ['Unavailable = 0','Full = 1','Partial = 2']))
req('noResolvableConditionsOutcome', 'NoResolvablePresetConditions' in compat)

# Whole authored conditions are dropped, not member-pruned.
req('relicConditionWholeDrop', 'ResolveRelicSequence' in compat and 'output.Add(condition)' in compat and 'dropped++' in compat)
req('eventConditionWholeDrop', 'ResolveEventSequence' in compat)
req('ancientRowWholeDrop', 'IsActive = false' in compat and 'SeaGlassTargetKeys = Array.Empty<ModelKey>()' in compat)
req('combatSequenceWholeDrop', 'cards = CombatRewardSearchDraft.Empty.Cards' in compat and 'potions = CombatRewardSearchDraft.Empty.Potions' in compat)
req('neowEffectWholeDrop', 'effects.Add(effect)' in compat and 'else dropped++' in compat)

# Original asset remains immutable/preserved; partial load is a projection only.
req('resolverReturnsNewDraftProjection', 'SearchDraft resolved = source with' in compat)
req('assetRawQueryStillPersisted', 'string RawQueryJson' in persist and 'querySnapshot' in persist)
req('loadLogsRawPreserved', 'rawPresetPreserved=true' in controller)
req('loadDoesNotRewritePresetAsset', 'WriteOne' not in controller[controller.index('private void LoadPreset'):controller.index('private void DeletePreset')])

# Version/environment compatibility is warning/provenance, never Search admission.
req('semanticCompatibilityAssessment', 'SearchPresetCompatibilityAssessment' in compat and 'SameRuntimeSemantics' in compat and 'DifferentRuntimeSemantics' in compat)
req('historicalFingerprintReinterpreted', 'InterpretHistoricalFingerprint' in compat)
req('sameFingerprintAcrossVersionRecognized', 'CrossVersionSameSemanticFingerprint' in compat)
req('unlockDifferenceIsMetadataOnly', 'CurrentUnlockMayBeNarrower' in compat and 'VanillaUnlockOverallStatus.Partial' in compat)
req('overlayShowsCompatibilityNotice', 'BindCompatibilityNotice' in overlay and 'SearchPresetCompatibilitySemanticDifferent' in overlay)
req('playerUiDoesNotExposeInternalLoadIssueCode', '_text.Get(Ui1TextKey.SearchPresetCompatibilityUnavailable)' in overlay and 'resolution.Issue' not in overlay[overlay.index('private void BindCompatibilityNotice'):overlay.index('private void AddDetailIcon')])
req('compatibilityDoesNotCallSearchAdmission', 'SearchExecutionCoordinator' not in compat and 'SearchCompiler' not in compat)

# Load while Search is active: async cancel, discard old candidates, commit only at terminal.
load_block = controller[controller.index('private void LoadPreset'):controller.index('private void DeletePreset')]
req('activeSearchLoadRequestsCancel', '_session.Cancel();' in load_block and 'searchPresetLoadCancellationRequested=true' in load_block)
req('activeSearchLoadDoesNotWait', 'mainThreadWait=false' in load_block and '.Wait(' not in load_block and 'GetAwaiter().GetResult' not in load_block)
req('loadDeferredUntilTerminal', 'deferredCommitUntilSessionTerminal=true' in load_block and '_pendingPresetLoad = pending' in load_block)
req('oldSessionCandidatesDiscarded', '_discardActiveSessionCandidates' in controller and 'continue;' in controller[controller.index('private void Poll'):])
req('terminalCommitsPendingPreset', 'searchPresetLoadCancellationCompleted=true' in controller and 'CommitPresetLoad(pending, cancelledActiveSearch: true)' in controller)
req('pendingPresetLoadSuppressesWrapContinuation', '_presetLoadCancellationRequested || _wrappedThisCycle' in controller)

# Results are preserved and become stale when Current Query differs.
req('loadDoesNotClearResults', 'ClearResults()' not in load_block)
req('workspaceKeepsResultQueryFingerprint', 'public string QueryFingerprint' in workspace and 'ResetWorkspaceForSuccessfulStart' in workspace)
req('staleComparisonUsesCompiledFingerprint', 'RefreshResultsStaleness' in controller and 'compiled.SemanticFingerprint' in controller and 'Workspace.QueryFingerprint' in controller)
req('staleUiExists', 'SetResultsStale' in page and 'SearchResultsStale' in page)
req('newSuccessfulStartStillClearsOldResults', '_persistence.ResetWorkspaceForSuccessfulStart(queryFingerprint)' in controller and '_page.ClearResults();' in controller)

# Semantic Action Receipt: one aggregate action, no nested clear/restore receipts.
req('toastReceiptInjected', 'Action<string> _showReceipt' in controller and 'text => _toast?.ShowToast(text)' in app)
req('fullLoadReceipt', 'SearchPresetReceiptLoaded' in controller)
req('partialLoadReceipt', 'SearchPresetReceiptPartiallyLoaded' in controller)
req('unavailableLoadReceipt', 'SearchPresetReceiptLoadUnavailable' in controller)
req('saveDeleteReceipts', 'SearchPresetReceiptSaved' in controller and 'SearchPresetReceiptDeleted' in controller)
req('partialReceiptUsesCounts', 'resolution.LoadedConditionCount' in controller and 'resolution.UnresolvedConditionCount' in controller)

for key in [
    'ui1.search.preset.compatibility.partial',
    'ui1.search.preset.compatibility.unavailable',
    'ui1.search.preset.compatibility.semantic_different',
    'ui1.search.preset.compatibility.semantic_unknown',
    'ui1.search.preset.compatibility.cross_version_match',
    'ui1.search.preset.compatibility.unlock_reduced',
    'ui1.search.preset.receipt.loaded',
    'ui1.search.preset.receipt.partially_loaded',
    'ui1.search.preset.receipt.load_unavailable',
    'ui1.search.preset.receipt.saved',
    'ui1.search.preset.receipt.deleted',
    'ui1.search.results.stale']:
    req('localized_' + key.replace('.','_'), key in en and key in zh and bool(en[key]) and bool(zh[key]))

print('presetBCompatibility20260820=true')
