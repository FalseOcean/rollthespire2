using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.CombatReward;

/// <summary>
/// Player-facing Card Reward authoring card. The UI deliberately preserves three
/// positional draft slots even when only one slot currently has a target; canonical
/// sparse-slot execution support is adapted separately from this presentation batch.
/// </summary>
internal sealed partial class CombatRewardCardSequencePanel : PanelContainer
{
    private readonly Label _title;
    private readonly Label _countPrefix;
    private readonly OptionButton _countSelector;
    private readonly Label _countUnit;
    private readonly CheckButton _orderedToggle;
    private readonly Button _clear;
    private readonly HBoxContainer[] _slotGroups;
    private readonly Label[] _slotOrdinals;
    private readonly SearchCardResultSlot[] _slots;
    private CombatRewardSearchUiCatalog _catalog = CombatRewardSearchUiCatalog.Empty(RuntimeProfileId.Unsupported, "not-bound");
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private int _count = 1;
    private CombatRewardSequenceOrderMode _orderMode = CombatRewardSequenceOrderMode.Ordered;
    private bool _running;
    private bool _refreshingControls;

    public CombatRewardCardSequencePanel(
        IGameIconResolver icons,
        Action<RelicPickerRequest> openPicker)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, 12f);

        var root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 10);

        var header = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        header.AddThemeConstantOverride("separation", 10);
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(_title);

        var countGroup = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        countGroup.AddThemeConstantOverride("separation", 5);
        _countPrefix = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _countPrefix.VerticalAlignment = VerticalAlignment.Center;
        _countSelector = new OptionButton
        {
            CustomMinimumSize = new Vector2(62f, 34f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplyOptionButton(_countSelector);
        for (int value = 1; value <= 3; value++) _countSelector.AddItem(value.ToString(), value);
        _countSelector.ItemSelected += _ =>
        {
            if (_refreshingControls || _countSelector.Selected < 0) return;
            SetCount(_countSelector.GetItemId(_countSelector.Selected), notify: true);
        };
        _countUnit = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _countUnit.VerticalAlignment = VerticalAlignment.Center;
        countGroup.AddChild(_countPrefix);
        countGroup.AddChild(_countSelector);
        countGroup.AddChild(_countUnit);
        header.AddChild(countGroup);

        _orderedToggle = new CheckButton
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            ButtonPressed = true
        };
        _orderedToggle.Toggled += pressed =>
        {
            if (_refreshingControls) return;
            SetOrder(
                pressed ? CombatRewardSequenceOrderMode.Ordered : CombatRewardSequenceOrderMode.Unordered,
                notify: true);
        };
        header.AddChild(_orderedToggle);

        _clear = new Button { CustomMinimumSize = new Vector2(96f, 34f) };
        Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Ghost);
        _clear.Pressed += () => Clear(notify: true);
        header.AddChild(_clear);
        root.AddChild(header);

        var slotsRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        slotsRow.AddThemeConstantOverride("separation", 10);

        _slotGroups = new HBoxContainer[3];
        _slotOrdinals = new Label[3];
        _slots = new SearchCardResultSlot[3];
        for (int i = 0; i < 3; i++)
        {
            var group = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
                SizeFlagsStretchRatio = 1f
            };
            group.AddThemeConstantOverride("separation", 6);
            var ordinal = Ui1Theme.Label(i == 0 ? "①" : i == 1 ? "②" : "③", Ui1TextRole.Accent);
            ordinal.CustomMinimumSize = new Vector2(22f, 0f);
            ordinal.HorizontalAlignment = HorizontalAlignment.Center;
            ordinal.VerticalAlignment = VerticalAlignment.Center;
            var slot = new SearchCardResultSlot(icons, openPicker);
            slot.UseExpandedWidth();
            slot.UseLargeVisual(slotHeight: 104f, iconSize: 76f);
            slot.Changed += () => Changed?.Invoke();
            group.AddChild(ordinal);
            group.AddChild(slot);
            slotsRow.AddChild(group);
            _slotGroups[i] = group;
            _slotOrdinals[i] = ordinal;
            _slots[i] = slot;
        }
        root.AddChild(slotsRow);

        AddChild(root);
        RefreshState();
    }

    public event Action? Changed;

    public bool HasAnyValue => _slots.Take(_count).Any(slot => slot.SelectedKey.HasValue);
    public bool NeedsReset => _slots.Any(slot => slot.SelectedKey.HasValue) || _count != 3 || _orderMode != CombatRewardSequenceOrderMode.Unordered;

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _text = text;
        _names = names;
        _title.Text = text.Get(Ui1TextKey.SearchCombatRewardCardsTitle);
        _countPrefix.Text = text.Get(Ui1TextKey.SearchCombatRewardCountPrefix);
        _countUnit.Text = text.Get(Ui1TextKey.SearchCombatRewardCountUnit);
        _orderedToggle.Text = text.Get(Ui1TextKey.SearchCombatRewardOrdered);
        _clear.Text = text.Get(Ui1TextKey.SearchCombatRewardClearConditions);
        ConfigureSlots();
        RefreshState();
    }

    public bool BindCatalog(CombatRewardSearchUiCatalog catalog)
    {
        _catalog = catalog;
        bool changed = false;
        foreach (SearchCardResultSlot slot in _slots)
        {
            if (catalog.CardCatalogAvailable &&
                slot.SelectedKey is ModelKey card && !catalog.CardCandidates.Contains(card, ModelKeyComparer.Instance))
            {
                slot.Select(null, notify: false);
                changed = true;
            }
        }
        ConfigureSlots();
        RefreshState();
        return changed;
    }

    public void SetRunning(bool running)
    {
        _running = running;
        RefreshState();
    }

    public CombatRewardCardSequenceDraft CaptureDraft() => new(
        _count,
        _orderMode,
        _slots.Select(slot => slot.SelectedKey).ToArray());

    public bool TryBuildDraft(out CombatRewardCardSequenceDraft draft, out string issue, bool focusInvalid)
    {
        draft = CaptureDraft();
        issue = string.Empty;
        return true;
    }

    public void RestoreDraft(CombatRewardCardSequenceDraft? draft, bool notify)
    {
        CombatRewardCardSequenceDraft value = draft ?? CombatRewardSearchDraft.Empty.Cards;
        _count = Math.Clamp(value.Count, 1, 3);
        _orderMode = value.OrderMode;
        for (int i = 0; i < _slots.Length; i++)
        {
            ModelKey? key = i < value.Slots.Count ? value.Slots[i] : null;
            _slots[i].Select(key, notify: false);
        }
        RefreshState();
        if (notify) Changed?.Invoke();
    }

    public void Clear(bool notify)
    {
        bool changed = NeedsReset;
        foreach (SearchCardResultSlot slot in _slots) slot.Select(null, notify: false);
        _count = 3;
        _orderMode = CombatRewardSequenceOrderMode.Unordered;
        RefreshState();
        if (changed && notify) Changed?.Invoke();
    }

    private void SetCount(int count, bool notify)
    {
        if (_running || _count == count) return;
        _count = Math.Clamp(count, 1, 3);
        for (int i = _count; i < _slots.Length; i++) _slots[i].Select(null, notify: false);
        RefreshState();
        if (notify) Changed?.Invoke();
    }

    private void SetOrder(CombatRewardSequenceOrderMode mode, bool notify)
    {
        if (_running || _orderMode == mode) return;
        _orderMode = mode;
        RefreshState();
        if (notify) Changed?.Invoke();
    }

    private void ConfigureSlots()
    {
        if (_text is null || _names is null) return;
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i].Configure(
                string.Empty,
                _catalog.CardCandidates,
                GameContentKind.Card,
                IconVariant.CardPickerLarge,
                _names,
                _text.Get(Ui1TextKey.SearchCombatRewardSelectCard),
                _text.Get(Ui1TextKey.SearchCombatRewardSelectCard),
                _catalog.CardCategories,
                _text.Get(Ui1TextKey.SearchCombatRewardCardPickerTitle),
                _catalog.CreateCardPickerContext($"combat-reward:card-slot:{i + 1}"));
        }
    }

    private void RefreshState()
    {
        _refreshingControls = true;
        SelectCount(_count);
        _orderedToggle.ButtonPressed = _orderMode == CombatRewardSequenceOrderMode.Ordered;
        _refreshingControls = false;

        _countSelector.Disabled = _running;
        _orderedToggle.Disabled = _running;
        _clear.Disabled = _running || !NeedsReset;
        for (int i = 0; i < _slots.Length; i++)
        {
            bool active = i < _count;
            _slotGroups[i].Visible = true;
            _slotGroups[i].Modulate = active ? Colors.White : new Color(1f, 1f, 1f, 0.42f);
            _slotOrdinals[i].Visible = _orderMode == CombatRewardSequenceOrderMode.Ordered;
            _slots[i].SetEnabled(!_running && _catalog.CardCatalogAvailable && active);
        }
    }

    private void SelectCount(int value)
    {
        for (int i = 0; i < _countSelector.ItemCount; i++)
        {
            if (_countSelector.GetItemId(i) != value) continue;
            _countSelector.Select(i);
            return;
        }
    }
}
