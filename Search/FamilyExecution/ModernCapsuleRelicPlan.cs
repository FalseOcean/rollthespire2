using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// R observes bounded heads of the eligible personal lanes. The initial R
// sequence remains a separate observation of the same raw bag.
internal sealed partial class ModernCapsuleRelicPlan
{
    private const byte Small = Beta110FastRelicCatalog.SmallCapsule;
    private const byte Large = Beta110FastRelicCatalog.LargeCapsule;
    private const byte NoSource = Beta110FastRelicCatalog.InvalidId;
    private const ushort Circlet = ushort.MaxValue;
    private const ushort Absent = ushort.MaxValue - 2;

    private readonly record struct Lane(int Bucket, ushort[] Entries, int EligibleCount);
    private readonly record struct Row(byte Source, ushort[] Targets);

    private readonly NeowReplayPlan _route;
    internal RelicFamilyPool Pool { get; }
    private readonly Lane[] _lanes;
    private readonly Row[] _rows;
    private readonly ushort[] _any, _all, _ban;
    private readonly ushort _ineligible;
    private readonly ushort[] _uncertainObtains;
    private readonly ushort[] _nonCombatObtains;
    private readonly int[] _companionRewardsDraws;
    private readonly int _maximumDraws;
    private readonly bool _unresolvedRoutes;
    private readonly int _maxBucketLength;
    private readonly int _lastBucket;

    private ModernCapsuleRelicPlan(NeowReplayPlan route, RelicFamilyPool pool, Lane[] lanes, Row[] rows, ushort[] any, ushort[] all,
        ushort[] ban, ushort ineligible, ushort[] uncertainObtains, ushort[] nonCombatObtains, int[] companionRewardsDraws, int maximumDraws,
        bool unresolvedRoutes)
    {
        _route = route;
        Pool = pool;
        _lanes = lanes;
        _rows = rows;
        _any = any;
        _all = all;
        _ban = ban;
        _ineligible = ineligible;
        _uncertainObtains = uncertainObtains;
        _nonCombatObtains = nonCombatObtains;
        _companionRewardsDraws = companionRewardsDraws;
        _maximumDraws = maximumDraws;
        _unresolvedRoutes = unresolvedRoutes;
        _maxBucketLength = lanes.Max(lane => lane.Entries.Length);
        _lastBucket = lanes.Max(lane => lane.Bucket);
    }

    internal bool HasUnresolvedPickup => _unresolvedRoutes || _maximumDraws > 1 && _uncertainObtains.Length != 0;

    internal static ModernCapsuleRelicPlan? TryCompile(SearchModel request, NeowReplayPlan route)
    {
        var authority = request.Authority;
        if (authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113 ||
            !authority.IsVanilla || authority.UsesBestEffortModel || authority.NoRunModifiers != true ||
            authority.WorldAuthority is not { SourceAuthority: SourceAuthority.OfficialRuntimeExact or SourceAuthority.AuditedStaticExact,
                Beta109Generation.NoUnknownHooksOrModifiers: true } ||
            authority.EffectAuthority is not { HasExactRelicBagSourcePools: true } effects ||
            request.Evaluation.EffectOutputConditions.Any(condition =>
                !condition.IsEmpty && NeowReplayPlan.IsCapsule(condition.SourceRelicKey)))
            return null;

        int[] companionRewardsDraws = Enumerable.Repeat(-1, 30).ToArray();
        foreach (byte source in route.Authority.BonesEligibleRelicIds)
            if (source < companionRewardsDraws.Length && DrawCount(source) == 0 &&
                RelicOpeningPrefixPlan.TryProveCompanionRewardsDraws(route, source, out int draws))
                companionRewardsDraws[source] = draws;
        int maximumDraws = DrawCount(route.Selected);
        bool unresolvedRoutes = false;
        if (route.Bones)
        {
            bool observesRoute = false;
            byte[] pairSources = route.Authority.BonesEligibleRelicIds;
            foreach (byte first in pairSources)
            foreach (byte second in pairSources)
            {
                if (first == second || !MayObservePair(route, first, second)) continue;
                int draws = DrawCount(first) + DrawCount(second);
                if (draws == 0) continue;
                maximumDraws = Math.Max(maximumDraws, draws);
                if (DrawCount(first) == 0 && CompanionsUnknown(companionRewardsDraws, first))
                    unresolvedRoutes = true;
                else observesRoute = true;
            }
            if (!observesRoute) return null;
        }
        if (maximumDraws == 0 || !RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var dense, out _, out _, requireOrdinaryLanes: true) ||
            !pool.UsesChildShuffle) return null;

        // Compare the raw bucket sources also used by Production Exact. Solo
        // Exact reconstructs from the two ordered model catalogs; Party
        // obtains its ordering from the same per-seat bucket snapshots.
        var snapshots = effects.SharedRelicPoolSource!.Concat(effects.CharacterRelicPoolSource!)
            .DistinctBy(relic => relic.RelicKey, ModelKeyComparer.Instance)
            .ToDictionary(relic => relic.RelicKey, ModelKeyComparer.Instance);
        if (authority.PlayersCount == 1 && !MatchesSoloSources(pool, dense, effects)) return null;
        if (dense.Count > Absent) return null; // Reserve separate absent/unobserved IDs.

        ushort ineligible = Absent;
        if (dense.TryGetValue(StandardOpeningRelicEligibility.LastingCandy, out ushort candy))
        {
            bool? allowed = StandardOpeningRelicEligibility.AllowsLastingCandy(authority);
            if (!allowed.HasValue) return null;
            if (!allowed.Value) ineligible = candy;
        }

        var keys = new ModelKey[dense.Count];
        foreach (var (key, id) in dense) keys[id] = key;
        var lanes = new Lane[3];
        var uncertain = new HashSet<ushort>();
        var nonCombat = new HashSet<ushort>();
        for (int bucket = 0; bucket < pool.BucketCount; bucket++)
        {
            int kind = pool.BucketKinds[bucket];
            if (pool.BucketScopes[bucket] == 0 || kind is < 1 or > 3) continue;
            ushort[] entries = pool.DenseRelicIds.AsSpan(pool.BucketOffsets[bucket], pool.BucketLengths[bucket]).ToArray();
            foreach (ushort id in entries)
            {
                if (id == ineligible) continue;
                if (!snapshots.TryGetValue(keys[id], out var snapshot) ||
                    snapshot.Rarity != (EffectRelicRarity)(kind - 1) ||
                    !string.Equals(snapshot.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(snapshot.SourceModId)) return null;
                int advance = NeowCapsuleNonCombatContinuation.NativeAdvance(snapshot);
                if (advance < 0) uncertain.Add(id);
                else if (advance > 0) nonCombat.Add(id);
            }
            lanes[kind - 1] = new(bucket, entries, entries.Count(id => id != ineligible));
        }

        ushort Target(ModelKey key) => key == BaseGameModelKeys.OrdinaryRelics.Circlet
            ? Circlet : dense.GetValueOrDefault(key, Absent);
        Row[] rows = route.Filter.StructuredNeowEffects.Select(condition => new Row(
            NeowReplayPlan.IsGroupedCapsule(condition) ? NoSource : condition.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule ? Small : Large,
            condition.OutputKeys.Select(Target).ToArray())).ToArray();
        var evaluation = request.Evaluation;
        var set = evaluation.CapsuleContainedRelics;
        var all = set.All.Concat(evaluation.RequireWhetstone ? [BaseGameModelKeys.OrdinaryRelics.Whetstone] : Array.Empty<ModelKey>())
            .Concat(evaluation.RequireWarPaint ? [BaseGameModelKeys.OrdinaryRelics.WarPaint] : Array.Empty<ModelKey>())
            .Select(Target).ToArray();
        if (rows.Length == 0 && set.IsEmpty && all.Length == 0) return null;
        return new(route, pool, lanes, rows, set.Any.Select(Target).ToArray(), all, set.Ban.Select(Target).ToArray(),
            ineligible, uncertain.ToArray(), nonCombat.ToArray(), companionRewardsDraws, maximumDraws, unresolvedRoutes);
    }

    private static bool MayObservePair(NeowReplayPlan route, byte first, byte second)
    {
        if (route.First != NoSource && route.Second != NoSource && (first != route.First || second != route.Second)) return false;
        ulong offered = Beta110FastRelicCatalog.Bit(first) | Beta110FastRelicCatalog.Bit(second);
        if ((offered & route.BonesAll) != route.BonesAll ||
            (offered & route.BonesBan) != 0 || route.BonesAny != 0 && (offered & route.BonesAny) == 0) return false;
        foreach (var condition in route.Filter.StructuredNeowEffects)
        {
            if (NeowReplayPlan.IsGroupedCapsule(condition))
            {
                if (DrawCount(first) + DrawCount(second) != 3) return false;
            }
            else if (condition.SourceRelicKey != Beta110FastRelicCatalog.KeyOf(first) &&
                     condition.SourceRelicKey != Beta110FastRelicCatalog.KeyOf(second)) return false;
        }
        return true;
    }

    private static bool CompanionsUnknown(int[] burns, byte source) => source >= burns.Length || burns[source] == -1;

    private static bool MatchesSoloSources(RelicFamilyPool pool, Dictionary<ModelKey, ushort> dense,
        NeowEffectAuthoritySnapshot effects)
    {
        var shared = effects.SharedRelicPoolSource!;
        var personal = shared.Concat(effects.CharacterRelicPoolSource!)
            .Where(relic => relic.Rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or
                EffectRelicRarity.Rare or EffectRelicRarity.Shop)
            .DistinctBy(relic => relic.RelicKey, ModelKeyComparer.Instance);
        return MatchesBuckets(shared, 0) && MatchesBuckets(personal, 1);

        bool MatchesBuckets(IEnumerable<NeowEffectRelicSnapshot> source, byte scope)
        {
            int[] buckets = Enumerable.Range(0, pool.BucketCount).Where(index => pool.BucketScopes[index] == scope).ToArray();
            var grouped = source.GroupBy(relic => string.IsNullOrWhiteSpace(relic.RarityCode)
                ? relic.Rarity.ToString() : relic.RarityCode, StringComparer.Ordinal).ToArray();
            return grouped.Length == buckets.Length && grouped.Select((group, index) =>
            {
                int bucket = buckets[index];
                return group.Select(relic => dense.GetValueOrDefault(relic.RelicKey, Absent))
                    .SequenceEqual(pool.DenseRelicIds.Skip(pool.BucketOffsets[bucket]).Take(pool.BucketLengths[bucket]));
            }).All(matches => matches);
        }
    }

    internal bool Matches(ulong root)
    {
        var observation = NeowFamilyReplay.Observe(root, _route);
        if (!observation.IdentityPass) return false;

        Span<Beta110FastRng> children = stackalloc Beta110FastRng[3];
        Span<bool> initialized = stackalloc bool[3];
        initialized.Clear();
        Span<ushort> heads = stackalloc ushort[9];
        bool ready = false;
        if (!_route.Bones) return MatchesRoute(root, observation.RewardsAfterBones, _route.Selected, NoSource,
            ref ready, children, initialized, heads);
        if (_route.First != NoSource && _route.Second != NoSource)
            return MatchesRoute(root, observation.RewardsAfterBones, _route.First, _route.Second,
                ref ready, children, initialized, heads);
        return MatchesRoute(root, observation.RewardsAfterBones, observation.BonesFirst, observation.BonesSecond,
                ref ready, children, initialized, heads) ||
            MatchesRoute(root, observation.RewardsAfterBones, observation.BonesSecond, observation.BonesFirst,
                ref ready, children, initialized, heads);
    }

    private bool MatchesRoute(ulong root, Beta110FastRng rewards, byte first, byte second, ref bool ready,
        Span<Beta110FastRng> children, Span<bool> initialized, Span<ushort> heads, NeowReplayPlan? local = null)
    {
        int firstCount = DrawCount(first), drawCount = firstCount + DrawCount(second);
        foreach (var row in _rows)
            if (row.Source == NoSource ? drawCount != 3 : row.Source != first && row.Source != second) return false;
        if (drawCount == 0) return MatchesRows([], first, second) &&
            (local is null || NeowSingleplayerReplay.Route(root, local, first, second, rewards));
        var arrival = rewards;
        bool testedFirst = local is not null && firstCount == 0;
        if (testedFirst && !MatchesFirstLocal(root, local!, first, arrival))
            return false;
        if (firstCount == 0)
        {
            if (CompanionsUnknown(_companionRewardsDraws, first)) return true;
            RelicOpeningPrefixPlan.AdvanceObservedRewards(_companionRewardsDraws[first], ref rewards);
        }
        if (!ready)
        {
            var parent = new Beta110FastRng(unchecked(root + RelicCpuPlan.UpFrontHash));
            for (int bucket = 0; bucket <= _lastBucket; bucket++)
            {
                if (bucket == _lanes[0].Bucket) children[0] = parent.ForkShuffle();
                else if (bucket == _lanes[1].Bucket) children[1] = parent.ForkShuffle();
                else if (bucket == _lanes[2].Bucket) children[2] = parent.ForkShuffle();
                else parent.ConsumeShuffleSeed();
            }
            ready = true;
        }
        Span<ushort> seen = stackalloc ushort[3];
        Span<int> consumed = stackalloc int[3];
        consumed.Clear();
        Span<ushort> scratch = stackalloc ushort[_maxBucketLength];
        int firstAdvance = 0, secondAdvance = 0;
        bool laterLocalUnknown = false;
        for (int draw = 0; draw < drawCount; draw++)
        {
            float roll = rewards.NextFloat();
            int lane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
            while (lane < 3 && consumed[lane] >= _lanes[lane].EligibleCount) lane++;
            ushort key = Circlet;
            if (lane < 3)
            {
                int head = lane * 3;
                if (!initialized[lane])
                {
                    Span<ushort> entries = scratch[.._lanes[lane].Entries.Length];
                    _lanes[lane].Entries.AsSpan().CopyTo(entries);
                    children[lane].UnstableShuffle(entries);
                    int count = 0;
                    foreach (ushort id in entries)
                        if (id != _ineligible)
                        {
                            heads[head + count++] = id;
                            if (count == _maximumDraws) break;
                        }
                    initialized[lane] = true;
                }
                key = heads[head + consumed[lane]++];
            }
            if (_uncertainObtains.AsSpan().Contains(key))
            {
                if (draw + 1 < drawCount) return true;
                laterLocalUnknown = local is not null && (local.HasFinalCurseFastProjection ||
                    firstCount != 0 && local.StructuredConditions.Any(condition => condition.SourceRelicId == second));
            }
            if (local is not null && _nonCombatObtains.AsSpan().Contains(key))
            {
                if (draw < firstCount) firstAdvance++;
                else secondAdvance++;
            }
            seen[draw] = key;
        }
        return MatchesRows(seen[..drawCount], first, second) && (local is null || laterLocalUnknown ||
            testedFirst && !local.HasFinalCurseFastProjection ||
            NeowSingleplayerReplay.RouteAtArrival(root, local, first, second, arrival, firstAdvance, secondAdvance));
    }

    private static bool MatchesFirstLocal(ulong root, NeowReplayPlan local, byte source, Beta110FastRng rewards)
    {
        var niche = default(Beta110FastRng);
        var transforms = new Beta110FastRng(unchecked(root + RolltheSpire2.Core.Seed.XxHash64.Hash("transformations"u8, 0)));
        var potions = default(Beta110FastRng);
        var nonCombat = NeowFamilyReplay.NonCombatRng(root, local);
        Span<byte> matched = stackalloc byte[local.StructuredConditions.Length];
        matched.Clear();
        if (!NeowSingleplayerOperators.ExecuteRelic(source, true, local, local.Authority.EffectCatalog,
            local.NeedsRewards, local.NeedsNicheOrKaleidoscopePools, local.NeedsTransformations, local.NeedsPotions,
            ref rewards, ref niche, ref transforms, ref potions, ref nonCombat, matched))
            throw new InvalidOperationException("ModernLocalNeow.CpuContinuationUnavailable");
        for (int i = 0; i < matched.Length; i++)
            if (local.StructuredConditions[i].SourceRelicId == source && matched[i] == 0)
                return false;
        return true;
    }

    internal bool MatchesLocal(ulong root, NeowReplayPlan local)
    {
        var observation = NeowSingleplayerReplay.Observe(root, local);
        if (!observation.IdentityPass || local.HasCapsuleRarityGate &&
            !NeowCapsuleRarityGate.Passes(observation.RewardsAfterBones)) return false;
        Span<Beta110FastRng> children = stackalloc Beta110FastRng[3];
        Span<bool> initialized = stackalloc bool[3];
        initialized.Clear();
        Span<ushort> heads = stackalloc ushort[9];
        bool ready = false;
        foreach (var (first, second) in NeowSingleplayerReplay.Routes(local, observation))
        {
            if (!MayObservePair(_route, first, second) ||
                !NeowLocalOperators.RequiredStructuredSourcesPresent(local.StructuredConditions, first, second)) continue;
            if (MatchesRoute(root, observation.RewardsAfterBones, first, second, ref ready, children, initialized, heads, local))
                return true;
        }
        return false;
    }

    internal bool LocalHasUnresolvedPickup => _unresolvedRoutes || _uncertainObtains.Length != 0;

    private bool MatchesRows(ReadOnlySpan<ushort> seen, byte first, byte second)
    {
        foreach (ushort target in _all) if (!seen.Contains(target)) return false;
        foreach (ushort target in _ban) if (seen.Contains(target)) return false;
        bool any = _any.Length == 0;
        foreach (ushort target in _any) any |= seen.Contains(target);
        if (!any) return false;

        int firstCount = DrawCount(first);
        foreach (var row in _rows)
        {
            if (row.Source == NoSource)
            {
                if (seen.Length != 3 || !ContainsMultiset(seen, row.Targets)) return false;
            }
            else if (row.Source == first)
            {
                if (!ContainsMultiset(seen[..firstCount], row.Targets)) return false;
            }
            else if (row.Source != second || !ContainsMultiset(seen[firstCount..], row.Targets)) return false;
        }
        return true;
    }

    private static bool ContainsMultiset(ReadOnlySpan<ushort> actual, ReadOnlySpan<ushort> targets)
    {
        int used = 0;
        foreach (ushort target in targets)
        {
            int index = 0;
            while (index < actual.Length && (actual[index] != target || (used & (1 << index)) != 0)) index++;
            if (index == actual.Length) return false;
            used |= 1 << index;
        }
        return true;
    }

    private static int DrawCount(byte source) => source == Small ? 1 : source == Large ? 2 : 0;
}
