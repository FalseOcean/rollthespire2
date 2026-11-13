using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.World;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.BossMap;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    // Mounted product controls and real window captures. No search or performance work.
    private static async Task RunSemanticEditorsUiAsync(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, members)!.GetValue(owner)!;
        void Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, members)!.Invoke(owner, args);
        IEnumerable<Node> Nodes(Node node)
        { yield return node; foreach (Node child in node.GetChildren()) foreach (Node nested in Nodes(child)) yield return nested; }
        int checks = 0;
        void Check(bool ok, string reason)
        { checks++; if (!ok) throw new InvalidOperationException("SemanticEditorsUi:" + reason); }
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        T Find<T>(Node owner, string name) where T : Node => (T)(owner.FindChild(name, true, false)
            ?? throw new InvalidOperationException("SemanticEditorsUi:MissingControl:" + name));
        void Fit(Control editor, IEnumerable<Control> controls, string reason)
        {
            var bounds = editor.GetGlobalRect();
            foreach (var control in controls)
            {
                var rect = control.GetGlobalRect();
                Check(rect.Position.X >= bounds.Position.X - 1 && rect.End.X <= bounds.End.X + 1,
                    reason + ":horizontal:" + control.Name + ":" + rect + ":editor=" + bounds);
                if (control is Button { Text.Length: > 0 } button && !button.ClipText)
                {
                    var font = button.GetThemeFont("font");
                    float textWidth = font.GetStringSize(button.Text, HorizontalAlignment.Left, -1,
                        button.GetThemeFontSize("font_size")).X;
                    Check(textWidth <= button.Size.X + 1, reason + ":buttonText:" + button.Name);
                }
            }
        }
        async Task Reveal(Control editor, Control target)
        {
            foreach (var scroll in Nodes(editor).OfType<ScrollContainer>()) scroll.EnsureControlVisible(target);
            await Frames();
            Check(target.IsVisibleInTree(), "mounted visible target " + target.Name);
        }
        var screenshots = new List<object>();
        async Task Capture(string name, Control editor)
        {
            await Frames(); RenderingServer.ForceDraw();
            string file = name + ".png";
            Check(root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, file)) == Error.Ok, "save " + file);
            screenshots.Add(new { file, editor = editor.GetGlobalRect().ToString(),
                labels = Nodes(editor).OfType<Label>().Where(label => label.IsVisibleInTree())
                    .Select(label => new { label.Text, rect = label.GetGlobalRect().ToString(),
                        lines = label.GetLineCount(), visibleLines = label.GetVisibleLineCount() }).ToArray() });
        }
        var window = root.GetWindow();
        var previousSize = window.Size;
        var previousScale = window.ContentScaleMode;
        float previousFactor = window.ContentScaleFactor;
        window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        window.ContentScaleFactor = 1;
        var host = new Control { Size = new(1920, 1080) }; root.AddChild(host);
        var shell = new WorkspaceShell();
        shell.InitializeWithPersistence(runtime, Path.Combine(output, "semantic-editors-state", Guid.NewGuid().ToString("N")));
        host.AddChild(shell);
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var store = Field<SearchWorkspace>(shell, "_persistence");
        var ancient = Field<AncientSearchEditor>(frame, "_ancientEditor");
        var combat = Field<CombatRewardSearchEditor>(frame, "_combatEditor");
        var maps = Field<ActInformationSearchEditor>(frame, "_actInformationEditor");
        var events = Field<EventSearchEditor>(frame, "_eventEditor");
        var pearl = BaseGameModelKeys.Relics.GoldenPearl;
        var arcane = BaseGameModelKeys.Relics.ArcaneScroll;
        var curse = BaseGameModelKeys.Relics.CursedPearl;
        var basis = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        async Task Restore(WorkbenchSearchDraft draft, string domain, string locale)
        {
            frame.RestoreDraft(draft, render: false);
            frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
            Call(frame, "SelectDomain", domain);
            await Frames();
        }
        try
        {
            shell.Open(); await Frames(8);
            foreach (string locale in new[] { "zh", "en" })
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                store.SetLanguageOverride(locale);
                shell.GetType().GetField("_languageCode", members)!.SetValue(shell, "");
                Call(shell, "RefreshLanguage");
                window.Size = size; host.Size = size; await Frames();
                string suffix = $"{locale}-{size.X}x{size.Y}";
                for (int mode = -1; mode <= 3; mode++)
                {
                    var keys = new ModelKey[] { pearl, arcane, curse };
                    var filter = mode switch { -1 => new ModelKeySetFilter([pearl], [], []),
                        0 => new(keys, [], []), 1 => new([], [pearl, arcane], []), 3 => new([], [], [curse]), _ => ModelKeySetFilter.Empty };
                    var query = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with { NeowRelics = filter },
                        NeowOfferWhitelist = mode == 2 ? new(keys) : null };
                    var saved = new AncientEditorStateSnapshot(0, 2,
                        [new([], false, [], 1), new([], false, [], 1), new([], false, [], 1)])
                    { NeowOffers = new(mode >= 0, Math.Max(0, mode), [pearl], [keys, [pearl, arcane], keys, [curse]]) };
                    await Restore(basis with { Query = query, AncientEditor = saved }, "ancient", locale);
                    var modeButton = Find<Button>(ancient, mode < 0 ? "NeowOffersBasic" : "NeowOffersRelation" + mode);
                    await Reveal(ancient, modeButton);
                    var controls = Nodes(ancient).OfType<Control>().Where(control =>
                        control.Name.ToString().StartsWith("NeowOffer", StringComparison.Ordinal)).ToArray();
                    Fit(ancient, controls, "Neow " + mode + " " + suffix);
                    Check((frame.CaptureDraft().Query.NeowOfferWhitelist is not null) == (mode == 2), "Neow active relation survives mounted render");
                    await Capture("semantic-a-" + (mode < 0 ? "basic" : mode) + "-" + suffix, ancient);
                }
                var empty = SearchQuery.Empty with { NeowOfferWhitelist = new([]) };
                await Restore(basis with { Query = empty }, "ancient", locale);
                Check(frame.CaptureDraft().Query.NeowOfferWhitelist is { AllowedKeys.Count: 0 }, "mounted empty whitelist remains authored");
                await Capture("semantic-a-empty-" + suffix, ancient);

                var cards = new ModelKey[] { new("CARD", "STRIKE_IRONCLAD"), new("CARD", "BASH") };
                await Restore(basis with { Query = SearchQuery.Empty with { CombatCardExclusion = new(3, cards) } }, "combat", locale);
                for (int mode = 0; mode < 2; mode++)
                {
                    var selector = Find<OptionButton>(combat, "CombatCardCollectionMode");
                    selector.Select(mode); selector.EmitSignal(OptionButton.SignalName.ItemSelected, (long)mode);
                    await Frames(); selector = Find<OptionButton>(combat, "CombatCardCollectionMode");
                    await Reveal(combat, selector);
                    Fit(combat, [selector, Find<Control>(combat, "CombatCardExclusionCollection")], "C " + mode + " " + suffix);
                    var selected = frame.CaptureDraft().Query.CombatCardExclusion!;
                    Check((int)selected.Mode == mode && selected.CardKeys.SequenceEqual(cards), "C toggling retains authored member set");
                    await Capture("semantic-c-" + mode + "-" + suffix, combat);
                }

                await Restore(basis, "boss", locale);
                var catalog = Field<BossMapSearchUiCatalog>(Field<object>(maps, "_draft"), "Catalog");
                var variant = catalog.Sections.Single(section => section.Act == 1).Variants.First();
                foreach (var kind in Enum.GetValues<EncounterQueueKind>())
                {
                    var allowed = variant.EncounterPools[kind].Take(2).ToArray();
                    Check(allowed.Length > 0, "native encounter catalog " + kind);
                    var queue = new EncounterQueueSearchCondition(1, kind, SearchSequenceRangeMode.FirstN, 2, allowed) { VariantKey = variant.ActKey };
                    await Restore(basis with { Query = SearchQuery.Empty with { EncounterQueues = [queue] } }, "boss", locale);
                    var count = Nodes(maps).OfType<SpinBox>().Single();
                    await Reveal(maps, count);
                    Fit(maps, Nodes(count.GetParent()).OfType<Control>().Where(control => control is BaseButton or SpinBox), "encounter " + kind + " " + suffix);
                    Check(frame.CaptureDraft().Query.EncounterQueues.Single().AllowedKeys.SequenceEqual(allowed), "encounter render preserves allowed members");
                    await Capture("semantic-w-" + kind + "-" + suffix, maps);
                }

                await Restore(basis with { Query = SearchQuery.Empty with { FirstActUnknownMonsterPrefixCount = 3 } }, "map", locale);
                var mapPrefix = Find<OptionButton>(maps, "MapUnknownRoomPrefix");
                Check(mapPrefix.Selected == 6, "map reads shared monster prefix");
                await Reveal(maps, mapPrefix); Fit(maps, [mapPrefix], "map Q " + suffix);
                await Capture("semantic-q-map-" + suffix, maps);
                Call(frame, "SelectDomain", "events"); await Frames();
                var eventPrefix = Find<OptionButton>(events, "FirstActUnknownEventPrefix");
                Check(eventPrefix.Selected == 6, "event reads same shared monster prefix");
                eventPrefix.Select(2); eventPrefix.EmitSignal(OptionButton.SignalName.ItemSelected, 2L); await Frames();
                Check(frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == 2 &&
                    frame.CaptureDraft().Query.FirstActUnknownMonsterPrefixCount == 0, "event explicit edit updates shared category");
                eventPrefix = Find<OptionButton>(events, "FirstActUnknownEventPrefix");
                await Reveal(events, eventPrefix); Fit(events, [eventPrefix], "event Q " + suffix);
                await Capture("semantic-q-event-" + suffix, events);
                Call(frame, "SelectDomain", "map"); await Frames();
                Check(Find<OptionButton>(maps, "MapUnknownRoomPrefix").Selected == 2, "map sees event edit");
                mapPrefix = Find<OptionButton>(maps, "MapUnknownRoomPrefix");
                mapPrefix.Select(4); mapPrefix.EmitSignal(OptionButton.SignalName.ItemSelected, 4L); await Frames();
                Call(frame, "SelectDomain", "events"); await Frames();
                eventPrefix = Find<OptionButton>(events, "FirstActUnknownEventPrefix");
                Check(eventPrefix.Selected == 4 && frame.CaptureDraft().Query.FirstActUnknownMonsterPrefixCount == 1,
                    "cached event page sees map edit");
                eventPrefix.Select(2); eventPrefix.EmitSignal(OptionButton.SignalName.ItemSelected, 2L); await Frames();
                Call(frame, "SelectDomain", "map"); await Frames();
                Check(Find<OptionButton>(maps, "MapUnknownRoomPrefix").Selected == 2, "cached map page sees second event edit");
                frame.ClearCategoryConditions(); await Frames();
                Check(frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == 0, "map page clear clears shared Q");
                frame.UndoClearConditions(); await Frames();
                Check(frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == 2, "one undo restores shared Q");
                Call(frame, "SelectDomain", "events"); await Frames();
                frame.ClearCategoryConditions(); await Frames();
                Check(frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == 0, "event page clear clears shared Q");
                frame.UndoClearConditions(); await Frames();
                Check(frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == 2, "one event undo restores shared Q");
            }
            File.WriteAllText(Path.Combine(output, "semantic-editors-ui.json"), JsonSerializer.Serialize(new { passed = true, checks, screenshots },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            host.Free(); window.Size = previousSize;
            window.ContentScaleMode = previousScale; window.ContentScaleFactor = previousFactor;
        }
    }
}
