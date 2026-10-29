using RolltheSpire2.Search.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Persistence;

// A query owns its accumulated results. Runs and presets refer to this record;
// no execution plan, authority, or real-game state is retained here.
internal sealed class QueryHistoryEntry
{
    public string Id { get; set; } = "";
    public string QueryKey { get; set; } = "";
    public string EnvironmentFingerprint { get; set; } = "";
    public string RuntimeEnvironmentFingerprint { get; set; } = "";
    public SearchEnvironmentSignature? EnvironmentSignature { get; set; }
    public int IdentityVersion { get; set; }
    public bool EnvironmentKnown { get; set; }
    public WorkbenchSearchDraft Draft { get; set; } = null!;
    public SeedLibraryContext? Context { get; set; }
    public DateTimeOffset LastUsedAtUtc { get; set; }
    public int RunCount { get; set; }
    public double ElapsedSeconds { get; set; }
    public List<PersistedSearchResult> Results { get; set; } = [];
    public SearchContinuationSnapshot? Continuation { get; set; }
    public List<SearchContinuationSnapshot> RetainedContinuations { get; set; } = [];
    public List<ManualValidationSourceIdentity> ManualValidationSources { get; set; } = [];
    public List<ManualValidationRecovery> ManualValidationCandidates { get; set; } = [];
    public SearchProgressSnapshot? Progress { get; set; }
    public List<MapSearchProvenance> MapScans { get; set; } = [];

    internal SearchPresetDefinition AsPreset(string title) => new("history:" + Id, SearchPresetSource.Temporary,
        title, "", Draft.Character, Draft.Ascension, Draft.ToPresetIntent(), SearchQueryProjection.CountConditions(Draft.Query), [],
        SearchPresetProbabilitySnapshot.Unavailable(LastUsedAtUtc),
        SearchPresetProvenance.Unknown(Context?.GameVersion ?? Draft.GameVersion, LastUsedAtUtc), LastUsedAtUtc);
}
