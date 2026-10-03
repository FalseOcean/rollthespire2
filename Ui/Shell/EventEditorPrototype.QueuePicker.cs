using Godot;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class EventEditorPrototype
{
    private readonly Control _queuePickerOverlay = new() { Visible = false, MouseFilter = MouseFilterEnum.Stop };
    private readonly Dictionary<BaseButton, bool> _queuePickerBlocked = [];
    private IReadOnlyList<EventSearchUiCandidate> _queuePickerCandidates = [];
    private VBoxContainer? _queuePickerFilters;
    private GridContainer? _queuePickerGrid;
    private ScrollContainer? _queuePickerScroll;
    private LineEdit? _queuePickerSearch;
    private Label? _queuePickerEmpty;
    private AnchoredTooltipHost? _queuePickerTooltipHost;
    private ModelKey? _queuePickerVariant;
    private bool _queuePickerShared;
    private int _queuePickerConditionMode;
    private int _queuePickerResultMode;
    private int _queuePickerPlayers = 1;
    private IUiTextProvider _queuePickerTooltipText = JsonUiTextProvider.CreatePredictorUi13("zh");

    public event Action<bool>? ModalChanged;

    public void AttachOverlay(Control shell)
    {
        _queuePickerOverlay.Reparent(shell);
        _queuePickerOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private void OpenQueuePicker()
    {
        if (_queuePickerOverlay.GetParent() is not Node parent) return;
        _queuePickerCandidates = _draft.Catalog.CandidatesForAct(_draft.QueueAct)
            .DistinctBy(candidate => candidate.EventKey, ModelKeyComparer.Instance)
            .ToArray();
        _queuePickerTooltipText = JsonUiTextProvider.CreatePredictorUi13(_text.LanguageCode);
        _queuePickerVariant = null;
        _queuePickerShared = false;
        _queuePickerConditionMode = 0;
        _queuePickerResultMode = 0;
        _queuePickerBlocked.Clear();
        foreach (Node node in parent.FindChildren("*", "BaseButton", true, false))
            if (node is BaseButton button && !_queuePickerOverlay.IsAncestorOf(button))
            {
                _queuePickerBlocked[button] = button.Disabled;
                button.Disabled = true;
            }

        Clear(_queuePickerOverlay);
        _queuePickerOverlay.Show();
        _queuePickerOverlay.MoveToFront();
        ModalChanged?.Invoke(true);

        var shade = new ColorRect { Color = new Color(0, 0, 0, .68f), MouseFilter = MouseFilterEnum.Stop };
        _queuePickerOverlay.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        shade.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
                CloseQueuePicker();
        };
        var panel = new Panel { Position = new Vector2(160, 96), Size = new Vector2(1280, 708) };
        panel.AddThemeStyleboxOverride("panel", _p.Box(_p.Canvas, _p.Line, 1));
        _queuePickerOverlay.AddChild(panel);

        Text(panel, _text.Format("query.event.picker.title", _draft.QueueAct), 24, 18, 360, 22);
        Button(panel, "×", 1184, 16, 48, () => CloseQueuePicker(), false, 40);

        var filterScroll = new ScrollContainer
        {
            Name = "EventPickerFilterScroll",
            Position = new Vector2(24, 76), Size = new Vector2(220, 600),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        panel.AddChild(filterScroll);
        _queuePickerFilters = new VBoxContainer { CustomMinimumSize = new Vector2(204, 0) };
        _queuePickerFilters.AddThemeConstantOverride("separation", 4);
        filterScroll.AddChild(_queuePickerFilters);

        _queuePickerSearch = new LineEdit
        {
            Position = new Vector2(500, 14), Size = new Vector2(660, 44),
            PlaceholderText = _text.Get("picker.search_name"), ClearButtonEnabled = true
        };
        _queuePickerSearch.AddThemeStyleboxOverride("normal", _p.Box(_p.Surface));
        _queuePickerSearch.AddThemeStyleboxOverride("focus", _p.FocusRing());
        panel.AddChild(_queuePickerSearch);

        _queuePickerScroll = new ScrollContainer
        {
            Position = new Vector2(268, 76), Size = new Vector2(988, 600),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        panel.AddChild(_queuePickerScroll);
        _queuePickerGrid = new GridContainer { Columns = 5 };
        _queuePickerGrid.AddThemeConstantOverride("h_separation", 12);
        _queuePickerGrid.AddThemeConstantOverride("v_separation", 12);
        _queuePickerScroll.AddChild(_queuePickerGrid);
        _queuePickerEmpty = _p.Label(_text.Get("query.event.picker.empty"), 16, true);
        _queuePickerEmpty.Position = new Vector2(268, 86);
        _queuePickerEmpty.Size = new Vector2(900, 32);
        panel.AddChild(_queuePickerEmpty);

        _queuePickerTooltipHost = new AnchoredTooltipHost(new RuntimeRelicTooltipResolver(),
            new RuntimePotionTooltipResolver(), new RuntimeCardTooltipResolver());
        _queuePickerOverlay.AddChild(_queuePickerTooltipHost);
        _queuePickerTooltipHost.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _queuePickerSearch.TextChanged += _ => RebuildQueuePickerGrid();
        RebuildQueuePickerFilters();
        RebuildQueuePickerGrid();
        _queuePickerSearch.GrabFocus();
    }

    private void RebuildQueuePickerFilters()
    {
        if (_queuePickerFilters is null) return;
        Clear(_queuePickerFilters);
        _queuePickerFilters.AddChild(_p.Label(_text.Get("query.event.picker.source"), 18, true));
        QueuePickerFilter(_text.Get("query.event.picker.all"), !_queuePickerShared && !_queuePickerVariant.HasValue,
            () => { _queuePickerShared = false; _queuePickerVariant = null; });
        if (_queuePickerCandidates.Any(candidate => candidate.IsShared))
            QueuePickerFilter(_text.Get("query.event.pool.shared"), _queuePickerShared,
                () => { _queuePickerShared = true; _queuePickerVariant = null; });
        foreach (ModelKey variant in _queuePickerCandidates.SelectMany(candidate => candidate.VariantActKeys)
                     .Distinct(ModelKeyComparer.Instance))
        {
            ModelKey selectedVariant = variant;
            QueuePickerFilter(_names.Resolve(variant, GameContentKind.Act), _queuePickerVariant == variant,
                () => { _queuePickerShared = false; _queuePickerVariant = selectedVariant; });
        }
        _queuePickerFilters.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        _queuePickerFilters.AddChild(_p.Label(_text.Get("query.event.picker.results"), 18, true));
        QueuePickerFilter(_text.Get("query.event.picker.all"), _queuePickerResultMode == 0,
            () => _queuePickerResultMode = 0);
        QueuePickerFilter(_text.Get("query.event.picker.results_supported"), _queuePickerResultMode == 1,
            () => _queuePickerResultMode = 1);
        QueuePickerFilter(_text.Get("query.event.picker.appearance_only"), _queuePickerResultMode == 2,
            () => _queuePickerResultMode = 2);
        _queuePickerFilters.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        _queuePickerFilters.AddChild(_p.Label(_text.Get("query.event.picker.conditions"), 18, true));
        QueuePickerFilter(_text.Get("query.event.picker.all"), _queuePickerConditionMode == 0,
            () => _queuePickerConditionMode = 0);
        QueuePickerFilter(_text.Get("query.event.picker.known"), _queuePickerConditionMode == 1,
            () => _queuePickerConditionMode = 1);
        QueuePickerFilter(_text.Get("query.event.picker.other"), _queuePickerConditionMode == 2,
            () => _queuePickerConditionMode = 2);
    }

    private void QueuePickerFilter(string title, bool selected, Action select)
    {
        var button = _p.Button(title);
        button.CustomMinimumSize = new Vector2(204, 36);
        button.Alignment = HorizontalAlignment.Left;
        button.AddThemeConstantOverride("h_separation", 8);
        button.AddThemeFontSizeOverride("font_size", 15);
        foreach ((string state, string fill) in new[]
                 { ("normal", _p.Canvas), ("hover", _p.Hover), ("pressed", _p.Line), ("disabled", _p.Canvas) })
        {
            var style = _p.Box(fill);
            style.BorderColor = _p.Color(_p.Selected);
            style.BorderWidthLeft = selected ? 3 : 0;
            button.AddThemeStyleboxOverride(state, style);
        }
        button.Pressed += () => { select(); RebuildQueuePickerFilters(); RebuildQueuePickerGrid(); };
        _queuePickerFilters!.AddChild(button);
    }

    private bool QueuePickerHasCondition(EventSearchUiCandidate candidate) =>
        Beta111EventPresentationKnowledge.ShouldShowRuntimeConditionMarker(
            _runtime.Profile.ProfileId, candidate.EventKey, _queuePickerPlayers);

    private void RebuildQueuePickerGrid()
    {
        if (_queuePickerGrid is null || _queuePickerSearch is null || _queuePickerEmpty is null || _queuePickerScroll is null)
            return;
        _queuePickerTooltipHost?.Dismiss();
        Clear(_queuePickerGrid);
        string search = _queuePickerSearch.Text.Trim();
        EventSearchUiCandidate[] visible = _queuePickerCandidates
            .Where(candidate => !_queuePickerShared || candidate.IsShared)
            .Where(candidate => !_queuePickerVariant.HasValue ||
                candidate.VariantActKeys.Contains(_queuePickerVariant.Value, ModelKeyComparer.Instance))
            .Where(candidate => _queuePickerConditionMode == 0 ||
                QueuePickerHasCondition(candidate) == (_queuePickerConditionMode == 1))
            .Where(candidate => _queuePickerResultMode == 0 ||
                (EventResultPrototypeWhitelist.Find(candidate.EventKey) is not null) == (_queuePickerResultMode == 1))
            .Where(candidate => search.Length == 0 || _names.Resolve(candidate.EventKey, GameContentKind.Event)
                .Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        _queuePickerEmpty.Visible = visible.Length == 0;
        _queuePickerScroll.Visible = visible.Length > 0;
        foreach (EventSearchUiCandidate candidate in visible)
        {
            ModelKey key = candidate.EventKey;
            string name = _names.Resolve(key, GameContentKind.Event);
            const float tileWidth = 174;
            const float artworkSize = 150;
            var tile = _p.Button(string.Empty);
            tile.CustomMinimumSize = new Vector2(tileWidth, 224);
            tile.ClipContents = true;
            string condition = QueuePickerCondition(candidate);
            tile.MouseEntered += () =>
            {
                if (_queuePickerTooltipHost is null) return;
                if (string.IsNullOrWhiteSpace(condition)) _queuePickerTooltipHost.ShowText(tile, name);
                else _queuePickerTooltipHost.ShowStructuredText(tile, name,
                    _queuePickerTooltipText.Get(Ui1TextKey.SearchEventTooltipConditionsTitle), condition);
            };
            tile.MouseExited += () => _queuePickerTooltipHost?.Dismiss(tile);
            tile.TreeExiting += () => _queuePickerTooltipHost?.Dismiss(tile);
            tile.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            tile.AddThemeStyleboxOverride("hover", _p.Box(_p.Hover));
            var label = _p.Label(name, 15);
            label.Position = new Vector2(5, 6);
            label.Size = new Vector2(tileWidth - 10, 54);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MouseFilter = MouseFilterEnum.Ignore;
            tile.AddChild(label);
            tile.AddChild(new EventThumbnailView(_thumbnails.Resolve(key),
                new Vector2(artworkSize, artworkSize), EventThumbnailPresentation.PickerSquareCrop)
            { Position = new Vector2(12, 64), Size = new Vector2(artworkSize, artworkSize) });
            tile.Pressed += () =>
            {
                CloseQueuePicker();
                AddQueueCondition(key);
            };
            _queuePickerGrid.AddChild(tile);
        }
        _queuePickerScroll.ScrollVertical = 0;
    }

    private string QueuePickerCondition(EventSearchUiCandidate candidate)
    {
        string key = Beta111EventPresentationKnowledge.RuntimeConditionLocalizationKey(
            _runtime.Profile.ProfileId, candidate.EventKey, _queuePickerPlayers);
        return string.IsNullOrWhiteSpace(key) ? string.Empty : _queuePickerTooltipText.Get(key);
    }

    public bool CloseQueuePicker()
    {
        if (!_queuePickerOverlay.Visible) return false;
        _queuePickerTooltipHost?.Dismiss();
        _queuePickerOverlay.Hide();
        Clear(_queuePickerOverlay);
        _queuePickerTooltipHost = null;
        foreach ((BaseButton button, bool disabled) in _queuePickerBlocked)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = disabled;
        _queuePickerBlocked.Clear();
        _queuePickerCandidates = [];
        _queuePickerFilters = null;
        _queuePickerGrid = null;
        _queuePickerScroll = null;
        _queuePickerSearch = null;
        _queuePickerEmpty = null;
        ModalChanged?.Invoke(false);
        return true;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!_queuePickerOverlay.Visible || @event is not InputEventKey key || !key.Pressed || key.Echo ||
            key.Keycode != Key.Escape) return;
        CloseQueuePicker();
        GetViewport().SetInputAsHandled();
    }
}
