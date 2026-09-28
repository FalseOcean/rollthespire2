using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Relics;

/// <summary>
/// Pure dual-profile replay of initial RelicGrabBag population. This type only
/// consumes canonical seed text and immutable authority DTOs. It never accesses
/// Godot, ModelDb, RunState, Player, game RNG, Obtain, or AfterObtained.
/// </summary>
public static class RelicSequencePredictor
{
    private const string UpFrontStream = "up_front";
    private static readonly string[] LaneRarities = { "Common", "Uncommon", "Rare", "Shop" };

    public static RelicSequencePredictionResult Predict(
        IRuntimeProfile profile,
        string canonicalSeed,
        WorldAuthoritySnapshot? worldAuthority,
        int previewCount = 5)
    {
        ArgumentNullException.ThrowIfNull(profile);
        previewCount = Math.Clamp(previewCount, 1, 10);

        if (!profile.SupportsSeedRng)
            return RelicSequencePredictionResult.Unsupported(profile.ProfileId, "RelicSequenceProfileRngUnsupported");
        if (worldAuthority is null)
            return RelicSequencePredictionResult.Unknown(profile.ProfileId, "MissingRelicSequenceWorldAuthority");
        if (worldAuthority.CapturedProfileId != profile.ProfileId)
            return RelicSequencePredictionResult.Unknown(
                profile.ProfileId,
                "RelicSequenceProfileMismatch",
                worldAuthority.SnapshotFingerprint,
                worldAuthority.CatalogFingerprint);
        if (string.IsNullOrWhiteSpace(canonicalSeed))
            return RelicSequencePredictionResult.Unknown(
                profile.ProfileId,
                "MissingCanonicalSeed",
                worldAuthority.SnapshotFingerprint,
                worldAuthority.CatalogFingerprint);

        return profile.ProfileId switch
        {
            RuntimeProfileId.Stable107 => PredictLegacy(profile, canonicalSeed, worldAuthority, previewCount),
            RuntimeProfileId.Beta109 or RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 =>
                PredictModern(profile, canonicalSeed, worldAuthority, previewCount),
            _ => RelicSequencePredictionResult.Unsupported(profile.ProfileId, "RelicSequenceProfileUnsupported")
        };
    }

    private static RelicSequencePredictionResult PredictLegacy(
        IRuntimeProfile profile,
        string canonicalSeed,
        WorldAuthoritySnapshot world,
        int previewCount)
    {
        if (!world.HasExactLegacyFoundation)
            return Unknown(world, profile.ProfileId, "MissingStable107RelicSequenceFoundation");
        if (world.SharedRelicPoolSource is null || world.CharacterRelicPoolSource is null)
            return Unknown(world, profile.ProfileId, "MissingStable107RelicSourcePools");

        RelicCatalogEntry[] shared = ConvertLegacySource(world.SharedRelicPoolSource, out string sharedIssue);
        if (sharedIssue.Length > 0) return Unknown(world, profile.ProfileId, sharedIssue);
        RelicCatalogEntry[] character = ConvertLegacySource(world.CharacterRelicPoolSource, out string characterIssue);
        if (characterIssue.Length > 0) return Unknown(world, profile.ProfileId, characterIssue);

        IReadOnlyList<RelicBucket> sharedBuckets = BuildBuckets(shared, filterPlayerRarities: false);
        IReadOnlyList<RelicBucket> playerBuckets = BuildBuckets(shared.Concat(character), filterPlayerRarities: true);
        return Replay(profile, canonicalSeed, world, sharedBuckets, playerBuckets, previewCount,
            "stable107.relic-grab-bag.direct-donor-audit");
    }

    internal static RelicSequencePredictionResult PredictFromRootHash(
        IRuntimeProfile profile,
        ulong rootHash,
        string seedIdentity,
        WorldAuthoritySnapshot? worldAuthority,
        int previewCount = 5)
    {
        ArgumentNullException.ThrowIfNull(profile);
        previewCount = Math.Clamp(previewCount, 1, 10);
        if (!profile.SupportsSeedRng)
            return RelicSequencePredictionResult.Unsupported(profile.ProfileId, "RelicSequenceProfileRngUnsupported");
        if (!RuntimeProfilePolicies.IsModernCore(profile.ProfileId))
            return RelicSequencePredictionResult.Unsupported(profile.ProfileId, "RelicSequenceRootHashProfileUnsupported");
        if (worldAuthority is null)
            return RelicSequencePredictionResult.Unknown(profile.ProfileId, "MissingRelicSequenceWorldAuthority");
        if (worldAuthority.CapturedProfileId != profile.ProfileId)
            return RelicSequencePredictionResult.Unknown(
                profile.ProfileId,
                "RelicSequenceProfileMismatch",
                worldAuthority.SnapshotFingerprint,
                worldAuthority.CatalogFingerprint);
        return PredictModern(profile, rootHash, seedIdentity, worldAuthority, previewCount);
    }

    private static RelicSequencePredictionResult PredictModern(
        IRuntimeProfile profile,
        string canonicalSeed,
        WorldAuthoritySnapshot world,
        int previewCount) =>
        PredictModern(profile, profile.ComputeRootSeed(canonicalSeed), canonicalSeed, world, previewCount);

    private static RelicSequencePredictionResult PredictModern(
        IRuntimeProfile profile,
        ulong rootHash,
        string seedIdentity,
        WorldAuthoritySnapshot world,
        int previewCount)
    {
        Beta109WorldGenerationSnapshot? source = world.Beta109Generation;
        if (source is null || source.Profile != profile.ProfileId ||
            !RuntimeProfilePolicies.IsModernCore(source.Profile))
            return Unknown(world, profile.ProfileId, "MissingModernRelicSequenceGenerationAuthority");
        Beta109WorldGenerationSnapshot modern = Beta109WorldSnapshotProjector.ProjectForRootHash(
            source,
            rootHash,
            seedIdentity);
        bool bestEffort = profile.ProfileId == RuntimeProfileId.Beta111 &&
            (world.SourceAuthority == SourceAuthority.ModdedRuntimeBestEffort || !modern.NoUnknownHooksOrModifiers);
        if (!bestEffort && (!SourceAuthorityRules.SupportsExactIdentity(world.SourceAuthority) ||
            world.Completeness != SnapshotCompleteness.Complete))
            return Unknown(world, profile.ProfileId, "MissingModernRelicSequenceSourceAuthority");
        if (!(modern.GameMode == WorldGameMode.Singleplayer && !modern.IsMultiplayer && modern.PlayerCount == 1) &&
            !(modern.HasExactFixedParty && modern.PersonalPlayerSlot >= 0 && modern.PersonalPlayerSlot < modern.PlayerCount))
            return Unknown(world, profile.ProfileId, "ModernRelicSequencePartyAuthorityMissing");
        if (modern.RunSeedHashKind != Beta109RunSeedHashKind.ModernXxHash64 ||
            modern.OldSeedBranchStatus != Beta109OldSeedBranchStatus.NewSeedHashed)
            return Unknown(world, profile.ProfileId, "ModernRelicSequenceSeedBranchUnsupported");
        if (!modern.RunSeedRootExact || modern.RunSeedRoot != rootHash)
            return Unknown(world, profile.ProfileId, "ModernRelicSequenceSeedRootMismatch");
        if (!modern.RelicInitializationExact ||
            !modern.SharedRelicPoolOrderExact ||
            !modern.CharacterRelicPoolOrderExact ||
            !modern.RelicRarityAuthorityExact ||
            !modern.PlayerRelicPoolCompositionExact)
            return Unknown(world, profile.ProfileId, "MissingModernRelicInitializationAuthority");
        if (!modern.RelicShopEligibilityAuthorityExact)
            return Unknown(world, profile.ProfileId, "MissingModernRelicShopEligibilityAuthority");
        if (!modern.NoUnknownHooksOrModifiers && !bestEffort)
            return Unknown(world, profile.ProfileId, "UnknownModernRelicSequenceHooksOrModifiers");
        if (modern.SharedRelicBuckets.Count == 0 || modern.PlayerRelicBuckets.Count == 0)
            return Unknown(world, profile.ProfileId, "MissingModernRelicBuckets");

        IReadOnlyList<RelicBucket>? shared = ConvertModernBuckets(modern.SharedRelicBuckets, out string sharedIssue);
        if (shared is null) return Unknown(world, profile.ProfileId, sharedIssue);
        IReadOnlyList<RelicBucket>? player = ConvertModernBuckets(modern.PlayerRelicBuckets, out string playerIssue);
        if (player is null) return Unknown(world, profile.ProfileId, playerIssue);

        bool validationAccepted = profile.ProfileId switch
        {
            RuntimeProfileId.Beta109 => true,
            RuntimeProfileId.Beta110 => Beta110ValidationAuthority.RelicEventSequenceAccepted,
            RuntimeProfileId.Beta111 => Beta111ValidationAuthority.RuntimeAccepted,
            _ => false
        };
        string evidenceCode = profile.ProfileId switch
        {
            RuntimeProfileId.Beta111 => "beta111.relic-grab-bag.shared-modern-semantic-compatible",
            RuntimeProfileId.Beta110 => "beta110.relic-grab-bag.shared-modern-source-audit",
            _ => "beta109.relic-grab-bag.direct-source-delta-audit"
        };
        return Replay(profile, seedIdentity, world, shared, player, previewCount,
            evidenceCode,
            rootOverride: modern.RunSeedRoot,
            authorityFingerprintOverride: modern.SnapshotFingerprint,
            productionValidated: validationAccepted);
    }

    private static RelicSequencePredictionResult Replay(
        IRuntimeProfile profile,
        string canonicalSeed,
        WorldAuthoritySnapshot world,
        IReadOnlyList<RelicBucket> sharedBuckets,
        IReadOnlyList<RelicBucket> playerBuckets,
        int previewCount,
        string evidenceCode,
        ulong? rootOverride = null,
        string? authorityFingerprintOverride = null,
        bool productionValidated = true)
    {
        ulong root = rootOverride ?? profile.ComputeRootSeed(canonicalSeed);
        var rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(root, UpFrontStream));
        foreach (RelicBucket bucket in sharedBuckets)
        {
            rng.UnstableShuffle(bucket.Entries);
        }
        if (world.Beta109Generation is { IsMultiplayer: true } party)
        {
            // RunState populates every personal bag in slot order with the same UpFront stream.
            // These are initial snapshots; later pickups never mutate the displayed sequences.
            for (int slot = 0; slot < party.PersonalPlayerSlot; slot++)
                foreach (var bucket in party.PartyRelicBuckets[slot])
                {
                    var preceding = bucket.OrderedRelics.ToList();
                    rng.UnstableShuffle(preceding);
                }
        }
        foreach (RelicBucket bucket in playerBuckets)
        {
            rng.UnstableShuffle(bucket.Entries);
        }

        PredictionPrecision precision = productionValidated
            ? PredictionPrecision.Exact
            : PredictionPrecision.Partial;
        SourceAuthority resultAuthority = productionValidated
            ? world.SourceAuthority
            : SourceAuthority.Incomplete;
        SnapshotCompleteness resultCompleteness = productionValidated
            ? SnapshotCompleteness.Complete
            : SnapshotCompleteness.Partial;

        var lanes = new List<RelicSequenceLaneResult>(LaneRarities.Length);
        foreach (string rarity in LaneRarities)
        {
            RelicBucket? bucket = playerBuckets.FirstOrDefault(item => RarityEquals(item.RarityCode, rarity));
            if (bucket is null)
                return Unknown(world, profile.ProfileId, "MissingRelicSequenceBucket:" + rarity);

            bool isShop = RarityEquals(rarity, "Shop");
            RelicCatalogEntry[] effectiveEntries = isShop
                ? bucket.Entries.Where(entry => entry.IsAllowedInShops).ToArray()
                : bucket.Entries.ToArray();

            IEnumerable<RelicCatalogEntry> primaryPullOrder = isShop
                ? effectiveEntries.AsEnumerable().Reverse()
                : effectiveEntries;
            RelicSequenceEntryResult[] entries = primaryPullOrder
                .Take(previewCount)
                .Select((entry, index) => new RelicSequenceEntryResult(
                    index + 1,
                    entry.RelicKey,
                    precision,
                    evidenceCode + "." + rarity.ToLowerInvariant()))
                .ToArray();

            // Analysis may present Common/Uncommon/Rare from both physical ends.
            // Do not overlap the head preview on short bags: every physical slot is
            // represented at most once even though duplicate relic identities remain
            // legal when source multiplicity contains them.
            int tailCount = isShop
                ? 0
                : Math.Min(previewCount, Math.Max(0, effectiveEntries.Length - entries.Length));
            RelicSequenceEntryResult[] tailEntries = isShop
                ? Array.Empty<RelicSequenceEntryResult>()
                : effectiveEntries
                    .AsEnumerable()
                    .Reverse()
                    .Take(tailCount)
                    .Select((entry, index) => new RelicSequenceEntryResult(
                        index + 1,
                        entry.RelicKey,
                        precision,
                        evidenceCode + "." + rarity.ToLowerInvariant() + ".tail"))
                    .ToArray();

            lanes.Add(new RelicSequenceLaneResult(
                ParseKind(rarity),
                rarity,
                isShop ? RelicSequencePullDirection.Back : RelicSequencePullDirection.Front,
                entries,
                precision,
                resultAuthority,
                resultCompleteness,
                evidenceCode + "." + rarity.ToLowerInvariant())
            {
                TotalCount = effectiveEntries.Length,
                FullEntries = primaryPullOrder.Select((entry, index) => new RelicSequenceEntryResult(
                    index + 1, entry.RelicKey, precision, evidenceCode + "." + rarity.ToLowerInvariant())).ToArray(),
                TailEntries = tailEntries
            });
        }

        var treasureRoomLanes = new List<RelicSequenceLaneResult>(3);
        foreach (string rarity in LaneRarities.Take(3))
        {
            RelicBucket? bucket = sharedBuckets.FirstOrDefault(item => RarityEquals(item.RarityCode, rarity));
            if (bucket is null) continue;

            RelicSequenceEntryResult[] fullEntries = bucket.Entries
                .Select((entry, index) => new RelicSequenceEntryResult(index + 1, entry.RelicKey,
                    precision, evidenceCode + ".treasure." + rarity.ToLowerInvariant()))
                .ToArray();
            treasureRoomLanes.Add(new RelicSequenceLaneResult(
                ParseKind(rarity), rarity, RelicSequencePullDirection.Front,
                fullEntries.Take(previewCount).ToArray(), precision, resultAuthority,
                resultCompleteness, evidenceCode + ".treasure." + rarity.ToLowerInvariant())
            {
                TotalCount = fullEntries.Length,
                FullEntries = fullEntries
            });
        }

        string authorityFingerprint = string.IsNullOrWhiteSpace(authorityFingerprintOverride)
            ? world.SnapshotFingerprint
            : authorityFingerprintOverride;
        string catalogFingerprint = Fingerprint(
            RuntimeProfilePolicies.CatalogFingerprintPrefix(profile.ProfileId) + "-relic-sequence-v2",
            sharedBuckets.SelectMany(DescribeBucket).Select(item => "shared:" + item)
                .Concat(playerBuckets.SelectMany(DescribeBucket).Select(item => "player:" + item)));
        return new RelicSequencePredictionResult(
            profile.ProfileId,
            SeedDomainEvaluationStatus.Evaluated,
            lanes,
            precision,
            resultAuthority,
            resultCompleteness,
            string.Empty,
            authorityFingerprint,
            catalogFingerprint,
            UpFrontStream,
            rng.CallCount,
            evidenceCode,
            new[]
            {
                new PredictionDiagnostic("relic-sequence-profile", profile.ProfileId.ToString()),
                new PredictionDiagnostic("relic-sequence-authority-fingerprint", authorityFingerprint),
                new PredictionDiagnostic("relic-sequence-catalog-fingerprint", catalogFingerprint),
                new PredictionDiagnostic("relic-sequence-rng-stream", UpFrontStream),
                new PredictionDiagnostic("relic-sequence-rng-calls", rng.CallCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new PredictionDiagnostic("relic-sequence-preview-count", previewCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new PredictionDiagnostic("relic-sequence-scope", "InitialRelicGrabBagSequences"),
                new PredictionDiagnostic("relic-sequence-validation-status",
                    productionValidated ? "Accepted" : profile.ProfileId == RuntimeProfileId.Beta111
                        ? Beta111ValidationAuthority.RuntimeSupportStatus
                        : Beta110ValidationAuthority.RuntimeSupportStatus)
            })
        {
            TreasureRoomLanes = treasureRoomLanes
        };
    }

    private static RelicCatalogEntry[] ConvertLegacySource(
        IReadOnlyList<NeowEffectRelicSnapshot> source,
        out string issue)
    {
        issue = string.Empty;
        var output = new List<RelicCatalogEntry>(source.Count);
        foreach (NeowEffectRelicSnapshot relic in source)
        {
            string rarity = string.IsNullOrWhiteSpace(relic.RarityCode)
                ? relic.Rarity.ToString()
                : relic.RarityCode;
            if (!relic.RelicKey.IsValid || string.IsNullOrWhiteSpace(rarity))
            {
                issue = "InvalidStable107RelicSequenceCatalogEntry";
                return Array.Empty<RelicCatalogEntry>();
            }
            if (!relic.ShopEligibilityExact)
            {
                issue = "MissingStable107RelicShopEligibilityAuthority";
                return Array.Empty<RelicCatalogEntry>();
            }
            output.Add(new RelicCatalogEntry(relic.RelicKey, rarity, relic.IsAllowedInShops));
        }
        if (output.Count == 0) issue = "EmptyStable107RelicSequenceCatalog";
        return output.ToArray();
    }

    private static IReadOnlyList<RelicBucket>? ConvertModernBuckets(
        IReadOnlyList<Beta109RelicBucketSnapshot> source,
        out string issue)
    {
        issue = string.Empty;
        var output = new List<RelicBucket>(source.Count);
        foreach (Beta109RelicBucketSnapshot bucket in source)
        {
            string rarity = RarityFromBucketId(bucket.BucketId);
            if (!bucket.OrderExact || string.IsNullOrWhiteSpace(rarity) ||
                bucket.OrderedEntries.Count != bucket.OrderedRelics.Count ||
                !bucket.HasExactShopEligibility)
            {
                issue = "InvalidModernRelicSequenceBucket:" + bucket.BucketId;
                return null;
            }
            var entries = new List<RelicCatalogEntry>(bucket.OrderedEntries.Count);
            for (int index = 0; index < bucket.OrderedEntries.Count; index++)
            {
                var entry = bucket.OrderedEntries[index];
                if (!entry.RelicKey.IsValid || entry.RelicKey != bucket.OrderedRelics[index])
                {
                    issue = "ModernRelicBucketEntryAlignmentMismatch:" + bucket.BucketId;
                    return null;
                }
                entries.Add(new RelicCatalogEntry(entry.RelicKey, rarity, entry.IsAllowedInShops));
            }
            output.Add(new RelicBucket(rarity, entries));
        }
        return output;
    }

    private static IReadOnlyList<RelicBucket> BuildBuckets(
        IEnumerable<RelicCatalogEntry> source,
        bool filterPlayerRarities)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, List<RelicCatalogEntry>>(StringComparer.Ordinal);
        foreach (RelicCatalogEntry relic in source)
        {
            if (filterPlayerRarities && !LaneRarities.Contains(relic.RarityCode, StringComparer.Ordinal))
                continue;
            if (!groups.TryGetValue(relic.RarityCode, out List<RelicCatalogEntry>? bucket))
            {
                bucket = new List<RelicCatalogEntry>();
                groups.Add(relic.RarityCode, bucket);
                order.Add(relic.RarityCode);
            }
            bucket.Add(relic);
        }
        return order.Select(rarity => new RelicBucket(rarity, groups[rarity])).ToArray();
    }

    private static IEnumerable<string> DescribeBucket(RelicBucket bucket)
    {
        yield return "bucket:" + bucket.RarityCode;
        for (int index = 0; index < bucket.Entries.Count; index++)
        {
            RelicCatalogEntry entry = bucket.Entries[index];
            yield return $"{index}:{entry.RelicKey.Serialized}:{entry.IsAllowedInShops}";
        }
    }

    private static string Fingerprint(string prefix, IEnumerable<string> values)
    {
        string payload = prefix + "\n" + string.Join("\n", values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static RelicSequencePredictionResult Unknown(
        WorldAuthoritySnapshot world,
        RuntimeProfileId profileId,
        string issue) => RelicSequencePredictionResult.Unknown(
        profileId,
        issue,
        world.SnapshotFingerprint,
        world.CatalogFingerprint);

    private static RelicSequenceKind ParseKind(string rarity) => rarity.ToUpperInvariant() switch
    {
        "COMMON" => RelicSequenceKind.Common,
        "UNCOMMON" => RelicSequenceKind.Uncommon,
        "RARE" => RelicSequenceKind.Rare,
        "SHOP" => RelicSequenceKind.Shop,
        _ => throw new InvalidOperationException("UnsupportedRelicSequenceRarity:" + rarity)
    };

    private static bool RarityEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static string RarityFromBucketId(string bucketId)
    {
        int separator = bucketId.LastIndexOf(':');
        return separator >= 0 && separator < bucketId.Length - 1
            ? bucketId[(separator + 1)..]
            : string.Empty;
    }

    private sealed record RelicCatalogEntry(ModelKey RelicKey, string RarityCode, bool IsAllowedInShops);
    private sealed record RelicBucket(string RarityCode, List<RelicCatalogEntry> Entries);
}
