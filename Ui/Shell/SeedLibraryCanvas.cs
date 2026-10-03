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
    private readonly WorkspacePalette _palette = WorkspacePalette.Canonical;
    private readonly VBoxContainer _entries = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly VBoxContainer _detail = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly VBoxContainer _detailHeader = new() { Name = "LibraryDetailHeader" };
    private readonly VBoxContainer _detailFooter = new() { Name = "LibraryDetailFooter", Visible = false };
    private readonly LineEdit _filter = new() { Name = "LibraryFilter", ClearButtonEnabled = true, CustomMinimumSize = new(0, 42) };
    private readonly ScrollContainer _detailScroll = new() { Name = "LibraryDetailScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private string _selectedSeed = "";
    private readonly Label _title;
    private readonly Label _description;
    private readonly Label _receipt;
    private SeedLibraryNoteOverlay? _note;
    private SearchConfirmationTransactionOverlay? _confirmation;
    private Action<string, string>? _saveInformation;
    private Action? _confirmed;
    private Control? _returnFocus;
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private string _language = "zh";

    public SeedLibraryCanvas(SeedLibraryStore store, SearchWorkspacePersistence? persistence = null)
    {
        _historyStore = persistence;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Name = "SeedLibraryCanvas";
        FocusMode = FocusModeEnum.All;
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(body); body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        body.AddThemeConstantOverride("separation", 12);
        _title = _palette.Label("", 28); body.AddChild(_title);
        _description = Text("", 17, true); body.AddChild(_description);
        _receipt = Text("", 17, true); _receipt.Visible = false; body.AddChild(_receipt);
        BuildHistoryToolbar(body);
        var split = new HBoxContainer { Name = "LibrarySplit", SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 18); body.AddChild(split);
        var listPanel = new PanelContainer { Name = "LibraryListPanel", CustomMinimumSize = new(340, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        listPanel.AddThemeStyleboxOverride("panel", _palette.Box(_palette.Surface));
        var listColumn = new VBoxContainer(); listColumn.AddThemeConstantOverride("separation", 12); listPanel.AddChild(listColumn);
        listColumn.AddChild(_filter); Ui1Theme.ApplyLineEdit(_filter); _filter.TextChanged += _ => RefreshEntries();
        var scroll = new ScrollContainer { Name = "LibraryListScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        listColumn.AddChild(scroll); scroll.AddChild(_entries); split.AddChild(listPanel);
        _entries.AddThemeConstantOverride("separation", 8);
        var detailPanel = new PanelContainer { Name = "LibraryDetailPanel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var detailStyle = _palette.Box(_palette.Surface, _palette.Line, 1);
        detailStyle.ContentMarginLeft = detailStyle.ContentMarginRight = 22;
        detailStyle.ContentMarginTop = detailStyle.ContentMarginBottom = 20;
        detailPanel.AddThemeStyleboxOverride("panel", detailStyle);
        var detailColumn = new VBoxContainer(); detailColumn.AddThemeConstantOverride("separation", 20);
        detailPanel.AddChild(detailColumn);
        detailColumn.AddChild(_detailHeader); detailColumn.AddChild(_detailScroll); detailColumn.AddChild(_detailFooter);
        _detailScroll.AddChild(_detail); split.AddChild(detailPanel);
        _detail.AddThemeConstantOverride("separation", 18);
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
    }

    public void Refresh(string language)
    {
        _language = language == "en" ? "en" : "zh";
        _names = RuntimeGameContentNameResolver.Create(_language);
        _title.Text = T("种子库", "Seed library");
        RefreshHistoryToolbar();
        RefreshEntries();
    }

    public void RefreshEntries()
    {
        RefreshHistoryToolbar();
        foreach (Node child in _entries.GetChildren()) { _entries.RemoveChild(child); child.QueueFree(); }
        foreach (var container in new[] { _detail, _detailHeader, _detailFooter })
            foreach (Node child in container.GetChildren()) { container.RemoveChild(child); child.QueueFree(); }
        _detailFooter.Hide();
        if (_section == LibrarySection.History && _historyStore is not null) { RenderQueryHistory(); return; }
        if (_section == LibrarySection.Presets && _historyStore is not null) { RenderPresets(); return; }
        var all = _store.GetAll();
        if (_store.LoadIssues.Count > 0)
            _entries.AddChild(Text(T("部分种子库文件无法读取，原文件已保留。请使用兼容版本后重试。", "Some seed library files could not be read. The originals are preserved; retry with a compatible version."), 17, true));
        RenderBookmarks(all.Where(e => Matches(e.TitleFor(_language) + " " + e.Seed + " " + e.NoteFor(_language) + " " +
                (e.DeveloperDetails?.Instructions.Resolve(_language) ?? "") + " " + (e.Context is null ? "" : FormatContext(e.Context))))
            .OrderBy(e => e.Source == SeedLibrarySource.User ? 0 : 1).ThenByDescending(e => e.SavedAtUtc).ToArray());
    }

    public void OpenSave(string seed, SeedLibraryContext context, string? initialNote = null, string? queryKey = null, SeedQueryAssociation? association = null,
        string? initialTitle = null)
    {
        if (_note is null) throw new InvalidOperationException("SeedLibraryOverlayNotAttached");
        bool duplicate = _store.TryFind(seed, context, out SeedLibraryEntry existing);
        string note = duplicate ? existing.Note : initialNote ?? "";
        _saveInformation = (title, value) =>
        {
            var saved = _store.Save(seed, context, value, queryKey, association, title);
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
        _receipt.Text = message; _receipt.Visible = !string.IsNullOrWhiteSpace(message);
        _receipt.AddThemeColorOverride("font_color", _palette.Warning);
    }

    public bool CloseModal()
    {
        if (_confirmation?.IsOpen == true) { _confirmation.Cancel(); return true; }
        if (_note?.IsOpen == true) { _note.Cancel(); return true; }
        return false;
    }

    private void RenderBookmarks(IReadOnlyList<SeedLibraryEntry> entries)
    {
        if (entries.Count == 0)
        { EmptyDetail(T("从搜索结果或种子分析中收藏种子，就能在这里查看。", "Favorite seeds from search results or seed analysis to keep them here.")); return; }
        if (!entries.Any(e => e.Id == _selectedSeed)) _selectedSeed = entries[0].Id;
        foreach (var source in new[] { SeedLibrarySource.User, SeedLibrarySource.Developer })
        {
            var group = entries.Where(e => e.Source == source).ToArray();
            if (group.Length == 0) continue;
            _entries.AddChild(Text(source == SeedLibrarySource.User ? T("我的收藏", "My favorites") : T("开发者推荐", "Developer picks"), 16, true));
            foreach (SeedLibraryEntry entry in group)
                AddListItem(entry.Id, entry.TitleFor(_language), (entry.TitleFor(_language) != entry.Seed ? entry.Seed + " · " : "") +
                    (entry.Context is { } c ? FormatContext(c) : T("上下文不可用", "Context unavailable")), _selectedSeed == entry.Id,
                    () => { _selectedSeed = entry.Id; SelectDetail(); }, entry.DeveloperDetails?.Icons);
        }
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
        if (_note?.IsOpen == true || _confirmation?.IsOpen == true) return;
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
        Ui1Theme.ApplyButton(button, Ui1ButtonRole.Danger);
        button.AddThemeFontSizeOverride("font_size", 16); return button;
    }

    private HBoxContainer DetailHeading(string title, string context, IReadOnlyList<SearchPresetVisualIconRef>? icons = null)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 24); _detailHeader.AddChild(row);
        var information = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        information.AddThemeConstantOverride("separation", 12); row.AddChild(information);
        var titleRow = new HBoxContainer(); titleRow.AddThemeConstantOverride("separation", 12); information.AddChild(titleRow);
        if (icons is not null) AddPresetIcons(titleRow, icons, 40);
        titleRow.AddChild(Text(title, 27)); information.AddChild(Text(context, 17, true));
        var actions = new HBoxContainer { Name = "LibraryPrimaryActions", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        actions.AddThemeConstantOverride("separation", 10); row.AddChild(actions); return actions;
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
    private void SelectDetail() { _detailScroll.ScrollVertical = 0; _receipt.Hide(); RefreshEntries(); }
    private void EmptyDetail(string message) => _detail.AddChild(Text(string.IsNullOrWhiteSpace(_filter.Text) ? message : T("没有找到匹配的记录。", "No matching entries."), 19, true));
    private void AddListItem(string id, string title, string context, bool selected, Action action, IReadOnlyList<SearchPresetVisualIconRef>? icons = null)
    {
        var button = _palette.Button("", selected: selected); button.Name = "LibraryItem" + _entries.GetChildCount();
        button.SetMeta("entry_id", id); button.SetMeta("selected", selected);
        button.CustomMinimumSize = new(0, 88); button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
        button.Pressed += action; _entries.AddChild(button);
    }

    private Label Text(string text, int size, bool secondary = false)
    {
        Label label = _palette.Label(text, size, secondary);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.SizeFlagsHorizontal = SizeFlags.ExpandFill; return label;
    }

    private void ShowReceipt(string message)
    {
        _receipt.Text = message; _receipt.Visible = true; _receipt.AddThemeColorOverride("font_color", _palette.Confirmed);
        ReceiptShown?.Invoke(message);
    }
    private string Failure(Exception ex) => T("未能保存更改。原始内容已保留，请稍后重试。", "Changes could not be saved. The original content is preserved; please retry.") + "\n" + ex.Message;
    private string T(string zh, string en) => _language == "zh" ? zh : en;
}
