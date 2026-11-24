#!/usr/bin/env python3
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]
def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f'{name}=true')

preset = read('Ui/Persistence/SearchPresetPersistence.cs')
context = read('Ui/Pages/Search/SearchContextBar.cs')
nav = read('Ui/Pages/Search/SearchCategoryNavigationBar.cs')
page = read('Ui/Pages/Search/SearchPage.cs')
overlay = read('Ui/Components/SearchPresetLibraryOverlay.cs')
save = read('Ui/Components/SearchPresetSaveTransactionOverlay.cs')
controller = read('Ui/Controllers/SearchPageController.cs')
contracts = read('Ui/Components/SearchPresetTransactionContracts.cs')
en = json.loads(read('Localization/rt2_ui/en.json'))
zh = json.loads(read('Localization/rt2_ui/zh.json'))

# Current Owner-approved hierarchy: Character/Ascension only in the context card;
# explicit Save/Load Preset actions share the existing Search title row.
req('contextOwnsOnlyCharacterAscension', '_presetWorkspace' not in context and 'PresetWorkspaceRequested' not in context)
req('domainNavigationHasNoPresetActions', all(token not in nav for token in ['_savePreset','_loadPreset','SavePresetRequested','LoadPresetRequested']))
req('clearAllMovedToDomainNavigation', 'ClearConditionsRequested' in nav and '_clearConditions' in nav)
req('titleRowOwnsPresetActions', all(token in page for token in ['titleRow.AddChild(_pageTitle);','titleRow.AddChild(_savePresetAction);','titleRow.AddChild(_loadPresetAction);']))
req('searchWiresDomainClear', '_categoryNavigation.ClearConditionsRequested += ClearAllFilters;' in page)
req('searchWiresExplicitPresetActions', '_savePresetAction.Pressed += () => ShowSavePresetDialog(_knownPresets);' in page and '_loadPresetAction.Pressed += () => PresetWorkspaceRequested?.Invoke();' in page)

# Three fixed source roots and no invented official product content.
for source in ['BuiltIn','Temporary','User']:
    req('sourceEnum_' + source, source in preset[preset.index('enum SearchPresetSource'):preset.index('SearchPresetVisualIconRef')])
req('officialProviderBundledOwnerCurated', 'BuiltInSearchPresetProvider' in preset and 'OfficialPresets/' in preset and 'SearchPresetStore.Normalize(' in preset and 'SearchPresetSource.BuiltIn' in preset)
req('workspaceThreeFixedRoots', all(token in overlay for token in [
    'AddSection(SearchPresetSource.BuiltIn', 'AddSection(SearchPresetSource.Temporary', 'AddSection(SearchPresetSource.User']))
req('workspaceHeaderHasNoSaveCurrent', 'SaveCurrentRequested' not in overlay and '_saveCurrent' not in overlay)
req('temporarySaveAsMine', 'SaveAsUserRequested' in overlay and 'SearchPresetSource.Temporary' in overlay)
req('userMetadataEditOnly', 'EditMetadataRequested' in overlay and 'SearchPresetSource.User' in overlay)

# v2 persistence + narrow v1 read; unresolved raw JSON retained for B-Compatibility.
req('presetSchema2', 'internal const int SchemaVersion = 2;' in preset)
req('v1NarrowFieldsRetained', all(token in preset for token in ['public string Name', 'public JsonElement Draft', 'public string VisualRelicKey']))
req('v1RelicPromotedToGenericRef', 'SearchPresetVisualIconRef.FromRelic(legacyRelic)' in preset)
req('rawQueryEnvelope', 'string RawQueryJson' in preset and 'preservedRaw=true' in preset)
req('noGenericMigrationFramework', 'PresetMigration' not in preset and 'MigrationRegistry' not in preset and 'IMigration' not in preset)

# Query authority remains Character + Ascension + SearchDraft; B-Core metadata is separate.
record_block = preset[preset.index('internal sealed record SearchPresetDefinition'):preset.index('internal sealed record SearchPresetCapture')]
for required in ['ModelKey CharacterKey','int Ascension','SearchDraft? Draft']:
    req('queryAuthority_' + required.split()[-1].replace('?',''), required in record_block)
for forbidden in ['SearchRunDraft','UnlockContext','StartSeed','TargetMatchCount','WorkerCount']:
    req('presetExcludesExecution_' + forbidden, forbidden not in record_block)
req('savedProbabilityIsMetadata', 'SearchPresetProbabilitySnapshot SavedProbability' in record_block)
req('provenanceIsMetadata', 'SearchPresetProvenance Provenance' in record_block)

# Visual contract is generic; current UI is Relic-only and max three.
visual_block = preset[preset.index('record SearchPresetVisualIconRef'):preset.index('record SearchPresetProbabilitySnapshot')]
req('visualRefGenericKindStableIdentity', 'string Kind' in visual_block and 'string StableContentIdentity' in visual_block)
req('visualRefNotRelicIdField', 'RelicId' not in visual_block and 'ModelKey Relic' not in visual_block)
req('maxThreeVisualRefsPersisted', '.Take(3)' in preset)
req('currentUiUsesThreeRelicSlots', 'new RelicSequencePickerSlot[3]' in save and 'new Button[3]' in save)
req('currentPickerRelicOnly', 'SearchPresetVisualIconRef.RelicKind' in page and 'SearchPresetVisualIconRef.FromRelic' in page)
for future_ui in ['EventPicker','PotionPicker','CardPicker','UniversalAssetBrowser']:
    req('noPrematureVisualUi_' + future_ui, future_ui not in save)

# Temporary history contract: successful real run only, persistent five-entry FIFO, no dedupe.
req('temporaryPersistentFile', 'temporary_search_presets.json' in preset)
req('temporaryCapacityFive', 'private const int Capacity = 5;' in preset)
req('temporaryFifoTrim', '_entries.Insert(0, entry);' in preset and '_entries.RemoveRange(Capacity' in preset)
req('temporaryNoDedupe', 'temporary:' in preset and 'Guid.NewGuid()' in preset)
req('temporaryRecordedAfterSessionConstructed', '_session = createdSession;' in controller and 'RecordSuccessfulRunTemporaryPreset' in controller and controller.index('_session = createdSession;') < controller.index('RecordSuccessfulRunTemporaryPreset', controller.index('_session = createdSession;')))
req('temporaryFailureCannotRollbackSearch', 'searchContinues=true' in controller and 'must never roll back' in controller)

# Saved probability/provenance come from existing production presentation/runtime authority.
req('savedProbabilityUsesExistingBuilder', 'SearchProbabilityPresentationBuilder.Build' in controller)
req('provenanceUsesRuntimeAuthoritySnapshot', 'RuntimeAuthorityEnvironment.Current' in controller)
req('tempToMyCopiesHistoricalSnapshot', 'SaveUserPresetFromExisting' in controller and 'CreateFromExistingSnapshot' in controller and 'source with' in preset)

# Metadata transaction edits title/description/icons only.
req('metadataContractNarrow', all(token in contracts for token in ['string Title','string Description','IReadOnlyList<SearchPresetVisualIconRef> VisualIcons']))
for forbidden in ['SearchDraft','CharacterKey','Ascension']:
    req('metadataDoesNotEdit_' + forbidden, forbidden not in contracts[contracts.index('SearchPresetMetadataDraft'):])

# B-Core persistence/raw-query boundaries remain intact after B-Compatibility lands.
compat = read('Ui/Persistence/SearchPresetCompatibility.cs')
req('unresolvedRawStillPreserved', 'RawQueryJson' in preset and 'rawPresetPreserved=true' in controller)
req('bCompatibilityExternalToAssetAuthority', 'SearchPresetCompatibilityResolver' in compat and '.CurrentUnlockState' not in compat and 'FindCurrentUnlockDomain' not in compat)

for key in [
    'ui1.search.preset.library_title', 'ui1.search.preset.save_current',
    'ui1.search.preset.official_section', 'ui1.search.preset.temporary_section',
    'ui1.search.preset.user_section', 'ui1.search.clear_draft']:
    req('localized_' + key.replace('.','_'), key in en and key in zh and bool(en[key]) and bool(zh[key]))

print('presetWorkspaceBCore20260820=true')
