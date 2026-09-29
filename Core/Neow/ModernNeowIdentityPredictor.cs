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

    internal static bool CanCoOffer(IEnumerable<ModelKey> targets)
    {
        ModelKey[] selected = targets.Distinct(ModelKeyComparer.Instance).ToArray();
        if (selected.Length > 3) return false;
        ModelKey[] curses = selected.Where(key => CursePool.Contains(key, ModelKeyComparer.Instance)).ToArray();
        if (curses.Length > 1) return false;
        ModelKey[] positives = selected.Where(key => !curses.Contains(key, ModelKeyComparer.Instance)).ToArray();
        if (positives.Length > 2 || positives.Any(key => !IsPositiveCandidate(key))) return false;
        if (ContainsBoth(positives, BaseGameModelKeys.Relics.LavaRock, BaseGameModelKeys.Relics.SmallCapsule) ||
            ContainsBoth(positives, BaseGameModelKeys.Relics.NutritiousOyster, BaseGameModelKeys.Relics.StoneHumidifier) ||
            ContainsBoth(positives, BaseGameModelKeys.Relics.NeowsTalisman, BaseGameModelKeys.Relics.Pomander))
            return false;
        return curses.Length == 0 || positives.All(positive => !Conflicts(curses[0], positive));
    }

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
    internal static bool TryEstimatePositiveOfferProbability(RuntimeContextAuthoritySnapshot authority,
        ModelKey target, out double probability, out string evidence) =>
        TryEstimateOfferProbability(authority, (a,b,c) => a == target || b == target || c == target, out probability, out evidence);

    internal static bool TryEstimateOfferProbability(RuntimeContextAuthoritySnapshot authority,
        Func<ModelKey, ModelKey, ModelKey, bool> accepts, out double probability, out string evidence)
    {
        double sum = 0;
        bool exact = TryVisitOfferSpace(authority, (a,b,c,mass) => { if (accepts(a,b,c)) sum += mass; });
        probability = exact ? Math.Clamp(sum, 0, 1) : 0;
        evidence = "NeowOfferJoint;CurseConditionedPool;BinarySlotsAndUnorderedPositivePairs;OneSharedOffer";
        return exact;
    }

    internal static bool TryVisitOfferSpace(RuntimeContextAuthoritySnapshot authority,
        Action<ModelKey, ModelKey, ModelKey, double> visit)
    {
        if (!TryGetEligibleCursePool(authority, out var curses)) return false;
        foreach (ModelKey curse in curses)
        {
            ModelKey?[] capsuleChoices = curse == BaseGameModelKeys.Relics.LargeCapsule
                ? [null] : [BaseGameModelKeys.Relics.LavaRock, BaseGameModelKeys.Relics.SmallCapsule];
            foreach (ModelKey? capsule in capsuleChoices)
            foreach (ModelKey oyster in new[] { BaseGameModelKeys.Relics.NutritiousOyster, BaseGameModelKeys.Relics.StoneHumidifier })
            foreach (ModelKey talisman in new[] { BaseGameModelKeys.Relics.NeowsTalisman, BaseGameModelKeys.Relics.Pomander })
            {
                var positives = PositivePool.ToList();
                RemoveConflict(positives, curse);
                if (capsule.HasValue) positives.Add(capsule.Value);
                positives.Add(oyster); positives.Add(talisman);
                if (positives.Any(key => !IsAllowed(key, authority).HasValue)) return false;
                positives.RemoveAll(key => IsAllowed(key, authority) == false);
                int n = positives.Count;
                if (n < 2) return false;
                double mass = 2d / (curses.Count * capsuleChoices.Length * 4d * n * (n - 1));
                for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++)
                    visit(positives[i], positives[j], curse, mass);
            }
        }
        return true;
    }

    private static bool? IsAllowed(ModelKey key, RuntimeContextAuthoritySnapshot authority) =>
        IsAllowed(key, authority.PlayersCount, authority.AllCharacterCardPoolsUnlocked, authority.IsScrollBoxesAllowed);

    // Top-level offers and the Bones pool must apply the same eligibility rules.
    // Null preserves unknown unlock eligibility; callers retain their precision policy.
    internal static bool? IsAllowed(ModelKey key, int playersCount,
        bool? allCharacterCardPoolsUnlocked, bool? scrollBoxesAllowed)
    {
        if (key == BaseGameModelKeys.Relics.MassiveScroll)
        {
            return playersCount > 1;
        }
        if (key == BaseGameModelKeys.Relics.Kaleidoscope)
        {
            return allCharacterCardPoolsUnlocked;
        }
        if (key == BaseGameModelKeys.Relics.ScrollBoxes)
        {
            return scrollBoxesAllowed;
        }
        if (key == BaseGameModelKeys.Relics.WingedBoots ||
            key == BaseGameModelKeys.Relics.SilverCrucible)
        {
            return playersCount == 1;
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

    private static bool IsPositiveCandidate(ModelKey key) =>
        PositivePool.Contains(key, ModelKeyComparer.Instance) ||
        key == BaseGameModelKeys.Relics.LavaRock ||
        key == BaseGameModelKeys.Relics.SmallCapsule ||
        key == BaseGameModelKeys.Relics.NutritiousOyster ||
        key == BaseGameModelKeys.Relics.StoneHumidifier ||
        key == BaseGameModelKeys.Relics.NeowsTalisman ||
        key == BaseGameModelKeys.Relics.Pomander;

    private static bool ContainsBoth(IReadOnlyCollection<ModelKey> values, ModelKey first, ModelKey second) =>
        values.Contains(first, ModelKeyComparer.Instance) && values.Contains(second, ModelKeyComparer.Instance);

    private static bool Conflicts(ModelKey curse, ModelKey positive) =>
        curse == BaseGameModelKeys.Relics.CursedPearl && positive == BaseGameModelKeys.Relics.GoldenPearl ||
        curse == BaseGameModelKeys.Relics.HeftyTablet && positive == BaseGameModelKeys.Relics.ArcaneScroll ||
        curse == BaseGameModelKeys.Relics.LargeCapsule &&
            (positive == BaseGameModelKeys.Relics.LavaRock || positive == BaseGameModelKeys.Relics.SmallCapsule) ||
        curse == BaseGameModelKeys.Relics.LeafyPoultice && positive == BaseGameModelKeys.Relics.NewLeaf ||
        curse == BaseGameModelKeys.Relics.PrecariousShears && positive == BaseGameModelKeys.Relics.PreciseScissors ||
        curse == BaseGameModelKeys.Relics.NeowsSacrifice &&
            (positive == BaseGameModelKeys.Relics.PhialHolster || positive == BaseGameModelKeys.Relics.LostCoffer);
}
