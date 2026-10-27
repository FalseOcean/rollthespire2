#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding='utf-8')


def sha(path: str) -> str:
    return hashlib.sha256((ROOT / path).read_bytes()).hexdigest()


def require(value: bool, code: str, message: str) -> None:
    if not value:
        raise AssertionError(f'{code}: {message}')
    print(f'PASS: {code}')


en = json.loads(read('Localization/rt2_ui/en.json'))
zh = json.loads(read('Localization/rt2_ui/zh.json'))
metrics = read('Ui/Theme/Ui1Theme.cs')
nav = read('Ui/Shell/NavigationRail.cs')
tabs = read('Ui/Pages/Search/SearchCategoryNavigationBar.cs')
neow = read('Ui/Pages/Search/Neow/NeowRouteEffectCardHost.cs')
ancient_page = read('Ui/Pages/Search/Ancient/AncientFilterPage.cs')
ancient_section = read('Ui/Pages/Search/Ancient/AncientActSection.cs')
ancient_card = read('Ui/Pages/Search/Ancient/AncientConditionRow.cs')
ancient_geometry = read('Ui/Pages/Search/Ancient/AncientRowGeometry.cs')
boss_page = read('Ui/Pages/Search/BossMap/BossMapFilterPage.cs')
boss_row = read('Ui/Pages/Search/BossMap/BossMapVariantEditor.cs')
boss_geometry = read('Ui/Pages/Search/BossMap/BossMapRowGeometry.cs')

for key, value in {
    'ui1.nav.search': 'Search',
    'ui1.nav.analysis': 'Analysis',
    'ui1.nav.events': 'Events',
    'ui1.nav.save_status': 'Saves',
}.items():
    require(en.get(key) == value, f'nav_copy:{key}', f'Unexpected English navigation copy for {key}.')
require(en['ui1.search.title'] == 'Search' and en['ui1.analysis.title'] == 'Analysis',
        'page_titles_short', 'Search/Analysis page titles still duplicate Seed terminology.')
require(en['ui1.search.tab.boss_and_map'] == 'Act Info' and
        en['ui1.search.tab.combat_reward'] == 'Rewards',
        'search_tab_copy_en', 'English Search category copy is not the frozen compact wording.')
require(zh['ui1.search.tab.boss_and_map'] == '幕信息',
        'boss_variant_copy_zh', 'Chinese Act Info category does not use the current compact wording.')
require('NavigationWidth = 124f' in metrics and 'NavigationWidth = 144f' not in metrics,
        'sidebar_width_budget', 'Expanded sidebar width was not reduced after shortening navigation copy.')
require('AddTab(tabs, SearchCategoryKey.BossAndMap);' in tabs and
        'AddTab(tabs, SearchCategoryKey.CombatReward);' in tabs and
        'RefreshContentWidth();' in tabs and 'float desired = 28f + iconWidth + iconGap + textWidth;' in tabs and 'Mathf.Max(80f' in tabs,
        'tab_content_sized_width', 'Search category tabs are still using category-specific fixed widths.')
require('AutowrapMode = TextServer.AutowrapMode.Off' in tabs and
        '_label.ClipText = false' in tabs and 'ScrollContainer' in tabs,
        'tab_full_label_guard', 'Ordinary localized tab labels can still be clipped instead of receiving content width.')
require('string relicName =' in neow and 'title.AutowrapMode = TextServer.AutowrapMode.Off' in neow and
        'title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis' in neow,
        'neow_title_word_break_guard', 'Neow card titles can still split a localized word across lines.')

require('public const int StandardOptionColumns = 5' in ancient_geometry and
        'DarvOptionColumns' not in ancient_geometry and
        'Math.Min(AncientRowGeometry.StandardOptionColumns, definition.Options.Count)' in ancient_card,
        'ancient_fixed_option_grids', 'Ancient option layout is not bounded to five columns (10->5x2; Act2 Darv 12->5+5+2).')
require('"PAEL" => 0' in ancient_section and '"TEZCATARA" => 1' in ancient_section and
        '"OROBAS" => 2' in ancient_section and '"DARV" => 3' in ancient_section,
        'ancient_act2_card_order', 'Act 2 does not place Orobas and Darv together on the bottom row.')
require('var optionsViewport = new Control' in ancient_card and
        'CustomMinimumSize = new Vector2(0, optionHeight)' in ancient_card and
        'ClipContents = true' in ancient_card,
        'ancient_option_width_isolation', 'Option grid minimum width can still expand the parent Ancient card.')
require('Key.F6' not in ancient_page and 'Ancient layout prototype: DarvColumns=' not in ancient_page,
        'ancient_prototype_toggle_removed', 'Darv prototype A/B toggle remains in the finalized UI.')
require(en['ui1.search.ancient.sea_glass_target_any'] == 'Sea Glass' and
        zh['ui1.search.ancient.sea_glass_target_any'] == '海玻璃',
        'sea_glass_short_label', 'Sea Glass nested label is still unnecessarily verbose.')
ancient_eligibility = read('Ui/Pages/Search/Ancient/AncientOptionConditionEditor.cs')
require('header.AddChild(_help);' in ancient_eligibility and 'header.AddChild(_reset);' in ancient_eligibility and
        'resetRow' not in ancient_eligibility,
        'ancient_reset_in_header', 'Eligibility Reset is not aligned with the foldout/help header row.')

require(en['ui1.search.tab.boss_and_map'] == 'Act Info' and
        'Bosses & Map' not in en.values() and '首领与地图' not in zh.values(),
        'boss_variant_terminology_clean', 'Legacy Bosses & Map player terminology remains in localization.')
require(en['ui1.search.boss_map.page_hint'] == 'Select Act variants and bosses' and
        zh['ui1.search.boss_map.page_hint'] == '选择地图变体与首领' and
        '_hint.Visible = true' in boss_page and 'SearchBossMapPageHint' in boss_page,
        'boss_concise_hint', 'Boss page is missing the concise player-facing variant/Boss guidance.')
require('SearchBossMapFixedVariant' in boss_row and 'SearchBossMapBoss' in boss_row,
        'boss_regular_row_layout_restored', 'Bosses & Variants did not restore the previous aligned row matrix.')
require('SearchBossMapFirstBoss' in boss_row and 'SearchBossMapSecondBoss' in boss_row,
        'boss_act3_order_labels_preserved', 'Act 3 first/second Boss order labels were removed.')
require('BossCellWidth = 142f' in boss_geometry and 'OrdinalLabelWidth = 88f' in boss_geometry,
        'boss_width_budget', 'Boss row cell geometry was not bounded for localization-safe main-content width.')
require('CustomMinimumSize = new Vector2(112, 34)' in boss_page,
        'boss_clear_button_compact', 'Boss page clear action still reserves the old oversized width.')
require('SizeFlagsHorizontal = Control.SizeFlags.ExpandFill' in boss_page and
        'header.AddChild(_clear);' in boss_page and 'MouseFilter = Control.MouseFilterEnum.Ignore' in boss_page,
        'boss_clear_top_right', 'Boss clear action is not pushed to the stable top-right edge by an elastic spacer.')
require('_tooltipHost.ShowStructuredText(' in ancient_eligibility and
        '_help.MouseEntered += ShowHelpTooltip' in ancient_eligibility and
        'button.MouseEntered += () => ShowConditionTooltip(button)' in ancient_eligibility,
        'ancient_eligibility_advanced_tooltip', 'Ancient eligibility still uses native/plain tooltips instead of AnchoredTooltipHost.')

frozen = {
    'Ui/Pages/Search/SearchDraft.cs': '4d3516dacfeb17cad50368d102c430ee20db90b14946cfbfb767cc1de47b27a3',
    'Search/Contracts/SearchContracts.cs': 'ccbde41390ed27bcf4f3b21add2f04fc4e36db27df7853d4ff82e6affdf52457',
    'Search/Semantics/SearchCompiler.cs': '2d245bec2e576b244dfbde6146886e02aafcb09d1990bb612986a856c30adcd3',
    'Ui/Controllers/SearchPageController.cs': 'b3674d1fa7c1614165f4585c4fbba6764517f7f46562e838ac57769a7476e3d5',
    'Ui/Persistence/SearchWorkspacePersistence.cs': '6b1b02dc951c89230c52f78180e88599ba00022f7b5be4f4066d7ec4e98a6ddd',
}
for path, expected in frozen.items():
    require(sha(path) == expected, 'semantic_baseline:' + path, f'Canonical Search file changed: {path}')

print('SearcherLocalizationSafeLayoutValidation=PASS')
