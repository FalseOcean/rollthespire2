using RolltheSpire2.Ui.Workspace;
using System.Text.Json.Serialization;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Shell;

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
/// Current preset intent. Historical formats exist only at the read boundary;
/// unresolved original assets are opaque and cannot become executable branches.
/// </summary>
internal sealed record SearchPresetDefinition(
    string Id,
    SearchPresetSource Source,
    string Title,
    string Description,
    ModelKey CharacterKey,
    int Ascension,
    WorkbenchSearchDraft? Workbench,
    int ConditionCount,
    IReadOnlyList<SearchPresetVisualIconRef> VisualIcons,
    SearchPresetProbabilitySnapshot SavedProbability,
    SearchPresetProvenance Provenance,
    DateTimeOffset CreatedAtUtc)
{
    public string Name => Title; // narrow v1/source compatibility alias
    // Curated translations are presentation only; raw text remains the save/name identity.
    [JsonIgnore] public DeveloperSeedText? BuiltInTitle { get; init; }
    [JsonIgnore] public DeveloperSeedText? BuiltInDescription { get; init; }
    public string TitleFor(string language) => Source == SearchPresetSource.BuiltIn && BuiltInTitle?.Zh == Title
        ? BuiltInTitle.Resolve(language) : Title;
    public string DescriptionFor(string language) => Source == SearchPresetSource.BuiltIn && BuiltInDescription?.Zh == Description
        ? BuiltInDescription.Resolve(language) : Description;
    [JsonIgnore] public string SearchText => Title + " " + Description + " " + TitleFor("en") + " " + DescriptionFor("en");
    public string EnvironmentFingerprint { get; init; } = "";
    public SearchPresetUnresolvedAsset? UnresolvedAsset { get; init; }
    // Persistence provenance only; never used to execute a query.
    internal string PreservedSourceFileName { get; init; } = string.Empty;
    public bool IsMultiplayer => Workbench?.Mode == WorldGameMode.Multiplayer;
    public bool QueryResolved => Workbench is not null && UnresolvedAsset is null;
}

internal sealed record SearchPresetCapture(
    ModelKey CharacterKey,
    int Ascension,
    WorkbenchSearchDraft? Workbench,
    int ConditionCount,
    SearchPresetProbabilitySnapshot SavedProbability,
    SearchPresetProvenance Provenance,
    DateTimeOffset CapturedAtUtc)
{
}

internal sealed record SearchPresetUnresolvedAsset(
    string OriginalJson, string Issue, IReadOnlyList<SearchPresetUnresolvedReference> References);
