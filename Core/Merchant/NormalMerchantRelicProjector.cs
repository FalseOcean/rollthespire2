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

        var bags = BuildBags(initialSequences);
        var shops = new Xoshiro256StarStar(Beta111Profile.Instance.DeriveNamedStreamSeed(rootHash, "shops"));
        int calls = Beta111NormalMerchantShopsContinuation.For(true).Total;
        var merchants = new List<NormalMerchantRelics>(merchantCount);
        string issue = string.Empty;
        for (int ordinal = 1; ordinal <= merchantCount; ordinal++)
        {
            // Relic rarities are the first two calls. Later ordinals also
            // require the preceding successful inventory's consumption shape.
            var start = shops.Clone();
            var left = Slot();
            var middle = Slot();
            merchants.Add(new(ordinal, left, middle));
            // Slot three can draw Common when the Shop-only bucket runs out.
            bags?.ConsumeShopSlot();
            if (ordinal < merchantCount && authority.EffectAuthority?.MerchantInitialInventoryShapeExact != true &&
                Beta111NormalMerchantShopsContinuation.CheckPotionStock(authority.EffectAuthority?.MerchantPotionStock, start, ordinal) is { } lost)
            {
                issue = lost;
                break;
            }
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

        private static Stack<(int, ModelKey)>[] NewLanes() => [new(), new(), new()];

        internal readonly bool[] UnknownLane = new bool[3];
        internal int PossibleShopStock;
        internal int CertainShopStock;

        internal void ConsumeShopSlot()
        {
            if (PossibleShopStock > 0) PossibleShopStock--;
            else Pull(Possible, RelicSequenceKind.Common);
            if (CertainShopStock > 0) CertainShopStock--;
            else Pull(Certain, RelicSequenceKind.Common);
        }

        internal ModelKey? Pull(RelicSequenceKind rolled)
        {
            var first = Pull(Possible, rolled);
            var last = Pull(Certain, rolled);
            return first == last ? first?.Key : null;
        }

        private (RelicSequenceKind Lane, int Index, ModelKey Key)? Pull(
            Stack<(int Index, ModelKey Key)>[] bags, RelicSequenceKind rolled)
        {
            var lane = rolled;
            while (true)
            {
                if (bags == Possible && UnknownLane[(int)lane]) return null;
                if (bags[(int)lane].TryPop(out var item)) return (lane, item.Index, item.Key);
                if (lane == RelicSequenceKind.Rare) return (lane, -1, Circlet);
                lane = lane + 1;
            }
        }
    }

    private static Bags? BuildBags(RelicSequencePredictionResult sequence)
    {
        if (sequence.Status != SeedDomainEvaluationStatus.Evaluated || sequence.RngSemantics != RngSemanticProfileId.Modern113) return null;
        var stock = sequence.ShopStockCount ?? (Certain: 0, Possible: int.MaxValue);
        var bags = new Bags { CertainShopStock = stock.Certain, PossibleShopStock = stock.Possible };
        foreach (var kind in new[] { RelicSequenceKind.Common, RelicSequenceKind.Uncommon, RelicSequenceKind.Rare })
        {
            var lane = sequence.GetPlayerLane(kind);
            if (lane is null || lane.FullEntries.Count != lane.TotalCount || lane.IssueCode.Length > 0)
            {
                bags.UnknownLane[(int)kind] = true;
                continue;
            }
            foreach (var entry in lane.FullEntries)
            {
                var key = entry.RelicKey;
                if (key == BaseGameModelKeys.Relics.MassiveScroll) continue;
                bool? allowed = entry.ShopEligible;
                if (key == StandardOpeningRelicEligibility.LastingCandy)
                    allowed &= sequence.EarlyLastingCandyEligible;
                if (allowed != false) bags.Possible[(int)lane.Kind].Push((entry.Position, key));
                if (allowed == true) bags.Certain[(int)lane.Kind].Push((entry.Position, key));
            }
        }
        return bags;
    }
}
