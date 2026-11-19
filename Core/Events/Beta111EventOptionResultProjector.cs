using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

public sealed record Beta111EventOptionResult(
    string OptionKey,
    int? GoldAmount,
    IReadOnlyList<ModelKey> Relics)
{
    public EventRelicRarityBranch? RarityBranch { get; init; }
    public IReadOnlyList<EffectRelicRarity> RelicRarityRolls { get; init; } = [];
}

/// <summary>
/// Seed-local base amounts, relic alternatives and rarity rolls, conditional on
/// normal eligible entry and choosing the option. Does not simulate settlement hooks,
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
        var stable = ProjectBaseOptions(root, slot, eventEntry, semanticProfile);
        if (semanticProfile != RngSemanticProfileId.Modern113) return stable;

        List<Beta111EventOptionResult>? options = null;
        foreach (var opportunity in EventOpportunityCatalog.RarityBranches)
        {
            if (opportunity.EventKey.Entry != eventEntry) continue;
            var rolls = new EffectRelicRarity[opportunity.RollCount];
            if (!EventRelicRarityProjector.Project(root, slot, opportunity, rolls)) continue;

            options ??= stable.ToList();
            var branch = opportunity.Branch;
            // Offer Tribute already shows the same option's seed-local gold cost.
            if (branch == EventRelicRarityBranch.OfferTribute)
                options[0] = options[0] with { RarityBranch = branch, RelicRarityRolls = rolls };
            else
                options.Add(new(branch.ToString(), null, []) { RarityBranch = branch, RelicRarityRolls = rolls });
        }
        return options is null ? stable : options;
    }

    private static IReadOnlyList<Beta111EventOptionResult> ProjectBaseOptions(ulong root, int slot, string eventEntry,
        RngSemanticProfileId semanticProfile)
    {
        if (eventEntry is not ("DENSE_VEGETATION" or "JUNGLE_MAZE_ADVENTURE" or "LOST_WISP" or
            "LUMINOUS_CHOIR" or "CRYSTAL_SPHERE" or "SUNKEN_TREASURY" or "SUNKEN_STATUE" or
            "THIS_OR_THAT" or "WHISPERING_HOLLOW" or EventOpportunityCatalog.DollRoom or
            EventOpportunityCatalog.MuseumOfTorture or EventOpportunityCatalog.ThermaeNonupeipe))
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
            EventOpportunityCatalog.DollRoom => Dolls(rng, semanticProfile),
            EventOpportunityCatalog.MuseumOfTorture when semanticProfile == RngSemanticProfileId.Modern113 =>
                [new("TAKE", null, [Beta111EventRules.MuseumOfferedRelic(rng)])],
            EventOpportunityCatalog.ThermaeNonupeipe when semanticProfile == RngSemanticProfileId.Modern113 =>
                [new("DRINK_WATERS", null, [Beta111EventRules.ThermaeDrinkRelic(rng)])],
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
