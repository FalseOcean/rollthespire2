namespace RolltheSpire2.Search.FamilyExecution;

// Rfull's global NextInt is the existing historical integer shortcut. Change
// only its binary64-to-binary32 rarity draw. Generic arrivals additionally own
// the C donor, whose genuine binary64 operations use its audited adapter.
internal static class RelicFullGpuNumericalSource
{
    internal static string IntegerRne(string source, bool genericReplay)
    {
        source = GpuIntegerNumerics.NormalizeLines(source);
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "float rfull_next_float() {\n" +
            "    return float(double(rfull_next_u64() >> 11u) * 1.1102230246251565e-16lf);\n}",
            // Generic Rfull places its rarity function before the C donor,
            // which supplies the single shared helper definition later.
            (genericReplay ? "float rt2_next_float_from_words_u32(uvec2 raw);\n" : GpuIntegerNumerics.UnitFloatSource + "\n") +
            "float rfull_next_float() {\n" +
            "    uint64_t draw = rfull_next_u64();\n" +
            "    return rt2_next_float_from_words_u32(uvec2(uint(draw), uint(draw >> 32u)));\n}", "Rfull");
        // This adapter preserves the C donor's RT2_NEXT_INT_VARIANT branches,
        // converts its unit-float primitive and retains consume-only draws.
        if (genericReplay) source = CombatRewardIntegerSource.Adapt(source);
        return GpuIntegerNumerics.RequireNoFloat64(source, "Rfull");
    }
}
