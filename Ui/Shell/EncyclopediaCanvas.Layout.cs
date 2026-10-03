using Godot;
using System.Reflection;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class EncyclopediaCanvas
{
    private const string Paper = "182433", Quiet = "91A5B9", Gold = "D8C49B";
    private ScrollContainer? _railScroll;
    private readonly HashSet<string> _expandedTopics = new(StringComparer.Ordinal);
    private bool _revealArticle;

    private static ArticleChild[] ChildrenFor(string id) => id switch
    {
        "neow" => NeowChildren, "ancient" => AncientChildren, "shop" => ShopChildren,
        "combat" => CombatChildren, "events" => EventChildren, "map" => MapChildren,
        "relics" => RelicChildren, _ => []
    };

    private static string RootTopic(string id)
    {
        while (Articles.FirstOrDefault(a => a.Id == id)?.ParentId is { } parent) id = parent;
        return id;
    }

    private void Render()
    {
        foreach (Node child in GetChildren().Where(child => child != _rail && child != _main).ToArray())
        { RemoveChild(child); child.QueueFree(); }
        if (_articleId is { } id) _expandedTopics.Add(RootTopic(id));

        Label(this, T("百科", "Encyclopedia"), 28, 8, 5);
        Label(this, T("查清前提，理解结果。", "Understand your results and their premises."), 17, 270, 17, true);
        Label(this, "BETA " + EncyclopediaGameVersion, 14, 850, 19, true);
        _search = new LineEdit
        {
            Position = new Vector2(1036, 4), Size = new Vector2(500, 46),
            PlaceholderText = T("搜索主题、遗物或机制…", "Search topics, relics or mechanics…"),
            Text = _query, ClearButtonEnabled = true
        };
        _search.AddThemeFontSizeOverride("font_size", 17);
        _search.AddThemeColorOverride("font_color", _palette.Color(_palette.Text));
        _search.AddThemeColorOverride("font_placeholder_color", new Color(Quiet));
        _search.AddThemeStyleboxOverride("normal", _palette.Box(Paper, "2C3D50", 1));
        _search.AddThemeStyleboxOverride("focus", _palette.FocusRing());
        AddChild(_search);
        _search.TextChanged += query => { _query = query; _articleId = null; RenderRail(); RenderMain(); };
        _search.TextSubmitted += _ =>
        {
            if (GuideTopics.FirstOrDefault(AdditionalMatches) is { } guide) { ShowArticle(guide.Id); return; }
            if (IntroductionMatches()) { ShowArticle(IntroductionId); return; }
            if (FilterTopics.FirstOrDefault(Matches) is { } topic) ShowArticle(topic.Id);
            else if (FilterTopics.SelectMany(t => ChildrenFor(t.Id)).FirstOrDefault(ChildMatches) is { } child) ShowArticle(child.Id);
            else if (AdditionalTopics.FirstOrDefault(AdditionalMatches) is { } additional) ShowArticle(additional.Id);
        };
        Rule(this, 0, 62, 1536);
        RenderRail();
        RenderMain();
    }

    private void RenderRail()
    {
        int position = GodotObject.IsInstanceValid(_railScroll) ? _railScroll!.ScrollVertical : 0;
        Clear(_rail);
        var scroll = NewScroll(_rail, 0, 0, 268, 700);
        scroll.FollowFocus = false;
        _railScroll = scroll;
        var content = new Control { CustomMinimumSize = new Vector2(250, 0) };
        scroll.AddChild(content);
        Control? selected = null;
        float y = 0;
        var home = RailItem(content, T("百科首页", "Encyclopedia home"), y, _articleId is null, 18, 18,
            () => { _articleId = null; _query = string.Empty; Render(); });
        if (_articleId is null) selected = home;
        y += 46;
        foreach (AdditionalTopic topic in GuideTopics)
        {
            var item = RailItem(content, T(topic.ZhName, topic.EnName), y, _articleId == topic.Id, 17, 18,
                () => ShowArticle(topic.Id));
            if (_articleId == topic.Id) selected = item;
            y += 46;
        }
        var intro = RailItem(content, T("预测为何可行", "Why prediction works"), y, _articleId == IntroductionId, 18, 18,
            () => ShowArticle(IntroductionId));
        if (_articleId == IntroductionId) selected = intro;
        y += 66;
        Label(content, T("游戏机制", "GAME MECHANICS"), 14, 18, y, true);
        y += 34;
        foreach (Topic topic in FilterTopics)
        {
            ArticleChild[] children = ChildrenFor(topic.Id);
            bool expanded = _expandedTopics.Contains(topic.Id);
            var item = RailItem(content, NameOf(topic), y, _articleId == topic.Id, 18, 46, () => ShowArticle(topic.Id));
            AddIcon(item, _icons.Resolve(topic.Icon).Texture, 14, 11, 22);
            if (_articleId == topic.Id) selected = item;
            if (children.Length > 0)
            {
                var toggle = new Button { FocusMode = FocusModeEnum.All };
                toggle.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
                foreach (string state in new[] { "hover", "pressed" })
                {
                    var style = _palette.Box(state == "hover" ? "233447" : "304255");
                    style.ContentMarginLeft = style.ContentMarginRight = 0;
                    style.ContentMarginTop = style.ContentMarginBottom = 0;
                    toggle.AddThemeStyleboxOverride(state, style);
                }
                toggle.AddThemeStyleboxOverride("focus", _palette.FocusRing());
                toggle.Position = new Vector2(222, y + 6);
                toggle.Size = new Vector2(28, 28);
                toggle.Draw += () => toggle.DrawPolyline(expanded
                    ? [new Vector2(8, 11), new Vector2(14, 17), new Vector2(20, 11)]
                    : [new Vector2(11, 8), new Vector2(17, 14), new Vector2(11, 20)],
                    new Color(expanded ? Gold : Quiet), 1.5f, true);
                toggle.Pressed += () =>
                {
                    if (!_expandedTopics.Remove(topic.Id)) _expandedTopics.Add(topic.Id);
                    RenderRail();
                };
                content.AddChild(toggle);
                toggle.FocusEntered += () => RevealRailItem(toggle);
                toggle.TooltipText = T(expanded ? "收起目录" : "展开目录", expanded ? "Collapse topics" : "Expand topics");
            }
            y += 46;
            if (!expanded) continue;
            foreach (ArticleChild child in children)
            {
                float indent = 14 + Math.Max(0, child.Indent - 1) * 12;
                var row = RailItem(content, ChildName(child), y, _articleId == child.Id, 16, 88,
                    () => ShowArticle(child.Id), indent);
                row.TooltipText = T(child.Title, child.EnLabel);
                Texture2D?[] icons = ChildIcons(child);
                // Reserve one aligned group slot, preserving every authored icon.
                float iconSize = icons.Length > 0 ? Math.Min(20, (64f - (icons.Length - 1) * 2) / icons.Length) : 20;
                float groupWidth = icons.Length * iconSize + Math.Max(0, icons.Length - 1) * 2;
                float left = 14 + (64 - groupWidth) / 2;
                for (int i = 0; i < icons.Length; i++)
                {
                    float x = left + i * (iconSize + 2);
                    if (icons[i] is { } icon) AddIcon(row, icon, x, (40 - iconSize) / 2, iconSize);
                    else Label(row, "◇", 16, x, 8, true);
                }
                if (icons.Length == 0) Label(row, "·", 20, 41, 6, true);
                foreach (string state in new[] { "normal", "hover", "pressed" })
                    ((StyleBoxFlat)row.GetThemeStylebox(state)).ContentMarginRight = 10;
                if (_articleId == child.Id) selected = row;
                y += 39;
            }
            y += 8;
        }
        y += 20;
        Rule(content, 18, y, 218); y += 20;
        Label(content, T("多人和模组", "PARTY & MODS"), 14, 18, y, true); y += 32;
        foreach (AdditionalTopic topic in AdditionalTopics)
        {
            var item = RailItem(content, T(topic.ZhName, topic.EnName), y, _articleId == topic.Id, 18, 18,
                () => ShowArticle(topic.Id));
            if (_articleId == topic.Id) selected = item;
            y += 46;
        }
        content.CustomMinimumSize = new Vector2(250, y + 20);
        bool reveal = _revealArticle; _revealArticle = false;
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(scroll) || !scroll.IsInsideTree() || scroll != _railScroll) return;
            scroll.ScrollVertical = position;
            if (reveal && GodotObject.IsInstanceValid(selected)) RevealRailItem(selected!);
        }).CallDeferred();
    }

    private Button RailItem(Control parent, string title, float y, bool selected, int size, int textOffset, Action action, float indent = 0)
    {
        var button = Button(parent, title, 4 + indent, y, 246 - indent, 40, action);
        button.Alignment = HorizontalAlignment.Left;
        button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        button.ClipContents = true;
        StyleQuietButton(button, selected, textOffset);
        button.AddThemeFontSizeOverride("font_size", size);
        button.Size = new Vector2(246 - indent, 40);
        button.FocusEntered += () => RevealRailItem(button);
        return button;
    }

    private void RevealRailItem(Control item)
    {
        if (!GodotObject.IsInstanceValid(_railScroll) || !item.IsInsideTree() || !_railScroll!.IsAncestorOf(item)) return;
        float top = item.Position.Y, bottom = top + item.Size.Y;
        float position = _railScroll.ScrollVertical, height = _railScroll.Size.Y;
        // Keep visible entries in place. Only move the rail when the destination is outside it.
        if (top < position || bottom > position + height)
            _railScroll.ScrollVertical = (int)Math.Max(0, (top + bottom - height) / 2);
    }

    private void StyleQuietButton(Button button, bool selected, int inset = 12)
    {
        foreach (var (state, fill) in new[] { ("normal", selected ? "26384A" : _palette.Canvas), ("hover", selected ? "2D4155" : "1C2A3B"), ("pressed", "304255") })
        {
            var style = _palette.Box(fill);
            style.BorderWidthLeft = selected ? 2 : 0;
            style.BorderColor = new Color(Gold);
            style.ContentMarginLeft = inset; style.ContentMarginRight = inset == 0 ? 0 : 26;
            style.ContentMarginTop = style.ContentMarginBottom = 4;
            button.AddThemeStyleboxOverride(state, style);
        }
        button.AddThemeColorOverride("font_color", new Color(selected ? Gold : "B7C5D3"));
        button.AddThemeFontSizeOverride("font_size", 17);
    }

    private ScrollContainer NewScroll(Control parent, float x, float y, float width, float height)
    {
        var scroll = new ScrollContainer
        {
            Position = new Vector2(x, y), Size = new Vector2(width, height),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            FollowFocus = true
        };
        parent.AddChild(scroll);
        var bar = scroll.GetVScrollBar();
        var track = new StyleBoxEmpty { ContentMarginLeft = 3, ContentMarginRight = 3 };
        bar.AddThemeStyleboxOverride("scroll", track);
        foreach (string state in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
        {
            var thumb = _palette.Box(state == "grabber" ? "405165" : "70869D");
            thumb.ContentMarginLeft = thumb.ContentMarginRight = 3;
            thumb.ContentMarginTop = thumb.ContentMarginBottom = 12;
            bar.AddThemeStyleboxOverride(state, thumb);
        }
        return scroll;
    }

    private void RenderMain()
    {
        RememberReadingPosition();
        Clear(_main);
        _articleScroll = null;
        if (string.IsNullOrWhiteSpace(_query) && Articles.FirstOrDefault(a => a.Id == _articleId) is { } article)
            RenderMarkdownArticle(article);
        else RenderIndex();
    }

    private string TopicDescription(string id) => id switch
    {
        ReadingGuideId => T("说明基准、查询环境与阅读方式", "Scope, query context and where to start"),
        SeedHistoryId => T("种子、Hash、筛选与预测，以及版本历史", "Seeds, hashes, filtering, prediction and version history"),
        FaqId => T("复现前提、常见偏移与问题反馈", "Reproduction, mismatches and feedback"),
        IntroductionId => T("Seed、随机状态与玩家选择", "Seeds, random states and choices"),
        "neow" => T("开局选项、骨骰与遗物的随机结果", "Opening offers, Bones and relic outcomes"),
        "ancient" => T("先古身份、候选池与可选遗物", "Ancient identities, pools and offered relics"),
        "shop" => T("商品如何产生，哪些结果能够预测", "How shop inventories are generated"),
        "combat" => T("卡牌、药水与开局对奖励的影响", "Cards, potions and opening reward effects"),
        "events" => T("遇到什么事件，选项会带来什么", "Event appearance and conditional outcomes"),
        "boss" => T("幕变体、Boss 与世界的共同事实", "Act variants, bosses and the shared world"),
        "map" => T("路线的最好、最坏与地图生成", "Route bounds and map generation"),
        "relics" => T("遗物袋、宝箱与商店的取用顺序", "Relic bags, chests and shop consumption"),
        "transform" => T("多个来源，一起寻找想要的变牌", "Combine transformation sources and targets"),
        "multiplayer" => T("个人条件、共享事实与领取顺序", "Personal conditions and shared facts"),
        MultiplayerSharedId => T("共同世界、个人事件与变形灵林谷特例", "Shared worlds, personal events and Morphic Grove"),
        "mods" => T("模组环境下的候选池与适用范围", "Content pools and compatibility with mods"),
        _ => T("了解规则、前提与具体例子", "Rules, premises and practical examples")
    };

    private void RenderIndex()
    {
        var scroll = NewScroll(_main, 0, 0, 1240, 700);
        var content = new Control { CustomMinimumSize = new Vector2(1216, 0) };
        scroll.AddChild(content);
        bool searching = !string.IsNullOrWhiteSpace(_query);
        Label(content, searching ? T("寻找一个答案", "Find an answer") : T("从一个问题开始", "Start with a question"), 30, 12, 2);
        Label(content, searching ? T($"与「{_query.Trim()}」相关的条目", $"Topics matching “{_query.Trim()}”") :
            T("按当前需要选择入口，也可以直接查下面的游戏主题。", "Choose an entry for your question, or browse the game topics below."), 18, 12, 49, true);
        float y = 94;
        if (!searching)
        {
            float topicsTop = y + 252;
            var entries = new (string Title, string Description, Action Open)[]
            {
                (T("开始使用", "Getting started"), T("先了解说明范围与复现前提", "Reading scope and reproduction premises"), () => ShowArticle(ReadingGuideId)),
                (T("结果与预测不同", "Results do not match"), T("查常见原因，找到反馈入口", "Common causes and when to send feedback"), () => ShowArticle(FaqId)),
                (T("种子：我们在搜索什么？", "Seeds: What are we searching?"), T("理解种子、Hash，以及筛选与预测的区别", "Seeds, hashes, filtering and prediction"), () => ShowArticle(SeedHistoryId)),
                (T("深入了解预测原理", "How prediction works"), T("理解 Seed、随机状态与选择的关系", "How seeds, random states and choices relate"), () => ShowArticle(IntroductionId))
            };
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var card = Button(content, string.Empty, 12 + i % 2 * 604, y + i / 2 * 120, 586, 104, entry.Open);
                card.AddThemeStyleboxOverride("normal", _palette.Box("233446", "50616B", 1));
                var title = Label(card, entry.Title, 23, 22, 19);
                title.Size = new Vector2(528, 32);
                title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                var description = Label(card, entry.Description, 17, 22, 61, true);
                description.Size = new Vector2(538, 28);
                description.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            }
            y = topicsTop;
        }
        Label(content, searching ? T("匹配条目", "MATCHING TOPICS") : T("按游戏主题查找", "BROWSE GAME TOPICS"), 16, 12, y, true);
        y += 36;
        var cards = new List<(string Id, string Name, string Description, Texture2D?[] Icons)>();
        if (searching)
        {
            foreach (AdditionalTopic topic in GuideTopics.Where(AdditionalMatches))
                cards.Add((topic.Id, T(topic.ZhName, topic.EnName), TopicDescription(topic.Id), []));
            if (IntroductionMatches())
                cards.Add((IntroductionId, T(IntroductionTitle, "Why can the future be predicted?"), TopicDescription(IntroductionId), []));
        }
        foreach (Topic topic in FilterTopics.Where(Matches))
            cards.Add((topic.Id, NameOf(topic), TopicDescription(topic.Id), [_icons.Resolve(topic.Icon).Texture]));
        foreach (ArticleChild child in FilterTopics.SelectMany(t => ChildrenFor(t.Id)).Where(ChildMatches))
            cards.Add((child.Id, ChildName(child), T(child.Title, child.EnLabel), ChildIcons(child)));
        foreach (AdditionalTopic topic in AdditionalTopics.Where(t => !searching || AdditionalMatches(t)))
            cards.Add((topic.Id, T(topic.ZhName, topic.EnName), TopicDescription(topic.Id), []));
        for (int i = 0; i < cards.Count; i++)
        {
            var item = cards[i];
            var card = Button(content, string.Empty, 12 + i % 3 * 402, y + i / 3 * 132, 386, 116, () => ShowArticle(item.Id));
            card.AddThemeStyleboxOverride("normal", _palette.Box(Paper, "2A3A4B", 1));
            for (int j = 0; j < item.Icons.Length; j++)
                AddIcon(card, item.Icons[j], 22 + j * 32, 23, 28);
            if (item.Icons.Length == 0) Label(card, item.Id == "multiplayer" ? "◇" : "＋", 27, 24, 17, true);
            float titleX = Math.Max(66, 34 + item.Icons.Length * 32);
            var name = Label(card, item.Name, 22, titleX, 20);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.Size = new Vector2(336 - titleX, 34);
            Label(card, "›", 24, 350, 21, true);
            var description = Label(card, item.Description, 16, 22, 64, true);
            description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            description.Size = new Vector2(340, 44);
        }
        if (cards.Count == 0 && !IntroductionMatches())
        {
            Label(content, T("暂时没有找到这个条目", "No matching topic yet"), 23, 20, y + 38);
            Label(content, T("可以试试「骨骰」「地图」或遗物的名称。", "Try “Bones”, “map” or a relic name."), 18, 20, y + 82, true);
        }
        content.CustomMinimumSize = new Vector2(1216, y + Math.Max(1, (cards.Count + 2) / 3) * 132 + 16);
    }

    private void ShowArticle(string id)
    {
        _query = string.Empty; _articleId = id; _revealArticle = true;
        _expandedTopics.Add(RootTopic(id));
        Render();
    }

    private Texture2D?[] ChildIcons(ArticleChild child) => child.UseMapUnknownIcon
        ? [ResolveMapUnknownIcon()]
        : child.IconKeys.Select(key => _entryIcons.Resolve(key, child.IconKind, child.IconVariant).Texture).ToArray();

    private static void AddIcon(Control parent, Texture2D? texture, float x, float y, float size)
    {
        if (texture is null) return;
        parent.AddChild(new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = texture, Position = new Vector2(x, y), Size = new Vector2(size, size),
            MouseFilter = MouseFilterEnum.Ignore
        });
    }

    private static Texture2D? ResolveMapUnknownIcon()
    {
        try
        {
            string? name = typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NNormalMapPoint)
                .GetMethod("IconName", BindingFlags.NonPublic | BindingFlags.Static)?
                .Invoke(null, [MegaCrit.Sts2.Core.Map.MapPointType.Unknown]) as string;
            return string.IsNullOrWhiteSpace(name) ? null : ResourceLoader.Load<Texture2D>($"res://images/atlases/ui_atlas.sprites/map/icons/{name}.tres");
        }
        catch { return null; }
    }
}
