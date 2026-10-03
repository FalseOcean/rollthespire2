using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.World;

internal enum UnknownRoomCategory { Event, Monster, Treasure, Shop }
internal readonly record struct UnknownOddsSnapshot(float Monster, float Elite, float Treasure, float Shop);
internal sealed record UnknownRoomObservation(int Ordinal, MapPosition? Position, UnknownRoomCategory Room,
    bool ShopBlacklisted, float Roll, UnknownOddsSnapshot Before, UnknownOddsSnapshot After, int RngCounter);
internal sealed record UnknownRoomSequence(bool RouteBound, int PriorDraws, IReadOnlyList<UnknownRoomObservation> Steps)
{
    public const string Contract = "Beta111/SP/non-tutorial/fresh-act-odds/no-special-hooks/no-event-side-effects/no-saved-continuation";
}

/// <summary>
/// Room-entry truth, separate from map generation. Mirrors Beta111 UnknownMapPointOdds.Roll
/// under the explicit ordinary contract. Act entry resets odds, NOT the run-global RNG.
/// </summary>
internal static class OrdinaryUnknownRoomPredictor
{
    internal static UnknownRoomSequence Baseline(string seed, int priorDraws, int count)
    {
        if (count is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(count));
        var replay = new Replay(seed, priorDraws);
        return new(false, priorDraws, Array.AsReadOnly(Enumerable.Range(0, count).Select(i => replay.Roll(i + 1, null, false)).ToArray()));
    }

    internal static int PriorDraws(MapRouteCommitment route, IReadOnlyList<MapRouteCommitment> priorActs)
    {
        if (route.Map.ActIndex is < 0 or > 2 || priorActs.Count != route.Map.ActIndex)
            throw new ArgumentException("Prior Act commitments are required for the run-global Unknown RNG.");
        int count = 0;
        for (int i = 0; i < priorActs.Count; i++)
        {
            var prior = priorActs[i];
            if (prior.Map.Seed != route.Map.Seed || prior.Map.ActIndex != i)
                throw new ArgumentException("Prior route belongs to a different seed or Act.");
            var nodes = prior.Map.Points.ToDictionary(p => p.Position);
            count += prior.Coordinates.Count(p => nodes[p].Type == MapPointType.Unknown);
        }
        return count;
    }

    internal static UnknownRoomSequence ForRoute(MapRouteCommitment route, IReadOnlyList<MapRouteCommitment> priorActs)
    {
        int priorDraws = PriorDraws(route, priorActs);
        var replay = new Replay(route.Map.Seed, priorDraws);
        var nodes = route.Map.Points.ToDictionary(p => p.Position);
        var result = new List<UnknownRoomObservation>();
        bool previousRoomWasShop = false;
        foreach (var coord in route.Coordinates)
        {
            var node = nodes[coord];
            if (node.Type == MapPointType.Unknown)
            {
                // EnterMapCoord adds the NEW coord before EnterMapPointInternal calls this rule.
                // Thus Children belongs to the entered node; previous room comes from history.
                bool blocked = previousRoomWasShop || (node.Children.Count > 0 && node.Children.All(p => nodes[p].Type == MapPointType.Shop));
                var observation = replay.Roll(result.Count + 1, coord, blocked);
                result.Add(observation);
                previousRoomWasShop = observation.Room == UnknownRoomCategory.Shop;
            }
            else previousRoomWasShop = node.Type == MapPointType.Shop;
        }
        return new(true, priorDraws, Array.AsReadOnly(result.ToArray()));
    }

    private sealed class Replay
    {
        private readonly Xoshiro256StarStar _rng;
        private UnknownOddsSnapshot _odds = new(.1f, -1f, .02f, .03f);
        internal Replay(string seed, int priorDraws)
        {
            if (priorDraws < 0) throw new ArgumentOutOfRangeException(nameof(priorDraws));
            var profile = Beta111Profile.Instance;
            if (!profile.TryCanonicalizeSeed(seed, out string canonical, out string issue)) throw new ArgumentException(issue);
            _rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(profile.ComputeRootSeed(canonical), "unknown_map_point"));
            _rng.Advance(priorDraws);
        }
        internal UnknownRoomObservation Roll(int ordinal, MapPosition? position, bool shopBlocked)
        {
            var before = _odds;
            float roll = _rng.NextFloat();
            float total = before.Monster;
            UnknownRoomCategory room;
            // Preserve float accumulation, inclusive thresholds and dictionary insertion order.
            if (roll <= total) room = UnknownRoomCategory.Monster;
            else if (roll <= (total += before.Treasure)) room = UnknownRoomCategory.Treasure;
            else if (!shopBlocked && roll <= total + before.Shop) room = UnknownRoomCategory.Shop;
            else room = UnknownRoomCategory.Event;
            _odds = new(
                room == UnknownRoomCategory.Monster ? .1f : before.Monster + .1f,
                before.Elite - 1f, // Negative Elite never rolls; vanilla still adds its negative base each ordinary roll.
                room == UnknownRoomCategory.Treasure ? .02f : before.Treasure + .02f,
                room == UnknownRoomCategory.Shop ? .03f : shopBlocked ? before.Shop : before.Shop + .03f);
            return new(ordinal, position, room, shopBlocked, roll, before, _odds, _rng.CallCount);
        }
    }
}
