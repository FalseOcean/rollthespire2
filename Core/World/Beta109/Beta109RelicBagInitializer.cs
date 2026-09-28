using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.World.Beta109;

/// <summary>
/// Pure donor-semantic projection of RelicGrabBag.Populate.
/// It preserves source multiplicity and first-seen rarity order. The shared bag
/// accepts every captured rarity; the player bag filters to normal reward and
/// Shop rarities before grouping. The caller owns RNG replay of each bucket.
/// </summary>
public static class Beta109RelicBagInitializer
{
    public static IReadOnlyList<Beta109RelicBucketSnapshot> BuildBuckets(
        IEnumerable<Beta109RelicSourceEntry> source,
        string prefix,
        bool filterPlayerGrabBagRarities)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("RelicBucketPrefixMissing", nameof(prefix));

        var bucketOrder = new List<string>();
        var buckets = new Dictionary<string, List<Beta109RelicSourceEntry>>(StringComparer.Ordinal);
        foreach (Beta109RelicSourceEntry relic in source)
        {
            if (!relic.RelicKey.IsValid || string.IsNullOrWhiteSpace(relic.RarityCode))
            {
                throw new InvalidOperationException("InvalidBeta109RelicSourceEntry");
            }

            if (filterPlayerGrabBagRarities && !IsPlayerGrabBagRarity(relic.RarityCode))
            {
                continue;
            }

            if (!buckets.TryGetValue(relic.RarityCode, out List<Beta109RelicSourceEntry>? items))
            {
                items = new List<Beta109RelicSourceEntry>();
                buckets.Add(relic.RarityCode, items);
                bucketOrder.Add(relic.RarityCode);
            }
            items.Add(relic);
        }

        return bucketOrder
            .Select(rarity => new Beta109RelicBucketSnapshot(
                $"{prefix}:{rarity}",
                buckets[rarity].Select(entry => entry.RelicKey).ToArray(),
                OrderExact: true)
            {
                OrderedEntries = buckets[rarity]
                    .Select(entry => new Beta109RelicBucketEntrySnapshot(
                        entry.RelicKey,
                        entry.IsAllowedInShops,
                        entry.ShopEligibilityExact))
                    .ToArray()
            })
            .ToArray();
    }

    public static bool IsPlayerGrabBagRarity(string rarityCode) =>
        string.Equals(rarityCode, "Common", StringComparison.Ordinal) ||
        string.Equals(rarityCode, "Uncommon", StringComparison.Ordinal) ||
        string.Equals(rarityCode, "Rare", StringComparison.Ordinal) ||
        string.Equals(rarityCode, "Shop", StringComparison.Ordinal);
}

public sealed record Beta109RelicSourceEntry(
    ModelKey RelicKey,
    string RarityCode,
    bool IsAllowedInShops = false,
    bool ShopEligibilityExact = false);
