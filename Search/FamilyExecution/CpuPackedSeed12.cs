using System.Numerics;

namespace RolltheSpire2.Search.FamilyExecution;

// Invocation-local representation of the twelve visible ASCII bytes. Neither
// this state nor roots are retained across Family stages or shared by workers.
internal struct CpuPackedSeed12
{
    internal ulong First8 { get; private set; }
    internal uint Tail4 { get; private set; }

    internal static CpuPackedSeed12 FromOrdinal(ulong ordinal)
    {
        if (ordinal >= Beta110SeedCodec.SpaceSize) throw new ArgumentOutOfRangeException(nameof(ordinal));
        uint tail = 0;
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            tail |= (uint)Beta110SeedCodec.Alphabet[(int)(ordinal % 34)] << shift;
            ordinal /= 34;
        }
        ulong first = 0;
        for (int shift = 56; shift >= 0; shift -= 8)
        {
            first |= (ulong)Beta110SeedCodec.Alphabet[(int)(ordinal % 34)] << shift;
            ordinal /= 34;
        }
        return new() { First8 = first, Tail4 = tail };
    }

    internal bool Advance()
    {
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            uint value = (Tail4 >> shift) & 255;
            Tail4 = (Tail4 & ~(255U << shift)) | (Next(value) << shift);
            if (value != 'Z') return true;
        }
        for (int shift = 56; shift >= 0; shift -= 8)
        {
            uint value = (uint)(First8 >> shift) & 255;
            First8 = (First8 & ~(255UL << shift)) | ((ulong)Next(value) << shift);
            if (value != 'Z') return true;
        }
        return false;
    }

    private static uint Next(uint value) => value switch
    {
        '9' => 'A', 'H' => 'J', 'N' => 'P', 'Z' => '0', _ => value + 1
    };

    // xxHash64(seed=0), consuming the already packed little-endian 8/4 lanes.
    // General-purpose xxHash and profile/string authority remain independent.
    internal readonly ulong Hash()
    {
        unchecked
        {
            const ulong p1 = 11400714785074694791UL, p2 = 14029467366897019727UL;
            const ulong p3 = 1609587929392839161UL, p4 = 9650029242287828579UL;
            const ulong p5 = 2870177450012600261UL;
            ulong lane = BitOperations.RotateLeft(First8 * p2, 31) * p1;
            ulong hash = BitOperations.RotateLeft((p5 + 12) ^ lane, 27) * p1 + p4;
            hash = BitOperations.RotateLeft(hash ^ (Tail4 * p1), 23) * p2 + p3;
            hash ^= hash >> 33;
            hash *= p2;
            hash ^= hash >> 29;
            hash *= p3;
            return hash ^ (hash >> 32);
        }
    }
}
