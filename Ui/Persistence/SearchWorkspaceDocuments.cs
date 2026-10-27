using System.Text.Json.Serialization;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Persistence;

internal sealed class UserPreferencesDocument
{
    public UiColorPreferences Appearance { get; set; } = new();
    public string FontChoice { get; set; } = "game-serif";
    public Dictionary<string, bool> WorkbenchFlags { get; set; } = new();
    public string WorkbenchPage { get; set; } = "neow";
    public int SchemaVersion { get; set; } = SearchWorkspace.SchemaVersion;
    // Last observed Workshop UI language only. The workspace deliberately does not use this
    // as localization authority; live TranslationServer locale wins on startup and hot reload.
    public string Language { get; set; } = string.Empty;
    // Explicit UI choice; missing/empty preserves existing follow-game behavior.
    public string LanguageOverride { get; set; } = string.Empty;
    public float PanelWidth { get; set; }
    public float PanelHeight { get; set; }
    public string SearchMode { get; set; } = "Auto";
    public int? SearchWorkerBudget { get; set; }
    public int QueryHistoryLimit { get; set; } = 30;
    public bool ShowOfficialPresets { get; set; } = true;
    // Opt-in, including preferences saved before the in-run entry was introduced.
    public bool ShowInRunPredictionEntry { get; set; }
    public bool CrystalSphereAvoidCurse { get; set; } = true;
    public bool CrystalSphereIncludeDriftwood { get; set; } = true;
    public bool CrystalSphereIncludeEnchantments { get; set; }
    public bool CrystalSphereTransparentMask { get; set; }
    // Missing values retain the prediction behavior from before feature management.
    public bool OperationPredictionEnabled { get; set; } = true;
    public OperationPredictionPreferences OperationPredictionModes { get; set; } = new();
    public bool MapPredictionEnabled { get; set; } = true;
    public bool ShowInRunConsole { get; set; } = true;
    public double InRunConsoleVerticalPercent { get; set; } = 50;
    public double InRunConsoleScalePercent { get; set; } = 100;
    public bool SkipExactValidation { get; set; }
    public string ReadReleaseNotesVersion { get; set; } = string.Empty;
    public Dictionary<string, string> ReadNotesVersions { get; set; } = new();
    public string LastPage { get; set; } = "Analysis";
    public bool ActInformationGuideExpanded { get; set; }
    public bool ActInformationIdentityGuideExpanded { get; set; }
    public bool? ActInformationMapGuideExpanded { get; set; }
}

internal sealed class PredictorContextDocument
{
    public SeedLibraryContext? Party { get; set; }
    public string PartySeed { get; set; } = "";
    public bool PartyActive { get; set; }
    public int SchemaVersion { get; set; } = SearchWorkspace.SchemaVersion;
    public string Seed { get; set; } = string.Empty;
    public string CharacterKey { get; set; } = BaseGameModelKeys.Characters.Silent.Serialized;
    public int Ascension { get; set; } = SeedPredictionInputLimits.MaximumAscension;
    public int PlayersCount { get; set; } = SeedPredictionInputLimits.MinimumPlayers;
    public int PlayerSlotIndex { get; set; }
    // Last-known display value; never a substitute for runtime authority capture.
    public bool? AllCharacterCardPoolsUnlocked { get; set; }
    public bool TezcataraHasBasicStrike { get; set; } = true;
    public bool NonupeipeSwiftEnchantableAtLeast4 { get; set; } = true;
    public bool TanxInstinctEnchantableAtLeast3 { get; set; } = true;
    public bool PaelGoopyDefendCardsAtLeast3 { get; set; } = true;
    public bool PaelAllowLegionNoEventPet { get; set; } = true;
    public bool PaelRemovableCardsAtLeast5 { get; set; } = true;
    public bool OrobasArchaicToothConditionMet { get; set; } = true;
    public bool OrobasTouchOfOrobasConditionMet { get; set; } = true;
    public bool DarvAllowPandorasBoxRelicSet { get; set; } = true;
    public string PreferredOpeningRouteId { get; set; } = string.Empty;
    public int PreferredOpeningChoiceSlotIndex { get; set; } = -1;
    public string PreferredRewardRouteGroupId { get; set; } = string.Empty;

    public AncientOptionConditionProfile AncientOptionConditions => new(
        TezcataraHasBasicStrike,
        NonupeipeSwiftEnchantableAtLeast4,
        TanxInstinctEnchantableAtLeast3,
        PaelGoopyDefendCardsAtLeast3,
        PaelAllowLegionNoEventPet,
        PaelRemovableCardsAtLeast5,
        OrobasArchaicToothConditionMet,
        OrobasTouchOfOrobasConditionMet,
        DarvAllowPandorasBoxRelicSet);
}

internal sealed class SearchWorkspaceDocument
{
    public int SchemaVersion { get; set; } = SearchWorkspace.SchemaVersion;
    public string CharacterKey { get; set; } = BaseGameModelKeys.Characters.Silent.Serialized;
    public int Ascension { get; set; } = SeedPredictionInputLimits.MaximumAscension;
    public SearchDraft? Draft { get; set; }
    public SearchRunDraft? RunDraft { get; set; }
    public string QueryFingerprint { get; set; } = string.Empty;
    public List<PersistedSearchResult> Results { get; set; } = new();
    public Dictionary<string, string> LegacyPresetQueryKeys { get; set; } = [];
    public Dictionary<string, string> LegacyQueryAliases { get; set; } = [];
    public List<QueryHistoryEntry> QueryHistory { get; set; } = [];
    public string ActiveHistoryId { get; set; } = "";
    public string ResultBatchId { get; set; } = string.Empty;
    public DateTimeOffset? ResultsStartedAtUtc { get; set; }
    public global::RolltheSpire2.Ui.Workspace.WorkbenchSearchDraft? ResultDraft { get; set; }
    public SeedLibraryContext? ResultContext { get; set; }
    public bool ResultsSkipExactValidation { get; set; }
    public SearchProgressSnapshot? ResultProgress { get; set; }
    public SearchContinuationSnapshot? ResultContinuation { get; set; }
    public SearchCursorDocument? CommittedCursor { get; set; }
    public List<string> AuxiliaryIssues { get; set; } = [];
}

internal sealed record PersistedTreasureSource(string RelicKey, int TargetSlot, int? EncounterOrdinal, bool ObtainedAtOpening);

internal sealed class PersistedSearchResult
{
    public SeedQueryAssociation? SourceAssociation { get; set; }
    private bool _legacyIsUnverified;
    public SeedLibraryContext? Context { get; set; }
    public bool IsUnverified
    {
        get => ValidationDisposition is { } state ? state != SearchDisposition.Match : _legacyIsUnverified;
        set => _legacyIsUnverified = value;
    }
    public bool IsUnfiltered { get; set; }
    public SearchDisposition? ValidationDisposition { get; set; }
    public string ValidationIssue { get; set; } = string.Empty;
    public IReadOnlyList<string> ExecutionWarnings { get; set; } = [];
    public RequestSupportAssessment? SupportScope { get; set; }
    [JsonIgnore] public RequestSupportScopeState SupportScopeState => SupportScope?.State ?? RequestSupportScopeState.Unknown;
    [JsonIgnore] public SearchDisposition EffectiveValidationDisposition => ValidationDisposition ??
        (IsUnverified ? SearchDisposition.NotEvaluatedByPolicy : SearchDisposition.Match);
    [JsonIgnore] public bool HasIncompleteValidation => EffectiveValidationDisposition is SearchDisposition.Unknown or SearchDisposition.Unsupported;
    public PartySeedInformation? Party { get; set; }
    public string Seed { get; set; } = string.Empty;
    public string CharacterKey { get; set; } = string.Empty;
    public int Ascension { get; set; }
    public string QueryFingerprint { get; set; } = string.Empty;
    public string WitnessOpeningRouteId { get; set; } = string.Empty;
    private IReadOnlyList<PersistedTreasureSource> _treasureSources = [];
    public IReadOnlyList<PersistedTreasureSource> TreasureSources
    {
        get => EffectiveValidationDisposition == SearchDisposition.Match ? _treasureSources : [];
        set => _treasureSources = value ?? [];
    }
}

internal sealed class SearchCursorDocument
{
    public int SchemaVersion { get; set; } = SearchWorkspace.SchemaVersion;
    public bool Initialized { get; set; }
    public ulong PersistentOrigin { get; set; }
    public ulong NextCursor { get; set; }
    public long WrapCount { get; set; }
    public string PendingOriginChangeId { get; set; } = "";
}

internal sealed class SearchEnvironmentSignature
{
    public SearchEnvironmentSignature() { IdentityVersion = SearchEnvironmentIdentity.CurrentVersion; }
    // JSON without this new component belongs to the old mixed module/content
    // algorithm. Never reinterpret its hash as the current environment identity.
    [JsonConstructor]
    public SearchEnvironmentSignature(int identityVersion) { IdentityVersion = identityVersion; }
    public int IdentityVersion { get; set; }
    public int SchemaVersion { get; set; } = SearchWorkspace.SchemaVersion;
    public string GameVersionIdentity { get; set; } = string.Empty;
    public string CharacterCatalogFingerprint { get; set; } = string.Empty;
    public string CardCatalogFingerprint { get; set; } = string.Empty;
    public string RelicCatalogFingerprint { get; set; } = string.Empty;
    public string PotionCatalogFingerprint { get; set; } = string.Empty;
    public string WorldCatalogFingerprint { get; set; } = string.Empty;
    public string UnlockFingerprint { get; set; } = string.Empty;
    public string CatalogFingerprint { get; set; } = string.Empty;
    public string RuntimeContentFingerprint { get; set; } = string.Empty;
    public string RuntimeModuleDiagnostics { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int CardCount { get; set; }
    public int RelicCount { get; set; }
    public int PotionCount { get; set; }
    public int AncientCount { get; set; }
    public bool CaptureComplete { get; set; }
    public string CaptureIssue { get; set; } = string.Empty;
    // Compare persisted fields without serializing an unchanged capture on the UI thread.
    internal bool HasSameStoredContent(SearchEnvironmentSignature other) =>
        IdentityVersion == other.IdentityVersion &&
        SchemaVersion == other.SchemaVersion &&
        GameVersionIdentity == other.GameVersionIdentity &&
        CharacterCatalogFingerprint == other.CharacterCatalogFingerprint &&
        CardCatalogFingerprint == other.CardCatalogFingerprint &&
        RelicCatalogFingerprint == other.RelicCatalogFingerprint &&
        PotionCatalogFingerprint == other.PotionCatalogFingerprint &&
        WorldCatalogFingerprint == other.WorldCatalogFingerprint &&
        UnlockFingerprint == other.UnlockFingerprint &&
        CatalogFingerprint == other.CatalogFingerprint &&
        RuntimeContentFingerprint == other.RuntimeContentFingerprint &&
        RuntimeModuleDiagnostics == other.RuntimeModuleDiagnostics &&
        Fingerprint == other.Fingerprint &&
        CharacterCount == other.CharacterCount &&
        CardCount == other.CardCount &&
        RelicCount == other.RelicCount &&
        PotionCount == other.PotionCount &&
        AncientCount == other.AncientCount &&
        CaptureComplete == other.CaptureComplete &&
        CaptureIssue == other.CaptureIssue;

    internal SearchEnvironmentSignature Snapshot() => (SearchEnvironmentSignature)MemberwiseClone();
}
