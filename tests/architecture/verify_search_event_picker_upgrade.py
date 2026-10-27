from pathlib import Path
import hashlib, json, sys
ROOT=Path(__file__).resolve().parents[2]

def read(p): return (ROOT/p).read_text(encoding='utf-8')
def req(name, ok):
    if not ok:
        print('FAIL:',name); sys.exit(1)
    print('PASS:',name)

page=read('Ui/Pages/Search/Event/EventSequenceFilterPage.cs')
picker=read('Ui/Pages/Search/Event/EventSearchPickerPanel.cs')
catalog=read('Ui/Pages/Search/Event/EventSequenceSearchUiCatalog.cs')
thumb=read('Ui/Icons/EventThumbnailProvider.cs')
card=read('Ui/Pages/Search/Event/EventSequenceConditionCard.cs')
host=read('Ui/Pages/Search/SearchCategoryHost.cs')

req('event_authoring_prefix_only', 'EventAuthoringControlStrip' in page and 'SearchSequenceRangeMode.FirstN' in page and 'SearchEventRangeExactSlot' not in page)
req('event_add_button_removed', 'private readonly Button _add;' not in page and 'AddCondition(' not in page and 'SearchEventAddCondition' not in page)
req('picker_selection_commits_immediately', 'CommitCandidate(candidate)' in page and '_conditions.Add(new EventSequenceUiCondition' in page)
req('authoring_context_preserved_after_commit', 'CommitCandidate' in page and 'ResetDefaults' not in page[page.index('private void CommitCandidate'):page.index('private void RemoveCondition')])
req('existing_exact_slot_restore_preserved', 'condition.RangeMode' in page and 'SearchEventConditionDetailExactAppears' in page)
req('picker_scoped_to_current_act', 'CandidatesForAct(act)' in page and 'SearchEventPickerTitle' in page)
req('picker_localized_search', 'StringComparison.CurrentCultureIgnoreCase' in picker and 'SearchEventPickerSearchPlaceholder' in picker)
req('picker_provenance_filters', 'SearchEventSourceAll' in picker and 'SearchEventSourceShared' in picker and 'VariantActKeys' in picker)
req('provenance_is_catalog_projection', 'OrderedSharedEventsRaw' in catalog and 'OrderedRawEvents' in catalog and 'VariantMembership' in catalog)
req('thumbnail_provider_injected', 'new EventThumbnailProvider()' in host and 'EventSequenceFilterPage(new EventThumbnailProvider(), tooltipHost)' in host)
req('default_portrait_identity_path', 'res://images/events/' in thumb and 'eventKey.Entry.ToLowerInvariant()' in thumb)
req('thumbnail_layout_aware', 'LayoutType' in thumb and 'Default' in thumb and 'Ancient' in thumb and 'MapIcon' in thumb)
req('thumbnail_no_scene_instantiation', 'PackedScene' not in thumb and '.Instantiate' not in thumb)
req('thumbnail_main_thread_guard', 'RuntimeSnapshotThreadGuard.RequireMainThread()' in thumb)
req('thumbnail_fail_soft', 'EventThumbnailKind.Fallback' in thumb and 'return Fallback' in thumb)
req('picker_grid_has_visual_tiles', 'EventPickerTile' in picker and 'GridContainer' in picker)
req('condition_cards_have_thumbnail', 'EventThumbnailView' in card and 'detailText' in card)

for lang in ['en.json','zh.json']:
    d=json.loads(read('Localization/rt2_ui/'+lang))
    for key in ['ui1.search.event.page_title','ui1.search.event.source.all','ui1.search.event.source.shared','ui1.search.event.range.unit','ui1.search.event.condition_detail.first_n_appears']:
        req(f'localized:{lang}:{key}', key in d and d[key] is not None)

expected={
'Ui/Pages/Search/SearchDraft.cs':'4d3516dacfeb17cad50368d102c430ee20db90b14946cfbfb767cc1de47b27a3',
'Search/Semantics/SearchCompiler.cs':'8728ba5ae35f29f94974cbc1fdcd5b8039c0be03e983b6105abfc79a5a174c9c',
'Search/Contracts/SearchContracts.cs':'e4c5b052f1a6b33bc0557dfb8941cb09604cc66618bd9a02fed617a32ad13347',
'Search/Runtime/ProductionQueryValidator.cs':'fac219676c78fb6e7a7f7d2e9b10b371a8c4bd9bcb5f5a729effb53eacd9531e',
'Ui/Controllers/SearchPageController.cs':'8ecbf387cac5b62b91c0d346276a58f1ea5c72e586b2e11dceb96996026bba85',
'Ui/Persistence/SearchWorkspacePersistence.cs':'61ac938b7fffbcfa648a9a4b2b009e8180c1f89d9ab54026be217b18fad8ec27',
'Core/Events/EventStaticEligibilityCatalog.cs':'ea4cfd4732bbcd6125150a82f0cbb32e8b58d4dda2a13d32fcffda84147a4959',
}
for rel,sha in expected.items():
    got=hashlib.sha256((ROOT/rel).read_bytes()).hexdigest()
    req('baseline_unchanged:'+rel, got==sha)
print('SearchEventPickerUpgradeValidation=PASS')
