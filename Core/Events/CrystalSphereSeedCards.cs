using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Events;

internal sealed record CrystalSphereSeedCardTargets(
    ImmutableArray<ModelKey> UncommonPool, ImmutableArray<ModelKey> RarePool,
    ImmutableArray<ModelKey> Uncommon, ImmutableArray<ModelKey> Rare)
{
    internal static CrystalSphereSeedCardTargets? Create(IReadOnlyList<NeowEffectCardSnapshot> orderedPool,
        ImmutableArray<ModelKey> requested)
    {
        var uncommon = ImmutableArray.CreateBuilder<ModelKey>();
        var rare = ImmutableArray.CreateBuilder<ModelKey>();
        var distinctPool = orderedPool.Where(card => card.IsUnlockedInCapturedPool)
            .DistinctBy(card => card.CardKey).ToArray();
        var rarityByKey = distinctPool.ToDictionary(card => card.CardKey, card => card.Rarity);
        foreach (var key in requested)
        {
            if (!rarityByKey.TryGetValue(key, out var rarity)) return null;
            switch (rarity)
            {
                case EffectCardRarity.Uncommon: uncommon.Add(key); break;
                case EffectCardRarity.Rare: rare.Add(key); break;
                default: return null;
            }
        }

        var uncommonPool = Pool(EffectCardRarity.Uncommon);
        var rarePool = Pool(EffectCardRarity.Rare);
        if ((uncommon.Count != 0 && uncommonPool.Length < 3) || (rare.Count != 0 && rarePool.Length < 3)) return null;
        return new(uncommonPool, rarePool, uncommon.ToImmutable(), rare.ToImmutable());

        ImmutableArray<ModelKey> Pool(EffectCardRarity rarity) => distinctPool
            .Where(card => card.Rarity == rarity).Select(card => card.CardKey).ToImmutableArray();
    }
}

// For the ordinary fixed-rarity Crystal menu, identity draws are interleaved
// with upgrade draws. Upgrade odds never alter the number of calls or the
// identities left in the menu's ordered pool.
internal sealed class CrystalSphereSeedCardOffers(ImmutableArray<ModelKey> pool, ImmutableArray<ModelKey> goals,
    Xoshiro256StarStar afterPlacement)
{
    internal readonly record struct Menu(ModelKey First, ModelKey Second, ModelKey Third)
    {
        internal bool Offers(ModelKey card) => First == card || Second == card || Third == card;
    }

    internal readonly record struct RewardSlot(int Item, int Subscription, int Offset);

    private readonly List<Menu> _menus = [];
    private readonly Xoshiro256StarStar _cursor = afterPlacement.Clone();

    internal int Required => goals.Length;

    internal Menu MenuAt(int offset)
    {
        while (_menus.Count <= offset)
        {
            var rng = _cursor.Clone();
            int first = rng.NextInt(pool.Length);
            rng.Advance(1);
            int second = rng.NextInt(pool.Length - 1);
            if (second >= first) second++;
            rng.Advance(1);
            int third = rng.NextInt(pool.Length - 2);
            if (third >= Math.Min(first, second)) third++;
            if (third >= Math.Max(first, second)) third++;
            _menus.Add(new(pool[first], pool[second], pool[third]));
            _cursor.Advance(1);
        }
        return _menus[offset];
    }

    internal bool AllPossible(int maximumOffset)
    {
        foreach (var goal in goals)
        {
            bool found = false;
            for (int offset = 0; offset <= maximumOffset && !found; offset++)
                found = MenuAt(offset).Offers(goal);
            if (!found) return false;
        }
        return true;
    }

    internal bool TryAssign(ReadOnlySpan<RewardSlot> slots, int potionCalls,
        ImmutableArray<CrystalSphereSeedCardTake>.Builder? takes = null)
    {
        if (slots.Length < goals.Length) return false;
        if (goals.Length == 1)
        {
            foreach (var slot in slots)
            {
                if (!MenuAt(slot.Offset + potionCalls).Offers(goals[0])) continue;
                takes?.Add(new(slot.Item, slot.Subscription, goals[0]));
                return true;
            }
            return false;
        }

        // Augment a card-to-menu assignment. In particular, duplicate card
        // goals need different subscriptions even when a tile is shared.
        Span<int> assignments = stackalloc int[slots.Length];
        Span<bool> visited = stackalloc bool[slots.Length];
        assignments.Fill(-1);
        for (int goal = 0; goal < goals.Length; goal++)
        {
            visited.Clear();
            if (!Assign(goal, slots, potionCalls, assignments, visited)) return false;
        }
        if (takes != null)
            for (int i = 0; i < assignments.Length; i++)
                if (assignments[i] >= 0)
                    takes.Add(new(slots[i].Item, slots[i].Subscription, goals[assignments[i]]));
        return true;
    }

    private bool Assign(int goal, ReadOnlySpan<RewardSlot> slots, int potionCalls, Span<int> assignments, Span<bool> visited)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (visited[i] || !MenuAt(slots[i].Offset + potionCalls).Offers(goals[goal])) continue;
            visited[i] = true;
            if (assignments[i] >= 0 && !Assign(assignments[i], slots, potionCalls, assignments, visited)) continue;
            assignments[i] = goal;
            return true;
        }
        return false;
    }
}
