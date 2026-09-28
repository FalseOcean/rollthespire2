namespace RolltheSpire2.Core.Prediction;

public enum PredictionPrecision
{
    Exact,
    Partial,
    DescriptionOnly,
    Unsupported,
    Unknown
}

public enum SeedPredictionOverallStatus
{
    Completed,
    CompletedWithWarnings,
    Unsupported,
    InvalidRequest,
    Unknown
}
