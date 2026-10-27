using RolltheSpire2.Ui.Workspace;
using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class SeedLibraryCanvas
{
    internal enum LibrarySection { Favorites, Presets, History, CardCollections }
    private readonly SearchWorkspace? _historyStore;
    private LibrarySection _section;
    private SearchPresetDefinition? _queryFilter;
    private string _sourceHistoryId = "";
    private string _selectedHistory = "";
    private bool _historyResults;
    private double _historyRefreshAge;
    private long _historyRevision = -1;
    private QueryHistoryEntry[] _visibleQueries = [];
    private SearchPresetDefinition[] _visibleLegacyQueries = [];
    private readonly Dictionary<LibrarySection, Button> _tabs = [];
    private Label? _scope;
    private Button? _clearScope;
    internal LibrarySection Section => _section;
    internal Func<bool>? SearchBusy { get; set; }
    public event Action<QueryHistoryEntry, string>? QueryLoadRequested;
    public event Action<PersistedSearchResult>? ResultOpenRequested;

    public override void _Process(double delta)
    {
        if (_retired || !IsVisibleInTree() || _note?.IsOpen == true || _confirmation?.IsOpen == true || _collectionEditor?.IsOpen == true) return;
        TickPresentation();
        if (_historyStore is null) return;
        _historyRefreshAge += delta;
        if (_historyRefreshAge < 1) return;
        _historyRefreshAge = 0;
        bool busy = SearchBusy?.Invoke() == true;
        foreach (var button in _idleActions)
            button.Disabled = busy || button.GetMeta("unavailable").AsBool();
        if (_section != LibrarySection.History || !IsPresentationReady || _historyRevision == _historyStore.ResultsRevision) return;
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
            button.Visible = section == LibrarySection.CardCollections ? _cardCollections is not null
                : section == LibrarySection.Favorites || _historyStore is not null;
            _tabs[section] = button;
        }
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _scope = Text("", 16, true);
        _scope.AutowrapMode = TextServer.AutowrapMode.Off; _scope.MaxLinesVisible = 1;
        _scope.ClipText = true; _scope.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.AddChild(_scope);
        _clearScope = AddAction(row, "", () => { _queryFilter = null; _sourceHistoryId = ""; _selectedHistory = ""; RefreshEntries(); });
        _clearScope.Name = "ClearHistoryFilter";
        _newCardCollection = AddAction(row, "", () => EditCardCollection(null), true);
        _newCardCollection.Name = "NewCardCollection";
        _returnFromCollections = AddAction(row, "", () => { _collectionReturnToSearch = false; CardCollectionReturnRequested?.Invoke(); });
        _returnFromCollections.Name = "ReturnToSearchFromCollections";
    }

    internal void ShowSection(LibrarySection section)
    {
        if (_section == section) return;
        _section = section; ClearFilter();
        if (section != LibrarySection.History) { _queryFilter = null; _sourceHistoryId = ""; }
        _detailScroll.ScrollVertical = 0; HideReceipt(); RefreshEntries();
    }

    private void RefreshHistoryToolbar()
    {
        _tabs[LibrarySection.Favorites].Text = T("种子收藏", "Favorite seeds");
        _tabs[LibrarySection.Presets].Text = T("预设搜索", "Search presets");
        _tabs[LibrarySection.History].Text = T("历史搜索", "Search history");
        _tabs[LibrarySection.CardCollections].Text = T("牌集", "Card collections");
        _newCardCollection!.Text = T("新建牌集", "New collection");
        _newCardCollection.Visible = _section == LibrarySection.CardCollections && _cardCollections is not null;
        _returnFromCollections!.Text = T("返回筛种", "Back to Search");
        _returnFromCollections.Visible = _section == LibrarySection.CardCollections && _collectionReturnToSearch;
        foreach (var (section, button) in _tabs) { button.Disabled = false; _palette.SetActive(button, section == _section); }
        _description.Text = _section switch
        {
            LibrarySection.Presets => T("保存常用条件，随时使用或查看对应的历史结果。", "Keep useful queries, reuse them and revisit their results."),
            LibrarySection.History => T("选中一条记录，查看当时的条件和找到的种子。", "Select a search to view its conditions and results."),
            LibrarySection.CardCollections => T("保存一组卡牌，在筛种时选用。修改牌集不会改变已经保存的搜索条件。", "Save cards to reuse in Search. Editing a collection does not change saved search conditions."),
            _ => T("保存自己的种子，或从开发者推荐中挑一颗开始。", "Keep your own seeds, or try one of the developer picks.")
        };
        _filter.PlaceholderText = _section == LibrarySection.CardCollections ? T("查找牌集名称", "Find a collection")
            : _section == LibrarySection.Favorites ? T("查找标题、种子或备注", "Find a title, seed or note") : T("查找名称或角色", "Find a name or character");
        bool filtered = _section == LibrarySection.History && (_queryFilter is not null || _sourceHistoryId.Length > 0);
        _scope!.Visible = _clearScope!.Visible = filtered;
        _scope.Text = filtered ? _queryFilter?.TitleFor(_language) ?? T("来源记录", "Source search") : "";
        _clearScope.Text = T("全部历史", "All history");
    }

    internal void ShowPresetResults(SearchPresetDefinition preset)
    {
        _section = LibrarySection.History; _queryFilter = preset; _sourceHistoryId = "";
        _selectedHistory = ""; _historyResults = true; ClearFilter(); RefreshEntries();
    }

    private string QueryTitle(QueryHistoryEntry query)
    {
        var presets = _historyStore!.PresetsForQuery(query.QueryKey);
        if (presets.Count > 0) return string.Join(" / ", presets.Select(p => p.TitleFor(_language)));
        string characters = query.Draft.Players.Count > 0
            ? string.Join(" / ", query.Draft.Players.Select(p => _names.Resolve(p.Character, GameContentKind.Character)))
            : _names.Resolve(query.Draft.Character, GameContentKind.Character);
        return characters + $" · A{query.Draft.Ascension}";
    }
    private string QueryContext(QueryHistoryEntry query) =>
        (query.Draft.Players.Count > 0 ? T($"{query.Draft.Players.Count} 人", $"{query.Draft.Players.Count} players") : T("单人", "Solo")) +
        $" · A{query.Draft.Ascension} · {query.LastUsedAtUtc.ToLocalTime():MM-dd HH:mm}";

    private SearchPresetDefinition? RenderQueryHistory()
    {
        if (_historyStore!.QueryHistoryIssue.Length > 0) ShowIssue(T("部分历史无法读取，原文件已保留。", "Some history could not be read; original files are preserved."));
        var selected = _visibleQueries.FirstOrDefault(q => q.Id == _selectedHistory);
        var old = _visibleLegacyQueries.FirstOrDefault(p => p.Id == _selectedHistory);
        if (selected is null && old is null) { EmptyDetail(T("尚无对应的搜索记录。", "No matching search history yet.")); return null; }
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
        var conditions = AddAction(actions, T("查看条件", "Conditions"), () => { if (_historyResults) { _historyResults = false; SelectDetail(); } });
        conditions.Name = "HistoryConditions"; _palette.SetActive(conditions, !_historyResults);
        var results = AddAction(actions, T($"结果（{selected?.Results.Count ?? 0}）", $"Results ({selected?.Results.Count ?? 0})"), () => { if (!_historyResults) { _historyResults = true; SelectDetail(); } });
        results.Name = "HistoryResults"; _palette.SetActive(results, _historyResults);
        if (!canUse) _detail.AddChild(Text(issue, 17, true));
        if (selected is not null) AddEnvironmentNotice(selected);
        // Conditions/results are consumed separately by the presentation iterator.
        return _historyResults ? null : definition;
    }

    private IEnumerable<bool> RenderHistoryResults(QueryHistoryEntry? query)
    {
        if (query is null || query.Results.Count == 0)
        { _detail.AddChild(Text(T("这条记录没有保存的结果。", "This record has no saved results."), 18, true)); yield break; }
        // Search can append while this view spans frames. Copy record references,
        // retaining their provenance; a changed revision schedules a fresh view.
        var results = query.Results.ToArray();
        for (int index = results.Length - 1; index >= 0; index--)
        {
            var result = results[index];
            var row = new HBoxContainer { Name = "HistoryResultRow" + result.Seed }; row.AddThemeConstantOverride("separation", 10); _detail.AddChild(row);
            var status = SearchResultValidationDisplay.StatusKey(result);
            var seed = Text(result.Seed + (result.IsUnverified ? " · " +
                RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(_language).Get(status) : ""), 19);
            seed.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(seed);
            AddAction(row, T("复制", "Copy"), () => DisplayServer.ClipboardSet(result.Seed));
            var predict = AddAction(row, T("进行预测", "Predict"), () => ResultOpenRequested?.Invoke(result)); predict.Disabled = result.Context is null;
            var favorite = AddAction(row, T("收藏", "Favorite"), () =>
            {
                var origin = _historyStore!.ResultAssociation(result.Seed, query);
                OpenSave(result.Seed, result.Context!, queryKey: query.QueryKey, association: origin,
                    resultProvenance: SeedLibraryResultProvenance.From(result, origin));
            });
            favorite.Disabled = result.Context is null;
            SearchResultValidationDisplay.AddReason(_detail, result, _palette,
                RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(_language));
            yield return true;
        }
    }

    private void AddBookmarkSource(SeedLibraryEntry entry)
    {
        if (_historyStore is null) return;
        var presets = entry.QueryKeys.SelectMany(_historyStore.PresetsForQuery).DistinctBy(p => p.Id).ToArray();
        if (presets.Length > 0) _detail.AddChild(Text(T("关联预设 · ", "Linked presets · ") + string.Join(" / ", presets.Select(p => p.TitleFor(_language))), 17, true));
        var source = _historyStore.Workspace.QueryHistory.FirstOrDefault(q => entry.QueryAssociations.Any(a => a.SearchRecordId == q.Id));
        if (source is not null)
        {
            AddEnvironmentNotice(source);
            var go = AddAction(Actions(), T("查看来源记录", "View source search"), () =>
            { _section = LibrarySection.History; _queryFilter = null; _sourceHistoryId = source.Id; _selectedHistory = source.Id; _historyResults = true; ClearFilter(); RefreshEntries(); });
            go.Name = "ViewFavoriteSource";
        }
        else if (entry.QueryAssociations.Count > 0)
        {
            _detail.AddChild(Text(T("原搜索记录已不在近期历史中，收藏仍然保留。", "The original search is no longer in recent history. This favorite is retained."), 16, true));
            AddEnvironmentNotice(null);
        }
    }

    private void AddEnvironmentNotice(QueryHistoryEntry? record)
    {
        string message = SearchEnvironmentIdentity.Describe(_historyStore?.EnvironmentComparison(record)
            ?? new(SearchEnvironmentComparisonStatus.Unknown, []), _language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (message.Length == 0) return;
        var notice = Text(message, 16, true);
        notice.Name = "LibraryEnvironmentNotice"; _detail.AddChild(notice);
    }

    private void RequireIdle(Button button, bool unavailable = false)
    {
        _idleActions.Add(button);
        button.SetMeta("requires_idle", true); button.SetMeta("unavailable", unavailable);
        button.Disabled = unavailable || SearchBusy?.Invoke() == true;
    }
    private IEnumerable<bool> AddConditions(SearchPresetDefinition preset)
    {
        var summary = new SearchConditionSummary { Name = "LibraryConditionSummary" };
        _detail.AddChild(summary);
        yield return true;
        foreach (bool step in summary.BindPresentation(preset, _language, _names)) yield return step;
    }
}
