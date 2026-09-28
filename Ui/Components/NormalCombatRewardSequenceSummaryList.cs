using Godot;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Main-thread rendering of immutable route-grouped normal-combat reward results.
/// Route selection only chooses an already-produced Document branch; it never
/// advances RNG, replays Neow, rebuilds pools, or invokes game reward APIs.
/// Battle layout is driven by the actual result shape: CardReward group count and
/// candidate count, not by relic-specific UI modes.
/// </summary>
internal sealed partial class NormalCombatRewardSequenceSummaryList : VBoxContainer
{
    public const float BattleCardPadding = 10f;
    public const float BattleCardRadius = 3f;
    public const int BattleCardBorderWidth = 1;
    public const int BattleGap = 10;
    public const int BattleContentGap = 7;
    public const int RewardMetaGap = 12;
    public const int RewardGroupGap = 8;
    public const int MiniCardGap = 6;
    public const float PotionIconSize = 28f;
    public const float CandyOuterBattleNormalWeight = 31f;
    public const float CandyOuterBattleWideWeight = 38f;
    public const float StandardGroupWidthInsideWideBattle = CandyOuterBattleNormalWeight / CandyOuterBattleWideWeight;

    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private string _selectedRouteGroupId = string.Empty;
    private float _firstTrackWeight = 1f;
    private float _secondTrackWeight = 1f;
    private float _thirdTrackWeight = 1f;

    public NormalCombatRewardSequenceSummaryList(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);
    }

    public event Action<string>? RouteSelectionChanged;

    public string SelectedRouteGroupId => _selectedRouteGroupId;

    public void ResetSelection() => _selectedRouteGroupId = string.Empty;

    public void SelectRouteGroup(string? routeGroupId) =>
        _selectedRouteGroupId = routeGroupId?.Trim() ?? string.Empty;

    public void SetColumnTrackWeights(float first, float second, float third)
    {
        _firstTrackWeight = NormalizeTrackWeight(first);
        _secondTrackWeight = NormalizeTrackWeight(second);
        _thirdTrackWeight = NormalizeTrackWeight(third);
    }

    public static bool UsesWideMiddleTrack(NormalCombatRewardRouteViewModel route) =>
        route is not null && route.Battles.Any(IsBattleTwoPrimaryFourCardShape);

    public void Bind(
        SeedDomainViewModel<NormalCombatRewardRouteViewModel> domain,
        string unavailableFormat,
        string scopeNote,
        string routePrompt,
        string currentRouteFormat,
        string equivalentRoutesFormat,
        string cardsLabel,
        string potionNoneLabel,
        string missingIconText,
        bool advanced,
        bool showRouteSelector = true,
        string? selectedRouteGroupId = null,
        string? battleShortFormat = null,
        string? rewardGroupShortFormat = null,
        string? goldShortFormat = null,
        string? goldExtraShortFormat = null,
        string? potionNoneShortLabel = null)
    {
        Clear();
        if (showRouteSelector && !string.IsNullOrWhiteSpace(scopeNote))
        {
            AddChild(Ui1Theme.Label(scopeNote, Ui1TextRole.Meta, true));
        }
        if (domain.Status != SeedDomainEvaluationStatus.Evaluated || domain.Items.Count == 0)
        {
            _selectedRouteGroupId = string.Empty;
            AddChild(Ui1Theme.Label(
                string.Format(unavailableFormat, domain.Status, domain.IssueCode),
                Ui1TextRole.Warning,
                true));
            return;
        }

        NormalCombatRewardRouteViewModel[] routes = domain.Items.ToArray();
        OptionButton? selector = null;
        if (showRouteSelector)
        {
            var selectorRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            selectorRow.AddThemeConstantOverride("separation", 10);
            selectorRow.AddChild(Ui1Theme.Label(routePrompt, Ui1TextRole.Body));
            selector = new OptionButton
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(260, 38)
            };
            Ui1Theme.ApplyOptionButton(selector);
            selector.AddItem(routePrompt);
            foreach (NormalCombatRewardRouteViewModel route in routes)
            {
                selector.AddItem(route.RouteLabel);
            }
            selectorRow.AddChild(selector);
            AddChild(selectorRow);
        }

        var routeContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        routeContent.AddThemeConstantOverride("separation", 10);
        AddChild(routeContent);

        void RenderByIndex(int selectedIndex)
        {
            ClearContainer(routeContent);
            if (selectedIndex <= 0 || selectedIndex > routes.Length)
            {
                _selectedRouteGroupId = string.Empty;
                routeContent.AddChild(Ui1Theme.Label(routePrompt, Ui1TextRole.Meta, true));
                return;
            }

            NormalCombatRewardRouteViewModel route = routes[selectedIndex - 1];
            _selectedRouteGroupId = route.RouteGroupId;
            if (showRouteSelector)
            {
                routeContent.AddChild(Ui1Theme.Label(
                    string.Format(currentRouteFormat, route.RouteLabel),
                    Ui1TextRole.SectionTitle,
                    true));
                if (route.EquivalentRouteLabels.Count > 1)
                {
                    routeContent.AddChild(Ui1Theme.Label(
                        string.Format(
                            equivalentRoutesFormat,
                            route.EquivalentRouteLabels.Count,
                            string.Join(" · ", route.EquivalentRouteLabels)),
                        Ui1TextRole.Meta,
                        true));
                }
            }
            if (route.Status != SeedDomainEvaluationStatus.Evaluated)
            {
                routeContent.AddChild(Ui1Theme.Label(
                    string.Format(unavailableFormat, route.Status, route.IssueCode),
                    Ui1TextRole.Warning,
                    true));
                return;
            }

            if (advanced)
            {
                routeContent.AddChild(Ui1Theme.Label(
                    $"{route.PrecisionLabel} · {route.Authority} · {route.Completeness}\n" +
                    $"RewardsDrawCount={route.RewardsDrawCount}\n" +
                    $"RewardContext={route.RewardContextFingerprint}\n" +
                    $"Continuation={route.ContinuationFingerprint}\n" +
                    $"RewardImpact={route.RewardImpactFingerprint}\n" +
                    $"ImpactSources={string.Join(",", route.ActiveRewardImpactSources.Select(source => source.Serialized))}\n" +
                    $"Capabilities={route.Capabilities}\n" +
                    $"UnknownReasons={string.Join(",", route.UnknownReasonCodes)}\n" +
                    $"EquivalentRoutes={string.Join(" | ", route.EquivalentRouteLabels)}\n" +
                    route.EvidenceCode,
                    Ui1TextRole.Meta,
                    true));
            }

            var battles = new VBoxContainer
            {
                Name = "CombatRewardBattleStrip",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
            };
            battles.AddThemeConstantOverride("separation", BattleGap);
            NormalCombatRewardBattleViewModel[] orderedBattles = route.Battles
                .OrderBy(item => item.BattleOrdinal)
                .ToArray();
            bool candyWideShape = UsesWideMiddleTrack(route);
            foreach (NormalCombatRewardBattleViewModel battle in orderedBattles)
            {
                bool wideBattle = candyWideShape && battle.BattleOrdinal == 2;
                PanelContainer battleCard = BuildBattlePanel(
                    battle,
                    cardsLabel,
                    potionNoneLabel,
                    missingIconText,
                    advanced,
                    battleShortFormat,
                    rewardGroupShortFormat,
                    goldShortFormat,
                    goldExtraShortFormat,
                    potionNoneShortLabel,
                    wideBattle);
                battleCard.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
                battles.AddChild(battleCard);
            }
            routeContent.AddChild(battles);
        }

        string requestedRouteGroupId = showRouteSelector
            ? _selectedRouteGroupId
            : selectedRouteGroupId?.Trim() ?? string.Empty;
        int preservedIndex = Array.FindIndex(routes,
            route => string.Equals(route.RouteGroupId, requestedRouteGroupId, StringComparison.Ordinal));
        if (selector is not null)
        {
            selector.ItemSelected += index =>
            {
                RenderByIndex((int)index);
                RouteSelectionChanged?.Invoke(_selectedRouteGroupId);
            };
            if (preservedIndex >= 0)
            {
                selector.Select(preservedIndex + 1);
                RenderByIndex(preservedIndex + 1);
            }
            else
            {
                selector.Select(0);
                RenderByIndex(0);
            }
        }
        else if (preservedIndex >= 0)
        {
            RenderByIndex(preservedIndex + 1);
        }
        else
        {
            RenderByIndex(0);
        }
    }

    private PanelContainer BuildBattlePanel(
        NormalCombatRewardBattleViewModel battle,
        string cardsLabel,
        string potionNoneLabel,
        string missingIconText,
        bool advanced,
        string? battleShortFormat,
        string? rewardGroupShortFormat,
        string? goldShortFormat,
        string? goldExtraShortFormat,
        string? potionNoneShortLabel,
        bool wideBattle)
    {
        var panel = new PanelContainer
        {
            Name = $"CombatRewardBattle{battle.BattleOrdinal}",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Input, BattleCardRadius, BattleCardBorderWidth, BattleCardPadding);

        var battleColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        battleColumn.AddThemeConstantOverride("separation", BattleContentGap);
        string battleTitle = string.IsNullOrWhiteSpace(battleShortFormat)
            ? $"Battle {battle.BattleOrdinal}"
            : string.Format(battleShortFormat, battle.BattleOrdinal);
        Label header = Ui1Theme.Label(battleTitle, Ui1TextRole.CardTitle, wrap: false);
        header.MouseFilter = MouseFilterEnum.Ignore;
        var topInfoRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        topInfoRow.AddThemeConstantOverride("separation", RewardMetaGap);
        topInfoRow.AddChild(header);
        Control rewardSummary = BuildRewardSummaryRow(
            battle,
            potionNoneLabel,
            missingIconText,
            goldShortFormat,
            goldExtraShortFormat,
            potionNoneShortLabel);
        rewardSummary.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        topInfoRow.AddChild(rewardSummary);
        battleColumn.AddChild(topInfoRow);

        var rewards = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        rewards.AddThemeConstantOverride("separation", BattleContentGap);

        IReadOnlyList<NormalCombatRewardCardRewardViewModel> cardRewards = GetCardRewards(battle);
        foreach (NormalCombatRewardCardRewardViewModel cardReward in cardRewards.OrderBy(item => item.RewardOrdinal))
        {
            string groupLabel = string.IsNullOrWhiteSpace(rewardGroupShortFormat)
                ? $"{cardsLabel} {cardReward.RewardOrdinal}"
                : string.Format(rewardGroupShortFormat, cardReward.RewardOrdinal);
            Label label = Ui1Theme.Label(groupLabel, Ui1TextRole.Muted, wrap: false);
            label.MouseFilter = MouseFilterEnum.Ignore;

            var groupRow = new HBoxContainer
            {
                Name = $"Battle{battle.BattleOrdinal}RewardGroup{cardReward.RewardOrdinal}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                Alignment = BoxContainer.AlignmentMode.Begin
            };
            groupRow.AddThemeConstantOverride("separation", MiniCardGap);
            groupRow.AddChild(label);
            foreach (NormalCombatRewardCardViewModel card in cardReward.Cards.OrderBy(item => item.Ordinal))
            {
                groupRow.AddChild(BuildRewardItem(card, missingIconText));
            }
            rewards.AddChild(groupRow);
        }
        battleColumn.AddChild(rewards);

        if (advanced)
        {
            rewards.AddChild(Ui1Theme.Label(
                $"{battle.PrecisionLabel} · {battle.Authority} · {battle.Completeness}\n" +
                $"RNG: {battle.RngStream}:{battle.RngCallCountBefore}->{battle.RngCallCountAfter}\n" +
                $"Gold: {battle.GoldStatus} · RngCallConsumed={battle.GoldRngCallConsumed} · " +
                $"Entries={string.Join("|", battle.GoldRewards.Select(item => $"{item.Amount}:{item.Source?.Serialized ?? "base"}"))}\n" +
                $"PotionOdds: {battle.PotionOddsBefore:0.00}->{battle.PotionOddsAfter:0.00} · " +
                $"CardOffset: {battle.CardRarityOffsetBefore:0.0000}->{battle.CardRarityOffsetAfter:0.0000}\n" +
                $"Reason: {battle.ReasonCode}\n" +
                $"RewardImpact: {battle.RewardImpactFingerprint}\n" +
                $"ImpactSources: {string.Join(",", battle.AppliedRewardImpactSources.Select(source => source.Serialized))}\n" +
                $"Conditions: {string.Join(", ", battle.ConditionalAssumptions)}\n" +
                battle.EvidenceCode,
                Ui1TextRole.Meta,
                true));
        }

        panel.AddChild(battleColumn);
        return panel;
    }

    private Control BuildRewardItem(
        NormalCombatRewardCardViewModel card,
        string missingIconText)
    {
        var item = new IconWithLabel(32f, Ui1TextRole.Body)
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1f,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        item.UseClippedName();
        item.Bind(
            _icons.Resolve(card.CardDisplay.ModelKey, GameContentKind.Card, IconVariant.CardPickerLarge),
            card.CardDisplay.DisplayName + card.DisplaySuffix,
            card.CardDisplay.Tooltip,
            missingIconText);
        item.IconAnchor.MouseEntered += () =>
            _tooltipHost.ShowFor(item.IconAnchor, card.CardDisplay.ModelKey, GameContentKind.Card, card.CardDisplay.DisplayName);
        item.IconAnchor.MouseExited += () => _tooltipHost.Dismiss(item.IconAnchor);
        item.IconAnchor.TreeExiting += () => _tooltipHost.Dismiss(item.IconAnchor);
        return item;
    }

    private Control BuildRewardSummaryRow(
        NormalCombatRewardBattleViewModel battle,
        string potionNoneLabel,
        string missingIconText,
        string? goldShortFormat,
        string? goldExtraShortFormat,
        string? potionNoneShortLabel)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        row.AddThemeConstantOverride("separation", RewardMetaGap);

        IReadOnlyList<NormalCombatRewardGoldRewardViewModel> goldRewards = battle.GoldRewards.Count > 0
            ? battle.GoldRewards
            : Array.Empty<NormalCombatRewardGoldRewardViewModel>();
        if (goldRewards.Count > 0)
        {
            foreach (NormalCombatRewardGoldRewardViewModel gold in goldRewards.OrderBy(item => item.RewardOrdinal))
            {
                string format = gold.IsBaseReward || string.IsNullOrWhiteSpace(goldExtraShortFormat)
                    ? goldShortFormat ?? "Gold {0}"
                    : goldExtraShortFormat;
                Label goldLabel = Ui1Theme.Label(string.Format(format, gold.Amount), Ui1TextRole.Body, wrap: false);
                goldLabel.MouseFilter = MouseFilterEnum.Ignore;
                row.AddChild(goldLabel);
            }
        }
        else if (battle.Gold.HasValue)
        {
            Label goldLabel = Ui1Theme.Label(
                string.Format(goldShortFormat ?? "Gold {0}", battle.Gold.Value),
                Ui1TextRole.Body,
                wrap: false);
            goldLabel.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(goldLabel);
        }
        else
        {
            Label goldUnavailable = Ui1Theme.Label(battle.GoldLabel, Ui1TextRole.Muted, wrap: false);
            goldUnavailable.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(goldUnavailable);
        }

        if (battle.Potion.Generated && battle.Potion.PotionDisplay is { } potionDisplay)
        {
            var potion = new IconWithLabel(PotionIconSize, Ui1TextRole.Body)
            {
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            potion.Bind(
                _icons.Resolve(potionDisplay.ModelKey, GameContentKind.Potion, IconVariant.Small),
                potionDisplay.DisplayName,
                string.Empty,
                missingIconText);
            potion.IconAnchor.MouseEntered += () =>
                _tooltipHost.ShowFor(potion.IconAnchor, potionDisplay.ModelKey, GameContentKind.Potion, potionDisplay.DisplayName);
            potion.IconAnchor.MouseExited += () => _tooltipHost.Dismiss(potion.IconAnchor);
            potion.IconAnchor.TreeExiting += () => _tooltipHost.Dismiss(potion.IconAnchor);
            row.AddChild(potion);
        }
        else
        {
            string none = string.IsNullOrWhiteSpace(potionNoneShortLabel)
                ? potionNoneLabel
                : potionNoneShortLabel;
            Label potionNone = Ui1Theme.Label(none, Ui1TextRole.Muted, wrap: false);
            potionNone.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(potionNone);
        }

        return row;
    }

    private static IReadOnlyList<NormalCombatRewardCardRewardViewModel> GetCardRewards(
        NormalCombatRewardBattleViewModel battle) =>
        battle.CardRewards.Count > 0
            ? battle.CardRewards
            : new[]
            {
                new NormalCombatRewardCardRewardViewModel(
                    1,
                    true,
                    battle.Cards,
                    battle.EvidenceCode)
            };

    private float TrackWeightForBattle(int battleOrdinal) => battleOrdinal switch
    {
        1 => _firstTrackWeight,
        2 => _secondTrackWeight,
        3 => _thirdTrackWeight,
        _ => 1f
    };

    private static float NormalizeTrackWeight(float value) =>
        float.IsFinite(value) && value > 0f ? value : 1f;

    private static bool IsBattleTwoPrimaryFourCardShape(NormalCombatRewardBattleViewModel battle)
    {
        if (battle.BattleOrdinal != 2)
        {
            return false;
        }

        NormalCombatRewardCardRewardViewModel? primary = GetCardRewards(battle)
            .OrderBy(group => group.RewardOrdinal)
            .FirstOrDefault();
        return primary?.Cards.Count >= 4;
    }

    /// <summary>
    /// Runtime-width-aware reward row. A normal group consumes its available battle
    /// width; when Battle 2 is widened by a four-card primary group, later three-card
    /// groups intentionally use only the standard 31/38 share and remain left aligned.
    /// </summary>
    private sealed partial class ResponsiveMiniCardRow : HBoxContainer
    {
        private readonly float _widthFraction;
        private readonly int _gap;
        private readonly List<PredictorMiniCard> _cards = new();

        public ResponsiveMiniCardRow(float widthFraction, int gap)
        {
            _widthFraction = Math.Clamp(widthFraction, 0.5f, 1f);
            _gap = gap;
            AddThemeConstantOverride("separation", gap);
            Resized += RefreshSizing;
        }

        public void AddMiniCard(PredictorMiniCard card)
        {
            _cards.Add(card);
            AddChild(card);
            RefreshSizing();
        }

        private void RefreshSizing()
        {
            if (_cards.Count == 0)
            {
                return;
            }

            float available = Size.X;
            if (available <= 1f)
            {
                return;
            }

            float targetWidth = available * _widthFraction;
            float gaps = _gap * Math.Max(0, _cards.Count - 1);
            float perCard = Math.Max(PredictorMiniCard.MinimumResponsiveWidth,
                (targetWidth - gaps) / _cards.Count);
            foreach (PredictorMiniCard card in _cards)
            {
                card.SetResponsiveWidth(perCard);
            }
        }
    }

    private void Clear()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void ClearContainer(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
