using RolltheSpire2.Core.Authority;
using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Runtime;

/// <summary>
/// Pure worker-side reprojection of seed-dependent copied authority. It never reads
/// ModelDb, RunState, Player, Godot objects, or a live game RNG.
/// </summary>
public static class SearchAuthorityProjector
{
    internal static bool TryProjectForRootHash(
        SearchExecutionRequest legacyPlan,
        ulong rootHash,
        string seedIdentity,
        out RuntimeContextAuthoritySnapshot projected,
        out SearchDisposition disposition,
        out string issue) => TryProjectForRootHash(
            new ExactSearchExecutionRequest(
                legacyPlan.CompiledSearch,
                legacyPlan.RunOptions,
                legacyPlan.CanonicalStartSeed,
                legacyPlan.ResolvedScanCount,
                ExactSearchEvaluationProjection.FromLegacyFilter(legacyPlan.Filter),
                legacyPlan.CombatRewardRoutePolicy,
                legacyPlan.SnapshotFingerprint),
            rootHash,
            seedIdentity,
            out projected,
            out disposition,
            out issue);

    public static bool TryProjectForSeed(
        ExactSearchExecutionRequest plan,
        string canonicalSeed,
        out RuntimeContextAuthoritySnapshot projected,
        out SearchDisposition disposition,
        out string issue)
    {
        IRuntimeProfile profile = RuntimeProfileRegistry.Select(plan.Detection);
        if (profile.ProfileId != plan.ProfileId)
        {
            projected = plan.Authority;
            disposition = SearchDisposition.Unsupported;
            issue = "ProfileSelectionMismatch";
            return false;
        }
        return TryProjectForRootHash(
            plan,
            profile.ComputeRootSeed(canonicalSeed),
            canonicalSeed,
            out projected,
            out disposition,
            out issue);
    }

    internal static bool TryProjectForRootHash(
        ExactSearchExecutionRequest plan,
        ulong rootHash,
        string seedIdentity,
        out RuntimeContextAuthoritySnapshot projected,
        out SearchDisposition disposition,
        out string issue)
    {
        projected = plan.Authority;
        disposition = SearchDisposition.Unknown;
        issue = string.Empty;

        IRuntimeProfile profile = RuntimeProfileRegistry.Select(plan.Detection);
        if (profile.ProfileId != plan.ProfileId)
        {
            disposition = SearchDisposition.Unsupported;
            issue = "ProfileSelectionMismatch";
            return false;
        }

        if (plan.Evaluation.RequiresEffectColdPath)
        {
            NeowEffectAuthoritySnapshot? source = projected.EffectAuthority;
            if (source is null)
            {
                issue = "EffectAuthorityMissing";
                return false;
            }

            // A generated-card-only opening effect (for example Arcane Scroll) does
            // not consume the RelicGrabBag. Do not make an unrelated Mod relic-pool
            // uncertainty reject every Production Exact candidate. Rebuild the bag
            // only for query shapes whose exact opening route can actually consume it.
            if (RequiresPerSeedRelicBagProjection(plan.Evaluation) && source.HasExactRelicBagSourcePools)
            {
                IReadOnlyList<NeowEffectRelicSnapshot> ordered = NeowRewardGenerator.BuildOrderedRelicBag(
                    profile,
                    rootHash,
                    projected);
                string relicFingerprint = Fingerprint(ordered.Select(RelicDescriptor));
                string snapshotFingerprint = Fingerprint(new[]
                {
                    source.SnapshotFingerprint,
                    seedIdentity,
                    relicFingerprint,
                    "search-per-seed-relic-bag-v1"
                });
                NeowEffectAuthoritySnapshot projectedEffects = source with
                {
                    OrderedRelicBag = ordered,
                    RelicBagExact = true,
                    RelicBagFingerprint = relicFingerprint,
                    SnapshotFingerprint = snapshotFingerprint,
                    CapturedAtUtc = source.CapturedAtUtc,
                    CaptureDiagnosticCode = "SearchPerSeedRelicBagProjected"
                };
                projected = projected.WithEffectAuthority(projectedEffects);
            }
            else
            {
                // Missing bag authority is domain-local. For example, Bones pair
                // identity can still be predicted; requested Capsule outputs will
                // remain Unknown in their own Predictor/validation boundary.
                projected = PreserveSeedIndependentEffectFoundation(projected);
            }
        }
        else
        {
            projected = PreserveSeedIndependentEffectFoundation(projected);
        }

        if (plan.Evaluation.RequiresWorldAuthority && RuntimeProfilePolicies.IsModernCore(plan.ProfileId))
        {
            WorldAuthoritySnapshot? world = projected.WorldAuthority;
            Beta109WorldGenerationSnapshot? source = world?.Beta109Generation;
            if (world is null || source is null)
            {
                issue = "ModernWorldGenerationSnapshotMissing";
                return false;
            }

            projected = projected.WithWorldAuthority(
                Beta109WorldSnapshotProjector.ProjectAuthorityForRootHash(world, rootHash, seedIdentity));
        }

        disposition = SearchDisposition.NoMatch;
        return true;
    }

    private static bool RequiresPerSeedRelicBagProjection(ExactSearchEvaluationProjection filter)
    {
        bool bones =
            filter.RequireNeowsBones ||
            !filter.BonesRelics.IsEmpty ||
            filter.RequiredBonesCombination.Count > 0 ||
            filter.RequiredBonesAcquisitionOrder.Count > 0 ||
            filter.NeowRoute?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones;
        if (bones) return true;

        if (filter.RequireSmallCapsule || filter.RequireLargeCapsule || !filter.CapsuleContainedRelics.IsEmpty)
            return true;

        return filter.StructuredNeowEffects.Any(condition =>
            condition.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule ||
            condition.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule);
    }

    public static RuntimeContextAuthoritySnapshot PreserveSeedIndependentEffectFoundation(
        RuntimeContextAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        NeowEffectAuthoritySnapshot? source = authority.EffectAuthority;
        if (source is null)
        {
            return authority;
        }

        if (source.OrderedRelicBag is null && !source.RelicBagExact)
        {
            return authority;
        }

        string snapshotFingerprint = Fingerprint(new[]
        {
            source.SnapshotFingerprint,
            source.CatalogFingerprint,
            source.UnlockFingerprint,
            "search-seed-independent-effect-foundation-v1"
        });
        return authority.WithEffectAuthority(source with
        {
            OrderedRelicBag = null,
            RelicBagExact = false,
            RelicBagFingerprint = string.Empty,
            SnapshotFingerprint = snapshotFingerprint,
            CaptureDiagnosticCode = "SearchSeedIndependentEffectFoundation"
        });
    }

    private static string RelicDescriptor(NeowEffectRelicSnapshot relic) => string.Join("|", new[]
    {
        relic.RelicKey.Serialized,
        relic.BagOrder.ToString(System.Globalization.CultureInfo.InvariantCulture),
        relic.Rarity.ToString(),
        relic.RarityCode,
        relic.NestedEffectKind.ToString(),
        relic.NestedClassificationExact.ToString()
    });

    private static string Fingerprint(IEnumerable<string> values)
    {
        string joined = string.Join("\n", values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
    }
}
