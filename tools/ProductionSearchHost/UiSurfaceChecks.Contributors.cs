using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.DeveloperNotes;
using RolltheSpire2.Presentation.DeveloperNotes;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.DeveloperNotes;
using RolltheSpire2.Ui.Theme;

internal static partial class UiSurfaceChecks
{
    // The existing initialized native UI host, real bundled articles and renderer;
    // URL requests are observed at the page boundary, never opened by this slice.
    private static async Task RunContributorsChecksAsync(Node root, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, flags)!.GetValue(owner)!;
        int checks = 0;
        void Check(bool ok, string reason)
        { checks++; if (!ok) throw new InvalidOperationException("CONTRIBUTORS " + reason); }
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Ready(DeveloperNotesPage page)
        {
            for (int i = 0; i < 1800 && !page.IsPresentationReady && !page.HasPresentationFailure; i++) await Frames(1);
            Check(page.IsPresentationReady && !page.HasPresentationFailure, "complete native article and draw");
            await Frames();
        }
        IEnumerable<Node> Descendants(Node node)
        {
            foreach (Node child in node.GetChildren())
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }

        string priorFont = MainPanelTheme.FontChoice;
        Vector2I priorSize = root.GetWindow().Size;
        var surface = new Control();
        root.AddChild(surface);
        surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var background = new ColorRect { Color = MainPanelTheme.Palette.Page, MouseFilter = Control.MouseFilterEnum.Ignore };
        surface.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        surface.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var provider = new DeveloperNotesDocumentProvider();
        var page = new DeveloperNotesPage(provider);
        margin.AddChild(page);
        var urls = new List<string>();
        page.ExternalLinkRequested += urls.Add;
        int acknowledgements = 0;
        page.AcknowledgementRequested += _ => acknowledgements++;
        try
        {
            MainPanelTheme.SelectFont("game-sans");
            foreach (string language in new[] { "zh", "en" })
            {
                var document = provider.Resolve(language).Document!;
                Check(document.Chapters.Last().Id == DeveloperNotesChapterIds.Contributors,
                    "permanent contributors chapter follows the existing chapters " + language);
                page.ApplyLocalization(JsonUiTextProvider.Create(language), RuntimeGameContentNameResolver.Create(language));
                var buttons = Field<Dictionary<string, Button>>(page, "_chapterButtons");
                var button = buttons[DeveloperNotesChapterIds.Contributors];
                Check(button.Text == document.Chapters.Last().Title, "navigation has no unread marker " + language);
                button.EmitSignal(Button.SignalName.Pressed);
                await Ready(page);
                Check(page.SelectedChapterId == DeveloperNotesChapterIds.Contributors &&
                    !Field<Control>(page, "_acknowledgement").Visible, "chapter opens without acknowledgement " + language);
                Field<Button>(page, "_acknowledge").EmitSignal(Button.SignalName.Pressed);
                Check(acknowledgements == 0, "contributors cannot request acknowledgement " + language);

                var renderer = Field<DeveloperNotesArticleRenderer>(page, "_renderer");
                var labels = Descendants(renderer).OfType<RichTextLabel>().ToArray();
                Check(renderer.Sections.Select(s => s.Title).SequenceEqual(new[] { "James Zhou", "Egoistic今宵" }),
                    "public names retain Owner-confirmed order " + language);
                Check(labels.Where(l => l.Text is "James Zhou" or "Egoistic今宵").All(l => l.GetThemeFontSize("normal_font_size") == 24),
                    "contributor headings use existing 24-point role " + language);
                foreach (var label in labels)
                    foreach (Match match in Regex.Matches(label.Text, @"\[url=([^\]]+)\]"))
                        label.EmitSignal(RichTextLabel.SignalName.MetaClicked, match.Groups[1].Value);
                Check(urls.SequenceEqual(new[] {
                    "https://github.com/FalseOcean/rollthespire2/pull/4",
                    "https://github.com/shion0528/rollthespire2/releases/tag/v1.3.5-resource.17" }),
                    "actual RichTextLabel metadata signals route the two public sources to page " + language);
                urls.Clear();

                foreach (var size in new[] { new Vector2I(1600, 900), new Vector2I(960, 720) })
                {
                    root.GetWindow().Size = size;
                    page.SetCompact(size.X < 1200);
                    await Ready(page);
                    var scroll = Field<ScrollContainer>(page, "_articleScroll");
                    var reading = Field<MarginContainer>(page, "_readingHost");
                    float readingWidth = reading.Size.X - reading.GetThemeConstant("margin_left") - reading.GetThemeConstant("margin_right");
                    Check(readingWidth <= 841 && readingWidth > 200 && scroll.GetHScrollBar().MaxValue <= scroll.Size.X + 1,
                        "existing reading width and wrapping fit " + language + " " + size);
                    Check(button.IsVisibleInTree() && button.GetGlobalRect().End.X <= size.X,
                        "chapter navigation stays reachable " + language + " " + size);
                    scroll.ScrollVertical = 0;
                    await Frames();
                    RenderingServer.ForceDraw();
                    root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"contributors-{language}-{size.X}.png"));
                    scroll.EnsureControlVisible(labels.Last());
                    await Frames();
                    Check(labels.Last().GetGlobalRect().End.Y <= scroll.GetGlobalRect().End.Y + 2,
                        "final source remains reachable by native vertical scroll " + language + " " + size);
                    if (scroll.ScrollVertical > 0)
                    {
                        RenderingServer.ForceDraw();
                        root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"contributors-{language}-{size.X}-tail.png"));
                    }
                }
                buttons[DeveloperNotesChapterIds.Positioning].EmitSignal(Button.SignalName.Pressed);
                await Ready(page);
                buttons[DeveloperNotesChapterIds.Contributors].EmitSignal(Button.SignalName.Pressed);
                await Ready(page);
                Check(page.SelectedChapterId == DeveloperNotesChapterIds.Contributors && !Field<Control>(page, "_acknowledgement").Visible,
                    "permanent chapter survives navigation and revisits " + language);
            }
            File.WriteAllText(Path.Combine(output, "contributors.json"), JsonSerializer.Serialize(new { Passed = checks, BrowserOpened = false }));
            GD.Print("CONTRIBUTORS_PASS checks=" + checks);
        }
        finally
        {
            page.RetirePresentation();
            surface.Free();
            root.GetWindow().Size = priorSize;
            MainPanelTheme.SelectFont(priorFont);
        }
    }
}
