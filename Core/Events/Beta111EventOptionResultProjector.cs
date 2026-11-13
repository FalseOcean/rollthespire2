using RolltheSpire2.Compatibility;
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
    public static IReadOnlyList<Beta111EventOptionResult> Project(ulong root, int slot, string eventEntry) =>
        Project(root, slot, eventEntry, RngSemanticProfileId.Modern110);

    public static IReadOnlyList<Beta111EventOptionResult> Project(ulong root, int slot, string eventEntry,
        RngSemanticProfileId semanticProfile)
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
            "DENSE_VEGETATION" => [Gold("TRUDGE_ON", Beta111EventRules.DenseVegetationGold(rng))],
            // Native NextFloat casts the ranged double to float before decimal conversion;
            // IntValue then truncates the entire positive amount, not its signed offset.
            "JUNGLE_MAZE_ADVENTURE" =>
            [
                Gold("SOLO_QUEST", (int)Beta111EventRules.JungleGold(rng, 150m)),
                Gold("JOIN_FORCES", (int)Beta111EventRules.JungleGold(rng, 50m))
            ],
            "LOST_WISP" => [Gold("SEARCH", Beta111EventRules.LostWispGold(rng))],
            "LUMINOUS_CHOIR" => [Gold("OFFER_TRIBUTE", Beta111EventRules.LuminousChoirGold(rng))],
            "CRYSTAL_SPHERE" => [Gold("UNCOVER_FUTURE", Beta111EventRules.CrystalSphereGold(rng))],
            "SUNKEN_TREASURY" =>
            [
                Gold("FIRST_CHEST", Beta111EventRules.SunkenTreasurySmallGold(rng)),
                Gold("SECOND_CHEST", Beta111EventRules.SunkenTreasuryLargeGold(rng))
            ],
            "SUNKEN_STATUE" => [Gold("DIVE_INTO_WATER", Beta111EventRules.SunkenStatueGold(rng))],
            "THIS_OR_THAT" => [Gold("PLAIN", Beta111EventRules.ThisOrThatGold(rng))],
            "WHISPERING_HOLLOW" => [Gold("GOLD", Beta111EventRules.WhisperingHollowGold(rng))],
            "DOLL_ROOM" => Dolls(rng, semanticProfile),
            _ => []
        };
    }

    private static Beta111EventOptionResult Gold(string option, int amount) => new(option, amount, []);

    private static IReadOnlyList<Beta111EventOptionResult> Dolls(Xoshiro256StarStar rng, RngSemanticProfileId semanticProfile) =>
    [
        // Each alternative owns its own copy of the caller's entry checkpoint.
        new("RANDOM", null, [Beta111EventRules.RandomDoll(rng.Clone())]),
        new("TAKE_SOME_TIME", null, Beta111EventRules.DollOffers(rng.Clone(), 2, semanticProfile))
    ];
}
