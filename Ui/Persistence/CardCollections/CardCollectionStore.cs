using System.Text.Json;
using System.Text.Json.Serialization;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Persistence;

/// <summary>
/// Independent user assets. Unreadable files remain untouched; explicit writes
/// commit before changing memory. No catalog or SearchWorkspace initialization.
/// </summary>
internal sealed class CardCollectionStore
{
    internal const int SchemaVersion = 1;
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = CreateJsonOptions();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _loadIssues = [];
    private sealed record Entry(CardCollectionSnapshot Snapshot, string Path, byte[] Bytes);

    public CardCollectionStore(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _directory = Path.Combine(stateDirectory, "card_collections");
        Refresh();
    }

    public IReadOnlyList<string> LoadIssues => _loadIssues.ToArray();

    public IReadOnlyList<CardCollectionSnapshot> GetAll() => _entries.Values
        .Select(entry => entry.Snapshot.Copy()).ToArray();

    public bool TryGet(string id, out CardCollectionSnapshot snapshot)
    {
        if (_entries.TryGetValue(id, out Entry? entry))
        {
            snapshot = entry.Snapshot.Copy();
            return true;
        }
        snapshot = null!;
        return false;
    }

    public void Refresh()
    {
        _entries.Clear();
        _loadIssues.Clear();
        string[] paths;
        try
        {
            // Directory.Exists hides access failures; enumerate directly so their
            // actual causes remain visible, while a never-created store is normal.
            paths = Directory.GetFiles(_directory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(paths, StringComparer.Ordinal);
        }
        catch (DirectoryNotFoundException) { return; }
        catch (Exception exception)
        {
            RememberLoadIssue(_directory, exception);
            return;
        }
        foreach (string path in paths)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                CardCollectionSnapshot snapshot = Read(bytes);
                if (_entries.ContainsKey(snapshot.Id))
                    throw new InvalidDataException("Duplicate card collection id: " + snapshot.Id);
                _entries.Add(snapshot.Id, new(snapshot, path, bytes));
            }
            catch (Exception exception) { RememberLoadIssue(path, exception); }
        }
    }

    public CardCollectionSnapshot Save(string? id, string name, IReadOnlyList<ModelKey> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ModelKey[] detached = cards.ToArray();
        ValidateCards(detached);
        Entry? previous = null;
        if (id is not null && !_entries.TryGetValue(id, out previous))
            throw new KeyNotFoundException("Card collection does not exist: " + id);
        string savedId = previous?.Snapshot.Id ?? "collection:" + Guid.NewGuid().ToString("N");
        var saved = new CardCollectionSnapshot(savedId, name.Trim(),
            previous is null ? 1 : checked(previous.Snapshot.Revision + 1), Array.AsReadOnly(detached))
        { UpdatedAtUtc = DateTimeOffset.UtcNow };
        string path = previous?.Path ?? Path.Combine(_directory, savedId["collection:".Length..] + ".json");
        try
        {
            EnsureUnchanged(previous, path);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Document
            {
                SchemaVersion = SchemaVersion, Id = saved.Id, Name = saved.Name,
                Revision = saved.Revision, Cards = detached, UpdatedAtUtc = saved.UpdatedAtUtc
            }, _json);
            SearchPersistenceFile.WriteAtomic(path, bytes);
            _entries[saved.Id] = new(saved, path, bytes);
        }
        catch (Exception exception)
        {
            RecordFailure(exception, "Save", path);
            throw;
        }
        return saved.Copy();
    }

    public bool Delete(string id)
    {
        if (!_entries.TryGetValue(id, out Entry? entry)) return false;
        try
        {
            EnsureUnchanged(entry, entry.Path);
            File.Delete(entry.Path);
            _entries.Remove(id);
            return true;
        }
        catch (Exception exception)
        {
            RecordFailure(exception, "Delete", entry.Path);
            throw;
        }
    }

    private static void EnsureUnchanged(Entry? previous, string path)
    {
        // A stale UI must not overwrite a newly damaged/future-format asset.
        // Refresh is an explicit way to observe external edits before retrying.
        if (previous is null)
        {
            if (File.Exists(path)) throw new IOException("Card collection path already exists: " + path);
        }
        else if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(previous.Bytes))
            throw new IOException("Card collection file changed; refresh before editing: " + path);
    }

    private CardCollectionSnapshot Read(byte[] bytes)
    {
        // Inspect schema before strict deserialization so newer envelopes retain
        // their real unsupported-version cause even if they contain new fields.
        using (JsonDocument envelope = JsonDocument.Parse(bytes))
        {
            if (envelope.RootElement.ValueKind != JsonValueKind.Object ||
                !envelope.RootElement.TryGetProperty(nameof(Document.SchemaVersion), out JsonElement version) ||
                !version.TryGetInt32(out int schema))
                throw new InvalidDataException("Card collection schema version is required.");
            if (schema != SchemaVersion)
                throw new InvalidDataException($"Unsupported card collection schema: {schema}; supported={SchemaVersion}");
        }
        Document document = JsonSerializer.Deserialize<Document>(bytes, _json)
            ?? throw new InvalidDataException("Card collection document is null.");
        if (string.IsNullOrWhiteSpace(document.Id) || string.IsNullOrWhiteSpace(document.Name) ||
            document.Revision < 1 || document.Cards is null)
            throw new InvalidDataException("Card collection requires id, name, positive revision and cards.");
        ValidateCards(document.Cards);
        return new(document.Id, document.Name, document.Revision, Array.AsReadOnly(document.Cards))
        { UpdatedAtUtc = document.UpdatedAtUtc };
    }

    private static void ValidateCards(IReadOnlyList<ModelKey> cards)
    {
        var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach (ModelKey card in cards)
        {
            if (!card.IsValid || card.Category != BaseGameModelKeys.Categories.Card ||
                !ModelKey.TryParseExact(card.Serialized, out ModelKey parsed) || parsed != card)
                throw new InvalidDataException("Card collection member must be a valid CARD identity: " + card.Serialized);
            if (!seen.Add(card))
                throw new InvalidDataException("Duplicate card collection member: " + card.Serialized);
        }
    }

    private void RememberLoadIssue(string path, Exception exception)
    {
        _loadIssues.Add(path + ": " + exception);
        RecordFailure(exception, "Load", path);
    }

    private static void RecordFailure(Exception exception, string stage, string path)
    {
        try
        {
            RuntimeIssueLedger.Current.Record(exception, "CardCollectionPersistenceFailed",
                nameof(CardCollectionStore), stage, "issues.capture_failed", correlationId: path);
        }
        catch { /* Diagnostics cannot replace the original persistence failure. */ }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var json = new JsonSerializerOptions
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        json.Converters.Add(new SearchWorkspace.ModelKeyJsonConverter());
        return json;
    }

    private sealed class Document
    {
        public int SchemaVersion { get; set; }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int Revision { get; set; }
        public ModelKey[]? Cards { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
