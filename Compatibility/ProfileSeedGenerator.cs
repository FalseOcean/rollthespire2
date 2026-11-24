using System.Security.Cryptography;

namespace RolltheSpire2.Compatibility;

public static class ProfileSeedGenerator
{
    public const string UnavailableCode = "SeedProfileUnavailable";

    public static string CreateProbeSeed(IRuntimeProfile profile)
    {
        if (!TryCreateProbeSeed(profile, out string seed, out string issue))
            throw new InvalidOperationException(issue);
        return seed;
    }

    public static bool TryCreateProbeSeed(IRuntimeProfile profile, out string seed, out string issue)
    {
        ArgumentNullException.ThrowIfNull(profile);
        seed = string.Empty;
        string reason;
        if (!Enum.IsDefined(profile.ProfileId) || profile.ProfileId == RuntimeProfileId.Unsupported || !profile.SupportsSeedRng)
            reason = "Seed/RNG execution is unavailable for this game version/profile.";
        // Search ordinals are ulong; even a binary alphabet cannot use more than 64 digits.
        else if (profile.SeedLength is <= 0 or > 64)
            reason = "Invalid seed length.";
        else if (string.IsNullOrEmpty(profile.SeedAlphabet) || profile.SeedAlphabet.Length < 2 ||
                 profile.SeedAlphabet.Distinct().Count() != profile.SeedAlphabet.Length)
            reason = "Invalid seed alphabet.";
        else
        {
            string probe = new(profile.SeedAlphabet[0], profile.SeedLength);
            if (profile.TryCanonicalizeSeed(probe, out string canonical, out reason) && canonical == probe)
            {
                seed = probe;
                issue = string.Empty;
                return true;
            }
            reason = "Profile rejected its probe seed: " + reason;
        }
        issue = $"{UnavailableCode}:profile={profile.ProfileId};{reason}";
        return false;
    }

    public static string Generate(IRuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.SupportsSeedRng || profile.SeedLength <= 0 || string.IsNullOrEmpty(profile.SeedAlphabet))
        {
            return string.Empty;
        }

        char[] result = new char[profile.SeedLength];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = profile.SeedAlphabet[RandomNumberGenerator.GetInt32(profile.SeedAlphabet.Length)];
        }
        return new string(result);
    }
}
