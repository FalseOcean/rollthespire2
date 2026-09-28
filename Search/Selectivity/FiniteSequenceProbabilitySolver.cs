using System.Numerics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Exact probability solver for the Search UI's finite unique-item sequence semantics.
/// It never reads runtime/game state: callers provide an immutable, already-eligible pool.
/// Only target identities referenced by the query are tracked individually; all unrelated
/// identities are aggregated by partition (Event source, or partition 0 for Relic lanes).
/// </summary>
internal static class FiniteSequenceProbabilitySolver
{
    internal readonly record struct Item(ModelKey Key, int Partition = 0);

    internal sealed record Constraint(
        SearchSequenceRangeMode RangeMode,
        int RangeValue,
        ModelKeySetFilter Keys,
        int? Partition = null,
        bool ExactSlotRequiresPartitionMatch = false)
    {
        public bool IsEmpty => RangeValue <= 0 || Keys.IsEmpty;
    }

    internal sealed record Result(
        double Probability,
        int PoolSize,
        int RelevantKeyCount,
        int EvaluatedStateCount,
        string Normalization);

    private readonly record struct State(
        int Position,
        ulong RemainingRelevantMask,
        int Other0,
        int Other1,
        int Other2,
        int Other3);

    private sealed record CompiledConstraint(
        SearchSequenceRangeMode RangeMode,
        int TriggerPosition,
        ulong AnyMask,
        ulong AllMask,
        ulong BanMask,
        int? Partition,
        bool ExactSlotRequiresPartitionMatch);

    public static bool TrySolve(
        IReadOnlyList<Item> pool,
        IReadOnlyList<Constraint> constraints,
        out Result result,
        out string issue)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(constraints);
        result = default!;
        issue = string.Empty;

        Constraint[] active = constraints.Where(item => !item.IsEmpty).ToArray();
        if (active.Length == 0)
        {
            result = new Result(1d, pool.Count, 0, 1, "NoActiveSequenceConstraint");
            return true;
        }

        if (pool.Any(item => !item.Key.IsValid))
        {
            issue = "InvalidPoolIdentity";
            return false;
        }
        if (pool.Any(item => item.Partition is < 0 or > 3))
        {
            issue = "PartitionOutsideSupportedRange0To3";
            return false;
        }

        // The production Relic/Event queues represented by this solver are unique-item
        // permutations. Event local-visited duplicate collapse is handled by the caller;
        // silently treating duplicate identities as independent entries would misprice set semantics.
        ModelKey[] duplicateKeys = pool.GroupBy(item => item.Key, ModelKeyComparer.Instance)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateKeys.Length != 0)
        {
            issue = "DuplicateIdentityRequiresFirstOccurrenceModel:" +
                    string.Join(',', duplicateKeys.Take(4).Select(key => key.Serialized));
            return false;
        }

        ModelKey[] targetKeys = active
            .SelectMany(condition => condition.Keys.Any.Concat(condition.Keys.All).Concat(condition.Keys.Ban))
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (targetKeys.Length > 12)
            return TrySolveEquivalentClasses(pool, active, targetKeys.Length, out result, out issue);

        var bitByKey = new Dictionary<ModelKey, ulong>(ModelKeyComparer.Instance);
        for (int index = 0; index < targetKeys.Length; index++)
            bitByKey[targetKeys[index]] = 1UL << index;

        ulong[] partitionMasks = new ulong[4];
        ulong initialRelevantMask = 0UL;
        int[] otherCounts = new int[4];
        foreach (Item item in pool)
        {
            if (bitByKey.TryGetValue(item.Key, out ulong bit))
            {
                initialRelevantMask |= bit;
                partitionMasks[item.Partition] |= bit;
            }
            else
            {
                otherCounts[item.Partition]++;
            }
        }

        ulong Mask(IEnumerable<ModelKey> keys)
        {
            ulong mask = 0UL;
            foreach (ModelKey key in keys.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance))
            {
                if (bitByKey.TryGetValue(key, out ulong bit)) mask |= bit;
                else
                {
                    // Keep an absent positive target observable by assigning no bit: Any/All
                    // compilation below separately marks the constraint impossible when needed.
                }
            }
            return mask;
        }

        var compiled = new List<CompiledConstraint>(active.Length);
        foreach (Constraint condition in active)
        {
            if (condition.Partition is < 0 or > 3)
            {
                issue = "ConstraintPartitionOutsideSupportedRange0To3";
                return false;
            }

            ModelKey[] anyKeys = condition.Keys.Any.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray();
            ModelKey[] allKeys = condition.Keys.All.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray();
            ModelKey[] banKeys = condition.Keys.Ban.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray();

            // Positive identities absent from the immutable eligible pool make this
            // sequence constraint structurally impossible. Ban identities absent from
            // the pool are simply redundant.
            var poolKeys = new HashSet<ModelKey>(pool.Select(item => item.Key), ModelKeyComparer.Instance);
            if (allKeys.Any(key => !poolKeys.Contains(key)) ||
                (anyKeys.Length != 0 && anyKeys.All(key => !poolKeys.Contains(key))))
            {
                result = new Result(0d, pool.Count, targetKeys.Length, 0,
                    "PositiveTargetAbsentFromEligiblePool");
                return true;
            }

            int trigger = condition.RangeMode == SearchSequenceRangeMode.FirstN
                ? Math.Min(condition.RangeValue, pool.Count)
                : condition.RangeValue;
            if (condition.RangeMode == SearchSequenceRangeMode.ExactSlot && condition.RangeValue > pool.Count)
            {
                result = new Result(0d, pool.Count, targetKeys.Length, 0,
                    "ExactSlotOutsideFinitePool");
                return true;
            }

            compiled.Add(new CompiledConstraint(
                condition.RangeMode,
                trigger,
                Mask(anyKeys),
                Mask(allKeys),
                Mask(banKeys),
                condition.Partition,
                condition.ExactSlotRequiresPartitionMatch));
        }

        foreach (CompiledConstraint condition in compiled.Where(item => item.TriggerPosition == 0))
        {
            // A FirstN window over an empty finite sequence observes the empty set.
            if (condition.RangeMode == SearchSequenceRangeMode.ExactSlot || !Matches(0UL, condition))
            {
                result = new Result(0d, pool.Count, targetKeys.Length, 1, "EmptyFiniteSequenceRejectsConstraint");
                return true;
            }
        }

        int maxPosition = compiled.Max(condition => condition.TriggerPosition);
        if (maxPosition <= 0)
        {
            result = new Result(1d, pool.Count, targetKeys.Length, 1, "EmptyFiniteSequenceBanOnlyAccepted");
            return true;
        }

        var memo = new Dictionary<State, double>();
        int evaluated = 0;

        double Solve(State state)
        {
            if (memo.TryGetValue(state, out double cached)) return cached;
            evaluated++;

            int remainingTotal = BitOperations.PopCount(state.RemainingRelevantMask) +
                                 state.Other0 + state.Other1 + state.Other2 + state.Other3;
            if (state.Position >= maxPosition || remainingTotal == 0)
            {
                memo[state] = 1d;
                return 1d;
            }

            int nextPosition = state.Position + 1;
            double probability = 0d;

            ulong bits = state.RemainingRelevantMask;
            while (bits != 0UL)
            {
                int bitIndex = BitOperations.TrailingZeroCount(bits);
                ulong bit = 1UL << bitIndex;
                bits &= ~bit;
                int partition = PartitionOf(bit, partitionMasks);
                ulong nextMask = state.RemainingRelevantMask & ~bit;
                if (!AcceptAtPosition(nextPosition, bit, partition, nextMask)) continue;
                var next = state with { Position = nextPosition, RemainingRelevantMask = nextMask };
                probability += (1d / remainingTotal) * Solve(next);
            }

            int[] others = { state.Other0, state.Other1, state.Other2, state.Other3 };
            for (int partition = 0; partition < 4; partition++)
            {
                int count = others[partition];
                if (count <= 0) continue;
                if (!AcceptAtPosition(nextPosition, 0UL, partition, state.RemainingRelevantMask)) continue;
                State next = partition switch
                {
                    0 => state with { Position = nextPosition, Other0 = state.Other0 - 1 },
                    1 => state with { Position = nextPosition, Other1 = state.Other1 - 1 },
                    2 => state with { Position = nextPosition, Other2 = state.Other2 - 1 },
                    _ => state with { Position = nextPosition, Other3 = state.Other3 - 1 }
                };
                probability += (count / (double)remainingTotal) * Solve(next);
            }

            probability = Math.Clamp(probability, 0d, 1d);
            memo[state] = probability;
            return probability;
        }

        bool AcceptAtPosition(int position, ulong currentBit, int currentPartition, ulong remainingAfter)
        {
            ulong seen = initialRelevantMask ^ remainingAfter;
            foreach (CompiledConstraint condition in compiled)
            {
                if (condition.TriggerPosition != position) continue;

                if (condition.RangeMode == SearchSequenceRangeMode.ExactSlot)
                {
                    if (condition.Partition.HasValue &&
                        condition.ExactSlotRequiresPartitionMatch &&
                        currentPartition != condition.Partition.Value)
                        return false;

                    ulong observed = currentBit;
                    if (condition.Partition.HasValue && currentPartition != condition.Partition.Value)
                        observed = 0UL;
                    if (!Matches(observed, condition)) return false;
                }
                else
                {
                    ulong observed = seen;
                    if (condition.Partition.HasValue)
                        observed &= partitionMasks[condition.Partition.Value];
                    if (!Matches(observed, condition)) return false;
                }
            }
            return true;
        }

        static bool Matches(ulong observed, CompiledConstraint condition)
        {
            if (condition.AnyMask != 0UL && (observed & condition.AnyMask) == 0UL) return false;
            if (condition.AllMask != 0UL && (observed & condition.AllMask) != condition.AllMask) return false;
            if (condition.BanMask != 0UL && (observed & condition.BanMask) != 0UL) return false;
            return true;
        }

        static int PartitionOf(ulong bit, ulong[] partitionMasks)
        {
            for (int partition = 0; partition < partitionMasks.Length; partition++)
            {
                if ((partitionMasks[partition] & bit) != 0UL) return partition;
            }
            return 0;
        }

        State initial = new(
            0,
            initialRelevantMask,
            otherCounts[0],
            otherCounts[1],
            otherCounts[2],
            otherCounts[3]);
        double final = Solve(initial);
        result = new Result(
            final,
            pool.Count,
            targetKeys.Length,
            evaluated,
            $"ExactFinitePermutation;pool={pool.Count};relevant={targetKeys.Length};maxPosition={maxPosition};states={evaluated}");
        return true;
    }

    // Large Any/Ban sets distinguish membership, not each identity. Identities
    // with the same membership in every constraint and the same partition are
    // exchangeable. Track their remaining counts; All still requires every
    // member of its class to have appeared, so no set/multiset fact is weakened.
    private static bool TrySolveEquivalentClasses(IReadOnlyList<Item> pool, Constraint[] active,
        int relevantCount, out Result result, out string issue)
    {
        issue = ""; result = default!;
        var poolKeys = pool.Select(p => p.Key).ToHashSet();
        var any = active.Select(c => c.Keys.Any.Where(k => k.IsValid).ToHashSet()).ToArray();
        var all = active.Select(c => c.Keys.All.Where(k => k.IsValid).ToHashSet()).ToArray();
        var ban = active.Select(c => c.Keys.Ban.Where(k => k.IsValid).ToHashSet()).ToArray();
        var triggers = active.Select(c => c.RangeMode == SearchSequenceRangeMode.FirstN
            ? Math.Min(c.RangeValue, pool.Count) : c.RangeValue).ToArray();
        for (int row = 0; row < active.Length; row++)
        {
            if (active[row].Partition is < 0 or > 3)
            { issue = "ConstraintPartitionOutsideSupportedRange0To3"; return false; }
            if (all[row].Any(k => !poolKeys.Contains(k)) || any[row].Count > 0 && !any[row].Overlaps(poolKeys) ||
                active[row].RangeMode == SearchSequenceRangeMode.ExactSlot &&
                (triggers[row] > pool.Count || all[row].Count > 1))
            { result = new(0, pool.Count, relevantCount, 0, "EquivalentClassesImpossibleConstraint"); return true; }
        }
        var classes = pool.GroupBy(item => item.Partition + ":" + string.Concat(Enumerable.Range(0, active.Length)
            .Select(row => (char)('0' + (any[row].Contains(item.Key) ? 1 : 0) +
                (all[row].Contains(item.Key) ? 2 : 0) + (ban[row].Contains(item.Key) ? 4 : 0)))))
            .Select(g => g.ToArray()).ToArray();
        int[] counts = classes.Select(g => g.Length).ToArray(), remaining = (int[])counts.Clone();
        var flags = new byte[classes.Length, active.Length];
        for (int category = 0; category < classes.Length; category++)
        for (int row = 0; row < active.Length; row++)
        {
            ModelKey key = classes[category][0].Key;
            flags[category, row] = (byte)((any[row].Contains(key) ? 1 : 0) |
                (all[row].Contains(key) ? 2 : 0) | (ban[row].Contains(key) ? 4 : 0));
        }
        bool Accept(int position, int current)
        {
            for (int row = 0; row < active.Length; row++)
            {
                if (triggers[row] != position) continue;
                Constraint constraint = active[row];
                if (constraint.RangeMode == SearchSequenceRangeMode.ExactSlot)
                {
                    bool samePartition = current >= 0 && (!constraint.Partition.HasValue ||
                        classes[current][0].Partition == constraint.Partition.Value);
                    if (!samePartition && constraint.ExactSlotRequiresPartitionMatch) return false;
                    byte observed = samePartition ? flags[current, row] : (byte)0;
                    if (any[row].Count > 0 && (observed & 1) == 0 ||
                        all[row].Count > 0 && (observed & 2) == 0 || (observed & 4) != 0) return false;
                    continue;
                }
                bool anySeen = any[row].Count == 0;
                for (int category = 0; category < classes.Length; category++)
                {
                    bool observedPartition = !constraint.Partition.HasValue ||
                        classes[category][0].Partition == constraint.Partition.Value;
                    int seen = observedPartition ? counts[category] - remaining[category] : 0;
                    byte mask = flags[category, row];
                    if ((mask & 1) != 0 && seen > 0) anySeen = true;
                    if ((mask & 2) != 0 && seen < counts[category] || (mask & 4) != 0 && seen > 0) return false;
                }
                if (!anySeen) return false;
            }
            return true;
        }
        if (!Accept(0, -1))
        { result = new(0, pool.Count, relevantCount, 1, "EquivalentClassesEmptyWindowReject"); return true; }
        int maxPosition = triggers.Max(), evaluated = 0;
        var memo = new Dictionary<string, double>(StringComparer.Ordinal);
        double Solve(int position)
        {
            if (position >= maxPosition) return 1;
            string key = string.Join(',', remaining);
            if (memo.TryGetValue(key, out double cached)) return cached;
            evaluated++;
            int total = pool.Count - position;
            if (total == 0) return 1;
            double probability = 0;
            for (int category = 0; category < classes.Length; category++)
            {
                int count = remaining[category];
                if (count == 0) continue;
                remaining[category]--;
                if (Accept(position + 1, category)) probability += count / (double)total * Solve(position + 1);
                remaining[category]++;
            }
            return memo[key] = Math.Clamp(probability, 0, 1);
        }
        result = new(Solve(0), pool.Count, relevantCount, evaluated,
            $"ExactFinitePermutationEquivalentClasses;pool={pool.Count};classes={classes.Length};maxPosition={maxPosition}");
        return true;
    }
}
