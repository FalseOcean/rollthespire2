using Godot;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

// The shell presents issues; workers publish facts without holding a Godot node.
internal sealed partial class WorkspaceShell
{
    private Button? _issuesEntry;
    private PanelContainer? _issuesPanel;
    private ScrollContainer? _issuesBodyScroll;
    private ScrollContainer? _issuesDetailScroll;
    private VBoxContainer? _issuesRows;
    private Label? _issuesTitle;
    private Label? _issuesHelp;
    private Button? _issuesClose;
    private readonly Dictionary<Guid, IssueRowView> _issuesRowViews = new();
    private long _issuesRevision = -1;
    private string _issuesLanguage = "";

    private sealed record IssueRowView(VBoxContainer Row, Label Heading, Label Metadata,
        Button Expand, Button Copy, TextEdit Details);

    private void InitializeIssues()
    {
        _issuesEntry = _palette.Button("");
        _issuesEntry.Name = "RuntimeIssuesEntry";
        _issuesEntry.CustomMinimumSize = new(192, 28);
        _issuesEntry.Position = new(1376, 870); _issuesEntry.Size = new(192, 28);
        _issuesEntry.Alignment = HorizontalAlignment.Center;
        _issuesEntry.ClipText = true; _issuesEntry.Visible = false;
        _issuesEntry.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _issuesEntry.AddThemeFontSizeOverride("font_size", 16);
        MainPanelTheme.ApplyFont(_issuesEntry);
        foreach (var (state, fill) in new[] { ("normal", _palette.ContentSurface),
            ("hover", _palette.Hover), ("pressed", _palette.SelectedSurface) })
        {
            var box = _palette.Box(fill, _palette.Line, 1);
            box.ContentMarginTop = box.ContentMarginBottom = 2;
            _issuesEntry.AddThemeStyleboxOverride(state, box);
        }
        _issuesEntry.Pressed += () => { if (!CloseIssues()) ShowIssues(); };
        AddChild(_issuesEntry);
        // Attach while the body is empty. Runtime reparenting would exit every
        // page from the tree and run its resource/picker teardown callbacks.
        var scroll = _issuesBodyScroll = new ScrollContainer
        {
            Name = "RuntimeIssuesBodyScroll", Position = new(32, 96), Size = new(1536, 772),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        AddChild(scroll);
        _content.Reparent(scroll, false);
        _content.Position = Vector2.Zero;
        _content.CustomMinimumSize = _content.Size = new(1536, 772);
    }

    private void RefreshIssues()
    {
        if (_issuesEntry is null) return;
        long revision = RuntimeIssueLedger.Current.Revision;
        if (_issuesRevision == revision && _issuesLanguage == _languageCode) return;
        _issuesRevision = revision;
        _issuesLanguage = _languageCode;
        var issues = RuntimeIssueLedger.Current.Snapshot();
        var text = JsonUiTextProvider.CreateUi13(_languageCode);
        _issuesEntry.Visible = issues.Count > 0;
        _issuesEntry.Text = issues.Count == 0 ? "" : text.Format("issues.entry", issues.Count);
        if (issues.Count > 0)
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                _issuesEntry.AddThemeColorOverride(state,
                    issues[0].Severity == RuntimeIssueSeverity.Error ? _palette.Error : _palette.Warning);
        if (_issuesPanel is not null) ShowIssues();
    }

    private bool CloseIssues()
    {
        if (_issuesPanel is null) return false;
        RemoveChild(_issuesPanel);
        _issuesPanel.QueueFree();
        _issuesPanel = null;
        _issuesDetailScroll = null;
        _issuesRows = null;
        _issuesTitle = _issuesHelp = null;
        _issuesClose = null;
        _issuesRowViews.Clear();
        return true;
    }

    private void ShowIssues()
    {
        var text = JsonUiTextProvider.CreateUi13(_languageCode);
        if (_issuesPanel is null) CreateIssuesPanel();
        _issuesTitle!.Text = text.Get("issues.title");
        _issuesHelp!.Text = text.Get("issues.help");
        _issuesClose!.Text = text.Get("issues.close");
        var issues = RuntimeIssueLedger.Current.Snapshot();
        var retained = issues.Select(issue => issue.Id).ToHashSet();
        var rows = _issuesRows!;
        var scroll = _issuesDetailScroll!;
        // Keep the open list in reading order. A fresh panel starts newest-first;
        // subsequent arrivals append instead of moving existing reading targets.
        // Stable ledger identities also mean a repeat never replaces a TextEdit.
        var anchor = _issuesRowViews.Where(pair => retained.Contains(pair.Key)).Select(pair => pair.Value.Row)
            .Where(row => row.Position.Y + row.Size.Y > scroll.ScrollVertical)
            .OrderBy(row => row.Position.Y).FirstOrDefault();
        float anchorOffset = anchor is null ? 0 : anchor.Position.Y - scroll.ScrollVertical;
        bool removed = false;
        foreach (Guid id in _issuesRowViews.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            var view = _issuesRowViews[id];
            rows.RemoveChild(view.Row); view.Row.QueueFree();
            _issuesRowViews.Remove(id); removed = true;
        }
        foreach (var issue in issues)
        {
            if (!_issuesRowViews.TryGetValue(issue.Id, out var view))
            {
                view = CreateIssueRow(issue);
                rows.AddChild(view.Row);
                _issuesRowViews.Add(issue.Id, view);
            }
            view.Heading.Text = text.Get(issue.MessageKey);
            view.Metadata.Text = text.Format("issues.metadata", issue.LastOccurredUtc.ToLocalTime().ToString("HH:mm:ss"),
                issue.OccurrenceCount);
            view.Expand.Text = text.Get("issues.details");
            view.Copy.Text = text.Get("issues.copy");
        }
        // Ledger capacity can evict rows above the viewport. Anchor to a surviving
        // row after the container has laid out, rather than jumping to another item.
        if (removed && anchor is not null)
            Callable.From(() =>
            {
                if (_issuesRows == rows && GodotObject.IsInstanceValid(anchor) && anchor.GetParent() == rows)
                    scroll.ScrollVertical = Math.Max(0, (int)Math.Round(anchor.Position.Y - anchorOffset));
            }).CallDeferred();
    }

    private void CreateIssuesPanel()
    {
        var panel = _issuesPanel = new PanelContainer
        {
            Name = "RuntimeIssuesPanel", Position = new(32, 96), Size = new(1536, 772)
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = _palette.Color(_palette.Canvas), BorderColor = _palette.Color(_palette.Line),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 16, ContentMarginBottom = 16
        });
        AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);
        var header = new HBoxContainer(); column.AddChild(header);
        header.AddChild(_issuesTitle = _palette.Label("", 20));
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var close = _issuesClose = _palette.Button("");
        close.Pressed += () => CloseIssues(); header.AddChild(close);
        var help = _issuesHelp = _palette.Label("", 18);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart; column.AddChild(help);
        var scroll = _issuesDetailScroll = new ScrollContainer
        {
            Name = "RuntimeIssuesDetailScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        column.AddChild(scroll);
        var rows = _issuesRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 18); scroll.AddChild(rows);
    }

    private IssueRowView CreateIssueRow(RuntimeIssue issue)
    {
        var row = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var heading = _palette.Label("", 18);
        heading.AutowrapMode = TextServer.AutowrapMode.WordSmart; row.AddChild(heading);
        heading.AddThemeColorOverride("font_color", issue.Severity == RuntimeIssueSeverity.Error ? _palette.Error : _palette.Warning);
        var meta = _palette.Label("", 16);
        meta.AutowrapMode = TextServer.AutowrapMode.WordSmart; row.AddChild(meta);
        var controls = new HBoxContainer(); row.AddChild(controls);
        var details = new TextEdit
        {
            Text = $"{issue.Code}\n{issue.Source}/{issue.Stage}\n{issue.CorrelationId}\n{issue.ExceptionDetail}",
            Editable = false, Visible = false, CustomMinimumSize = new(0, 190),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        details.AddThemeFontSizeOverride("font_size", 16);
        MainPanelTheme.SetColor(details, "font_readonly_color", _palette.Text);
        var expand = _palette.Button("");
        expand.Pressed += () => details.Visible = !details.Visible; controls.AddChild(expand);
        var copy = _palette.Button("");
        copy.Pressed += () => DisplayServer.ClipboardSet(
            $"{issue.Code}\n{issue.Source}/{issue.Stage}\n{issue.CorrelationId}\n{issue.ExceptionDetail}");
        controls.AddChild(copy); row.AddChild(details);
        row.AddChild(new HSeparator());
        return new(row, heading, meta, expand, copy, details);
    }
}
