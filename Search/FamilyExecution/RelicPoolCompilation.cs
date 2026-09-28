using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum Beta110RelicPoolAuthorityKind : byte
{
    MissingRuntimeSnapshot = 0,
    StandardRuntimeSnapshot = 1,
    BestEffortRuntimeSnapshot = 2
}

internal enum Beta110RelicBucketScope : byte
{
    Shared = 0,
    Player = 1
}

internal enum Beta110RelicBucketKind : byte
{
    Other = 0,
    Common = 1,
    Uncommon = 2,
    Rare = 3,
    Shop = 4
}

internal enum Beta110RelicDrawDirection : byte
{
    None = 0,
    Front = 1,
    Back = 2
}

[Flags]
internal enum Beta110RelicEntryFlags : byte
{
    None = 0,
    AllowedInShops = 1 << 0
}

/// <summary>
/// Family-private xoshiro256** value used by CPU replay and numeric checks.
/// It contains no game RNG objects and is not part of public candidate transport.
/// </summary>
internal readonly record struct UpFrontRngCheckpoint(
    ulong S0,
    ulong S1,
    ulong S2,
    ulong S3)
{
    public bool IsZero => (S0 | S1 | S2 | S3) == 0UL;
}

/// <summary>
/// GPU-ready, plan-local, immutable numeric snapshot of the exact runtime relic
/// bag source. Entries preserve source multiplicity. Buckets are flattened in
/// initialization order: every shared bucket first, then every player bucket.
/// </summary>
internal sealed record CompiledRelicPoolSnapshot(
    ushort[] DenseRelicIds,
    byte[] EntryFlags,
    int[] BucketOffsets,
    int[] BucketLengths,
    byte[] BucketScopes,
    byte[] BucketKinds,
    byte[] DrawDirections,
    ushort[] BucketInitializationOrder,
    short[] PlayerLaneBucketIndexes,
    int MaxBucketLength,
    string CatalogFingerprint,
    Beta110RelicPoolAuthorityKind AuthorityKind,
    string AuthorityIssue)
{
    public int BucketCount => BucketOffsets.Length;
    public int TotalEntryCount => DenseRelicIds.Length;
    public bool Available =>
        AuthorityKind != Beta110RelicPoolAuthorityKind.MissingRuntimeSnapshot &&
        BucketOffsets.Length == BucketLengths.Length &&
        BucketOffsets.Length == BucketScopes.Length &&
        BucketOffsets.Length == BucketKinds.Length &&
        BucketOffsets.Length == DrawDirections.Length &&
        DenseRelicIds.Length == EntryFlags.Length &&
        PlayerLaneBucketIndexes.Length == 4 &&
        MaxBucketLength >= 0;
}

internal static class RelicPoolCompilation
{
    internal const int MaximumTotalRelicEntries = ushort.MaxValue - 1;
    internal const int MaximumRelicsPerBucket = 4_096;
    internal const int MaximumBucketCount = 4_096;
    private const int LaneCount = 4;

    /// <summary>
    /// Authority-only donor for P5 selectivity. This exposes the exact runtime pool
    /// shape without consulting Enabled, GPU support, output density, or execution
    /// applicability. Probability support is intentionally independent of execution
    /// support.
    /// </summary>
    internal static bool TryCompileAuthorityPoolForSelectivity(
        RuntimeProfileId profileId,
        RuntimeContextAuthoritySnapshot authority,
        out CompiledRelicPoolSnapshot snapshot,
        out IReadOnlyDictionary<ModelKey, ushort> denseByKey,
        out string issue)
    {
        bool ok = TryCompilePool(
            profileId,
            authority,
            out snapshot,
            out Dictionary<ModelKey, ushort> dense,
            out _,
            out issue);
        denseByKey = dense;
        return ok;
    }

    private static bool TryCompilePool(
        RuntimeProfileId profileId,
        RuntimeContextAuthoritySnapshot authority,
        out CompiledRelicPoolSnapshot snapshot,
        out Dictionary<ModelKey, ushort> denseByKey,
        out HashSet<ushort>[] laneMembership,
        out string issue)
    {
        denseByKey = new Dictionary<ModelKey, ushort>(ModelKeyComparer.Instance);
        laneMembership = Enumerable.Range(0, LaneCount).Select(_ => new HashSet<ushort>()).ToArray();
        issue = string.Empty;
        WorldAuthoritySnapshot? world = authority.WorldAuthority;
        Beta109WorldGenerationSnapshot? generation = world?.Beta109Generation;
        if (world is null || generation is null ||
            !RuntimeProfilePolicies.UsesBeta110SharedAlgorithms(profileId) ||
            world.CapturedProfileId != profileId ||
            generation.Profile != profileId)
        {
            snapshot = Missing("MissingModernRelicRuntimeSnapshot");
            issue = snapshot.AuthorityIssue;
            return false;
        }

        bool sourceAuthorityUsable =
            world.Completeness == SnapshotCompleteness.Complete &&
            world.SourceAuthority is SourceAuthority.OfficialRuntimeExact or
                SourceAuthority.AuditedStaticExact or
                SourceAuthority.ModdedRuntimeBestEffort;
        bool singleplayerAuthority =
            generation.ModeFactsExact &&
            generation.GameMode == WorldGameMode.Singleplayer &&
            generation.IsMultiplayerExact &&
            !generation.IsMultiplayer &&
            generation.PlayerCount == 1;
        bool supportedSeedBranch =
            generation.RunSeedHashKind == Beta109RunSeedHashKind.ModernXxHash64 &&
            generation.OldSeedBranchStatus == Beta109OldSeedBranchStatus.NewSeedHashed;
        bool partyAuthority = generation.HasExactFixedParty && generation.ModeFactsExact && generation.IsMultiplayerExact &&
            authority.PlayersCount == generation.PlayerCount && authority.PlayerSlotIndex == generation.PersonalPlayerSlot &&
            generation.PersonalPlayerSlot >= 0 && generation.PersonalPlayerSlot < generation.PlayerCount &&
            generation.PartyRelicBuckets.Take(generation.PersonalPlayerSlot).SelectMany(b => b).All(IsBucketExact);
        if (!(sourceAuthorityUsable || authority.UsesBestEffortModel) || !(singleplayerAuthority || partyAuthority) || !supportedSeedBranch ||
            !generation.DirectSourceAudited || !generation.NoUnknownHooksOrModifiers && !authority.UsesBestEffortModel)
        {
            snapshot = Missing("UnsupportedRelicRuntimeAuthorityOrHooks");
            issue = snapshot.AuthorityIssue;
            return false;
        }

        bool exactRelicInputs = generation.RelicInitializationExact &&
                                generation.SharedRelicPoolOrderExact &&
                                generation.CharacterRelicPoolOrderExact &&
                                generation.RelicRarityAuthorityExact &&
                                generation.RelicShopEligibilityAuthorityExact &&
                                generation.PlayerRelicPoolCompositionExact &&
                                generation.SharedRelicBuckets.Count > 0 &&
                                generation.PlayerRelicBuckets.Count > 0 &&
                                generation.SharedRelicBuckets.All(IsBucketExact) &&
                                generation.PlayerRelicBuckets.All(IsBucketExact);
        if (!exactRelicInputs)
        {
            snapshot = Missing("MissingExactRuntimeRelicPoolSnapshot");
            issue = snapshot.AuthorityIssue;
            return false;
        }

        var prefixBuckets = generation.SharedRelicBuckets.Concat(partyAuthority
            ? generation.PartyRelicBuckets.Take(generation.PersonalPlayerSlot).SelectMany(b => b) : []).ToArray();
        int bucketCount = prefixBuckets.Length + generation.PlayerRelicBuckets.Count;
        int totalEntries = prefixBuckets.Sum(bucket => bucket.OrderedEntries.Count) +
                           generation.PlayerRelicBuckets.Sum(bucket => bucket.OrderedEntries.Count);
        int maxBucketLength = prefixBuckets.Concat(generation.PlayerRelicBuckets)
            .Max(bucket => bucket.OrderedEntries.Count);
        if (bucketCount > MaximumBucketCount ||
            totalEntries > MaximumTotalRelicEntries ||
            maxBucketLength > MaximumRelicsPerBucket)
        {
            snapshot = Missing($"RelicPoolCapacityExceeded:{bucketCount}:{totalEntries}:{maxBucketLength}");
            issue = snapshot.AuthorityIssue;
            return false;
        }

        foreach (Beta109RelicBucketSnapshot bucket in prefixBuckets.Concat(generation.PlayerRelicBuckets))
        {
            foreach (Beta109RelicBucketEntrySnapshot entry in bucket.OrderedEntries)
            {
                if (denseByKey.ContainsKey(entry.RelicKey)) continue;
                if (denseByKey.Count >= MaximumTotalRelicEntries)
                {
                    snapshot = Missing("RelicDenseIdCapacityExceeded");
                    issue = snapshot.AuthorityIssue;
                    return false;
                }
                denseByKey.Add(entry.RelicKey, checked((ushort)denseByKey.Count));
            }
        }

        var denseEntries = new ushort[totalEntries];
        var entryFlags = new byte[totalEntries];
        var offsets = new int[bucketCount];
        var lengths = new int[bucketCount];
        var scopes = new byte[bucketCount];
        var kinds = new byte[bucketCount];
        var directions = new byte[bucketCount];
        var initializationOrder = new ushort[bucketCount];
        var laneBucketIndexes = new short[] { -1, -1, -1, -1 };

        int entryOffset = 0;
        int bucketIndex = 0;
        foreach (Beta109RelicBucketSnapshot bucket in prefixBuckets)
        {
            WriteBucket(bucket, Beta110RelicBucketScope.Shared, bucketIndex, ref entryOffset,
                denseByKey, denseEntries, entryFlags, offsets, lengths, scopes, kinds, directions,
                initializationOrder, laneBucketIndexes, laneMembership);
            bucketIndex++;
        }
        foreach (Beta109RelicBucketSnapshot bucket in generation.PlayerRelicBuckets)
        {
            WriteBucket(bucket, Beta110RelicBucketScope.Player, bucketIndex, ref entryOffset,
                denseByKey, denseEntries, entryFlags, offsets, lengths, scopes, kinds, directions,
                initializationOrder, laneBucketIndexes, laneMembership);
            bucketIndex++;
        }

        if (entryOffset != totalEntries || laneBucketIndexes.Any(index => index < 0))
        {
            snapshot = Missing("RelicPoolLaneOrEntryAlignmentMismatch");
            issue = snapshot.AuthorityIssue;
            return false;
        }

        Beta110RelicPoolAuthorityKind authorityKind = generation.IsVanilla && generation.NoUnknownHooksOrModifiers
            ? Beta110RelicPoolAuthorityKind.StandardRuntimeSnapshot
            : Beta110RelicPoolAuthorityKind.BestEffortRuntimeSnapshot;
        string fingerprint = Fingerprint(new[]
        {
            generation.CatalogFingerprint,
            generation.UnlockFingerprint,
            generation.RelicAuthorityEvidenceCode,
            authorityKind.ToString(),
            string.Join(',', denseEntries),
            string.Join(',', entryFlags),
            string.Join(',', offsets),
            string.Join(',', lengths),
            string.Join(',', scopes),
            string.Join(',', kinds),
            string.Join(',', directions),
            string.Join(',', initializationOrder),
            string.Join(',', laneBucketIndexes)
        });
        snapshot = new CompiledRelicPoolSnapshot(
            denseEntries,
            entryFlags,
            offsets,
            lengths,
            scopes,
            kinds,
            directions,
            initializationOrder,
            laneBucketIndexes,
            maxBucketLength,
            fingerprint,
            authorityKind,
            string.Empty);
        return true;
    }

    private static void WriteBucket(
        Beta109RelicBucketSnapshot bucket,
        Beta110RelicBucketScope scope,
        int bucketIndex,
        ref int entryOffset,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey,
        ushort[] denseEntries,
        byte[] entryFlags,
        int[] offsets,
        int[] lengths,
        byte[] scopes,
        byte[] kinds,
        byte[] directions,
        ushort[] initializationOrder,
        short[] laneBucketIndexes,
        HashSet<ushort>[] laneMembership)
    {
        Beta110RelicBucketKind kind = ParseBucketKind(bucket.BucketId);
        offsets[bucketIndex] = entryOffset;
        lengths[bucketIndex] = bucket.OrderedEntries.Count;
        scopes[bucketIndex] = (byte)scope;
        kinds[bucketIndex] = (byte)kind;
        int lane = LaneIndex(kind);
        directions[bucketIndex] = scope == Beta110RelicBucketScope.Player && lane >= 0
            ? kind == Beta110RelicBucketKind.Shop
                ? (byte)Beta110RelicDrawDirection.Back
                : (byte)Beta110RelicDrawDirection.Front
            : (byte)Beta110RelicDrawDirection.None;
        initializationOrder[bucketIndex] = checked((ushort)bucketIndex);

        if (scope == Beta110RelicBucketScope.Player && lane >= 0)
        {
            laneBucketIndexes[lane] = laneBucketIndexes[lane] >= 0
                ? (short)-2
                : checked((short)bucketIndex);
        }

        foreach (Beta109RelicBucketEntrySnapshot entry in bucket.OrderedEntries)
        {
            ushort denseId = denseByKey[entry.RelicKey];
            denseEntries[entryOffset] = denseId;
            entryFlags[entryOffset] = entry.IsAllowedInShops
                ? (byte)Beta110RelicEntryFlags.AllowedInShops
                : (byte)Beta110RelicEntryFlags.None;
            if (scope == Beta110RelicBucketScope.Player && lane >= 0 &&
                (kind != Beta110RelicBucketKind.Shop || entry.IsAllowedInShops))
            {
                // Shop eligibility is applied when drawing from the shuffled tail.
                // Keep blocked entries in the flattened bucket so they still consume
                // shuffle positions and RNG, but do not allow an impossible blocked
                // relic to compile as a Shop-lane target.
                laneMembership[lane].Add(denseId);
            }
            entryOffset++;
        }
    }

    private static CompiledRelicPoolSnapshot Missing(string issue) => new(
        Array.Empty<ushort>(),
        Array.Empty<byte>(),
        Array.Empty<int>(),
        Array.Empty<int>(),
        Array.Empty<byte>(),
        Array.Empty<byte>(),
        Array.Empty<byte>(),
        Array.Empty<ushort>(),
        new short[] { -1, -1, -1, -1 },
        0,
        string.Empty,
        Beta110RelicPoolAuthorityKind.MissingRuntimeSnapshot,
        issue);

    private static int LaneIndex(RelicSequenceKind lane) => lane switch
    {
        RelicSequenceKind.Common => 0,
        RelicSequenceKind.Uncommon => 1,
        RelicSequenceKind.Rare => 2,
        RelicSequenceKind.Shop => 3,
        _ => -1
    };

    private static int LaneIndex(Beta110RelicBucketKind kind) => kind switch
    {
        Beta110RelicBucketKind.Common => 0,
        Beta110RelicBucketKind.Uncommon => 1,
        Beta110RelicBucketKind.Rare => 2,
        Beta110RelicBucketKind.Shop => 3,
        _ => -1
    };

    private static Beta110RelicBucketKind ParseBucketKind(string bucketId)
    {
        int separator = bucketId.LastIndexOf(':');
        string rarity = separator >= 0 && separator < bucketId.Length - 1
            ? bucketId[(separator + 1)..]
            : bucketId;
        return rarity.ToUpperInvariant() switch
        {
            "COMMON" => Beta110RelicBucketKind.Common,
            "UNCOMMON" => Beta110RelicBucketKind.Uncommon,
            "RARE" => Beta110RelicBucketKind.Rare,
            "SHOP" => Beta110RelicBucketKind.Shop,
            _ => Beta110RelicBucketKind.Other
        };
    }

    private static string Fingerprint(IEnumerable<string> values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values))))
            .ToLowerInvariant();
    private static bool IsBucketExact(Beta109RelicBucketSnapshot bucket) =>
        bucket.OrderExact &&
        bucket.OrderedEntries.Count == bucket.OrderedRelics.Count &&
        bucket.HasExactShopEligibility &&
        bucket.OrderedEntries.Select(entry => entry.RelicKey).SequenceEqual(bucket.OrderedRelics);

}
