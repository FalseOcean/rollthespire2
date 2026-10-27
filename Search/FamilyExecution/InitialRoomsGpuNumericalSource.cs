namespace RolltheSpire2.Search.FamilyExecution;

internal static class InitialRoomsGpuNumericalSource
{
    internal static string Adapt(string source)
    {
        const string owner = "InitialRooms";
        source = GpuIntegerNumerics.RemoveFloat64Extension(GpuIntegerNumerics.NormalizeLines(source), owner);
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "uint next_int(uint bound) {\n    const double unit = 1.1102230246251565e-16lf;\n    double unit_sample = double(next_u64() >> 11u) * unit;\n    return uint(unit_sample * double(bound));\n}",
            GpuIntegerNumerics.NextIntSource + "\nuint next_int(uint bound) {\n    uint64_t raw = next_u64();\n    return rt2_next_int_rne_raw32(uint(raw), uint(raw >> 32u), bound);\n}", owner);
        source = GpuIntegerNumerics.ReplaceUnique(source,
            "float next_float(){return float(double(next_u64()>>11u)*1.1102230246251565e-16lf);}",
            GpuIntegerNumerics.UnitFloatSource + "\nfloat next_float(){uint64_t raw=next_u64();return rt2_next_float_from_words_u32(uvec2(uint(raw),uint(raw>>32u)));}", owner);
        return GpuIntegerNumerics.RequireNoFloat64(source, owner);
    }
}
