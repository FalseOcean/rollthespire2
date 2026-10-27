namespace RolltheSpire2.Search.FamilyExecution;

// The ordinary C donor's FP64 draws are either exact integer selection, a
// binary32 unit value, or a discarded draw. Keep each draw and every policy branch.
internal static class CombatRewardIntegerSource
{
    internal static string Adapt(string source)
    {
        source = GpuIntegerNumerics.RemoveFloat64Extension(GpuIntegerNumerics.NormalizeLines(source), "C");
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "uint cr_next_int_direct_fp64_from_mantissa(uint64_t x, uint bound) {\n" +
            "    double unit_value = double(x) * 1.1102230246251565e-16lf;\n" +
            "    return uint(unit_value * double(bound));\n}",
            GpuIntegerNumerics.NextIntSource + "\n" + GpuIntegerNumerics.UnitFloatSource + "\n" +
            "uint cr_next_int_direct_fp64_from_mantissa(uint64_t x, uint bound) {\n" +
            "    uint64_t raw = x << 11u;\n" +
            "    return rt2_next_int_rne_raw32(uint(raw), uint(raw >> 32u), bound);\n}", "C");
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "float cr_next_float(inout RngState rng) { return float(double(cr_next_u64(rng) >> 11u) * 1.1102230246251565e-16lf); }",
            "float cr_next_float(inout RngState rng) { uint64_t raw = cr_next_u64(rng); return rt2_next_float_from_words_u32(uvec2(uint(raw), uint(raw >> 32u))); }", "C");
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "double cr_next_double(inout RngState rng) { return double(cr_next_u64(rng) >> 11u) * 1.1102230246251565e-16lf; }\n",
            "", "C");
        // All current callers discard this value. Reusing the existing void
        // burn helper also makes a future value-consuming caller fail compilation.
        source = source.Replace("cr_next_double(", "cr_consume_double(", StringComparison.Ordinal);
        return GpuIntegerNumerics.RequireNoFloat64(source, "C");
    }
}
