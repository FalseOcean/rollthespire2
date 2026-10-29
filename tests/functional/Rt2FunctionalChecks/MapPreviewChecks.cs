using System.Reflection;
using System.Runtime.CompilerServices;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.FamilyExecution;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using GameMap = MegaCrit.Sts2.Core.Map;

internal static class MapPreviewChecks
{
    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    internal static int Run()
    {
        int checks = CheckGraphSegmentReplay() + CheckSearchReplay();
        foreach (var (kind, actType, index) in new[] { ("OVERGROWTH", typeof(Overgrowth),0),
                     ("UNDERDOCKS",typeof(Underdocks),0), ("HIVE",typeof(Hive),1), ("GLORY",typeof(Glory),2) })
        foreach (int level in new[] { 0, 1, 10 })
        foreach (ulong ordinal in new ulong[] { 0, 37, 8307042, 100000501454 })
        {
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            bool second = index == 2 && level == 10;
            var actual = Beta111MapPredictor.Predict(seed, RuntimeProfileId.Beta111,
                new ModelKey("ACT", kind), index, level, 1, second, default);
            string graph = string.Join("\n", actual.Points.OrderBy(n => n.Position.Row).ThenBy(n => n.Position.Column)
                .Select(n => $"{n.Position.Row},{n.Position.Column}:{(int)n.Type}:{n.CanBeModified}->" + string.Join(";",
                    n.Children.OrderBy(c => c.Row).ThenBy(c => c.Column).Select(c => $"{c.Row},{c.Column}"))));
            // Actual DLL oracle, isolated test process ONLY. Production never sets these singleton fields.
            var act = (ActModel)RuntimeHelpers.GetUninitializedObject(actType);
            var rooms = new RoomSet();
            if (second) rooms.SecondBoss = (EncounterModel)RuntimeHelpers.GetUninitializedObject(typeof(KaiserCrabBoss));
            Set(act, "_rooms", rooms);
            var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            Set(run, "_players", new List<Player> { (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player)) });
            Set(run, "<Acts>k__BackingField", Enumerable.Repeat(act, index + 1).ToList());
            Set(run, "_currentActIndex", index); Set(run, "<AscensionLevel>k__BackingField", level);
            Set(run, "<Rng>k__BackingField", new RunRngSet(seed));
            var manager = RunManager.Instance;
            var stateProperty = typeof(RunManager).GetProperty("State", Fields)!;
            var ascProperty = typeof(RunManager).GetProperty("AscensionManager", Fields)!;
            var oldState = stateProperty.GetValue(manager); var oldAsc = ascProperty.GetValue(manager);
            try
            {
                stateProperty.SetValue(manager, run); ascProperty.SetValue(manager, new AscensionManager(level));
                var oracle = (GameMap.StandardActMap)act.CreateMap(run, false);
                var nodes = oracle.GetAllMapPoints().Prepend(oracle.StartingMapPoint).Append(oracle.BossMapPoint).ToList();
                if (oracle.SecondBossMapPoint is { } secondNode) nodes.Add(secondNode);
                string expected = string.Join("\n", nodes.OrderBy(n => n.coord.row).ThenBy(n => n.coord.col)
                    .Select(n => $"{n.coord.row},{n.coord.col}:{(int)n.PointType}:{n.CanBeModified}->" + string.Join(";",
                        n.Children.OrderBy(c => c.coord.row).ThenBy(c => c.coord.col).Select(c => $"{c.coord.row},{c.coord.col}"))));
                var rng = typeof(GameMap.StandardActMap).GetField("_rng", Fields)!.GetValue(oracle)!;
                int calls = (int)rng.GetType().GetField("_counter", Fields)!.GetValue(rng)!;
                if (graph != expected || calls != actual.RngCalls) throw new Exception($"Map parity {seed}/{kind}/A{level}");
                checks++;
            }
            finally { stateProperty.SetValue(manager, oldState); ascProperty.SetValue(manager, oldAsc); }
        }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { Beta111MapPredictor.Predict("000000000000", RuntimeProfileId.Beta111, new ModelKey("ACT","HIVE"),1,0,1,false,cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { checks++; }
        Console.WriteLine($"MapPreview PASS: {checks} checks; 48 actual DLL full graphs/RNG counts, including full DoubleBoss and capacity witness.");
        return 0;
    }
    private static int CheckGraphSegmentReplay()
    {
        // Compare complete graph state and insertion order with both previous
        // graph implementations, independently of the numerical Fast replay.
        // Include SP/MP room lengths and all map-generation context switches.
        int checks = 0;
        string Snapshot(GeneratedStandardMap map)
        {
            string Id(MapNode node) => $"{node.RawRow},{node.RawCol}";
            return $"{map.Seed}|{map.Context}|{map.MapRngCalls}|" +
                $"{map.Grid.GetLength(0)},{map.Grid.GetLength(1)}|" +
                $"{map.Counts.NumOfElites},{map.Counts.NumOfRests},{map.Counts.NumOfShops},{map.Counts.NumOfUnknowns}|" +
                string.Join(";", map.StartMapPoints.Select(Id)) + "\n" +
                string.Join("\n", map.AllNodes().Select(node =>
                    $"{Id(node)}:{node.FinalRow},{node.FinalCol}:{node.PointType}:{node.CanBeModified}:" +
                    string.Join(";", node.Parents.Select(Id)) + "->" + string.Join(";", node.Children.Select(Id))));
        }
        foreach (ActKind act in Enum.GetValues<ActKind>())
        foreach (bool multiplayer in new[] { false, true })
        foreach (bool swarming in new[] { false, true })
        foreach (bool replaceTreasure in new[] { false, true })
        foreach (ulong ordinal in new ulong[] { 0, 37, 8307042, 100000501454 })
        {
            int index = act switch { ActKind.Hive => 1, ActKind.Glory => 2, _ => 0 };
            var context = new ActContext(act, index, multiplayer, swarming,
                act == ActKind.Glory && (ordinal & 1) != 0, replaceTreasure);
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            var reference = Beta111StandardMapGenerator.GenerateStandardMap(seed, context, mode: MapReplayMode.Reference);
            var packed = Beta111StandardMapGenerator.GenerateStandardMap(seed, context, mode: MapReplayMode.PackedSegmentDedup);
            var graph = Beta111StandardMapGenerator.GenerateStandardMap(seed, context);
            string expected = Snapshot(reference);
            if (expected != Snapshot(packed) || expected != Snapshot(graph) ||
                MapBasisScalars.Evaluate(reference) != MapBasisScalars.Evaluate(graph))
                throw new Exception($"Graph segment discovery parity {seed}/{context}");
            checks++;
        }
        Console.WriteLine($"MAP_GRAPH_DISCOVERY_PARITY checks={checks};authority=ReferenceAndPackedFullGraphs;" +
            "includes=RngCallsRawAndFinalCoordinatesTypesModifiabilityParentChildAndStartOrder;contexts=SP/MP/AllActs/Swarming/DoubleBoss/ReplaceTreasure");
        return checks;
    }
    private static int CheckSearchReplay()
    {
        int checks=0;
        foreach(int level in new[]{0,1,10})
        foreach(int act in new[]{1,2,3})
        {
            var context=StandardMapFamily.Context(act,level); var fast=new BoundedActMap(context);
            foreach(ulong ordinal in Enumerable.Range(0,256).Select(i=>(ulong)i*7919).Concat(new[]{8307042UL,100000501454UL}))
            {
                string seed=VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance,ordinal);
                fast.ReplayRoot(Beta111Profile.Instance.ComputeRootSeed(seed));
                var graph=Beta111StandardMapGenerator.GenerateStandardMap(seed,context);
                var actual=fast.EvaluateFinalScalars(); var expected=MapBasisScalars.Evaluate(graph);
                if(actual!=expected || fast.RngCalls!=graph.MapRngCalls) throw new Exception($"M Final parity {seed}/Act{act}/A{level}: {actual} != {expected}");
                checks++;
            }
            using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
            try {fast.ReplayRoot(0,cancelled.Token);throw new Exception("M ignored cancellation");}
            catch(OperationCanceledException){checks++;}
        }
        Console.WriteLine($"MAP_FINAL_SEARCH_PARITY checks={checks};evidence=FullFinalPlusScalarAndRngCalls;timing=false");
        return checks;
    }
    private static void Set(object target, string name, object value)
    {
        for (Type? t = target.GetType(); t is not null; t = t.BaseType)
            if (t.GetField(name, Fields) is { } f) { f.SetValue(target, value); return; }
        throw new MissingFieldException(name);
    }
}
