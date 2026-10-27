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
        void Set(object obj, string name, object value) => obj.GetType().GetField(name, flags)!.SetValue(obj, value);
        Button Clear() => (Button)Field(frame, "_clearConditions");
        Button Undo() => (Button)Field(frame, "_undoClearConditions");
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new InvalidOperationException("ClearConditions:" + reason); }
        string Query() => JsonSerializer.Serialize(frame.CaptureDraft().WithoutCapturedAuthority());
        async Task Frames() { for (int i = 0; i < 3; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
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
        var partyQuery = SearchQuery.Empty with { StandardMaps = conditions.StandardMaps,
            Players = party.Query.Players.Select(p => p with { Conditions = p.Slot == 1
                ? SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.SilkenTress), CombatPotionRewards = conditions.CombatPotionRewards }
                : SearchQuery.Empty }).ToArray() };
        frame.RestoreDraft(party with { Query = partyQuery }, render: false);
        Set(frame, "_seat", 1); frame.Refresh("zh", JsonUiTextProvider.CreateUi13("zh")); await Frames();
        string partyBefore = Query(); var unlocks = Field(frame, "_lobbyUnlocks");
        Clear().EmitSignal(Button.SignalName.Pressed); await Frames();
        var cleared = frame.CaptureDraft();
        Check(cleared.Query.StandardMaps.Count == 0 && cleared.Query.Players.All(p => JsonSerializer.Serialize(p.Conditions) == JsonSerializer.Serialize(SearchQuery.Empty) && p.Offers.IsEmpty),
            "shared and off-screen player conditions cleared");
        Check((int)Field(frame, "_seat") == 1 && ReferenceEquals(unlocks, Field(frame, "_lobbyUnlocks")) &&
            cleared.Players.Select(p => p.Character).SequenceEqual(party.Players.Select(p => p.Character)), "party context retained");
        Undo().EmitSignal(Button.SignalName.Pressed); await Frames();
        Check(Query() == partyBefore, "party undo");
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
