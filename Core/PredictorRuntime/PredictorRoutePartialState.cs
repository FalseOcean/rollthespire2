using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // This owner exists only in a detached route preview. Concrete execution,
    // checkpoints and the native RunState never acquire hypothetical facts.

    private sealed partial class RoutePartialState
    {
        internal readonly PredictorRouteFactRecorder Facts;
        internal PredictorDeckKnowledge Deck { get; private set; }
        private PredictorState _observed;
        private readonly PredictorContext _context;
        private readonly List<RouteDependency> _dependencies;
        private readonly Dictionary<string, PredictorCombatPendingCause> _choices = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Node, string Operation), PredictorCombatPendingCause> _deckOperations = [];
        private readonly HashSet<string> _opaqueDeck = new(StringComparer.Ordinal);
        private readonly Dictionary<PredictorStream, PredictorChoiceInt> _streamOffsets = [];
        private readonly Dictionary<PredictorStream, PredictorStreamState> _streamBases;
        private readonly Dictionary<PredictorStream, ImmutableArray<PredictorCombatPendingCause>> _streamCauses = [];
        private readonly List<PredictorCombatPendingCause> _nicheConsumptionCauses = [];
        private readonly Dictionary<string, PredictorKnowledgeFactId> _latest = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Node, string Property), PredictorKnowledgeFactId> _stateWrites = [];

        // Entry hooks can run before the public Before frame is attached to its
        // destination. Keep their writes separate from the previous boundary.
        internal void RecordStateWrite(string node, string property, PredictorKnowledgeFactId fact)
        {
            if (Facts.Fact(fact).NodeId != node) throw new ArgumentException("PredictorStateWriteNodeMismatch");
            _stateWrites[(node, property)] = fact;
        }

        internal RoutePartialState(PredictorContext context, PredictorState state, List<RouteDependency> dependencies,
            PredictorRouteFactRecorder facts)
        {
            Facts = facts;
            _context = context; _observed = state; _dependencies = dependencies;
            Deck = PredictorDeckKnowledge.Create(context, state);
            _streamBases = state.Streams.ToDictionary(s => s.Stream);
            foreach (var stream in state.Streams) _streamOffsets[stream.Stream] = PredictorChoiceInt.Constant(0);
            Synchronize(state);
        }

        private ImmutableArray<PredictorCombatPendingCause> Domain(PredictorCombatDomain domains) =>
            PredictorRouteCauseEvidence.Normalize(_dependencies.Where(d => (d.Cause.AffectedDomains & domains) != 0).Select(d => d.Cause));

        internal void Synchronize(PredictorState state)
        {
            // The ordinary simulator carries one execution witness. Only edits
            // not depending on an unresolved existing-card change are facts.
            foreach (var dependency in _dependencies.Where(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0))
            {
                var cause = PredictorRouteCauseEvidence.Bind(dependency.Cause);
                if (_choices.ContainsKey(cause.ChoiceId ?? "") || cause.Code == "PartialUpgradeTargets" ||
                    !_opaqueDeck.Add(cause.Identity)) continue;
                string sourceNode = cause.NodeId ?? state.Position.NodeId;
                string sourceOperation = "dependency:" + cause.Identity;
                _deckOperations[(sourceNode, sourceOperation)] = cause;
                if (cause.Code == "PaelsToothReturnedCardPending")
                {
                    // The original stored members were masked at acquisition.
                    // A return adds an unknown upgraded card; it does not edit
                    // every existing or subsequently acquired card.
                    _choices[cause.Identity] = cause;
                    Deck = Deck.MarkUnknownAddition(sourceNode, "PaelsToothReturnedIdentityNotKnown", cause.Identity);
                    continue;
                }
                if (dependency.DeckFacts != PredictorDeckUncertainFact.None)
                    Deck = Deck.MarkExistingChangePending(sourceNode,
                        "UnprojectedDeckOperation:" + cause.Code, dependency.DeckFacts,
                        instanceIds: dependency.AffectedCardIds, cardTypes: dependency.AffectedCardTypes,
                        choiceId: cause.ChoiceId, sourceOperation: sourceOperation);
            }
            var before = _observed.Deck.Select(_observed.Card).ToDictionary(c => c.Id);
            var after = state.Deck.Select(state.Card).ToDictionary(c => c.Id);
            foreach (var card in before.Values)
            {
                if (!Deck.HasFixedMember(card.Id))
                {
                    // This instance belongs to one prior alternative. It is
                    // never promoted to a fixed card by a later witness edit.
                    var possible = after.GetValueOrDefault(card.Id);
                    var fields = PredictorDeckKnowledge.ChangedFacts(card, possible);
                    if (fields != PredictorDeckUncertainFact.None)
                    {
                        string operation = "conditional-card-edit:" + card.Id;
                        var cause = PredictorRouteCauseEvidence.CreateExplicit("ConditionalCardEditNotProjected", state.Position.NodeId,
                            PredictorCombatDomain.Deck, PredictorPendingReasonKind.ImplementationGap,
                            "An operation edited a representative of a prior card choice; its conditional target effects are not yet projected.",
                            ["deck.operation.targets"], Domain(PredictorCombatDomain.Deck), occurrenceKey: operation);
                        _deckOperations[(state.Position.NodeId, operation)] = cause;
                        Deck = Deck.MarkExistingChangePending(state.Position.NodeId, cause.Code, fields,
                            cardTypes: [card.Type], sourceOperation: operation);
                    }
                    continue;
                }
                if (!after.TryGetValue(card.Id, out var next)) Deck = Deck.RemoveKnown(card.Id);
                else if (card != next) Deck = Deck.ReplaceKnown(card.Id, next, append: false, observedBefore: card);
            }
            foreach (var card in after.Values.Where(c => !before.ContainsKey(c.Id))) Deck = Deck.AddKnown(card);
            foreach (var cursor in state.Streams)
            {
                int consumed = cursor.Calls - _observed.Streams.Single(s => s.Stream == cursor.Stream).Calls;
                if (consumed < 0) throw new InvalidOperationException("PredictorPartialCursorRewound");
                if (consumed != 0)
                {
                    if (_streamOffsets[cursor.Stream].KnownValue == null)
                    {
                        // An arbitrary later operation might use a different
                        // number of draws at another cursor. Do not infer its
                        // consumption from the representative execution.
                        AddStreamGap(cursor.Stream, state.Position.NodeId, "UnprojectedRandomConsumption",
                            StreamCauses(cursor.Stream));
                    }
                    _streamOffsets[cursor.Stream] += consumed;
                }
            }
            _observed = state;
        }

        internal void PendingReward(PredictorRun run, PredictorReward reward, PredictorCombatPendingCause cause,
            bool offerKnown, PendingRouteRewardAlternatives alternativeEffects)
        {
            if (reward.Kind is not (PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) ||
                cause.ChoiceId == null || _choices.ContainsKey(cause.ChoiceId)) return;
            // Register first: this cause is modeled by the grouped choice and
            // must not become an unrelated whole-deck opacity marker.
            _choices.Add(cause.ChoiceId, cause);
            Synchronize(run.Working);
            if (!offerKnown)
            {
                Deck = Deck.MarkUnknownAddition(cause.NodeId!, "RewardOfferNotDetermined", choiceId: cause.ChoiceId);
                return;
            }
            var alternatives = reward.Cards.Select((card, i) => new PredictorDeckRewardAlternative(
                "card:" + i, card)).ToImmutableArray();
            if (reward.CanSkip && !reward.Mandatory) alternatives = alternatives.Add(new("skip", null));
            alternatives = alternatives.AddRange(alternativeEffects.Options);
            bool hooksKnown = !Domain(PredictorCombatDomain.Relics).Any(parent => parent.ChoiceId != cause.ChoiceId) &&
                (!PredictorSettlementEffects.Has(run.Working, "BING_BONG") && !run.Context.HasRunModifier("HOARDER") ||
                    Deck.Choices.IsEmpty);
            Deck = Deck.AddRewardChoice(cause.ChoiceId, cause.NodeId!, alternatives, run.Context, run.Working, hooksKnown);
            if (alternativeEffects.ChangesExistingDeck)
                Deck = Deck.MarkExistingChangePending(cause.NodeId!, "SacrificeRelicObtainChangesExistingDeck",
                    PredictorDeckUncertainFact.All, choiceId: cause.ChoiceId,
                    sourceOperation: "reward-alternative:" + cause.ChoiceId);
        }

        internal bool CanProjectUpgrade(ModelKey relic)
        {
            if (relic.Entry is not ("WHETSTONE" or "WAR_PAINT")) return false;
            // An unprojected eligible count is an unknown upgrade result, not
            // a failure to receive the relic. ProjectUpgrade retains its deck
            // mutation and marks Niche unknown without changing other streams.
            return Domain(PredictorCombatDomain.Relics).IsEmpty;
        }

        internal PredictorCombatDomain RefineObtainReads(ModelKey relic, PredictorCombatDomain reads, bool entryOnly)
        {
            if (CanProjectUpgrade(relic)) return PredictorCombatDomain.None;
            int targets = relic.Entry switch { "EMPTY_CAGE" => 2, "ASTROLABE" => 3, "NEW_LEAF" => 1, _ => 0 };
            if (targets > 0)
            {
                var scope = relic.Entry == "EMPTY_CAGE" ? PredictorDeckProjectionScope.Removable : PredictorDeckProjectionScope.Transformable;
                var candidates = Deck.Project(scope);
                if (candidates.KnownCount == 0 || entryOnly && candidates.Count?.Bounds.Lower > targets)
                    return reads & ~(PredictorCombatDomain.Deck | PredictorCombatDomain.NicheRng);
                if (candidates.OrderedCandidatesKnown) reads &= ~PredictorCombatDomain.Deck;
            }
            if (relic.Entry == "PANDORAS_BOX")
            {
                var strikes = Deck.Project(PredictorDeckProjectionScope.BasicStrike);
                var defends = Deck.Project(PredictorDeckProjectionScope.BasicDefend);
                var removable = Deck.Project(PredictorDeckProjectionScope.Removable).KnownCandidates.Select(c => c.Id).ToHashSet();
                if (strikes.OrderedCandidatesKnown && defends.OrderedCandidatesKnown &&
                    strikes.KnownCandidates.Concat(defends.KnownCandidates).All(c => removable.Contains(c.Id)))
                {
                    reads &= ~PredictorCombatDomain.Deck;
                    if (strikes.KnownCount == 0 && defends.KnownCount == 0) reads &= ~PredictorCombatDomain.NicheRng;
                }
            }
            return reads;
        }

        internal bool StreamKnown(PredictorStream stream) =>
            _streamOffsets[stream].KnownValue != null &&
            ExternalStreamCauses(stream).IsEmpty && !_streamCauses.ContainsKey(stream);

        private ImmutableArray<PredictorCombatPendingCause> ExternalStreamCauses(PredictorStream stream) =>
            Domain(StreamDomain(stream)).Where(c => c.Code != "PartialStreamOffset").ToImmutableArray();

        private void AddStreamGap(PredictorStream stream, string node, string code,
            ImmutableArray<PredictorCombatPendingCause> parents, string? sourceOperation = null)
        {
            var cause = PredictorRouteCauseEvidence.CreateExplicit(code, node, StreamDomain(stream),
                PredictorPendingReasonKind.ImplementationGap,
                "The current operation does not prove its draw count for every possible incoming cursor.",
                ["rng." + stream + ".consumed"], parents,
                occurrenceKey: PredictorRouteCauseEvidence.Key(stream.ToString(), sourceOperation ?? code));
            _streamCauses[stream] = PredictorRouteCauseEvidence.Normalize(
                _streamCauses.GetValueOrDefault(stream, []).Add(cause));
            AddDependency(_dependencies, new(cause));
        }

        internal PredictorState ProjectUpgrade(PredictorState before, PredictorState after, ModelKey relic)
        {
            // BeginObtainRelic calls Synchronize immediately before the actual
            // shuffle. Its fixed-size semantics supplies a symbolic draw count.
            var projection = Deck.Project(UpgradeScope(relic));
            var recordedReads = ReadUpgrade(before, relic, projection);
            bool targetsKnown = projection.OrderedCandidatesKnown && StreamKnown(PredictorStream.Niche);
            if (targetsKnown)
            {
                Synchronize(after);
                WriteUpgrade(recordedReads);
                return after;
            }
            var type = relic.Entry == "WHETSTONE" ? EffectCardType.Attack : EffectCardType.Skill;
            string operationId = relic.Entry + ":" + before.Relics.Length;
            var upgraded = Deck.ApplyRandomUpgrade(before.Position.NodeId, operationId, type);
            Deck = upgraded.Deck;
            if (upgraded.Consumption?.KnownValue == null)
            {
                var countCause = PredictorRouteCauseEvidence.CreateExplicit("UpgradePopulationPending", before.Position.NodeId,
                    PredictorCombatDomain.None, PredictorPendingReasonKind.DerivedDependency,
                    "This upgrade shuffles exactly the eligible population; independent card types are not read.",
                    [DeckKey(UpgradeScope(relic)) + ".count"], ProjectionCauses(projection, count: true), occurrenceKey: operationId);
                _nicheConsumptionCauses.Add(PredictorRouteCauseEvidence.CreateExplicit("UpgradeConsumptionPending", before.Position.NodeId,
                    PredictorCombatDomain.NicheRng, PredictorPendingReasonKind.DerivedDependency,
                    "UnstableShuffle consumes max(eligible count - 1, 0) Niche draws.",
                    ["rng.Niche.consumed"], [countCause], occurrenceKey: operationId));
            }
            if (upgraded.Consumption != null) _streamOffsets[PredictorStream.Niche] += upgraded.Consumption;
            else AddStreamGap(PredictorStream.Niche, before.Position.NodeId, "UpgradeCountNotDerived",
                ProjectionCauses(projection, count: true), operationId);
            // Do not import the representative upgraded card identities.
            _observed = after;
            var current = _streamOffsets[PredictorStream.Niche];
            var parents = ProjectionCauses(projection, count: false).AddRange(StreamCauses(PredictorStream.Niche));
            var affected = before.Deck.Select(before.Card).Where(c => c.Type == type).ToImmutableArray();
            if (!upgraded.Gaps.IsEmpty)
            {
                var targetCause = PredictorRouteCauseEvidence.CreateExplicit("PartialUpgradeTargets",
                    before.Position.NodeId, PredictorCombatDomain.Deck, PredictorPendingReasonKind.DerivedDependency,
                    "Random upgrade targets depend on the ordered eligible candidates and the incoming Niche cursor.",
                    [DeckKey(UpgradeScope(relic)) + ".candidates", "rng.Niche.state"], parents,
                    occurrenceKey: operationId);
                _deckOperations[(before.Position.NodeId, operationId)] = targetCause;
                AddDependency(_dependencies, new(targetCause, affected, MayChangeExistingCards: true)
                { DeckFacts = upgraded.Gaps.Aggregate(PredictorDeckUncertainFact.None, (facts, gap) => facts | gap.Facts),
                    AffectedCardTypes = [type] });
            }
            _dependencies.RemoveAll(d => d.Cause.Code == "PartialStreamOffset");
            if (current.KnownValue == null)
                AddDependency(_dependencies, new(PredictorRouteCauseEvidence.CreateExplicit("PartialStreamOffset",
                    before.Position.NodeId, PredictorCombatDomain.NicheRng, PredictorPendingReasonKind.DerivedDependency,
                    "The cumulative Niche draw count retains the original mutually exclusive reward choices.",
                    ["rng.Niche.calls"], StreamCauses(PredictorStream.Niche), occurrenceKey: "Niche")));
            if (current.KnownValue is { } calls && !_streamCauses.ContainsKey(PredictorStream.Niche) &&
                ExternalStreamCauses(PredictorStream.Niche).IsEmpty)
            {
                var rng = _streamBases[PredictorStream.Niche].Restore();
                for (long i = 0; i < calls; i++) rng.NextDouble();
                after = after.WithRng(PredictorStream.Niche, rng);
                _observed = after;
            }
            WriteUpgrade(recordedReads);
            return after;
        }

        internal ImmutableArray<PredictorCombatPendingCause> ProjectionCauses(PredictorDeckProjection projection, bool count)
        {
            var sources = count ? projection.UnresolvedCountSources : projection.UnresolvedCandidateSources;
            var causes = sources.Where(s => _choices.ContainsKey(s.ChoiceId)).Select(s => _choices[s.ChoiceId]).ToList();
            foreach (var gap in count ? projection.UnresolvedCountGaps : projection.Gaps)
            {
                var operation = gap.SourceOperation == null ? null :
                    _deckOperations.GetValueOrDefault((gap.NodeId, gap.SourceOperation));
                if (gap.Reason == "RandomUpgradeTargetIdentityNotKnown" && operation != null)
                {
                    // This is a consequence of the earlier correlated choice,
                    // not a new implementation gap or every unrelated deck edit.
                    causes.Add(operation);
                    continue;
                }
                // Keep the inputs recorded at the original operation. Looking
                // at today's whole Deck domain can attach this gap's own later
                // descendants as parents, as well as unrelated player choices.
                var parents = gap.ChoiceId != null && _choices.TryGetValue(gap.ChoiceId, out var choice) ? ImmutableArray.Create(choice) :
                    operation != null ? ImmutableArray.Create(operation) : [];
                causes.Add(PredictorRouteCauseEvidence.CreateExplicit("DeckPropertyDerivationGap", gap.NodeId,
                    PredictorCombatDomain.Deck, parents.IsEmpty ? PredictorPendingReasonKind.ImplementationGap :
                        PredictorPendingReasonKind.DerivedDependency, gap.Reason,
                    [DeckKey(projection.Scope) + (count ? ".count" : ".candidates")], parents,
                    occurrenceKey: gap.Reason + ":" + projection.Scope));
            }
            return PredictorRouteCauseEvidence.Normalize(causes);
        }

        internal ImmutableArray<PredictorCombatPendingCause> StreamCauses(PredictorStream stream)
        {
            var causes = ExternalStreamCauses(stream).AddRange(_streamCauses.GetValueOrDefault(stream, []));
            if (_streamOffsets[stream].KnownValue == null)
                causes = stream == PredictorStream.Niche ? causes.AddRange(_nicheConsumptionCauses) :
                    causes.AddRange(_streamOffsets[stream].Sources.Where(s => _choices.ContainsKey(s.ChoiceId)).Select(s => _choices[s.ChoiceId]));
            return PredictorRouteCauseEvidence.Normalize(causes);
        }

        internal ImmutableDictionary<PredictorStream, PredictorChoiceInt> StreamProgress() => _streamOffsets.ToImmutableDictionary();

        internal void RecordConsumption(string nodeId, ImmutableDictionary<PredictorStream, PredictorChoiceInt> before)
        {
            foreach (var (stream, progress) in _streamOffsets)
            {
                var delta = progress - before[stream];
                var causes = delta.KnownValue.HasValue ? ExternalStreamCauses(stream).AddRange(_streamCauses.GetValueOrDefault(stream, [])) : StreamCauses(stream);
                string key = "rng." + stream + ".consumed";
                if (delta.KnownValue is { } exact)
                    Facts.RecordValue(key, nodeId, PredictorRouteFactPhase.After, exact, causes,
                        operation: "node RNG consumption");
                else if (!ExternalStreamCauses(stream).IsEmpty || _streamCauses.ContainsKey(stream))
                    Facts.RecordUnknown(key, nodeId, PredictorRouteFactPhase.After, causes, operation: "node RNG consumption");
                else Facts.RecordPartial(key, nodeId, PredictorRouteFactPhase.After, Range(delta), causes,
                    [new("range-kind", "conservative enclosure")], operation: "node RNG consumption");
            }
        }

        internal static PredictorDeckProjectionScope UpgradeScope(ModelKey key) => key.Entry == "WHETSTONE"
            ? PredictorDeckProjectionScope.UpgradableAttack : PredictorDeckProjectionScope.UpgradableSkill;
        internal static string DeckKey(PredictorDeckProjectionScope scope) => "deck." + scope;
        internal static PredictorCombatDomain StreamDomain(PredictorStream stream) => StreamKnowledgeDomain(stream);
    }

    private static ImmutableArray<PredictorCombatPendingCause> ObtainCauses(PredictorRun run, ModelKey key,
        List<RouteDependency> dependencies, bool entryOnly = false)
    {
        run._preview.Partial?.Synchronize(run.Working);
        if (HasLiveDeckRandomObtainBoundary(run, key) || run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
            key.Entry == "LEAFY_POULTICE")
            return CausesFor(dependencies, PredictorCombatDomain.Relics);
        var reads = entryOnly ? ObtainEntryReadDomains(run, key, dependencies) : ObtainReadDomains(key);
        if (run._preview.Partial != null) reads = run._preview.Partial.RefineObtainReads(key, reads, entryOnly);
        return CausesFor(dependencies, reads);
    }
}
