using Godot;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Settings;

// Workspace files seed each directory once. Native hover callbacks use the active
// game directory's memory snapshot, never a temporary workspace or a disk poll.
internal static class InRunFeaturePreferences
{
    internal sealed record Snapshot(bool OperationPredictionEnabled, bool MapPredictionEnabled, string LanguageOverride,
        bool CrystalSphereAvoidCurse, bool CrystalSphereIncludeDriftwood,
        bool CrystalSphereIncludeEnchantments, bool CrystalSphereTransparentMask,
        OperationPredictionPreferences OperationPredictionModes);
    private static readonly Dictionary<string, Snapshot> ByDirectory = new(StringComparer.OrdinalIgnoreCase);
    private static string? _runtimeDirectory;
    internal static event Action<bool>? OperationChanged;
    internal static event Action? OperationPolicyChanged;
    internal static event Action<bool>? MapChanged;

    private static string Key(string directory) => Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    internal static void Initialize(string directory, UserPreferencesDocument preferences)
    {
        directory = Key(directory);
        if (!ByDirectory.TryGetValue(directory, out var current))
        {
            current = new(preferences.OperationPredictionEnabled, preferences.MapPredictionEnabled, preferences.LanguageOverride,
                preferences.CrystalSphereAvoidCurse, preferences.CrystalSphereIncludeDriftwood,
                preferences.CrystalSphereIncludeEnchantments, preferences.CrystalSphereTransparentMask,
                (preferences.OperationPredictionModes ?? new()).Normalize());
            ByDirectory.Add(directory, current);
        }
        // Another UI surface may construct a workspace while an older instance
        // remains open. Neither its disk copy nor a temporary test root is a
        // new authority for this directory's live feature choices.
        preferences.OperationPredictionEnabled = current.OperationPredictionEnabled;
        preferences.MapPredictionEnabled = current.MapPredictionEnabled;
        preferences.LanguageOverride = current.LanguageOverride;
        preferences.CrystalSphereAvoidCurse = current.CrystalSphereAvoidCurse;
        preferences.CrystalSphereIncludeDriftwood = current.CrystalSphereIncludeDriftwood;
        preferences.CrystalSphereIncludeEnchantments = current.CrystalSphereIncludeEnchantments;
        preferences.CrystalSphereTransparentMask = current.CrystalSphereTransparentMask;
        preferences.OperationPredictionModes = current.OperationPredictionModes;
    }

    private static Snapshot Runtime
    {
        get
        {
            if (_runtimeDirectory == null)
            {
                string userDirectory = OS.GetUserDataDir();
                string directory = Key(Path.Combine(userDirectory, "RolltheSpire2", "state"));
                if (!ByDirectory.ContainsKey(directory)) _ = new SearchWorkspace(userDirectory, false);
                _runtimeDirectory = directory;
            }
            return ByDirectory[_runtimeDirectory];
        }
    }

    internal static bool OperationPredictionEnabled => Runtime.OperationPredictionEnabled;
    internal static OperationPredictionMode GetOperationMode(OperationPredictionCategory category) =>
        Runtime.OperationPredictionModes.Get(category);
    internal static OperationPredictionMode GetOperationMode(string id) => Runtime.OperationPredictionModes.Get(id);
    internal static OperationPredictionPreferences CaptureOperationModes() => Runtime.OperationPredictionModes;
    internal static bool MapPredictionEnabled => Runtime.MapPredictionEnabled;
    // Native entry may precede UI initialization. Callers resolve an unknown
    // value through Runtime once; this cached read itself never loads documents.
    internal static bool? CachedMapPredictionEnabled => _runtimeDirectory is { } directory &&
        ByDirectory.TryGetValue(directory, out var current) ? current.MapPredictionEnabled : null;
    internal static string LanguageOverride => Runtime.LanguageOverride;
    internal static bool CrystalSphereTransparentMask => Runtime.CrystalSphereTransparentMask;

    internal static void SetCrystalSphere(string directory, bool? avoidCurse, bool? includeDriftwood,
        bool? includeEnchantments, bool? transparentMask)
    {
        directory = Key(directory);
        var current = ByDirectory[directory];
        ByDirectory[directory] = current with
        {
            CrystalSphereAvoidCurse = avoidCurse ?? current.CrystalSphereAvoidCurse,
            CrystalSphereIncludeDriftwood = includeDriftwood ?? current.CrystalSphereIncludeDriftwood,
            CrystalSphereIncludeEnchantments = includeEnchantments ?? current.CrystalSphereIncludeEnchantments,
            CrystalSphereTransparentMask = transparentMask ?? current.CrystalSphereTransparentMask
        };
    }

    internal static void SetLanguage(string directory, string language)
    {
        directory = Key(directory);
        ByDirectory[directory] = ByDirectory[directory] with { LanguageOverride = language };
    }

    internal static void SetOperation(string directory, bool enabled)
    {
        directory = Key(directory);
        var current = ByDirectory[directory];
        if (current.OperationPredictionEnabled == enabled) return;
        ByDirectory[directory] = current with { OperationPredictionEnabled = enabled };
        if (StringComparer.OrdinalIgnoreCase.Equals(directory, _runtimeDirectory)) OperationChanged?.Invoke(enabled);
    }

    internal static void SetOperationMode(string directory, OperationPredictionCategory category, OperationPredictionMode mode)
    {
        directory = Key(directory);
        var current = ByDirectory[directory];
        var updated = current.OperationPredictionModes.With(category, mode);
        if (current.OperationPredictionModes == updated) return;
        ByDirectory[directory] = current with { OperationPredictionModes = updated };
        if (StringComparer.OrdinalIgnoreCase.Equals(directory, _runtimeDirectory)) OperationPolicyChanged?.Invoke();
    }

    internal static void SetOperationMode(string directory, string id, OperationPredictionMode mode)
    {
        directory = Key(directory);
        var current = ByDirectory[directory];
        var updated = current.OperationPredictionModes.With(id, mode);
        if (current.OperationPredictionModes == updated) return;
        ByDirectory[directory] = current with { OperationPredictionModes = updated };
        if (StringComparer.OrdinalIgnoreCase.Equals(directory, _runtimeDirectory)) OperationPolicyChanged?.Invoke();
    }

    internal static void SetMap(string directory, bool enabled)
    {
        directory = Key(directory);
        var current = ByDirectory[directory];
        if (current.MapPredictionEnabled == enabled) return;
        ByDirectory[directory] = current with { MapPredictionEnabled = enabled };
        if (StringComparer.OrdinalIgnoreCase.Equals(directory, _runtimeDirectory)) MapChanged?.Invoke(enabled);
    }
}
