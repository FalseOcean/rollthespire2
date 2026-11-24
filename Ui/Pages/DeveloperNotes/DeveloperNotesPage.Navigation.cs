using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.DeveloperNotes;

internal sealed partial class DeveloperNotesPage
{
    private readonly VBoxContainer _contents = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label _contentsTitle = MainPanelTheme.Label(string.Empty, Ui1TextRole.Meta);
    private readonly Label _progress = MainPanelTheme.Label(string.Empty, Ui1TextRole.Meta);
    private readonly Button _backToTop = new();
    private readonly HBoxContainer _chapterFooter = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly List<Button> _sectionButtons = [];
    private readonly Dictionary<string, int> _readingPositions = new(StringComparer.Ordinal);
    private int _highlightedSection = -2;
    private bool _restoringPosition;

    private string ReadingKey => _resolvedLocale + "/" + _selectedChapterId;
    private string Local(string zh, string en) => _resolvedLocale.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? zh : en;

    private void BuildReadingNavigation(VBoxContainer rail)
    {
        var divider = new HSeparator();
        MainPanelTheme.ApplySeparator(divider);
        rail.AddChild(divider);
        rail.AddChild(_contentsTitle);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FocusMode = FocusModeEnum.None
        };
        _contents.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_contents);
        rail.AddChild(scroll);
        rail.AddChild(_progress);
        _backToTop.CustomMinimumSize = new Vector2(0, 36);
        _backToTop.Pressed += () => { _readerMoved = true; _articleScroll.ScrollVertical = 0; };
        StyleReadingLink(_backToTop, false);
        rail.AddChild(_backToTop);
        _chapterFooter.AddThemeConstantOverride("separation", 16);
    }

    private static void ClearNavigation(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }

    private void RebuildReadingNavigation()
    {
        ClearNavigation(_contents);
        ClearNavigation(_chapterFooter);
        _sectionButtons.Clear();
        _highlightedSection = -2;
        _contentsTitle.Text = Local("本篇目录", "ON THIS PAGE");
        _backToTop.Text = Local("回到开篇 ↑", "Back to top ↑");
        _contentsTitle.Visible = _renderer.Sections.Count > 0;
        foreach (var section in _renderer.Sections)
            AppendReadingSection(section);
        if (_document is null) return;
        int index = _document.Chapters.ToList().FindIndex(chapter => chapter.Id == _selectedChapterId);
        foreach (int step in new[] { -1, 1 })
        {
            int targetIndex = index + step;
            if (targetIndex < 0 || targetIndex >= _document.Chapters.Count) continue;
            var target = _document.Chapters[targetIndex];
            var button = new Button
            {
                Text = (step < 0 ? Local("← 上一篇", "← Previous") : Local("下一篇 →", "Next →")) + "\n" + target.Title,
                TooltipText = target.Title, CustomMinimumSize = new Vector2(0, 76),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            StyleReadingLink(button, false);
            button.Pressed += () => SelectChapter(target.Id);
            _chapterFooter.AddChild(button);
        }
    }

    private void AppendReadingSection(DeveloperNotesArticleRenderer.Section section)
    {
        var button = new Button
        {
            Text = section.Title, TooltipText = section.Title,
            Alignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 40)
        };
        try
        {
            StyleReadingLink(button, false);
            button.Pressed += () => { _readerMoved = true; _articleScroll.ScrollVertical = AnchorPosition(section.Anchor); };
            _contents.AddChild(button);
            _sectionButtons.Add(button);
            _contentsTitle.Show();
        }
        catch { if (button.GetParent() is null) button.Free(); throw; }
    }

    private static void StyleReadingLink(Button button, bool selected)
    {
        MainPanelTheme.ApplyButton(button, selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
        var palette = WorkspacePalette.MainPanel;
        var normal = new StyleBoxFlat
        {
            BgColor = selected ? palette.Color(palette.SelectedSurface) : Colors.Transparent,
            BorderColor = palette.Color(palette.Selected), BorderWidthLeft = selected ? 2 : 0,
            ContentMarginLeft = 12, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8
        };
        button.AddThemeStyleboxOverride("normal", normal);
        foreach (string state in new[] { "hover", "pressed" })
        {
            var pointer = (StyleBoxFlat)normal.Duplicate();
            pointer.BgColor = palette.Color(state == "hover" ? palette.Hover : palette.SelectedSurface);
            button.AddThemeStyleboxOverride(state, pointer);
        }
        MainPanelTheme.SetColor(button, "font_color", selected ? palette.Text : palette.Secondary);
        button.AddThemeFontSizeOverride("font_size", 16);
    }

    private int AnchorPosition(Control anchor) => Math.Max(0,
        (int)(anchor.GetGlobalRect().Position.Y - _readingHost.GetGlobalRect().Position.Y) - 12);

    private void UpdateReadingProgress()
    {
        if (!IsInsideTree() || _restoringPosition) return;
        var bar = _articleScroll.GetVScrollBar();
        double maximum = Math.Max(0, bar.MaxValue - bar.Page);
        int percent = maximum > 0 ? (int)Math.Clamp(bar.Value / maximum * 100, 0, 100) : 100;
        _progress.Text = Local($"阅读进度  {percent}%", $"Reading progress  {percent}%");
        int selected = -1;
        for (int i = 0; i < _renderer.Sections.Count; i++)
            if (AnchorPosition(_renderer.Sections[i].Anchor) <= _articleScroll.ScrollVertical + 24) selected = i;
        if (selected == _highlightedSection) return;
        _highlightedSection = selected;
        for (int i = 0; i < _sectionButtons.Count; i++) StyleReadingLink(_sectionButtons[i], i == selected);
    }

    private void SaveReadingPosition()
    {
        if ((!_restoringPosition || _readerMoved) && _document is not null)
            _readingPositions[ReadingKey] = _articleScroll.ScrollVertical;
    }
}
