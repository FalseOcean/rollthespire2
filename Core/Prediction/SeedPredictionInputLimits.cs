namespace RolltheSpire2.Core.Prediction;

/// <summary>
/// Shared request-input limits. UI controls consume this contract; they do not
/// duplicate or infer version rules locally.
/// </summary>
public static class SeedPredictionInputLimits
{
    public const int MinimumAscension = 0;
    public const int MaximumAscension = 10;
    public const int MinimumPlayers = 1;
    public const int MaximumPlayers = 4;
    public const int DefaultRelicSequencePreviewCount = 5;
    public const int MaximumRelicSequencePreviewCount = 10;
}
