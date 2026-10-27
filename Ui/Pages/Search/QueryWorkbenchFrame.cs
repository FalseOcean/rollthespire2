using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Pages.Search.Editors;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search;

/// <summary>Search authoring and presentation. Editors export intent; execution
/// commands and received state belong to the shared workspace.</summary>
internal sealed partial class QueryWorkbenchFrame : Control
{
    // The shell places this 1536 x 772 canvas at (32,96) in its logical 1600 x 900 surface.
    private const float CanvasWidth = WorkspaceLayout.CanvasWidth, CanvasHeight = WorkspaceLayout.CanvasHeight;
    private const float LeftRailWidth = WorkspaceLayout.LeftRailWidth, RightRailWidth = WorkspaceLayout.RightRailWidth, RailGap = WorkspaceLayout.RailGap;
    private const float AuthoringHeaderHeight = 32, DockHeight = 32, ContextControlHeight = 32;
    private const float ConditionActionsHeight = 32, ContextTop = 32;
    private const float ColumnInset = WorkspaceLayout.ColumnInset, RailContentWidth = LeftRailWidth - ColumnInset * 2;
    private const float SelectorColumnStride = 108, SelectorWidth = 100, CenterTop = 0;
    private const float DockTop = CanvasHeight - DockHeight;
    private const float PresetActionsTop = CanvasHeight - AuthoringHeaderHeight;
    private const float CenterLeft = WorkspaceLayout.CenterLeft;
    private const float RightRailLeft = WorkspaceLayout.RightRailLeft;
    private const float CenterWidth = WorkspaceLayout.CenterWidth;
    private float CharacterTop => _multiplayer ? ContextTop + 124 : ContextTop + 42;
    private float ConditionActionsTop => CharacterTop + (_multiplayer ? 110 : 86);
    private float DomainTop => ConditionActionsTop + ConditionActionsHeight + 42;
    private const float DomainRowHeight = 40;
    private ScrollContainer? _domainScroll;
    private Label? _currentUnlockStatus;

    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("query-workbench-frame");
    private readonly ISearchCategoryTabIconProvider _domainIcons;
    private readonly NativeCharacterPoolIconProvider _characterIcons;
    private readonly Dictionary<string, Button> _domainButtons = [];
    private readonly Dictionary<string, Label> _domainCountLabels = [];
    private string _language = "zh", _selectedDomain = "neow";
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    // Product UI cap; the backend party collection has no four-player limit.
    private const int MaximumPreviewPlayers = 4;
    private bool _multiplayer;
    private int _seat, _playerCount = 2;
    private readonly MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState?[] _lobbyUnlocks = Enumerable.Range(0, MaximumPreviewPlayers).Select(_ => (MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState?)MegaCrit.Sts2.Core.Unlocks.UnlockState.all.ToSerializable()).ToArray();
    private readonly int[] _unlockReadStatus = Enumerable.Repeat(4, MaximumPreviewPlayers).ToArray(); // 0 unread, 1 copied, 2 failed, 3 absent, 4 assumed full.
    private readonly Dictionary<int, Button> _seatButtons = [];
    private MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby? _readLobby;
    private string _readRoster = "";
    private double _rosterPoll;
    private readonly Control _configOverlay = new() { Visible = false, Theme = MainPanelTheme.Create() };
    // Godot embeds popup Windows on canvas layer 1024. This text-only layer
    // remains above them without creating another window or intercepting input.
    private readonly CanvasLayer _searchTooltipLayer = new() { Layer = 1025, Name = "SearchTextTooltips" };
    private readonly AnchoredTooltipHost _searchTooltipHost = new(new RuntimeRelicTooltipResolver(),
        new RuntimePotionTooltipResolver(), new RuntimeCardTooltipResolver());
    private readonly Dictionary<Button, bool> _configBlocked = [];
    private Button? _configButton;
    private ModelKey _soloCharacter = BaseGameModelKeys.Characters.Ironclad;
    private int _soloAscension = 10;
    private readonly ModelKey[] _seatCharacters = Enumerable.Repeat(BaseGameModelKeys.Characters.Ironclad, MaximumPreviewPlayers).ToArray();

    private readonly ModelKey[] _characters = RuntimeCharacterCatalogCapture.Capture().EffectiveCharacters.ToArray();
    private Label? _currentDomain;
    private readonly ObjectPickerPanel _objectPicker;
    private readonly NeowSearchEditor _neowEditor;
    private readonly CombatRewardSearchEditor _combatEditor;
    private readonly ShopSearchEditor _shopEditor;
    private readonly RelicSequenceSearchEditor _relicEditor;
    private readonly AncientSearchEditor _ancientEditor;
    private readonly EventSearchEditor _eventEditor;
    private readonly ActInformationSearchEditor _actInformationEditor;
    private readonly TransformationSearchEditor _transformationEditor;
    public event Action<string>? Navigate;
    public event Action<bool>? ModalChanged;
    internal event Action<string?>? CardCollectionEditRequested;
    internal void ConfigureCardCollections(RolltheSpire2.Ui.Persistence.CardCollectionStore store) => _combatEditor.ConfigureCardCollections(store);

    public QueryWorkbenchFrame(RolltheSpire2.Bootstrap.ModRuntimeSnapshot runtime,
        RolltheSpire2.Ui.Workspace.SearchWorkspace persistence)
    {
        _runtime = runtime; _persistence = persistence;
        _domainIcons = new SearchCategoryTabIconProvider(_icons);
        _objectPicker = new ObjectPickerPanel();
        AddChild(_objectPicker);
        _neowEditor = new NeowSearchEditor(runtime, _objectPicker);
        AddChild(_neowEditor);
        _combatEditor = new CombatRewardSearchEditor(runtime, _objectPicker,
            (players, seat) => players > 1 ? _neowEditor.ExportPartyConditions(seat) : _neowEditor.ExportQuery()) { Visible = false };
        AddChild(_combatEditor);
        _shopEditor = new ShopSearchEditor(runtime, _objectPicker) { Visible = false };
        AddChild(_shopEditor);
        _relicEditor = new RelicSequenceSearchEditor(runtime, _objectPicker) { Visible = false };
        AddChild(_relicEditor);
        _ancientEditor = new AncientSearchEditor(runtime) { Visible = false };
        AddChild(_ancientEditor);
        _eventEditor = new EventSearchEditor(runtime) { Visible = false, Picker = _objectPicker };
        AddChild(_eventEditor);
        _actInformationEditor = new ActInformationSearchEditor(runtime, persistence) { Visible = false };
        AddChild(_actInformationEditor);
        _transformationEditor = new TransformationSearchEditor(PickTransformationCards) { Visible = false };
        AddChild(_transformationEditor);
        _neowEditor.TransformTakenOver = (slot, source) => !_multiplayer && _transformationEditor.IsTakenOver(slot, source);
        _eventEditor.TransformTakenOver = (slot, source) => !_multiplayer && _transformationEditor.IsTakenOver(slot, source);
        _transformationEditor.TakeoverChanged += (seat, sourceId) =>
        {
            // Takeover changes the active projection, never the saved source requirements.
            using var observation = _editorInputs.Observe(_runtime);
            RefreshTransformationContext();
            _neowEditor.Refresh(_language, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
                _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
                _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, sharedContext: ActiveEditorContext());
            RefreshCombatContext();
            RefreshEventContext();
        };
        _objectPicker.ModalChanged += open => ModalChanged?.Invoke(open);
        _eventEditor.ModalChanged += open => ModalChanged?.Invoke(open);
        _neowEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:neow");
        _combatEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:combat");
        _combatEditor.CardCollectionEditRequested += id => CardCollectionEditRequested?.Invoke(id);
        _shopEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:shop");
        _relicEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:relics");
        _ancientEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:ancient");
        _eventEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:events");
        _actInformationEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:" + (_selectedDomain == "map" ? "map" : "boss"));
        _transformationEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:transform");
        _transformationEditor.SourcePageRequested += SelectDomain;
        _transformationEditor.SourceEditorRequested += source =>
        {
            bool isEvent = source.Id.StartsWith("E.", StringComparison.Ordinal);
            SelectDomain(isEvent ? "events" : "neow");
            if (isEvent) _eventEditor.FocusEvent(source.Identity);
        };
        _eventEditor.TransformationRequested += source =>
        {
            if (_multiplayer) return;
            RefreshTransformationContext();
            if (_transformationEditor.SetSourceTakenOver(source, true)) SelectDomain("transform");
        };
        _neowEditor.OpeningChanged += OpeningContextChanged;
        _eventEditor.QueueChanged += EventQueueChanged;
        _characterIcons = new NativeCharacterPoolIconProvider(_icons);
        Name = "QueryWorkbenchFrame";
        AddChild(_configOverlay);
        AddChild(_searchTooltipLayer);
        _searchTooltipLayer.AddChild(_searchTooltipHost);
        SearchTooltip.AttachScope(this, _searchTooltipHost);
        VisibilityChanged += () =>
        {
            bool visible = IsVisibleInTree();
            if (GodotObject.IsInstanceValid(_searchTooltipLayer)) _searchTooltipLayer.Visible = visible;
            if (visible) RefreshEnvironmentInputs();
            if (!visible)
            {
                SuspendProbabilityPreview();
                if (GodotObject.IsInstanceValid(_searchTooltipHost)) _searchTooltipHost.Dismiss();
            }
        };
    }

    public void AttachOverlay(Control shell)
    {
        _configOverlay.Reparent(shell); _configOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _objectPicker.AttachOverlay(shell, this); _ancientEditor.AttachOverlay(shell); _eventEditor.AttachOverlay(shell); AttachPresetOverlays(shell);
        _searchTooltipLayer.Reparent(shell);
        SearchTooltip.AttachScope(this, _searchTooltipHost, shell);
        SearchTooltip.AttachScope(_configOverlay, this);
    }

    public override void _Process(double delta)
    {
        // Execution/receipts/checkpoints continue in SearchWorkspaceRuntime.
        // This retained page only does presentation while its ancestors are visible.
        if (!IsVisibleInTree()) return;
        PollProduction(delta);
        UpdateRuntimeVisibility();
        UpdateDomainCounts(); UpdateConditionActions();
        if (_readLobby is null) return;
        _rosterPoll -= delta;
        if (_rosterPoll > 0) return;
        _rosterPoll = 1;
        // A captured player's status must not silently transfer to a replacement in the same slot.
        var current = LobbyUnlockReadout.Find(GetTree().Root);
        if (ReferenceEquals(current, _readLobby) && LobbyUnlockReadout.Roster(current!) == _readRoster) return;
        _readLobby = null; _readRoster = "";
        Array.Clear(_lobbyUnlocks); Array.Clear(_unlockReadStatus); UpdateSeatReadLabels();
        RefreshEditorContexts();
    }

    public bool CloseModal()
    {
        if (ClosePresetModal()) return true;
        if (_objectPicker.ClosePicker()) return true;
        if (_eventEditor.CloseQueuePicker()) return true;
        if (!_configOverlay.Visible) return false;
        _configOverlay.Hide();
        foreach (var (button, disabled) in _configBlocked)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = disabled;
        _configBlocked.Clear(); ModalChanged?.Invoke(false);
        if (_configButton is not null && GodotObject.IsInstanceValid(_configButton)) _configButton.GrabFocus();
        return true;
    }

    public override void _ExitTree()
    {
        CloseSearch();
        TryAuxiliary(() =>
        {
            if (GodotObject.IsInstanceValid(_objectPicker))
            {
                _objectPicker.ClosePicker();
                if (_objectPicker.GetParent() != this) _objectPicker.QueueFree();
            }
            SearchTooltip.DetachScope(this);
            SearchTooltip.DetachScope(_configOverlay);
            if (GodotObject.IsInstanceValid(_searchTooltipLayer) && _searchTooltipLayer.GetParent() != this)
                _searchTooltipLayer.QueueFree();
            _characterIcons.Dispose();
        }, "ExitPresentation");
    }
}
