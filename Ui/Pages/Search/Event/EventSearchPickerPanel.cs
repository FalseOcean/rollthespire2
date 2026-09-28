using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Event;

internal sealed partial class EventSearchPickerPanel : Control
{
    private const int StandardColumns = 4;
    private const float GridGap = 10f;
    private const float ReservedScrollBarWidth = 18f;

    private readonly EventThumbnailProvider _thumbnails;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly PanelContainer _dialog;
    private readonly Label _title;
    private readonly Button _close;
    private readonly LineEdit _search;
    private readonly HFlowContainer _sourceFilters;
    private readonly ScrollContainer _scroll;
    private readonly GridContainer _grid;
    private readonly Label _empty;
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private IReadOnlyList<EventSearchUiCandidate> _candidates = Array.Empty<EventSearchUiCandidate>();
    private Action<EventSearchUiCandidate?>? _onSelected;
    private bool _sharedOnly;
    private ModelKey? _variantOnly;
    private RuntimeProfileId _profileId = RuntimeProfileId.Unsupported;
    private int _playersCount = 1;
    private float _tileWidth = EventPickerTile.MaxWidth;

    public EventSearchPickerPanel(EventThumbnailProvider thumbnails, AnchoredTooltipHost tooltipHost)
    {
        _thumbnails = thumbnails;
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        Visible = false;
        ZIndex = UiZLayers.PickerModal;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        SetProcessUnhandledKeyInput(true);

        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.66f),
            MouseFilter = MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            {
                Cancel();
            }
        };
        AddChild(backdrop);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        _dialog = new PanelContainer
        {
            CustomMinimumSize = new Vector2(760f, 560f),
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            ClipContents = true
        };
        Ui1Theme.ApplyPanel(_dialog, Ui1SurfaceRole.Drawer, 5f, 2, 16f);
        center.AddChild(_dialog);

        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 10);

        var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.ClipText = true;
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _close = new Button { CustomMinimumSize = new Vector2(76f, 36f) };
        Ui1Theme.ApplyButton(_close, Ui1ButtonRole.Ghost);
        _close.Pressed += Cancel;
        header.AddChild(_title);
        header.AddChild(_close);
        body.AddChild(header);

        _search = new LineEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClearButtonEnabled = true
        };
        Ui1Theme.ApplyLineEdit(_search);
        _search.TextChanged += _ => RebuildGrid();
        body.AddChild(_search);

        _sourceFilters = new HFlowContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        _sourceFilters.AddThemeConstantOverride("h_separation", 6);
        _sourceFilters.AddThemeConstantOverride("v_separation", 6);
        body.AddChild(_sourceFilters);

        _scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            CustomMinimumSize = new Vector2(0f, 300f)
        };
        _grid = new GridContainer
        {
            Columns = StandardColumns,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        _grid.AddThemeConstantOverride("h_separation", (int)GridGap);
        _grid.AddThemeConstantOverride("v_separation", (int)GridGap);
        _scroll.AddChild(_grid);
        _scroll.Resized += QueueGridColumnRefresh;
        body.AddChild(_scroll);

        _empty = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.Visible = false;
        body.AddChild(_empty);
        _dialog.AddChild(body);
    }

    public bool IsOpen => Visible;

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _text = text;
        _names = names;
        _close.Text = text.Get(Ui1TextKey.SearchEventPickerClose);
        _search.PlaceholderText = text.Get(Ui1TextKey.SearchEventPickerSearchPlaceholder);
        _empty.Text = text.Get(Ui1TextKey.SearchEventPickerNoResults);
        if (Visible)
        {
            RebuildSourceFilters();
            RebuildGrid();
        }
    }

    public void Open(
        string title,
        RuntimeProfileId profileId,
        int playersCount,
        IReadOnlyList<EventSearchUiCandidate> candidates,
        Action<EventSearchUiCandidate?> onSelected)
    {
        _tooltipHost.Dismiss();
        if (_text is null || _names is null || candidates.Count == 0) return;
        _title.Text = title;
        _title.TooltipText = title;
        _candidates = candidates;
        _onSelected = onSelected;
        _profileId = profileId;
        _playersCount = Math.Max(1, playersCount);
        _sharedOnly = false;
        _variantOnly = null;
        _search.Text = string.Empty;
        _tileWidth = EventPickerTile.MaxWidth;
        Visible = true;
        RebuildSourceFilters();
        RebuildGrid();
        QueueGridColumnRefresh();
        _search.GrabFocus();
    }

    public void Cancel()
    {
        _tooltipHost.Dismiss();
        Visible = false;
        _candidates = Array.Empty<EventSearchUiCandidate>();
        _onSelected = null;
        _profileId = RuntimeProfileId.Unsupported;
        _playersCount = 1;
        _sharedOnly = false;
        _variantOnly = null;
        ClearGrid();
        ClearSourceFilters();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape)
        {
            return;
        }
        Cancel();
        GetViewport().SetInputAsHandled();
    }

    private void RebuildSourceFilters()
    {
        ClearSourceFilters();
        if (_text is null || _names is null) return;

        _sourceFilters.AddChild(SourceButton(
            _text.Get(Ui1TextKey.SearchEventSourceAll),
            !_sharedOnly && !_variantOnly.HasValue,
            () => SetSourceFilter(false, null)));

        if (_candidates.Any(candidate => candidate.IsShared))
        {
            _sourceFilters.AddChild(SourceButton(
                _text.Get(Ui1TextKey.SearchEventSourceShared),
                _sharedOnly,
                () => SetSourceFilter(true, null)));
        }

        foreach (ModelKey variant in DistinctVariants())
        {
            string name = _names.Resolve(variant, GameContentKind.Act);
            ModelKey captured = variant;
            _sourceFilters.AddChild(SourceButton(
                name,
                _variantOnly == variant,
                () => SetSourceFilter(false, captured)));
        }
    }

    private void SetSourceFilter(bool sharedOnly, ModelKey? variantOnly)
    {
        _sharedOnly = sharedOnly;
        _variantOnly = variantOnly;
        RebuildSourceFilters();
        RebuildGrid();
    }

    private Button SourceButton(string text, bool selected, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(68f, 32f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            FocusMode = FocusModeEnum.All,
            TooltipText = text
        };
        Ui1Theme.ApplyButton(button, selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Secondary);
        button.Pressed += pressed;
        return button;
    }

    private void RebuildGrid()
    {
        _tooltipHost.Dismiss();
        ClearGrid();
        if (_names is null || _text is null)
        {
            _empty.Visible = true;
            return;
        }

        string query = _search.Text.Trim();
        EventSearchUiCandidate[] visible = _candidates
            .Where(SourceMatches)
            .Select(candidate => (Candidate: candidate, Name: _names.Resolve(candidate.EventKey, GameContentKind.Event)))
            .Where(item => query.Length == 0 || item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .Select(item => item.Candidate)
            .ToArray();

        _empty.Visible = visible.Length == 0;
        _scroll.Visible = visible.Length > 0;
        foreach (EventSearchUiCandidate candidate in visible)
        {
            _grid.AddChild(BuildTile(candidate));
        }
        QueueGridColumnRefresh();
    }

    private bool SourceMatches(EventSearchUiCandidate candidate)
    {
        if (_sharedOnly) return candidate.IsShared;
        if (_variantOnly is ModelKey variant)
        {
            return candidate.VariantActKeys.Contains(variant, ModelKeyComparer.Instance);
        }
        return true;
    }

    private Control BuildTile(EventSearchUiCandidate candidate)
    {
        string displayName = _names!.Resolve(candidate.EventKey, GameContentKind.Event);
        string conditionKey = Beta111EventPresentationKnowledge.RuntimeConditionLocalizationKey(
            _profileId,
            candidate.EventKey,
            _playersCount);
        string? conditionText = string.IsNullOrWhiteSpace(conditionKey)
            ? null
            : _text!.Get(conditionKey);
        if (string.IsNullOrWhiteSpace(conditionText))
        {
            conditionText = null;
        }

        var tile = new EventPickerTile(
            _thumbnails.Resolve(candidate.EventKey),
            displayName,
            _text!.Get(Ui1TextKey.SearchEventTooltipConditionsTitle),
            conditionText,
            _tooltipHost,
            _tileWidth);
        tile.Pressed += () => Commit(candidate);
        return tile;
    }

    private ModelKey[] DistinctVariants()
    {
        var output = new List<ModelKey>();
        var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach (EventSearchUiCandidate candidate in _candidates)
        {
            foreach (ModelKey key in candidate.VariantActKeys)
            {
                if (key.IsValid && seen.Add(key)) output.Add(key);
            }
        }
        return output.ToArray();
    }

    private void Commit(EventSearchUiCandidate candidate)
    {
        Action<EventSearchUiCandidate?>? callback = _onSelected;
        Cancel();
        callback?.Invoke(candidate);
    }

    private void QueueGridColumnRefresh()
    {
        if (!IsInsideTree()) return;
        Callable.From(UpdateGridColumnsFromViewport).CallDeferred();
    }

    private void UpdateGridColumnsFromViewport()
    {
        if (!IsInsideTree() || !Visible) return;
        float renderedWidth = _scroll.Size.X;
        if (renderedWidth <= 1f)
        {
            renderedWidth = Math.Max(_dialog.Size.X, _dialog.CustomMinimumSize.X) - 32f;
        }
        float usableWidth = Math.Max(EventPickerTile.MinWidth, renderedWidth - ReservedScrollBarWidth);
        int columns = Math.Clamp(
            (int)Math.Floor((usableWidth + GridGap) / (EventPickerTile.MinWidth + GridGap)),
            2,
            StandardColumns);
        float nextTileWidth = Math.Clamp(
            (usableWidth - (GridGap * (columns - 1))) / columns,
            EventPickerTile.MinWidth,
            EventPickerTile.MaxWidth);
        bool widthChanged = Math.Abs(nextTileWidth - _tileWidth) > 0.5f;
        _grid.Columns = columns;
        if (widthChanged)
        {
            _tileWidth = nextTileWidth;
            RebuildGrid();
        }
    }

    private void ClearGrid()
    {
        foreach (Node child in _grid.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ClearSourceFilters()
    {
        foreach (Node child in _sourceFilters.GetChildren())
        {
            _sourceFilters.RemoveChild(child);
            child.QueueFree();
        }
    }
}
