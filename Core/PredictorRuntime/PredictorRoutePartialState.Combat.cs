using System.Collections.Immutable;
using System.Globalization;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed class CombatTraceInput(string nodeId, PredictorState entry, PredictorCombatFacts resolved)
    {
        internal readonly string NodeId = nodeId;
        internal readonly PredictorState Entry = entry;
        internal readonly PredictorCombatFacts Resolved = resolved;
        internal readonly Dictionary<string, PredictorKnowledgeFactId> Inputs = new(StringComparer.Ordinal);
        internal readonly List<PredictorKnowledgeFactId> RewardReads = [];
        internal readonly List<PredictorKnowledgeFactId> EnemyReads = [];
        internal readonly List<PredictorKnowledgeFactId> NicheReads = [];
        internal readonly List<PredictorKnowledgeFactId> HuntReads = [];
        internal readonly List<PredictorKnowledgeFactId> RoyaltiesReads = [];
        internal bool CardReadsRarity;
        internal bool PotionDropRolled;
    }

    private sealed partial class RoutePartialState
    {
        private sealed record CombatRewardSource(PredictorReward Generated, string Identity);
        private readonly Dictionary<(string Node, long RewardId), CombatRewardSource> _combatRewardSources = [];
        private readonly Dictionary<string, PredictorReward> _combatGeneratedRewards = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Node, string Source), PredictorKnowledgeFactId> _combatResultFacts = [];
        internal ImmutableArray<PredictorCombatPendingCause> CombatOddsCauses(string property) =>
            Domain(PredictorCombatDomain.Odds | (property == "odds.cardRarity" ? PredictorCombatDomain.CardRarityOdds :
                property == "odds.potionDrop" ? PredictorCombatDomain.PotionDropOdds : throw new ArgumentException("CombatOddsPropertyInvalid")))
                .Where(cause => CombatOddsCauseApplies(cause, property)).ToImmutableArray();
        internal string RegisterCombatRewardSource(string nodeId, PredictorReward reward, int occurrence)
        {
            string identity = PredictorRouteCauseEvidence.Key(Facts.ForecastId.ToString("N"), "combat-reward", nodeId,
                reward.Kind.ToString(), reward.Origin.ToString(), reward.Key.Serialized, occurrence.ToString(CultureInfo.InvariantCulture));
            _combatRewardSources[(nodeId, reward.Id)] = new(reward, identity);
            _combatGeneratedRewards.Add(identity, reward);
            return identity;
        }
        internal string CombatPlaceholderIdentity(string nodeId, string source) =>
            PredictorRouteCauseEvidence.Key(Facts.ForecastId.ToString("N"), "combat-placeholder", nodeId, source);
        internal string? CombatRewardSourceIdentity(string nodeId, PredictorReward reward) =>
            _combatRewardSources.TryGetValue((nodeId, reward.Id), out var source) && CombatRewardSourceMatches(source.Generated, reward)
                ? source.Identity : null;
        internal ImmutableArray<PredictorKnowledgeFactId> CombatResultReads(string nodeId, PredictorCombatRewardKnowledge reward) =>
            reward.SourceIdentity is { } source && _combatResultFacts.TryGetValue((nodeId, source), out var id) ? [id] : [];
        internal ImmutableArray<PredictorKnowledgeFactId> CombatResultUnresolvedReads(string nodeId, PredictorCombatRewardKnowledge reward) =>
            CombatResultReads(nodeId, reward).Where(id => Facts.Fact(id).Status != PredictorKnowledgeStatus.Known).ToImmutableArray();

        // This hook runs before ProjectCurrentCombat introduces any scratch
        // 0/1. It records resolved input semantics, not the witness's fields.
        internal CombatTraceInput BeginCombatTrace(PredictorRun run, PredictorCombatEntry entry,
            PredictorCombatDefaultResolution resolution, PredictorCombatNodeOverrides? plan,
            ImmutableArray<PredictorCombatPendingCause> pending)
        {
            Synchronize(run.Working);
            var token = new CombatTraceInput(run.Working.Position.NodeId, run.Working, resolution.Facts);
            var resolved = resolution.Facts;
            var missing = resolution.MissingFacts;
            var sourceOrigin = run.Context.Live == null ? PredictorKnowledgeOrigin.Derived : PredictorKnowledgeOrigin.Captured;
            ImmutableArray<PredictorCombatPendingCause> Causes(string code, PredictorEnemyCreationSource? source = null) =>
                pending.Where(c => c.Code == code && (source == null || c.EnemySource == source)).ToImmutableArray();
            PredictorKnowledgeFactId Input<T>(string key, T value, PredictorKnowledgeOrigin origin,
                ImmutableArray<PredictorCombatPendingCause> causes = default, string? stateRead = null, string? provenance = null,
                bool invariantRead = false, ImmutableArray<PredictorKnowledgeFactId> additionalReads = default)
            {
                var reads = stateRead != null && _latest.TryGetValue(stateRead, out var prior) ? ImmutableArray.Create(prior) : [];
                if (!additionalReads.IsDefault) reads = reads.AddRange(additionalReads);
                var unresolved = invariantRead ? [] : Unresolved(reads);
                var id = Facts.RecordValue("combat.input." + key, token.NodeId, PredictorRouteFactPhase.Results,
                    value, causes, origin, origin == PredictorKnowledgeOrigin.PlayerInput ? "combat input: node override or saved settings" :
                        "combat input: resolved source", reads, unresolved, provenance);
                token.Inputs[key] = id;
                return id;
            }
            PredictorKnowledgeFactId Missing(string key, ImmutableArray<PredictorCombatPendingCause> causes)
            {
                var id = Facts.RecordUnknown("combat.input." + key, token.NodeId, PredictorRouteFactPhase.Results, causes,
                    status: PredictorKnowledgeStatus.NotObserved, operation: "combat input: missing source observation");
                token.Inputs[key] = id; return id;
            }
            void RewardInput<T>(string key, T value) => token.RewardReads.Add(Input(key, value, sourceOrigin));
            RewardInput("room.kind", entry.Kind);
            RewardInput("room.act", run.Working.Position.Act);
            RewardInput("encounter.rewardsEnabled", entry.Encounter.ShouldGiveRewards);
            RewardInput("encounter.minimumGold", entry.Encounter.MinimumGold);
            RewardInput("encounter.maximumGold", entry.Encounter.MaximumGold);
            RewardInput("context.ascension", run.Context.Ascension);
            RewardInput("context.numberOfRuns", run.Context.NumberOfRuns);
            RewardInput("context.revealedEpochs", string.Join(" | ", run.Context.RevealedEpochs));
            RewardInput("context.character", run.Context.Character.Serialized);
            RewardInput("context.modifiers", string.Join(" | ", run.Context.Modifiers.Select(m => m.Key.Serialized + ":" + m.Character?.Serialized)));
            RewardInput("context.cardPool", string.Join(" | ", run.Context.Catalog?.CharacterCards.Select(card =>
                card.CardKey.Serialized + ":" + card.Rarity + ":unlocked=" + card.IsUnlockedInCapturedPool) ?? []));
            RewardInput("context.potionPool", string.Join(" | ", run.Context.PotionPool.Select(potion => potion.Key.Serialized + ":" + potion.Rarity)));
            token.RewardReads.Add(Input("relics.orderedInventory", string.Join(" | ", run.Working.Relics.Where(r => !r.Melted)
                .Select(r => r.Key.Serialized)), sourceOrigin, stateRead: "relics.orderedInventory"));
            token.RewardReads.Add(Input("odds.cardRarity", run.Working.Odds.CardRarityOffset, sourceOrigin,
                CombatOddsCauses("odds.cardRarity"), stateRead: "odds.cardRarity"));
            token.RewardReads.Add(Input("odds.potionDrop", run.Working.Odds.PotionDropChance, sourceOrigin,
                CombatOddsCauses("odds.potionDrop"), stateRead: "odds.potionDrop"));
            foreach (var stream in new[] { PredictorStream.Niche, PredictorStream.Rewards })
            {
                var cursor = run.Working.Streams.Single(s => s.Stream == stream);
                var id = Input("rng." + stream + ".calls", cursor.Calls, sourceOrigin, StreamCauses(stream));
                if (stream == PredictorStream.Rewards) token.RewardReads.Add(id);
                else token.NicheReads.Add(id);
            }
            foreach (var source in entry.Encounter.EnemyCreationSources)
            {
                string key = "enemyCreation." + source + ".count";
                var declared = resolved.EnemyCreations.FirstOrDefault(value => value.Source == source);
                var id = declared == null ? Missing(key, Causes("EnemyCreationCount", source)) :
                    Input(key, declared.Count, plan?.EnemyCreations.Any(value => value.Source == source) == true ||
                        run.CombatDefaults.EnemyRules.Any(value => value.Source == source)
                        ? PredictorKnowledgeOrigin.PlayerInput : PredictorKnowledgeOrigin.ApprovedDefault,
                        provenance: plan?.EnemyCreations.Any(value => value.Source == source) == true ? "node.Combat.EnemyCreations" :
                            run.CombatDefaults.EnemyRules.Any(value => value.Source == source) ? "settings.CombatDefaults.EnemyRules" : "built-in approved enemy premise");
                token.EnemyReads.Add(id); token.NicheReads.Add(id);
            }
            // Consequences.Apply filters by supported source models. Optional
            // ordinary cards do not change that filter. Retain the complete
            // read audit while propagating only actual possible source changes.
            var projection = Deck.Project(PredictorDeckProjectionScope.All);
            var sourceCauses = projection.Sources.Where(choice => choice.Effects.Any(effect => !effect.AdditionsKnown ||
                effect.AddedCards.Any(PredictorCombatConsequences.IsTriggeredSource)))
                .Where(choice => _choices.ContainsKey(choice.ChoiceId)).Select(choice => _choices[choice.ChoiceId]).ToImmutableArray();
            if (!projection.Gaps.IsEmpty) sourceCauses = sourceCauses.AddRange(ProjectionCauses(projection, count: false));
            var deck = Input("cardSources.entryDeck", string.Join(" | ", resolved.CardTriggers.Select(trigger =>
                run.Working.Card(trigger.InstanceId).Key.Serialized)), sourceOrigin, sourceCauses,
                stateRead: "deck.All.candidates", invariantRead: true,
                provenance: "Supported trigger models are filtered from the entry deck; known optional additions without those models do not alter this source population.");
            PredictorKnowledgeFactId SourcePopulation(string model)
            {
                var causes = projection.Sources.Where(choice => choice.Effects.Any(effect => !effect.AdditionsKnown ||
                    effect.AddedCards.Any(card => card.Key.Entry == model)))
                    .Where(choice => _choices.ContainsKey(choice.ChoiceId)).Select(choice => _choices[choice.ChoiceId]).ToImmutableArray();
                var relevantGaps = projection.Gaps.Where(gap => (gap.Facts & (PredictorDeckUncertainFact.Membership |
                    PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Other)) != 0 ||
                    model == "ROYALTIES" && (gap.Facts & PredictorDeckUncertainFact.Upgrade) != 0 &&
                    resolved.CardTriggers.Any(trigger => run.Working.Card(trigger.InstanceId).Key.Entry == model &&
                        (gap.InstanceIds.IsEmpty || gap.InstanceIds.Contains(trigger.InstanceId)))).ToImmutableArray();
                if (!relevantGaps.IsEmpty) causes = causes.AddRange(ProjectionCauses(projection with { Sources = [], Gaps = relevantGaps }, count: false));
                return Input("cardSources." + model, string.Join(" | ", resolved.CardTriggers.Where(trigger =>
                    run.Working.Card(trigger.InstanceId).Key.Entry == model).Select(trigger => trigger.InstanceId)), sourceOrigin, causes,
                    stateRead: "deck.All.candidates", invariantRead: true,
                    provenance: "Only this source model affects its extra-reward population; other known optional card models do not.");
            }
            var hunts = SourcePopulation("THE_HUNT");
            var royaltiesPopulation = SourcePopulation("ROYALTIES");
            token.HuntReads.Add(hunts); token.RoyaltiesReads.Add(royaltiesPopulation);
            foreach (var trigger in resolved.CardTriggers)
            {
                var card = run.Working.Card(trigger.InstanceId);
                var population = card.Key.Entry == "THE_HUNT" ? hunts : card.Key.Entry == "ROYALTIES" ? royaltiesPopulation : deck;
                var model = Input("card." + trigger.InstanceId + ".model", card.Key.Serialized, sourceOrigin, additionalReads: [population]);
                var upgradeGaps = projection.Gaps.Where(gap => (gap.Facts & PredictorDeckUncertainFact.Upgrade) != 0 &&
                    (gap.InstanceIds.IsEmpty || gap.InstanceIds.Contains(trigger.InstanceId))).ToImmutableArray();
                var upgrade = Input("card." + trigger.InstanceId + ".upgrade", card.UpgradeLevel, sourceOrigin,
                    upgradeGaps.IsEmpty ? [] : ProjectionCauses(projection with { Sources = [], Gaps = upgradeGaps }, count: false), additionalReads: [population]);
                var id = Input("card." + trigger.InstanceId + ".triggers", trigger.EffectiveTriggers,
                    plan?.CardTriggers.Any(value => value.InstanceId == trigger.InstanceId) == true ||
                        run.CombatDefaults.CardRules.Any(value => value.Card == card.Key)
                        ? PredictorKnowledgeOrigin.PlayerInput : PredictorKnowledgeOrigin.ApprovedDefault,
                        provenance: plan?.CardTriggers.Any(value => value.InstanceId == trigger.InstanceId) == true ? "node.Combat.CardTriggers" :
                            run.CombatDefaults.CardRules.Any(value => value.Card == card.Key) ? "settings.CombatDefaults.CardRules" : "built-in approved one-trigger premise",
                        additionalReads: [population]);
                if (card.Key.Entry == "THE_HUNT") token.HuntReads.AddRange([model, id]);
                if (card.Key.Entry == "ROYALTIES") token.RoyaltiesReads.AddRange([model, upgrade, id]);
            }
            for (int i = 0; i < resolved.TemporaryCardTriggers.Length; i++)
            {
                var source = resolved.TemporaryCardTriggers[i];
                var id = Input("temporaryCard." + i, source.Card.Serialized + ":" + source.UpgradeLevel + ":" + source.EffectiveTriggers,
                    PredictorKnowledgeOrigin.PlayerInput);
                if (source.Card.Entry == "THE_HUNT") token.HuntReads.Add(id);
                if (source.Card.Entry == "ROYALTIES") token.RoyaltiesReads.Add(id);
            }
            Input("automaticCardEffects", resolved.AutomaticCardEffects, PredictorKnowledgeOrigin.ApprovedDefault);
            var order = Input("cardSourceOrder", string.Join(" | ", resolved.CombatCardSourceOrder.Select(value =>
                value.DeckCardId?.ToString(CultureInfo.InvariantCulture) ?? "temporary:" + value.TemporarySourceIndex)),
                resolved.CombatCardSourceOrder.IsEmpty ? PredictorKnowledgeOrigin.ApprovedDefault : PredictorKnowledgeOrigin.PlayerInput);
            token.RewardReads.Add(order);
            foreach (var relic in resolved.RelicEffects)
            {
                var id = Input("relic." + relic.RelicId + ".effectEnabled", relic.EffectEnabled,
                    plan?.RelicEffects.Any(value => value.RelicId == relic.RelicId) == true ||
                        run.CombatDefaults.RelicRules.Any(value => value.Relic == run.Working.Relics.Single(r => r.Id == relic.RelicId).Key)
                        ? PredictorKnowledgeOrigin.PlayerInput : PredictorKnowledgeOrigin.ApprovedDefault);
                token.RewardReads.Add(id);
            }
            Input("tookDamage", resolved.TookDamageThisCombat, PredictorKnowledgeOrigin.Derived,
                stateRead: "relics.orderedInventory", additionalReads: token.Inputs.Where(pair =>
                    pair.Key.StartsWith("relic.", StringComparison.Ordinal) && pair.Key.EndsWith(".effectEnabled", StringComparison.Ordinal))
                    .Select(pair => pair.Value).ToImmutableArray());
            Input("extraRewardOrder", resolved.ExtraRewardOrder.IsEmpty ? "no explicit ordering" :
                string.Join(" | ", resolved.ExtraRewardOrder.Select(value => value.Source + ":" + value.Index)),
                resolved.ExtraRewardOrder.IsEmpty ? PredictorKnowledgeOrigin.ApprovedDefault : PredictorKnowledgeOrigin.PlayerInput);
            token.RewardReads.Add(Input("potions.noAdditionalChange", resolved.Potions?.NoAdditionalPotionChange ?? true,
                plan?.Potions == null ? PredictorKnowledgeOrigin.ApprovedDefault : PredictorKnowledgeOrigin.PlayerInput));
            token.RewardReads.Add(Input("potions.changes", string.Join(" | ", resolved.Potions?.Changes.Select(value =>
                value.Action + ":existing=" + value.Potion?.ExistingId + ":generated=" + value.Potion?.GeneratedOrdinal +
                ":before=" + value.BeforeCardId + ":temporary=" + value.BeforeTemporarySourceIndex + ":occurrence=" + value.BeforeOccurrence) ?? []),
                plan?.Potions == null ? PredictorKnowledgeOrigin.ApprovedDefault : PredictorKnowledgeOrigin.PlayerInput));
            if (missing.StolenCards) Missing("stolenCards", Causes("StolenCardIdentity"));
            else if (resolved.StolenCards is { } stolen) Input("stolenCards", string.Join(" | ", stolen.Cards.Select(value =>
                value.CardId + ":returned=" + value.ReturnedAsReward)), PredictorKnowledgeOrigin.PlayerInput);
            if (missing.VictoryHealth)
            {
                Missing("victoryHealth.current", Causes("VictoryHealth")); Missing("victoryHealth.maximum", Causes("VictoryHealth"));
            }
            else if (resolved.VictoryHealth is { } health)
            {
                Input("victoryHealth.current", health.CurrentHp, PredictorKnowledgeOrigin.PlayerInput);
                Input("victoryHealth.maximum", health.MaxHp, PredictorKnowledgeOrigin.PlayerInput);
            }
            if (missing.FirstPlayerTurn) Missing("firstPlayerTurn", Causes("FirstPlayerTurn"));
            else Input("firstPlayerTurn", resolved.FirstPlayerTurnOccurred,
                plan?.FirstPlayerTurnOccurred == null ? PredictorKnowledgeOrigin.ApprovedDefault : PredictorKnowledgeOrigin.PlayerInput);
            if (missing.EventTimeout) Missing("eventTimeout", Causes("EventTimeout"));
            if (resolved.GremlinGold is { } gremlin)
            {
                var origin = plan?.GremlinGold == null ? PredictorKnowledgeOrigin.ApprovedDefault : PredictorKnowledgeOrigin.PlayerInput;
                token.RewardReads.Add(Input("gremlin.escaped", gremlin.FatGremlinEscaped, origin));
                token.RewardReads.Add(Input("gremlin.wasStolen", gremlin.GoldWasStolen, origin));
                token.RewardReads.Add(Input("gremlin.recoverAll", gremlin.RecoverAllStolenGold, origin));
                if (!Causes("RecoveredGoldAmount").IsEmpty) Missing("gremlin.amount", Causes("RecoveredGoldAmount"));
                else Input("gremlin.amount", gremlin.RecoveredGold?.ToString(CultureInfo.InvariantCulture) ?? "no numeric recovery declared", origin);
                if (!Causes("StolenGoldLoss").IsEmpty) Missing("gremlin.loss", Causes("StolenGoldLoss"));
                else Input("gremlin.loss", gremlin.StolenGoldLoss?.ToString(CultureInfo.InvariantCulture) ?? "no numeric loss declared", origin);
            }
            if (run.Working.Relics.Any(r => !r.Melted && r.Key.Entry == "SEAL_OF_GOLD"))
            {
                if (run.Context.SealOfGoldFixedDeduction is { } amount) Input("seal.fixedDeduction", amount, PredictorKnowledgeOrigin.PlayerInput);
                else Missing("seal.fixedDeduction", Causes("SealOfGoldDeduction"));
            }
            return token;
        }

        // End records no state transition. The route owner imports pending
        // sources and synchronizes the final state later, in its existing order.
        internal void EndCombatTrace(CombatTraceInput input, PredictorRun settled, PredictorCombatPreview knowledge, bool rolledPotion)
        {
            input.PotionDropRolled = rolledPotion;
            // Calculation writes are appended only after the existing source
            // pipeline succeeded, never for an invalid/rejected plan.
            int creations = input.Resolved.EnemyCreations.Sum(value => value.Count);
            var consumed = Facts.RecordResult("combat.compute.enemyCreation.Niche.consumed", input.NodeId, creations,
                operation: "PredictorCombatEnemyFacts.Apply: one Niche draw per creation",
                allReadFactIds: input.EnemyReads.ToImmutableArray(), unresolvedReadFactIds: Unresolved(input.EnemyReads),
                derivation: "Every concrete creation/hatch path consumes exactly one draw; no incoming cursor value is needed for its count.");
            input.Inputs["enemyConsumption"] = consumed;
            var cursorReads = input.NicheReads.Where(id => !input.EnemyReads.Contains(id)).Append(consumed).ToImmutableArray();
            input.Inputs["enemyCursorAfter"] = Facts.RecordResult("combat.compute.enemyCreation.Niche.callsAfter", input.NodeId,
                checked(input.Entry.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls + creations),
                operation: "PredictorCombatEnemyFacts.Apply: cursor advance", allReadFactIds: cursorReads,
                unresolvedReadFactIds: Unresolved(cursorReads));
            int hunts = input.Resolved.CardTriggers.Where(trigger => input.Entry.Card(trigger.InstanceId).Key.Entry == "THE_HUNT")
                .Sum(trigger => trigger.EffectiveTriggers) + input.Resolved.TemporaryCardTriggers.Where(trigger => trigger.Card.Entry == "THE_HUNT")
                .Sum(trigger => trigger.EffectiveTriggers);
            int royalties = input.Resolved.CardTriggers.Where(trigger => input.Entry.Card(trigger.InstanceId).Key.Entry == "ROYALTIES")
                .Sum(trigger => trigger.EffectiveTriggers * (input.Entry.Card(trigger.InstanceId).UpgradeLevel > 0 ? 40 : 30)) +
                input.Resolved.TemporaryCardTriggers.Where(trigger => trigger.Card.Entry == "ROYALTIES")
                .Sum(trigger => trigger.EffectiveTriggers * (trigger.UpgradeLevel > 0 ? 40 : 30));
            input.HuntReads.Add(Facts.RecordResult("combat.compute.THE_HUNT.cardRewardCount", input.NodeId, hunts,
                operation: "PredictorCombatConsequences.Apply / THE_HUNT: append one Card plan per effective trigger",
                allReadFactIds: input.HuntReads.ToImmutableArray(), unresolvedReadFactIds: Unresolved(input.HuntReads)));
            input.RoyaltiesReads.Add(Facts.RecordResult("combat.compute.ROYALTIES.goldAmount", input.NodeId, royalties,
                operation: "PredictorCombatConsequences.Apply / ROYALTIES: accumulate 30 or 40 per effective trigger",
                allReadFactIds: input.RoyaltiesReads.ToImmutableArray(), unresolvedReadFactIds: Unresolved(input.RoyaltiesReads)));
            input.CardReadsRarity = settled.Rewards.Any(reward => CombatCardReadsRarityOffset(settled.Context, reward)) ||
                settled.Working.DetachedCardRewards.Skip(input.Entry.DetachedCardRewards.Length)
                    .Any(reward => CombatCardReadsRarityOffset(settled.Context, reward));
            var dropCauses = knowledge.PendingCauses.Where(cause => cause.Code == "CombatPotionDropConsumptionPending")
                .SelectMany(cause => cause.Parents).ToImmutableArray();
            if (!dropCauses.IsEmpty)
            {
                var dropReads = RewardReadsFor(input, PredictorRewardKind.Potion);
                input.Inputs["dropPopulationConsumption"] = Facts.RecordPartial("combat.compute.potionDrop.Rewards.consumed", input.NodeId,
                    PredictorRouteFactPhase.Results, "0 … 2", dropCauses,
                    [new("range-kind", "conservative enclosure")], operation: "Potion.Populate: optional two Rewards draws",
                    allReadFactIds: dropReads, unresolvedReadFactIds: Unresolved(dropReads));
            }
            var orderCauses = knowledge.PendingCauses.Where(c => c.Code == "ExtraRewardOrder").ToImmutableArray();
            var order = orderCauses.IsEmpty ? input.Inputs["extraRewardOrder"] :
                Facts.RecordUnknown("combat.input.extraRewardOrder", input.NodeId, PredictorRouteFactPhase.Results,
                    orderCauses, operation: "combat reward ordering");
            input.Inputs["extraRewardOrder"] = order;
            var reads = input.RewardReads.Concat(input.HuntReads).Concat(input.RoyaltiesReads).Append(order)
                .Where(id => input.PotionDropRolled || id != input.Inputs["odds.potionDrop"]).Distinct().ToImmutableArray();
            // GenerateCard's rarity branch changes identities/odds, but every
            // NextInt and NextFloat consumes once. It does not change the number
            // of draws for the same card plan; potion existence does.
            var unresolved = Unresolved(reads).Where(id => id != input.Inputs["odds.cardRarity"]).ToImmutableArray();
            // Endpoint of the existing settlement/reward pipeline. This is a
            // bounded read audit, not instruction coverage of every relic hook.
            foreach (string key in new[] { "cardRarity", "potionDrop" })
            {
                var oddsReads = RewardReadsFor(input, key == "cardRarity" ? PredictorRewardKind.Card : PredictorRewardKind.Potion);
                // The endpoint also retains the incoming property when no
                // recipe writes it. Absence of a rarity read in a card's
                // identity calculation does not prove an unknown state value.
                oddsReads = oddsReads.Add(input.Inputs["odds." + key]).Distinct().ToImmutableArray();
                var written = Facts.RecordResult("combat.compute.odds." + key, input.NodeId,
                    key == "cardRarity" ? settled.Working.Odds.CardRarityOffset : settled.Working.Odds.PotionDropChance,
                    operation: "SettleCombat / reward population: odds endpoint (conditional write or retention)", allReadFactIds: oddsReads,
                    unresolvedReadFactIds: Unresolved(oddsReads));
                RecordStateWrite(input.NodeId, "odds." + key, written);
            }
            var rewardCursor = knowledge.Streams.Single(s => s.Stream == PredictorStream.Rewards);
            var cursorCauses = rewardCursor.PendingCauses;
            Facts.RecordResult("combat.compute.rng.Rewards.consumed", input.NodeId,
                settled.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls -
                    input.Entry.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls,
                cursorCauses, operation: "SettleCombat / reward population: Rewards consumption",
                allReadFactIds: reads, unresolvedReadFactIds: unresolved,
                derivation: "For the same generated card plan, each bounded draw advances exactly once regardless of rolled rarity; optional potion population consumes two additional draws. A guaranteed potion or disabled reward roll does not read the incoming drop probability.");
            for (int i = 0; i < knowledge.Rewards.Length; i++)
            {
                var reward = knowledge.Rewards[i];
                var generated = reward.SourceIdentity != null ? _combatGeneratedRewards.GetValueOrDefault(reward.SourceIdentity) : null;
                var rewardReads = RewardReadsFor(input, reward.Kind,
                    generated != null && CombatCardReadsRarityOffset(settled.Context, generated));
                if (reward.Kind == PredictorRewardKind.Card && generated?.CardRewardNicheScope is
                    { DependentEnchantmentIndices.IsEmpty: false })
                    rewardReads = rewardReads.Add(input.Inputs["enemyCursorAfter"]);
                if (reward.Kind == PredictorRewardKind.SpecialCard && input.Inputs.TryGetValue("stolenCards", out var stolen)) rewardReads = rewardReads.Add(stolen);
                if (generated?.WasGoldStolenBack == true && input.Inputs.TryGetValue("gremlin.amount", out var amount)) rewardReads = rewardReads.Add(amount);
                var unknown = Unresolved(rewardReads);
                var id = reward.IsKnown ? Facts.RecordResult("combat.compute.reward." + i, input.NodeId, RewardText(reward.Value!),
                    operation: "SettleCombat / reward population: bounded actual inputs", allReadFactIds: rewardReads,
                    unresolvedReadFactIds: unknown) : Facts.RecordUnknown("combat.compute.reward." + i, input.NodeId,
                    PredictorRouteFactPhase.Results, reward.PendingCauses, operation: "SettleCombat / reward population: unresolved inputs",
                    allReadFactIds: rewardReads, unresolvedReadFactIds: unknown);
                if (reward.SourceIdentity is { } identity) _combatResultFacts[(input.NodeId, identity)] = id;
            }
        }
        private static ImmutableArray<PredictorKnowledgeFactId> RewardReadsFor(CombatTraceInput input, PredictorRewardKind kind,
            bool cardRarityRead = true)
        {
            // This is a bounded subset of the actual source pipeline's reads.
            // Do not attach another reward kind's odds as a causal input.
            var keys = new List<string> { "room.kind", "room.act", "encounter.rewardsEnabled",
                "context.numberOfRuns", "context.revealedEpochs", "context.character" };
            if (kind != PredictorRewardKind.SpecialCard && kind != PredictorRewardKind.RemoveCard) keys.Add("rng.Rewards.calls");
            if (kind == PredictorRewardKind.Gold) keys.AddRange(["encounter.minimumGold", "encounter.maximumGold",
                "context.modifiers", "gremlin.escaped", "gremlin.wasStolen", "gremlin.recoverAll"]);
            if (kind == PredictorRewardKind.Card) keys.AddRange(["context.ascension", "context.cardPool", "context.modifiers",
                "relics.orderedInventory", "cardSourceOrder", "extraRewardOrder"]);
            if (kind == PredictorRewardKind.Card && cardRarityRead && input.CardReadsRarity) keys.Add("odds.cardRarity");
            if (kind == PredictorRewardKind.Potion) keys.AddRange(["context.potionPool", "relics.orderedInventory"]);
            if (kind == PredictorRewardKind.Potion && input.PotionDropRolled) keys.Add("odds.potionDrop");
            if (kind == PredictorRewardKind.Relic) keys.Add("relics.orderedInventory");
            if (kind == PredictorRewardKind.SpecialCard) keys.Add("stolenCards");
            if (kind is PredictorRewardKind.Card or PredictorRewardKind.Relic) keys.Add("dropPopulationConsumption");
            var reads = keys.Where(input.Inputs.ContainsKey).Select(key => input.Inputs[key]);
            if (kind == PredictorRewardKind.Card) reads = reads.Concat(input.HuntReads).Concat(input.Inputs
                .Where(pair => pair.Key.StartsWith("relic.", StringComparison.Ordinal) && pair.Key.EndsWith(".effectEnabled", StringComparison.Ordinal)).Select(pair => pair.Value));
            if (kind == PredictorRewardKind.Gold) reads = reads.Concat(input.RoyaltiesReads);
            return reads.Distinct().ToImmutableArray();
        }
        private ImmutableArray<PredictorKnowledgeFactId> Unresolved(IEnumerable<PredictorKnowledgeFactId> reads)
        {
            return reads.Where(id => Facts.Fact(id).Status != PredictorKnowledgeStatus.Known).Distinct().ToImmutableArray();
        }
    }
    internal static bool CombatRewardSourceMatches(PredictorReward generated, PredictorReward current) =>
        generated.Id == current.Id && generated.RerollRecipe == current.RerollRecipe && SameOffer(generated, current);
}
