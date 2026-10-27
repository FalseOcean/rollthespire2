using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorEncounterDefinition(ModelKey Key, int MinimumGold, int MaximumGold, bool ShouldGiveRewards = true)
{
    public ImmutableArray<PredictorEnemyCreationSource> EnemyCreationSources { get; init; } = [];
    public bool CanStealCards { get; init; }
    public bool IsMonsterEncounter { get; init; }
}
internal sealed record PredictorCombatEntry(PredictorCombatKind Kind, PredictorEncounterDefinition Encounter,
    ImmutableArray<PredictorPotionAttempt> PotionAttempts)
{
    public ImmutableArray<PredictorRewardPlan> ExtraRewards { get; init; } = [];
    public bool FurCoatMarkedEnemies { get; init; }
}

internal sealed partial class PredictorRun
{
    internal ImmutableArray<PredictorRewardStage> GenerationStages { get; private set; } = [];
    internal bool HasRunModifier(string entry) => Context.HasRunModifier(entry);

    internal void EnterOpening()
    {
        RequireRouteSimulation();
        if (Phase != PredictorPhase.Complete || Working.OpeningCompleted || Context.Modifiers.IsEmpty && Working.OpeningOptions.Length != 3)
            throw new InvalidOperationException("PredictorOpeningEntryInvalid");
        Working = Working with { Position = _routeNode ?? new(1, 1, "opening") };
        _inputs = [];
        Rewards = [];
        if (!Context.Modifiers.IsEmpty) { BeginModifierOpening(); return; }
        Phase = PredictorPhase.OpeningChoice;
        _requestId++;
    }

    private PredictorInputResult ChooseOpening(ModelKey key, CancellationToken cancellationToken)
    {
        if (!Working.OpeningOptions.Contains(key)) return PredictorInputResult.Invalid;
        long sourceId = Working.NextInstanceId;
        Accept(new("ChooseOpeningRelic", sourceId, [], null, null));
        if (BeginObtainRelic(key, null, PredictorObtainReturn.Opening, sourceId, cancellationToken))
        {
            Working = Working with { OpeningCompleted = true };
            CompleteNode();
        }
        return PredictorInputResult.Accepted;
    }
    internal void EnterCombatRewards(PredictorCombatKind kind, int act, int floor,
        float goldProportion = 1f, CancellationToken cancellationToken = default, PredictorCombatFacts? facts = null)
    {
        RequireRouteSimulation();
        var boundary = Working;
        var phase = Phase;
        try
        {
            var entry = PrepareCombatEntry(kind, act, floor, cancellationToken);
            SettleCombat(entry, goldProportion, facts ?? new(), cancellationToken);
        }
        catch
        {
            Working = boundary; Phase = phase;
            throw;
        }
    }

    private PredictorCombatEntry PrepareCombatEntry(PredictorCombatKind kind, int act, int floor, CancellationToken cancellationToken,
        ModelKey? encounterOverride = null, bool initialEnemiesAlreadyCreated = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Phase != PredictorPhase.Complete || !Working.OpeningCompleted || !Enum.IsDefined(kind))
            throw new InvalidOperationException("PredictorCombatBoundaryInvalid");
        var catalog = Context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        int actIndex = Working.Acts.FindIndex(a => a.Act == act);
        if (actIndex < 0) throw new ArgumentOutOfRangeException(nameof(act));
        var queue = Working.Acts[actIndex];
        ModelKey encounter = encounterOverride ?? (kind switch
        {
            PredictorCombatKind.Monster => queue.NormalEncounters[queue.NormalCursor % queue.NormalEncounters.Length],
            PredictorCombatKind.Elite => queue.EliteEncounters[queue.EliteCursor % queue.EliteEncounters.Length],
            _ => queue.Bosses[queue.BossCursor]
        });
        var definition = catalog.Encounters.Single(e => e.Key == encounter);
        PredictorSettlementEffects.RequireImplementedInventory(Working, context: Context);
        // Combat-boundary defaults/facts for future-relevant cards are still being
        // implemented. Do not silently skip their execution in this source path.
        if (Working.Visits.LastOrDefault() is { } currentVisit && currentVisit.Act == act && currentVisit.TotalFloor == floor)
            Working = Working with { Visits = Working.Visits.SetItem(Working.Visits.Length - 1,
                currentVisit with { CombatKinds = currentVisit.CombatKinds.Add(kind) }) };
        if (encounterOverride == null) queue = kind switch { PredictorCombatKind.Monster => queue with { NormalCursor = queue.NormalCursor + 1 },
            PredictorCombatKind.Elite => queue with { EliteCursor = queue.EliteCursor + 1 }, _ => queue with { BossCursor = queue.BossCursor + 1 } };
        var state = Working with { Position = new(act, floor, $"combat:{act}:{floor}"), CombatCount = Working.CombatCount + 1, CurrentRoomIsCombat = true,
            Acts = Working.Acts.SetItem(actIndex, queue) };
        if (!initialEnemiesAlreadyCreated)
        {
            int initialEnemies = PredictorEncounterEntry.InitialEnemyCount(encounter,
                RolltheSpire2.Compatibility.Beta111Profile.Instance.ComputeRootSeed(Context.Seed), floor);
            state = PredictorEncounterEntry.ConsumeInitialEnemyHp(state, initialEnemies);
        }
        var entrance = PredictorPotionEffects.BeforeCombat(Context, state, cancellationToken);
        // These two counters are consumed before the unsimulated battle. The
        // powers/cards themselves have no retained out-of-combat reader.
        Working = entrance.State with { Relics = entrance.State.Relics.Select(r => !r.Melted && r.CombatsLeft > 0 &&
            r.Key.Entry is "EMBER_TEA" or "TEA_OF_DISCOURTESY" ? r with { CombatsLeft = r.CombatsLeft - 1 } : r).ToImmutableArray() };
        // The first energy reset consumes the saved Rest-site carry-over; the
        // temporary energy itself has no boundary-state reader.
        Working = Working with { Relics = Working.Relics.Select(r => !r.Melted && r.GainEnergyInNextCombat &&
            r.Key.Entry is "VENERABLE_TEA_SET" or "FAKE_VENERABLE_TEA_SET" ? r with { GainEnergyInNextCombat = false } : r).ToImmutableArray() };
        var visit = Working.Visits.LastOrDefault();
        if (kind == PredictorCombatKind.Boss)
            foreach (var relic in Working.Relics.Where(r => !r.Melted && r.Key.Entry == "PANTOGRAPH"))
                Working = Working with { CurrentHealthEffects = Working.CurrentHealthEffects.Add(new(relic.Key, relic.Id, 0, 25)) };
        return new(kind, definition, entrance.Attempts)
        { FurCoatMarkedEnemies = visit != null && visit.Act == act && IsFurCoatMarkedCombat(act, visit.Point) };
    }

    private void SettleCombat(PredictorCombatEntry entry, float goldProportion, PredictorCombatFacts facts, CancellationToken cancellationToken,
        bool preserveInputs = false)
    {
        var priorInputs = preserveInputs ? _inputs : [];
        if (!float.IsFinite(goldProportion) || goldProportion is < 0f or > 1f) throw new ArgumentException("PredictorCombatGoldProportionInvalid");
        var kind = entry.Kind;
        var definition = entry.Encounter;
        int act = Working.Position.Act;
        // Owner budget premise, not the vanilla per-turn energy/Gold hook.
        // Applicability is frozen before any post-combat reward can grant Seal.
        var seal = Working.Relics.FirstOrDefault(r => !r.Melted && r.Key.Entry == "SEAL_OF_GOLD");
        if (seal != null && Context.SealOfGoldFixedDeduction == null)
            throw new InvalidOperationException("PredictorSealOfGoldBudgetParameterUnassigned");
        if (facts.GremlinGold != null)
        {
            if (!PredictorCombatRewardFacts.ValidGremlin(definition, facts.GremlinGold))
                throw new PredictorCombatPlanBindingException(PredictorCombatPlanBindingReason.GremlinGoldFactsInvalid,
                    "PredictorGremlinGoldFactsInvalid");
            if (facts.GremlinGold.RecoverAllStolenGold && !PredictorCombatRewardFacts.CanBalanceRecoveredGold(Context, Working))
                throw new ArgumentException("PredictorRecoveredGoldAmountRequired");
            goldProportion = PredictorCombatRewardFacts.GoldProportion(facts.GremlinGold);
        }
        var combatState = Working with { Relics = Working.Relics.Select(r => !r.Melted && r.Key.Entry == "LAVA_LAMP"
            ? r with { TookDamageThisCombat = facts.RelicEffects.FirstOrDefault(e => e.RelicId == r.Id) is { } effect
                ? !effect.EffectEnabled : facts.TookDamageThisCombat } : r).ToImmutableArray() };
        combatState = PredictorCombatEnemyFacts.Apply(combatState, facts.EnemyCreations, cancellationToken);
        var consequences = PredictorCombatConsequences.Apply(Context, combatState, kind, facts, cancellationToken);
        var state = consequences.State;
        if (facts.GremlinGold?.StolenGoldLoss is { } loss)
        {
            if (loss > state.GoldBudget)
                throw new PredictorCombatPlanBindingException(PredictorCombatPlanBindingReason.StolenGoldLossExceedsBudget,
                    "PredictorStolenGoldLossExceedsBudget");
            state = state with { GoldBudget = state.GoldBudget - loss };
        }
        var stolenRewards = new List<PredictorRewardPlan>();
        if (facts.StolenCards is { } theft)
        {
            if (theft.Cards.IsDefault || theft.Cards.Any(c => c == null || !state.Deck.Contains(c.CardId)) ||
                theft.Cards.Select(c => c.CardId).Distinct().Count() != theft.Cards.Length)
                throw new PredictorCombatPlanBindingException(PredictorCombatPlanBindingReason.StolenCardFactsInvalid,
                    "PredictorStolenCardFactsInvalid");
            foreach (var stolen in theft.Cards)
            {
                var card = state.Card(stolen.CardId);
                // Swipe removes the master; a killed thief re-registers that
                // same instance for a SpecialCardReward, without adding to Deck.
                if (card.Key.Entry == "SPOILS_MAP" && card.SpoilsActIndex == state.Position.Act - 1)
                    card = card with { SpoilsCoord = null };
                state = state.WithCard(card with { Removed = !stolen.ReturnedAsReward }) with { Deck = state.Deck.Remove(card.Id) };
                if (stolen.ReturnedAsReward) stolenRewards.Add(new(PredictorRewardKind.SpecialCard, PrefilledCardInstanceId: card.Id));
            }
        }
        // Bone Tea's first-turn hand upgrade is combat-only; its remaining-use
        // counter persists after the battle without upgrading the master Deck.
        state = state with { Relics = state.Relics.Select(r => !r.Melted && r.Key.Entry == "BONE_TEA" && r.CombatsLeft > 0 && facts.FirstPlayerTurnOccurred == true
            ? r with { CombatsLeft = r.CombatsLeft - 1 } : r).ToImmutableArray() };
        // Guilty is an automatic master-deck callback, not an assumed play.
        foreach (long id in state.Deck)
        {
            var card = state.Card(id);
            if (card.Key.Entry != "GUILTY") continue;
            card = card with { CombatsSeen = card.CombatsSeen + 1 };
            state = state.WithCard(card);
            if (card.CombatsSeen >= 5) state = state.WithCard(card with { Removed = true }) with { Deck = state.Deck.Remove(id) };
        }
        // Hook traversal freezes the initially active listeners. Melting does
        // not remove the relic instance; it changes eligibility of later phases.
        foreach (long relicId in state.Relics.Where(r => !r.Melted).Select(r => r.Id).ToArray())
        {
            int i = state.Relics.FindIndex(r => r.Id == relicId);
            if (i < 0) continue;
            var relic = state.Relics[i];
            switch (relic.Key.Entry)
            {
                case "CHOSEN_CHEESE":
                    state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(new(relic.Key, relic.Id, 1, 1)) };
                    break;
                case "FISHING_ROD" when definition.IsMonsterEncounter:
                    state = PredictorCombatRelicUpgrade.FishingRodMonster(Context, state, relic.Id).State;
                    break;
                case "WONGOS_MYSTERY_TICKET":
                    state = state with { Relics = state.Relics.SetItem(i, relic with { CombatsFinished = relic.CombatsFinished + 1 }) };
                    break;
                case "PUMPKIN_CANDLE":
                    state = state with { Relics = state.Relics.SetItem(i, relic with { KindleCount = Math.Max(0, relic.KindleCount - 1) }) };
                    break;
                case "TOY_BOX" when relic.ToyBoxCombatsSeen < 15:
                    int count = relic.ToyBoxCombatsSeen + 1;
                    state = state with { Relics = state.Relics.SetItem(i, relic with { ToyBoxCombatsSeen = count }) };
                    if (count % 3 == 0)
                    {
                        int wax = state.Relics.FindIndex(r => r.IsWax && !r.Melted);
                        if (wax >= 0) state = state with { Relics = state.Relics.SetItem(wax, state.Relics[wax] with { Melted = true }) };
                    }
                    break;
                case "PAELS_TOOTH" when ProjectLivePaelsToothReturn(ref state, relic):
                    break;
                case "PAELS_TOOTH" when !relic.StoredCards.IsEmpty:
                    (int selected, state) = state.Draw(PredictorStream.Rewards, 0, relic.StoredCards.Length);
                    var returned = relic.StoredCards[selected];
                    if (returned.UpgradeLevel < returned.MaxUpgradeLevel) returned = returned with { UpgradeLevel = returned.UpgradeLevel + 1 };
                    state = PredictorObtainSources.AddCard(Context, state, returned);
                    state = state with { Relics = state.Relics.SetItem(i, relic with { StoredCards = relic.StoredCards.RemoveAt(selected) }) };
                    break;
            }
        }
        // CombatManager.EndCombatInternal completes AfterCombatEnd (including
        // Pael's returned card and ToyBox melting) before player cleanup and
        // the separate VictoryEarly / Victory traversals. Re-read active relics.
        ObserveOperationVictoryHealth(facts.VictoryHealth, state.CurrentHealthEffects.Length);
        if (state.Relics.Any(r => !r.Melted && r.Key.Entry == "MEAT_ON_THE_BONE"))
        {
            var health = facts.VictoryHealth;
            if (health == null || health.MaxHp < 1 || health.CurrentHp < 1 || health.CurrentHp > health.MaxHp)
                throw new ArgumentException("PredictorVictoryHealthFactRequired");
            int hp = health.CurrentHp;
            foreach (var relic in state.Relics.Where(r => !r.Melted && r.Key.Entry == "MEAT_ON_THE_BONE"))
                if (hp <= health.MaxHp / 2)
                {
                    state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(new(relic.Key, relic.Id, 0, 12)) };
                    hp = Math.Min(health.MaxHp, checked(hp + 12));
                }
        }
        foreach (var relic in state.Relics.Where(r => !r.Melted).ToArray())
            {
                if (relic.Key.Entry is "BURNING_BLOOD" or "BLACK_BLOOD")
                    state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(new(relic.Key, relic.Id, 0, relic.Key.Entry == "BLACK_BLOOD" ? 12 : 6)) };
                if (kind != PredictorCombatKind.Elite) continue;
                if (relic.Key.Entry == "WAR_HAMMER") state = PredictorCombatRelicUpgrade.WarHammerVictory(Context, state).State;
                if (relic.Key.Entry != "SWORD_OF_STONE") continue;
                int index = state.Relics.FindIndex(r => r.Id == relic.Id);
                int defeated = checked(relic.SwordElitesDefeated + 1);
                if (defeated < 5) state = state with { Relics = state.Relics.SetItem(index, relic with { SwordElitesDefeated = defeated }) };
                else
                {
                    // RelicCmd.Replace removes then obtains a fresh instance at
                    // the original inventory position. Jade has no obtain or
                    // open-card-reward modifier; its entrance power is combat-only.
                    state = state with { Relics = state.Relics.RemoveAt(index) };
                    state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", "SWORD_OF_JADE"));
                    var jade = state.Relics[^1];
                    state = state with { Relics = state.Relics.RemoveAt(state.Relics.Length - 1).Insert(index, jade) };
                }
            }
        int? sealBudgetBefore = seal == null ? null : state.GoldBudget;
        if (seal != null)
            state = state with { GoldBudget = Math.Max(0, state.GoldBudget - Context.SealOfGoldFixedDeduction!.Value) };
        var plans = new List<PredictorRewardPlan>();
        var tutorial = entry.Encounter.ShouldGiveRewards ? TryTutorialRewardPlans(kind, definition) : null;
        if (tutorial is { } fixedPlans) plans.AddRange(fixedPlans);
        else if (entry.Encounter.ShouldGiveRewards && !(kind == PredictorCombatKind.Boss && act == 3))
        {
            if (kind != PredictorCombatKind.Monster || goldProportion > 0)
                plans.Add(new(PredictorRewardKind.Gold, (int)Math.Round(definition.MinimumGold * (kind == PredictorCombatKind.Monster ? goldProportion : 1f)),
                    (int)Math.Round(definition.MaximumGold * (kind == PredictorCombatKind.Monster ? goldProportion : 1f))));
            bool potion = PredictorSettlementEffects.Has(state, "WHITE_BEAST_STATUE");
            if (!potion)
            {
                var rng = state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
                potion = rng.NextFloat() < state.Odds.PotionDropChance + (kind == PredictorCombatKind.Elite ? .125f : 0);
                state = state.WithRng(PredictorStream.Rewards, rng) with { Odds = state.Odds with
                { PotionDropChance = state.Odds.PotionDropChance + (potion ? -.1f : .1f) } };
            }
            if (potion) plans.Add(new(PredictorRewardKind.Potion));
            plans.Add(new(PredictorRewardKind.Card, Odds: kind switch { PredictorCombatKind.Elite => PredictorCardOdds.Elite,
                PredictorCombatKind.Boss => PredictorCardOdds.Boss, _ => PredictorCardOdds.Regular }, EncounterSource: true, IsFromCombat: true));
            if (kind == PredictorCombatKind.Elite) plans.Add(new(PredictorRewardKind.Relic));
        }
        if (!(kind == PredictorCombatKind.Boss && act == 3))
        {
            var extras = entry.ExtraRewards.AddRange(PredictorCombatRewardFacts.Order(
                consequences.Extras, stolenRewards, facts.GremlinGold, facts.ExtraRewardOrder));
            if (!entry.Encounter.ShouldGiveRewards && _eventCombatEntry != null && _eventCombatResume)
                _eventDeferredCombatExtras = extras;
            else plans.AddRange(extras);
        }
        var generated = entry.Encounter.ShouldGiveRewards
            ? PredictorRewardGeneration.Generate(Context, state, plans, kind, cancellationToken)
            : new PredictorGeneratedRewards(state, [], []);
        cancellationToken.ThrowIfCancellationRequested();
        PrepareRewardNode(state.Position, generated.Rewards.Select(r => r with { Origin = PredictorRewardOrigin.Combat }).ToImmutableArray(), generated.State, cancellationToken);
        GenerationStages = generated.Stages;
        _inputs = priorInputs.AddRange(_inputs);
        if (seal != null)
            Accept(new("SealOfGoldBudgetDeduction", seal.Id, [], sealBudgetBefore, state.GoldBudget));
        Accept(new("CombatBoundary", 0, [], null, null) { CombatCardTriggers = consequences.Triggers,
            CombatTemporaryCardTriggers = facts.TemporaryCardTriggers, CombatCardSourceOrder = facts.CombatCardSourceOrder,
            GeneratedPotions = consequences.Potions, HealthConsequences = consequences.Health, PotionAttempts = entry.PotionAttempts.AddRange(consequences.PotionAttempts),
            CombatPotionFacts = facts.Potions, CombatRelicEffects = facts.RelicEffects,
            CombatEnemyCreations = facts.EnemyCreations,
            CombatStolenCards = facts.StolenCards,
            CombatGremlinGold = facts.GremlinGold, CombatExtraRewardOrder = facts.ExtraRewardOrder,
            FirstPlayerTurnOccurred = facts.FirstPlayerTurnOccurred,
            VictoryHealthFact = facts.VictoryHealth,
            FurCoatMarkedEnemies = entry.FurCoatMarkedEnemies,
            CombatTookDamage = facts.TookDamageThisCombat });
    }

    private static PredictorState AddGeneratedCard(PredictorState state, PredictorCard card)
    {
        card = card with { Id = state.NextInstanceId, FloorAdded = state.Position.Floor };
        foreach (var relic in state.Relics.Where(r => !r.Melted)) card = PredictorRewardGeneration.UpgradeOnDeckAddWithEgg(card, relic.Key.Entry);
        return state with { NextInstanceId = state.NextInstanceId + 1, Cards = state.Cards.Add(card), Deck = state.Deck.Add(card.Id) };
    }
}
