using Godot;

namespace RolltheSpire2.Ui.Settings;

// RT2-owned labels follow the workspace override. Native model names and card
// art continue to use the game's own localization. Reads never poll files.
internal static class UiLanguagePreferences
{
    internal static string LanguageCode => Resolve(InRunFeaturePreferences.LanguageOverride);

    internal static string Resolve(string? languageOverride) => languageOverride is "zh" or "en"
        ? languageOverride
        : TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
}
