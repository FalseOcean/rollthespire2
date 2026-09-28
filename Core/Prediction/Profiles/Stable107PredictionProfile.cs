using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction.Profiles;

public sealed class Stable107PredictionProfile : ISeedPredictionProfile
{
    public static Stable107PredictionProfile Instance { get; } = new();

    private Stable107PredictionProfile() { }

    public RuntimeProfileId ProfileId => RuntimeProfileId.Stable107;
    public string PredictorId => "Stable107AnalysisProfile";

    public SeedPredictionDocument Predict(SeedPredictionRequest request, GameVersionDetection detection)
    {
        if (request.Authority.ProfileId != ProfileId ||
            !string.Equals(request.Authority.GameVersion, "0.107.1", StringComparison.Ordinal))
        {
            return Fail(request, detection, string.Empty, PredictionWarningCode.InvalidRequestContext, SeedPredictionOverallStatus.Unknown);
        }

        if (!Stable107Profile.Instance.TryCanonicalizeSeed(request.OriginalSeed, out string canonicalSeed, out string issue))
        {
            return SeedPredictionDocument.Invalid(detection.DisplayVersion, ProfileId, PredictorId, request, issue);
        }

        if (request.PlayersCount != 1)
        {
            return Fail(request, detection, canonicalSeed, PredictionWarningCode.LegacyMultiplayerUnsupported, SeedPredictionOverallStatus.Unsupported);
        }

        if (request.Authority.NoRunModifiers == false || request.Authority.VanillaNeowCatalogExact == false)
        {
            return Fail(request, detection, canonicalSeed, PredictionWarningCode.NonVanillaCatalogUnsupported, SeedPredictionOverallStatus.Unsupported);
        }

        if (!request.Authority.CanAnalyzeStable107)
        {
            return Fail(request, detection, canonicalSeed, PredictionWarningCode.SnapshotAuthorityIncomplete, SeedPredictionOverallStatus.Unknown);
        }

        IReadOnlyList<ModelKey> relicKeys;
        int rngCalls;
        try
        {
            relicKeys = Stable107NeowIdentityAnalyzer.Analyze(canonicalSeed, request.Authority.PlayerSlotIndex, out rngCalls);
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

        PredictionPrecision identityPrecision = request.Authority.IsExactStable107
            ? PredictionPrecision.Exact
            : PredictionPrecision.Unknown;
        NeowChoiceResult[] choices = relicKeys.Select((key, index) =>
        {
            var projection = Stable107NeowEffectProjector.Project(key, identityPrecision, request.Authority, canonicalSeed, request.EnableComplexBonesDeckInteractions);
            return SeedPredictionDocumentFactory.Choice(
                index + 1,
                key,
                request,
                identityPrecision,
                "stable107.accepted-neow-identity-oracle",
                projection);
        }).ToArray();

        var warnings = new List<PredictionWarning>();
        warnings.AddRange(choices.SelectMany(choice => choice.Warnings));
        if (identityPrecision != PredictionPrecision.Exact)
        {
            warnings.Add(new PredictionWarning(
                PredictionWarningCode.SnapshotAuthorityIncomplete,
                EvidenceCode: "legacy-identity-authority"));
        }
        PredictionWarning[] distinctWarnings = warnings.Distinct().ToArray();

        WorldPredictionResult? worldPrediction = request.Includes(SeedPredictionDomainSelection.World)
            ? WorldPredictionEngine.Predict(
                Stable107Profile.Instance,
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
                Stable107Profile.Instance,
                canonicalSeed,
                request.Authority.WorldAuthority,
                request.RelicSequencePreviewCount)
            : null;

        NormalCombatRewardSequencePredictionResult? normalCombatRewardPrediction =
            request.Includes(SeedPredictionDomainSelection.NormalCombatRewards)
                ? NormalCombatRewardSequencePredictor.Predict(
                    Stable107Profile.Instance,
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
                new PredictionDiagnostic(PredictionDiagnosticCodes.RngCalls, rngCalls.ToString()),
                new PredictionDiagnostic(PredictionDiagnosticCodes.EventEntry, "NEOW")
            },
            SeedPredictionDocumentFactory.ProductFacingOverallStatus(
                choices,
                identityPrecision,
                additionalIdentityAuthorityWarning: identityPrecision != PredictionPrecision.Exact),
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
