using System.Text;

namespace RolltheSpire2.Search.FamilyExecution;

// A complete per-seat predicate when the curse draw resolves all offer gates
// and Leafy, if observed, is picked directly. Both streams belong to the seat.
internal sealed record NeowPartyDirectDraw(string OfferBody, string? TransformBody, int TargetCount)
{
    internal const string Revision = ".PartyDirectDraw.20261119.v1";
    internal static NeowPartyDirectDraw? Compile(NeowReplayPlan replay, NeowFamilyGpuPlan gpu)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("RT2_N_PARTY_DIRECT_DRAW"), "off", StringComparison.OrdinalIgnoreCase) ||
            !gpu.IsMultiplayer || replay.Bones || replay.HasFinalCurseFastProjection)
            return null;
        int bound = replay.Authority.EligibleCurseRelicIds.Length;
        uint accepted = gpu.Meta[68], rejected = gpu.Meta[69];
        if (bound is < 1 or > 10 || accepted == 0 || rejected == 0 ||
            (accepted & rejected) != 0 || (accepted | rejected) != (1u << bound) - 1 ||
            !NeowStaticDrawGateCompiler.TryCompileRanks(bound, i => (accepted & (1u << i)) != 0,
                bound, out var offers, binary64: true)) return null;

        string SeatStream(int index)
        {
            ulong offset = unchecked((((ulong)gpu.Meta[index + 1] << 32) | gpu.Meta[index]) + (uint)replay.Authority.PlayerSlotIndex);
            return FormattableString.Invariant($"make_u64(0x{(uint)offset:x8}u,0x{(uint)(offset >> 32):x8}u)");
        }
        string offerBody = $$"""
            uint64_t mix = root + {{SeatStream(31)}} + make_u64(0x7f4a7c15u,0x9e3779b9u);
            uint64_t m = party_draw_mantissa(splitmix_next(mix));
            return {{offers!.GlslExpression("m")}};
            """;
        if (replay.StructuredConditions.Length == 0) return new(offerBody, null, 0);
        if (replay is not { Selected: Beta110FastRelicCatalog.LeafyPoultice,
                EnabledDomains: Beta110FastDomain.LeafyPoulticeTransforms, SharedArrivals.Length: 0,
                StructuredConditions: [{ Kind: Beta110FastStructuredConditionKind.LeafyPoulticeTransforms } row] } ||
            row.TargetCount is not (1 or 2) || !replay.Authority.EffectCatalog.LeafyTransformAuthorityExact)
            return null;

        static string Range(uint[] pool, int length, uint target, string variable)
        {
            NeowStaticDrawGateCompiler.TryCompileRanks(length, i => pool[i] == target, length, out var gate, binary64: true);
            return gate!.GlslExpression(variable);
        }
        string Strike(uint id) => Range(gpu.Strike, (int)gpu.Meta[58], id, "first");
        string Defend(uint id) => Range(gpu.Defend, (int)gpu.Meta[59], id, "second");
        string firstTest = row.TargetCount == 1
            ? $"if ({Strike(row.Target0)}) return true;"
            : $"bool a = {Strike(row.Target0)}; bool b = {Strike(row.Target1)}; if (!a && !b) return false;";
        string secondTest = row.TargetCount == 1 ? Defend(row.Target0) :
            $"(a && ({Defend(row.Target1)})) || (b && ({Defend(row.Target0)}))";
        string transformBody = $$"""
            uint64_t stride = make_u64(0x7f4a7c15u,0x9e3779b9u);
            uint64_t seed = root + {{SeatStream(37)}};
            uint64_t mix = seed + stride;
            uint64_t s1 = splitmix_next(mix);
            uint64_t first = party_draw_mantissa(s1);
            {{firstTest}}
            uint64_t s2 = splitmix_next(mix);
            mix = seed;
            uint64_t s0 = splitmix_next(mix);
            uint64_t second = party_draw_mantissa(s1 ^ s0 ^ s2);
            return {{secondTest}};
            """;
        return new(offerBody, transformBody, row.TargetCount);
    }

    internal static string Apply(string source, IReadOnlyList<NeowPartyDirectDraw> seats)
    {
        var helpers = new StringBuilder("""
            uint64_t party_draw_mantissa(uint64_t s1) {
                return (rotl64(s1 * uint64_t(5u), 7u) * uint64_t(9u)) >> 11u;
            }
            """).AppendLine();
        var terms = new List<string>();
        for (int i = 0; i < seats.Count; i++)
        {
            helpers.AppendLine($"bool party_offer_{i}(uint64_t root) {{ {seats[i].OfferBody} }}");
            if (seats[i].TransformBody is { } transform)
                helpers.AppendLine($"bool party_transform_{i}(uint64_t root) {{ {transform} }}");
        }
        // Two named cards usually reject on the first transformation. A single
        // target can still hit on Defend, so check all required offers first.
        for (int i = 0; i < seats.Count; i++) if (seats[i].TargetCount == 2) terms.Add($"party_transform_{i}(root)");
        for (int i = 0; i < seats.Count; i++) terms.Add($"party_offer_{i}(root)");
        for (int i = 0; i < seats.Count; i++) if (seats[i].TargetCount == 1) terms.Add($"party_transform_{i}(root)");
        helpers.AppendLine($"bool party_direct_matches(uint64_t root) {{ return {string.Join(" && ", terms)}; }}");
        const string marker = "/*__RT2_PARTY_DIRECT_DRAW__*/";
        const string entry = "if (matches(root)) append_ordinal(ordinal);";
        if (!source.Contains(marker, StringComparison.Ordinal) || !source.Contains(entry, StringComparison.Ordinal))
            throw new InvalidOperationException("N.PartyDirectDrawShaderSeam");
        return source.Replace(marker, helpers.ToString(), StringComparison.Ordinal)
            .Replace("#define N_PARTY_DIRECT_DRAW 0", "#define N_PARTY_DIRECT_DRAW 1", StringComparison.Ordinal)
            .Replace(entry, "if (party_direct_matches(root)) append_ordinal(ordinal);", StringComparison.Ordinal);
    }
}
