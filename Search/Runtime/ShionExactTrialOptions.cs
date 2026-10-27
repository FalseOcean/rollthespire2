namespace RolltheSpire2.Search.Runtime;

/// <summary>Production Exact optimization, captured once when the request is compiled; off remains a comparison control.</summary>
internal readonly record struct ShionExactTrialOptions(bool OpeningReuse = false)
{
    internal static ShionExactTrialOptions Off => default;
    internal static ShionExactTrialOptions Default => new(OpeningReuse: true);
    internal static ShionExactTrialOptions CaptureEnvironment() => Parse(
        Environment.GetEnvironmentVariable("RT2_SHION_EXACT_TRIAL"));
    internal static ShionExactTrialOptions Parse(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            null or "" => Default,
            "off" => Off,
            "opening-reuse" => new(OpeningReuse: true),
            _ => throw new ArgumentException("Unsupported RT2_SHION_EXACT_TRIAL: " + value +
                "; supported values: off, opening-reuse")
        };
}

internal readonly record struct ShionExactTrialSnapshot(long OpeningReuseCandidates, long IdentityReuses);

/// <summary>Observability only; these counters never participate in result truth.</summary>
internal static class ShionExactTrialMetrics
{
    private static long _openingReuseCandidates, _identityReuses;
    internal static void Candidate() => Interlocked.Increment(ref _openingReuseCandidates);
    internal static void IdentityReused() => Interlocked.Increment(ref _identityReuses);
    internal static ShionExactTrialSnapshot Snapshot() => new(
        Interlocked.Read(ref _openingReuseCandidates), Interlocked.Read(ref _identityReuses));
    internal static void Reset()
    {
        Interlocked.Exchange(ref _openingReuseCandidates, 0);
        Interlocked.Exchange(ref _identityReuses, 0);
    }
}
