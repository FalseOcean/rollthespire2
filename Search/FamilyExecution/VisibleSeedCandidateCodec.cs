using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>Visible-seed identity codec owned by Family Execution, not a Fast backend.</summary>
internal static class VisibleSeedCandidateCodec
{
    public static ulong SpaceSize(IRuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ulong result = 1;
        for (int index = 0; index < profile.SeedLength; index++)
            result = checked(result * (ulong)profile.SeedAlphabet.Length);
        return result;
    }

    public static string FormatOrdinal(IRuntimeProfile profile, ulong ordinal)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ulong spaceSize = SpaceSize(profile);
        if (ordinal >= spaceSize) throw new ArgumentOutOfRangeException(nameof(ordinal));
        string alphabet = profile.SeedAlphabet;
        int radix = alphabet.Length;
        return string.Create(profile.SeedLength, (ordinal, alphabet, radix), static (chars, state) =>
        {
            ulong value = state.ordinal;
            for (int index = chars.Length - 1; index >= 0; index--)
            {
                chars[index] = state.alphabet[(int)(value % (ulong)state.radix)];
                value /= (ulong)state.radix;
            }
        });
    }

    public static bool TryParseOrdinal(
        IRuntimeProfile profile,
        string rawSeed,
        out ulong ordinal,
        out string canonicalSeed,
        out string issue)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ordinal = 0;
        if (!profile.TryCanonicalizeSeed(rawSeed, out canonicalSeed, out issue))
            return false;
        foreach (char character in canonicalSeed)
        {
            int digit = profile.SeedAlphabet.IndexOf(character);
            if (digit < 0)
            {
                issue = $"Character '{character}' is outside the visible seed alphabet.";
                return false;
            }
            ordinal = checked(ordinal * (ulong)profile.SeedAlphabet.Length + (uint)digit);
        }
        issue = string.Empty;
        return true;
    }
}
