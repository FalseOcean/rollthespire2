using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Rewards;

namespace RolltheSpire2.Core.Effects;

internal sealed record GeneratedCard(NeowEffectCardSnapshot Card, bool Upgraded);

internal static class NeowRewardGenerator
{
    // Root-specific RelicGrabBag replay belongs to prediction, not Search admission.
    internal static IReadOnlyList<NeowEffectRelicSnapshot> BuildOrderedRelicBag(
        IRuntimeProfile profile,
        ulong rootHash,
        IReadOnlyList<NeowEffectRelicSnapshot> shared,
        IReadOnlyList<NeowEffectRelicSnapshot> character)
    {
        var rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(rootHash, "up_front"));

        foreach (IGrouping<string, NeowEffectRelicSnapshot> bucket in GroupInSourceRarityOrder(shared))
        {
            List<NeowEffectRelicSnapshot> consumed = bucket.ToList();
            rng.UnstableShuffle(consumed);
        }

        var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        var playerRelics = new List<NeowEffectRelicSnapshot>();
        foreach (NeowEffectRelicSnapshot relic in shared.Concat(character))
        {
            if (relic.Rarity is not (EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare or EffectRelicRarity.Shop))
            {
                continue;
            }
            if (seen.Add(relic.RelicKey))
            {
                playerRelics.Add(relic);
            }
        }

        var output = new List<NeowEffectRelicSnapshot>(playerRelics.Count);
        foreach (IGrouping<string, NeowEffectRelicSnapshot> bucket in GroupInSourceRarityOrder(playerRelics))
        {
            List<NeowEffectRelicSnapshot> shuffled = bucket.ToList();
            rng.UnstableShuffle(shuffled);
            foreach (NeowEffectRelicSnapshot relic in shuffled)
            {
                output.Add(relic with { BagOrder = output.Count });
            }
        }
        return output;
    }

    private static IEnumerable<IGrouping<string, NeowEffectRelicSnapshot>> GroupInSourceRarityOrder(
        IEnumerable<NeowEffectRelicSnapshot> source) => source.GroupBy(
            relic => string.IsNullOrWhiteSpace(relic.RarityCode)
                ? relic.Rarity.ToString()
                : relic.RarityCode,
            StringComparer.Ordinal);

    public static GeneratedCard? CreateCard(
        Xoshiro256StarStar rng,
        IReadOnlyList<NeowEffectCardSnapshot> source,
        int ascension,
        HashSet<string> excludedInstanceIds,
        EffectCardRarity? forcedRarity,
        bool consumeUpgradeRoll,
        CardBaseOddsPolicy baseOddsPolicy,
        string? poolId = null)
    {
        List<NeowEffectCardSnapshot> pool = source
            .Where(card => card.Rarity is not EffectCardRarity.Basic and not EffectCardRarity.Ancient)
            .Where(card => poolId is null || string.Equals(card.PoolId, poolId, StringComparison.Ordinal))
            .Where(card => !excludedInstanceIds.Contains(card.InstanceId))
            .OrderBy(card => card.PoolOrder)
            .ToList();
        if (pool.Count == 0)
        {
            return null;
        }

        EffectCardRarity rarity = forcedRarity ?? CardBaseOddsPolicyEvaluator.Roll(
            rng,
            CardBaseOddsType.Regular,
            ascension,
            baseOddsPolicy);
        IReadOnlyList<EffectCardRarity> fallback = CardRarityFallback(rarity);
        List<NeowEffectCardSnapshot>? candidates = null;
        foreach (EffectCardRarity candidateRarity in fallback)
        {
            candidates = pool.Where(card => card.Rarity == candidateRarity).ToList();
            if (candidates.Count > 0)
            {
                break;
            }
        }

        if (candidates is null || candidates.Count == 0)
        {
            return null;
        }

        NeowEffectCardSnapshot selected = candidates[rng.NextInt(candidates.Count)];
        excludedInstanceIds.Add(selected.InstanceId);
        bool upgraded = false;
        if (consumeUpgradeRoll)
        {
            // Neow reward generation still consumes the upgrade roll even when
            // its effective upgrade odds are zero. The draw must not be skipped,
            // and the card must not be promoted based on ordinary combat odds.
            _ = rng.NextFloat();
        }

        return new GeneratedCard(selected, upgraded);
    }

    public static NeowEffectPotionSnapshot? CreatePotion(
        Xoshiro256StarStar rng,
        IReadOnlyList<NeowEffectPotionSnapshot> source,
        HashSet<ModelKey> excluded)
    {
        EffectPotionRarity rarity = RollPotionRarity(rng);
        List<NeowEffectPotionSnapshot> candidates = source
            .Where(potion => potion.Rarity == rarity && !excluded.Contains(potion.PotionKey))
            .OrderBy(potion => potion.PoolOrder)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        NeowEffectPotionSnapshot selected = candidates[rng.NextInt(candidates.Count)];
        excluded.Add(selected.PotionKey);
        return selected;
    }

    public static EffectRelicRarity RollRelicRarity(Xoshiro256StarStar rng)
    {
        float roll = rng.NextFloat();
        return roll < 0.5f
            ? EffectRelicRarity.Common
            : roll < 0.83f
                ? EffectRelicRarity.Uncommon
                : EffectRelicRarity.Rare;
    }

    public static NeowEffectRelicSnapshot? PullRelicWithFallback(
        List<NeowEffectRelicSnapshot> bag,
        EffectRelicRarity rarity)
    {
        EffectRelicRarity[] order = rarity switch
        {
            EffectRelicRarity.Common => new[] { EffectRelicRarity.Common, EffectRelicRarity.Uncommon, EffectRelicRarity.Rare },
            EffectRelicRarity.Uncommon => new[] { EffectRelicRarity.Uncommon, EffectRelicRarity.Rare },
            _ => new[] { EffectRelicRarity.Rare }
        };

        foreach (EffectRelicRarity candidate in order)
        {
            int index = bag.FindIndex(item => item.Rarity == candidate);
            if (index >= 0)
            {
                NeowEffectRelicSnapshot result = bag[index];
                bag.RemoveAt(index);
                return result;
            }
        }

        // RelicFactory falls back to Circlet only after exhausting the permitted
        // rarity chain. Circlet is not inserted into or removed from the shuffled bag.
        return new NeowEffectRelicSnapshot(
            RelicKey: BaseGameModelKeys.OrdinaryRelics.Circlet,
            BagOrder: -1,
            Rarity: EffectRelicRarity.Other,
            NestedEffectKind: NestedRelicEffectKind.NoTrackedImmediateEffect,
            NestedClassificationExact: true,
            SourceAssembly: "sts2",
            RarityCode: "Special");
    }

    private static IReadOnlyList<EffectCardRarity> CardRarityFallback(EffectCardRarity rarity) => rarity switch
    {
        EffectCardRarity.Common => new[] { EffectCardRarity.Common, EffectCardRarity.Uncommon, EffectCardRarity.Rare },
        EffectCardRarity.Uncommon => new[] { EffectCardRarity.Uncommon, EffectCardRarity.Rare, EffectCardRarity.Common },
        _ => new[] { EffectCardRarity.Rare, EffectCardRarity.Common, EffectCardRarity.Uncommon }
    };

    private static EffectPotionRarity RollPotionRarity(Xoshiro256StarStar rng)
    {
        float roll = rng.NextFloat();
        return roll <= 0.1f
            ? EffectPotionRarity.Rare
            : roll <= 0.35f
                ? EffectPotionRarity.Uncommon
                : EffectPotionRarity.Common;
    }
}
