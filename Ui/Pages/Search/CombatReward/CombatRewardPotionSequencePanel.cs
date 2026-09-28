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
/// Player-facing Potion Reward authoring card. UI mode has three explicit states:
/// unconstrained / no drop / drop. Unconstrained is retained as nullable draft state
/// and canonical sparse-slot adaptation is intentionally deferred to the next batch.
/// </summary>
internal sealed partial class CombatRewardPotionSequencePanel : PanelContainer
{
    private enum PotionSlotUiMode
    {
        Unconstrained = 0,
        NoDrop = 1,
        Drop = 2
    }

    private readonly Label _title;
    private readonly Label _countPrefix;
    private readonly OptionButton _countSelector;
    private readonly Label _countUnit;
    private readonly CheckButton _orderedToggle;
    private readonly Button _clear;
    private readonly VBoxContainer[] _slotGroups;
    private readonly Label[] _slotOrdinals;
    private readonly OptionButton[] _modeSelectors;
    private readonly HBoxContainer[] _potionRows;
    private readonly Control[] _potionOrdinalSpacers;
    private readonly SearchPotionResultSlot[] _potionSlots;
    private readonly CombatPotionSlotRequirement?[] _requirements = new CombatPotionSlotRequirement?[3];
    private CombatRewardSearchUiCatalog _catalog = CombatRewardSearchUiCatalog.Empty(RuntimeProfileId.Unsupported, "not-bound");
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private int _count = 1;
    private CombatRewardSequenceOrderMode _orderMode = CombatRewardSequenceOrderMode.Ordered;
    private bool _running;
    private bool _refreshingControls;

    public CombatRewardPotionSequencePanel(
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
        slotsRow.AddThemeConstantOverride("separation", 12);

        _slotGroups = new VBoxContainer[3];
        _slotOrdinals = new Label[3];
        _modeSelectors = new OptionButton[3];
        _potionRows = new HBoxContainer[3];
        _potionOrdinalSpacers = new Control[3];
        _potionSlots = new SearchPotionResultSlot[3];

        for (int i = 0; i < 3; i++)
        {
            int slotIndex = i;
            var group = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
                SizeFlagsStretchRatio = 1f
            };
            group.AddThemeConstantOverride("separation", 6);

            var modeRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            modeRow.AddThemeConstantOverride("separation", 6);
            var ordinal = Ui1Theme.Label(i == 0 ? "①" : i == 1 ? "②" : "③", Ui1TextRole.Accent);
            ordinal.CustomMinimumSize = new Vector2(22f, 0f);
            ordinal.HorizontalAlignment = HorizontalAlignment.Center;
            ordinal.VerticalAlignment = VerticalAlignment.Center;

            var mode = new OptionButton
            {
                CustomMinimumSize = new Vector2(104f, 40f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            Ui1Theme.ApplyOptionButton(mode);
            mode.ItemSelected += _ =>
            {
                if (_refreshingControls || mode.Selected < 0) return;
                SetPotionMode(slotIndex, (PotionSlotUiMode)mode.GetItemId(mode.Selected));
            };

            var potionRow = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkBegin
            };
            potionRow.AddThemeConstantOverride("separation", 6);
            var potionOrdinalSpacer = new Control
            {
                CustomMinimumSize = new Vector2(22f, 0f),
                SizeFlagsHorizontal = SizeFlags.ShrinkBegin
            };
            var potion = new SearchPotionResultSlot(icons, openPicker);
            potion.UseExpandedWidth();
            potion.Changed += () => OnPotionChanged(slotIndex);

            modeRow.AddChild(ordinal);
            modeRow.AddChild(mode);
            group.AddChild(modeRow);
            potionRow.AddChild(potionOrdinalSpacer);
            potionRow.AddChild(potion);
            group.AddChild(potionRow);
            slotsRow.AddChild(group);
            _slotGroups[i] = group;
            _slotOrdinals[i] = ordinal;
            _modeSelectors[i] = mode;
            _potionRows[i] = potionRow;
            _potionOrdinalSpacers[i] = potionOrdinalSpacer;
            _potionSlots[i] = potion;
        }
        root.AddChild(slotsRow);

        AddChild(root);
        RefreshState();
    }

    public event Action? Changed;

    public bool HasAnyValue => Enumerable.Range(0, _count).Any(index => _requirements[index].HasValue);
    public bool NeedsReset => _requirements.Any(requirement => requirement.HasValue) || _potionSlots.Any(slot => slot.SelectedKey.HasValue) || _count != 3 || _orderMode != CombatRewardSequenceOrderMode.Unordered;

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _text = text;
        _names = names;
        _title.Text = text.Get(Ui1TextKey.SearchCombatRewardPotionsTitle);
        _countPrefix.Text = text.Get(Ui1TextKey.SearchCombatRewardCountPrefix);
        _countUnit.Text = text.Get(Ui1TextKey.SearchCombatRewardCountUnit);
        _orderedToggle.Text = text.Get(Ui1TextKey.SearchCombatRewardOrdered);
        _clear.Text = text.Get(Ui1TextKey.SearchCombatRewardClearConditions);
        ConfigureModeSelectors();
        ConfigureSlots();
        RefreshState();
    }

    public bool BindCatalog(CombatRewardSearchUiCatalog catalog)
    {
        _catalog = catalog;
        bool changed = false;
        for (int i = 0; i < _potionSlots.Length; i++)
        {
            if (catalog.PotionCatalogAvailable &&
                _potionSlots[i].SelectedKey is ModelKey potion && !catalog.PotionCandidates.Contains(potion, ModelKeyComparer.Instance))
            {
                _potionSlots[i].Select(null, notify: false);
                if (_requirements[i] == CombatPotionSlotRequirement.DropSpecific)
                    _requirements[i] = CombatPotionSlotRequirement.DropAny;
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

    public CombatRewardPotionSequenceDraft CaptureDraft() => new(
        _count,
        _orderMode,
        Enumerable.Range(0, 3)
            .Select(index => new CombatRewardPotionSlotDraft(
                _requirements[index],
                _requirements[index] == CombatPotionSlotRequirement.DropSpecific
                    ? _potionSlots[index].SelectedKey
                    : null))
            .ToArray());

    public bool TryBuildDraft(out CombatRewardPotionSequenceDraft draft, out string issue, bool focusInvalid)
    {
        draft = CaptureDraft();
        issue = string.Empty;
        return true;
    }

    public void RestoreDraft(CombatRewardPotionSequenceDraft? draft, bool notify)
    {
        CombatRewardPotionSequenceDraft value = draft ?? CombatRewardSearchDraft.Empty.Potions;
        _count = Math.Clamp(value.Count, 1, 3);
        _orderMode = value.OrderMode;
        for (int i = 0; i < 3; i++)
        {
            CombatRewardPotionSlotDraft slot = i < value.Slots.Count
                ? value.Slots[i]
                : new CombatRewardPotionSlotDraft(null, null);
            _requirements[i] = slot.Requirement;
            _potionSlots[i].Select(slot.PotionKey, notify: false);
            if (slot.PotionKey.HasValue)
                _requirements[i] = CombatPotionSlotRequirement.DropSpecific;
        }
        RefreshState();
        if (notify) Changed?.Invoke();
    }

    public void Clear(bool notify)
    {
        bool changed = NeedsReset;
        for (int i = 0; i < 3; i++)
        {
            _requirements[i] = null;
            _potionSlots[i].Select(null, notify: false);
        }
        _count = 3;
        _orderMode = CombatRewardSequenceOrderMode.Unordered;
        RefreshState();
        if (changed && notify) Changed?.Invoke();
    }

    private void SetCount(int count, bool notify)
    {
        if (_running || _count == count) return;
        _count = Math.Clamp(count, 1, 3);
        for (int i = _count; i < 3; i++)
        {
            _requirements[i] = null;
            _potionSlots[i].Select(null, notify: false);
        }
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

    private void SetPotionMode(int index, PotionSlotUiMode mode)
    {
        if (_running || index >= _count) return;
        _requirements[index] = mode switch
        {
            PotionSlotUiMode.Unconstrained => null,
            PotionSlotUiMode.NoDrop => CombatPotionSlotRequirement.NoDrop,
            PotionSlotUiMode.Drop => _potionSlots[index].SelectedKey.HasValue
                ? CombatPotionSlotRequirement.DropSpecific
                : CombatPotionSlotRequirement.DropAny,
            _ => null
        };
        RefreshState();
        Changed?.Invoke();
    }

    private void OnPotionChanged(int index)
    {
        if (index >= _count || CurrentMode(index) != PotionSlotUiMode.Drop) return;
        _requirements[index] = _potionSlots[index].SelectedKey.HasValue
            ? CombatPotionSlotRequirement.DropSpecific
            : CombatPotionSlotRequirement.DropAny;
        RefreshState();
        Changed?.Invoke();
    }

    private void ConfigureModeSelectors()
    {
        if (_text is null) return;
        foreach (OptionButton mode in _modeSelectors)
        {
            mode.Clear();
            mode.AddItem(_text.Get(Ui1TextKey.SearchCombatRewardPotionUnconstrained), (int)PotionSlotUiMode.Unconstrained);
            mode.AddItem(_text.Get(Ui1TextKey.SearchCombatRewardPotionNoDrop), (int)PotionSlotUiMode.NoDrop);
            mode.AddItem(_text.Get(Ui1TextKey.SearchCombatRewardPotionDrop), (int)PotionSlotUiMode.Drop);
        }
    }

    private void ConfigureSlots()
    {
        if (_text is null || _names is null) return;
        for (int i = 0; i < _potionSlots.Length; i++)
        {
            _potionSlots[i].Configure(
                string.Empty,
                _catalog.PotionCandidates,
                GameContentKind.Potion,
                IconVariant.Small,
                _names,
                _text.Get(Ui1TextKey.SearchCombatRewardAnyPotion),
                _text.Get(Ui1TextKey.SearchCombatRewardAnyPotion),
                _catalog.PotionCategories,
                _text.Get(Ui1TextKey.SearchCombatRewardPotionPickerTitle));
        }
    }

    private void RefreshState()
    {
        _refreshingControls = true;
        SelectCount(_count);
        _orderedToggle.ButtonPressed = _orderMode == CombatRewardSequenceOrderMode.Ordered;
        for (int i = 0; i < _modeSelectors.Length; i++) SelectMode(_modeSelectors[i], CurrentMode(i));
        _refreshingControls = false;

        _countSelector.Disabled = _running;
        _orderedToggle.Disabled = _running;
        _clear.Disabled = _running || !NeedsReset;
        for (int i = 0; i < 3; i++)
        {
            bool active = i < _count;
            bool dropping = CurrentMode(i) == PotionSlotUiMode.Drop;
            _slotGroups[i].Visible = true;
            _slotGroups[i].Modulate = active ? Colors.White : new Color(1f, 1f, 1f, 0.42f);
            bool ordered = _orderMode == CombatRewardSequenceOrderMode.Ordered;
            _slotOrdinals[i].Visible = ordered;
            _potionOrdinalSpacers[i].Visible = ordered;
            _modeSelectors[i].Disabled = _running || !_catalog.PotionCatalogAvailable || !active;
            _potionRows[i].Visible = dropping;
            _potionSlots[i].SetEnabled(!_running && _catalog.PotionCatalogAvailable && active && dropping);
        }
    }

    private PotionSlotUiMode CurrentMode(int index) => _requirements[index] switch
    {
        CombatPotionSlotRequirement.NoDrop => PotionSlotUiMode.NoDrop,
        CombatPotionSlotRequirement.DropAny => PotionSlotUiMode.Drop,
        CombatPotionSlotRequirement.DropSpecific => PotionSlotUiMode.Drop,
        _ => PotionSlotUiMode.Unconstrained
    };

    private void SelectCount(int value)
    {
        for (int i = 0; i < _countSelector.ItemCount; i++)
        {
            if (_countSelector.GetItemId(i) != value) continue;
            _countSelector.Select(i);
            return;
        }
    }

    private static void SelectMode(OptionButton selector, PotionSlotUiMode mode)
    {
        for (int i = 0; i < selector.ItemCount; i++)
        {
            if (selector.GetItemId(i) != (int)mode) continue;
            selector.Select(i);
            return;
        }
    }
}
