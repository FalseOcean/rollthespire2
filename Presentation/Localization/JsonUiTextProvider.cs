using System.Collections.Frozen;
using System.Globalization;

namespace RolltheSpire2.Presentation.Localization;

public sealed class JsonUiTextProvider : IUiTextProvider
{
    private static readonly object CacheLock = new();
    // Factory-controlled groups and normalized locales bound these to six providers
    // and two merged catalogs, even when loose development resources change.
    private static readonly Dictionary<(string Group, string Language), JsonUiTextProvider> Providers = new();
    private static readonly Dictionary<string, MergedCatalog> PredictorCatalogs = new(StringComparer.Ordinal);

    private sealed record MergedCatalog(
        IReadOnlyDictionary<string, string> Legacy,
        IReadOnlyDictionary<string, string> Workbench,
        IReadOnlyDictionary<string, string> Values);

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

    public bool TryGet(string key, out string value)
    {
        if (_localized.TryGetValue(key, out string? localized) && !string.IsNullOrWhiteSpace(localized))
        {
            value = localized;
            return true;
        }

        if (_english.TryGetValue(key, out string? english) && !string.IsNullOrWhiteSpace(english))
        {
            value = english;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public string Get(string key)
    {
        if (TryGet(key, out string value)) return value;
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
        // Preserve the Predictor factory's existing exact language-code behavior.
        string language = languageCode == "zh" ? "zh" : "en";
        lock (CacheLock)
        {
            var english = PredictorCatalog("en");
            var localized = language == "en" ? english : PredictorCatalog(language);
            return ReuseProvider("predictor", language, localized, english);
        }
    }

    private static IUiTextProvider Create(string languageCode, string group)
    {
        string normalized = string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        IReadOnlyDictionary<string, string> english = JsonLocalizationCatalog.Load(group, "en");
        IReadOnlyDictionary<string, string> localized = normalized == "en"
            ? english
            : JsonLocalizationCatalog.Load(group, normalized);
        lock (CacheLock) return ReuseProvider(group, normalized, localized, english);
    }

    private static IReadOnlyDictionary<string, string> PredictorCatalog(string language)
    {
        var legacy = JsonLocalizationCatalog.Load("rt2_ui_legacy", language);
        var workbench = JsonLocalizationCatalog.Load("rt2_ui_v13", language);
        if (PredictorCatalogs.TryGetValue(language, out var cached) &&
            ReferenceEquals(cached.Legacy, legacy) && ReferenceEquals(cached.Workbench, workbench))
            return cached.Values;

        var entries = new Dictionary<string, string>(legacy, StringComparer.Ordinal);
        foreach (var pair in workbench) entries[pair.Key] = pair.Value;
        var values = entries.ToFrozenDictionary(StringComparer.Ordinal);
        if (legacy.Count > 0 && workbench.Count > 0)
            PredictorCatalogs[language] = new MergedCatalog(legacy, workbench, values);
        return values;
    }

    private static IUiTextProvider ReuseProvider(string group, string language,
        IReadOnlyDictionary<string, string> localized, IReadOnlyDictionary<string, string> english)
    {
        if (Providers.TryGetValue((group, language), out var cached) &&
            ReferenceEquals(cached._localized, localized) && ReferenceEquals(cached._english, english))
            return cached;

        var provider = new JsonUiTextProvider(language, localized, english);
        if (localized.Count > 0 && english.Count > 0) Providers[(group, language)] = provider;
        return provider;
    }
}
