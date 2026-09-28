using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Prediction;

public enum PredictionWarningCode
{
    InvalidSeed,
    InvalidRequestContext,
    EffectNotImplemented,
    EffectAuthorityIncomplete,
    EffectProjectionFailed,
    EffectTypeUnsupported,
    EffectNotApplicable,
    EffectSnapshotIncomplete,
    RuntimeEffectSnapshotMissing,
    RuntimeEffectSnapshotPartial,
    EffectPoolEmpty,
    EffectBranchBudgetExceeded,
    EffectNestedObtainIncomplete,
    ComplexRouteNotEvaluated,
    ComplexResultNotEvaluatedByPolicy,
    LegacyMultiplayerUnsupported,
    NonVanillaCatalogUnsupported,
    SnapshotAuthorityIncomplete,
    AnalysisFailed,
    ModernEligibilityIncomplete,
    UnsupportedGameVersion,
    Beta110ValidationPending,
    Beta111ValidationPending,
    UnverifiedVersionFallback,
    CharacterAuthorityUnknown,
    IdentityAmbiguous
}

public readonly record struct EvidenceCode(string Value)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
    public static implicit operator EvidenceCode(string value) => new(value);
}

public static class PredictionDiagnosticCodes
{
    public const string ValidationIssue = "validation-issue";
    public const string Exception = "exception";
    public const string Analyzer = "analyzer";
    public const string RngCalls = "rng-calls";
    public const string EventEntry = "event-entry";
    public const string EligibilityExact = "eligibility-exact";
    public const string RequestId = "request-id";
    public const string PlayerSlot = "player-slot";
    public const string UnlockSnapshotFingerprint = "unlock-snapshot-fingerprint";
    public const string CatalogFingerprint = "catalog-fingerprint";
    public const string PredictionDomain = "prediction-domain";
    public const string PredictionScope = "prediction-scope";
    public const string SourceState = "source-state";
    public const string ChoiceEvidence = "choice-evidence";
    public const string EffectEvidence = "effect-evidence";
    public const string EffectCapability = "effect-capability";
    public const string EffectAuthoritySource = "effect-authority-source";
    public const string EffectSnapshotFingerprint = "effect-snapshot-fingerprint";
    public const string EffectDeckFingerprint = "effect-deck-fingerprint";
    public const string EffectRelicBagFingerprint = "effect-relic-bag-fingerprint";
    public const string EffectPotionPoolFingerprint = "effect-potion-pool-fingerprint";
    public const string EffectSnapshotCompleteness = "effect-snapshot-completeness";
    public const string EffectSnapshotCapturedAt = "effect-snapshot-captured-at";
    public const string EffectSnapshotWarnings = "effect-snapshot-warnings";
    public const string EffectSnapshotCaptureDiagnostic = "effect-snapshot-capture-diagnostic";
    public const string RuntimeCompatibilityMode = "runtime-compatibility-mode";
    public const string GameVersionIdentity = "game-version-identity";
    public const string RngSemanticProfile = "rng-semantic-profile";
    public const string CompatibilityConfidence = "compatibility-confidence";
    public const string RuntimeAccepted = "runtime-accepted";
    public const string RuntimeCompatibilityReferenceVersion = "runtime-compatibility-reference-version";
    public const string RuntimeAuthorityId = "runtime-authority-id";
    public const string RuntimeAuditFingerprint = "runtime-audit-fingerprint";
    public const string RuntimeCardCatalogFingerprint = "runtime-card-catalog-fingerprint";
    public const string CardBaseOddsPolicy = "card-base-odds-policy";
    public const string Beta110ValidationStatus = "beta110-validation-status";
    public const string Beta111ValidationStatus = "beta111-validation-status";
    public const string RuntimeValidationStatus = "runtime-validation-status";
}

/// <summary>
/// Predictor/document or presentation warning. This is not Search disposition
/// authority; Production Exact must use the Query-relevant section/domain status
/// and may continue when an unrelated document warning is present.
/// </summary>
public sealed record PredictionWarning(
    PredictionWarningCode Code,
    ModelKey? RelatedKey = null,
    EvidenceCode EvidenceCode = default);

/// <summary>
/// Raw developer diagnostic evidence. It is not player-facing copy and does not
/// replace an PredictionWarning, Search FailureCode, Planner fallback, GPU recovery
/// result, capability applicability, or runtime compatibility presentation.
/// </summary>
public sealed record PredictionDiagnostic(string Code, string Value);
