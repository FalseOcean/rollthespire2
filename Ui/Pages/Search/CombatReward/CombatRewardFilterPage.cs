using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.CombatReward;

/// <summary>
/// Combat Reward authoring surface. Card and Potion reward sequences are separate
/// player semantics with independent count/order/permutation; this page never
/// constructs a battle-bundled semantic condition.
/// </summary>
internal sealed partial class CombatRewardFilterPage : MarginContainer
{
    private readonly Label _catalogNotice;
    private readonly VBoxContainer _sequenceHost;
    private readonly CombatRewardCardSequencePanel _cards;
    private readonly CombatRewardPotionSequencePanel _potions;
    private readonly RelicPickerPanel _picker;
    private CombatRewardSearchUiCatalog _catalog = CombatRewardSearchUiCatalog.Empty(RuntimeProfileId.Unsupported, "not-bound");
    private bool _running;

    public CombatRewardFilterPage(
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterPoolIcons,
        ICardPickerFilterIconProvider cardPickerFilterIcons,
        AnchoredTooltipHost tooltipHost)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("margin_left", 2);
        AddThemeConstantOverride("margin_top", 2);
        AddThemeConstantOverride("margin_right", 2);
        AddThemeConstantOverride("margin_bottom", 2);

        _picker = new RelicPickerPanel(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 10);

        _catalogNotice = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _catalogNotice.Visible = false;
        root.AddChild(_catalogNotice);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _sequenceHost = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        _sequenceHost.AddThemeConstantOverride("separation", 10);

        Action<RelicPickerRequest> openPicker = request =>
        {
            if (!_running) _picker.Open(request);
        };
        _cards = new CombatRewardCardSequencePanel(icons, openPicker);
        _potions = new CombatRewardPotionSequencePanel(icons, openPicker);
        _cards.Changed += OnChanged;
        _potions.Changed += OnChanged;
        _sequenceHost.AddChild(_cards);
        _sequenceHost.AddChild(_potions);
        scroll.AddChild(_sequenceHost);
        root.AddChild(scroll);
        AddChild(root);

        AddChild(_picker);
        _picker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        VisibilityChanged += () =>
        {
            if (!IsVisibleInTree() && _picker.IsOpen) _picker.Cancel();
        };
    }

    public bool TryCancelTransientSurface()
    {
        if (!_picker.IsOpen) return false;
        _picker.Cancel();
        return true;
    }

    public event Action? Changed;

    public int EnabledConditionCount
    {
        get
        {
            CombatRewardSearchDraft draft = BuildDraft();
            int cards = draft.Cards.HasAnyValue ? draft.Cards.Count : 0;
            int potions = draft.Potions.HasAnyValue ? draft.Potions.Count : 0;
            return cards + potions;
        }
    }

    public CombatRewardSearchDraft BuildDraft() => new(
        _cards.CaptureDraft(),
        _potions.CaptureDraft());

    public bool TryBuildDraft(out CombatRewardSearchDraft draft, out string issue, bool focusInvalid)
    {
        if (!_cards.TryBuildDraft(out CombatRewardCardSequenceDraft cards, out issue, focusInvalid))
        {
            draft = BuildDraft();
            return false;
        }
        if (!_potions.TryBuildDraft(out CombatRewardPotionSequenceDraft potions, out issue, focusInvalid))
        {
            draft = BuildDraft();
            return false;
        }
        draft = new CombatRewardSearchDraft(cards, potions);
        issue = string.Empty;
        return true;
    }

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _catalogNotice.Text = text.Get(Ui1TextKey.SearchCombatRewardCatalogUnavailable);
        _picker.ApplyLocalization(text, names);
        _cards.ApplyLocalization(text, names);
        _potions.ApplyLocalization(text, names);
        RefreshState();
    }

    public void BindCatalog(CombatRewardSearchUiCatalog catalog)
    {
        _catalog = catalog;
        if (_picker.IsOpen) _picker.Cancel();
        bool cardsCleared = _cards.BindCatalog(catalog);
        bool potionsCleared = _potions.BindCatalog(catalog);
        RefreshState();
        if (cardsCleared || potionsCleared) Changed?.Invoke();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        if (running && _picker.IsOpen) _picker.Cancel();
        _cards.SetRunning(running);
        _potions.SetRunning(running);
        RefreshState();
    }

    public void SetCompact(bool compact)
    {
        // Card/Potion authoring remains vertically stacked at every supported width.
    }

    public void RestoreDraft(CombatRewardSearchDraft? draft, bool notify)
    {
        if (_running) return;
        CombatRewardSearchDraft value = draft ?? CombatRewardSearchDraft.Empty;
        _cards.RestoreDraft(value.Cards, notify: false);
        _potions.RestoreDraft(value.Potions, notify: false);
        RefreshState();
        if (notify) Changed?.Invoke();
    }

    public void ClearDraft()
    {
        if (_running) return;
        bool changed = _cards.NeedsReset || _potions.NeedsReset;
        _cards.Clear(notify: false);
        _potions.Clear(notify: false);
        RefreshState();
        if (changed) Changed?.Invoke();
    }

    private void OnChanged()
    {
        RefreshState();
        Changed?.Invoke();
    }

    private void RefreshState()
    {
        _catalogNotice.Visible = !_catalog.CardCatalogAvailable || !_catalog.PotionCatalogAvailable;
    }
}
