using System.Security.Cryptography;

namespace RolltheSpire2.Compatibility;

public static class ProfileSeedGenerator
{
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
