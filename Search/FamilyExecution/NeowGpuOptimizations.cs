namespace RolltheSpire2.Search.FamilyExecution;

// Query-owned immutable options; explicit overrides support same-build comparisons.
// Independently adapted from the
// methods in shion0528 (Egoistic今宵) resource.17, commit da82f7f9023c3d3598d50b8642e93f2801c37ccc.
internal readonly record struct NeowGpuOptimizations(bool LeafyLazy = false, bool DrawIntervals = false,
    bool GpuPrefix = false, bool CurseFirst = false)
{
    internal static NeowGpuOptimizations Production => new(true, true, true, true);
    internal static NeowGpuOptimizations Capture() => Parse(Environment.GetEnvironmentVariable("RT2_SHION_GPU_TRIAL"));
    internal static NeowGpuOptimizations Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "on") return Production;
        var result = new NeowGpuOptimizations();
        foreach (string token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            result = token switch {
                "off" => result,
                "leafy-lazy" => result with { LeafyLazy = true },
                "draw-intervals" => result with { DrawIntervals = true },
                "gpu-prefix" => result with { GpuPrefix = true },
                "n-curse-first" => result with { CurseFirst = true },
                _ => throw new InvalidOperationException("N.UnknownShionGpuTrial:" + token)
            };
        return result;
    }

    internal NeowGpuOptimizations Applicable(NeowFamilyGpuPlan plan, bool compact, bool ordinary = true) =>
        !ordinary || plan.IsMultiplayer ? default : this with {
            LeafyLazy = LeafyLazy && plan.UsesLeafyPreGate,
            DrawIntervals = DrawIntervals && NeowFamilyGpuExecutor.SupportsDrawIntervals(plan, compact),
            GpuPrefix = GpuPrefix && !compact && plan.UsesStagedDense,
            CurseFirst = CurseFirst && !compact && NeowInternalFilterOrder.Compile(plan).Enabled
        };
    internal string Revision => (LeafyLazy ? ".ShionLeafyLazy.v1" : "") +
        (DrawIntervals ? ".ShionDrawIntervals53.v1" : "") + (GpuPrefix ? ".ShionGpuPrefix.v1" : "") +
        (CurseFirst ? ".ShionCurseFirst.v1" : "");

    // Generated operation structure, not target IDs or bucket positions. Old
    // pool membership alone cannot distinguish one interval from two intervals.
    internal string DescribeWork(NeowFamilyGpuPlan plan, bool compact)
    {
        if (this == default) return "";
        static int[] Arity(NeowStaticDrawGateCompiler.Gate gate) => gate.Intervals.Select(i =>
            (i.LowerInclusive == 0 ? 0 : 1) + (i.UpperExclusive == NeowStaticDrawGateCompiler.MantissaSpace ? 0 : 1)).ToArray();
        int[]? curse = null;
        int[][]? leafy = null;
        if (DrawIntervals && !compact && plan.UsesStagedDense && plan.Meta[39] is > 0 and <= 10 &&
            NeowStaticDrawGateCompiler.TryCompileRanks((int)plan.Meta[39],
                i => (plan.Meta[69] & (1u << i)) == 0 && plan.Meta[71 + i] != uint.MaxValue, 2, out var c)) curse = Arity(c!);
        if (DrawIntervals && plan.UsesLeafyPreGate && plan.Conditions[20] == 2 &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Strike, (int)plan.Meta[58], [plan.Conditions[21]], 2, out var s0) &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Strike, (int)plan.Meta[58], [plan.Conditions[22]], 2, out var s1) &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Defend, (int)plan.Meta[59], [plan.Conditions[21]], 2, out var d0) &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Defend, (int)plan.Meta[59], [plan.Conditions[22]], 2, out var d1))
            leafy = [Arity(s0!), Arity(s1!), Arity(d0!), Arity(d1!)];
        var finalCurse = CurseFirst ? NeowInternalFilterOrder.Compile(plan) : null;
        return ";N.GeneratedWork.20261027.v1:" + System.Text.Json.JsonSerializer.Serialize(new {
            CurseIntervalComparisons = curse, LeafyIntervalComparisons = leafy,
            FinalCurseAdvances = finalCurse?.NicheAdvances, FinalCurseAccepts = finalCurse?.HasAcceptedCurse
        });
    }
}
