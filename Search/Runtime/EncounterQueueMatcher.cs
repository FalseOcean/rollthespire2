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
            var sequence = world?.EncounterSequences.SingleOrDefault(s => s.Act == condition.Act);
            if (sequence is null || sequence.Precision != PredictionPrecision.Exact)
            { unknown ??= sequence is null ? "EncounterQueue.ObservationMissing:Act" + condition.Act :
                string.IsNullOrEmpty(sequence.IssueCode) ? "EncounterQueue.Precision:" + sequence.Precision : sequence.IssueCode; continue; }
            if (condition.VariantKey is { } variant && sequence.ActKey != variant)
                return SearchQueryEvaluation.NoMatch("EncounterQueue.VariantMismatch");
            var act = authority?.Beta109Generation?.OrderedActCatalog.FirstOrDefault(a => a.Act == condition.Act && a.ActKey == sequence.ActKey);
            if (condition.Kind != EncounterQueueKind.Elite && act is null) { unknown ??= "EncounterQueue.ActShapeMissing:Act" + condition.Act; continue; }
            var queue = condition.Kind == EncounterQueueKind.Elite ? sequence.Elite :
                condition.Kind == EncounterQueueKind.Weak ? sequence.Normal.Take(act!.WeakEncounterSlots).ToArray() :
                sequence.Normal.Skip(act!.WeakEncounterSlots).ToArray();
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
