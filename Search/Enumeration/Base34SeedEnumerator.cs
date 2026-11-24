using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.Enumeration;

public sealed class Base34SeedEnumerator
{
    private readonly IRuntimeProfile _profile;
    private readonly ulong _startValue;
    private readonly ulong _spaceSize;

    public Base34SeedEnumerator(IRuntimeProfile profile, string canonicalStartSeed)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _ = ProfileSeedGenerator.CreateProbeSeed(profile);
        if (!_profile.TryCanonicalizeSeed(canonicalStartSeed, out string canonical, out string issue))
        {
            throw new ArgumentException(issue, nameof(canonicalStartSeed));
        }

        _startValue = Parse(canonical, _profile.SeedAlphabet);
        _spaceSize = ComputeSpaceSize(_profile.SeedAlphabet.Length, _profile.SeedLength);
    }

    public string SeedAt(long offset)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        ulong value = checked(_startValue + (ulong)offset);
        if (value >= _spaceSize)
        {
            throw new InvalidOperationException("The requested search range exceeds the profile seed space.");
        }
        return Format(value, _profile.SeedAlphabet, _profile.SeedLength);
    }

    public IEnumerable<(long Offset, string Seed)> Partition(long count, int workerIndex, int workerCount)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
        if (workerIndex < 0 || workerIndex >= workerCount) throw new ArgumentOutOfRangeException(nameof(workerIndex));

        for (long offset = workerIndex; offset < count; offset += workerCount)
        {
            yield return (offset, SeedAt(offset));
        }
    }

    public static string ZeroSeed(IRuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ProfileSeedGenerator.CreateProbeSeed(profile);
    }

    private static ulong Parse(string seed, string alphabet)
    {
        ulong value = 0;
        foreach (char character in seed)
        {
            int digit = alphabet.IndexOf(character);
            if (digit < 0)
            {
                throw new ArgumentException($"Seed character '{character}' is outside the profile alphabet.", nameof(seed));
            }
            value = checked(value * (ulong)alphabet.Length + (ulong)digit);
        }
        return value;
    }

    private static string Format(ulong value, string alphabet, int length)
    {
        char[] output = new char[length];
        ulong radix = (ulong)alphabet.Length;
        for (int index = length - 1; index >= 0; index--)
        {
            output[index] = alphabet[(int)(value % radix)];
            value /= radix;
        }
        if (value != 0)
        {
            throw new InvalidOperationException("Seed value exceeds the requested fixed-width representation.");
        }
        return new string(output);
    }

    private static ulong ComputeSpaceSize(int radix, int length)
    {
        ulong size = 1;
        for (int index = 0; index < length; index++)
        {
            size = checked(size * (ulong)radix);
        }
        return size;
    }
}
