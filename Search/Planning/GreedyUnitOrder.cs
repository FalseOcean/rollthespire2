namespace RolltheSpire2.Search.FamilyExecution;

// Scheduling evidence only. Missing cost or probability remains Unknown and
// never removes an obligation; stable registration order resolves missing data.
internal static class GreedyUnitOrder
{
    internal static double? Score(double? cost, double? survival) =>
        cost is >= 0d && double.IsFinite(cost.Value) && survival is >= 0d and <= 1d && double.IsFinite(survival.Value)
            ? survival == 1d ? double.PositiveInfinity : cost.Value / (1d - survival.Value) : null;

    internal static T Select<T>(IReadOnlyList<T> available, Func<T, double?> hardwareCost,
        Func<T, double?> survival, Func<T, double?> analyticalCost)
    {
        if (available.Count == 0) throw new ArgumentException("At least one scheduling unit is required.", nameof(available));
        foreach (var cost in new[] { hardwareCost, analyticalCost })
        {
            int best = -1;
            double bestScore = double.PositiveInfinity;
            for (int index = 0; index < available.Count; index++)
            {
                double? score = Score(cost(available[index]), survival(available[index]));
                if (score.HasValue && (best < 0 || score.Value < bestScore))
                {
                    best = index;
                    bestScore = score.Value;
                }
            }
            if (best >= 0) return available[best];
        }
        return available[0];
    }
}
