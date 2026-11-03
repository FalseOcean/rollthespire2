using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Coverage;
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
    public static SeedPredictionDocument FromFacts(SeedPredictionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var choices = facts.Neow?.Choices ?? [];
        return new()
        {
            Context = facts.Context, PredictorId = facts.PredictorId,
            OriginalSeed = facts.OriginalSeed, CanonicalSeed = facts.CanonicalSeed,
            Sections = BuildSections(choices, facts.World, facts.Relics, facts.CombatRewards,
                facts.WorldAuthorityFingerprint, facts.Neow?.Status, facts.Neow?.IssueCode ?? ""),
            Warnings = facts.Warnings, Diagnostics = facts.Diagnostics, Party = facts.Party,
            OverallStatus = facts.InputStatus switch
            {
                SeedPredictionInputStatus.InvalidRequest => SeedPredictionOverallStatus.InvalidRequest,
                SeedPredictionInputStatus.Unsupported => SeedPredictionOverallStatus.Unsupported,
                SeedPredictionInputStatus.Unknown => SeedPredictionOverallStatus.Unknown,
                _ => ProductFacingOverallStatus(choices, facts.Neow?.IdentityPrecision ?? PredictionPrecision.Unknown,
                    facts.Neow?.HasIdentityAuthorityWarning ?? false)
            },
            ProductRelevantProjectionPrecision = AggregateProductPrecision(choices),
            ProductRelevantProjectionStatus = AggregateProductStatus(choices),
            FullEffectSemanticsCompleteness = AggregateFullSemantics(choices)
        };
    }

    private static IReadOnlyList<PredictionSection> BuildSections(
        IReadOnlyList<NeowChoiceResult> choices,
        WorldPredictionResult? worldPrediction,
        RelicSequencePredictionResult? relicSequencePrediction,
        NormalCombatRewardSequencePredictionResult? normalCombatRewardPrediction,
        string worldSnapshotFingerprint,
        SeedDomainEvaluationStatus? neowStatus,
        string neowIssueCode)
    {
        var sections = new List<PredictionSection>();
        if (choices.Count > 0 || neowStatus.HasValue)
        {
            sections.Add(new PredictionSection(
                PredictionSectionKind.NeowIdentity,
                PredictionDomain.NeowOpening,
                PredictionScope.NeowChoiceIdentityAndMigratedImmediateEffects,
                PredictionSourceState.SeedAndAuthoritySnapshot,
                choices)
            {
                DomainStatus = neowStatus ?? SeedDomainEvaluationStatus.Evaluated,
                IssueCode = neowIssueCode
            });
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
    private static SeedPredictionOverallStatus ProductFacingOverallStatus(
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

}
