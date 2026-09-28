using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.FamilyExecution;

// R-owned single initial-bag replay. Route branches have only private consumption
// offsets; N never receives any of these arrays, positions or RNG values.
internal sealed class CapsuleRelicReplay
{
    internal readonly RelicFamilyPool Pool;
    private readonly ModelKey[] _keys;
    private readonly ExactSearchEvaluationProjection _evaluation;
    private readonly NeowReplayPlan _route;
    private readonly HashSet<ModelKey> _unresolvedObtains;
    internal CapsuleRelicReplay(ExactSearchExecutionRequest request, NeowReplayPlan route)
    {
        if (!RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var dense, out _, out string issue))
            throw new InvalidOperationException(issue);
        Pool = pool; _route = route; _evaluation = request.Evaluation;
        _unresolvedObtains = request.Authority.PlayersCount == 1 ? [] : PartyInitialQuery.CapsuleEffectPremise(
            request.CompiledSearch.NormalizedQuery).Values.SelectMany(keys => keys).Where(k =>
                !Core.Rewards.VanillaRelicRewardEffects.TryGet(request.ProfileId, k, out var e) ||
                !e.NestedOnObtainPreservesRewardContinuation).ToHashSet();
        _keys = new ModelKey[dense.Count];
        foreach (var pair in dense) _keys[pair.Value] = pair.Key;
    }

    internal bool Matches(ulong root)
    {
        var rng = new Beta110FastRng(unchecked(root + XxHash64.Hash("up_front"u8, 0)));
        var lanes = new ModelKey[4][];
        // Adopt the runtime pool compiler and canonical shuffle/draw direction.
        // Ordinary and Capsule predicates both inspect these same initial lanes.
        for (int bucket = 0; bucket < Pool.BucketCount; bucket++)
        {
            int length = Pool.BucketLengths[bucket], offset = Pool.BucketOffsets[bucket];
            if (Pool.BucketScopes[bucket] == 0) { rng.ConsumeUnstableShuffle(length); continue; }
            int[] entries = Enumerable.Range(offset, length).ToArray();
            rng.UnstableShuffle(entries.AsSpan());
            int lane = Pool.BucketKinds[bucket] - 1;
            if (lane is < 0 or > 3) continue;
            IEnumerable<int> effective = entries;
            if (lane == 3) effective = effective.Where(index => Pool.EntryFlags[index] != 0).Reverse();
            lanes[lane] = effective.Select(index => _keys[Pool.DenseRelicIds[index]]).ToArray();
        }
        foreach (var condition in _evaluation.RelicSequenceConditions.Where(c => !c.IsEmpty))
        {
            ModelKey[] lane = lanes[Lane(condition.Lane)];
            if (condition.RangeValue > lane.Length) throw new InvalidOperationException("RFamilyRelicRangeUnavailable");
            IEnumerable<ModelKey> observed = condition.RangeMode == SearchSequenceRangeMode.FirstN
                ? lane.Take(condition.RangeValue) : lane.Skip(condition.RangeValue - 1).Take(1);
            if (!QueryKeySetPredicate.MatchesKeySet(observed, condition.Keys)) return false;
        }
        foreach (var condition in _evaluation.RelicShopSequenceConditions.Where(c => !c.IsEmpty))
        {
            ModelKey[] observed = lanes[3].Take(condition.Count).ToArray();
            if (observed.Length != condition.Count) throw new InvalidOperationException("RFamilyShopRangeUnavailable");
            if (condition.OrderMode == CombatRewardSequenceOrderMode.Ordered)
            {
                for (int i = 0; i < condition.Count; i++)
                    if (condition.Slots[i] is ModelKey key && observed[i] != key) return false;
            }
            else foreach (var group in condition.Slots.Where(k => k.HasValue).Select(k => k!.Value).GroupBy(k => k))
                if (observed.Count(k => k == group.Key) < group.Count()) return false;
        }
        NeowReplayObservation observation = NeowFamilyReplay.Observe(root, _route);
        // Absence means no real nested observation. These are prerequisites, not
        // additional R-owned Bones/Neow filter rows.
        if (!observation.IdentityPass) return false;
        if (_route.Bones && !NeowLocalOperators.RequiredStructuredSourcesPresent(_route.StructuredConditions,
                observation.BonesFirst, observation.BonesSecond)) return false;
        bool keep = false;
        foreach (var order in NeowFamilyReplay.Routes(_route, observation))
            keep |= Route(root, observation, order.First, order.Second, lanes);
        return keep;
    }

    private bool Route(ulong root, NeowReplayObservation observation, byte first, byte second, ModelKey[][] lanes)
    {
        var rewards = _route.Bones ? observation.RewardsAfterBones : new Beta110FastRng(unchecked(root +
            (ulong)_route.Authority.PlayerSlotIndex + NeowFamilyReplay.RewardsHash));
        var niche = new Beta110FastRng(unchecked(root + XxHash64.Hash("niche"u8, 0)));
        var transformations = new Beta110FastRng(unchecked(root + (ulong)_route.Authority.PlayerSlotIndex + XxHash64.Hash("transformations"u8, 0)));
        var potions = new Beta110FastRng(unchecked(root + XxHash64.Hash("combat_potion_generation"u8, 0)));
        var consumed = new int[3];
        var outputs = new List<ModelKey>(4);
        var bySource = new Dictionary<byte, ModelKey[]>();
        foreach (byte relic in new[] { first, second })
        {
            if (relic == Beta110FastRelicCatalog.InvalidId) continue;
            if (relic is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule)
            {
                int count = relic == Beta110FastRelicCatalog.LargeCapsule ? 2 : 1;
                var pulled = new ModelKey[count];
                for (int i = 0; i < count; i++)
                {
                    float roll = rewards.NextFloat();
                    int lane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
                    while (lane < 3 && consumed[lane] >= lanes[lane].Length) lane++;
                    pulled[i] = lane < 3 ? lanes[lane][consumed[lane]++] : _route.Authority.EffectCatalog.KeyOf(_route.Authority.EffectCatalog.CapsuleCircletId);
                }
                if (pulled.Any(_unresolvedObtains.Contains)) return true;
                outputs.AddRange(pulled); bySource[relic] = pulled;
            }
            else
            {
                // Rewards continuation donor, with no local output predicates.
                if (!NeowLocalOperators.ExecuteRelic(relic, true, _route, _route.Authority.EffectCatalog, true, true,
                    false, false, ref rewards, ref niche, ref transformations, ref potions, Span<byte>.Empty))
                    throw new InvalidOperationException("RFamilyCapsuleContinuationUnavailable");
            }
        }
        foreach (var condition in _route.Filter.StructuredNeowEffects)
        {
            if (NeowReplayPlan.IsGroupedCapsule(condition))
            {
                if (outputs.Count != 3 || !MultisetContains(outputs, condition.OutputKeys)) return false;
                continue;
            }
            if (!Beta110FastRelicCatalog.TryGetId(condition.SourceRelicKey, out byte source) || !bySource.TryGetValue(source, out var actual)) return false;
            foreach (var group in condition.OutputKeys.GroupBy(k => k))
                if (actual.Count(k => k == group.Key) < group.Count()) return false;
        }
        if (!QueryKeySetPredicate.MatchesKeySet(outputs, _evaluation.CapsuleContainedRelics)) return false;
        if (_evaluation.RequireWhetstone && !outputs.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone)) return false;
        if (_evaluation.RequireWarPaint && !outputs.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint)) return false;
        return true;
    }
    private static bool MultisetContains(IReadOnlyList<ModelKey> actual, IReadOnlyList<ModelKey> expected)
    {
        if (expected.Count is < 1 or > 3 || actual.Count < expected.Count) return false;
        var remaining = actual.GroupBy(key => key, ModelKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.Count(), ModelKeyComparer.Instance);
        foreach (ModelKey key in expected)
        {
            if (!remaining.TryGetValue(key, out int count) || count == 0) return false;
            remaining[key] = count - 1;
        }
        return true;
    }
    private static int Lane(RelicSequenceKind kind) => kind switch
    { RelicSequenceKind.Common => 0, RelicSequenceKind.Uncommon => 1, RelicSequenceKind.Rare => 2, RelicSequenceKind.Shop => 3, _ => throw new InvalidOperationException("RFamilyUnknownLane") };
}
