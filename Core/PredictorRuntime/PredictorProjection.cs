using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorInventoryView(ImmutableArray<PredictorCard> Deck,
    ImmutableArray<PredictorRelic> Relics, ImmutableArray<PredictorPotion?> PotionSlots, int GoldBudget);
internal sealed record PredictorRewardView(PredictorReward Reward, bool CanOpen, bool CanTake, bool CanExitCardSelection);
internal sealed record PredictorPotionView(int Slot, PredictorPotion? Potion, bool CanDiscard, bool CanUseOutsideCombat);
internal sealed record PredictorRouteRoomView(MapPosition Point, PredictorRoomKind Room, bool Preview);

// A value projection of the existing transaction. It does not generate offers,
// run hooks, draw randomness or turn UI selections into execution state.
internal sealed record PredictorRuntimeView(PredictorContext Context, PredictorPosition Position,
    PredictorInventoryView Inventory, ImmutableArray<PredictorActMap> Maps, ImmutableArray<PredictorRoute> Routes,
    ImmutableArray<PredictorNodeRecord> History, PredictorRequest? Request, PredictorPhase Phase,
    PredictorRunOutcome? Outcome)
{
    public ImmutableArray<PredictorRewardView> Rewards { get; init; } = [];
    public ImmutableArray<PredictorVisit> Visits { get; init; } = [];
    public ModelKey? CurrentNodeModel { get; init; }
    public ModelKey? CurrentEvent { get; init; }
    public ModelKey? CurrentAncient { get; init; }
    public ModelKey? CurrentObtainSource { get; init; }
    public PredictorObtainDeckOperation? ObtainDeckOperation { get; init; }
    public ImmutableArray<PredictorAcceptedInput> CurrentNodeInputs { get; init; } = [];
    public ImmutableArray<PredictorReward> CompletedChildRewards { get; init; } = [];
    public ImmutableArray<PredictorFakeMerchantStock> FakeMerchantStock { get; init; } = [];
    public bool FakeMerchantInventoryOpen { get; init; }
    public bool CanSkipObtainCard { get; init; }
    public int ObtainManyMinimum { get; init; }
    public int ObtainManyMaximum { get; init; }
    public ImmutableArray<PredictorHealthEffect> CurrentHealthEffects { get; init; } = [];
    public ImmutableArray<PredictorCardRewardAlternative> CardRewardAlternatives { get; init; } = [];
    public PredictorSelection? DeckSelection { get; init; }
    public ImmutableArray<ModelKey> OpeningChoices { get; init; } = [];
    public int? OpeningModifierOrdinal { get; init; }
    public ImmutableArray<PredictorCard> OpeningModifierCards { get; init; } = [];
    public int OpeningModifierCardsRequired { get; init; }
    public ImmutableArray<PredictorAncientOffer> AncientChoices { get; init; } = [];
    public ImmutableArray<PredictorEventOption> EventChoices { get; init; } = [];
    public PredictorCrystalView? Crystal { get; init; }
    public ImmutableArray<long> EventCardCandidates { get; init; } = [];
    public ImmutableArray<PredictorCard> EventOfferedCards { get; init; } = [];
    public int EventCardsRequired { get; init; }
    public ImmutableArray<PredictorShopOffer> ShopOffers { get; init; } = [];
    public int ShopRevision { get; init; }
    public bool ShopInventoryOpen { get; init; }
    public ImmutableArray<long> ShopRemovalCandidates { get; init; } = [];
    public bool ShopAutomaticPurchasesActive { get; init; }
    public ImmutableArray<PredictorRestOffer> RestOffers { get; init; } = [];
    public ImmutableArray<PredictorCard> ObtainCardChoices { get; init; } = [];
    public ImmutableArray<PredictorCard> ObtainManyCardChoices { get; init; } = [];
    public ImmutableArray<ImmutableArray<PredictorCard>> BundleChoices { get; init; } = [];
    public ImmutableArray<PredictorPotionView> Potions { get; init; } = [];
    public PredictorRelic? TreasureRelic { get; init; }
    public ModelKey? EventEligibilityCandidate { get; init; }
    public bool NeedsHealthFact { get; init; }
    public bool NeedsRoomMaximumHp { get; init; }
    public bool NeedsCombatFacts { get; init; }
    public bool CanAdjustCombat { get; init; }
    public ResolveCombat? CombatInputForEditing { get; init; }
    public ImmutableArray<ModelKey> TemporaryCombatCardSources { get; init; } = [];
    public bool NeedsPotionBoundaryConfirmation { get; init; }
    public bool NeedsCombatInventoryConfirmation { get; init; }
    public ImmutableArray<long> CombatLizardTailCandidates { get; init; } = [];
    public ImmutableArray<long> DefaultCombatCardInstances { get; init; } = [];
    public ImmutableArray<PredictorEnemyCreationSource> RequiredEnemyCreationFacts { get; init; } = [];
    public bool NeedsStolenCardFacts { get; init; }
    public bool NeedsGremlinGoldFacts { get; init; }
    public bool NeedsFirstPlayerTurnFact { get; init; }
    public bool NeedsVictoryHealthFact { get; init; }
    public ModelKey? CombatEncounter { get; init; }
    public bool FurCoatMarkedEnemies { get; init; }
    public bool NeedsEventTimeoutFact { get; init; }
    public bool NeedsAbandonConfirmation { get; init; }
    public bool CanAdvance { get; init; }
    public ImmutableArray<int> RouteActsNeedingReplan { get; init; } = [];
    public bool CanLeave { get; init; }
    public bool CanCancelTargetSelection { get; init; }
}

internal sealed partial class PredictorRun
{
    // Conditional map-type projection only: retain current inventory, replay
    // the real room resolver on an isolated state, and never settle choices.
    // Future Acts wait for their actual entry/reset and map hooks.
    internal ImmutableArray<PredictorRouteRoomView> PreviewRouteRoomsUnderCurrentInventory(
        int act, ImmutableArray<MapPosition> path)
    {
        var result = Working.Visits.Where(v => v.Act == act && v.PointType == MapPointType.Unknown)
            .Select(v => new PredictorRouteRoomView(v.Point, v.Room, false)).ToImmutableArray();
        if (path.IsDefaultOrEmpty || act != NextRouteAct ||
            (act != 1 && !Working.Maps.Any(m => m.Act == act))) return result;
        var map = RouteMap(act);
        var last = Working.Visits.LastOrDefault(v => v.Act == act);
        int start = last is null ? 0 : path.IndexOf(last.Point) + 1;
        if (last != null && start == 0) return result;
        var preview = new PredictorRun(Context, Working);
        var actMap = new PredictorActMap(act, map, []);
        foreach (var position in path.Skip(start))
        {
            var point = map.Points.SingleOrDefault(p => p.Position == position);
            if (point is null) break;
            var room = preview.ResolveRoom(point, actMap);
            preview.Working = preview.Working with { Visits = preview.Working.Visits.Add(
                new(act, position, point.Type, room, null, preview.Working.Visits.Length + 1)) };
            if (point.Type == MapPointType.Unknown) result = result.Add(new(position, room, true));
        }
        return result;
    }

    private ModelKey? ActiveObtainSource => Phase switch
    {
        PredictorPhase.ObtainDeckCards => _obtainDeckSource,
        PredictorPhase.ObtainChooseCard => _obtainCardSource,
        PredictorPhase.ObtainChooseManyCards => new("RELIC", "SEA_GLASS"),
        PredictorPhase.ChooseBundle => new("RELIC", "SCROLL_BOXES"),
        PredictorPhase.TransformCards when _newLeafObtain != null => new("RELIC", "NEW_LEAF"),
        PredictorPhase.ObtainHealth when _leafyPending != null => new("RELIC", "LEAFY_POULTICE"),
        PredictorPhase.ObtainHealth when _fragrantPending != null => new("RELIC", "FRAGRANT_MUSHROOM"),
        PredictorPhase.ObtainHealth when _wafflePending != null => new("RELIC", "LEES_WAFFLE"),
        PredictorPhase.ObtainHealth when _fakeWafflePending != null => new("RELIC", "FAKE_LEES_WAFFLE"),
        PredictorPhase.ObtainHealth when _serePending != null => new("RELIC", "SERE_TALON"),
        PredictorPhase.ObtainHealth when _precariousPending != null => new("RELIC", "PRECARIOUS_SHEARS"),
        _ => _obtainFrames.LastOrDefault()?.Relic
    };

    internal PredictorRuntimeView Project()
    {
        RequireRouteSimulation();
        var combatRequirements = CombatRequirements;
        bool rewardsActive = Phase == PredictorPhase.Rewards;
        bool freePotionActions = Request != null && Phase is not (PredictorPhase.CombatFacts or PredictorPhase.OpeningChoice or
            PredictorPhase.EventEligibility or PredictorPhase.AncientChoice or PredictorPhase.RoomEntryHealth) && !EventPotionsLocked;
        return new(Context, Working.Position, new(Working.Deck.Select(Working.Card).ToImmutableArray(), Working.Relics,
            Working.PotionSlots, Working.GoldBudget), Working.Maps, Routes,
            Checkpoints.Where(c => c.CompletedNode != null).Select(c => c.CompletedNode!).ToImmutableArray(), Request, Phase, Working.Outcome)
        {
            Rewards = Rewards.Select(r => new PredictorRewardView(r,
                rewardsActive && r.Status == PredictorRewardStatus.Open && r.Kind == PredictorRewardKind.Card && _cardSelection == null,
                rewardsActive && r.Status == PredictorRewardStatus.Open && (_cardSelection == null || _cardSelection == r.Id) &&
                    (r.Kind != PredictorRewardKind.Card || r.CardSelectionEntered) &&
                    (r.Kind != PredictorRewardKind.RemoveCard || Working.Deck.Any(id => Working.Card(id).Removable)),
                rewardsActive && r.Status == PredictorRewardStatus.Open && _cardSelection == r.Id)).ToImmutableArray(),
            DeckSelection = Selection,
            Visits = Working.Visits, CurrentNodeModel = Working.Visits.LastOrDefault()?.Model,
            CurrentEvent = _eventKey,
            CurrentAncient = Working.Visits.LastOrDefault()?.Room == PredictorRoomKind.Ancient ? CurrentAncient : null,
            CurrentObtainSource = ActiveObtainSource,
            ObtainDeckOperation = Phase == PredictorPhase.ObtainDeckCards ? _obtainDeckOperation : null,
            CurrentNodeInputs = _inputs, CompletedChildRewards = CompletedChildRewards,
            FakeMerchantStock = _eventKey?.Entry == "FAKE_MERCHANT" ? FakeMerchantStock : [],
            FakeMerchantInventoryOpen = _eventKey?.Entry == "FAKE_MERCHANT" && _fakeInventoryOpen,
            CanSkipObtainCard = Phase == PredictorPhase.ObtainChooseCard,
            ObtainManyMinimum = 0,
            ObtainManyMaximum = Phase == PredictorPhase.ObtainChooseManyCards ? _obtainManyChoices.Length : 0,
            CurrentHealthEffects = Working.CurrentHealthEffects,
            CardRewardAlternatives = CardRewardAlternatives,
            OpeningChoices = Phase == PredictorPhase.OpeningChoice ? Working.OpeningOptions : [],
            OpeningModifierOrdinal = OpeningModifierOrdinal, OpeningModifierCards = OpeningModifierCards,
            OpeningModifierCardsRequired = OpeningModifierCardsRequired,
            AncientChoices = Phase == PredictorPhase.AncientChoice ? AncientOffers : [],
            EventChoices = EventOptions,
            Crystal = CrystalView,
            EventCardCandidates = EventCardCandidates, EventCardsRequired = EventCardsRequired,
            EventOfferedCards = EventOfferedCards,
            ShopOffers = ShopOffers, ShopRevision = ShopInventoryRevision, ShopRemovalCandidates = ShopRemovalCandidates,
            ShopInventoryOpen = ShopInventoryOpen,
            ShopAutomaticPurchasesActive = ShopParasolActive,
            RestOffers = Phase == PredictorPhase.RestSite ? RestOffers : [],
            ObtainCardChoices = ObtainCardChoices, ObtainManyCardChoices = ObtainManyCardChoices,
            BundleChoices = ScrollBoxBundles,
            Potions = Working.PotionSlots.Select((p, i) => new PredictorPotionView(i, p, freePotionActions && p != null,
                freePotionActions && (p?.Key.Entry is "ENTROPIC_BREW" or "AMBERGRIS" or "BLOOD_POTION" or "FRUIT_JUICE" ||
                    p?.Key.Entry == "FOUL_POTION" && (CanUseFoulPotionInShop || CanUseFoulPotionInEvent)))).ToImmutableArray(),
            TreasureRelic = TreasureRelic, EventEligibilityCandidate = EventEligibilityCandidate,
            NeedsHealthFact = EventNeedsHealthFact || Phase is PredictorPhase.ObtainHealth or PredictorPhase.RestHealFact,
            NeedsRoomMaximumHp = Phase == PredictorPhase.RoomEntryHealth,
            NeedsCombatFacts = Phase == PredictorPhase.CombatFacts,
            CanAdjustCombat = CanAdjustCurrentCombat,
            CombatInputForEditing = Phase == PredictorPhase.CombatFacts ? _combatInputForEditing : null,
            TemporaryCombatCardSources = Phase == PredictorPhase.CombatFacts ? Context.Catalog!.CardDefinitions
                .Select(d => d.Prototype.Key).Where(PredictorCombatConsequences.IsTemporarySource).ToImmutableArray() : [],
            NeedsPotionBoundaryConfirmation = Phase == PredictorPhase.CombatFacts,
            NeedsCombatInventoryConfirmation = Phase == PredictorPhase.CombatFacts,
            CombatLizardTailCandidates = Phase == PredictorPhase.CombatFacts ? Working.Relics
                .Where(r => !r.Melted && r.Key.Entry == "LIZARD_TAIL" && !r.LizardTailUsed).Select(r => r.Id).ToImmutableArray() : [],
            RequiredEnemyCreationFacts = combatRequirements.EnemyCreations,
            NeedsStolenCardFacts = combatRequirements.StolenCards,
            NeedsGremlinGoldFacts = combatRequirements.GremlinGold,
            NeedsFirstPlayerTurnFact = combatRequirements.FirstPlayerTurn,
            NeedsVictoryHealthFact = combatRequirements.VictoryHealth,
            DefaultCombatCardInstances = Phase == PredictorPhase.CombatFacts ? Working.Deck.Where(id => PredictorCombatConsequences.IsTriggeredSource(Working.Card(id))).ToImmutableArray() : [],
            CombatEncounter = (_eventCombatEntry ?? _combatEntry)?.Encounter.Key,
            FurCoatMarkedEnemies = (_eventCombatEntry ?? _combatEntry)?.FurCoatMarkedEnemies == true,
            NeedsEventTimeoutFact = combatRequirements.EventTimeout,
            NeedsAbandonConfirmation = TrialAbandonConfirmationPending,
            CanAdvance = Phase == PredictorPhase.Complete && Working.Outcome == null &&
                (NextRouteAct is not { } nextAct || !RouteActsNeedingReplan.Contains(nextAct)),
            RouteActsNeedingReplan = RouteActsNeedingReplan,
            CanLeave = Phase switch
            {
                PredictorPhase.Rewards => _cardSelection == null && !Rewards.Any(r => r.Status == PredictorRewardStatus.Open && r.Mandatory),
                PredictorPhase.Shop => !ShopInventoryOpen && !ShopParasolActive && _shopRemovalEntry == null,
                PredictorPhase.RestSite => true,
                PredictorPhase.TreasureClosed or PredictorPhase.TreasureRelic => !_treasureObtaining,
                _ => false
            },
            CanCancelTargetSelection = Phase is PredictorPhase.SmithCards or PredictorPhase.RestCookCards or PredictorPhase.RewardRemoveCards ||
                Phase == PredictorPhase.Shop && _shopRemovalEntry != null && !ShopParasolActive
        };
    }
}
