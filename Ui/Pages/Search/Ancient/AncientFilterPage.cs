using System.Diagnostics;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Ancient;

internal sealed partial class AncientFilterPage : MarginContainer
{
    private readonly IGameIconResolver _icons;
    private readonly ICharacterPoolIconProvider _characterIcons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly HBoxContainer _sectionsHost;
    private readonly Label _catalogNotice;
    private readonly AncientOptionConditionEditor _conditionEditor;
    private readonly Button _clear;
    private readonly Control _clearOverlay;
    private readonly List<AncientActSection> _sections = new();
    private readonly Dictionary<(int Act, ModelKey Key), AncientRowUiState> _history = new();
    private Label _clearConfirmTitle = null!;
    private Label _clearConfirmBody = null!;
    private Button _clearConfirmCancel = null!;
    private Button _clearConfirmAccept = null!;
    private AncientSearchUiCatalog _catalog = AncientSearchUiCatalog.Empty(
        RuntimeProfileId.Unsupported,
        BaseGameModelKeys.Characters.Silent,
        "not-bound");
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private bool _running;

    public AncientFilterPage(
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterIcons,
        AnchoredTooltipHost tooltipHost)
    {
        _icons = icons;
        _characterIcons = characterIcons;
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
        root.AddThemeConstantOverride("separation", 7);

        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 8);
        _conditionEditor = new AncientOptionConditionEditor(_icons, _tooltipHost)
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _conditionEditor.Changed += OnOptionConditionsChanged;
        _clear = new Button
        {
            CustomMinimumSize = new Vector2(132, 42),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Ghost);
        _clear.Pressed += OpenClearConfirmation;
        header.AddChild(_conditionEditor);
        header.AddChild(_clear);
        root.AddChild(header);

        _catalogNotice = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _catalogNotice.Visible = false;
        root.AddChild(_catalogNotice);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _sectionsHost = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            ClipContents = true
        };
        _sectionsHost.AddThemeConstantOverride("separation", AncientRowGeometry.ActGap);
        scroll.AddChild(_sectionsHost);
        root.AddChild(scroll);
        AddChild(root);

        _clearOverlay = BuildClearConfirmationOverlay();
        AddChild(_clearOverlay);
        _clearOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        VisibilityChanged += () =>
        {
            if (!IsVisibleInTree())
            {
                _tooltipHost.Dismiss();
            }
        };
    }

    public bool TryCancelTransientSurface()
    {
        if (!_clearOverlay.Visible) return false;
        CloseClearConfirmation();
        return true;
    }

    public event Action? Changed;

    public int EnabledConditionCount => _sections.Sum(section => section.EnabledConditionCount);
    public AncientOptionConditionProfile OptionConditions => _conditionEditor.CurrentProfile;

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        CaptureHistory();
        _text = text;
        _names = names;
        _clear.Text = text.Get(Ui1TextKey.SearchAncientClearConditions);
        _clearConfirmTitle.Text = text.Get(Ui1TextKey.SearchAncientClearConfirmTitle);
        _clearConfirmBody.Text = text.Get(Ui1TextKey.SearchAncientClearConfirmBody);
        _clearConfirmCancel.Text = text.Get(Ui1TextKey.SearchAncientClearCancel);
        _clearConfirmAccept.Text = text.Get(Ui1TextKey.SearchAncientClearAccept);
        _conditionEditor.ApplyLocalization(text, names);
        Rebuild();
    }

    public void BindCatalog(AncientSearchUiCatalog catalog)
    {
        CaptureHistory();
        _catalog = catalog;
        Rebuild();
    }

    public AncientSearchMatrixDraft BuildDraft()
    {
        CaptureHistory();
        AncientSearchRowDraft[] rows = _history.Values
            .OrderBy(state => state.Act)
            .ThenBy(state => state.AncientKey.Serialized, StringComparer.Ordinal)
            .Select(state => new AncientSearchRowDraft(
                state.Act,
                state.AncientKey,
                state.IsActive,
                state.SelectedOptions,
                state.SeaGlassTargets))
            .ToArray();
        return new AncientSearchMatrixDraft(rows);
    }

    public void RestoreDraft(AncientSearchMatrixDraft draft, AncientOptionConditionProfile optionProfile, bool notify)
    {
        _history.Clear();
        foreach (AncientSearchRowDraft row in draft?.Rows ?? Array.Empty<AncientSearchRowDraft>())
        {
            if (row.Act is < 1 or > 3 || !row.AncientKey.IsValid) continue;
            _history[(row.Act, row.AncientKey)] = new AncientRowUiState(
                row.Act,
                row.AncientKey,
                row.IsActive,
                row.SelectedOptionKeys?.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray() ?? Array.Empty<ModelKey>(),
                row.SeaGlassTargetKeys?.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray() ?? Array.Empty<ModelKey>());
        }
        _conditionEditor.RestoreProfile(optionProfile ?? AncientOptionConditionProfile.BroadDefault, notify: false);
        Rebuild();
        CloseClearConfirmation();
        RefreshClearState();
        if (notify) Changed?.Invoke();
    }

    public void ClearDraft()
    {
        ClearAncientTargets(notify: false);
        _conditionEditor.ResetToBroadDefault(notify: false);
        RefreshClearState();
        Changed?.Invoke();
    }

    private void ClearAncientTargets(bool notify)
    {
        foreach (AncientActSection section in _sections) section.Clear(notify: false);
        _history.Clear();
        CloseClearConfirmation();
        RefreshClearState();
        if (notify) Changed?.Invoke();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        if (running)
        {
            CloseClearConfirmation();
            _tooltipHost.Dismiss();
        }
        _conditionEditor.SetEnabled(!running);
        foreach (AncientActSection section in _sections) section.SetEnabled(!running);
        RefreshClearState();
    }

    public void SetCompact(bool compact)
    {
        // Prototype intentionally keeps the Act2/Act3 split stable. Individual
        // Ancient cards own their compact option geometry and the page scrolls vertically.
    }

    private void Rebuild()
    {
        _tooltipHost.Dismiss();
        var stopwatch = Stopwatch.StartNew();
        foreach (Node child in _sectionsHost.GetChildren())
        {
            _sectionsHost.RemoveChild(child);
            child.QueueFree();
        }
        _sections.Clear();
        if (_text is null || _names is null)
        {
            RuntimeLog.Detail("Ancient UI rebuild deferred: localization or content-name resolver is not bound.");
            return;
        }

        string missingIcon = _text.Get(Ui1TextKey.MissingIconTooltip);
        foreach (AncientActSectionDefinition definition in _catalog.Sections.OrderBy(section => section.Act))
        {
            var section = new AncientActSection(
                definition,
                _catalog.SeaGlassTargetCharacters,
                _catalog.CharacterTargetAuthorityExact,
                _icons,
                _characterIcons,
                _names,
                _text,
                _tooltipHost,
                missingIcon);
            foreach (AncientConditionRow row in section.Rows)
            {
                if (_history.TryGetValue((row.Act, row.AncientKey), out AncientRowUiState? state) && state is not null)
                {
                    row.RestoreState(state);
                }
            }
            section.Changed += OnSectionChanged;
            section.SetEnabled(!_running);
            _sections.Add(section);
            _sectionsHost.AddChild(section);
        }

        AncientRowDefinition[] rows = _catalog.Sections.SelectMany(section => section.Rows).ToArray();
        int pendingRows = rows.Count(row => !row.IdentityAvailable || row.Options.Count == 0);
        bool completeMatrix = _catalog.Sections.Count == 2 &&
                              _catalog.Sections.All(section => section.Rows.Count == 4) &&
                              pendingRows == 0;
        _catalogNotice.Text = _text.Get(Ui1TextKey.SearchAncientCatalogPending);
        _catalogNotice.Visible = !completeMatrix;
        RefreshClearState();
        RuntimeLog.Ui(
            $"Ancient page built: profile={_catalog.ProfileId}; character={_catalog.CharacterKey.Serialized}; " +
            $"sections={_sections.Count}; rows={rows.Length}; optionIcons={rows.Sum(row => row.Options.Count)}; " +
            $"pendingRows={pendingRows}; historyRows={_history.Count}; " +
            $"matrixComplete={completeMatrix.ToString().ToLowerInvariant()}; " +
            $"took={stopwatch.Elapsed.TotalMilliseconds:0.0}ms");
    }

    private void OnSectionChanged()
    {
        CaptureHistory();
        RefreshClearState();
        Changed?.Invoke();
    }

    private void OnOptionConditionsChanged()
    {
        RefreshClearState();
        Changed?.Invoke();
    }

    private void CaptureHistory()
    {
        foreach (AncientActSection section in _sections)
        foreach (AncientConditionRow row in section.Rows)
        {
            AncientRowUiState state = row.CaptureState();
            _history[(state.Act, state.AncientKey)] = state;
        }
    }

    private void RefreshClearState()
    {
        CaptureHistory();
        bool hasAnyTarget = _history.Values.Any(state =>
            state.IsActive || state.SelectedOptions.Count > 0 || state.SeaGlassTargets.Count > 0);
        _clear.Disabled = _running || !hasAnyTarget;
    }

    private void OpenClearConfirmation()
    {
        if (_clear.Disabled) return;
        _tooltipHost.Dismiss();
        _clearOverlay.Visible = true;
        _clearConfirmCancel.GrabFocus();
    }

    private void CloseClearConfirmation() => _clearOverlay.Visible = false;

    private void ConfirmClear()
    {
        ClearAncientTargets(notify: true);
    }

    public override void _ExitTree()
    {
        _tooltipHost.Dismiss();
    }

    private Control BuildClearConfirmationOverlay()
    {
        var overlay = new Control
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = UiZLayers.ToastModal
        };
        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.68f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backdrop.GuiInput += input =>
        {
            if (input is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            {
                CloseClearConfirmation();
            }
        };
        overlay.AddChild(backdrop);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(430, 0) };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.CardElevated, 6f, 1, 16f);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        _clearConfirmTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle, true);
        _clearConfirmBody = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, true);
        var actions = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        actions.AddThemeConstantOverride("separation", 8);
        actions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _clearConfirmCancel = new Button { CustomMinimumSize = new Vector2(92, 36) };
        _clearConfirmAccept = new Button { CustomMinimumSize = new Vector2(132, 36) };
        Ui1Theme.ApplyButton(_clearConfirmCancel, Ui1ButtonRole.Ghost);
        Ui1Theme.ApplyButton(_clearConfirmAccept, Ui1ButtonRole.Primary);
        _clearConfirmCancel.Pressed += CloseClearConfirmation;
        _clearConfirmAccept.Pressed += ConfirmClear;
        actions.AddChild(_clearConfirmCancel);
        actions.AddChild(_clearConfirmAccept);
        column.AddChild(_clearConfirmTitle);
        column.AddChild(_clearConfirmBody);
        column.AddChild(actions);
        panel.AddChild(column);
        center.AddChild(panel);
        overlay.AddChild(center);
        return overlay;
    }

}
