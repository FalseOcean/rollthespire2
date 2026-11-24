using RolltheSpire2.Ui.Workspace;
using System.Text.Json;
using System.Text.Json.Serialization;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Persistence;

/// <summary>
/// Fail-soft current-format user store. Legacy reads never write. Explicit edits
/// create current overlays beside preserved source assets; deletion markers prevent
/// those sources from resurfacing after a migrated preset is deleted.
/// </summary>
internal sealed class SearchPresetStore
{
    internal const int SchemaVersion = 4;
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

    internal long Revision { get; private set; }

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

        if (existingIndex >= 0)
            saved = saved with { PreservedSourceFileName = _presets[existingIndex].PreservedSourceFileName };
        saved = WriteOne(saved);
        if (existingIndex >= 0)
            _presets[existingIndex] = saved;
        else
            _presets.Add(saved);
        Revision++;
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
            CreatedAtUtc = DateTimeOffset.UtcNow,
            PreservedSourceFileName = existingIndex >= 0 ? _presets[existingIndex].PreservedSourceFileName : string.Empty
        };
        if (existingIndex >= 0)
            saved = saved with { PreservedSourceFileName = _presets[existingIndex].PreservedSourceFileName };
        saved = WriteOne(saved);
        if (existingIndex >= 0)
            _presets[existingIndex] = saved;
        else
            _presets.Add(saved);
        Revision++;
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
        updated = WriteOne(updated);
        _presets[index] = updated;
        Revision++;
        return updated;
    }

    public SearchPresetDefinition UpdateEnvironment(string id, string fingerprint)
    {
        int index = _presets.FindIndex(p => p.Id == id);
        if (index < 0) throw new KeyNotFoundException("SearchPresetMissing");
        if (string.IsNullOrWhiteSpace(fingerprint)) throw new InvalidOperationException("CurrentEnvironmentUnavailable");
        var updated = _presets[index] with { EnvironmentFingerprint = fingerprint };
        updated = WriteOne(updated);
        _presets[index] = updated;
        Revision++;
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
            if (_presets[index].PreservedSourceFileName.Length > 0)
            {
                EnsureOverlayTarget(_presets[index], path);
                var tombstone = ToPersisted(_presets[index], _json);
                tombstone.Deleted = true;
                SearchPersistenceFile.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(tombstone, _json));
            }
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(
                $"searchPresetDeleteFileFailed=true;failSoft=true;id={id};issue={ex.GetType().Name}:{Compact(ex.Message)}");
            return false;
        }
        _presets.RemoveAt(index);
        Revision++;
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
        var files = new List<(string Path, string Json, PersistedSearchPreset Asset)>();
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
                files.Add((path, jsonText, persisted));
            }
            catch (Exception ex)
            {
                _loadIssues.Add(LoadIssue(fileName, LoadIssueReason(ex)));
                RuntimeLog.Warn($"searchPresetEntrySkipped=true;failSoft=true;file={fileName};issue={ex.GetType().Name}:{Compact(ex.Message)}");
            }
        }
        var overlays = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files.OrderByDescending(f => f.Asset.SchemaVersion))
        {
            string path = file.Path;
            string fileName = Path.GetFileName(path);
            string jsonText = file.Json;
            var persisted = file.Asset;
            try
            {
                if (persisted.SchemaVersion < 4 && overlays.TryGetValue(persisted.Id, out string? preserved) &&
                    string.Equals(fileName, preserved, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(persisted.PreservedSourceFileName) &&
                    (persisted.PreservedSourceFileName != Path.GetFileName(persisted.PreservedSourceFileName) ||
                     !persisted.PreservedSourceFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("PresetPreservedSourceInvalid");
                if (persisted.Deleted)
                {
                    if (persisted.SchemaVersion != 4 || !persisted.Id.StartsWith("user:", StringComparison.Ordinal) ||
                        persisted.Id.Length <= 5 || string.IsNullOrEmpty(persisted.PreservedSourceFileName))
                        throw new InvalidDataException("PresetDeletionMarkerInvalid");
                    if (!ids.Add(persisted.Id)) throw new InvalidDataException("DuplicateId");
                    overlays[persisted.Id] = persisted.PreservedSourceFileName;
                    continue;
                }
                SearchPresetDefinition preset = Normalize(persisted, SearchPresetSource.User, _json, jsonText);
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
                if (persisted.SchemaVersion < 4)
                {
                    preset = preset with { PreservedSourceFileName = fileName };
                    _paths[preset.Id] = Path.Combine(_directory, Path.GetFileNameWithoutExtension(fileName) + ".format4.json");
                }
                else
                {
                    _paths[preset.Id] = path;
                    if (preset.PreservedSourceFileName.Length > 0) overlays[preset.Id] = preset.PreservedSourceFileName;
                }
                _presets.Add(preset);
            }
            catch (Exception ex)
            {
                _loadIssues.Add(LoadIssue(fileName, LoadIssueReason(ex)));
                RuntimeLog.Warn(
                    $"searchPresetEntrySkipped=true;failSoft=true;file={fileName};issue={ex.GetType().Name}:{Compact(ex.Message)}");
            }
        }
    }

    private SearchPresetDefinition WriteOne(SearchPresetDefinition preset)
    {
        string path = PathForId(preset.Id);
        EnsureOverlayTarget(preset, path);
        PersistedSearchPreset persisted = ToPersisted(preset, _json);
        SearchPersistenceFile.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(persisted, _json));
        _paths[preset.Id] = path;
        return preset;
    }

    private void EnsureOverlayTarget(SearchPresetDefinition preset, string path)
    {
        if (preset.PreservedSourceFileName.Length > 0 && File.Exists(path))
        {
            var existing = JsonSerializer.Deserialize<PersistedSearchPreset>(File.ReadAllText(path), _json);
            if (existing is null || existing.SchemaVersion != SchemaVersion || existing.Id != preset.Id ||
                existing.PreservedSourceFileName != preset.PreservedSourceFileName)
                throw new InvalidDataException("PresetOverlayPathConflict");
        }
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
        json.Converters.Add(new SearchWorkspace.ModelKeyJsonConverter());
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
        var workbench = capture.Workbench ?? throw new InvalidDataException("PresetCurrentIntentRequired");
        if (workbench.Character != capture.CharacterKey || workbench.Ascension != capture.Ascension)
            throw new InvalidDataException("PresetCaptureContextMismatch");
        SearchPresetCompatibilityResolver.ValidateWorkbenchShape(workbench);
        // Detach lists and retain authored intent only; runtime authority is rebound on use.
        workbench = JsonSerializer.Deserialize<WorkbenchSearchDraft>(JsonSerializer.Serialize(workbench.ToPresetIntent(), json), CreateWorkbenchJsonOptions(json))
            ?? throw new InvalidDataException("PresetWorkbenchShapeUnresolved");
        SearchPresetCompatibilityResolver.ValidateWorkbenchShape(workbench);
        return new(id, source, title, description, capture.CharacterKey, capture.Ascension,
            workbench, Math.Max(0, capture.ConditionCount), NormalizeVisualIcons(visualIcons),
            capture.SavedProbability, capture.Provenance, capture.CapturedAtUtc);
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
        JsonSerializerOptions json, string? originalJson = null)
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

        WorkbenchSearchDraft? workbench = null;
        SearchPresetUnresolvedAsset? unresolvedAsset = persisted.UnresolvedAsset;
        if (unresolvedAsset is null)
        {
            try { workbench = SearchPresetMigration.ReadIntent(persisted, characterKey, json); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or SearchPresetMigrationException)
            {
                var references = ex is SearchPresetMigrationException migration ? migration.References : Array.Empty<SearchPresetUnresolvedReference>();
                unresolvedAsset = new(originalJson ?? JsonSerializer.Serialize(persisted, json), LoadIssueReason(ex), references);
                RuntimeLog.Warn($"searchPresetQueryUnresolved=true;id={id};source={source};preservedRaw=true;issue={ex.GetType().Name}:{Compact(ex.Message)}");
            }
        }
        else if (persisted.SchemaVersion < 4 || persisted.WorkbenchSnapshot.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ||
            persisted.QuerySnapshot.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ||
            persisted.Draft.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            throw new InvalidDataException("PresetMultipleQueryPayloads");

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

        if (unresolvedAsset is not null && (string.IsNullOrWhiteSpace(unresolvedAsset.OriginalJson) ||
            string.IsNullOrWhiteSpace(unresolvedAsset.Issue) || unresolvedAsset.References is null ||
            unresolvedAsset.References.Any(reference => reference is null || reference.Path is null || reference.StableIdentity is null || reference.Reason is null)))
            throw new InvalidDataException("PresetUnresolvedAssetInvalid");
        int conditionCount = Math.Max(0, persisted.ConditionCount);
        return new SearchPresetDefinition(
            id,
            source,
            title,
            NormalizeDescription(persisted.Description),
            characterKey,
            persisted.Ascension,
            workbench,
            conditionCount,
            NormalizeVisualIcons(visualIcons),
            probability,
            provenance,
            createdAt)
        {
            UnresolvedAsset = unresolvedAsset,
            PreservedSourceFileName = persisted.PreservedSourceFileName ?? string.Empty,
            BuiltInTitle = source == SearchPresetSource.BuiltIn
                ? new() { Zh = title, En = NormalizeTitle(persisted.EnglishTitle) } : null,
            BuiltInDescription = source == SearchPresetSource.BuiltIn
                ? new() { Zh = NormalizeDescription(persisted.Description), En = NormalizeDescription(persisted.EnglishDescription) } : null,
            EnvironmentFingerprint = persisted.EnvironmentFingerprint ?? ""
        };
    }

    internal static PersistedSearchPreset ToPersisted(SearchPresetDefinition preset, JsonSerializerOptions json)
    {
        JsonElement workbenchSnapshot = preset.Workbench is not null
            ? JsonSerializer.SerializeToElement(preset.Workbench.ToPresetIntent(), json) : default;
        if (preset.Workbench is null && preset.UnresolvedAsset is null || preset.Workbench is not null && preset.UnresolvedAsset is not null)
            throw new InvalidDataException("PresetCurrentIntentRequired");

        return new PersistedSearchPreset
        {
            SchemaVersion = SchemaVersion,
            Id = preset.Id,
            EnvironmentFingerprint = preset.EnvironmentFingerprint,
            Title = preset.Title,
            Description = preset.Description,
            CharacterKey = preset.CharacterKey.Serialized,
            Ascension = preset.Ascension,
            WorkbenchSnapshot = workbenchSnapshot,
            UnresolvedAsset = preset.UnresolvedAsset,
            PreservedSourceFileName = preset.PreservedSourceFileName.Length == 0 ? null : preset.PreservedSourceFileName,
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
        InvalidDataException or SearchPresetMigrationException => Compact(exception.Message),
        InvalidOperationException or ArgumentException => "PresetIntentConversionFailed",
        UnauthorizedAccessException => "ReadAccessDenied",
        _ => exception.GetType().Name
    };

    internal sealed class PersistedSearchPreset
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SearchPresetUnresolvedAsset? UnresolvedAsset { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Deleted { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PreservedSourceFileName { get; set; }
        public string EnvironmentFingerprint { get; set; } = "";
        public int SchemaVersion { get; set; } = SearchPresetStore.SchemaVersion;
        public string Id { get; set; } = string.Empty;

        // v2
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        // Optional embedded-asset display metadata. User exports keep their literal text.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EnglishTitle { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EnglishDescription { get; set; }
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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }
        // Read legacy v1 Draft when present, but never emit an Undefined JsonElement
        // from new v2 files. JsonElement.WriteTo throws InvalidOperationException for
        // ValueKind=Undefined, which broke Temporary Preset persistence after a
        // successful Search Run.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public JsonElement Draft { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? VisualRelicKey { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DescriptionKey { get; set; }

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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EnvironmentKind { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UnlockKind { get; set; }
    }
}

/// <summary>
/// Cross-restart recent successful Search Run snapshots. This is deliberately a tiny
/// five-entry FIFO history, not a general Search run log.
/// </summary>
