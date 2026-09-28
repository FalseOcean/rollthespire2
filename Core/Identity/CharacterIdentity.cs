using RolltheSpire2.Core.Authority;

namespace RolltheSpire2.Core.Identity;

public enum KnownVanillaCharacterKind
{
    Ironclad,
    Silent,
    Defect,
    Regent,
    Necrobinder
}

public readonly record struct CharacterIdentity(
    ModelKey CharacterKey,
    KnownVanillaCharacterKind? KnownVanillaKind)
{
    public bool IsValid =>
        CharacterKey.IsValid &&
        string.Equals(CharacterKey.Category, BaseGameModelKeys.Categories.Character, StringComparison.Ordinal);

    public bool IsKnownVanilla => KnownVanillaKind.HasValue;

    public static CharacterIdentity FromKey(ModelKey key)
    {
        KnownVanillaCharacterKind? kind = null;
        if (key == BaseGameModelKeys.Characters.Ironclad)
        {
            kind = KnownVanillaCharacterKind.Ironclad;
        }
        else if (key == BaseGameModelKeys.Characters.Silent)
        {
            kind = KnownVanillaCharacterKind.Silent;
        }
        else if (key == BaseGameModelKeys.Characters.Defect)
        {
            kind = KnownVanillaCharacterKind.Defect;
        }
        else if (key == BaseGameModelKeys.Characters.Regent)
        {
            kind = KnownVanillaCharacterKind.Regent;
        }
        else if (key == BaseGameModelKeys.Characters.Necrobinder)
        {
            kind = KnownVanillaCharacterKind.Necrobinder;
        }
        return new CharacterIdentity(key, kind);
    }
}

public sealed record ModelSourceMetadata(
    bool IsVanilla,
    string? SourceModId,
    string? SourceAssembly,
    SourceAuthority SourceAuthority,
    IdentityResolutionStatus ResolutionStatus);
