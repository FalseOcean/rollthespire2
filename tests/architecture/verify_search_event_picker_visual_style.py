from pathlib import Path
import json, sys
ROOT=Path(__file__).resolve().parents[2]

def read(p): return (ROOT/p).read_text(encoding='utf-8')
def req(name, ok):
    if not ok:
        print('FAIL:',name); sys.exit(1)
    print('PASS:',name)

picker=read('Ui/Pages/Search/Event/EventSearchPickerPanel.cs')
tile=read('Ui/Pages/Search/Event/EventPickerTile.cs')
thumb=read('Ui/Pages/Search/Event/EventThumbnailView.cs')
host=read('Ui/Pages/Search/SearchCategoryHost.cs')
page=read('Ui/Pages/Search/Event/EventSequenceFilterPage.cs')
card=read('Ui/Pages/Search/Event/EventSequenceConditionCard.cs')
tooltip=read('Ui/Tooltips/AnchoredTooltipHost.cs')
knowledge=read('Presentation/Ui1/Beta111EventPresentationKnowledge.cs')

req('picker_uses_card_like_grid', 'GridContainer' in picker and 'StandardColumns = 4' in picker and 'EventPickerTile' in picker)
req('picker_tile_only_renders_art_and_name', 'EventThumbnailView' in tile and 'LocalizedName' not in tile and 'provenance' not in tile.lower())
req('picker_tile_is_taller_media_tile', 'Height = 190f' in tile and 'ArtMaxSize = 146f' in tile)
req('picker_uses_shared_anchored_tooltip', 'AnchoredTooltipHost' in tile and 'ShowStructuredText' in tile and 'ShowText' in tile and 'tooltipHost' in page)
req('tooltip_reuses_predictor_runtime_condition_knowledge', 'Beta111EventPresentationKnowledge.RuntimeConditionLocalizationKey' in picker and 'RuntimeConditionLocalizationKey' in knowledge)
req('tooltip_only_shows_condition_when_present', 'string.IsNullOrWhiteSpace(conditionText)' in tile and 'tooltipHost.ShowText(this, eventName)' in tile and 'conditionsTitle' in tile)
req('event_options_hidden_for_now', 'SearchEventTooltipOptionsTitle' not in picker and 'SearchEventTooltipAuditPending' not in picker and 'optionsTitle' not in tile and 'auditPending' not in tile)
req('search_picker_uses_singleplayer_predictor_condition_policy', '_catalog.ProfileId' in page and '_catalog.ProfileId,\n            1,\n            candidates' in page)
req('thumbnail_crop_policy_exact', 'PickerCropLeft = 0.15f' in thumb and 'PickerCropRight = 0.50f' in thumb and 'PickerCropTop = 0.15f' in thumb and 'PickerCropBottom = 0.15f' in thumb)
req('thumbnail_crop_is_region_based_not_asset_copy', 'AtlasTexture' in thumb and 'Region = new Rect2' in thumb and 'SavePng' not in thumb)
req('crop_only_applies_to_default_portraits', 'descriptor.Kind == EventThumbnailKind.DefaultPortrait' in thumb and 'PickerSquareCrop' in tile and 'ConditionSquareCrop' in card)
req('condition_card_reuses_subject_crop_without_growing_icon', 'new Vector2(66f, 48f)' in card and 'EventThumbnailPresentation.ConditionSquareCrop' in card)
req('picker_dialog_bounds_grid_with_internal_scroll', 'new Vector2(760f, 560f)' in picker and 'new Vector2(0f, 300f)' in picker and 'ClipContents = true' in picker and 'VerticalScrollMode = ScrollContainer.ScrollMode.Auto' in picker)
req('event_page_semantics_still_commit_existing_candidate', 'CommitCandidate(candidate)' in page and 'SearchSequenceRangeMode.FirstN' in page)
req('event_picker_reuses_existing_global_tooltip_host', 'EventPage = new EventSequenceFilterPage(' in host and 'tooltipHost)' in host)
req('shared_tooltip_contract_unchanged', 'ShowStructuredText' in tooltip and 'ShowText' in tooltip)

for lang in ['en.json','zh.json']:
    d=json.loads(read('Localization/rt2_ui/'+lang))
    req(f'localized:{lang}:conditions_title', 'ui1.search.event.tooltip.conditions_title' in d and bool(d['ui1.search.event.tooltip.conditions_title']))
    runtime_keys=[k for k in d if k.startswith('ui1.analysis.event_runtime_condition.')]
    req(f'localized:{lang}:predictor_runtime_conditions', len(runtime_keys) >= 30)

print('SearchEventPickerVisualStyleValidation=PASS')
