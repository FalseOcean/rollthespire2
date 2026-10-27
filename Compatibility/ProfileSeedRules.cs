namespace RolltheSpire2.Compatibility;

internal static class ProfileSeedRules
{
    public const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string Canonicalize(string rawSeed)
    {
        ArgumentNullException.ThrowIfNull(rawSeed);
        return rawSeed.ToUpperInvariant().Replace('O', '0').Replace('I', '1').Trim();
    }

    // Native Beta111 prediction hashes the canonical input string. Twelve
    // base-34 characters describe generated Search candidates, not custom or
    // daily-run inputs (the latter use dd_MM_yyyy). Never pad or truncate here.
    internal static bool TryCanonicalizePredictionSeed(IRuntimeProfile profile, string rawSeed,
        out string canonicalSeed, out string issue)
    {
        if (profile.ProfileId != RuntimeProfileId.Beta111)
            return profile.TryCanonicalizeSeed(rawSeed, out canonicalSeed, out issue);
        canonicalSeed = Canonicalize(rawSeed);
        issue = canonicalSeed.Length == 0 ? "Prediction seed is empty." : string.Empty;
        return canonicalSeed.Length > 0;
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
