using RolltheSpire2.Ui.Workspace;
using Godot;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

/// <summary>Seed favorites, saved queries and search history share a retained master/detail workspace.</summary>
internal sealed partial class SeedLibraryCanvas : Control
{
    private readonly SeedLibraryStore _store;
    private readonly WorkspacePalette _palette = WorkspacePalette.MainPanel;
    private readonly VBoxContainer _entries = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly VBoxContainer _detail = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly VBoxContainer _detailHeader = new() { Name = "LibraryDetailHeader" };
    private readonly VBoxContainer _detailFooter = new() { Name = "LibraryDetailFooter", Visible = false };
    private readonly LineEdit _filter = new() { Name = "LibraryFilter", ClearButtonEnabled = true, CustomMinimumSize = new(0, 42) };
    private readonly ScrollContainer _detailScroll = new() { Name = "LibraryDetailScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private string _selectedSeed = "";
    private IReadOnlyList<SeedLibraryEntry> _visibleBookmarks = [];
    private readonly Label _title;
    private readonly Label _description;
    private readonly Label _receipt;
    private readonly HBoxContainer _receiptRow = new() { Name = "LibraryReceipt", Visible = false };
    private readonly TextEdit _issueDetails = new() { Name = "LibraryIssueDetails", Visible = false,
        Editable = false, CustomMinimumSize = new(0, 148), SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button _issueExpand;
    private readonly Button _issueCopy;
    private string _issueMessage = "";
    private SeedLibraryNoteOverlay? _note;
    private SearchConfirmationTransactionOverlay? _confirmation;
    private Action<string, string>? _saveInformation;
    private Action? _confirmed;
    private Control? _returnFocus;
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private string _language = "zh";
    private bool _updatingFilter;

    public SeedLibraryCanvas(SeedLibraryStore store, SearchWorkspace? persistence = null, string language = "zh", CardCollectionStore? cardCollections = null)
    {
        _historyStore = persistence;
        _cardCollections = cardCollections;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _language = language == "en" ? "en" : "zh";
        Name = "SeedLibraryCanvas";
        FocusMode = FocusModeEnum.All;
        Theme = MainPanelTheme.Create();
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(body); body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        body.AddThemeConstantOverride("separation", 16);
        _title = _palette.Label("", 28); body.AddChild(_title);
        _description = Text("", 17, true); body.AddChild(_description);
        _receipt = Text("", 17, true); _receipt.MaxLinesVisible = 1;
        _receipt.AutowrapMode = TextServer.AutowrapMode.Off; _receipt.ClipText = true;
        _receipt.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _receiptRow.AddThemeConstantOverride("separation", 12);
        body.AddChild(_receiptRow); _receiptRow.AddChild(_receipt);
        _issueExpand = AddAction(_receiptRow, "", ToggleIssueDetails);
        _issueExpand.Name = "LibraryIssueExpand";
        _issueCopy = AddAction(_receiptRow, "", () => DisplayServer.ClipboardSet(_issueMessage));
        _issueCopy.Name = "LibraryIssueCopy"; _issueCopy.Hide();
        _issueDetails.AddThemeFontSizeOverride("font_size", 16);
        MainPanelTheme.SetColor(_issueDetails, "font_readonly_color", _palette.Text);
        body.AddChild(_issueDetails);
        BuildHistoryToolbar(body);
        var split = new HBoxContainer { Name = "LibrarySplit", SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 24); body.AddChild(split);
        var listPanel = new PanelContainer { Name = "LibraryListPanel", CustomMinimumSize = new(316, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        listPanel.AddThemeStyleboxOverride("panel", _palette.ContentBox(0));
        var listColumn = new VBoxContainer(); listColumn.AddThemeConstantOverride("separation", 10); listPanel.AddChild(listColumn);
        listColumn.AddChild(_filter); _filter.TextChanged += _ => { if (!_updatingFilter) RefreshEntries(); };
        var scroll = new ScrollContainer { Name = "LibraryListScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        listColumn.AddChild(scroll); scroll.AddChild(_entries); split.AddChild(listPanel);
        _entries.AddThemeConstantOverride("separation", 6);
        var detailPanel = new PanelContainer { Name = "LibraryDetailPanel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var detailStyle = _palette.ContentBox();
        detailStyle.ContentMarginLeft = detailStyle.ContentMarginRight = 22;
        detailStyle.ContentMarginTop = detailStyle.ContentMarginBottom = 20;
        detailPanel.AddThemeStyleboxOverride("panel", detailStyle);
        var detailColumn = new VBoxContainer(); detailColumn.AddThemeConstantOverride("separation", 18);
        detailPanel.AddChild(detailColumn);
        detailColumn.AddChild(_detailHeader); detailColumn.AddChild(_detailScroll); detailColumn.AddChild(_detailFooter);
        _detailScroll.AddChild(_detail); split.AddChild(detailPanel);
        _detail.AddThemeConstantOverride("separation", 16);
        Refresh(_language);
    }

    public event Action<SeedLibraryEntry>? OpenRequested;
    public event Action<bool>? ModalChanged;
    public event Action<string>? ReceiptShown;

    public void AttachOverlay(Control shell)
    {
        if (_note is not null) return;
        _note = new SeedLibraryNoteOverlay(); _confirmation = new SearchConfirmationTransactionOverlay();
        shell.AddChild(_note); _note.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        shell.AddChild(_confirmation); _confirmation.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _note.Cancelled += EndModal;
        _note.SaveRequested += (title, note) =>
        {
            try { _saveInformation?.Invoke(title, note); _note.CloseCommitted(); _saveInformation = null; EndModal(); }
            catch (Exception ex) { _note.ShowIssue(Failure(ex)); }
        };
        _confirmation.Cancelled += () => { _confirmed = null; EndModal(); };
        _confirmation.Confirmed += () =>
        {
            try { _confirmed?.Invoke(); }
            catch (Exception ex) { ShowIssue(Failure(ex)); }
            finally { _confirmed = null; EndModal(); }
        };
        AttachCardCollectionEditor(shell);
    }

    public void Refresh(string language)
    {
        _language = language == "en" ? "en" : "zh";
        _names = RuntimeGameContentNameResolver.Create(_language);
        _title.Text = T("资料库", "Library");
        _issueExpand.Text = T("错误详情", "Error details");
        _issueCopy.Text = T("复制", "Copy");
        RefreshHistoryToolbar();
        RefreshEntries();
    }

    public void RefreshEntries()
    {
        if (_retired) return;
        RefreshHistoryToolbar();
        RequestPresentation(refreshList: true);
    }

    public void OpenSave(string seed, SeedLibraryContext context, string? initialNote = null, string? queryKey = null, SeedQueryAssociation? association = null,
        string? initialTitle = null, SeedLibraryResultProvenance? resultProvenance = null)
    {
        if (_note is null) throw new InvalidOperationException("SeedLibraryOverlayNotAttached");
        bool duplicate = _store.TryFind(seed, context, out SeedLibraryEntry existing);
        string note = duplicate ? existing.Note : initialNote ?? "";
        _saveInformation = (title, value) =>
        {
            var saved = _store.Save(seed, context, value, queryKey, association, title, resultProvenance);
            _selectedSeed = saved.Id;
            RefreshEntries();
            ShowReceipt(duplicate ? T("收藏信息已更新。", "Favorite information updated.") : T("已加入我的收藏。", "Added to My favorites."));
        };
        BeginModal();
        _note.Open(_language, T("收藏种子", "Favorite seed"), seed + "\n" + FormatContext(context), note,
            duplicate ? T("已有相同种子与上下文的收藏。保存会更新它的标题和备注。", "This seed and context are already saved. Saving updates its title and notes.") : "",
            duplicate ? existing.Title : initialTitle ?? "");
    }

    public void ShowIssue(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) { HideReceipt(); return; }
        if (_issueMessage != message)
        {
            _issueMessage = message; _issueDetails.Text = message; _issueDetails.Hide(); _issueCopy.Hide();
        }
        _receipt.Text = message.Split('\n', 2)[0].TrimEnd('\r');
        _receipt.Show(); _receiptRow.Show(); _issueExpand.Show();
        _receipt.AddThemeColorOverride("font_color", _palette.Warning);
    }

    private void HideReceipt()
    {
        _receipt.Hide(); _receiptRow.Hide(); _issueDetails.Hide(); _issueCopy.Hide();
    }

    private void ToggleIssueDetails()
    {
        _issueDetails.Visible = !_issueDetails.Visible;
        _issueCopy.Visible = _issueDetails.Visible;
    }

    public bool CloseModal()
    {
        if (_collectionEditor?.IsOpen == true) { _collectionEditor.Cancel(); return true; }
        if (_confirmation?.IsOpen == true) { _confirmation.Cancel(); return true; }
        if (_note?.IsOpen == true) { _note.Cancel(); return true; }
        return false;
    }

    private void RenderBookmarks(IReadOnlyList<SeedLibraryEntry> entries)
    {
        if (entries.Count == 0)
        { EmptyDetail(T("从搜索结果或种子分析中收藏种子，就能在这里查看。", "Favorite seeds from search results or seed analysis to keep them here.")); return; }
        var selected = entries.Single(e => e.Id == _selectedSeed);
        if (selected.Source == SeedLibrarySource.Developer) { RenderDeveloperSeed(selected); return; }
        var actions = DetailHeading(selected.DisplayTitle, (selected.Title.Length > 0 ? selected.Seed + "\n" : "") +
            (selected.Context is { } context ? FormatContext(context) : T("上下文不可用", "Context unavailable")));
        AddAction(actions, T("复制", "Copy"), () => { DisplayServer.ClipboardSet(selected.Seed); ShowReceipt(T("已复制种子。", "Seed copied.")); }).Name = "CopyFavorite";
        var predict = AddAction(actions, T("进行预测", "Predict"), () => OpenRequested?.Invoke(selected), true); predict.Name = "PredictFavorite"; predict.Disabled = !selected.CanOpen;
        var management = ManagementActions();
        AddAction(management, T("编辑信息", "Edit information"), () => EditNote(selected)).Name = "EditFavorite";
        AddDeleteAction(management, () => ConfirmRemove(selected)).Name = "DeleteFavorite";
        if (!selected.CanOpen) _detail.AddChild(Text(T("当前无法预测，已保留原始信息。", "Prediction is unavailable; the original information is preserved.") + "\n" + selected.Issue, 16, true));
        if (selected.ResultProvenance is { } provenance)
        {
            var text = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(_language);
            _detail.AddChild(Text(T("历史搜索来源；重新预测不代表再次通过原查询。", "Historical search source; prediction does not revalidate the original query."), 16));
            if (provenance.SourceSeed.Length > 0)
                _detail.AddChild(Text(T("来源种子：", "Source seed: ") + provenance.SourceSeed, 16));
            if (!provenance.AppliesToCurrentContext)
                _detail.AddChild(Text(T("此收藏的种子或配置已改变；原结果仅作历史记录。", "This favorite's seed or configuration has changed; the original result is historical only."), 16, true));
            _detail.AddChild(Text(text.Get(SearchResultValidationDisplay.StatusKey(provenance)), 18, true));
            SearchResultValidationDisplay.AddReason(_detail, provenance, _palette, text);
        }
        _detail.AddChild(Text(T("备注", "Notes"), 18, true));
        _detail.AddChild(Text(string.IsNullOrWhiteSpace(selected.Note) ? T("暂无备注", "No note") : selected.Note, 20));
        AddBookmarkSource(selected);
    }

    private void EditNote(SeedLibraryEntry entry)
    {
        if (_note is null) return;
        _saveInformation = (title, value) =>
        {
            _store.UpdateInformation(entry.Id, title, value); RefreshEntries(); ShowReceipt(T("收藏信息已保存。", "Favorite information saved."));
        };
        BeginModal();
        _note.Open(_language, T("编辑信息", "Edit information"), entry.Seed + "\n" + (entry.Context is { } context ? FormatContext(context) : ""), entry.Note, seedTitle: entry.Title);
    }

    private void ConfirmRemove(SeedLibraryEntry entry)
    {
        if (_confirmation is null) return;
        _confirmed = () =>
        {
            if (!_store.DeleteUser(entry.Id)) throw new IOException(T("无法移除此收藏，请检查文件是否可写。", "Could not remove this favorite. Check that its file is writable."));
            RefreshEntries(); ShowReceipt(T("已移除收藏。", "Favorite removed."));
        };
        BeginModal();
        _confirmation.Open(T("移除收藏", "Remove favorite"), T($"确认移除种子 {entry.Seed} 和它的备注？", $"Remove seed {entry.Seed} and its note?"), T("移除", "Remove"), T("取消", "Cancel"));
    }

    private string FormatContext(SeedLibraryContext context)
    {
        string mode = context.Mode == WorldGameMode.Multiplayer ? T($"多人 · {context.Players.Count} 人", $"Party · {context.Players.Count} players") : T("单人", "Solo");
        string roster = string.Join(" / ", context.Players.Select(p => $"P{p.Slot + 1} {_names.Resolve(p.Character, GameContentKind.Character)}"));
        return $"{mode} · A{context.Ascension} · {roster} · {context.GameVersion}";
    }

    private void BeginModal()
    {
        _returnFocus = GetViewport().GuiGetFocusOwner();
        // The shell blocks navigation; the overlay contains pointer and keyboard focus.
        ModalChanged?.Invoke(true);
    }

    private void EndModal()
    {
        if (_note?.IsOpen == true || _confirmation?.IsOpen == true || _collectionEditor?.IsOpen == true) return;
        _saveInformation = null; ModalChanged?.Invoke(false);
        if (_returnFocus is not null && GodotObject.IsInstanceValid(_returnFocus) && _returnFocus.IsVisibleInTree()) _returnFocus.GrabFocus();
        else if (IsVisibleInTree()) GrabFocus();
    }

    private Button AddAction(Control parent, string text, Action action, bool primary = false)
    {
        Button button = _palette.Button(text, primary); button.CustomMinimumSize = new Vector2(96, 42);
        if (!primary) button.AddThemeStyleboxOverride("normal", _palette.Box(_palette.Canvas, _palette.Line, 1));
        button.AddThemeFontSizeOverride("font_size", 16); button.Pressed += action; parent.AddChild(button); return button;
    }

    private Button AddDeleteAction(Control parent, Action action)
    {
        var button = AddAction(parent, T("删除", "Delete"), action);
        MainPanelTheme.ApplyButton(button, Ui1ButtonRole.Danger);
        button.AddThemeFontSizeOverride("font_size", 16); return button;
    }

    private HBoxContainer DetailHeading(string title, string context, IReadOnlyList<SearchPresetVisualIconRef>? icons = null)
    {
        var stack = new VBoxContainer { Name = "LibraryHeadingStack", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 16);
        _detailHeader.AddChild(stack);
        var information = new VBoxContainer { Name = "LibraryHeadingInformation", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        information.AddThemeConstantOverride("separation", 12); stack.AddChild(information);
        var titleRow = new HBoxContainer(); titleRow.AddThemeConstantOverride("separation", 12); information.AddChild(titleRow);
        if (icons is not null) AddPresetIcons(titleRow, icons, 40);
        var heading = Text(title, 27); heading.Name = "LibraryDetailTitle";
        heading.MaxLinesVisible = 2; heading.ClipText = true;
        heading.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        titleRow.AddChild(heading); information.AddChild(Text(context, 17, true));
        // The fixed header keeps actions reachable; only overflowing names are
        // repeated in full in the existing scrollable detail content.
        var fullTitle = Text(title, 20); fullTitle.Name = "LibraryFullTitle";
        fullTitle.Hide(); _detail.AddChild(fullTitle);
        void UpdateFullTitle()
        {
            if (IsInstanceValid(heading) && IsInstanceValid(fullTitle))
            {
                int lines = Math.Clamp(heading.GetLineCount(), 1, 2);
                heading.CustomMinimumSize = new(0, heading.GetLineHeight() * lines +
                    Math.Max(0, heading.GetThemeConstant("line_spacing")) * (lines - 1));
                fullTitle.Visible = heading.GetLineCount() > heading.MaxLinesVisible;
            }
        }
        void ScheduleFullTitle() => Callable.From(UpdateFullTitle).CallDeferred();
        heading.Resized += ScheduleFullTitle; heading.ThemeChanged += ScheduleFullTitle;
        ScheduleFullTitle();
        var actions = new HBoxContainer { Name = "LibraryPrimaryActions", SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 10); stack.AddChild(actions); return actions;
    }

    private HFlowContainer ManagementActions()
    {
        _detailFooter.Show(); _detailFooter.AddThemeConstantOverride("separation", 14);
        _detailFooter.AddChild(new ColorRect { Color = _palette.Color(_palette.Line), CustomMinimumSize = new(0, 1), MouseFilter = MouseFilterEnum.Ignore });
        var row = new HFlowContainer { Name = "LibraryManagementActions", Alignment = FlowContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("h_separation", 14); row.AddThemeConstantOverride("v_separation", 8);
        _detailFooter.AddChild(row); return row;
    }

    private bool Matches(string value) => string.IsNullOrWhiteSpace(_filter.Text) || value.Contains(_filter.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    private HFlowContainer Actions()
    {
        var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 10); row.AddThemeConstantOverride("v_separation", 8); _detail.AddChild(row); return row;
    }
    private void ClearFilter()
    {
        _updatingFilter = true;
        try { _filter.Text = ""; }
        finally { _updatingFilter = false; }
    }
    private void SelectDetail()
    {
        _detailScroll.ScrollVertical = 0; HideReceipt();
        UpdateSelectedRow();
        RequestPresentation(refreshList: false);
    }
    private void EmptyDetail(string message) => _detail.AddChild(Text(string.IsNullOrWhiteSpace(_filter.Text) ? message : T("没有找到匹配的记录。", "No matching entries."), 19, true));
    private void AddListItem(string id, string title, string context, bool selected, Action action, IReadOnlyList<SearchPresetVisualIconRef>? icons = null)
    {
        var button = _palette.Button(""); ApplyListItemState(button, selected); button.Name = "LibraryItem" + _entries.GetChildCount();
        button.SetMeta("entry_id", id); button.SetMeta("selected", selected);
        button.CustomMinimumSize = new(0, 76); button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        button.AddChild(row); row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = row.OffsetTop = 12; row.OffsetRight = row.OffsetBottom = -12;
        if (icons is { Count: > 0 })
        {
            var iconRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            iconRow.AddThemeConstantOverride("separation", 2);
            AddPresetIcons(iconRow, icons, 26);
            if (iconRow.GetChildCount() > 0) row.AddChild(iconRow);
            else iconRow.Free();
        }
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(column);
        foreach (var label in new[] { _palette.Label(title, 18), _palette.Label(context.Replace("\n", " · "), 14, true) })
        { label.MouseFilter = MouseFilterEnum.Ignore; label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; column.AddChild(label); }
        long version = _listVersion;
        button.Pressed += () =>
        {
            if (version == _listVersion && !_refreshPending && !_buildingList && !button.GetMeta("selected").AsBool()) action();
        };
        _entries.AddChild(button);
        _listButtons[id] = button;
        if (selected) _activeListButton = button;
    }

    private void ApplyListItemState(Button button, bool selected)
    {
        _palette.SetActive(button, selected);
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
        {
            var style = (StyleBoxFlat)button.GetThemeStylebox(state).Duplicate();
            style.BorderWidthBottom = 0;
            style.BorderWidthLeft = selected ? 2 : 0;
            button.AddThemeStyleboxOverride(state, style);
        }
    }

    private Label Text(string text, int size, bool secondary = false)
    {
        Label label = _palette.Label(text, size, secondary);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.SizeFlagsHorizontal = SizeFlags.ExpandFill; return label;
    }

    private void ShowReceipt(string message)
    {
        _issueDetails.Hide(); _issueExpand.Hide(); _issueCopy.Hide(); _receiptRow.Show();
        _receipt.Text = message; _receipt.Visible = true; _receipt.AddThemeColorOverride("font_color", _palette.Confirmed);
        ReceiptShown?.Invoke(message);
    }
    private string Failure(Exception ex) => T("未能保存更改。原始内容已保留，请稍后重试。", "Changes could not be saved. The original content is preserved; please retry.") + "\n" + ex.Message;
    private string T(string zh, string en) => _language == "zh" ? zh : en;
}
