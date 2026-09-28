namespace RolltheSpire2.Search.Predictability;

internal static class SearchPredictabilityMath
{
    public static double ExpectedRootsToFirst(double probability) => probability > 0d ? 1d / probability : double.PositiveInfinity;

    public static double ExpectedRootsToTarget(double probability, int targetMatches) =>
        probability > 0d ? Math.Max(1, targetMatches) / probability : double.PositiveInfinity;

    public static double ProbabilityAtLeastOne(double probability, double roots)
    {
        if (!(roots > 0d)) return 0d;
        if (probability <= 0d) return 0d;
        if (probability >= 1d) return 1d;
        double logMiss = roots * double.LogP1(-probability);
        return -double.ExpM1(logMiss);
    }

    // Independent-root approximation only. This is never an Exact probability authority.
    public static double ProbabilityAtLeastTarget(double probability, long roots, int targetMatches)
    {
        int target = Math.Max(1, targetMatches);
        if (roots <= 0) return 0d;
        if (target > roots) return 0d;
        if (probability <= 0d) return 0d;
        if (probability >= 1d) return 1d;
        if (target == 1) return ProbabilityAtLeastOne(probability, roots);

        // Compute lower binomial tail P[X < target] by stable recurrence from P[X=0].
        double logP0 = roots * Math.Log(1d - probability);
        if (logP0 < -745d) return 1d; // lower tail is negligible in double precision.
        double term = Math.Exp(logP0);
        double lower = term;
        double odds = probability / (1d - probability);
        for (int k = 0; k < target - 1; k++)
        {
            term *= ((double)(roots - k) / (k + 1d)) * odds;
            lower += term;
            if (!double.IsFinite(lower) || lower >= 1d) return 0d;
        }
        return Math.Clamp(1d - lower, 0d, 1d);
    }
}
