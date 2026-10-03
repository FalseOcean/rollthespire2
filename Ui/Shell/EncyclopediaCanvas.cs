using Godot;
using System.Reflection;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

/// <summary>
/// Read-only encyclopedia shell. The topic catalog is intentionally independent of
/// Search predicates so future rules and object references can share this surface.
/// </summary>
internal sealed partial class EncyclopediaCanvas : Control
{
    private const string IntroductionId = "why-predictable";
    private const string ReadingGuideId = "reading-guide";
    private const string SeedHistoryId = "seed-history";
    private const string FaqId = "faq";
    private const string CrystalSphereId = "crystal-sphere";
    private const string IntroductionTitle = "为什么未来可以被预测？";
    private const string NeowOfferId = "neow-offer";
    private const string NeowOfferTitle = "涅奥候选是怎样产生的？";
    private const string NeowKaleidoscopeId = "neow-kaleidoscope";
    private const string NeowKaleidoscopeTitle = "万花筒：跨角色的开局卡牌";
    private const string NeowSilkenTressId = "neow-silken-tress";
    private const string NeowSilkenTressTitle = "华美发束：下一组卡牌奖励";
    private const string NeowCapsuleId = "neow-capsule";
    private const string NeowCapsuleTitle = "扭蛋：从涅奥进入遗物袋";
    private const string NeowTransformId = "neow-transform";
    private const string NeowTransformTitle = "树叶药膏：为什么变牌结果可以提前检查";
    private const string NeowBonesCurseId = "neow-bones-curse";
    private const string NeowBonesCurseTitle = "骨骰最终诅咒：玩家选择为什么会影响结果";
    private const string AncientDarvId = "ancient-darv";
    private const string AncientDarvTitle = "达弗：共享池先古与先古遗物";
    private const string AncientPaelId = "ancient-pael";
    private const string AncientPaelTitle = "佩尔：三个候选池与不均匀权重";
    private const string AncientTezcataraId = "ancient-tezcatara";
    private const string AncientTezcataraTitle = "特兹卡塔拉：三个独立的遗物位置";
    private const string AncientOrobasId = "ancient-orobas";
    private const string AncientOrobasTitle = "欧洛巴斯：分支候选与条件选项";
    private const string AncientAct3Id = "ancient-act3";
    private const string AncientAct3Title = "第三幕先古：总池与固定位置";
    private const string ShopStabilityId = "shop-stability";
    private const string ShopStabilityTitle = "稳定与不稳定：商店预测的三个层级";
    private const string CombatNeowId = "combat-neow";
    private const string CombatNeowTitle = "Neow：开局如何改变后续战斗奖励？";
    private const string CombatRarityRelicsId = "combat-rarity-relics";
    private const string CombatRarityRelicsTitle = "战斗奖励：稀有牌与改变奖励的遗物";
    private const string EventAppearanceId = "event-appearance";
    private const string EventAppearanceTitle = "事件出现性：一个问号房到底会遇到什么？";
    private const string EventResultsId = "event-results";
    private const string EventResultsTitle = "事件结果：哪些结果值得预测？";
    private const string StableEventResultsId = "event-stable-results";
    private const string StableEventResultsTitle = "稳定事件结果：色彩哲学家、假商人与垃圾堆";
    private const string TransformEventResultsId = "event-transform-results";
    private const string TransformEventResultsTitle = "变牌事件：默认变化初始牌";
    private const string MultiplayerSharedId = "multiplayer-shared";
    private const string MapRoutesId = "map-routes";
    private const string MapRoutesTitle = "地图路线：最好与最坏能到什么程度？";
    private const string MapGenerationId = "map-generation";
    private const string MapGenerationTitle = "地图生成：剪枝与修补";
    private const string ChestRelicsId = "relic-chests";
    private const string ChestRelicsTitle = "宝箱房的遗物";
    private const string ShopRelicConsumptionId = "relic-shop-consumption";
    private const string ShopRelicConsumptionTitle = "商店如何消费遗物队列？";
    private sealed record Topic(string Id, string TextKey, SearchCategoryKey Icon);
    private sealed record AdditionalTopic(string Id, string ZhName, string EnName, string SearchTerms);
    private sealed record ArticleChild(
        string Id, string ZhLabel, string EnLabel, string Title, string SearchTerms, ModelKey[] IconKeys,
        GameContentKind IconKind = GameContentKind.Relic, IconVariant IconVariant = IconVariant.Small,
        bool UseMapUnknownIcon = false, int Indent = 1);
    private sealed record ArticleDefinition(
        string Id, string ResourceStem, string ZhName, string EnName,
        string? ParentId, string? NextId, string[] RelatedIds,
        bool ReturnToSearch = true, string? GameVersion = null);

    private const string EncyclopediaGameVersion = "0.111.0";
    private static readonly ArticleDefinition[] Articles =
    [
        new(ReadingGuideId, "reading_guide", "阅读与适用说明", "Reading & scope", null, SeedHistoryId,
            [SeedHistoryId, FaqId, "multiplayer", "mods"], false),
        new(SeedHistoryId, "seed_history", "种子：我们到底在搜索什么？", "Seeds: What Are We Searching?", null, IntroductionId,
            [ReadingGuideId, IntroductionId, FaqId], false),
        new(FaqId, "faq", "常见问题与结果排查", "FAQ & result checks", null, null,
            [ReadingGuideId, IntroductionId, "events", "shop", "combat", "multiplayer"], false),
        new(IntroductionId, "why_future_can_be_predicted", "为什么未来可以被预测？",
            "Why can the future be predicted?", null, "neow", [SeedHistoryId, ReadingGuideId, FaqId, "neow", "ancient"], false),
        new("neow", "neow", "涅奥", "Neow", null, "ancient",
            [NeowOfferId, NeowKaleidoscopeId, "ancient"]),
        new(NeowOfferId, "neow_offer", "涅奥候选与骨骰", "Offer & Bones", "neow",
            null, [NeowBonesCurseId]),
        new(NeowKaleidoscopeId, "neow_kaleidoscope", "万花筒", "Kaleidoscope", "neow",
            null, [NeowSilkenTressId, NeowCapsuleId]),
        new(NeowSilkenTressId, "neow_silken_tress", "华美发束", "Silken Tress", "neow",
            null, [NeowKaleidoscopeId, NeowBonesCurseId]),
        new(NeowCapsuleId, "neow_capsule", "扭蛋", "Capsules", "neow",
            null, [NeowKaleidoscopeId, NeowBonesCurseId]),
        new(NeowTransformId, "neow_transform", "树叶药膏与新叶", "Leafy Poultice & New Leaf",
            "neow", null, [NeowOfferId, "transform"]),
        new(NeowBonesCurseId, "neow_bones_curse", "骨骰最终诅咒", "Bones' Final Curse",
            "neow", null, [NeowOfferId, NeowCapsuleId]),
        new("ancient", "ancient", "先古之民", "Ancients", null, "combat",
            [AncientDarvId, AncientPaelId, AncientTezcataraId, AncientOrobasId, AncientAct3Id,
                "neow", NeowOfferId]),
        new(AncientDarvId, "ancient_darv", "达弗", "Darv", "ancient", null,
            ["ancient", AncientPaelId]),
        new(AncientPaelId, "ancient_pael", "佩尔", "Pael", "ancient", null,
            ["ancient", AncientDarvId, AncientTezcataraId]),
        new(AncientTezcataraId, "ancient_tezcatara", "特兹卡塔拉", "Tezcatara", "ancient",
            null, ["ancient", AncientPaelId, AncientOrobasId]),
        new(AncientOrobasId, "ancient_orobas", "欧洛巴斯", "Orobas", "ancient",
            null, ["ancient", AncientTezcataraId, AncientAct3Id]),
        new(AncientAct3Id, "ancient_act3", "第三幕先古", "Act 3 Ancients", "ancient", null,
            ["ancient", AncientOrobasId]),
        new("shop", "shop", "商店", "Shop", null, "events",
            [ShopStabilityId, ShopRelicConsumptionId, IntroductionId]),
        new(ShopStabilityId, "shop_stability", "稳定与不稳定", "Stable & unstable",
            "shop", null, ["shop", "combat"]),
        new("combat", "combat", "战斗奖励", "Combat rewards", null, "relics",
            [CombatNeowId, CombatRarityRelicsId, IntroductionId, ShopStabilityId]),
        new(CombatNeowId, "combat_neow", "涅奥与战斗奖励", "Neow & combat rewards",
            "combat", null, ["combat", CombatRarityRelicsId, "neow"]),
        new(CombatRarityRelicsId, "combat_rarity_relics", "稀有牌与遗物",
            "Rare cards & relics", "combat", null, ["combat", CombatNeowId, "events"]),
        new("events", "events", "事件", "Events", null, "boss",
            [EventAppearanceId, EventResultsId, StableEventResultsId,
                TransformEventResultsId, CrystalSphereId, IntroductionId, "combat"]),
        new(EventAppearanceId, "event_appearance", "事件出现性", "Event appearance",
            "events", null, ["events", EventResultsId, "map"]),
        new(EventResultsId, "event_results", "事件结果", "Event results",
            "events", null,
            [StableEventResultsId, TransformEventResultsId, "events", EventAppearanceId]),
        new(StableEventResultsId, "event_stable_results", "稳定事件结果", "Stable event results",
            EventResultsId, null,
            [EventResultsId, TransformEventResultsId, EventAppearanceId]),
        new(TransformEventResultsId, "event_transform_results", "变牌事件", "Transform events",
            EventResultsId, null, [EventResultsId, StableEventResultsId, "transform", MultiplayerSharedId]),
        new(CrystalSphereId, "crystal_sphere", "水晶球", "Crystal Sphere", "events", null,
            ["events", FaqId, ReadingGuideId], false),
        new("boss", "boss", "首领 / 变体", "Bosses & Act variants", null, "map",
            ["map", IntroductionId, "ancient", "events"]),
        new("map", "map", "地图", "Map", null, "transform",
            [MapRoutesId, MapGenerationId, "boss", "events", EventAppearanceId]),
        new(MapRoutesId, "map_routes", "地图路线", "Map routes", "map", null,
            ["map", MapGenerationId, EventAppearanceId]),
        new(MapGenerationId, "map_generation", "地图生成", "Map generation", "map", null,
            ["map", MapRoutesId]),
        new("relics", "relics", "遗物", "Relics", null, "shop",
            [ChestRelicsId, ShopRelicConsumptionId, "shop", "neow", IntroductionId]),
        new(ChestRelicsId, "relic_chests", "宝箱房", "Treasure chests",
            "relics", null, ["relics", ShopRelicConsumptionId]),
        new(ShopRelicConsumptionId, "relic_shop_consumption", "商店与遗物队列",
            "Shops & relic bags", "relics", null, ["relics", "shop", ShopStabilityId]),
        new("transform", "transform", "变牌组合", "Combined transformations", null, "multiplayer",
            [NeowTransformId, TransformEventResultsId, IntroductionId]),
        new("multiplayer", "multiplayer", "多人", "Multiplayer", null, "mods",
            [MultiplayerSharedId, "mods", "ancient", "map", "neow"], ReturnToSearch: false),
        new(MultiplayerSharedId, "multiplayer_shared", "多人共享条件", "Shared party conditions", "multiplayer", null,
            ["multiplayer", EventAppearanceId, TransformEventResultsId, StableEventResultsId], ReturnToSearch: false),
        new("mods", "mods", "模组", "Mods", null, null,
            ["multiplayer", IntroductionId], ReturnToSearch: false)
    ];

    private static readonly ArticleChild[] NeowChildren =
    [
        new(NeowOfferId, "涅奥候选与骨骰", "Offer & Bones", NeowOfferTitle,
            "Offer 骨骰 Neow 诅咒池", [BaseGameModelKeys.Relics.NeowsBones]),
        new(NeowKaleidoscopeId, "万花筒", "Kaleidoscope", NeowKaleidoscopeTitle,
            "万花筒 Kaleidoscope 跨角色 金卡 Neow", [BaseGameModelKeys.Relics.Kaleidoscope]),
        new(NeowSilkenTressId, "华美发束", "Silken Tress", NeowSilkenTressTitle,
            "华美发束 丝绸发束 Silken Tress 华彩 卡牌奖励 Neow", [BaseGameModelKeys.Relics.SilkenTress]),
        new(NeowCapsuleId, "扭蛋", "Capsules", NeowCapsuleTitle,
            "扭蛋 巨大扭蛋 小扭蛋 Large Capsule Small Capsule 遗物袋 Neow",
            [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule]),
        new(NeowTransformId, "树叶药膏与新叶", "Leafy Poultice & New Leaf", NeowTransformTitle,
            "树叶药膏 新叶 Leafy Poultice New Leaf 变牌 Transformations Niche Neow",
            [BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf]),
        new(NeowBonesCurseId, "骨骰最终诅咒", "Bones' Final Curse", NeowBonesCurseTitle,
            "骨骰 最终诅咒 Neow's Bones Curse Niche Whetstone War Paint 玩家选择",
            [BaseGameModelKeys.Relics.NeowsBones])
    ];

    private static readonly ArticleChild[] AncientChildren =
    [
        new(AncientDarvId, "达弗", "Darv", AncientDarvTitle,
            "达弗 Darv 共享池 先古遗物 Dusty Tome Beta 0.111.0",
            [new ModelKey(BaseGameModelKeys.Categories.Event, "DARV")],
            GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon),
        new(AncientPaelId, "佩尔", "Pael", AncientPaelTitle,
            "佩尔 Pael 三个候选池 权重 Goopy IsRemovable Event Pet Beta 0.111.0",
            [new ModelKey(BaseGameModelKeys.Categories.Event, "PAEL")],
            GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon),
        new(AncientTezcataraId, "特兹卡塔拉", "Tezcatara", AncientTezcataraTitle,
            "特兹卡塔拉 Tezcatara 三个位置 Nutritious Soup Strike Beta 0.111.0",
            [new ModelKey(BaseGameModelKeys.Categories.Event, "TEZCATARA")],
            GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon),
        new(AncientOrobasId, "欧洛巴斯", "Orobas", AncientOrobasTitle,
            "欧洛巴斯 Orobas Sea Glass Prismatic Gem Touch of Orobas Archaic Tooth Beta 0.111.0",
            [new ModelKey(BaseGameModelKeys.Categories.Event, "OROBAS")],
            GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon),
        new(AncientAct3Id, "第三幕先古", "Act 3 Ancients", AncientAct3Title,
            "Act 3 第三幕 Nonupeipe Tanx Vakuu Swift Instinct Beautiful Bracelet Tri-Boomerang",
            [new ModelKey(BaseGameModelKeys.Categories.Event, "NONUPEIPE"),
                new ModelKey(BaseGameModelKeys.Categories.Event, "TANX"),
                new ModelKey(BaseGameModelKeys.Categories.Event, "VAKUU")],
            GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon)
    ];

    private static readonly ArticleChild[] ShopChildren =
    [
        new(ShopStabilityId, "稳定与不稳定", "Stable & unstable", ShopStabilityTitle,
            "假商人 送货员 Fake Merchant Courier 商店 Shops RNG 遗物队列 稳定 不稳定",
            [new ModelKey(BaseGameModelKeys.Categories.Relic, "THE_COURIER")])
    ];

    private static readonly ArticleChild[] CombatChildren =
    [
        new(CombatNeowId, "涅奥与战斗奖励", "Neow & combat rewards", CombatNeowTitle,
            "Neow 涅奥 开局 战斗奖励 Rewards RNG 连续 骨骰",
            [new ModelKey(BaseGameModelKeys.Categories.Event, "NEOW")],
            GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon),
        new(CombatRarityRelicsId, "稀有牌与遗物", "Rare cards & relics", CombatRarityRelicsTitle,
            "战斗奖励 稀有牌 Rare Prayer Wheel 转经轮 Lasting Candy 吃不完的糖 White Beast Statue",
            [new ModelKey(BaseGameModelKeys.Categories.Relic, "PRAYER_WHEEL"),
                new ModelKey(BaseGameModelKeys.Categories.Relic, "LASTING_CANDY")])
    ];

    private static readonly ArticleChild[] EventChildren =
    [
        new(EventAppearanceId, "事件出现性", "Event appearance", EventAppearanceTitle,
            "事件 问号 地图 出现性 Event Unknown Room Shared 队列",
            [], UseMapUnknownIcon: true),
        new(EventResultsId, "事件结果", "Event results", EventResultsTitle,
            "事件 随机 结果 变牌 Event Result RNG", []),
        new(StableEventResultsId, "稳定事件结果", "Stable event results", StableEventResultsTitle,
            "色彩哲学家 假商人 垃圾堆 Colorful Philosophers Fake Merchant Junk Heap",
            [], Indent: 2),
        new(TransformEventResultsId, "变牌事件", "Transform events", TransformEventResultsTitle,
            "事件结果 变牌 初始牌 Strike Defend Transformation Group",
            [], Indent: 2),
        new(CrystalSphereId, "水晶球", "Crystal Sphere", "水晶球：当前事件的奖励与点击路线",
            "水晶球 Crystal Sphere 局内 预测 奖励 点击 翻牌 金币", [])
    ];

    private static readonly ArticleChild[] MapChildren =
    [
        new(MapRoutesId, "地图路线", "Map routes", MapRoutesTitle,
            "Guaranteed Reachable Max Forced Monster Prefix 精英 火堆 问号 最好 最坏", []),
        new(MapGenerationId, "地图生成", "Map generation", MapGenerationTitle,
            "Prune Repair 地图生成 剪枝 修复 路线 房间", [])
    ];

    private static readonly ArticleChild[] RelicChildren =
    [
        new(ChestRelicsId, "宝箱房", "Treasure chests", ChestRelicsTitle,
            "宝箱 Shared Relic Bag 共享遗物袋 Common Uncommon Rare", []),
        new(ShopRelicConsumptionId, "商店与遗物队列", "Shops & relic bags",
            ShopRelicConsumptionTitle, "商店 遗物队列 后端 Back Courier 可出售 Shop", [])
    ];

    private static readonly Topic[] FilterTopics =
    [
        new("neow", "query.domain.neow", SearchCategoryKey.Neow),
        new("ancient", "query.domain.ancients", SearchCategoryKey.Ancient),
        new("shop", "query.domain.shop", SearchCategoryKey.Shop),
        new("combat", "query.domain.combat_rewards", SearchCategoryKey.CombatReward),
        new("events", "query.domain.events", SearchCategoryKey.Event),
        new("boss", "query.domain.boss_variant", SearchCategoryKey.BossIdentity),
        new("map", "query.domain.map", SearchCategoryKey.BossAndMap),
        new("relics", "query.domain.relics", SearchCategoryKey.Relic),
        new("transform", "query.domain.transform", SearchCategoryKey.Transformation)
    ];

    private static readonly AdditionalTopic[] GuideTopics =
    [
        new(ReadingGuideId, "阅读与适用说明", "Reading & scope",
            "开始使用 适用 前提 解锁 版本 原版 单人 reading scope unlocks version baseline"),
        new(SeedHistoryId, "种子与搜索", "Seeds & search",
            "种子 Seed Hash 哈希 搜索 筛选 预测 字符 空间 碰撞 历史 版本 107 109 111 GPU filtering prediction history collision"),
        new(FaqId, "常见问题与结果排查", "FAQ & result checks",
            "常见问题 FAQ 结果不同 偏移 错误 反馈 bug feedback mismatch troubleshooting Developer Notes 开发者笔记")
    ];

    private static readonly AdditionalTopic[] AdditionalTopics =
    [
        new("multiplayer", "多人", "Multiplayer",
            "多人 玩家 Slot P1 P2 P3 P4 共享状态 世界 随机 Multiplayer"),
        new(MultiplayerSharedId, "多人共享条件", "Shared party conditions",
            "多人 共享 条件 事件 变形灵林谷 变牌 同角色 双铁甲 黑拥 无惧 Morphic Grove shared events transforms party"),
        new("mods", "模组", "Mods",
            "Mod 模组 兼容 运行时 候选池 随机机制 第三方")
    ];

    private readonly WorkspacePalette _palette = WorkspacePalette.Canonical;
    private readonly IUiTextProvider _zhText = JsonUiTextProvider.CreateUi13("zh");
    private readonly IUiTextProvider _enText = JsonUiTextProvider.CreateUi13("en");
    private readonly ISearchCategoryTabIconProvider _icons =
        new SearchCategoryTabIconProvider(new ReflectionGameIconResolver("encyclopedia-categories"));
    private readonly IGameIconResolver _entryIcons =
        new ReflectionGameIconResolver("encyclopedia-entries");
    private IGameContentNameResolver _contentNames = RuntimeGameContentNameResolver.Create("zh");
    private readonly Control _main = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly Control _rail = new() { MouseFilter = MouseFilterEnum.Ignore };
    private LineEdit? _search;
    private string _language = "zh";
    private string _query = string.Empty;
    private string? _articleId;

    public event Action? ReturnToSearchRequested;
    public event Action<string>? OpenSeedRequested;

    public EncyclopediaCanvas()
    {
        Name = "EncyclopediaCanvas";
        AddChild(_rail);
        AddChild(_main);
        _rail.Position = new Vector2(0, 72);
        _rail.Size = new Vector2(268, 700);
        _main.Position = new Vector2(296, 72);
        _main.Size = new Vector2(1240, 700);
        Render();
    }

    public void Refresh(string language)
    {
        _language = language == "en" ? "en" : "zh";
        _contentNames = RuntimeGameContentNameResolver.Create(_language);
        Render();
    }

    public void Open(string? topicId)
    {
        _articleId = Articles.Any(article => article.Id == topicId) ||
                     FilterTopics.Any(topic => topic.Id == topicId)
            ? topicId : null;
        _query = string.Empty;
        Render();
    }

    private string T(string zh, string en) => _language == "zh" ? zh : en;
    private string NameOf(Topic topic) => (_language == "zh" ? _zhText : _enText).Get(topic.TextKey);
    private string ChildName(ArticleChild child) => child.Id switch
    {
        NeowKaleidoscopeId => _contentNames.Resolve(BaseGameModelKeys.Relics.Kaleidoscope, GameContentKind.Relic),
        NeowSilkenTressId => _contentNames.Resolve(BaseGameModelKeys.Relics.SilkenTress, GameContentKind.Relic),
        _ => T(child.ZhLabel, child.EnLabel)
    };

    private bool Matches(Topic topic) => string.IsNullOrWhiteSpace(_query) ||
        _zhText.Get(topic.TextKey).Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase) ||
        _enText.Get(topic.TextKey).Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool IntroductionMatches() => string.IsNullOrWhiteSpace(_query) ||
        IntroductionTitle.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase) ||
        "Why can the future be predicted?".Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase) ||
        "预测为何可行 Seed RNG 随机 未来".Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool ChildMatches(ArticleChild child) => !string.IsNullOrWhiteSpace(_query) &&
        (child.Title.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase) ||
         child.ZhLabel.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase) ||
         child.EnLabel.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase) ||
         child.SearchTerms.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase) ||
         child.IconKeys.Any(key => _contentNames.Resolve(key, child.IconKind)
             .Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase)));

    private bool AdditionalMatches(AdditionalTopic topic) => !string.IsNullOrWhiteSpace(_query) &&
        (topic.ZhName.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase) ||
         topic.EnName.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase) ||
         topic.SearchTerms.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase));

    private Label Label(Control parent, string text, int size, float x, float y, bool secondary = false)
    {
        Label label = _palette.Label(text, size, secondary);
        label.Position = new Vector2(x, y);
        parent.AddChild(label);
        return label;
    }

    private Button Button(Control parent, string text, float x, float y, float width, float height, Action action, bool selected = false)
    {
        Button button = _palette.Button(text, selected: selected);
        button.Position = new Vector2(x, y);
        button.CustomMinimumSize = Vector2.Zero;
        button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        button.Size = new Vector2(width, height);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private void Panel(Control parent, float x, float y, float width, float height, string fill)
    {
        var panel = new Panel { Position = new Vector2(x, y), Size = new Vector2(width, height), MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", _palette.Box(fill, _palette.Line, 1));
        parent.AddChild(panel);
    }

    private void Rule(Control parent, float x, float y, float width, float height = 1) =>
        parent.AddChild(new ColorRect
        {
            Position = new Vector2(x, y), Size = new Vector2(width, height),
            Color = _palette.Color(_palette.Line), MouseFilter = MouseFilterEnum.Ignore
        });

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
