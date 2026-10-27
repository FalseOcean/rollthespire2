using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record ChooseEventOption(string OptionKey) : PredictorInput;
internal sealed record SelectEventCards(ImmutableArray<long> CardIds) : PredictorInput;
internal sealed record PredictorEventOption(string Key, bool Enabled = true, int? GoldCost = null, int? GoldGain = null,
    long? CardTargetId = null, int? HealthDamage = null, long? PotionId = null, EffectCardType? CardType = null,
    int? MaxHpCost = null, bool? WillKill = null, long? RelicId = null, ModelKey? OfferedRelic = null,
    int? HealthHeal = null, int? LocalPotionOrdinal = null, ModelKey? LocalPotionKey = null);
internal sealed record PredictorEventHealthFact(int CurrentHp, int MaxHp);
internal sealed record PredictorEventHealthDelta(int HpDelta, int MaxHpDelta);
internal sealed record ProvideEventHealthFact(PredictorEventHealthFact Fact) : PredictorInput;
// Event-local drawing premises. This does not establish current or maximum HP.
internal sealed record PredictorConveyorInput(int EmptyPotionSlots, int LostHp);
internal sealed record ProvideConveyorInputs(PredictorConveyorInput Fact) : PredictorInput;
internal sealed record ConfirmTrialAbandon(bool Confirm) : PredictorInput;
internal sealed record PredictorFakeMerchantStock(int Slot, ModelKey Key, int Price, bool Stocked);

internal sealed partial class PredictorRun
{
    private ModelKey? _eventKey;
    private Xoshiro256StarStar? _eventRng;
    private string? _eventBranch;
    private int _eventCardsRequired;
    private ImmutableArray<long> _eventCardCandidates = [];
    private bool _eventRewardContinuation;
    private PredictorEventHealthFact? _eventHealth;
    private string? _pendingHealthOption;
    private ImmutableArray<PredictorEventOption> _pendingHealthOptions = [];
    private int _eventGoldAmount;
    internal bool EventPotionsLocked => Phase != PredictorPhase.Complete &&
        _eventKey?.Entry is ("THE_FUTURE_OF_POTIONS" or "STONE_OF_ALL_TIME" or "RANWID_THE_ELDER");
    internal bool TrialAbandonConfirmationPending => _trialAbandonPending;
    internal bool EventNeedsHealthFact => _pendingHealthOption != null || _conveyorEntryHealthPending;
    internal ImmutableArray<PredictorEventOption> EventOptions { get; private set; } = [];
    internal ImmutableArray<PredictorFakeMerchantStock> FakeMerchantStock => _fakeStock;
    internal bool CanUseFoulPotionInEvent => _eventKey?.Entry == "FAKE_MERCHANT" &&
        Phase == PredictorPhase.EventChoice && !_fakeInventoryOpen;
    internal ImmutableArray<PredictorCard> EventOfferedCards => _eventOfferedCards;
    internal ImmutableArray<long> EventCardCandidates => _eventCardCandidates;
    internal int EventCardsRequired => _eventCardsRequired;

    internal void RefreshEventOptionsAfterBudgetCorrection()
    {
        if (Phase != PredictorPhase.EventChoice || _eventKey == null || _pendingHealthOption != null) return;
        if (_eventKey.Value.Entry == "ENDLESS_CONVEYOR" && !_conveyorEntryHealthPending)
        {
            EventOptions = ConveyorOptions();
            _requestId++;
            return;
        }
        EventOptions = EventOptions.Select(o => o.Key is "EMOTIONAL_AWARENESS" or "ARACHNID_ACUPUNCTURE" or
            "BONE_TEA" or "EMBER_TEA" or "OFFER_TRIBUTE" or "TENTACLE_QUILL" or "PRICKLY_SPONGE" or
            "BARGAIN_BIN" or "FEATURED_ITEM" or "MYSTERY_BOX" ||
            o.Key.StartsWith("BUY_", StringComparison.Ordinal)
            ? o with { Enabled = o.GoldCost <= Working.GoldBudget } : o).ToImmutableArray();
        _requestId++;
    }

    internal void RefreshEventOptionsAfterPotionMutation()
    {
        if (_eventKey?.Entry != "FAKE_MERCHANT" || Phase != PredictorPhase.EventChoice || _fakeInventoryOpen) return;
        EventOptions = FakeMerchantOptions();
        _requestId++;
    }

    internal void BeginEvent(PredictorPosition position, ModelKey eventKey, PredictorEventHealthFact? health = null)
    {
        _preview.Event.EnterCombatParent();
        RequireRouteSimulation();
        if (Phase != PredictorPhase.Complete || eventKey.Category != "EVENT" || !eventKey.IsValid ||
            position.Act is < 1 or > 3 || position.Floor < 1 || string.IsNullOrWhiteSpace(position.NodeId))
            throw new InvalidOperationException("PredictorEventEntryInvalid");
        if (_checkpoints.Any(c => c.CompletedNode?.NodeId == position.NodeId))
            throw new InvalidOperationException("PredictorEventDuplicateNode");
        PredictorSettlementEffects.RequireImplementedInventory(Working, context: Context);
        _eventKey = eventKey;
        _preview.Event.EnterEvent();
        _eventRng = new Xoshiro256StarStar(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(Context.Seed), 0, eventKey.Entry));
        _eventBranch = null;
        ResetEventChoiceState(bathsDamage: 3, resetAssignedGold: true);
        if (health is { CurrentHp: < 1 } || health is { MaxHp: < 1 } ||
            health is not null && health.CurrentHp > health.MaxHp)
            throw new ArgumentException("PredictorEventHealthFactInvalid", nameof(health));
        _eventHealth = health;
        _eventRewardContinuation = false;
        _eventCardsRequired = 0;
        _eventCardCandidates = [];
        Working = Working with { Position = position };
        BeginEventPotionInput(eventKey);
        var eventTrace = EventTrace();
        eventTrace?.Seed(this, eventKey);
        _inputs = [];
        Rewards = [];
        if (!MissingLocalEventPotionRarities(eventKey).IsEmpty)
        {
            // Detached active-draft replay may reach setup before the route
            // consumer can report the missing rarity. Defer these options;
            // RequireLocalEventPotionRarities owns their explicit local gap.
            EventOptions = [];
            Phase = PredictorPhase.EventChoice;
            _requestId++;
            return;
        }
        EventOptions = eventKey.Entry switch
        {
            "AROMA_OF_CHAOS" => [new("LET_GO"), new("MAINTAIN_CONTROL")],
            "MORPHIC_GROVE" =>
                [new("GROUP"), new("LONER")],
            "WHISPERING_HOLLOW" =>
                [new("GOLD", GoldCost: 35 + _eventRng.NextInt(19) - 9), new("HUG")],
            "TRASH_HEAP" => [new("DIVE_IN"), new("GRAB")],
            "SYMBIOTE" => [new("APPROACH", SymbioteCandidates().Length > 0), new("KILL_WITH_FIRE")],
            "TINKER_TIME" => [new("CHOOSE_CARD_TYPE")],
            "COLORFUL_PHILOSOPHERS" => ColorfulOptions(),
            "TRIAL" => [new("ACCEPT"), new("REJECT")],
            "THIS_OR_THAT" => ThisOrThatOptions(),
            "UNREST_SITE" => [new("REST"), new("KILL", MaxHpCost: 8)],
            "SAPPHIRE_SEED" => [new("EAT"), new("PLANT")],
            "ZEN_WEAVER" => ZenWeaverOptions(),
            "POTION_COURIER" => [new("GRAB_POTIONS"), new("RANSACK")],
            "REFLECTIONS" => [new("TOUCH_A_MIRROR"), new("SHATTER")],
            "WELLSPRING" => [new("BOTTLE"), new("BATHE")],
            "SLIPPERY_BRIDGE" => SlipperyBridgeOptions(),
            "TEA_MASTER" => TeaMasterOptions(),
            "THE_FUTURE_OF_POTIONS" => FuturePotionOptions(),
            "STONE_OF_ALL_TIME" => StoneOfAllTimeOptions(),
            "SUNKEN_STATUE" => SunkenStatueOptions(),
            "FIELD_OF_MAN_SIZED_HOLES" => [new("RESIST"), new("ENTER_YOUR_HOLE")],
            "ROUND_TEA_PARTY" => [new("ENJOY_TEA"), new("PICK_FIGHT")],
            "ABYSSAL_BATHS" => [new("IMMERSE", HealthDamage: 1), new("ABSTAIN")],
            "DROWNING_BEACON" => [new("BOTTLE"), new("CLIMB", MaxHpCost: 13)],
            "TABLET_OF_TRUTH" => [new("DECIPHER", MaxHpCost: 3,
                WillKill: _eventHealth?.MaxHp <= 3), new("SMASH")],
            "LUMINOUS_CHOIR" => LuminousChoirOptions(),
            "LOST_WISP" => LostWispOptions(),
            "AMALGAMATOR" => [new("COMBINE_STRIKES"), new("COMBINE_DEFENDS")],
            "COLOSSAL_FLOWER" => ColossalFlowerOptions(),
            "BUGSLAYER" => [new("EXTERMINATION"), new("SQUASH")],
            "SUNKEN_TREASURY" => SunkenTreasuryOptions(),
            "RANWID_THE_ELDER" => RanwidOptions(),
            "WOOD_CARVINGS" => [new("BIRD"), new("SNAKE", WoodSnakeCandidates().Length > 0), new("TORUS")],
            "RELIC_TRADER" => RelicTraderOptions(),
            "DENSE_VEGETATION" => DenseVegetationOptions(),
            "SELF_HELP_BOOK" => SelfHelpBookOptions(),
            "SPIRALING_WHIRLPOOL" => [new("OBSERVE"),
                new("DRINK", HealthHeal: _eventHealth == null ? null : (int)(_eventHealth.MaxHp * .33m))],
            "DOORS_OF_LIGHT_AND_DARK" => [new("LIGHT"), new("DARK")],
            "PUNCH_OFF" => PunchOffOptions(),
            "BATTLEWORN_DUMMY" => [new("SETTING_1"), new("SETTING_2"), new("SETTING_3")],
            "FAKE_MERCHANT" => FakeMerchantOptions(initializing: true),
            "THE_LANTERN_KEY" => TheLanternKeyOptions(),
            "GRAVE_OF_THE_FORGOTTEN" =>
                [new("CONFRONT", GraveSoulCandidates().Length > 0), new("ACCEPT")],
            "ROOM_FULL_OF_CHEESE" =>
                [new("GORGE"), new("SEARCH", HealthDamage: 14)],
            "WATERLOGGED_SCRIPTORIUM" =>
                [new("BLOODY_INK", HealthHeal: 6),
                 new("TENTACLE_QUILL", Working.GoldBudget >= 55, GoldCost: 55),
                 new("PRICKLY_SPONGE", Working.GoldBudget >= 99, GoldCost: 99)],
            "THE_LEGENDS_WERE_TRUE" =>
                [new("NAB_THE_MAP"), new("SLOWLY_FIND_AN_EXIT", HealthDamage: 8)],
            "BRAIN_LEECH" =>
                [new("SHARE_KNOWLEDGE"), new("RIP", HealthDamage: 5)],
            "HUNGRY_FOR_MUSHROOMS" => [new("BIG_MUSHROOM"), new("FRAGRANT_MUSHROOM", HealthDamage: 15)],
            "BYRDONIS_NEST" => [new("EAT", HealthHeal: 7), new("TAKE")],
            "DOLL_ROOM" =>
                [new("RANDOM"), new("TAKE_SOME_TIME", HealthDamage: 5), new("EXAMINE", HealthDamage: 15)],
            "INFESTED_AUTOMATON" => [new("STUDY"), new("TOUCH_CORE")],
            "SPIRIT_GRAFTER" => [new("LET_IT_IN", HealthHeal: 25), new("REJECTION", HealthDamage: 10)],
            "ENDLESS_CONVEYOR" => BeginConveyorOptions(),
            "JUNGLE_MAZE_ADVENTURE" => JungleMazeOptions(),
            "WELCOME_TO_WONGOS" => WongoOptions(),
            "WAR_HISTORIAN_REPY" => [new("INITIAL_UNLOCK_CAGE"), new("INITIAL_UNLOCK_CHEST")],
            "CRYSTAL_SPHERE" => CrystalSphereOptions(),
            _ => throw new NotImplementedException("PredictorEventPending:" + eventKey)
        };
        Phase = PredictorPhase.EventChoice;
        _requestId++;
        eventTrace?.Setup(this, _preview.Partial?.EventTraceDependencies ?? []);
    }

    // null means this is not an event input. The common Submit dispatches other phases.
    private PredictorInputResult? TrySubmitEvent(PredictorInput input, CancellationToken token)
    {
        if (Phase == PredictorPhase.EventChoice)
        {
            var crystalResult = TrySubmitCrystal(input, token);
            if (crystalResult != null) return crystalResult;
            if (_conveyorEntryHealthPending)
            {
                if (input is ProvideConveyorInputs conveyor && _preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
                    conveyor.Fact is { EmptyPotionSlots: >= 0, LostHp: >= 0 })
                {
                    _conveyorInput = conveyor.Fact;
                    _conveyorLostHp = conveyor.Fact.LostHp;
                    _eventPotionInput = new(conveyor.Fact.EmptyPotionSlots);
                    _conveyorEntryHealthPending = false;
                    Accept(new("ConveyorLocalInputs", 0, [], null, null));
                    RollConveyorDish();
                    EventOptions = ConveyorOptions();
                    return PredictorInputResult.Accepted;
                }
                if (input is not ProvideEventHealthFact entryHealth || entryHealth.Fact is null ||
                    entryHealth.Fact.CurrentHp < 1 || entryHealth.Fact.MaxHp < entryHealth.Fact.CurrentHp)
                    return PredictorInputResult.Invalid;
                _eventHealth = entryHealth.Fact;
                _conveyorEntryHealthPending = false;
                Accept(new("EventHealthFact", 0, [], null, null) { EventHealthFact = entryHealth.Fact });
                RollConveyorDish();
                EventOptions = ConveyorOptions();
                return PredictorInputResult.Accepted;
            }
            if (_trialAbandonPending)
            {
                if (input is not ConfirmTrialAbandon decision) return PredictorInputResult.Invalid;
                _trialAbandonPending = false;
                Accept(new(decision.Confirm ? "TrialAbandonConfirmed" : "TrialAbandonCanceled", 0, [], null, null));
                if (decision.Confirm)
                {
                    Working = Working with { Outcome = PredictorRunOutcome.Abandoned };
                    FinishEvent();
                }
                else
                {
                    EventOptions = _trialAbandonOptions;
                    _trialAbandonOptions = [];
                }
                return PredictorInputResult.Accepted;
            }
            if (_pendingHealthOption is string pending)
            {
                if (input is not ProvideEventHealthFact provided || provided.Fact is null ||
                    provided.Fact.CurrentHp < 1 || provided.Fact.MaxHp < provided.Fact.CurrentHp)
                    return PredictorInputResult.Invalid;
                _eventHealth = provided.Fact;
                _pendingHealthOption = null;
                EventOptions = _pendingHealthOptions;
                _pendingHealthOptions = [];
                Accept(new("EventHealthFact", 0, [], null, null) { EventHealthFact = provided.Fact });
                return TrySubmitEvent(new ChooseEventOption(pending), token);
            }
            if (input is not ChooseEventOption choice) return PredictorInputResult.Invalid;
            var option = EventOptions.FirstOrDefault(o => o.Key == choice.OptionKey);
            if (option == null || !option.Enabled)
                return PredictorInputResult.Invalid;
            if (_eventHealth == null && !_preview.Event.ProjectHealthIndependently && ((_eventKey!.Value.Entry, choice.OptionKey) is ("MORPHIC_GROVE", "LONER") or
                ("WHISPERING_HOLLOW", "HUG") or ("TRASH_HEAP", "DIVE_IN") or
                ("TRIAL", "NOBLE_GUILTY") or ("THIS_OR_THAT", "PLAIN") or
                ("UNREST_SITE", "REST") or ("UNREST_SITE", "KILL") or
                ("SAPPHIRE_SEED", "EAT") or ("SLIPPERY_BRIDGE", "HOLD_ON") or
                ("STONE_OF_ALL_TIME", "LIFT") or ("STONE_OF_ALL_TIME", "PUSH") or
                ("SUNKEN_STATUE", "DIVE_INTO_WATER") or ("ROUND_TEA_PARTY", "ENJOY_TEA") or
                ("ROUND_TEA_PARTY", "CONTINUE_FIGHT") or ("ABYSSAL_BATHS", "IMMERSE") or
                ("ABYSSAL_BATHS", "LINGER") or ("ABYSSAL_BATHS", "ABSTAIN") or
                ("DROWNING_BEACON", "CLIMB") or ("TABLET_OF_TRUTH", "DECIPHER") or
                ("TABLET_OF_TRUTH", "SMASH") or ("COLOSSAL_FLOWER", "REACH_DEEPER") or
                ("COLOSSAL_FLOWER", "POLLINOUS_CORE") or
                ("DENSE_VEGETATION", "TRUDGE_ON") or ("DENSE_VEGETATION", "REST") or
                ("SPIRALING_WHIRLPOOL", "DRINK") or ("ROOM_FULL_OF_CHEESE", "SEARCH") or
                ("WATERLOGGED_SCRIPTORIUM", "BLOODY_INK") or
                ("THE_LEGENDS_WERE_TRUE", "SLOWLY_FIND_AN_EXIT") or ("BRAIN_LEECH", "RIP") or
                ("BYRDONIS_NEST", "EAT") or
                ("DOLL_ROOM", "TAKE_SOME_TIME") or ("DOLL_ROOM", "EXAMINE") or
                ("SPIRIT_GRAFTER", "LET_IT_IN") or ("SPIRIT_GRAFTER", "REJECTION") or
                ("JUNGLE_MAZE_ADVENTURE", "SOLO_QUEST")))
            {
                _pendingHealthOption = choice.OptionKey;
                _pendingHealthOptions = EventOptions;
                EventOptions = [];
                Accept(new("EventOptionNeedsHealth", 0, [], null, null));
                return PredictorInputResult.Accepted;
            }
            _eventBranch = choice.OptionKey;
            Accept(new("EventOption:" + choice.OptionKey, 0, [], null, null));
            return _eventKey!.Value.Entry switch
            {
                "AROMA_OF_CHAOS" or "SYMBIOTE" or "MORPHIC_GROVE" or
                "WHISPERING_HOLLOW" or "TRASH_HEAP" or "TINKER_TIME" or
                "TRIAL" or "COLORFUL_PHILOSOPHERS" =>
                    SubmitEventTransformsOption(choice, option, token),
                "POTION_COURIER" or "THE_FUTURE_OF_POTIONS" or "STONE_OF_ALL_TIME" or
                "RANWID_THE_ELDER" =>
                    SubmitEventPotionsOption(choice, option, token),
                "SLIPPERY_BRIDGE" or "ABYSSAL_BATHS" or "TABLET_OF_TRUTH" or
                "COLOSSAL_FLOWER" or "SUNKEN_TREASURY" or "JUNGLE_MAZE_ADVENTURE" =>
                    SubmitEventProgressOption(choice, option, token),
                "TEA_MASTER" or "SUNKEN_STATUE" or "ROUND_TEA_PARTY" or
                "DROWNING_BEACON" or "LOST_WISP" or "BUGSLAYER" or
                "RELIC_TRADER" or "FAKE_MERCHANT" or "HUNGRY_FOR_MUSHROOMS" or
                "BYRDONIS_NEST" or "DOLL_ROOM" or "WELCOME_TO_WONGOS" or
                "WAR_HISTORIAN_REPY" =>
                    SubmitEventInventoryOption(choice, option, token),
                "DENSE_VEGETATION" or "PUNCH_OFF" or "BATTLEWORN_DUMMY" or
                "THE_LANTERN_KEY" =>
                    SubmitEventCombatChoicesOption(choice, option, token),
                "ENDLESS_CONVEYOR" =>
                    SubmitEventConveyorOption(choice, option, token),
                "THIS_OR_THAT" or "UNREST_SITE" or "SAPPHIRE_SEED" or
                "ZEN_WEAVER" or "REFLECTIONS" or "WELLSPRING" or
                "FIELD_OF_MAN_SIZED_HOLES" or "LUMINOUS_CHOIR" or "AMALGAMATOR" or
                "WOOD_CARVINGS" or "SELF_HELP_BOOK" or "SPIRALING_WHIRLPOOL" or
                "DOORS_OF_LIGHT_AND_DARK" or "GRAVE_OF_THE_FORGOTTEN" or "ROOM_FULL_OF_CHEESE" or
                "WATERLOGGED_SCRIPTORIUM" or "THE_LEGENDS_WERE_TRUE" or "BRAIN_LEECH" or
                "INFESTED_AUTOMATON" or "SPIRIT_GRAFTER" =>
                    SubmitEventCardsOption(choice, option, token),
                "CRYSTAL_SPHERE" =>
                    SubmitEventCrystalOption(choice, option, token),
                _ => throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey)
            };
        }
        return TrySubmitEventCardSelection(input, token);
    }

    private void SpendEventGold(int amount)
    {
        int before = Working.GoldBudget;
        Working = Working with { GoldBudget = Math.Max(0, before - amount) };
        Accept(new("EventGoldSpent", 0, [], before, Working.GoldBudget));
    }

    private void RemoveEventCards(ImmutableArray<long> ids)
    {
        foreach (long id in ids) ApplySpoilsMapBeforeRemoved(id);
        RemoveCards(ids);
    }

    private void OfferEventRewardPlans(IEnumerable<PredictorRewardPlan> plans, CancellationToken token)
    {
        var generated = PredictorRewardGeneration.Generate(Context, Working, plans, cancellationToken: token);
        _eventRewardContinuation = true;
        var parentInputs = _inputs;
        Phase = PredictorPhase.Complete;
        PrepareRewardNode(Working.Position, generated.Rewards.Select(r => r with { Origin = PredictorRewardOrigin.Event }).ToImmutableArray(), generated.State, token);
        _inputs = parentInputs;
        EventOptions = [];
        GenerationStages = generated.Stages;
    }

    private void BeginEventFrontRelic(long sourceId, CancellationToken token)
    {
        var state = Working;
        var key = PredictorRewardGeneration.PullRelic(Context, ref state);
        Working = state;
        Accept(new("EventRelicPull", sourceId, [], null, null));
        if (BeginObtainRelic(key, null, PredictorObtainReturn.Event, sourceId, token))
            ResumeEventAfterRelicObtain(sourceId);
    }

    internal void ResumeEventAfterRelicObtain(long sourceId)
    {
        if (_eventKey == null) throw new InvalidOperationException("PredictorEventObtainParentMissing");
        if (_eventKey.Value.Entry == "TRASH_HEAP" && _eventBranch == "DIVE_IN" && sourceId == 1)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "TRIAL" && _eventBranch == "MERCHANT_GUILTY")
        {
            if (sourceId == 2) { BeginTrialMerchantRelic(2, CancellationToken.None); return; }
            if (sourceId == 3) { FinishEvent(); return; }
        }
        if (_eventKey.Value.Entry == "THIS_OR_THAT" && _eventBranch == "ORNATE" && sourceId == 4)
        {
            AddTrialCurse("CLUMSY");
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "UNREST_SITE" && _eventBranch == "KILL" && sourceId == 5)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "TEA_MASTER" && sourceId is 6 or 7 or 8)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "SUNKEN_STATUE" && sourceId == 9)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "ROUND_TEA_PARTY")
        {
            if (sourceId == 11) ApplyEventFullHeal();
            if (sourceId is 10 or 11) { FinishEvent(); return; }
        }
        if (_eventKey.Value.Entry == "DROWNING_BEACON" && sourceId == 12)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "LUMINOUS_CHOIR" && sourceId == 13 ||
            _eventKey.Value.Entry == "LOST_WISP" && sourceId == 14)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "COLOSSAL_FLOWER" && sourceId == 15)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "RANWID_THE_ELDER")
        {
            if (sourceId is 16 or 17 or 19) { FinishEvent(); return; }
            if (sourceId == 18) { BeginEventFrontRelic(19, CancellationToken.None); return; }
        }
        if (_eventKey.Value.Entry == "RELIC_TRADER" && sourceId == 20)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "FAKE_MERCHANT" && sourceId == 22)
        {
            int purchasedSlot = int.Parse(_eventBranch![4..]);
            Working = PredictorSettlementEffects.AfterItemPurchased(Working, _fakeStock[purchasedSlot].Price);
            Accept(new("FakeMerchantAfterItemPurchased", 0, [], null, null));
            EventOptions = FakeMerchantOptions();
            Phase = PredictorPhase.EventChoice;
            _requestId++;
            return;
        }
        if (_eventKey.Value.Entry == "GRAVE_OF_THE_FORGOTTEN" && sourceId == 23)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "ROOM_FULL_OF_CHEESE" && sourceId == 24)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "HUNGRY_FOR_MUSHROOMS" && sourceId is (25 or 26))
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "DOLL_ROOM" && sourceId == 27)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "WELCOME_TO_WONGOS" && sourceId is (28 or 29 or 30))
        {
            Working = Working with { WongoPoints = _wongoPointsEarned };
            Accept(new("WongoPointsEarned", _wongoPointsEarned, [], null, null));
            if (Context.AccountWongoPoints!.Value % 2000 + _wongoPointsEarned >= 2000)
            {
                if (BeginObtainRelic(new("RELIC", "WONGO_CUSTOMER_APPRECIATION_BADGE"), null,
                    PredictorObtainReturn.Event, 31, CancellationToken.None)) ResumeEventAfterRelicObtain(31);
            }
            else FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "WELCOME_TO_WONGOS" && sourceId == 31)
        {
            FinishEvent();
            return;
        }
        if (_eventKey.Value.Entry == "WAR_HISTORIAN_REPY" && sourceId is (32 or 33))
        {
            if (_repySecondPending && sourceId == 32)
            {
                EventOptions = [new("SECOND_UNLOCK_CHEST")];
                Phase = PredictorPhase.EventChoice;
                _requestId++;
            }
            else FinishEvent();
            return;
        }
        throw new InvalidOperationException("PredictorEventObtainContinuationInvalid");
    }

    internal void ResumeEventAfterMimicRestHeal(long sourceId)
    {
        if (_eventKey?.Entry != "DENSE_VEGETATION" || sourceId != 21)
            throw new InvalidOperationException("PredictorEventMimicRestHealContinuationInvalid");
        EventOptions = [new("FIGHT")];
        Phase = PredictorPhase.EventChoice;
        _requestId++;
    }

    internal void ResumeEventAfterCombat(bool resume, bool? ranOutOfTime)
    {
        if (_eventKey?.Entry is ("DENSE_VEGETATION" or "PUNCH_OFF" or "THE_LANTERN_KEY" or "FAKE_MERCHANT") && !resume)
        {
            FinishEvent();
            return;
        }
        if (_eventKey?.Entry == "BATTLEWORN_DUMMY" && resume && ranOutOfTime.HasValue)
        {
            Accept(new(ranOutOfTime.Value ? "BattlewornDummyTimeout" : "BattlewornDummyVictory", 0, [], null, null));
            if (ranOutOfTime.Value)
            {
                _eventDeferredCombatExtras = [];
                FinishEvent();
                return;
            }
            var plans = new List<PredictorRewardPlan>();
            if (_eventBranch == "SETTING_1")
            {
                var potions = Context.PotionPool;
                if (!potions.IsEmpty)
                {
                    var draw = Working.Draw(PredictorStream.Rewards, 0, potions.Length);
                    Working = draw.State;
                    plans.Add(new(PredictorRewardKind.Potion,
                        PrefilledPotionKey: potions[draw.Value].Key));
                }
            }
            else if (_eventBranch == "SETTING_2")
            {
                var cards = Working.Deck.Select(Working.Card).Where(c => c.UpgradeLevel < c.MaxUpgradeLevel)
                    .OrderBy(c => c.Key.Entry, StringComparer.Ordinal).ThenBy(c => c.UpgradeLevel).ToList();
                _eventRng!.UnstableShuffle(cards);
                var ids = cards.Take(2).Select(c => c.Id).ToImmutableArray();
                foreach (long id in ids)
                {
                    var card = Working.Card(id);
                    Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                        card with { UpgradeLevel = card.UpgradeLevel + 1 }));
                }
                Accept(new("BattlewornDummyUpgrade", 0, ids, null, null));
            }
            else if (_eventBranch == "SETTING_3")
            {
                var state = Working;
                var key = PredictorRewardGeneration.PullRelic(Context, ref state);
                Working = state;
                plans.Add(new(PredictorRewardKind.Relic, PrefilledRelicKey: key));
            }
            else throw new InvalidOperationException("PredictorBattlewornDummySettingMissing");
            plans.AddRange(_eventDeferredCombatExtras);
            _eventDeferredCombatExtras = [];
            if (plans.Count > 0) OfferEventRewardPlans(plans, CancellationToken.None);
            else FinishEvent();
            return;
        }
        throw new NotImplementedException("PredictorEventCombatContinuationPending:" + _eventKey);
    }

    private bool TryFinishEventRewards()
    {
        if (!_eventRewardContinuation) return false;
        _eventRewardContinuation = false;
        if (_eventKey?.Entry == "ENDLESS_CONVEYOR" && _conveyorHadGrab)
        {
            // Only actual inventory settlement can consume an empty slot. A
            // skipped bottle or a replacement leaves the local count intact.
            if (_conveyorInput != null && _eventPotionInput?.EmptySlots is { } slots &&
                _conveyorRewardEmptySlotsBefore is { } before)
                _eventPotionInput = new(checked(slots + Working.PotionSlots.Count(potion => potion == null) - before));
            _conveyorRewardEmptySlotsBefore = null;
            AdvanceConveyor();
            return true;
        }
        if (_eventKey?.Entry == "WAR_HISTORIAN_REPY" && _repySecondPending)
        {
            EventOptions = [new("SECOND_UNLOCK_CAGE")];
            Phase = PredictorPhase.EventChoice;
            _requestId++;
            return true;
        }
        FinishEvent();
        return true;
    }

    private void ApplyEventHealth(int hpDelta, int maxHpDelta)
    {
        if (_eventHealth == null && _preview.Event.ProjectHealthIndependently)
        {
            _preview.Event.PendingHealthCommands = _preview.Event.PendingHealthCommands.Add(new(hpDelta, maxHpDelta));
            return;
        }
        if (_eventHealth is not { } health) throw new InvalidOperationException("PredictorEventHealthFactRequired");
        int rawMax = checked(health.MaxHp + maxHpDelta);
        int nextMax = maxHpDelta < 0 ? Math.Max(1, rawMax) : rawMax;
        // LoseMaxHp first deals the raw current-minus-new-max damage, through
        // persistent ModifyHpLost hooks, and only then clamps SetMaxHp to 1.
        int rawDamage = maxHpDelta < 0 ? Math.Max(0, checked(health.CurrentHp - rawMax)) : Math.Max(0, -hpDelta);
        int actualDamage = PredictorHealthEffects.ModifyOutOfCombatHpLoss(Working, rawDamage);
        int nextHp = maxHpDelta < 0 ? health.CurrentHp - actualDamage
            : Math.Min(nextMax, checked(health.CurrentHp + Math.Max(0, hpDelta) - actualDamage));
        long preventedBy = 0;
        if (rawDamage > 0 && nextHp <= 0)
        {
            int fairySlot = EventPotionSlots.FindIndex(p => p?.Key.Entry == "FAIRY_IN_A_BOTTLE");
            if (fairySlot >= 0)
            {
                preventedBy = EventPotionSlots[fairySlot]!.Id;
                SetEventPotionSlot(fairySlot, null);
                nextHp = Math.Max(1, (int)(health.MaxHp * .3m));
            }
            else
            {
                int tailIndex = Working.Relics.FindIndex(r => !r.Melted && r.Key.Entry == "LIZARD_TAIL" && !r.LizardTailUsed);
                if (tailIndex >= 0)
                {
                    var tail = Working.Relics[tailIndex];
                    preventedBy = tail.Id;
                    Working = Working with { Relics = Working.Relics.SetItem(tailIndex,
                        tail with { LizardTailUsed = true }) };
                    nextHp = Math.Max(1, health.MaxHp / 2);
                }
            }
            nextHp = Math.Min(nextHp, nextMax);
        }
        if (maxHpDelta < 0) nextHp = Math.Min(nextHp, nextMax);
        int settledHp = Math.Max(0, nextHp);
        _eventHealth = new(settledHp, nextMax);
        Accept(new("EventHealth", 0, [], null, null)
        { EventHealthConsequences = [new(settledHp - health.CurrentHp, nextMax - health.MaxHp)] });
        if (preventedBy != 0) Accept(new("EventDeathPrevented", preventedBy, [], null, null));
        if (nextHp <= 0) CompleteRunDeath("event:" + _eventKey!.Value.Entry);
    }

    private void ApplyEventFullHeal()
    {
        if (_eventHealth == null && _preview.Event.ProjectHealthIndependently)
            _preview.Event.UnknownHealingAmount = true;
        else ApplyEventHealth(_eventHealth!.MaxHp - _eventHealth.CurrentHp, 0);
    }

    private void ApplyEventKill()
    {
        if (_eventHealth is not { } health) throw new InvalidOperationException("PredictorEventHealthFactRequired");
        int fairySlot = EventPotionSlots.FindIndex(p => p?.Key.Entry == "FAIRY_IN_A_BOTTLE");
        if (fairySlot >= 0)
        {
            long potionId = EventPotionSlots[fairySlot]!.Id;
            SetEventPotionSlot(fairySlot, null);
            int revivedHp = Math.Max(1, (int)(health.MaxHp * .3m));
            _eventHealth = health with { CurrentHp = revivedHp };
            Accept(new("EventKillPrevented", potionId, [], null, null)
            { EventHealthConsequences = [new(revivedHp - health.CurrentHp, 0)] });
            return;
        }
        int tailIndex = Working.Relics.FindIndex(r => !r.Melted && r.Key.Entry == "LIZARD_TAIL" && !r.LizardTailUsed);
        if (tailIndex >= 0)
        {
            var tail = Working.Relics[tailIndex];
            Working = Working with { Relics = Working.Relics.SetItem(tailIndex, tail with { LizardTailUsed = true }) };
            int revivedHp = Math.Max(1, health.MaxHp / 2);
            _eventHealth = health with { CurrentHp = revivedHp };
            Accept(new("EventKillPrevented", tail.Id, [], null, null)
            { EventHealthConsequences = [new(revivedHp - health.CurrentHp, 0)] });
            return;
        }
        Accept(new("EventKill", 0, [], null, null)
        { EventHealthConsequences = [new(-health.CurrentHp, 0)] });
        _eventHealth = health with { CurrentHp = 0 };
        CompleteRunDeath("event:" + _eventKey!.Value.Entry);
    }

    private void FinishEvent()
    {
        if (_localCrystalSimulation)
        {
            Working.Validate();
            Phase = PredictorPhase.Complete;
            _requestId++;
        }
        else CompleteNode();
        ResetEvent();
    }

    private void ResetEvent()
    {
        _preview.EventTrace?.RngBoundary(this, "ResetEvent: retain final event-local RNG before clearing handler state");
        _preview.Event.ProjectHealthIndependently = false;
        ResetEventCombat();
        ResetCrystalSphere();
        _eventKey = null;
        _eventPotionInput = null;
        _eventLocalPotionSlots = default;
        _eventRng = null;
        _eventBranch = null;
        _eventCardsRequired = 0;
        _eventCardCandidates = [];
        _eventRewardContinuation = false;
        _eventHealth = null;
        ResetEventChoiceState(bathsDamage: 0, resetAssignedGold: false);
        EventOptions = [];
    }

    // Entry and exit share this ordered reset; assigned gold is entry-only, and Baths starts at 3.
    private void ResetEventChoiceState(int bathsDamage, bool resetAssignedGold)
    {
        _pendingHealthOption = null;
        _pendingHealthOptions = [];
        _tinkerType = null;
        _eventGoldAmount = 0;
        if (resetAssignedGold) _preview.Event.AssignedGold = null;
        _trialAbandonPending = false;
        _trialAbandonOptions = [];
        _bridgeCard = null;
        _bridgeHoldOns = 0;
        _bridgeSkipped = [];
        _futurePotionTypes = ImmutableDictionary<long, EffectCardType>.Empty;
        _stonePotionId = null;
        _bathsDamage = bathsDamage;
        _tabletCount = 0;
        _flowerDigs = 0;
        _smallTreasuryGold = _largeTreasuryGold = 0;
        _ranwidPotionId = _ranwidRelicId = null;
        _traderOwned = [];
        _traderOffers = [];
        _fakeStock = [];
        _fakeInventoryOpen = false;
        _eventOfferedCards = [];
        _conveyorEntryHealthPending = false;
        _conveyorRewardEmptySlotsBefore = null;
        // Retain the accepted arrival premises/current missing HP for the
        // completed node DTO; a different event clears them on entry.
        if (resetAssignedGold)
        {
            _conveyorInput = null;
            _conveyorLostHp = null;
        }
        _conveyorDish = _conveyorLastDish = null;
        _conveyorGrabs = 0;
        _conveyorHadGrab = false;
        _jungleSoloGold = _jungleJoinGold = 0;
        _wongoFeatured = null;
        _wongoPointsEarned = 0;
        _repySecondPending = false;
    }

}
