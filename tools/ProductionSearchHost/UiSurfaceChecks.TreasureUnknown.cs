using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    // A bounded product-control scenario: no seed search or performance suite.
    private static async Task RunTreasureUnknownChecks(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, flags)!.GetValue(value)!;
        void Set(object value, string name, object data) => value.GetType().GetField(name, flags)!.SetValue(value, data);
        object? Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, flags)!.Invoke(value, args);
        int checks = 0;
        void Check(bool value, string description)
        { checks++; if (!value) throw new InvalidOperationException("TreasureUnknownUi:" + description); }
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        IEnumerable<Node> Nodes(Node node)
        {
            yield return node;
            foreach (Node child in node.GetChildren()) foreach (Node nested in Nodes(child)) yield return nested;
        }
        T Find<T>(Node node, string name) where T : Node => (T)node.FindChild(name, true, false);
        void Choose(Node editor, string name, int index)
        {
            var option = Find<OptionButton>(editor, name);
            option.Select(index); option.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        }
        void HorizontalFit(Control editor, params string[] names)
        {
            Rect2 outer = editor.GetGlobalRect();
            foreach (string name in names)
            {
                Control control = Find<Control>(editor, name);
                Rect2 rect = control.GetGlobalRect();
                Check(rect.Position.X >= outer.Position.X - 1 && rect.End.X <= outer.End.X + 1,
                    name + " stays inside editor width");
            }
        }
        var host = new Control { Size = new(1600, 900) }; root.AddChild(host);
        var shell = new WorkspaceShell();
        shell.InitializeWithPersistence(runtime, Path.Combine(output, "treasure-unknown-state", Guid.NewGuid().ToString("N")));
        host.AddChild(shell);
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var store = Field<SearchWorkspace>(shell, "_persistence");
        var relics = Field<RelicSequenceSearchEditor>(frame, "_relicEditor");
        var events = Field<EventSearchEditor>(frame, "_eventEditor");
        ModelKey first = new("RELIC", "WHETSTONE"), second = new("RELIC", "WAR_PAINT");
        var empty = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        var screenshots = new List<string>();
        async Task Screenshot(string name)
        {
            await Frames(); RenderingServer.ForceDraw();
            string path = Path.Combine(output, name + ".png");
            root.GetViewport().GetTexture().GetImage().SavePng(path); screenshots.Add(path);
        }
        try
        {
            shell.Open(); await Frames(8);
            foreach (string locale in new[] { "zh", "en" })
            foreach (Vector2I size in new[] { new Vector2I(1600, 900), new Vector2I(1280, 720) })
            {
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                store.SetLanguageOverride(locale); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                root.GetWindow().Size = size; host.Size = size; await Frames();
                var text = JsonUiTextProvider.CreateUi13(locale);
                frame.RestoreDraft(empty with { Query = SearchQuery.Empty with
                {
                    TreasureRelics = new(1, 2, TreasureRelicOrderMode.Ordered, [first, second]),
                    FirstActUnknownEventPrefixCount = 3
                } }, render: false);
                frame.Refresh(locale, text); Call(frame, "SelectDomain", "relics"); await Frames();
                var treasure = frame.CaptureDraft().Query.TreasureRelics!;
                Check(treasure.StartOrdinal == 1 && treasure.Count == 2 && treasure.Slots.SequenceEqual(new ModelKey?[] { first, second }),
                    "two-slot import/capture");
                Check(relics.FindChild("TreasureTargetSlot3", true, false) is null, "exactly two target slots");
                Find<Button>(relics, "TreasureWindowFirst").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Find<Button>(relics, "TreasureOrderMode").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                treasure = frame.CaptureDraft().Query.TreasureRelics!;
                Check(treasure.StartOrdinal == 1 && treasure.Count == 1 && treasure.OrderMode == TreasureRelicOrderMode.Unordered &&
                    treasure.Slots.SequenceEqual(new ModelKey?[] { first, second }), "unordered preserves range and dormant target");
                Check(Find<Button>(relics, "TreasureChooseTarget2").Disabled, "out-of-window target is inactive");
                var dormant = frame.CaptureDraft();
                frame.RestoreDraft(dormant, render: false); frame.Refresh(locale, text); Call(frame, "SelectDomain", "relics"); await Frames();
                Check(frame.CaptureDraft().Query.TreasureRelics!.Slots[1] == second, "dormant slot survives restore");
                Find<Button>(relics, "TreasureWindowBoth").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Check(!Find<Button>(relics, "TreasureChooseTarget2").Disabled && frame.CaptureDraft().Query.TreasureRelics!.Slots[1] == second,
                    "restored range reactivates retained target");
                Find<Button>(relics, "TreasureWindowSecond").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                treasure = frame.CaptureDraft().Query.TreasureRelics!;
                Check(treasure.StartOrdinal == 2 && treasure.Count == 1 && treasure.Slots[1] == second,
                    "second-chest window stays bounded and retains targets");
                Find<Button>(relics, "TreasureOrderMode").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Check(Find<Label>(relics, "TreasureChestOrdinal1").Text == text.Format("query.treasure.encounter", 2),
                    "single second-chest window shows actual chest number");
                Check(relics.FindChild("TreasureChestOrdinal2", true, false) is null, "second chest window has one active position");
                Find<Button>(relics, "TreasureWindowBoth").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                HorizontalFit(relics, "RelicSequenceTab", "TreasureRelicTab", "TreasureWindowFirst", "TreasureWindowSecond", "TreasureWindowBoth", "TreasureOrderMode",
                    "TreasureChooseTarget1", "TreasureChooseTarget2", "TreasureClearTarget2");
                Rect2 windowFirst = Find<Button>(relics, "TreasureWindowFirst").GetGlobalRect();
                Rect2 windowSecond = Find<Button>(relics, "TreasureWindowSecond").GetGlobalRect();
                Rect2 windowBoth = Find<Button>(relics, "TreasureWindowBoth").GetGlobalRect();
                Rect2 order = Find<Button>(relics, "TreasureOrderMode").GetGlobalRect();
                Check(windowFirst.End.X <= windowSecond.Position.X && windowSecond.End.X <= windowBoth.Position.X && windowBoth.End.X <= order.Position.X,
                    "window and order controls do not overlap");
                Check(Find<Button>(relics, "TreasureChooseTarget1").TooltipText.Length == 0 && Find<Button>(relics, "TreasureOrderMode").TooltipText.Length == 0,
                    "new treasure controls add no tooltips;choose=" + Find<Button>(relics, "TreasureChooseTarget1").TooltipText +
                    ";mode=" + Find<Button>(relics, "TreasureOrderMode").TooltipText);
                await Screenshot($"treasure-{locale}-{size.X}x{size.Y}");
                Find<Button>(relics, "TreasureClearTarget1").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                treasure = frame.CaptureDraft().Query.TreasureRelics!;
                Check(treasure.OrderMode == TreasureRelicOrderMode.Ordered && treasure.Slots[0] is null && treasure.Slots[1] == second,
                    "clearing an ordered position leaves its unrestricted gap");
                Check(Find<Button>(relics, "TreasureChooseTarget1").Text == text.Get("query.treasure.add_target"), "empty position uses shop-style add target");
                Find<Button>(relics, "TreasureOrderMode").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Check(Find<Button>(relics, "TreasureChooseTarget2").GetGlobalRect().Position.X < Find<Button>(relics, "TreasureChooseTarget1").GetGlobalRect().Position.X,
                    "unordered compacts filled targets before its add slot");
                treasure = frame.CaptureDraft().Query.TreasureRelics!;
                Check(treasure.Slots[0] is null && treasure.Slots[1] == second, "unordered visual compaction preserves source slots");
                await Screenshot($"treasure-unordered-{locale}-{size.X}x{size.Y}");
                string secondName = Find<Button>(relics, "TreasureChooseTarget2").AccessibilityName;
                Find<Button>(relics, "TreasureChooseTarget1").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                var picker = Field<ObjectPickerPanel>(relics, "_picker");
                Check(picker.Visible, "add target opens the actual relic picker");
                Nodes(picker).OfType<Button>().Single(button => button.GetChildren().OfType<Label>().Any(label => label.Text == secondName))
                    .EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                treasure = frame.CaptureDraft().Query.TreasureRelics!;
                Check(!picker.Visible && treasure.Slots.SequenceEqual(new ModelKey?[] { second, second }),
                    "picker permits duplicate relic targets without deduplicating authored slots");
                Find<Button>(relics, "TreasureOrderMode").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Check(frame.CaptureDraft().Query.TreasureRelics!.Slots.SequenceEqual(new ModelKey?[] { second, second }),
                    "ordered mode preserves duplicate targets");
                Find<Button>(relics, "TreasureWindowFirst").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Check(Find<Button>(relics, "TreasureChooseTarget2").Disabled, "retained duplicate is inactive outside the window");
                Find<Button>(relics, "TreasureClearTarget2").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                treasure = frame.CaptureDraft().Query.TreasureRelics!;
                Check(treasure.Slots[0] == second && treasure.Slots[1] is null && relics.FindChild("TreasureRetainedTargets", true, false) is null,
                    "clearing retained target preserves the active identical relic");
                frame.RestoreDraft(empty with { Query = SearchQuery.Empty with
                {
                    TreasureRelics = new(1, 2, TreasureRelicOrderMode.Ordered, [first, second]),
                    FirstActUnknownEventPrefixCount = 3
                } }, render: false);
                frame.Refresh(locale, text); Call(frame, "SelectDomain", "relics"); await Frames();
                Find<Button>(relics, "RelicSequenceTab").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Check(relics.FindChild("RelicConditionBuilder", true, false) is not null, "existing sequence entry remains available");
                Check(frame.CaptureDraft().Query.TreasureRelics!.Slots[1] == second, "tab switch preserves treasure targets");
                Call(frame, "SelectDomain", "events"); await Frames();
                Check(Find<OptionButton>(events, "FirstActUnknownEventPrefix").ItemCount == 4, "prefix has four choices");
                for (int prefix = 0; prefix <= 3; prefix++)
                {
                    Choose(events, "FirstActUnknownEventPrefix", prefix); await Frames();
                    Check(frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == prefix, "prefix choice exports " + prefix);
                }
                HorizontalFit(events, "FirstActUnknownEventPrefix");
                Check(events.FindChild("EventConditionBuilder", true, false) is not null, "existing event queue remains available");
                await Screenshot($"unknown-{locale}-{size.X}x{size.Y}");
                var combinedQuery = frame.CaptureDraft().Query;
                frame.RestoreDraft(empty with { Query = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 } }, render: false);
                frame.Refresh(locale, text); Call(frame, "SelectDomain", "events");
                object? eventEntry = null;
                var previewWait = System.Diagnostics.Stopwatch.StartNew();
                while (previewWait.Elapsed < TimeSpan.FromSeconds(15))
                {
                    await Frames(1);
                    eventEntry = Field<System.Collections.IEnumerable>(frame, "_familyEntries").Cast<object>().FirstOrDefault(entry =>
                        (string)entry.GetType().GetProperty("Id")!.GetValue(entry)! == "E");
                    if (eventEntry?.GetType().GetProperty("Probability")!.GetValue(eventEntry) is double) break;
                }
                var eventProbability = eventEntry?.GetType().GetProperty("Probability")!.GetValue(eventEntry) as double?;
                Check(eventProbability.HasValue && Math.Abs(eventProbability.Value - .32725) < .00001 &&
                    ((string)eventEntry!.GetType().GetProperty("Detail")!.GetValue(eventEntry)!).Contains(
                        text.Get("query.event.unknown.probability_assumption"), StringComparison.Ordinal),
                    "prefix-only E dashboard has the conditional estimate and explicit no-adjacent-shop premise;compile=" +
                    Field<string>(frame, "_draftCompileIssue") + ";pending=" + Field<bool>(frame, "_probabilityPending") +
                    ";entry=" + JsonSerializer.Serialize(eventEntry));
                await Screenshot($"unknown-probability-{locale}-{size.X}x{size.Y}");
                frame.RestoreDraft(empty with { Query = combinedQuery }, render: false);
                frame.Refresh(locale, text); Call(frame, "SelectDomain", "events"); await Frames();
                string authored = JsonSerializer.Serialize(frame.CaptureDraft().Query);
                frame.ClearAllConditions(); await Frames();
                Check(frame.CaptureDraft().Query.TreasureRelics is null && frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == 0,
                    "clear removes both conditions");
                frame.UndoClearConditions(); await Frames();
                Check(JsonSerializer.Serialize(frame.CaptureDraft().Query) == authored, "undo restores treasure and prefix");
                Check(JsonSerializer.Serialize(store.LoadWorkbench()!.Query) == authored, "restored authoring persisted");
                var results = store.ResultBatch?.Results ?? store.Workspace.Results;
                results.Clear();
                results.Add(new PersistedSearchResult
                {
                    Seed = "000000000001", CharacterKey = empty.Character.Serialized, Ascension = 10,
                    ValidationDisposition = SearchDisposition.Match,
                    TreasureSources = [new(first.Serialized, 1, null, true), new(second.Serialized, 2, 2, false)]
                });
                Set(frame, "_showResults", true); frame.Refresh(locale, text); Call(frame, "RenderResults"); await Frames();
                string[] rendered = Nodes(Field<Control>(frame, "_resultRows")).OfType<Label>().Select(label => label.Text).ToArray();
                Check(rendered.Any(value => value.Contains(text.Get("query.treasure.source.opening"))) &&
                    rendered.Any(value => value.Contains(text.Format("query.treasure.encounter", 2))), "truthful opening and actual chest origins rendered");
                Check(!rendered.Any(value => value.Contains(text.Format("query.treasure.encounter", 1))), "opening acquisition is not labeled chest one");
                await Screenshot($"treasure-sources-{locale}-{size.X}x{size.Y}");
                Set(frame, "_showResults", false); results.Clear();
            }
            File.WriteAllText(Path.Combine(output, "treasure-unknown-ui-checks.json"), JsonSerializer.Serialize(new
            { checks, screenshots, scope = "two-slot+window+order+dormant+empty+unordered-compaction+duplicate-picker+prefix+clear+undo+persistence+sources+en+zh+1600+1280" },
                new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"TREASURE_UNKNOWN_UI_PASS checks={checks};screenshots={screenshots.Count}");
        }
        finally { shell.QueueFree(); host.QueueFree(); await Frames(); }
    }
}
