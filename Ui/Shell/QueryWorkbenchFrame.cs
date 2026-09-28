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

namespace RolltheSpire2.Ui.Shell;

/// <summary>Production Workbench shell. Editors export typed intent; the Search partial
/// binds persistence, compilation and the existing Production session.</summary>
internal sealed partial class QueryWorkbenchFrame : Control
{
    // Canonical 1536 × 772 task canvas inside the 1600 × 900 Shell.
    // These are the Owner's next visual iteration knobs; all region bounds derive from them.
    private const float CanvasWidth = 1536, CanvasHeight = 772;
    private const float LeftRailWidth = 224, RightRailWidth = 304, RailGap = 24;
    private const float AuthoringHeaderHeight = 36, HeaderGap = 16, DockHeight = 32;
    private const float RegionInset = 24, DomainRowHeight = 44;
    private const float ContextControlHeight = 32;
    private float CharacterTop => BodyTop + (_multiplayer ? 120 + ((_playerCount + 1) / 2 - 1) * 32 : 52);
    private float DomainTop => CharacterTop + 104;
    private const float SelectorColumnStride = 116, SelectorWidth = 100;
    private const float CenterTop = 0;
    private const float ConditionActionsTop = AuthoringHeaderHeight + 8, ConditionActionsHeight = AuthoringHeaderHeight;
    private const float BodyTop = ConditionActionsTop + ConditionActionsHeight + HeaderGap;
    private const float DockTop = CanvasHeight - DockHeight;
    private const float BodyHeight = CanvasHeight - BodyTop;
    private const float CenterLeft = LeftRailWidth + RailGap;
    private const float RightRailLeft = CanvasWidth - RightRailWidth;
    private const float CenterWidth = RightRailLeft - RailGap - CenterLeft;

    private readonly WorkspacePalette _p = WorkspacePalette.Canonical;
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
    private readonly Control _configOverlay = new() { Visible = false };
    private readonly Dictionary<Button, bool> _configBlocked = [];
    private Button? _configButton;
    private ModelKey _soloCharacter = BaseGameModelKeys.Characters.Ironclad;
    private int _soloAscension = 10;
    private readonly ModelKey[] _seatCharacters = Enumerable.Repeat(BaseGameModelKeys.Characters.Ironclad, MaximumPreviewPlayers).ToArray();

    private readonly ModelKey[] _characters = RuntimeCharacterCatalogCapture.Capture().EffectiveCharacters.ToArray();
    private Label? _currentDomain;
    private readonly NeowEditorPrototype _neowEditor;
    private readonly CombatRewardEditorPrototype _combatEditor;
    private readonly ShopEditorPrototype _shopEditor;
    private readonly RelicSequenceEditorPrototype _relicEditor;
    private readonly AncientEditorPrototype _ancientEditor;
    private readonly EventEditorPrototype _eventEditor;
    private readonly ActInformationEditorPrototype _actInformationEditor;
    private readonly TransformationEditorPrototype _transformationEditor;
    public event Action<string>? Navigate;
    public event Action<bool>? ModalChanged;

    public QueryWorkbenchFrame(RolltheSpire2.Bootstrap.ModRuntimeSnapshot runtime,
        RolltheSpire2.Ui.Persistence.SearchWorkspacePersistence persistence)
    {
        _runtime = runtime; _persistence = persistence;
        _domainIcons = new SearchCategoryTabIconProvider(_icons);
        _neowEditor = new NeowEditorPrototype(runtime);
        AddChild(_neowEditor);
        _combatEditor = new CombatRewardEditorPrototype(runtime, _neowEditor) { Visible = false };
        AddChild(_combatEditor);
        _shopEditor = new ShopEditorPrototype(runtime, _neowEditor) { Visible = false };
        AddChild(_shopEditor);
        _relicEditor = new RelicSequenceEditorPrototype(runtime, _neowEditor) { Visible = false };
        AddChild(_relicEditor);
        _ancientEditor = new AncientEditorPrototype(runtime) { Visible = false };
        AddChild(_ancientEditor);
        _eventEditor = new EventEditorPrototype(runtime) { Visible = false, PickerHost = _neowEditor };
        AddChild(_eventEditor);
        _actInformationEditor = new ActInformationEditorPrototype(runtime, persistence) { Visible = false };
        AddChild(_actInformationEditor);
        _transformationEditor = new TransformationEditorPrototype((candidates, selected) =>
            _neowEditor.PickTransformationCards(_multiplayer ? _seat : 0, candidates, selected)) { Visible = false };
        AddChild(_transformationEditor);
        _neowEditor.TransformTakenOver = (slot, source) => !_multiplayer && _transformationEditor.IsTakenOver(slot, source);
        _eventEditor.TransformTakenOver = (slot, source) => !_multiplayer && _transformationEditor.IsTakenOver(slot, source);
        _transformationEditor.TakeoverChanged += (seat, sourceId) =>
        {
            if (_transformationEditor.IsTakenOver(seat, sourceId))
            {
                if (sourceId.StartsWith("N.", StringComparison.Ordinal)) _neowEditor.ClearTakenOverTransformTargets(seat);
                else _eventEditor.ClearTakenOverTransformTargets(seat, sourceId);
            }
            RefreshTransformationContext();
            _neowEditor.Refresh(_language, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
                _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
                _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null);
            RefreshEventContext();
        };
        _neowEditor.ModalChanged += open => ModalChanged?.Invoke(open);
        _eventEditor.ModalChanged += open => ModalChanged?.Invoke(open);
        _neowEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:neow");
        _combatEditor.GuideRequested += () => Navigate?.Invoke("encyclopedia:combat");
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
        _neowEditor.OpeningChanged += () => RefreshCombatContext();
        _neowEditor.OpeningChanged += () => { if (!_multiplayer) RefreshTransformationContext(); };
        _eventEditor.QueueChanged += () => RefreshTransformationContext();
        _characterIcons = new NativeCharacterPoolIconProvider(_icons);
        Name = "QueryWorkbenchFrame";
        AddChild(_configOverlay);
    }

    public void AttachOverlay(Control shell)
    { _configOverlay.Reparent(shell); _configOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _neowEditor.AttachOverlay(shell); _eventEditor.AttachOverlay(shell); AttachPresetOverlays(shell); }

    public void Refresh(string language, IUiTextProvider text)
    {
        CloseModal();
        _language = language; _text = text;
        bool firstRefresh = !_restored;
        if (firstRefresh)
        {
            _restored = true;
            // Restore context before drawing its mode, seat and character controls.
            try { if (_persistence.LoadWorkbench() is { } saved) RestoreDraft(saved, render: false); }
            catch (Exception ex) { _loadFailed = true; _lastIssue = ex.Message; RolltheSpire2.Bootstrap.RuntimeLog.Warn("workbenchRestore=" + ex); }
        }
        foreach (var child in GetChildren())
        { if (child != _configOverlay && child != _neowEditor && child != _combatEditor && child != _shopEditor && child != _relicEditor && child != _ancientEditor && child != _eventEditor && child != _actInformationEditor && child != _transformationEditor) { RemoveChild(child); child.QueueFree(); } }
        _domainButtons.Clear();
        _domainCountLabels.Clear();
        _seatButtons.Clear();
        var names = RuntimeGameContentNameResolver.Create(language);
        const float presetWidth = (LeftRailWidth - 16) / 2;
        var back = _p.CompactButton(_text.Get("preset.title")); back.Name = "OpenPresets";
        Place(back, 0, 0, presetWidth, AuthoringHeaderHeight);
        back.Pressed += OpenPresets;
        var savePreset = _p.CompactButton(_language == "zh" ? "保存预设" : "Save"); savePreset.Name = "SavePreset";
        savePreset.TooltipText = _language == "zh" ? "保存当前条件为预设" : "Save current conditions as a preset";
        Place(savePreset, presetWidth + 8, 0, presetWidth, AuthoringHeaderHeight);
        savePreset.Pressed += () => OpenPresetSave(Components.SearchPresetSaveIntentKind.CreateCurrentQuery, "");

        Line(LeftRailWidth + RailGap / 2, BodyTop, 1, BodyHeight - 24);
        Line(RightRailLeft - RailGap / 2, 0, 1, CanvasHeight);
        SearchContext(names);
        float domainTop = DomainTop;
        Line(8, domainTop - 16, LeftRailWidth - 24, 1);

        (string id, string key)[] domains =
        [
            ("neow", "query.domain.neow"), ("ancient", "query.domain.ancients"),
            ("shop", "query.domain.shop"), ("combat", "query.domain.combat_rewards"),
            ("events", "query.domain.events"), ("boss", "query.domain.boss_variant"),
            ("map", "query.domain.map"),
            ("relics", "query.domain.relics")
        ];
        for (int i = 0; i < domains.Length; i++)
            Domain(domains[i].id, _text.Get(domains[i].key), domainTop + i * DomainRowHeight);
        if (!_multiplayer)
        {
            float compositeTop = domainTop + domains.Length * DomainRowHeight + 20;
            Line(16, compositeTop - 12, LeftRailWidth - 40, 1);
            Domain("transform", _text.Get("query.domain.transform"), compositeTop);
        }
        if (firstRefresh) RestoreUiPreferences();

        _currentDomain = Text(string.Empty, 14, CenterLeft + 8, CenterTop, true);
        _currentDomain.Size = new Vector2(CenterWidth - 16, 24);
        _neowEditor.Position = new(CenterLeft + 8, CenterTop + 30);
        _neowEditor.Size = new(CenterWidth - 16, DockTop - CenterTop - 38);
        _combatEditor.Position = _neowEditor.Position;
        _combatEditor.Size = _neowEditor.Size;
        _shopEditor.Position = _neowEditor.Position;
        _shopEditor.Size = _neowEditor.Size;
        _relicEditor.Position = _neowEditor.Position;
        _relicEditor.Size = _neowEditor.Size;
        _ancientEditor.Position = _neowEditor.Position;
        _ancientEditor.Size = _neowEditor.Size;
        _eventEditor.Position = _neowEditor.Position;
        _eventEditor.Size = _neowEditor.Size;
        _actInformationEditor.Position = _neowEditor.Position;
        _actInformationEditor.Size = _neowEditor.Size;
        _transformationEditor.Position = _neowEditor.Position;
        _transformationEditor.Size = _neowEditor.Size;
        RefreshEditorContexts();
        SelectDomain(_selectedDomain);
        UpdateDomainCounts();

        BuildConditionActions();
        BuildProductionDock();
    }

    private void SearchContext(IGameContentNameResolver names)
    {
        for (int i = 0; i < 2; i++)
        {
            bool multi = i == 1;
            var mode = ContextButton(_text.Get(multi ? "query.context.multiplayer" : "query.context.singleplayer"), i * SelectorColumnStride, BodyTop, SelectorWidth);
            mode.AddThemeFontSizeOverride("font_size", 16);
            _p.SetActive(mode, _multiplayer == multi, horizontalPadding: 6);
            FitContextButton(mode, SelectorWidth, ContextControlHeight);
            mode.Pressed += () => SetSearchMode(multi);
        }
        if (_multiplayer)
        {
            _configButton = ContextButton(_text.Get("query.context.multiplayer_setup"), 0, BodyTop + 44, LeftRailWidth - 8);
            _configButton.Pressed += OpenMultiplayerConfig;
            for (int i = 0; i < _playerCount; i++)
            {
                int seat = i;
                var tab = ContextButton(SeatCaption(i), (i % 2) * SelectorColumnStride, BodyTop + 84 + (i / 2) * 32, SelectorWidth);
                tab.CustomMinimumSize = new Vector2(0, 28); tab.Size = new Vector2(SelectorWidth, 28);
                tab.AddThemeFontSizeOverride("font_size", 14);
                _p.SetActive(tab, _seat == i, horizontalPadding: 6);
                FitContextButton(tab, SelectorWidth, 28);
                _seatButtons[i] = tab;
                tab.Pressed += () => { _seat = seat; Refresh(_language, _text); };
            }
        }
        float characterTop = CharacterTop;
        ModelKey character = _multiplayer ? _seatCharacters[_seat] : _soloCharacter;
        var picker = ContextOptions(0, characterTop, LeftRailWidth - 8);
        picker.ExpandIcon = true;
        picker.AddThemeConstantOverride("icon_max_width", 28);
        for (int i = 0; i < _characters.Length; i++)
        {
            ModelKey key = _characters[i];
            picker.AddIconItem(_characterIcons.Resolve(key).Texture, names.Resolve(key, GameContentKind.Character));
            if (key == character) picker.Select(i);
        }
        picker.ItemSelected += index =>
        {
            if (_multiplayer) _seatCharacters[_seat] = _characters[(int)index];
            else _soloCharacter = _characters[(int)index];
            RefreshEditorContexts();
        };
        Text(_multiplayer ? (_language == "zh" ? "共享进阶" : "Shared asc.") : _text.Get("query.context.ascension"), 18, 8, characterTop + 44, true);
        var ascension = ContextOptions(SelectorColumnStride, characterTop + 40, SelectorWidth);
        for (int n = SeedPredictionInputLimits.MinimumAscension; n <= SeedPredictionInputLimits.MaximumAscension; n++)
            ascension.AddItem($"A{n}", n);
        ascension.Select(ascension.GetItemIndex(_multiplayer ? _partyAscension : _soloAscension));
        ascension.ItemSelected += index =>
        {
            int value = ascension.GetItemId((int)index);
            if (_multiplayer) _partyAscension = value;
            else _soloAscension = value;
            RefreshEditorContexts();
        };
    }

    private void RefreshEditorContexts(bool render = true)
    {
        if (_multiplayer)
        {
            for (int seat = 0; seat < _playerCount; seat++)
            {
                _neowEditor.Refresh(_language, _seatCharacters[seat], _partyAscension, _playerCount, seat, _lobbyUnlocks[seat], render: false);
            }
            _neowEditor.Refresh(_language, _seatCharacters[_seat], _partyAscension, _playerCount, _seat, _lobbyUnlocks[_seat], render: render);
            var union = _lobbyUnlocks.Take(_playerCount).All(u => u is not null)
                ? new MegaCrit.Sts2.Core.Unlocks.UnlockState(_lobbyUnlocks.Take(_playerCount).Select(u => MegaCrit.Sts2.Core.Unlocks.UnlockState.FromSerializable(u!))).ToSerializable() : null;
            _actInformationEditor.Refresh(_language, _text, _seatCharacters[0], _partyAscension, _playerCount, 0, union, render: render,
                partyWorld: EditorParty()?.World);
            RefreshCombatContext(render);
            RefreshShopContext(render);
            RefreshRelicContext(render);
            RefreshAncientContext(render);
            RefreshEventContext(render);
            return;
        }
        _neowEditor.Refresh(_language, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render);
        RefreshCombatContext(render);
        RefreshShopContext(render);
        RefreshRelicContext(render);
        RefreshAncientContext(render);
        RefreshEventContext(render);
        RefreshActInformationContext(render);
        RefreshTransformationContext(render: render);
    }

    private void RefreshCombatContext(bool render = true)
    {
        _combatEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render);
    }

    private void RefreshShopContext(bool render = true)
    {
        _shopEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render);
    }

    private void RefreshRelicContext(bool render = true)
    {
        _relicEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render);
    }

    private void RefreshAncientContext(bool render = true)
    {
        _ancientEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render,
                partyAuthority: EditorParty()?.Players[_seat]);
    }

    private void RefreshEventContext(bool render = true)
    {
        _eventEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render,
                partyAuthority: EditorParty()?.Players[_seat]);
    }

    private void RefreshActInformationContext(bool render = true)
    {
        _actInformationEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render);
    }

    private void RefreshTransformationContext(int? targetSeat = null, bool render = true)
    {
        int seat = targetSeat ?? (_multiplayer ? _seat : 0);
        TransformationPrototypeSource[] sources = _neowEditor.TransformationSources(seat)
            .Concat(_eventEditor.TransformationSources(seat)).ToArray();
        _transformationEditor.Refresh(_language, _text, seat, sources,
            _neowEditor.TransformationCardCandidates(seat).Concat(_eventEditor.TransformationCardCandidates).ToArray(), render);
    }

    private void OpenMultiplayerConfig()
    {
        foreach (var child in _configOverlay.GetChildren()) { _configOverlay.RemoveChild(child); child.QueueFree(); }
        _configBlocked.Clear();
        void Block(Node node)
        {
            if (node == _configOverlay) return;
            if (node is Button button) { _configBlocked[button] = button.Disabled; button.Disabled = true; }
            foreach (var child in node.GetChildren()) Block(child);
        }
        Block(this);
        var shade = new ColorRect { Color = new Color(0, 0, 0, .65f), MouseFilter = MouseFilterEnum.Stop };
        _configOverlay.AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new Panel { Position = new Vector2(540, 234), Size = new Vector2(520, 488) };
        panel.AddThemeStyleboxOverride("panel", _p.Box(_p.Surface, _p.Line, 1));
        _configOverlay.AddChild(panel);
        void Caption(string text, int size, float x, float y)
        { var label = _p.Label(text, size); panel.AddChild(label); label.Position = new Vector2(x, y); }
        Caption(_text.Get("query.context.multiplayer_dialog.title"), 26, 32, 24);
        Caption(_text.Get("query.context.player_count"), 20, 32, 94);
        Caption(_language == "zh" ? "领取顺序：P1 → P2 → P3 → P4" : "Pickup order: P1 → P2 → P3 → P4", 20, 32, 158);
        var count = ContextOptions(0, 0, 208);
        count.Reparent(panel, false); count.Position = new Vector2(280, 88);
        for (int n = 2; n <= MaximumPreviewPlayers; n++) count.AddItem(_text.Format("query.context.player_count_value", n), n);
        count.Select(count.GetItemIndex(_playerCount));
        var read = _p.Button(_text.Get("query.context.read_unlocks"));
        panel.AddChild(read); read.Position = new Vector2(32, 216); read.Size = new Vector2(456, 44);
        var full = _p.Button(_language == "zh" ? "使用全员全解锁（默认）" : "Use fully unlocked party (default)");
        panel.AddChild(full); full.Position = new Vector2(32, 268); full.Size = new Vector2(456, 44);
        var readMessage = _p.Label(_language == "zh" ? "默认全解锁；有未解锁玩家时可主动读取大厅。" : "Defaults to full unlocks; read the lobby for actual progress.", 18, true);
        panel.AddChild(readMessage); readMessage.Position = new Vector2(32, 324); readMessage.Size = new Vector2(456, 48);
        readMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        read.Pressed += () => readMessage.Text = ReadLobbyUnlocks();
        full.Pressed += () => { UseFullyUnlockedParty(); readMessage.Text = _language == "zh" ? "当前前提：全员全解锁（含全部遭遇已发现）" : "Assumption: all unlocked, all encounters discovered"; };
        var cancel = _p.Button(_text.Get("common.cancel"));
        panel.AddChild(cancel); cancel.Position = new Vector2(216, 408); cancel.Size = new Vector2(128, 48);
        cancel.Pressed += () => CloseModal();
        var confirm = _p.Button(_text.Get("common.apply"), primary: true);
        panel.AddChild(confirm); confirm.Position = new Vector2(360, 408); confirm.Size = new Vector2(128, 48);
        confirm.Pressed += () =>
        {
            _playerCount = count.GetSelectedId();
            _seat = Math.Min(_seat, _playerCount - 1);
            // Removed seats retain their UI-only values for a later increase, but are never displayed or edited.
            CloseModal(); Refresh(_language, _text); _configButton?.GrabFocus();
        };
        Control[] focus = [count, read, full, cancel, confirm];
        for (int i = 0; i < focus.Length; i++)
        {
            focus[i].FocusNext = focus[i].GetPathTo(focus[(i + 1) % focus.Length]);
            focus[i].FocusPrevious = focus[i].GetPathTo(focus[(i + focus.Length - 1) % focus.Length]);
        }
        ModalChanged?.Invoke(true); _configOverlay.Show(); count.GrabFocus();
    }

    private string SeatCaption(int seat) => $"P{seat + 1} " + (_unlockReadStatus[seat] switch
    {
        4 => _language == "zh" ? "全解锁" : "Full unlocks",
        1 => _text.Get("query.context.unlock_status.read"), 2 => _text.Get("query.context.unlock_status.failed"),
        3 => _text.Get("query.context.unlock_status.empty"), _ => _text.Get("query.context.unlock_status.unread")
    });

    private string ReadLobbyUnlocks()
    {
        Array.Clear(_lobbyUnlocks); Array.Fill(_unlockReadStatus, 2);
        Array.Fill(_partyUnlockSources, "CapturedLobbySlot");
        _readLobby = null; _readRoster = "";
        string result;
        try
        {
            var lobby = LobbyUnlockReadout.Find(GetTree().Root);
            if (lobby is null) result = _text.Get("query.context.lobby_not_found");
            else
            {
                _readLobby = lobby; _readRoster = LobbyUnlockReadout.Roster(lobby);
                int copied = 0;
                for (int slot = 0; slot < MaximumPreviewPlayers; slot++)
                {
                    // P1 follows vanilla slotId 0, never list iteration order or the local-player position.
                    var players = lobby.Players.Where(player => player.slotId == slot).ToArray();
                    if (players.Length == 0) { _unlockReadStatus[slot] = 3; continue; }
                    if (players.Length != 1) continue;
                    try
                    {
                        _lobbyUnlocks[slot] = LobbyUnlockReadout.Copy(players[0].unlockState);
                        _unlockReadStatus[slot] = 1; copied++;
                    }
                    catch { _unlockReadStatus[slot] = 2; }
                }
                result = _text.Format("query.context.lobby_read_success", copied);
            }
        }
        catch { result = _text.Get("query.context.lobby_read_failed"); }
        UpdateSeatReadLabels();
        RefreshEditorContexts();
        return result;
    }

    private void UpdateSeatReadLabels()
    {
        foreach (var (seat, button) in _seatButtons)
            if (GodotObject.IsInstanceValid(button)) button.Text = SeatCaption(seat);
    }

    public override void _Process(double delta)
    {
        PollProduction(delta);
        if (Visible) { UpdateDomainCounts(); UpdateConditionActions(); }
        if (!Visible || _readLobby is null) return;
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
        if (_neowEditor.ClosePicker()) return true;
        if (_eventEditor.CloseQueuePicker()) return true;
        if (!_configOverlay.Visible) return false;
        _configOverlay.Hide();
        foreach (var (button, disabled) in _configBlocked)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = disabled;
        _configBlocked.Clear(); ModalChanged?.Invoke(false);
        if (_configButton is not null && GodotObject.IsInstanceValid(_configButton)) _configButton.GrabFocus();
        return true;
    }

    private Button ContextButton(string title, float x, float y, float width)
    {
        var button = _p.CompactButton(title, ContextControlHeight, 18);
        button.CustomMinimumSize = new Vector2(0, ContextControlHeight);
        button.AddThemeFontSizeOverride("font_size", 18);
        FitContextButton(button, width, ContextControlHeight);
        Place(button, x, y, width, ContextControlHeight);
        return button;
    }

    private static void FitContextButton(Button button, float width, float height)
    {
        // Text and theme padding must not enlarge a manually positioned rail cell.
        button.ClipText = true;
        button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            var style = (StyleBox)button.GetThemeStylebox(state).Duplicate();
            style.ContentMarginLeft = style.ContentMarginRight = 6;
            style.ContentMarginTop = style.ContentMarginBottom = 2;
            button.AddThemeStyleboxOverride(state, style);
        }
        button.CustomMinimumSize = new(0, height);
        button.Size = new(width, height);
    }

    private OptionButton ContextOptions(float x, float y, float width)
    {
        var option = new OptionButton { FitToLongestItem = false, ClipText = true };
        option.AddThemeFontSizeOverride("font_size", 18);
        using var donor = _p.Button(string.Empty);
        foreach (string style in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            if (style == "focus") { option.AddThemeStyleboxOverride(style, _p.FocusRing()); continue; }
            var box = (StyleBoxFlat)donor.GetThemeStylebox(style).Duplicate();
            box.ContentMarginLeft = box.ContentMarginRight = 8;
            box.ContentMarginTop = box.ContentMarginBottom = 4;
            option.AddThemeStyleboxOverride(style, box);
        }
        option.AddThemeColorOverride("font_color", _p.Color(_p.Text));
        Place(option, x, y, width, ContextControlHeight);
        return option;
    }

    private void Domain(string id, string title, float y)
    {
        var button = _p.CompactButton(title, DomainRowHeight - 4, _language == "zh" ? 18 : 16);
        SearchCategoryKey category = id switch
        {
            "neow" => SearchCategoryKey.Neow,
            "ancient" => SearchCategoryKey.Ancient,
            "shop" => SearchCategoryKey.Shop,
            "combat" => SearchCategoryKey.CombatReward,
            "events" => SearchCategoryKey.Event,
            "boss" => SearchCategoryKey.BossIdentity,
            "map" => SearchCategoryKey.BossAndMap,
            "relics" => SearchCategoryKey.Relic,
            "transform" => SearchCategoryKey.Transformation,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown workbench domain")
        };
        SearchCategoryTabIconAsset icon = _domainIcons.Resolve(category);
        button.Alignment = HorizontalAlignment.Left;
        button.ClipContents = true;
        button.CustomMinimumSize = new Vector2(0, DomainRowHeight - 4);
        Place(button, 0, y, LeftRailWidth - 8, DomainRowHeight - 4);
        var countLabel = _p.Label(string.Empty, 15, true);
        countLabel.Position = new Vector2(LeftRailWidth - 56, 5);
        countLabel.Size = new Vector2(34, DomainRowHeight - 14);
        countLabel.HorizontalAlignment = HorizontalAlignment.Right;
        countLabel.VerticalAlignment = VerticalAlignment.Center;
        countLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        countLabel.Visible = false;
        countLabel.AddThemeColorOverride("font_color", _p.Color(_p.Selected));
        button.AddChild(countLabel);
        _domainCountLabels.Add(id, countLabel);
        if (!icon.IsMissing)
        {
            var iconHost = new CenterContainer
            {
                Position = new Vector2(12, 8),
                Size = new Vector2(24, 24),
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            button.AddChild(iconHost);
            iconHost.AddChild(new TextureRect
            {
                Texture = icon.Texture,
                CustomMinimumSize = new Vector2(20, 20),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore
            });
        }
        button.Disabled = _multiplayer && id == "transform";
        if (button.Disabled) button.TooltipText = _language == "zh" ? "多人不启用 T 复合变牌筛选" : "T aggregate filtering is outside multiplayer scope";
        button.Pressed += () => { _showResults = false; _runtimeSurface?.Hide(); SelectDomain(id); };
        _domainButtons.Add(id, button);
    }

    private void SelectDomain(string id)
    {
        if (_multiplayer && id == "transform") id = "neow";
        if (id == "transform") RefreshTransformationContext();
        if (id is "boss" or "map") _actInformationEditor.SelectPage(id == "map");
        _selectedDomain = id;
        foreach (var (key, button) in _domainButtons)
        {
            var style = _p.Box(key == id ? _p.Surface : _p.Canvas);
            style.BorderColor = _p.Color(_p.Selected);
            style.BorderWidthLeft = key == id ? 3 : 0;
            style.ContentMarginLeft = 44;
            style.ContentMarginRight = 44;
            style.ContentMarginTop = style.ContentMarginBottom = 2;
            button.AddThemeStyleboxOverride("normal", style);
            // Disabling during Search must preserve the space reserved for the child icon.
            button.AddThemeStyleboxOverride("disabled", (StyleBox)style.Duplicate());
            var hover = _p.Box(_p.Hover);
            hover.BorderColor = _p.Color(_p.Selected);
            hover.BorderWidthLeft = key == id ? 3 : 0;
            hover.ContentMarginLeft = 44;
            hover.ContentMarginRight = 44;
            hover.ContentMarginTop = hover.ContentMarginBottom = 2;
            button.AddThemeStyleboxOverride("hover", hover);
            var pressed = _p.Box(_p.Line);
            pressed.BorderColor = _p.Color(_p.Selected);
            pressed.BorderWidthLeft = key == id ? 3 : 0;
            pressed.ContentMarginLeft = 44;
            pressed.ContentMarginRight = 44;
            pressed.ContentMarginTop = pressed.ContentMarginBottom = 2;
            button.AddThemeStyleboxOverride("pressed", pressed);
            button.AddThemeColorOverride("font_color", _p.Color(key == id ? _p.Text : _p.Secondary));
        }
        if (_currentDomain is not null)
        {
            _currentDomain.Text = !_multiplayer ? (_language == "zh" ? "单人条件" : "Single-player conditions") : id switch
            {
                "boss" or "map" => _language == "zh" ? "全队共用条件" : "Shared party conditions",
                "ancient" => _language == "zh" ? $"身份全队共用 · P{_seat + 1} 的选项" : $"Shared identities · P{_seat + 1}'s options",
                "events" => _language == "zh" ? $"P{_seat + 1} · 事件队列与个人结果" : $"P{_seat + 1} · Event queue and personal results",
                _ => _language == "zh" ? $"正在编辑 P{_seat + 1} 的条件" : $"Editing P{_seat + 1}'s conditions"
            };
            _currentDomain.Visible = !_showResults;
        }
        _neowEditor.Visible = id == "neow";
        _combatEditor.Visible = id == "combat";
        _shopEditor.Visible = id == "shop";
        _relicEditor.Visible = id == "relics";
        _ancientEditor.Visible = id == "ancient";
        _eventEditor.Visible = id == "events";
        _actInformationEditor.Visible = id is "boss" or "map";
        _transformationEditor.Visible = id == "transform";
    }

    private Label Text(string text, int fontSize, float x, float y, bool secondary = false)
    {
        var label = _p.Label(text, fontSize, secondary);
        Place(label, x, y);
        return label;
    }
    private void Line(float x, float y, float width, float height)
        => Place(new ColorRect { Color = _p.Color(_p.Line), MouseFilter = MouseFilterEnum.Ignore }, x, y, width, height);
    private void Place(Control control, float x, float y, float width = 0, float height = 0)
    {
        AddChild(control); control.Position = new Vector2(x, y);
        if (width > 0 || height > 0) control.Size = new Vector2(width, height);
    }
    public override void _ExitTree() { CloseSearch(); if (_session is { } session) { _session = null; _ = session.DisposeAsync(); } _characterIcons.Dispose(); }
}
