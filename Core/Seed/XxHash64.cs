using System.Numerics;
using System.Text;

namespace RolltheSpire2.Core.Seed;

/// <summary>Pure xxHash64 implementation used by the Beta109 profile.</summary>
public static class XxHash64
{
    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    public static ulong HashUtf8(string value, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Hash(Encoding.UTF8.GetBytes(value), seed);
    }

    public static ulong Hash(ReadOnlySpan<byte> data, ulong seed)
    {
        unchecked
        {
            int length = data.Length;
            int offset = 0;
            ulong hash;
            if (length >= 32)
            {
                ulong v1 = seed + Prime1 + Prime2;
                ulong v2 = seed + Prime2;
                ulong v3 = seed;
                ulong v4 = seed - Prime1;
                int limit = length - 32;
                while (offset <= limit)
                {
                    v1 = Round(v1, ReadUInt64(data, offset)); offset += 8;
                    v2 = Round(v2, ReadUInt64(data, offset)); offset += 8;
                    v3 = Round(v3, ReadUInt64(data, offset)); offset += 8;
                    v4 = Round(v4, ReadUInt64(data, offset)); offset += 8;
                }

                hash = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) +
                       BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
                hash = MergeRound(hash, v1);
                hash = MergeRound(hash, v2);
                hash = MergeRound(hash, v3);
                hash = MergeRound(hash, v4);
            }
            else
            {
                hash = seed + Prime5;
            }

            hash += (ulong)length;
            while (offset <= length - 8)
            {
                ulong lane = Round(0UL, ReadUInt64(data, offset));
                hash ^= lane;
                hash = BitOperations.RotateLeft(hash, 27) * Prime1 + Prime4;
                offset += 8;
            }

            if (offset <= length - 4)
            {
                hash ^= (ulong)ReadUInt32(data, offset) * Prime1;
                hash = BitOperations.RotateLeft(hash, 23) * Prime2 + Prime3;
                offset += 4;
            }

            while (offset < length)
            {
                hash ^= data[offset] * Prime5;
                hash = BitOperations.RotateLeft(hash, 11) * Prime1;
                offset++;
            }

            hash ^= hash >> 33;
            hash *= Prime2;
            hash ^= hash >> 29;
            hash *= Prime3;
            hash ^= hash >> 32;
            return hash;
        }
    }

    private static ulong Round(ulong accumulator, ulong input)
    {
        unchecked
        {
            accumulator += input * Prime2;
            accumulator = BitOperations.RotateLeft(accumulator, 31);
            return accumulator * Prime1;
        }
    }

    private static ulong MergeRound(ulong accumulator, ulong value)
    {
        unchecked
        {
            accumulator ^= Round(0UL, value);
            return accumulator * Prime1 + Prime4;
        }
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset) =>
        (ulong)data[offset] |
        ((ulong)data[offset + 1] << 8) |
        ((ulong)data[offset + 2] << 16) |
        ((ulong)data[offset + 3] << 24) |
        ((ulong)data[offset + 4] << 32) |
        ((ulong)data[offset + 5] << 40) |
        ((ulong)data[offset + 6] << 48) |
        ((ulong)data[offset + 7] << 56);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset] |
        (data[offset + 1] << 8) |
        (data[offset + 2] << 16) |
        (data[offset + 3] << 24));
}
