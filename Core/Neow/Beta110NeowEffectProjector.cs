using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Neow;

/// <summary>
/// Beta 0.110.1 profile wrapper over the shared Modern effect engine. The wrapper owns
/// Beta110 authority, evidence and base-odds policy selection.
/// </summary>
public static class Beta110NeowEffectProjector
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
        if (identityPrecision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported ||
            !authority.IsBeta110NeowIdentityAuthorityExact)
        {
            return NeowEffectProjection.UnknownAuthority(
                relicKey,
                "beta110.neow-effect.identity-authority-incomplete");
        }

        var engine = new NeowEffectProjectionEngine(
            Beta110Profile.Instance,
            "beta110.neow-effect",
            canonicalSeed,
            authority,
            enableComplexBonesDeckInteractions);
        return ApplyValidationPolicy(relicKey, authority, engine.Project(relicKey));
    }

    internal static NeowEffectProjection ProjectFromRootHash(
        ModelKey relicKey,
        PredictionPrecision identityPrecision,
        RuntimeContextAuthoritySnapshot authority,
        ulong rootHash,
        bool enableComplexBonesDeckInteractions = false)
    {
        if (identityPrecision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported ||
            !authority.IsBeta110NeowIdentityAuthorityExact)
        {
            return NeowEffectProjection.UnknownAuthority(
                relicKey,
                "beta110.neow-effect.identity-authority-incomplete");
        }

        var engine = new NeowEffectProjectionEngine(
            Beta110Profile.Instance,
            "beta110.neow-effect",
            rootHash,
            authority,
            enableComplexBonesDeckInteractions);
        return ApplyValidationPolicy(relicKey, authority, engine.Project(relicKey));
    }

    private static NeowEffectProjection ApplyValidationPolicy(
        ModelKey relicKey,
        RuntimeContextAuthoritySnapshot authority,
        NeowEffectProjection projection)
    {
        if (Beta110ValidationAuthority.NeowAccepted &&
            Beta110ValidationAuthority.CardCatalogAccepted &&
            !authority.IsCompatibilityFallback)
        {
            return projection;
        }

        PredictionWarning pending = new(
            authority.IsCompatibilityFallback
                ? PredictionWarningCode.UnverifiedVersionFallback
                : PredictionWarningCode.Beta110ValidationPending,
            relicKey,
            authority.IsCompatibilityFallback
                ? authority.VersionResolution.EvidenceCode
                : RuntimeProfilePolicies.Beta110AuditFingerprint);
        return projection with
        {
            Precision = projection.Precision == PredictionPrecision.Exact
                ? PredictionPrecision.Partial
                : projection.Precision,
            ProductRelevantProjectionPrecision = projection.ProductRelevantProjectionPrecision == PredictionPrecision.Exact
                ? PredictionPrecision.Partial
                : projection.ProductRelevantProjectionPrecision,
            Warnings = projection.Warnings.Append(pending).Distinct().ToArray()
        };
    }
}
