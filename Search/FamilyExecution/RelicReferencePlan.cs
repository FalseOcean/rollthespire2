using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// Immutable canonical-reference inputs. Every prediction is local to its root;
// runtime notification deduplication is supplied by the selected CPU handle.
internal sealed class RelicReferencePlan(ExactSearchEvaluationProjection evaluation,
    WorldAuthoritySnapshot? world, IRuntimeProfile profile)
{
    private readonly int _preview = Math.Max(
        evaluation.RelicSequenceConditions.Select(c => c.RangeValue).DefaultIfEmpty(1).Max(),
        evaluation.RelicShopSequenceConditions.Select(c => c.Count).DefaultIfEmpty(1).Max());

    internal static ExactSearchEvaluationProjection ProjectEvaluation(SearchModel model) =>
        ExactSearchEvaluationProjection.Empty with {
            RelicSequenceConditions = model.Evaluation.RelicSequenceConditions,
            RelicShopSequenceConditions = model.Evaluation.RelicShopSequenceConditions,
            CapsuleContainedRelics = model.Evaluation.CapsuleContainedRelics,
            RequireWhetstone = model.Evaluation.RequireWhetstone,
            RequireWarPaint = model.Evaluation.RequireWarPaint
        };

    internal bool Matches(ulong root, Action<string>? onUnresolved = null)
    {
        var prediction = RelicSequencePredictor.PredictFromRootHash(profile, root, "CpuReference", world, _preview);
        var match = MatchesPrediction(prediction);
        if (match is null) onUnresolved?.Invoke($"R.CpuReferenceNotExact;status={prediction.Status}");
        return match ?? true;
    }

    internal bool Reference(ulong root)
    {
        var prediction = RelicSequencePredictor.PredictFromRootHash(profile, root, "CpuReference", world, _preview);
        return MatchesPrediction(prediction) ?? throw new InvalidDataException("R.ReferenceNotExact:" + prediction.Status);
    }

    private bool? MatchesPrediction(RelicSequencePredictionResult prediction)
    {
        if (prediction.Status != SeedDomainEvaluationStatus.Evaluated) return null;
        bool? result = true;
        foreach (var condition in evaluation.RelicSequenceConditions.Where(c => !c.IsEmpty))
        {
            var lane = prediction.GetPlayerLane(condition.Lane);
            if (lane?.Precision != PredictionPrecision.Exact) { result = null; continue; }
            var range = condition.RangeMode == SearchSequenceRangeMode.FirstN
                ? lane.Entries.Where(item => item.Position <= condition.RangeValue).Select(item => item.RelicKey).ToArray()
                : lane.Entries.Where(item => item.Position == condition.RangeValue).Select(item => item.RelicKey).ToArray();
            if (condition.RangeMode == SearchSequenceRangeMode.ExactSlot && range.Length != 1) return false;
            if (!QueryKeySetPredicate.MatchesKeySet(range, condition.Keys)) return false;
        }
        var shop = prediction.GetPlayerLane(RelicSequenceKind.Shop);
        foreach (var condition in evaluation.RelicShopSequenceConditions.Where(c => !c.IsEmpty))
        {
            if (shop?.Precision != PredictionPrecision.Exact) { result = null; continue; }
            var actual = shop.Entries.Where(item => item.Position <= condition.Count).OrderBy(item => item.Position).Select(item => item.RelicKey).ToArray();
            if (actual.Length < condition.Count || !ShopSequenceSemantics.Matches(actual, condition.Count, condition.OrderMode, condition.Slots)) return false;
        }
        return result;
    }
}
