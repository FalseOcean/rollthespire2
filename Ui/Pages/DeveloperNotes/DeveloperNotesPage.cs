using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.DeveloperNotes;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.DeveloperNotes;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.DeveloperNotes;

internal sealed partial class DeveloperNotesPage : MarginContainer, IAppPage, IResponsiveAppPage
{
    private const float RailWidth = 176f;
    private const float CompactRailWidth = 146f;
    private const float ReadingMaxWidth = 840f;
    private const float ReadingMinimumSideMargin = 14f;

    private readonly DeveloperNotesDocumentProvider _provider;
    private readonly Label _pageTitle;
    private readonly Label _pageSubtitle;
    private readonly PanelContainer _railPanel;
    private readonly VBoxContainer _railColumn;
    private readonly Dictionary<string, Button> _chapterButtons = new(StringComparer.Ordinal);
    private readonly MarginContainer _articlePane;
    private readonly ScrollContainer _articleScroll;
    private readonly MarginContainer _readingHost;
    private readonly Label _articleTitle;
    private readonly Label _articleSummary;
    private readonly HSeparator _articleDivider;
    private readonly DeveloperNotesArticleRenderer _renderer;
    private IUiTextProvider? _uiText;
    private DeveloperNotesDocument? _document;
    private string _selectedChapterId = DeveloperNotesChapterIds.Release;
    private string _resolvedLocale = string.Empty;
    private bool _compact;

    public DeveloperNotesPage(DeveloperNotesDocumentProvider provider)
    {
        _provider = provider;
        PageKey = AppPageKey.DeveloperNotes;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 10);
        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.PageTitle);
        _pageSubtitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, wrap: true);
        root.AddChild(_pageTitle);
        root.AddChild(_pageSubtitle);

        var topDivider = new HSeparator();
        Ui1Theme.ApplySeparator(topDivider);
        root.AddChild(topDivider);

        var body = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 14);

        _railPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(RailWidth, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyPanel(_railPanel, Ui1SurfaceRole.Card, 4f, 1, 8f);
        _railColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _railColumn.AddThemeConstantOverride("separation", 5);
        _railPanel.AddChild(_railColumn);

        var divider = new VSeparator();
        Ui1Theme.ApplySeparator(divider);

        _articlePane = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _articleScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        _readingHost = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var articleColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        articleColumn.AddThemeConstantOverride("separation", 10);
        _articleTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle, wrap: true);
        _articleSummary = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, wrap: true);
        _articleDivider = new HSeparator();
        Ui1Theme.ApplySeparator(_articleDivider);
        _renderer = new DeveloperNotesArticleRenderer();
        articleColumn.AddChild(_articleTitle);
        articleColumn.AddChild(_articleSummary);
        articleColumn.AddChild(_articleDivider);
        articleColumn.AddChild(_renderer);
        _readingHost.AddChild(articleColumn);
        _articleScroll.AddChild(_readingHost);
        _articlePane.AddChild(_articleScroll);
        _articlePane.Resized += RefreshReadingMargins;

        body.AddChild(_railPanel);
        body.AddChild(divider);
        body.AddChild(_articlePane);
        root.AddChild(body);
        AddChild(root);
    }

    public AppPageKey PageKey { get; }
    public Control View => this;
    public string SelectedChapterId => _selectedChapterId;

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _uiText = uiText;
        _pageTitle.Text = uiText.Get(Ui1TextKey.DeveloperNotesTitle);
        _pageSubtitle.Text = uiText.Get(Ui1TextKey.DeveloperNotesSubtitle);

        DeveloperNotesResolution resolution = _provider.Resolve(uiText.LanguageCode);
        _document = resolution.Document;
        _resolvedLocale = resolution.ResolvedLocale;
        if (resolution.UsedEnglishFallback)
        {
            RuntimeLog.Detail(
                $"developerNotesLocaleFallback=true;requestedLocale={resolution.RequestedLocale};" +
                $"resolvedLocale={resolution.ResolvedLocale};reason={resolution.Issue}");
        }
        if (_document is null || !_document.Chapters.Any(chapter => chapter.Id == _selectedChapterId))
        {
            _selectedChapterId = DeveloperNotesChapterIds.Release;
        }
        RebuildChapterRail();
        RenderSelectedChapter();

        if (!resolution.IsAvailable)
        {
            RuntimeLog.Warn($"developerNotesPageFailSoft=true;locale={uiText.LanguageCode};issue={resolution.Issue}");
        }
    }

    public void ApplyDisplayMode(AppDisplayMode mode)
    {
    }

    public void SetCompact(bool compact)
    {
        _compact = compact;
        _railPanel.CustomMinimumSize = new Vector2(compact ? CompactRailWidth : RailWidth, 0);
        RefreshReadingMargins();
    }

    private void RebuildChapterRail()
    {
        foreach (Node child in _railColumn.GetChildren())
        {
            _railColumn.RemoveChild(child);
            child.QueueFree();
        }
        _chapterButtons.Clear();

        if (_document is null)
        {
            _railPanel.Visible = false;
            return;
        }

        _railPanel.Visible = true;
        foreach (DeveloperNotesChapter chapter in _document.Chapters)
        {
            var button = new Button
            {
                Text = chapter.Title,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 42),
                FocusMode = Control.FocusModeEnum.All,
                TooltipText = chapter.Title
            };
            string chapterId = chapter.Id;
            button.Pressed += () => SelectChapter(chapterId);
            _chapterButtons[chapterId] = button;
            _railColumn.AddChild(button);
        }
        RefreshChapterButtonStyles();
    }

    private void SelectChapter(string chapterId)
    {
        if (_document is null || !_document.Chapters.Any(chapter => chapter.Id == chapterId))
        {
            return;
        }
        _selectedChapterId = chapterId;
        RefreshChapterButtonStyles();
        RenderSelectedChapter();
        ResetArticleScroll();
    }

    private void RefreshChapterButtonStyles()
    {
        foreach ((string id, Button button) in _chapterButtons)
        {
            Ui1Theme.ApplyButton(
                button,
                string.Equals(id, _selectedChapterId, StringComparison.Ordinal)
                    ? Ui1ButtonRole.NavigationSelected
                    : Ui1ButtonRole.Ghost);
        }
    }

    private void RenderSelectedChapter()
    {
        if (_document is null)
        {
            _articleTitle.Text = _uiText?.Get(Ui1TextKey.DeveloperNotesUnavailableTitle) ?? "Developer Notes unavailable";
            _articleSummary.Text = _uiText?.Get(Ui1TextKey.DeveloperNotesUnavailableMessage) ?? "The bundled notes could not be loaded.";
            _articleSummary.Visible = true;
            _articleDivider.Visible = true;
            _renderer.ClearBlocks();
            ResetArticleScroll();
            return;
        }

        DeveloperNotesChapter chapter = _document.Chapters.First(chapter => chapter.Id == _selectedChapterId);
        _articleTitle.Text = chapter.Title;
        _articleSummary.Text = chapter.Summary ?? string.Empty;
        _articleSummary.Visible = !string.IsNullOrWhiteSpace(chapter.Summary);
        _articleDivider.Visible = true;
        _renderer.Render(chapter);
        RuntimeLog.Detail($"developerNotesChapterRendered=true;chapter={chapter.Id};locale={_resolvedLocale}");
    }

    private void ResetArticleScroll()
    {
        _articleScroll.ScrollVertical = 0;
        _articleScroll.SetDeferred("scroll_vertical", 0);
    }

    private void RefreshReadingMargins()
    {
        float availableWidth = Math.Max(0f, _articlePane.Size.X);
        float targetWidth = _compact ? Math.Min(ReadingMaxWidth, availableWidth) : ReadingMaxWidth;
        float side = Math.Max(ReadingMinimumSideMargin, (availableWidth - targetWidth) * 0.5f);
        _readingHost.AddThemeConstantOverride("margin_left", (int)MathF.Round(side));
        _readingHost.AddThemeConstantOverride("margin_right", (int)MathF.Round(side));
        _readingHost.AddThemeConstantOverride("margin_top", 4);
        _readingHost.AddThemeConstantOverride("margin_bottom", 24);
    }
}
