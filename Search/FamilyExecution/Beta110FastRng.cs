using System.Numerics;
using System.Runtime.CompilerServices;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>Allocation-free SplitMix64-initialized xoshiro256** for Fast kernels.</summary>
internal struct Beta110FastRng
{
    private const double DoubleUnit = 1.1102230246251565E-16;
    private const ulong SplitMixIncrement = 11400714819323198485UL;
    private const ulong SplitMixMultiplier1 = 13787848793156543929UL;
    private const ulong SplitMixMultiplier2 = 10723151780598845931UL;
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public Beta110FastRng(ulong seed)
    {
        ulong state = seed;
        _s0 = SplitMixNext(ref state);
        _s1 = SplitMixNext(ref state);
        _s2 = SplitMixNext(ref state);
        _s3 = SplitMixNext(ref state);
    }

    public Beta110FastRng(UpFrontRngCheckpoint checkpoint)
    {
        _s0 = checkpoint.S0;
        _s1 = checkpoint.S1;
        _s2 = checkpoint.S2;
        _s3 = checkpoint.S3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UpFrontRngCheckpoint CaptureCheckpoint() => new(_s0, _s1, _s2, _s3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int NextInt(int maxExclusive) => (int)(NextDouble() * maxExclusive);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool NextBool() => NextInt(2) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat() => (float)NextDouble();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double NextDouble() => (NextUInt64() >> 11) * DoubleUnit;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnstableShuffle<T>(Span<T> values)
    {
        for (int count = values.Length; count > 1; count--)
        {
            int selected = NextInt(count);
            int tail = count - 1;
            (values[selected], values[tail]) = (values[tail], values[selected]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ConsumeUnstableShuffle(int count)
    {
        for (; count > 1; count--) _ = NextInt(count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong NextUInt64()
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong SplitMixNext(ref ulong state)
    {
        unchecked
        {
            ulong result = state += SplitMixIncrement;
            result = (result ^ (result >> 30)) * SplitMixMultiplier1;
            result = (result ^ (result >> 27)) * SplitMixMultiplier2;
            return result ^ (result >> 31);
        }
    }
}
