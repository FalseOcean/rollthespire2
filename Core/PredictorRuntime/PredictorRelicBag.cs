using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorBagEntry(ModelKey Key, string Rarity);
internal sealed record PredictorBagBucket(string Rarity, ImmutableArray<ModelKey> Entries);

// Pure continuation of the actual bag. Eligibility is supplied by the source
// caller on current state; presentation must not commit a speculative pull.
internal sealed record PredictorRelicBag(bool RefreshAllowed, ImmutableArray<PredictorBagEntry> Original,
    ImmutableArray<PredictorBagBucket> Buckets, ImmutableArray<ModelKey> MultiplayerFallback)
{
    public bool HasOriginalCatalog { get; init; } = true;

    internal static PredictorRelicBag Empty(bool refresh) => new(refresh, [], [], []);

    internal static PredictorRelicBag Populate(IEnumerable<PredictorBagEntry> source, bool personal,
        Xoshiro256StarStar rng)
    {
        var entries = source.Where(e => !personal || Beta109RelicBagInitializer.IsPlayerGrabBagRarity(e.Rarity)).ToImmutableArray();
        var donor = Beta109RelicBagInitializer.BuildBuckets(entries.Select(e => new Beta109RelicSourceEntry(e.Key, e.Rarity)),
            personal ? "personal" : "shared", personal);
        var buckets = ImmutableArray.CreateBuilder<PredictorBagBucket>();
        foreach (var bucket in donor)
        {
            var keys = bucket.OrderedEntries!.Select(e => e.RelicKey).ToList();
            rng.UnstableShuffle(keys);
            buckets.Add(new(bucket.BucketId[(bucket.BucketId.LastIndexOf(':') + 1)..], keys.ToImmutableArray()));
        }
        var result = new PredictorRelicBag(!personal, entries, buckets.ToImmutable(), []);
        result.Validate();
        return result;
    }

    internal (ModelKey? Key, PredictorRelicBag Bag) Pull(string rarity, bool fromBack,
        Func<ModelKey, bool> allowed, Func<ModelKey, bool> filter)
    {
        var bag = this with
        {
            Buckets = Buckets.Select(b => b with { Entries = b.Entries.Where(allowed).ToImmutableArray() }).ToImmutableArray(),
            MultiplayerFallback = MultiplayerFallback.Where(allowed).ToImmutableArray()
        };
        int first = bag.Buckets.FindIndex(b => b.Rarity == rarity);
        if ((first < 0 || bag.Buckets[first].Entries.Length == 0) && RefreshAllowed)
        {
            if (!HasOriginalCatalog) throw new InvalidOperationException("Tried to refresh relics but original list is null");
            var refill = new PredictorBagBucket(rarity, Original.Where(e => e.Rarity == rarity).Select(e => e.Key).Where(allowed).ToImmutableArray());
            // A missing GetDeque returns a detached list in vanilla. Refilling
            // creates the real bucket but this invocation still falls through.
            if (first >= 0 || refill.Entries.Length > 0)
                bag = bag with { Buckets = first < 0 ? bag.Buckets.Add(refill) : bag.Buckets.SetItem(first, refill) };
        }
        string current = rarity;
        bool initial = true;
        while (initial || current != "None")
        {
            int index = bag.Buckets.FindIndex(b => b.Rarity == current);
            if (index >= 0 && !(initial && first < 0))
            {
                var bucket = bag.Buckets[index];
                int selected = Select(bucket.Entries, fromBack, filter);
                if (selected >= 0)
                    return (bucket.Entries[selected], bag with { Buckets = bag.Buckets.SetItem(index,
                        bucket with { Entries = bucket.Entries.RemoveAt(selected) }) });
            }
            initial = false;
            current = current switch { "Shop" => "Common", "Common" => "Uncommon", "Uncommon" => "Rare", _ => "None" };
        }
        int fallback = Select(bag.MultiplayerFallback, fromBack, filter);
        return fallback < 0 ? (null, bag) : (bag.MultiplayerFallback[fallback],
            bag with { MultiplayerFallback = bag.MultiplayerFallback.RemoveAt(fallback) });
    }

    // Vanilla Remove does not clear the MP fallback or Original refresh catalog.
    internal PredictorRelicBag Remove(ModelKey key) => this with
    { Buckets = Buckets.Select(b => b with { Entries = b.Entries.Where(k => k != key).ToImmutableArray() }).ToImmutableArray() };

    internal PredictorRelicBag MoveToFallback(ModelKey key)
    {
        bool found = Buckets.Any(b => b.Entries.Contains(key));
        var bag = Remove(key);
        return found ? bag with { MultiplayerFallback = bag.MultiplayerFallback.Add(key) } : bag;
    }

    internal void Validate()
    {
        if (Original.IsDefault || Buckets.IsDefault || MultiplayerFallback.IsDefault ||
            Original.Any(e => !e.Key.IsValid || string.IsNullOrWhiteSpace(e.Rarity)) ||
            Buckets.Select(b => b.Rarity).Distinct(StringComparer.Ordinal).Count() != Buckets.Length ||
            Buckets.Any(b => string.IsNullOrWhiteSpace(b.Rarity) || b.Entries.IsDefault || b.Entries.Any(k => !k.IsValid)) ||
            MultiplayerFallback.Any(k => !k.IsValid)) throw new InvalidDataException("PredictorBagInvalid");
    }

    private static int Select(ImmutableArray<ModelKey> entries, bool back, Func<ModelKey, bool> filter)
    {
        for (int n = 0; n < entries.Length; n++)
        {
            int i = back ? entries.Length - 1 - n : n;
            if (filter(entries[i])) return i;
        }
        return -1;
    }
}
