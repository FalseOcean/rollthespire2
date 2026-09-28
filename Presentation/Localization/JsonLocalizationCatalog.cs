using System.Reflection;
using System.Text.Json;

namespace RolltheSpire2.Presentation.Localization;

internal static class JsonLocalizationCatalog
{
    public static IReadOnlyDictionary<string, string> Load(string group, string languageCode)
    {
        Assembly assembly = typeof(JsonLocalizationCatalog).Assembly;
        string normalizedLanguage = string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase)
            ? "zh"
            : "en";
        string resourceName = $"RolltheSpire2.Localization.{group}.{normalizedLanguage}.json";

        IReadOnlyDictionary<string, string> embedded = LoadStream(assembly.GetManifestResourceStream(resourceName));
        if (embedded.Count > 0)
        {
            return embedded;
        }

        string assemblyDirectory = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
        string loosePath = Path.Combine(assemblyDirectory, "Localization", group, normalizedLanguage + ".json");
        if (!File.Exists(loosePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            using FileStream stream = File.OpenRead(loosePath);
            return LoadStream(stream);
        }
        catch (IOException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static IReadOnlyDictionary<string, string> LoadStream(Stream? stream)
    {
        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using (stream)
        {
            try
            {
                Dictionary<string, string>? values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                return values is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(values, StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }
    }
}
