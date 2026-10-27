using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorMapRouteDraftChecks
{
    private static readonly MapPosition Start = new(3, 0), Left1 = new(1, 1), Right1 = new(5, 1),
        Left2 = new(1, 2), Right2 = new(5, 2), Left3 = new(1, 3), Right3 = new(5, 3), Boss = new(3, 4);

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Map route draft: " + message); }

    internal static int Run()
    {
        PointSelectionAndAtomicStrokes();
        EraseAndSessionUndo();
        ActualProgressAndMapReplacement();
        TravelAndSnapshotPurity();
        NativeIncrementalMerge();
        NativeSharedSections();
        Console.WriteLine("PREDICTOR_MAP_ROUTE_DRAFT_PASS unordered atomic strokes; same-route fragments; explicit prefix; erase pending intent; session undo; factual boundary; map replacement; Boots/Flight; immutable graph; native section priorities; non-undoable reset");
        return 0;
    }

    private static MapPrediction Fixture() => new("MAP_DRAFT", new("ACT", "OVERGROWTH"), 0, 7,
    [
        new(Start, MapPointType.Ancient, true, [Left1, Right1]),
        new(Left1, MapPointType.Monster, true, [Left2]),
        new(Right1, MapPointType.Monster, true, [Right2]),
        new(Left2, MapPointType.Monster, true, [Left3]),
        new(Right2, MapPointType.Monster, true, [Right3]),
        new(Left3, MapPointType.RestSite, true, [Boss]),
        new(Right3, MapPointType.RestSite, true, [Boss]),
        new(Boss, MapPointType.Boss, false, [])
    ], 0);

    private static void PointSelectionAndAtomicStrokes()
    {
        var draft = new PredictorMapRouteDraft(Fixture());
        Check(draft.IsCompatible && draft.Selected.IsEmpty && !draft.CanUndo, "initial empty draft is valid");
        Check(draft.ApplyStroke([Boss, Left2, Left2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed &&
            draft.Selected.SequenceEqual([Left2, Boss]) && draft.ReadyPrefix.IsEmpty,
            "top-down disconnected gesture normalizes and preserves gaps without auto-fill");
        Check(draft.ApplyStroke([Left1, Start], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed &&
            draft.ReadyPrefix.SequenceEqual([Start, Left1, Left2]), "selected prefix ends at the unselected third row");
        var selected = draft.Selected;
        Check(draft.ApplyStroke([Left3, Right1], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid &&
            draft.Selected.SequenceEqual(selected), "whole gesture rejects a same-row contradiction without partial acceptance");
        Check(draft.ApplyStroke([Left3, new(99, 99)], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid &&
            draft.Selected.SequenceEqual(selected), "unknown hit rejects gesture atomically");
        Check(draft.ApplyStroke([Start, Left2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.NoChange &&
            draft.Undo() && draft.Selected.SequenceEqual([Left2, Boss]), "invalid and repeated draws consume no undo entries");

        var mixed = new PredictorMapRouteDraft(Fixture(), Start);
        Check(mixed.ApplyStroke([Left1, Right2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid &&
            mixed.Selected.IsEmpty && !mixed.CanUndo, "individually reachable points still need one shared route");
        var reversed = new PredictorMapRouteDraft(Fixture(), Start);
        var forward = new PredictorMapRouteDraft(Fixture(), Start);
        reversed.ApplyStroke([Boss, Left3, Left2, Left1], PredictorMapStrokeMode.Draw);
        forward.ApplyStroke([Left1, Left2, Left3, Boss], PredictorMapStrokeMode.Draw);
        Check(reversed.Selected.SequenceEqual(forward.Selected) && reversed.ReadyPrefix.SequenceEqual(forward.ReadyPrefix),
            "traversal order is independent of gesture direction");
    }

    private static void EraseAndSessionUndo()
    {
        var draft = new PredictorMapRouteDraft(Fixture(), Start);
        draft.ApplyStroke([Left1, Left2, Left3, Boss], PredictorMapStrokeMode.Draw);
        Check(draft.ApplyStroke([Left2], PredictorMapStrokeMode.Erase) == PredictorMapDraftEditResult.Changed &&
            draft.Selected.SequenceEqual([Left1, Left3, Boss]) && draft.ReadyPrefix.SequenceEqual([Left1]),
            "erase removes only its hit; later points remain pending");
        Check(draft.ApplyStroke([Right2, new(99, 99)], PredictorMapStrokeMode.Erase) == PredictorMapDraftEditResult.NoChange,
            "eraser misses are no-ops");
        Check(draft.Clear() == PredictorMapDraftEditResult.Changed && draft.Selected.IsEmpty &&
            draft.Clear() == PredictorMapDraftEditResult.NoChange, "meaningful clear is one session operation");
        Check(draft.Undo() && draft.Selected.SequenceEqual([Left1, Left3, Boss]) &&
            draft.Undo() && draft.Selected.SequenceEqual([Left1, Left2, Left3, Boss]) &&
            draft.Undo() && draft.Selected.IsEmpty && !draft.Undo(), "session undo restores each successful operation");
        Check(draft.ApplyStroke([Start], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.NoChange && !draft.CanUndo,
            "factual-point hits cannot edit prior traversal");
    }

    private static void ActualProgressAndMapReplacement()
    {
        var draft = new PredictorMapRouteDraft(Fixture(), Start);
        draft.ApplyStroke([Left1, Left2, Left3, Boss], PredictorMapStrokeMode.Draw);
        Check(draft.Rebase(Fixture(), Left1).SequenceEqual([Left1]) &&
            draft.Selected.SequenceEqual([Left2, Left3, Boss]) && draft.ReadyPrefix.SequenceEqual([Left2, Left3, Boss]) &&
            !draft.CanUndo && !draft.Undo(), "actual progress keeps future intent and establishes a fresh undo boundary");
        draft.ApplyStroke([Left2], PredictorMapStrokeMode.Erase);
        draft.Rebase(Fixture(), Left1);
        Check(!draft.CanUndo && draft.Selected.SequenceEqual([Left3, Boss]), "another factual boundary expires session history");

        var pending = new PredictorMapRouteDraft(Fixture(), Start);
        pending.ApplyStroke([Left2, Boss], PredictorMapStrokeMode.Draw);
        pending.Rebase(Fixture(), Right1);
        Check(!pending.IsCompatible && pending.ReadyPrefix.IsEmpty && pending.Selected.SequenceEqual([Left2, Boss]) && !pending.CanUndo,
            "changed actual branch preserves incompatible future intent without choosing a replacement");
        Check(pending.ApplyStroke([Right2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid &&
            pending.ApplyStroke([Left2], PredictorMapStrokeMode.Erase) == PredictorMapDraftEditResult.Changed &&
            pending.IsCompatible && pending.Selected.SequenceEqual([Boss]), "explicit erase repairs changed-boundary conflict");
        Check(pending.Undo() && !pending.IsCompatible && pending.Selected.SequenceEqual([Left2, Boss]),
            "undo can restore retained incompatible intent within the new session");

        var replacement = Fixture() with
        {
            Points = Fixture().Points.Where(p => p.Position != Left2).Select(p => p.Position == Left1
                ? p with { Children = [Right2] } : p).ToArray()
        };
        var removed = pending.Rebase(replacement, Right1);
        Check(removed.SequenceEqual([Left2]) && pending.Selected.SequenceEqual([Boss]) && pending.IsCompatible && !pending.CanUndo,
            "replacement removes absent coordinates and keeps surviving future intent");
        pending.Rebase(Fixture() with { Act = new("ACT", "UNDERDOCKS"), ActIndex = 1 }, null);
        Check(pending.Selected.IsEmpty && !pending.CanUndo, "coordinates are not borrowed across acts");
    }

    private static void TravelAndSnapshotPurity()
    {
        MapPosition[] twoJumps = [Left1, Right2, Left3, Boss];
        var oneBoot = new PredictorMapRouteDraft(Fixture(), Start, new(1));
        Check(oneBoot.ApplyStroke(twoJumps, PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid,
            "whole-route compatibility respects the total remaining Boots budget");
        var twoBoots = new PredictorMapRouteDraft(Fixture(), Start, new(2));
        Check(twoBoots.ApplyStroke(twoJumps, PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed &&
            twoBoots.ReadyPrefix.SequenceEqual(twoJumps), "enough Boots permits both non-edge entries");
        var flight = new PredictorMapRouteDraft(Fixture(), Start, new(0, true));
        Check(flight.ApplyStroke(twoJumps, PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed,
            "Flight permits non-edges without a remaining Boots admission budget");
        Check(twoBoots.ApplyStroke([Right1, Right2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid,
            "free travel still cannot select multiple points at one row");

        var children = new[] { Left1 };
        var points = Fixture().Points.Select(p => p.Position == Start ? p with { Children = children } : p).ToArray();
        var map = Fixture() with { Points = points };
        var snapshot = new PredictorMapRouteDraft(map, Start);
        children[0] = Right1;
        points[1] = points[1] with { Children = [Right2] };
        Check(snapshot.ApplyStroke([Left1, Left2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed &&
            snapshot.Map.Points.Single(p => p.Position == Start).Children.SequenceEqual([Left1]) &&
            map.Points.Single(p => p.Position == Start).Children.SequenceEqual([Right1]),
            "draft owns a frozen graph without mutating caller point or child arrays");
        var unchanged = snapshot.Selected;
        try { snapshot.Rebase(map, new(99, 99)); }
        catch (ArgumentException) { }
        Check(snapshot.Selected.SequenceEqual(unchanged) && snapshot.CanUndo, "invalid rebase leaves the old session intact");
    }

    private static void NativeIncrementalMerge()
    {
        var draft = new PredictorMapRouteDraft(Fixture(), Start);
        draft.ApplyNativeStroke([Left3, Boss], PredictorMapStrokeMode.Draw);
        draft.ApplyNativeStroke([Left1], PredictorMapStrokeMode.Draw);
        Check(draft.Selected.SequenceEqual([Left1, Left3, Boss]) && draft.ReadyPrefix.SequenceEqual([Left1]),
            "native disjoint fragments coexist without filling their hole");
        draft.ApplyNativeStroke([Left2], PredictorMapStrokeMode.Draw);
        Check(draft.ReadyPrefix.SequenceEqual([Left1, Left2, Left3, Boss]),
            "native ready prefix advances only after explicit hole selection");
        Check(draft.ApplyNativeStroke([Right2, Right3], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.NoChange &&
            draft.Selected.SequenceEqual([Left1, Left2, Left3, Boss]),
            "an earlier-start fragment wins an unanchored later branch conflict");
        var unchanged = draft.Selected;
        Check(draft.ApplyNativeStroke([Left2, Right2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid &&
            draft.ApplyNativeStroke([Left1, Right2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid &&
            draft.ApplyNativeStroke([Right1, new(99, 99)], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid &&
            draft.Selected.SequenceEqual(unchanged), "ambiguous, incompatible and unknown native hits preserve existing intent");

        var tie = new PredictorMapRouteDraft(Fixture(), Start);
        tie.ApplyNativeStroke([Left2, Left3, Boss], PredictorMapStrokeMode.Draw);
        tie.ApplyNativeStroke([Right3, Right2], PredictorMapStrokeMode.Draw);
        Check(tie.Selected.SequenceEqual([Right2, Right3, Boss]) && tie.ReadyPrefix.IsEmpty,
            "newest same-start native branch replaces conflicting points while retaining compatible suffix");
        tie.ApplyNativeStroke([Left1, Left2], PredictorMapStrokeMode.Draw);
        Check(tie.Selected.SequenceEqual([Left1, Left2, Boss]) && tie.ReadyPrefix.SequenceEqual([Left1, Left2]),
            "lower-start incoming intent wins and retains compatible later selected points without inventing a route");
        Check(tie.Reset() == PredictorMapDraftEditResult.Changed && tie.Selected.IsEmpty && !tie.CanUndo && !tie.Undo() &&
            tie.Reset() == PredictorMapDraftEditResult.NoChange, "native reset cannot restore cleared selection");

        MapPosition[] jumps = [Left1, Right2, Left3, Boss];
        var oneBoot = new PredictorMapRouteDraft(Fixture(), Start, new(1));
        var twoBoots = new PredictorMapRouteDraft(Fixture(), Start, new(2));
        var flight = new PredictorMapRouteDraft(Fixture(), Start, new(0, true));
        Check(oneBoot.ApplyNativeStroke(jumps, PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid &&
            twoBoots.ApplyNativeStroke(Enumerable.Reverse(jumps), PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed &&
            flight.ApplyNativeStroke(jumps, PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed,
            "native gestures use the existing total Boots and Flight legality");
        Check(twoBoots.ApplyNativeStroke([Start], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.NoChange,
            "native gestures cannot edit real prior traversal");
        var legacy = new PredictorMapRouteDraft(Fixture(), Start);
        legacy.ApplyStroke([Left1], PredictorMapStrokeMode.Draw);
        Check(legacy.CanUndo && legacy.ApplyNativeStroke([Left2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed &&
            !legacy.CanUndo && !legacy.Undo(), "native edit expires unused legacy history without recording native snapshots");
        Check(legacy.ApplyNativeStroke([Left1], PredictorMapStrokeMode.Erase) == PredictorMapDraftEditResult.Changed &&
            legacy.Selected.SequenceEqual([Left2]) && legacy.ReadyPrefix.IsEmpty && !legacy.CanUndo,
            "native erase preserves unhit pending intent and creates no undo history");
        legacy.ApplyStroke([Left3], PredictorMapStrokeMode.Draw);
        legacy.Clear();
        Check(legacy.Selected.IsEmpty && legacy.CanUndo && legacy.Reset() == PredictorMapDraftEditResult.NoChange && !legacy.Undo(),
            "native reset also expires old history when selection was already empty");
    }

    private static void NativeSharedSections()
    {
        MapPosition first = new(3, 1), left2 = new(1, 2), right2 = new(5, 2), left3 = new(1, 3), right3 = new(5, 3),
            join4 = new(3, 4), left5 = new(1, 5), right5 = new(5, 5), left6 = new(1, 6), right6 = new(5, 6),
            join7 = new(3, 7), suffix8 = new(3, 8), boss9 = new(3, 9);
        var map = Fixture() with
        {
            Points = [new(Start, MapPointType.Ancient, true, [first]),
                new(first, MapPointType.Monster, true, [left2, right2]),
                new(left2, MapPointType.Monster, true, [left3]), new(right2, MapPointType.Monster, true, [right3]),
                new(left3, MapPointType.Monster, true, [join4]), new(right3, MapPointType.Monster, true, [join4]),
                new(join4, MapPointType.Monster, true, [left5, right5]),
                new(left5, MapPointType.Monster, true, [left6]), new(right5, MapPointType.Monster, true, [right6]),
                new(left6, MapPointType.Monster, true, [join7]), new(right6, MapPointType.Monster, true, [join7]),
                new(join7, MapPointType.Monster, true, [suffix8]), new(suffix8, MapPointType.RestSite, true, [boss9]),
                new(boss9, MapPointType.Boss, false, [])]
        };
        var draft = new PredictorMapRouteDraft(map, Start);
        draft.ApplyNativeStroke([first, left2, left3, join4, left5, left6, join7, suffix8, boss9], PredictorMapStrokeMode.Draw);
        draft.ApplyNativeStroke([join7, right6, right5, join4], PredictorMapStrokeMode.Draw);
        Check(draft.Selected.SequenceEqual([first, left2, left3, join4, right5, right6, join7, suffix8, boss9]) &&
            draft.ReadyPrefix.SequenceEqual(draft.Selected),
            "shared anchor splits old lower-start intent so newest branch wins and both prefix and rejoined suffix survive");
        draft.ApplyNativeStroke([join4, left5, left6], PredictorMapStrokeMode.Draw);
        Check(draft.Selected.SequenceEqual([first, left2, left3, join4, left5, left6, join7, suffix8, boss9]),
            "a later edit can replace the anchored section again and retain a suffix beyond an unselected rejoin hit");
        draft.ApplyNativeStroke([first, right2, right3, join4], PredictorMapStrokeMode.Draw);
        Check(draft.Selected.SequenceEqual([first, right2, right3, join4, left5, left6, join7, suffix8, boss9]),
            "editing an earlier shared section preserves the independently authored later branch");
        Check(draft.ApplyStroke([left2], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid,
            "native merge does not weaken strict factual bridge union validation");
    }
}
