using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;

namespace RolltheSpire2.Presentation.Localization;

public static class UiTextKey
{
    public const string PanelTitle = "ui.analysis.panel_title";
    public const string Game = "ui.analysis.game";
    public const string Profile = "ui.analysis.profile";
    public const string Vectors = "ui.analysis.vectors";
    public const string Capability = "ui.analysis.capability";
    public const string CanonicalSeed = "ui.analysis.canonical_seed";
    public const string Request = "ui.analysis.request";
    public const string Status = "ui.analysis.status";
    public const string NeowSection = "ui.analysis.neow_section";
    public const string Identity = "ui.analysis.identity";
    public const string Effect = "ui.analysis.effect";
    public const string Warnings = "ui.analysis.warnings";
    public const string Diagnostics = "ui.analysis.diagnostics";
    public const string InitialResult = "ui.analysis.initial_result";
    public const string AnalyzeButton = "ui.button.analyze";
    public const string CloseButton = "ui.button.close";
    public const string SeedPlaceholder = "ui.field.seed_placeholder";
    public const string Character = "ui.field.character";
    public const string CustomCharacter = "ui.field.custom_character";
    public const string CustomCharacterPlaceholder = "ui.field.custom_character_placeholder";
    public const string Ascension = "ui.field.ascension";
    public const string Authority = "ui.field.authority";
    public const string Language = "ui.field.language";
    public const string ShowIdentityKeys = "ui.field.show_identity_keys";
    public const string RuntimeSummary = "ui.analysis.runtime_summary";
    public const string RequestSummary = "ui.analysis.request_summary";
    public const string Passed = "status.passed";
    public const string Failed = "status.failed";
    public const string MissingTranslation = "status.missing_translation";
    public const string InvalidCharacterKey = "ui.request_error.invalid_character_key";

    public static string Precision(PredictionPrecision precision) => precision switch
    {
        PredictionPrecision.Exact => "precision.exact",
        PredictionPrecision.Partial => "precision.partial",
        PredictionPrecision.DescriptionOnly => "precision.description_only",
        PredictionPrecision.Unsupported => "precision.unsupported",
        _ => "precision.unknown"
    };

    public static string OverallStatus(SeedPredictionOverallStatus status) => status switch
    {
        SeedPredictionOverallStatus.Completed => "status.completed",
        SeedPredictionOverallStatus.CompletedWithWarnings => "status.completed_with_warnings",
        SeedPredictionOverallStatus.Unsupported => "status.unsupported",
        SeedPredictionOverallStatus.InvalidRequest => "status.invalid_request",
        _ => "status.unknown"
    };

    public static string Warning(PredictionWarningCode code) => code switch
    {
        PredictionWarningCode.InvalidSeed => "warning.invalid_seed",
        PredictionWarningCode.InvalidRequestContext => "warning.invalid_request_context",
        PredictionWarningCode.EffectNotImplemented => "warning.effect_not_implemented",
        PredictionWarningCode.EffectAuthorityIncomplete => "warning.effect_authority_incomplete",
        PredictionWarningCode.EffectProjectionFailed => "warning.effect_projection_failed",
        PredictionWarningCode.EffectTypeUnsupported => "warning.effect_type_unsupported",
        PredictionWarningCode.EffectNotApplicable => "warning.effect_not_applicable",
        PredictionWarningCode.EffectSnapshotIncomplete => "warning.effect_snapshot_incomplete",
        PredictionWarningCode.RuntimeEffectSnapshotMissing => "warning.runtime_effect_snapshot_missing",
        PredictionWarningCode.RuntimeEffectSnapshotPartial => "warning.runtime_effect_snapshot_partial",
        PredictionWarningCode.EffectPoolEmpty => "warning.effect_pool_empty",
        PredictionWarningCode.EffectBranchBudgetExceeded => "warning.effect_branch_budget_exceeded",
        PredictionWarningCode.EffectNestedObtainIncomplete => "warning.effect_nested_obtain_incomplete",
        PredictionWarningCode.ComplexRouteNotEvaluated => "warning.complex_route_not_evaluated",
        PredictionWarningCode.ComplexResultNotEvaluatedByPolicy => "warning.complex_result_not_evaluated_by_policy",
        PredictionWarningCode.LegacyMultiplayerUnsupported => "warning.legacy_multiplayer_unsupported",
        PredictionWarningCode.NonVanillaCatalogUnsupported => "warning.non_vanilla_catalog_unsupported",
        PredictionWarningCode.SnapshotAuthorityIncomplete => "warning.snapshot_authority_incomplete",
        PredictionWarningCode.AnalysisFailed => "warning.analysis_failed",
        PredictionWarningCode.ModernEligibilityIncomplete => "warning.modern_eligibility_incomplete",
        PredictionWarningCode.UnsupportedGameVersion => "warning.unsupported_game_version",
        PredictionWarningCode.Beta110ValidationPending => "warning.validation_pending",
        PredictionWarningCode.Beta111ValidationPending => "warning.validation_pending",
        PredictionWarningCode.UnverifiedVersionFallback => "warning.unverified_version_fallback",
        PredictionWarningCode.CharacterAuthorityUnknown => "warning.character_authority_unknown",
        _ => "warning.identity_ambiguous"
    };

    public static string RequestError(SeedPredictionRequestError error) => error switch
    {
        SeedPredictionRequestError.MissingSeed => "ui.request_error.missing_seed",
        SeedPredictionRequestError.MissingCharacter => "ui.request_error.missing_character",
        SeedPredictionRequestError.AscensionOutOfRange => "ui.request_error.ascension_out_of_range",
        SeedPredictionRequestError.PlayersCountOutOfRange => "ui.request_error.players_out_of_range",
        SeedPredictionRequestError.PlayerSlotOutOfRange => "ui.request_error.player_slot_out_of_range",
        SeedPredictionRequestError.MissingAuthority => "ui.request_error.missing_authority",
        SeedPredictionRequestError.MissingSnapshotFingerprint => "ui.request_error.missing_snapshot_fingerprint",
        SeedPredictionRequestError.AuthorityContextMismatch => "ui.request_error.authority_context_mismatch",
        _ => "ui.request_error.unknown"
    };

    public static string SourceAuthorityTextKey(SourceAuthority authority) => authority switch
    {
        SourceAuthority.OfficialRuntimeExact => "authority.official_runtime_exact",
        SourceAuthority.AuditedStaticExact => "authority.audited_static_exact",
        SourceAuthority.ModdedRuntimeBestEffort => "authority.modded_runtime_best_effort",
        SourceAuthority.UserDeclaredAssumption => "authority.user_declared_assumption",
        SourceAuthority.Incomplete => "authority.incomplete",
        SourceAuthority.Ambiguous => "authority.ambiguous",
        _ => "authority.unknown"
    };

    public static string Completeness(SnapshotCompleteness completeness) => completeness switch
    {
        SnapshotCompleteness.Complete => "completeness.complete",
        SnapshotCompleteness.Partial => "completeness.partial",
        _ => "completeness.missing"
    };
}
