using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Persistence;

internal enum SeedLibrarySource { Developer, User }
internal enum SeedQueryAssociationKind { UserAssigned, VerifiedFromSearch, LegacyUnspecified }
internal sealed record SeedQueryAssociation(string QueryKey, SeedQueryAssociationKind Kind,
    string EnvironmentFingerprint = "", string SearchRecordId = "");

internal sealed record SeedLibraryOpeningSelection(int? ChoiceSlot, string Route, bool Explicit);

internal sealed record SeedLibraryPlayer(int Slot, ModelKey Character, SerializableUnlockState Unlocks,
    string UnlockSource, AncientOptionConditionProfile AncientPremises, SearchQuery OpeningPremise,
    SeedLibraryOpeningSelection? Selection);

// Reopening captures current runtime authority from this intent. No predicted
// document, pool, worker/session, cursor or live RunState belongs in a library item.
internal sealed record SeedLibraryContext(string GameVersion, RuntimeProfileId Profile, WorldGameMode Mode,
    int Ascension, IReadOnlyList<SeedLibraryPlayer> Players, int SelectedSlot = 0);

internal sealed record SeedLibraryEntry(string Id, SeedLibrarySource Source, string Seed, string Note,
    SeedLibraryContext? Context, DateTimeOffset SavedAtUtc)
{
    public string Title { get; init; } = "";
    public DeveloperSeedDetails? DeveloperDetails { get; init; }
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Seed : Title;
    public string TitleFor(string language) => DeveloperDetails?.Title.Resolve(language) is { Length: > 0 } title ? title : DisplayTitle;
    public string NoteFor(string language) => DeveloperDetails?.Description.Resolve(language) is { Length: > 0 } note ? note : Note;
    public string FavoriteNote(string language)
    {
        string instructions = DeveloperDetails?.Instructions.Resolve(language) ?? "";
        return instructions.Length == 0 ? NoteFor(language) : string.Join("\n\n", new[] { NoteFor(language),
            (language == "zh" ? "游玩说明\n" : "How to play\n") + instructions }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
    public IReadOnlyList<string> QueryKeys { get; init; } = [];
    public IReadOnlyList<SeedQueryAssociation> QueryAssociations { get; init; } = [];
    public string RawContextJson { get; init; } = string.Empty;
    public string Issue { get; init; } = string.Empty;
    public bool CanOpen => Context is not null && string.IsNullOrEmpty(Issue);
}

/// <summary>Player notes and developer recommendations share one context contract.</summary>
internal sealed class SeedLibraryStore
{
    internal const int SchemaVersion = 1;
    private readonly string _directory;
    private readonly List<SeedLibraryEntry> _user = [];
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
    private readonly List<string> _issues = [];
    private readonly IReadOnlyList<SeedLibraryEntry> _developer;
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    public SeedLibraryStore(string stateDirectory)
        : this(stateDirectory, new DeveloperSeedLibraryProvider()) { }

    private SeedLibraryStore(string stateDirectory, DeveloperSeedLibraryProvider developer)
        : this(stateDirectory, developer.GetEntries()) => _issues.AddRange(developer.LoadIssues);

    internal SeedLibraryStore(string stateDirectory, IReadOnlyList<SeedLibraryEntry> developer)
    {
        _directory = Path.Combine(stateDirectory, "seed_library");
        _developer = developer;
        Load();
    }

    public IReadOnlyList<string> LoadIssues => _issues.ToArray();
    public IReadOnlyList<SeedLibraryEntry> GetAll() => _developer.Concat(_user)
        .OrderBy(e => e.Source).ThenByDescending(e => e.SavedAtUtc).ThenBy(e => e.Seed, StringComparer.Ordinal).ToArray();

    public bool TryFind(string seed, SeedLibraryContext context, out SeedLibraryEntry entry)
    {
        string key = Identity(seed, context);
        entry = _user.FirstOrDefault(e => e.Context is not null && Identity(e.Seed, e.Context) == key)!;
        return entry is not null;
    }

    public SeedLibraryEntry Save(string seed, SeedLibraryContext context, string note, string? queryKey = null, SeedQueryAssociation? association = null, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        SeedLibraryContext savedContext = CloneContext(context);
        string canonical = CanonicalSeed(seed);
        string key = Identity(canonical, savedContext);
        int index = _user.FindIndex(e => e.Context is not null && Identity(e.Seed, e.Context) == key);
        string id = index >= 0 ? _user[index].Id : "user:" + Guid.NewGuid().ToString("N");
        var saved = new SeedLibraryEntry(id, SeedLibrarySource.User, canonical, (note ?? string.Empty).Trim(),
            savedContext, DateTimeOffset.UtcNow) { RawContextJson = JsonSerializer.Serialize(savedContext, Json),
                Title = title?.Trim() ?? (index >= 0 ? _user[index].Title : ""),
                QueryKeys = (index >= 0 ? _user[index].QueryKeys : []).Concat(string.IsNullOrEmpty(queryKey) ? [] : new[] { queryKey }).Distinct().ToArray(),
                QueryAssociations = (index >= 0 ? _user[index].QueryAssociations : [])
                    .Concat(association is not null ? new[] { association } : !string.IsNullOrEmpty(queryKey)
                        ? new[] { new SeedQueryAssociation(queryKey, SeedQueryAssociationKind.UserAssigned) } : [])
                    .Distinct().ToArray() };
        Write(saved);
        if (index >= 0) _user[index] = saved;
        else _user.Add(saved);
        return saved;
    }

    public void SetQueryLinks(string id, IEnumerable<string> keys)
    {
        int index = _user.FindIndex(e => e.Id == id);
        if (index < 0) throw new KeyNotFoundException("SeedLibraryUserEntryMissing");
        var linked = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToArray();
        var updated = _user[index] with { QueryKeys = linked,
            QueryAssociations = _user[index].QueryAssociations
                .Where(a => a.Kind != SeedQueryAssociationKind.UserAssigned || linked.Contains(a.QueryKey))
                .Concat(linked.Where(k => !_user[index].QueryAssociations.Any(a => a.QueryKey == k))
                    .Select(k => new SeedQueryAssociation(k, SeedQueryAssociationKind.UserAssigned))).ToArray() };
        Write(updated); _user[index] = updated;
    }

    public SeedLibraryEntry UpdateNote(string id, string note)
    {
        var entry = _user.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException("SeedLibraryUserEntryMissing");
        return UpdateInformation(id, entry.Title, note);
    }

    public SeedLibraryEntry UpdateInformation(string id, string title, string note)
    {
        int index = _user.FindIndex(e => e.Id == id);
        if (index < 0) throw new KeyNotFoundException("SeedLibraryUserEntryMissing");
        var updated = _user[index] with { Title = (title ?? "").Trim(), Note = (note ?? string.Empty).Trim() };
        Write(updated);
        _user[index] = updated;
        return updated;
    }

    public bool DeleteUser(string id)
    {
        int index = _user.FindIndex(e => e.Id == id);
        if (index < 0) return false;
        string path = PathFor(id);
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex)
        {
            RuntimeLog.Warn("seedLibraryDeleteFailed=true;issue=" + SearchPresetStore.Compact(ex.Message));
            return false;
        }
        _user.RemoveAt(index);
        _paths.Remove(id);
        return true;
    }

    private void Load()
    {
        if (!Directory.Exists(_directory)) return;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(_directory, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex)
        {
            string issue = "seed_library: " + SearchPresetStore.Compact(ex.Message);
            _issues.Add(issue);
            RuntimeLog.Warn("seedLibraryDirectoryReadFailed=true;preservedOriginal=true;issue=" + issue);
            return;
        }
        foreach (string path in paths)
        {
            try
            {
                SeedLibraryEntry entry = Read(File.ReadAllText(path), SeedLibrarySource.User);
                if (_paths.ContainsKey(entry.Id)) throw new InvalidDataException("SeedLibraryDuplicateId");
                if (entry.Context is not null && !identities.Add(Identity(entry.Seed, entry.Context)))
                    throw new InvalidDataException("SeedLibraryDuplicateContext");
                _user.Add(entry);
                _paths.Add(entry.Id, path);
            }
            catch (Exception ex)
            {
                string issue = Path.GetFileName(path) + ": " + SearchPresetStore.Compact(ex.Message);
                _issues.Add(issue);
                RuntimeLog.Warn("seedLibraryEntrySkipped=true;preservedOriginal=true;issue=" + issue);
            }
        }
    }

    private string PathFor(string id)
    {
        // Loaded file names are retained; external IDs never become path segments.
        if (_paths.TryGetValue(id, out string? loaded)) return loaded;
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        return Path.Combine(_directory, name + ".json");
    }

    private void Write(SeedLibraryEntry entry)
    {
        string raw = !string.IsNullOrWhiteSpace(entry.RawContextJson) ? entry.RawContextJson : JsonSerializer.Serialize(entry.Context, Json);
        using JsonDocument context = JsonDocument.Parse(raw);
        var document = new PersistedEntry(SchemaVersion, entry.Id, entry.Seed, entry.Note,
            context.RootElement.Clone(), entry.SavedAtUtc) { Title = entry.Title, QueryKeys = entry.QueryKeys, QueryAssociations = entry.QueryAssociations };
        string path = PathFor(entry.Id);
        SearchPersistenceFile.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(document, Json));
        _paths[entry.Id] = path;
    }

    internal static SeedLibraryEntry Read(string text, SeedLibrarySource source, string? developerId = null)
    {
        PersistedEntry document = JsonSerializer.Deserialize<PersistedEntry>(text, Json)
            ?? throw new InvalidDataException("SeedLibraryDocumentMissing");
        if (document.SchemaVersion != SchemaVersion)
            throw new InvalidDataException("SeedLibrarySchemaUnsupported:" + document.SchemaVersion);
        string id = developerId ?? document.Id;
        string prefix = source == SeedLibrarySource.User ? "user:" : "developer:";
        if (!id.StartsWith(prefix, StringComparison.Ordinal) || id.Length <= prefix.Length)
            throw new InvalidDataException("SeedLibraryIdInvalid");
        string seed = CanonicalSeed(document.Seed);
        if (source == SeedLibrarySource.Developer) document.DeveloperDetails?.Validate();
        if (document.Context.ValueKind is not JsonValueKind.Object)
            throw new InvalidDataException("SeedLibraryContextMissing");
        string raw = document.Context.GetRawText();
        SeedLibraryContext? context = null;
        string issue = string.Empty;
        try
        {
            context = JsonSerializer.Deserialize<SeedLibraryContext>(raw, Json)
                ?? throw new InvalidDataException("SeedLibraryContextMissing");
            ValidateContext(context);
        }
        catch (Exception ex)
        {
            context = null;
            issue = "SeedLibraryContextUnsupported:" + SearchPresetStore.Compact(ex.Message);
        }
        var keys = (document.QueryKeys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToArray();
        var associations = (document.QueryAssociations ?? []).Where(a => a is not null && !string.IsNullOrWhiteSpace(a.QueryKey))
            .Select(a => !Enum.IsDefined(a.Kind) || a.Kind == SeedQueryAssociationKind.VerifiedFromSearch &&
                (string.IsNullOrWhiteSpace(a.EnvironmentFingerprint) || string.IsNullOrWhiteSpace(a.SearchRecordId))
                    ? a with { Kind = SeedQueryAssociationKind.LegacyUnspecified } : a).Distinct().ToList();
        associations.AddRange(keys.Where(k => !associations.Any(a => a.QueryKey == k))
            .Select(k => new SeedQueryAssociation(k, SeedQueryAssociationKind.LegacyUnspecified)));
        return new(id, source, seed, document.Note, context, document.SavedAtUtc)
        { Title = (document.Title ?? "").Trim(), RawContextJson = raw, Issue = issue, QueryKeys = keys, QueryAssociations = associations,
            DeveloperDetails = source == SeedLibrarySource.Developer ? document.DeveloperDetails : null };
    }

    internal static SeedLibraryContext CloneContext(SeedLibraryContext context)
    {
        ValidateContext(context);
        var cloned = JsonSerializer.Deserialize<SeedLibraryContext>(JsonSerializer.Serialize(context, Json), Json)
            ?? throw new InvalidDataException("SeedLibraryContextMissing");
        ValidateContext(cloned);
        return cloned;
    }

    internal static void ValidateContext(SeedLibraryContext context)
    {
        if (string.IsNullOrWhiteSpace(context.GameVersion) || !Enum.IsDefined(context.Profile) ||
            context.Profile == RuntimeProfileId.Unsupported ||
            context.Mode is not (WorldGameMode.Singleplayer or WorldGameMode.Multiplayer) ||
            context.Players is null || context.Players.Count is < 1 or > SeedPredictionInputLimits.MaximumPlayers ||
            (context.Mode == WorldGameMode.Singleplayer) != (context.Players.Count == 1) ||
            context.SelectedSlot < 0 || context.SelectedSlot >= context.Players.Count ||
            context.Ascension is < SeedPredictionInputLimits.MinimumAscension or > SeedPredictionInputLimits.MaximumAscension)
            throw new InvalidDataException("SeedLibraryContextInvalid");
        foreach (var (player, index) in context.Players.Select((p, i) => (p, i)))
        {
            if (player is null || player.Slot != index || !player.Character.IsValid || player.Unlocks is null ||
                string.IsNullOrWhiteSpace(player.UnlockSource) || player.AncientPremises is null || player.OpeningPremise is null ||
                player.Selection is { ChoiceSlot: <= 0 } || player.Selection is { Route: null })
                throw new InvalidDataException("SeedLibraryPlayerInvalid");
            SearchPresetCompatibilityResolver.ValidateWorkbenchShape(new WorkbenchSearchDraft(player.Character,
                context.Ascension, player.OpeningPremise, player.AncientPremises));
            if (context.Mode == WorldGameMode.Singleplayer &&
                JsonSerializer.Serialize(player.OpeningPremise, Json) != JsonSerializer.Serialize(SearchQuery.Empty, Json))
                throw new InvalidDataException("SeedLibrarySoloOpeningPremiseUnsupported");
            var nonOpening = player.OpeningPremise with { OpeningRoute = null, OpeningRouteRelicRequirement = null,
                StructuredOpeningEffects = [] };
            if (JsonSerializer.Serialize(nonOpening, Json) != JsonSerializer.Serialize(SearchQuery.Empty, Json))
                throw new InvalidDataException("SeedLibraryOnlyOpeningPremisesAllowed");
        }
    }

    internal static void ValidateRuntimeReferences(SeedLibraryContext context, RuntimeAuthoritySnapshot authority)
    {
        ValidateContext(context);
        var missing = SearchPresetCompatibilityResolver.FindUnresolvedIntentReferences(context, authority, "SeedLibrary");
        if (missing.Count > 0)
            throw new InvalidDataException("SeedLibrary.ReferenceUnavailable:" + string.Join("; ",
                missing.Take(6).Select(item => item.Path + "=" + item.StableIdentity + " (" + item.Reason + ")")));
    }

    private static string CanonicalSeed(string seed)
    {
        if (!ProfileSeedRules.TryCanonicalize(seed, Beta110Profile.Instance.SeedLength, out string canonical, out string issue))
            throw new InvalidDataException("SeedLibrarySeedInvalid:" + issue);
        return canonical;
    }

    private static string Identity(string seed, SeedLibraryContext context) =>
        CanonicalSeed(seed) + "\n" + JsonSerializer.Serialize(context with
        {
            SelectedSlot = 0,
            Players = context.Players.Select(player => player with
            {
                Selection = player.Selection is { } selection
                    ? !selection.Explicit && player.OpeningPremise.OpeningRoute is null ? null
                        : !string.IsNullOrEmpty(selection.Route) ? selection with { ChoiceSlot = null } : selection
                    : null
            }).ToArray()
        }, Json);

    internal static JsonSerializerOptions CreateJsonOptions() => new(SearchPresetStore.CreateJsonOptions())
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    internal sealed record PersistedEntry(int SchemaVersion, string Id, string Seed, string Note,
        JsonElement Context, DateTimeOffset SavedAtUtc)
    {
        public string Title { get; init; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DeveloperSeedDetails? DeveloperDetails { get; init; }
        public IReadOnlyList<string> QueryKeys { get; init; } = [];
        public IReadOnlyList<SeedQueryAssociation> QueryAssociations { get; init; } = [];
    }
}

internal sealed class DeveloperSeedLibraryProvider
{
    internal const string ResourcePrefix = "RolltheSpire2.DeveloperSeeds.";
    private readonly IReadOnlyList<SeedLibraryEntry> _entries;
    public IReadOnlyList<string> LoadIssues { get; }

    public DeveloperSeedLibraryProvider() : this(typeof(DeveloperSeedLibraryProvider).Assembly) { }
    internal DeveloperSeedLibraryProvider(Assembly assembly) : this(assembly.GetManifestResourceNames().Where(n =>
        n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        .OrderBy(n => n, StringComparer.Ordinal).Select(resource => (resource, (Func<string>)(() =>
        {
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidDataException("SeedLibraryDeveloperResourceMissing");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        })))) { }

    internal DeveloperSeedLibraryProvider(IEnumerable<(string Resource, Func<string> Read)> resources)
    {
        var entries = new List<SeedLibraryEntry>();
        var issues = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (resource, read) in resources)
        {
            try
            {
                if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SeedLibraryDeveloperResourceNameInvalid");
                string id = "developer:" + resource[ResourcePrefix.Length..^5];
                var entry = SeedLibraryStore.Read(read(), SeedLibrarySource.Developer, id);
                if (!ids.Add(id)) throw new InvalidDataException("SeedLibraryDeveloperDuplicateId");
                entries.Add(entry);
            }
            catch (Exception ex)
            {
                issues.Add(resource + ": " + SearchPresetStore.Compact(ex.Message));
                RuntimeLog.Warn("developerSeedLibraryEntrySkipped=true;resource=" + resource +
                    ";issue=" + SearchPresetStore.Compact(ex.Message));
            }
        }
        _entries = entries;
        LoadIssues = issues;
    }
    public IReadOnlyList<SeedLibraryEntry> GetEntries() => _entries;
}
