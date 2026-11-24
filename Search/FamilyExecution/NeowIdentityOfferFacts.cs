using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;

namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct NeowIdentityBinaryChoice(byte TrueCandidate, byte FalseCandidate, byte SuppressedByCurse);

internal sealed class NeowIdentityCurseFacts
{
    private readonly byte[] _fixed;
    private readonly NeowIdentityBinaryChoice[] _append;
    internal ReadOnlySpan<byte> FixedCandidates => _fixed;
    internal ReadOnlySpan<NeowIdentityBinaryChoice> AppendChoices => _append;
    internal NeowIdentityCurseFacts(ModernNeowCursePool pool)
    {
        _fixed = pool.FixedCandidates.Select(NeowIdentityOfferFacts.Map).ToArray();
        _append = pool.AppendChoices.Select(NeowIdentityOfferFacts.Map).ToArray();
    }
}

// Fixed-ID compilation of Core facts, shared by immutable request authorities.
// The eight known eligibility combinations are bounded; record-with clones read
// the table selected by their own current inputs, never an inherited cached field.
internal sealed class NeowIdentityOfferFacts
{
    internal const string Revision = ".IdentityFacts.20261031.v1";
    private static readonly byte[] FixedIds = ModernNeowIdentityRules.PositiveCandidates.Select(Map).ToArray();
    private static readonly byte[] CurseIds = ModernNeowIdentityRules.CurseCandidates.Select(Map).ToArray();
    private static readonly NeowIdentityBinaryChoice[] Choices = ModernNeowIdentityRules.BinaryChoices.Select(Map).ToArray();
    private static readonly NeowIdentityOfferFacts[] Profiles = Enumerable.Range(0, 8).Select(index =>
        new NeowIdentityOfferFacts(ModernNeowIdentityRules.Prepare((index & 4) == 0 ? 1 : 2,
            (index & 1) != 0, (index & 2) != 0))).ToArray();
    private readonly NeowIdentityCurseFacts[] _curses;
    private readonly byte[] _eligibleCurses;
    private readonly byte[] _admittedFixed;

    internal static ReadOnlySpan<byte> FixedCandidates => FixedIds;
    internal static ReadOnlySpan<byte> CurseCandidates => CurseIds;
    internal static ReadOnlySpan<NeowIdentityBinaryChoice> BinaryChoices => Choices;
    internal ReadOnlySpan<byte> EligibleCurseCandidates => _eligibleCurses;
    internal ReadOnlySpan<byte> AdmittedFixedCandidates => _admittedFixed;
    internal static NeowIdentityOfferFacts For(int playersCount, bool allUnlocked, bool scrollAllowed) =>
        Profiles[(playersCount > 1 ? 4 : 0) | (allUnlocked ? 1 : 0) | (scrollAllowed ? 2 : 0)];

    private NeowIdentityOfferFacts(ModernNeowIdentityPreparation preparation)
    {
        _eligibleCurses = preparation.EligibleCurses.Select(Map).ToArray();
        _curses = new NeowIdentityCurseFacts[CurseIds.Max() + 1];
        foreach (ModernNeowCursePool pool in preparation.CursePools)
            _curses[Map(pool.Curse)] = new(pool);
        _admittedFixed = FixedIds.Where(id => _curses.Any(row => row.FixedCandidates.Contains(id))).ToArray();
    }

    internal NeowIdentityCurseFacts ForCurse(byte curse) => _curses[curse];
    internal static bool IsPositiveCandidate(byte id) => FixedCandidates.Contains(id) ||
        Choices.Any(choice => choice.TrueCandidate == id || choice.FalseCandidate == id);
    internal static byte Map(ModelKey key) => Beta110FastRelicCatalog.TryGetId(key, out byte id)
        ? id : throw new InvalidOperationException("NeowIdentityFixedIdUnavailable:" + key);
    internal static NeowIdentityBinaryChoice Map(ModernNeowBinaryChoice choice) => new(Map(choice.TrueCandidate),
        Map(choice.FalseCandidate), choice.SuppressedByCurse is { } curse ? Map(curse) : Beta110FastRelicCatalog.InvalidId);
}
