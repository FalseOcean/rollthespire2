using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Tests.FastSearch;

internal static class PartySearchContextChecks
{
    internal static void Check(ModRuntimeSnapshot runtime, string directory, WorkbenchSearchDraft four,
        Action<bool, string> check)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var store = new SearchWorkspace(Path.Combine(directory, "party-context"), false);
        var frame = new QueryWorkbenchFrame(runtime, store) { Size = new(1536, 772) };
        var panel = new Control { Size = new(1600, 900), Theme = MainPanelTheme.Create() };
        tree.CurrentScene.AddChild(panel);
        panel.AddChild(frame);
        void Press(string name) => frame.GetNode<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Count(int count)
        {
            var pick = frame.GetNode<OptionButton>("PartyPlayerCount");
            int index = pick.GetItemIndex(count);
            pick.Select(index);
            pick.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        }
        var full = UnlockState.all.ToSerializable();
        var local = SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable();
        bool SameProgress(SerializableUnlockState? found, SerializableUnlockState expected) =>
            found is not null && found.NumberOfRuns == expected.NumberOfRuns &&
            found.EncountersSeen.SequenceEqual(expected.EncountersSeen) &&
            found.UnlockedEpochs.SequenceEqual(expected.UnlockedEpochs);
        void Offline(WorkbenchSearchDraft draft, string action)
        {
            check(!draft.RequiresLobbyRead && draft.Players[0].UnlockSource == "CapturedLocalProfile" &&
                SameProgress(draft.Players[0].Unlocks, local), action + ": P1 uses local progress");
            check(draft.Players.Skip(1).All(p => p.UnlockSource == "AssumedFullyUnlocked" && SameProgress(p.Unlocks, full)),
                action + ": other seats use full progress");
            _ = draft.Compile(runtime, out _);
        }
        try
        {
            frame.Refresh("en", JsonUiTextProvider.CreateUi13("en"));
            Press("SearchMode1");
            var defaults = frame.CaptureDraft();
            check(defaults.Players.Count == 2, "default multiplayer size is 2");
            Offline(defaults, "initial party");
            var previous = defaults with { GameVersion = "0.110.1" };
            var oldStore = new SearchWorkspace(Path.Combine(directory, "older-manual-party"), false);
            oldStore.SaveWorkbench(previous);
            var reopened = new QueryWorkbenchFrame(runtime, oldStore) { Size = frame.Size };
            tree.CurrentScene.AddChild(reopened);
            try
            {
                reopened.Refresh("en", JsonUiTextProvider.CreateUi13("en"));
                var migrated = reopened.CaptureDraft();
                check(migrated.GameVersion == runtime.Detection.NormalizedVersion && migrated.Players.Count == 2,
                    "opening an older saved manual party rebinds to the detected game version");
                Offline(migrated, "cross-version editable party");
            }
            finally { reopened.Free(); }
            foreach (var lang in new[] { "en", "zh" })
            {
                var text = JsonUiTextProvider.CreateUi13(lang);
                frame.Refresh(lang, text);
                var count = frame.GetNode<OptionButton>("PartyPlayerCount");
                var read = frame.GetNode<Button>("PartyReadLobby");
                var left = count.GetRect();
                var right = read.GetRect();
                check(count.ItemCount == 3 && Enumerable.Range(0, 3).All(i => count.GetItemId(i) == i + 2 &&
                    count.GetItemText(i) == text.Format("query.context.player_count_value", i + 2)) &&
                    read.Text == text.Get("query.context.read_lobby"), lang + ": count options and Read lobby labels");
                check(left.Position.X == 8 && right.End.X == 216 && left.End.X <= right.Position.X &&
                    left.Position.Y == right.Position.Y && count.IsVisibleInTree() && read.IsVisibleInTree(),
                    lang + ": count and read controls fit on the same 208px rail row");
                var readFont = read.GetThemeFont("font");
                float labelWidth = readFont.GetStringSize(read.Text, fontSize: read.GetThemeFontSize("font_size")).X;
                float margins = read.GetThemeStylebox("normal").GetMinimumSize().X;
                string geometry = $"{lang}: '{read.Text}' text={labelWidth}, margins={margins}, button={right.Size.X}";
                Console.WriteLine("PARTY_SEARCH_RAIL " + geometry);
                check(labelWidth + margins <= right.Size.X, geometry);
                float arrow = count.GetThemeIcon("arrow").GetWidth() + count.GetThemeConstant("h_separation");
                var countFont = count.GetThemeFont("font");
                var countMargins = count.GetThemeStylebox("normal").GetMinimumSize().X;
                foreach (int index in Enumerable.Range(0, count.ItemCount))
                {
                    string title = count.GetItemText(index);
                    float width = countFont.GetStringSize(title, fontSize: count.GetThemeFontSize("font_size")).X;
                    string line = $"{lang}: '{title}' text={width}, margins={countMargins}, arrow+gap={arrow}, option={left.Size.X}";
                    Console.WriteLine("PARTY_SEARCH_RAIL " + line);
                    check(width + countMargins + arrow <= left.Size.X, line);
                }
                Count(4);
                foreach (int slot in Enumerable.Range(0, 4))
                {
                    var tab = frame.GetNode<Button>("SearchPlayer" + slot);
                    float width = tab.GetThemeFont("font").GetStringSize(tab.Text, fontSize: tab.GetThemeFontSize("font_size")).X;
                    var spacing = tab.GetThemeConstant("h_separation");
                    float margin = tab.GetThemeStylebox("normal").GetMinimumSize().X;
                    var icon = Math.Min(tab.Icon?.GetWidth() ?? 0, tab.GetThemeConstant("icon_max_width"));
                    string line = $"{lang}: {tab.Text} x={tab.Position.X}, text={width}, icon={icon}, gap={spacing}, margin={margin}, available={tab.Size.X}";
                    Console.WriteLine("PARTY_SEARCH_RAIL " + line);
                    check(tab.Text == "P" + (slot + 1) && width + icon + spacing + margin <= tab.Size.X, line);
                }
                Count(2);
            }
            Press("PartyReadLobby");
            Offline(frame.CaptureDraft(), "no lobby");
            Count(4);
            check(frame.CaptureDraft().Players.Count == 4 && Enumerable.Range(0, 4).All(i =>
                frame.GetNode<Button>("SearchPlayer" + i).Text == "P" + (i + 1)), "2 to 4 reveals P1–P4");
            frame.RestoreDraft(four);
            Press("SearchPlayer3");
            var authored = frame.CaptureDraft();
            string lastQuery = JsonSerializer.Serialize(authored.Query.Players[3]);
            Count(2);
            var reduced = frame.CaptureDraft();
            check(reduced.Players.Count == 2 && reduced.Query.Players.Count == 2 &&
                frame.GetNodeOrNull<Button>("SearchPlayer3") is null, "removing seats excludes their predicates from the request");
            Count(4);
            var expanded = frame.CaptureDraft();
            var expandedQuery = JsonSerializer.Serialize(expanded.Query.Players[3]);
            if (expandedQuery != lastQuery || expanded.Players[3].Character != authored.Players[3].Character)
            {
                File.WriteAllText(Path.Combine(directory, "p4-before.json"), lastQuery);
                File.WriteAllText(Path.Combine(directory, "p4-after.json"), expandedQuery);
            }
            check(expandedQuery == lastQuery && expanded.Players[3].Character == authored.Players[3].Character,
                "P4 character and authored query survive 4 to 2 to 4; evidence=" + directory);
            // Use a fresh, empty manual request for the lobby fixture. Party authoring
            // with unsatisfied card conditions must not affect this setup's compile checks.
            frame.RestoreDraft(defaults);
            var screen = new PartySearchLobbyScreen();
            var service = DispatchProxy.Create<INetHostGameService, PartySearchNetProxy>();
            var proxy = (PartySearchNetProxy)(object)service;
            var lobbyType = typeof(StartRunLobby);
            object[] args = lobbyType.GetConstructors().Any(c => c.GetParameters().Length == 5 &&
                c.GetParameters()[1].ParameterType == typeof(bool))
                ? [GameMode.Standard, false, service, screen, 4] : [GameMode.Standard, service, screen, 4];
            var lobby = (StartRunLobby)Activator.CreateInstance(lobbyType, args)!;
            AccessTools.Field(typeof(NCharacterSelectScreen), "_lobby").SetValue(screen, lobby);
            AccessTools.Property(lobbyType, nameof(StartRunLobby.Ascension)).SetValue(lobby, 7);
            var slotKeys = new[] { "SILENT", "DEFECT", "IRONCLAD" };
            var slotProgress = new[] { UnlockState.none.ToSerializable(), UnlockState.all.ToSerializable(), null };
            foreach (int i in new[] { 2, 0, 1 })
                lobby.Players.Add(new() { slotId = i, id = (ulong)(100 + i),
                    character = ModelDb.AllCharacters.Single(c => c.Id.Entry == slotKeys[i]),
                    unlockState = slotProgress[i]! });
            tree.CurrentScene.AddChild(screen);
            try
            {
                Press("PartyReadLobby");
                var captured = frame.CaptureDraft();
                check(captured.Ascension == 7 && captured.Players.Select(p => p.Character.Entry).SequenceEqual(slotKeys) &&
                    frame.GetNode<OptionButton>("PartyPlayerCount").GetSelectedId() == 3, "native lobby count, ascension and characters follow slotId");
                check(captured.Players.Select(p => p.UnlockSource).SequenceEqual(["CapturedLobbySlot", "CapturedLobbySlot", "AssumedFullyUnlocked"]) &&
                    SameProgress(captured.Players[0].Unlocks, slotProgress[0]!) && SameProgress(captured.Players[1].Unlocks, slotProgress[1]!) &&
                    SameProgress(captured.Players[2].Unlocks, full), "missing P3 progress falls back independently");
                _ = captured.Compile(runtime, out _);
                proxy.Connected = false;
                frame._Process(2);
                var disconnected = frame.CaptureDraft();
                Offline(disconnected, "disconnect");
                check(disconnected.Ascension == captured.Ascension && disconnected.Players.Select(p => p.Character.Entry).SequenceEqual(slotKeys),
                    "disconnect keeps the configured party editable");
                proxy.Connected = true;
                lobby.Players.RemoveAll(p => p.slotId == 1);
                Press("PartyReadLobby");
                var sparse = frame.CaptureDraft();
                check(sparse.Compile(runtime, out _).SemanticFingerprint == disconnected.Compile(runtime, out _).SemanticFingerprint,
                    "sparse native slots do not shift P3 into P2 or replace the editable request");
            }
            finally { screen.Free(); }
        }
        finally { panel.Free(); }
    }
}

internal partial class PartySearchLobbyScreen : NCharacterSelectScreen
{
    public override void _Ready() { }
    public override void _Process(double delta) { }
    public override void _Input(InputEvent input) { }
}

public class PartySearchNetProxy : DispatchProxy
{
    internal bool Connected = true;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "get_Type" => NetGameType.Host, "get_IsConnected" => Connected, "get_NetId" => 100UL,
        _ => method.ReturnType == typeof(void) || !method.ReturnType.IsValueType ? null : Activator.CreateInstance(method.ReturnType)
    };
}
