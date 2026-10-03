using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
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
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 12);
        _text = JsonUiTextProvider.CreatePredictorUi13(language);
        _workbenchText = JsonUiTextProvider.CreateUi13(language);
        _names = names;
        BuildSemanticSummary(preset);
    }
    private string Local(string zh, string en) => _text?.LanguageCode.StartsWith("zh") == true ? zh : en;
    private void BuildSemanticSummary(SearchPresetDefinition preset)
    {
        if (_text is null || _names is null) return;
        if (preset.Workbench is { } workbench)
        {
            BuildWorkbenchSummary(workbench);
            return;
        }
        if (preset.Draft is null)
        {
            _summary.AddChild(Ui1Theme.Label(_text.Get(Ui1TextKey.SearchPresetQueryUnavailable), Ui1TextRole.Warning, true));
            return;
        }

        var sections = new List<(string Title, List<string> Lines)>();
        void Add(string title, IEnumerable<string> lines)
        {
            string[] values = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Distinct(StringComparer.Ordinal).ToArray();
            if (values.Length > 0) sections.Add((title, values.ToList()));
        }
        SearchDraft draft = preset.Draft;

        var neow = new List<string>();
        if (draft.NeowRouteDraft.RouteRelicKey is { IsValid: true } route)
            neow.Add(_names.Resolve(route, GameContentKind.Relic));
        if (draft.NeowRouteDraft.RequiredBonesRelics.Count > 0)
            neow.Add(string.Join(draft.NeowRouteDraft.BonesOrderMode == RolltheSpire2.Search.Semantics.BonesRouteOrderMode.AnyOrder ? " + " : " → ",
                draft.NeowRouteDraft.RequiredBonesRelics.Select(key => _names.Resolve(key, GameContentKind.Relic))));
        foreach (var effect in draft.NeowRouteDraft.EffectConditions)
        {
            var kind = effect.OutputKind == NeowStructuredOutputKind.Relic ? GameContentKind.Relic :
                effect.OutputKind == NeowStructuredOutputKind.Potion ? GameContentKind.Potion : GameContentKind.Card;
            string label = effect.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset
                ? _text.Get("ui1.preset.summary.combined") : _names.Resolve(effect.SourceRelicKey, GameContentKind.Relic);
            string targets = string.Join(" + ", effect.OutputKeys.Select(k => _names.Resolve(k, kind)));
            if (effect.KaleidoscopePositionalSlots.Count > 0)
                targets += " · " + FormatSlots(effect.KaleidoscopePositionalSlots, kind);
            if (effect.Scope == NeowStructuredEffectScope.FinalCurse) label = _text.Get(Ui1TextKey.SearchFinalCurse);
            neow.Add(label + " · " + targets);
        }
        if (draft.RequireSmallCapsule) neow.Add(_text.Get(Ui1TextKey.SearchSmallCapsule));
        if (draft.RequireLargeCapsule) neow.Add(_text.Get(Ui1TextKey.SearchLargeCapsule));
        if (draft.RequireWhetstone) neow.Add(_text.Get(Ui1TextKey.SearchWhetstone));
        if (draft.RequireWarPaint) neow.Add(_text.Get(Ui1TextKey.SearchWarPaint));
        if (ModelKey.TryParseExact(draft.RequiredFinalCurse, out ModelKey finalCurse))
            neow.Add(_text.Get(Ui1TextKey.SearchFinalCurse) + " · " + _names.Resolve(finalCurse, GameContentKind.Card));
        if (draft.RequireBones && neow.Count == 0) neow.Add(_text.Get(Ui1TextKey.SearchCategoryNeow));
        Add(_text.Get(Ui1TextKey.SearchCategoryNeow), neow);

        Add(_text.Get(Ui1TextKey.SearchCategoryBossAncient), draft.AncientMatrixDraft.Rows
            .Where(row => row.IsActive)
            .Select(row =>
            {
                var parts = new List<string> { _names.Resolve(row.AncientKey, GameContentKind.Ancient) };
                parts.AddRange(row.SelectedOptionKeys.Select(key => _names.Resolve(key, GameContentKind.Relic)));
                return string.Join(" · ", parts);
            }));

        Add(_text.Get(Ui1TextKey.SearchCategoryBossAncient), draft.BossMapDraft.Rows
            .Where(row => row.IsVariantActive || row.FirstBossAny.Count > 0 || row.SecondBossAny.Count > 0)
            .Select(row =>
            {
                string[] names = row.FirstBossAny.Concat(row.SecondBossAny)
                    .Distinct()
                    .Select(key => _names.Resolve(key, GameContentKind.Encounter))
                    .ToArray();
                return names.Length > 0 ? string.Join(" · ", names) : _text.Format(Ui1TextKey.SearchPresetSummaryAct, row.Act);
            }));

        Add(_text.Get(Ui1TextKey.SearchCategoryRelicsShop), draft.RelicSequenceDraft
            .Where(condition => !condition.IsEmpty)
            .Select(FormatRelicCondition));

        Add(_text.Get(Ui1TextKey.SearchCategoryEvents), draft.EventSequenceDraft
            .Where(condition => !condition.IsEmpty)
            .Select(FormatEventCondition));
        if ((draft.MorphicGroveContainsCard ?? draft.MorphicGroveSecondCard) is { } morphicCard)
            Add(_text.Get(Ui1TextKey.SearchCategoryEvents), new[] {
                _text.Get(Ui1TextKey.SearchEventMorphicContains) + ": " + _names!.Resolve(morphicCard, GameContentKind.Card) +
                (draft.MorphicGroveContainsCard.HasValue && draft.MorphicGroveSecondCard is { } second ? " + " + _names.Resolve(second, GameContentKind.Card) : "") });
        if (draft.TransformationAggregate is { } aggregate)
            Add(_text.LanguageCode.StartsWith("zh") ? "变牌组合" : "Transformation aggregate", new[] {
                aggregate.Opening + " · " + aggregate.PickupOrder + " · " +
                string.Join(" + ", new[] { aggregate.MorphicGrove ? "Morphic ×2" : null, aggregate.AromaOfChaos ? "Aroma ×1" : null, aggregate.WhisperingHollow ? "Whisper ×1" : null, aggregate.Symbiote ? "Symbiote ×1" : null, aggregate.TrialNondescript ? "Trial ×2" : null }.Where(x => x is not null)),
                aggregate.Predicate == RolltheSpire2.Search.Semantics.TransformationAggregatePredicate.RareCountAtLeast
                    ? (_text.LanguageCode.StartsWith("zh") ? "稀有牌至少 " : "Rare cards ≥ ") + aggregate.MinimumRareCount
                    : string.Join(" + ", aggregate.TargetMultiset.Select(k => _names.Resolve(k, GameContentKind.Card))) +
                      (aggregate.RequiresRareRemainder ? (_text.LanguageCode.StartsWith("zh") ? "；其余稀有牌 ×" : "; remaining Rare ×") +
                          (aggregate.OpportunityCount - aggregate.TargetMultiset.Count) : "") });

        Add(_text.Get(Ui1TextKey.SearchCategoryEvents), draft.EventResultDraft.Where(c => c.IsValid).Select(c => {
            var (key, kind) = c.Kind switch {
                EventResultConditionKind.TrashHeapGrabCard => ("trash_grab", GameContentKind.Card),
                EventResultConditionKind.MorphicGroveGroupInitialBasicsContains => ("morphic_contains", GameContentKind.Card),
                EventResultConditionKind.TrashHeapDiveRelic => ("trash_dive", GameContentKind.Relic),
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => ("color", GameContentKind.Character),
                _ => ("fake_relic", GameContentKind.Relic)
            };
            return _text.Get("ui1.search.event_result." + key) + " · " + _names.Resolve(c.TargetKey, kind) +
                (c.MorphicGroveSecondCard is { } second ? " + " + _names.Resolve(second, GameContentKind.Card) : "");
        }));
        var shop = new List<string>();
        shop.AddRange(draft.MerchantColorlessSequenceDraft.Where(c => !c.IsEmpty).Select(c =>
            _text.Get("ui1.search.shop_colorless.title") + " · " + _text.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Card)));
        shop.AddRange(draft.RelicShopSequenceDraft.Where(c => !c.IsEmpty).Select(c =>
            _text.Get(Ui1TextKey.SearchRelicLaneShop) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Relic)));
        if (draft.MerchantColorlessSequenceDraft.Count == 0)
            shop.AddRange(draft.MerchantColorlessDraft.Where(c => c.IsValid).Select(c =>
                $"#{c.MerchantOrdinal} · " + _text.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + _names.Resolve(c.TargetCardKey, GameContentKind.Card)));
        Add(_text.Get("ui1.search.shop_colorless.title"), shop);

        if (draft.CombatRewardDraft.HasAnyValue)
        {
            var reward = new List<string>();
            if (draft.CombatRewardDraft.Cards.HasAnyValue)
                reward.Add(_text.Format(Ui1TextKey.SearchPresetSummaryRewardCards, draft.CombatRewardDraft.Cards.Count, FormatOrderMode(draft.CombatRewardDraft.Cards.OrderMode)) + " · " + FormatSlots(draft.CombatRewardDraft.Cards.Slots.Take(draft.CombatRewardDraft.Cards.Count), GameContentKind.Card));
            if (draft.CombatRewardDraft.Potions.HasAnyValue)
                reward.Add(_text.Format(Ui1TextKey.SearchPresetSummaryRewardPotions, draft.CombatRewardDraft.Potions.Count, FormatOrderMode(draft.CombatRewardDraft.Potions.OrderMode)) + " · " +
                    string.Join(" / ", draft.CombatRewardDraft.Potions.Slots.Take(draft.CombatRewardDraft.Potions.Count).Select(s =>
                        s.PotionKey.HasValue ? _names.Resolve(s.PotionKey.Value, GameContentKind.Potion) :
                        s.Requirement == CombatPotionSlotRequirement.NoDrop ? _text.Get(Ui1TextKey.SearchCombatRewardPotionNoDrop) :
                        s.Requirement == CombatPotionSlotRequirement.DropAny ? _text.Get(Ui1TextKey.SearchCombatRewardPotionDrop) : "—")));
            Add(_text.Get(Ui1TextKey.AnalysisSubsectionCombatReward), reward);
        }

        foreach ((string title, List<string> lines) in sections)
        {
            var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            Ui1Theme.ApplyPanel(card, Ui1SurfaceRole.Input, 3f, 1, 8f);
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 3);
            column.AddChild(Ui1Theme.Label(title, Ui1TextRole.Meta));
            foreach (string line in lines) column.AddChild(Ui1Theme.Label(line, Ui1TextRole.Body, true));
            card.AddChild(column);
            _summary.AddChild(card);
        }
        if (sections.Count == 0)
            _summary.AddChild(Ui1Theme.Label(_text.Get(Ui1TextKey.SearchPresetSummaryEmpty), Ui1TextRole.Muted, true));
    }

    private void BuildWorkbenchSummary(WorkbenchSearchDraft draft)
    {
        if (draft.Players.Count == 0)
            AddQuerySummary(draft.Query, Local("单人条件", "Solo conditions"));
        else
        {
            AddQuerySummary(draft.Query, Local("整桌共享条件", "Shared party conditions"));
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
                    AddSummaryCard(title, [Local("此席位的条件无法解析", "This slot's conditions are unavailable")]);
                    continue;
                }
                var offers = new List<string>();
                if (!personal.Offers.IsEmpty) offers.Add("N · " + FormatKeyFilter(personal.Offers, GameContentKind.Relic));
                if (personal.SelectedOption is { } plan)
                    offers.Add("N · " + _names.Resolve(plan.Option, GameContentKind.Relic));
                offers.AddRange(personal.Results.Select(FormatStructuredEffect));
                AddQuerySummary(personal.Conditions, title, offers);
            }
        }
    }

    private void AddQuerySummary(SearchQuery query, string title, IEnumerable<string>? extra = null)
    {
        var lines = new List<string>(extra ?? []);
        if (query.OpeningRoute is { } route) lines.Add("N · " + _names!.Resolve(route.RouteRelicKey, GameContentKind.Relic));
        if (query.OpeningRouteRelicRequirement is { } bones)
            lines.Add("N · " + string.Join(bones.OrderMode == BonesRouteOrderMode.ExactOrder ? " → " : " + ", bones.RequiredRelicKeys.Select(k => _names!.Resolve(k, GameContentKind.Relic))));
        lines.AddRange(query.StructuredOpeningEffects.Select(FormatStructuredEffect));
        if (!query.LegacyNeow.NeowRelics.IsEmpty) lines.Add("N · " + FormatKeyFilter(query.LegacyNeow.NeowRelics, GameContentKind.Relic));
        if (query.LegacyNeow.RequiredFinalCurse is { } curse) lines.Add("N · " + _text!.Get(Ui1TextKey.SearchFinalCurse) + " · " + _names!.Resolve(curse, GameContentKind.Card));
        lines.AddRange(query.RelicSequenceConstraints.Select(c => "R · " + FormatRelicCondition(c)));
        lines.AddRange(query.EventSequenceConstraints.Select(c => "E · " + FormatEventCondition(c)));
        lines.AddRange(query.EventResultConditions.Select(c => "E · " + FormatEventResult(c)));
        lines.AddRange(query.AncientBranches.Select(c => $"A · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {_names!.Resolve(c.AncientKey, GameContentKind.Ancient)}" +
            (c.OptionAny.Count > 0 ? " · " + string.Join(" / ", c.OptionAny.Select(k => _names.Resolve(k, GameContentKind.Relic))) : "") +
            (c.SeaGlassTargetAny.Count > 0 ? " · " + string.Join(" / ", c.SeaGlassTargetAny.Select(k => _names.Resolve(k, GameContentKind.Character))) : "")));
        lines.AddRange(query.LegacyWorld.AncientIdentityFilters.Select(c => $"A · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {FormatKeyFilter(c.Keys, GameContentKind.Ancient)}"));
        lines.AddRange(query.LegacyWorld.AncientOptionFilters.Select(c => $"A · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {FormatKeyFilter(c.Keys, GameContentKind.Relic)}"));
        lines.AddRange(query.VariantBossBranches.Select(c => $"W · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {_names!.Resolve(c.VariantKey, GameContentKind.Act)}" +
            (!c.FirstBoss.IsEmpty ? " · " + FormatKeyFilter(c.FirstBoss, GameContentKind.Encounter) : "") +
            (!c.SecondBoss.IsEmpty ? " · #2 " + FormatKeyFilter(c.SecondBoss, GameContentKind.Encounter) : "")));
        lines.AddRange(query.LegacyWorld.BossFilters.Select(c => $"W · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {FormatKeyFilter(c.Keys, GameContentKind.Encounter)}"));
        lines.AddRange(query.LegacyWorld.BossOrdinalFilters.Select(c => $"W · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · #{c.Ordinal} · {FormatKeyFilter(c.Keys, GameContentKind.Encounter)}"));
        lines.AddRange(query.StandardMaps.Select(FormatMapCondition));
        lines.AddRange(query.RelicShopSequenceConditions.Select(c => "S · " + _text!.Get(Ui1TextKey.SearchRelicLaneShop) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Relic)));
        lines.AddRange(query.MerchantColorlessSequenceConditions.Select(c => "S · " + _text!.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Card)));
        lines.AddRange(query.MerchantColorlessConditions.Select(c => $"S · #{c.MerchantOrdinal} · " + _text!.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + _names!.Resolve(c.TargetCardKey, GameContentKind.Card)));
        if (query.CombatCardRewards is { } cards)
            lines.Add("C · " + _text!.Format(Ui1TextKey.SearchPresetSummaryRewardCards, cards.Count, FormatOrderMode(cards.OrderMode)) + " · " + FormatSlots(cards.Slots.Take(cards.Count), GameContentKind.Card));
        if (query.CombatPotionRewards is { } potions)
            lines.Add("C · " + _text!.Format(Ui1TextKey.SearchPresetSummaryRewardPotions, potions.Count, FormatOrderMode(potions.OrderMode)) + " · " +
                string.Join(" / ", potions.Slots.Take(potions.Count).Select(s => s.PotionKey is { } key ? _names!.Resolve(key, GameContentKind.Potion) :
                    s.Requirement == CombatPotionSlotRequirement.NoDrop ? _text.Get(Ui1TextKey.SearchCombatRewardPotionNoDrop) :
                    s.Requirement == CombatPotionSlotRequirement.DropAny ? _text.Get(Ui1TextKey.SearchCombatRewardPotionDrop) : "—")));
        if (query.TransformationAggregate is { } transform)
            lines.Add("T · " + (transform.Predicate == TransformationAggregatePredicate.RareCountAtLeast
                ? Local("稀有牌至少 ", "Rare cards ≥ ") + transform.MinimumRareCount
                : string.Join(" + ", transform.TargetMultiset.Select(k => _names!.Resolve(k, GameContentKind.Card))) +
                  (transform.RequiresRareRemainder ? Local("；其余稀有牌 ×", "; remaining Rare ×") +
                      (transform.OpportunityCount - transform.TargetMultiset.Count) : "")));
        AddSummaryCard(title, lines.Count > 0 ? lines : [_text!.Get(Ui1TextKey.SearchPresetSummaryEmpty)]);
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

    private void AddSummaryCard(string title, IEnumerable<string> lines)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(card, Ui1SurfaceRole.Input, 3f, 1, 8f);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 3);
        column.AddChild(Ui1Theme.Label(title, Ui1TextRole.Meta, true));
        foreach (string line in lines) column.AddChild(Ui1Theme.Label(line, Ui1TextRole.Body, true));
        card.AddChild(column);
        _summary.AddChild(card);
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
