using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // Missing values in this private witness are used only behind masks. It
    // cannot be exported or persisted through the observation API.
    private sealed record PredictorCombatProjection(PredictorRun Witness, PredictorCombatPreview Knowledge);

    internal PredictorCombatPreview? PreviewCurrentCombat(PredictorCombatNodeOverrides? node = null,
        CancellationToken cancellationToken = default)
        => ProjectCurrentCombat(node, cancellationToken)?.Knowledge;

    // A local display query supplies its own declared premise. It never
    // resolves route defaults or exports scratch health/budget witnesses.
    // Resuming event-specific victory extras remains the operation workflow's
    // responsibility; this projection covers non-resuming battle rewards.
    internal PredictorCombatPreview PreviewOperationCombatRewards(PredictorCombatFacts facts,
        CancellationToken cancellationToken = default)
    {
        if (Context.Operation == null || Phase != PredictorPhase.CombatFacts || _eventCombatEntry == null || _eventCombatResume)
            throw new InvalidOperationException("PredictorOperationCombatProjectionBoundaryInvalid");
        var required = CombatRequirements;
        var missing = required with
        {
            EnemyCreations = required.EnemyCreations.Where(s => !facts.EnemyCreations.Any(f => f.Source == s)).ToImmutableArray(),
            StolenCards = required.StolenCards && facts.StolenCards == null,
            GremlinGold = required.GremlinGold && facts.GremlinGold == null,
            FirstPlayerTurn = required.FirstPlayerTurn && facts.FirstPlayerTurnOccurred == null,
            VictoryHealth = required.VictoryHealth && facts.VictoryHealth == null,
            EventTimeout = false
        };
        return ProjectCurrentCombat(null, cancellationToken, new(facts, missing))!.Knowledge;
    }

    private PredictorCombatProjection? ProjectCurrentCombat(PredictorCombatNodeOverrides? node = null,
        CancellationToken cancellationToken = default, PredictorCombatDefaultResolution? supplied = null)
    {
        var resolution = supplied ?? ResolveCombatDefaults(node);
        var entry = _eventCombatEntry ?? _combatEntry;
        if (resolution == null || entry == null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        if (_preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults)
        {
            long operations = resolution.Facts.CardTriggers.Sum(trigger => (long)trigger.EffectiveTriggers) +
                resolution.Facts.TemporaryCardTriggers.Sum(trigger => (long)trigger.EffectiveTriggers) +
                resolution.Facts.EnemyCreations.Sum(creation => (long)creation.Count);
            if (operations > PredictorDrawingWorkLimitException.MaximumOperations)
                throw new PredictorDrawingWorkLimitException(operations);
        }
        var missing = resolution.MissingFacts;
        var pending = ImmutableArray.CreateBuilder<PredictorCombatPendingCause>();
        bool Has(string relic) => Working.Relics.Any(r => !r.Melted && r.Key.Entry == relic);
        bool upgradesDeck = entry.Kind == PredictorCombatKind.Elite && Has("WAR_HAMMER") ||
            entry.Encounter.IsMonsterEncounter && Working.Relics.Any(r => !r.Melted &&
                r.Key.Entry == "FISHING_ROD" && (r.FishingRodCombatsSeen + 1) % 3 == 0);
        var nicheDependents = PredictorCombatDomain.NicheRng;
        if (upgradesDeck) nicheDependents |= PredictorCombatDomain.Deck;
        // Wing Charm reads Niche only after card identities have been generated.
        // Its actual source-level eligibility scope below owns whether any
        // enchantment target is uncertain; possession is not a card dependency.
        foreach (var source in missing.EnemyCreations)
            pending.Add(new("EnemyCreationCount", $"{source}: creature/hatch count is unknown",
                nicheDependents, source));
        if (missing.StolenCards)
        {
            var domains = PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds |
                PredictorCombatDomain.ReturnedCards;
            // These callbacks read the post-theft deck, including its size.
            if (upgradesDeck) domains |= nicheDependents;
            pending.Add(new("StolenCardIdentity", "Stolen card identities and returned-card count are unknown", domains));
        }
        var gremlinGold = resolution.Facts.GremlinGold;
        bool unknownLossOnly = missing.GremlinGold && gremlinGold?.GoldWasStolen == true &&
            !PredictorCombatRewardFacts.HasHeistReward(gremlinGold) && gremlinGold.StolenGoldLoss == null;
        if (unknownLossOnly)
            pending.Add(new("StolenGoldLoss", "Gold was stolen without recovery; the lost amount is unknown", PredictorCombatDomain.Gold));
        else if (missing.GremlinGold)
            pending.Add(new("RecoveredGoldAmount", "Gold is stolen and recovered; the recovered amount is unknown",
                PredictorCombatDomain.Gold | PredictorCombatDomain.GoldRewards |
                    (Has("DRAGON_FRUIT") ? PredictorCombatDomain.Health : PredictorCombatDomain.None)));
        if (missing.VictoryHealth)
            pending.Add(new("VictoryHealth", "Health before conditional victory healing is unknown", PredictorCombatDomain.Health));
        if (missing.FirstPlayerTurn)
            pending.Add(new("FirstPlayerTurn", "Whether the first player turn occurred is unknown", PredictorCombatDomain.Relics));
        if (missing.EventTimeout)
            pending.Add(new("EventTimeout", "Event combat timeout is unknown", PredictorCombatDomain.Continuation));
        bool missingSeal = Has("SEAL_OF_GOLD") && Context.SealOfGoldFixedDeduction == null;
        if (missingSeal)
            pending.Add(new("SealOfGoldDeduction", "Seal of Gold deduction has no assigned amount", PredictorCombatDomain.Gold));

        var combatTrace = _preview.Partial?.BeginCombatTrace(this, entry, resolution, node,
            pending.Select(c => PredictorRouteCauseEvidence.Bind(c, Working.Position.NodeId)).ToImmutableArray());

        // Values below are witnesses for executing the existing bounded source
        // pipeline, NOT source facts. They never escape or enter route history.
        // Missing creature counts use zero draws; their dependent cursor/results
        // are masked. A known-existing Heist uses one fixed-range draw even while
        // its amount is unknown (NextInt(1) still advances Rewards exactly once).
        // Keep explicit escape/theft and reward existence observations. Only a
        // blocked net-recovery disposition needs a numeric scratch witness;
        // unknown unrecovered loss is omitted, never turned into a new Heist.
        if (missing.GremlinGold && gremlinGold?.RecoverAllStolenGold == true)
            gremlinGold = gremlinGold with { RecoverAllStolenGold = false, RecoveredGold = 1 };
        var facts = resolution.Facts with
        {
            EnemyCreations = resolution.Facts.EnemyCreations.AddRange(missing.EnemyCreations.Select(s => new PredictorEnemyCreationFact(s, 0))),
            StolenCards = missing.StolenCards ? new([]) : resolution.Facts.StolenCards,
            GremlinGold = gremlinGold,
            VictoryHealth = missing.VictoryHealth ? new(1, 1) : resolution.Facts.VictoryHealth,
            FirstPlayerTurnOccurred = missing.FirstPlayerTurn ? false : resolution.Facts.FirstPlayerTurnOccurred
        };
        var consequences = PredictorCombatConsequences.Apply(Context, Working, entry.Kind, facts, cancellationToken);
        int huntCount = consequences.Extras.Count(p => p.Kind == PredictorRewardKind.Card);
        int returnedCount = facts.StolenCards?.Cards.Count(c => c.ReturnedAsReward) ?? 0;
        var availableOrder = Enumerable.Range(0, huntCount).Select(i => new PredictorCombatExtraRewardRef(PredictorCombatExtraRewardSource.Hunt, i))
            .Concat(Enumerable.Range(0, returnedCount).Select(i => new PredictorCombatExtraRewardRef(PredictorCombatExtraRewardSource.StolenCard, i)))
            .Concat(PredictorCombatRewardFacts.HasHeistReward(facts.GremlinGold) ? [new(PredictorCombatExtraRewardSource.Heist, 0)] : Array.Empty<PredictorCombatExtraRewardRef>())
            .ToImmutableArray();
        bool mixedExtras = availableOrder.Select(r => r.Source).Distinct().Count() > 1;
        bool uncertainExtraOrder = mixedExtras && facts.ExtraRewardOrder.IsEmpty;
        if (uncertainExtraOrder)
            pending.Add(new("ExtraRewardOrder", "Order of combat extra rewards has not been declared",
                PredictorCombatDomain.Continuation | (huntCount > 0 && PredictorCombatRewardFacts.HasHeistReward(facts.GremlinGold)
                    ? PredictorCombatDomain.CardRewards | PredictorCombatDomain.Odds |
                        (Has("WING_CHARM") ? PredictorCombatDomain.NicheRng : PredictorCombatDomain.None)
                    : PredictorCombatDomain.None)));
        // SpecialCard.Populate has no RNG or reward listener. Removing unknown
        // stolen-card references from the isolated ordering cannot change the
        // other rewards; their IDs are stripped from every public payload.
        if (missing.StolenCards && !facts.ExtraRewardOrder.IsEmpty)
            facts = facts with { ExtraRewardOrder = facts.ExtraRewardOrder.Where(r => r.Source != PredictorCombatExtraRewardSource.StolenCard).ToImmutableArray() };
        else if (facts.ExtraRewardOrder.IsEmpty && mixedExtras)
            facts = facts with { ExtraRewardOrder = availableOrder };

        var scratchContext = missingSeal ? Context with { SealOfGoldFixedDeduction = 0 } : Context;
        var scratch = CreateCombatSettlement(scratchContext, entry, facts, cancellationToken);
        pending.AddRange(scratch._preview.PaelsToothCombatCauses);
        ImmutableArray<PredictorCombatPendingCause> OddsCauses(string property) => _preview.Partial?.CombatOddsCauses(property) ??
            (Context.Live?.PendingCauses ?? []).Where(cause => CombatOddsCauseApplies(cause, property))
                .Select(cause => PredictorRouteCauseEvidence.Bind(cause)).ToImmutableArray();
        bool rolledPotion = entry.Encounter.ShouldGiveRewards && !(entry.Kind == PredictorCombatKind.Boss && Working.Position.Act == 3) &&
            TryTutorialRewardPlans(entry.Kind, entry.Encounter) == null &&
            !scratch.Working.Relics.Any(r => !r.Melted && r.Key.Entry == "WHITE_BEAST_STATUE");
        var dropCauses = rolledPotion ? OddsCauses("odds.potionDrop") : [];
        var rarityCauses = OddsCauses("odds.cardRarity");
        if (!dropCauses.IsEmpty)
        {
            pending.AddRange(dropCauses);
            pending.Add(PredictorRouteCauseEvidence.CreateExplicit("CombatPotionDropConsumptionPending", Working.Position.NodeId,
                PredictorCombatDomain.RewardsRng | PredictorCombatDomain.CardRarityOdds, PredictorPendingReasonKind.DerivedDependency,
                "Potion existence changes the two Rewards draws from Potion.Populate before Card/Relic population; current original Gold precedes it.",
                ["rng.Rewards.calls", "rng.Rewards.state", "rng.Rewards.consumed", "odds.cardRarity"], dropCauses));
        }
        if (scratch.Rewards.Any(reward => CombatCardReadsRarityOffset(Context, reward))) pending.AddRange(rarityCauses);
        var causes = pending.Select(c => PredictorRouteCauseEvidence.Bind(c, Working.Position.NodeId)).ToImmutableArray();
        ImmutableArray<PredictorCombatPendingCause> For(PredictorCombatDomain domain) =>
            causes.Where(c => (c.AffectedDomains & domain) != 0).ToImmutableArray();
        var rewards = ImmutableArray.CreateBuilder<PredictorCombatRewardKnowledge>();
        for (int rewardIndex = 0; rewardIndex < scratch.Rewards.Length; rewardIndex++)
        {
            var reward = scratch.Rewards[rewardIndex];
            int occurrence = scratch.Rewards.Take(rewardIndex).Count(prior => prior.Kind == reward.Kind &&
                prior.Origin == reward.Origin && prior.Key == reward.Key);
            var relevant = reward.Kind switch
            {
                PredictorRewardKind.Card => For(PredictorCombatDomain.CardRewards)
                    .Where(c => c.Code != "ExtraRewardOrder" || reward.RerollRecipe?.IsFromCombat != true).ToImmutableArray(),
                PredictorRewardKind.SpecialCard => For(PredictorCombatDomain.ReturnedCards),
                PredictorRewardKind.Gold when reward.WasGoldStolenBack => For(PredictorCombatDomain.GoldRewards),
                PredictorRewardKind.Potion => For(PredictorCombatDomain.PotionRewards),
                PredictorRewardKind.Relic => For(PredictorCombatDomain.RelicRewards),
                _ => ImmutableArray<PredictorCombatPendingCause>.Empty
            };
            if (reward.Kind == PredictorRewardKind.Card && CombatCardReadsRarityOffset(Context, reward)) relevant = relevant.AddRange(rarityCauses);
            if (reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.Relic or PredictorRewardKind.Potion) relevant = relevant.AddRange(dropCauses);
            relevant = PredictorRouteCauseEvidence.Normalize(relevant);
            // Display payloads cannot be used as a TakeReward/EnterCardSelection
            // input, and unknown theft must not leak guessed instance IDs.
            var value = relevant.IsEmpty ? reward with
            {
                Id = 0, ContentInstanceId = null, SubscriptionOrder = 0,
                Cards = reward.Cards.Select(c => c with { Id = 0 }).ToImmutableArray()
            } : null;
            var observation = reward.Kind == PredictorRewardKind.Card
                ? CardRewardKnowledge(reward, relevant, PredictorRouteCauseEvidence.Normalize(For(PredictorCombatDomain.NicheRng)
                    .AddRange(_preview.Partial?.StreamCauses(PredictorStream.Niche) ?? (Context.Live?.PendingCauses ?? [])
                        .Where(cause => (cause.AffectedDomains & PredictorCombatDomain.NicheRng) != 0).ToImmutableArray())))
                : new PredictorCombatRewardKnowledge(reward.Kind, value, relevant,
                    UnknownMultiplicity: reward.Kind == PredictorRewardKind.Potion && !dropCauses.IsEmpty);
            rewards.Add(observation with
            { Occurrence = occurrence, SourceIdentity = _preview.Partial?.RegisterCombatRewardSource(Working.Position.NodeId, reward, occurrence) });
        }
        if (!dropCauses.IsEmpty && !scratch.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Potion))
            rewards.Add(new(PredictorRewardKind.Potion, null, dropCauses, UnknownMultiplicity: true)
            { SourceIdentity = _preview.Partial?.CombatPlaceholderIdentity(Working.Position.NodeId, "potion-drop-existence") });
        if (missing.StolenCards)
            rewards.Add(new(PredictorRewardKind.SpecialCard, null, For(PredictorCombatDomain.ReturnedCards), UnknownMultiplicity: true)
            { SourceIdentity = _preview.Partial?.CombatPlaceholderIdentity(Working.Position.NodeId, "stolen-card-existence") });
        var streams = scratch.Working.Streams.Select(s =>
        {
            var relevant = PredictorRouteCauseEvidence.Normalize(For(StreamKnowledgeDomain(s.Stream)).AddRange(
                _preview.Partial?.StreamCauses(s.Stream) ?? (Context.Live?.PendingCauses ?? [])
                    .Where(cause => (cause.AffectedDomains & StreamKnowledgeDomain(s.Stream)) != 0).ToImmutableArray()));
            return new PredictorCombatStreamKnowledge(s.Stream,
                relevant.IsEmpty ? s : Working.Streams.Single(before => before.Stream == s.Stream),
                relevant.IsEmpty ? s : null, relevant);
        }).ToImmutableArray();
        var goldCauses = For(PredictorCombatDomain.Gold);
        var knowledge = new PredictorCombatPreview(Working.Position, rewards.ToImmutable(), causes, streams,
            // A scratch witness may have omitted an unknown theft/loss or used
            // a zero Seal deduction. Only the entry balance is a proven base in
            // that case; never promote the placeholder arithmetic to knowledge.
            new(goldCauses.IsEmpty ? scratch.Working.GoldBudget : Working.GoldBudget,
                goldCauses.IsEmpty ? scratch.Working.GoldBudget : null, goldCauses), missing);
        if (combatTrace != null) _preview.Partial!.EndCombatTrace(combatTrace, scratch, knowledge, rolledPotion);
        return new(scratch, knowledge);
    }

    internal static bool CombatOddsCauseApplies(PredictorCombatPendingCause cause, string property)
    {
        var exact = property == "odds.cardRarity" ? PredictorCombatDomain.CardRarityOdds :
            property == "odds.potionDrop" ? PredictorCombatDomain.PotionDropOdds : throw new ArgumentException("CombatOddsPropertyInvalid");
        if ((cause.AffectedDomains & exact) != 0) return true;
        if ((cause.AffectedDomains & PredictorCombatDomain.Odds) == 0) return false;
        var fields = cause.RequiredPropertyKeys.Where(key => key is "odds.cardRarity" or "odds.potionDrop").ToArray();
        return fields.Length == 0 || fields.Contains(property, StringComparer.Ordinal);
    }
    internal static bool CombatCardReadsRarityOffset(PredictorContext context, PredictorReward reward) =>
        reward.Kind == PredictorRewardKind.Card && !reward.ManuallySetCards && reward.RerollRecipe is { } plan &&
        (plan.EncounterSource || plan.ForceRarityOddsChange) && plan.Odds is not (PredictorCardOdds.Boss or PredictorCardOdds.Uniform) &&
        !(plan.EncounterSource && plan.Odds == PredictorCardOdds.Elite && !plan.NoRarityModification && context.HasRunModifier("BIG_GAME_HUNTER"));
}
