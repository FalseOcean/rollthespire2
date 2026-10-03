using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Ui.Persistence;

internal enum SearchPresetSource : byte
{
    BuiltIn = 0,
    Temporary = 1,
    User = 2
}

/// <summary>
/// Extensible presentation-only visual reference. Persistence deliberately stores a
/// kind string plus stable content identity rather than hard-wiring Preset assets to
/// Relic. B-Core UI currently resolves only Kind=Relic; future kinds can be added
/// without changing the Preset query contract or schema shape.
/// </summary>
internal sealed record SearchPresetVisualIconRef(
    string Kind,
    string StableContentIdentity,
    string ResourceHint = "")
{
    public const string RelicKind = "Relic";

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Kind) &&
        !string.IsNullOrWhiteSpace(StableContentIdentity);

    public bool TryGetModelKey(out ModelKey key) =>
        ModelKey.TryParseExact(StableContentIdentity ?? string.Empty, out key) && key.IsValid;

    public static SearchPresetVisualIconRef FromRelic(ModelKey key) =>
        new(RelicKind, key.Serialized, string.Empty);
}

internal sealed record SearchPresetProbabilitySnapshot(
    string Status,
    double? TotalProbability,
    DateTimeOffset CapturedAtUtc)
{
    public static SearchPresetProbabilitySnapshot Unavailable(DateTimeOffset capturedAtUtc) =>
        new(SearchProbabilityQuickViewStatus.Unavailable.ToString(), null, capturedAtUtc);

    public static SearchPresetProbabilitySnapshot From(
        SearchProbabilityQuickView view,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(view);
        double? total = view.Status == SearchProbabilityQuickViewStatus.Impossible
            ? 0d
            : view.TotalProbability;
        return new SearchPresetProbabilitySnapshot(view.Status.ToString(), total, capturedAtUtc);
    }

    public bool IsComplete =>
        string.Equals(Status, SearchProbabilityQuickViewStatus.Complete.ToString(), StringComparison.Ordinal);

    public bool IsImpossible =>
        string.Equals(Status, SearchProbabilityQuickViewStatus.Impossible.ToString(), StringComparison.Ordinal);
}

internal static class SearchPresetUnlockKinds
{
    public const string Unknown = "Unknown";
    public const string Full = "Full";
    public const string Partial = "Partial";
}

/// <summary>
/// Historical Runtime Authority interpretation captured with the Query asset. These
/// facts are provenance only and are never consulted as Search admission authority.
/// The stored schema + overall semantic fingerprint allows a later RT2 baseline registry
/// to reinterpret an old Unknown preset without rewriting the original historical facts.
/// </summary>
internal sealed record SearchPresetProvenance(
    string GameVersion,
    int FingerprintSchemaVersion,
    string SemanticFingerprint,
    string EnvironmentStatus,
    string MatchedBaselineGameVersion,
    string ComparisonBaselineGameVersion,
    string EnvironmentReason,
    string UnlockKind,
    DateTimeOffset CapturedAtUtc)
{
    public static SearchPresetProvenance Unknown(string gameVersion, DateTimeOffset capturedAtUtc) =>
        new(
            gameVersion ?? string.Empty,
            0,
            string.Empty,
            SemanticEnvironmentStatus.Unknown.ToString(),
            string.Empty,
            string.Empty,
            "PresetProvenanceUnavailable",
            SearchPresetUnlockKinds.Unknown,
            capturedAtUtc);
}

/// <summary>
/// Preset asset. v1/v2 carry the historical solo SearchDraft; v3 can instead carry
/// the complete typed workbench intent. Raw payloads survive metadata edits even when
/// their shape cannot be materialized by this version.
/// </summary>
internal sealed record SearchPresetDefinition(
    string Id,
    SearchPresetSource Source,
    string Title,
    string Description,
    ModelKey CharacterKey,
    int Ascension,
    SearchDraft? Draft,
    string RawQueryJson,
    int ConditionCount,
    IReadOnlyList<SearchPresetVisualIconRef> VisualIcons,
    SearchPresetProbabilitySnapshot SavedProbability,
    SearchPresetProvenance Provenance,
    DateTimeOffset CreatedAtUtc)
{
    public string Name => Title; // narrow v1/source compatibility alias
    public string EnvironmentFingerprint { get; init; } = "";
    public WorkbenchSearchDraft? Workbench { get; init; }
    public string RawWorkbenchJson { get; init; } = string.Empty;
    public bool IsWorkbench => Workbench is not null || !string.IsNullOrWhiteSpace(RawWorkbenchJson);
    public bool IsMultiplayer => Workbench?.Mode == WorldGameMode.Multiplayer;
    public bool QueryResolved => IsWorkbench ? Workbench is not null : Draft is not null;
}

internal sealed record SearchPresetCapture(
    ModelKey CharacterKey,
    int Ascension,
    SearchDraft? Draft,
    int ConditionCount,
    SearchPresetProbabilitySnapshot SavedProbability,
    SearchPresetProvenance Provenance,
    DateTimeOffset CapturedAtUtc)
{
    public WorkbenchSearchDraft? Workbench { get; init; }
}

internal interface ISearchPresetProvider
{
    IReadOnlyList<SearchPresetDefinition> GetPresets();
}

/// <summary>
/// Owner-curated official presets are bundled as ordinary Preset v2 JSON documents under
/// OfficialPresets/. The provider owns only source/identity normalization: the payload is
/// parsed by the same persistence model and loaded through the same catalog/compatibility
/// path as player presets. One malformed bundled file is skipped fail-soft.
/// </summary>
internal sealed class BuiltInSearchPresetProvider : ISearchPresetProvider
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
            assembly.GetManifestResourceStream);
    }

    public IReadOnlyList<SearchPresetDefinition> GetPresets() => _presets;

    internal static IReadOnlyList<SearchPresetDefinition> LoadFailSoft(
        IReadOnlyList<string> resourceNames,
        Func<string, Stream?> openResource)
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

                // Owner workflow intentionally accepts an ordinary user-exported Preset JSON.
                // The source file name, not the persisted user:* Id, becomes official identity.
                persisted.Id = BuildOfficialId(resourceName);
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

internal sealed class UserSearchPresetProvider : ISearchPresetProvider
{
    private readonly SearchPresetStore _store;

    public UserSearchPresetProvider(SearchPresetStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public IReadOnlyList<SearchPresetDefinition> GetPresets() => _store.GetPresets();
    public IReadOnlyList<string> LoadIssues => _store.LoadIssues;

    public SearchPresetDefinition Save(
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons,
        SearchPresetCapture capture) =>
        _store.Save(title, description, visualIcons, capture);

    public SearchPresetDefinition SaveFromExisting(
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons,
        SearchPresetDefinition source) =>
        _store.SaveFromExisting(title, description, visualIcons, source);

    public SearchPresetDefinition UpdateMetadata(
        string id,
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons) =>
        _store.UpdateMetadata(id, title, description, visualIcons);

    public bool Delete(string id) => _store.Delete(id);
    public SearchPresetDefinition UpdateEnvironment(string id, string fingerprint) => _store.UpdateEnvironment(id, fingerprint);
}

internal sealed class TemporarySearchPresetProvider : ISearchPresetProvider
{
    private readonly TemporarySearchPresetStore _store;

    public TemporarySearchPresetProvider(TemporarySearchPresetStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public IReadOnlyList<SearchPresetDefinition> GetPresets() => _store.GetPresets();
    public IReadOnlyList<string> LoadIssues => _store.LoadIssues;

    public SearchPresetDefinition Record(SearchPresetCapture capture) => _store.Record(capture);
}

internal sealed class SearchPresetCatalog
{
    private readonly BuiltInSearchPresetProvider _builtIn;
    private readonly TemporarySearchPresetProvider _temporary;
    private readonly UserSearchPresetProvider _user;

    public SearchPresetCatalog(string stateDirectory)
    {
        _builtIn = new BuiltInSearchPresetProvider();
        _temporary = new TemporarySearchPresetProvider(new TemporarySearchPresetStore(stateDirectory));
        _user = new UserSearchPresetProvider(new SearchPresetStore(stateDirectory));
    }

    public IReadOnlyList<SearchPresetDefinition> GetAll() =>
        _builtIn.GetPresets()
            .Concat(_temporary.GetPresets())
            .Concat(_user.GetPresets())
            .OrderBy(preset => preset.Source)
            .ThenByDescending(preset => preset.Source == SearchPresetSource.Temporary ? preset.CreatedAtUtc : DateTimeOffset.MinValue)
            .ThenBy(preset => preset.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public IReadOnlyList<string> LoadIssues => _temporary.LoadIssues.Concat(_user.LoadIssues).ToArray();

    public SearchPresetDefinition SaveUserPreset(
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons,
        SearchPresetCapture capture) =>
        _user.Save(title, description, visualIcons, capture);

    public SearchPresetDefinition SaveUserPresetFromExisting(
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons,
        SearchPresetDefinition source) =>
        _user.SaveFromExisting(title, description, visualIcons, source);

    public SearchPresetDefinition UpdateUserMetadata(
        string id,
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons) =>
        _user.UpdateMetadata(id, title, description, visualIcons);

    public SearchPresetDefinition RecordTemporary(SearchPresetCapture capture) =>
        _temporary.Record(capture);

    public bool DeleteUserPreset(string id) => _user.Delete(id);
    public SearchPresetDefinition UpdateUserEnvironment(string id, string fingerprint) => _user.UpdateEnvironment(id, fingerprint);

    public bool TryGetUserByName(string name, out SearchPresetDefinition preset)
    {
        string normalized = (name ?? string.Empty).Trim();
        preset = _user.GetPresets().FirstOrDefault(candidate =>
            string.Equals(candidate.Title, normalized, StringComparison.CurrentCultureIgnoreCase))!;
        return preset is not null;
    }

    public bool TryGet(string id, out SearchPresetDefinition preset)
    {
        preset = GetAll().FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.Ordinal))!;
        return preset is not null;
    }
}

/// <summary>
/// Fail-soft user preset store. v3 keeps one file per user asset. v1/v2 files are read
/// through a narrow compatibility projection and remain valid; no generic migration
/// framework is introduced.
/// </summary>
internal sealed class SearchPresetStore
{
    internal const int SchemaVersion = 3;
    private const string DirectoryName = "search_presets";

    private readonly string _directory;
    private readonly JsonSerializerOptions _json;
    private readonly List<SearchPresetDefinition> _presets = new();
    private readonly List<string> _loadIssues = new();
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);

    public SearchPresetStore(string stateDirectory)
    {
        _directory = Path.Combine(stateDirectory, DirectoryName);
        _json = CreateJsonOptions();
        LoadFailSoft();
    }

    public IReadOnlyList<SearchPresetDefinition> GetPresets() => _presets.ToArray();
    public IReadOnlyList<string> LoadIssues => _loadIssues.ToArray();

    public SearchPresetDefinition Save(
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons,
        SearchPresetCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        string normalizedTitle = NormalizeTitle(title);
        if (normalizedTitle.Length == 0)
            throw new ArgumentException("Search preset title is required.", nameof(title));
        if (!capture.CharacterKey.IsValid)
            throw new ArgumentException("Search preset character must be valid.", nameof(capture));

        int existingIndex = _presets.FindIndex(candidate =>
            string.Equals(candidate.Title, normalizedTitle, StringComparison.CurrentCultureIgnoreCase));
        string id = existingIndex >= 0
            ? _presets[existingIndex].Id
            : "user:" + Guid.NewGuid().ToString("N");

        SearchPresetDefinition saved = BuildFromCapture(
            id,
            SearchPresetSource.User,
            normalizedTitle,
            NormalizeDescription(description),
            NormalizeVisualIcons(visualIcons),
            capture,
            _json);

        WriteOne(saved);
        if (existingIndex >= 0)
            _presets[existingIndex] = saved;
        else
            _presets.Add(saved);
        return saved;
    }

    public SearchPresetDefinition SaveFromExisting(
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons,
        SearchPresetDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string normalizedTitle = NormalizeTitle(title);
        if (normalizedTitle.Length == 0)
            throw new ArgumentException("Search preset title is required.", nameof(title));

        int existingIndex = _presets.FindIndex(candidate =>
            string.Equals(candidate.Title, normalizedTitle, StringComparison.CurrentCultureIgnoreCase));
        string id = existingIndex >= 0
            ? _presets[existingIndex].Id
            : "user:" + Guid.NewGuid().ToString("N");
        SearchPresetDefinition saved = source with
        {
            Id = id,
            Source = SearchPresetSource.User,
            Title = normalizedTitle,
            Description = NormalizeDescription(description),
            VisualIcons = NormalizeVisualIcons(visualIcons),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        WriteOne(saved);
        if (existingIndex >= 0)
            _presets[existingIndex] = saved;
        else
            _presets.Add(saved);
        return saved;
    }

    public SearchPresetDefinition UpdateMetadata(
        string id,
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons)
    {
        int index = _presets.FindIndex(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        if (index < 0)
            throw new KeyNotFoundException("Search preset does not exist: " + id);
        string normalizedTitle = NormalizeTitle(title);
        if (normalizedTitle.Length == 0)
            throw new ArgumentException("Search preset title is required.", nameof(title));
        for (int candidateIndex = 0; candidateIndex < _presets.Count; candidateIndex++)
        {
            if (candidateIndex == index) continue;
            if (string.Equals(
                    _presets[candidateIndex].Title,
                    normalizedTitle,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                throw new InvalidOperationException("SearchPresetTitleAlreadyExists");
            }
        }

        SearchPresetDefinition updated = _presets[index] with
        {
            Title = normalizedTitle,
            Description = NormalizeDescription(description),
            VisualIcons = NormalizeVisualIcons(visualIcons)
        };
        WriteOne(updated);
        _presets[index] = updated;
        return updated;
    }

    public SearchPresetDefinition UpdateEnvironment(string id, string fingerprint)
    {
        int index = _presets.FindIndex(p => p.Id == id);
        if (index < 0) throw new KeyNotFoundException("SearchPresetMissing");
        if (string.IsNullOrWhiteSpace(fingerprint)) throw new InvalidOperationException("CurrentEnvironmentUnavailable");
        var updated = _presets[index] with { EnvironmentFingerprint = fingerprint };
        WriteOne(updated);
        _presets[index] = updated;
        return updated;
    }

    public bool Delete(string id)
    {
        int index = _presets.FindIndex(candidate =>
            string.Equals(candidate.Id, id, StringComparison.Ordinal));
        if (index < 0)
            return false;
        string path = PathForId(_presets[index].Id);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(
                $"searchPresetDeleteFileFailed=true;failSoft=true;id={id};issue={ex.GetType().Name}:{Compact(ex.Message)}");
            return false;
        }
        _presets.RemoveAt(index);
        _paths.Remove(id);
        return true;
    }

    private void LoadFailSoft()
    {
        _presets.Clear();
        _paths.Clear();
        _loadIssues.Clear();
        if (!Directory.Exists(_directory))
            return;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            _loadIssues.Add(LoadIssue("search_presets", LoadIssueReason(ex)));
            RuntimeLog.Warn($"searchPresetDirectoryReadFailed=true;failSoft=true;issue={ex.GetType().Name}:{Compact(ex.Message)}");
            return;
        }
        foreach (string path in paths)
        {
            string fileName = Path.GetFileName(path);
            try
            {
                string jsonText = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(jsonText))
                {
                    _loadIssues.Add(LoadIssue(fileName, "EmptyFile"));
                    RuntimeLog.Warn($"searchPresetEntrySkipped=true;failSoft=true;reason=EmptyFile;file={fileName}");
                    continue;
                }
                PersistedSearchPreset? persisted = JsonSerializer.Deserialize<PersistedSearchPreset>(jsonText, _json);
                if (persisted is null)
                    throw new InvalidDataException("PresetEntryNull");
                if (persisted.SchemaVersion > SchemaVersion)
                {
                    _loadIssues.Add(LoadIssue(fileName, $"UnsupportedFutureSchema:{persisted.SchemaVersion};supported={SchemaVersion}"));
                    RuntimeLog.Warn(
                        $"searchPresetEntrySkipped=true;failSoft=true;reason=FutureSchema;file={fileName};schema={persisted.SchemaVersion};supported={SchemaVersion}");
                    continue;
                }
                SearchPresetDefinition preset = Normalize(persisted, SearchPresetSource.User, _json);
                if (!ids.Add(preset.Id))
                {
                    _loadIssues.Add(LoadIssue(fileName, "DuplicateId"));
                    RuntimeLog.Warn($"searchPresetEntrySkipped=true;failSoft=true;reason=DuplicateId;file={fileName};id={preset.Id}");
                    continue;
                }
                if (!names.Add(preset.Title))
                {
                    _loadIssues.Add(LoadIssue(fileName, "DuplicateName"));
                    RuntimeLog.Warn($"searchPresetEntrySkipped=true;failSoft=true;reason=DuplicateName;file={fileName};name={preset.Title}");
                    continue;
                }
                _presets.Add(preset);
                _paths[preset.Id] = path;
            }
            catch (Exception ex)
            {
                _loadIssues.Add(LoadIssue(fileName, LoadIssueReason(ex)));
                RuntimeLog.Warn(
                    $"searchPresetEntrySkipped=true;failSoft=true;file={fileName};issue={ex.GetType().Name}:{Compact(ex.Message)}");
            }
        }
    }

    private void WriteOne(SearchPresetDefinition preset)
    {
        string path = PathForId(preset.Id);
        PersistedSearchPreset persisted = ToPersisted(preset, _json);
        SearchPersistenceFile.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(persisted, _json));
        _paths[preset.Id] = path;
    }

    private string PathForId(string id)
    {
        // Imported files may have any file name. Identity is not a filesystem path.
        if (_paths.TryGetValue(id, out string? loadedPath)) return loadedPath;
        string suffix = id.StartsWith("user:", StringComparison.Ordinal)
            ? id["user:".Length..]
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        if (suffix.Length == 0 || suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || suffix is "." or "..")
            throw new InvalidDataException("PresetIdInvalid");
        return Path.Combine(_directory, suffix + ".json");
    }

    internal static JsonSerializerOptions CreateJsonOptions()
    {
        var json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            // A newer envelope may add context outside QuerySnapshot. Do not
            // erase those fields when an older client edits the display metadata.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        json.Converters.Add(new SearchWorkspacePersistence.ModelKeyJsonConverter());
        return json;
    }

    internal static SearchPresetDefinition BuildFromCapture(
        string id,
        SearchPresetSource source,
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons,
        SearchPresetCapture capture,
        JsonSerializerOptions json)
    {
        WorkbenchSearchDraft? workbench = capture.Workbench;
        string rawWorkbenchJson = string.Empty;
        SearchDraft? legacy = capture.Draft;
        if (workbench is not null)
        {
            if (legacy is not null || workbench.Character != capture.CharacterKey || workbench.Ascension != capture.Ascension)
                throw new InvalidDataException("PresetCaptureContextMismatch");
            SearchPresetCompatibilityResolver.ValidateWorkbenchShape(workbench);
            rawWorkbenchJson = JsonSerializer.Serialize(workbench.ToPresetIntent(), json);
            // Detach mutable unlock/editor lists from the live workbench before the
            // asset enters the catalog. Templates retain player intent only.
            workbench = JsonSerializer.Deserialize<WorkbenchSearchDraft>(rawWorkbenchJson, CreateWorkbenchJsonOptions(json))
                ?? throw new InvalidDataException("PresetWorkbenchShapeUnresolved");
            SearchPresetCompatibilityResolver.ValidateWorkbenchShape(workbench);
        }
        else if (legacy is null)
            throw new InvalidDataException("PresetQuerySnapshotMissing");
        string rawQueryJson = workbench is null ? JsonSerializer.Serialize(legacy, json) : string.Empty;
        return new SearchPresetDefinition(
            id,
            source,
            title,
            description,
            capture.CharacterKey,
            Math.Clamp(capture.Ascension, SeedPredictionInputLimits.MinimumAscension, SeedPredictionInputLimits.MaximumAscension),
            legacy,
            rawQueryJson,
            Math.Max(0, capture.ConditionCount),
            NormalizeVisualIcons(visualIcons),
            capture.SavedProbability,
            capture.Provenance,
            capture.CapturedAtUtc)
        {
            Workbench = workbench,
            RawWorkbenchJson = rawWorkbenchJson
        };
    }

    private static JsonSerializerOptions CreateWorkbenchJsonOptions(JsonSerializerOptions json) => new(json)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    internal static SearchPresetDefinition Normalize(
        PersistedSearchPreset persisted,
        SearchPresetSource source,
        JsonSerializerOptions json)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        if (persisted.SchemaVersion > SchemaVersion)
            throw new InvalidDataException($"UnsupportedFutureSchema:{persisted.SchemaVersion}");
        if (persisted.SchemaVersion is < 1)
            throw new InvalidDataException($"UnsupportedPresetSchema:{persisted.SchemaVersion}");
        string id = persisted.Id?.Trim() ?? string.Empty;
        string expectedPrefix = source switch
        {
            SearchPresetSource.BuiltIn => BuiltInSearchPresetProvider.OfficialIdPrefix,
            SearchPresetSource.Temporary => "temporary:",
            _ => "user:"
        };
        if (!id.StartsWith(expectedPrefix, StringComparison.Ordinal) || id.Length <= expectedPrefix.Length)
            throw new InvalidDataException("PresetIdInvalid");

        string title = NormalizeTitle(
            !string.IsNullOrWhiteSpace(persisted.Title) ? persisted.Title : persisted.Name);
        if (title.Length == 0 && source != SearchPresetSource.Temporary)
            throw new InvalidDataException("PresetTitleMissing");

        if (!ModelKey.TryParseExact(persisted.CharacterKey ?? string.Empty, out ModelKey characterKey) || !characterKey.IsValid)
            throw new InvalidDataException("PresetCharacterInvalid");

        JsonElement queryElement = persisted.QuerySnapshot.ValueKind != JsonValueKind.Undefined && persisted.QuerySnapshot.ValueKind != JsonValueKind.Null
            ? persisted.QuerySnapshot
            : persisted.Draft;
        bool hasWorkbench = persisted.WorkbenchSnapshot.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        if (hasWorkbench && persisted.SchemaVersion < 3)
            throw new InvalidDataException("PresetWorkbenchSchemaMismatch");
        if (!hasWorkbench && queryElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidDataException("PresetQuerySnapshotMissing");

        string rawQueryJson = queryElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? string.Empty : queryElement.GetRawText();
        string rawWorkbenchJson = hasWorkbench ? persisted.WorkbenchSnapshot.GetRawText() : string.Empty;
        WorkbenchSearchDraft? workbench = null;
        SearchDraft? resolvedDraft = null;
        try
        {
            if (hasWorkbench)
            {
                // Ambiguous dual payloads are preserved, never silently prioritized.
                if (!string.IsNullOrWhiteSpace(rawQueryJson))
                    throw new InvalidDataException("PresetMultipleQueryPayloads");
                workbench = JsonSerializer.Deserialize<WorkbenchSearchDraft>(rawWorkbenchJson, CreateWorkbenchJsonOptions(json))
                    ?? throw new InvalidDataException("PresetWorkbenchShapeUnresolved");
                SearchPresetCompatibilityResolver.ValidateWorkbenchShape(workbench);
                if (workbench.Character != characterKey || workbench.Ascension != persisted.Ascension)
                    throw new InvalidDataException("PresetWorkbenchContextMismatch");
                workbench = workbench.WithoutCapturedAuthority();
                rawWorkbenchJson = JsonSerializer.Serialize(workbench, json);
            }
            else
            {
                // Unknown query fields cannot be presented as a complete load. Keep the
                // raw asset for metadata edits/export when its typed shape is unavailable.
                var resolver = new DefaultJsonTypeInfoResolver();
                resolver.Modifiers.Add(type =>
                {
                    if (type.Type != typeof(SearchDraft)) return;
                    // These retired run controls have an explicit migration policy;
                    // they never belonged to the semantic query and stay inert.
                    foreach (string name in new[] { "StartSeed", "ScanCount", "TargetMatchCount", "WorkerCount" })
                    {
                        JsonPropertyInfo property = type.CreateJsonPropertyInfo(typeof(JsonElement), name);
                        property.Set = (_, _) => { };
                        type.Properties.Add(property);
                    }
                });
                var queryJson = new JsonSerializerOptions(json)
                {
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    TypeInfoResolver = resolver
                };
                SearchDraft? draft = JsonSerializer.Deserialize<SearchDraft>(rawQueryJson, queryJson);
                if (draft is null || !SearchPresetCompatibilityResolver.HasCompleteShape(draft))
                    throw new InvalidDataException("PresetQueryShapeUnresolved");
                resolvedDraft = draft;
            }
        }
        catch (Exception ex)
        {
            workbench = null;
            // v2 keeps the raw query even when the current typed SearchDraft cannot
            // materialize it. B-Compatibility will own partial resolution.
            RuntimeLog.Warn(
                $"searchPresetQueryUnresolved=true;id={id};source={source};preservedRaw=true;issue={ex.GetType().Name}:{Compact(ex.Message)}");
        }

        var visualIcons = new List<SearchPresetVisualIconRef>();
        if (persisted.VisualIcons is not null)
        {
            foreach (PersistedVisualIconRef icon in persisted.VisualIcons)
            {
                var value = new SearchPresetVisualIconRef(
                    icon.Kind?.Trim() ?? string.Empty,
                    icon.StableContentIdentity?.Trim() ?? string.Empty,
                    icon.ResourceHint?.Trim() ?? string.Empty);
                if (value.IsValid) visualIcons.Add(value);
                if (visualIcons.Count == 3) break;
            }
        }
        // Narrow v1 compatibility: old user asset stored one optional VisualRelicKey.
        if (visualIcons.Count == 0 &&
            !string.IsNullOrWhiteSpace(persisted.VisualRelicKey) &&
            ModelKey.TryParseExact(persisted.VisualRelicKey, out ModelKey legacyRelic) && legacyRelic.IsValid)
        {
            visualIcons.Add(SearchPresetVisualIconRef.FromRelic(legacyRelic));
        }

        DateTimeOffset createdAt = persisted.CreatedAtUtc == default
            ? DateTimeOffset.UtcNow
            : persisted.CreatedAtUtc;
        SearchPresetProbabilitySnapshot probability = persisted.SavedProbability is null
            ? SearchPresetProbabilitySnapshot.Unavailable(createdAt)
            : new SearchPresetProbabilitySnapshot(
                string.IsNullOrWhiteSpace(persisted.SavedProbability.Status)
                    ? SearchProbabilityQuickViewStatus.Unavailable.ToString()
                    : persisted.SavedProbability.Status,
                persisted.SavedProbability.TotalProbability,
                persisted.SavedProbability.CapturedAtUtc == default ? createdAt : persisted.SavedProbability.CapturedAtUtc);
        SearchPresetProvenance provenance = persisted.Provenance is null
            ? SearchPresetProvenance.Unknown(string.Empty, createdAt)
            : new SearchPresetProvenance(
                persisted.Provenance.GameVersion ?? string.Empty,
                Math.Max(0, persisted.Provenance.FingerprintSchemaVersion),
                NormalizeSha256(persisted.Provenance.SemanticFingerprint),
                NormalizeEnvironmentStatus(persisted.Provenance.EnvironmentStatus),
                persisted.Provenance.MatchedBaselineGameVersion?.Trim() ?? string.Empty,
                persisted.Provenance.ComparisonBaselineGameVersion?.Trim() ?? string.Empty,
                persisted.Provenance.EnvironmentReason?.Trim() ?? string.Empty,
                NormalizeUnlockKind(persisted.Provenance.VanillaUnlockKind),
                persisted.Provenance.CapturedAtUtc == default ? createdAt : persisted.Provenance.CapturedAtUtc);

        int conditionCount = Math.Max(0, persisted.ConditionCount);
        return new SearchPresetDefinition(
            id,
            source,
            title,
            NormalizeDescription(persisted.Description),
            characterKey,
            Math.Clamp(persisted.Ascension, SeedPredictionInputLimits.MinimumAscension, SeedPredictionInputLimits.MaximumAscension),
            resolvedDraft,
            rawQueryJson,
            conditionCount,
            NormalizeVisualIcons(visualIcons),
            probability,
            provenance,
            createdAt)
        {
            Workbench = workbench,
            RawWorkbenchJson = rawWorkbenchJson,
            EnvironmentFingerprint = persisted.EnvironmentFingerprint ?? ""
        };
    }

    internal static PersistedSearchPreset ToPersisted(SearchPresetDefinition preset, JsonSerializerOptions json)
    {
        JsonElement querySnapshot = default;
        JsonElement workbenchSnapshot = default;
        string rawQuery = !string.IsNullOrWhiteSpace(preset.RawQueryJson) ? preset.RawQueryJson :
            preset.Draft is not null ? JsonSerializer.Serialize(preset.Draft, json) : string.Empty;
        if (!string.IsNullOrWhiteSpace(rawQuery))
        {
            using JsonDocument document = JsonDocument.Parse(rawQuery);
            querySnapshot = document.RootElement.Clone();
        }
        string rawWorkbench = preset.Workbench is not null ? JsonSerializer.Serialize(preset.Workbench.ToPresetIntent(), json) :
            !string.IsNullOrWhiteSpace(preset.RawWorkbenchJson) ? preset.RawWorkbenchJson : string.Empty;
        if (!string.IsNullOrWhiteSpace(rawWorkbench))
        {
            using JsonDocument document = JsonDocument.Parse(rawWorkbench);
            workbenchSnapshot = document.RootElement.Clone();
        }

        return new PersistedSearchPreset
        {
            SchemaVersion = SchemaVersion,
            Id = preset.Id,
            EnvironmentFingerprint = preset.EnvironmentFingerprint,
            Title = preset.Title,
            Description = preset.Description,
            CharacterKey = preset.CharacterKey.Serialized,
            Ascension = preset.Ascension,
            QuerySnapshot = querySnapshot,
            WorkbenchSnapshot = workbenchSnapshot,
            ConditionCount = preset.ConditionCount,
            VisualIcons = preset.VisualIcons.Take(3).Select(icon => new PersistedVisualIconRef
            {
                Kind = icon.Kind,
                StableContentIdentity = icon.StableContentIdentity,
                ResourceHint = icon.ResourceHint
            }).ToList(),
            SavedProbability = new PersistedProbabilitySnapshot
            {
                Status = preset.SavedProbability.Status,
                TotalProbability = preset.SavedProbability.TotalProbability,
                CapturedAtUtc = preset.SavedProbability.CapturedAtUtc
            },
            Provenance = new PersistedProvenance
            {
                GameVersion = preset.Provenance.GameVersion,
                FingerprintSchemaVersion = preset.Provenance.FingerprintSchemaVersion,
                SemanticFingerprint = preset.Provenance.SemanticFingerprint,
                EnvironmentStatus = preset.Provenance.EnvironmentStatus,
                MatchedBaselineGameVersion = preset.Provenance.MatchedBaselineGameVersion,
                ComparisonBaselineGameVersion = preset.Provenance.ComparisonBaselineGameVersion,
                EnvironmentReason = preset.Provenance.EnvironmentReason,
                VanillaUnlockKind = preset.Provenance.UnlockKind,
                CapturedAtUtc = preset.Provenance.CapturedAtUtc
            },
            CreatedAtUtc = preset.CreatedAtUtc
        };
    }

    internal static IReadOnlyList<SearchPresetVisualIconRef> NormalizeVisualIcons(
        IReadOnlyList<SearchPresetVisualIconRef>? visualIcons) =>
        (visualIcons ?? Array.Empty<SearchPresetVisualIconRef>())
            .Where(icon => icon is not null && icon.IsValid)
            .Take(3)
            .ToArray();

    internal static string NormalizeTitle(string? value) => (value ?? string.Empty).Trim();

    internal static string NormalizeDescription(string? value) => (value ?? string.Empty).Trim();

    private static string NormalizeEnvironmentStatus(string? value)
    {
        if (Enum.TryParse(value, ignoreCase: true, out SemanticEnvironmentStatus status))
            return status.ToString();
        // Legacy Vanilla/Modded heuristics are deliberately not upgraded into the new
        // semantic-fingerprint authority. Old provenance remains Unknown.
        return SemanticEnvironmentStatus.Unknown.ToString();
    }

    private static string NormalizeSha256(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(ch => !Uri.IsHexDigit(ch))) return string.Empty;
        return normalized;
    }

    private static string NormalizeUnlockKind(string? value) => value switch
    {
        SearchPresetUnlockKinds.Full => SearchPresetUnlockKinds.Full,
        SearchPresetUnlockKinds.Partial => SearchPresetUnlockKinds.Partial,
        _ => SearchPresetUnlockKinds.Unknown
    };

    internal static string Compact(string value)
    {
        string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 180 ? normalized : normalized[..180];
    }

    internal static string LoadIssue(string fileName, string reason) =>
        Compact(Path.GetFileName(fileName)) + ": " + Compact(reason);

    // Parser and I/O exception messages may quote file data or full paths. UI
    // diagnostics expose only local file names and known validation reason codes.
    internal static string LoadIssueReason(Exception exception) => exception switch
    {
        JsonException => "InvalidOrUnknownJson",
        InvalidDataException => Compact(exception.Message),
        UnauthorizedAccessException => "ReadAccessDenied",
        _ => exception.GetType().Name
    };

    internal sealed class PersistedSearchPreset
    {
        public string EnvironmentFingerprint { get; set; } = "";
        public int SchemaVersion { get; set; } = SearchPresetStore.SchemaVersion;
        public string Id { get; set; } = string.Empty;

        // v2
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public JsonElement QuerySnapshot { get; set; }
        // v3: full typed solo/party intent; never a single-player projection.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public JsonElement WorkbenchSnapshot { get; set; }
        public int ConditionCount { get; set; }
        public List<PersistedVisualIconRef> VisualIcons { get; set; } = new();
        public PersistedProbabilitySnapshot? SavedProbability { get; set; }
        public PersistedProvenance? Provenance { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }

        // v1 narrow compatibility fields
        public string Name { get; set; } = string.Empty;
        // Read legacy v1 Draft when present, but never emit an Undefined JsonElement
        // from new v2 files. JsonElement.WriteTo throws InvalidOperationException for
        // ValueKind=Undefined, which broke Temporary Preset persistence after a
        // successful Search Run.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public JsonElement Draft { get; set; }
        public string VisualRelicKey { get; set; } = string.Empty;
        public string DescriptionKey { get; set; } = string.Empty;

        // shared
        public string CharacterKey { get; set; } = string.Empty;
        public int Ascension { get; set; } = SeedPredictionInputLimits.MaximumAscension;
    }

    internal sealed class PersistedVisualIconRef
    {
        public string Kind { get; set; } = string.Empty;
        public string StableContentIdentity { get; set; } = string.Empty;
        public string ResourceHint { get; set; } = string.Empty;
    }

    internal sealed class PersistedProbabilitySnapshot
    {
        public string Status { get; set; } = string.Empty;
        public double? TotalProbability { get; set; }
        public DateTimeOffset CapturedAtUtc { get; set; }
    }

    internal sealed class PersistedProvenance
    {
        public string GameVersion { get; set; } = string.Empty;
        public int FingerprintSchemaVersion { get; set; }
        public string SemanticFingerprint { get; set; } = string.Empty;
        public string EnvironmentStatus { get; set; } = SemanticEnvironmentStatus.Unknown.ToString();
        public string MatchedBaselineGameVersion { get; set; } = string.Empty;
        public string ComparisonBaselineGameVersion { get; set; } = string.Empty;
        public string EnvironmentReason { get; set; } = string.Empty;
        public string VanillaUnlockKind { get; set; } = SearchPresetUnlockKinds.Unknown;
        public DateTimeOffset CapturedAtUtc { get; set; }

        // Narrow compatibility with the pre-Runtime-Authority B-Core draft shape.
        // These fields are readable but are never promoted to semantic authority.
        public string EnvironmentKind { get; set; } = string.Empty;
        public string UnlockKind { get; set; } = string.Empty;
    }
}

/// <summary>
/// Cross-restart recent successful Search Run snapshots. This is deliberately a tiny
/// five-entry FIFO history, not a general Search run log.
/// </summary>
internal sealed class TemporarySearchPresetStore
{
    private const int Capacity = 5;
    private const string FileName = "temporary_search_presets.json";
    private readonly string _path;
    private readonly JsonSerializerOptions _json;
    private readonly List<SearchPresetDefinition> _entries = new();
    private readonly List<string> _loadIssues = new();
    private bool _preserveUnreadableFile;

    public TemporarySearchPresetStore(string stateDirectory)
    {
        _path = Path.Combine(stateDirectory, FileName);
        _json = SearchPresetStore.CreateJsonOptions();
        LoadFailSoft();
    }

    public IReadOnlyList<SearchPresetDefinition> GetPresets() =>
        _entries.OrderByDescending(entry => entry.CreatedAtUtc).ToArray();
    public IReadOnlyList<string> LoadIssues => _loadIssues.ToArray();

    public SearchPresetDefinition Record(SearchPresetCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        string id = "temporary:" + Guid.NewGuid().ToString("N");
        SearchPresetDefinition entry = SearchPresetStore.BuildFromCapture(
            id,
            SearchPresetSource.Temporary,
            string.Empty,
            string.Empty,
            Array.Empty<SearchPresetVisualIconRef>(),
            capture,
            _json);
        _entries.Insert(0, entry);
        if (_entries.Count > Capacity)
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        WriteAll();
        return entry;
    }

    private void LoadFailSoft()
    {
        _entries.Clear();
        _loadIssues.Clear();
        if (!File.Exists(_path)) return;
        try
        {
            string jsonText = File.ReadAllText(_path);
            List<SearchPresetStore.PersistedSearchPreset>? persisted =
                JsonSerializer.Deserialize<List<SearchPresetStore.PersistedSearchPreset>>(jsonText, _json);
            if (persisted is null) throw new InvalidDataException("TemporaryPresetDocumentNull");
            // Validate every persisted entry before limiting the visible history.
            // An unreadable tail must not be overwritten by the next five-entry save.
            foreach (SearchPresetStore.PersistedSearchPreset item in persisted)
            {
                try
                {
                    SearchPresetDefinition entry = SearchPresetStore.Normalize(item, SearchPresetSource.Temporary, _json);
                    if (!entry.QueryResolved)
                    {
                        _preserveUnreadableFile = true;
                        _loadIssues.Add(SearchPresetStore.LoadIssue(FileName, "PresetQueryShapeUnresolved"));
                    }
                    if (_entries.Count < Capacity) _entries.Add(entry);
                }
                catch (Exception ex)
                {
                    _preserveUnreadableFile = true;
                    _loadIssues.Add(SearchPresetStore.LoadIssue(FileName, SearchPresetStore.LoadIssueReason(ex)));
                    RuntimeLog.Warn(
                        $"temporarySearchPresetEntrySkipped=true;failSoft=true;issue={ex.GetType().Name}:{SearchPresetStore.Compact(ex.Message)}");
                }
            }
            _entries.Sort((left, right) => right.CreatedAtUtc.CompareTo(left.CreatedAtUtc));
            if (_entries.Count > Capacity)
                _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
        catch (Exception ex)
        {
            _preserveUnreadableFile = true;
            _loadIssues.Add(SearchPresetStore.LoadIssue(FileName, SearchPresetStore.LoadIssueReason(ex)));
            RuntimeLog.Warn(
                $"temporarySearchPresetStoreLoadFailed=true;failSoft=true;issue={ex.GetType().Name}:{SearchPresetStore.Compact(ex.Message)}");
        }
    }

    private void WriteAll()
    {
        if (_preserveUnreadableFile)
        {
            RuntimeLog.Warn("temporarySearchPresetStoreWriteSkipped=true;reason=UnreadableHistoryPreserved;inMemoryContinues=true");
            return;
        }
        try
        {
            var persisted = _entries
                .OrderByDescending(entry => entry.CreatedAtUtc)
                .Take(Capacity)
                .Select(entry => SearchPresetStore.ToPersisted(entry, _json))
                .ToList();
            SearchPersistenceFile.WriteAtomic(_path, JsonSerializer.SerializeToUtf8Bytes(persisted, _json));
        }
        catch (Exception ex)
        {
            // Search run success must not be rolled back because lightweight recent
            // preset history could not be persisted.
            RuntimeLog.Warn(
                $"temporarySearchPresetStoreWriteFailed=true;failSoft=true;issue={ex.GetType().Name}:{SearchPresetStore.Compact(ex.Message)}");
        }
    }
}
