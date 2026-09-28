namespace RolltheSpire2.Search.Predictability;

internal enum SearchPredictabilityConfidence
{
    Unavailable,
    Low,
    Medium,
    High
}

internal enum SearchVerificationFamily
{
    NeowOnly,
    RelicOnly,
    WorldOnly,
    CombatRewardOnly,
    MixedWithoutReward,
    MixedWithReward
}

internal sealed record SearchVerificationEvidence(
    SearchVerificationFamily Family,
    int SearchSampleCount,
    long ExactAttempts,
    long VerifiedMatches,
    double ExactAggregateWorkMs,
    double ExactAcceptanceRate,
    double ExactAttemptsPerVerifiedMatch,
    double AverageExactMsPerAttempt,
    SearchPredictabilityConfidence Confidence,
    string Source,
    DateTimeOffset LastObservedAtUtc)
{
    public bool Usable => SearchSampleCount > 0 && ExactAttempts > 0 && VerifiedMatches > 0 &&
                          ExactAggregateWorkMs > 0d && double.IsFinite(ExactAggregateWorkMs) &&
                          AverageExactMsPerAttempt > 0d && double.IsFinite(AverageExactMsPerAttempt) &&
                          ExactAttemptsPerVerifiedMatch >= 1d && double.IsFinite(ExactAttemptsPerVerifiedMatch);
}

internal sealed record SearchExactTimingEvidence(
    SearchVerificationFamily Family,
    int SearchSampleCount,
    long ExactAttempts,
    double ExactAggregateWorkMs,
    double AverageExactMsPerAttempt,
    SearchPredictabilityConfidence Confidence,
    string Source,
    DateTimeOffset LastObservedAtUtc)
{
    public bool Usable => SearchSampleCount > 0 && ExactAttempts > 0 &&
                          ExactAggregateWorkMs > 0d && double.IsFinite(ExactAggregateWorkMs) &&
                          AverageExactMsPerAttempt > 0d && double.IsFinite(AverageExactMsPerAttempt);
}
