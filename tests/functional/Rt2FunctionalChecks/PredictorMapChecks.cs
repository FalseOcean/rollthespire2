using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.InRunMap;
using MapPointType = RolltheSpire2.Core.Prediction.Maps.MapPointType;

internal static class PredictorMapChecks
{
    private static void Check(bool ok, string why)
    {
        if (!ok) throw new InvalidOperationException("MAP_RUNTIME " + why);
    }

    internal static void Run(PredictorRun baseline)
    {
        var context = baseline.Context;
        var state = baseline.Working with { Relics = baseline.Working.Relics.Add(
            new(baseline.Working.NextInstanceId, new("RELIC", "GOLDEN_COMPASS"), 1)
            { GoldenPathAct = 0 }), NextInstanceId = baseline.Working.NextInstanceId + 1 };
        var golden = new PredictorRun(context, state);
        var map = golden.GenerateActualMap(1);
        var previewRun = new PredictorRun(context with { NumberOfRuns = 1 }, state);
        var beforePreview = previewRun.Working;
        var previewPath = map.Points.Select(p => p.Position).ToImmutableArray();
        var preview = previewRun.PreviewRouteRoomsUnderCurrentInventory(1, previewPath);
        Check(preview.Length == map.Points.Count(p => p.Type == MapPointType.Unknown) &&
            preview.All(p => p.Preview && p.Room == PredictorRoomKind.Event), "Golden Compass conditional room preview");
        Check(ReferenceEquals(beforePreview, previewRun.Working) &&
            preview.SequenceEqual(previewRun.PreviewRouteRoomsUnderCurrentInventory(1, previewPath)),
            "room preview does not consume working RNG/state and is repeatable");
        Check(previewRun.PreviewRouteRoomsUnderCurrentInventory(2, previewPath).IsEmpty,
            "unentered future Act does not borrow current Act unknown odds");
        Console.WriteLine("RUNTIME_ROOM_PREVIEW_PASS Golden Compass categories; isolated RNG/state; repeatable; future Act deferred");
        MapPointType[] sourceTypes =
        [
            MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster, MapPointType.RestSite,
            MapPointType.Monster, MapPointType.RestSite, MapPointType.Unknown, MapPointType.Treasure,
            MapPointType.Unknown, MapPointType.Treasure, MapPointType.Unknown, MapPointType.Shop,
            MapPointType.Elite, MapPointType.RestSite, MapPointType.Elite, MapPointType.RestSite
        ];
        Check(map.Points.Count == 18 && map.Points[0].Type == MapPointType.Ancient &&
            map.Points[^1].Type == MapPointType.Boss &&
            map.Points.Skip(1).Take(16).Select(p => p.Type).SequenceEqual(sourceTypes) &&
            map.Points.All(p => p.Position.Column == 3) && golden.UnknownRoomMustBeEvent(1),
            "GoldenPath source geometry and same-act unknown override");
        var nativePlayer = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var native = RunState.CreateForTest([nativePlayer],
            state.Acts.Select(a => ModelDb.GetById<ActModel>(new MegaCrit.Sts2.Core.Models.ModelId(a.Key.Category, a.Key.Entry))).ToArray(),
            seed: context.Seed, ascensionLevel: context.Ascension);
        var nativeGolden = new GoldenPathActMap(native);
        Check(map.Points.Skip(1).Take(16).All(p => nativeGolden.GetPoint(p.Position.Column, p.Position.Row)?.PointType.ToString() == p.Type.ToString()) &&
            nativeGolden.BossMapPoint.coord.row == map.Points[^1].Position.Row,
            "native GoldenPathActMap point identity");
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var stateProperty = typeof(RunManager).GetProperty("State",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        object? oldAscension = ascensionProperty.GetValue(RunManager.Instance);
        object? oldState = stateProperty.GetValue(RunManager.Instance);
        SpoilsActMap nativeSpoils;
        ActMap nativeBigGameSpoils;
        try
        {
            ascensionProperty.SetValue(RunManager.Instance, new AscensionManager(context.Ascension));
            stateProperty.SetValue(RunManager.Instance, native);
            Check(RunManager.Instance.HasAscension(AscensionLevel.SwarmingElites) == (context.Ascension >= 1),
                "native Spoils AscensionManager setup");
            native.CurrentActIndex = 1;
            nativeSpoils = new SpoilsActMap(native);
            nativeBigGameSpoils = ModelDb.Modifier<BigGameHunter>().ToMutable()
                .ModifyGeneratedMap(native, nativeSpoils, 1);
        }
        finally
        {
            stateProperty.SetValue(RunManager.Instance, oldState);
            ascensionProperty.SetValue(RunManager.Instance, oldAscension);
        }
        var spoilsPrediction = Beta111StandardMapGenerator.GenerateSpoilsMap(context.Seed,
            state.Acts[1].Key, context.Ascension);
        var nativeRng = (MegaCrit.Sts2.Core.Random.Rng)typeof(SpoilsActMap)
            .GetField("_rng", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(nativeSpoils)!;
        int nativeRngCalls = nativeRng.ToSerializable().counter;
        var sourcePoints = nativeSpoils.GetAllMapPoints().Append(nativeSpoils.StartingMapPoint)
            .Append(nativeSpoils.BossMapPoint).ToDictionary(p => new MapPosition(p.coord.col, p.coord.row));
        if (sourcePoints.Count != spoilsPrediction.Points.Count)
            throw new InvalidOperationException($"MAP_RUNTIME Spoils point count native={sourcePoints.Count} predicted={spoilsPrediction.Points.Count}");
        foreach (var p in spoilsPrediction.Points)
        {
            if (!sourcePoints.TryGetValue(p.Position, out var sourcePoint))
                throw new InvalidOperationException($"MAP_RUNTIME Spoils missing native point {p.Position}");
            var nativeChildren = sourcePoint.Children.Select(c => new MapPosition(c.coord.col, c.coord.row))
                .OrderBy(c => c.Column).ThenBy(c => c.Row).ToArray();
            if (!nativeChildren.SequenceEqual(p.Children))
                throw new InvalidOperationException($"MAP_RUNTIME Spoils {p.Position} type {sourcePoint.PointType}/{p.Type} " +
                    $"children [{string.Join(',', nativeChildren)}]/[{string.Join(',', p.Children)}] " +
                    $"rngCalls {nativeRngCalls}/{spoilsPrediction.RngCalls}");
        }
        var typeMismatches = spoilsPrediction.Points.Where(p =>
            sourcePoints[p.Position].PointType.ToString() != p.Type.ToString()).ToArray();
        if (typeMismatches.Length > 0)
            throw new InvalidOperationException("MAP_RUNTIME Spoils type mismatches " +
                string.Join(";", typeMismatches.Select(p =>
                    $"{p.Position}:{sourcePoints[p.Position].PointType}/{p.Type}")) +
                $" rngCalls {nativeRngCalls}/{spoilsPrediction.RngCalls}");
        Check(nativeRngCalls == spoilsPrediction.RngCalls, "native Spoils map RNG calls");
        var lanternState = PredictorObtainSources.AddCard(context, baseline.Working,
            new ModelKey("CARD", "LANTERN_KEY"));
        var lantern = new PredictorRun(context, lanternState);
        Check(lantern.UnknownRoomMustBeEvent(3) && lantern.LanternKeyReplacesEvent(3) &&
            !lantern.UnknownRoomMustBeEvent(2) && !lantern.LanternKeyReplacesEvent(2),
            "LanternKey Act 3 only unknown/event override");
        var spoilState = PredictorObtainSources.AddCard(context, baseline.Working,
            new ModelKey("CARD", "SPOILS_MAP"));
        long spoilId = spoilState.Deck[^1];
        var spoilPosition = new MapPosition(3, 9);
        spoilState = spoilState.WithCard(spoilState.Card(spoilId) with
            { SpoilsActIndex = 1, SpoilsCoord = spoilPosition }) with
            { Position = new(2, 9, "map:2:9:3") };
        var spoil = new PredictorRun(context, spoilState);
        int priorGold = spoil.Working.GoldBudget;
        Check(spoil.CompleteSpoilsMapAtTreasure(spoilPosition) == 600 &&
            spoil.Working.GoldBudget == priorGold + 600 &&
            !spoil.Working.Deck.Contains(spoilId) && spoil.Working.Card(spoilId).Removed,
            "Spoils quest gold then card removal at attached treasure");
        var bigGame = new PredictorRun(context with
            { Modifiers = [new(new ModelKey("MODIFIER", "BIG_GAME_HUNTER"))] }, spoilState);
        var bigMap = bigGame.GenerateActualMap(2);
        var nativeBigPoints = nativeBigGameSpoils.GetAllMapPoints().Append(nativeBigGameSpoils.StartingMapPoint)
            .Append(nativeBigGameSpoils.BossMapPoint).ToDictionary(p => new MapPosition(p.coord.col, p.coord.row));
        Check(nativeBigPoints.Count == bigMap.Points.Count && bigMap.Points.All(p =>
            nativeBigPoints.TryGetValue(p.Position, out var sourcePoint) &&
            sourcePoint.PointType.ToString() == p.Type.ToString() &&
            sourcePoint.Children.Select(c => new MapPosition(c.coord.col, c.coord.row)).OrderBy(c => c.Column)
                .ThenBy(c => c.Row).SequenceEqual(p.Children)), "native BigGameHunter on Spoils map");

        var furRelic = (FurCoat)ModelDb.Relic<FurCoat>().ToMutable();
        var furState = spoilState with
        {
            Relics = spoilState.Relics.Add(new(spoilState.NextInstanceId,
                new("RELIC", "FUR_COAT"), spoilState.Position.Floor)),
            NextInstanceId = spoilState.NextInstanceId + 1,
            Maps = [new(2, spoilsPrediction, [])]
        };
        var fur = new PredictorRun(context, furState);
        try
        {
            stateProperty.SetValue(RunManager.Instance, native);
            ascensionProperty.SetValue(RunManager.Instance, new AscensionManager(context.Ascension));
            native.Map = nativeSpoils;
            nativePlayer.AddRelicInternal(furRelic);
            furRelic.AfterObtained().GetAwaiter().GetResult();
        }
        finally
        {
            stateProperty.SetValue(RunManager.Instance, oldState);
            ascensionProperty.SetValue(RunManager.Instance, oldAscension);
        }
        long furId = furState.Relics[^1].Id;
        fur.ApplyFurCoatObtained(furId);
        var expectedFur = furRelic.GetMarkedCoords()!.Select(c => new MapPosition(c.col, c.row)).ToArray();
        Check(fur.Working.Relics[^1].FurCoatQuestCoords.SequenceEqual(expectedFur) &&
            fur.Working.Relics[^1].FurCoatCoordsSet, "native FurCoat marked coord order");

        var start = new MapPosition(3, 0);
        var a = new MapPosition(2, 1);
        var b = new MapPosition(3, 1);
        var c = new MapPosition(2, 2);
        var d = new MapPosition(3, 2);
        var e = new MapPosition(2, 3);
        var f = new MapPosition(3, 3);
        var boss = new MapPosition(3, 4);
        MapPredictionPoint[] nodes =
        [
            new(start, MapPointType.Ancient, true, [a, b]),
            new(a, MapPointType.Monster, true, [c]), new(b, MapPointType.Monster, true, [d]),
            new(c, MapPointType.Monster, true, [e]), new(d, MapPointType.Monster, true, [f]),
            new(e, MapPointType.Monster, true, [boss]), new(f, MapPointType.Monster, true, [boss]),
            new(boss, MapPointType.Boss, true, [])
        ];
        var travelMap = new MapPrediction(context.Seed, state.Acts[0].Key, 0, 7, nodes, 0);
        var boots = state with { Relics = state.Relics.Add(new(state.NextInstanceId,
            new("RELIC", "WINGED_BOOTS"), 1)), NextInstanceId = state.NextInstanceId + 1 };
        var run = new PredictorRun(context, boots);
        Check(run.IsLegalRoute(travelMap, [start, a, d, e, boss], []) &&
            run.CanTravel(travelMap, a, d), "WingedBoots legal nonedge row travel");
        run.ApplyTravelAfterRoomEntered(travelMap, a, d);
        Check(run.Working.Relics.Single(r => r.Key.Entry == "WINGED_BOOTS").TimesUsed == 1,
            "WingedBoots spends only after nonedge room entry");
        run.ApplyTravelAfterRoomEntered(travelMap, d, f);
        Check(run.Working.Relics.Single(r => r.Key.Entry == "WINGED_BOOTS").TimesUsed == 1,
            "WingedBoots does not spend on ordinary edge");
        var exhausted = run.Working with { Relics = run.Working.Relics.Select(r => r.Key.Entry == "WINGED_BOOTS"
            ? r with { TimesUsed = 3 } : r).ToImmutableArray() };
        var noFlight = new PredictorRun(context, exhausted);
        Check(!noFlight.CanTravel(travelMap, a, d), "used-up Boots deny free travel");
        var oneUse = run.Working with { Relics = run.Working.Relics.Select(r => r.Key.Entry == "WINGED_BOOTS"
            ? r with { TimesUsed = 2 } : r).ToImmutableArray() };
        Check(!new PredictorRun(context, oneUse).IsLegalRoute(travelMap, [start, a, d, e, boss], []),
            "whole route cannot overspend remaining Boots uses");
        var flightContext = context with { Modifiers = [new(new ModelKey("MODIFIER", "FLIGHT"))] };
        var flight = new PredictorRun(flightContext, exhausted);
        Check(flight.CanTravel(travelMap, a, d), "Flight permits travel when Boots exhausted");
        CheckSharedProjectionNativeParity(baseline);
        Console.WriteLine("RUNTIME_MAP GoldenPath fixed geometry; WingedBoots travel/spend; Flight override PASS");
    }

    private static void CheckSharedProjectionNativeParity(PredictorRun baseline)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var manager = RunManager.Instance;
        var stateProperty = typeof(RunManager).GetProperty("State", fields)!;
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", fields)!;
        object? oldState = stateProperty.GetValue(manager), oldAscension = ascensionProperty.GetValue(manager);
        int checks = 0;
        try
        {
            // Native constructors/hooks run only on newly created test fixtures.
            // Production capture/projection is then observed without invoking hooks.
            foreach (string seed in new[] { baseline.Context.Seed, "000000000000" }.Distinct())
            foreach (int ascension in new[] { 0, baseline.Context.Ascension }.Distinct())
            foreach (int playersCount in new[] { 1, 2 })
            foreach (var variant in new[]
            {
                (Name: "Standard", Golden: false, Spoils: false, BigGame: false),
                (Name: "Golden", Golden: true, Spoils: false, BigGame: false),
                (Name: "Golden+BigGame", Golden: true, Spoils: false, BigGame: true),
                (Name: "Spoils", Golden: false, Spoils: true, BigGame: false),
                (Name: "Spoils+BigGame", Golden: false, Spoils: true, BigGame: true),
                (Name: "Standard+BigGame", Golden: false, Spoils: false, BigGame: true)
            })
            {
                var players = Enumerable.Range(1, playersCount)
                    .Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i)).ToArray();
                var bigGame = variant.BigGame ? ModelDb.Modifier<BigGameHunter>().ToMutable() : null;
                var native = RunState.CreateForTest(players,
                    baseline.Working.Acts.Select(a => ModelDb.GetById<ActModel>(
                        new MegaCrit.Sts2.Core.Models.ModelId(a.Key.Category, a.Key.Entry))).ToArray(),
                    modifiers: bigGame == null ? [] : [bigGame], seed: seed, ascensionLevel: ascension);
                native.CurrentActIndex = 1;
                // Put authored content on the second player in multiplayer, so
                // UI capture must inspect the whole party rather than player zero.
                var owner = players[^1];
                GoldenCompass? compass = null;
                SpoilsMap? spoils = null;
                if (variant.Golden)
                {
                    compass = (GoldenCompass)ModelDb.Relic<GoldenCompass>().ToMutable();
                    compass.GoldenPathAct = 1;
                    owner.AddRelicInternal(compass, silent: true);
                }
                if (variant.Spoils)
                {
                    spoils = native.CreateCard<SpoilsMap>(owner);
                    spoils.SpoilsActIndex = 1;
                    owner.Deck.AddInternal(spoils, silent: true);
                }
                stateProperty.SetValue(manager, native);
                ascensionProperty.SetValue(manager, new AscensionManager(ascension));
                var standard = native.Act.CreateMap(native, false);
                ActMap oracle = standard;
                if (compass != null) oracle = compass.ModifyGeneratedMap(native, oracle, 1);
                if (spoils != null) oracle = spoils.ModifyGeneratedMap(native, oracle, 1);
                if (bigGame != null) oracle = bigGame.ModifyGeneratedMap(native, oracle, 1);
                Check(variant.Golden ? oracle is GoldenPathActMap : variant.Spoils ? oracle is SpoilsActMap : oracle is StandardActMap,
                    $"native fixture activated {variant.Name}");
                native.Map = oracle;
                native.AddVisitedMapCoord(oracle.StartingMapPoint.coord);
                string label = $"{variant.Name}/{seed}/A{ascension}/players{playersCount}";
                string before = NativeObservationState(native);
                string graph = NativeGeometry(oracle);
                // GoldenPath is deterministic and has no map RNG of its own;
                // its provider counter describes the original Standard generation.
                int nativeCalls = MapRngCalls(variant.Golden ? standard : oracle);
                var key = new ModelKey(native.Act.Id.Category, native.Act.Id.Entry);
                var projection = PredictorMapProjection.Generate(seed, key, 1, ascension, playersCount,
                    native.Act.HasSecondBoss, variant.Golden, variant.Spoils, variant.BigGame);
                Check(PredictedGeometry(projection) == graph, "shared provider native full graph " + label);
                Check(projection.RngCalls == nativeCalls,
                    $"shared provider map RNG {label}: native={nativeCalls} predicted={projection.RngCalls}");
                var actualCapture = InRunMapSnapshot.Actual(native);
                var projectedCapture = InRunMapSnapshot.Projected(native, 1);
                var repeatedCapture = InRunMapSnapshot.Projected(native, 1);
                Check(PredictedGeometry(actualCapture) == graph && PredictedGeometry(projectedCapture) == graph &&
                    PredictedGeometry(repeatedCapture) == graph && projectedCapture.RngCalls == nativeCalls &&
                    repeatedCapture.RngCalls == nativeCalls, "UI actual/projected capture geometry and repeatability " + label);
                var draft = new PredictorMapRouteDraft(actualCapture,
                    InRunMapSnapshot.Position(native.CurrentMapCoord!.Value), InRunMapSnapshot.Travel(native));
                var route = new MapRouteSet(projection).At(0).Skip(1).ToArray();
                Check(draft.ApplyStroke(Enumerable.Reverse(route), PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed &&
                    draft.ReadyPrefix.SequenceEqual(route), "captured draft accepts reverse gesture without source mutation " + label);
                draft.Clear(); draft.Undo();
                Check(ReferenceEquals(native.Map, oracle) && NativeObservationState(native) == before &&
                    NativeGeometry(oracle) == graph && MapRngCalls(variant.Golden ? standard : oracle) == nativeCalls &&
                    ReferenceEquals(stateProperty.GetValue(manager), native), "provider/capture/draft preserve native fixture facts and RNG " + label);
                checks++;
            }
        }
        finally
        {
            stateProperty.SetValue(manager, oldState);
            ascensionProperty.SetValue(manager, oldAscension);
        }
        Check(ReferenceEquals(stateProperty.GetValue(manager), oldState) &&
            ReferenceEquals(ascensionProperty.GetValue(manager), oldAscension), "native parity restores singleton test context");
        Console.WriteLine($"RUNTIME_MAP_PROJECTION_NATIVE_PASS cases={checks}; solo/multiplayer Standard/Golden/Spoils/BigGame full geometry, flags, edges and map RNG; UI capture/provider/draft preserve native test facts and RNG");
    }

    private static int MapRngCalls(ActMap map) => ((MegaCrit.Sts2.Core.Random.Rng)map.GetType()
        .GetField("_rng", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(map)!).ToSerializable().counter;

    private static IEnumerable<MegaCrit.Sts2.Core.Map.MapPoint> NativePoints(ActMap map)
    {
        var points = map.GetAllMapPoints().Append(map.StartingMapPoint).Append(map.BossMapPoint);
        return (map.SecondBossMapPoint is { } second ? points.Append(second) : points).DistinctBy(p => p.coord);
    }

    private static string NativeGeometry(ActMap map) => string.Join("\n", NativePoints(map)
        .OrderBy(p => p.coord.row).ThenBy(p => p.coord.col).Select(p =>
            $"{p.coord.row},{p.coord.col}:{(int)p.PointType}:{p.CanBeModified}->" + string.Join(";",
                p.Children.OrderBy(c => c.coord.row).ThenBy(c => c.coord.col).Select(c => $"{c.coord.row},{c.coord.col}"))));

    private static string PredictedGeometry(MapPrediction map) => string.Join("\n", map.Points
        .OrderBy(p => p.Position.Row).ThenBy(p => p.Position.Column).Select(p =>
            $"{p.Position.Row},{p.Position.Column}:{(int)p.Type}:{p.CanBeModified}->" + string.Join(";",
                p.Children.OrderBy(c => c.Row).ThenBy(c => c.Column).Select(c => $"{c.Row},{c.Column}"))));

    private static string NativeObservationState(RunState native) => JsonSerializer.Serialize(new
    {
        native.CurrentActIndex, native.AscensionLevel, native.CurrentMapCoord,
        Visited = native.VisitedMapCoords, Rng = native.Rng.ToSerializable(),
        Players = native.Players.Select(p => p.ToSerializable()).ToArray(),
        Extra = native.ExtraFields.ToSerializable()
    }, new JsonSerializerOptions { IncludeFields = true });
}
