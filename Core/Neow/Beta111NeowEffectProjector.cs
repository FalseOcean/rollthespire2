using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Neow;

/// <summary>Beta111 profile wrapper over the shared modern Neow effect engine.</summary>
public static class Beta111NeowEffectProjector
{
    internal static NeowEffectProjection ProjectFromRootHash(
        ModelKey relicKey,
        PredictionPrecision identityPrecision,
        RuntimeContextAuthoritySnapshot authority,
        ulong rootHash,
        bool enableComplexBonesDeckInteractions = false)
    {
        if (identityPrecision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported ||
            !authority.IsBeta111NeowIdentityAuthorityExact)
        {
            return NeowEffectProjection.UnknownAuthority(relicKey, "beta111.neow-effect.identity-authority-incomplete");
        }

        // Do not require whole-snapshot / whole-character Exact authority before the
        // shared Modern effect engine can run. Each effect implementation owns its
        // actual dependencies (card identity pool, relic bag, deck, potion pool, etc.)
        // and must fail/partial locally when those facts are not authoritative. This
        // is what lets a runtime Mod character reuse audited vanilla identity-only
        // effects such as Arcane Scroll without granting its custom behavior Exact.

        var engine = new NeowEffectProjectionEngine(
            Beta111Profile.Instance,
            "beta111.neow-effect",
            rootHash,
            authority,
            enableComplexBonesDeckInteractions);
        return engine.Project(relicKey);
    }
}
