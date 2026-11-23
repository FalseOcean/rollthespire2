using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Runtime;

internal static class EncounterQueueMatcher
{
    internal static SearchQueryEvaluation Evaluate(IReadOnlyList<EncounterQueueSearchCondition> conditions,
        WorldPredictionResult? world, WorldAuthoritySnapshot? authority)
    {
        string? unknown = null;
        var evidence = new List<SearchMatchEvidence>();
        foreach (var condition in conditions)
        {
            var generation = authority?.Beta109Generation;
            if (generation is { SelectedActsExact: true } && condition.Act > 0 && condition.Act <= generation.SelectedActs.Count)
            {
                ModelKey selected = generation.SelectedActs[condition.Act - 1];
                if (selected.IsValid && condition.VariantKey is { } required && selected != required)
                    return SearchQueryEvaluation.NoMatch("EncounterQueue.VariantMismatch");
                var shape = generation.OrderedActCatalog.FirstOrDefault(a => a.ActKey == selected);
                if (condition.Kind == EncounterQueueKind.Elite && generation.WorldGenerationHooksExact &&
                    shape is { EncounterRetryShapeExact: true, EliteEncounterSlots: 15 } && condition.RangeValue > 15)
                    return SearchQueryEvaluation.NoMatch("EncounterQueue.PositionAbsent");
            }
            var sequence = world?.EncounterSequences.SingleOrDefault(s => s.Act == condition.Act);
            if (sequence is null || sequence.Precision != PredictionPrecision.Exact)
            { unknown ??= sequence is null ? "EncounterQueue.ObservationMissing:Act" + condition.Act :
                string.IsNullOrEmpty(sequence.IssueCode) ? "EncounterQueue.Precision:" + sequence.Precision : sequence.IssueCode; continue; }
            if (condition.VariantKey is { } variant && sequence.ActKey != variant)
                return SearchQueryEvaluation.NoMatch("EncounterQueue.VariantMismatch");
            var act = authority?.Beta109Generation?.OrderedActCatalog.FirstOrDefault(a => a.Act == condition.Act && a.ActKey == sequence.ActKey);
            if (condition.Kind != EncounterQueueKind.Elite && act is null) { unknown ??= "EncounterQueue.ActShapeMissing:Act" + condition.Act; continue; }
            int weakCount = act is not null && act.WeakEncounters.Count != 0 ? act.WeakEncounterSlots : 0;
            var queue = condition.Kind == EncounterQueueKind.Elite ? sequence.Elite :
                condition.Kind == EncounterQueueKind.Weak ? sequence.Normal.Take(weakCount).ToArray() :
                sequence.Normal.Skip(weakCount).ToArray();
            if (queue.Count < condition.RangeValue) return SearchQueryEvaluation.NoMatch("EncounterQueue.PositionAbsent");
            var observed = condition.RangeMode == SearchSequenceRangeMode.FirstN ? queue.Take(condition.RangeValue) : queue.Skip(condition.RangeValue - 1).Take(1);
            var allowed = condition.AllowedKeys.ToHashSet(ModelKeyComparer.Instance);
            if (observed.Any(entry => !allowed.Contains(entry.EncounterKey)))
                return SearchQueryEvaluation.NoMatch("EncounterQueue.OutsideAllowedSet");
            evidence.AddRange(observed.Select(entry => new SearchMatchEvidence("EncounterQueueMatched", entry.EncounterKey,
                $"encounter:act{condition.Act}:{condition.Kind}:{condition.RangeMode}{condition.RangeValue}", Act: condition.Act,
                Ordinal: entry.Ordinal, AuthorityFingerprint: authority?.SnapshotFingerprint ?? "",
                ConditionId: $"encounter-{condition.Act}-{condition.Kind}-{condition.RangeMode}-{condition.RangeValue}")));
        }
        return unknown is not null ? SearchQueryEvaluation.Unknown(unknown) : SearchQueryEvaluation.Match(evidence);
    }
}
