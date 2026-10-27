using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

public sealed record Beta111EventOptionResult(
    string OptionKey,
    int? GoldAmount,
    IReadOnlyList<ModelKey> Relics);

/// <summary>
/// Seed-local base amounts and Doll Room alternatives, conditional on entering the
/// event and choosing the option. Does not simulate eligibility, settlement hooks,
/// future rewards, or the player's deck. No run state or continuation is consumed.
/// </summary>
public static class Beta111EventOptionResultProjector
{
    public static IReadOnlyList<Beta111EventOptionResult> Project(ulong root, int slot, string eventEntry)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        if (eventEntry is not ("DENSE_VEGETATION" or "JUNGLE_MAZE_ADVENTURE" or "LOST_WISP" or
            "LUMINOUS_CHOIR" or "CRYSTAL_SPHERE" or "SUNKEN_TREASURY" or "SUNKEN_STATUE" or
            "THIS_OR_THAT" or "WHISPERING_HOLLOW" or "DOLL_ROOM"))
            return [];

        var rng = new Xoshiro256StarStar(Beta109WorldRng.DeriveEventLocalSeed(root, slot,
            eventEntry is "DENSE_VEGETATION" or "JUNGLE_MAZE_ADVENTURE", eventEntry));
        return eventEntry switch
        {
            "DENSE_VEGETATION" => [Gold("TRUDGE_ON", 61 + rng.NextInt(39))],
            // Native NextFloat casts the ranged double to float before decimal conversion;
            // IntValue then truncates the entire positive amount, not its signed offset.
            "JUNGLE_MAZE_ADVENTURE" =>
            [
                Gold("SOLO_QUEST", JungleGold(rng, 150m)),
                Gold("JOIN_FORCES", JungleGold(rng, 50m))
            ],
            "LOST_WISP" => [Gold("SEARCH", 60 + rng.NextInt(31) - 15)],
            "LUMINOUS_CHOIR" => [Gold("OFFER_TRIBUTE", 149 - rng.NextInt(50))],
            "CRYSTAL_SPHERE" => [Gold("UNCOVER_FUTURE", 51 + rng.NextInt(49))],
            "SUNKEN_TREASURY" =>
            [
                Gold("FIRST_CHEST", 60 + rng.NextInt(16) - 8),
                Gold("SECOND_CHEST", 333 + rng.NextInt(61) - 30)
            ],
            "SUNKEN_STATUE" => [Gold("DIVE_INTO_WATER", 111 + rng.NextInt(21) - 10)],
            "THIS_OR_THAT" => [Gold("PLAIN", 41 + rng.NextInt(28))],
            "WHISPERING_HOLLOW" => [Gold("GOLD", 35 + rng.NextInt(19) - 9)],
            "DOLL_ROOM" => Dolls(rng),
            _ => []
        };
    }

    private static Beta111EventOptionResult Gold(string option, int amount) => new(option, amount, []);

    private static int JungleGold(Xoshiro256StarStar rng, decimal baseAmount) =>
        (int)(baseAmount + (decimal)(float)(rng.NextDouble() * 30d - 15d));

    private static IReadOnlyList<Beta111EventOptionResult> Dolls(Xoshiro256StarStar rng)
    {
        // ChooseRandom uses the authored array. TakeSomeTime starts independently at
        // event entry and StableShuffle first sorts by RelicModel.CompareTo/ModelId.
        ModelKey[] authored =
        [
            new("RELIC", "DAUGHTER_OF_THE_WIND"),
            new("RELIC", "MR_STRUGGLES"),
            new("RELIC", "BING_BONG")
        ];
        var randomRng = rng.Clone();
        ModelKey random = authored[randomRng.NextInt(authored.Length)];
        ModelKey[] sorted = [authored[2], authored[0], authored[1]];
        rng.UnstableShuffle(sorted);
        return
        [
            new("RANDOM", null, [random]),
            new("TAKE_SOME_TIME", null, [sorted[0], sorted[1]])
        ];
    }
}
