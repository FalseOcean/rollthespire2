using Godot;
using System.Text;
using System.Text.RegularExpressions;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class EncyclopediaCanvas
{
    private ScrollContainer? _articleScroll;
    private string? _readingId;
    private readonly Dictionary<string, int> _readingPositions = new(StringComparer.Ordinal);
    private sealed record Chapter(string Title, Control Anchor, int Level);

    private void RememberReadingPosition()
    {
        if (_readingId is { } id && GodotObject.IsInstanceValid(_articleScroll))
            _readingPositions[id] = _articleScroll!.ScrollVertical;
    }

    private void RenderMarkdownArticle(ArticleDefinition definition)
    {
        _mentionedRelics.Clear();
        _readingId = definition.Id;
        ArticleDefinition? parent = Articles.FirstOrDefault(a => a.Id == definition.ParentId);
        var back = Button(_main, parent is null ? T("‹  百科首页", "‹  Home") : "‹  " + ArticleName(parent), 0, 0, 270, 38,
            () => { if (parent is not null) ShowArticle(parent.Id); else { _articleId = null; Render(); } });
        back.Alignment = HorizontalAlignment.Left;
        StyleQuietButton(back, false);
        var breadcrumb = Label(_main, (parent is null ? T("百科", "Encyclopedia") : ArticleName(parent)) + "  /  " + ArticleName(definition), 16, 288, 9, true);
        breadcrumb.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        breadcrumb.Size = new Vector2(650, 26);
        if (definition.ReturnToSearch)
        {
            var search = Button(_main, T("返回筛选  ↗", "Back to filters  ↗"), 1030, 0, 184, 38, () => ReturnToSearchRequested?.Invoke());
            StyleQuietButton(search, false);
        }

        Panel(_main, 0, 54, 944, 642, Paper);
        var scroll = NewScroll(_main, 0, 54, 944, 642);
        _articleScroll = scroll;
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 28);
        margin.AddThemeConstantOverride("margin_top", 26); margin.AddThemeConstantOverride("margin_bottom", 30);
        scroll.AddChild(margin);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);

        using Stream? localized = typeof(EncyclopediaCanvas).Assembly.GetManifestResourceStream(ResourceName(definition, _language));
        using Stream? fallback = localized is null ? typeof(EncyclopediaCanvas).Assembly.GetManifestResourceStream(ResourceName(definition, "zh")) : null;
        Stream? source = localized ?? fallback;
        if (source is null) { content.AddChild(ArticleText(T("百科正文暂不可读取。", "Article text is unavailable."))); return; }
        using var reader = new StreamReader(source);
        string[] lines = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
        int firstHeading = Array.FindIndex(lines, line => Regex.IsMatch(line, @"^#{1,3}\s"));
        string title = firstHeading >= 0 ? lines[firstHeading].TrimStart('#', ' ') : ArticleName(definition);
        var eyebrow = ArticleText(T("机制与使用参考", "MECHANICS & USAGE") + "   /   BETA " + (definition.GameVersion ?? EncyclopediaGameVersion), 14, Gold);
        content.AddChild(eyebrow);
        var heading = ArticleText(title, 30, "E9ECED", firstRelicIcons: false);
        content.AddChild(heading);
        if (_language != "zh" && localized is null)
            content.AddChild(ArticleText("English translation pending · Showing the Chinese article.", 16, Quiet));
        string? takeaway = Takeaway(definition.Id);
        if (takeaway is not null) AddCallout(content, takeaway, T("阅读提示", "AT A GLANCE"));

        var chapters = new List<Chapter> { new(T("开篇", "Overview"), eyebrow, 1) };
        RenderDocumentBlocks(content, lines, firstHeading, chapters);
        AddArticleFooter(content, definition);
        RenderTableOfContents(chapters, scroll);
        int saved = _readingPositions.GetValueOrDefault(definition.Id);
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(scroll) && scroll.IsInsideTree()) scroll.ScrollVertical = saved;
        }).CallDeferred();
    }

    private string? Takeaway(string id) => id switch
    {
        IntroductionId => T("同一个 Seed，在相同规则与选择下，会产生相同的随机过程。", "The same seed, rules and choices produce the same random process."),
        NeowOfferId => T("先区分候选池；提供哪些选项，与领取后产生什么结果，是两层问题。", "Separate the offer pools. Offered choices and pickup results are different questions."),
        TransformEventResultsId => T("默认变化开局初始牌：把指定的牌保留到事件，并在事件中选择它。", "Transform a starting Basic card: keep that card until the event, then select it."),
        MapRoutesId => T("Guaranteed 看所有路线的最小值；Reachable Max 看所有路线的最大值。", "Guaranteed is the minimum across complete routes; Reachable Max is the maximum."),
        "multiplayer" => T("共享事实一起看，个人结果按玩家区分。", "Shared facts belong to the party; personal results belong to each player."),
        _ => null
    };

    private void RenderDocumentBlocks(VBoxContainer content, string[] lines, int titleLine, List<Chapter> chapters)
    {
        for (int i = 0; i < lines.Length;)
        {
            string line = lines[i].TrimEnd().TrimEnd('\\');
            if (i == titleLine || string.IsNullOrWhiteSpace(line) || line == "---" ||
                line.TrimStart('>', ' ').StartsWith("适用版本：", StringComparison.Ordinal)) { i++; continue; }
            Match heading = Regex.Match(line, @"^(#{1,3})\s+(.+)$");
            if (heading.Success)
            {
                int level = heading.Groups[1].Length;
                var section = new MarginContainer();
                section.AddThemeConstantOverride("margin_top", level == 3 ? 12 : 22);
                section.AddThemeConstantOverride("margin_bottom", 4);
                section.AddChild(ArticleText(heading.Groups[2].Value, level == 3 ? 22 : 25, level == 3 ? "D3DCE4" : Gold));
                content.AddChild(section);
                if (level <= 2) chapters.Add(new(heading.Groups[2].Value, section, level));
                i++; continue;
            }
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                i++; var code = new List<string>();
                while (i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal)) code.Add(lines[i++]);
                if (i < lines.Length) i++;
                var box = BlockPanel("111C28");
                var text = ArticleText(string.Empty, 18, "BDCFDD");
                text.PushMono(); text.AddText(string.Join('\n', code)); text.Pop(); box.AddChild(text); content.AddChild(box);
                continue;
            }
            if (line.StartsWith('>'))
            {
                var quote = new List<string>();
                while (i < lines.Length && lines[i].StartsWith('>')) quote.Add(lines[i++].TrimStart('>', ' ').TrimEnd('\\'));
                AddCallout(content, string.Join('\n', quote)); continue;
            }
            if (line.StartsWith('|'))
            {
                var rows = new List<string[]>();
                while (i < lines.Length && lines[i].StartsWith('|')) rows.Add(TableCells(lines[i++]));
                AddTable(content, rows); continue;
            }
            bool list = Regex.IsMatch(line, @"^(?:[-*] |\d+\. )");
            var paragraph = new List<string>();
            while (i < lines.Length)
            {
                string current = lines[i].TrimEnd().TrimEnd('\\');
                if (string.IsNullOrWhiteSpace(current) || current == "---" || current.StartsWith('>') || current.StartsWith('|') ||
                    current.StartsWith("```", StringComparison.Ordinal) || Regex.IsMatch(current, @"^#{1,3}\s") || i == titleLine) break;
                bool currentList = Regex.IsMatch(current, @"^(?:[-*] |\d+\. )");
                if (paragraph.Count > 0 && currentList != list) break;
                paragraph.Add(current.StartsWith("- ", StringComparison.Ordinal) || current.StartsWith("* ", StringComparison.Ordinal) ? "•  " + current[2..] : current);
                i++;
            }
            if (paragraph.Count > 0) content.AddChild(ArticleText(string.Join('\n', paragraph)));
            else i++;
        }
    }

    private RichTextLabel ArticleText(string text, int size = 20, string color = "C4D0DC", bool firstRelicIcons = true)
    {
        var label = new RichTextLabel
        {
            FitContent = true, ScrollActive = false, BbcodeEnabled = true,
            SelectionEnabled = false, FocusMode = FocusModeEnum.None,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Pass
        };
        label.AddThemeFontOverride("normal_font", _main.GetThemeFont("font", "Label"));
        label.AddThemeFontSizeOverride("normal_font_size", size);
        label.AddThemeColorOverride("default_color", new Color(color));
        label.AddThemeConstantOverride("line_separation", 5);
        label.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        label.MetaClicked += meta =>
        {
            string target = meta.AsString();
            if (target.StartsWith("article:", StringComparison.Ordinal) && Articles.Any(a => a.Id == target[8..])) ShowArticle(target[8..]);
            else if (target.StartsWith("seed:", StringComparison.Ordinal)) OpenSeedRequested?.Invoke(target[5..]);
        };
        RenderInline(label, text, firstRelicIcons);
        return label;
    }

    private PanelContainer BlockPanel(string fill, bool accent = false)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        var style = _palette.Box(fill);
        style.ContentMarginLeft = style.ContentMarginRight = 18;
        style.ContentMarginTop = style.ContentMarginBottom = 13;
        if (accent) { style.BorderColor = new Color("8B806A"); style.BorderWidthLeft = 2; }
        box.AddThemeStyleboxOverride("panel", style);
        return box;
    }

    private void AddCallout(VBoxContainer content, string text, string? caption = null)
    {
        var panel = BlockPanel("223143", true);
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 7);
        panel.AddChild(inner);
        if (caption is not null) inner.AddChild(ArticleText(caption, 13, Gold));
        inner.AddChild(ArticleText(text, 20, "DDD5C4"));
        content.AddChild(panel);
    }

    private static string[] TableCells(string line)
    {
        // Authored links use a pipe inside [[id|label]]; it is not a column boundary.
        var cells = new List<string>(); var cell = new StringBuilder(); bool link = false;
        string value = line.Trim().Trim('|');
        for (int i = 0; i < value.Length; i++)
        {
            if (i + 1 < value.Length && value[i] == '[' && value[i + 1] == '[') link = true;
            if (value[i] == '|' && !link) { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(value[i]);
            if (i > 0 && value[i] == ']' && value[i - 1] == ']') link = false;
        }
        cells.Add(cell.ToString().Trim()); return cells.ToArray();
    }

    private void AddTable(VBoxContainer content, List<string[]> rows)
    {
        bool header = rows.Count > 1 && rows[1].All(c => c.Length > 0 && c.Trim('-', ':', ' ').Length == 0);
        var table = new VBoxContainer(); table.AddThemeConstantOverride("separation", 2);
        int index = 0;
        foreach (string[] cells in rows)
        {
            if (cells.All(c => c.Trim('-', ':', ' ').Length == 0)) continue;
            var row = BlockPanel(index == 0 && header ? "2B3B4E" : index % 2 == 0 ? "1B2939" : "1D2C3D");
            var grid = new HBoxContainer(); grid.AddThemeConstantOverride("separation", 18); row.AddChild(grid);
            foreach (string cell in cells)
                grid.AddChild(ArticleText(cell, 18, index == 0 && header ? Gold : "C4D0DC"));
            table.AddChild(row); index++;
        }
        content.AddChild(table);
    }

    private void RenderTableOfContents(List<Chapter> chapters, ScrollContainer article)
    {
        Label(_main, T("本篇目录", "ON THIS PAGE"), 15, 976, 72, true);
        var nav = NewScroll(_main, 964, 110, 264, 488);
        var items = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        items.AddThemeConstantOverride("separation", 5); nav.AddChild(items);
        var links = new List<Button>();
        for (int i = 0; i < chapters.Count; i++)
        {
            Chapter chapter = chapters[i];
            int number = i;
            var button = new Button { CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            StyleQuietButton(button, false);
            button.AddThemeStyleboxOverride("focus", _palette.FocusRing());
            string chapterTitle = InlinePlainText(chapter.Title);
            var title = ArticleText(chapterTitle, 16, "A7B8CB");
            title.SelectionEnabled = false;
            title.MouseFilter = MouseFilterEnum.Ignore;
            title.Position = new Vector2(32, 8);
            title.Size = new Vector2(202, 0);
            title.Resized += () => button.CustomMinimumSize = new Vector2(0, Math.Max(44, title.GetContentHeight() + 16));
            button.AddChild(title);
            button.ClipContents = true;
            Label(button, i == 0 ? "·" : i.ToString("00"), 12, 9, 12, true);
            button.TooltipText = chapterTitle;
            button.Pressed += () => article.ScrollVertical = number == 0 ? 0 : Math.Max(0, (int)chapter.Anchor.Position.Y + 14);
            items.AddChild(button); links.Add(button);
        }
        var progress = Label(_main, string.Empty, 14, 976, 634, true);
        Rule(_main, 976, 618, 224);
        var top = Button(_main, T("回到开篇  ↑", "Back to top  ↑"), 964, 662, 245, 34, () => article.ScrollVertical = 0);
        StyleQuietButton(top, false);
        int highlighted = -1;
        void Update()
        {
            if (!GodotObject.IsInstanceValid(article) || !article.IsInsideTree()) return;
            double maximum = article.GetVScrollBar().MaxValue - article.GetVScrollBar().Page;
            int percent = maximum > 0 ? (int)Math.Clamp(article.ScrollVertical / maximum * 100, 0, 100) : 100;
            progress.Text = T($"阅读进度  {percent}%", $"Reading progress  {percent}%");
            int current = 0;
            for (int i = 1; i < chapters.Count; i++) if (chapters[i].Anchor.Position.Y <= article.ScrollVertical + 28) current = i;
            if (current == highlighted) return;
            for (int i = 0; i < links.Count; i++) StyleQuietButton(links[i], i == current);
            highlighted = current;
        }
        article.GetVScrollBar().ValueChanged += _ => Update();
        Callable.From(Update).CallDeferred();
    }

    private void AddArticleFooter(VBoxContainer content, ArticleDefinition definition)
    {
        var spacer = new Control { CustomMinimumSize = new Vector2(0, 18) }; content.AddChild(spacer);
        if (definition.RelatedIds.Length > 0)
        {
            content.AddChild(ArticleText(T("继续探索", "EXPLORE FURTHER"), 14, Gold));
            var related = ArticleText(string.Empty, 18);
            foreach (string id in definition.RelatedIds)
                if (Articles.FirstOrDefault(a => a.Id == id) is { } target)
                { AddArticleLink(related, target.Id, ArticleName(target)); related.AddText("    "); }
            content.AddChild(related);
        }
        ArticleDefinition? previous = Articles.FirstOrDefault(a => a.NextId == definition.Id);
        ArticleDefinition? next = Articles.FirstOrDefault(a => a.Id == definition.NextId);
        if (previous is not null || next is not null)
            content.AddChild(ArticleText(T("推荐阅读", "SUGGESTED READING"), 14, Gold));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); content.AddChild(row);
        foreach (var (target, caption) in new[] { (previous, T("←  上一篇", "←  Previous")), (next, T("下一篇  →", "Next  →")) })
        {
            if (target is null) continue;
            var button = new Button { CustomMinimumSize = new Vector2(0, 84), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            button.AddThemeStyleboxOverride("normal", _palette.Box("223244", "34485C", 1));
            button.AddThemeStyleboxOverride("hover", _palette.Box("2B3E52", "667B8D", 1));
            button.AddThemeStyleboxOverride("pressed", _palette.Box("304255"));
            button.AddThemeStyleboxOverride("focus", _palette.FocusRing());
            Label(button, caption, 14, 18, 12, true);
            var name = Label(button, ArticleName(target), 20, 18, 40);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; name.Size = new Vector2(366, 30);
            button.TooltipText = ArticleName(target);
            button.Pressed += () => ShowArticle(target.Id);
            row.AddChild(button);
        }
    }

    private static string ResourceName(ArticleDefinition definition, string language) => $"RolltheSpire2.Encyclopedia.{definition.ResourceStem}_{language}.md";

    private string InlinePlainText(string text) => InlineToken.Replace(text, match =>
    {
        if (match.Groups["bold"].Success) return InlinePlainText(match.Groups["bold"].Value);
        if (match.Groups["content"].Success)
        {
            var (kind, category) = match.Groups["kind"].Value switch
            {
                "card" => (GameContentKind.Card, BaseGameModelKeys.Categories.Card),
                "event" => (GameContentKind.Event, BaseGameModelKeys.Categories.Event),
                "act" => (GameContentKind.Act, BaseGameModelKeys.Categories.Act),
                "encounter" => (GameContentKind.Encounter, BaseGameModelKeys.Categories.Encounter),
                _ => (GameContentKind.Relic, BaseGameModelKeys.Categories.Relic)
            };
            return _contentNames.Resolve(new ModelKey(category, match.Groups["content"].Value), kind);
        }
        if (match.Groups["article"].Success)
            return match.Groups["label"].Success ? match.Groups["label"].Value :
                Articles.FirstOrDefault(a => a.Id == match.Groups["article"].Value) is { } target ? ArticleName(target) : match.Value;
        if (match.Groups["seed"].Success) return match.Groups["seedLabel"].Success ? match.Groups["seedLabel"].Value : match.Groups["seed"].Value;
        return match.Groups["code"].Success ? match.Groups["code"].Value : match.Value;
    });
    private string ArticleName(ArticleDefinition article) => article.Id switch
    {
        NeowKaleidoscopeId => _contentNames.Resolve(BaseGameModelKeys.Relics.Kaleidoscope, GameContentKind.Relic),
        NeowSilkenTressId => _contentNames.Resolve(BaseGameModelKeys.Relics.SilkenTress, GameContentKind.Relic),
        _ => T(article.ZhName, article.EnName)
    };
}
