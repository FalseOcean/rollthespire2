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
            var count = ContextOptions(ColumnInset, ContextTop + 42, SelectorWidth, horizontalPadding: 5);
            count.Name = "PartyPlayerCount";
            count.AddThemeFontSizeOverride("font_size", 15);
            for (int n = 2; n <= MaximumPreviewPlayers; n++)
                count.AddItem(_text.Format("query.context.player_count_value", n), n);
            count.Select(count.GetItemIndex(_playerCount));
            count.ItemSelected += index =>
            {
                int next = count.GetItemId((int)index);
                if (next == _playerCount) return;
                UseManualPartyUnlocks();
                ResizeParty(next);
                Refresh(_language, _text);
                GetNode<OptionButton>("PartyPlayerCount").GrabFocus();
            };
            var read = ContextButton(_text.Get("query.context.read_lobby"), ColumnInset + SelectorColumnStride,
                ContextTop + 42, SelectorWidth);
            read.Name = "PartyReadLobby";
            read.AddThemeFontSizeOverride("font_size", 16);
            FitContextButton(read, SelectorWidth, ContextControlHeight, horizontalPadding: 3);
            read.Pressed += () =>
            {
                string receipt = ReadLobbyUnlocks();
                Refresh(_language, _text);
                ReceiptText(receipt);
                GetNode<Button>("PartyReadLobby").GrabFocus();
            };
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
        RolltheSpire2.Ui.Components.CharacterDropdownVisuals.Apply(picker);
        picker.ItemSelected += index =>
        {
            ChangeCharacter(_characters[(int)index]);
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
        if (_multiplayer || _useFullUnlocks)
        {
            _currentUnlockStatus = Text(_multiplayer ? SeatCaption(_seat) : _text.Get("query.context.assumed_full"), 14, ColumnInset, characterTop + 80, true);
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

    private string SeatCaption(int seat) => $"P{seat + 1} · " + _text.Get(_partyUnlockSources[seat] switch
    {
        "CapturedLocalProfile" => "query.context.local_progress",
        "AssumedFullyUnlocked" => "query.context.assumed_full",
        _ => "query.context.lobby_progress"
    });

    private string ReadLobbyUnlocks()
    {
        try
        {
            var lobby = IsInsideTree() ? LobbyUnlockReadout.Find(GetTree().Root) : null;
            if (lobby is null)
            {
                UseManualPartyUnlocks();
                return _text.Get("query.context.lobby_not_found");
            }
            var players = lobby.Players.OrderBy(p => p.slotId).ToArray();
            if (players.Length is < 2 or > MaximumPreviewPlayers ||
                players.Where((p, i) => p.slotId != i).Any())
            {
                UseManualPartyUnlocks();
                return _text.Get("query.context.lobby_slots_unavailable");
            }
            var characters = players.Select(p => new ModelKey(BaseGameModelKeys.Categories.Character, p.character.Id.Entry)).ToArray();
            if (characters.Any(character => !_characters.Contains(character)))
            {
                UseManualPartyUnlocks();
                return _text.Get("query.context.lobby_character_unavailable");
            }
            var progress = players.Select(p => _useFullUnlocks ? ManualSeatUnlocks(p.slotId, true) : ReadSeatUnlocks(p.unlockState)).ToArray();
            int ascension = lobby.Ascension;
            string roster = LobbyUnlockReadout.Roster(lobby);
            for (int slot = 0; slot < players.Length; slot++)
            {
                _seatCharacters[slot] = characters[slot];
                (_lobbyUnlocks[slot], _partyUnlockSources[slot]) = progress[slot];
            }
            _readLobby = _useFullUnlocks ? null : lobby;
            _readRoster = roster;
            ResizeParty(players.Length);
            _partyAscension = ascension;
            if (_useFullUnlocks) return _text.Get("query.context.assumed_full");
            var assumed = players.Where(p => _partyUnlockSources[p.slotId] == "AssumedFullyUnlocked")
                .Select(p => $"P{p.slotId + 1}").ToArray();
            return assumed.Length == 0 ? _text.Format("query.context.lobby_read_success", players.Length)
                : _text.Format("query.context.lobby_partial", string.Join(", ", assumed));
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                "PartyLobbyReadFailed", nameof(QueryWorkbenchFrame), "LobbyRead", "issues.capture_failed");
            UseManualPartyUnlocks();
            return _text.Get("query.context.lobby_read_failed");
        }
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
            _currentUnlockStatus.Text = _multiplayer ? SeatCaption(_seat) : _text.Get("query.context.assumed_full");
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
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            var style = (StyleBox)button.GetThemeStylebox(state).Duplicate();
            style.ContentMarginLeft = style.ContentMarginRight = horizontalPadding;
            style.ContentMarginTop = style.ContentMarginBottom = 2;
            button.AddThemeStyleboxOverride(state, style);
        }
        button.CustomMinimumSize = new(0, height);
        button.Size = new(width, height);
    }

    private OptionButton ContextOptions(float x, float y, float width, float horizontalPadding = 8)
    {
        var option = new OptionButton { FitToLongestItem = false, ClipText = true };
        option.AddThemeFontSizeOverride("font_size", 18);
        using var donor = _p.Button(string.Empty);
        foreach (string style in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            if (style == "focus") { option.AddThemeStyleboxOverride(style, _p.FocusRing()); continue; }
            var box = (StyleBoxFlat)donor.GetThemeStylebox(style).Duplicate();
            box.ContentMarginLeft = box.ContentMarginRight = horizontalPadding;
            box.ContentMarginTop = box.ContentMarginBottom = 4;
            option.AddThemeStyleboxOverride(style, box);
        }
        option.AddThemeColorOverride("font_color", _p.Color(_p.Text));
        Place(option, x, y, width, ContextControlHeight);
        return option;
    }

}
