using System.Text.RegularExpressions;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum GpuNumericalVariant { NativeFloat64, IntegerRne }

// The shared primitives do not own RNG advancement. Each donor consumes its
// original raw draw once, then passes its words to these exact conversions.
internal static class GpuIntegerNumerics
{
    internal static string NextIntSource => FamilyGpuComputeUtility.LoadEmbeddedShader("NextIntIntegerRne.glsl");
    internal static string UnitFloatSource => FamilyGpuComputeUtility.LoadEmbeddedShader("NextFloatIntegerUnit.glsl");
    internal static GpuNumericRequirements Requirements(GpuNumericalVariant variant) => variant switch
    {
        GpuNumericalVariant.NativeFloat64 => GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64,
        GpuNumericalVariant.IntegerRne => GpuNumericRequirements.Int64,
        _ => GpuNumericRequirements.Unspecified
    };

    internal static string NormalizeLines(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal);

    internal static string ReplaceUnique(string source, string before, string after, string owner)
    {
        int first = source.IndexOf(before, StringComparison.Ordinal);
        if (first < 0 || first != source.LastIndexOf(before, StringComparison.Ordinal))
            throw new InvalidOperationException(owner + ".IntegerRne.SourceSeamChanged");
        return source.Replace(before, after, StringComparison.Ordinal);
    }

    internal static string RemoveFloat64Extension(string source, string owner) => ReplaceUnique(source,
        "#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require\n", "", owner);

    internal static string RequireNoFloat64(string source, string owner)
    {
        string code = Regex.Replace(source, @"/\*.*?\*/|//[^\r\n]*", "", RegexOptions.Singleline);
        if (Regex.IsMatch(code, @"\b(?:double|dvec[234]|dmat[234](?:x[234])?|\w*float64\w*)\b|\b\d[\w.+-]*lf\b"))
            throw new InvalidOperationException(owner + ".IntegerRne.UnconvertedFloat64Source");
        return source;
    }
}
