using System.Text.Json;
using System.Text.Json.Serialization;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Persistence;

/// <summary>Drawing's captured selection; null selects the built-in all-rare projection.</summary>
internal sealed class DrawingCardCollectionPreferenceStore
{
    internal const string FileName = "drawing_card_collection.json";
    private const int SchemaVersion = 1;
    private readonly string _path;
    private readonly JsonSerializerOptions _json = CreateJsonOptions();

    internal DrawingCardCollectionPreferenceStore(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _path = Path.Combine(stateDirectory, FileName);
    }

    internal CardCollectionSnapshot? Load()
    {
        try
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(_path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            // Check the envelope first so future schemas retain their real cause.
            using (JsonDocument envelope = JsonDocument.Parse(bytes))
            {
                if (envelope.RootElement.ValueKind != JsonValueKind.Object ||
                    !envelope.RootElement.TryGetProperty(nameof(Document.SchemaVersion), out JsonElement version) ||
                    version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema))
                    throw new InvalidDataException("Drawing card collection preference schema version is required.");
                if (schema != SchemaVersion)
                    throw new InvalidDataException($"Unsupported drawing card collection preference schema: {schema}; supported={SchemaVersion}");
            }
            Document document = JsonSerializer.Deserialize<Document>(bytes, _json)
                ?? throw new InvalidDataException("Drawing card collection preference document is null.");
            if (document.Selection is null) return null;
            Selection selection = document.Selection;
            var snapshot = new CardCollectionSnapshot(selection.Id!, selection.Name!, selection.Revision,
                selection.Cards!) { UpdatedAtUtc = selection.UpdatedAtUtc };
            Validate(snapshot);
            return snapshot.Copy();
        }
        catch (Exception exception)
        {
            RecordFailure(exception, "Load");
            throw;
        }
    }

    // The caller commits its runtime selection only after this succeeds.
    internal void Save(CardCollectionSnapshot? selection)
    {
        try
        {
            CardCollectionSnapshot? captured = null;
            if (selection is not null)
            {
                Validate(selection);
                captured = selection.Copy();
            }
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Document
            {
                SchemaVersion = SchemaVersion,
                Selection = captured is null ? null : new Selection
                {
                    Id = captured.Id, Name = captured.Name, Revision = captured.Revision,
                    Cards = captured.Cards.ToArray(), UpdatedAtUtc = captured.UpdatedAtUtc
                }
            }, _json);
            SearchPersistenceFile.WriteAtomic(_path, bytes);
        }
        catch (Exception exception)
        {
            RecordFailure(exception, "Save");
            throw;
        }
    }

    private static void Validate(CardCollectionSnapshot selection)
    {
        if (string.IsNullOrWhiteSpace(selection.Id) || string.IsNullOrWhiteSpace(selection.Name) ||
            selection.Revision < 1 || selection.Cards is null)
            throw new InvalidDataException("Drawing card collection selection requires id, name, positive revision and cards.");
        var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach (ModelKey card in selection.Cards)
        {
            if (!card.IsValid || card.Category != BaseGameModelKeys.Categories.Card ||
                !ModelKey.TryParseExact(card.Serialized, out ModelKey parsed) || parsed != card)
                throw new InvalidDataException("Drawing card collection member must be a valid CARD identity: " + card.Serialized);
            if (!seen.Add(card))
                throw new InvalidDataException("Duplicate drawing card collection member: " + card.Serialized);
        }
    }

    private void RecordFailure(Exception exception, string stage)
    {
        try
        {
            RuntimeIssueLedger.Current.Record(exception, "DrawingCardCollectionPreferencePersistenceFailed",
                nameof(DrawingCardCollectionPreferenceStore), stage, "issues.capture_failed", correlationId: _path);
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
        [JsonRequired] public Selection? Selection { get; set; }
    }

    private sealed class Selection
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int Revision { get; set; }
        public ModelKey[]? Cards { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
