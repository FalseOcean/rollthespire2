using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>Plain-text note transaction; storage and seed context remain with the caller.</summary>
internal sealed partial class SeedLibraryNoteOverlay : Control
{
    private readonly Label _title;
    private readonly Label _context;
    private readonly Label _notice;
    private readonly Label _noteLabel;
    private readonly TextEdit _note;
    private readonly Label _issue;
    private readonly Button _cancel;
    private readonly Button _save;

    public SeedLibraryNoteOverlay()
    {
        Name = "SeedLibraryNoteOverlay";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = UiZLayers.TransactionModal;
        SetProcessUnhandledKeyInput(true);
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, .66f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(backdrop); backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(center); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 480) };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Drawer, 5, 2, 18);
        center.AddChild(panel);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);
        _title = Ui1Theme.Label("", Ui1TextRole.SectionTitle, true); column.AddChild(_title);
        _context = Ui1Theme.Label("", Ui1TextRole.Meta, true); column.AddChild(_context);
        _notice = Ui1Theme.Label("", Ui1TextRole.Warning, true); column.AddChild(_notice);
        _noteLabel = Ui1Theme.Label("", Ui1TextRole.Meta); column.AddChild(_noteLabel);
        _note = new TextEdit { CustomMinimumSize = new Vector2(660, 210), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.Boundary, TabInputMode = false };
        Ui1Theme.ApplyTextEdit(_note); column.AddChild(_note);
        _issue = Ui1Theme.Label("", Ui1TextRole.Warning, true); column.AddChild(_issue);
        var actions = new HBoxContainer(); actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _cancel = new Button { CustomMinimumSize = new Vector2(104, 38) };
        _save = new Button { CustomMinimumSize = new Vector2(104, 38) };
        Ui1Theme.ApplyButton(_cancel, Ui1ButtonRole.Ghost); Ui1Theme.ApplyButton(_save, Ui1ButtonRole.Primary);
        _cancel.Pressed += Cancel;
        _save.Pressed += () => SaveRequested?.Invoke(_note.Text);
        actions.AddChild(_cancel); actions.AddChild(_save); column.AddChild(actions);
        Control[] focus = [_note, _cancel, _save];
        for (int i = 0; i < focus.Length; i++)
        {
            NodePath next = focus[i].GetPathTo(focus[(i + 1) % focus.Length]);
            NodePath previous = focus[i].GetPathTo(focus[(i + focus.Length - 1) % focus.Length]);
            focus[i].FocusNext = focus[i].FocusNeighborRight = focus[i].FocusNeighborBottom = next;
            focus[i].FocusPrevious = focus[i].FocusNeighborLeft = focus[i].FocusNeighborTop = previous;
        }
    }

    public event Action<string>? SaveRequested;
    public event Action? Cancelled;
    public bool IsOpen => Visible;

    public void Open(string language, string title, string context, string note, string notice = "")
    {
        bool zh = language == "zh";
        _title.Text = title; _context.Text = context;
        _notice.Text = notice; _notice.Visible = !string.IsNullOrWhiteSpace(notice);
        _noteLabel.Text = zh ? "备注" : "Note";
        _note.PlaceholderText = zh ? "记录玩法、亮点或提醒，可留空。" : "Add ideas, highlights or reminders. Optional.";
        _cancel.Text = zh ? "取消" : "Cancel"; _save.Text = zh ? "保存" : "Save";
        _note.Text = note;
        ShowIssue(""); Visible = true; _note.GrabFocus();
    }

    public void ShowIssue(string message) { _issue.Text = message; _issue.Visible = !string.IsNullOrWhiteSpace(message); }
    public void CloseCommitted() => Visible = false;
    public void Cancel() { if (!Visible) return; Visible = false; Cancelled?.Invoke(); }
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (IsVisibleInTree() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { Cancel(); GetViewport().SetInputAsHandled(); }
    }
}
