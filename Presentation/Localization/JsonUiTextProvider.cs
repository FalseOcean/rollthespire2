using System.Globalization;

namespace RolltheSpire2.Presentation.Localization;

public sealed class JsonUiTextProvider : IUiTextProvider
{
    private readonly IReadOnlyDictionary<string, string> _localized;
    private readonly IReadOnlyDictionary<string, string> _english;

    private JsonUiTextProvider(
        string languageCode,
        IReadOnlyDictionary<string, string> localized,
        IReadOnlyDictionary<string, string> english)
    {
        LanguageCode = languageCode;
        _localized = localized;
        _english = english;
    }

    public string LanguageCode { get; }

    public string Get(string key)
    {
        if (_localized.TryGetValue(key, out string? localized) && !string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        if (_english.TryGetValue(key, out string? english) && !string.IsNullOrWhiteSpace(english))
        {
            return english;
        }

        return _english.TryGetValue(UiTextKey.MissingTranslation, out string? missing)
            ? missing
            : "Missing translation";
    }

    public string Format(string key, params object?[] args) =>
        string.Format(
            string.Equals(LanguageCode, "zh", StringComparison.Ordinal) ? CultureInfo.GetCultureInfo("zh-CN") : CultureInfo.InvariantCulture,
            Get(key),
            args);

    public static IUiTextProvider Create(string languageCode)
        => Create(languageCode, "rt2_ui_legacy");

    public static IUiTextProvider CreateUi13(string languageCode)
        => Create(languageCode, "rt2_ui_v13");

    // The migrated Predictor still uses the original presentation builders. Keep
    // their keyed vocabulary; new shell keys and overrides belong to v1.3.
    public static IUiTextProvider CreatePredictorUi13(string languageCode)
    {
        string language = languageCode == "zh" ? "zh" : "en";
        Dictionary<string, string> Catalog(string locale)
        {
            var entries = new Dictionary<string, string>(JsonLocalizationCatalog.Load("rt2_ui_legacy", locale));
            foreach (var pair in JsonLocalizationCatalog.Load("rt2_ui_v13", locale)) entries[pair.Key] = pair.Value;
            return entries;
        }
        return new JsonUiTextProvider(language, Catalog(language), Catalog("en"));
    }

    private static IUiTextProvider Create(string languageCode, string group)
    {
        string normalized = string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        IReadOnlyDictionary<string, string> english = JsonLocalizationCatalog.Load(group, "en");
        IReadOnlyDictionary<string, string> localized = normalized == "en"
            ? english
            : JsonLocalizationCatalog.Load(group, normalized);
        return new JsonUiTextProvider(normalized, localized, english);
    }
}
