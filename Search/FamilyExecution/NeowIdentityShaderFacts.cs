using System.Globalization;
using System.Text;

namespace RolltheSpire2.Search.FamilyExecution;

// Compile the same immutable numerical facts once, before existing donor slicing
// and assembly. Shader execution keeps its own RNG and specialized shuffle loops.
internal static class NeowIdentityShaderFacts
{
    internal const string SourceMarker = "/*__RT2_NEOW_IDENTITY_FACTS__*/";
    private static readonly Lazy<string> CompiledSource = new(Compile);
    internal static string Source => CompiledSource.Value;

    private static string Compile()
    {
        byte[] fixedIds = NeowIdentityOfferFacts.FixedCandidates.ToArray();
        byte[] curseIds = NeowIdentityOfferFacts.CurseCandidates.ToArray();
        int curseCapacity = curseIds.Max() + 1;
        var appendChoices = NeowIdentityOfferFacts.BinaryChoices.ToArray();
        // This physical hot loop has one optional first draw followed by two
        // always-present draws. Prove that shape from Core facts once; operands
        // and the suppressing identity remain entirely Core-owned.
        if (appendChoices.Length != 3 ||
            !curseIds.Contains(appendChoices[0].SuppressedByCurse) ||
            appendChoices[1].SuppressedByCurse != Beta110FastRelicCatalog.InvalidId ||
            appendChoices[2].SuppressedByCurse != Beta110FastRelicCatalog.InvalidId)
            throw new InvalidOperationException("NeowIdentityShader.UnrolledAppendShape");
        var fixedMasks = new uint[8 * curseCapacity];
        var curseCounts = new uint[8];
        var eligibleCurses = new uint[8 * curseCapacity];
        for (int profile = 0; profile < 8; profile++)
        {
            var facts = NeowIdentityOfferFacts.For((profile & 4) == 0 ? 1 : 2,
                (profile & 1) != 0, (profile & 2) != 0);
            curseCounts[profile] = checked((uint)facts.EligibleCurseCandidates.Length);
            for (int index = 0; index < facts.EligibleCurseCandidates.Length; index++)
                eligibleCurses[profile * curseCapacity + index] = facts.EligibleCurseCandidates[index];
            foreach (byte curse in curseIds)
            {
                var pool = facts.ForCurse(curse);
                uint mask = 0;
                foreach (byte id in pool.FixedCandidates)
                {
                    int index = Array.IndexOf(fixedIds, id);
                    if (index < 0 || index >= 32)
                        throw new InvalidOperationException("NeowIdentityShader.FixedCandidateOrdinal:" + id);
                    mask |= 1u << index;
                }
                if (!fixedIds.Where((_, index) => (mask & (1u << index)) != 0u)
                    .SequenceEqual(pool.FixedCandidates.ToArray()))
                    throw new InvalidOperationException("NeowIdentityShader.FixedCandidateOrder");
                fixedMasks[profile * curseCapacity + curse] = mask;
                // Includes T's profile-independent append contract and the
                // exact optional-first/full-or-suffix order used by the loop.
                var expected = appendChoices.AsSpan(curse == appendChoices[0].SuppressedByCurse ? 1 : 0);
                if (!pool.AppendChoices.SequenceEqual(expected))
                    throw new InvalidOperationException("NeowIdentityShader.UnrolledPreparedAppendShape");
            }
        }
        var source = new StringBuilder("#ifndef RT2_NEOW_IDENTITY_FACTS\n#define RT2_NEOW_IDENTITY_FACTS\n");
        source.AppendLine("// Compiled from NeowIdentityOfferFacts; no game rules are authored in GLSL.");
        Constant("RT2_NEOW_FIXED_COUNT", fixedIds.Length);
        Constant("RT2_NEOW_CURSE_CAPACITY", curseCapacity);
        Constant("RT2_NEOW_OPTIONAL_APPEND_CURSE", appendChoices[0].SuppressedByCurse);
        for (int index = 0; index < appendChoices.Length; index++)
        {
            string operand = "RT2_NEOW_APPEND" + index.ToString(CultureInfo.InvariantCulture);
            Constant(operand + "_TRUE", appendChoices[index].TrueCandidate);
            Constant(operand + "_FALSE", appendChoices[index].FalseCandidate);
        }
        EmitArray("rt2_neow_fixed_candidates", fixedIds.Select(id => (uint)id));
        EmitArray("rt2_neow_fixed_masks", fixedMasks);
        EmitArray("rt2_neow_curse_counts", curseCounts);
        EmitArray("rt2_neow_eligible_curses", eligibleCurses);
        source.AppendLine("uint rt2_neow_profile(uint players, bool all_unlocked, bool scroll_allowed) { return (players > 1u ? 4u : 0u) | (all_unlocked ? 1u : 0u) | (scroll_allowed ? 2u : 0u); }");
        source.AppendLine("uint rt2_neow_fixed_mask(uint profile, uint curse) { return rt2_neow_fixed_masks[profile * RT2_NEOW_CURSE_CAPACITY + curse]; }");
        source.AppendLine("#endif");
        return source.ToString();

        void Constant(string name, int value) => source.Append("const uint ").Append(name).Append(" = ")
            .Append(value.ToString(CultureInfo.InvariantCulture)).AppendLine("u;");
        void EmitArray(string name, IEnumerable<uint> values)
        {
            string[] literals = values.Select(value => value.ToString(CultureInfo.InvariantCulture) + "u").ToArray();
            string length = literals.Length.ToString(CultureInfo.InvariantCulture);
            source.Append("const uint ").Append(name).Append('[').Append(length).Append("] = uint[")
                .Append(length).Append("](").Append(string.Join(',', literals)).AppendLine(");");
        }
    }
}
