using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// R-owned Capsule rarity/rank predicate. Source partitions remain distinct.
// Only target positions are replayed; no complete bag or result objects exist per root.
internal sealed class CapsuleRelicCpuPlan
{
    private readonly record struct Target(int Bucket, int Initial, int Lane);
    private sealed record Requirement(byte Source, bool Grouped, int[] Targets);
    private readonly NeowReplayPlan _route;
    private readonly RelicFamilyPool _pool;
    private readonly Target[] _targets;
    private readonly Requirement[] _requirements;
    private readonly RelicCpuPlan? _ordinary;
    private readonly int _lastBucket;
    private CapsuleRelicCpuPlan(NeowReplayPlan route, RelicFamilyPool pool, Target[] targets,
        Requirement[] requirements, RelicCpuPlan? ordinary)
    { _route = route; _pool = pool; _targets = targets; _requirements = requirements; _ordinary = ordinary; _lastBucket = targets.Max(t => t.Bucket); }

    internal static CapsuleRelicCpuPlan? TryCompile(ExactSearchExecutionRequest request, NeowReplayPlan route)
    {
        var e = request.Evaluation;
        if (route.ExactOnly.Length != 0 || !route.DirectNestedVanilla111 ||
            !e.CapsuleContainedRelics.IsEmpty || e.RequireWhetstone || e.RequireWarPaint) return null;
        if (!route.Bones)
        { if (!IsCapsule(route.Selected)) return null; }
        else
        {
            var pair = route.Filter.RequiredBonesAcquisitionOrder.Count == 2
                ? route.Filter.RequiredBonesAcquisitionOrder : route.Filter.RequiredBonesCombination;
            if (pair.Count != 2 || !pair.ToHashSet().SetEquals(new[] { BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule })) return null;
        }
        if (!RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var dense, out _, out _)) return null;
        // Up to three draws cannot exhaust an admitted lane; unusual tiny/modded
        // pools stay on the reference realization before execution.
        for (int b = 0; b < pool.BucketCount; b++)
            if (pool.BucketScopes[b] == 1 && pool.BucketKinds[b] is >= 1 and <= 3 && pool.BucketLengths[b] < 3) return null;
        RelicCpuPlan? ordinary = null;
        if (e.RelicSequenceConditions.Any(c => !c.IsEmpty) || e.RelicShopSequenceConditions.Any(c => !c.IsEmpty))
        {
            if (!RelicFamilyPlanCompiler.TryCompile(request, out var p, out _) || (ordinary = RelicCpuPlan.TryCompile(p!)) is null) return null;
        }
        var conditions = route.Filter.StructuredNeowEffects.ToArray();
        var keys = conditions.SelectMany(c => c.OutputKeys).Distinct().ToArray();
        if (keys.Length is < 1 or > 64) return null;
        var targets = new Target[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            if (!dense.TryGetValue(keys[i], out ushort id)) return null;
            int found = 0;
            for (int b = 0; b < pool.BucketCount; b++)
            {
                if (pool.BucketScopes[b] != 1 || pool.BucketKinds[b] is < 1 or > 3) continue;
                int at = Array.IndexOf(pool.DenseRelicIds, id, pool.BucketOffsets[b], pool.BucketLengths[b]);
                if (at < 0) continue;
                targets[i] = new(b, at - pool.BucketOffsets[b], pool.BucketKinds[b] - 1); found++;
            }
            if (found != 1) return null;
        }
        var requirements = new List<Requirement>();
        foreach (var c in conditions)
        {
            bool grouped = NeowReplayPlan.IsGroupedCapsule(c);
            byte source = Beta110FastRelicCatalog.InvalidId;
            if (!grouped && (!Beta110FastRelicCatalog.TryGetId(c.SourceRelicKey, out source) || !IsCapsule(source))) return null;
            requirements.Add(new(source, grouped, c.OutputKeys.Select(k => Array.IndexOf(keys, k)).ToArray()));
        }
        return new(route, pool, targets, requirements.ToArray(), ordinary);
    }

    internal string? DirectPricingBand => _route.Bones || _ordinary is not null || _requirements.Length != 1 || _requirements[0].Grouped || _requirements[0].Targets.Length != _targets.Length ||
        !_pool.BucketLengths.SequenceEqual(new[]{30,25,35,25,1,2,32,26,38,26}) ? null :
        _route.Selected == Beta110FastRelicCatalog.SmallCapsule && _targets.Length == 1 && _targets[0].Lane == 0 ? "SmallC" :
        _route.Selected == Beta110FastRelicCatalog.LargeCapsule && _targets.Length == 2 && _targets.Select(t=>t.Lane).Order().SequenceEqual(new[]{1,2}) ? "LargeUR" : null;
    internal bool Matches(ulong root)
    {
        var observation = NeowFamilyReplay.Observe(root, _route);
        if (!observation.IdentityPass) return false;
        Span<int> draws = stackalloc int[6];
        Span<byte> sources = stackalloc byte[6];
        byte first = _route.Bones ? observation.BonesFirst : _route.Selected;
        byte second = _route.Bones ? observation.BonesSecond : Beta110FastRelicCatalog.InvalidId;
        if (_route.Bones && (!IsCapsule(first) || !IsCapsule(second))) return false;
        bool bothOrders = _route.Bones && _route.First == Beta110FastRelicCatalog.InvalidId;
        if (_route.Bones && !bothOrders) { first = _route.First; second = _route.Second; }
        int count = Draw(root, observation, first, second, draws[..3], sources[..3]);
        int count2 = bothOrders ? Draw(root, observation, second, first, draws[3..], sources[3..]) : 0;
        // Rarity-only precheck avoids bag replay for impossible source/rarity shapes.
        bool a = Accept(draws[..count], sources[..count], default, true);
        bool b = count2 != 0 && Accept(draws.Slice(3, count2), sources.Slice(3, count2), default, true);
        if (!a && !b) return false;
        if (_ordinary is not null && !_ordinary.Matches(root)) return false;
        Span<int> final = stackalloc int[_targets.Length];
        Span<int> positions = stackalloc int[_targets.Length];
        Span<int> indexes = stackalloc int[_targets.Length];
        var rng = new Beta110FastRng(unchecked(root + RelicCpuPlan.UpFrontHash));
        for (int bucket = 0; bucket <= _lastBucket; bucket++)
        {
            int n = 0;
            for (int i = 0; i < _targets.Length; i++)
                if (_targets[i].Bucket == bucket) { positions[n] = _targets[i].Initial; indexes[n++] = i; }
            if (n == 0) { rng.ConsumeUnstableShuffle(_pool.BucketLengths[bucket]); continue; }
            RelicCpuPlan.ShufflePositions(ref rng, _pool.BucketLengths[bucket], positions[..n]);
            for (int i = 0; i < n; i++) final[indexes[i]] = positions[i];
        }
        return (a && Accept(draws[..count], sources[..count], final, false)) ||
            (b && Accept(draws.Slice(3, count2), sources.Slice(3, count2), final, false));
    }

    private int Draw(ulong root, NeowReplayObservation observation, byte first, byte second, Span<int> draws, Span<byte> sources)
    {
        var rng = _route.Bones ? observation.RewardsAfterBones : new Beta110FastRng(unchecked(root +
            (ulong)_route.Authority.PlayerSlotIndex + NeowFamilyReplay.RewardsHash));
        Span<int> consumed = stackalloc int[3]; consumed.Clear();
        int output = 0;
        for (int r = 0; r < 2; r++)
        {
            byte source = r == 0 ? first : second;
            if (source == Beta110FastRelicCatalog.InvalidId) continue;
            if (!IsCapsule(source)) throw new InvalidOperationException("CpuCapsuleSelectedRouteUnsupported");
            int count = source == Beta110FastRelicCatalog.SmallCapsule ? 1 : 2;
            for (int i = 0; i < count; i++)
            {
                float roll = rng.NextFloat();
                int lane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
                draws[output] = lane * 4096 + consumed[lane]++;
                sources[output++] = source;
            }
        }
        return output;
    }

    private bool Accept(ReadOnlySpan<int> draws, ReadOnlySpan<byte> sources, ReadOnlySpan<int> positions, bool rarityOnly)
    {
        foreach (var requirement in _requirements)
        {
            if (requirement.Grouped && draws.Length != 3) return false;
            foreach (int target in requirement.Targets)
            {
                int required = 0, actual = 0;
                foreach (int other in requirement.Targets)
                    if (rarityOnly ? _targets[other].Lane == _targets[target].Lane : other == target) required++;
                for (int i = 0; i < draws.Length; i++)
                    if ((requirement.Grouped || sources[i] == requirement.Source) && draws[i] / 4096 == _targets[target].Lane &&
                        (rarityOnly || draws[i] % 4096 == positions[target])) actual++;
                if (actual < required) return false;
            }
        }
        return true;
    }

    private static bool IsCapsule(byte id) => id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule;
}
