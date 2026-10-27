#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]

def read(rel):
    return (ROOT / rel).read_text(encoding='utf-8')

def req(name, ok):
    if not ok:
        print('FAIL:', name)
        sys.exit(1)
    print('PASS:', name)

picker = read('Ui/Pages/Search/Event/EventSearchPickerPanel.cs')
event_tile = read('Ui/Pages/Search/Event/EventPickerTile.cs')
event_page = read('Ui/Pages/Search/Event/EventSequenceFilterPage.cs')
predictor_card = read('Ui/Components/AnalysisActPredictionCard.cs')
knowledge = read('Presentation/Ui1/Beta111EventPresentationKnowledge.cs')
card_tile = read('Ui/Controls/Pickers/RelicPickerTile.cs')

req('event_picker_reuses_predictor_condition_catalog',
    'Beta111EventPresentationKnowledge.RuntimeConditionLocalizationKey' in picker and
    'Beta111EventPresentationKnowledge.RuntimeConditionLocalizationKey' in predictor_card)
req('event_picker_passes_profile_and_singleplayer_context',
    '_catalog.ProfileId' in event_page and '_catalog.ProfileId,\n            1,\n            candidates' in event_page and
    'RuntimeProfileId profileId' in picker and 'int playersCount' in picker)
req('conditionless_event_tooltip_is_title_only',
    'string.IsNullOrWhiteSpace(conditionText)' in event_tile and
    'tooltipHost.ShowText(this, eventName)' in event_tile)
req('conditional_event_tooltip_has_conditions_section',
    'tooltipHost.ShowStructuredText' in event_tile and 'conditionsTitle' in event_tile and 'conditionText' in event_tile)
req('event_option_section_hidden',
    'optionsTitle' not in event_tile and 'auditPending' not in event_tile and
    'SearchEventTooltipOptionsTitle' not in picker and 'SearchEventTooltipAuditPending' not in picker)
req('predictor_condition_policy_remains_presentation_only',
    'not Search/Probability/Planner authority' in knowledge)

build_card = card_tile[card_tile.index('private void BuildCard('):card_tile.index('private void BuildFallback(')]
name_decl = build_card.index('var name = Ui1Theme.Label(candidate.LocalizedName')
name_add = build_card.index('content.AddChild(name);')
art_decl = build_card.index('var artCenter = new CenterContainer')
art_add = build_card.index('content.AddChild(artCenter);')
req('card_name_is_added_before_art', name_decl < name_add < art_decl < art_add)
req('card_art_geometry_unchanged', 'CardArtHeight = 106f' in card_tile and 'CardArtMaxWidth = 146f' in card_tile)
req('card_selection_and_tooltip_unchanged',
    'tooltipHost.ShowCardFor' in build_card and 'AddSelectedBadge(candidate.IsSelected)' in build_card)

print('EventPickerPredictorConditionsCardNameTop20260821=PASS')
