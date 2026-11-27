using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Persistence;

// A share carries authored intent. The recipient binds game content and full unlocks locally.
internal static class SearchPresetShare
{
    private const string LegacyPrefix = "RT2P1-";
    private const string Prefix = "RT2P2-";
    private const int MaximumBytes = 2 * 1024 * 1024;
    private sealed record Payload(string Title, string Description, IReadOnlyList<SearchPresetVisualIconRef> Icons, WorkbenchSearchDraft Draft);
    private static readonly JsonSerializerOptions Json = new(SearchPresetStore.CreateJsonOptions())
    {
        WriteIndented = false, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true
    };

    private static WorkbenchSearchDraft SharedIntent(WorkbenchSearchDraft draft) =>
        (draft with { UseFullUnlocks = true }).ToPresetIntent();

    internal static string Encode(SearchPresetDefinition preset)
    {
        var draft = preset.Workbench ?? throw new InvalidDataException("PresetCurrentIntentRequired");
        var payload = SharedSearchConditions.From(draft);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("PresetShareTooLarge");
        using var packed = new MemoryStream();
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        packed.Write(length);
        using (var compressor = new BrotliStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            compressor.Write(bytes);
        return Prefix + Convert.ToBase64String(packed.ToArray());
    }

    internal static SearchPresetDefinition Decode(string code, string language = "zh")
    {
        code = code.Trim();
        bool legacy = code.StartsWith(LegacyPrefix, StringComparison.Ordinal);
        if ((!legacy && !code.StartsWith(Prefix, StringComparison.Ordinal)) || code.Length > (MaximumBytes + 1024) * 2)
            throw new InvalidDataException("PresetShareFormat");
        byte[] packed = Convert.FromBase64String(code[Prefix.Length..]);
        if (packed.Length <= sizeof(int)) throw new InvalidDataException("PresetShareTruncated");
        int size = BinaryPrimitives.ReadInt32LittleEndian(packed);
        if (size <= 0 || size > MaximumBytes) throw new InvalidDataException("PresetShareLength");
        byte[] bytes = new byte[size];
        using var decoder = new BrotliDecoder();
        if (decoder.Decompress(packed.AsSpan(sizeof(int)), bytes, out int consumed, out int written) != OperationStatus.Done ||
            written != size || consumed != packed.Length - sizeof(int)) throw new InvalidDataException("PresetShareCorrupt");
        Payload data;
        if (legacy)
        {
            data = JsonSerializer.Deserialize<Payload>(bytes, Json) ?? throw new InvalidDataException("PresetShareEmpty");
            if (string.IsNullOrWhiteSpace(data.Title)) throw new InvalidDataException("PresetShareTitle");
        }
        else
        {
            var conditions = JsonSerializer.Deserialize<SharedSearchConditions>(bytes, Json) ?? throw new InvalidDataException("PresetShareEmpty");
            data = new(language == "zh" ? "分享预设" : "Shared preset", "", [], conditions.ToDraft());
        }
        SearchPresetCompatibilityResolver.ValidateWorkbenchShape(data.Draft);
        var draft = SharedIntent(data.Draft);
        var now = DateTimeOffset.UtcNow;
        return new("share:" + Guid.NewGuid().ToString("N"), SearchPresetSource.Temporary, data.Title.Trim(), data.Description,
            draft.Character, draft.Ascension, draft, SearchQueryProjection.CountConditions(draft.Query),
            SearchPresetStore.NormalizeVisualIcons(data.Icons.Select(icon => icon with { ResourceHint = "" }).ToArray()),
            SearchPresetProbabilitySnapshot.Unavailable(now), SearchPresetProvenance.Unknown("", now), now);
    }
}
