from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(path):
    return (ROOT / path).read_text(encoding='utf-8')

def req(name, cond):
    if not cond:
        raise AssertionError(name)
    print(f'PASS: {name}')

catalog = read('Ui/Pages/Search/Neow/NeowSearchUiCatalog.cs')
host = read('Ui/Pages/Search/Neow/NeowRouteEffectCardHost.cs')
neow = read('Ui/Pages/Search/Neow/NeowFilterPage.cs')
picker = read('Ui/Controls/Pickers/RelicPickerPanel.cs')
relic = read('Ui/Pages/Search/Relic/RelicSequenceFilterPage.cs')
combat_page = read('Ui/Pages/Search/CombatReward/CombatRewardFilterPage.cs')
combat_cards = read('Ui/Pages/Search/CombatReward/CombatRewardCardSequencePanel.cs')
combat_potions = read('Ui/Pages/Search/CombatReward/CombatRewardPotionSequencePanel.cs')
tooltip = read('Ui/Tooltips/AnchoredTooltipHost.cs')

req('leafy_transform_picker_excludes_basic_and_ancient',
    'card.Rarity is not EffectCardRarity.Basic and not EffectCardRarity.Ancient' in catalog and
    'ModelKey[] transforms' in catalog)
req('neow_history_reconciles_all_nested_candidate_kinds',
    'SanitizeHistoryForCatalog' in host and
    'catalog.Candidates(definition.Components[i].CandidatePool)' in host and
    'KeepIfAllowed(scroll.Uncommon, catalog.UncommonCharacterCards)' in host and
    'KeepIfAllowed(scroll.CommonA, catalog.CommonCharacterCards)' in host and
    '!catalog.Curses.Contains(_bonesFinalCurse.Value' in host)
req('neow_character_change_reports_reconciled_state',
    'masterCleared || detailCleared' in neow and 'if (reconciled) Changed?.Invoke();' in neow)
req('relic_sequence_drops_new_character_illegal_relics',
    '_conditions.RemoveAll(condition =>' in relic and
    '!catalog.TryResolveLane(condition.RelicKey' in relic and
    'if (removed > 0) Changed?.Invoke();' in relic)
req('combat_card_character_change_clears_illegal_cards',
    'public bool BindCatalog(CombatRewardSearchUiCatalog catalog)' in combat_cards and
    '!catalog.CardCandidates.Contains(card' in combat_cards)
req('combat_potion_character_change_clears_illegal_potions',
    'public bool BindCatalog(CombatRewardSearchUiCatalog catalog)' in combat_potions and
    '!catalog.PotionCandidates.Contains(potion' in combat_potions and
    '_requirements[i] = CombatPotionSlotRequirement.DropAny;' in combat_potions)
req('combat_page_surfaces_reconciliation_change',
    'bool cardsCleared = _cards.BindCatalog(catalog);' in combat_page and
    'bool potionsCleared = _potions.BindCatalog(catalog);' in combat_page and
    'if (cardsCleared || potionsCleared) Changed?.Invoke();' in combat_page)

# Each Card Picker filter dimension is now single-or-none rather than additive OR.
req('card_picker_character_filter_single_select',
    'bool alreadySelected = !_allCharactersSelected' in picker and
    '_selectedCharacters.Clear();' in picker and
    '_selectedCharacters.Add(character);' in picker)
req('card_picker_rarity_filter_single_select',
    'bool alreadySelected = _selectedRarities.Count == 1' in picker and
    '_selectedRarities.Clear();' in picker and
    'if (!alreadySelected) _selectedRarities.Add(rarity);' in picker)
req('card_picker_type_filter_single_select',
    'bool alreadySelected = _selectedTypes.Count == 1' in picker and
    '_selectedTypes.Clear();' in picker and
    'if (!alreadySelected) _selectedTypes.Add(type);' in picker)

req('tooltip_first_show_is_hidden_during_initial_wrap_measurement',
    '_panel.Modulate = new Color(1f, 1f, 1f, 0f);' in tooltip and
    'PrepareFirstShowLayout' in tooltip and
    'PresentAfterFirstShowLayout' in tooltip)
req('tooltip_first_show_uses_deferred_layout_before_present',
    'Callable.From(() => PrepareFirstShowLayout(generation)).CallDeferred();' in tooltip and
    'Callable.From(() => PresentAfterFirstShowLayout(generation)).CallDeferred();' in tooltip and
    '_panel.Modulate = Colors.White;' in tooltip)
req('tooltip_pending_show_is_cancelled_on_dismiss',
    '_showGeneration++;' in tooltip and 'generation != _showGeneration' in tooltip)
req('tooltip_wrap_width_is_fixed_before_height_measurement',
    'ApplyStableWrapWidth(preferredWidth);' in tooltip and
    '_description.CustomMinimumSize = new Vector2(contentWidth, 0f);' in tooltip and
    'TooltipPanelMargin * 2f' in tooltip)
req('tooltip_first_show_does_not_reset_at_unconstrained_width',
    '_panel.ResetSize();' not in tooltip and
    '_panel.Size = new Vector2(_pendingPreferredWidth, 1f);' in tooltip)
req('tooltip_panel_cannot_inherit_full_rect_anchors',
    '_panel.AnchorRight = 0f;' in tooltip and '_panel.AnchorBottom = 0f;' in tooltip)

print('SearchAuthoringLegalityPickerTooltipCloseoutValidation=PASS')
