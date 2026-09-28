using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record RelicFamilyPool(
    ushort[] DenseRelicIds,
    byte[] EntryFlags,
    int[] BucketOffsets,
    int[] BucketLengths,
    byte[] BucketScopes,
    byte[] BucketKinds,
    byte[] DrawDirections,
    int MaxBucketLength)
{
    public int BucketCount => BucketOffsets.Length;
}

internal readonly record struct RelicFamilyPredicate(
    byte Lane,
    byte RangeMode,
    byte RangeValue,
    int AnyOffset,
    ushort AnyCount,
    int AllOffset,
    ushort AllCount,
    int BanOffset,
    ushort BanCount);

internal readonly record struct RelicFamilyShopPredicate(byte Count, byte OrderMode, ushort[] TargetIds);

internal sealed record RelicFamilyPlan(
    RelicFamilyPool Pool,
    RelicFamilyPredicate[] Predicates,
    ushort[] PredicateTargetIndexes,
    RelicFamilyShopPredicate[] ShopPredicates,
    byte[] PositiveDepthByLane,
    byte[] ExclusionDepthByLane,
    ushort[] TrackedInitialPositions,
    int[] TrackedOffsetsByLane,
    byte[] TrackedCountsByLane,
    int LastRequiredBucket,
    bool AlwaysReject)
{
    public int PredicateCount => Predicates.Length + ShopPredicates.Length;
    internal int LocalStateCapacity => Math.Max(64, Math.Max(TrackedCountsByLane.Max(),
        Predicates.Any(p => p.Lane == 3) || ShopPredicates.Length > 0
            ? Enumerable.Range(0, Pool.BucketCount).Where(i => Pool.BucketScopes[i] == 1 && Pool.BucketKinds[i] == 4)
                .Select(i => Pool.BucketLengths[i]).DefaultIfEmpty(0).Max() : 0));
}

/// <summary>
/// Family-local immutable projection of the runtime Relic pool and active R predicates.
/// It migrates pool/bag knowledge only; no Fast plan, backend, continuation or topology
/// participates in Family execution.
/// </summary>
internal static class RelicFamilyPlanCompiler
{
    private const int LaneCount = 4;
    internal const int MaximumShaderLocalState = byte.MaxValue;
    private const int MaximumEntries = ushort.MaxValue - 1;
    private const int MaximumBuckets = 4096;
    private const int MaximumBucketLength = 4096;

    public static bool TryCompile(ExactSearchExecutionRequest request, out RelicFamilyPlan? plan, out string issue)
    {
        ArgumentNullException.ThrowIfNull(request);
        plan = null;
        issue = string.Empty;
        RelicSequenceSearchCondition[] laneConditions = request.Evaluation.RelicSequenceConditions
            .Where(condition => !condition.IsEmpty)
            .ToArray();
        RelicShopSequenceSearchCondition[] shopConditions = request.Evaluation.RelicShopSequenceConditions
            .Where(condition => !condition.IsEmpty)
            .ToArray();
        if (laneConditions.Length == 0 && shopConditions.Length == 0)
        {
            issue = "NoRelicFamilyPredicates";
            return false;
        }

        if (!TryCompilePool(request, out RelicFamilyPool pool, out Dictionary<ModelKey, ushort> denseByKey,
                out Dictionary<ushort, ushort>[] lanePositions, out issue))
            return false;

        var predicateIds = new List<ushort>();
        var predicates = new List<RelicFamilyPredicate>(laneConditions.Length);
        var shopPredicates = new List<RelicFamilyShopPredicate>(shopConditions.Length);
        var positiveDepth = new byte[LaneCount];
        var exclusionDepth = new byte[LaneCount];
        bool alwaysReject = false;

        foreach (RelicSequenceSearchCondition condition in laneConditions)
        {
            int lane = LaneIndex(condition.Lane);
            if (lane < 0 || condition.RangeValue is < 1 or > byte.MaxValue)
            {
                issue = "RelicFamilyPredicateInvalid";
                return false;
            }
            if (condition.RangeValue > EffectiveLaneCount(pool, lane))
            {
                issue = "RelicFamilyPredicateRangeUnavailable";
                return false;
            }

            int anyOffset = predicateIds.Count;
            ushort anyCount = AppendLaneKeys(condition.Keys.Any, lane, denseByKey, lanePositions, predicateIds,
                failIfMissing: false, out int originalAny, out _);
            int allOffset = predicateIds.Count;
            ushort allCount = AppendLaneKeys(condition.Keys.All, lane, denseByKey, lanePositions, predicateIds,
                failIfMissing: true, out int originalAll, out bool allMissing);
            int banOffset = predicateIds.Count;
            ushort banCount = AppendLaneKeys(condition.Keys.Ban, lane, denseByKey, lanePositions, predicateIds,
                failIfMissing: false, out int originalBan, out _);
            if (allMissing || (originalAny > 0 && anyCount == 0)) alwaysReject = true;

            byte depth = checked((byte)condition.RangeValue);
            if (anyCount > 0 || allCount > 0)
                positiveDepth[lane] = Math.Max(positiveDepth[lane], depth);
            if (banCount > 0)
                exclusionDepth[lane] = Math.Max(exclusionDepth[lane], depth);
            predicates.Add(new RelicFamilyPredicate(
                checked((byte)lane),
                condition.RangeMode == SearchSequenceRangeMode.FirstN ? (byte)0 : (byte)1,
                depth,
                anyOffset, anyCount, allOffset, allCount, banOffset, banCount));
        }

        foreach (RelicShopSequenceSearchCondition condition in shopConditions)
        {
            if (condition.Count is < 1 or > byte.MaxValue || condition.Slots.Count < condition.Count)
            {
                issue = "RelicFamilyShopPredicateInvalid";
                return false;
            }
            if (condition.Count > EffectiveLaneCount(pool, 3))
            {
                issue = "RelicFamilyShopRangeUnavailable";
                return false;
            }
            var targets = new ushort[condition.Count];
            for (int index = 0; index < condition.Count; index++)
            {
                ModelKey? target = condition.Slots[index];
                if (!target.HasValue)
                {
                    targets[index] = ushort.MaxValue;
                    continue;
                }
                if (!denseByKey.TryGetValue(target.Value, out ushort denseId) || !lanePositions[3].ContainsKey(denseId))
                {
                    alwaysReject = true;
                    targets[index] = ushort.MaxValue;
                    continue;
                }
                targets[index] = denseId;
            }
            positiveDepth[3] = Math.Max(positiveDepth[3], checked((byte)condition.Count));
            shopPredicates.Add(new RelicFamilyShopPredicate(
                checked((byte)condition.Count),
                condition.OrderMode == CombatRewardSequenceOrderMode.Ordered ? (byte)0 : (byte)1,
                targets));
        }

        int lastRequiredBucket = -1;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            if (positiveDepth[lane] == 0 && exclusionDepth[lane] == 0) continue;
            for (int bucket = 0; bucket < pool.BucketCount; bucket++)
            {
                if (pool.BucketScopes[bucket] == 1 && LaneIndex(pool.BucketKinds[bucket]) == lane)
                    lastRequiredBucket = Math.Max(lastRequiredBucket, bucket);
            }
        }
        if (lastRequiredBucket < 0 && !alwaysReject)
        {
            issue = "RelicFamilyObservationWindowEmpty";
            return false;
        }

        var trackedInitialPositions = new List<ushort>();
        var trackedOffsets = new int[LaneCount];
        var trackedCounts = new byte[LaneCount];
        for (int lane = 0; lane < LaneCount; lane++)
        {
            trackedOffsets[lane] = trackedInitialPositions.Count;
            ushort[] trackedIds = lane == 3
                ? []
                : predicates.Where(predicate => predicate.Lane == lane)
                    .SelectMany(predicate => EnumeratePredicateIds(predicate, predicateIds))
                    .Distinct().ToArray();
            if (trackedIds.Length > MaximumShaderLocalState ||
                (lane == 3 && (positiveDepth[lane] > 0 || exclusionDepth[lane] > 0) &&
                 EffectiveLaneBucketLength(pool, lane) > MaximumShaderLocalState))
            {
                issue = "RelicFamilyTrackedStateCapacityExceeded";
                return false;
            }

            var trackedIndexById = new Dictionary<ushort, ushort>();
            for (int index = 0; index < trackedIds.Length; index++)
            {
                ushort id = trackedIds[index];
                trackedIndexById.Add(id, checked((ushort)index));
                trackedInitialPositions.Add(lanePositions[lane][id]);
            }
            trackedCounts[lane] = checked((byte)trackedIds.Length);
            if (lane != 3)
                foreach (RelicFamilyPredicate predicate in predicates.Where(predicate => predicate.Lane == lane))
                    RewritePredicateIds(predicate, predicateIds, trackedIndexById);
        }

        plan = new RelicFamilyPlan(
            pool, predicates.ToArray(), predicateIds.ToArray(), shopPredicates.ToArray(),
            positiveDepth, exclusionDepth, trackedInitialPositions.ToArray(), trackedOffsets, trackedCounts,
            Math.Max(0, lastRequiredBucket), alwaysReject);
        return true;
    }

    internal static bool TryCompilePool(
        ExactSearchExecutionRequest request,
        out RelicFamilyPool pool,
        out Dictionary<ModelKey, ushort> denseByKey,
        out Dictionary<ushort, ushort>[] lanePositions,
        out string issue)
    {
        denseByKey = new Dictionary<ModelKey, ushort>(ModelKeyComparer.Instance);
        lanePositions = Enumerable.Range(0, LaneCount).Select(_ => new Dictionary<ushort, ushort>()).ToArray();
        WorldAuthoritySnapshot? world = request.Authority.WorldAuthority;
        Beta109WorldGenerationSnapshot? generation = world?.Beta109Generation;
        if (world is null || generation is null ||
            !RuntimeProfilePolicies.UsesBeta110SharedAlgorithms(request.ProfileId) ||
            world.CapturedProfileId != request.ProfileId || generation.Profile != request.ProfileId)
            return Missing("MissingModernRelicRuntimeSnapshot", out pool, out issue);

        bool sourceUsable = world.Completeness == SnapshotCompleteness.Complete &&
            world.SourceAuthority is SourceAuthority.OfficialRuntimeExact or
                SourceAuthority.AuditedStaticExact or SourceAuthority.ModdedRuntimeBestEffort;
        bool singleplayer = generation.ModeFactsExact && generation.GameMode == WorldGameMode.Singleplayer &&
                            generation.IsMultiplayerExact && !generation.IsMultiplayer && generation.PlayerCount == 1;
        bool seedBranch = generation.RunSeedHashKind == Beta109RunSeedHashKind.ModernXxHash64 &&
                          generation.OldSeedBranchStatus == Beta109OldSeedBranchStatus.NewSeedHashed;
        bool exactInputs = generation.RelicInitializationExact && generation.SharedRelicPoolOrderExact &&
                           generation.CharacterRelicPoolOrderExact && generation.RelicRarityAuthorityExact &&
                           generation.RelicShopEligibilityAuthorityExact && generation.PlayerRelicPoolCompositionExact &&
                           generation.SharedRelicBuckets.Count > 0 && generation.PlayerRelicBuckets.Count > 0 &&
                           generation.SharedRelicBuckets.All(IsBucketExact) &&
                           generation.PlayerRelicBuckets.All(IsBucketExact);
        bool party = generation.HasExactFixedParty && generation.PlayerCount <= 4 &&
            generation.ModeFactsExact && generation.IsMultiplayerExact &&
            request.Authority.PlayersCount == generation.PlayerCount &&
            request.Authority.PlayerSlotIndex == generation.PersonalPlayerSlot && generation.PersonalPlayerSlot >= 0 &&
            generation.PersonalPlayerSlot < generation.PlayerCount && generation.PartyRelicBuckets
                .Take(generation.PersonalPlayerSlot).SelectMany(b => b).All(IsBucketExact);
        if (!(sourceUsable || request.Authority.UsesBestEffortModel) || !(singleplayer || party) || !seedBranch || !generation.DirectSourceAudited ||
            !generation.NoUnknownHooksOrModifiers && !request.Authority.UsesBestEffortModel || !exactInputs)
            return Missing("UnsupportedOrIncompleteRelicRuntimeAuthority", out pool, out issue);

        // Shared and preceding personal bags consume the same UpFront stream.
        // Only this owner's buckets are exposed as observable lanes.
        var prefixBuckets = generation.SharedRelicBuckets.Concat(party
            ? generation.PartyRelicBuckets.Take(generation.PersonalPlayerSlot).SelectMany(b => b)
            : []).ToArray();
        Beta109RelicBucketSnapshot[] buckets = prefixBuckets.Concat(generation.PlayerRelicBuckets).ToArray();
        int totalEntries = buckets.Sum(bucket => bucket.OrderedEntries.Count);
        int maxLength = buckets.Max(bucket => bucket.OrderedEntries.Count);
        if (buckets.Length > MaximumBuckets || totalEntries > MaximumEntries || maxLength > MaximumBucketLength)
            return Missing("RelicFamilyPoolCapacityExceeded", out pool, out issue);

        foreach (Beta109RelicBucketSnapshot bucket in buckets)
        foreach (Beta109RelicBucketEntrySnapshot entry in bucket.OrderedEntries)
            if (!denseByKey.ContainsKey(entry.RelicKey))
                denseByKey.Add(entry.RelicKey, checked((ushort)denseByKey.Count));

        var ids = new ushort[totalEntries];
        var flags = new byte[totalEntries];
        var offsets = new int[buckets.Length];
        var lengths = new int[buckets.Length];
        var scopes = new byte[buckets.Length];
        var kinds = new byte[buckets.Length];
        var directions = new byte[buckets.Length];
        int entryOffset = 0;
        for (int bucketIndex = 0; bucketIndex < buckets.Length; bucketIndex++)
        {
            Beta109RelicBucketSnapshot bucket = buckets[bucketIndex];
            bool shared = bucketIndex < prefixBuckets.Length;
            byte kind = ParseBucketKind(bucket.BucketId);
            int lane = LaneIndex(kind);
            offsets[bucketIndex] = entryOffset;
            lengths[bucketIndex] = bucket.OrderedEntries.Count;
            scopes[bucketIndex] = shared ? (byte)0 : (byte)1;
            kinds[bucketIndex] = kind;
            directions[bucketIndex] = !shared && lane >= 0 ? (kind == 4 ? (byte)2 : (byte)1) : (byte)0;
            int bucketPosition = 0;
            foreach (Beta109RelicBucketEntrySnapshot entry in bucket.OrderedEntries)
            {
                ushort id = denseByKey[entry.RelicKey];
                ids[entryOffset] = id;
                flags[entryOffset] = entry.IsAllowedInShops ? (byte)1 : (byte)0;
                if (!shared && lane >= 0 && (kind != 4 || entry.IsAllowedInShops))
                {
                    if (!lanePositions[lane].TryAdd(id, checked((ushort)bucketPosition)))
                        return Missing("RelicFamilyLaneContainsDuplicateRelic", out pool, out issue);
                }
                entryOffset++;
                bucketPosition++;
            }
        }
        for (int lane = 0; lane < LaneCount; lane++)
        {
            int matchingPlayerBuckets = Enumerable.Range(0, buckets.Length)
                .Count(index => scopes[index] == 1 && LaneIndex(kinds[index]) == lane);
            if (matchingPlayerBuckets != 1)
                return Missing(
                    matchingPlayerBuckets == 0 ? "RelicFamilyLaneMissing" : "RelicFamilyLaneAmbiguous",
                    out pool, out issue);
        }

        pool = new RelicFamilyPool(ids, flags, offsets, lengths, scopes, kinds, directions, maxLength);
        issue = string.Empty;
        return true;
    }

    private static ushort AppendLaneKeys(
        IReadOnlyList<ModelKey> keys,
        int lane,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey,
        IReadOnlyList<Dictionary<ushort, ushort>> lanePositions,
        List<ushort> output,
        bool failIfMissing,
        out int originalCount,
        out bool missing)
    {
        originalCount = keys.Count;
        missing = false;
        int start = output.Count;
        foreach (ModelKey key in keys)
        {
            if (!denseByKey.TryGetValue(key, out ushort id) || !lanePositions[lane].ContainsKey(id))
            {
                if (failIfMissing) missing = true;
                continue;
            }
            if (!output.Skip(start).Contains(id)) output.Add(id);
        }
        return checked((ushort)(output.Count - start));
    }

    private static bool IsBucketExact(Beta109RelicBucketSnapshot bucket) =>
        bucket.OrderExact && bucket.HasExactShopEligibility &&
        bucket.OrderedEntries.Count == bucket.OrderedRelics.Count &&
        bucket.OrderedEntries.Select(entry => entry.RelicKey).SequenceEqual(bucket.OrderedRelics);

    private static int EffectiveLaneCount(RelicFamilyPool pool, int lane)
    {
        for (int bucket = 0; bucket < pool.BucketCount; bucket++)
        {
            if (pool.BucketScopes[bucket] != 1 || LaneIndex(pool.BucketKinds[bucket]) != lane) continue;
            if (lane != 3) return pool.BucketLengths[bucket];
            int start = pool.BucketOffsets[bucket];
            return Enumerable.Range(start, pool.BucketLengths[bucket]).Count(index => (pool.EntryFlags[index] & 1) != 0);
        }
        return 0;
    }

    private static int EffectiveLaneBucketLength(RelicFamilyPool pool, int lane)
    {
        for (int bucket = 0; bucket < pool.BucketCount; bucket++)
            if (pool.BucketScopes[bucket] == 1 && LaneIndex(pool.BucketKinds[bucket]) == lane)
                return pool.BucketLengths[bucket];
        return 0;
    }

    private static IEnumerable<ushort> EnumeratePredicateIds(
        RelicFamilyPredicate predicate,
        IReadOnlyList<ushort> predicateIds)
    {
        for (int index = 0; index < predicate.AnyCount; index++)
            yield return predicateIds[predicate.AnyOffset + index];
        for (int index = 0; index < predicate.AllCount; index++)
            yield return predicateIds[predicate.AllOffset + index];
        for (int index = 0; index < predicate.BanCount; index++)
            yield return predicateIds[predicate.BanOffset + index];
    }

    private static void RewritePredicateIds(
        RelicFamilyPredicate predicate,
        IList<ushort> predicateIds,
        IReadOnlyDictionary<ushort, ushort> trackedIndexById)
    {
        RewriteRange(predicate.AnyOffset, predicate.AnyCount);
        RewriteRange(predicate.AllOffset, predicate.AllCount);
        RewriteRange(predicate.BanOffset, predicate.BanCount);
        return;

        void RewriteRange(int offset, int count)
        {
            for (int index = 0; index < count; index++)
                predicateIds[offset + index] = trackedIndexById[predicateIds[offset + index]];
        }
    }

    private static bool Missing(string reason, out RelicFamilyPool pool, out string issue)
    {
        pool = new RelicFamilyPool([], [], [], [], [], [], [], 0);
        issue = reason;
        return false;
    }

    private static int LaneIndex(RelicSequenceKind kind) => kind switch
    {
        RelicSequenceKind.Common => 0,
        RelicSequenceKind.Uncommon => 1,
        RelicSequenceKind.Rare => 2,
        RelicSequenceKind.Shop => 3,
        _ => -1
    };

    private static int LaneIndex(byte kind) => kind is >= 1 and <= 4 ? kind - 1 : -1;

    private static byte ParseBucketKind(string bucketId)
    {
        int separator = bucketId.LastIndexOf(':');
        string rarity = separator >= 0 && separator < bucketId.Length - 1 ? bucketId[(separator + 1)..] : bucketId;
        return rarity.ToUpperInvariant() switch
        {
            "COMMON" => 1,
            "UNCOMMON" => 2,
            "RARE" => 3,
            "SHOP" => 4,
            _ => 0
        };
    }
}
