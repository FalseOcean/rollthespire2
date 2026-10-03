using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class SeedLibraryCanvas
{
    internal enum LibrarySection { Favorites, Presets, History }
    private readonly SearchWorkspacePersistence? _historyStore;
    private LibrarySection _section;
    private SearchPresetDefinition? _queryFilter;
    private string _sourceHistoryId = "";
    private string _selectedHistory = "";
    private bool _historyResults;
    private double _historyRefreshAge;
    private long _historyRevision = -1;
    private readonly Dictionary<LibrarySection, Button> _tabs = [];
    private Label? _scope;
    private Button? _clearScope;
    internal LibrarySection Section => _section;
    internal Func<bool>? SearchBusy { get; set; }
    public event Action<QueryHistoryEntry, string>? QueryLoadRequested;
    public event Action<PersistedSearchResult>? ResultOpenRequested;

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _historyStore is null || _note?.IsOpen == true || _confirmation?.IsOpen == true) return;
        _historyRefreshAge += delta;
        if (_historyRefreshAge < 1) return;
        _historyRefreshAge = 0;
        bool busy = SearchBusy?.Invoke() == true;
        foreach (var button in FindChildren("*", "Button", true, false).OfType<Button>())
            if (button.HasMeta("requires_idle")) button.Disabled = busy || button.GetMeta("unavailable").AsBool();
        if (_section != LibrarySection.History || _historyRevision == _historyStore.ResultsRevision) return;
        _historyRevision = _historyStore.ResultsRevision;
        RefreshEntries();
    }

    private void BuildHistoryToolbar(VBoxContainer body)
    {
        var row = new HBoxContainer { Name = "SeedLibraryTabs" };
        row.AddThemeConstantOverride("separation", 12); body.AddChild(row);
        foreach (var section in Enum.GetValues<LibrarySection>())
        {
            var button = AddAction(row, "", () => ShowSection(section));
            button.Name = "SeedLibrary" + section;
            button.Visible = section == LibrarySection.Favorites || _historyStore is not null;
            _tabs[section] = button;
        }
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _scope = Text("", 16, true); row.AddChild(_scope);
        _clearScope = AddAction(row, "", () => { _queryFilter = null; _sourceHistoryId = ""; _selectedHistory = ""; SelectDetail(); });
        _clearScope.Name = "ClearHistoryFilter";
    }

    internal void ShowSection(LibrarySection section)
    {
        if (_section == section) { RefreshEntries(); return; }
        _section = section; _filter.Text = "";
        if (section != LibrarySection.History) { _queryFilter = null; _sourceHistoryId = ""; }
        SelectDetail();
    }

    private void RefreshHistoryToolbar()
    {
        _tabs[LibrarySection.Favorites].Text = T("种子收藏", "Favorite seeds");
        _tabs[LibrarySection.Presets].Text = T("预设搜索", "Search presets");
        _tabs[LibrarySection.History].Text = T("历史搜索", "Search history");
        foreach (var (section, button) in _tabs) { button.Disabled = false; _palette.SetActive(button, section == _section); }
        _description.Text = _section switch
        {
            LibrarySection.Presets => T("保存常用条件，随时使用或查看对应的历史结果。", "Keep useful queries, reuse them and revisit their results."),
            LibrarySection.History => T("选中一条记录，查看当时的条件和找到的种子。", "Select a search to view its conditions and results."),
            _ => T("保存自己的种子，或从开发者推荐中挑一颗开始。", "Keep your own seeds, or try one of the developer picks.")
        };
        _filter.PlaceholderText = _section == LibrarySection.Favorites ? T("查找标题、种子或备注", "Find a title, seed or note") : T("查找名称或角色", "Find a name or character");
        bool filtered = _section == LibrarySection.History && (_queryFilter is not null || _sourceHistoryId.Length > 0);
        _scope!.Visible = _clearScope!.Visible = filtered;
        _scope.Text = filtered ? _queryFilter?.Title ?? T("来源记录", "Source search") : "";
        _clearScope.Text = T("全部历史", "All history");
    }

    internal void ShowPresetResults(SearchPresetDefinition preset)
    {
        _section = LibrarySection.History; _queryFilter = preset; _sourceHistoryId = "";
        _selectedHistory = ""; _historyResults = true; _filter.Text = ""; SelectDetail();
    }

    private string QueryTitle(QueryHistoryEntry query)
    {
        var presets = _historyStore!.PresetsForQuery(query.QueryKey);
        if (presets.Count > 0) return string.Join(" / ", presets.Select(p => p.Title));
        string characters = query.Draft.Players.Count > 0
            ? string.Join(" / ", query.Draft.Players.Select(p => _names.Resolve(p.Character, GameContentKind.Character)))
            : _names.Resolve(query.Draft.Character, GameContentKind.Character);
        return characters + $" · A{query.Draft.Ascension}";
    }
    private string QueryContext(QueryHistoryEntry query) =>
        (query.Draft.Players.Count > 0 ? T($"{query.Draft.Players.Count} 人", $"{query.Draft.Players.Count} players") : T("单人", "Solo")) +
        $" · A{query.Draft.Ascension} · {query.LastUsedAtUtc.ToLocalTime():MM-dd HH:mm}";

    private void RenderQueryHistory()
    {
        if (_historyStore!.QueryHistoryIssue.Length > 0) ShowIssue(T("部分历史无法读取，原文件已保留。", "Some history could not be read; original files are preserved."));
        IEnumerable<QueryHistoryEntry> records = _sourceHistoryId.Length > 0
            ? _historyStore.Workspace.QueryHistory.Where(q => q.Id == _sourceHistoryId)
            : _queryFilter is null ? _historyStore.RecentQueries : _historyStore.QueriesForPreset(_queryFilter);
        var queries = records
            .Where(q => Matches(QueryTitle(q) + " " + QueryContext(q))).ToArray();
        // Preserve older condition-only records without inventing runs or results.
        var legacy = _queryFilter is null && _sourceHistoryId.Length == 0 ? _historyStore.Presets.GetAll().Where(p => p.Source == SearchPresetSource.Temporary && Matches(PresetContext(p))).ToArray() : [];
        if (!queries.Any(q => q.Id == _selectedHistory) && !legacy.Any(p => p.Id == _selectedHistory))
            _selectedHistory = queries.FirstOrDefault()?.Id ?? legacy.FirstOrDefault()?.Id ?? "";
        foreach (var query in queries)
            AddListItem(query.Id, QueryTitle(query), QueryContext(query) + T($" · {query.Results.Count} 个结果", $" · {query.Results.Count} results"), _selectedHistory == query.Id,
                () => { _selectedHistory = query.Id; SelectDetail(); });
        if (legacy.Length > 0)
        {
            _entries.AddChild(Text(T("旧版记录", "Legacy records"), 16, true));
            foreach (var preset in legacy)
                AddListItem(preset.Id, preset.CreatedAtUtc.ToLocalTime().ToString("MM-dd HH:mm") + " · " + _names.Resolve(preset.CharacterKey, GameContentKind.Character),
                    T("仅保存了条件", "Conditions only"), _selectedHistory == preset.Id,
                    () => { _selectedHistory = preset.Id; SelectDetail(); });
        }
        var selected = queries.FirstOrDefault(q => q.Id == _selectedHistory);
        var old = legacy.FirstOrDefault(p => p.Id == _selectedHistory);
        if (selected is null && old is null) { EmptyDetail(T("尚无对应的搜索记录。", "No matching search history yet.")); return; }
        var definition = selected is null ? old! : selected.AsPreset(QueryTitle(selected));
        var primary = DetailHeading(selected is null ? T("旧版搜索条件", "Legacy search conditions") : QueryTitle(selected),
            selected is null ? PresetContext(definition) : QueryContext(selected));
        var load = AddAction(primary, T("重载条件", "Load conditions"), () =>
        {
            if (selected is not null) QueryLoadRequested?.Invoke(selected, QueryTitle(selected));
            else PresetUseRequested?.Invoke(definition);
        }, true); load.Name = "ReloadHistory";
        bool canUse = CanUse(definition, out string issue); RequireIdle(load, !canUse);
        var actions = Actions(); actions.Name = "HistoryViewTabs";
        var conditions = AddAction(actions, T("查看条件", "Conditions"), () => { _historyResults = false; SelectDetail(); });
        conditions.Name = "HistoryConditions"; _palette.SetActive(conditions, !_historyResults);
        var results = AddAction(actions, T($"结果（{selected?.Results.Count ?? 0}）", $"Results ({selected?.Results.Count ?? 0})"), () => { _historyResults = true; SelectDetail(); });
        results.Name = "HistoryResults"; _palette.SetActive(results, _historyResults);
        if (!canUse) _detail.AddChild(Text(issue, 17, true));
        if (selected is not null) AddEnvironmentNotice(selected.RuntimeEnvironmentFingerprint);
        if (_historyResults) RenderHistoryResults(selected);
        else AddConditions(definition);
    }

    private void RenderHistoryResults(QueryHistoryEntry? query)
    {
        if (query is null || query.Results.Count == 0)
        { _detail.AddChild(Text(T("这条记录没有保存的结果。", "This record has no saved results."), 18, true)); return; }
        foreach (var result in query.Results.AsEnumerable().Reverse())
        {
            var row = new HBoxContainer { Name = "HistoryResultRow" + result.Seed }; row.AddThemeConstantOverride("separation", 10); _detail.AddChild(row);
            var seed = Text(result.Seed + (result.IsUnverified ? T(" · 未校验", " · Unverified") : ""), 19); seed.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(seed);
            AddAction(row, T("复制", "Copy"), () => DisplayServer.ClipboardSet(result.Seed));
            var predict = AddAction(row, T("进行预测", "Predict"), () => ResultOpenRequested?.Invoke(result)); predict.Disabled = result.Context is null;
            var favorite = AddAction(row, T("收藏", "Favorite"), () => OpenSave(result.Seed, result.Context!, queryKey: query.QueryKey, association: _historyStore!.ResultAssociation(result.Seed, query)));
            favorite.Disabled = result.Context is null || result.IsUnverified;
        }
    }

    private void AddBookmarkSource(SeedLibraryEntry entry)
    {
        if (_historyStore is null) return;
        var presets = entry.QueryKeys.SelectMany(_historyStore.PresetsForQuery).DistinctBy(p => p.Id).ToArray();
        if (presets.Length > 0) _detail.AddChild(Text(T("关联预设 · ", "Linked presets · ") + string.Join(" / ", presets.Select(p => p.Title)), 17, true));
        var source = _historyStore.Workspace.QueryHistory.FirstOrDefault(q => entry.QueryAssociations.Any(a => a.SearchRecordId == q.Id));
        if (source is not null)
        {
            var go = AddAction(Actions(), T("查看来源记录", "View source search"), () =>
            { _section = LibrarySection.History; _queryFilter = null; _sourceHistoryId = source.Id; _selectedHistory = source.Id; _historyResults = true; _filter.Text = ""; SelectDetail(); });
            go.Name = "ViewFavoriteSource";
        }
        else if (entry.QueryAssociations.Count > 0) _detail.AddChild(Text(T("原搜索记录已不在近期历史中，收藏仍然保留。", "The original search is no longer in recent history. This favorite is retained."), 16, true));
    }

    private void AddEnvironmentNotice(string fingerprint)
    {
        string current = _historyStore?.CurrentEnvironmentFingerprint ?? "";
        if (fingerprint.Length > 0 && current == fingerprint) return;
        var notice = Text(fingerprint.Length == 0 || current.Length == 0
            ? T("暂无法确认环境是否变化。", "Environment changes cannot be confirmed.")
            : T("环境已变化。", "The environment has changed."), 16, true);
        notice.Name = "LibraryEnvironmentNotice"; _detail.AddChild(notice);
    }

    private void RequireIdle(Button button, bool unavailable = false)
    {
        button.SetMeta("requires_idle", true); button.SetMeta("unavailable", unavailable);
        button.Disabled = unavailable || SearchBusy?.Invoke() == true;
    }
    private void AddConditions(SearchPresetDefinition preset)
    {
        var summary = new SearchConditionSummary { Name = "LibraryConditionSummary" };
        _detail.AddChild(summary); summary.Bind(preset, _language, _names);
    }
}
