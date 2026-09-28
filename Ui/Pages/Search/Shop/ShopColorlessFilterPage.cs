using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Tooltips;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Shop;

internal sealed partial class ShopColorlessFilterPage : MarginContainer
{
    private sealed partial class SequenceCard : PanelContainer
    {
        private readonly OptionButton _count;
        private readonly Label _title;
        private readonly Label _countPrefix;
        private readonly CheckButton _ordered;
        private readonly Label _countUnit;
        private readonly Button _clear;
        private readonly SearchHorizontalResultSlot[] _slots;
        private readonly HBoxContainer[] _slotGroups;
        private readonly Label[] _slotOrdinals;
        private readonly int _maximum;
        private readonly Func<bool> _authorityAvailable;
        private bool _refreshing;
        private bool _running;

        public SequenceCard(int maximum, bool cards, IGameIconResolver icons, Action<RelicPickerRequest> openPicker, Func<bool> authorityAvailable, Action changed)
        {
            _maximum = maximum;
            _authorityAvailable = authorityAvailable;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, 12f);
            var root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            root.AddThemeConstantOverride("separation", 8);
            var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
            _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            header.AddChild(_title);
            var countGroup = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
            countGroup.AddThemeConstantOverride("separation", 5);
            _countPrefix = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
            _countUnit = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
            _count = new OptionButton { CustomMinimumSize = new Vector2(58, 32) };
            Ui1Theme.ApplyOptionButton(_count);
            for (int i = 1; i <= maximum; i++) _count.AddItem(i.ToString(), i);
            _count.ItemSelected += _ => { if (!_refreshing) { RefreshState(); changed(); } };
            countGroup.AddChild(_countPrefix); countGroup.AddChild(_count); countGroup.AddChild(_countUnit);
            header.AddChild(countGroup);
            _ordered = new CheckButton { ButtonPressed = false };
            _ordered.Toggled += _ => { if (!_refreshing) changed(); };
            header.AddChild(_ordered);
            _clear = new Button { CustomMinimumSize = new Vector2(80, 32) };
            Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Ghost);
            _clear.Pressed += () => { Clear(false); changed(); };
            header.AddChild(_clear);
            root.AddChild(header);
            var rowScroll = new ScrollContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                VerticalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 8);
            _slots = new SearchHorizontalResultSlot[maximum];
            _slotGroups = new HBoxContainer[maximum];
            _slotOrdinals = new Label[maximum];
            for (int i = 0; i < maximum; i++)
            {
                SearchHorizontalResultSlot slot = cards
                    ? new SearchCardResultSlot(icons, openPicker)
                    : new NeowModelKeySlot(icons, openPicker);
                slot.UseExpandedWidth(132f);
                slot.Changed += () => { RefreshState(); changed(); };
                var group = new HBoxContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsStretchRatio = 1f,
                    SizeFlagsVertical = SizeFlags.ShrinkBegin
                };
                group.AddThemeConstantOverride("separation", 5);
                var ordinal = Ui1Theme.Label((i + 1).ToString(), Ui1TextRole.Accent);
                ordinal.CustomMinimumSize = new Vector2(16f, 0f);
                ordinal.HorizontalAlignment = HorizontalAlignment.Center;
                ordinal.VerticalAlignment = VerticalAlignment.Center;
                group.AddChild(ordinal); group.AddChild(slot); row.AddChild(group);
                _slots[i] = slot;
                _slotGroups[i] = group;
                _slotOrdinals[i] = ordinal;
            }
            rowScroll.AddChild(row);
            root.AddChild(rowScroll);
            AddChild(root);
            SelectCount(3);
            RefreshState();
        }

        public int Count => _count.Selected >= 0 ? _count.GetItemId(_count.Selected) : 1;
        public CombatRewardSequenceOrderMode OrderMode => _ordered.ButtonPressed ? CombatRewardSequenceOrderMode.Ordered : CombatRewardSequenceOrderMode.Unordered;
        public IReadOnlyList<ModelKey?> Slots => _slots.Select(slot => slot.SelectedKey).ToArray();
        public int EnabledCount => _slots.Take(Count).Count(slot => slot.SelectedKey.HasValue);
        public IReadOnlyList<SearchHorizontalResultSlot> SlotControls => _slots;

        public void ApplyText(string title, string countPrefix, string countUnit, string ordered, string clear) { _title.Text = title; _countPrefix.Text = countPrefix; _countUnit.Text = countUnit; _ordered.Text = ordered; _clear.Text = clear; }
        public void SetRunning(bool running)
        {
            _running = running;
            RefreshState();
        }
        public void Restore(int count, CombatRewardSequenceOrderMode order, IReadOnlyList<ModelKey?> slots)
        {
            _refreshing = true; SelectCount(Math.Clamp(count, 1, _maximum)); _ordered.ButtonPressed = order == CombatRewardSequenceOrderMode.Ordered;
            for (int i = 0; i < _slots.Length; i++) _slots[i].Select(i < slots.Count ? slots[i] : null, notify: false);
            _refreshing = false; RefreshState();
        }
        public void Clear(bool notify)
        {
            bool changed = NeedsReset;
            _refreshing = true; foreach (SearchHorizontalResultSlot slot in _slots) slot.Select(null, notify: false);
            SelectCount(3); _ordered.ButtonPressed = false; _refreshing = false; RefreshState();
            if (notify && changed) Changed?.Invoke();
        }
        public event Action? Changed;
        private void SelectCount(int count) { int index = _count.GetItemIndex(count); if (index >= 0) _count.Select(index); }
        public bool NeedsReset => EnabledCount > 0 || Count != 3 || OrderMode != CombatRewardSequenceOrderMode.Unordered;

        public void RefreshState()
        {
            _refreshing = true;
            SelectCount(Math.Clamp(Count, 1, _maximum));
            _ordered.ButtonPressed = OrderMode == CombatRewardSequenceOrderMode.Ordered;
            _refreshing = false;
            bool available = _authorityAvailable();
            bool ordered = OrderMode == CombatRewardSequenceOrderMode.Ordered;
            _count.Disabled = _running || !available;
            _ordered.Disabled = _running || !available;
            _clear.Disabled = _running || !NeedsReset;
            for (int i = 0; i < _slots.Length; i++)
            {
                bool active = i < Count;
                _slotGroups[i].Modulate = active && available ? Colors.White : new Color(1f, 1f, 1f, 0.42f);
                _slotOrdinals[i].Visible = ordered;
                _slots[i].SetEnabled(!_running && available && active);
            }
        }
    }

    private readonly RelicPickerPanel _picker;
    private readonly Label _title;
    private readonly Label _helper;
    private readonly Label _authorityNotice;
    private readonly Button _clear;
    private readonly SequenceCard _relics;
    private readonly SequenceCard _uncommon;
    private readonly SequenceCard _rare;
    private RelicSequenceSearchUiCatalog _relicCatalog = RelicSequenceSearchUiCatalog.Empty(RuntimeProfileId.Unsupported, "not-bound");
    private ShopColorlessSearchUiCatalog _catalog = ShopColorlessSearchUiCatalog.Empty(RuntimeProfileId.Unsupported, "not-bound");
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private bool _running;
    private IReadOnlyList<MerchantColorlessSlotCondition> _legacyColorless = Array.Empty<MerchantColorlessSlotCondition>();
    private IReadOnlyList<RelicSequenceSearchCondition> _legacyRelics = Array.Empty<RelicSequenceSearchCondition>();

    public ShopColorlessFilterPage(IGameIconResolver icons, ICharacterPoolIconProvider characterPoolIcons, ICardPickerFilterIconProvider cardPickerFilterIcons, AnchoredTooltipHost tooltipHost)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("margin_left", 2); AddThemeConstantOverride("margin_top", 2); AddThemeConstantOverride("margin_right", 2); AddThemeConstantOverride("margin_bottom", 2);
        _picker = new RelicPickerPanel(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost);
        var root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 8);
        var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle); _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _clear = new Button { CustomMinimumSize = new Vector2(132, 34) }; Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Ghost); _clear.Pressed += ClearDraft;
        header.AddChild(_title); header.AddChild(_clear); root.AddChild(header);
        _helper = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true); root.AddChild(_helper);
        _authorityNotice = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true); _authorityNotice.Visible = false; root.AddChild(_authorityNotice);
        _relics = new SequenceCard(5, false, icons, _picker.Open, () => _relicCatalog.CatalogAvailable, HandleUserChanged);
        _uncommon = new SequenceCard(5, true, icons, _picker.Open, () => _catalog.UncommonCandidates.Count > 0, HandleUserChanged);
        _rare = new SequenceCard(5, true, icons, _picker.Open, () => _catalog.RareCandidates.Count > 0, HandleUserChanged);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var cards = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("separation", 8);
        cards.AddChild(_relics); cards.AddChild(_uncommon); cards.AddChild(_rare);
        scroll.AddChild(cards); root.AddChild(scroll); AddChild(root);
        AddChild(_picker); _picker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        VisibilityChanged += () => { if (!IsVisibleInTree() && _picker.IsOpen) _picker.Cancel(); };
        TreeExiting += () => { if (_picker.IsOpen) _picker.Cancel(); };
    }

    public event Action? Changed;
    public int EnabledConditionCount => _relics.EnabledCount + _uncommon.EnabledCount + _rare.EnabledCount + _legacyColorless.Count + _legacyRelics.Count;
    public bool TryCancelTransientSurface() { if (!_picker.IsOpen) return false; _picker.Cancel(); return true; }

    public IReadOnlyList<MerchantColorlessSequenceSearchCondition> BuildSearchConditions() => _legacyColorless.Count > 0 ? Array.Empty<MerchantColorlessSequenceSearchCondition>() : new[]
    {
        new MerchantColorlessSequenceSearchCondition(_uncommon.Count, _uncommon.OrderMode, MerchantColorlessSlot.Uncommon, _uncommon.Slots),
        new MerchantColorlessSequenceSearchCondition(_rare.Count, _rare.OrderMode, MerchantColorlessSlot.Rare, _rare.Slots)
    }.Where(item => item.Slots.Any(key => key.HasValue)).ToArray();

    public IReadOnlyList<RelicShopSequenceSearchCondition> BuildRelicSearchConditions() => _legacyRelics.Count > 0 ? Array.Empty<RelicShopSequenceSearchCondition>() : !_relics.Slots.Any(key => key.HasValue)
        ? Array.Empty<RelicShopSequenceSearchCondition>()
        : new[] { new RelicShopSequenceSearchCondition(_relics.Count, _relics.OrderMode, _relics.Slots) };

    public IReadOnlyList<MerchantColorlessSlotCondition> BuildLegacyColorlessConditions() => _legacyColorless;
    public IReadOnlyList<RelicSequenceSearchCondition> BuildLegacyRelicConditions() => _legacyRelics;

    public void RestoreDraft(IReadOnlyList<MerchantColorlessSequenceSearchCondition>? conditions, IReadOnlyList<RelicShopSequenceSearchCondition>? relicConditions, bool notify)
    {
        MerchantColorlessSequenceSearchCondition? u = conditions?.FirstOrDefault(item => item.Slot == MerchantColorlessSlot.Uncommon);
        MerchantColorlessSequenceSearchCondition? r = conditions?.FirstOrDefault(item => item.Slot == MerchantColorlessSlot.Rare);
        _uncommon.Restore(u?.Count ?? 3, u?.OrderMode ?? CombatRewardSequenceOrderMode.Unordered, u?.Slots ?? Array.Empty<ModelKey?>());
        _rare.Restore(r?.Count ?? 3, r?.OrderMode ?? CombatRewardSequenceOrderMode.Unordered, r?.Slots ?? Array.Empty<ModelKey?>());
        RelicShopSequenceSearchCondition? relic = relicConditions?.FirstOrDefault();
        _relics.Restore(relic?.Count ?? 3, relic?.OrderMode ?? CombatRewardSequenceOrderMode.Unordered, relic?.Slots ?? Array.Empty<ModelKey?>());
        if (notify) Changed?.Invoke();
    }

    public void RestoreLegacyColorless(IReadOnlyList<MerchantColorlessSlotCondition>? conditions, bool notify)
    {
        _legacyColorless = (conditions ?? Array.Empty<MerchantColorlessSlotCondition>()).Where(item => item.IsValid).ToArray();
        foreach (MerchantColorlessSlot slot in Enum.GetValues<MerchantColorlessSlot>())
        {
            MerchantColorlessSlotCondition[] values = (conditions ?? Array.Empty<MerchantColorlessSlotCondition>()).Where(item => item.Slot == slot && item.IsValid).OrderBy(item => item.MerchantOrdinal).ToArray();
            (slot == MerchantColorlessSlot.Uncommon ? _uncommon : _rare).Restore(Math.Max(1, values.Length), CombatRewardSequenceOrderMode.Unordered, values.Select(item => (ModelKey?)item.TargetCardKey).ToArray());
        }
        if (notify) Changed?.Invoke();
    }

    public void RestoreLegacyRelics(IReadOnlyList<RelicSequenceSearchCondition>? conditions, bool notify)
    {
        RelicSequenceSearchCondition[] shop = (conditions ?? Array.Empty<RelicSequenceSearchCondition>())
            .Where(item => item.Lane == RelicSequenceKind.Shop && !item.IsEmpty)
            .ToArray();
        if (shop.Length == 0) return;
        _legacyRelics = shop;
        RelicSequenceSearchCondition first = shop[0];
        ModelKey[] keys = (first.Keys.Any ?? Array.Empty<ModelKey>())
            .Concat(first.Keys.All ?? Array.Empty<ModelKey>())
            .Concat(first.Keys.Ban ?? Array.Empty<ModelKey>())
            .Distinct(ModelKeyComparer.Instance)
            .Take(5)
            .ToArray();
        _relics.Restore(Math.Clamp(first.RangeValue, 1, 5), CombatRewardSequenceOrderMode.Unordered, keys.Select(key => (ModelKey?)key).ToArray());
        if (notify) Changed?.Invoke();
    }

    public void ClearDraft() { if (_running) return; bool had = EnabledConditionCount > 0 || _legacyColorless.Count > 0 || _legacyRelics.Count > 0 || _relics.NeedsReset || _uncommon.NeedsReset || _rare.NeedsReset; _legacyColorless = Array.Empty<MerchantColorlessSlotCondition>(); _legacyRelics = Array.Empty<RelicSequenceSearchCondition>(); _relics.Clear(false); _uncommon.Clear(false); _rare.Clear(false); if (had) Changed?.Invoke(); }
    public void SetRunning(bool running) { _running = running; _relics.SetRunning(running); _uncommon.SetRunning(running); _rare.SetRunning(running); RefreshVisuals(); if (running && _picker.IsOpen) _picker.Cancel(); }
    public void BindCatalog(ShopColorlessSearchUiCatalog catalog) { _catalog = catalog; ConfigureSlots(); RefreshVisuals(); }
    public void BindRelicCatalog(RelicSequenceSearchUiCatalog catalog) { _relicCatalog = catalog; ConfigureSlots(); RefreshVisuals(); }

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _text = text; _names = names; _title.Text = text.Get(Ui1TextKey.SearchShopColorlessTitle); _helper.Text = text.Get(Ui1TextKey.SearchShopColorlessHelper); _clear.Text = text.Get(Ui1TextKey.SearchShopClearAllConditions);
        _relics.ApplyText(text.Get(Ui1TextKey.SearchShopRelics), text.Get(Ui1TextKey.SearchShopCountPrefix), text.Get(Ui1TextKey.SearchShopCountUnit), text.Get(Ui1TextKey.SearchShopOrdered), text.Get(Ui1TextKey.SearchShopClearConditions)); _uncommon.ApplyText(text.Get(Ui1TextKey.SearchShopUncommon), text.Get(Ui1TextKey.SearchShopCountPrefix), text.Get(Ui1TextKey.SearchShopCountUnit), text.Get(Ui1TextKey.SearchShopOrdered), text.Get(Ui1TextKey.SearchShopClearConditions)); _rare.ApplyText(text.Get(Ui1TextKey.SearchShopRare), text.Get(Ui1TextKey.SearchShopCountPrefix), text.Get(Ui1TextKey.SearchShopCountUnit), text.Get(Ui1TextKey.SearchShopOrdered), text.Get(Ui1TextKey.SearchShopClearConditions));
        _picker.ApplyLocalization(text, names); ConfigureSlots(); RefreshVisuals();
    }

    private void ConfigureSlots()
    {
        if (_text is null || _names is null) return;
        foreach (SearchHorizontalResultSlot slot in _relics.SlotControls) slot.Configure(string.Empty, _relicCatalog.CandidatesFor(RelicSequenceKind.Shop), GameContentKind.Relic, IconVariant.Small, _names, _text.Get(Ui1TextKey.SearchShopNeutralSlot), _text.Get(Ui1TextKey.SearchShopCanonicalTooltip), _relicCatalog.Categories, _text.Get(Ui1TextKey.SearchRelicSelectRelic));
        RefreshRelicCandidateExclusions();
        ConfigureCards(_uncommon, _catalog.UncommonCandidates, "uncommon"); ConfigureCards(_rare, _catalog.RareCandidates, "rare");
    }
    private void ConfigureCards(SequenceCard card, IReadOnlyList<ModelKey> candidates, string source)
    {
        foreach (SearchHorizontalResultSlot slot in card.SlotControls) slot.Configure(string.Empty, candidates, GameContentKind.Card, IconVariant.CardPickerLarge, _names!, _text!.Get(Ui1TextKey.SearchShopNeutralSlot), _text.Get(Ui1TextKey.SearchShopCanonicalTooltip), _catalog.Categories, _text.Get(Ui1TextKey.SearchShopPickerTitle), _catalog.CreateCardPickerContext(candidates, "shop-colorless:" + source));
    }
    private void RefreshVisuals() { if (_text is null) return; _authorityNotice.Text = _text.Get(Ui1TextKey.SearchShopColorlessAuthorityUnavailable); _authorityNotice.Visible = !_catalog.CatalogAvailable || !_relicCatalog.CatalogAvailable; _clear.Disabled = _running || !(EnabledConditionCount > 0 || _relics.NeedsReset || _uncommon.NeedsReset || _rare.NeedsReset); _relics.RefreshState(); _uncommon.RefreshState(); _rare.RefreshState(); RefreshRelicCandidateExclusions(); }
    private void RefreshRelicCandidateExclusions() { for (int i = 0; i < _relics.SlotControls.Count; i++) _relics.SlotControls[i].SetDisabledKeys(_relics.SlotControls.Where((_, index) => index != i).Select(slot => slot.SelectedKey).Where(key => key.HasValue).Select(key => key!.Value)); }
    private void HandleUserChanged() { if (_running) return; _legacyColorless = Array.Empty<MerchantColorlessSlotCondition>(); _legacyRelics = Array.Empty<RelicSequenceSearchCondition>(); RefreshVisuals(); Changed?.Invoke(); }
}
