using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Prediction.Maps;

/// <summary>Map-local Gaussian adapter over the existing canonical RT2 RNG.</summary>
internal sealed class ReferenceRng(ulong streamSeed, CancellationToken cancellationToken, MapReplayMode mode)
{
    internal MapReplayMode Mode => mode;
    private readonly Xoshiro256StarStar _random = new(streamSeed);
    public int Counter => _random.CallCount;
    public void CheckCancellation() => cancellationToken.ThrowIfCancellationRequested();
    public int NextInt(int maxExclusive) => _random.NextInt(maxExclusive);
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
