using System.Collections.Immutable;
using System.Text.Json;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed record RouteEventOptionOutcome(string Key, PredictorState State, PredictorPhase Phase,
        ImmutableArray<PredictorCombatPendingCause> Local,
        ImmutableArray<RouteDependency> Dependencies,
        ImmutableArray<PredictorEventHealthDelta> HealthCommands, bool UnknownHealing,
        PendingRouteEventCardEffect? CardEffect, int? AssignedGold);

    // Join only actual completed alternatives from the same event boundary.
    // A child, loop, combat or terminal transition is not silently completed.
    // It remains an explicit local continuation until its own contract can
    // describe the remaining effects. No future-route Cartesian exploration.
    private static bool ContinuePendingEventOptions(PredictorRun run,
        ImmutableArray<PredictorEventOptionKnowledge> options, List<RouteDependency> dependencies,
        List<PredictorCombatPendingCause> local)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults ||
            run.Phase != PredictorPhase.EventChoice || run.EventNeedsHealthFact ||
            local.Count != 1 || local[0].Code != "EventOptionChoice") return false;
        // Gold differences stay in the correlated alternatives below. Only a
        // separate business choice or different public-stream consumption keeps
        // this source open; actual gold readers own their later prerequisites.
        if (DrawingEventBusinessChoiceRequired(run, options)) return false;
        var possible = options.Where(o => o.Enabled != false).ToArray();
        var outcomes = run._preview.Event.OptionOutcomes;
        bool Pending(string code, PredictorPendingReasonKind kind, string evidence,
            ImmutableArray<string> properties, IEnumerable<PredictorCombatPendingCause>? parents = null)
        {
            local[0] = PredictorRouteCauseEvidence.CreateExplicit(code, run.Working.Position.NodeId,
                PredictorCombatDomain.Continuation, kind, evidence, properties,
                PredictorRouteCauseEvidence.Normalize(new[] { local[0] }.Concat(parents ?? [])),
                occurrenceKey: run._eventKey?.Serialized);
            return false;
        }
        if (possible.Length == 0 || possible.Any(o => !o.IsKnown))
            return Pending("EventBranchAvailabilityPending", PredictorPendingReasonKind.DerivedDependency,
                "The set of available event branches is not determined by the recorded option inputs.",
                ["event.options.availability"], possible.SelectMany(o => o.PendingCauses));
        if (outcomes.Length != possible.Length ||
            outcomes.Any(o => o.Phase != PredictorPhase.Complete ||
                !o.Local.IsEmpty && (o.CardEffect == null || o.Local.Any(c => (c.AffectedDomains & PredictorCombatDomain.Continuation) != 0))))
            return Pending("EventBranchContinuationNotProjected", PredictorPendingReasonKind.ImplementationGap,
                "At least one branch still has a child, loop, combat or continuation whose remaining effects are not summarized by this local join.",
                outcomes.Where(o => o.Phase != PredictorPhase.Complete || !o.Local.IsEmpty)
                    .Select(o => "event.option." + o.Key + "." + o.Phase).ToImmutableArray(), outcomes.SelectMany(o => o.Local));
        var before = run.Working;
        // Optional card opportunities have already used the drawing's Skip
        // contract. Remaining direct/mandatory additions keep their real hooks;
        // subtracting all new-card gold would erase genuine forced receipts.
        var witness = outcomes[0].State;
        // Different terminal/map transitions cannot be represented by one
        // subsequent node. Require that actual structural choice at this point.
        if (outcomes.Any(o => o.State.Outcome != witness.Outcome ||
            !Same(o.State.Maps, witness.Maps) || !Same(o.State.Acts, witness.Acts) ||
            !Same(o.State.Visits, witness.Visits) || !Same(o.State.VisitedEvents, witness.VisitedEvents)))
            return Pending("EventBranchRouteContinuationPending", PredictorPendingReasonKind.DerivedDependency,
                "The options lead to different maps, queues or terminal outcomes; there is no common next route node.",
                ["map.route", "run.outcome", "event.queue"]);
        if (witness.Outcome != null)
            return Pending("EventBranchesTerminal", PredictorPendingReasonKind.KnownUnavailable,
                "All currently available options end the modeled run.", ["run.outcome"]);
        bool existingCardsChange = outcomes.Any(o => o.CardEffect?.MayEditExistingCards == true || o.CardEffect?.ExistingCards.IsEmpty == false ||
            before.Deck.Any(id => !o.State.Deck.Contains(id) || o.State.Card(id) != before.Card(id)) ||
            !o.State.Deck.Where(before.Deck.Contains).SequenceEqual(before.Deck.Where(o.State.Deck.Contains)));
        // Deck knowledge composes edits into the existing correlated choices.
        // Prior partial membership is not itself a structural route boundary.

        bool Varies<T>(Func<PredictorState, T> field) => outcomes.Any(o => !Same(field(witness), field(o.State)));
        var domains = PredictorCombatDomain.None;
        var newDependencies = outcomes.SelectMany(o => o.Dependencies).Where(d =>
            !dependencies.Any(prior => prior.Cause.Identity == d.Cause.Identity && prior.Cause.AffectedDomains == d.Cause.AffectedDomains))
            .ToImmutableArray();
        foreach (var added in newDependencies) domains |= added.Cause.AffectedDomains & ~PredictorCombatDomain.Continuation;
        foreach (var effect in outcomes.Select(o => o.CardEffect).OfType<PendingRouteEventCardEffect>()) domains |= effect.Domains;
        if (Varies(s => s.Deck.Select(s.Card).ToArray())) domains |= PredictorCombatDomain.Deck;
        if (Varies(s => s.GoldBudget)) domains |= PredictorCombatDomain.Gold;
        if (Varies(s => s.Relics)) domains |= PredictorCombatDomain.Relics;
        if (Varies(s => s.PotionSlots)) domains |= PredictorCombatDomain.PotionInventory;
        if (Varies(s => s.NextInstanceId)) domains |= PredictorCombatDomain.InstanceIds;
        if (Varies(s => s.PersonalBag) || Varies(s => s.SharedBag)) domains |= PredictorCombatDomain.RelicRewards;
        if (Varies(s => s.Odds.CardRarityOffset)) domains |= PredictorCombatDomain.CardRarityOdds;
        if (Varies(s => s.Odds.PotionDropChance)) domains |= PredictorCombatDomain.PotionDropOdds;
        if (Varies(s => s.UnknownOdds)) domains |= PredictorCombatDomain.Map;
        if (Varies(s => s.HoarderCardsToSkip)) domains |= PredictorCombatDomain.CardRewardHookState;
        if (Varies(s => s.DetachedCardRewards)) domains |= PredictorCombatDomain.CardRewardHookState;
        if (Varies(s => s.CardShopRemovalsUsed) || Varies(s => s.WongoPoints) ||
            Varies(s => s.CombatCount) || Varies(s => s.CurrentRoomIsCombat))
            return Pending("EventBranchCounterJoinNotProjected", PredictorPendingReasonKind.ImplementationGap,
                "A branch changes a persistent counter not yet consumed symbolically by this event join.",
                ["run.counters"]);
        foreach (var stream in witness.Streams)
            if (Varies(s => s.Streams.Single(r => r.Stream == stream.Stream))) domains |= StreamKnowledgeDomain(stream.Stream);
        if (outcomes.Any(o => o.UnknownHealing || !o.HealthCommands.IsEmpty)) domains |= PredictorCombatDomain.Health;

        string[] VaryingRelics(bool membership) => outcomes.SelectMany(o => o.State.Relics.Select(r => r.Key.Entry)).Distinct()
            .Where(key => Varies(s => membership
                ? JsonSerializer.Serialize(s.Relics.Count(r => !r.Melted && r.Key.Entry == key))
                : JsonSerializer.Serialize(s.Relics.Where(r => r.Key.Entry == key).ToArray())))
            .ToArray();
        string[] VaryingCards(bool membership) => outcomes.SelectMany(o => o.State.Deck.Select(id => o.State.Card(id).Key.Entry)).Distinct()
            .Where(key => Varies(s => membership
                ? JsonSerializer.Serialize(s.Deck.Count(id => s.Card(id).Key.Entry == key))
                : JsonSerializer.Serialize(s.Deck.Select(s.Card).Where(c => c.Key.Entry == key).ToArray())))
            .ToArray();
        ImmutableArray<string> RelicScope(bool membership)
        {
            var scopes = newDependencies.Where(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Relics) != 0)
                .Select(d => membership ? d.ChangedRelicEntries : d.ChangedRelicStateEntries)
                .Concat(outcomes.Select(o => o.CardEffect).OfType<PendingRouteEventCardEffect>()
                    .Select(effect => membership ? effect.ChangedRelicEntries : effect.ChangedRelicStateEntries)).ToArray();
            return scopes.Any(scope => scope.IsDefault) ? default :
                VaryingRelics(membership).Concat(scopes.SelectMany(scope => scope)).Distinct().ToImmutableArray();
        }
        bool? RelicBagScope(bool shared)
        {
            if (Varies(state => shared ? state.SharedBag : state.PersonalBag)) return true;
            var scopes = newDependencies.Where(d =>
                    (d.Cause.AffectedDomains & (PredictorCombatDomain.Relics | PredictorCombatDomain.RelicRewards)) != 0)
                .Select(d => shared ? d.ChangesSharedRelicBag : d.ChangesPersonalRelicBag).ToArray();
            return scopes.Any(scope => scope == true) ? true : scopes.Any(scope => scope == null) ? null : false;
        }
        var choice = PredictorRouteCauseEvidence.Bind(local[0] with
        {
            AffectedDomains = domains,
            SourceEvidence = "The completed options of this event differ in these properties; each alternative retains one whole correlated state.",
            RequiredPropertyKeys = ["event.option"],
            Parents = PredictorRouteCauseEvidence.Normalize(local[0].Parents.AddRange(newDependencies
                .Where(d => d.Cause.ReasonKind != PredictorPendingReasonKind.UserChoice).Select(d => d.Cause))),
            ReasonKind = PredictorPendingReasonKind.UserChoice
        });
        var dependency = new RouteDependency(choice,
            outcomes.SelectMany(o => o.State.Deck.Select(o.State.Card)).Where(card =>
                outcomes.Any(o => !o.State.Deck.Contains(card.Id) || o.State.Card(card.Id) != card))
                .Concat(outcomes.SelectMany(o => o.CardEffect?.Cards ?? [])).Distinct().ToImmutableArray(),
            MayChangeExistingCards: existingCardsChange)
        {
            ChangedRelicEntries = RelicScope(true),
            ChangedRelicStateEntries = RelicScope(false),
            ChangedCardMembershipEntries = VaryingCards(true).Concat(outcomes.Where(o => o.CardEffect?.ChangesMembership == true)
                .SelectMany(o => o.CardEffect!.Cards.Select(c => c.Key.Entry))).Distinct().ToImmutableArray(),
            ChangedCardStateEntries = VaryingCards(false).Concat(outcomes.SelectMany(o => o.CardEffect?.Cards ?? []).Select(c => c.Key.Entry))
                .Distinct().ToImmutableArray(),
            ChangesPersonalRelicBag = RelicBagScope(false),
            ChangesSharedRelicBag = RelicBagScope(true)
        };
        run._preview.Partial?.PendingEventOptions(before, witness, outcomes, choice);
        if (domains != PredictorCombatDomain.None) AddDependency(dependencies, dependency);
        // Keep one complete execution as private numerical witness. Never
        // construct a hybrid from convenient fields of different options.
        run.Working = witness;
        run.FinishEvent();
        local.Clear();
        return true;
    }

    private static bool Same<T>(T left, T right) =>
        EqualityComparer<T>.Default.Equals(left, right) || JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    private sealed partial class RoutePartialState
    {
        // Retained together, not split into unrelated per-property intervals.
        private readonly Dictionary<string, ImmutableArray<RouteEventOptionOutcome>> _eventAlternatives = [];
        private int _goldProjectionStart;
        private readonly HashSet<string> _conditionalGoldAssignments = [];
        internal void AssignEventGold() => _goldProjectionStart = Deck.GoldProjectionBoundary;

        internal ImmutableArray<PredictorCombatPendingCause> WitnessCardCauses(PredictorCard card)
        {
            // A full card observation reads both membership and payload. Stable
            // IDs do not certify an unresolved enchantment; conditional receipt
            // IDs must likewise retain their own target gap.
            if (!Deck.HasFixedMember(card.Id))
                return WitnessCardMembershipCauses(card);
            var gaps = Deck.TargetGaps(card.Id, PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.All);
            var retained = gaps.Where(gap => gap.ChoiceId != null && _choices.ContainsKey(gap.ChoiceId))
                .Select(gap => _choices[gap.ChoiceId!]);
            var unbound = gaps.Where(gap => gap.ChoiceId == null || !_choices.ContainsKey(gap.ChoiceId)).ToImmutableArray();
            var target = PredictorRouteCauseEvidence.Normalize(retained.Concat(ProjectionCauses(
                new(PredictorDeckProjectionScope.All, null, [], false, [], unbound), count: false)));
            return target;
        }

        internal ImmutableArray<PredictorCombatPendingCause> WitnessCardMembershipCauses(PredictorCard card)
        {
            if (Deck.HasFixedMember(card.Id)) return [];
            return PredictorRouteCauseEvidence.Normalize(Deck.Project(PredictorDeckProjectionScope.All)
                    .UnresolvedCandidateSources.Where(source => _choices.ContainsKey(source.ChoiceId) &&
                        source.Effects.Any(effect => effect.AddedCards.Any(added => added.Id == card.Id)))
                    .Select(source => _choices[source.ChoiceId]).Concat(_eventAlternatives.Where(pair => pair.Value.Any(o => o.State.Deck.Contains(card.Id)) &&
                    pair.Value.Any(o => !o.State.Deck.Contains(card.Id) || o.State.Card(card.Id).Key != card.Key))
                    .Select(pair => _choices[pair.Key])));
        }

        internal PredictorChoiceInt? EventGoldBalance(PredictorState state)
        {
            // Only complete local outcomes have a proved Gold delta. An
            // unrelated missing capture or unmodeled operation stays unknown.
            if (Domain(PredictorCombatDomain.Gold).Any(c => c.ChoiceId == null ||
                _conditionalGoldAssignments.Contains(c.ChoiceId) || !_eventAlternatives.TryGetValue(c.ChoiceId, out var alternatives) || alternatives.Any(o =>
                    o.CardEffect != null && (o.CardEffect.Domains & PredictorCombatDomain.Gold) != 0))) return null;
            var delta = Deck.ProjectGoldDelta(_goldProjectionStart);
            return delta == null ? null : delta + state.GoldBudget;
        }

        internal void PendingEventCards(PredictorRun run, PredictorCombatPendingCause cause,
            PendingRouteEventCardEffect effect, PredictorState? targetBoundary = null)
        {
            var boundary = targetBoundary ?? run.Working;
            Synchronize(boundary);
            cause = PredictorRouteCauseEvidence.Bind(cause);
            if (cause.ChoiceId is { } choice) _choices.TryAdd(choice, cause);
            ApplyPendingEventCards(boundary, cause, effect);
        }

        private void ApplyPendingEventCards(PredictorState state, PredictorCombatPendingCause cause,
            PendingRouteEventCardEffect effect)
        {
            string operation = "event-target:" + cause.Identity;
            _deckOperations[(state.Position.NodeId, operation)] = cause;
            _opaqueDeck.Add(cause.Identity);
            if (effect.MayEditExistingCards || !effect.ExistingCards.IsEmpty)
            {
                // A partial population may contain further eligible targets.
                // Do not mistake the execution witness's IDs for that full set.
                var read = effect.TargetRead;
                var projection = Deck.Project(read.Scope);
                bool complete = projection.OrderedCandidatesKnown && projection.KnownCandidates.All(card =>
                    Deck.TargetGaps(card.Id, read.Scope, read.Qualification).IsEmpty);
                Deck = Deck.MarkExistingChangePending(state.Position.NodeId,
                    "PendingEventCardTarget", effect.AffectedFacts,
                    instanceIds: complete ? effect.ExistingCards.Select(c => c.Id).ToImmutableArray() : [],
                    choiceId: cause.ChoiceId, sourceOperation: operation);
            }
            if (effect.AddsCards)
                Deck = cause.ChoiceId is { } choiceId
                    ? Deck.MarkChoiceAdditionPending(state.Position.NodeId, choiceId, effect.Cards.Select(c => c.Type).Distinct().ToImmutableArray())
                    : Deck.MarkUnknownAddition(state.Position.NodeId, "PendingEventCardAddition",
                        cardTypes: effect.Cards.Select(c => c.Type).Distinct().ToImmutableArray());
        }

        internal void PendingEventOptions(PredictorState before, PredictorState witness,
            ImmutableArray<RouteEventOptionOutcome> outcomes, PredictorCombatPendingCause cause)
        {
            Synchronize(before);
            string choiceId = cause.ChoiceId ?? throw new InvalidOperationException("PredictorEventChoiceIdentityMissing");
            if (outcomes.Any(o => o.AssignedGold != null))
            {
                if (outcomes.All(o => o.AssignedGold != null && o.AssignedGold == outcomes[0].AssignedGold))
                {
                    AssignEventGold();
                    ClearRouteEventDependencyDomain(_dependencies, PredictorCombatDomain.Gold);
                }
                else if (EventGoldBalance(before)?.KnownValue == null) _conditionalGoldAssignments.Add(choiceId);
            }
            _choices.Add(choiceId, cause);
            _eventAlternatives.Add(choiceId, outcomes);
            Deck = Deck.AddStateChoice(choiceId, before.Position.NodeId, before, witness,
                outcomes.Select(o => (o.Key, o.State)).ToImmutableArray());
            foreach (var effect in outcomes.Select(o => o.CardEffect).OfType<PendingRouteEventCardEffect>())
                ApplyPendingEventCards(witness, cause, effect);
            string contract = before.Position.NodeId.Length + ":" + before.Position.NodeId +
                string.Concat(outcomes.Select(o => o.Key.Length + ":" + o.Key));
            foreach (var stream in before.Streams)
            {
                var consumed = outcomes.Select(o => (long)o.State.Streams.Single(s => s.Stream == stream.Stream).Calls - stream.Calls).ToArray();
                if (consumed.Any(n => n < 0)) throw new InvalidOperationException("PredictorEventChoiceCursorRewound");
                _streamOffsets[stream.Stream] += PredictorChoiceInt.Alternatives(choiceId, contract, consumed);
            }
            // Synchronize must not promote this one alternative's card/stream
            // changes. They have just been incorporated as a grouped choice.
            _observed = witness;
            var source = Facts.RecordResult("event.option.pending", before.Position.NodeId, "unselected", [cause],
                operation: "event option: correlated completed outcomes");
            foreach (var outcome in outcomes)
            {
                void Conditional(string property, string value, PredictorCombatDomain reads) =>
                    Facts.RecordResult("event.option." + outcome.Key + ".conditional." + property, before.Position.NodeId,
                        value, CausesFor(outcome.Dependencies.ToList(), reads),
                        operation: "conditional outcome of option " + outcome.Key, allReadFactIds: [source],
                        derivation: "Conditional on this one option; earlier missing inputs still apply. Fields from different options cannot be combined.");
                Conditional("gold.balance", outcome.State.GoldBudget.ToString(), PredictorCombatDomain.Gold);
                Conditional("deck.models", string.Join(",", outcome.State.Deck.Select(id => outcome.State.Card(id).Key.Entry)), PredictorCombatDomain.Deck);
                Conditional("relics.models", string.Join(",", outcome.State.Relics.Where(r => !r.Melted).Select(r => r.Key.Entry)), PredictorCombatDomain.Relics);
            }
        }
    }
}
