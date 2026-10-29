using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowFamilyGpuExecutor
{
    private Rid _prefixHashTable;
    private readonly NeowInternalFilterOrderPlan _curseFirstPlan;
    private bool CurseFirstEnabled => _optimizations.CurseFirst && !_privateInput;
    private int StagedDispatchCount => CurseFirstEnabled ? 4 : 3;
    internal string OptimizationRevisionFor(bool compact) => _optimizations.Applicable(_plan, compact || _privateInput).Revision +
        GpuRngRotation.Revision;

    internal static bool SupportsDrawIntervals(NeowFamilyGpuPlan plan, bool compact)
    {
        if (plan.IsMultiplayer) return false;
        if (!compact && plan.UsesStagedDense && plan.Meta[39] is > 0 and <= 10 &&
            NeowStaticDrawGateCompiler.TryCompileRanks((int)plan.Meta[39],
                i => (plan.Meta[69] & (1u << i)) == 0 && plan.Meta[71 + i] != uint.MaxValue, 2, out _)) return true;
        return plan.UsesLeafyPreGate && plan.Conditions[20] == 2 &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Strike, (int)plan.Meta[58], [plan.Conditions[21]], 2, out _) &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Strike, (int)plan.Meta[58], [plan.Conditions[22]], 2, out _) &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Defend, (int)plan.Meta[59], [plan.Conditions[21]], 2, out _) &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Defend, (int)plan.Meta[59], [plan.Conditions[22]], 2, out _);
    }

    private string ApplyOptimizations(string source, int stage)
    {
        if (_plan.IsMultiplayer) return source;
        if (_optimizations.GpuPrefix && stage == 0)
            source = source.Replace("#define RT2_PREFIX_HASH_CACHE 0", "#define RT2_PREFIX_HASH_CACHE 1", StringComparison.Ordinal);
        if (CurseFirstEnabled)
            source = source.Replace("#define N_INTERNAL_ORDER 0", "#define N_INTERNAL_ORDER 1", StringComparison.Ordinal);
        source = source.Replace("/*__RT2_N_INTERNAL_FILTER_ORDER__*/", _curseFirstPlan.Source, StringComparison.Ordinal);
        return _optimizations.DrawIntervals ? ApplyDrawIntervals(source, stage, _plan) : source;
    }

    // Compile exactly the existing GPU floor(mantissa * bound / 2^53) map.
    // This does not replace CPU/game binary64 behavior or continuation state.
    internal static string ApplyDrawIntervals(string source, int stage, NeowFamilyGpuPlan plan)
    {
        if (plan.IsMultiplayer || stage is not (0 or 3)) return source;
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var helpers = new System.Text.StringBuilder();
        if (stage == 0 && plan.Meta[39] is > 0 and <= 10 &&
            NeowStaticDrawGateCompiler.TryCompileRanks((int)plan.Meta[39],
                i => (plan.Meta[69] & (1u << i)) == 0 && plan.Meta[71 + i] != uint.MaxValue, 2, out var curse))
        {
            helpers.Append("bool rt2_interval_curse(uint64_t root) {\n")
                .Append(" uint64_t s=root+plan_u64(31u)+make_u64(0x7f4a7c15u,0x9e3779b9u);\n")
                .Append(" uint64_t s1=splitmix_next(s); uint64_t m=(rotl64(s1*uint64_t(5u),7u)*uint64_t(9u))>>11u;\n")
                .Append(" return ").Append(curse!.GlslExpression("m")).Append(";\n}\n");
            const string predicate = "draw_neow_curse(root,event_rng,curse_ordinal,curse) && (plan_value(69u)&(1u<<curse_ordinal))==0u";
            if (!source.Contains(predicate, StringComparison.Ordinal)) throw new InvalidOperationException("N.IntervalCurseSeamChanged");
            source = source.Replace(predicate, "rt2_interval_curse(root)", StringComparison.Ordinal);
        }
        if (plan.UsesLeafyPreGate && plan.Conditions[20] == 2 &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Strike, (int)plan.Meta[58], [plan.Conditions[21]], 2, out var s0) &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Strike, (int)plan.Meta[58], [plan.Conditions[22]], 2, out var s1) &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Defend, (int)plan.Meta[59], [plan.Conditions[21]], 2, out var d0) &&
            NeowStaticDrawGateCompiler.TryCompilePool(plan.Defend, (int)plan.Meta[59], [plan.Conditions[22]], 2, out var d1))
        {
            // Overrides can isolate lazy initialization. Intervals alone
            // initialize the same four words as the original Leafy gate.
            helpers.Append("bool rt2_interval_leafy(uint64_t root) {\n uint64_t seed=root+plan_u64(37u);\n uint64_t first,second;\n")
                .Append("#if RT2_N_LEAFY_LAZY\n uint64_t mix=seed+make_u64(0x7f4a7c15u,0x9e3779b9u); uint64_t initial_s1=splitmix_next(mix);\n")
                .Append(" first=(rotl64(initial_s1*uint64_t(5u),7u)*uint64_t(9u))>>11u;\n#else\n RngState r=rng_initialize(seed); first=next_u64(r)>>11u;\n#endif\n")
                .Append(" bool a=").Append(s0!.GlslExpression("first")).Append(", b=").Append(s1!.GlslExpression("first")).Append("; if(!a&&!b)return false;\n")
                .Append("#if RT2_N_LEAFY_LAZY\n mix=seed; uint64_t initial_s0=splitmix_next(mix); mix+=make_u64(0x7f4a7c15u,0x9e3779b9u); uint64_t initial_s2=splitmix_next(mix);\n")
                .Append(" second=(rotl64((initial_s1^initial_s2^initial_s0)*uint64_t(5u),7u)*uint64_t(9u))>>11u;\n#else\n second=next_u64(r)>>11u;\n#endif\n")
                .Append(" return (a&&(").Append(d1!.GlslExpression("second")).Append("))||(b&&(").Append(d0!.GlslExpression("second")).Append("));\n}\n");
            const string call = "if (leafy_pre_gate(root) &&";
            if (stage == 0) source = source.Replace(call, "if (rt2_interval_leafy(root) &&", StringComparison.Ordinal);
            else source = source.Replace("if (!leafy_pre_gate(root)) return false;", "if (!rt2_interval_leafy(root)) return false;", StringComparison.Ordinal);
        }
        const string anchor = "#if N_DIRECT_NESTED == 101";
        int insert = source.IndexOf(anchor, StringComparison.Ordinal);
        if (insert < 0) throw new InvalidOperationException("N.IntervalHelperSeamChanged");
        return source.Insert(insert, helpers.ToString());
    }
}
