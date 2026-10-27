using System.Text.Json;
using System.Text.Json.Serialization;

namespace RolltheSpire2.Ui.Workspace;

internal sealed partial class SearchWorkspace
{
    private PreparedWorkbenchRead? _preparedWorkbenchRead;
    private long _workbenchReadRevision;

    // A menu session may strongly hold this receipt without creating any controls.
    // Its bytes, DTO and original error are only published to the workspace on adoption.
    internal sealed class PreparedWorkbenchRead(string path, byte[]? bytes, WorkbenchSearchDraft? draft,
        Exception? error, long revision)
    {
        internal string Path { get; } = path;
        internal WorkbenchSearchDraft? Draft { get; } = draft;
        internal Exception? Error { get; } = error;
        internal long Revision { get; } = revision;
        internal bool SameBytes(byte[]? current) => bytes is null ? current is null && Error is null
            : current is not null && bytes.AsSpan().SequenceEqual(current);
    }

    // Safe for Task.Run: file bytes and DTO parsing only. No quarantine, state writes,
    // diagnostics, game-model observation or Godot access occurs on this path.
    internal PreparedWorkbenchRead PrepareWorkbenchRead() => RefreshPreparedWorkbenchRead(null);

    internal PreparedWorkbenchRead RefreshPreparedWorkbenchRead(PreparedWorkbenchRead? candidate)
    {
        long revision = Volatile.Read(ref _workbenchReadRevision);
        string path = Path.Combine(_directory, "query_workbench.json");
        var read = ReadWorkbenchBytes(path);
        if (candidate is not null && candidate.Revision == revision && read.Error is null &&
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(candidate.Path),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && candidate.SameBytes(read.Bytes))
            return candidate;
        return ParseWorkbenchRead(path, read.Bytes, read.Error, revision);
    }

    // Called by the owning main-thread menu session after checking its generation/account.
    // Saving or explicit recovery also retires receipts that were still being prepared.
    internal void AdoptPreparedWorkbenchRead(PreparedWorkbenchRead prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        string expected = Path.GetFullPath(Path.Combine(_directory, "query_workbench.json"));
        if (!string.Equals(expected, Path.GetFullPath(prepared.Path),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("PreparedWorkbenchDirectoryMismatch", nameof(prepared));
        if (prepared.Revision == Volatile.Read(ref _workbenchReadRevision)) _preparedWorkbenchRead = prepared;
    }

    private void InvalidatePreparedWorkbenchRead()
    {
        Interlocked.Increment(ref _workbenchReadRevision);
        _preparedWorkbenchRead = null;
    }

    private static (byte[]? Bytes, Exception? Error) ReadWorkbenchBytes(string path)
    {
        try { return (File.ReadAllBytes(path), null); }
        catch (FileNotFoundException) { return (null, null); }
        catch (DirectoryNotFoundException) { return (null, null); }
        catch (Exception exception) { return (null, exception); }
    }

    private PreparedWorkbenchRead ParseWorkbenchRead(string path, byte[]? bytes, Exception? readError, long revision)
    {
        if (readError is not null || bytes is null) return new(path, bytes, null, readError, revision);
        try
        {
            // Match File.ReadAllText's BOM-aware decoding, including older UTF-16 saves.
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            string json = reader.ReadToEnd();
            using (var header = JsonDocument.Parse(json))
                if (header.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var field in header.RootElement.EnumerateObject())
                        if (field.Name.Equals("SchemaVersion", StringComparison.OrdinalIgnoreCase) &&
                            field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out int version) && version > SchemaVersion)
                            throw new InvalidDataException($"UnsupportedFutureSchema:{version}");
            var strict = new JsonSerializerOptions(_json)
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectRequiredConstructorParameters = true,
                RespectNullableAnnotations = true
            };
            var draft = JsonSerializer.Deserialize<WorkbenchSearchDraft>(json, strict)
                ?? throw new InvalidDataException("PersistenceDocumentNull");
            if (draft.Version is not (1 or 2 or 3 or 4)) throw new InvalidDataException("integration.load_version");
            return new(path, bytes, draft, null, revision);
        }
        catch (Exception exception) { return new(path, bytes, null, exception, revision); }
    }
}
