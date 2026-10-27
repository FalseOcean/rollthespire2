using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    internal MapPrediction GenerateActualMap(int act, CancellationToken token = default, bool applyStartingPoint = true)
    {
        var map = GenerateModifiedMap(act, token);
        return applyStartingPoint ? ApplyStartingPoint(map) : map;
    }

    private MapPrediction ApplyStartingPoint(MapPrediction map) => Working.StartedWithNeow || map.ActIndex != 0 ? map :
        map with { Points = map.Points.Select(p => p.Type == MapPointType.Ancient
            ? p with { Type = MapPointType.Monster } : p).ToArray() };

    private MapPrediction GenerateModifiedMap(int act, CancellationToken token)
    {
        var source = Working.Acts.Single(a => a.Act == act);
        return PredictorMapProjection.Generate(Context.Seed, source.Key, act - 1, Context.Ascension, 1,
            source.Bosses.Length > 1,
            Working.Relics.Any(r => !r.Melted && r.Key.Entry == "GOLDEN_COMPASS" && r.GoldenPathAct == act - 1),
            Working.Deck.Any(id => Working.Card(id).Key.Entry == "SPOILS_MAP" && Working.Card(id).SpoilsActIndex == act - 1),
            Context.HasRunModifier("BIG_GAME_HUNTER"), token);
    }

    internal void ApplyFurCoatObtained(long relicId)
    {
        int index = Working.Relics.FindIndex(r => r.Id == relicId && r.Key.Entry == "FUR_COAT");
        if (index < 0) throw new InvalidOperationException("PredictorFurCoatInstanceMissing");
        int act = Working.Position.Act;
        var actual = Working.Maps.FirstOrDefault(m => m.Act == act)?.Map
            ?? throw new InvalidOperationException("PredictorFurCoatActualMapMissing");
        Working = Working with { Relics = Working.Relics.SetItem(index,
            Working.Relics[index] with { FurCoatAct = act - 1 }) };
        MarkFurCoatRooms(index, actual, act, freshMap: false);
    }

    internal void ApplyFurCoatAfterActualMapGenerated(int act, MapPrediction map)
    {
        for (int i = 0; i < Working.Relics.Length; i++)
            if (!Working.Relics[i].Melted && Working.Relics[i].Key.Entry == "FUR_COAT" &&
                Working.Relics[i].FurCoatAct == act - 1)
                MarkFurCoatRooms(i, map, act, freshMap: true);
    }

    private void MarkFurCoatRooms(int index, MapPrediction map, int act, bool freshMap)
    {
        var relic = Working.Relics[index];
        if (relic.FurCoatAct != act - 1) return;
        bool valid = relic.FurCoatCoordsSet && relic.FurCoatQuestCoords.All(coord =>
            map.Points.Any(p => p.Position == coord && p.Type is MapPointType.Monster or MapPointType.Elite));
        if (valid) return;
        ulong rootSeed = Beta111Profile.Instance.ComputeRootSeed(Context.Seed);
        ulong hash = XxHash64.HashUtf8("FUR_COAT", 0UL);
        var rng = new Xoshiro256StarStar(unchecked(rootSeed + hash));
        var occupied = Working.Relics.Where((other, otherIndex) => otherIndex != index &&
            other.FurCoatAct == act - 1 && other.FurCoatCoordsSet && (freshMap ? !other.Melted : true))
            .SelectMany(other => other.FurCoatQuestCoords).ToHashSet();
        var candidates = map.Points.Where(p => p.Type is MapPointType.Monster or MapPointType.Elite &&
                !occupied.Contains(p.Position))
            .OrderBy(p => p.Position.Column).ThenBy(p => p.Position.Row)
            .Select(p => p.Position).ToList();
        rng.UnstableShuffle(candidates);
        Working = Working with { Relics = Working.Relics.SetItem(index, relic with
        { FurCoatCoordsSet = true, FurCoatQuestCoords = candidates.Take(8).ToImmutableArray() }) };
    }

    internal bool IsFurCoatMarkedCombat(int act, MapPosition point) => Working.Relics.Any(r =>
        !r.Melted && r.Key.Entry == "FUR_COAT" && r.FurCoatAct == act - 1 &&
        r.FurCoatCoordsSet && r.FurCoatQuestCoords.Contains(point));

    internal void ApplySpoilsMapAfterMapGenerated(int act, MapPrediction map)
    {
        // ModifyGeneratedMapLate records the first treasure in map enumeration;
        // AfterMapGenerated attaches the quest to that coordinate.
        MapPosition? treasure = map.Points.Where(p => p.Type == MapPointType.Treasure)
            .OrderBy(p => p.Position.Column).ThenBy(p => p.Position.Row)
            .Select(p => (MapPosition?)p.Position).FirstOrDefault();
        foreach (long id in Working.Deck)
        {
            var card = Working.Card(id);
            if (card.Key.Entry == "SPOILS_MAP" && card.SpoilsActIndex == act - 1)
                Working = Working.WithCard(card with { SpoilsCoord = treasure });
        }
    }

    internal void ApplySpoilsMapBeforeRemoved(long cardId)
    {
        var card = Working.Card(cardId);
        if (card.Key.Entry != "SPOILS_MAP" || card.SpoilsActIndex != Working.Position.Act - 1 ||
            card.SpoilsCoord == null) return;
        Working = Working.WithCard(card with { SpoilsCoord = null });
    }

    internal int CompleteSpoilsMapAtTreasure(MapPosition position)
    {
        bool quest = Working.Deck.Any(id => Working.Card(id).Key.Entry == "SPOILS_MAP" &&
            Working.Card(id).SpoilsCoord == position);
        if (!quest) return 0;
        var ids = Working.Deck.Where(id => Working.Card(id).Key.Entry == "SPOILS_MAP").ToImmutableArray();
        int amount = checked(600 * ids.Length);
        foreach (long id in ids)
        {
            Working = PredictorSettlementEffects.TakeGold(Working, 600);
            ApplySpoilsMapBeforeRemoved(id);
            RemoveCards([id]);
        }
        Accept(new("SpoilsMapQuestComplete", 0, ids, null, Working.GoldBudget));
        return amount;
    }

    internal bool CanTravel(MapPrediction map, MapPosition? previous, MapPosition target)
    {
        if (previous is not { } from) return map.Points.Any(p => p.Position == target && p.Position.Row == 0);
        var prior = map.Points.Single(p => p.Position == from);
        if (prior.Children.Contains(target)) return true;
        bool free = Working.Relics.Any(r => !r.Melted && r.Key.Entry == "WINGED_BOOTS" && r.TimesUsed < 3) ||
            Context.HasRunModifier("FLIGHT");
        return free && target.Row == from.Row + 1 && map.Points.Any(p => p.Position == target);
    }

    internal bool IsLegalRoute(MapPrediction map, IReadOnlyList<MapPosition> points,
        IReadOnlyList<MapPosition> alreadyVisitedPrefix, bool requireBoss = true)
    {
        if (points.Count < (requireBoss ? 2 : 1) || map.Points.Count(p => p.Position.Row == 0) != 1 ||
            points[0] != map.Points.Single(p => p.Position.Row == 0).Position ||
            requireBoss && !map.Points.Any(p => p.Position == points[^1] && p.Type == MapPointType.Boss && p.Children.Count == 0) ||
            points.Count < alreadyVisitedPrefix.Count) return false;
        // A generated replacement map can retain only some visited coordinates.
        // Those coordinates remain fixed; missing historical rows are not replayed.
        int lastVisitedIndex = -1;
        foreach (var visited in alreadyVisitedPrefix)
        {
            int index = -1;
            for (int p = lastVisitedIndex + 1; p < points.Count; p++)
                if (points[p] == visited) { index = p; break; }
            if (index < 0) return false;
            lastVisitedIndex = index;
        }
        int usesLeft = Working.Relics.Where(r => !r.Melted && r.Key.Entry == "WINGED_BOOTS")
            .Select(r => Math.Max(0, 3 - r.TimesUsed)).DefaultIfEmpty(0).Max();
        bool flight = Context.HasRunModifier("FLIGHT");
        for (int i = Math.Max(1, lastVisitedIndex + 1); i < points.Count; i++)
        {
            var previous = map.Points.FirstOrDefault(p => p.Position == points[i - 1]);
            var target = map.Points.FirstOrDefault(p => p.Position == points[i]);
            if (previous == null || target == null || target.Position.Row != previous.Position.Row + 1) return false;
            if (previous.Children.Contains(target.Position)) continue;
            if (flight) continue;
            if (usesLeft-- <= 0) return false;
        }
        return true;
    }

    internal void ApplyTravelAfterRoomEntered(MapPrediction map, MapPosition? previous, MapPosition target)
    {
        if (previous is not { } from || map.Points.Single(p => p.Position == from).Children.Contains(target)) return;
        // WingedBoots counts any non-edge entry, including one enabled by Flight.
        // There is no priority-based reservation of the free-travel source.
        Working = Working with { Relics = Working.Relics.Select(r => !r.Melted &&
            r.Key.Entry == "WINGED_BOOTS" && r.TimesUsed < 3 ? r with { TimesUsed = r.TimesUsed + 1 } : r)
            .ToImmutableArray() };
    }

    internal void ApplyGoldenCompassObtained(long relicId)
    {
        int index = Working.Relics.FindIndex(r => r.Id == relicId && r.Key.Entry == "GOLDEN_COMPASS");
        if (index < 0) throw new InvalidOperationException("PredictorGoldenCompassInstanceMissing");
        var relic = Working.Relics[index] with { GoldenPathAct = Working.Position.Act - 1 };
        Working = Working with { Relics = Working.Relics.SetItem(index, relic) };
        int mapIndex = Working.Maps.FindIndex(m => m.Act == Working.Position.Act);
        if (mapIndex < 0) return;
        var generated = GenerateActualMap(Working.Position.Act, applyStartingPoint: false);
        ApplyFurCoatAfterActualMapGenerated(Working.Position.Act, generated);
        generated = ApplyStartingPoint(generated);
        var visited = Working.Maps[mapIndex].Visited.Where(p => generated.Points.Any(n => n.Position == p)).ToImmutableArray();
        Working = Working with { Maps = Working.Maps.SetItem(mapIndex, Working.Maps[mapIndex] with
            { Map = generated, Visited = visited }) };
        DiscardRouteForMapChange(Working.Position.Act);
    }

    internal bool UnknownRoomMustBeEvent(int act) =>
        Working.Relics.Any(r => !r.Melted && r.Key.Entry == "GOLDEN_COMPASS" && r.GoldenPathAct == act - 1) ||
        act == 3 && Working.Deck.Any(id => Working.Card(id).Key.Entry == "LANTERN_KEY");

    internal bool LanternKeyReplacesEvent(int act) =>
        act == 3 && Working.Deck.Any(id => Working.Card(id).Key.Entry == "LANTERN_KEY");
}
