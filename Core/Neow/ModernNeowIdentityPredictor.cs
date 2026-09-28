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
/// version-specific seed derivation and eligibility; this is not an all-version algorithm.
/// </summary>
internal static class ModernNeowIdentityPredictor
{
    private static readonly ModelKey[] PositivePool =
    {
        BaseGameModelKeys.Relics.ArcaneScroll,
        BaseGameModelKeys.Relics.BoomingConch,
        BaseGameModelKeys.Relics.FishingRod,
        BaseGameModelKeys.Relics.GoldenPearl,
        BaseGameModelKeys.Relics.Kaleidoscope,
        BaseGameModelKeys.Relics.LeadPaperweight,
        BaseGameModelKeys.Relics.LostCoffer,
        BaseGameModelKeys.Relics.MassiveScroll,
        BaseGameModelKeys.Relics.NeowsTorment,
        BaseGameModelKeys.Relics.NewLeaf,
        BaseGameModelKeys.Relics.PhialHolster,
        BaseGameModelKeys.Relics.PreciseScissors,
        BaseGameModelKeys.Relics.ScrollBoxes,
        BaseGameModelKeys.Relics.WingedBoots
    };

    private static readonly ModelKey[] CursePool =
    {
        BaseGameModelKeys.Relics.CursedPearl,
        BaseGameModelKeys.Relics.DowsingRod,
        BaseGameModelKeys.Relics.HeftyTablet,
        BaseGameModelKeys.Relics.LargeCapsule,
        BaseGameModelKeys.Relics.LeafyPoultice,
        BaseGameModelKeys.Relics.NeowsBones,
        BaseGameModelKeys.Relics.NeowsSacrifice,
        BaseGameModelKeys.Relics.PrecariousShears,
        BaseGameModelKeys.Relics.SilkenTress,
        BaseGameModelKeys.Relics.SilverCrucible
    };

    internal static IReadOnlyList<ModelKey> BasePositivePoolForAuthority => PositivePool;
    internal static IReadOnlyList<ModelKey> BaseCursePoolForFiltering => CursePool;

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

        List<ModelKey> curses = CursePool
            .Where(key => IsAllowed(key, authority) != false)
            .ToList();
        if (curses.Count == 0)
        {
            throw new InvalidOperationException("Modern Neow curse pool is empty after copied eligibility filtering.");
        }

        ModelKey curse = curses[rng.NextInt(curses.Count)];
        List<ModelKey> positives = PositivePool.ToList();
        RemoveConflict(positives, curse);

        if (curse != BaseGameModelKeys.Relics.LargeCapsule)
        {
            positives.Add(rng.NextBool()
                ? BaseGameModelKeys.Relics.LavaRock
                : BaseGameModelKeys.Relics.SmallCapsule);
        }

        positives.Add(rng.NextBool()
            ? BaseGameModelKeys.Relics.NutritiousOyster
            : BaseGameModelKeys.Relics.StoneHumidifier);
        positives.Add(rng.NextBool()
            ? BaseGameModelKeys.Relics.NeowsTalisman
            : BaseGameModelKeys.Relics.Pomander);
        positives.RemoveAll(key => IsAllowed(key, authority) == false);
        rng.UnstableShuffle(positives);

        bool unknownEligibility =
            !authority.AllCharacterCardPoolsUnlocked.HasValue ||
            !authority.IsScrollBoxesAllowed.HasValue;
        return new ModernNeowIdentityResult(
            new[] { positives[0], positives[1], curse },
            rng.CallCount,
            exactAuthority,
            unknownEligibility);
    }

    internal static bool TryGetEligibleCursePool(
        RuntimeContextAuthoritySnapshot authority,
        out IReadOnlyList<ModelKey> eligible)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.IsProductionBetaProjectionAuthorityExact ||
            !authority.AllCharacterCardPoolsUnlocked.HasValue ||
            !authority.IsScrollBoxesAllowed.HasValue)
        {
            eligible = Array.Empty<ModelKey>();
            return false;
        }

        eligible = CursePool
            .Where(key => IsAllowed(key, authority) == true)
            .ToArray();
        return eligible.Count != 0;
    }

    /// <summary>
    /// Exact query-wide marginal for one positive Neow identity. This reuses the
    /// same copied Beta110 pool/conflict/eligibility rules as Analyze rather than
    /// maintaining a second UI probability table. Binary positive slots are
    /// enumerated exactly and the two visible positive offers are treated as the
    /// first two entries of a uniform shuffle.
    /// </summary>
    internal static bool TryEstimatePositiveOfferProbability(
        RuntimeContextAuthoritySnapshot authority,
        ModelKey target,
        out double probability,
        out string evidence)
    {
        ArgumentNullException.ThrowIfNull(authority);
        probability = 0d;
        evidence = string.Empty;
        if (!target.IsValid ||
            !TryGetEligibleCursePool(authority, out IReadOnlyList<ModelKey> curses))
        {
            evidence = "NeowPositiveAuthorityMissing";
            return false;
        }

        double total = 0d;
        foreach (ModelKey curse in curses)
        {
            var binaryBranches = new List<(ModelKey? CapsuleSlot, ModelKey OysterSlot, ModelKey TalismanSlot, double Weight)>();
            ModelKey?[] capsuleChoices = curse == BaseGameModelKeys.Relics.LargeCapsule
                ? new ModelKey?[] { null }
                : new ModelKey?[] { BaseGameModelKeys.Relics.LavaRock, BaseGameModelKeys.Relics.SmallCapsule };
            double branchWeight = 1d / (capsuleChoices.Length * 2d * 2d);
            foreach (ModelKey? capsule in capsuleChoices)
            foreach (ModelKey oyster in new[] { BaseGameModelKeys.Relics.NutritiousOyster, BaseGameModelKeys.Relics.StoneHumidifier })
            foreach (ModelKey talisman in new[] { BaseGameModelKeys.Relics.NeowsTalisman, BaseGameModelKeys.Relics.Pomander })
                binaryBranches.Add((capsule, oyster, talisman, branchWeight));

            double conditional = 0d;
            foreach ((ModelKey? capsule, ModelKey oyster, ModelKey talisman, double weight) in binaryBranches)
            {
                List<ModelKey> positives = PositivePool.ToList();
                RemoveConflict(positives, curse);
                if (capsule.HasValue) positives.Add(capsule.Value);
                positives.Add(oyster);
                positives.Add(talisman);
                positives.RemoveAll(key => IsAllowed(key, authority) == false);

                int n = positives.Count;
                int targetMultiplicity = positives.Count(key => key == target);
                if (n < 2 || targetMultiplicity <= 0) continue;
                double miss = targetMultiplicity >= n
                    ? 0d
                    : ((n - targetMultiplicity) * (n - targetMultiplicity - 1d)) / (n * (n - 1d));
                conditional += weight * (1d - miss);
            }
            total += conditional / curses.Count;
        }

        probability = Math.Clamp(total, 0d, 1d);
        evidence = $"CurseMixture={curses.Count};BinaryPositiveSlotsEnumerated=true;TwoOfferUniformShuffle=true";
        return true;
    }

    private static bool? IsAllowed(ModelKey key, RuntimeContextAuthoritySnapshot authority)
    {
        if (key == BaseGameModelKeys.Relics.MassiveScroll)
        {
            return authority.PlayersCount > 1;
        }
        if (key == BaseGameModelKeys.Relics.Kaleidoscope)
        {
            return authority.AllCharacterCardPoolsUnlocked;
        }
        if (key == BaseGameModelKeys.Relics.ScrollBoxes)
        {
            return authority.IsScrollBoxesAllowed;
        }
        if (key == BaseGameModelKeys.Relics.WingedBoots ||
            key == BaseGameModelKeys.Relics.SilverCrucible)
        {
            return authority.PlayersCount == 1;
        }
        return true;
    }

    private static void RemoveConflict(List<ModelKey> positives, ModelKey curse)
    {
        if (curse == BaseGameModelKeys.Relics.CursedPearl)
        {
            positives.Remove(BaseGameModelKeys.Relics.GoldenPearl);
        }
        else if (curse == BaseGameModelKeys.Relics.HeftyTablet)
        {
            positives.Remove(BaseGameModelKeys.Relics.ArcaneScroll);
        }
        else if (curse == BaseGameModelKeys.Relics.LeafyPoultice)
        {
            positives.Remove(BaseGameModelKeys.Relics.NewLeaf);
        }
        else if (curse == BaseGameModelKeys.Relics.PrecariousShears)
        {
            positives.Remove(BaseGameModelKeys.Relics.PreciseScissors);
        }
        else if (curse == BaseGameModelKeys.Relics.NeowsSacrifice)
        {
            positives.Remove(BaseGameModelKeys.Relics.PhialHolster);
            positives.Remove(BaseGameModelKeys.Relics.LostCoffer);
        }
    }
}
