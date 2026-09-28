using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

internal sealed partial class NeowFilterPage : MarginContainer
{
    private const float WideMasterPanelWidth = 290f;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly RelicPickerPanel _picker;
    private readonly NeowRouteSelectionPanel _master;
    private readonly NeowRouteEffectCardHost _detail;
    private readonly PanelContainer _masterPanel;
    private readonly PanelContainer _detailPanel;
    private readonly HBoxContainer _wideLayout;
    private readonly VBoxContainer _compactLayout;
    private readonly Label _detailTitle;
    private readonly Label _contextNotice;
    private NeowSearchUiCatalog _catalog = NeowSearchUiCatalog.Empty(
        RuntimeProfileId.Unsupported,
        BaseGameModelKeys.Characters.Silent,
        "not-bound");
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private bool _compact;
    private bool _running;

    public NeowFilterPage(
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterPoolIcons,
        ICardPickerFilterIconProvider cardPickerFilterIcons,
        AnchoredTooltipHost tooltipHost)
    {
        _tooltipHost = tooltipHost;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("margin_left", 2);
        AddThemeConstantOverride("margin_top", 2);
        AddThemeConstantOverride("margin_right", 2);
        AddThemeConstantOverride("margin_bottom", 2);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 8);

        _contextNotice = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _contextNotice.Visible = false;
        root.AddChild(_contextNotice);

        var layoutStack = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 320)
        };
        _wideLayout = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _wideLayout.AddThemeConstantOverride("separation", 12);
        _wideLayout.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _compactLayout = new VBoxContainer
        {
            Visible = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _compactLayout.AddThemeConstantOverride("separation", 12);
        _compactLayout.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layoutStack.AddChild(_wideLayout);
        layoutStack.AddChild(_compactLayout);
        root.AddChild(layoutStack);

        _masterPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(WideMasterPanelWidth, 0),
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyPanel(_masterPanel, Ui1SurfaceRole.Card, 4f, 1, 12f);
        _master = new NeowRouteSelectionPanel(icons, tooltipHost);
        _master.Changed += () =>
        {
            _contextNotice.Visible = false;
            SyncActiveContext();
            Changed?.Invoke();
        };
        _masterPanel.AddChild(_master);

        _detailPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(430, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyPanel(_detailPanel, Ui1SurfaceRole.Card, 4f, 1, 12f);
        var detailRoot = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        detailRoot.AddThemeConstantOverride("separation", 8);
        var detailHeader = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _detailTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _detailTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detailHeader.AddChild(_detailTitle);
        detailRoot.AddChild(detailHeader);

        var detailScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _picker = new RelicPickerPanel(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost);
        _detail = new NeowRouteEffectCardHost(icons, _picker.Open);
        _detail.Changed += () => Changed?.Invoke();
        _detail.BonesOrderModeChanged += mode => _master.SetBonesOrderMode(mode, notify: true);
        _detail.BonesSwapRequested += () => _master.SwapBonesOrder(notify: true);
        detailScroll.AddChild(_detail);
        detailRoot.AddChild(detailScroll);
        _detailPanel.AddChild(detailRoot);

        _wideLayout.AddChild(_masterPanel);
        _wideLayout.AddChild(_detailPanel);
        AddChild(root);

        AddChild(_picker);
        _picker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        VisibilityChanged += () =>
        {
            if (!IsVisibleInTree())
            {
                _tooltipHost.Dismiss();
            }
        };
        TreeExiting += () => _tooltipHost.Dismiss();
    }

    public bool TryCancelTransientSurface()
    {
        if (!_picker.IsOpen) return false;
        _picker.Cancel();
        return true;
    }

    public event Action? Changed;

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _tooltipHost.Dismiss();
        _text = text;
        _names = names;
        _master.Bind(_catalog.RouteRelics, names, text);
        _detail.ApplyLocalization(text, names);
        _picker.ApplyLocalization(text, names);
        _detailTitle.Text = text.Get(Ui1TextKey.SearchNeowDetailTitle);
        _contextNotice.Text = text.Get(Ui1TextKey.SearchNeowContextCleared);
        SyncActiveContext();
    }

    public void BindCatalog(NeowSearchUiCatalog catalog)
    {
        _tooltipHost.Dismiss();
        bool characterChanged = _catalog.CharacterKey != catalog.CharacterKey;
        ModelKey? previousRoute = _master.ActiveRouteRelic;
        ModelKey[] previousBones = _master.BonesRelics.ToArray();
        _catalog = catalog;
        if (_names is not null && _text is not null)
        {
            _master.Bind(catalog.RouteRelics, _names, _text);
        }
        bool masterCleared = previousRoute != _master.ActiveRouteRelic ||
                             !previousBones.SequenceEqual(_master.BonesRelics, ModelKeyComparer.Instance);
        bool detailCleared = _detail.BindCatalog(catalog);
        if (characterChanged && _picker.IsOpen) _picker.Cancel();
        bool reconciled = masterCleared || detailCleared;
        _contextNotice.Visible = reconciled;
        SyncActiveContext();
        if (reconciled) Changed?.Invoke();
    }

    public bool TryBuildDraft(out NeowRouteFilterDraft draft, out string issue, bool focusInvalid)
    {
        ModelKey? route = _master.ActiveRouteRelic;
        if (!route.HasValue)
        {
            draft = NeowRouteFilterDraft.Empty;
            issue = string.Empty;
            return true;
        }
        if (!_catalog.RouteRelics.Contains(route.Value, ModelKeyComparer.Instance))
        {
            draft = NeowRouteFilterDraft.Empty;
            issue = _text?.Get(Ui1TextKey.SearchNeowConditionUnavailable) ?? "Neow route is unavailable.";
            if (focusInvalid) _master.GrabSelectionFocus();
            return false;
        }
        if (!_detail.TryBuildConditions(out IReadOnlyList<NeowStructuredEffectSearchCondition> conditions, out issue, focusInvalid))
        {
            draft = NeowRouteFilterDraft.Empty;
            return false;
        }
        IReadOnlyList<ModelKey> requiredBonesRelics = _master.BonesMode
            ? _master.BonesRelics.ToArray()
            : Array.Empty<ModelKey>();
        draft = new NeowRouteFilterDraft(route, requiredBonesRelics, _master.BonesOrderMode, conditions);
        return true;
    }

    public void RestoreDraft(NeowRouteFilterDraft? draft, bool notify)
    {
        if (_running) return;
        NeowRouteFilterDraft restored = draft ?? NeowRouteFilterDraft.Empty;
        _master.RestoreSelection(restored.RouteRelicKey, restored.RequiredBonesRelics, restored.BonesOrderMode, notify: false);
        SyncActiveContext();
        _detail.RestoreConditions(restored.EffectConditions, notify: false);
        _contextNotice.Visible = false;
        if (notify) Changed?.Invoke();
    }

    public int EnabledConditionCount
    {
        get
        {
            if (!_master.ActiveRouteRelic.HasValue) return 0;
            int bonesRelicRefinement = _master.BonesMode && _master.BonesRelics.Count > 0 ? 1 : 0;
            return 1 + bonesRelicRefinement + _detail.ConditionCount;
        }
    }

    public void ClearDraft()
    {
        _master.ClearAllSelections(notify: false);
        _detail.ClearAllEffects();
        SyncActiveContext();
        _contextNotice.Visible = false;
        Changed?.Invoke();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        if (running && _picker.IsOpen) _picker.Cancel();
        _master.SetEnabled(!running);
        _detail.SetEnabled(!running);
    }

    public void SetCompact(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact;
        Container target = compact ? _compactLayout : _wideLayout;
        foreach (Control panel in new[] { _masterPanel, _detailPanel })
        {
            panel.GetParent()?.RemoveChild(panel);
            target.AddChild(panel);
        }
        _wideLayout.Visible = !compact;
        _compactLayout.Visible = compact;
        _masterPanel.CustomMinimumSize = compact ? Vector2.Zero : new Vector2(WideMasterPanelWidth, 0);
        _detailPanel.CustomMinimumSize = compact ? Vector2.Zero : new Vector2(430, 0);
        _master.SetCompact(compact);
    }

    private void SyncActiveContext() =>
        _detail.SetActiveContext(_master.ActiveRouteRelic, _master.BonesRelics, _master.BonesOrderMode);
}
