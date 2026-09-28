using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Neow;

namespace RolltheSpire2.Core.Prediction.Profiles;

public sealed class UnsupportedPredictionProfile : ISeedPredictionProfile
{
    public static UnsupportedPredictionProfile Instance { get; } = new();

    private UnsupportedPredictionProfile() { }

    public RuntimeProfileId ProfileId => RuntimeProfileId.Unsupported;
    public string PredictorId => "UnsupportedAnalysisProfile";

    public SeedPredictionDocument Predict(SeedPredictionRequest request, GameVersionDetection detection) =>
        SeedPredictionDocumentFactory.Create(
            request,
            detection,
            ProfileId,
            PredictorId,
            canonicalSeed: string.Empty,
            choices: Array.Empty<NeowChoiceResult>(),
            warnings: new[] { new PredictionWarning(PredictionWarningCode.UnsupportedGameVersion) },
            diagnostics: Array.Empty<PredictionDiagnostic>(),
            documentViabilityStatus: SeedPredictionOverallStatus.Unsupported);
}
