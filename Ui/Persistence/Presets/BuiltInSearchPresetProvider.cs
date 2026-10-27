using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Ui.Persistence;

/// <summary>
/// Owner-curated official presets are bundled as Preset v2 data documents under
/// OfficialPresets/. The provider joins presentation text and normalizes source/identity: the payload is
/// parsed by the same persistence model and loaded through the same catalog/compatibility
/// path as player presets. One malformed bundled file is skipped fail-soft.
/// Bundled presentation text is joined by official identity before normalization.
/// </summary>
internal sealed class BuiltInSearchPresetProvider
{
    internal const string ResourcePrefix = "RolltheSpire2.OfficialPresets.";
    internal const string OfficialIdPrefix = "official:";

    private readonly IReadOnlyList<SearchPresetDefinition> _presets;

    public BuiltInSearchPresetProvider()
        : this(typeof(BuiltInSearchPresetProvider).Assembly)
    {
    }

    internal BuiltInSearchPresetProvider(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _presets = LoadFailSoft(
            assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                               name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
            assembly.GetManifestResourceStream,
            assembly == typeof(BuiltInSearchPresetProvider).Assembly ? JsonLocalizationCatalog.LoadEntries("seed_presets", "zh") : null,
            assembly == typeof(BuiltInSearchPresetProvider).Assembly ? JsonLocalizationCatalog.LoadEntries("seed_presets", "en") : null);
    }

    public IReadOnlyList<SearchPresetDefinition> GetPresets() => _presets;

    internal static IReadOnlyList<SearchPresetDefinition> LoadFailSoft(
        IReadOnlyList<string> resourceNames,
        Func<string, Stream?> openResource,
        IReadOnlyDictionary<string, Dictionary<string, string>>? chineseTexts = null,
        IReadOnlyDictionary<string, Dictionary<string, string>>? englishTexts = null)
    {
        ArgumentNullException.ThrowIfNull(resourceNames);
        ArgumentNullException.ThrowIfNull(openResource);

        JsonSerializerOptions json = SearchPresetStore.CreateJsonOptions();
        var presets = new List<SearchPresetDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var titles = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        foreach (string resourceName in resourceNames)
        {
            string fileName = ResourceFileName(resourceName);
            try
            {
                using Stream? stream = openResource(resourceName);
                if (stream is null)
                    throw new FileNotFoundException("BundledOfficialPresetResourceMissing", resourceName);

                SearchPresetStore.PersistedSearchPreset? persisted =
                    JsonSerializer.Deserialize<SearchPresetStore.PersistedSearchPreset>(stream, json);
                if (persisted is null)
                    throw new InvalidDataException("OfficialPresetEntryNull");
                if (persisted.SchemaVersion > SearchPresetStore.SchemaVersion)
                {
                    RuntimeLog.Warn(
                        $"officialSearchPresetEntrySkipped=true;failSoft=true;reason=FutureSchema;file={fileName};" +
                        $"schema={persisted.SchemaVersion};supported={SearchPresetStore.SchemaVersion}");
                    continue;
                }

                // The exported query uses the ordinary persistence contract. The
                // source file name, not the persisted user:* Id, becomes official identity.
                persisted.Id = BuildOfficialId(resourceName);
                // Custom/exported resource factories keep their own literal text. Only
                // the bundled assembly path supplies the separate presentation catalog.
                if (chineseTexts is not null && englishTexts is not null)
                {
                    persisted.Title = chineseTexts[persisted.Id]["title"];
                    persisted.Description = chineseTexts[persisted.Id]["description"];
                    persisted.EnglishTitle = englishTexts[persisted.Id]["title"];
                    persisted.EnglishDescription = englishTexts[persisted.Id]["description"];
                }
                SearchPresetDefinition preset = SearchPresetStore.Normalize(
                    persisted,
                    SearchPresetSource.BuiltIn,
                    json);

                if (!ids.Add(preset.Id))
                {
                    RuntimeLog.Warn(
                        $"officialSearchPresetEntrySkipped=true;failSoft=true;reason=DuplicateId;file={fileName};id={preset.Id}");
                    continue;
                }
                if (!titles.Add(preset.Title))
                {
                    RuntimeLog.Warn(
                        $"officialSearchPresetEntrySkipped=true;failSoft=true;reason=DuplicateName;file={fileName};name={preset.Title}");
                    continue;
                }
                presets.Add(preset);
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn(
                    $"officialSearchPresetEntrySkipped=true;failSoft=true;file={fileName};" +
                    $"issue={ex.GetType().Name}:{SearchPresetStore.Compact(ex.Message)}");
            }
        }

        return presets
            .OrderBy(preset => preset.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    internal static string BuildOfficialId(string resourceName)
    {
        string fileName = ResourceFileName(resourceName);
        string stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (stem.Length == 0)
            throw new InvalidDataException("OfficialPresetFileNameInvalid");
        return OfficialIdPrefix + stem;
    }

    private static string ResourceFileName(string resourceName)
    {
        if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            throw new InvalidDataException("OfficialPresetResourcePrefixInvalid");
        return resourceName[ResourcePrefix.Length..];
    }
}
