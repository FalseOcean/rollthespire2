using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Neow;

internal sealed record ModernNeowIdentityResult(
    IReadOnlyList<ModelKey> RelicKeys,
    int RngCalls,
    bool ExactAuthority,
    bool UnknownEligibilityAffectedPool);

/// <summary>
/// Shared ModernCore Neow identity prediction. The supplied runtime profile owns
/// version-specific seed derivation; immutable rules own candidate preparation.
/// </summary>
internal static class ModernNeowIdentityPredictor
{
    public static ModernNeowIdentityResult PredictBeta109(
        string canonicalSeed,
        RuntimeContextAuthoritySnapshot authority) =>
        PredictModernCore(
            canonicalSeed,
            authority,
            Beta109Profile.Instance,
            authority.IsBeta109ProjectionAuthorityExact);

    public static ModernNeowIdentityResult PredictModernCore(
        string canonicalSeed,
        RuntimeContextAuthoritySnapshot authority,
        IRuntimeProfile profile,
        bool exactAuthority) =>
        PredictModernCore(
            profile.ComputeRootSeed(canonicalSeed),
            authority,
            profile,
            exactAuthority);

    internal static ModernNeowIdentityResult PredictModernCore(
        ulong rootSeed,
        RuntimeContextAuthoritySnapshot authority,
        IRuntimeProfile profile,
        bool exactAuthority)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!RuntimeProfilePolicies.IsModernCore(profile.ProfileId))
        {
            throw new InvalidOperationException("Modern Neow analyzer requires a Modern donor/profile.");
        }
        ulong eventSeed = profile.DeriveEventStreamSeed(rootSeed, authority.PlayerSlotIndex, "NEOW");
        var rng = new Xoshiro256StarStar(eventSeed);
        ModernNeowIdentityPreparation preparation = ModernNeowIdentityRules.Prepare(
            authority.PlayersCount, authority.AllCharacterCardPoolsUnlocked, authority.IsScrollBoxesAllowed);
        if (preparation.EligibleCurses.IsEmpty)
        {
            throw new InvalidOperationException("Modern Neow curse pool is empty after copied eligibility filtering.");
        }

        ModelKey curse = preparation.EligibleCurses[rng.NextInt(preparation.EligibleCurses.Length)];
        ModernNeowCursePool pool = preparation.ForCurse(curse);
        var positives = pool.FixedCandidates.ToList();
        foreach (ModernNeowBinaryChoice choice in pool.AppendChoices)
            positives.Add(rng.NextBool() ? choice.TrueCandidate : choice.FalseCandidate);
        rng.UnstableShuffle(positives);

        return new ModernNeowIdentityResult(
            new[] { positives[0], positives[1], curse },
            rng.CallCount,
            exactAuthority,
            preparation.UnknownEligibilityAffectedPool);
    }
}
