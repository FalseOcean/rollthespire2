using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Controls;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Relic;

internal sealed partial class RelicSequenceFilterPage : MarginContainer
{
    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly RelicPickerPanel _picker;
    private readonly Label _pageTitle;
    private readonly Button _clear;
    private readonly Label _builderTitle;
    private readonly SequenceConditionControlStrip _sequenceControls;
    private readonly Button _chooseRelic;
    private readonly Label _catalogNotice;
    private readonly Label _addedTitle;
    private readonly Label _empty;
    private readonly VBoxContainer _conditionsHost;
    private readonly List<RelicSequenceUiCondition> _conditions = new();
    private RelicSequenceSearchUiCatalog _catalog = RelicSequenceSearchUiCatalog.Empty(
        RuntimeProfileId.Unsupported,
        "not-bound");
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private bool _running;

    public RelicSequenceFilterPage(
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterPoolIcons,
        ICardPickerFilterIconProvider cardPickerFilterIcons,
        AnchoredTooltipHost tooltipHost)
    {
        _icons = icons;
        _tooltipHost = tooltipHost;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("margin_left", 2);
        AddThemeConstantOverride("margin_top", 2);
        AddThemeConstantOverride("margin_right", 2);
        AddThemeConstantOverride("margin_bottom", 2);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 10);

        var header = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        header.AddThemeConstantOverride("separation", 10);
        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _pageTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _pageTitle.VerticalAlignment = VerticalAlignment.Center;
        _clear = new Button { CustomMinimumSize = new Vector2(132f, 34f) };
        Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Ghost);
        _clear.Pressed += ClearDraft;
        header.AddChild(_pageTitle);
        header.AddChild(_clear);
        root.AddChild(header);

        var builderPanel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyPanel(builderPanel, Ui1SurfaceRole.Card, 4f, 1, 12f);
        var builderColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        builderColumn.AddThemeConstantOverride("separation", 8);
        _builderTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        builderColumn.AddChild(_builderTitle);

        var builderRow = new HFlowContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        builderRow.AddThemeConstantOverride("h_separation", 8);
        builderRow.AddThemeConstantOverride("v_separation", 8);

        _sequenceControls = new SequenceConditionControlStrip();
        _sequenceControls.ResetDefaults(notify: false, firstNValue: 3);
        _sequenceControls.Changed += RefreshBuilderState;

        _chooseRelic = new Button
        {
            CustomMinimumSize = new Vector2(156f, 38f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplyButton(_chooseRelic, Ui1ButtonRole.Primary);
        _chooseRelic.Pressed += OpenPicker;

        builderRow.AddChild(_sequenceControls);
        builderRow.AddChild(_chooseRelic);
        builderColumn.AddChild(builderRow);

        _catalogNotice = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _catalogNotice.Visible = false;
        builderColumn.AddChild(_catalogNotice);
        builderPanel.AddChild(builderColumn);
        root.AddChild(builderPanel);

        _addedTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        root.AddChild(_addedTitle);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0f, 150f)
        };
        _conditionsHost = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        _conditionsHost.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_conditionsHost);
        root.AddChild(scroll);

        _empty = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _conditionsHost.AddChild(_empty);

        AddChild(root);

        _picker = new RelicPickerPanel(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost);
        AddChild(_picker);
        _picker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        VisibilityChanged += () =>
        {
            if (!IsVisibleInTree())
            {
                _tooltipHost.Dismiss();
                if (_picker.IsOpen) _picker.Cancel();
            }
        };
        TreeExiting += () => _tooltipHost.Dismiss();
        RefreshBuilderState();
    }

    public bool TryCancelTransientSurface()
    {
        if (!_picker.IsOpen) return false;
        _picker.Cancel();
        return true;
    }

    public event Action? Changed;

    public int EnabledConditionCount => _conditions.Count;

    public IReadOnlyList<RelicSequenceSearchCondition> BuildSearchConditions() =>
        _conditions
            .Where(condition => condition.Lane != RelicSequenceKind.Shop)
            .Select(condition => new RelicSequenceSearchCondition(
                condition.Lane,
                condition.RangeMode,
                condition.RangeValue,
                condition.MatchMode == RelicSequenceUiMatchMode.Appears
                    ? new ModelKeySetFilter(new[] { condition.RelicKey }, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())
                    : new ModelKeySetFilter(Array.Empty<ModelKey>(), Array.Empty<ModelKey>(), new[] { condition.RelicKey })))
            .ToArray();

    public string SerializedConditions => string.Join(
        ";",
        _conditions.Select(condition => condition.Serialize()));

    public bool TryValidateCatalogConsistency(out string issue)
    {
        foreach (RelicSequenceUiCondition condition in _conditions)
        {
            if (!_catalog.TryResolveLane(condition.RelicKey, out RelicSequenceKind lane) ||
                lane != condition.Lane)
            {
                issue = _text?.Get(Ui1TextKey.SearchRelicCatalogMismatch) ??
                        "Relic sequence catalog mismatch.";
                return false;
            }
        }
        issue = string.Empty;
        return true;
    }

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _tooltipHost.Dismiss();
        _text = text;
        _names = names;
        _pageTitle.Text = text.Get(Ui1TextKey.SearchRelicPageHint);
        _clear.Text = text.Get(Ui1TextKey.SearchRelicClearConditions);
        _builderTitle.Text = text.Get(Ui1TextKey.SearchRelicBuilderTitle);
        _chooseRelic.Text = text.Get(Ui1TextKey.SearchRelicSelectRelic);
        _catalogNotice.Text = text.Get(Ui1TextKey.SearchRelicCatalogUnavailable);
        _empty.Text = text.Get(Ui1TextKey.SearchRelicNoConditions);
        _picker.ApplyLocalization(text, names);
        _sequenceControls.ApplyText(
            text.Get(Ui1TextKey.SearchRelicRangeExactSlot),
            text.Get(Ui1TextKey.SearchRelicRangeFirstN),
            text.Get(Ui1TextKey.SearchSequenceUnit),
            text.Get(Ui1TextKey.SearchRelicModeAppears),
            text.Get(Ui1TextKey.SearchRelicModeExcluded),
            text.Get(Ui1TextKey.SearchSequenceExactTooltip),
            text.Get(Ui1TextKey.SearchSequenceFirstTooltip),
            text.Get(Ui1TextKey.SearchSequenceAppearsTooltip),
            text.Get(Ui1TextKey.SearchSequenceExcludedTooltip));
        RebuildConditions();
        RefreshBuilderState();
    }

    public void BindCatalog(RelicSequenceSearchUiCatalog catalog)
    {
        _tooltipHost.Dismiss();
        _catalog = catalog;
        if (_picker.IsOpen) _picker.Cancel();
        int removed = catalog.CatalogAvailable && catalog.LaneMappingExact
            ? _conditions.RemoveAll(condition =>
                !catalog.TryResolveLane(condition.RelicKey, out RelicSequenceKind lane) || lane != condition.Lane)
            : 0;
        RebuildConditions();
        RefreshBuilderState();
        if (removed > 0) Changed?.Invoke();
    }

    public void SetRunning(bool running)
    {
        if (_running == running)
        {
            RefreshBuilderState();
            return;
        }
        _running = running;
        _sequenceControls.SetDisabled(running);
        _chooseRelic.Disabled = running || !_catalog.CatalogAvailable;
        _clear.Disabled = running || _conditions.Count == 0;
        if (running && _picker.IsOpen) _picker.Cancel();
        RebuildConditions();
        RefreshBuilderState();
    }

    public void RestoreDraft(IReadOnlyList<RelicSequenceSearchCondition>? conditions, bool notify)
    {
        if (_running) return;
        _tooltipHost.Dismiss();
        _conditions.Clear();
        foreach (RelicSequenceSearchCondition condition in conditions ?? Array.Empty<RelicSequenceSearchCondition>())
        {
            if (condition.IsEmpty || condition.RangeValue <= 0) continue;
            if (condition.Lane == RelicSequenceKind.Shop) continue;
            foreach (ModelKey key in (condition.Keys.Any ?? Array.Empty<ModelKey>()).Concat(condition.Keys.All ?? Array.Empty<ModelKey>()))
            {
                if (!key.IsValid) continue;
                _conditions.Add(new RelicSequenceUiCondition(
                    Guid.NewGuid(), condition.Lane, condition.RangeMode, condition.RangeValue, key, RelicSequenceUiMatchMode.Appears));
            }
            foreach (ModelKey key in condition.Keys.Ban ?? Array.Empty<ModelKey>())
            {
                if (!key.IsValid) continue;
                _conditions.Add(new RelicSequenceUiCondition(
                    Guid.NewGuid(), condition.Lane, condition.RangeMode, condition.RangeValue, key, RelicSequenceUiMatchMode.Excluded));
            }
        }
        _sequenceControls.ResetDefaults(notify: false, firstNValue: 3);
        RebuildConditions();
        RefreshBuilderState();
        if (notify) Changed?.Invoke();
    }

    public void ClearDraft()
    {
        if (_running)
        {
            return;
        }
        _tooltipHost.Dismiss();
        bool hadConditions = _conditions.Count > 0;
        _conditions.Clear();
        _sequenceControls.ResetDefaults(notify: false, firstNValue: 3);
        RebuildConditions();
        RefreshBuilderState();
        if (hadConditions)
        {
            Changed?.Invoke();
        }
    }

    private void OpenPicker()
    {
        if (_running || _text is null || _names is null || !_catalog.CatalogAvailable)
        {
            return;
        }

        _tooltipHost.Dismiss();
        _picker.Open(new RelicPickerRequest(
            _text.Get(Ui1TextKey.SearchRelicPickerTitle),
            _catalog.AllCandidates.Where(key => _catalog.TryResolveLane(key, out RelicSequenceKind lane) && lane != RelicSequenceKind.Shop).ToArray(),
            null,
            Array.Empty<ModelKey>(),
            _catalog.Categories,
            false,
            GameContentKind.Relic,
            IconVariant.Small,
            CommitCondition));
    }

    private void CommitCondition(ModelKey? selectedKey)
    {
        if (!selectedKey.HasValue || !CanAuthorCondition() ||
            !_catalog.TryResolveLane(selectedKey.Value, out RelicSequenceKind lane) || lane == RelicSequenceKind.Shop)
        {
            return;
        }

        _conditions.Add(new RelicSequenceUiCondition(
            Guid.NewGuid(),
            lane,
            _sequenceControls.RangeMode,
            _sequenceControls.RangeValue,
            selectedKey.Value,
            _sequenceControls.IsExcluded
                ? RelicSequenceUiMatchMode.Excluded
                : RelicSequenceUiMatchMode.Appears));
        RebuildConditions();
        RefreshBuilderState();
        Changed?.Invoke();
    }

    private void RemoveCondition(Guid id)
    {
        if (_running)
        {
            return;
        }

        int removed = _conditions.RemoveAll(condition => condition.Id == id);
        if (removed == 0)
        {
            return;
        }
        _tooltipHost.Dismiss();
        RebuildConditions();
        Changed?.Invoke();
    }

    private void RebuildConditions()
    {
        foreach (Node child in _conditionsHost.GetChildren())
        {
            if (ReferenceEquals(child, _empty))
            {
                continue;
            }
            _conditionsHost.RemoveChild(child);
            child.QueueFree();
        }

        if (_text is null || _names is null)
        {
            _empty.Text = string.Empty;
            _empty.Visible = true;
            return;
        }

        _addedTitle.Text = _text.Format(Ui1TextKey.SearchRelicAddedConditions, _conditions.Count);
        _clear.Disabled = _running || _conditions.Count == 0;
        if (_conditions.Count == 0)
        {
            _empty.Text = _text.Get(Ui1TextKey.SearchRelicNoConditions);
            _empty.Visible = true;
            return;
        }

        _empty.Visible = false;
        foreach (RelicSequenceUiCondition condition in _conditions)
        {
            bool catalogConsistent = _catalog.TryResolveLane(condition.RelicKey, out RelicSequenceKind lane) &&
                                     lane == condition.Lane;
            var card = new RelicSequenceConditionCard(
                condition,
                _icons,
                _tooltipHost,
                _names,
                catalogConsistent
                    ? ConditionDetail(condition)
                    : _text.Get(Ui1TextKey.SearchRelicCatalogMismatch),
                _text.Get(Ui1TextKey.SearchRelicRemoveCondition),
                _running,
                () => RemoveCondition(condition.Id));
            _conditionsHost.AddChild(card);
        }
    }

    private string ConditionDetail(RelicSequenceUiCondition condition)
    {
        if (_text is null)
        {
            return condition.Serialize();
        }

        string lane = LaneText(condition.Lane);
        return condition.RangeMode == SearchSequenceRangeMode.FirstN
            ? condition.MatchMode == RelicSequenceUiMatchMode.Appears
                ? _text.Format(Ui1TextKey.SearchRelicConditionFirstNAppears, lane, condition.RangeValue)
                : _text.Format(Ui1TextKey.SearchRelicConditionFirstNExcluded, lane, condition.RangeValue)
            : condition.MatchMode == RelicSequenceUiMatchMode.Appears
                ? _text.Format(Ui1TextKey.SearchRelicConditionExactAppears, lane, condition.RangeValue)
                : _text.Format(Ui1TextKey.SearchRelicConditionExactExcluded, lane, condition.RangeValue);
    }

    private string LaneText(RelicSequenceKind lane)
    {
        if (_text is null) return lane.ToString();
        return lane switch
        {
            RelicSequenceKind.Common => _text.Get(Ui1TextKey.SearchRelicLaneCommon),
            RelicSequenceKind.Uncommon => _text.Get(Ui1TextKey.SearchRelicLaneUncommon),
            RelicSequenceKind.Rare => _text.Get(Ui1TextKey.SearchRelicLaneRare),
            RelicSequenceKind.Shop => _text.Get(Ui1TextKey.SearchRelicLaneShop),
            _ => lane.ToString()
        };
    }

    private void RefreshBuilderState()
    {
        bool catalogAvailable = _catalog.CatalogAvailable;
        _catalogNotice.Visible = !catalogAvailable;
        _chooseRelic.Disabled = !CanAuthorCondition();
        _clear.Disabled = _running || _conditions.Count == 0;
    }

    private bool CanAuthorCondition() =>
        !_running &&
        _catalog.CatalogAvailable &&
        _sequenceControls.RangeValue is >= 1 and <= 10;
}
