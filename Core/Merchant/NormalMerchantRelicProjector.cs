using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Merchant;

public sealed record NormalMerchantRelicSlot(RelicSequenceKind Rarity, ModelKey? RelicKey);
public sealed record NormalMerchantRelics(int MerchantOrdinal, NormalMerchantRelicSlot Left, NormalMerchantRelicSlot Middle);
public sealed record NormalMerchantRelicProjection(IReadOnlyList<NormalMerchantRelics> Merchants, string IssueCode);

/// <summary>Solo 0.113 standard shop initial stock before late Act 3 eligibility changes.</summary>
public static class NormalMerchantRelicProjector
{
    public const int MerchantCount = Beta111NormalMerchantColorlessSequenceProjector.MerchantCount;
    private static readonly ModelKey Circlet = new("RELIC", "CIRCLET");

    public static NormalMerchantRelicProjection Project(ulong rootHash, RuntimeContextAuthoritySnapshot authority,
        RelicSequencePredictionResult initialSequences, int merchantCount = MerchantCount)
    {
        if (merchantCount is < 1 or > MerchantCount) throw new ArgumentOutOfRangeException(nameof(merchantCount));
        if (authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113 ||
            authority.PlayersCount != 1 || authority.PlayerSlotIndex != 0 ||
            authority.PredictionGameMode != WorldGameMode.Singleplayer)
            return new([], "NormalMerchantRelicsRequiresSolo113");

        var bags = BuildBags(initialSequences, authority);
        var shops = new Xoshiro256StarStar(Beta111Profile.Instance.DeriveNamedStreamSeed(rootHash, "shops"));
        int calls = Beta111NormalMerchantShopsContinuation.For(true).Total;
        var merchants = new List<NormalMerchantRelics>(merchantCount);
        string issue = string.Empty;
        for (int ordinal = 1; ordinal <= merchantCount; ordinal++)
        {
            // Relic rarities are the first two calls. Later ordinals also
            // require the preceding successful inventory's consumption shape.
            if (ordinal > 1 && authority.EffectAuthority?.MerchantInitialInventoryShapeExact != true)
            {
                issue = "NormalMerchantRelicsMissingStoreContinuation";
                break;
            }
            var left = Slot();
            var middle = Slot();
            merchants.Add(new(ordinal, left, middle));
            // Slot three can draw Common when the Shop-only bucket runs out.
            bags?.Pull(RelicSequenceKind.Shop);
            shops.Advance(calls - 2);
        }
        return new(merchants, issue);

        NormalMerchantRelicSlot Slot()
        {
            float roll = shops.NextFloat();
            var rarity = roll < .5f ? RelicSequenceKind.Common :
                roll < .83f ? RelicSequenceKind.Uncommon : RelicSequenceKind.Rare;
            var key = bags?.Pull(rarity);
            if (key is null)
                issue = initialSequences.IssueCode.Length > 0 ? initialSequences.IssueCode : "NormalMerchantRelicsMissingBagEligibility";
            return new(rarity, key);
        }
    }

    // When an entry's eligibility is unknown, the two bounds consume the most
    // and least available stock. A draw has an identity only when both reach
    // the same physical slot (including all rarity fallbacks).
    private sealed class Bags
    {
        internal readonly Stack<(int Index, ModelKey Key)>[] Possible = NewLanes();
        internal readonly Stack<(int Index, ModelKey Key)>[] Certain = NewLanes();

        private static Stack<(int, ModelKey)>[] NewLanes() => [new(), new(), new(), new()];

        internal ModelKey? Pull(RelicSequenceKind rolled)
        {
            var first = Pull(Possible, rolled);
            var last = Pull(Certain, rolled);
            return first == last ? first.Key : null;
        }

        private static (RelicSequenceKind Lane, int Index, ModelKey Key) Pull(
            Stack<(int Index, ModelKey Key)>[] bags, RelicSequenceKind rolled)
        {
            var lane = rolled;
            while (true)
            {
                if (bags[(int)lane].TryPop(out var item)) return (lane, item.Index, item.Key);
                if (lane == RelicSequenceKind.Rare) return (lane, -1, Circlet);
                lane = lane == RelicSequenceKind.Shop ? RelicSequenceKind.Common : lane + 1;
            }
        }
    }

    private static Bags? BuildBags(RelicSequencePredictionResult sequence, RuntimeContextAuthoritySnapshot authority)
    {
        var catalog = authority.WorldAuthority?.Beta109Generation;
        if (sequence.Status != SeedDomainEvaluationStatus.Evaluated || sequence.RngSemantics != RngSemanticProfileId.Modern113 ||
            catalog is null || sequence.Lanes.Count != 4) return null;
        var eligibility = new Dictionary<ModelKey, bool?>();
        foreach (var bucket in catalog.PlayerRelicBuckets)
            foreach (var entry in bucket.OrderedEntries)
                eligibility[entry.RelicKey] = entry.ShopEligibilityExact ? entry.IsAllowedInShops : null;

        var bags = new Bags();
        foreach (var lane in sequence.Lanes)
        {
            // Slot three matters only when its eligible stock is exhausted.
            // Captured eligibility counts suffice even when the initial Shop
            // lane omitted its ordered preview due to one unknown entry.
            if (lane.Kind == RelicSequenceKind.Shop)
            {
                var shop = catalog.PlayerRelicBuckets.FirstOrDefault(b =>
                    b.BucketId.EndsWith(":Shop", StringComparison.Ordinal));
                if (shop == null) return null;
                for (int i = 0; i < shop.OrderedEntries.Count; i++)
                {
                    var entry = shop.OrderedEntries[i];
                    if (!entry.ShopEligibilityExact || entry.IsAllowedInShops)
                        bags.Possible[(int)lane.Kind].Push((i, entry.RelicKey));
                    if (entry.ShopEligibilityExact && entry.IsAllowedInShops)
                        bags.Certain[(int)lane.Kind].Push((i, entry.RelicKey));
                }
                continue;
            }
            if (lane.FullEntries.Count != lane.TotalCount || lane.IssueCode.Length > 0) return null;
            foreach (var entry in lane.FullEntries)
            {
                var key = entry.RelicKey;
                if (key == BaseGameModelKeys.Relics.MassiveScroll) continue;
                bool? allowed = eligibility.GetValueOrDefault(key);
                if (key == StandardOpeningRelicEligibility.LastingCandy)
                    allowed &= sequence.EarlyLastingCandyEligible;
                if (allowed != false) bags.Possible[(int)lane.Kind].Push((entry.Position, key));
                if (allowed == true) bags.Certain[(int)lane.Kind].Push((entry.Position, key));
            }
        }
        return bags;
    }
}
