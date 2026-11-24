namespace RolltheSpire2.Search.FamilyExecution;

// Source adaptation for ordinary solo and individual party donors, whose
// NextFloat expressions are identical. Native source never passes through this
// adapter. NextInt and RNG state transitions stay untouched.
internal static class NeowGpuNumericalSource
{
    private const string Float64Extension = "#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require\n";
    private const string NativeNextFloat = "float next_float(inout RngState rng) {\n" +
        "    return float(double(next_u64(rng) >> 11u) * 1.1102230246251565e-16lf);\n}";
    private const string IntegerNextFloat = "float next_float(inout RngState rng) {\n" +
        "    uint64_t draw = next_u64(rng);\n" +
        "    return rt2_next_float_from_words_u32(uvec2(uint(draw), uint(draw >> 32u)));\n}";

    internal static string IntegerUnitFloat(string source)
    {
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        RequireUnique(source, Float64Extension);
        RequireUnique(source, NativeNextFloat);
        string helper = FamilyGpuComputeUtility.LoadEmbeddedShader("NextFloatIntegerUnit.glsl");
        return source.Replace(Float64Extension, "", StringComparison.Ordinal)
            .Replace(NativeNextFloat, helper + "\n" + IntegerNextFloat, StringComparison.Ordinal);
    }

    private static void RequireUnique(string source, string seam)
    {
        int first = source.IndexOf(seam, StringComparison.Ordinal);
        if (first < 0 || first != source.LastIndexOf(seam, StringComparison.Ordinal))
            throw new InvalidOperationException("N.IntegerUnitFloat.DonorSourceSeamChanged");
    }
}
