using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Core.Authority;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Workspace;

/// <summary>
/// Shared workspace state and commands. Runtime owns execution; document and file
/// partials own compatible reads, atomic commits, preferences and saved UI context.
/// Live plans, authority and witnesses stay in process-only batches.
/// </summary>
internal sealed partial class SearchWorkspace
{
    internal const int SchemaVersion = 1;
    internal const double WorkspaceDebounceSeconds = 0.60;
    private const double CursorCheckpointSeconds = 1.50;

    private readonly SearchPresetCatalog _presets;
    private SearchWorkspaceDocument _workspace;
    private SearchCursorDocument _cursor;
    private SearchEnvironmentSignature _environment;
    private bool _environmentDirty;
    private bool _workspaceDirty;
    private bool _cursorDirty;
    private double _workspaceDirtyAge;
    private double _cursorDirtyAge;
    internal bool HasPendingWorkspaceCommit => (_workspaceDirty || _cursorDirty) && !_unreadableDocuments.Contains("search_workspace.json");
    internal bool HasUnsavedSearchState => _workspaceDirty && _documentFailures.ContainsKey("search_workspace.json");

    public SearchWorkspace(string userDataDirectory, bool initializeSearchCursor = true)
    {
        _directory = Path.Combine(userDataDirectory, "RolltheSpire2", "state");
        _json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        _json.Converters.Add(new ModelKeyJsonConverter());
        _presets = new SearchPresetCatalog(_directory);
        _preferences = Load<UserPreferencesDocument>("user_preferences.json") ?? new UserPreferencesDocument();
        _workspace = Load<SearchWorkspaceDocument>("search_workspace.json") ?? new SearchWorkspaceDocument();
        _predictorContext = Load<PredictorContextDocument>("predictor_context.json") ?? new PredictorContextDocument();
        _cursor = Load<SearchCursorDocument>("search_cursor.json") ?? new SearchCursorDocument();
        _environment = Load<SearchEnvironmentSignature>("search_environment.json") ?? new SearchEnvironmentSignature();
        // The workspace commit owns both result/pending delivery and its cursor.
        // A crash between the workspace and mirror writes must resume its checkpoint.
        if (_workspace.CommittedCursor is { } committed)
            _cursor = CopyCursor(committed);
        NormalizeDocuments();
        ClearLegacyAppearance();
        InitializeFeaturePreferences();
        LoadQueryHistory();
        if (initializeSearchCursor)
            EnsureCursorInitialized();
    }

    public SearchPresetCatalog Presets => _presets;
    public SearchWorkspaceDocument Workspace => _workspace;
    public SearchCursorDocument Cursor => _cursor;
    public SearchEnvironmentSignature Environment => _environment;
    public bool EnvironmentResetOccurred { get; private set; }
    public string EnvironmentResetReason { get; private set; } = string.Empty;

    internal long ResultsRevision { get; private set; }

    internal sealed record PreparedSearchResultWorkspace(SearchWorkspaceDocument Workspace,
        QueryHistoryEntry? History, SearchResultBatch Batch, DateTimeOffset StartedAtUtc);

    internal PreparedSearchResultWorkspace PrepareResultWorkspace(string queryFingerprint,
        global::RolltheSpire2.Ui.Workspace.WorkbenchSearchDraft? draft = null, SeedLibraryContext? context = null, bool skipExactValidation = false)
    {
        // Establish frozen request identity and all receiving storage before a
        // session exists. A structural/identity error cannot leave half a new run
        // installed or silently continue with an editor-owned mutable draft.
        var savedDraft = draft is null ? null : FreezeDraft(draft);
        SeedLibraryContext? savedContext = null;
        if (context is not null)
        {
            try { savedContext = JsonSerializer.Deserialize<SeedLibraryContext>(JsonSerializer.Serialize(context, _json), _json); }
            catch (Exception exception) { RecordPresentationFailure(exception, "ResultContextCopy"); }
        }
        string batchId = Guid.NewGuid().ToString("N");
        DateTimeOffset started = DateTimeOffset.UtcNow;
        var histories = _workspace.QueryHistory.ToList();
        QueryHistoryEntry? history = null;
        if (savedDraft is not null)
        {
            string key = QueryHistoryIdentity.Key(savedDraft);
            bool known = SearchEnvironmentIdentity.IsComparable(_environment);
            string environment = known
                ? QueryHistoryIdentity.EnvironmentKey(savedDraft, _environment.Fingerprint) : "unknown:" + batchId;
            history = SearchQueryProjection.Find(histories, key, environment, _environment, known);
            if (history is null)
            {
                history = SearchQueryProjection.Create(savedDraft, savedContext, _environment, batchId, histories);
                histories.Add(history);
            }
        }
        var workspace = new SearchWorkspaceDocument
        {
            CharacterKey = _workspace.CharacterKey, Ascension = _workspace.Ascension,
            Draft = _workspace.Draft, RunDraft = _workspace.RunDraft,
            LegacyPresetQueryKeys = _workspace.LegacyPresetQueryKeys, LegacyQueryAliases = _workspace.LegacyQueryAliases,
            CommittedCursor = _workspace.CommittedCursor, AuxiliaryIssues = _workspace.AuxiliaryIssues,
            QueryFingerprint = queryFingerprint ?? string.Empty, QueryHistory = histories,
            ResultBatchId = batchId, ResultsStartedAtUtc = started,
            ResultDraft = savedDraft, ResultContext = savedContext, ResultsSkipExactValidation = skipExactValidation
        };
        ProjectActiveHistory(workspace, history, newExecution: true);
        return new(workspace, history, PrepareBatch(batchId, history, savedDraft, savedContext, workspace.Results), started);
    }

    internal void BindPreparedResultWorkspace(PreparedSearchResultWorkspace prepared, SearchStartResolution? start = null)
    {
        // No serialization, identity calculation, filesystem work or UI callback
        // occurs before the complete receiving workspace is installed.
        _workspace = prepared.Workspace;
        InstallBatch(prepared.Batch);
        start ??= prepared.Batch.Start?.Resolution;
        if (start is { InitialContinuation: { } initial, ExplicitOriginChangeId.Length: > 0 } &&
            _cursor.PendingOriginChangeId == start.ExplicitOriginChangeId)
        {
            // Consume only the intent frozen by this successfully started run.
            // The cursor copy and receiving workspace are persisted together.
            SetResultContinuation(initial);
            _cursor.PendingOriginChangeId = "";
            MarkCursorDirty();
        }
        _historyRunElapsed = 0;
        if (prepared.History is { } history)
        { history.RunCount++; history.LastUsedAtUtc = prepared.StartedAtUtc; }
        ResultsRevision++; MarkWorkspaceDirty();
        try { TrimQueryHistory(); }
        catch (Exception exception) { RecordPresentationFailure(exception, "QueryHistoryPruning"); }
    }

    internal void SaveResultProgress(SearchProgressSnapshot progress)
    {
        if (ActiveHistory is { } history)
            history.ElapsedSeconds += Math.Max(0, progress.ElapsedSeconds - _historyRunElapsed);
        _historyRunElapsed = progress.ElapsedSeconds;
        _workspace.ResultProgress = progress;
        if (ActiveHistory is { } record) record.Progress = progress;
        MarkWorkspaceDirty();
    }

    public void AppendResult(SearchCandidate candidate, string queryFingerprint, QueryHistoryEntry? targetHistory = null, SearchResultBatch? targetBatch = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var result = SearchResultProjection.FromCandidate(candidate, queryFingerprint ?? string.Empty);
        if ((targetBatch is not null ? targetBatch.Context : _workspace.ResultContext) is { } context)
        {
            try
            {
                result.Context = candidate.IsUnverified ? context : SeedLibraryContextCapture.WithWitness(context, candidate);
            }
            catch (Exception ex)
            {
                RecordPresentationFailure(ex, "ResultContext", candidate.Seed);
                result.ExecutionWarnings = result.ExecutionWarnings.Append(ex.ToString()).ToArray();
            }
        }
        RecordHistoryResult(result, targetHistory, targetBatch);
        RecordLiveResult(candidate, targetBatch);
    }

    public ulong CurrentNextCursorOrdinal => _cursor.NextCursor;
    public ulong CurrentPersistentOriginOrdinal => _cursor.PersistentOrigin;
    public string CurrentNextCursorSeed => Beta110SeedCodec.FormatOrdinal(Math.Min(_cursor.NextCursor, Beta110SeedCodec.SpaceSize - 1UL));
    public string CurrentPersistentOriginSeed => Beta110SeedCodec.FormatOrdinal(Math.Min(_cursor.PersistentOrigin, Beta110SeedCodec.SpaceSize - 1UL));

    public bool TrySetPersistentOrigin(string rawSeed, out string canonicalSeed, out string issue)
    {
        canonicalSeed = string.Empty;
        if (!Beta110SeedCodec.TryParseOrdinal(rawSeed?.Trim() ?? string.Empty, out ulong ordinal, out canonicalSeed, out issue))
            return false;
        if (ordinal >= Beta110SeedCodec.SpaceSize)
        {
            issue = "PersistentOriginOutsideVisibleSeedSpace";
            return false;
        }
        SetPersistentOriginOrdinal(ordinal, "Manual");
        return true;
    }

    public string RandomizePersistentOrigin()
    {
        ulong ordinal = RandomVisibleOrdinal();
        SetPersistentOriginOrdinal(ordinal, "SystemRandom");
        return Beta110SeedCodec.FormatOrdinal(ordinal);
    }

    public void ObserveSafeNextCursor(ulong nextOrdinal)
    {
        if (nextOrdinal >= Beta110SeedCodec.SpaceSize)
            return;
        if (_cursor.NextCursor == nextOrdinal)
            return;
        _cursor.NextCursor = nextOrdinal;
        MarkCursorDirty();
    }

    public void CommitEndOfSpaceWrap()
    {
        _cursor.NextCursor = 0UL;
        _cursor.WrapCount++;
        MarkCursorDirty();
        PersistenceNotice(() => RuntimeLog.Info($"persistentSearchCursorWrap=true;nextCursor=0;wrapCount={_cursor.WrapCount}"));
    }

    public void EnsureEnvironment(SearchEnvironmentSignature current)
    {
        ArgumentNullException.ThrowIfNull(current);
        EnvironmentResetOccurred = false;
        EnvironmentResetReason = string.Empty;
        if (!SearchEnvironmentIdentity.IsComparable(current))
        {
            PersistenceNotice(() => RuntimeLog.Warn($"searchWorkspaceEnvironmentCheckDeferred=true;reason=CurrentSignatureUnavailable;issue={current.CaptureIssue};workspacePreserved=true"));
            // An old successful capture is not evidence about this process.
            SetEnvironment(current);
            return;
        }
        // An unknown historical signature establishes the current capture without
        // invalidating saved state. Only known differences trigger a reset.
        var comparison = SearchEnvironmentIdentity.Compare(_environment, current);
        if (!string.IsNullOrWhiteSpace(_environment.Fingerprint) &&
            comparison.Status == SearchEnvironmentComparisonStatus.Changed && comparison.Reasons.Count > 0)
        {
            EnvironmentResetOccurred = true;
            EnvironmentResetReason = string.Join(",", comparison.Reasons);
            PersistenceNotice(() => RuntimeLog.Warn(
                "searchWorkspaceEnvironmentReset=true;" +
                $"reasons={EnvironmentResetReason};" +
                $"oldGameVersion={_environment.GameVersionIdentity};newGameVersion={current.GameVersionIdentity};" +
                $"oldCatalog={_environment.CatalogFingerprint};newCatalog={current.CatalogFingerprint};" +
                $"oldUnlock={_environment.UnlockFingerprint};newUnlock={current.UnlockFingerprint}"));
            // Historical results belong to the user. Environment changes invalidate
            // reusable query state, not the saved seeds and their original context.
            _workspace = new SearchWorkspaceDocument
            {
                QueryFingerprint = _workspace.QueryFingerprint, Results = _workspace.Results,
                ResultBatchId = _workspace.ResultBatchId, ResultsStartedAtUtc = _workspace.ResultsStartedAtUtc,
                ResultDraft = _workspace.ResultDraft, ResultContext = _workspace.ResultContext,
                ResultsSkipExactValidation = _workspace.ResultsSkipExactValidation, ResultProgress = _workspace.ResultProgress,
                QueryHistory = _workspace.QueryHistory, ActiveHistoryId = _workspace.ActiveHistoryId,
                LegacyPresetQueryKeys = _workspace.LegacyPresetQueryKeys,
                AuxiliaryIssues = _workspace.AuxiliaryIssues, LegacyQueryAliases = _workspace.LegacyQueryAliases, CommittedCursor = _workspace.CommittedCursor,
                ResultContinuation = _workspace.ResultContinuation
            };
            ResultsRevision++;
            MarkWorkspaceDirty();
        }
        SetEnvironment(current);
        if (EnvironmentResetOccurred) FlushWorkspace();
        FlushEnvironment();
    }

    public void Tick(double deltaSeconds)
    {
        double delta = Math.Max(0d, deltaSeconds);
        if (_preferencesDirty && (_preferencesDirtyAge += delta) >= PreferenceDebounceSeconds)
            FlushPreferences();
        if (_workspaceDirty && (_workspaceDirtyAge += delta) >= WorkspaceDebounceSeconds)
            FlushWorkspace();
        if (_predictorContextDirty && (_predictorContextDirtyAge += delta) >= WorkspaceDebounceSeconds)
            FlushPredictorContext();
        if (_cursorDirty && (_cursorDirtyAge += delta) >= CursorCheckpointSeconds)
            FlushCursor();
    }

    public void FlushAll()
    {
        FlushPreferences();
        FlushWorkspace();
        FlushPredictorContext();
        FlushCursor();
        FlushEnvironment();
    }

    private void SetEnvironment(SearchEnvironmentSignature current)
    {
        if (_environment.HasSameStoredContent(current)) return;
        _environment = current.Snapshot();
        _environmentDirty = true;
    }

    private void FlushEnvironment()
    {
        if (_environmentDirty && WriteNow("search_environment.json", _environment))
            _environmentDirty = false;
    }

    public void FlushCursor()
    {
        if (!_cursorDirty) return;
        // Never let the independent cursor mirror outrun results and pending.
        FlushWorkspace();
        if (_workspaceDirty) return;
        FlushDocument(ref _cursorDirty, ref _cursorDirtyAge, "search_cursor.json", _cursor);
    }
    public void FlushWorkspace()
    {
        if (_cursorDirty)
        {
            _workspace.CommittedCursor = CopyCursor(_cursor);
            MarkWorkspaceDirty();
        }
        FlushDocument(ref _workspaceDirty, ref _workspaceDirtyAge, "search_workspace.json", _workspace);
    }
    private static SearchCursorDocument CopyCursor(SearchCursorDocument cursor) => new()
    { Initialized = cursor.Initialized, PersistentOrigin = cursor.PersistentOrigin,
        NextCursor = cursor.NextCursor, WrapCount = cursor.WrapCount,
        PendingOriginChangeId = cursor.PendingOriginChangeId };

    private void SetPersistentOriginOrdinal(ulong ordinal, string source)
    {
        _cursor.Initialized = true;
        _cursor.PersistentOrigin = ordinal;
        _cursor.NextCursor = ordinal;
        _cursor.PendingOriginChangeId = Guid.NewGuid().ToString("N");
        MarkCursorDirty();
        FlushCursor();
        PersistenceNotice(() => RuntimeLog.Info(
            $"persistentSearchOriginChanged=true;source={source};origin={ordinal};seed={Beta110SeedCodec.FormatOrdinal(ordinal)};nextCursor={ordinal}"));
    }

    private void EnsureCursorInitialized()
    {
        if (_cursor.Initialized && _cursor.PersistentOrigin < Beta110SeedCodec.SpaceSize && _cursor.NextCursor < Beta110SeedCodec.SpaceSize)
            return;
        ulong ordinal = RandomVisibleOrdinal();
        _cursor = new SearchCursorDocument
        {
            Initialized = true,
            PersistentOrigin = ordinal,
            NextCursor = ordinal,
            WrapCount = 0
        };
        MarkCursorDirty();
        FlushCursor();
        PersistenceNotice(() => RuntimeLog.Info($"persistentSearchCursorInitialized=true;origin={ordinal};seed={Beta110SeedCodec.FormatOrdinal(ordinal)};randomSource=SystemCryptographicRandom"));
    }

    private static ulong RandomVisibleOrdinal()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        ulong value = BitConverter.ToUInt64(bytes);
        return value % Beta110SeedCodec.SpaceSize;
    }

    private void NormalizeDocuments()
    {
        NormalizePreferences();
        _workspace.SchemaVersion = SchemaVersion;
        _workspace.Results ??= new List<PersistedSearchResult>();
        _workspace.AuxiliaryIssues ??= [];
        foreach (string detail in _auxiliaryFailures)
            if (!_workspace.AuxiliaryIssues.Contains(detail, StringComparer.Ordinal)) _workspace.AuxiliaryIssues.Add(detail);
        NormalizePredictorContext();
        // Pre-cutover SearchDraft embedded run/session controls. They are deliberately
        // no longer semantic draft state. Existing persistent cursor and runtime settings
        // remain authoritative; the UI-local run draft is repopulated on the next save.
        if (_workspace.Draft is not null && _workspace.RunDraft is null)
        {
            PersistenceNotice(() => RuntimeLog.Info("searchWorkspaceRunDraftMigrated=true;legacyEmbeddedRunOptionsDropped=true"));
        }
        // Beta 19.x stored Combat Reward as battle-bound rows. The current Draft
        // owns independent Card/Potion sequences. System.Text.Json can deserialize
        // the old object with null constructor-backed members, so normalize only
        // that local Draft block to Empty instead of letting UI restore fail later.
        if (_workspace.Draft?.CombatRewardDraft is { } rewardDraft &&
            (rewardDraft.Cards is null || rewardDraft.Potions is null))
        {
            _workspace.Draft = _workspace.Draft with { CombatRewardDraft = CombatRewardSearchDraft.Empty };
            PersistenceNotice(() => RuntimeLog.Info("searchWorkspaceCombatRewardDraftMigrated=true;legacyBattleBundleDropped=true"));
        }
        _cursor.SchemaVersion = SchemaVersion;
        _environment.SchemaVersion = SchemaVersion;
    }

    private void MarkWorkspaceDirty() { if (!_workspaceDirty) _workspaceDirtyAge = 0d; _workspaceDirty = true; }
    private void MarkCursorDirty() { if (!_cursorDirty) _cursorDirtyAge = 0d; _cursorDirty = true; }
}
