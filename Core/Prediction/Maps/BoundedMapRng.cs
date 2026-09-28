using System.Numerics;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Prediction.Maps;

// Value-type physical RNG; initialization reuses the canonical SplitMix primitive.
// Differential checks compare all four words with the frozen oracle's consumed stream.
internal struct BoundedMapRng
{
    private ulong _s0, _s1, _s2, _s3;
    internal int Calls { get; private set; }
    internal readonly (ulong S0, ulong S1, ulong S2, ulong S3) State => (_s0, _s1, _s2, _s3);
    internal BoundedMapRng(ulong seed)
    {
        _s0 = SplitMix64.Next(ref seed); _s1 = SplitMix64.Next(ref seed);
        _s2 = SplitMix64.Next(ref seed); _s3 = SplitMix64.Next(ref seed); Calls = 0;
    }
    internal double Double()
    {
        unchecked
        {
            Calls++;
            ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3; _s2 ^= t;
            _s3 = BitOperations.RotateLeft(_s3, 45);
            return (result >> 11) * 1.1102230246251565E-16;
        }
    }
    internal int Int(int upper) => (int)(Double() * upper);
    internal int Gaussian(int mean, int min, int max)
    {
        int result;
        do
        {
            double a = 1 - Double(), b = 1 - Double();
            result = (int)Math.Round(mean + Math.Sqrt(-2 * Math.Log(a)) * Math.Sin(Math.PI * 2 * b));
        } while (result < min || result > max);
        return result;
    }
}
