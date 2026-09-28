using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Presentation.ContentNames;

public sealed class JsonGameContentNameResolver : IGameContentNameResolver
{
    private readonly IReadOnlyDictionary<string, string> _localized;
    private readonly IReadOnlyDictionary<string, string> _english;

    private JsonGameContentNameResolver(
        string languageCode,
        IReadOnlyDictionary<string, string> localized,
        IReadOnlyDictionary<string, string> english)
    {
        LanguageCode = languageCode;
        _localized = localized;
        _english = english;
    }

    public string LanguageCode { get; }

    public string Resolve(ModelKey key, GameContentKind kind)
    {
        if (!key.IsValid || !CategoryMatches(key.Category, kind))
        {
            return key.Serialized;
        }

        if (_localized.TryGetValue(key.Serialized, out string? localized) && !string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        if (_english.TryGetValue(key.Serialized, out string? english) && !string.IsNullOrWhiteSpace(english))
        {
            return english;
        }

        return key.Entry;
    }

    public static IGameContentNameResolver Create(string languageCode)
    {
        string normalized = string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        IReadOnlyDictionary<string, string> english = JsonLocalizationCatalog.Load("game_content", "en");
        IReadOnlyDictionary<string, string> localized = normalized == "en"
            ? english
            : JsonLocalizationCatalog.Load("game_content", normalized);
        return new JsonGameContentNameResolver(normalized, localized, english);
    }

    private static bool CategoryMatches(string category, GameContentKind kind) => kind switch
    {
        GameContentKind.Relic => string.Equals(category, BaseGameModelKeys.Categories.Relic, StringComparison.Ordinal),
        GameContentKind.Card => string.Equals(category, BaseGameModelKeys.Categories.Card, StringComparison.Ordinal),
        GameContentKind.Potion => string.Equals(category, BaseGameModelKeys.Categories.Potion, StringComparison.Ordinal),
        GameContentKind.Character => string.Equals(category, BaseGameModelKeys.Categories.Character, StringComparison.Ordinal),
        GameContentKind.Event => string.Equals(category, BaseGameModelKeys.Categories.Event, StringComparison.Ordinal),
        GameContentKind.Encounter => string.Equals(category, BaseGameModelKeys.Categories.Encounter, StringComparison.Ordinal),
        GameContentKind.Ancient =>
            string.Equals(category, BaseGameModelKeys.Categories.Event, StringComparison.Ordinal) ||
            string.Equals(category, BaseGameModelKeys.Categories.Ancient, StringComparison.Ordinal),
        GameContentKind.Act => string.Equals(category, BaseGameModelKeys.Categories.Act, StringComparison.Ordinal),
        _ => false
    };
}
