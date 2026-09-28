using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Presentation-only risk summary for Predictor/Search. It never participates in
/// admission or semantic authority; details live on the Save Status inspector.
/// </summary>
internal static class RuntimeAuthorityWarningComposer
{
    public static IReadOnlyList<string> Build(IUiTextProvider uiText, ModRuntimeSnapshot runtime)
    {
        ArgumentNullException.ThrowIfNull(uiText);
        ArgumentNullException.ThrowIfNull(runtime);
        var output = new List<string>(2);

        if (runtime.RequiresCompatibilityWarning)
        {
            output.Add(runtime.IsCompatibilityFallback
                ? uiText.Format(Ui1TextKey.RuntimeFallbackWarning, runtime.Detection.DisplayVersion)
                : uiText.Format(Ui1TextKey.RuntimePendingValidationWarning, runtime.Detection.DisplayVersion));
        }

        RuntimeAuthorityEnvironmentSnapshot environment = RuntimeAuthorityEnvironment.Current;
        RuntimeAuthorityInterpretation interpretation = environment.Interpretation;
        switch (interpretation.EnvironmentStatus)
        {
            case SemanticEnvironmentStatus.VerifiedVanillaMatch:
                break;
            case SemanticEnvironmentStatus.KnownBaselineMatchButGameVersionUnverified:
                output.Add(uiText.Format(
                    Ui1TextKey.RuntimeSemanticCrossVersionWarning,
                    interpretation.MatchedBaselineGameVersion));
                break;
            case SemanticEnvironmentStatus.SemanticMismatch:
                output.Add(uiText.Get(Ui1TextKey.RuntimeSemanticMismatchWarning));
                break;
            case SemanticEnvironmentStatus.Unverified:
            case SemanticEnvironmentStatus.Unknown:
                output.Add(environment.Authority.Fingerprint.Complete
                    ? uiText.Get(Ui1TextKey.RuntimeSemanticBaselineUnverifiedWarning)
                    : uiText.Get(Ui1TextKey.RuntimeSemanticFingerprintIncompleteWarning));
                break;
        }

        return output.Distinct(StringComparer.Ordinal).ToArray();
    }
}
