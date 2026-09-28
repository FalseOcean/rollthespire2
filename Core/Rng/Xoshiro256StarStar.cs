using System.Numerics;

namespace RolltheSpire2.Core.Rng;

/// <summary>
/// Pure SplitMix64-initialized Xoshiro256** implementation shared by both profiles.
/// NextBool deliberately uses the same bounded draw path as NextInt(2).
/// </summary>
public sealed class Xoshiro256StarStar
{
    private const double DoubleUnit = 1.1102230246251565E-16;
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public Xoshiro256StarStar(ulong seed)
    {
        ulong state = seed;
        _s0 = SplitMix64.Next(ref state);
        _s1 = SplitMix64.Next(ref state);
        _s2 = SplitMix64.Next(ref state);
        _s3 = SplitMix64.Next(ref state);
    }

    private Xoshiro256StarStar(ulong s0, ulong s1, ulong s2, ulong s3, int callCount)
    {
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
        CallCount = callCount;
    }

    public int CallCount { get; private set; }

    public (ulong S0, ulong S1, ulong S2, ulong S3) State => (_s0, _s1, _s2, _s3);

    public Xoshiro256StarStar Clone() => new(_s0, _s1, _s2, _s3, CallCount);

    public static Xoshiro256StarStar FromState(
        ulong s0,
        ulong s1,
        ulong s2,
        ulong s3,
        int callCount = 0)
    {
        if (callCount < 0) throw new ArgumentOutOfRangeException(nameof(callCount));
        return new Xoshiro256StarStar(s0, s1, s2, s3, callCount);
    }

    public void Advance(int calls)
    {
        if (calls < 0) throw new ArgumentOutOfRangeException(nameof(calls));
        for (int index = 0; index < calls; index++)
        {
            _ = NextUInt64();
        }
    }

    public ulong NextUInt64()
    {
        CallCount++;
        return NextUInt64Core();
    }

    public double NextDouble()
    {
        ulong value = NextUInt64();
        return (value >> 11) * DoubleUnit;
    }

    public float NextFloat() => (float)NextDouble();

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        return (int)(NextDouble() * maxExclusive);
    }

    public bool NextBool() => NextInt(2) == 0;

    public void UnstableShuffle<T>(IList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        for (int count = values.Count; count > 1; count--)
        {
            int selected = NextInt(count);
            int tail = count - 1;
            (values[selected], values[tail]) = (values[tail], values[selected]);
        }
    }

    private ulong NextUInt64Core()
    {
        unchecked
        {
            ulong result = BitOperations.RotateLeft(_s1 * 5UL, 7) * 9UL;
            ulong temporary = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= temporary;
            _s3 = BitOperations.RotateLeft(_s3, 45);
            return result;
        }
    }
}
