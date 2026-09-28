using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Shadow;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;

namespace RolltheSpire2.Core.Effects;

internal sealed partial class NeowEffectProjectionEngine
{
    private NeowEffectProjection AttachOpeningRewardContinuations(
        ModelKey rootRelicKey,
        NeowEffectWorkingState state,
        NeowEffectProjection projection)
    {
        if (rootRelicKey == BaseGameModelKeys.Relics.NeowsBones)
        {
            OpeningRewardContinuation[] bonesRoutes = projection.BonesOutcome?.OriginalRoutes
                .Select(route => route.OpeningRewardContinuation)
                .Where(route => route is not null)
                .Cast<OpeningRewardContinuation>()
                .OrderBy(route => route.Route.RouteOrder)
                .ThenBy(route => route.Route.RouteId, StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<OpeningRewardContinuation>();
            if (bonesRoutes.Length == 0)
            {
                EvidenceCode evidence = Evidence(rootRelicKey, "opening-reward-continuation-bones-missing");
                return projection with
                {
                    OpeningRewardContinuations = OpeningRewardContinuationAnalysis.Unknown(
                        rootRelicKey,
                        "OpeningRewardContinuationBonesRoutesMissing",
                        evidence)
                };
            }

            return projection with
            {
                OpeningRewardContinuations = new OpeningRewardContinuationAnalysis(
                    bonesRoutes,
                    AggregateOpeningRewardPrecision(bonesRoutes),
                    string.Empty,
                    Evidence(rootRelicKey, "opening-reward-continuation-bones-routes"))
            };
        }

        EffectDependencyImpact impact = ResolveProjectionDependencyImpact(rootRelicKey, projection);
        ModelKey[] nestedRewardContextRelics = ExtractNestedRelicKeys(projection.EffectGroups);
        OpeningRewardContinuation continuation = BuildOpeningRewardContinuation(
            routeId: $"opening.direct.{rootRelicKey.Serialized}",
            routeOrder: 0,
            routeKind: OpeningRewardRouteKind.DirectNeowChoice,
            rootRelicKey,
            acquisitionOrder: new[] { rootRelicKey },
            state,
            impact.RewardsRng,
            impact.UnknownHook,
            impact.NestedObtain,
            new[] { projection.EvidenceCode, Evidence(rootRelicKey, "opening-reward-continuation-direct") },
            nestedRewardContextRelics,
            sharedStateExact: impact.NicheRng.IsExact && impact.UnknownHook.IsExact && impact.NestedObtain.IsExact,
            executedNestedObtainRelics: ExecutedNestedObtainRelics(rootRelicKey, projection.EffectGroups));
        return projection with
        {
            OpeningRewardContinuations = new OpeningRewardContinuationAnalysis(
                new[] { continuation },
                continuation.Precision,
                continuation.UnknownReasonCodes.FirstOrDefault() ?? string.Empty,
                Evidence(rootRelicKey, "opening-reward-continuation-direct-analysis"))
        };
    }

    private OpeningRewardContinuation BuildOpeningRewardContinuation(
        string routeId,
        int routeOrder,
        OpeningRewardRouteKind routeKind,
        ModelKey rootRelicKey,
        IReadOnlyList<ModelKey> acquisitionOrder,
        NeowEffectWorkingState state,
        BonesContinuationDomainState rewardsContinuity,
        BonesContinuationDomainState unknownHookContinuity,
        BonesContinuationDomainState nestedObtainContinuity,
        IReadOnlyList<EvidenceCode> additionalEvidence,
        IReadOnlyList<ModelKey>? nestedRewardContextRelics = null,
        bool sharedStateExact = true,
        IReadOnlyList<ModelKey>? executedNestedObtainRelics = null)
    {
        ModelKey[] nestedRelics = (nestedRewardContextRelics ?? Array.Empty<ModelKey>())
            .Where(key => key.IsValid)
            .Distinct()
            .ToArray();
        ModelKey[] obtainedRelics = new[] { rootRelicKey }
            .Concat(acquisitionOrder)
            .Concat(nestedRelics)
            .Where(key => key.IsValid)
            .Distinct()
            .ToArray();
        OpeningRewardSupportEvaluation capabilityEvaluation =
            OpeningRewardSupport.EvaluateRoute(
                _profileId,
                obtainedRelics,
                executedNestedObtainRelics ?? nestedRelics,
                key => IsKnownModdedRelic(state, key));
        OpeningRewardSupportFlags capabilities = capabilityEvaluation.Capabilities;
        if (state.Rng.Rewards.CallCount > 0)
        {
            capabilities |= OpeningRewardSupportFlags.ConsumesRewardsRng;
        }

        var unknownReasons = capabilityEvaluation.ReasonCodes.ToList();
        if (rewardsContinuity.Status is BonesContinuityStatus.Unknown or BonesContinuityStatus.Invalidated)
        {
            unknownReasons.Add("OpeningRewardsRngContinuity" + rewardsContinuity.Status);
        }

        // Full Neow/Bones semantics may remain incomplete while the normal combat
        // reward domain is proven independent. Do not let a deck/HP/Niche-only
        // nested obtain contaminate the copied Rewards continuation.
        bool genericHookContinuityIncomplete =
            !unknownHookContinuity.IsExact || !nestedObtainContinuity.IsExact;
        if (genericHookContinuityIncomplete &&
            !capabilityEvaluation.RewardDomainResolved &&
            unknownReasons.Count == 0)
        {
            capabilities |= OpeningRewardSupportFlags.IntroducesUnknownRewardHook;
            unknownReasons.Add("OpeningRouteIntroducesUnknownRewardHook");
        }
        if ((capabilities & OpeningRewardSupportFlags.ChangesPotionDropState) != 0)
        {
            unknownReasons.Add("OpeningRouteChangesPotionDropStateWithoutCapturedValue");
        }

        PredictionPrecision precision = rewardsContinuity.Status switch
        {
            BonesContinuityStatus.Exact when unknownReasons.Count == 0 => PredictionPrecision.Exact,
            BonesContinuityStatus.Partial when unknownReasons.Count == 0 => PredictionPrecision.Partial,
            _ => PredictionPrecision.Unknown
        };
        RewardsRngStateSnapshot? rngState = rewardsContinuity.Status is BonesContinuityStatus.Exact or BonesContinuityStatus.Partial
            ? RewardsRngStateSnapshot.Capture(state.Rng.Rewards)
            : null;
        const float potionRewardState = 0.4f;
        string rewardContextFingerprint = FingerprintOpeningRewardParts(
            "opening-reward-context-v1",
            capabilities.ToString(),
            state.Authority.CatalogFingerprint,
            state.Authority.UnlockFingerprint,
            state.Authority.PotionPoolFingerprint,
            state.Authority.SourceAuthority.ToString(),
            state.Authority.Completeness.ToString(),
            capabilityEvaluation.RewardImpactFingerprint,
            $"silken-tress-consumed:{state.SilkenTressConsumed}",
            string.Join(",", capabilityEvaluation.SupportedImpactSources
                .OrderBy(source => source.Serialized, StringComparer.Ordinal)
                .Select(source => source.Serialized)),
            string.Join(",", unknownReasons.OrderBy(reason => reason, StringComparer.Ordinal)));
        string continuationFingerprint = rngState is null
            ? FingerprintOpeningRewardParts(
                "opening-reward-continuation-unknown-v1",
                routeId,
                rewardContextFingerprint,
                string.Join(",", unknownReasons.OrderBy(reason => reason, StringComparer.Ordinal)))
            : FingerprintOpeningRewardParts(
                "opening-reward-continuation-v1",
                rngState.Fingerprint,
                potionRewardState.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                rewardContextFingerprint);

        EvidenceCode[] evidence = rewardsContinuity.EvidenceCodes
            .Concat(unknownHookContinuity.EvidenceCodes)
            .Concat(nestedObtainContinuity.EvidenceCodes)
            .Concat(capabilityEvaluation.EvidenceCodes)
            .Concat(genericHookContinuityIncomplete && capabilityEvaluation.RewardDomainResolved
                ? new[]
                {
                    Evidence(
                        rootRelicKey,
                        capabilityEvaluation.SupportedImpactSources.Count == 0
                            ? "opening-reward-domain-independent-from-generic-hook-continuity"
                            : "opening-reward-domain-resolved-by-special-impact-adapter")
                }
                : Array.Empty<EvidenceCode>())
            .Concat(additionalEvidence)
            .Where(code => code.IsValid)
            .Distinct()
            .ToArray();
        EvidenceCode routeEvidence = evidence.FirstOrDefault(code => code.IsValid);
        if (!routeEvidence.IsValid)
        {
            routeEvidence = Evidence(rootRelicKey, "opening-reward-continuation");
        }

        return new OpeningRewardContinuation(
            new OpeningRewardRouteDescriptor(
                routeId,
                routeOrder,
                routeKind,
                rootRelicKey,
                acquisitionOrder.ToArray(),
                precision,
                routeEvidence),
            rngState,
            state.Rng.Rewards.CallCount,
            potionRewardState,
            capabilities,
            rewardContextFingerprint,
            continuationFingerprint,
            precision,
            state.Authority.SourceAuthority,
            state.Authority.Completeness,
            evidence,
            unknownReasons.Distinct(StringComparer.Ordinal).ToArray())
        {
            NicheState = sharedStateExact ? RewardsRngStateSnapshot.Capture(state.Rng.Niche) : null,
            CombatPotionGenerationState = sharedStateExact ? RewardsRngStateSnapshot.Capture(state.Rng.CombatPotionGeneration) : null,
            SilkenTressConsumedDuringOpening = state.SilkenTressConsumed,
            ActiveRewardImpactSources = capabilityEvaluation.SupportedImpactSources,
            RewardImpactFingerprint = capabilityEvaluation.RewardImpactFingerprint
        };
    }


    private static bool? IsKnownModdedRelic(
        NeowEffectWorkingState state,
        ModelKey relicKey)
    {
        IEnumerable<NeowEffectRelicSnapshot> candidates =
            (state.Authority.OrderedRelicBag ?? Array.Empty<NeowEffectRelicSnapshot>())
                .Concat(state.Authority.SharedRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>())
                .Concat(state.Authority.CharacterRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>());
        NeowEffectRelicSnapshot? snapshot = candidates
            .FirstOrDefault(item => item.RelicKey == relicKey);
        if (snapshot is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SourceModId))
        {
            return true;
        }
        if (!string.IsNullOrWhiteSpace(snapshot.SourceAssembly))
        {
            return !string.Equals(snapshot.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase);
        }
        return null;
    }


    // All drawn relics keep their held reward effects. Only the W/WP obtain
    // whitelist (further restricted by an authored premise) is replayed here.
    private ModelKey[] ExecutedNestedObtainRelics(ModelKey source, IEnumerable<PredictedEffectGroup> groups)
    {
        var keys = ExtractNestedRelicKeys(groups).Where(TracksCapsuleObtain).ToArray();
        if (AuthoredCapsuleEffects is null || source != BaseGameModelKeys.Relics.SmallCapsule &&
            source != BaseGameModelKeys.Relics.LargeCapsule) return keys;
        return AuthoredCapsuleEffects.TryGetValue(source, out var enabled)
            ? keys.Where(enabled.Contains).ToArray() : [];
    }

    private static ModelKey[] ExtractNestedRelicKeys(
        IEnumerable<PredictedEffectGroup> groups) => groups
        .SelectMany(group => group.OrderedItems)
        .Where(item => item.Kind == PredictedEffectKind.AddRelic &&
                       item.Relation == PredictedEffectRelation.NestedRelic &&
                       item.TargetKey.HasValue &&
                       item.TargetKey.Value.IsValid)
        .Select(item => item.TargetKey!.Value)
        .Distinct()
        .OrderBy(key => key.Serialized, StringComparer.Ordinal)
        .ToArray();

    private static PredictionPrecision AggregateOpeningRewardPrecision(
        IReadOnlyList<OpeningRewardContinuation> routes)
    {
        if (routes.Count == 0 || routes.All(route => route.Precision == PredictionPrecision.Unknown))
        {
            return PredictionPrecision.Unknown;
        }
        return routes.All(route => route.Precision == PredictionPrecision.Exact)
            ? PredictionPrecision.Exact
            : PredictionPrecision.Partial;
    }

    private static string FingerprintOpeningRewardParts(params string[] parts)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join("|", parts));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
