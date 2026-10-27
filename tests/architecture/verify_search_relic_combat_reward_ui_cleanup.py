#!/usr/bin/env python3
from pathlib import Path
import hashlib, json, sys

ROOT = Path(__file__).resolve().parents[2]
checks=[]
def text(p): return (ROOT/p).read_text(encoding='utf-8')
def ck(name, cond):
    checks.append((name, bool(cond)))
    print(('PASS' if cond else 'FAIL')+': '+name)

def sha(p): return hashlib.sha256((ROOT/p).read_bytes()).hexdigest()

relic=text('Ui/Pages/Search/Relic/RelicSequenceFilterPage.cs')
strip=text('Ui/Controls/SequenceConditionControlStrip.cs')
combat=text('Ui/Pages/Search/CombatReward/CombatRewardFilterPage.cs')
cards=text('Ui/Pages/Search/CombatReward/CombatRewardCardSequencePanel.cs')
potions=text('Ui/Pages/Search/CombatReward/CombatRewardPotionSequencePanel.cs')
draft=text('Ui/Pages/Search/CombatReward/CombatRewardSearchDraft.cs')

# Relic authoring: rule first, relic selection last, immediate commit.
ck('relic_add_button_removed', 'private readonly Button _add;' not in relic and '_add.Pressed' not in relic and 'SearchRelicAddCondition' not in relic)
ck('relic_sequence_outside_picker_removed', '_laneLabel' not in relic and '_relicSlot' not in relic)
ck('relic_choose_last_commits', 'CommitCondition' in relic and 'CommitCondition));' in relic and 'Changed?.Invoke();' in relic)
ck('relic_sequence_from_authority', '_catalog.TryResolveLane(selectedKey.Value' in relic and '_catalog.Categories' in relic)
ck('relic_authoring_context_preserved_after_commit', 'CommitCondition(ModelKey? selectedKey)' in relic and '_sequenceControls.ResetDefaults' not in relic[relic.index('private void CommitCondition'):relic.index('private void RemoveCondition')])
ck('relic_prefix_exact_both_retained', 'SearchSequenceRangeMode.FirstN' in strip and 'SearchSequenceRangeMode.ExactSlot' in strip)
ck('relic_prefix_first_visual_order', 'CreateSegmentGroup(_first, _exact)' in strip)
ck('relic_include_exclude_retained', '_appears' in strip and '_excluded' in strip)
ck('relic_page_hint_present', 'SearchRelicPageHint' in relic and '_pageTitle' in relic)


# Combat layout and draft-preserving prototype.
ck('combat_vertical_stack', 'VBoxContainer _sequenceHost' in combat and '_sequenceHost.AddChild(_cards)' in combat and '_sequenceHost.AddChild(_potions)' in combat and 'GridContainer _sequenceGrid' not in combat)
ck('combat_global_clear_removed', 'private readonly Button _clear;' not in combat and 'SearchCombatRewardClearConditions' not in combat)
ck('combat_per_module_clear', 'Button _clear' in cards and 'Button _clear' in potions and 'SearchCombatRewardClearConditions' in cards and 'SearchCombatRewardClearConditions' in potions)
ck('combat_three_slots_single_row', 'var slotsRow = new HBoxContainer' in cards and 'var slotsRow = new HBoxContainer' in potions and '_slotGroups[i].Visible = true' in cards and '_slotGroups[i].Visible = true' in potions)
ck('combat_count_controls_slot_enablement', '_catalog.CardCatalogAvailable && active' in cards and '_catalog.PotionCatalogAvailable || !active' in potions)
ck('combat_card_visual_enlarged', 'slot.UseLargeVisual(slotHeight: 104f, iconSize: 76f);' in cards)
ck('combat_old_explanation_removed', 'SearchCombatRewardPageDescription' not in combat)
ck('combat_card_compact_count_selector', 'OptionButton _countSelector' in cards and 'for (int value = 1; value <= 3; value++)' in cards)
ck('combat_potion_compact_count_selector', 'OptionButton _countSelector' in potions and 'for (int value = 1; value <= 3; value++)' in potions)
ck('combat_potion_identity_width_tracks_mode_width', '_potionOrdinalSpacers' in potions and 'potionRow.AddChild(potionOrdinalSpacer);' in potions and 'potionRow.AddChild(potion);' in potions)
ck('combat_potion_ordered_gutter_alignment', '_potionOrdinalSpacers[i].Visible = ordered;' in potions and '_slotOrdinals[i].Visible = ordered;' in potions)
ck('combat_order_toggle_not_target_count_gated', 'CheckButton _orderedToggle' in cards and 'CheckButton _orderedToggle' in potions and 'selectedTargetCount' not in cards+potions)
ck('combat_card_sparse_positions_preserved_in_ui_draft', '_slots.Select(slot => slot.SelectedKey).ToArray()' in cards and 'Where(' not in cards[cards.index('public CombatRewardCardSequenceDraft CaptureDraft'):cards.index('public bool TryBuildDraft')])
ck('combat_card_ordinals_ui_only', all(x in cards for x in ['"①"','"②"','"③"']) and '_slotOrdinals[i].Visible' in cards)
ck('combat_potion_three_state_ui', 'PotionSlotUiMode.Unconstrained' in potions and 'PotionSlotUiMode.NoDrop' in potions and 'PotionSlotUiMode.Drop' in potions)
ck('combat_potion_nullable_draft_retained', 'CombatPotionSlotRequirement?[] _requirements' in potions and 'new CombatRewardPotionSlotDraft' in potions)
ck('combat_potion_identity_only_effective_when_drop_specific', '_requirements[index] == CombatPotionSlotRequirement.DropSpecific' in potions)
ck('combat_potion_ordinals_ui_only', all(x in potions for x in ['"①"','"②"','"③"']) and '_slotOrdinals[i].Visible' in potions)
ck('combat_sparse_semantic_gate_connected', 'SearchCombatRewardSequenceIncomplete' not in cards and 'SearchCombatRewardSequenceIncomplete' not in potions)
ck('combat_ui_draft_nullable_contract_already_present', 'IReadOnlyList<ModelKey?> Slots' in draft and 'CombatPotionSlotRequirement? Requirement' in draft)

# Localization coverage.
for lang in ['zh','en']:
    data=json.loads((ROOT/f'Localization/rt2_ui/{lang}.json').read_text(encoding='utf-8'))
    for key in ['ui1.search.combat_reward.count_prefix','ui1.search.combat_reward.count_unit','ui1.search.combat_reward.potion_unconstrained','ui1.search.combat_reward.ordered','ui1.search.combat_reward.clear_conditions','ui1.search.relic.page_hint','ui1.search.relic.builder_title','ui1.search.relic.select_relic']:
        ck(f'localized:{lang}:{key}', bool(data.get(key)))

baseline={
'Ui/Pages/Search/SearchDraft.cs':'4d3516dacfeb17cad50368d102c430ee20db90b14946cfbfb767cc1de47b27a3',
'Search/Contracts/SearchContracts.cs':'ccbde41390ed27bcf4f3b21add2f04fc4e36db27df7853d4ff82e6affdf52457',
'Search/Semantics/SearchCompiler.cs':'2d245bec2e576b244dfbde6146886e02aafcb09d1990bb612986a856c30adcd3',
'Ui/Controllers/SearchPageController.cs':'b3674d1fa7c1614165f4585c4fbba6764517f7f46562e838ac57769a7476e3d5',
'Ui/Persistence/SearchWorkspacePersistence.cs':'6b1b02dc951c89230c52f78180e88599ba00022f7b5be4f4066d7ec4e98a6ddd',
}
for p,h in baseline.items(): ck('canonical_unchanged:'+p, sha(p)==h)

failed=[n for n,o in checks if not o]
print(f'SearchRelicCombatRewardUiCleanupValidation={len(checks)-len(failed)}/{len(checks)}_PASS')
if failed:
    print('FAILED='+','.join(failed))
    sys.exit(1)
