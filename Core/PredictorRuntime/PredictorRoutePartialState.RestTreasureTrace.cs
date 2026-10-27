using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // Installed only in detached route projection. These tokens observe model
    // branch inputs/results; they never feed values back into calculation.
    private RoutePartialState.RoomCalculation? _restPlanTrace, _restModelTrace, _treasurePlanTrace;

    private sealed partial class RoutePartialState
    {
        private int _roomCalculationOccurrence;
        private readonly Dictionary<(string Node, string Field), ImmutableArray<PredictorKnowledgeFactId>> _restCompute = [];
        private readonly Dictionary<(string Node, string Field), ImmutableArray<PredictorKnowledgeFactId>> _treasureCompute = [];
        internal ImmutableArray<PredictorKnowledgeFactId> RestResultReads(string node, string field) =>
            _restCompute.GetValueOrDefault((node, field), []);
        internal ImmutableArray<PredictorKnowledgeFactId> TreasureResultReads(string node, string field) =>
            _treasureCompute.GetValueOrDefault((node, field), []);

        private ImmutableArray<PredictorKnowledgeFactId> RoomLatest(params string[] keys) =>
            keys.Where(_latest.ContainsKey).Select(key => _latest[key]).Distinct().ToImmutableArray();
        private ImmutableArray<PredictorCombatPendingCause> RoomRelics() =>
            Domain(PredictorCombatDomain.Relics).Where(c => c.Code != "FirstPlayerTurn").ToImmutableArray();
        private RoomCalculation RoomCalculationFor(string node, string room, string operation) =>
            new(this, node, room, operation, ++_roomCalculationOccurrence);

        internal RoomCalculation BeginRestPlan(PredictorRun run, PredictorPlannedRestAction step, bool authored = true)
        {
            var trace = RoomCalculationFor(run.Working.Position.NodeId, "rest", "route action binding");
            trace.InputOrigin = authored ? PredictorKnowledgeOrigin.PlayerInput : PredictorKnowledgeOrigin.ApprovedDefault;
            trace.Input("action", step.Action, trace.InputOrigin);
            return trace;
        }

        internal RoomCalculation BeginRestCommand(PredictorRun run, PredictorInput input)
        {
            RecordEntryCoverage(run.Working.Position.NodeId, "rest", _restCompute.ContainsKey((run.Working.Position.NodeId, "options")));
            var trace = RoomCalculationFor(run.Working.Position.NodeId, "rest", "SubmitRest " + input.GetType().Name);
            var origin = run._restPlanTrace?.InputOrigin ?? (run._preview.RestDefaultInProgress ?
                PredictorKnowledgeOrigin.ApprovedDefault : PredictorKnowledgeOrigin.Derived);
            trace.Input("command", input.GetType().Name, origin);
            trace.Read("phase", run.Phase);
            if (input is SelectRestAction selected)
            {
                trace.Input("action", selected.Action, origin);
                trace.Read("optionPresent", run.RestOptions.Contains(selected.Action), RoomRelics(),
                    RestResultReads(trace.Node, "options"), selected.Action == PredictorRestAction.Hatch ? null :
                        "This concrete non-Hatch option's membership follows its base/modifier or relic listener and accepted rest actions. Uncertain Byrdonis Egg supplier multiplicity changes Hatch instances only.");
            }
            if (input is ProvideRestMaxHpFact fact)
                trace.Input("maximumHp", fact.MaxHp, PredictorKnowledgeOrigin.PlayerInput);
            return trace;
        }

        internal void RecordRestOptions(PredictorRun run)
        {
            // Route dispatch temporarily uses rest:act:floor inside the model;
            // the accepted map occurrence is already bound on _routeNode.
            var trace = RoomCalculationFor(run._routeNode?.NodeId ?? run.Working.Position.NodeId, "rest", "EnterRestSite option generation");
            trace.Read("modifier.MIDAS", run.Context.HasRunModifier("MIDAS"));
            var hatchCauses = PredictorRouteCauseEvidence.Normalize(_dependencies.Where(d =>
                RouteCardMayVary(run, [d], "BYRDONIS_EGG")).Select(d => d.Cause));
            trace.Read("cards.byrdonisEgg.suppliers", string.Join(" | ", run._restHatchSuppliers), hatchCauses,
                RoomLatest("deck.All.candidates"), hatchCauses.IsEmpty ?
                    "No dependency may change Byrdonis Egg membership; optional additions of other card models cannot change these Hatch suppliers." : null);
            foreach (string key in new[] { "GIRYA", "SHOVEL", "MEAT_CLEAVER", "PAELS_GROWTH", "PUMPKIN_CANDLE" })
                trace.Read("relics.option." + key, PredictorSettlementEffects.Has(run.Working, key), RoomRelics(),
                    RoomLatest("relics.orderedInventory"));
            var girya = run.Working.Relics.FirstOrDefault(r => !r.Melted && r.Key.Entry == "GIRYA");
            if (girya != null) trace.Read("relics.girya.timesLifted", girya.TimesLifted, RoomRelics(), RoomLatest("relics.girya.timesLifted"));
            trace.Write("options", string.Join(" | ", run.RestOptions), RoomRelics().AddRange(hatchCauses), alias: "options");
        }

        internal void RecordRestQualification(PredictorRun run, PredictorRestAction action, bool actual)
        {
            var trace = RoomCalculationFor(run.Working.Position.NodeId, "rest", "RestOffers qualification " + action);
            if (action is not (PredictorRestAction.Smith or PredictorRestAction.Cook))
            {
                trace.Write("enabled", actual, alias: "offer." + action + ".enabled",
                    proof: "This retained rest option has no deck qualification predicate in RestOffers.");
                return;
            }
            var scope = action == PredictorRestAction.Smith ? PredictorDeckProjectionScope.Upgradable : PredictorDeckProjectionScope.Removable;
            int threshold = action == PredictorRestAction.Smith ? 1 : 2;
            var projection = Deck.Project(scope);
            var sources = RoomLatest(DeckKey(scope) + ".count", DeckKey(scope) + ".candidates");
            bool sameDeck = run.Working.Deck.Select(run.Working.Card).SequenceEqual(_observed.Deck.Select(_observed.Card));
            bool? proven = sameDeck && projection.Count?.Bounds.Lower >= threshold ? true :
                sameDeck && projection.Count?.Bounds.Upper < threshold ? false : null;
            if (proven == actual)
                trace.Write("enabled", actual, reads: sources, alias: "offer." + action + ".enabled",
                    proof: "Deck.Project(" + scope + ").Count enclosure proves the threshold " + threshold +
                        " on every retained alternative, and agrees with the actual RestOffers predicate.");
            else
            {
                var causes = sameDeck ? ProjectionCauses(projection, count: true) : Domain(PredictorCombatDomain.Deck);
                if (proven != null && proven != actual) causes = causes.Add(PredictorRouteCauseEvidence.CreateExplicit(
                    "RestQualificationProjectionMismatch", trace.Node, PredictorCombatDomain.None, PredictorPendingReasonKind.ImplementationGap,
                    "The typed qualification proof disagrees with the ordinary RestOffers calculation.", ["rest.offer." + action + ".enabled"]));
                trace.Write("enabled", actual, causes, sources, "offer." + action + ".enabled");
            }
        }

        internal void RecordRestHealInputs(PredictorRun run, bool mimicked, int? maxHp)
        {
            var trace = run._restModelTrace;
            if (trace == null) return;
            trace.Read("heal.isMimicked", mimicked);
            trace.Read("heal.stoneHumidifier.count", run.Working.Relics.Count(r => !r.Melted && r.Key.Entry == "STONE_HUMIDIFIER"), RoomRelics());
            bool night = run.Context.HasRunModifier("NIGHT_TERRORS");
            trace.Read("heal.modifier.NIGHT_TERRORS", night);
            if (night)
            {
                if (maxHp is { } hp) trace.Read("heal.maximumHp", hp, reads: RestResultReads(trace.Node, "maximumHp").TakeLast(1).ToImmutableArray());
                else trace.Unknown("heal.maximumHp", [RouteCause("RestMaximumHp", trace.Node, PredictorCombatDomain.Health)]);
            }
            else trace.Read("heal.regalPillow.count", run.Working.Relics.Count(r => !r.Melted && r.Key.Entry == "REGAL_PILLOW"), RoomRelics());
        }

        internal void RecordRestHealOutput(PredictorRun run, PredictorRestHealOutcome outcome)
        {
            var trace = run._restModelTrace;
            if (trace == null) return;
            trace.RestHealOutput(outcome);
            // No current/final HP is inferred: RestHealOutcome is nominal typed
            // metadata, and the independent simulator has no combat HP value.
        }

        internal RoomCalculation BeginTreasureGeneration(PredictorRun run, PredictorPosition target)
        {
            var trace = RoomCalculationFor(target.NodeId, "treasure", "EnterTreasure generation");
            trace.Read("silverCrucible.countersBefore", string.Join(" | ", run.Working.Relics.Where(r =>
                !r.Melted && r.Key.Entry == "SILVER_CRUCIBLE").Select(r => r.TreasureRoomsEntered)), RoomRelics(),
                RoomLatest("relics.silverCrucible.orderedTreasureRoomCounters"));
            return trace;
        }

        internal RoomCalculation BeginTreasureCommand(PredictorRun run, PredictorInput input)
        {
            var trace = RoomCalculationFor(run.Working.Position.NodeId, "treasure", "SubmitTreasure " + input.GetType().Name);
            trace.Input("command", input.GetType().Name, run._treasurePlanTrace == null ?
                PredictorKnowledgeOrigin.Derived : run._treasurePlanTrace.InputOrigin);
            trace.Read("phase", run.Phase);
            trace.Read("obtainInProgress", run._treasureObtaining);
            return trace;
        }

        internal RoomCalculation BeginTreasurePlan(PredictorRun run, PredictorPlannedTreasureAction action, bool authored)
        {
            var trace = RoomCalculationFor(run.Working.Position.NodeId, "treasure", "route action binding");
            trace.InputOrigin = authored ? PredictorKnowledgeOrigin.PlayerInput : PredictorKnowledgeOrigin.ApprovedDefault;
            trace.Input("action", action.Action, trace.InputOrigin);
            if (action.ExpectedRelic is { } key) trace.Input("expectedRelic", key.Serialized, trace.InputOrigin);
            return trace;
        }

        private void RecordEntryCoverage(string node, string room, bool instrumented)
        {
            if (instrumented) return;
            var gap = PredictorRouteCauseEvidence.CreateExplicit("RoomEntryCalculationNotRecorded", node,
                PredictorCombatDomain.None, PredictorPendingReasonKind.ImplementationGap,
                "This active room's entry calculation/replay preceded this forecast recorder. Current captured room observations remain available; its entry internals are not claimed as actual reads.",
                [room + ".entryCalculation"], occurrenceKey: room + ":entry");
            Facts.RecordUnknown(room + ".compute.entryCoverage", node, PredictorRouteFactPhase.Results, [gap],
                operation: room + " entry calculation coverage");
        }

        internal void RecordTreasureEntryCoverage(PredictorRun run) => RecordEntryCoverage(run.Working.Position.NodeId,
            "treasure", _treasureCompute.ContainsKey((run.Working.Position.NodeId, "generationAllowed")));

        internal void RecordTreasureExtraEmpty(PredictorRun run, int count)
        {
            var trace = RoomCalculationFor(run.Working.Position.NodeId, "treasure", "SettleTreasureExtraRewards return");
            trace.Read("initialPlans.count", 0);
            trace.Write("extraRewards.count", count, alias: "extraRewards",
                proof: "Generate was called with an empty TreasureRoom plan list; the maintained 0.111 treasure reward-list hooks add no rewards. Generator internals are not instrumented by this hook.");
        }

        internal sealed class RoomCalculation(RoutePartialState owner, string node, string room, string operation, int occurrence)
        {
            internal readonly string Node = node;
            private readonly string _prefix = room + ".compute." + occurrence;
            private readonly List<PredictorKnowledgeFactId> _reads = [];
            internal PredictorKnowledgeOrigin InputOrigin = PredictorKnowledgeOrigin.PlayerInput;
            private readonly Dictionary<string, PredictorKnowledgeFactId> _inputs = [];
            private ImmutableArray<PredictorKnowledgeFactId> Inputs(params string[] fields) =>
                fields.Where(_inputs.ContainsKey).Select(field => _inputs[field]).Distinct().ToImmutableArray();
            private ImmutableArray<PredictorKnowledgeFactId> Unresolved(IEnumerable<PredictorKnowledgeFactId> ids) =>
                ids.Where(id => owner.Facts.Fact(id).Status != PredictorKnowledgeStatus.Known).Distinct().ToImmutableArray();
            private void Alias(string field, PredictorKnowledgeFactId id)
            {
                var target = room == "rest" ? owner._restCompute : owner._treasureCompute;
                target[(Node, field)] = target.GetValueOrDefault((Node, field), []).Add(id);
            }
            internal void Input<T>(string field, T value, PredictorKnowledgeOrigin origin)
            {
                var id = owner.Facts.RecordResult(_prefix + ".input." + field, Node, value, origin: origin, operation: operation + " input");
                _reads.Add(id); _inputs[field] = id;
                if (field == "maximumHp") Alias("maximumHp", id);
            }
            internal PredictorKnowledgeFactId Read<T>(string field, T value,
                ImmutableArray<PredictorCombatPendingCause> causes = default,
                ImmutableArray<PredictorKnowledgeFactId> reads = default, string? proof = null)
            {
                if (reads.IsDefault) reads = [];
                var id = owner.Facts.RecordResult(_prefix + ".read." + field, Node, value, causes,
                    operation: operation + " read " + field, allReadFactIds: reads,
                    unresolvedReadFactIds: proof == null ? Unresolved(reads) : [], derivation: proof);
                _reads.Add(id); _inputs[field] = id; return id;
            }
            internal void Unknown(string field, ImmutableArray<PredictorCombatPendingCause> causes)
            {
                if (causes.IsDefaultOrEmpty) causes = [PredictorRouteCauseEvidence.CreateExplicit("RoomCalculationDerivationMissing", Node,
                    PredictorCombatDomain.None, PredictorPendingReasonKind.ImplementationGap,
                    "The model read has neither an established value nor a proven dependency.", [room + "." + field], occurrenceKey: _prefix + field)];
                _reads.Add(owner.Facts.RecordUnknown(_prefix + ".read." + field, Node, PredictorRouteFactPhase.Results,
                    causes, operation: operation + " unresolved " + field));
            }
            internal PredictorKnowledgeFactId Write<T>(string field, T value,
                ImmutableArray<PredictorCombatPendingCause> causes = default,
                ImmutableArray<PredictorKnowledgeFactId> reads = default, string? alias = null, string? proof = null, string? description = null)
            {
                if (reads.IsDefault) reads = _reads.Distinct().ToImmutableArray();
                var id = owner.Facts.RecordResult(_prefix + ".write." + field, Node, value, causes,
                    operation: operation + " write " + field, allReadFactIds: reads,
                    unresolvedReadFactIds: proof == null ? Unresolved(reads) : [], derivation: proof ?? description);
                if (alias != null) Alias(alias, id);
                _inputs[field] = id;
                return id;
            }
            internal void Return(PredictorRun run, PredictorInputResult result)
            {
                Write("return", result, proof: "Actual model Submit return; it records execution control flow rather than the truth of unresolved state fields.");
            }
            internal void PlanReturn(bool applied, ImmutableArray<PredictorCombatPendingCause> causes)
            {
                Write("projectionApplied", applied, proof: "Return from the route action binding gate; uninstrumented child internals are not included.");
                if (!applied) Unknown("routePrerequisites", causes);
            }
            internal void BindRestTarget(PredictorRun run, PredictorPlannedCard? target, bool known,
                ImmutableArray<PredictorCombatPendingCause> causes)
            {
                if (!known || target == null) { Unknown("target.binding", causes); return; }
                Read("target." + target.InstanceId + ".identity", target.InstanceId + ":" + target.Key.Serialized + ":" + target.FloorAdded,
                    reads: owner.RoomLatest("deck.All.candidates"),
                    proof: "ApplyRouteRestAction.KnownTarget verified membership, authored instance/model/floor, established instance identity, and absence of an unresolved existing-card mutation covering this fixed target.");
            }
            internal void ReadRestCard(PredictorRun run, long id, bool smith)
            {
                var card = run.Working.Card(id);
                var bindings = run._restPlanTrace != null && run._restPlanTrace._inputs.TryGetValue("target." + id + ".identity", out var bound) ?
                    ImmutableArray.Create(bound) : ImmutableArray<PredictorKnowledgeFactId>.Empty;
                var causes = owner.Domain(PredictorCombatDomain.Deck).Where(c => owner._dependencies.Any(d =>
                    d.Cause.Identity == c.Identity && d.MayChangeExistingCards &&
                    (d.AnyExistingCard || d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(p => p.Id == id)))).ToImmutableArray();
                Read("target." + id + ".identity", card.Id + ":" + card.Key.Serialized + ":" + card.FloorAdded, causes, bindings,
                    proof: "The model operates on a route-bound fixed target; additive reward choices do not change that existing card.");
                if (smith)
                {
                    Read("target." + id + ".upgradeLevel", card.UpgradeLevel, causes, bindings);
                    Read("target." + id + ".maxUpgradeLevel", card.MaxUpgradeLevel, causes, bindings);
                }
                else Read("target." + id + ".removable", card.Removable, causes, bindings);
            }
            internal void RestQualification(PredictorRun run, PredictorRestAction action)
            {
                bool actual = action == PredictorRestAction.Smith ? run.Working.Deck.Any(id =>
                    run.Working.Card(id).UpgradeLevel < run.Working.Card(id).MaxUpgradeLevel) :
                    run.Working.Deck.Count(id => run.Working.Card(id).Removable) >= 2;
                owner.RecordRestQualification(run, action, actual);
                foreach (var fact in owner.RestResultReads(Node, "offer." + action + ".enabled")) _reads.Add(fact);
            }
            internal void Relic(PredictorRun run, string key, string field, int value) =>
                Read("relics." + key + "." + field, value, owner.RoomRelics(),
                    owner.RoomLatest(key == "GIRYA" ? "relics.girya.timesLifted" : "relics.pumpkinCandle.kindleCount"));
            internal void RestCounterAfter(string field, int value, PredictorRestAction action, string canonical)
            {
                var id = Write(field, value, alias: "action." + action);
                owner.RecordStateWrite(Node, canonical, id);
            }
            internal void RestDeckAfter(PredictorRun run, PredictorRestAction action)
                => DeckAfter(run, "action." + action, "fixed-target rest edit");
            private void DeckAfter(PredictorRun run, string alias, string edit)
            {
                owner.Synchronize(run.Working);
                foreach (var scope in Enum.GetValues<PredictorDeckProjectionScope>())
                {
                    var projection = owner.Deck.Project(scope);
                    string canonical = DeckKey(scope);
                    var reads = _reads.Distinct().ToImmutableArray();
                    var countCauses = owner.ProjectionCauses(projection, count: true);
                    if (projection.KnownCount == null && countCauses.IsEmpty)
                        countCauses = [PredictorRouteCauseEvidence.CreateExplicit("RoomDeckCountDerivationMissing", Node,
                            PredictorCombatDomain.None, PredictorPendingReasonKind.ImplementationGap,
                            "The typed deck owner has not proved this post-calculation count or supplied its dependency.", [canonical + ".count"], occurrenceKey: _prefix + canonical)];
                    PredictorKnowledgeFactId count;
                    if (projection.KnownCount is { } exact)
                        count = Write(canonical + ".count", exact, alias: alias,
                            proof: "After the actual " + edit + ", Deck.Project(" + scope + ") proves this exact count on every retained reward alternative.");
                    else if (projection.Count is { } range)
                        count = owner.Facts.RecordPartial(_prefix + ".write." + canonical + ".count", Node, PredictorRouteFactPhase.Results,
                            Range(range), countCauses, [new("range-kind", "conservative enclosure")],
                            operation: operation + " typed deck count", allReadFactIds: reads,
                            derivation: "The typed deck owner's conservative enclosure applies after the actual " + edit + ".");
                    else count = owner.Facts.RecordUnknown(_prefix + ".write." + canonical + ".count", Node, PredictorRouteFactPhase.Results,
                        countCauses, operation: operation + " unresolved typed deck count", allReadFactIds: reads);
                    owner.RecordStateWrite(Node, canonical + ".count", count);
                    var candidateCauses = owner.ProjectionCauses(projection, count: false);
                    if (!projection.OrderedCandidatesKnown && candidateCauses.IsEmpty)
                        candidateCauses = [PredictorRouteCauseEvidence.CreateExplicit("RoomDeckCandidatesDerivationMissing", Node,
                            PredictorCombatDomain.None, PredictorPendingReasonKind.ImplementationGap,
                            "The typed deck owner has not proved the complete post-calculation candidate list or supplied its dependency.", [canonical + ".candidates"], occurrenceKey: _prefix + canonical)];
                    string candidates = string.Join(" | ", projection.KnownCandidates.Select(CardDiagnostic));
                    var identities = projection.OrderedCandidatesKnown ?
                        Write(canonical + ".candidates", candidates, alias: alias,
                            proof: "The typed deck owner proves the ordered model identities in this scope after the actual " + edit + "; this does not prove unrelated upgrade/floor fields.") :
                        owner.Facts.RecordPartial(_prefix + ".write." + canonical + ".candidates", Node, PredictorRouteFactPhase.Results,
                            candidates, candidateCauses, [new("interpretation", "Guaranteed model-identity subset in deck order.")],
                            operation: operation + " typed deck candidate subset", allReadFactIds: reads,
                            derivation: "The typed deck owner guarantees this model-identity subset after the actual " + edit + ".");
                    owner.RecordStateWrite(Node, canonical + ".candidates", identities);
                }
            }
            internal void DigBefore(PredictorRun run)
            {
                ReadStream(run, PredictorStream.Rewards);
                Read("bags.personal", Bag(run.Working.PersonalBag), owner.RoomRelics().AddRange(owner.Domain(PredictorCombatDomain.RelicRewards)),
                    owner.RoomLatest("bags.personal"));
            }
            internal void DigAfter(PredictorRun run, ModelKey pulled)
            {
                Write("dig.relic", pulled.Serialized, alias: "action.Dig",
                    description: "Actual PullRelic return with RNG/personal-bag boundaries. Delegated RelicAllowed predicate internals are not instrumented here.");
                var personal = Write("bags.personal.after", Bag(run.Working.PersonalBag));
                var shared = Write("bags.shared.after", Bag(run.Working.SharedBag));
                owner.RecordStateWrite(Node, "bags.personal", personal);
                owner.RecordStateWrite(Node, "bags.shared", shared);
                StreamAfter(run, PredictorStream.Rewards);
            }
            internal void ReadRestHealHooks(PredictorRun run)
            {
                foreach (string key in new[] { "DREAM_CATCHER", "TINY_MAILBOX" })
                    Read("heal.rewardHook." + key, run.Working.Relics.Count(r => !r.Melted && r.Key.Entry == key), owner.RoomRelics());
            }
            internal void RestHealOutput(PredictorRestHealOutcome outcome)
            {
                void Output<T>(string field, T value, ImmutableArray<PredictorKnowledgeFactId> reads) =>
                    Alias("heal", Write("heal." + field, value, reads: reads, alias: "heal." + field));
                var mode = Inputs("heal.modifier.NIGHT_TERRORS");
                var health = mode.AddRange(Inputs("heal.stoneHumidifier.count", "heal.isMimicked", "heal.maximumHp"));
                Output("baseMaxHpFraction", outcome.BaseMaxHpFraction, mode);
                Output("flatBonus", outcome.FlatHealBonus, outcome.OverrideHealAmount.HasValue ? mode :
                    mode.AddRange(Inputs("heal.regalPillow.count")));
                Output("maxHpDelta", outcome.MaxHpDelta, health);
                Output("maxHpLoss", outcome.MaxHpLoss, outcome.OverrideHealAmount.HasValue ? health : mode);
                Output("nominalOverride", outcome.OverrideHealAmount?.ToString() ?? "none",
                    mode.AddRange(Inputs("heal.maximumHp")));
            }
            internal void RestComplete(PredictorRun run, PredictorRestAction action)
            {
                bool tent = PredictorSettlementEffects.Has(run.Working, "MINIATURE_TENT");
                Read("miniatureTent.present", tent, owner.RoomRelics());
                var hatch = tent ? PredictorRouteCauseEvidence.Normalize(owner._dependencies.Where(d =>
                    RouteCardMayVary(run, [d], "BYRDONIS_EGG")).Select(d => d.Cause)) : [];
                Write("remainingOptions", string.Join(" | ", run.RestOptions), owner.RoomRelics().AddRange(hatch), alias: "action." + action);
            }
            internal void TreasureEligibility(PredictorRun run)
            {
                string counters = string.Join(" | ", run.Working.Relics.Where(r =>
                    !r.Melted && r.Key.Entry == "SILVER_CRUCIBLE").Select(r => r.TreasureRoomsEntered));
                var written = Write("silverCrucible.countersAfter", counters, owner.RoomRelics());
                owner.RecordStateWrite(Node, "relics.silverCrucible.orderedTreasureRoomCounters", written);
                Read("silverCrucible.countersAfter", counters, owner.RoomRelics(), [written]);
                Write("generationAllowed", run._treasureAllowed, owner.RoomRelics(), alias: "generationAllowed");
            }
            internal void ReadStream(PredictorRun run, PredictorStream stream)
            {
                var cursor = run.Working.Streams.Single(s => s.Stream == stream);
                Read("rng." + stream + ".state", $"{cursor.S0:X16}:{cursor.S1:X16}:{cursor.S2:X16}:{cursor.S3:X16}",
                    owner.StreamCauses(stream), owner.RoomLatest("rng." + stream + ".state"));
            }
            internal void ReadTreasureBag(PredictorRun run)
            {
                Read("bags.shared", Bag(run.Working.SharedBag), owner.RoomRelics().AddRange(owner.Domain(PredictorCombatDomain.RelicRewards)),
                    owner.RoomLatest("bags.shared"));
            }
            internal void ReadGorgetProbe(PredictorRun run)
            {
                Read("firstRun.numberOfRuns", run.Context.NumberOfRuns);
                if (run.Context.NumberOfRuns != 0) return;
                int count = run.RoomHistory.Count(v => v.Room == PredictorRoomKind.Treasure);
                Read("firstRun.treasureVisits", count);
                if (count == 1) Read("firstRun.gorgetInPersonalBag", run.Working.PersonalBag.Buckets.Any(b =>
                    b.Entries.Contains(new ModelKey("RELIC", "GORGET"))), owner.RoomRelics().AddRange(owner.Domain(PredictorCombatDomain.RelicRewards)));
            }
            internal void ReadGorgetBags(PredictorRun run)
            {
                Read("bags.personal", Bag(run.Working.PersonalBag), owner.RoomRelics().AddRange(owner.Domain(PredictorCombatDomain.RelicRewards)),
                    owner.RoomLatest("bags.personal"));
                ReadTreasureBag(run);
            }
            internal void TreasureGenerated(PredictorRun run, string rarity, bool firstRunGorget)
            {
                Write("rarity", rarity, reads: Inputs("rng.TreasureRoomRelics.state", "silverCrucible.countersAfter"));
                Write("relic.model", run.TreasureRelic?.Key.Serialized ?? "none", alias: "relic",
                    description: "Actual treasure pull result. Delegated RelicAllowed predicate internals and instance allocation are not instrumented here.");
                var bag = Write("bags.shared.after", Bag(run.Working.SharedBag));
                owner.RecordStateWrite(Node, "bags.shared", bag);
                if (firstRunGorget)
                {
                    var personal = Write("bags.personal.after", Bag(run.Working.PersonalBag));
                    owner.RecordStateWrite(Node, "bags.personal", personal);
                }
                StreamAfter(run, PredictorStream.TreasureRoomRelics);
            }
            internal void TreasureSuppressed(PredictorRun run)
            {
                Write("relic.model", "none", alias: "relic");
                // Returning an unchanged cursor is an output of the skipped
                // branch; this is not a claim that the branch read/drew RNG.
                StreamAfter(run, PredictorStream.TreasureRoomRelics);
            }
            internal void TreasureOpeningSuppressed(PredictorRun run)
            {
                Write("normalGold", 0, reads: Inputs("generationAllowed"), alias: "normalGold");
                Write("spoilsGold", 0, reads: Inputs("generationAllowed"), alias: "spoilsGold");
                var gold = Write("gold.balance.unchanged", run.Working.GoldBudget, owner.Domain(PredictorCombatDomain.Gold),
                    owner.RoomLatest("gold.balance").AddRange(Inputs("generationAllowed")), "goldBalance");
                owner.RecordStateWrite(Node, "gold.balance", gold);
                StreamAfter(run, PredictorStream.Rewards);
            }
            internal void StreamAfter(PredictorRun run, PredictorStream stream)
            {
                var cursor = run.Working.Streams.Single(s => s.Stream == stream);
                var causes = owner.StreamCauses(stream);
                var reads = Inputs("rng." + stream + ".state", "generationAllowed", "silverCrucible.countersAfter", "optionPresent",
                    "normalGold.rangeMinimum", "normalGold.rangeMaximum");
                var state = Write("rng." + stream + ".state", $"{cursor.S0:X16}:{cursor.S1:X16}:{cursor.S2:X16}:{cursor.S3:X16}", causes, reads);
                owner.RecordStateWrite(Node, "rng." + stream + ".state", state);
                var calls = Write("rng." + stream + ".calls", cursor.Calls, causes, reads);
                owner.RecordStateWrite(Node, "rng." + stream + ".calls", calls);
            }
            internal void ReadTreasureAllowed(PredictorRun run) =>
                Read("generationAllowed", run._treasureAllowed, owner.RoomRelics(), owner.TreasureResultReads(Node, "generationAllowed"));
            internal void ReadGoldSettlement(PredictorRun run)
            {
                Read("gold.balance.before", run.Working.GoldBudget, owner.Domain(PredictorCombatDomain.Gold), owner.RoomLatest("gold.balance"));
                Read("gold.bowlerHat.count", run.Working.Relics.Count(r => !r.Melted && r.Key.Entry == "BOWLER_HAT"), owner.RoomRelics());
                Read("gold.ectoplasm.present", PredictorSettlementEffects.Has(run.Working, "ECTOPLASM"), owner.RoomRelics());
                // Dragon Fruit's health-effect listener is inside TakeGold and
                // is skipped for nonpositive settlement. It is not instrumented
                // by this balance boundary.
            }
            internal void TreasureNormalGold(PredictorRun run, int amount)
            {
                Write("normalGold", amount, reads: Inputs("generationAllowed", "rng.Rewards.state", "normalGold.ascension",
                    "normalGold.rangeMinimum", "normalGold.rangeMaximum", "normalGold.ascensionMultiplier"), alias: "normalGold");
                bool ectoplasm = owner.RoomRelics().IsEmpty && PredictorSettlementEffects.Has(run.Working, "ECTOPLASM");
                bool knownBalance = owner.Facts.Fact(Inputs("gold.balance.before")[0]).Status == PredictorKnowledgeStatus.Known;
                var gold = Write("gold.balance.after", run.Working.GoldBudget, owner.Domain(PredictorCombatDomain.Gold),
                    ectoplasm && !knownBalance ? Inputs("gold.balance.before", "gold.ectoplasm.present") :
                        Inputs("normalGold", "gold.balance.before", "gold.bowlerHat.count", "gold.ectoplasm.present"), "goldBalance",
                    ectoplasm && knownBalance ? "Known Ectoplasm forces TakeGold to return the known pre-existing balance for every generated amount." : null);
                owner.RecordStateWrite(Node, "gold.balance", gold);
                StreamAfter(run, PredictorStream.Rewards);
            }
            internal void TreasureSpoilsBefore(PredictorRun run, MapPosition point)
            {
                var cards = run.Working.Deck.Select(run.Working.Card).Where(c => c.Key.Entry == "SPOILS_MAP").ToArray();
                bool quest = cards.Any(c => c.SpoilsCoord == point);
                var causes = quest ? PredictorRouteCauseEvidence.Normalize(owner._dependencies.Where(d =>
                    RouteCardMayVary(run, [d], "SPOILS_MAP")).Select(d => d.Cause)) : [];
                Read("spoils.targetPoint", point.Column + ":" + point.Row);
                Read("spoils.questAtPoint", quest, causes, owner.RoomLatest("deck.All.candidates"),
                    proof: quest ? null : "No attached SpoilsMap marker exists at this entry point. Cards added after map generation cannot create a marker for this generated map.");
                if (quest) Read("spoils.mapCount", cards.Length, causes);
            }
            internal void TreasureSpoilsAfter(PredictorRun run, int amount)
            {
                Write("spoilsGold", amount, reads: Inputs("generationAllowed", "spoils.targetPoint", "spoils.questAtPoint", "spoils.mapCount"), alias: "spoilsGold");
                bool ectoplasm = owner.RoomRelics().IsEmpty && PredictorSettlementEffects.Has(run.Working, "ECTOPLASM");
                bool knownBalance = owner.Facts.Fact(Inputs("gold.balance.after")[0]).Status == PredictorKnowledgeStatus.Known;
                var gold = Write("gold.balance.afterSpoils", run.Working.GoldBudget, owner.Domain(PredictorCombatDomain.Gold),
                    ectoplasm && !knownBalance ? Inputs("gold.balance.after", "gold.ectoplasm.present") :
                        Inputs("spoilsGold", "gold.balance.after", "gold.bowlerHat.count", "gold.ectoplasm.present"), "goldBalance",
                    ectoplasm && knownBalance ? "Known Ectoplasm makes every SpoilsMap TakeGold call preserve the known pre-Spoils balance regardless of uncertain payout multiplicity." : null);
                owner.RecordStateWrite(Node, "gold.balance", gold);
                if (amount > 0) DeckAfter(run, "spoilsDeck", "CompleteSpoilsMapAtTreasure removal of its established map cards");
            }
            internal void TreasureTake(PredictorRun run)
            {
                Read("offeredRelic.model", run.TreasureRelic!.Key.Serialized, owner.RoomRelics().AddRange(owner.StreamCauses(PredictorStream.TreasureRoomRelics)),
                    owner.TreasureResultReads(Node, "relic"));
                Read("offeredRelic.instance", run.TreasureRelic.Id, owner.Domain(PredictorCombatDomain.InstanceIds));
            }
        }
    }
}
