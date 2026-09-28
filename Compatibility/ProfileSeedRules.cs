namespace RolltheSpire2.Compatibility;

internal static class ProfileSeedRules
{
    public const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string Canonicalize(string rawSeed)
    {
        ArgumentNullException.ThrowIfNull(rawSeed);
        return rawSeed.ToUpperInvariant().Replace('O', '0').Replace('I', '1').Trim();
    }

    public static bool TryCanonicalize(string rawSeed, int requiredLength, out string canonicalSeed, out string issue)
    {
        canonicalSeed = Canonicalize(rawSeed);
        if (canonicalSeed.Length != requiredLength)
        {
            issue = $"Expected {requiredLength} canonical seed characters; got {canonicalSeed.Length}.";
            return false;
        }

        foreach (char value in canonicalSeed)
        {
            if (Alphabet.IndexOf(value) < 0)
            {
                issue = $"Character '{value}' is outside the STS2 base-34 seed alphabet.";
                return false;
            }
        }

        issue = string.Empty;
        return true;
    }
}
