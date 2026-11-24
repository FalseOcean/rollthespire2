using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed partial class RoutePartialState
    {
        private sealed record UpgradeReads(string Node, string Operation,
            PredictorKnowledgeFactId Population, PredictorKnowledgeFactId Candidates,
            PredictorKnowledgeFactId Cursor, PredictorKnowledgeFactId Consumption,
            bool CursorUnresolved, bool ConsumptionUnresolved);

        private UpgradeReads ReadUpgrade(PredictorState state, ModelKey relic, PredictorDeckProjection projection)
        {
            string node = state.Position.NodeId, operation = "obtain." + relic.Entry + "." + state.Relics.Length;
            string key = operation + "." + DeckKey(projection.Scope);
            var count = RecordInteger(key + ".count", node, projection.Count, ProjectionCauses(projection, count: true),
                "enumerate eligible " + relic.Entry + " population");
            var causes = ProjectionCauses(projection, count: false);
            var ordered = projection.OrderedCandidatesKnown
                ? Facts.RecordResult(key + ".candidates", node, string.Join(" | ", projection.KnownCandidates.Select(CardDiagnostic)),
                    operation: "read eligible deck order")
                : !projection.KnownCandidates.IsEmpty
                    ? Facts.RecordPartial(key + ".candidates", node, PredictorRouteFactPhase.Results,
                        string.Join(" | ", projection.KnownCandidates.Select(CardDiagnostic)), causes,
                        [new("subset", "Guaranteed candidates in deck order; other eligible identities remain unresolved.")], operation: "read eligible deck order")
                    : Facts.RecordUnknown(key + ".candidates", node, PredictorRouteFactPhase.Results, causes,
                        operation: "read eligible deck order");
            var incoming = _streamOffsets[PredictorStream.Niche] + _streamBases[PredictorStream.Niche].Calls;
            var cursor = RecordInteger(operation + ".rng.Niche.before", node, incoming,
                StreamCauses(PredictorStream.Niche), "read incoming Niche cursor");
            var draws = projection.UnstableShuffleDraws;
            var countStatus = Facts.Fact(count).Status;
            PredictorKnowledgeFactId consumption;
            if (draws?.KnownValue is { } exact)
                consumption = Facts.RecordResult(operation + ".rng.Niche.consumed", node, exact,
                    operation: "UnstableShuffle draw count", allReadFactIds: [count],
                    derivation: "max(eligible population - 1, 0) is invariant across the retained choices.");
            else if (draws != null)
                consumption = Facts.RecordPartial(operation + ".rng.Niche.consumed", node, PredictorRouteFactPhase.Results,
                    Range(draws), provenDetails: [new("range-kind", "conservative enclosure")],
                    operation: "UnstableShuffle draw count", allReadFactIds: [count],
                    unresolvedReadFactIds: countStatus == PredictorKnowledgeStatus.Known ? [] : [count]);
            else consumption = Facts.RecordUnknown(operation + ".rng.Niche.consumed", node, PredictorRouteFactPhase.Results,
                ProjectionCauses(projection, count: true), operation: "UnstableShuffle draw count", allReadFactIds: [count],
                unresolvedReadFactIds: countStatus == PredictorKnowledgeStatus.Known ? [] : [count]);
            return new(node, operation, count, ordered, cursor, consumption,
                Facts.Fact(cursor).Status != PredictorKnowledgeStatus.Known,
                Facts.Fact(consumption).Status != PredictorKnowledgeStatus.Known);
        }

        private PredictorKnowledgeFactId RecordInteger(string key, string node, PredictorChoiceInt? value,
            ImmutableArray<PredictorCombatPendingCause> causes, string operation)
        {
            if (value?.KnownValue is { } known)
                return Facts.RecordResult(key, node, known, causes, operation: operation);
            if (value != null)
                return Facts.RecordPartial(key, node, PredictorRouteFactPhase.Results, Range(value), causes,
                    [new("range-kind", "conservative enclosure")], operation: operation);
            if (causes.IsEmpty) causes = [PredictorRouteCauseEvidence.CreateExplicit("OperationPropertyNotDerived", node,
                PredictorCombatDomain.None, PredictorPendingReasonKind.ImplementationGap,
                "No established integer projection for this operation.", [key], occurrenceKey: key)];
            return Facts.RecordUnknown(key, node, PredictorRouteFactPhase.Results, causes, operation: operation);
        }

        private void WriteUpgrade(UpgradeReads reads)
        {
            var progress = _streamOffsets[PredictorStream.Niche] + _streamBases[PredictorStream.Niche].Calls;
            var all = ImmutableArray.Create(reads.Cursor, reads.Consumption);
            var unresolved = ImmutableArray.CreateBuilder<PredictorKnowledgeFactId>();
            if (reads.CursorUnresolved) unresolved.Add(reads.Cursor);
            if (reads.ConsumptionUnresolved) unresolved.Add(reads.Consumption);
            PredictorKnowledgeFactId written;
            if (StreamKnown(PredictorStream.Niche))
                written = Facts.RecordResult(reads.Operation + ".rng.Niche.after", reads.Node, progress.KnownValue!.Value,
                    operation: "advance correlated Niche cursor", allReadFactIds: all,
                    derivation: "Incoming progress and this shuffle retain the same choice assignments; their sum is exact.");
            else if (!ExternalStreamCauses(PredictorStream.Niche).IsEmpty || _streamCauses.ContainsKey(PredictorStream.Niche))
                written = Facts.RecordUnknown(reads.Operation + ".rng.Niche.after", reads.Node, PredictorRouteFactPhase.Results,
                    StreamCauses(PredictorStream.Niche), operation: "advance correlated Niche cursor", allReadFactIds: all,
                    unresolvedReadFactIds: unresolved.ToImmutable());
            else written = Facts.RecordPartial(reads.Operation + ".rng.Niche.after", reads.Node, PredictorRouteFactPhase.Results,
                Range(progress), StreamCauses(PredictorStream.Niche), [new("range-kind", "conservative enclosure")],
                operation: "advance correlated Niche cursor", allReadFactIds: all,
                unresolvedReadFactIds: unresolved.ToImmutable());
            RecordStateWrite(reads.Node, "rng.Niche.calls", written);
        }
    }
}
