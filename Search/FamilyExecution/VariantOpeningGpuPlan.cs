using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

// Concrete V packing shares W's proven counted-ordinal transport ports, without
// packing or executing any World progression pools, predicates or scratch body.
internal sealed class VariantOpeningGpuPlan
{
    internal const int WindowCapacity = 1 << 20;
    internal int Capacity => WindowCapacity;
    internal int GroupingK => 4;
    internal int ScratchWords => 0;
    internal GpuNumericalVariant NumericalVariant { get; }
    internal GpuNumericRequirements NumericRequirements => GpuIntegerNumerics.Requirements(NumericalVariant);
    internal uint[][] Buffers { get; }
    internal VariantOpeningGpuPlan(VariantOpeningPlan plan, GpuNumericalVariant numericalVariant)
    {
        if (numericalVariant is not (GpuNumericalVariant.NativeFloat64 or GpuNumericalVariant.IntegerRne))
            throw new ArgumentOutOfRangeException(nameof(numericalVariant));
        NumericalVariant = numericalVariant;
        var groups = new List<uint>(); var allows = new List<uint>();
        foreach (var group in plan.Groups)
        {
            groups.AddRange([(uint)allows.Count, (uint)group.Allowed.Length,
                group.DeterministicFirst ? 1u : 0u,
                group.OverrideAllowed is { } forced ? (forced ? 2u : 1u) : 0u]);
            allows.AddRange(group.Allowed.Select(allowed => allowed ? 1u : 0u));
        }
        Buffers = Enumerable.Range(0, 19).Select(_ => Array.Empty<uint>()).ToArray();
        ulong selection = XxHash64.Hash("act_selection"u8, 0), unknown = XxHash64.Hash("unknown_map_point"u8, 0);
        Buffers[2] = [(uint)plan.Groups.Length, (uint)plan.UnknownPrefixCount, plan.UnknownFirst ? 1u : 0u,
            (uint)selection, (uint)(selection >> 32), (uint)unknown, (uint)(unknown >> 32)];
        Buffers[3] = groups.ToArray(); Buffers[4] = allows.ToArray();
    }
    internal string Revision(bool compact) => "V.VariantOpening.Gpu" + (compact ? ".CompactAbi1" : ".Dense") +
        ".K4.CanonicalAbi1Ready.20261028.v1" + (NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne" : ".NativeFloat64") + GpuRngRotation.Revision;
    internal string ShaderSource()
    {
        // The same numerical donor and compatibility adapter as InitialRooms:
        // one RNG implementation, historical binary64 NextInt, float recurrence.
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta111GpuEventResult.comp.glsl");
        string rngAndHash = donor[donor.IndexOf("uint64_t s0;", StringComparison.Ordinal)..
            donor.IndexOf("bool evaluate_event_results", StringComparison.Ordinal)];
        string source = FamilyGpuComputeUtility.LoadEmbeddedShader("VariantOpeningFamily.comp.glsl")
            .Replace("/*__RNG_HASH__*/", rngAndHash, StringComparison.Ordinal);
        return NumericalVariant == GpuNumericalVariant.NativeFloat64 ? source : InitialRoomsGpuNumericalSource.Adapt(source);
    }
}
