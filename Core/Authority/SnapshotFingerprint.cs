using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Authority;

internal static class SnapshotFingerprint
{
    public static string BuildUnlockSnapshot(
        CharacterIdentity character,
        int ascension,
        int playerSlotIndex,
        int playersCount,
        bool? allCharacterCardPoolsUnlocked,
        int? unlockedCommonCards,
        int? unlockedUncommonCards,
        SourceAuthority authority,
        SnapshotCompleteness completeness)
    {
        string descriptor = string.Join("|", new[]
        {
            "unlock-snapshot-v1",
            character.CharacterKey.Serialized,
            ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
            playerSlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            playersCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NullableBool(allCharacterCardPoolsUnlocked),
            NullableInt(unlockedCommonCards),
            NullableInt(unlockedUncommonCards),
            authority.ToString(),
            completeness.ToString()
        });
        return Sha256(descriptor);
    }

    public static string BuildCatalog(
        RuntimeProfileId profileId,
        string gameVersion,
        bool? vanillaNeowCatalogExact,
        bool isVanilla,
        string? sourceModId,
        string? sourceAssembly)
    {
        string profileCatalog = profileId switch
        {
            RuntimeProfileId.Stable107 => "neow-identity-catalog-stable107-v1",
            RuntimeProfileId.Beta109 => "neow-identity-catalog-beta109-historical-v1",
            RuntimeProfileId.Beta110 => "neow-identity-catalog-beta110-v1",
            RuntimeProfileId.Beta111 => "neow-identity-catalog-beta111-v1",
            _ => "unsupported-catalog"
        };
        string descriptor = string.Join("|", new[]
        {
            "catalog-snapshot-v1",
            profileCatalog,
            gameVersion ?? string.Empty,
            NullableBool(vanillaNeowCatalogExact),
            isVanilla ? "vanilla" : "modded",
            sourceModId ?? "none",
            sourceAssembly ?? "none"
        });
        return Sha256(descriptor);
    }

    private static string NullableBool(bool? value) => value.HasValue
        ? value.Value ? "true" : "false"
        : "unknown";

    private static string NullableInt(int? value) => value.HasValue
        ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "unknown";

    private static string Sha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
}
