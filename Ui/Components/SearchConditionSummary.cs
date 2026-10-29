using RolltheSpire2.Ui.Workspace;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

// Shared condition presentation for presets and history. No query mutation or capture.
internal sealed partial class SearchConditionSummary : VBoxContainer
{
    private IUiTextProvider? _text;
    private IUiTextProvider? _workbenchText;
    private IGameContentNameResolver? _names;
    private VBoxContainer _summary => this;
    public void Bind(SearchPresetDefinition preset, string language, IGameContentNameResolver names)
    {
        foreach (bool _ in BindPresentation(preset, language, names)) { }
    }

    // The calling page owns enumeration/cancellation and the main-thread budget.
    // Keeping the iterator here preserves the shared condition text and structure.
    internal IEnumerable<bool> BindPresentation(SearchPresetDefinition preset, string language, IGameContentNameResolver names)
    {
        foreach (bool step in ClearPresentation()) yield return step;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 12);
        _text = JsonUiTextProvider.CreatePredictorUi13(language);
        _workbenchText = JsonUiTextProvider.CreateUi13(language);
        _names = names;
        foreach (bool step in BuildSemanticSummary(preset)) yield return step;
    }

    internal IEnumerable<bool> ClearPresentation() => ReleaseChildren(this);

    private static IEnumerable<bool> ReleaseChildren(Node parent)
    {
        while (parent.GetChildCount() > 0)
        {
            Node child = parent.GetChild(0);
            foreach (bool step in ReleaseChildren(child)) yield return step;
            parent.RemoveChild(child); child.QueueFree();
            yield return true;
        }
    }
    private string Local(string zh, string en) => _text?.LanguageCode.StartsWith("zh") == true ? zh : en;
    private IEnumerable<bool> BuildSemanticSummary(SearchPresetDefinition preset)
    {
        if (_text is null || _names is null) yield break;
        if (preset.Workbench is { } workbench)
        {
            foreach (bool step in BuildWorkbenchSummary(workbench)) yield return step;
            yield break;
        }
        _summary.AddChild(MainPanelTheme.Label(_text.Get("ui1.preset.intent_unresolved"), Ui1TextRole.Warning, true));
        yield return true;
        if (preset.UnresolvedAsset is { } unresolved)
        {
            _summary.AddChild(MainPanelTheme.Label(unresolved.Issue, Ui1TextRole.Meta, true));
            yield return true;
        }
    }

    private IEnumerable<bool> BuildWorkbenchSummary(WorkbenchSearchDraft draft)
    {
        if (draft.Players.Count == 0)
        {
            foreach (bool step in AddQuerySummary(draft.Query, Local("单人条件", "Solo conditions"), collection: draft.CombatRewardEditor?.ExcludedCollection)) yield return step;
        }
        else
        {
            foreach (bool step in AddQuerySummary(draft.Query, Local("整桌共享条件", "Shared party conditions"))) yield return step;
            foreach (WorkbenchPlayerDraft player in draft.Players)
            {
                string source = player.UnlockSource switch
                {
                    "AssumedFullyUnlocked" => Local("假定全解锁", "Assumed fully unlocked"),
                    "CapturedLobbySlot" or "CurrentContext" => Local("读取当前上下文", "Read current context"),
                    _ => player.UnlockSource
                };
                string title = $"P{player.Slot + 1} · {_names!.Resolve(player.Character, GameContentKind.Character)} · {source}";
                PlayerOfferQuery? personal = draft.Query.Players.FirstOrDefault(p => p.Slot == player.Slot);
                if (personal is null)
                {
                    foreach (bool step in AddSummaryCard(title, [Local("此席位的条件无法解析", "This slot's conditions are unavailable")])) yield return step;
                    continue;
                }
                foreach (bool step in AddQuerySummary(personal.Conditions, title, Offers(), player.CombatRewardEditor?.ExcludedCollection)) yield return step;
                IEnumerable<string> Offers()
                {
                    if (!personal.Offers.IsEmpty) yield return "N · " + FormatKeyFilter(personal.Offers, GameContentKind.Relic);
                    if (personal.SelectedOption is { } plan) yield return "N · " + _names.Resolve(plan.Option, GameContentKind.Relic);
                    foreach (var result in personal.Results) yield return FormatStructuredEffect(result);
                }
            }
        }
    }

    private IEnumerable<bool> AddQuerySummary(SearchQuery query, string title, IEnumerable<string>? extra = null, CardCollectionSnapshot? collection = null)
    {
        foreach (bool step in AddSummaryCard(title, QueryLines(query, extra, collection))) yield return step;
    }

    private IEnumerable<string> QueryLines(SearchQuery query, IEnumerable<string>? extra, CardCollectionSnapshot? collection)
    {
        foreach (string line in extra ?? []) yield return line;
        if (query.OpeningRoute is { } route) yield return "N · " + _names!.Resolve(route.RouteRelicKey, GameContentKind.Relic);
        if (query.OpeningRouteRelicRequirement is { } bones)
            yield return "N · " + string.Join(bones.OrderMode == BonesRouteOrderMode.ExactOrder ? " → " : " + ", bones.RequiredRelicKeys.Select(k => _names!.Resolve(k, GameContentKind.Relic)));
        foreach (string line in query.StructuredOpeningEffects.Select(FormatStructuredEffect)) yield return line;
        if (!query.LegacyNeow.NeowRelics.IsEmpty) yield return "N · " + FormatKeyFilter(query.LegacyNeow.NeowRelics, GameContentKind.Relic);
        if (query.LegacyNeow.RequiredFinalCurse is { } curse) yield return "N · " + _text!.Get(Ui1TextKey.SearchFinalCurse) + " · " + _names!.Resolve(curse, GameContentKind.Card);
        foreach (string line in query.RelicSequenceConstraints.Select(c => "R · " + FormatRelicCondition(c))) yield return line;
        if (query.TreasureRelics is { IsEmpty: false } treasure)
        {
            string mode = _workbenchText!.Get(treasure.OrderMode == TreasureRelicOrderMode.Ordered ? "query.treasure.ordered" : "query.treasure.unordered");
            string targets = string.Join(" / ", treasure.Slots.Take(treasure.Count).Select((key, index) =>
                _workbenchText.Format("query.treasure.target", index + 1) + ": " +
                (key is { } relic ? _names!.Resolve(relic, GameContentKind.Relic) : "—")));
            yield return "R · " + _workbenchText.Get("query.relic.tab.treasure") + " · " +
                _workbenchText.Format("query.treasure.encounter", treasure.StartOrdinal) + " · " +
                _workbenchText.Get("query.treasure.range") + " " + treasure.Count + " · " + mode + " · " + targets;
        }
        if (query.FirstActUnknownEventPrefixCount > 0)
            yield return "E · " + _workbenchText!.Get("query.event.unknown.title") + " · " +
                _workbenchText.Get("query.event.unknown.option_" + query.FirstActUnknownEventPrefixCount);
        foreach (string line in query.EventSequenceConstraints.Select(c => "E · " + FormatEventCondition(c))) yield return line;
        foreach (string line in query.EventResultConditions.Select(c => "E · " + FormatEventResult(c))) yield return line;
        foreach (string line in query.AncientBranches.Select(c => $"A · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {_names!.Resolve(c.AncientKey, GameContentKind.Ancient)}" +
            (c.OptionAny.Count > 0 ? " · " + string.Join(" / ", c.OptionAny.Select(k => _names.Resolve(k, GameContentKind.Relic))) : "") +
            (c.SeaGlassTargetAny.Count > 0 ? " · " + string.Join(" / ", c.SeaGlassTargetAny.Select(k => _names.Resolve(k, GameContentKind.Character))) : ""))) yield return line;
        foreach (string line in query.LegacyWorld.AncientIdentityFilters.Select(c => $"A · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {FormatKeyFilter(c.Keys, GameContentKind.Ancient)}")) yield return line;
        foreach (string line in query.LegacyWorld.AncientOptionFilters.Select(c => $"A · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {FormatKeyFilter(c.Keys, GameContentKind.Relic)}")) yield return line;
        foreach (string line in query.VariantBossBranches.Select(c => $"W · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {_names!.Resolve(c.VariantKey, GameContentKind.Act)}" +
            (!c.FirstBoss.IsEmpty ? " · " + FormatKeyFilter(c.FirstBoss, GameContentKind.Encounter) : "") +
            (!c.SecondBoss.IsEmpty ? " · #2 " + FormatKeyFilter(c.SecondBoss, GameContentKind.Encounter) : ""))) yield return line;
        foreach (string line in query.LegacyWorld.BossFilters.Select(c => $"W · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {FormatKeyFilter(c.Keys, GameContentKind.Encounter)}")) yield return line;
        foreach (string line in query.LegacyWorld.BossOrdinalFilters.Select(c => $"W · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · #{c.Ordinal} · {FormatKeyFilter(c.Keys, GameContentKind.Encounter)}")) yield return line;
        foreach (string line in query.StandardMaps.Select(FormatMapCondition)) yield return line;
        foreach (var drawingRoute in query.StandardMapRoutes)
        {
            string Room(StandardMapRoomType room) => room switch
            {
                StandardMapRoomType.Monster => Local("小怪", "Monster"),
                StandardMapRoomType.Elite => Local("精英", "Elite"),
                StandardMapRoomType.RestSite => Local("火堆", "Rest Site"),
                StandardMapRoomType.Shop => Local("商店", "Shop"),
                StandardMapRoomType.Unknown => Local("问号", "Unknown"),
                _ => room.ToString()
            };
            string groups = string.Join(" → ", drawingRoute.Groups.Select(group => group.Nodes.Count == 1
                ? Room(group.Nodes[0]) : "[" + string.Join(" / ", group.Nodes.Select(Room)) + "]"));
            yield return $"M · {_workbenchText!.Get($"query.map.scope.act{drawingRoute.Act}")} · " +
                Local("绘画路线", "Drawn route") + " · " + groups + " · " +
                Local("组内无序，允许间隔；不含固定节点", "Unordered within groups, gaps allowed; fixed rooms excluded");
        }
        foreach (string line in query.RelicShopSequenceConditions.Select(c => "S · " + _text!.Get(Ui1TextKey.SearchRelicLaneShop) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Relic))) yield return line;
        foreach (string line in query.MerchantColorlessSequenceConditions.Select(c => "S · " + _text!.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Card))) yield return line;
        foreach (string line in query.MerchantColorlessConditions.Select(c => $"S · #{c.MerchantOrdinal} · " + _text!.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + _names!.Resolve(c.TargetCardKey, GameContentKind.Card))) yield return line;
        if (query.CombatCardRewards is { } cards)
            yield return "C · " + _text!.Format(Ui1TextKey.SearchPresetSummaryRewardCards, cards.Count, FormatOrderMode(cards.OrderMode)) + " · " + FormatSlots(cards.Slots.Take(cards.Count), GameContentKind.Card);
        if (query.CombatCardExclusion is { } excluded)
            yield return "C · " + _workbenchText!.Get("query.combat.exclusion.title") + " · " +
                _workbenchText.Format("query.combat.exclusion.summary", excluded.BattleCount) + " · " +
                (collection is null ? _workbenchText.Format("query.combat.exclusion.imported", excluded.CardKeys.Count)
                    : _workbenchText.Format("query.combat.exclusion.snapshot", collection.Name, collection.Revision, collection.Cards.Count)) +
                " · " + string.Join(" / ", excluded.CardKeys.Select(k => _names!.Resolve(k, GameContentKind.Card)));
        if (query.CombatPotionRewards is { } potions)
            yield return "C · " + _text!.Format(Ui1TextKey.SearchPresetSummaryRewardPotions, potions.Count, FormatOrderMode(potions.OrderMode)) + " · " +
                string.Join(" / ", potions.Slots.Take(potions.Count).Select(s => s.PotionKey is { } key ? _names!.Resolve(key, GameContentKind.Potion) :
                    s.Requirement == CombatPotionSlotRequirement.NoDrop ? _text.Get(Ui1TextKey.SearchCombatRewardPotionNoDrop) :
                    s.Requirement == CombatPotionSlotRequirement.DropAny ? _text.Get(Ui1TextKey.SearchCombatRewardPotionDrop) : "—"));
        if (query.TransformationAggregate is { } transform)
            yield return "T · " + (transform.Predicate == TransformationAggregatePredicate.RareCountAtLeast
                ? Local("稀有牌至少 ", "Rare cards ≥ ") + transform.MinimumRareCount
                : string.Join(" + ", transform.TargetMultiset.Select(k => _names!.Resolve(k, GameContentKind.Card))) +
                  (transform.RequiresRareRemainder ? Local("；其余稀有牌 ×", "; remaining Rare ×") +
                      (transform.OpportunityCount - transform.TargetMultiset.Count) : ""));
    }

    private string FormatStructuredEffect(NeowStructuredEffectSearchCondition effect)
    {
        GameContentKind kind = effect.OutputKind == NeowStructuredOutputKind.Relic ? GameContentKind.Relic :
            effect.OutputKind == NeowStructuredOutputKind.Potion ? GameContentKind.Potion : GameContentKind.Card;
        string targets = effect.KaleidoscopePositionalSlots.Count > 0
            ? FormatSlots(effect.KaleidoscopePositionalSlots, kind)
            : string.Join(" + ", effect.OutputKeys.Select(k => _names!.Resolve(k, kind)));
        if (effect.SpecialOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw)
            targets = _names!.Resolve(BaseGameModelKeys.Cards.Claw, GameContentKind.Card) + " ×3";
        return $"N · {_names!.Resolve(effect.SourceRelicKey, GameContentKind.Relic)} · {targets}";
    }

    private string FormatEventResult(EventResultSearchCondition condition)
    {
        string eventId = condition.Kind switch
        {
            EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic => "TRASH_HEAP",
            EventResultConditionKind.ColorfulPhilosophersOfferedColor => "COLORFUL_PHILOSOPHERS",
            EventResultConditionKind.FakeMerchantOfferedFakeRelic => "FAKE_MERCHANT",
            EventResultConditionKind.MorphicGroveGroupInitialBasicsContains => "MORPHIC_GROVE",
            EventResultConditionKind.SymbioteInitialBasicTransform => "SYMBIOTE",
            EventResultConditionKind.AromaOfChaosInitialBasicTransform => "AROMA_OF_CHAOS",
            EventResultConditionKind.WhisperingHollowInitialBasicTransform => "WHISPERING_HOLLOW",
            EventResultConditionKind.TrialCase or EventResultConditionKind.TrialNondescriptInitialBasicsContains => "TRIAL",
            _ => "TINKER_TIME"
        };
        string target;
        if (condition.TrialCase is { } trial)
            target = _workbenchText!.Get($"query.event.results.case_{(int)trial + 1}");
        else if (condition.TinkerCardType is { } cardType)
            target = _workbenchText!.Get($"query.event.results.card_type_{(int)cardType + 1}") +
                (condition.TinkerRider is { } rider ? " · " + _workbenchText.Get("query.event.results.rider." + rider.ToString().ToLowerInvariant()) : "");
        else
        {
            GameContentKind kind = condition.Kind switch
            {
                EventResultConditionKind.TrashHeapDiveRelic or EventResultConditionKind.FakeMerchantOfferedFakeRelic => GameContentKind.Relic,
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => GameContentKind.Character,
                _ => GameContentKind.Card
            };
            target = _names!.Resolve(condition.TargetKey, kind) + (condition.MorphicGroveSecondCard is { } second ? " + " + _names.Resolve(second, GameContentKind.Card) : "");
        }
        return _names!.Resolve(new ModelKey("EVENT", eventId), GameContentKind.Event) + " · " + target;
    }

    private string FormatMapCondition(StandardMapSearchCondition condition)
    {
        string metric = condition.Metric switch
        {
            StandardMapMetric.GuaranteedMonster => "monster", StandardMapMetric.GuaranteedElite => "elite",
            StandardMapMetric.GuaranteedRest => "rest", StandardMapMetric.GuaranteedUnknown => "unknown",
            StandardMapMetric.ReachableMaxMonster => "max_monster", StandardMapMetric.ReachableMaxElite => "max_elite",
            StandardMapMetric.ReachableMaxRest => "max_rest", StandardMapMetric.ReachableMaxUnknown => "max_unknown", _ => "prefix"
        };
        string scope = _workbenchText!.Get(condition.Scope == 0 ? "query.map.scope.total" : $"query.map.scope.act{condition.Scope}");
        return $"M · {scope} · {_workbenchText.Get(condition.RouteObjective ? "query.map.route" : "query.map.property")} · {_workbenchText.Get("query.map.property." + metric)} {(condition.Comparison == StandardMapComparison.AtLeast ? "≥" : "≤")} {condition.Value}";
    }

    private IEnumerable<bool> AddSummaryCard(string title, IEnumerable<string> lines)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        MainPanelTheme.ApplyPanel(card, Ui1SurfaceRole.Input, 3f, 1, 8f);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 3);
        column.AddChild(MainPanelTheme.Label(title, Ui1TextRole.Meta, true));
        card.AddChild(column); _summary.AddChild(card);
        yield return true;
        bool any = false;
        foreach (string line in lines)
        {
            any = true; column.AddChild(MainPanelTheme.Label(line, Ui1TextRole.Body, true));
            yield return true;
        }
        if (!any)
        {
            column.AddChild(MainPanelTheme.Label(_text!.Get(Ui1TextKey.SearchPresetSummaryEmpty), Ui1TextRole.Body, true));
            yield return true;
        }
    }

    private string FormatSlots(IEnumerable<ModelKey?> slots, GameContentKind kind) =>
        string.Join(" / ", slots.Select(k => k.HasValue ? _names!.Resolve(k.Value, kind) : "—"));

    private string FormatRelicCondition(RelicSequenceSearchCondition condition)
    {
        if (_text is null || _names is null) return string.Empty;
        string lane = condition.Lane switch
        {
            RelicSequenceKind.Common => _text.Get(Ui1TextKey.SearchRelicLaneCommon),
            RelicSequenceKind.Uncommon => _text.Get(Ui1TextKey.SearchRelicLaneUncommon),
            RelicSequenceKind.Rare => _text.Get(Ui1TextKey.SearchRelicLaneRare),
            RelicSequenceKind.Shop => _text.Get(Ui1TextKey.SearchRelicLaneShop),
            _ => string.Empty
        };
        return condition.RangeMode == SearchSequenceRangeMode.FirstN
            ? _text.Format(Ui1TextKey.SearchRelicConditionFirstN, lane, condition.RangeValue, FormatKeyFilter(condition.Keys, GameContentKind.Relic))
            : _text.Format(Ui1TextKey.SearchRelicConditionExactSlot, lane, condition.RangeValue, FormatKeyFilter(condition.Keys, GameContentKind.Relic));
    }

    private string FormatEventCondition(EventSequenceSearchCondition condition)
    {
        if (_text is null || _names is null) return string.Empty;
        string act = _text.Format(Ui1TextKey.SearchPresetSummaryAct, condition.Act);
        string targets = FormatKeyFilter(condition.Keys, GameContentKind.Event);
        return condition.RangeMode == SearchSequenceRangeMode.FirstN
            ? _text.Format(Ui1TextKey.SearchEventConditionFirstN, act, condition.RangeValue, targets)
            : _text.Format(Ui1TextKey.SearchEventConditionExactSlot, act, condition.RangeValue, targets);
    }

    private string FormatKeyFilter(ModelKeySetFilter filter, GameContentKind kind)
    {
        if (_text is null || _names is null) return string.Empty;
        var parts = new List<string>();
        AddMode(_text.Get(Ui1TextKey.SearchAny), filter.Any);
        AddMode(_text.Get(Ui1TextKey.SearchAll), filter.All);
        AddMode(_text.Get(Ui1TextKey.SearchBan), filter.Ban);
        return string.Join(" · ", parts);

        void AddMode(string label, IReadOnlyList<ModelKey> keys)
        {
            string[] names = keys.Where(key => key.IsValid)
                .Distinct()
                .Select(key => _names.Resolve(key, kind))
                .ToArray();
            if (names.Length > 0) parts.Add(label + ": " + string.Join(", ", names));
        }
    }

    private string FormatOrderMode(CombatRewardSequenceOrderMode mode)
    {
        if (_text is null) return string.Empty;
        return mode == CombatRewardSequenceOrderMode.Ordered
            ? _text.Get(Ui1TextKey.SearchCombatRewardOrdered)
            : _text.Get(Ui1TextKey.SearchCombatRewardUnordered);
    }

}
