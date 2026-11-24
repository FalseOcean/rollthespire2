using System.Diagnostics;
using Godot;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class SeedLibraryCanvas
{
    // The view owns only pending presentation. Selection, filters and modal
    // transactions outlive each list/detail incarnation in their existing fields.
    private IEnumerator<bool>? _presentation;
    private readonly List<Button> _idleActions = [];
    private readonly Dictionary<string, Button> _listButtons = new(StringComparer.Ordinal);
    private Button? _activeListButton;
    private bool _refreshPending;
    private bool _buildingList;
    private bool _retired;
    internal bool HasPresentationFailure { get; private set; }
    private long _listVersion;
    private long _presentationVersion;
    private ulong _readyFrame;

    internal bool IsPresentationReady => !_retired && _presentation is null &&
        _readyFrame > 0 && Engine.GetProcessFrames() >= _readyFrame;

    private void RequestPresentation(bool refreshList)
    {
        if (_retired) return;
        refreshList |= _refreshPending || _buildingList;
        _presentation?.Dispose();
        _presentationVersion++;
        if (HasPresentationFailure) HideReceipt();
        HasPresentationFailure = false;
        _readyFrame = 0;
        _refreshPending = refreshList;
        _buildingList = false;
        if (refreshList)
        {
            _listVersion++;
            _entries.Hide();
        }
        _detailHeader.Hide(); _detailScroll.Hide(); _detailFooter.Hide();
        _presentation = Present(refreshList).GetEnumerator();
    }

    private void TickPresentation()
    {
        if (_presentation is null) return;
        // Both an item ceiling and a soft CPU budget limit each actual frame.
        // A row/summary is an indivisible existing widget; subsequent layout is
        // allowed to run before the next batch and before readiness is reported.
        long started = Stopwatch.GetTimestamp();
        try
        {
            for (int count = 0, controls = 0; count < 128 && controls < 4; count++)
            {
                if (!_presentation.MoveNext())
                {
                    _presentation.Dispose(); _presentation = null;
                    _readyFrame = Engine.GetProcessFrames() + 2;
                    return;
                }
                if (_presentation.Current) controls++;
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 3) return;
            }
        }
        catch (Exception ex)
        {
            _presentation?.Dispose(); _presentation = null;
            _refreshPending = _buildingList = false;
            HasPresentationFailure = true;
            RuntimeIssueLedger.Current.Record(ex, "SeedLibraryPresentationFailed", nameof(SeedLibraryCanvas),
                "Present:" + _section, "issues.library_failed", correlationId: _presentationVersion.ToString());
            ShowIssue(T("资料库显示未完成；原始内容已保留。请重新打开此页后重试。",
                "The library view could not finish. Original content is preserved; reopen this page to retry.") + "\n" + ex.Message);
            // A failed view is terminal and exposes its real error; it must not
            // leave Shell's navigation waiting forever behind a loading layer.
            _entries.Show(); _detailHeader.Show(); _detailScroll.Show();
            _readyFrame = Engine.GetProcessFrames() + 2;
        }
    }

    private IEnumerable<bool> Present(bool refreshList)
    {
        _refreshPending = false;
        _buildingList = refreshList;
        if (refreshList)
        {
            _activeListButton = null;
            _listButtons.Clear();
            while (_entries.GetChildCount() > 0)
            {
                Node child = _entries.GetChild(0);
                _entries.RemoveChild(child); child.QueueFree();
                yield return true;
            }
            _entries.Show();
        }
        _idleActions.Clear();
        foreach (var container in new[] { _detail, _detailHeader, _detailFooter })
            while (container.GetChildCount() > 0)
            {
                Node child = container.GetChild(0);
                if (child is SearchConditionSummary summary)
                    foreach (bool step in summary.ClearPresentation()) yield return step;
                container.RemoveChild(child); child.QueueFree();
                yield return true;
            }
        _detailFooter.Hide();
        if (refreshList)
        {
            if (_historyStore is not null) _historyRevision = _historyStore.ResultsRevision;
            IEnumerable<bool> rows = _section switch
            {
                LibrarySection.History when _historyStore is not null => PresentHistoryList(),
                LibrarySection.Presets when _historyStore is not null => PresentPresetList(),
                LibrarySection.CardCollections => PresentCardCollectionList(),
                _ => PresentBookmarkList()
            };
            foreach (bool step in rows) yield return step;
        }
        _buildingList = false;
        UpdateSelectedRow();
        _detailHeader.Show(); _detailScroll.Show();
        if (_section == LibrarySection.History && _historyStore is not null)
        {
            var conditions = RenderQueryHistory();
            yield return true;
            if (conditions is not null)
                foreach (bool step in AddConditions(conditions)) yield return step;
            else if (_historyResults && (_visibleQueries.Any(q => q.Id == _selectedHistory) ||
                _visibleLegacyQueries.Any(p => p.Id == _selectedHistory)))
                foreach (bool step in RenderHistoryResults(_visibleQueries.FirstOrDefault(q => q.Id == _selectedHistory)))
                    yield return step;
        }
        else if (_section == LibrarySection.Presets && _historyStore is not null)
        {
            var conditions = RenderPresets();
            yield return true;
            if (conditions is not null)
                foreach (bool step in AddConditions(conditions)) yield return step;
        }
        else if (_section == LibrarySection.CardCollections)
        {
            foreach (bool step in PresentCardCollectionDetail()) yield return step;
        }
        else RenderBookmarks(_visibleBookmarks);
    }

    private IEnumerable<bool> PresentBookmarkList()
    {
        var entries = new List<SeedLibraryEntry>();
        foreach (var entry in _store.GetAll())
        {
            if (Matches(entry.TitleFor(_language) + " " + entry.Seed + " " + entry.NoteFor(_language) + " " +
                (entry.DeveloperDetails?.Instructions.Resolve(_language) ?? "") + " " +
                (entry.Context is null ? "" : FormatContext(entry.Context)))) entries.Add(entry);
            yield return false;
        }
        _visibleBookmarks = entries;
        if (!entries.Any(e => e.Id == _selectedSeed))
            _selectedSeed = entries.FirstOrDefault(e => e.Source == SeedLibrarySource.User)?.Id ?? entries.FirstOrDefault()?.Id ?? "";
        if (_store.LoadIssues.Count > 0)
        {
            _entries.AddChild(Text(T("部分资料库文件无法读取，原文件已保留。请使用兼容版本后重试。",
                "Some library files could not be read. The originals are preserved; retry with a compatible version."), 17, true));
            yield return true;
        }
        foreach (var source in new[] { SeedLibrarySource.User, SeedLibrarySource.Developer })
        {
            bool heading = false;
            foreach (var entry in entries)
            {
                if (entry.Source != source) continue;
                if (!heading)
                {
                    _entries.AddChild(Text(source == SeedLibrarySource.User ? T("我的收藏", "My favorites") : T("开发者推荐", "Developer picks"), 16, true));
                    heading = true;
                    yield return true;
                }
                AddListItem(entry.Id, entry.TitleFor(_language), (entry.TitleFor(_language) != entry.Seed ? entry.Seed + " · " : "") +
                    (entry.Context is { } c ? FormatContext(c) : T("上下文不可用", "Context unavailable")), _selectedSeed == entry.Id,
                    () => { _selectedSeed = entry.Id; SelectDetail(); }, entry.DeveloperDetails?.Icons);
                yield return true;
            }
        }
    }

    private IEnumerable<bool> PresentPresetList()
    {
        var presets = new List<SearchPresetDefinition>();
        foreach (var preset in _historyStore!.Presets.GetAll())
        {
            if (preset.Source != SearchPresetSource.Temporary && Matches(preset.SearchText + " " + PresetContext(preset))) presets.Add(preset);
            yield return false;
        }
        _visiblePresets = presets.ToArray();
        if (!presets.Any(p => p.Id == _selectedPreset))
            _selectedPreset = presets.FirstOrDefault(p => p.Source == SearchPresetSource.User)?.Id ?? presets.FirstOrDefault()?.Id ?? "";
        foreach (var source in new[] { SearchPresetSource.User, SearchPresetSource.BuiltIn })
        {
            bool heading = false;
            foreach (var preset in presets)
            {
                if (preset.Source != source) continue;
                if (!heading)
                {
                    _entries.AddChild(Text(source == SearchPresetSource.User ? T("我的预设", "My presets") : T("开发者预设", "Developer presets"), 16, true));
                    heading = true;
                    yield return true;
                }
                AddListItem(preset.Id, preset.TitleFor(_language), PresetContext(preset), _selectedPreset == preset.Id,
                    () => { _selectedPreset = preset.Id; SelectDetail(); }, preset.VisualIcons);
                yield return true;
            }
        }
        if (_historyStore.Presets.LoadIssues.Count > 0)
            ShowIssue(T("部分预设无法读取，原文件已保留。", "Some presets could not be read; original files are preserved."));
    }

    private IEnumerable<bool> PresentHistoryList()
    {
        if (_historyStore!.QueryHistoryIssue.Length > 0)
            ShowIssue(T("部分历史无法读取，原文件已保留。", "Some history could not be read; original files are preserved."));
        IEnumerable<QueryHistoryEntry> records = _sourceHistoryId.Length > 0
            ? _historyStore.Workspace.QueryHistory.Where(q => q.Id == _sourceHistoryId).ToArray()
            : _queryFilter is null ? _historyStore.RecentQueries : _historyStore.QueriesForPreset(_queryFilter);
        var queries = new List<QueryHistoryEntry>();
        foreach (var query in records)
        {
            if (string.IsNullOrWhiteSpace(_filter.Text) || Matches(QueryTitle(query) + " " + QueryContext(query) + " " +
                string.Join(" ", _historyStore.PresetsForQuery(query.QueryKey).Select(p => p.SearchText)))) queries.Add(query);
            yield return false;
        }
        var legacy = new List<SearchPresetDefinition>();
        if (_queryFilter is null && _sourceHistoryId.Length == 0)
            foreach (var preset in _historyStore.Presets.GetAll())
            {
                if (preset.Source == SearchPresetSource.Temporary && Matches(PresetContext(preset))) legacy.Add(preset);
                yield return false;
            }
        _visibleQueries = queries.ToArray(); _visibleLegacyQueries = legacy.ToArray();
        if (!queries.Any(q => q.Id == _selectedHistory) && !legacy.Any(p => p.Id == _selectedHistory))
            _selectedHistory = queries.FirstOrDefault()?.Id ?? legacy.FirstOrDefault()?.Id ?? "";
        foreach (var query in queries)
        {
            AddListItem(query.Id, QueryTitle(query), QueryContext(query) + T($" · {query.Results.Count} 个结果", $" · {query.Results.Count} results"), _selectedHistory == query.Id,
                () => { _selectedHistory = query.Id; SelectDetail(); });
            yield return true;
        }
        if (legacy.Count > 0)
        {
            _entries.AddChild(Text(T("旧版记录", "Legacy records"), 16, true));
            yield return true;
            foreach (var preset in legacy)
            {
                AddListItem(preset.Id, preset.CreatedAtUtc.ToLocalTime().ToString("MM-dd HH:mm") + " · " + _names.Resolve(preset.CharacterKey, GameContentKind.Character),
                    T("仅保存了条件", "Conditions only"), _selectedHistory == preset.Id,
                    () => { _selectedHistory = preset.Id; SelectDetail(); });
                yield return true;
            }
        }
    }

    private void UpdateSelectedRow()
    {
        string selected = _section switch { LibrarySection.History => _selectedHistory,
            LibrarySection.Presets => _selectedPreset, LibrarySection.CardCollections => _selectedCollection, _ => _selectedSeed };
        if (_activeListButton is { } previous && IsInstanceValid(previous))
        { previous.SetMeta("selected", false); ApplyListItemState(previous, false); }
        // Detail changes keep list instances and touch only old/new selection.
        _activeListButton = _listButtons.GetValueOrDefault(selected);
        if (_activeListButton is { } current)
        { current.SetMeta("selected", true); ApplyListItemState(current, true); }
    }

    public override void _ExitTree()
    {
        _retired = true;
        _presentation?.Dispose(); _presentation = null;
        _idleActions.Clear(); _listButtons.Clear(); _activeListButton = null;
        _visibleBookmarks = []; _visiblePresets = []; _visibleQueries = []; _visibleLegacyQueries = [];
        _saveInformation = null; _confirmed = null; _returnFocus = null; _queryFilter = null;
        SearchBusy = null;
        // These are siblings in Shell so that they cover the whole workspace,
        // but their transaction and event ownership belongs to this view.
        if (_note is { } note && IsInstanceValid(note)) note.QueueFree();
        if (_confirmation is { } confirmation && IsInstanceValid(confirmation)) confirmation.QueueFree();
        if (_collectionEditor is { } editor && IsInstanceValid(editor)) editor.QueueFree();
        _collectionEditor = null;
        CardCollectionReturnRequested = null;
        _note = null; _confirmation = null;
        OpenRequested = null; ModalChanged = null; ReceiptShown = null;
        PresetUseRequested = null; PresetHistoryRequested = null; PresetEditRequested = null; PresetEnvironmentRequested = null;
        QueryLoadRequested = null; ResultOpenRequested = null;
    }
}
