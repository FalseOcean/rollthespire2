using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

// Method reference: shion0528 (Egoistic今宵), v1.3.5-resource.17,
// da82f7f9023c3d3598d50b8642e93f2801c37ccc. The four-lane arithmetic is adapted
// from the independently checked ExternalCpuOptimizationChecks prototype.
// RT2's existing gate owns applicability; surviving roots still run the entire
// original matcher. No prefix cache, extra workers or semantic state is added.
internal sealed class NeowCpuFourWayPreGate
{
    internal const string EnvironmentVariable = "RT2_CPU_FOUR_WAY_PREFILTER";
    internal const string Revision = ".FourWayFirstNeowDraw.20261027.v1";
    private static readonly ulong NeowOffset = XxHash64.Hash("NEOW"u8, 0);
    private readonly int _bound;
    private readonly ulong _allowedIndexes;
    internal bool Enabled { get; }
    internal int Bound => _bound;
    internal ulong AllowedIndexes => _allowedIndexes;

    private NeowCpuFourWayPreGate(int bound, ulong allowed, bool enabled)
    { _bound = bound; _allowedIndexes = allowed; Enabled = enabled; }

    internal static NeowCpuFourWayPreGate? TryCompile(NeowReplayPlan plan)
    {
        string? value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        // Default on for the proven gate. Explicit off retains the packed scalar
        // comparison; legacy restores the original byte-root body and price scope.
        if (string.Equals(value, "legacy", StringComparison.OrdinalIgnoreCase)) return null;
        bool enabled = string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ? false :
            value is null || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ? true :
            throw new InvalidOperationException($"NeowCpuFourWayPreGate.InvalidConfiguration:{EnvironmentVariable}={value};expected=on|off|legacy");
        return TryCompile(plan, enabled);
    }

    internal static NeowCpuFourWayPreGate? TryCompile(NeowReplayPlan plan, bool enabled)
    {
        if (!Avx2.IsSupported || plan.Authority.PlayersCount != 1 || plan.Authority.PlayerSlotIndex != 0 ||
            plan.Authority.EligibleCurseRelicIds.Length is < 1 or > 64 ||
            NeowCpuPreGate.TryCompile(plan) is not { } scalar) return null;
        ulong allowed = scalar.FirstDrawAllowedIndexes;
        ulong all = plan.Authority.EligibleCurseRelicIds.Length == 64 ? ulong.MaxValue :
            (1UL << plan.Authority.EligibleCurseRelicIds.Length) - 1;
        if (allowed == all) return null; // no necessary rejection to accelerate
        return new(plan.Authority.EligibleCurseRelicIds.Length, allowed, enabled);
    }

    // Bit i means lane i survives this necessary condition, never a full match.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int PassMask(ulong a, ulong b, ulong c, ulong d)
    {
        var ranks = Ranks(FirstDraws(Vector256.Create(a, b, c, d)), _bound);
        int mask = 0;
        for (int lane = 0; lane < 4; lane++)
            if ((_allowedIndexes & (1UL << ranks.GetElement(lane))) != 0) mask |= 1 << lane;
        return mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector256<ulong> FirstDraws(Vector256<ulong> roots)
    {
        var state = Avx2.Add(roots, Vector256.Create(unchecked(NeowOffset + 2UL * 11400714819323198485UL)));
        state = Multiply(Avx2.Xor(state, Avx2.ShiftRightLogical(state, 30)), 13787848793156543929UL);
        state = Multiply(Avx2.Xor(state, Avx2.ShiftRightLogical(state, 27)), 10723151780598845931UL);
        state = Avx2.Xor(state, Avx2.ShiftRightLogical(state, 31));
        state = Avx2.Add(state, Avx2.ShiftLeftLogical(state, 2));
        state = Avx2.Or(Avx2.ShiftLeftLogical(state, 7), Avx2.ShiftRightLogical(state, 57));
        return Avx2.Add(state, Avx2.ShiftLeftLogical(state, 3));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<ulong> Multiply(Vector256<ulong> value, ulong factor)
    {
        var lower = Avx2.Multiply(value.AsUInt32(), Vector256.Create((uint)factor));
        var cross = Avx2.Add(Avx2.Multiply(value.AsUInt32(), Vector256.Create((uint)(factor >> 32))),
            Avx2.Multiply(Avx2.ShiftRightLogical(value, 32).AsUInt32(), Vector256.Create((uint)factor)));
        return Avx2.Add(lower, Avx2.ShiftLeftLogical(cross, 32));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<int> Ranks(Vector256<ulong> draws, int bound)
    {
        // Match CPU NextDouble then NextInt: two distinct binary64 products.
        // Integer-floor substitution differs at real bucket boundaries.
        var mantissa = Avx2.ShiftRightLogical(draws, 11);
        var exponent = Vector256.Create(0x4330000000000000UL);
        var two52 = Vector256.Create(4503599627370496d);
        var low = Avx.Subtract(Avx2.Or(Avx2.And(mantissa, Vector256.Create((1UL << 52) - 1)), exponent).AsDouble(), two52);
        var high = Avx.Subtract(Avx2.Or(Avx2.ShiftRightLogical(mantissa, 52), exponent).AsDouble(), two52);
        var integer = Avx.Add(low, Avx.Multiply(high, two52));
        var unit = Avx.Multiply(integer, Vector256.Create(1.1102230246251565E-16));
        return Avx.ConvertToVector128Int32WithTruncation(Avx.Multiply(unit, Vector256.Create((double)bound)));
    }
}
