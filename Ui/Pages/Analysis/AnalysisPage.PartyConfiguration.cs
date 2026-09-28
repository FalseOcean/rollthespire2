using Godot;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private readonly ModelKey[] _partyCharacters;
    private PopupPanel? _partyConfigurationPopup;

    internal void SetPartyOpeningChoices(IReadOnlyDictionary<int, ModelKey> choices) =>
        _requestBar.SetPartyOpeningChoices(choices.ToDictionary(p => p.Key,
            p => _contentNames?.Resolve(p.Value, GameContentKind.Relic) ?? p.Value.Entry));

    internal bool ClosePartyConfiguration()
    {
        if (_partyConfigurationPopup is not { } popup || !GodotObject.IsInstanceValid(popup)) return false;
        popup.Hide();
        return true;
    }

    internal void OpenPartyConfiguration(WorkbenchSearchDraft current, Action<WorkbenchSearchDraft> apply)
    {
        ClosePartyConfiguration();
        string T(string suffix) => _uiText!.Get("predictor.party." + suffix);
        var staged = Enumerable.Range(0, 4).Select(slot => slot < current.Players.Count
            ? current.Players[slot] with { Unlocks = LobbyUnlockReadout.Copy(current.Players[slot].Unlocks) }
            : new WorkbenchPlayerDraft(slot, current.Character, UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")).ToArray();
        var popup = new PopupPanel { Name = "PredictorPartyConfigDialog", Exclusive = true };
        _partyConfigurationPopup = popup;
        AddChild(popup);
        popup.PopupHide += () =>
        {
            if (_partyConfigurationPopup == popup) _partyConfigurationPopup = null;
            popup.QueueFree();
        };
        var panel = new PanelContainer();
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Card, 8f, 1, 24f);
        popup.AddChild(panel); panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10); scroll.AddChild(column);
        column.AddChild(_palette.Label(T("title"), 26));
        var help = _palette.Label(T("help"), 18, true);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart; column.AddChild(help);
        var context = new HBoxContainer(); context.AddThemeConstantOverride("separation", 16); column.AddChild(context);
        context.AddChild(_palette.Label(T("count"), 20));
        var count = new OptionButton { Name = "PartyCount", CustomMinimumSize = new Vector2(96, 40) };
        for (int n = 2; n <= 4; n++) count.AddItem(n.ToString(), n);
        count.Select(current.Players.Count - 2); context.AddChild(count);
        context.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        context.AddChild(_palette.Label(T("ascension"), 20));
        var ascension = new SpinBox { Name = "PartyAscension", MinValue = 0, MaxValue = 10,
            Value = current.Ascension, CustomMinimumSize = new Vector2(112, 40) };
        context.AddChild(ascension);
        var rows = new HBoxContainer[4]; var pickers = new OptionButton[4]; var sources = new Label[4];
        using var icons = new NativeCharacterPoolIconProvider(_icons);
        for (int slot = 0; slot < 4; slot++)
        {
            int seat = slot;
            var row = rows[slot] = new HBoxContainer(); row.AddThemeConstantOverride("separation", 16); column.AddChild(row);
            var label = _palette.Label($"P{slot + 1}", 20); label.CustomMinimumSize = new Vector2(48, 0); row.AddChild(label);
            var picker = pickers[slot] = new OptionButton { Name = $"PartyCharacter{slot + 1}", FitToLongestItem = false,
                ExpandIcon = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(240, 40) };
            picker.AddThemeConstantOverride("icon_max_width", 28);
            foreach (var key in _partyCharacters)
                picker.AddIconItem(icons.Resolve(key).Texture, _contentNames!.Resolve(key, GameContentKind.Character));
            row.AddChild(picker);
            sources[slot] = _palette.Label("", 17, true);
            sources[slot].CustomMinimumSize = new Vector2(176, 0); row.AddChild(sources[slot]);
            picker.ItemSelected += index => staged[seat] = staged[seat] with { Character = _partyCharacters[(int)index] };
        }
        void Refresh()
        {
            for (int slot = 0; slot < 4; slot++)
            {
                rows[slot].Visible = slot < count.GetSelectedId();
                pickers[slot].Select(Math.Max(0, Array.IndexOf(_partyCharacters, staged[slot].Character)));
                sources[slot].Text = T(staged[slot].UnlockSource == "AssumedFullyUnlocked" ? "full" : "captured");
            }
        }
        count.ItemSelected += _ => Refresh();
        column.AddChild(new HSeparator());
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 12); column.AddChild(actions);
        var read = _palette.Button(T("read")); read.Name = "ReadPartyLobby"; read.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.AddChild(read);
        var full = _palette.Button(T("use_full")); full.Name = "PartyFullUnlocks"; full.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.AddChild(full);
        var message = _palette.Label(T("snapshot"), 18, true);
        message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        message.CustomMinimumSize = new Vector2(0, 60); column.AddChild(message);
        full.Pressed += () =>
        {
            for (int slot = 0; slot < 4; slot++) staged[slot] = staged[slot] with
                { Unlocks = UnlockState.all.ToSerializable(), UnlockSource = "AssumedFullyUnlocked" };
            message.Text = T("full_message"); Refresh();
        };
        read.Pressed += () =>
        {
            try
            {
                var lobby = LobbyUnlockReadout.Find(GetTree().Root);
                if (lobby is null) { message.Text = T("no_lobby"); return; }
                var members = lobby.Players.OrderBy(p => p.slotId).ToArray();
                if (members.Length is < 2 or > 4 || members.Where((p, i) => p.slotId != i).Any())
                    throw new InvalidOperationException("Party.LobbySlots");
                var captured = members.Select(p =>
                {
                    var key = new ModelKey(BaseGameModelKeys.Categories.Character, p.character.Id.Entry);
                    if (!_partyCharacters.Contains(key)) throw new InvalidOperationException("Party.CharacterUnavailable");
                    return new WorkbenchPlayerDraft(p.slotId, key, LobbyUnlockReadout.Copy(p.unlockState), "CapturedLobbySlot");
                }).ToArray();
                for (int slot = 0; slot < captured.Length; slot++) staged[slot] = captured[slot];
                count.Select(captured.Length - 2); ascension.Value = lobby.Ascension;
                message.Text = T("read_ok"); Refresh();
            }
            catch (Exception ex)
            {
                Bootstrap.RuntimeLog.Warn("predictorPartyLobbyReadFailed=" + ex.Message);
                message.Text = T("read_failed");
            }
        };
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; column.AddChild(footer);
        var cancel = _palette.Button(_uiText!.Get("common.cancel")); footer.AddChild(cancel); cancel.Pressed += () => popup.Hide();
        var confirm = _palette.Button(_uiText.Get("common.apply"), primary: true); confirm.Name = "ApplyPartyConfig"; footer.AddChild(confirm);
        confirm.Pressed += () =>
        {
            int n = count.GetSelectedId();
            var players = staged.Take(n).ToArray();
            var query = current.Query with { Players = Enumerable.Range(0, n).Select(slot =>
                slot < current.Query.Players.Count ? current.Query.Players[slot]
                    : new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) { AncientPremises = AncientOptionConditionProfile.BroadDefault }).ToArray() };
            popup.Hide();
            apply(current with { Character = players[0].Character, Players = players, Ascension = (int)ascension.Value, Query = query });
        };
        Refresh(); popup.PopupCentered(new Vector2I(700, Math.Min(660, (int)GetViewport().GetVisibleRect().Size.Y - 48)));
        count.GrabFocus();
    }
}
