using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Runtime;

public static partial class ProductionQueryValidator
{
    internal static bool HasInitialRoomConditions(ExactSearchEvaluationProjection criteria) =>
        criteria.TreasureRelics is { IsEmpty: false } || criteria.FirstActUnknownEventPrefixCount > 0;

    // An offered identity is not a pickup. Only authored opening commitments
    // bind these room observations to one actual acquisition path.
    internal static bool HasOpeningInitialRoomConditions(ExactSearchEvaluationProjection criteria) =>
        HasInitialRoomConditions(criteria) &&
        (criteria.NeowRoute is { IsValid: true } || HasRouteScopedNeowConstraints(criteria));

    internal static SearchQueryEvaluation EvaluateInitialRoomBaseline(ExactSearchExecutionRequest plan,
        SeedPredictionDocument? document, ulong rootHash) =>
        HasOpeningInitialRoomConditions(plan.Evaluation) ? SearchQueryEvaluation.Match() :
            EvaluateInitialRooms(plan, document, rootHash, [], true, null);

    private static SearchQueryEvaluation EvaluateOpeningInitialRooms(ExactSearchExecutionRequest plan,
        SeedPredictionDocument document, ulong rootHash, OpeningRouteContext route)
    {
        if (route.Choice.RelicKey == BaseGameModelKeys.Relics.NeowsBones && route.BonesRoute is null)
            return SearchQueryEvaluation.Unknown("InitialRooms.BonesAcquisitionsUnavailable");
        var acquired = new HashSet<ModelKey>(route.AcquisitionOrder) { route.Choice.RelicKey };
        // Optional outcomes belong only to this route/outcome. Do not flatten
        // Bones offers or sibling choice outcomes into fictitious ownership.
        var effects = EnumerateRouteEffects(route)
            .Concat(route.Outcome is { } outcome ? EnumerateEffects(outcome.ChoiceEffectGroups) : [])
            .Where(effect => effect.Kind == PredictedEffectKind.AddRelic).ToArray();
        bool complete = effects.All(effect => effect.TargetKey is { IsValid: true, Category: "RELIC" } &&
            effect.Precision == PredictionPrecision.Exact);
        foreach (var effect in effects.Where(effect => effect.Precision == PredictionPrecision.Exact &&
                     effect.TargetKey is { IsValid: true, Category: "RELIC" }))
            acquired.Add(effect.TargetKey!.Value);
        // Missing capsule observations must not silently become an empty bag
        // removal. The existing small-capsule product premise assumes obtain.
        int expectedNested = (acquired.Contains(BaseGameModelKeys.Relics.SmallCapsule) ? 1 : 0) +
            (acquired.Contains(BaseGameModelKeys.Relics.LargeCapsule) ? 2 : 0);
        complete &= effects.Count(effect => effect.Relation == PredictedEffectRelation.NestedRelic &&
            effect.Precision == PredictionPrecision.Exact && effect.TargetKey.HasValue) >= expectedNested;
        return EvaluateInitialRooms(plan, document, rootHash, acquired.ToArray(), complete, route);
    }

    private static SearchQueryEvaluation EvaluateInitialRooms(ExactSearchExecutionRequest plan,
        SeedPredictionDocument? document, ulong rootHash, IReadOnlyList<ModelKey> acquired,
        bool acquisitionsExact, OpeningRouteContext? route)
    {
        if (!HasInitialRoomConditions(plan.Evaluation)) return SearchQueryEvaluation.Match();
        if (plan.Authority.PlayersCount != 1)
            return SearchQueryEvaluation.Unsupported("InitialRooms.SinglePlayerOnly");
        if (plan.ProfileId != RuntimeProfileId.Beta111)
            return SearchQueryEvaluation.Unsupported("InitialRooms.ProfileUnsupported");

        SearchMatchEvidence Evidence(string code, string condition, ModelKey? key = null, int? ordinal = null) =>
            new(code, key, route?.RouteId, route?.AcquisitionOrder,
                "search.initial-rooms.standard-singleplayer", OutcomeId: route?.Outcome?.OutcomeId,
                ProfileId: plan.ProfileId, Ordinal: ordinal, ConditionId: condition);

        SearchQueryEvaluation UnknownRooms()
        {
            int count = plan.Evaluation.FirstActUnknownEventPrefixCount;
            if (count == 0) return SearchQueryEvaluation.Match();
            bool juzu = acquired.Contains(new ModelKey("RELIC", "JUZU_BRACELET"));
            if (!acquisitionsExact && !juzu)
                return SearchQueryEvaluation.Unknown("FirstActUnknownEventPrefix.OpeningJuzuUnresolved");
            var sequence = OrdinaryUnknownRoomPredictor.ForAct1Filter(rootHash, count, juzu);
            return sequence.Steps.All(step => step.Room == UnknownRoomCategory.Event)
                ? SearchQueryEvaluation.Match([Evidence("FirstActUnknownEventPrefixMatched", "FirstActUnknownEventPrefix",
                    juzu ? new ModelKey("RELIC", "JUZU_BRACELET") : null, count)])
                : SearchQueryEvaluation.NoMatch("FirstActUnknownEventPrefix.NonEventEncounter");
        }

        SearchQueryEvaluation Treasure()
        {
            if (plan.Evaluation.TreasureRelics is not { IsEmpty: false } condition)
                return SearchQueryEvaluation.Match();
            var evidence = new List<SearchMatchEvidence>();
            var remaining = new List<(int Slot, ModelKey Key)>();
            var usedOpeningKeys = new HashSet<ModelKey>();
            for (int slot = 0; slot < condition.Count; slot++)
            {
                if (condition.Slots[slot] is not { } target) continue;
                if (acquired.Contains(target) && usedOpeningKeys.Add(target))
                    evidence.Add(Evidence("TreasureRelicObtainedAtOpening", "TreasureRelics.Slot" + (slot + 1), target));
                else remaining.Add((slot, target));
            }
            // An actual acquisition proves the requested goal even when some
            // unrelated future bag observation is unavailable.
            if (remaining.Count == 0) return SearchQueryEvaluation.Match(evidence);
            if (!acquisitionsExact)
                return SearchQueryEvaluation.Unknown("TreasureRelics.OpeningAcquisitionsUnresolved");
            var initial = document?.Sections.Select(section => section.RelicSequencePrediction)
                .FirstOrDefault(value => value is not null);
            if (initial is null) return SearchQueryEvaluation.Unknown("TreasureRelics.SharedBagUnavailable");
            var prediction = TreasureRelicPredictor.Predict(RuntimeProfileRegistry.Select(plan.Detection), rootHash,
                initial, acquired, condition.StartOrdinal + condition.Count - 1);
            if (prediction.Status == SeedDomainEvaluationStatus.Unsupported)
                return SearchQueryEvaluation.Unsupported("TreasureRelics." + prediction.IssueCode);
            if (prediction.Status != SeedDomainEvaluationStatus.Evaluated || prediction.Precision != PredictionPrecision.Exact)
                return SearchQueryEvaluation.Unknown("TreasureRelics.SharedBagNotExact:" + prediction.IssueCode);
            var usedEncounters = new HashSet<int>();
            foreach (var (slot, target) in remaining)
            {
                var found = prediction.Entries.FirstOrDefault(entry => entry.RelicKey == target &&
                    entry.EncounterOrdinal >= condition.StartOrdinal &&
                    entry.EncounterOrdinal < condition.StartOrdinal + condition.Count &&
                    (condition.OrderMode == TreasureRelicOrderMode.Unordered || entry.EncounterOrdinal == condition.StartOrdinal + slot) &&
                    !usedEncounters.Contains(entry.EncounterOrdinal));
                if (found is null) return SearchQueryEvaluation.NoMatch("TreasureRelics.TargetMismatch.Slot" + (slot + 1));
                usedEncounters.Add(found.EncounterOrdinal);
                evidence.Add(Evidence("TreasureRelicAtEncounter", "TreasureRelics.Slot" + (slot + 1), target, found.EncounterOrdinal));
            }
            return SearchQueryEvaluation.Match(evidence);
        }

        // Neither a missing observation nor a partial success may hide a proved
        // independent mismatch; both obligations must match the same opening.
        return ProductionExactSearchEvaluator.CombineIndependentEvaluations(UnknownRooms(), Treasure());
    }
}
