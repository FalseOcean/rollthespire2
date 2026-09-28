using System.Runtime.CompilerServices;
using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class Beta110SeedCodec
{
    public const int SeedLength = 12;
    public const int Radix = 34;
    public const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    public static readonly ulong SpaceSize = Pow((ulong)Radix, SeedLength);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteOrdinal(ulong ordinal, Span<byte> destination)
    {
        if (destination.Length < SeedLength) throw new ArgumentException("A 12-byte destination is required.", nameof(destination));
        if (ordinal >= SpaceSize) throw new ArgumentOutOfRangeException(nameof(ordinal));
        for (int index = SeedLength - 1; index >= 0; index--)
        {
            destination[index] = (byte)Alphabet[(int)(ordinal % Radix)];
            ordinal /= Radix;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Advance(Span<byte> seedBytes, int stride)
    {
        if (seedBytes.Length < SeedLength) throw new ArgumentException("A 12-byte seed is required.", nameof(seedBytes));
        if (stride < 1) throw new ArgumentOutOfRangeException(nameof(stride));
        int carry = stride;
        for (int index = SeedLength - 1; index >= 0 && carry > 0; index--)
        {
            int digit = DigitOf(seedBytes[index]);
            int next = digit + carry;
            seedBytes[index] = (byte)Alphabet[next % Radix];
            carry = next / Radix;
        }
        return carry == 0;
    }

    public static string FormatOrdinal(ulong ordinal) =>
        string.Create(SeedLength, ordinal, static (chars, value) =>
        {
            for (int index = SeedLength - 1; index >= 0; index--)
            {
                chars[index] = Alphabet[(int)(value % Radix)];
                value /= Radix;
            }
        });

    public static string FormatBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SeedLength) throw new ArgumentException("A 12-byte seed is required.", nameof(bytes));
        char[] chars = new char[SeedLength];
        for (int index = 0; index < SeedLength; index++) chars[index] = (char)bytes[index];
        return new string(chars);
    }

    public static bool TryParseOrdinal(string rawSeed, out ulong ordinal, out string canonicalSeed, out string issue)
    {
        ordinal = 0;
        if (!Beta110Profile.Instance.TryCanonicalizeSeed(rawSeed, out canonicalSeed, out issue)) return false;
        foreach (char character in canonicalSeed)
        {
            int digit = Alphabet.IndexOf(character);
            if (digit < 0)
            {
                issue = $"Character '{character}' is outside the Beta110 seed alphabet.";
                return false;
            }
            ordinal = checked(ordinal * Radix + (uint)digit);
        }
        issue = string.Empty;
        return true;
    }

    public static bool IsCanonicalVisibleSeed(string value) =>
        value.Length == SeedLength &&
        value.All(character => Alphabet.IndexOf(character) >= 0) &&
        Beta110Profile.Instance.TryCanonicalizeSeed(value, out string canonical, out _) &&
        string.Equals(value, canonical, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAlphabetByte(byte value) =>
        value is >= (byte)'0' and <= (byte)'9' ||
        value is >= (byte)'A' and <= (byte)'H' ||
        value is >= (byte)'J' and <= (byte)'N' ||
        value is >= (byte)'P' and <= (byte)'Z';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DigitOf(byte value)
    {
        if (value is >= (byte)'0' and <= (byte)'9') return value - (byte)'0';
        if (value is >= (byte)'A' and <= (byte)'H') return 10 + value - (byte)'A';
        if (value is >= (byte)'J' and <= (byte)'N') return 18 + value - (byte)'J';
        if (value is >= (byte)'P' and <= (byte)'Z') return 23 + value - (byte)'P';
        throw new ArgumentException("Seed contains a byte outside the Beta110 alphabet.");
    }

    private static ulong Pow(ulong value, int exponent)
    {
        ulong result = 1;
        for (int index = 0; index < exponent; index++) result = checked(result * value);
        return result;
    }
}
