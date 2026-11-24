namespace RolltheSpire2.Search.FamilyExecution;

// Both generic and Carry8 T bodies assemble the E donor's binary64 NextInt.
// This is distinct from the historical integer shortcut used by ordinary R.
internal static class TransformationAggregateGpuNumericalSource
{
    internal static string IntegerRne(string source)
    {
        source = GpuIntegerNumerics.NormalizeLines(source);
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "uint next_int(uint bound) {\n" +
            "    const double unit = 1.1102230246251565e-16lf;\n" +
            "    double unit_sample = double(next_u64() >> 11u) * unit;\n" +
            "    return uint(unit_sample * double(bound));\n}",
            GpuIntegerNumerics.NextIntSource + "\n" +
            "uint next_int(uint bound) {\n" +
            "    uint64_t draw = next_u64();\n" +
            "    return rt2_next_int_rne_raw32(uint(draw), uint(draw >> 32u), bound);\n}", "T");
        return GpuIntegerNumerics.RequireNoFloat64(GpuIntegerNumerics.RemoveFloat64Extension(source, "T"), "T");
    }
}
