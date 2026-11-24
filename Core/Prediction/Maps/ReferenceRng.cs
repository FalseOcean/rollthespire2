using RolltheSpire2.Core.Rng;
using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Core.Prediction.Maps;

/// <summary>Map-local Gaussian adapter over the existing canonical RT2 RNG.</summary>
internal sealed class ReferenceRng(ulong streamSeed, CancellationToken cancellationToken, MapReplayMode mode,
    RngSemanticProfileId semanticProfile = RngSemanticProfileId.Modern110)
{
    internal MapReplayMode Mode => mode;
    internal bool UsesChildShuffle => RuntimeProfilePolicies.UsesChildStreamShuffle(semanticProfile);
    private readonly Xoshiro256StarStar _random = new(streamSeed);
    public int Counter => _random.CallCount;
    public void CheckCancellation() => cancellationToken.ThrowIfCancellationRequested();
    public int NextInt(int maxExclusive) => _random.NextInt(maxExclusive);
    internal void Shuffle<T>(IList<T> values) => _random.UnstableShuffle(values, semanticProfile);
    public int NextInt(int minInclusive, int maxExclusive) =>
        (int)(_random.NextDouble() * ((long)maxExclusive - minInclusive)) + minInclusive;

    // Beta111 Rng.NextGaussianInt: two double draws per attempt, ToEven rounding,
    // inclusive rejection bounds. Do not substitute a cached-normal sampler.
    public int NextGaussianInt(int mean, int stdDev, int min, int max)
    {
        int result;
        do
        {
            CheckCancellation();
            double d = 1.0 - _random.NextDouble();
            double d2 = 1.0 - _random.NextDouble();
            double normal = Math.Sqrt(-2.0 * Math.Log(d)) * Math.Sin(Math.PI * 2.0 * d2);
            result = (int)Math.Round(mean + stdDev * normal);
        } while (result < min || result > max);
        return result;
    }
}
