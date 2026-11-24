using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace RolltheSpire2.Presentation.Localization;

internal static class JsonLocalizationCatalog
{
    // Only successful embedded string catalogs are stable for this assembly's
    // lifetime. Loose development resources must still be read on each load.
    private static readonly Dictionary<(string Group, string Language), IReadOnlyDictionary<string, string>> EmbeddedCatalogs = new();

    public static IReadOnlyDictionary<string, string> Load(string group, string languageCode)
    {
        string language = string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        lock (EmbeddedCatalogs)
        {
            if (EmbeddedCatalogs.TryGetValue((group, language), out var cached)) return cached;
            var values = Load<string>(group, language, out bool embedded).ToFrozenDictionary(StringComparer.Ordinal);
            // Unknown groups and absent/invalid resources never occupy cache entries.
            if (embedded) EmbeddedCatalogs[(group, language)] = values;
            return values;
        }
    }

    public static IReadOnlyDictionary<string, Dictionary<string, string>> LoadEntries(string group, string languageCode)
        // Nested entry dictionaries remain caller-owned; do not share mutable values.
        => Load<Dictionary<string, string>>(group, languageCode, out _);

    private static IReadOnlyDictionary<string, T> Load<T>(string group, string languageCode, out bool isEmbedded)
    {
        isEmbedded = false;
        Assembly assembly = typeof(JsonLocalizationCatalog).Assembly;
        string normalizedLanguage = string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase)
            ? "zh"
            : "en";
        (string category, string name) = group switch
        {
            "rt2_ui_legacy" => ("UI", "legacy"),
            "rt2_ui_v13" => ("UI", "workbench"),
            "seed_presets" => ("SeedLibrary", "presets"),
            "seed_recommendations" => ("SeedLibrary", "seeds"),
            _ => (string.Empty, string.Empty)
        };
        if (string.IsNullOrEmpty(category))
        {
            return new Dictionary<string, T>(StringComparer.Ordinal);
        }
        string resourceName = $"RolltheSpire2.Texts.{normalizedLanguage}.{category}.{name}.json";

        IReadOnlyDictionary<string, T> embedded = LoadStream<T>(assembly.GetManifestResourceStream(resourceName));
        if (embedded.Count > 0)
        {
            isEmbedded = true;
            return embedded;
        }

        string assemblyDirectory = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
        string loosePath = Path.Combine(assemblyDirectory, "Texts", normalizedLanguage, category, name + ".json");
        if (!File.Exists(loosePath))
        {
            return new Dictionary<string, T>(StringComparer.Ordinal);
        }

        try
        {
            using FileStream stream = File.OpenRead(loosePath);
            return LoadStream<T>(stream);
        }
        catch (IOException)
        {
            return new Dictionary<string, T>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, T>(StringComparer.Ordinal);
        }
    }

    private static IReadOnlyDictionary<string, T> LoadStream<T>(Stream? stream)
    {
        if (stream is null)
        {
            return new Dictionary<string, T>(StringComparer.Ordinal);
        }

        using (stream)
        {
            try
            {
                Dictionary<string, T>? values = JsonSerializer.Deserialize<Dictionary<string, T>>(stream);
                return values is null
                    ? new Dictionary<string, T>(StringComparer.Ordinal)
                    : new Dictionary<string, T>(values, StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                return new Dictionary<string, T>(StringComparer.Ordinal);
            }
        }
    }
}
