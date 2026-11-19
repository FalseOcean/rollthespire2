namespace RolltheSpire2.Search.FamilyExecution;

// One complete fast predicate on the first output of a root-derived stream.
// Family preparation owns the covered obligations and the ordered rank mapping.
internal sealed record FamilyFirstDraw(string Group, ulong StreamOffset, NeowStaticDrawGateCompiler.Gate AcceptedRanks)
{
    internal static FamilyFirstDraw? Compile(string group, ulong streamOffset, int bound, Func<int, bool> accepts) =>
        NeowStaticDrawGateCompiler.TryCompileRanks(bound, accepts, bound, out var gate, binary64: true)
            ? new(group, streamOffset, gate!) : null;
}
