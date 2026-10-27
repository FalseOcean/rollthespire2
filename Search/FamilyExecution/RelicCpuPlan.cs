using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

// R-owned ordinary identity/rank filter. Reuses immutable bag facts, not GPU
// execution or the reference prediction document. Shop is a pre-execution alternative.
internal sealed class RelicCpuPlan
{
    internal static readonly ulong UpFrontHash = XxHash64.Hash("up_front"u8, 0);
    private readonly RelicFamilyPlan _plan;
    private readonly record struct RequiredPositionRange(int Minimum, int Maximum);
    private readonly RequiredPositionRange[]?[] _requiredRanges = new RequiredPositionRange[3][];
    private RelicCpuPlan(RelicFamilyPlan plan)
    {
        _plan = plan;
        foreach (var predicate in plan.Predicates)
        {
            if (predicate.AllCount == 0) continue;
            var ranges = _requiredRanges[predicate.Lane] ??= Enumerable.Repeat(
                new RequiredPositionRange(0, int.MaxValue), plan.TrackedCountsByLane[predicate.Lane]).ToArray();
            for (int i = 0; i < predicate.AllCount; i++)
            {
                int target = plan.PredicateTargetIndexes[predicate.AllOffset + i];
                var previous = ranges[target];
                ranges[target] = new(Math.Max(previous.Minimum, predicate.RangeMode == 0 ? 0 : predicate.RangeValue - 1),
                    Math.Min(previous.Maximum, predicate.RangeValue - 1));
            }
        }
    }
    internal static RelicCpuPlan? TryCompile(RelicFamilyPlan plan) =>
        plan.ShopPredicates.Length != 0 || plan.Predicates.Any(p => p.Lane == 3) ? null : new(plan);

    internal int NextBucket => _plan.LastRequiredBucket + 1;
    internal bool Matches(ulong root) => Matches(root, out _);

    // The continuation is usable only on success: rejected shuffles may stop
    // before completing a bucket. No RNG state is retained on the plan.
    internal bool Matches(ulong root, out Beta110FastRng continuation)
    {
        continuation = default;
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
            if (_requiredRanges[lane] is { } requiredRanges)
            {
                if (!ShuffleRequiredPositions(ref rng, length, positions[..count], requiredRanges)) return false;
            }
            else ShufflePositions(ref rng, length, positions[..count]);
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
        continuation = rng;
        return true;
    }

    private static bool InRange(int position, RelicFamilyPredicate p) =>
        p.RangeMode == 0 ? position < p.RangeValue : position + 1 == p.RangeValue;

    private static bool ShuffleRequiredPositions(ref Beta110FastRng rng, int length, Span<int> positions,
        ReadOnlySpan<RequiredPositionRange> requiredRanges)
    {
        for (int count = length; count > 1; count--)
        {
            int selected = rng.NextInt(count), tail = count - 1;
            for (int i = 0; i < positions.Length; i++)
            {
                int position = positions[i];
                if (position == selected)
                {
                    positions[i] = tail;
                    // The shuffled tail never moves again. A required target
                    // outside any authored All range cannot recover later.
                    var required = requiredRanges[i];
                    if (tail < required.Minimum || tail > required.Maximum) return false;
                }
                else if (position == tail) positions[i] = selected;
            }
        }
        return true;
    }

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
