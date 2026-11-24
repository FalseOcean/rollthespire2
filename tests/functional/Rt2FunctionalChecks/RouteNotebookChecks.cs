using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using NativeMap = MegaCrit.Sts2.Core.Map;

namespace RolltheSpire2.Tests.FastSearch;

internal static class RouteNotebookChecks
{
    internal static void Run(AnalysisPage page, string seed)
    {
        void Check(bool pass, string message) { if (!pass) throw new Exception("Route notebook: " + message); }
        var path = Path.Combine(Path.GetTempPath(), "rt2-route-notebook", Guid.NewGuid().ToString("N"), "routes.json");
        var store = new MapRouteNotebook(path);
        var solo = new RouteBookKey(seed, 10, 1, "");
        store.Edit(solo, b => { b.Spoils = true; b.Drawn["2/spoils"] = [new(3, 0)]; });
        var live = solo with { Room = "100/host" };
        store.Edit(live, b => b.NativeDrawn[1] = [new(3, 2)]);
        var restored = new MapRouteNotebook(path).Read(live);
        Check(restored.Spoils == true && restored.Drawn["2/spoils"][0] == new MapPosition(3,0) &&
            restored.NativeDrawn[1][0] == new MapPosition(3,2), "restart restores the plan in the matching run");
        foreach (string room in new[] { "A","B","C","A","D" })
            store.Edit(live with { Room = room, Players = 2 }, b => b.Spoils = true);
        Check(new[]{"A","C","D"}.All(r => store.Read(live with { Room = r, Players = 2 }).Spoils == true) &&
            store.Read(live with { Room = "B", Players = 2 }).Spoils is null &&
            store.Read(live).NativeDrawn.Count == 1, "multiplayer LRU and separate solo slot");
        store.Edit(live with { Room = "new solo" }, b => b.Spoils = true);
        Check(store.Read(live).NativeDrawn.Count == 0, "one singleplayer record");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var asc = typeof(RunManager).GetProperty("AscensionManager", flags)!;
        var state = typeof(RunManager).GetProperty("State", flags)!;
        var oldAsc = asc.GetValue(RunManager.Instance);
        var oldRun = state.GetValue(RunManager.Instance);
        var semantics = typeof(NativeMap.SpoilsActMap).GetField("_lowestRowByPointType", BindingFlags.NonPublic | BindingFlags.Static) is null
            ? RngSemanticProfileId.Modern110 : RngSemanticProfileId.Modern113;
        try
        {
            foreach (var players in new[] { 1, 2 })
            foreach (var level in new[] { 0, 10 })
            {
                var roster = Enumerable.Range(1, players).Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i)).ToArray();
                var native = RunState.CreateForTest(roster, seed: seed, ascensionLevel: level);
                state.SetValue(RunManager.Instance, native);
                asc.SetValue(RunManager.Instance, NativeGameFixture.StandardAscension(level));
                native.CurrentActIndex = 1;
                native.Map = new NativeMap.SpoilsActMap(native);
                var actual = InRunMapSnapshot.Actual(native);
                var generated = Beta111StandardMapGenerator.GenerateSpoilsMap(seed, actual.Act, level,
                    multiplayer: players > 1, semantics: semantics);
                Check(actual.Points.Count == generated.Points.Count, $"native size {players}/{level} {actual.Points.Count}/{generated.Points.Count}");
                foreach (var p in actual.Points)
                {
                    var other = generated.Points.SingleOrDefault(n => n.Position == p.Position);
                    Check(other?.Type == p.Type && other.CanBeModified == p.CanBeModified &&
                        other.Children.ToHashSet().SetEquals(p.Children), $"native room/edges {players}/{level}/{p.Position}");
                }
                if (players != 1 || level != 0) continue;
                var route = new MapRouteSet(actual).At(0);
                foreach (var p in route.Take(2))
                    native.AddVisitedMapCoord(new(p.Column, p.Row));
                page.SetLiveRunPredictionPremises(LiveRunPredictionPremises.Capture(native));
                page.SyncLiveRoute(native);
                var key = RouteBookKey.ForRun(native);
                Check(MapRouteNotebook.Local().Read(key).Seen[2].Walked.SequenceEqual(route.Take(2)), "actual entered coordinates");
                native.AddVisitedMapCoord(new(route[2].Column, route[2].Row));
                page.SyncLiveRoute(native);
                var read = MapRouteNotebook.Local().Read(key).Seen[2];
                Check(read.Walked.SequenceEqual(route.Take(3)) && read.Map(seed, 2).Points.Count == actual.Points.Count, "continued read after travel");
                Check(page.FindChild("Act2OverviewMap", true, false) is not null && page.FindChild("SpoilsMapToggle", true, false) is null, "actual act opens with native graph");
                var planned = new MapRouteSet(actual).MatchFrom([new(0, 6), new(1, 10)], read.Walked);
                Check(planned.Take(3).SequenceEqual(read.Walked), "redraw keeps actual prefix");
                var nativeDraft = new Core.PredictorRuntime.PredictorMapRouteDraft(actual, route[2]);
                nativeDraft.RestoreDrawnPoints([route[1], route[3], route[5]]);
                Check(nativeDraft.Selected.SequenceEqual(new[] {route[3], route[5]}) &&
                    nativeDraft.ReadyPrefix.SequenceEqual(new[] {route[3]}), "native ink restore keeps only authored future points");
            }
        }
        finally { state.SetValue(RunManager.Instance, oldRun); asc.SetValue(RunManager.Instance, oldAsc); }
        Console.WriteLine("ROUTE_NOTEBOOK_PASS native Spoils SP/MP A0/A10; real path advances; restore and 1+3 retention");
    }
}
