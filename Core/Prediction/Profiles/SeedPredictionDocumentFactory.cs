using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction.Profiles;

/// <summary>
/// Builds the immutable Predictor document while keeping its legacy
/// <see cref="SeedPredictionDocument.OverallStatus"/> contract: request/document
/// viability plus the Neow product-facing aggregate, not all-domain health.
/// </summary>
internal static class SeedPredictionDocumentFactory
{
    public static SeedPredictionDocument Create(
        SeedPredictionRequest request,
        GameVersionDetection detection,
        RuntimeProfileId profileId,
        string analyzerId,
        string canonicalSeed,
        IReadOnlyList<NeowChoiceResult> choices,
        IReadOnlyList<PredictionWarning> warnings,
        IReadOnlyList<PredictionDiagnostic> diagnostics,
        SeedPredictionOverallStatus documentViabilityStatus,
        WorldPredictionResult? worldPrediction = null,
        RelicSequencePredictionResult? relicSequencePrediction = null,
        NormalCombatRewardSequencePredictionResult? normalCombatRewardPrediction = null) => new()
    {
        Context = SeedPredictionDocument.CreateContext(request, detection.DisplayVersion, profileId),
        PredictorId = analyzerId,
        OriginalSeed = request.OriginalSeed,
        CanonicalSeed = canonicalSeed,
        Sections = BuildSections(
            choices,
            worldPrediction,
            relicSequencePrediction,
            normalCombatRewardPrediction,
            request.Authority.WorldSnapshotFingerprint),
        Warnings = warnings,
        Diagnostics = request.IncludeDiagnostics
            ? ContextDiagnostics(request)
                .Concat(diagnostics)
                .Concat(worldPrediction?.Diagnostics ?? Array.Empty<PredictionDiagnostic>())
                .Concat(worldPrediction?.EventPoolSequencePrediction?.Diagnostics ?? Array.Empty<PredictionDiagnostic>())
                .Concat(relicSequencePrediction?.Diagnostics ?? Array.Empty<PredictionDiagnostic>())
                .Concat(normalCombatRewardPrediction?.Diagnostics ?? Array.Empty<PredictionDiagnostic>())
                .ToArray()
            : warnings.Any(w => w.Code == PredictionWarningCode.AnalysisFailed)
                ? diagnostics.Where(d => d.Code == PredictionDiagnosticCodes.Exception).ToArray()
                : Array.Empty<PredictionDiagnostic>(),
        OverallStatus = documentViabilityStatus,
        ProductRelevantProjectionPrecision = AggregateProductPrecision(choices),
        ProductRelevantProjectionStatus = AggregateProductStatus(choices),
        FullEffectSemanticsCompleteness = AggregateFullSemantics(choices)
    };

    private static IReadOnlyList<PredictionSection> BuildSections(
        IReadOnlyList<NeowChoiceResult> choices,
        WorldPredictionResult? worldPrediction,
        RelicSequencePredictionResult? relicSequencePrediction,
        NormalCombatRewardSequencePredictionResult? normalCombatRewardPrediction,
        string worldSnapshotFingerprint)
    {
        var sections = new List<PredictionSection>();
        if (choices.Count > 0)
        {
            sections.Add(new PredictionSection(
                PredictionSectionKind.NeowIdentity,
                PredictionDomain.NeowOpening,
                PredictionScope.NeowChoiceIdentityAndMigratedImmediateEffects,
                PredictionSourceState.SeedAndAuthoritySnapshot,
                choices));
        }

        if (worldPrediction is not null)
        {
            sections.Add(new PredictionSection(PredictionSectionKind.EncounterSequences, PredictionDomain.EncounterSequence,
                PredictionScope.InitialEncounterQueues, PredictionSourceState.SeedAndAuthoritySnapshot, [])
            {
                EncounterSequences = worldPrediction.EncounterSequences,
                DomainStatus = worldPrediction.EncounterSequences.Count > 0 ? SeedDomainEvaluationStatus.Evaluated : SeedDomainEvaluationStatus.Unknown,
                AuthoritySnapshotFingerprint = worldSnapshotFingerprint
            });
            sections.Add(new PredictionSection(
                PredictionSectionKind.BossIdentity,
                PredictionDomain.Boss,
                PredictionScope.BossIdentity,
                PredictionSourceState.SeedAndAuthoritySnapshot,
                Array.Empty<NeowChoiceResult>())
            {
                Bosses = worldPrediction.Bosses,
                DomainStatus = worldPrediction.BossStatus,
                IssueCode = worldPrediction.BossIssueCode,
                AuthoritySnapshotFingerprint = worldSnapshotFingerprint
            });
            sections.Add(new PredictionSection(
                PredictionSectionKind.AncientIdentityAndOptions,
                PredictionDomain.Ancient,
                PredictionScope.AncientIdentityAndSeedDeterminedOptions,
                PredictionSourceState.SeedAndAuthoritySnapshot,
                Array.Empty<NeowChoiceResult>())
            {
                Ancients = worldPrediction.Ancients,
                DomainStatus = worldPrediction.AncientStatus,
                IssueCode = worldPrediction.AncientIssueCode,
                AuthoritySnapshotFingerprint = worldSnapshotFingerprint
            });
            EventPoolSequencePredictionResult eventPrediction = worldPrediction.EventPoolSequencePrediction ??
                EventPoolSequencePredictionResult.Unknown(
                    RuntimeProfileId.Unsupported,
                    "EventPoolSequencePredictionMissing",
                    worldSnapshotFingerprint);
            sections.Add(new PredictionSection(
                PredictionSectionKind.EventPoolSequences,
                PredictionDomain.EventPoolSequence,
                PredictionScope.InitialEventCandidateQueues,
                PredictionSourceState.SeedAndAuthoritySnapshot,
                Array.Empty<NeowChoiceResult>())
            {
                EventPoolSequencePrediction = eventPrediction,
                DomainStatus = eventPrediction.Status,
                IssueCode = eventPrediction.IssueCode,
                AuthoritySnapshotFingerprint = eventPrediction.AuthorityFingerprint
            });
        }

        if (relicSequencePrediction is not null)
        {
            sections.Add(new PredictionSection(
                PredictionSectionKind.RelicSequences,
                PredictionDomain.RelicSequence,
                PredictionScope.InitialRelicGrabBagSequences,
                PredictionSourceState.SeedAndAuthoritySnapshot,
                Array.Empty<NeowChoiceResult>())
            {
                RelicSequencePrediction = relicSequencePrediction,
                DomainStatus = relicSequencePrediction.Status,
                IssueCode = relicSequencePrediction.IssueCode,
                AuthoritySnapshotFingerprint = relicSequencePrediction.AuthorityFingerprint
            });
        }
        if (normalCombatRewardPrediction is not null)
        {
            sections.Add(new PredictionSection(
                PredictionSectionKind.NormalCombatRewardSequence,
                PredictionDomain.NormalCombatRewardSequence,
                PredictionScope.OpeningNormalCombatRewardSequence,
                PredictionSourceState.SeedAndAuthoritySnapshot,
                Array.Empty<NeowChoiceResult>())
            {
                NormalCombatRewardSequencePrediction = normalCombatRewardPrediction,
                DomainStatus = normalCombatRewardPrediction.Status,
                IssueCode = normalCombatRewardPrediction.IssueCode,
                AuthoritySnapshotFingerprint = normalCombatRewardPrediction.AuthorityFingerprint
            });
        }
        return sections;
    }

    /// <summary>
    /// Computes the preserved document viability/Neow product-facing status.
    /// Optional World, Relic, and Reward section status remains section-local.
    /// </summary>
    public static SeedPredictionOverallStatus ProductFacingOverallStatus(
        IReadOnlyList<NeowChoiceResult> choices,
        PredictionPrecision identityPrecision,
        bool additionalIdentityAuthorityWarning)
    {
        if (identityPrecision != PredictionPrecision.Exact || additionalIdentityAuthorityWarning)
        {
            return SeedPredictionOverallStatus.CompletedWithWarnings;
        }

        ProductRelevantProjectionStatus status = AggregateProductStatus(choices);
        PredictionPrecision precision = AggregateProductPrecision(choices);
        return status == ProductRelevantProjectionStatus.Evaluated &&
               precision == PredictionPrecision.Exact
            ? SeedPredictionOverallStatus.Completed
            : SeedPredictionOverallStatus.CompletedWithWarnings;
    }

    private static PredictionPrecision AggregateProductPrecision(IReadOnlyList<NeowChoiceResult> choices)
    {
        if (choices.Count == 0)
        {
            return PredictionPrecision.Unknown;
        }
        if (choices.Any(choice => choice.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Unknown ||
                                  choice.ProductRelevantProjectionPrecision == PredictionPrecision.Unknown))
        {
            return PredictionPrecision.Unknown;
        }
        if (choices.Any(choice => choice.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Unsupported ||
                                  choice.ProductRelevantProjectionPrecision == PredictionPrecision.Unsupported))
        {
            return PredictionPrecision.Unsupported;
        }
        if (choices.Any(choice => choice.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy ||
                                  choice.ProductRelevantProjectionPrecision == PredictionPrecision.DescriptionOnly))
        {
            return PredictionPrecision.DescriptionOnly;
        }
        return choices.All(choice => choice.ProductRelevantProjectionPrecision == PredictionPrecision.Exact)
            ? PredictionPrecision.Exact
            : PredictionPrecision.Partial;
    }

    private static ProductRelevantProjectionStatus AggregateProductStatus(IReadOnlyList<NeowChoiceResult> choices)
    {
        if (choices.Count == 0 || choices.Any(choice =>
                choice.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Unknown))
        {
            return ProductRelevantProjectionStatus.Unknown;
        }
        if (choices.Any(choice =>
                choice.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Unsupported))
        {
            return ProductRelevantProjectionStatus.Unsupported;
        }
        if (choices.Any(choice =>
                choice.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy))
        {
            return ProductRelevantProjectionStatus.NotEvaluatedByPolicy;
        }
        return ProductRelevantProjectionStatus.Evaluated;
    }

    private static FullEffectSemanticsCompleteness AggregateFullSemantics(
        IReadOnlyList<NeowChoiceResult> choices)
    {
        if (choices.Count == 0 || choices.Any(choice =>
                choice.FullEffectSemanticsCompleteness == FullEffectSemanticsCompleteness.Unknown))
        {
            return FullEffectSemanticsCompleteness.Unknown;
        }
        if (choices.All(choice =>
                choice.FullEffectSemanticsCompleteness == FullEffectSemanticsCompleteness.Complete))
        {
            return FullEffectSemanticsCompleteness.Complete;
        }
        if (choices.All(choice =>
                choice.FullEffectSemanticsCompleteness == FullEffectSemanticsCompleteness.NotEvaluated))
        {
            return FullEffectSemanticsCompleteness.NotEvaluated;
        }
        return FullEffectSemanticsCompleteness.Partial;
    }

    private static IReadOnlyList<PredictionDiagnostic> ContextDiagnostics(SeedPredictionRequest request)
    {
        var diagnostics = new List<PredictionDiagnostic>
        {
            new(PredictionDiagnosticCodes.RequestId, request.RequestId.Serialized),
            new(PredictionDiagnosticCodes.PlayerSlot, request.PlayerSlotIndex.ToString()),
            new(PredictionDiagnosticCodes.UnlockSnapshotFingerprint, request.Authority.UnlockSnapshotFingerprint),
            new(PredictionDiagnosticCodes.CatalogFingerprint, request.Authority.CatalogFingerprint)
        };
        if (request.Authority.EffectAuthority is { } effects)
        {
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectAuthoritySource, effects.AuthoritySource.ToString()));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectSnapshotFingerprint, effects.SnapshotFingerprint));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectDeckFingerprint, effects.DeckFingerprint));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectRelicBagFingerprint, effects.RelicBagFingerprint));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectPotionPoolFingerprint, effects.PotionPoolFingerprint));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectSnapshotCompleteness, effects.Completeness.ToString()));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectSnapshotCapturedAt,
                effects.CapturedAtUtc?.ToString("O") ?? "not-provided"));
            diagnostics.Add(new(
                PredictionDiagnosticCodes.EffectSnapshotWarnings,
                effects.WarningCodes is { Count: > 0 }
                    ? string.Join(",", effects.WarningCodes.Distinct())
                    : "none"));
            diagnostics.Add(new(
                PredictionDiagnosticCodes.EffectSnapshotCaptureDiagnostic,
                string.IsNullOrWhiteSpace(effects.CaptureDiagnosticCode)
                    ? "not-provided"
                    : effects.CaptureDiagnosticCode));
        }
        if (request.Authority.WorldAuthority is { } world)
        {
            diagnostics.Add(new("world-snapshot-fingerprint", world.SnapshotFingerprint));
            diagnostics.Add(new("world-catalog-fingerprint", world.CatalogFingerprint));
            diagnostics.Add(new("world-snapshot-completeness", world.Completeness.ToString()));
            diagnostics.Add(new("world-snapshot-authority", world.SourceAuthority.ToString()));
            diagnostics.Add(new("world-snapshot-capture-diagnostic", world.CaptureDiagnosticCode));
        }
        return diagnostics;
    }

    public static NeowChoiceResult Choice(
        int slotIndex,
        ModelKey relicKey,
        SeedPredictionRequest request,
        PredictionPrecision identityPrecision,
        EvidenceCode identityEvidenceCode,
        NeowEffectProjection effectProjection)
    {
        ArgumentNullException.ThrowIfNull(effectProjection);

        var warnings = new List<PredictionWarning>(effectProjection.Warnings);
        if (identityPrecision != PredictionPrecision.Exact)
        {
            warnings.Add(new PredictionWarning(
                PredictionWarningCode.SnapshotAuthorityIncomplete,
                relicKey,
                "identity-authority"));
        }

        return new NeowChoiceResult(
            slotIndex,
            relicKey,
            identityPrecision,
            effectProjection.Capability,
            effectProjection.Precision,
            effectProjection.EffectGroups,
            warnings,
            identityEvidenceCode,
            effectProjection.EvidenceCode,
            new ModelSourceMetadata(
                IsVanilla: request.Authority.IsVanilla,
                SourceModId: request.Authority.SourceModId,
                SourceAssembly: request.Authority.SourceAssembly,
                SourceAuthority: request.Authority.SourceAuthority,
                ResolutionStatus: request.Authority.ResolutionStatus),
            effectProjection.BonesOutcome,
            effectProjection.ProductRelevantProjectionPrecision,
            effectProjection.FullEffectSemanticsCompleteness,
            effectProjection.ProductRelevantProjectionStatus,
            effectProjection.OpeningRewardContinuations);
    }
}
