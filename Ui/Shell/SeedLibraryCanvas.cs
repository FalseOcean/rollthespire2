using Godot;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

/// <summary>Developer recommendations and personal seed bookmarks, independent of query presets.</summary>
internal sealed partial class SeedLibraryCanvas : Control
{
    private readonly SeedLibraryStore _store;
    private readonly WorkspacePalette _palette = WorkspacePalette.Canonical;
    private readonly VBoxContainer _entries = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label _title;
    private readonly Label _description;
    private readonly Label _receipt;
    private SeedLibraryNoteOverlay? _note;
    private SearchConfirmationTransactionOverlay? _confirmation;
    private Action<string>? _saveNote;
    private Action? _confirmed;
    private Control? _returnFocus;
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private string _language = "zh";

    public SeedLibraryCanvas(SeedLibraryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Name = "SeedLibraryCanvas";
        FocusMode = FocusModeEnum.All;
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(body); body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        body.AddThemeConstantOverride("separation", 12);
        _title = _palette.Label("", 28); body.AddChild(_title);
        _description = Text("", 17, true); body.AddChild(_description);
        _receipt = Text("", 17, true); _receipt.Visible = false; body.AddChild(_receipt);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll); scroll.AddChild(_entries);
        _entries.AddThemeConstantOverride("separation", 12);
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
        _note.SaveRequested += value =>
        {
            try { _saveNote?.Invoke(value); _note.CloseCommitted(); _saveNote = null; EndModal(); }
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
        _description.Text = T("收藏完整种子与人物上下文，添加备注，随时重新查看种子信息。", "Keep seeds with their character context and notes, then reopen their seed information.");
        RefreshEntries();
    }

    public void RefreshEntries()
    {
        foreach (Node child in _entries.GetChildren()) { _entries.RemoveChild(child); child.QueueFree(); }
        var all = _store.GetAll();
        if (_store.LoadIssues.Count > 0)
            _entries.AddChild(Text(T("部分种子库文件无法读取，原文件已保留。请使用兼容版本后重试。", "Some seed library files could not be read. The originals are preserved; retry with a compatible version."), 17, true));
        // Release UI temporarily exposes personal favorites only; bundled data remains intact.
        AddSection(T("我的收藏", "My favorites"), all.Where(e => e.Source == SeedLibrarySource.User).OrderByDescending(e => e.SavedAtUtc).ToArray(),
            T("从筛种结果或种子预测中收藏种子，即可在这里查看。", "Favorite a search result or a seed prediction to keep it here."));
    }

    public void OpenSave(string seed, SeedLibraryContext context, string? initialNote = null)
    {
        if (_note is null) throw new InvalidOperationException("SeedLibraryOverlayNotAttached");
        bool duplicate = _store.TryFind(seed, context, out SeedLibraryEntry existing);
        string note = duplicate ? existing.Note : initialNote ?? "";
        _saveNote = value =>
        {
            _store.Save(seed, context, value);
            RefreshEntries();
            ShowReceipt(duplicate ? T("已更新已有收藏的备注。", "Updated the note on the existing favorite.") : T("已加入我的收藏。", "Added to My favorites."));
        };
        BeginModal();
        _note.Open(_language, T("收藏种子", "Favorite seed"), seed + "\n" + FormatContext(context), note,
            duplicate ? T("已有相同种子与上下文的收藏。保存会更新这条收藏的备注。", "This seed and context are already saved. Saving updates that favorite's note.") : "");
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

    private void AddSection(string title, IReadOnlyList<SeedLibraryEntry> entries, string empty)
    {
        _entries.AddChild(_palette.Label(title, 22));
        if (entries.Count == 0) { _entries.AddChild(Text(empty, 17, true)); return; }
        foreach (SeedLibraryEntry entry in entries)
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            panel.AddThemeStyleboxOverride("panel", _palette.Box(_palette.Surface, _palette.Line, 1));
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 8); panel.AddChild(column);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); column.AddChild(row);
            var seed = _palette.Label(entry.Seed, 22); seed.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(seed);
            AddAction(row, T("复制", "Copy"), () => { DisplayServer.ClipboardSet(entry.Seed); ShowReceipt(T("已复制种子。", "Seed copied.")); });
            var open = AddAction(row, T("打开信息", "Open information"), () => OpenRequested?.Invoke(entry), true);
            open.Disabled = !entry.CanOpen;
            if (entry.Source == SeedLibrarySource.Developer)
            {
                var favorite = AddAction(row, T("收藏", "Favorite"), () => OpenSave(entry.Seed, entry.Context!, entry.Note));
                favorite.Disabled = !entry.CanOpen;
            }
            else
            {
                AddAction(row, T("编辑备注", "Edit note"), () => EditNote(entry));
                AddAction(row, T("移除", "Remove"), () => ConfirmRemove(entry));
            }
            column.AddChild(Text(entry.Context is { } context ? FormatContext(context) : T("上下文不可用", "Context unavailable"), 16, true));
            if (!entry.CanOpen)
            {
                var issue = Text(T("暂不能重新打开；原始内容已保留，可先复制种子，或使用兼容版本后重试。", "Cannot reopen this entry yet. The original is preserved; copy the seed or retry with a compatible version."), 16, true);
                issue.TooltipText = entry.Issue; column.AddChild(issue);
            }
            column.AddChild(Text(string.IsNullOrEmpty(entry.Note) ? T("暂无备注", "No note") : entry.Note, 18, string.IsNullOrEmpty(entry.Note)));
            _entries.AddChild(panel);
        }
    }

    private void EditNote(SeedLibraryEntry entry)
    {
        if (_note is null) return;
        _saveNote = value =>
        {
            _store.UpdateNote(entry.Id, value); RefreshEntries(); ShowReceipt(T("备注已保存。", "Note saved."));
        };
        BeginModal();
        _note.Open(_language, T("编辑备注", "Edit note"), entry.Seed + "\n" + (entry.Context is { } context ? FormatContext(context) : ""), entry.Note);
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
        _saveNote = null; ModalChanged?.Invoke(false);
        if (_returnFocus is not null && GodotObject.IsInstanceValid(_returnFocus) && _returnFocus.IsVisibleInTree()) _returnFocus.GrabFocus();
        else if (IsVisibleInTree()) GrabFocus();
    }

    private Button AddAction(Control parent, string text, Action action, bool primary = false)
    {
        Button button = _palette.Button(text, primary); button.CustomMinimumSize = new Vector2(96, 38);
        button.AddThemeFontSizeOverride("font_size", 16); button.Pressed += action; parent.AddChild(button); return button;
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
