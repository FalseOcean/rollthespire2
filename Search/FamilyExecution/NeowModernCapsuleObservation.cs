using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Sparse observation of the two native upgrades on one acquired Bones route.
// No deck is reconstructed: each actual native hook consumes one child seed.
internal sealed class NeowModernCapsuleObservation(
    NeowReplayPlan route, NeowModernCapsuleObservation.Lane[] lanes, int lastBucket, int longestLane, int[] companionRewards)
{
    internal sealed record Lane(int Bucket, sbyte[] Entries, int EligibleCount);
    private const sbyte Ineligible = -2;

    internal static NeowModernCapsuleObservation? Compile(SearchModel request, NeowReplayPlan route)
    {
        if (!route.Bones || !route.HasCapsuleNonCombatAlternatives ||
            request.Authority.EffectAuthority is not { HasExactRelicBagSourcePools: true } effects) return null;
        var shared = effects.SharedRelicPoolSource!;
        var personal = shared.Concat(effects.CharacterRelicPoolSource!)
            .Where(relic => relic.Rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or
                EffectRelicRarity.Rare or EffectRelicRarity.Shop)
            .DistinctBy(relic => relic.RelicKey, ModelKeyComparer.Instance).ToArray();
        bool? candyAllowed = personal.Any(relic => relic.RelicKey == StandardOpeningRelicEligibility.LastingCandy)
            ? StandardOpeningRelicEligibility.AllowsLastingCandy(request.Authority) : true;
        if (candyAllowed is null) return null;

        int bucket = Group(shared).Count();
        Lane[] lanes = [new(-1, [], 0), new(-1, [], 0), new(-1, [], 0)];
        foreach (var group in Group(personal))
        {
            var rarity = group.First().Rarity;
            if (group.Any(relic => relic.Rarity != rarity)) return null;
            if (rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare)
            {
                int lane = (int)rarity;
                if (lanes[lane].Bucket >= 0) return null;
                sbyte[] entries = group.Select(relic => !candyAllowed.Value &&
                    relic.RelicKey == StandardOpeningRelicEligibility.LastingCandy ? Ineligible :
                    (sbyte)NeowCapsuleNonCombatContinuation.NativeAdvance(relic)).ToArray();
                lanes[lane] = new(bucket, entries, entries.Count(entry => entry != Ineligible));
            }
            bucket++;
        }
        int[] burns = Enumerable.Repeat(-1, 30).ToArray();
        foreach (byte source in route.Authority.BonesEligibleRelicIds)
            if (source < burns.Length && DrawCount(source) == 0 &&
                RelicOpeningPrefixPlan.TryProveCompanionRewardsDraws(route, source, out int draws))
                burns[source] = draws;
        return new(route, lanes, lanes.Max(lane => lane.Bucket), lanes.Max(lane => lane.Entries.Length), burns);
    }

    internal bool TryObserve(ulong root, byte first, byte second, Beta110FastRng rewards,
        out int firstAdvance, out int secondAdvance)
    {
        firstAdvance = secondAdvance = 0;
        int firstCount = NeowCapsuleNonCombatContinuation.ReadsAfterFirst(route, second) ? DrawCount(first) : 0;
        int secondCount = route.HasFinalCurseFastProjection ? DrawCount(second) : 0;
        int count = firstCount + secondCount;
        if (count == 0) return true;
        if (firstCount == 0 && secondCount != 0)
        {
            if (first >= companionRewards.Length || companionRewards[first] == -1) return false;
            RelicOpeningPrefixPlan.AdvanceObservedRewards(companionRewards[first], ref rewards);
        }

        var parent = new Beta110FastRng(unchecked(root + RelicCpuPlan.UpFrontHash));
        Span<Beta110FastRng> children = stackalloc Beta110FastRng[3];
        for (int bucket = 0; bucket <= lastBucket; bucket++)
        {
            if (bucket == lanes[0].Bucket) children[0] = parent.ForkShuffle();
            else if (bucket == lanes[1].Bucket) children[1] = parent.ForkShuffle();
            else if (bucket == lanes[2].Bucket) children[2] = parent.ForkShuffle();
            else parent.ConsumeShuffleSeed();
        }

        Span<int> consumed = stackalloc int[3];
        consumed.Clear();
        Span<sbyte> heads = stackalloc sbyte[9];
        Span<sbyte> scratch = stackalloc sbyte[longestLane];
        for (int draw = 0; draw < count; draw++)
        {
            float roll = rewards.NextFloat();
            int lane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
            while (lane < 3 && consumed[lane] >= lanes[lane].EligibleCount) lane++;
            if (lane == 3) continue; // Circlet fallback consumes no NonCombat draw.
            int head = lane * 3;
            if (consumed[lane] == 0)
            {
                var entries = scratch[..lanes[lane].Entries.Length];
                lanes[lane].Entries.AsSpan().CopyTo(entries);
                children[lane].UnstableShuffle(entries);
                int copied = 0;
                foreach (sbyte entry in entries)
                {
                    if (entry == Ineligible) continue;
                    heads[head + copied++] = entry;
                    if (copied == count) break;
                }
            }
            int advance = heads[head + consumed[lane]++];
            if (advance < 0) return false;
            if (draw < firstCount) firstAdvance += advance;
            else secondAdvance += advance;
        }
        return true;
    }

    private static int DrawCount(byte source) => source switch
    {
        NeowCapsuleNonCombatContinuation.Small => 1,
        NeowCapsuleNonCombatContinuation.Large => 2,
        _ => 0
    };

    // Mirrors NeowRewardGenerator's solo raw-source grouping, before any
    // eligibility filtering. Ineligible identities still participate in shuffle.
    private static IEnumerable<IGrouping<string, NeowEffectRelicSnapshot>> Group(
        IEnumerable<NeowEffectRelicSnapshot> source) => source.GroupBy(
        relic => string.IsNullOrWhiteSpace(relic.RarityCode) ? relic.Rarity.ToString() : relic.RarityCode,
        StringComparer.Ordinal);
}
