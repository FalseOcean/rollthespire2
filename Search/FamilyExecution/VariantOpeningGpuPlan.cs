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
    internal bool DenseCarry8 { get; }
    internal GpuNumericalVariant NumericalVariant { get; }
    internal GpuNumericRequirements NumericRequirements => GpuIntegerNumerics.Requirements(NumericalVariant);
    private readonly bool _unknownMonster;
    internal uint[][] Buffers { get; }
    internal VariantOpeningGpuPlan(VariantOpeningPlan plan, GpuNumericalVariant numericalVariant, bool? denseCarry8 = null)
    {
        if (numericalVariant is not (GpuNumericalVariant.NativeFloat64 or GpuNumericalVariant.IntegerRne))
            throw new ArgumentOutOfRangeException(nameof(numericalVariant));
        NumericalVariant = numericalVariant;
        DenseCarry8 = denseCarry8 ?? Environment.GetEnvironmentVariable("RT2_V_DENSE_CARRY8") switch {
            null or "" or "default" or "on" => true,
            "off" => false,
            var mode => throw new InvalidOperationException("V.UnknownDenseCarryExperiment:" + mode)
        };
        _unknownMonster = plan.UnknownMonster;
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
            (uint)selection, (uint)(selection >> 32), (uint)unknown, (uint)(unknown >> 32),
            plan.UnknownJuzuAcquired ? 1u : 0u];
        Buffers[3] = groups.ToArray(); Buffers[4] = allows.ToArray();
    }
    internal string Revision(bool compact) => "V.VariantOpening.Gpu" + (compact ? ".CompactAbi1" : ".Dense") +
        ".K4.CanonicalAbi1Ready.20261029.v2" + (_unknownMonster ? ".MonsterPrefix.20261105.v1" : "") +
        (NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne" : ".NativeFloat64") +
        (DenseCarry8 ? compact ? ".PackedSeedHash.20261115.v1" : ".DenseSeedCarry8.20261115.v1" : "") +
        GpuRngRotation.Revision;
    internal string ShaderSource()
    {
        // The same numerical donor and compatibility adapter as InitialRooms:
        // one RNG implementation, historical binary64 NextInt, float recurrence.
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta111GpuEventResult.comp.glsl");
        string source = FamilyGpuComputeUtility.LoadEmbeddedShader("VariantOpeningFamily.comp.glsl");
        string rngAndHash = donor[donor.IndexOf("uint64_t s0;", StringComparison.Ordinal)..
            donor.IndexOf(DenseCarry8 ? "uint64_t prime1()" : "bool evaluate_event_results", StringComparison.Ordinal)];
        if (DenseCarry8)
        {
            rngAndHash += FamilyGpuComputeUtility.LoadEmbeddedShader("VisibleSeedRootHashCommon.glsl");
            source = source.Replace("#define RT2_V_DENSE_CARRY8 0", "#define RT2_V_DENSE_CARRY8 1", StringComparison.Ordinal);
        }
        source = source.Replace("/*__RNG_HASH__*/", rngAndHash, StringComparison.Ordinal);
        if (_unknownMonster) source = UnknownRoomPrefixGpuSource.RequireMonster(source, 7);
        return NumericalVariant == GpuNumericalVariant.NativeFloat64 ? source : InitialRoomsGpuNumericalSource.Adapt(source);
    }
}
