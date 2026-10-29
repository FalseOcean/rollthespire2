using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Bootstrap;
using System.Text.Json;
using System.Text.Json.Serialization;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Ui.Workspace;

internal sealed partial class SearchWorkspace
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _json;
    private readonly Dictionary<string, Exception> _documentFailures = new(StringComparer.OrdinalIgnoreCase);
    internal Exception? LastPersistenceFailure { get; private set; }
    private readonly HashSet<string> _unreadableDocuments = new(StringComparer.OrdinalIgnoreCase);

    internal global::RolltheSpire2.Ui.Workspace.WorkbenchSearchDraft? LoadWorkbench()
    {
        const string file = "query_workbench.json";
        var prepared = _preparedWorkbenchRead;
        _preparedWorkbenchRead = null;
        string path = Path.Combine(_directory, file);
        var current = ReadWorkbenchBytes(path);
        // Always observe the complete current file, including same-size replacements.
        // Only parsing is reused; menu preparation never owns disk freshness.
        var read = prepared is not null && prepared.Revision == Volatile.Read(ref _workbenchReadRevision) &&
            current.Error is null && prepared.SameBytes(current.Bytes)
            ? prepared : ParseWorkbenchRead(path, current.Bytes, current.Error, Volatile.Read(ref _workbenchReadRevision));
        if (read.Error is { } exception)
        {
            if (exception is InvalidDataException { Message: "integration.load_version" })
            {
                _unreadableDocuments.Add(file);
                throw exception;
            }
            if (exception is not InvalidDataException || !exception.Message.StartsWith("UnsupportedFutureSchema:", StringComparison.Ordinal))
                Quarantine(path);
            if (File.Exists(path)) _unreadableDocuments.Add(file);
            RememberDocumentFailure(file, exception);
            throw new InvalidDataException("integration.load_shape", exception);
        }
        return read.Draft;
    }

    internal void SaveWorkbench(global::RolltheSpire2.Ui.Workspace.WorkbenchSearchDraft draft)
    {
        InvalidatePreparedWorkbenchRead();
        bool dirty = true;
        double age = 0;
        FlushDocument(ref dirty, ref age, "query_workbench.json", draft.WithoutCapturedAuthority());
        if (dirty) throw new IOException("WorkbenchPersistenceWriteFailed", _documentFailures.GetValueOrDefault("query_workbench.json"));
        ResolvePresentationFailure("DraftSave");
        ResolvePresentationFailure("Persistence:query_workbench.json");
    }

    internal void RecoverWorkbench(global::RolltheSpire2.Ui.Workspace.WorkbenchSearchDraft draft)
    {
        InvalidatePreparedWorkbenchRead();
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Version is not (1 or 2 or 3 or 4)) throw new InvalidDataException("integration.load_version");
        const string file = "query_workbench.json";
        string path = Path.Combine(_directory, file);
        // Explicit recovery is the only operation allowed to replace protected
        // intent. Keep protection until both preservation and commit succeed.
        _unreadableDocuments.Add(file);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(draft.WithoutCapturedAuthority(), _json);
        if (File.Exists(path))
        {
            string backup = path + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N");
            SearchPersistenceFile.WriteAtomic(backup, File.ReadAllBytes(path));
        }
        // Previously quarantined .corrupt files are deliberately left intact.
        SearchPersistenceFile.WriteAtomic(path, payload);
        _unreadableDocuments.Remove(file);
        _documentFailures.Remove(file);
        LastPersistenceFailure = _documentFailures.Values.LastOrDefault();
        ResolvePresentationFailure("DraftSave");
        ResolvePresentationFailure("Persistence:" + file);
    }

    private T? Load<T>(string fileName, JsonSerializerOptions? options = null) where T : class
    {
        string path = Path.Combine(_directory, fileName);
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            // Inspect every occurrence before materializing enums or other current DTO fields.
            // These DTOs use plain integer schema setters/defaults and no numeric converters,
            // so deserialization cannot produce a future schema absent from this header.
            using (var header = JsonDocument.Parse(json))
                if (header.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var field in header.RootElement.EnumerateObject())
                        if (field.Name.Equals("SchemaVersion",StringComparison.OrdinalIgnoreCase) &&
                            field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out int version) && version > SchemaVersion)
                            throw new InvalidDataException($"UnsupportedFutureSchema:{version}");
            T value = JsonSerializer.Deserialize<T>(json, options ?? _json)
                ?? throw new InvalidDataException("PersistenceDocumentNull");
            return value;
        }
        catch (Exception ex)
        {
            // A future schema is not corruption. Preserve it in place; likewise
            // never overwrite a file whose failed read/quarantine left it intact.
            if (ex is not InvalidDataException || !ex.Message.StartsWith("UnsupportedFutureSchema:", StringComparison.Ordinal))
                Quarantine(path);
            if (File.Exists(path)) _unreadableDocuments.Add(fileName);
            RememberDocumentFailure(fileName, ex);
            return null;
        }
    }

    private void Quarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            string quarantined = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            File.Move(path, quarantined, overwrite: false);
        }
        catch (Exception exception)
        {
            RecordPresentationFailure(exception, "PersistenceQuarantine");
        }
    }

    private void FlushDocument<T>(ref bool dirty, ref double age, string fileName, T document)
    {
        if (!dirty) return;
        if (WriteNow(fileName, document)) dirty = false;
        age = 0d;
    }

    private bool WriteNow<T>(string fileName, T document)
    {
        if (_unreadableDocuments.Contains(fileName))
        {
            if (!_documentFailures.ContainsKey(fileName))
                RememberDocumentFailure(fileName, new InvalidDataException("PersistenceDocumentProtected:" + fileName));
            return false;
        }
        try
        {
            string path = Path.Combine(_directory, fileName);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(document, _json);
            SearchPersistenceFile.WriteAtomic(path, payload);
            _documentFailures.Remove(fileName);
            LastPersistenceFailure = _documentFailures.Values.LastOrDefault();
            return true;
        }
        catch (Exception ex)
        {
            RememberDocumentFailure(fileName, ex);
            return false;
        }
    }

    private void PersistenceNotice(Action write)
    { try { write(); } catch (Exception exception) { RecordPresentationFailure(exception, "PersistenceDiagnostics"); } }
    private void RememberDocumentFailure(string fileName, Exception exception)
    {
        _documentFailures[fileName] = exception;
        LastPersistenceFailure = exception;
        RecordPresentationFailure(exception, "Persistence:" + fileName);
    }
    internal void RecordPresentationFailure(Exception exception, string stage, string? seed = null)
    {
        // Diagnostics are evidence, never an additional admission or terminal owner.
        try
        {
            Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception, "SearchWorkspaceAuxiliaryFailed",
                "SearchWorkspace", stage, "issues.capture_failed", correlationId: seed);
        }
        catch { /* The original exception remains in memory below if diagnostics fail. */ }
        string detail = stage + ": " + exception;
        if (_workspace is { } workspace && !workspace.AuxiliaryIssues.Contains(detail, StringComparer.Ordinal))
        { workspace.AuxiliaryIssues.Add(detail); MarkWorkspaceDirty(); }
        if (!_auxiliaryFailures.Contains(detail, StringComparer.Ordinal)) _auxiliaryFailures.Add(detail);
        try { RuntimeLog.Warn("searchWorkspaceAuxiliaryFailed=true;stage=" + stage + ";seed=" + seed + ";exception=" + exception); }
        catch { /* Logging cannot take ownership of a valid search. */ }
    }
    private readonly List<string> _auxiliaryFailures = [];
    internal IReadOnlyList<string> AuxiliaryFailures => _auxiliaryFailures;
    internal void ResolvePresentationFailure(string stage) => ResolvePresentationFailure(stage, _ => true);
    internal bool ResolvePresentationFailure(string stage, Predicate<string> recoveredByThisOperation)
    {
        if (_workspace is not { } workspace) return false;
        // AuxiliaryIssues is the saved unresolved notice, not an error history.
        // Recovery of one responsibility does not resolve another stage, pending
        // candidate, execution failure or an unsuccessful write.
        string prefix = stage + ": ";
        var recovered = workspace.AuxiliaryIssues.Where(detail => detail.StartsWith(prefix, StringComparison.Ordinal) &&
            recoveredByThisOperation(detail)).ToArray();
        foreach (string detail in recovered)
        {
            if (!_auxiliaryFailures.Contains(detail, StringComparer.Ordinal)) _auxiliaryFailures.Add(detail);
            // Preserve loaded legacy evidence in the existing operational log
            // before withdrawing its current-failure notice. Do not invent a new
            // exception or replace the original stage/stack with a recovery code.
            try { RuntimeLog.TryBackgroundInfo("searchWorkspaceAuxiliaryRecovered=true;stage=" + stage + ";originalJson=" + RuntimeLog.SafeJson(detail)); }
            catch { /* Original evidence remains in memory and its originating log. */ }
        }
        if (recovered.Length == 0) return false;
        foreach (string detail in recovered) workspace.AuxiliaryIssues.Remove(detail);
        // A successful render may have included the old notice just before it
        // recovered. Give the existing projection poll a revision to withdraw it.
        ResultsRevision++;
        MarkWorkspaceDirty();
        return true;
    }
    internal sealed class ModelKeyJsonConverter : JsonConverter<ModelKey>
    {
        public override ModelKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string value = reader.GetString() ?? string.Empty;
            // ':' is the existing serialized empty ModelKey used by inactive UI
            // rows. Other malformed identities must not silently become that value.
            if (value == ":") return default;
            if (!ModelKey.TryParseExact(value, out ModelKey key))
                throw new JsonException("PersistenceModelKeyInvalid");
            return key;
        }

        public override void Write(Utf8JsonWriter writer, ModelKey value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Serialized);
    }
}
