using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Icons;

internal enum IconVariant
{
    Navigation,
    CharacterPortrait,
    CharacterCompendiumPoolSmall,
    WorldCompendiumBossIcon,
    WorldCompendiumAncientIcon,
    RelicLarge,
    CardPickerLarge,
    Small,
    Missing
}

internal sealed record IconDescriptor(
    ModelKey Key,
    GameContentKind Kind,
    IconVariant Variant,
    Texture2D? Texture,
    bool IsMissing,
    string EvidenceCode)
{
    public static IconDescriptor Missing(ModelKey key, GameContentKind kind, IconVariant variant, string evidenceCode) =>
        new(key, kind, variant, null, true, evidenceCode);
}

internal interface IGameIconResolver
{
    IconDescriptor Resolve(ModelKey key, GameContentKind kind, IconVariant variant);
    void InvalidateCatalog(string catalogFingerprint);
}
