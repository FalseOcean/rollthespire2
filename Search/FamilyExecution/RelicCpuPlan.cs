using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

// R-owned ordinary identity/rank filter. Reuses immutable bag facts, not GPU
// execution or the reference prediction document. Shop is a pre-execution alternative.
internal sealed class RelicCpuPlan
{
    internal static readonly ulong UpFrontHash = XxHash64.Hash("up_front"u8, 0);
    private readonly RelicFamilyPlan _plan;
    private RelicCpuPlan(RelicFamilyPlan plan) => _plan = plan;
    internal static RelicCpuPlan? TryCompile(RelicFamilyPlan plan) =>
        plan.ShopPredicates.Length != 0 || plan.Predicates.Any(p => p.Lane == 3) ? null : new(plan);

    internal bool Matches(ulong root)
    {
        if (_plan.AlwaysReject) return false;
        var rng = new Beta110FastRng(unchecked(root + UpFrontHash));
        Span<int> positions = stackalloc int[_plan.LocalStateCapacity];
        var pool = _plan.Pool;
        for (int bucket = 0; bucket <= _plan.LastRequiredBucket; bucket++)
        {
            int length = pool.BucketLengths[bucket], lane = pool.BucketKinds[bucket] - 1;
            if (pool.BucketScopes[bucket] == 0 || lane is < 0 or > 2 || _plan.TrackedCountsByLane[lane] == 0)
            { rng.ConsumeUnstableShuffle(length); continue; }
            int count = _plan.TrackedCountsByLane[lane], offset = _plan.TrackedOffsetsByLane[lane];
            for (int i = 0; i < count; i++) positions[i] = _plan.TrackedInitialPositions[offset + i];
            ShufflePositions(ref rng, length, positions[..count]);
            foreach (var p in _plan.Predicates)
            {
                if (p.Lane != lane) continue;
                bool any = p.AnyCount == 0;
                for (int i = 0; i < p.AnyCount; i++)
                    any |= InRange(positions[_plan.PredicateTargetIndexes[p.AnyOffset + i]], p);
                if (!any) return false;
                for (int i = 0; i < p.AllCount; i++)
                    if (!InRange(positions[_plan.PredicateTargetIndexes[p.AllOffset + i]], p)) return false;
                for (int i = 0; i < p.BanCount; i++)
                    if (InRange(positions[_plan.PredicateTargetIndexes[p.BanOffset + i]], p)) return false;
            }
        }
        return true;
    }

    private static bool InRange(int position, RelicFamilyPredicate p) =>
        p.RangeMode == 0 ? position < p.RangeValue : position + 1 == p.RangeValue;

    internal static void ShufflePositions(ref Beta110FastRng rng, int length, Span<int> positions)
    {
        for (int count = length; count > 1; count--)
        {
            int selected = rng.NextInt(count), tail = count - 1;
            for (int i = 0; i < positions.Length; i++)
            {
                int position = positions[i];
                if (position == selected) positions[i] = tail;
                else if (position == tail) positions[i] = selected;
            }
        }
    }
}
