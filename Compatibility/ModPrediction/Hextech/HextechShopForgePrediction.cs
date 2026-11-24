using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Core.PredictorRuntime;
using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

internal readonly record struct HextechShopForgeWeights(int Silver, int Gold, int Prismatic)
{
    internal int Total => unchecked(Silver + Gold + Prismatic);
}

// All game objects and optional-mod reflection stay at the capture boundary.
internal sealed record HextechShopForgeSnapshot(string Fingerprint, ModelKey Source,
    string StringSeed, int ActIndex, int TotalFloor, string PlayerKey, int PurchaseOrdinal,
    int RelicCount, HextechShopForgeWeights Weights, bool DirectGrant,
    ImmutableArray<ModelKey> SilverPool, ImmutableArray<ModelKey> GoldPool,
    ImmutableArray<ModelKey> PrismaticPool, ImmutableArray<ModelKey> AllPool)
{
    public ImmutableArray<HextechShopForgeUpgradePlan> UpgradePlans { get; init; } = [];
    public ImmutableArray<ModelKey> OwnedForges { get; init; } = [];
    public ImmutableArray<ModelKey> ContinuationForges { get; init; } = [];
    public string? ContinuationIssue { get; init; }
}

internal sealed record HextechShopForgeDeckCard(PredictorCard Source,
    ImmutableArray<PredictorCard> Levels, string? Issue = null);
internal sealed record HextechShopForgeUpgradePlan(ModelKey Forge, string Salt, int CardsPerGrant,
    ImmutableArray<HextechShopForgeDeckCard> Deck, string? Issue = null)
{
    public int OwnedStackCount { get; init; }
    public int GrantCount { get; init; } = 1;
}
internal sealed record HextechShopForgeUpgradeTarget(PredictorCard Before, PredictorCard? After);
internal sealed record HextechShopForgeUpgrade(ModelKey Forge,
    ImmutableArray<HextechShopForgeUpgradeTarget> Targets, string? Issue = null)
{
    public int GrantCount { get; init; } = 1;
    public ImmutableArray<PredictorCard> Cards => Targets.Where(t => t.After != null)
        .Select(t => t.After!).ToImmutableArray();
}

internal sealed record HextechShopForgeResult(string Fingerprint,
    ImmutableArray<ModelKey> Options, ModelKey? DirectGrant)
{
    public ImmutableArray<HextechShopForgeUpgrade> Upgrades { get; init; } = [];
    public ImmutableArray<HextechShopForgePurchase> Purchases { get; init; } = [];
    public string? ContinuationIssue { get; init; }
}

internal sealed record HextechShopForgePurchase(int PurchaseOrdinal,
    ImmutableArray<ModelKey> Options, ModelKey? DirectGrant);

internal static class HextechShopForgePrediction
{
    // Mirrors HextechForgeGrantHelper: normalize first, then DiceManiac doubles
    // gold/prismatic. Do not normalize a second time or add an RNG draw.
    internal static HextechShopForgeWeights ApplyDiceManiac(HextechShopForgeWeights weights, bool enabled)
    {
        var normalized = new HextechShopForgeWeights(Math.Max(0, weights.Silver),
            Math.Max(0, weights.Gold), Math.Max(0, weights.Prismatic));
        return enabled ? normalized with { Gold = unchecked(normalized.Gold * 2),
            Prismatic = unchecked(normalized.Prismatic * 2) } : normalized;
    }

    internal static HextechShopForgeResult Predict(HextechShopForgeSnapshot snapshot)
    {
        var current = PredictCurrent(snapshot);
        var (purchases, issue) = CommonPurchases(snapshot, current);
        return current with
        {
            Upgrades = snapshot.UpgradePlans.Select(plan => PredictUpgrade(snapshot, plan)).ToImmutableArray(),
            Purchases = purchases, ContinuationIssue = issue
        };
    }

    // Capture uses only this bounded current-offer calculation to decide which
    // optional deck facts are needed. Future purchases never request card faces.
    internal static HextechShopForgeResult PredictCurrent(HextechShopForgeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SilverPool.IsDefault || snapshot.GoldPool.IsDefault ||
            snapshot.PrismaticPool.IsDefault || snapshot.AllPool.IsDefault)
            throw new ArgumentException("HextechShopForgePoolsMissing", nameof(snapshot));
        var weights = snapshot.Weights;
        int rarity = 0;
        if (weights.Total > 0)
        {
            int roll = Index(snapshot, weights.Total, "shop-random-forge", "forge-rarity",
                snapshot.PlayerKey, snapshot.PurchaseOrdinal.ToString(), snapshot.RelicCount.ToString(),
                weights.Silver.ToString(), weights.Gold.ToString(), weights.Prismatic.ToString());
            rarity = roll < weights.Silver ? 0 : roll < unchecked(weights.Silver + weights.Gold) ? 1 : 2;
        }
        var pool = rarity switch { 0 => snapshot.SilverPool, 1 => snapshot.GoldPool, _ => snapshot.PrismaticPool };
        if (pool.IsEmpty) pool = snapshot.AllPool;
        // StableRandom sorts by Entry only, with stable ordering for equal keys.
        var remaining = pool.OrderBy(k => k.Entry, StringComparer.Ordinal).ToList();
        var options = ImmutableArray.CreateBuilder<ModelKey>(Math.Min(3, remaining.Count));
        for (int pick = 0; pick < 3 && remaining.Count > 0; pick++)
        {
            int index = Index(snapshot, remaining.Count, "shop-random-forge", "forge-choice",
                snapshot.PlayerKey, snapshot.PurchaseOrdinal.ToString(), rarity.ToString(),
                snapshot.RelicCount.ToString(), "pick", pick.ToString(), "pool",
                string.Join(",", remaining.Select(k => k.Entry)));
            options.Add(remaining[index]);
            remaining.RemoveAt(index);
        }
        var result = options.ToImmutable();
        ModelKey? direct = snapshot.DirectGrant && !result.IsEmpty
            ? result[Index(snapshot, result.Length, "forge-direct-grant", snapshot.PlayerKey, "shop",
                snapshot.RelicCount.ToString())] : null;
        return new(snapshot.Fingerprint, result, direct);
    }

    private sealed record Branch(int RelicCount, ImmutableHashSet<ModelKey> Owned);

    private static (ImmutableArray<HextechShopForgePurchase>, string?) CommonPurchases(
        HextechShopForgeSnapshot snapshot, HextechShopForgeResult first)
    {
        var purchases = ImmutableArray.CreateBuilder<HextechShopForgePurchase>();
        purchases.Add(new(snapshot.PurchaseOrdinal, first.Options, first.DirectGrant));
        if (first.Options.IsEmpty) return (purchases.ToImmutable(), null);
        if (snapshot.ContinuationIssue != null)
            return (purchases.ToImmutable(), snapshot.ContinuationIssue);
        var supported = snapshot.ContinuationForges.ToImmutableHashSet();
        var branches = new List<Branch> { new(snapshot.RelicCount, snapshot.OwnedForges.ToImmutableHashSet()) };
        var offer = first;
        for (int offset = 1; offset < 5; offset++)
        {
            ImmutableArray<ModelKey> choices = offer.DirectGrant is { } granted ? [granted] : offer.Options;
            // An unknown acquisition may alter count, weights or eligibility.
            // The known current offer survives; future rows are not invented.
            if (choices.Any(key => !supported.Contains(key)))
                return (purchases.ToImmutable(), "HextechForgeContinuationUnsupported");
            var next = new List<Branch>();
            HextechShopForgeResult? common = null;
            foreach (var branch in branches)
            foreach (var choice in choices)
            {
                var advanced = new Branch(branch.RelicCount + (branch.Owned.Contains(choice) ? 0 : 1),
                    branch.Owned.Add(choice));
                var candidate = PredictCurrent(snapshot with
                {
                    RelicCount = advanced.RelicCount,
                    PurchaseOrdinal = unchecked(snapshot.PurchaseOrdinal + offset)
                });
                if (common != null && (candidate.DirectGrant != common.DirectGrant ||
                    !candidate.Options.SequenceEqual(common.Options)))
                    return (purchases.ToImmutable(), "HextechForgeChoiceDivergence");
                common ??= candidate;
                // Equal offers are not equal states: preserve each owned set,
                // because buying a repeated identity may split a later row.
                next.Add(advanced);
            }
            if (common == null || common.Options.IsEmpty)
                return (purchases.ToImmutable(), "HextechForgeNoMoreOptions");
            purchases.Add(new(unchecked(snapshot.PurchaseOrdinal + offset), common.Options, common.DirectGrant));
            offer = common;
            branches = next;
        }
        return (purchases.ToImmutable(), null);
    }

    private static HextechShopForgeUpgrade PredictUpgrade(HextechShopForgeSnapshot snapshot,
        HextechShopForgeUpgradePlan plan)
    {
        if (plan.Issue != null) return new(plan.Forge, [], plan.Issue) { GrantCount = plan.GrantCount };
        var current = plan.Deck.Select(card => card.Source).ToArray();
        var affected = new List<int>();
        var targets = new Dictionary<int, HextechShopForgeUpgradeTarget>();
        string? issue = null;
        for (int grant = 0; grant < plan.GrantCount; grant++)
        {
            // Each obtain chooses from the deck after the previous obtain.
            // Equal card keys retain real deck order; selected instances are
            // distinct only within this grant, not across Double Vision copies.
            var remaining = Enumerable.Range(0, current.Length)
                .Where(index => current[index].UpgradeLevel < current[index].MaxUpgradeLevel)
                .OrderBy(index => current[index].Key.Entry, StringComparer.Ordinal).ToList();
            if (remaining.Count == 0) break;
            bool incomplete = false;
            for (int pick = 0; pick < plan.CardsPerGrant && remaining.Count > 0; pick++)
            {
                int choice = Index(snapshot, remaining.Count, plan.Salt, snapshot.PlayerKey,
                    plan.Deck.Length.ToString(), "pick", pick.ToString(), "pool",
                    string.Join(",", remaining.Select(index => current[index].Key.Entry)));
                int selected = remaining[choice];
                var card = plan.Deck[selected];
                int level = current[selected].UpgradeLevel - card.Source.UpgradeLevel;
                var after = level >= 0 && level < card.Levels.Length ? card.Levels[level] : null;
                if (after == null)
                {
                    issue ??= card.Issue ?? "HextechForgeUpgradeCardUnavailable";
                    incomplete = true;
                }
                else current[selected] = after;
                if (!targets.ContainsKey(selected)) affected.Add(selected);
                targets[selected] = new(card.Source, after);
                remaining.RemoveAt(choice);
            }
            if (incomplete && grant + 1 < plan.GrantCount)
            {
                // An unknown upgrade can change later eligibility. Keep known
                // affected identities, but do not present intermediate faces as
                // the final result of a partially simulated purchase.
                return new(plan.Forge, affected.Select(index => new HextechShopForgeUpgradeTarget(
                    plan.Deck[index].Source, null)).ToImmutableArray(), "HextechForgeUpgradeContinuationUnknown")
                { GrantCount = plan.GrantCount };
            }
        }
        return new(plan.Forge, affected.Select(index => targets[index]).ToImmutableArray(), issue)
        { GrantCount = plan.GrantCount };
    }

    private static int Index(HextechShopForgeSnapshot snapshot, int count, params string[] salt)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, snapshot.StringSeed);
            Add(ref hash, "|act:");
            Add(ref hash, snapshot.ActIndex.ToString());
            Add(ref hash, "|floor:");
            Add(ref hash, snapshot.TotalFloor.ToString());
            foreach (string part in salt) { Add(ref hash, "|"); Add(ref hash, part); }
            hash ^= hash >> 33;
            hash *= 18397679294719823053UL;
            hash ^= hash >> 33;
            hash *= 14181476777654086739UL;
            hash ^= hash >> 33;
            return (int)(hash % (ulong)count);
        }
    }

    private static void Add(ref ulong hash, string value)
    {
        unchecked { foreach (char c in value) { hash ^= c; hash *= 1099511628211UL; } }
    }
}
