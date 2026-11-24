using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Neow;

/// <summary>
/// STS2 0.107.1-only Neow effect projector. The profile owns its dispatch
/// boundary and never falls back to Beta109 rules.
/// </summary>
public static class Stable107NeowEffectProjector
{
    public static NeowEffectProjection Project(
        ModelKey relicKey,
        PredictionPrecision identityPrecision,
        RuntimeContextAuthoritySnapshot authority,
        bool enableComplexBonesDeckInteractions = false) =>
        Project(relicKey, identityPrecision, authority, canonicalSeed: string.Empty, enableComplexBonesDeckInteractions);

    public static NeowEffectProjection Project(
        ModelKey relicKey,
        PredictionPrecision identityPrecision,
        RuntimeContextAuthoritySnapshot authority,
        string canonicalSeed,
        bool enableComplexBonesDeckInteractions = false)
    {
        if (identityPrecision != PredictionPrecision.Exact || !authority.IsExactStable107)
        {
            return NeowEffectProjection.UnknownAuthority(
                relicKey,
                "stable107.neow-effect.authority-incomplete");
        }

        var engine = new NeowEffectProjectionEngine(
            Stable107Profile.Instance,
            "stable107.neow-effect",
            canonicalSeed,
            authority,
            enableComplexBonesDeckInteractions);
        return engine.Project(relicKey);
    }
}
