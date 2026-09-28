
namespace RolltheSpire2.Search.FamilyExecution;

// R-owned filtered back-prefix realization. Every source entry participates in
// shuffle; shop-ineligible entries are skipped only when observing finalized tails.
internal sealed class RelicShopCpuPlan
{
    private readonly RelicFamilyPlan _plan;
    private readonly RelicCpuPlan? _ordinary;
    private readonly int _bucket, _depth;
    private readonly RelicFamilyPredicate[] _predicates;
    private RelicShopCpuPlan(RelicFamilyPlan plan, int bucket, RelicCpuPlan? ordinary)
    { _plan = plan; _bucket = bucket; _ordinary = ordinary; _depth = Math.Max(plan.PositiveDepthByLane[3], plan.ExclusionDepthByLane[3]);
        _predicates = plan.Predicates.Where(p => p.Lane == 3).ToArray(); }

    internal static RelicShopCpuPlan? TryCompile(RelicFamilyPlan plan)
    {
        if (plan.ShopPredicates.Length == 0 && !plan.Predicates.Any(p => p.Lane == 3)) return null;
        int bucket = Array.FindIndex(plan.Pool.BucketKinds, kind => kind == 4);
        for (int b = 0; b < plan.Pool.BucketCount; b++)
            if (plan.Pool.BucketScopes[b] == 1 && plan.Pool.BucketKinds[b] == 4) bucket = b;
        if (bucket < 0 || plan.Pool.BucketScopes[bucket] != 1) return null;
        RelicCpuPlan? ordinary = null;
        if (plan.Predicates.Any(p => p.Lane != 3))
        {
            int last = 0;
            for (int b = 0; b < plan.Pool.BucketCount; b++)
                if (plan.Pool.BucketScopes[b] == 1 && plan.Pool.BucketKinds[b] is >= 1 and <= 3 &&
                    plan.TrackedCountsByLane[plan.Pool.BucketKinds[b] - 1] > 0) last = b;
            ordinary = RelicCpuPlan.TryCompile(plan with { Predicates = plan.Predicates.Where(p => p.Lane != 3).ToArray(),
                ShopPredicates = [], LastRequiredBucket = last });
        }
        return new(plan, bucket, ordinary);
    }

    internal bool Matches(ulong root)
    {
        if (_plan.AlwaysReject || (_ordinary is not null && !_ordinary.Matches(root))) return false;
        var pool = _plan.Pool;
        var rng = new Beta110FastRng(unchecked(root + RelicCpuPlan.UpFrontHash));
        for (int b = 0; b < _bucket; b++) rng.ConsumeUnstableShuffle(pool.BucketLengths[b]);
        int length = pool.BucketLengths[_bucket], offset = pool.BucketOffsets[_bucket];
        Span<int> entries = stackalloc int[length];
        Span<int> observed = stackalloc int[_depth];
        for (int i = 0; i < length; i++) entries[i] = offset + i;
        int remaining = length, count = 0;
        while (remaining > 0 && count < _depth)
        {
            if (remaining > 1)
            {
                int selected = rng.NextInt(remaining), tail = remaining - 1;
                (entries[selected], entries[tail]) = (entries[tail], entries[selected]);
            }
            int entry = entries[--remaining];
            if ((pool.EntryFlags[entry] & 1) == 0) continue;
            int actual = pool.DenseRelicIds[entry];
            observed[count++] = actual;
            foreach (var p in _predicates)
            {
                if (count != p.RangeValue) continue;
                var range = p.RangeMode == 0 ? observed[..count] : observed.Slice(count - 1, 1);
                bool any = p.AnyCount == 0;
                for (int i = 0; i < p.AnyCount; i++) any |= range.Contains((int)_plan.PredicateTargetIndexes[p.AnyOffset + i]);
                if (!any) return false;
                for (int i = 0; i < p.AllCount; i++) if (!range.Contains((int)_plan.PredicateTargetIndexes[p.AllOffset + i])) return false;
                for (int i = 0; i < p.BanCount; i++) if (range.Contains((int)_plan.PredicateTargetIndexes[p.BanOffset + i])) return false;
            }
            foreach (var p in _plan.ShopPredicates)
            {
                if (p.OrderMode == 0 && count <= p.Count && p.TargetIds[count - 1] != ushort.MaxValue && p.TargetIds[count - 1] != actual) return false;
                if (p.OrderMode != 0 && count == p.Count)
                    foreach (ushort target in p.TargetIds)
                    {
                        if (target == ushort.MaxValue) continue;
                        int required = 0, found = 0;
                        foreach (ushort id in p.TargetIds) if (id == target) required++;
                        foreach (int id in observed[..count]) if (id == target) found++;
                        if (found < required) return false;
                    }
            }
        }
        return count == _depth;
    }
}
