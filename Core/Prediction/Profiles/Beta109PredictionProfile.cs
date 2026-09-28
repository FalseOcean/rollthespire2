using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction.Profiles;

public sealed class Beta109PredictionProfile : ISeedPredictionProfile
{
    public static Beta109PredictionProfile Instance { get; } = new();

    private Beta109PredictionProfile() { }

    public RuntimeProfileId ProfileId => RuntimeProfileId.Beta109;
    public string PredictorId => "Beta109AnalysisProfile";

    public SeedPredictionDocument Predict(SeedPredictionRequest request, GameVersionDetection detection)
    {
        RuntimeVersionResolution compatibility = new(
            GameVersionIdentity.From(detection.NormalizedVersion),
            RuntimeProfileId.Beta109,
            RngSemanticProfileId.Modern109,
            CompatibilityConfidence.Unsupported,
            CompatibilityDomainMask.All,
            RuntimeVersionSupportKind.HistoricalBetaDonor,
            detection.NormalizedVersion,
            detection.NormalizedVersion,
            "direct-historical-donor-analysis:" + detection.NormalizedVersion);
        if (request.Authority.ProfileId != ProfileId ||
            !(RuntimeVersionCompatibility.IsBeta109_0(detection.NormalizedVersion) || RuntimeVersionCompatibility.IsBeta109_1(detection.NormalizedVersion)) ||
            !(RuntimeVersionCompatibility.IsBeta109_0(request.Authority.GameVersion) || RuntimeVersionCompatibility.IsBeta109_1(request.Authority.GameVersion)) ||
            !string.Equals(request.Authority.GameVersion, detection.NormalizedVersion, StringComparison.Ordinal))
        {
            return Fail(request, detection, string.Empty, PredictionWarningCode.InvalidRequestContext, SeedPredictionOverallStatus.Unknown);
        }

        if (!Beta109Profile.Instance.TryCanonicalizeSeed(request.OriginalSeed, out string canonicalSeed, out string issue))
        {
            return SeedPredictionDocument.Invalid(detection.DisplayVersion, ProfileId, PredictorId, request, issue);
        }

        if (request.Authority.NoRunModifiers == false || request.Authority.VanillaNeowCatalogExact == false)
        {
            return Fail(request, detection, canonicalSeed, PredictionWarningCode.NonVanillaCatalogUnsupported, SeedPredictionOverallStatus.Unsupported);
        }

        if (!request.Authority.HasCoreNoModifierCatalogAuthority)
        {
            return Fail(request, detection, canonicalSeed, PredictionWarningCode.SnapshotAuthorityIncomplete, SeedPredictionOverallStatus.Unknown);
        }

        ModernNeowIdentityResult result;
        try
        {
            result = ModernNeowIdentityPredictor.PredictBeta109(canonicalSeed, request.Authority);
        }
        catch (Exception ex)
        {
            return Fail(
                request,
                detection,
                canonicalSeed,
                PredictionWarningCode.AnalysisFailed,
                SeedPredictionOverallStatus.Unknown,
                new PredictionDiagnostic(PredictionDiagnosticCodes.Exception, $"{ex.GetType().Name}:{ex.Message}"));
        }

        PredictionPrecision identityPrecision = result.ExactAuthority
            ? PredictionPrecision.Exact
            : PredictionPrecision.Unknown;
        NeowChoiceResult[] choices = result.RelicKeys.Select((key, index) =>
        {
            var projection = Beta109NeowEffectProjector.Project(key, identityPrecision, request.Authority, canonicalSeed, request.EnableComplexBonesDeckInteractions);
            return SeedPredictionDocumentFactory.Choice(
                index + 1,
                key,
                request,
                identityPrecision,
                "beta109.g10ab6-p1-neow-identity",
                projection);
        }).ToArray();

        var warnings = new List<PredictionWarning>();
        warnings.AddRange(choices.SelectMany(choice => choice.Warnings));
        if (result.UnknownEligibilityAffectedPool)
        {
            warnings.Add(new PredictionWarning(
                PredictionWarningCode.ModernEligibilityIncomplete,
                EvidenceCode: "beta109-eligibility"));
        }
        else if (!result.ExactAuthority)
        {
            warnings.Add(new PredictionWarning(
                PredictionWarningCode.SnapshotAuthorityIncomplete,
                EvidenceCode: "beta109-identity-authority"));
        }
        PredictionWarning[] distinctWarnings = warnings.Distinct().ToArray();

        WorldPredictionResult? worldPrediction = request.Includes(SeedPredictionDomainSelection.World)
            ? WorldPredictionEngine.Predict(
                Beta109Profile.Instance,
                canonicalSeed,
                request.Ascension,
                request.Authority.PlayerSlotIndex,
                request.Character.CharacterKey,
                request.Authority.WorldAuthority,
                request.Authority.EffectAuthority,
                request.AncientOptionConditions)
            : null;

        RelicSequencePredictionResult? relicSequencePrediction = request.Includes(SeedPredictionDomainSelection.RelicSequence)
            ? RelicSequencePredictor.Predict(
                Beta109Profile.Instance,
                canonicalSeed,
                request.Authority.WorldAuthority,
                request.RelicSequencePreviewCount)
            : null;

        NormalCombatRewardSequencePredictionResult? normalCombatRewardPrediction =
            request.Includes(SeedPredictionDomainSelection.NormalCombatRewards)
                ? NormalCombatRewardSequencePredictor.Predict(
                    Beta109Profile.Instance,
                    canonicalSeed,
                    request.Authority.PlayerSlotIndex,
                    request.Ascension,
                    choices,
                    request.Authority.EffectAuthority,
                    request.Authority.WorldAuthority)
                : null;

        return SeedPredictionDocumentFactory.Create(
            request,
            detection,
            ProfileId,
            PredictorId,
            canonicalSeed,
            choices,
            distinctWarnings,
            new[]
            {
                new PredictionDiagnostic(PredictionDiagnosticCodes.Analyzer, PredictorId),
                new PredictionDiagnostic(PredictionDiagnosticCodes.RngCalls, result.RngCalls.ToString()),
                new PredictionDiagnostic(PredictionDiagnosticCodes.EventEntry, "NEOW"),
                new PredictionDiagnostic(PredictionDiagnosticCodes.EligibilityExact, result.ExactAuthority.ToString().ToLowerInvariant()),
                new PredictionDiagnostic(PredictionDiagnosticCodes.RuntimeCompatibilityMode, compatibility.SupportKind.ToString()),
                new PredictionDiagnostic(PredictionDiagnosticCodes.RuntimeCompatibilityReferenceVersion, compatibility.ReferenceVersion)
            },
            SeedPredictionDocumentFactory.ProductFacingOverallStatus(
                choices,
                identityPrecision,
                additionalIdentityAuthorityWarning:
                    result.UnknownEligibilityAffectedPool ||
                    !result.ExactAuthority),
            worldPrediction,
            relicSequencePrediction,
            normalCombatRewardPrediction);
    }

    private SeedPredictionDocument Fail(
        SeedPredictionRequest request,
        GameVersionDetection detection,
        string canonicalSeed,
        PredictionWarningCode warning,
        SeedPredictionOverallStatus status,
        PredictionDiagnostic? diagnostic = null) =>
        SeedPredictionDocumentFactory.Create(
            request,
            detection,
            ProfileId,
            PredictorId,
            canonicalSeed,
            Array.Empty<NeowChoiceResult>(),
            new[] { new PredictionWarning(warning) },
            diagnostic is null ? Array.Empty<PredictionDiagnostic>() : new[] { diagnostic },
            status);
}
