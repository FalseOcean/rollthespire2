using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.World.Beta109;

public static class Beta109WorldSnapshotProjector
{
    public static Beta109WorldGenerationSnapshot ProjectForSeed(
        Beta109WorldGenerationSnapshot source,
        string canonicalSeed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSeed);
        ulong rootHash = XxHash64.HashUtf8(canonicalSeed, 0UL);
        return ProjectForRootHash(source, rootHash, canonicalSeed);
    }

    internal static Beta109WorldGenerationSnapshot ProjectForRootHash(
        Beta109WorldGenerationSnapshot source,
        ulong rootHash,
        string seedIdentity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedIdentity);
        if (!RuntimeProfilePolicies.IsModernCore(source.Profile))
            throw new InvalidOperationException("ModernWorldSnapshotProfileMismatch");

        // Modern visible seeds use the same xxHash64 root for lobby act selection
        // and RunRngSet. Keeping the authority fields separate still preserves the
        // audited old-seed branch boundary without requiring visible-seed recovery.
        ulong actSelectionRoot = rootHash;
        ulong runSeedRoot = rootHash;
        IReadOnlyList<ModelKey> selectedActs = source.SelectedActs;
        bool selectedExact = source.SelectedActsExact;
        SelectedActProvenance provenance = source.SelectedActProvenance;

        bool rootChanged = !source.RunSeedRootExact || source.RunSeedRoot != rootHash ||
                           !source.ActSelectionRootExact || source.ActSelectionRoot != rootHash;
        // A visible-seed-bound call must preserve the Clean Baseline's
        // seed-identity projection boundary. Root-only internal calls have no
        // visible identity and therefore use the trusted RootHash boundary.
        bool projectionIdentityChanged = seedIdentity.StartsWith("root-hash:", StringComparison.Ordinal)
            ? rootChanged
            : !string.Equals(source.CanonicalSeed, seedIdentity, StringComparison.Ordinal);
        bool selectedActsNeedProjection = projectionIdentityChanged || !source.SelectedActsExact || source.SelectedActs.Count == 0;
        if (selectedActsNeedProjection)
        {
            if (source.CanReconstructSelectedActs)
            {
                var actSelection = Beta109WorldRng.CreateLobbyLocal(actSelectionRoot, "act_selection");
                ModelKey[] projected = source.ActSelectionGroups
                    .OrderBy(group => group.Act)
                    .Select(group => group.SelectionMode switch
                    {
                        Beta109ActSelectionMode.DeterministicFirst => group.EligibleActsInSourceOrder[0],
                        Beta109ActSelectionMode.RandomNextItem => actSelection.NextModelKey(
                            group.EligibleActsInSourceOrder,
                            $"act-selection:act{group.Act}"),
                        _ => throw new InvalidOperationException(
                            "UnsupportedActSelectionMode:Act" + group.Act)
                    })
                    .ToArray();

                // BeginRunLocally consumes the normal Act 1 selection first and only
                // then applies the explicit overgrowth/underdocks override.
                if (projected.Length > 0 && source.Act1OverrideResolvedKey is ModelKey overrideKey && overrideKey.IsValid)
                {
                    projected[0] = overrideKey;
                }

                selectedActs = projected;
                selectedExact = true;
                provenance = SelectedActProvenance.ReconstructedActSelection;
            }
            else if (projectionIdentityChanged)
            {
                selectedActs = Array.Empty<ModelKey>();
                selectedExact = false;
                provenance = SelectedActProvenance.Missing;
            }
        }

        Beta109UpFrontPrefixSnapshot projectedPrefix = source.UpFrontPrefix;
        if (projectionIdentityChanged && source.UpFrontPrefix.Kind == Beta109UpFrontPrefixAuthorityKind.ExactCheckpoint)
        {
            bool canReplayFromRunStart = source.RelicInitializationExact &&
                                         source.SharedRelicPoolOrderExact &&
                                         source.CharacterRelicPoolOrderExact &&
                                         source.RelicRarityAuthorityExact &&
                                         source.PlayerRelicPoolCompositionExact &&
                                         source.SharedRelicBuckets.Count > 0 &&
                                         source.PlayerRelicBuckets.Count > 0 &&
                                         source.SharedRelicBuckets.All(bucket => bucket.OrderExact) &&
                                         source.PlayerRelicBuckets.All(bucket => bucket.OrderExact);
            projectedPrefix = canReplayFromRunStart
                ? new Beta109UpFrontPrefixSnapshot(
                    Beta109UpFrontPrefixAuthorityKind.ReplayFromRunStart,
                    Beta109UpFrontReplayOrigin.RunStartBeforeRelicInitialization,
                    Checkpoint: null,
                    PriorInputsExact: true,
                    EvidenceCode: "PerSeedProjectionUsesExactReplayInputs")
                : Beta109UpFrontPrefixSnapshot.Missing("ExactCheckpointBoundToDifferentSeed");
        }

        Beta109AncientEventContextSnapshot[] projectedAncientContexts = source.AncientEventContexts
            .Select(context =>
            {
                ulong eventRoot = Beta109WorldRng.DeriveEventLocalSeed(
                    runSeedRoot,
                    context.PlayerSlot,
                    context.IsShared,
                    context.EventIdEntry);
                string contextFingerprint = Fingerprint(new[]
                {
                    context.ContextFingerprint,
                    seedIdentity,
                    eventRoot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    source.Profile switch
                    {
                        RuntimeProfileId.Beta111 => "beta111-ancient-option-context-per-seed-v1",
                        RuntimeProfileId.Beta110 => "beta110-ancient-option-context-per-seed-v1",
                        _ => "beta109-ancient-option-context-per-seed-v1"
                    }
                });
                return context with
                {
                    EventRngRoot = eventRoot,
                    EventRngRootExact = true,
                    ContextFingerprint = contextFingerprint
                };
            })
            .ToArray();

        string fingerprint = Fingerprint(new[]
        {
            source.SnapshotFingerprint,
            seedIdentity,
            actSelectionRoot.ToString(System.Globalization.CultureInfo.InvariantCulture),
            runSeedRoot.ToString(System.Globalization.CultureInfo.InvariantCulture),
            provenance.ToString(),
            selectedExact.ToString(),
            string.Join(",", selectedActs.Select(key => key.Serialized)),
            string.Join(",", projectedAncientContexts.Select(context =>
                context.AncientKey.Serialized + "@" + context.Act + "=" + context.EventRngRoot.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            projectedPrefix.Kind.ToString(),
            projectedPrefix.Origin.ToString(),
            projectedPrefix.EvidenceCode,
            source.Profile switch
            {
                RuntimeProfileId.Beta111 => "beta111-world-per-seed-v1",
                RuntimeProfileId.Beta110 => "beta110-world-per-seed-v1",
                _ => "beta109-world-per-seed-v3"
            }
        });

        return source with
        {
            OriginalSeed = seedIdentity,
            CanonicalSeed = seedIdentity,
            ActSelectionRoot = actSelectionRoot,
            ActSelectionRootExact = true,
            RunSeedRoot = runSeedRoot,
            RunSeedRootExact = true,
            RunSeedHashKind = Beta109RunSeedHashKind.ModernXxHash64,
            OldSeedBranchStatus = Beta109OldSeedBranchStatus.NewSeedHashed,
            SelectedActs = selectedActs,
            SelectedActProvenance = provenance,
            SelectedActsExact = selectedExact,
            UpFrontPrefix = projectedPrefix,
            AncientEventContexts = projectedAncientContexts,
            SnapshotFingerprint = fingerprint
        };
    }

    internal static WorldAuthoritySnapshot ProjectAuthorityForRootHash(
        WorldAuthoritySnapshot world, ulong rootHash, string seedIdentity)
    {
        var source = world.Beta109Generation ?? throw new InvalidOperationException("ModernWorldGenerationSnapshotMissing");
        Beta109WorldGenerationSnapshot projectedModern =
            ProjectForRootHash(source, rootHash, seedIdentity);
        string worldFingerprint = Fingerprint(new[]
        {
            world.SnapshotFingerprint,
            projectedModern.SnapshotFingerprint,
            seedIdentity,
            "search-per-seed-modern-world-v2",
            source.Profile.ToString(),
            RuntimeProfilePolicies.AuditFingerprint(source.Profile)
        });
        return world with
        {
            Beta109Generation = projectedModern,
            SnapshotFingerprint = worldFingerprint,
            CapturedAtUtc = world.CapturedAtUtc,
            CaptureDiagnosticCode = string.IsNullOrWhiteSpace(projectedModern.CaptureDiagnosticCode)
                ? "SearchPerSeedModernWorldProjected"
                : projectedModern.CaptureDiagnosticCode
        };
    }

    private static string Fingerprint(IEnumerable<string> values)
    {
        string joined = string.Join("\n", values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
    }
}
