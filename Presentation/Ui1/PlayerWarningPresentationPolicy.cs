using RolltheSpire2.Core.Prediction;

namespace RolltheSpire2.Presentation.Ui1;

internal enum PlayerWarningAudience : byte
{
    DeveloperDiagnostic = 0,
    PlayerActionable = 1,
    PlayerCompatibility = 2
}

/// <summary>
/// Explicit ordinary-player warning authority. New PredictionWarningCode values are
/// diagnostics by default and must be deliberately productized before they can
/// escape into Predictor UI.
/// </summary>
internal static class PlayerWarningPresentationPolicy
{
    public static PlayerWarningAudience Audience(PredictionWarningCode code) => code switch
    {
        PredictionWarningCode.InvalidSeed or
        PredictionWarningCode.InvalidRequestContext => PlayerWarningAudience.PlayerActionable,

        PredictionWarningCode.LegacyMultiplayerUnsupported or
        PredictionWarningCode.NonVanillaCatalogUnsupported or
        PredictionWarningCode.UnsupportedGameVersion or
        PredictionWarningCode.UnverifiedVersionFallback => PlayerWarningAudience.PlayerCompatibility,

        _ => PlayerWarningAudience.DeveloperDiagnostic
    };

    public static bool IsPlayerFacing(PredictionWarningCode code) =>
        Audience(code) != PlayerWarningAudience.DeveloperDiagnostic;
}
