using RolltheSpire2.Ui.Shell;
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

internal sealed partial class QueryWorkbenchFrame
{
    private void SearchContext(IGameContentNameResolver names)
    {
        for (int i = 0; i < 2; i++)
        {
            bool multi = i == 1;
            var mode = ContextButton(_text.Get(multi ? "query.context.multiplayer" : "query.context.singleplayer"), ColumnInset + i * SelectorColumnStride, ContextTop, SelectorWidth);
            mode.Name = "SearchMode" + i;
            SearchTooltip.Set(mode, _text.Get(multi ? "query.context.multiplayer_hint" : "query.context.singleplayer_hint"));
            mode.AddThemeFontSizeOverride("font_size", 16);
            ApplyContextTab(mode, _multiplayer == multi);
            FitContextButton(mode, SelectorWidth, ContextControlHeight);
            mode.Pressed += () => SetSearchMode(multi);
        }
        if (_multiplayer)
        {
            _configButton = ContextButton(_text.Get("query.context.multiplayer_setup"), ColumnInset, ContextTop + 42, RailContentWidth);
            _configButton.Name = "PartyConfiguration";
            _configButton.Pressed += OpenMultiplayerConfig;
            const float seatGap = 4;
            float seatWidth = (RailContentWidth - seatGap * (_playerCount - 1)) / _playerCount;
            for (int i = 0; i < _playerCount; i++)
            {
                int seat = i;
                var tab = ContextButton($"P{i + 1}", ColumnInset + i * (seatWidth + seatGap), ContextTop + 82, seatWidth);
                tab.Name = "SearchPlayer" + i;
                tab.TooltipText = "";
                tab.Icon = _characterIcons.Resolve(_seatCharacters[i]).Texture;
                tab.ExpandIcon = true;
                tab.AddThemeConstantOverride("icon_max_width", 20);
                tab.AddThemeConstantOverride("h_separation", 3);
                tab.AddThemeFontSizeOverride("font_size", 14);
                ApplyContextTab(tab, _seat == i);
                FitContextButton(tab, seatWidth, 28, horizontalPadding: 3);
                _seatButtons[i] = tab;
                tab.Pressed += () => { _seat = seat; Refresh(_language, _text); };
            }
        }
        float characterTop = CharacterTop;
        ModelKey character = _multiplayer ? _seatCharacters[_seat] : _soloCharacter;
        var picker = ContextOptions(ColumnInset, characterTop, RailContentWidth);
        picker.Name = "SearchCharacter";
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
            UpdateSeatReadLabels();
            RefreshEditorContexts();
        };
        Text(_multiplayer ? (_language == "zh" ? "共享进阶" : "Shared asc.") : _text.Get("query.context.ascension"), 18, 8, characterTop + 44, true).Name = "SearchAscensionCaption";
        var ascension = ContextOptions(ColumnInset + SelectorColumnStride, characterTop + 40, SelectorWidth);
        ascension.Name = "SearchAscension";
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
        if (_multiplayer)
        {
            _currentUnlockStatus = Text(SeatCaption(_seat), 14, ColumnInset, characterTop + 80, true);
            _currentUnlockStatus.Name = "SearchCurrentUnlockStatus";
            _currentUnlockStatus.Size = new(RailContentWidth, 24);
            _currentUnlockStatus.ClipText = true;
            _currentUnlockStatus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
    }

    private void ApplyContextTab(Button button, bool active)
    {
        _p.SetActive(button, active, horizontalPadding: 6);
        // Preserve the transparent tab and its selection line while Search locks input.
        button.AddThemeStyleboxOverride("disabled", (StyleBox)button.GetThemeStylebox("normal").Duplicate());
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
        var panel = new Panel { Position = new Vector2(540, 194), Size = new Vector2(520, 568) };
        panel.AddThemeStyleboxOverride("panel", _p.ContentBox());
        _configOverlay.AddChild(panel);
        Label Caption(string text, int size, float x, float y, bool heading = false)
        { var label = _p.Label(text, size, heading: heading); panel.AddChild(label); label.Position = new Vector2(x, y); return label; }
        Caption(_text.Get("query.context.multiplayer_dialog.title"), 26, 32, 24, heading: true);
        Caption(_text.Get("query.context.player_count"), 20, 32, 94);
        var pickupOrder = Caption(_text.Get("query.context.party_order"), 18, 32, 158);
        pickupOrder.MouseFilter = MouseFilterEnum.Pass;
        SearchTooltip.Set(pickupOrder, _text.Get("query.context.party_order_hint"));
        var count = ContextOptions(0, 0, 208);
        count.Reparent(panel, false); count.Position = new Vector2(280, 88);
        for (int n = 2; n <= MaximumPreviewPlayers; n++) count.AddItem(_text.Format("query.context.player_count_value", n), n);
        count.Select(count.GetItemIndex(_playerCount));
        var read = _p.Button(_text.Get("query.context.read_unlocks"));
        SearchTooltip.Set(read, _text.Get("query.context.read_unlocks_hint"));
        panel.AddChild(read); read.Position = new Vector2(32, 216); read.Size = new Vector2(456, 44);
        var full = _p.Button(_text.Get("query.context.full_party"));
        SearchTooltip.Set(full, _text.Get("query.context.full_party_hint"));
        panel.AddChild(full); full.Position = new Vector2(32, 268); full.Size = new Vector2(456, 44);
        string UnlockMessage(string message) => message + "\n" + string.Join("\n", Enumerable.Range(0, count.GetSelectedId()).Select(SeatCaption));
        var readMessage = _p.Label(UnlockMessage(_text.Get("query.context.unlocks_summary")), 16, true);
        panel.AddChild(readMessage); readMessage.Position = new Vector2(32, 324); readMessage.Size = new Vector2(456, 132);
        readMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        count.ItemSelected += _ => readMessage.Text = UnlockMessage(_text.Get("query.context.unlocks_summary"));
        read.Pressed += () => readMessage.Text = UnlockMessage(ReadLobbyUnlocks());
        full.Pressed += () => { UseFullyUnlockedParty(); readMessage.Text = UnlockMessage(_text.Get("query.context.full_party_applied")); };
        var cancel = _p.Button(_text.Get("common.cancel"));
        panel.AddChild(cancel); cancel.Position = new Vector2(216, 488); cancel.Size = new Vector2(128, 48);
        cancel.Pressed += () => CloseModal();
        var confirm = _p.Button(_text.Get("common.apply"), primary: true);
        panel.AddChild(confirm); confirm.Position = new Vector2(360, 488); confirm.Size = new Vector2(128, 48);
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
                    catch (Exception ex)
                    {
                        _unlockReadStatus[slot] = 2;
                        Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                            "PartyLobbyUnlockReadFailed", nameof(QueryWorkbenchFrame), "SeatRead", "issues.capture_failed", correlationId: "slot=" + slot);
                    }
                }
                result = _text.Format("query.context.lobby_read_success", copied);
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                "PartyLobbyUnlockReadFailed", nameof(QueryWorkbenchFrame), "LobbyRead", "issues.capture_failed");
            result = _text.Get("query.context.lobby_read_failed") + "\n" + ex.Message;
        }
        UpdateSeatReadLabels();
        RefreshEditorContexts();
        return result;
    }

    private void UpdateSeatReadLabels()
    {
        foreach (var (seat, button) in _seatButtons)
            if (GodotObject.IsInstanceValid(button))
            {
                button.Text = $"P{seat + 1}";
                button.Icon = _characterIcons.Resolve(_seatCharacters[seat]).Texture;
            }
        if (_currentUnlockStatus is not null && GodotObject.IsInstanceValid(_currentUnlockStatus))
            _currentUnlockStatus.Text = SeatCaption(_seat);
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

    private static void FitContextButton(Button button, float width, float height, float horizontalPadding = 6)
    {
        // Text and theme padding must not enlarge a manually positioned rail cell.
        button.ClipText = true;
        button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            var style = (StyleBox)button.GetThemeStylebox(state).Duplicate();
            style.ContentMarginLeft = style.ContentMarginRight = horizontalPadding;
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

}
