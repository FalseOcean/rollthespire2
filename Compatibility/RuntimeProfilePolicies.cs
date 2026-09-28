namespace RolltheSpire2.Compatibility;

/// <summary>
/// Profile-owned policy for CardRarityOdds.RollWithBaseOdds. This is intentionally
/// separate from the stateful encounter-reward pity algorithm.
/// </summary>
public enum CardBaseOddsPolicy
{
    LegacyAndBeta109NonCumulative,
    Beta110Cumulative
}

/// <summary>
/// RNG semantic implementation identity. This is deliberately not the game-version
/// identity. Multiple audited Beta patches may map to the same semantic implementation.
/// </summary>
public enum RngSemanticProfileId
{
    Unsupported = 0,
    Stable107Legacy = 1,
    Modern109 = 2,
    Modern110 = 3
}

/// <summary>
/// Central profile policy. Production code must select semantic differences here
/// instead of scattering raw game-version string checks through predictors.
/// Beta109 remains an internal historical donor only.
/// </summary>
public static class RuntimeProfilePolicies
{
    public const string Stable107AuthorityId = "runtime-authority:stable107:accepted";
    public const string Beta109HistoricalAuthorityId = "runtime-authority:beta109:historical-donor-only";
    public const string Beta110AuthorityId = "runtime-authority:beta110:source-candidate";
    public const string Beta111AuthorityId = "runtime-authority:beta111:semantic-compatible";

    public const string Stable107AuditFingerprint = "stable107-accepted-source-and-runtime-audit-v1";
    public const string Beta109HistoricalAuditFingerprint = "beta109-historical-donor-audit-v1";
    public const string Beta110AuditFingerprint = "beta110-vs-beta1091-seed-prediction-source-audit-20260731";
    public const string Beta111AuditFingerprint = "beta111-vs-beta1101-compatibility-source-audit-20260814:sha256=ca7cecb2a9900f8d621d34a29f90cf6b0167b684fa2e22674d2d472893ae5523";

    public const string Stable107CatalogFingerprintPrefix = "stable107-runtime-card-catalog";
    public const string Beta109HistoricalCatalogFingerprintPrefix = "beta109-historical-runtime-card-catalog";
    public const string Beta110CatalogFingerprintPrefix = "beta110-runtime-card-catalog";
    public const string Beta111CatalogFingerprintPrefix = "beta111-runtime-card-catalog";
    public const string Modern110SemanticFingerprint = "rng-semantic-profile:modern110:v1";
    public const string Modern110PhysicalImplementationFingerprint = "physical-implementation:modern110-search-stack:v1";

    public static bool IsModernCore(RuntimeProfileId profileId) =>
        profileId is RuntimeProfileId.Beta109 or RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111;

    public static RngSemanticProfileId RngSemanticProfile(RuntimeProfileId profileId) => profileId switch
    {
        RuntimeProfileId.Stable107 => RngSemanticProfileId.Stable107Legacy,
        RuntimeProfileId.Beta109 => RngSemanticProfileId.Modern109,
        RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 => RngSemanticProfileId.Modern110,
        _ => RngSemanticProfileId.Unsupported
    };

    public static string RngSemanticFingerprint(RuntimeProfileId profileId) =>
        RngSemanticProfile(profileId) == RngSemanticProfileId.Modern110
            ? Modern110SemanticFingerprint
            : "rng-semantic-profile:" + RngSemanticProfile(profileId).ToString().ToLowerInvariant();

    /// <summary>
    /// Source-audited shared algorithm family. Beta110 and Beta111 have distinct
    /// game-version/audit identities but intentionally share the Modern110 semantic implementation.
    /// </summary>
    public static bool UsesBeta110SharedAlgorithms(RuntimeProfileId profileId) =>
        RngSemanticProfile(profileId) == RngSemanticProfileId.Modern110;

    /// <summary>
    /// Existing planner evidence schema stores RuntimeProfileId. Until that schema is
    /// versioned, semantic-compatible patches normalize physical evidence to the semantic
    /// implementation owner rather than fragmenting identical physical evidence by patch number.
    /// </summary>
    public static RuntimeProfileId SemanticEvidenceOwnerProfile(RuntimeProfileId profileId) =>
        RngSemanticProfile(profileId) == RngSemanticProfileId.Modern110
            ? RuntimeProfileId.Beta110
            : profileId;


    [Obsolete("Use SemanticEvidenceOwnerProfile. Schema-v1 persisted field names remain historical only.")]
    public static RuntimeProfileId SemanticEvidenceProfile(RuntimeProfileId profileId) => SemanticEvidenceOwnerProfile(profileId);

    public static string PhysicalImplementationFingerprint(RuntimeProfileId profileId) =>
        RngSemanticProfile(profileId) == RngSemanticProfileId.Modern110
            ? Modern110PhysicalImplementationFingerprint
            : "physical-implementation:" + RngSemanticProfile(profileId).ToString().ToLowerInvariant();

    public static CardBaseOddsPolicy BaseOddsPolicy(RuntimeProfileId profileId) => profileId switch
    {
        RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 => CardBaseOddsPolicy.Beta110Cumulative,
        RuntimeProfileId.Stable107 or RuntimeProfileId.Beta109 =>
            CardBaseOddsPolicy.LegacyAndBeta109NonCumulative,
        _ => throw new InvalidOperationException("Unsupported profile has no card base-odds policy.")
    };

    public static string AuthorityId(RuntimeProfileId profileId) => profileId switch
    {
        RuntimeProfileId.Stable107 => Stable107AuthorityId,
        RuntimeProfileId.Beta109 => Beta109HistoricalAuthorityId,
        RuntimeProfileId.Beta110 => Beta110AuthorityId,
        RuntimeProfileId.Beta111 => Beta111AuthorityId,
        _ => "runtime-authority:unsupported"
    };

    public static string AuditFingerprint(RuntimeProfileId profileId) => profileId switch
    {
        RuntimeProfileId.Stable107 => Stable107AuditFingerprint,
        RuntimeProfileId.Beta109 => Beta109HistoricalAuditFingerprint,
        RuntimeProfileId.Beta110 => Beta110AuditFingerprint,
        RuntimeProfileId.Beta111 => Beta111AuditFingerprint,
        _ => "unsupported"
    };

    public static string CatalogFingerprintPrefix(RuntimeProfileId profileId) => profileId switch
    {
        RuntimeProfileId.Stable107 => Stable107CatalogFingerprintPrefix,
        RuntimeProfileId.Beta109 => Beta109HistoricalCatalogFingerprintPrefix,
        RuntimeProfileId.Beta110 => Beta110CatalogFingerprintPrefix,
        RuntimeProfileId.Beta111 => Beta111CatalogFingerprintPrefix,
        _ => "unsupported-runtime-card-catalog"
    };
}
