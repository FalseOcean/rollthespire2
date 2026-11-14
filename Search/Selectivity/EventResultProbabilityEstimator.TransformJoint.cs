using System.Numerics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class EventResultProbabilityEstimator
{
    // One event's shared draw(s), including Morphic across owners with different
    // ordered pools. The common random number chooses an interval in each pool.
    internal static SearchSelectivityEstimate EstimateTransformJoint(IReadOnlyList<EventResultSearchCondition> conditions)
    {
        SearchSelectivityEstimate Missing(string reason) => SearchSelectivityEstimate.Unpriced(
            "Probability.EventResult.TransformJoint." + reason, "One closed event-local draw context is required.");
        if (conditions.Count == 0) return Missing("NoConditions");
        var first = conditions[0];
        if (!EventResultTransformSemantics.IsTransform(first.Kind) || conditions.Any(c => !c.IsValid || c.Kind != first.Kind))
            return Missing("DifferentEvents");
        bool shared = first.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains;
        if (!shared && conditions.Any(c => c.MorphicGroveScenario?.Authority.PlayerSlotIndex != first.MorphicGroveScenario?.Authority.PlayerSlotIndex))
            return Missing("DifferentPersonalStreams");
        var pools = conditions.Select(EventResultTransformSemantics.ClosedPool).ToArray();
        if (pools.Any(p => p is null || p.Count == 0)) return Missing("AuthorityMissing");
        var boundaries = new SortedSet<double> { 0, 1 };
        foreach (var pool in pools)
            for (int i = 1; i < pool!.Count; i++) boundaries.Add((double)i / pool.Count);
        var points = boundaries.ToArray();
        var masses = new Dictionary<BigInteger, double>();
        for (int i = 1; i < points.Length; i++)
        {
            double middle = (points[i - 1] + points[i]) / 2;
            BigInteger mask = BigInteger.Zero;
            for (int c = 0; c < conditions.Count; c++)
            {
                var pool = pools[c]!;
                var key = pool[Math.Min(pool.Count - 1, (int)(middle * pool.Count))].CardKey;
                int bits = (key == conditions[c].TargetKey ? 1 : 0) | (key == conditions[c].MorphicGroveSecondCard ? 2 : 0);
                mask |= (BigInteger)bits << (2 * c);
            }
            masses[mask] = masses.GetValueOrDefault(mask) + points[i] - points[i - 1];
        }
        int draws = EventResultTransformSemantics.DrawCount(first.Kind);
        bool Accept(BigInteger a, BigInteger b)
        {
            for (int c = 0; c < conditions.Count; c++)
            {
                int x = (int)((a >> (2 * c)) & 3), y = (int)((b >> (2 * c)) & 3);
                if (conditions[c].MorphicGroveSecondCard.HasValue)
                {
                    if (!((x & 1) != 0 && (y & 2) != 0 || (x & 2) != 0 && (y & 1) != 0)) return false;
                }
                else if ((x & 1) == 0 && (draws == 1 || (y & 1) == 0)) return false;
            }
            return true;
        }
        double probability = draws == 1 ? masses.Where(m => Accept(m.Key, BigInteger.Zero)).Sum(m => m.Value) :
            masses.Sum(a => masses.Where(b => Accept(a.Key, b.Key)).Sum(b => a.Value * b.Value));
        if (first.Kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains) probability /= 3;
        return SearchSelectivityEstimate.Exact(Math.Clamp(probability, 0, 1), SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.EventResult.SharedTransformDrawJoint", "One event draw sequence, with each owner's ordered source pool and target predicate; Trial case mass is paid once.",
            ["EventOccurrenceNotProven=true", "SameEventDrawsPaidOnce=true", "DifferentOwnerPoolsRetained=true", "SuccessiveRandomDrawsUseProductModel=true"]);
    }
}
