using System.Collections.Immutable;
using System.Globalization;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private EventTraceBranch? EventTrace() => _preview.EventTrace?.Node == Working.Position.NodeId ? _preview.EventTrace :
        _preview.EventTrace = _preview.Partial?.NewEventTrace(this, "route");

    private sealed partial class RoutePartialState
    {
        // An option may acquire a relic whose live-map policy updates partial
        // state. Give it its own calculation owner and immutable source deck;
        // its recorder has private cause caches within the forecast evidence owner.
        internal RoutePartialState(PredictorContext context, PredictorState state,
            List<RouteDependency> dependencies, PredictorRouteFactRecorder facts, RoutePartialState? source)
            : this(context, state, dependencies, facts)
        {
            if (source == null) return;
            Deck = source.Deck;
            _knownRewardCardIds.UnionWith(source._knownRewardCardIds);
            // These values are immutable provenance. Copy their dictionaries,
            // so inherited deck alternatives retain their real causes without
            // allowing this branch to register choices on the parent owner.
            foreach (var pair in source._choices) _choices.Add(pair.Key, pair.Value);
            foreach (var pair in source._deckOperations) _deckOperations[pair.Key] = pair.Value;
            foreach (var identity in source._opaqueDeck) _opaqueDeck.Add(identity);
            foreach (var pair in source._eventAlternatives) _eventAlternatives.Add(pair.Key, pair.Value);
            _goldProjectionStart = source._goldProjectionStart;
            foreach (var identity in source._conditionalGoldAssignments) _conditionalGoldAssignments.Add(identity);
        }

        // Forks publish immutable ID groups only. They never receive this
        // calculation owner or synchronize its deck/stream offsets.
        internal List<RouteDependency> EventTraceDependencies => _dependencies;
        internal EventTraceBranch NewEventTrace(PredictorRun run, string scope)
        {
            string node = run.Working.Position.NodeId;
            return new(Facts, node, scope, (identity, groups, hypothetical) =>
            {
                Facts.PublishEventReads(node, identity, groups);
                if (!hypothetical && groups.TryGetValue("shared.gold.write", out var writes))
                    RecordStateWrite(node, "gold.balance", writes.Single());
            });
        }
        internal ImmutableArray<PredictorKnowledgeFactId> EventResultReads(string nodeId, string? identity, string field = "stage") =>
            Facts.EventResultReads(nodeId, identity, field);
        internal ImmutableArray<PredictorKnowledgeFactId> EventResultUnresolvedReads(string nodeId, string? identity, string field = "stage") =>
            EventResultReads(nodeId, identity, field).Where(id => Facts.Fact(id).Status != PredictorKnowledgeStatus.Known).ToImmutableArray();
    }

    private sealed class EventTraceBranch
    {
        private readonly PredictorRouteFactRecorder _facts;
        internal PredictorRouteFactRecorder Facts => _facts;
        private readonly Action<string, ImmutableDictionary<string, ImmutableArray<PredictorKnowledgeFactId>>, bool> _publish;
        private readonly Dictionary<string, ImmutableArray<PredictorKnowledgeFactId>> _groups = new(StringComparer.Ordinal);
        private readonly string _prefix;
        private int _operation;
        private PredictorStreamState? _lastRng;
        private ImmutableArray<PredictorCombatPendingCause> _rngCauses = [];
        private ImmutableArray<PredictorKnowledgeFactId> _selection = [];
        private ImmutableArray<RouteDependency> _sourceDependencies = [];
        private int? _actionGoldBefore;
        internal readonly string Node;
        internal string Identity { get; private set; }
        internal readonly bool Hypothetical;
        internal EventTraceBranch(PredictorRouteFactRecorder facts, string node, string scope,
            Action<string, ImmutableDictionary<string, ImmutableArray<PredictorKnowledgeFactId>>, bool> publish, bool hypothetical = false)
        {
            _facts = facts; _publish = publish; Node = node; Hypothetical = hypothetical;
            _prefix = "event.compute." + scope + "." + Guid.NewGuid().ToString("N");
            Identity = PredictorRouteCauseEvidence.Key(facts.ForecastId.ToString("N"), node, _prefix, "0");
        }
        internal EventTraceBranch Fork(string scope, PredictorRouteFactRecorder facts)
        {
            var fork = new EventTraceBranch(facts, Node, scope, _publish, hypothetical: true)
            { _rngCauses = _rngCauses, _sourceDependencies = _sourceDependencies };
            fork._groups["seed"] = _groups.GetValueOrDefault("seed", []);
            return fork;
        }
        internal void ReplayedBoundary(PredictorRun run)
        {
            var prefix = Value("input.replayedPrefix", string.Join(" | ", run._nodeReplayInputs.Select(InputText)),
                "independent event branch: accepted prefix replay", origin: PredictorKnowledgeOrigin.Derived);
            RngBoundary(run, "independent event branch: actual local cursor after replay", _groups.GetValueOrDefault("seed", []).Add(prefix));
        }
        private ImmutableArray<PredictorKnowledgeFactId> Unresolved(IEnumerable<PredictorKnowledgeFactId> ids) =>
            ids.Where(id => _facts.Fact(id).Status != PredictorKnowledgeStatus.Known).Distinct().ToImmutableArray();
        private PredictorKnowledgeFactId Value<T>(string property, T value, string operation,
            ImmutableArray<PredictorCombatPendingCause> causes = default, ImmutableArray<PredictorKnowledgeFactId> reads = default,
            PredictorKnowledgeOrigin origin = PredictorKnowledgeOrigin.Derived, bool propagate = true, string? proof = null) =>
            _facts.RecordResult(_prefix + "." + (++_operation) + "." + property, Node, value, causes, origin, operation,
                reads, propagate && !reads.IsDefault ? Unresolved(reads) : [], proof);
        private void Publish()
        {
            Identity = PredictorRouteCauseEvidence.Key(_facts.ForecastId.ToString("N"), Node, _prefix, _operation.ToString(CultureInfo.InvariantCulture));
            _publish(Identity, _groups.ToImmutableDictionary(StringComparer.Ordinal), Hypothetical);
        }
        private void Group(string key, params PredictorKnowledgeFactId[] ids) => _groups[key] = ids.ToImmutableArray();
        internal ImmutableArray<PredictorKnowledgeFactId> DomainReads(PredictorRun run, PredictorCombatDomain domains, List<RouteDependency> dependencies)
        {
            var reads = ImmutableArray.CreateBuilder<PredictorKnowledgeFactId>();
            void Input<T>(string key, T value, PredictorCombatDomain domain) => reads.Add(Value("input." + key, value,
                "event existing guard: domain input", CausesFor(dependencies, domain)));
            if ((domains & PredictorCombatDomain.Gold) != 0) Input("gold.balance", run.Working.GoldBudget, PredictorCombatDomain.Gold);
            if ((domains & PredictorCombatDomain.Deck) != 0) Input("deck.orderedCandidates", string.Join(" | ", run.Working.Deck
                .Select(id => CardDiagnostic(run.Working.Card(id)) + " #" + id)), PredictorCombatDomain.Deck);
            if ((domains & PredictorCombatDomain.Relics) != 0) Input("relics.orderedInventory", string.Join(" | ", run.Working.Relics
                .Where(relic => !relic.Melted).Select(relic => relic.Key.Serialized + " #" + relic.Id)), PredictorCombatDomain.Relics);
            if ((domains & PredictorCombatDomain.PotionInventory) != 0)
            {
                if (run._eventPotionInput is { } input)
                    reads.Add(Value(input.OrderedSlots.IsDefault ? "input.potions.emptySlots" : "input.potions.localHypothesis",
                        input.OrderedSlots.IsDefault ? "emptySlots=" + input.EmptySlots :
                        string.Join(" | ", run.EventPotionSlots.Select((potion, slot) => slot + ":" + (potion?.Key.Serialized ?? "empty"))),
                        "event-local potion hypothesis; no persistent inventory identity", origin: PredictorKnowledgeOrigin.PlayerInput));
                else Input("potions.slots", string.Join(" | ", run.Working.PotionSlots
                    .Select((potion, slot) => slot + ":" + (potion == null ? "empty" : potion.Key.Serialized + " #" + potion.Id))), PredictorCombatDomain.PotionInventory);
            }
            if ((domains & PredictorCombatDomain.Health) != 0 && run._eventHealth is { } health)
                Input("health", health.CurrentHp + "/" + health.MaxHp, PredictorCombatDomain.Health);
            if ((domains & PredictorCombatDomain.InstanceIds) != 0) Input("targetIdentities", string.Join(" | ", run.EventOptions
                .Select(option => option.Key + ":" + option.CardTargetId + ":" + option.PotionId + ":" + option.RelicId)), PredictorCombatDomain.InstanceIds);
            foreach (var cursor in run.Working.Streams.Where(cursor => (domains & StreamKnowledgeDomain(cursor.Stream)) != 0))
            {
                Input("rng." + cursor.Stream + ".calls", cursor.Calls, StreamKnowledgeDomain(cursor.Stream));
                Input("rng." + cursor.Stream + ".state", RngState(cursor), StreamKnowledgeDomain(cursor.Stream));
            }
            return reads.ToImmutable();
        }
        internal void Seed(PredictorRun run, ModelKey key)
        {
            _sourceDependencies = (run._preview.Partial?.EventTraceDependencies ?? []).ToImmutableArray();
            var seed = Value("input.seed", run.Context.Seed, "BeginEvent: ComputeRootSeed", origin: PredictorKnowledgeOrigin.Captured);
            var model = Value("input.event", key.Serialized, "BeginEvent: event.Entry seed discriminator");
            var player = Value("input.playerOrdinal", 0, "BeginEvent: approved local player ordinal", origin: PredictorKnowledgeOrigin.ApprovedDefault);
            var initial = Capture(run); if (initial == null) return;
            var state = Value("rng.eventLocal.initialState", RngState(initial), "BeginEvent: DeriveEventStreamSeed / Xoshiro initialization", reads: [seed, model, player]);
            _lastRng = initial; Group("seed", seed, model, player); Group("localRng", state); Group("selection", model); Publish();
        }
        internal void Setup(PredictorRun run, List<RouteDependency> dependencies)
        {
            _sourceDependencies = dependencies.ToImmutableArray();
            foreach (var option in run.EventOptions)
                _rngCauses = PredictorRouteCauseEvidence.Normalize(_rngCauses.AddRange(CausesFor(dependencies, RouteEventOptionIdentityDomains(run, option))));
            RngBoundary(run, "BeginEvent: option setup completed", _groups.GetValueOrDefault("selection", []));
        }
        internal void RngBoundary(PredictorRun run, string operation, ImmutableArray<PredictorKnowledgeFactId> reads = default)
        {
            var current = Capture(run); if (current == null) return;
            var old = _lastRng; var combined = _groups.GetValueOrDefault("localRng", []).AddRange(_selection).AddRange(reads.IsDefault ? [] : reads).Distinct().ToImmutableArray();
            var unresolved = Unresolved(combined).Where(id => !_facts.Fact(id).PropertyKey.EndsWith(".input.gold.balance", StringComparison.Ordinal)).ToImmutableArray();
            PredictorKnowledgeFactId RngValue<T>(string property, T value) => _facts.RecordResult(_prefix + "." + (++_operation) + "." + property,
                Node, value, _rngCauses, operation: operation, allReadFactIds: combined, unresolvedReadFactIds: unresolved,
                derivation: "The accepted choice and its source guard govern local draws; a numeric Gold balance input only affects the Gold write.");
            var calls = RngValue("rng.eventLocal.calls", current.Calls);
            var state = RngValue("rng.eventLocal.state", RngState(current));
            if (old != null)
            {
                var consumed = RngValue("rng.eventLocal.consumed", current.Calls - old.Calls);
                Group("localRng", calls, state, consumed);
            }
            else Group("localRng", calls, state);
            _lastRng = current; Publish();
        }
        private static PredictorStreamState? Capture(PredictorRun run) => run._eventRng == null ? null : PredictorStreamState.Capture(PredictorStream.Niche, run._eventRng);
        private static string RngState(PredictorStreamState state) => $"{state.S0:x16}/{state.S1:x16}/{state.S2:x16}/{state.S3:x16}";
        internal string OptionGuard(PredictorRun run, PredictorEventOption option, PredictorEventOptionKnowledge observation,
            PredictorCombatDomain availabilityDomains, List<RouteDependency> dependencies)
        {
            // The typed reader has already established the actual predicate and
            // its unresolved inputs. Keep source audit edges without repeating
            // a whole-domain dependency inference over the execution witness.
            var identityReads = observation.IdentityReadFactIds.AddRange(_groups.GetValueOrDefault("localRng", []));
            var identity = Value("option." + option.Key + ".identity", OptionText(option),
                "event actual option identity over typed inputs", observation.IdentityPendingCauses, identityReads,
                propagate: false, proof: "IdentityPendingCauses is established by the actual event option reader.");
            var enabled = Value("option." + option.Key + ".enabled", observation.Enabled ?? option.Enabled,
                "event actual availability predicate over typed inputs", observation.AvailabilityPendingCauses,
                observation.AvailabilityReadFactIds.Add(identity), propagate: false,
                proof: "The actual predicate may be known from a guaranteed eligible subset even when the full population remains unresolved.");
            Group("identity", identity); Group("guard", enabled); Group("stage", identity, enabled); Publish(); return Identity;
        }
        internal ImmutableArray<PredictorKnowledgeFactId> Action(PredictorRun run, PredictorInput input,
            PredictorCombatDomain domains, List<RouteDependency> dependencies)
        {
            _sourceDependencies = dependencies.ToImmutableArray();
            _actionGoldBefore = run.Working.GoldBudget;
            var choiceOrigin = Hypothetical ? PredictorKnowledgeOrigin.Derived : input is ChooseEventOccurrence
                ? run._preview.OccurrenceOrigin : PredictorKnowledgeOrigin.PlayerInput;
            var choice = Value("input.action", InputText(input), "event authored action, approved occurrence default, or independent option hypothesis", origin: choiceOrigin);
            var reads = DomainReads(run, domains, dependencies);
            if (input is ChooseEventOccurrence) reads = reads.AddRange(_groups.GetValueOrDefault("occurrence", []));
            var guard = Value("action.guard", true, "event actual binding/availability/dependency guards passed", reads: reads.Add(choice),
                propagate: false, proof: "The semantic gate has passed using its specific inputs; broader inventory snapshots are audit context.");
            if (input is ChooseEventOption selected && run.EventOptions.FirstOrDefault(option => option.Key == selected.OptionKey) is { } actual)
            {
                reads = reads.Add(Value("input.selectedOption", OptionText(actual), "event actual bound option payload", reads: [choice, guard]));
                if (actual.GoldCost.HasValue || actual.GoldGain.HasValue) reads = reads.AddRange(DomainReads(run, PredictorCombatDomain.Gold, dependencies));
            }
            _selection = [choice, guard]; Group("selection", choice); Group("guard", guard);
            if (input is SelectEventCards cards) Group("targets", Value("input.targets", string.Join(" | ", cards.CardIds), "event validated target selection", reads: [choice, guard], origin: Hypothetical ? PredictorKnowledgeOrigin.Derived : PredictorKnowledgeOrigin.PlayerInput));
            if (input is ProvideEventHealthFact health) Group("health", Value("input.health", health.Fact.CurrentHp + "/" + health.Fact.MaxHp, "event authored health observation", reads: [choice, guard], origin: choiceOrigin));
            if (input is ProvideConveyorInputs conveyor)
                Group("health", Value("conveyor.input.lostHp", conveyor.Fact.LostHp,
                    "Conveyor local missing HP; no current/max HP observation", reads: [choice, guard], origin: choiceOrigin));
            Publish(); return _selection.AddRange(reads);
        }
        internal void Stage(PredictorRun run, List<RouteDependency> dependencies, ImmutableArray<PredictorKnowledgeFactId> actionReads = default, bool zeroGold = false)
        {
            _sourceDependencies = dependencies.ToImmutableArray();
            var reads = _selection.AddRange(actionReads.IsDefault ? [] : actionReads).Distinct().ToImmutableArray();
            var phase = Value("stage", RouteEventNeededInput(run), "event accepted source handler: control boundary", reads: reads, propagate: false,
                proof: "This is the reached control boundary; it does not assert that an unresolved successor has executed.");
            Group("stage", phase);
            var goldReads = DomainReads(run, PredictorCombatDomain.Gold, dependencies).AddRange(_groups.GetValueOrDefault("gold", []));
            var gold = Value("gold.balance", run.Working.GoldBudget, zeroGold ? "Morphic GROUP: assign zero gold" : "event balance after source boundary",
                zeroGold ? [] : CausesFor(dependencies, PredictorCombatDomain.Gold), reads.AddRange(goldReads), propagate: false,
                proof: zeroGold ? "Morphic GROUP assigns zero; its result is independent of the incoming balance." :
                    "Only the existing Gold dependency mask controls this balance; non-Gold guard reads concern other successors.");
            Group("gold", gold); RngBoundary(run, "event source handler: local RNG boundary", reads); Publish();
            if (!Hypothetical && !actionReads.IsDefaultOrEmpty && (zeroGold || _actionGoldBefore != run.Working.GoldBudget))
            { Group("shared.gold.write", gold); Publish(); }
        }
        internal ImmutableArray<PredictorKnowledgeFactId> ConveyorPool(PredictorRun run, IReadOnlyList<(string Name, float Weight)> dishes)
        {
            var potions = DomainReads(run, PredictorCombatDomain.PotionInventory, _sourceDependencies.ToList());
            var health = run._conveyorLostHp is { } lost
                ? Value("conveyor.input.lostHp", lost, "RollConveyorDish: actual missing HP > 0 test",
                    reads: _groups.GetValueOrDefault("health", []))
                : Value("conveyor.input.health", run._eventHealth!.CurrentHp + "/" + run._eventHealth.MaxHp,
                    "RollConveyorDish: current != maximum health test", reads: _groups.GetValueOrDefault("health", []));
            var count = Value("conveyor.input.grabs", run._conveyorGrabs, "RollConveyorDish: modulo-five and subsequent-dish tests", reads: _selection);
            var last = Value("conveyor.input.lastDish", run._conveyorLastDish ?? "none", "RollConveyorDish: exclude previous dish", _rngCauses);
            var reads = potions.AddRange(ImmutableArray.Create(health, count, last)).AddRange(_groups.GetValueOrDefault("localRng", []));
            var pool = Value("conveyor.weightedPool", string.Join(" | ", dishes.Select(dish => dish.Name + ":" + dish.Weight.ToString(CultureInfo.InvariantCulture))),
                "RollConveyorDish: actual weighted candidates after slot/health/history guards", reads: reads);
            Group("conveyor", pool); Publish(); return [pool];
        }
        internal void MorphicGoldBefore(PredictorRun run)
        {
            var before = Value("morphic.input.goldBefore", run.Working.GoldBudget, "Morphic GROUP: old balance read for EventGoldStolen record",
                CausesFor(_sourceDependencies.ToList(), PredictorCombatDomain.Gold));
            Group("gold", before); Publish();
        }
        internal void DefaultCardTarget(PredictorCard target)
        {
            Group("targets", Value("conveyor.default.basicTarget", CardDiagnostic(target),
                "Owner Conveyor convention: first known eligible Basic card in deck order; an explicit target is applied first",
                origin: PredictorKnowledgeOrigin.ApprovedDefault));
            Publish();
        }
        internal void DefaultFakeMerchant()
        {
            var choice = Value("input.defaultNoPurchaseOrCombat", "No purchases or combat",
                "Owner Fake Merchant convention: continue without buying or fighting, including when a Foul Potion is available; explicit actions take precedence",
                origin: PredictorKnowledgeOrigin.ApprovedDefault);
            _selection = [choice];
            Group("selection", choice);
            Publish();
        }
        internal void PartialLocalDeckOperation(PredictorCombatPendingCause cause)
        {
            _rngCauses = PredictorRouteCauseEvidence.Normalize(_rngCauses.Add(cause));
            var effect = Value("deck.localRandomOperation", "unresolved targets", "event-local conditional deck operation",
                [cause]);
            Group("targets", effect);
            Publish();
        }
        internal void PickerGuard(PredictorRun run, ImmutableArray<long> candidates, int requested, int guaranteed, List<RouteDependency> dependencies)
        {
            var input = DomainReads(run, PredictorCombatDomain.Deck, dependencies);
            var causes = CausesFor(dependencies, PredictorCombatDomain.Deck);
            PredictorKnowledgeFactId population;
            string property = _prefix + "." + (++_operation) + ".picker.population";
            if (causes.IsEmpty)
                population = _facts.RecordResult(property, Node, string.Join(" | ", candidates), operation: "RouteEventPicker: actual ordered candidate subset", allReadFactIds: input);
            else population = _facts.RecordPartial(property, Node, PredictorRouteFactPhase.Results,
                "guaranteed candidates=" + guaranteed + "; requested=" + requested, causes,
                [new("subset-kind", "only targets accepted by IsKnownRouteDeckTarget")], operation: "RouteEventPicker: proven candidate lower bound",
                allReadFactIds: input, unresolvedReadFactIds: Unresolved(input));
            var threshold = Value("picker.guaranteedAboveAutoThreshold", guaranteed > requested,
                "RouteEventActionReadDomains: guaranteed candidates > requested prevents automatic target selection",
                reads: [population], propagate: false,
                proof: "The count is a proven guaranteed subset. This predicate does not assert a complete candidate list or an actual unchosen target.");
            Group("targets", population); Group("guard", threshold); Publish();
        }
        internal void ConveyorDish(PredictorRun run, ImmutableArray<PredictorKnowledgeFactId> poolReads, bool fixedFifth = false)
        {
            var reads = fixedFifth ? ImmutableArray.Create(Value("conveyor.input.grabs", run._conveyorGrabs,
                "RollConveyorDish: modulo-five fixed dish guard", reads: _selection)) : poolReads;
            var dish = Value("conveyor.dish", run._conveyorDish!, fixedFifth ? "RollConveyorDish: fixed fifth dish" :
                "RollConveyorDish: NextFloat * total weight / cumulative threshold", reads: reads);
            Group("conveyor", dish); RngBoundary(run, "RollConveyorDish: event-local draw completed", [dish]); Publish();
        }
        internal ImmutableArray<PredictorKnowledgeFactId> TransformInputs(PredictorRun run, ImmutableArray<long> selected)
        {
            var target = Value("transform.input.targets", string.Join(" | ", selected.Select(id => CardDiagnostic(run.Working.Card(id)) + " #" + id)),
                "event Transform: actual validated source targets", reads: _selection);
            var definitions = selected.Select(id => PredictorCardChanges.Definition(run.Context, run.Working.Card(id).Key)).ToImmutableArray();
            var definition = Value("transform.input.definitions", string.Join(" | ", definitions.Select(card =>
                card.Prototype.Key.Serialized + ":" + card.Rarity + ":" + card.TransformationPool.Serialized)),
                "event Transform: actual target definitions / transformation pool keys", reads: [target]);
            var pool = Value("transform.input.orderedPools", string.Join(" | ", definitions.Select(card => card.TransformationPool).Distinct()
                .Select(key => key.Serialized + "=" + string.Join(",", run.Context.Catalog!.TransformationPools.Single(pool => pool.Key == key).Cards.Select(card => card.Serialized)))),
                "event Transform: actual referenced ordered transformation pools", reads: [definition], origin: PredictorKnowledgeOrigin.Captured);
            return ImmutableArray.Create(target, pool).AddRange(_groups.GetValueOrDefault("localRng", []));
        }
        internal void TransformWrite(PredictorRun run, ImmutableArray<PredictorKnowledgeFactId> reads)
        {
            RngBoundary(run, "PredictorCardChanges.Transform: substitute event-local RNG and restore public Niche", reads);
            Group("targets", Value("transform.output.deck", string.Join(" | ", run.Working.Deck.Select(id => CardDiagnostic(run.Working.Card(id)) + " #" + id)),
                "event Transform: returned detached deck", CausesFor(_sourceDependencies.ToList(), PredictorCombatDomain.Deck), reads));
            Publish();
        }
        internal void Occurrence(PredictorRun run, PredictorEventOccurrenceObservation value, ImmutableArray<PredictorCombatPendingCause> replacementCauses)
        {
            var act = run.Working.Acts.Single(act => act.Act == run.Working.Position.Act);
            var queue = Value("occurrence.rawQueue", string.Join(" | ", act.RawEvents.Select(key => key.Serialized)), "ObserveEventOccurrence: ordered raw queue read");
            var cursor = Value("occurrence.cursor", act.EventCursor, "ObserveEventOccurrence: current cursor read");
            var examined = Value("occurrence.examined", run._eligibilityExamined, "ObserveEventOccurrence: raw-cycle limit read");
            var visited = Value("occurrence.visited", string.Join(" | ", run.Working.VisitedEvents.Select(key => key.Serialized)), "ObserveEventOccurrence: visited membership read");
            var result = Value("occurrence.candidates", string.Join(" | ", value.Candidates.Select(candidate => candidate.QueueEvent.Serialized + "→" + candidate.Event.Serialized + "@" + candidate.Cursor + ":" + candidate.EligibilityKind)),
                "ObserveEventOccurrence: static rejection / visited skip / first unconditional stop / raw-cycle fallback", replacementCauses,
                reads: ImmutableArray.Create(queue, cursor, examined, visited).AddRange(_groups.GetValueOrDefault("eligibility", [])));
            Group("occurrence", result); Group("stage", result); Publish();
        }
        internal void StaticEligibility(PredictorRun run, ModelKey key, string result)
        {
            var source = run.Context.AncientAuthority;
            var model = Value("eligibility.event", key.Serialized, "EventOccurrenceStaticEligibility: candidate identity");
            var act = Value("eligibility.act", run.Working.Position.Act, "EventOccurrenceStaticEligibility: act mask input");
            var immutable = Value("eligibility.immutableMode", source == null ? "unknown mode context" :
                "modeExact=" + source.ModeFactsExact + "; mode=" + (source.ModeFactsExact ? source.GameMode.ToString() : "not observed") +
                "; players=" + (source.ModeFactsExact ? source.PlayerCount.ToString(CultureInfo.InvariantCulture) : "not observed") +
                "; cardPoolExact=" + source.EventAuthority.CharacterCardPoolCountExact + "; cardPoolCount=" +
                (source.EventAuthority.CharacterCardPoolCountExact ? source.EventAuthority.CharacterCardPoolCount.ToString(CultureInfo.InvariantCulture) : "not observed"),
                "EventOccurrenceStaticEligibility: explicit exactness flags and immutable authority fields");
            var rule = Value("eligibility.rule", result, "EventStaticEligibilityCatalog.Evaluate: Beta111 Shared static predicate", reads: [model, act, immutable]);
            _groups["eligibility"] = _groups.GetValueOrDefault("eligibility", []).Add(rule); Publish();
        }
        private static string OptionText(PredictorEventOption option) => option.Key + "; goldCost=" + option.GoldCost + "; goldGain=" + option.GoldGain + "; card=" + option.CardTargetId + "; potion=" + option.PotionId + "; relic=" + option.RelicId + "; nominalDamage=" + option.HealthDamage + "; nominalHeal=" + option.HealthHeal;
        private static string InputText(PredictorInput input) => input switch
        {
            ChooseEventOption option => "option:" + option.OptionKey,
            SelectEventCards cards => "targets:" + string.Join(" | ", cards.CardIds),
            ProvideEventHealthFact health => "health:" + health.Fact.CurrentHp + "/" + health.Fact.MaxHp,
            ProvideConveyorInputs conveyor => "conveyor:" + conveyor.Fact.EmptyPotionSlots + "/lost:" + conveyor.Fact.LostHp,
            ChooseEventOccurrence occurrence => "occurrence:" + occurrence.Event.Serialized + "@" + occurrence.SelectedCursor,
            _ => input.GetType().Name
        };
    }
}
