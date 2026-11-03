using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Pages.Search.Editors;
using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

internal static partial class UiSurfaceChecks
{
    private static async Task RunClearConditionChecks(Node root, QueryWorkbenchFrame frame,
        SearchWorkspace store, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(object obj, string name) => obj.GetType().GetField(name, flags)!.GetValue(obj)!;
        Button Clear() => (Button)Field(frame, "_clearConditions");
        Button Undo() => (Button)Field(frame, "_undoClearConditions");
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new InvalidOperationException("ClearConditions:" + reason); }
        string Query() => JsonSerializer.Serialize(frame.CaptureDraft().WithoutCapturedAuthority());
        bool Same<T>(T left, T right) => System.Text.Json.Nodes.JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(left), JsonSerializer.SerializeToNode(right));
        WorkbenchSearchDraft Draft() => frame.CaptureDraft().WithoutCapturedAuthority();
        object UndoState()
        {
            JsonElement? Stamp(string? value) => value is null ? null : JsonSerializer.Deserialize<JsonElement>(value);
            return new { Draft = Draft(), HasReceipt = Field(frame, "_restoreClearedConditions") is Action, UndoDisabled = Undo().Disabled,
                RecordedContext = Field(frame, "_clearConditionsContext"),
                CurrentContext = frame.GetType().GetMethod("ClearConditionsContext", flags)!.Invoke(frame, null),
                RecordedStamp = Stamp((string?)Field(frame, "_clearUndoStamp")),
                CurrentStamp = Stamp((string?)frame.GetType().GetMethod("ConditionUndoStamp", flags)!.Invoke(frame, null)) };
        }
        async Task Frames() { for (int i = 0; i < 3; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Seat(int slot)
        {
            frame.GetNode<Button>("SearchPlayer" + slot).EmitSignal(Button.SignalName.Pressed);
            await Frames();
            Check((int)Field(frame, "_seat") == slot, "actual seat button selects P" + (slot + 1));
        }
        void CheckActiveBindings(bool party)
        {
            foreach (var (editor, dictionary) in new[] {
                ("_neowEditor", party ? "_partyCatalogDrafts" : "_drafts"),
                ("_combatEditor", party ? "_partyDrafts" : "_drafts"),
                ("_shopEditor", party ? "_partyDrafts" : "_drafts"),
                ("_relicEditor", party ? "_partyDrafts" : "_drafts"),
                ("_ancientEditor", party ? "_partyDrafts" : "_drafts"),
                ("_eventEditor", party ? "_partyDrafts" : "_drafts"), ("_actInformationEditor", "_drafts") })
            {
                var owner = Field(frame, editor);
                var drafts = (System.Collections.IDictionary)Field(owner, dictionary);
                Check(drafts.Values.Cast<object>().Any(value => ReferenceEquals(value, Field(owner, "_draft"))),
                    editor + " active draft belongs to its current mode dictionary");
            }
        }
        var empty = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        var conditions = SearchQuery.Empty with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
            CombatPotionRewards = new(2, CombatRewardSequenceOrderMode.Ordered,
                [new(CombatPotionSlotRequirement.DropAny, null), new(CombatPotionSlotRequirement.NoDrop, null)]),
            RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 5,
                new([BaseGameModelKeys.OrdinaryRelics.Whetstone], [], []))],
            StandardMaps = [new(1, StandardMapMetric.ReachableMaxElite, StandardMapComparison.AtLeast, 2, true)],
            TransformationAggregate = new(TransformationOpening.LeafyPoultice, TransformationPickupOrder.Any,
                false, false, false, TransformationAggregatePredicate.RareCountAtLeast, 1, [])
        };
        string origin = store.CurrentPersistentOriginSeed;
        foreach (string locale in new[] { "en", "zh" })
        {
            frame.RestoreDraft(empty with { Query = conditions }, render: false);
            frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale)); await Frames();
            string before = Query(); int target = (int)Field(frame, "_target");
            string searchMode = store.Preferences.SearchMode;
            Check(!Clear().Disabled, "enabled for authored conditions");
            root.GetViewport().PushInput(new InputEventMouseMotion { Position = Clear().GetGlobalRect().GetCenter() });
            await root.ToSignal(root.GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "clear-tooltip-" + locale + ".png"));
            root.GetViewport().PushInput(new InputEventMouseMotion { Position = new(1550, 850) });
            Clear().EmitSignal(Button.SignalName.Pressed); await Frames();
            Check(Clear().Disabled && !Undo().Disabled && ((Control)Field(frame, "_clearConditionsNotice")).Visible,
                "empty state and Undo are visible");
            Check(JsonSerializer.Serialize(frame.CaptureDraft().Query) == JsonSerializer.Serialize(SearchQuery.Empty), "all solo families empty");
            Check(frame.CaptureDraft().Character == empty.Character && frame.CaptureDraft().Ascension == 10 &&
                (int)Field(frame, "_target") == target && store.CurrentPersistentOriginSeed == origin &&
                store.Preferences.SearchMode == searchMode, "context and search controls preserved");
            Check(JsonSerializer.Serialize(store.LoadWorkbench()!.Query) == JsonSerializer.Serialize(SearchQuery.Empty), "clear persisted");
            var presets = frame.GetNode<Button>("OpenPresets").GetGlobalRect();
            var actions = ((Control)Field(frame, "_clearConditionsNotice")).GetGlobalRect();
            Check(Clear().Size.X >= 88 && Undo().Size.X >= 72 &&
                actions.Position.X == presets.Position.X && actions.End.Y <= frame.GetNode<ScrollContainer>("SearchCategoryScroll").GetGlobalRect().Position.Y &&
                actions.End.Y < presets.Position.Y &&
                actions.End.X <= frame.GetNode<Button>("SavePreset").GetGlobalRect().End.X,
                "clear and undo fit below context and above categories/presets");
            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "clear-conditions-" + locale + ".png"));
            Undo().EmitSignal(Button.SignalName.Pressed); await Frames();
            Check(Query() == before, "undo restores the draft");
            Check(!((Control)Field(frame, "_clearConditionsNotice")).Visible, "undo hint closes");
        }
        string solo = Query();
        frame.SetSearchMode(true); await Frames();
        var party = frame.CaptureDraft();
        foreach (int players in new[] { 2, 3, 4 })
        {
            var roster = Enumerable.Range(0, players).Select(slot => party.Players[slot % party.Players.Count] with
                { Slot = slot, NeowEditor = null, AncientEditor = null, EventEditor = null, CombatRewardEditor = null }).ToArray();
            var partyQuery = SearchQuery.Empty with { StandardMaps = conditions.StandardMaps,
                Players = Enumerable.Range(0, players).Select(slot => new PlayerOfferQuery(slot,
                    new([], [BaseGameModelKeys.Relics.SilkenTress], []))
                { Conditions = SearchQuery.Empty with { CombatPotionRewards = conditions.CombatPotionRewards,
                    RelicSequenceConstraints = conditions.RelicSequenceConstraints } }).ToArray() };
            var mapState = new MapEditorStateSnapshot(2,
                [new(1, false, new(StandardMapMetric.ReachableMaxElite, 2),
                    [new(21, StandardMapRoomType.Monster, 0, 21), new(22, StandardMapRoomType.Elite, 0, 22)]),
                 new(2, true, new(StandardMapMetric.ReachableMaxRest, 1), []),
                 new(3, false, new(StandardMapMetric.ReachableMaxUnknown, null), [])]);
            frame.RestoreDraft(party with { Players = roster, Query = partyQuery, MapEditor = mapState }, render: false);
            frame.Refresh("zh", JsonUiTextProvider.CreateUi13("zh")); await Frames();
            foreach (int slot in Enumerable.Range(0, players))
            {
                await Seat(slot);
                var combatDraft = Field(Field(frame, "_combatEditor"), "_draft");
                combatDraft.GetType().GetField("ExclusionBattleCount")!.SetValue(combatDraft, slot + 2);
            }
            var before = Draft(); var unlocks = Field(frame, "_lobbyUnlocks");
            CheckActiveBindings(true);
            Clear().EmitSignal(Button.SignalName.Pressed); await Frames();
            var cleared = Draft();
            Check(cleared.Query.StandardMaps.Count == 0 && cleared.Query.Players.All(p => Same(p.Conditions, SearchQuery.Empty) && p.Offers.IsEmpty),
                "shared and all off-screen personal conditions cleared for " + players + " players");
            Check((int)Field(frame, "_seat") == players - 1 && ReferenceEquals(unlocks, Field(frame, "_lobbyUnlocks")) &&
                cleared.Players.Select(p => p.Character).SequenceEqual(roster.Select(p => p.Character)), "party context retained");
            CheckActiveBindings(true);
            var afterClear = UndoState();
            await Seat(0); var afterFirstSeat = UndoState();
            await Seat(players - 1); var afterLastSeat = UndoState();
            File.WriteAllText(Path.Combine(output, $"clear-party-navigation-{players}p.json"), JsonSerializer.Serialize(new
                { Before = before, AfterClear = afterClear, AfterFirstSeat = afterFirstSeat, AfterLastSeat = afterLastSeat },
                new JsonSerializerOptions { WriteIndented = true }));
            Check(!Undo().Disabled && Field(frame, "_restoreClearedConditions") is Action,
                "viewing another seat retains the stable complete undo receipt");
            Undo().EmitSignal(Button.SignalName.Pressed); await Frames();
            var restored = Draft();
            var receipt = new { Players = players, Before = before, Cleared = cleared, Restored = restored,
                TypedDraftEqual = Same(before, restored), SelectedSeat = (int)Field(frame, "_seat") };
            File.WriteAllText(Path.Combine(output, players == 2 ? "clear-party-drafts.json" : $"clear-party-{players}p-drafts.json"),
                JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
            Check(Same(before, restored), "party undo restores the full typed draft and every sidecar for " + players + " players");
            CheckActiveBindings(true);
            foreach (int slot in Enumerable.Range(0, players))
            { await Seat(slot); Check(Same(before, Draft()), "restored complete draft survives subsequent seat views"); }

            foreach (string domain in new[] { "combat", "map" })
            {
                var domains = (System.Collections.IDictionary)Field(frame, "_domainButtons");
                ((Button)domains[domain]!).EmitSignal(Button.SignalName.Pressed); await Frames();
                var categoryBefore = Draft(); int clearedSeat = (int)Field(frame, "_seat");
                ((Button)Field(frame, "_clearCategory")).EmitSignal(Button.SignalName.Pressed); await Frames();
                var categoryCleared = Draft();
                Check(domain == "map"
                    ? categoryCleared.Query.StandardMaps.Count == 0 && Same(categoryCleared.Query.Players, categoryBefore.Query.Players)
                    : categoryCleared.Query.Players[clearedSeat].Conditions.CombatPotionRewards == null &&
                        Same(categoryCleared.Query.StandardMaps, categoryBefore.Query.StandardMaps) &&
                        Same(categoryCleared.Query.Players.Where(p => p.Slot != clearedSeat).ToArray(),
                            categoryBefore.Query.Players.Where(p => p.Slot != clearedSeat).ToArray()),
                    "category clear changes only its real shared/personal owner");
                await Seat((clearedSeat + 1) % players);
                Check(!Undo().Disabled, "category undo survives a seat view");
                Undo().EmitSignal(Button.SignalName.Pressed); await Frames();
                Check(Same(categoryBefore, Draft()), "category undo restores complete drafts and hidden sidecars: " + domain);
                CheckActiveBindings(true);
            }
            Clear().EmitSignal(Button.SignalName.Pressed); await Frames();
            await Seat(0);
            ((CombatRewardSearchEditor)Field(frame, "_combatEditor")).ImportQuery(SearchQuery.Empty with
                { CombatPotionRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [new(CombatPotionSlotRequirement.NoDrop, null)]) });
            var newEdit = Draft();
            Undo().EmitSignal(Button.SignalName.Pressed); await Frames();
            Check(Same(newEdit, Draft()) && Field(frame, "_restoreClearedConditions") is null,
                "a new personal edit invalidates whole-table undo without overwriting the new draft");
            Clear().EmitSignal(Button.SignalName.Pressed); await Frames();
            Check(!Undo().Disabled, "a new clear owns a fresh undo receipt");
            frame.GetNode<Button>("SearchMode0").EmitSignal(Button.SignalName.Pressed); await Frames();
            Check(Field(frame, "_restoreClearedConditions") is null && Query() == solo,
                "an actual mode/context change invalidates party undo without overwriting solo authoring");
            frame.GetNode<Button>("SearchMode1").EmitSignal(Button.SignalName.Pressed); await Frames();
        }
        frame.SetSearchMode(false); await Frames(); Check(Query() == solo, "other mode untouched");

        // Clear works even when a partially authored query cannot be exported.
        var transform = Field(frame, "_transformationEditor"); var draft = Field(transform, "_draft");
        draft.GetType().GetField("Objective")!.SetValue(draft, 1); // Target mode with no cards selected yet.
        bool incomplete = false;
        try { frame.CaptureDraft(); } catch (InvalidOperationException) { incomplete = true; }
        Check(incomplete, "fixture is a real unfinished target selection");
        Clear().EmitSignal(Button.SignalName.Pressed); await Frames();
        Check(Clear().Disabled && !Undo().Disabled, "invalid partial authoring is clearable");
        Undo().EmitSignal(Button.SignalName.Pressed); await Frames();
        Check((int)Field(transform, "_draft").GetType().GetField("Objective")!.GetValue(Field(transform, "_draft"))! == 1,
            "undo retains partial authoring without compile");
        draft.GetType().GetField("Objective")!.SetValue(draft, 0);
        frame.ClearAllConditions(); await Frames();
        ((NeowSearchEditor)Field(frame, "_neowEditor")).ImportQuery(SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.SilkenTress) });
        frame.UndoClearConditions();
        Check(frame.CaptureDraft().Query.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.SilkenTress,
            "new edits invalidate undo without overwriting them");
        frame.RestoreDraft(empty); frame.Refresh("zh", JsonUiTextProvider.CreateUi13("zh"));
        GD.Print($"CLEAR_CONDITIONS_PASS checks={checks};solo+party+undo+invalidDraft+context+persistence+en+zh=true");
    }
}
