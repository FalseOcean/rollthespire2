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
draft = read('Ui/Pages/Search/SearchDraft.cs')
page = read('Ui/Pages/Search/SearchPage.cs')
bar = read('Ui/Pages/Search/SearchContextBar.cs')
nav = read('Ui/Pages/Search/SearchCategoryNavigationBar.cs')
controller = read('Ui/Controllers/SearchPageController.cs')
en = json.loads(read('Localization/rt2_ui/en.json'))
zh = json.loads(read('Localization/rt2_ui/zh.json'))

# The v1 business boundary is preserved under the approved Preset v2 asset supersession.
req('searchDraftExplicitBusinessBoundary', 'Player business-query draft only' in draft and 'SearchRunDraft' in draft)
record_block = preset[preset.index('internal sealed record SearchPresetDefinition'):preset.index('internal sealed record SearchPresetCapture')]
for required in ['ModelKey CharacterKey','int Ascension','SearchDraft? Draft']:
    req('presetQueryAuthority_' + required.split()[-1].replace('?',''), required in record_block)
for excluded in ['SearchRunDraft','UnlockContext','StartSeed','TargetMatchCount','WorkerCount','scroll','accordion']:
    req('payloadExcludes_' + excluded, excluded not in record_block)
req('savedProbabilityMetadataAllowed', 'SearchPresetProbabilitySnapshot SavedProbability' in record_block)
req('provenanceMetadataAllowed', 'SearchPresetProvenance Provenance' in record_block)
req('separatePresetStorage', 'DirectoryName = "search_presets"' in preset and 'search_workspace.json' in workspace)

# Built-in / Temporary / User providers converge on one asset/load mechanism.
for provider in ['BuiltInSearchPresetProvider','TemporarySearchPresetProvider','UserSearchPresetProvider']:
    req(provider + 'Exists', provider + ' :' in preset)
req('singleCatalogCombinesProviders', all(x in preset for x in ['_builtIn.GetPresets()','_temporary.GetPresets()','_user.GetPresets()']))
req('builtInContentMustBeBundledOwnerCurated', 'OfficialPresets/' in preset and 'ResourcePrefix = "RolltheSpire2.OfficialPresets."' in preset and 'persisted.Id = BuildOfficialId(resourceName);' in preset)

# Fail-soft v2 persistence plus narrow v1 compatibility.
req('schema2', 'internal const int SchemaVersion = 2;' in preset)
req('missingDirectoryFailSoft', 'if (!Directory.Exists(_directory))' in preset)
req('emptyFileFailSoft', 'string.IsNullOrWhiteSpace(jsonText)' in preset and 'reason=EmptyFile' in preset)
req('futureSchemaFailSoft', 'reason=FutureSchema' in preset)
req('perFileEntryFailSoft', 'Directory.EnumerateFiles' in preset and 'searchPresetEntrySkipped=true' in preset and 'catch (Exception ex)' in preset)
req('duplicateIdHandled', 'reason=DuplicateId' in preset)
req('duplicateNameHandled', 'reason=DuplicateName' in preset)
req('unknownFieldsNaturallyIgnored', 'PropertyNameCaseInsensitive = true' in preset)
req('v1FieldsRetainedForNarrowRead', all(x in preset for x in ['public string Name','public JsonElement Draft','public string VisualRelicKey']))
req('v1VisualRelicPromoted', 'SearchPresetVisualIconRef.FromRelic(legacyRelic)' in preset)
req('rawUnresolvedQueryPreserved', 'string RawQueryJson' in preset and 'preservedRaw=true' in preset)

# Load still owns only authored query replacement; run controls / UnlockContext remain outside.
req('clearDoesNotResetContext', 'private void ClearAllFilters(bool notify)' in page and '_contextBar.ClearDraft()' not in page[page.index('private void ClearAllFilters(bool notify)'):page.index('private void OnQueryDraftChanged')])
req('replaceClearThenRestore', 'ClearAllFilters(notify: false);' in page and 'RestoreWorkspace(characterKey, ascension, draft, runDraft: null, notify: true);' in page)
req('runDraftOnlyAppliedWhenProvided', 'if (runDraft is not null)' in page and '_targetCount.Value' in page and '_startSeed.Text' in page)
req('controllerLoadsViaReplace', '_page.ReplaceAuthoredState(preset.CharacterKey, preset.Ascension, resolution.Draft)' in controller and 'SearchPresetCompatibilityResolver.Resolve' in controller)
req('controllerObservesRunControlsUnchanged', 'runControlsUnchanged=' in controller and 'SearchRunDraft runBefore' in controller and 'SearchRunDraft runAfter' in controller)
req('unlockExplicitlyExcluded', 'unlockContextUnchanged=true' in controller)

# Latest Owner UI decision: context bar remains Character/Ascension only; explicit
# Save/Load Preset actions share the existing Search title row, so they consume no new
# vertical row and remain separate from condition-domain navigation.
req('topContextHasNoPresetAction', '_presetWorkspace' not in bar and 'PresetWorkspaceRequested' not in bar)
req('domainNavigationHasNoPresetActions', all(x not in nav for x in ['SavePresetRequested','LoadPresetRequested','_savePreset','_loadPreset']))
req('titleRowOwnsExplicitPresetActions', all(x in page for x in ['titleRow.AddChild(_pageTitle);','titleRow.AddChild(_savePresetAction);','titleRow.AddChild(_loadPresetAction);']))
req('clearMovedToDomainNavigation', 'ClearConditionsRequested' in nav and '_clearConditions' in nav)
req('saveTransactionExists', 'SearchPresetSaveTransactionOverlay' in page and 'PresetSaveConfirmed' in page)
req('saveActionUsesCurrentQueryTransaction', '_savePresetAction.Pressed += () => ShowSavePresetDialog(_knownPresets);' in page)
req('loadUsesPresetWorkspace', 'SearchPresetLibraryOverlay' in page and 'PresetWorkspaceRequested' in page and 'PresetLoadConfirmed' in page and '_loadPresetAction.Pressed += () => PresetWorkspaceRequested?.Invoke();' in page)
req('userDeleteFlow', 'PresetDeleteRequested' in page and 'DeleteUserPreset' in controller)
req('domainClearUsesActualConditionClear', '_categoryNavigation.ClearConditionsRequested += ClearAllFilters' in page)

for key in ['ui1.search.preset.workspace_entry','ui1.search.clear_draft','ui1.search.preset.save_current','ui1.search.preset.library_title']:
    req('localized_' + key.replace('.','_'), key in en and key in zh and bool(en[key]) and bool(zh[key]))
req('clearCopyMatchesProductSemantics', zh['ui1.search.clear_draft'] == '清除当前所有条件' and en['ui1.search.clear_draft'] == 'Clear all conditions')

for token in ['NeowRouteDraft','BonesAcquisitionOrder','AncientMatrixDraft','BossMapDraft','EventSequenceDraft','RelicSequenceDraft','CombatRewardDraft','RequiredFinalCurse']:
    req('draftCoverage_' + token, token in draft)

print('searchPresetV1BoundaryPreservedUnderBCore20260820=true')
