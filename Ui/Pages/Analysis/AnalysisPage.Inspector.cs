using Godot;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Event;

namespace RolltheSpire2.Ui.Pages.Analysis;

// Predictor-local presentation state.
internal sealed partial class AnalysisPage
{
    private bool _ancientConditionsOpen;
    private VBoxContainer? _ancientSurface;
    private readonly bool[] _allEvents = new bool[3];
    private readonly ModelKey[] _inspectedEvents = new ModelKey[3];
    private string _relicQuery = "";
    private bool _relicNames = true;
    private bool _rewardAssumptionsOpen;

    private Label WNote(string text)
    {
        var label = WLabel(text);
        label.AddThemeFontSizeOverride("font_size", 15);
        return label;
    }

    private Button WAction(string key)
    {
        var button = _palette.Button(Text(key));
        button.CustomMinimumSize = new Vector2(0, 34);
        button.AddThemeFontSizeOverride("font_size", 15);
        return button;
    }

    private VBoxContainer WCard(Node parent, string title, bool accented = false)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", _palette.Box(_palette.Surface,
            accented ? _palette.Selected : _palette.Line, 1));
        parent.AddChild(panel);
        var column = WColumn(); column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);
        if (title.Length > 0) column.AddChild(WLabel(title, true));
        return column;
    }

    private void RenderOpeningInspector()
    {
        ReconcileOpeningSelection();
        var choice = _viewModel!.NeowChoices.FirstOrDefault(c => c.RelicKey == _selectedNeowRelicKey);
        var route = _viewModel.NormalCombatRewardDomain.Items.FirstOrDefault(r => r.RouteGroupId == _preferredRewardRouteGroupId);

        var choices = WCard(_wbCenter, "");
        var toolbar = new HBoxContainer(); toolbar.AddThemeConstantOverride("separation", 10); choices.AddChild(toolbar);
        var heading = WLabel(Text("predictor.neow_offers"), true); toolbar.AddChild(heading);
        heading.TooltipText = Text("predictor.design.opening_hint");
        _neowChoiceStrip.Reparent(choices);
        _neowChoiceStrip.Bind(_viewModel.NeowChoices, _selectedNeowRelicKey, _preferredOpeningRouteId,
            Text(Ui1TextKey.MissingIconTooltip), Text(Ui1TextKey.AnalysisNeowPickFirst));
        foreach (string warning in _viewModel.OpeningWarnings) _wbCenter.AddChild(WNote(warning));

        var identities = new GridContainer { Columns = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        identities.AddThemeConstantOverride("h_separation", 14); _wbCenter.AddChild(identities);
        RenderOpeningIdentity(identities, choice, _preferredOpeningRouteId);
        identities.Visible = identities.GetChildren().OfType<Control>().Any(c => c.Visible);
        var rewardHeader = new HBoxContainer(); _wbCenter.AddChild(rewardHeader);
        WTitle(rewardHeader, "predictor.combat_rewards");
        var assumptions = WAction("predictor.design.reward_assumptions"); assumptions.Name = "RewardAssumptionsToggle";
        rewardHeader.AddChild(assumptions);
        var scope = WNote(Text("predictor.design.reward_scope")); scope.Name = "RewardAssumptionsText"; scope.Visible = _rewardAssumptionsOpen;
        _wbCenter.AddChild(scope);
        assumptions.Pressed += () => { _rewardAssumptionsOpen = !_rewardAssumptionsOpen; scope.Visible = _rewardAssumptionsOpen; };
        if (route is null || route.Battles.Count == 0)
        {
            _wbCenter.AddChild(WNote(Text(choice is not null && NeowChoiceStrip.HasAmbiguousBonesRoutes(choice) && string.IsNullOrWhiteSpace(_preferredOpeningRouteId)
                ? Ui1TextKey.AnalysisOpeningBonesRoutePrompt : "predictor.unavailable")));
            return;
        }
        var battles = new GridContainer { Name = "OpeningBattleRewards", Columns = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        battles.AddThemeConstantOverride("h_separation", 14); battles.AddThemeConstantOverride("v_separation", 8); _wbCenter.AddChild(battles);
        for (int ordinal = 1; ordinal <= 3; ordinal++)
        {
            RenderBattleOutcome(battles, route.Battles.FirstOrDefault(b => b.BattleOrdinal == ordinal), ordinal, false, false);
        }
    }

    private void RenderOpeningIdentity(Node parent, NeowChoiceViewModel? choice, string opening)
    {
        var column = WCard(parent, "", true);
        if (choice is null) { column.AddChild(WNote(Text(Ui1TextKey.AnalysisOpeningSelectRelicPrompt))); return; }
        var binding = choice.OpeningRoutes.FirstOrDefault(r => r.RouteId == opening);
        if (binding is not null && choice.OpeningRoutes.Count > 1) column.AddChild(WNote(binding.Label));
        var detail = _neowChoiceDetail;
        detail.Reparent(column);
        detail.Bind(choice, opening, Text(Ui1TextKey.MissingIconTooltip));
        // The detail donor already includes the source relic. Do not repeat it above.
        if (!detail.Visible) ((Control)column.GetParent()).Hide();
    }

    private static bool UsesStandardCombatAssumptions(NormalCombatRewardBattleViewModel battle) =>
        battle.Precision == PredictionPrecision.Partial &&
        battle.ConditionalAssumptions.Contains(NormalCombatRewardConditionalAssumption.OpeningRouteContinuationApplied) &&
        battle.ReasonCode is "Beta111DefaultMonsterRewardPipelineSemanticCompatible:opening-route-first-combat"
            or "Beta111DefaultMonsterRewardPipelineSemanticCompatible:consecutive-no-intervening-consumer";

    private string BattlePotionStatus(NormalCombatRewardBattleViewModel battle)
    {
        bool knownAbsent = !battle.Potion.Generated && (battle.Potion.Precision == PredictionPrecision.Exact ||
            battle.Potion.Precision == PredictionPrecision.Partial && UsesStandardCombatAssumptions(battle));
        return Text(knownAbsent ? "predictor.design.potion_none" : "predictor.design.potion_unknown");
    }

    private void RenderBattleOutcome(Node parent, NormalCombatRewardBattleViewModel? battle, int ordinal, bool current, bool narrow)
    {
        var block = WCard(parent, "", current); block.Name = $"Battle{ordinal}Outcome";
        Container row = narrow ? block : new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (!narrow) { row.AddThemeConstantOverride("separation", 16); block.AddChild(row); }
        BoxContainer summary = narrow ? new HBoxContainer() : new VBoxContainer { CustomMinimumSize = new Vector2(124, 0) };
        summary.AddThemeConstantOverride("separation", narrow ? 16 : 4); row.AddChild(summary);
        var title = WLabel(_uiText!.Format(Ui1TextKey.AnalysisCombatRewardBattleShort, ordinal), true);
        title.SizeFlagsHorizontal = SizeFlags.Fill; title.AutowrapMode = TextServer.AutowrapMode.Off; summary.AddChild(title);
        if (battle is null) { WEmpty(block); return; }
        string gold = battle.GoldRewards.Count > 0
            ? _uiText.Format(Ui1TextKey.AnalysisCombatRewardGoldShort, battle.GoldRewards.Sum(g => g.Amount)) : battle.GoldLabel;
        var goldLabel = WNote(gold); goldLabel.SizeFlagsHorizontal = SizeFlags.Fill;
        goldLabel.Name = "GoldStatus"; goldLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        goldLabel.TooltipText = battle.GoldLabel; summary.AddChild(goldLabel);
        if (battle.Potion.PotionDisplay is { } potion)
        {
            var icon = new TextureRect { Texture = _icons.Resolve(potion.ModelKey, GameContentKind.Potion, IconVariant.Small).Texture,
                CustomMinimumSize = new Vector2(28, 28), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
            icon.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            BindWorkbenchTooltip(icon, potion.ModelKey, GameContentKind.Potion, potion.DisplayName); summary.AddChild(icon);
        }
        else
        {
            var status = WNote(BattlePotionStatus(battle)); status.Name = "PotionStatus"; status.SizeFlagsHorizontal = SizeFlags.Fill;
            status.AutowrapMode = TextServer.AutowrapMode.Off;
            summary.AddChild(status);
        }
        if (battle.Precision != PredictionPrecision.Exact && !UsesStandardCombatAssumptions(battle))
            block.AddChild(WNote(_uiText.Format("predictor.design.precision", battle.PrecisionLabel)));
        var rewards = WColumn(); rewards.AddThemeConstantOverride("separation", 6); row.AddChild(rewards);
        var groups = battle.CardRewards.Count > 0 ? battle.CardRewards.Select(g => g.Cards) : [battle.Cards];
        int groupOrdinal = 0;
        foreach (var cards in groups)
        {
            if (groupOrdinal++ > 0) rewards.AddChild(new HSeparator());
            int columns = narrow && cards.Count == 4 ? 2 : Math.Clamp(cards.Count, 1, narrow ? 3 : 4);
            var cardList = new GridContainer { Name = $"CardReward{groupOrdinal}", Columns = columns,
                SizeFlagsHorizontal = SizeFlags.ExpandFill };
            cardList.AddThemeConstantOverride("h_separation", 12); cardList.AddThemeConstantOverride("v_separation", 4); rewards.AddChild(cardList);
            foreach (var card in cards)
            {
                var item = WCompactIdentity(card.CardDisplay.ModelKey, GameContentKind.Card,
                    card.CardDisplay.DisplayName + card.DisplaySuffix, 32);
                item.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                item.CustomMinimumSize = new Vector2(0, 32);
                var name = item.GetChild<Label>(1);
                name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                BindWorkbenchTooltip(item, card.CardDisplay.ModelKey, GameContentKind.Card, card.CardDisplay.DisplayName);
                cardList.AddChild(item);
            }
        }
    }

    private void RenderAncientInspector(AncientPredictionViewModel ancient, VBoxContainer? existing = null)
    {
        var section = existing ?? WCard(_wbCenter, "");
        if (existing is not null) WClear(section);
        _ancientSurface = section;
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 12); section.AddChild(header);
        header.AddChild(WLabel(Text("predictor.ancient_options"), true));
        var keys = AncientOptionConditionProfile.AuthoredOptionKeys.Where(k => AncientForOption(k) == ancient.AncientDisplay.ModelKey.Entry).ToArray();
        var authored = _ancientGroupCard.CurrentConditions;
        int excluded = keys.Count(k => authored.TryGetOptionEligibility(k, out bool eligible) && !eligible);
        if (keys.Length > 0)
        {
            var edit = WAction(_ancientConditionsOpen ? "predictor.design.conditions_close" : "predictor.design.conditions");
            edit.Name = "AncientConditionsToggle";
            if (excluded > 0) edit.Text += $" · {excluded}";
            header.AddChild(edit); edit.Pressed += () => { _ancientConditionsOpen = !_ancientConditionsOpen; RenderWorkbench(); };
        }
        var identityRow = new HBoxContainer { Name = "AncientIdentityAndOptions" };
        identityRow.AddThemeConstantOverride("separation", 24); section.AddChild(identityRow);
        var identity = WObject(ancient.AncientDisplay.ModelKey, GameContentKind.Ancient, ancient.AncientDisplay.DisplayName);
        identity.Name = "AncientIdentity";
        identity.SizeFlagsHorizontal = SizeFlags.Fill;
        identity.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        identity.CustomMinimumSize = new Vector2(170, 0);
        identityRow.AddChild(identity);
        var offers = new HFlowContainer { Name = "AncientOffers", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter };
        offers.AddThemeConstantOverride("h_separation", 20); identityRow.AddChild(offers);
        foreach (var option in ancient.Options.OrderBy(o => o.Ordinal))
        {
            var tile = WColumn(); offers.AddChild(tile);
            tile.AddChild(WAncientOption(option.OptionDisplay.ModelKey, option.OptionDisplay.DisplayName));
            if (option.CharacterTargetDisplay is { } target)
                tile.AddChild(WCompactIdentity(target.ModelKey, GameContentKind.Character, target.DisplayName, 24));
        }
        if (ancient.Options.Count == 0) WEmpty(section);
        if (ancient.AncientDisplay.ModelKey.Entry == "OROBAS" && !authored.OrobasTouchOfOrobasConditionMet &&
            !authored.OrobasArchaicToothConditionMet && ancient.Options.Count == 2 &&
            ancient.OptionsEvaluationStatus == AncientOptionsEvaluationStatus.EvaluatedNonEmpty)
            offers.AddChild(WNote(Text("predictor.ancient_empty_option")));
        if (keys.Length > 0)
            section.AddChild(WNote(Text(excluded == 0 ? "predictor.design.conditions_default" : "predictor.design.conditions_custom")));
        if (!_ancientConditionsOpen || keys.Length == 0) return;
        bool canEdit = _ancientPremiseVariants.ContainsKey((ancient.Act, ancient.AncientDisplay.ModelKey));
        section.AddChild(new HSeparator());
        section.AddChild(WNote(Text("predictor.design.conditions_hint")));
        if (!canEdit) section.AddChild(WNote(Text("predictor.unavailable")));
        foreach (var key in keys)
        {
            authored.TryGetOptionEligibility(key, out bool eligible);
            var row = new HBoxContainer(); section.AddChild(row);
            var toggle = new CheckButton { Name = "AncientPremise" + key.Entry, ButtonPressed = eligible, Disabled = !canEdit,
                CustomMinimumSize = new Vector2(48, 40) }; row.AddChild(toggle);
            var description = WColumn(); description.AddThemeConstantOverride("separation", 2); row.AddChild(description);
            description.AddChild(WLabel(_contentNames!.Resolve(key, GameContentKind.Relic)));
            description.AddChild(WNote(Text(AncientOptionPremiseTextKey(key)!)));
            toggle.Toggled += value => _ancientGroupCard.SetConditions(_ancientGroupCard.CurrentConditions.WithOptionEligibility(key, value), notify: true);
        }
        var reset = WAction("predictor.design.conditions_reset"); reset.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        reset.Disabled = !canEdit;
        section.AddChild(reset);
        reset.Pressed += () => _ancientGroupCard.SetConditions(ResetAncientPremises(_ancientGroupCard.CurrentConditions, ancient.AncientDisplay.ModelKey), notify: true);
    }

    private static string AncientForOption(ModelKey key) => key.Entry switch
    {
        "PAELS_CLAW" or "PAELS_LEGION" or "PAELS_TOOTH" => "PAEL",
        "ARCHAIC_TOOTH" or "TOUCH_OF_OROBAS" => "OROBAS", "NUTRITIOUS_SOUP" => "TEZCATARA",
        "BEAUTIFUL_BRACELET" => "NONUPEIPE", "TRI_BOOMERANG" => "TANX", "PANDORAS_BOX" => "DARV", _ => ""
    };

    private void RenderEventInspector(int act, EventPoolActSequenceViewModel? info)
    {
        const int previewCount = 8;
        var section = WCard(_wbCenter, "");
        var header = new HBoxContainer(); section.AddChild(header);
        header.AddChild(WLabel(Text("predictor.events"), true));
        if (info is null || info.Entries.Count == 0) { _inspectedEvents[act - 1] = default; WEmpty(section); return; }
        var countLabel = WNote(""); header.AddChild(countLabel);
        var expand = WAction("predictor.design.show_all"); expand.Name = "EventQueueExpand";
        expand.Visible = info.Entries.Count > previewCount; header.AddChild(expand);
        section.AddChild(WNote(Text("predictor.design.event_scope")));
        var grid = new GridContainer { Name = "EventQueue", Columns = 8, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 8); section.AddChild(grid);
        var details = WColumn(); details.Name = "EventDetails"; section.AddChild(details);
        var tooltipSource = _actCards[act - 1];
        tooltipSource.BindEventTooltipContext(_viewModel!.ProfileId, _viewModel.PlayersCount, BuildEventResultProjection(), _uiText!);
        var buttons = new List<(ModelKey Key, Button Button)>();
        void ShowDetails()
        {
            foreach (var item in buttons) _palette.SetActive(item.Button, item.Key == _inspectedEvents[act - 1]);
            WClear(details);
            var selected = info.Entries.FirstOrDefault(e => e.EventDisplay.ModelKey == _inspectedEvents[act - 1]);
            details.Visible = selected is not null;
            if (selected is not null) RenderEventDetails(details, selected);
            UpdateCombatQueueVisibility(act);
        }
        void Populate()
        {
            WClear(grid); buttons.Clear();
            int count = _allEvents[act - 1] ? info.Entries.Count : Math.Min(previewCount, info.Entries.Count);
            countLabel.Text = _uiText!.Format("predictor.design.event_count", count, info.Entries.Count);
            expand.Text = Text(_allEvents[act - 1] ? "predictor.collapse" : "predictor.design.show_all");
            foreach (var entry in info.Entries.OrderBy(e => e.Ordinal).Take(count))
            {
                var tile = WEventTile(entry, tooltipSource);
                buttons.Add((entry.EventDisplay.ModelKey, tile)); grid.AddChild(tile);
                tile.Pressed += () =>
                {
                    tooltipSource.DismissEventTooltipFor(tile);
                    _inspectedEvents[act - 1] = _inspectedEvents[act - 1] == entry.EventDisplay.ModelKey ? default : entry.EventDisplay.ModelKey;
                    ShowDetails();
                };
            }
            if (!buttons.Any(item => item.Key == _inspectedEvents[act - 1])) _inspectedEvents[act - 1] = default;
            ShowDetails();
        }
        expand.Pressed += () => { _allEvents[act - 1] = !_allEvents[act - 1]; Populate(); };
        Populate();
    }

    private void RenderEventDetails(Node parent, EventPoolSequenceEntryViewModel entry)
    {
        var detail = WCard(parent, entry.EventDisplay.DisplayName, true);
        string condition = Beta111EventPresentationKnowledge.RuntimeConditionLocalizationKey(_viewModel!.ProfileId, entry.EventDisplay.ModelKey, _viewModel.PlayersCount);
        if (!string.IsNullOrWhiteSpace(condition)) detail.AddChild(WNote(Text(condition)));
        var results = BuildEventResultProjection();
        var resultItems = WResponsiveGrid(detail, "EventResultItems", 240, 3);
        bool shown = false;
        void Add(ModelKey key, GameContentKind kind)
        {
            var row = WObject(key, kind, size: 32, iconVariant: kind == GameContentKind.Character
                ? IconVariant.CharacterPortrait : kind == GameContentKind.Card ? IconVariant.CardPickerLarge : IconVariant.Small);
            BindWorkbenchTooltip(row, key, kind, _contentNames!.Resolve(key, kind));
            resultItems.AddChild(row); shown = true;
        }
        if (results is not null)
        {
            if (entry.EventDisplay.ModelKey.Entry == Beta111EventResultCatalog.TrashHeapEventEntry && results.TrashHeapPrecision == PredictionPrecision.Exact)
            { Add(results.TrashHeapGrabCard, GameContentKind.Card); Add(results.TrashHeapDiveRelic, GameContentKind.Relic); }
            if (entry.EventDisplay.ModelKey.Entry == Beta111EventResultCatalog.FakeMerchantEventEntry && results.FakeMerchantPrecision == PredictionPrecision.Exact)
                foreach (var key in results.FakeMerchantInventory) Add(key, GameContentKind.Relic);
            if (entry.EventDisplay.ModelKey.Entry == Beta111EventResultCatalog.ColorfulPhilosophersEventEntry && results.ColorfulPrecision == PredictionPrecision.Exact)
                foreach (var key in results.ColorfulOfferedColors) Add(key, GameContentKind.Character);
        }
        resultItems.Visible = shown;
        if (!shown) detail.AddChild(WNote(Text("predictor.design.event_result_unavailable")));
    }

    private void RenderRelicInspector()
    {
        _wbCenter.AddChild(WNote(Text("predictor.design.relic_scope")));
        var toolbar = new HBoxContainer(); toolbar.AddThemeConstantOverride("separation", 10); _wbCenter.AddChild(toolbar);
        var search = new LineEdit { Name = "RelicQueueSearch", Text = _relicQuery, PlaceholderText = Text("predictor.design.relic_search"),
            ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38) };
        search.AddThemeFontSizeOverride("font_size", 17);
        search.AddThemeStyleboxOverride("normal", _palette.Box(_palette.Canvas, _palette.Line, 1));
        search.AddThemeStyleboxOverride("focus", _palette.FocusRing());
        search.AddThemeColorOverride("font_color", _palette.Color(_palette.Text));
        toolbar.AddChild(search);
        var mode = WAction(_relicNames ? "predictor.design.icons" : "predictor.design.names");
        mode.Name = "RelicQueueDisplayMode"; toolbar.AddChild(mode);
        var results = WColumn(); results.Name = "RelicQueueResults"; _wbCenter.AddChild(results);
        void Populate()
        {
            WClear(results);
            void Lanes(string title, IEnumerable<RelicSequenceLaneViewModel> lanes)
            {
                VBoxContainer? group = null;
                foreach (var lane in lanes)
                {
                    var entries = lane.FullEntries.OrderBy(e => e.Position)
                        .Where(e => string.IsNullOrWhiteSpace(_relicQuery) || e.RelicDisplay.DisplayName.Contains(_relicQuery.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (entries.Length == 0 && !string.IsNullOrWhiteSpace(_relicQuery)) continue;
                    if (group is null)
                    {
                        group = WColumn(); results.AddChild(group);
                        WTitle(group, title);
                    }
                    var section = WCard(group, "");
                    var heading = new HBoxContainer(); section.AddChild(heading);
                    var rarity = WLabel(lane.Title, true); rarity.Modulate = WRelicRarityColor(lane.Kind);
                    rarity.AddThemeFontSizeOverride("font_size", 17); heading.AddChild(rarity);
                    heading.AddChild(_palette.Label(string.IsNullOrWhiteSpace(_relicQuery)
                        ? lane.FullEntries.Count.ToString() : $"{entries.Length} / {lane.FullEntries.Count}", 14, true));
                    Container items;
                    if (_relicNames) items = WResponsiveGrid(section, "RelicQueueItems", 240, 4);
                    else
                    {
                        items = new HFlowContainer { Name = "RelicQueueItems", SizeFlagsHorizontal = SizeFlags.ExpandFill };
                        items.AddThemeConstantOverride("h_separation", 6); items.AddThemeConstantOverride("v_separation", 6);
                        section.AddChild(items);
                    }
                    foreach (var entry in entries) items.AddChild(_relicNames ? WRelicPreviewTile(entry) : WSequenceIcon(entry));
                    if (entries.Length == 0) WEmpty(section);
                }
            }
            Lanes("predictor.player_relic_bag", _viewModel!.RelicSequenceDomain.Items.Where(l => l.Kind != RelicSequenceKind.Shop));
            Lanes("predictor.chest_shared_bag", _viewModel.TreasureRoomRelicSequenceDomain.Items);
            Lanes("predictor.shops", _viewModel.RelicSequenceDomain.Items.Where(l => l.Kind == RelicSequenceKind.Shop));
            if (results.GetChildCount() == 0) results.AddChild(WNote(Text(string.IsNullOrWhiteSpace(_relicQuery)
                ? "predictor.unavailable" : "predictor.design.no_matches")));
        }
        search.TextChanged += value => { _relicQuery = value; Populate(); };
        mode.Pressed += () => { _relicNames = !_relicNames; mode.Text = Text(_relicNames ? "predictor.design.icons" : "predictor.design.names"); Populate(); };
        Populate();
    }
}
