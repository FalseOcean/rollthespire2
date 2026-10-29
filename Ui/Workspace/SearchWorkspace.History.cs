using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Search.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Workspace;

internal sealed partial class SearchWorkspace
{
    internal const int ResultsPerQuery = 30;
    // Current-process display may exceed the disk quota when the search target does.
    private double _historyRunElapsed;
    internal string QueryHistoryIssue { get; private set; } = "";
    internal IReadOnlyList<PersistedSearchResult> DisplayResults => _resultBatch?.Results ?? _workspace.Results;
    internal string CurrentEnvironmentFingerprint => SearchEnvironmentIdentity.IsComparable(_environment) ? _environment.Fingerprint : "";
    internal SearchEnvironmentComparison EnvironmentComparison(QueryHistoryEntry? record) =>
        SearchEnvironmentIdentity.Compare(record?.EnvironmentSignature ?? (record is null ? null :
            new SearchEnvironmentSignature(0) { GameVersionIdentity = record.Context?.GameVersion ?? record.Draft.GameVersion }), _environment);
    internal SeedQueryAssociation? ResultAssociation(string seed, QueryHistoryEntry? record = null)
    {
        record ??= ActiveHistory;
        if (record is null) return null;
        var result = record.Results.FirstOrDefault(r => r.Seed == seed) ??
            (record == ActiveHistory ? DisplayResults.FirstOrDefault(r => r.Seed == seed) : null);
        if (result is null) return null;
        return AssociationFor(record, result);
    }
    private static SeedQueryAssociation AssociationFor(QueryHistoryEntry record, PersistedSearchResult result) =>
        new(record.QueryKey, result.IsUnverified || result.EffectiveValidationDisposition != SearchDisposition.Match
            ? SeedQueryAssociationKind.UnverifiedFromSearch
            : record.IdentityVersion == 2 && record.EnvironmentKnown
                ? SeedQueryAssociationKind.VerifiedFromSearch : SeedQueryAssociationKind.LegacyUnspecified,
            record.EnvironmentFingerprint, record.Id);
    private QueryHistoryEntry? ActiveHistory => _workspace.QueryHistory.FirstOrDefault(q => q.Id == _workspace.ActiveHistoryId);

    internal IReadOnlyList<QueryHistoryEntry> RecentQueries => _workspace.QueryHistory.Where(q => q.RunCount > 0)
        .OrderByDescending(q => q.LastUsedAtUtc).Take(_preferences.QueryHistoryLimit).ToArray();
    // Preset stores freeze intent and replace definitions on edit. Share identity
    // by that frozen Workbench instance, so metadata/environment edits retain it.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorkbenchSearchDraft, PresetQueryIdentity> _presetKeys = new();
    private sealed record PresetQueryIdentity(string Key);
    private IReadOnlyList<SearchPresetDefinition>? _indexedPresets;
    private Dictionary<string, IReadOnlyList<SearchPresetDefinition>> _presetsByQuery = new(StringComparer.Ordinal);
    internal string QueryKeyForPreset(SearchPresetDefinition preset) => preset.Workbench is { } draft
        ? _presetKeys.GetValue(draft, value => new(QueryHistoryIdentity.Key(value))).Key : "";
    internal string NormalizeQueryKey(string key) => _workspace.LegacyQueryAliases.GetValueOrDefault(key, key);
    internal IReadOnlyList<QueryHistoryEntry> QueriesForPreset(SearchPresetDefinition preset)
    {
        string key = QueryKeyForPreset(preset);
        return _workspace.QueryHistory.Where(q => q.QueryKey == key).OrderByDescending(q => q.LastUsedAtUtc).ToArray();
    }
    internal IReadOnlyList<SearchPresetDefinition> PresetsForQuery(string key)
    {
        var all = _presets.GetAll();
        if (!ReferenceEquals(_indexedPresets, all))
        {
            _presetsByQuery = all.Where(p => p.Source == SearchPresetSource.User)
                .GroupBy(QueryKeyForPreset, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<SearchPresetDefinition>)Array.AsReadOnly(g.ToArray()), StringComparer.Ordinal);
            _indexedPresets = all;
        }
        return _presetsByQuery.GetValueOrDefault(NormalizeQueryKey(key)) ?? [];
    }

    internal void SetQueryHistoryLimit(int count)
    {
        _preferences.QueryHistoryLimit = Math.Clamp(count, 1, 300);
        TrimQueryHistory(); MarkPreferencesDirty(); FlushPreferences(); FlushWorkspace();
    }

    internal QueryHistoryEntry EnsureQuery(WorkbenchSearchDraft draft, SeedLibraryContext? context)
    {
        string key = QueryHistoryIdentity.Key(draft);
        bool known = SearchEnvironmentIdentity.IsComparable(_environment);
        string environment = known
            ? QueryHistoryIdentity.EnvironmentKey(draft, _environment.Fingerprint)
            : "unknown:" + _workspace.ResultBatchId;
        var record = SearchQueryProjection.Find(_workspace.QueryHistory, key, environment, _environment, known);
        if (record is not null) return record;
        record = SearchQueryProjection.Create(FreezeDraft(draft), context is null ? null : SeedLibraryStore.CloneContext(context),
            _environment, _workspace.ResultBatchId, _workspace.QueryHistory);
        _workspace.QueryHistory.Add(record); MarkWorkspaceDirty();
        return record;
    }

    internal void PruneAndFlushQueryHistory()
    {
        TrimQueryHistory(); FlushWorkspace();
    }

    private QueryHistoryEntry? FindCurrentQuery(WorkbenchSearchDraft draft)
    {
        if (!SearchEnvironmentIdentity.IsComparable(_environment)) return null;
        string key = QueryHistoryIdentity.Key(draft), environment = QueryHistoryIdentity.EnvironmentKey(draft, _environment.Fingerprint);
        return SearchQueryProjection.Find(_workspace.QueryHistory.Where(record => record.IdentityVersion == 2), key, environment, _environment, known: true);
    }

    internal int ResultCountForQuery(WorkbenchSearchDraft draft)
    {
        var record = FindCurrentQuery(draft);
        return record is null ? 0 : _processBatches.TryGetValue(record.Id, out var batch) ? batch.Results.Count : record.Results.Count;
    }

    internal void ActivateQueryResults(WorkbenchSearchDraft draft)
    {
        if (Runtime.HasSearchInFlight) return;
        var record = FindCurrentQuery(draft);
        string selection = record?.Id ?? "empty:" + QueryHistoryIdentity.RecordId(QueryHistoryIdentity.Key(draft),
            QueryHistoryIdentity.EnvironmentKey(draft, _environment.Fingerprint));
        if (_workspace.ResultBatchId == selection || record is not null && record.Id == _workspace.ActiveHistoryId) return;
        ProjectActiveHistory(_workspace, record);
        _workspace.ResultBatchId = selection;
        if (record is not null && _processBatches.TryGetValue(record.Id, out var batch)) InstallBatch(batch);
        else InstallBatch(new(selection, record?.Id ?? "", record?.Draft ?? draft.ToPresetIntent(), record?.Context, _workspace.Results));
        _workspace.ResultDraft = record?.Draft ?? draft.ToPresetIntent();
        _workspace.ResultContext = record?.Context;
        _workspace.ResultsSkipExactValidation = _workspace.Results.Any(r => r.IsUnverified);
        ResultsRevision++; MarkWorkspaceDirty(); FlushWorkspace();
    }

    internal void TrimQueryHistory()
    {
        var userPresets = _presets.GetAll().Where(p => p.Source == SearchPresetSource.User).ToArray();
        var pinned = userPresets.Select(QueryKeyForPreset).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        // Historical preset links remain archived data, not current intent proof.
        var recent = RecentQueries.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        _workspace.QueryHistory.RemoveAll(q => !recent.Contains(q.Id) && !pinned.Contains(q.QueryKey) && q.Id != _workspace.ActiveHistoryId && q.Continuation?.PendingCandidates.Count is not > 0 && !q.RetainedContinuations.Any(c => c.PendingCandidates.Count > 0) &&
            !q.ManualValidationCandidates.Any(c => c.Continuation.PendingCandidates.Count > 0) &&
            q.Continuation?.UnreceivedOrdinals?.Count is not > 0 && !q.RetainedContinuations.Any(c => c.UnreceivedOrdinals?.Count > 0));
        foreach (var query in _workspace.QueryHistory)
            if (query.Results.Count > ResultsPerQuery) query.Results.RemoveRange(0, query.Results.Count - ResultsPerQuery);
        MarkWorkspaceDirty();
    }

    private void RecordHistoryResult(PersistedSearchResult result, QueryHistoryEntry? targetHistory = null, SearchResultBatch? targetBatch = null)
    {
        var history = targetBatch is not null ? targetHistory : ActiveHistory;
        if (history is { } record)
        {
            if (!_workspace.QueryHistory.Any(entry => entry.Id == record.Id)) _workspace.QueryHistory.Add(record);
            result.SourceAssociation = AssociationFor(record, result);
            Accumulate(record.Results, result);
            if (record.Results.Count > ResultsPerQuery) record.Results.RemoveAt(0);
            if (ActiveHistory?.Id == record.Id) _workspace.Results = record.Results.ToList();
        }
        else if (targetBatch is null || ReferenceEquals(targetBatch, _resultBatch))
        {
            Accumulate(_workspace.Results, result);
            if (_workspace.Results.Count > ResultsPerQuery) _workspace.Results.RemoveAt(0);
        }
        if (_resultBatch is null) InstallBatch(new(_workspace.ResultBatchId, _workspace.ActiveHistoryId,
            _workspace.ResultDraft, _workspace.ResultContext, _workspace.Results));
        Accumulate((targetBatch ?? _resultBatch)!.Results, result);
        ResultsRevision++; MarkWorkspaceDirty();
    }

    private static void Accumulate(List<PersistedSearchResult> rows, PersistedSearchResult result)
    {
        var previous = rows.FirstOrDefault(r => r.Seed == result.Seed);
        if (previous is not null)
        {
            rows.Remove(previous);
            // A later unchecked candidate cannot downgrade an already verified seed.
            if (!previous.IsUnverified && result.IsUnverified) result = previous;
        }
        rows.Add(result);
    }

    private void NormalizeQueryHistory()
    {
        _preferences.QueryHistoryLimit = Math.Clamp(_preferences.QueryHistoryLimit, 1, 300);
        _workspace.QueryHistory ??= [];
        _workspace.LegacyPresetQueryKeys ??= [];
        _workspace.LegacyQueryAliases ??= [];
        foreach (var record in _workspace.QueryHistory)
        {
            if (record?.Draft is null || record.Draft.Version is not (1 or 2 or 3 or 4 or 5) || string.IsNullOrEmpty(record.Id) || record.IdentityVersion > 2)
                throw new InvalidDataException("QueryHistory.UnsupportedRecord");
            SearchPresetCompatibilityResolver.ValidateWorkbenchShape(record.Draft);
            record.Results ??= [];
            record.MapScans ??= [];
            record.RetainedContinuations ??= [];
            record.ManualValidationSources ??= [];
            record.ManualValidationCandidates ??= [];
            if (record.IdentityVersion < 2)
            {
                // Historical environment evidence is retained, never assigned today's authority.
                string intentKey = QueryHistoryIdentity.Key(record.Draft);
                _workspace.LegacyQueryAliases[record.QueryKey] = intentKey;
                record.QueryKey = intentKey;
                record.IdentityVersion = 1;
            }
            // Older rows can inherit only their actual containing record's
            // provenance, never whichever query happens to be active now.
            foreach (var result in record.Results)
                result.SourceAssociation ??= AssociationFor(record, result);
        }
        if (_workspace.QueryHistory.Count == 0 && _workspace.ResultDraft is { Version: < 5 } draft &&
            !_workspace.ResultBatchId.StartsWith("empty:", StringComparison.Ordinal) &&
            (_workspace.ResultsStartedAtUtc is not null || _workspace.Results.Count > 0))
        {
            var migrated = EnsureQuery(draft, _workspace.ResultContext);
            migrated.IdentityVersion = 1;
            migrated.EnvironmentKnown = false;
            migrated.EnvironmentFingerprint = "";
            migrated.RuntimeEnvironmentFingerprint = "";
            migrated.EnvironmentSignature = null;
            migrated.Id = "legacy:" + migrated.Id;
            migrated.RunCount = 1;
            migrated.LastUsedAtUtc = _workspace.ResultsStartedAtUtc ?? DateTimeOffset.UtcNow;
            migrated.ElapsedSeconds = _workspace.ResultProgress?.ElapsedSeconds ?? 0;
            migrated.Progress = _workspace.ResultProgress;
            migrated.Continuation = _workspace.ResultContinuation;
            foreach (var result in _workspace.Results) Accumulate(migrated.Results, result);
            _workspace.ActiveHistoryId = migrated.Id;
        }
        TrimQueryHistory();
        if (ActiveHistory is { } active) ProjectActiveHistory(_workspace, active);
        else if (_workspace.Results.Count > ResultsPerQuery)
            _workspace.Results = _workspace.Results.TakeLast(ResultsPerQuery).ToList();
    }

    private void LoadQueryHistory()
    {
        try { NormalizeQueryHistory(); }
        catch (Exception ex)
        {
            // Protect the original file just like unreadable workbench/preset
            // documents. Current-process edits must not overwrite unknown intent.
            QueryHistoryIssue = ex.Message;
            _unreadableDocuments.Add("search_workspace.json");
            _workspace.QueryHistory = [];
            _workspace.ActiveHistoryId = "";
            RecordPresentationFailure(ex, "QueryHistoryReadProtected");
        }
    }
}
