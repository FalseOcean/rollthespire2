namespace RolltheSpire2.Search.FamilyExecution;

// Adapt only the assembled W donor. Native source does not pass through this
// adapter. Every NextInt retains the donor's binary64 product rounding, and
// every encounter attempt (including an empty bag) consumes one raw RNG draw.
internal static class WorldGpuNumericalSource
{
    internal static string IntegerRne(string source)
    {
        source = GpuIntegerNumerics.NormalizeLines(source);
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "double next_double() {\n" +
            "    return double(next_u64() >> 11u) * 1.1102230246251565e-16lf;\n" +
            "}\n" +
            "uint next_int(uint bound) {\n" +
            "    return uint(next_double() * double(bound));\n" +
            "}",
            GpuIntegerNumerics.NextIntSource + "\n" +
            "uint next_int(uint bound) {\n" +
            "    uint64_t draw = next_u64();\n" +
            "    return rt2_next_int_rne_raw32(uint(draw), uint(draw >> 32u), bound);\n" +
            "}", "W.NextInt");
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "uint candidate_index = uint(next_double() * double(count));",
            "uint candidate_index = next_int(count);", "W.EncounterRetry");
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "double draw_value = next_double();\n" +
            "            if (count > 0u) selected_bag_index = int(draw_value * double(count));",
            "uint64_t draw_value = next_u64();\n" +
            "            if (count > 0u) selected_bag_index = int(rt2_next_int_rne_raw32(uint(draw_value), uint(draw_value >> 32u), count));",
            "W.EncounterEmptyBag");
        source = GpuIntegerNumerics.RemoveFloat64Extension(source, "W");
        return GpuIntegerNumerics.RequireNoFloat64(source, "W");
    }
}
