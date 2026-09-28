using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;

namespace RolltheSpire2.Core.Prediction.Profiles;

/// <summary>
/// Version-bound authoritative prediction from a request and immutable runtime
/// authority. Search validates the resulting domains; Analysis presents them.
/// </summary>
public interface ISeedPredictionProfile
{
    RuntimeProfileId ProfileId { get; }
    // Stable evidence ID; historical string values are not CLR type names.
    string PredictorId { get; }
    SeedPredictionDocument Predict(SeedPredictionRequest request, GameVersionDetection detection);
}
