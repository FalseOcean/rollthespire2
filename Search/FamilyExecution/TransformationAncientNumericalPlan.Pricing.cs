namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class TransformationAncientNumericalPlan
{
    internal readonly record struct WorkEstimate(double? Survival, double ExpectedDraws, string Evidence);
    private readonly record struct DrawState(ulong Counts, int MissingTargets, int MissingRare);

    // Pre-hashed, 65,536-input windows on the i9-13900HX. Each ratio is the
    // median across eight card/rarity and material-count shapes at this budget.
    private static readonly (int Workers, double Ratio)[] WorkerAnchors =
        [(1, 1), (2, .50), (4, .275), (8, .216), (12, .203), (16, .184), (24, .167), (32, .162)];

    internal static (double Ratio, string Evidence) WorkerScale(int workers, double population)
    {
        // CpuFilterExecution uses at most one partition per 1,024 inputs.
        int parts = Math.Clamp((int)(Math.Min(65536, population) / 1024), 1, workers);
        int i = 1;
        while (WorkerAnchors[i].Workers < parts) i++;
        var (lo, first) = WorkerAnchors[i - 1];
        var (hi, last) = WorkerAnchors[i];
        return (first + (last - first) * (parts - lo) / (hi - lo),
            $"WorkerScale=AncientFixedPoolPrehashed.20261121;NumericalPartitions={parts}");
    }

    // Scheduling only. The source pool includes one entry per possible index, including
    // duplicate card identities. Each independent draw uses that same distribution.
    internal WorkEstimate EstimateWork()
    {
        const string evidence = "T.AncientFixedPool.WithReplacementStoppingTime";
        if (_drawCount == 0) return new(0, 0, evidence);

        // Saturating one counter per named card preserves under/over-counts without
        // carrying permutations. Bound the work of planning a large user-defined box.
        var fallback = new WorkEstimate(null, _drawCount, "T.AncientFixedPool.StateBudget.FullDrawUpperBound");
        var buckets = _candidateRules.GroupBy(rule => rule)
            .Select(group => (Rule: group.Key, Weight: (double)group.Count() / _candidateRules.Length)).ToArray();
        var strides = new ulong[_counterCount];
        ulong stride = 1;
        foreach (var (rule, _) in buckets)
        {
            if (rule.Counter < 0) continue;
            uint radix = (uint)CounterLimit(rule) + 1;
            if (stride > ulong.MaxValue / radix) return fallback;
            strides[rule.Counter] = stride;
            stride *= radix;
        }

        var current = new Dictionary<DrawState, double> { [new(0, _minimumTargets, _minimumRare)] = 1 };
        var next = new Dictionary<DrawState, double>();
        double accepted = 0, draws = 0;
        bool canAccept = false;
        int transitionsLeft = 32_768;
        for (int remaining = _drawCount; current.Count != 0; remaining--)
        {
            foreach (var (state, mass) in current)
            {
                if (state.MissingTargets > remaining || state.MissingRare > remaining) continue;
                if (state.MissingTargets == 0 && state.MissingRare == 0 && (remaining == 0 || !_requiresRareRemainder))
                {
                    canAccept = true;
                    accepted += mass;
                    continue;
                }
                if ((transitionsLeft -= buckets.Length) < 0) return fallback;
                draws += mass;
                foreach (var (rule, weight) in buckets)
                {
                    if (rule.Maximum == 0) continue;
                    ulong counts = state.Counts;
                    int missing = state.MissingTargets;
                    if (rule.Counter >= 0)
                    {
                        int limit = CounterLimit(rule);
                        ulong step = strides[rule.Counter];
                        int seen = (int)(counts / step % ((ulong)limit + 1));
                        if (seen == rule.Maximum && rule.Maximum < _drawCount) continue;
                        if (seen < limit) counts += step;
                        if (seen < rule.Minimum) missing--;
                    }
                    var after = new DrawState(counts, missing, Math.Max(0, state.MissingRare - (rule.Rare ? 1 : 0)));
                    next[after] = next.GetValueOrDefault(after) + mass * weight;
                }
            }
            (current, next) = (next, current);
            next.Clear();
        }
        // A possible but underflowed rare tail is not a zero-survival projection.
        return new(canAccept && accepted == 0 ? null : Math.Clamp(accepted, 0, 1),
            Math.Clamp(draws, 0, _drawCount), evidence);
    }

    private int CounterLimit(CandidateRule rule) => rule.Maximum < _drawCount ? rule.Maximum : rule.Minimum;
}
