using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.InRunConsole;

/// <summary>Only the display preference is durable; live models never enter this file.</summary>
internal sealed class ConsolePreferencesStore
{
    // Stable serialized identities; changing the UI's labels does not change preferences.
    private static readonly string[] CategoryKeys = ["cards", "relics", "potions", "ancients", "fights", "events"];
    private readonly string _path;
    private bool _preserveUnreadable;

    internal ConsolePreferencesStore(string userDataDirectory)
        => _path = Path.Combine(userDataDirectory, "RolltheSpire2", "state", "console_preferences.json");

    internal bool LoadCompactIcons(int category, bool fallback = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(category);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(category, CategoryKeys.Length);
        try { return File.Exists(_path) ? ReadCompactIcons()[category] : fallback; }
        catch (Exception ex)
        {
            _preserveUnreadable = true;
            RuntimeLog.WarnException($"inRunConsolePreferenceReadFailed=true;path={_path}", ex);
            return fallback;
        }
    }

    internal void SaveCompactIcons(int category, bool compactIcons)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(category);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(category, CategoryKeys.Length);
        if (_preserveUnreadable) return;
        try
        {
            // Revalidate before replacing an existing file, including one changed while
            // the console was open. A newer/unknown document is not ours to overwrite.
            bool[] preferences = File.Exists(_path) ? ReadCompactIcons() : Enumerable.Repeat(true, CategoryKeys.Length).ToArray();
            preferences[category] = compactIcons;
            var compactIconsByCategory = CategoryKeys.Select((key, index) => (key, value: preferences[index]))
                .ToDictionary(item => item.key, item => item.value);
            SearchPersistenceFile.WriteAtomic(_path, JsonSerializer.SerializeToUtf8Bytes(new { version = 2, compactIconsByCategory }));
        }
        catch (Exception ex)
        {
            if (ex is JsonException or InvalidDataException) _preserveUnreadable = true;
            RuntimeLog.WarnException($"inRunConsolePreferenceWriteFailed=true;path={_path}", ex);
        }
    }

    private bool[] ReadCompactIcons()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out int value))
            throw new InvalidDataException("Unsupported console preference document; preserving its original contents.");
        if (value == 1 && root.TryGetProperty("compactIcons", out var compact) &&
            compact.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return Enumerable.Repeat(compact.GetBoolean(), CategoryKeys.Length).ToArray();
        if (value != 2 || !root.TryGetProperty("compactIconsByCategory", out var categories) ||
            categories.ValueKind != JsonValueKind.Object || categories.EnumerateObject().Count() != CategoryKeys.Length)
            throw new InvalidDataException("Unsupported console preference document; preserving its original contents.");
        var result = new bool[CategoryKeys.Length];
        for (int i = 0; i < CategoryKeys.Length; i++)
        {
            if (!categories.TryGetProperty(CategoryKeys[i], out var preference) ||
                preference.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Unsupported console category preference; preserving its original contents.");
            result[i] = preference.GetBoolean();
        }
        return result;
    }
}
