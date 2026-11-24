using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed record EventInputSnapshot(PredictorState Boundary, PredictorDeckKnowledge Deck, PredictorChoiceInt? Gold);
    // Typed operation inputs carry the value used by the predicate and its
    // established provenance. A pending value is a private execution witness.
    private sealed record EventStateInput<T>(string PropertyKey, T Value,
        ImmutableArray<PredictorCombatPendingCause> PendingCauses,
        PredictorKnowledgeOrigin Origin, string Operation, PredictorKnowledgeFactId? FactId)
    {
        internal bool IsKnown => PendingCauses.IsEmpty;
    }

    private sealed record EventAppearanceResult(bool? Allowed,
        ImmutableArray<PredictorCombatPendingCause> PendingCauses,
        ImmutableArray<PredictorKnowledgeFactId> InputFacts,
        bool HealthAssumed, bool DeckAssumed);

    private sealed class EventStateInputs(PredictorRun run, ModelKey key,
        List<RouteDependency> dependencies, string stage)
    {
        private PredictorRouteFactRecorder? Recorder => run._preview.Partial?.Facts ?? run._preview.InputFacts;
        private PredictorDeckKnowledge? Deck => run._preview.Partial?.Deck ??
            (ReferenceEquals(run._preview.InputSnapshot?.Boundary, run.Working) ? run._preview.InputSnapshot?.Deck : null);
        private readonly List<PredictorKnowledgeFactId> _facts = [];
        private readonly List<PredictorCombatPendingCause> _causes = [];
        internal ImmutableArray<PredictorKnowledgeFactId> Facts => _facts.Distinct().ToImmutableArray();
        internal ImmutableArray<PredictorCombatPendingCause> Causes => PredictorRouteCauseEvidence.Normalize(_causes);

        internal EventStateInput<T> Read<T>(string property, T value,
            ImmutableArray<PredictorCombatPendingCause> causes = default,
            PredictorKnowledgeOrigin origin = PredictorKnowledgeOrigin.Derived,
            string? operation = null, params string[] sourceProperties)
        {
            causes = PredictorRouteCauseEvidence.Normalize(causes.IsDefault ? [] : causes);
            string propertyKey = "event." + stage + "." + key.Entry + "." + property;
            string computation = operation ?? key.Entry + ": read " + property;
            var sources = run._preview.Partial?.EventInputSources(run.Working.Position.NodeId, sourceProperties) ?? [];
            var fact = Recorder?.RecordResult(propertyKey, run.Working.Position.NodeId,
                value, causes, origin, computation, sources,
                derivation: causes.IsEmpty ? "The specific property reader proves this value; unresolved parts of the broader source snapshot do not change " + propertyKey + ". " + computation : null);
            if (fact.HasValue) _facts.Add(fact.Value);
            _causes.AddRange(causes);
            return new(propertyKey, value, causes, origin, computation, fact);
        }

        internal PredictorKnowledgeFactId? Result<T>(string property, T value,
            ImmutableArray<PredictorCombatPendingCause> causes, string operation) =>
            Recorder?.RecordResult("event." + stage + "." + key.Entry + "." + property,
                run.Working.Position.NodeId, value, causes, operation: operation, allReadFactIds: Facts,
                derivation: causes.IsEmpty ? "The actual typed predicate and its short-circuit rules establish this result. " + operation : null);

        internal EventStateInput<bool> GoldAtLeast(int required)
        {
            var balance = run._preview.Partial?.EventGoldBalance(run.Working) ??
                (ReferenceEquals(run._preview.InputSnapshot?.Boundary, run.Working) ? run._preview.InputSnapshot?.Gold : null);
            bool? proved = balance?.Bounds.Lower >= required ? true : balance?.Bounds.Upper < required ? false : null;
            return Read("gold.atLeast." + required, proved ?? run.Working.GoldBudget >= required,
                proved.HasValue ? [] : CausesFor(dependencies, PredictorCombatDomain.Gold),
                operation: "Event " + stage + ": actual Gold >= " + required + (proved.HasValue
                    ? "; retained correlated bounds [" + balance!.Bounds.Lower + ", " + balance.Bounds.Upper + "] prove " + proved.Value : ""),
                sourceProperties: ["gold.balance"]);
        }

        internal EventStateInput<bool> PotionsAtLeast(int required)
        {
            var supplied = run.RouteEventPotionInput(key);
            bool local = supplied is { OrderedSlots.IsDefault: false };
            var slots = run._eventKey == key && run.HasLocalEventPotionIdentities ? run.EventPotionSlots
                : local ? supplied!.OrderedSlots.Select((model, index) => model is { } potion
                    ? new PredictorPotion(-(index + 1L), potion) : null).ToImmutableArray() : run.Working.PotionSlots;
            if (local) Read("potions.localOrderedSlots", string.Join(" | ", supplied!.OrderedSlots.Select(model => model?.Serialized ?? "empty")),
                origin: PredictorKnowledgeOrigin.PlayerInput, operation: "Event-local ordered potion hypothesis; not a route inventory fact");
            return Read("potions.occupiedAtLeast." + required, slots.Count(p => p != null) >= required,
                local ? [] : CausesFor(dependencies, PredictorCombatDomain.PotionInventory),
                origin: local ? PredictorKnowledgeOrigin.PlayerInput : PredictorKnowledgeOrigin.Derived,
                operation: "Event " + stage + ": occupied potion count >= " + required,
                sourceProperties: local ? [] : ["potions.slots"]);
        }

        internal EventStateInput<bool> PotionMember(string entry)
        {
            // Appearance is evaluated before BeginEvent installs local slots.
            // An active event instead reads its remaining local bottles after
            // any consumption, rather than replaying the original hypothesis.
            var supplied = run._eventKey != key ? run.RouteEventPotionInput(key) : null;
            bool arrivalLocal = supplied is { OrderedSlots.IsDefault: false };
            bool local = arrivalLocal || run.HasLocalEventPotionIdentities;
            bool present = arrivalLocal ? supplied!.OrderedSlots.Any(p => p?.Entry == entry) :
                run.EventPotionSlots.Any(p => p?.Key.Entry == entry);
            return Read("potions.member." + entry, present,
                local ? [] : CausesFor(dependencies, PredictorCombatDomain.PotionInventory),
                origin: local ? PredictorKnowledgeOrigin.PlayerInput : PredictorKnowledgeOrigin.Derived,
                operation: "Event " + stage + ": potion model membership", sourceProperties: local ? [] : ["potions.slots"]);
        }

        internal EventStateInput<bool> RelicMember(string entry) => Read("relics.member." + entry,
            PredictorSettlementEffects.Has(run.Working, entry), RouteRelicMembershipCauses(run, dependencies, entry),
            operation: "Event " + stage + ": actual effective relic membership",
            sourceProperties: ["relics.orderedInventory"]);

        internal EventStateInput<bool> TradableAtLeast(int required)
        {
            var entries = run.Context.Catalog!.TradableRelicTypes;
            var membership = entries.SelectMany(relic => RouteRelicMembershipCauses(run, dependencies, relic.Entry));
            var states = new[] { "LIZARD_TAIL", "WINGED_BOOTS", "WONGOS_MYSTERY_TICKET" }
                .SelectMany(entry => RouteRelicStateCauses(run, dependencies, entry));
            var causes = PredictorRouteCauseEvidence.Normalize(membership.Concat(states));
            var tradable = run.Working.Relics.Where(run.IsRelicTradable).ToImmutableArray();
            int guaranteed = tradable.Count(relic => RouteRelicMembershipCauses(run, dependencies, relic.Key.Entry).IsEmpty &&
                (relic.Key.Entry is not ("LIZARD_TAIL" or "WINGED_BOOTS" or "WONGOS_MYSTERY_TICKET") ||
                    RouteRelicStateCauses(run, dependencies, relic.Key.Entry).IsEmpty));
            return Read("relics.tradableAtLeast." + required, tradable.Length >= required,
                guaranteed >= required ? [] : causes,
                operation: "Event " + stage + ": actual IsRelicTradable filter and count >= " + required,
                sourceProperties: ["relics.orderedInventory"]);
        }

        internal EventStateInput<bool> CardMember(string entry)
        {
            // Existing guaranteed membership short-circuits uncertain additions.
            // Project(All) reads Membership only, so random upgrades cannot hide
            // a known Egg. Other-model reward additions are irrelevant as well.
            var projection = Deck?.Project(PredictorDeckProjectionScope.All);
            var relevant = dependencies.Where(d => RouteCardMayVary(run, [d], entry)).ToImmutableArray();
            bool guaranteed = projection?.KnownCandidates.Any(c => c.Key.Entry == entry) == true &&
                relevant.All(d => !d.MayChangeExistingCards && !d.AnyExistingCard);
            bool present = run.Working.Deck.Any(id => run.Working.Card(id).Key.Entry == entry);
            var causes = guaranteed ? ImmutableArray<PredictorCombatPendingCause>.Empty : relevant.Select(d => d.Cause).ToImmutableArray();
            return Read("deck.member." + entry, guaranteed || present, causes,
                operation: "HasEventPet: actual specific card model membership",
                sourceProperties: ["deck.All.candidates"]);
        }

        internal EventStateInput<bool> DeckAtLeast(PredictorDeckProjectionScope scope, int required)
        {
            var projection = Deck?.Project(scope);
            bool? proven = projection?.Count is { } count ? count.Bounds.Lower >= required ? true :
                count.Bounds.Upper < required ? false : null : null;
            bool present = scope == PredictorDeckProjectionScope.Removable &&
                run.Working.Deck.Any(id => run.Working.Card(id).Removable);
            var causes = proven.HasValue ? ImmutableArray<PredictorCombatPendingCause>.Empty :
                projection != null ? run._preview.Partial!.ProjectionCauses(projection, count: true) :
                CausesFor(dependencies, PredictorCombatDomain.Deck);
            return Read("deck." + scope + ".atLeast." + required, proven ?? present, causes,
                operation: "Event entry: actual " + scope + " population threshold",
                sourceProperties: ["deck." + scope + ".count"]);
        }

        internal EventStateInput<bool> EventPetMember()
        {
            var pets = run.Context.Catalog!.EventPetRelics;
            bool present = run.Working.Relics.Any(r => !r.Melted && pets.Contains(r.Key));
            bool guaranteed = run.Working.Relics.Any(r => !r.Melted && pets.Contains(r.Key) &&
                RouteRelicMembershipCauses(run, dependencies, r.Key.Entry).IsEmpty);
            var causes = guaranteed ? ImmutableArray<PredictorCombatPendingCause>.Empty :
                PredictorRouteCauseEvidence.Normalize(pets.SelectMany(pet => RouteRelicMembershipCauses(run, dependencies, pet.Entry)));
            return Read("relics.hasEventPet", present, causes,
                operation: "HasEventPet: actual AddsPet relic membership",
                sourceProperties: ["relics.orderedInventory"]);
        }

        internal EventStateInput<bool> AvailableRelic()
        {
            var bag = run.Working.PersonalBag;
            bool available = bag.Buckets.SelectMany(b => b.Entries).Concat(bag.MultiplayerFallback)
                .Any(relic => PredictorRewardGeneration.RelicAllowed(run.Context, run.Working, relic));
            return Read("bags.personal.hasAllowedRelic", available,
                CausesFor(dependencies, PredictorCombatDomain.Relics | PredictorCombatDomain.RewardsRng),
                operation: "IsAllowed: actual personal bag and RelicAllowed filter",
                sourceProperties: ["bags.personal", "relics.orderedInventory"]);
        }

        internal EventAppearanceResult Appearance(bool? allowed, bool healthAssumed, bool deckAssumed)
        {
            var causes = allowed.HasValue ? ImmutableArray<PredictorCombatPendingCause>.Empty : Causes;
            Recorder?.RecordResult("event.appearance." + key.Entry + ".allowed",
                run.Working.Position.NodeId, allowed, causes, operation: "IsAllowed: source predicate over recorded inputs",
                allReadFactIds: Facts, unresolvedReadFactIds: allowed.HasValue ? [] : Facts.Where(id =>
                    Recorder!.Fact(id).Status != PredictorKnowledgeStatus.Known).ToImmutableArray(),
                derivation: allowed.HasValue && !Causes.IsEmpty ? "A known conjunct/disjunct settles this predicate independently of the remaining inputs." : null);
            return new(allowed, causes, Facts, healthAssumed, deckAssumed);
        }

        internal static bool? Known(EventStateInput<bool> input) => input.IsKnown ? input.Value : null;
        internal static bool? And(params bool?[] values) => values.Any(v => v == false) ? false :
            values.All(v => v == true) ? true : null;
        internal static bool? Or(params bool?[] values) => values.Any(v => v == true) ? true :
            values.All(v => v == false) ? false : null;
    }

    private sealed partial class RoutePartialState
    {
        internal ImmutableArray<PredictorKnowledgeFactId> EventInputSources(string node, params string[] properties) =>
            properties.Select(property => _stateWrites.TryGetValue((node, property), out var write) ?
                (PredictorKnowledgeFactId?)write : _latest.TryGetValue(property, out var observed) ? observed : null)
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToImmutableArray();
    }
}
