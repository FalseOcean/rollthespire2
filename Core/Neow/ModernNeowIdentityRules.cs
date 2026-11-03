using System.Collections.Immutable;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Neow;

internal sealed record ModernNeowBinaryChoice(
    ModelKey TrueCandidate,
    ModelKey FalseCandidate,
    ModelKey? SuppressedByCurse = null);

internal sealed record ModernNeowCursePool(
    ModelKey Curse,
    ImmutableArray<ModelKey> FixedCandidates,
    ImmutableArray<ModernNeowBinaryChoice> AppendChoices);

/// <summary>Ordered identity facts frozen for the captured eligibility inputs; contains no RNG state.</summary>
internal sealed record ModernNeowIdentityPreparation(
    ImmutableArray<ModelKey> EligibleCurses,
    ImmutableArray<ModernNeowCursePool> CursePools,
    bool UnknownEligibilityAffectedPool)
{
    internal ModernNeowCursePool ForCurse(ModelKey curse)
    {
        foreach (ModernNeowCursePool pool in CursePools)
            if (pool.Curse == curse) return pool;
        throw new ArgumentException("Not a Modern Neow curse identity.", nameof(curse));
    }
}

/// <summary>
/// Shared Modern Neow mechanism facts. Prediction, numeric execution, authority
/// capture and offer-space consumers use these same ordered rules.
/// </summary>
internal static class ModernNeowIdentityRules
{
    internal static ImmutableArray<ModelKey> PositiveCandidates { get; } =
    [
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
    ];

    internal static ImmutableArray<ModelKey> CurseCandidates { get; } =
    [
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
    ];

    // Order is the native append/draw order; true/false follows NextBool exactly.
    internal static ImmutableArray<ModernNeowBinaryChoice> BinaryChoices { get; } =
    [
        new(BaseGameModelKeys.Relics.LavaRock, BaseGameModelKeys.Relics.SmallCapsule,
            BaseGameModelKeys.Relics.LargeCapsule),
        new(BaseGameModelKeys.Relics.NutritiousOyster, BaseGameModelKeys.Relics.StoneHumidifier),
        new(BaseGameModelKeys.Relics.NeowsTalisman, BaseGameModelKeys.Relics.Pomander)
    ];

    private static readonly ImmutableArray<(ModelKey Curse, ModelKey Positive)> Conflicts =
    [
        (BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.GoldenPearl),
        (BaseGameModelKeys.Relics.HeftyTablet, BaseGameModelKeys.Relics.ArcaneScroll),
        (BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf),
        (BaseGameModelKeys.Relics.PrecariousShears, BaseGameModelKeys.Relics.PreciseScissors),
        (BaseGameModelKeys.Relics.NeowsSacrifice, BaseGameModelKeys.Relics.PhialHolster),
        (BaseGameModelKeys.Relics.NeowsSacrifice, BaseGameModelKeys.Relics.LostCoffer)
    ];

    // Only these three player-count categories affect the rules. Preparing once
    // avoids reconstructing all curse-conditioned pools during per-seed prediction.
    private static readonly ModernNeowIdentityPreparation[] Preparations = CreatePreparations();

    internal static ModernNeowIdentityPreparation Prepare(int playersCount,
        bool? allCharacterCardPoolsUnlocked, bool? scrollBoxesAllowed) =>
        Preparations[(playersCount == 1 ? 1 : playersCount > 1 ? 2 : 0) * 9 +
                     EligibilityIndex(allCharacterCardPoolsUnlocked) * 3 + EligibilityIndex(scrollBoxesAllowed)];

    private static int EligibilityIndex(bool? value) => value.HasValue ? value.Value ? 2 : 1 : 0;

    private static ModernNeowIdentityPreparation[] CreatePreparations()
    {
        bool?[] eligibility = [null, false, true];
        var preparations = new ModernNeowIdentityPreparation[27];
        for (int playersCount = 0; playersCount < 3; playersCount++)
        for (int colors = 0; colors < eligibility.Length; colors++)
        for (int scrolls = 0; scrolls < eligibility.Length; scrolls++)
        {
            bool? colorsAllowed = eligibility[colors], scrollsAllowed = eligibility[scrolls];
            var curses = CurseCandidates.Where(key =>
                IsAllowed(key, playersCount, colorsAllowed, scrollsAllowed) != false).ToImmutableArray();
            var pools = CurseCandidates.Select(curse => new ModernNeowCursePool(curse,
                PositiveCandidates.Where(key => !HasConflict(curse, key) &&
                    IsAllowed(key, playersCount, colorsAllowed, scrollsAllowed) != false).ToImmutableArray(),
                BinaryChoices.Where(choice => choice.SuppressedByCurse != curse).ToImmutableArray())).ToImmutableArray();
            preparations[playersCount * 9 + colors * 3 + scrolls] = new(curses, pools,
                !colorsAllowed.HasValue || !scrollsAllowed.HasValue);
        }
        return preparations;
    }

    internal static bool CanCoOffer(IEnumerable<ModelKey> targets)
    {
        ModelKey[] selected = targets.Distinct(ModelKeyComparer.Instance).ToArray();
        if (selected.Length > 3) return false;
        ModelKey[] curses = selected.Where(key => CurseCandidates.Contains(key)).ToArray();
        if (curses.Length > 1) return false;
        ModelKey[] positives = selected.Where(key => !curses.Contains(key, ModelKeyComparer.Instance)).ToArray();
        if (positives.Length > 2 || positives.Any(key => !IsPositiveCandidate(key))) return false;
        if (BinaryChoices.Any(choice => positives.Contains(choice.TrueCandidate) && positives.Contains(choice.FalseCandidate)))
            return false;
        return curses.Length == 0 || positives.All(positive => !HasConflict(curses[0], positive));
    }

    internal static bool TryGetEligibleCursePool(RuntimeContextAuthoritySnapshot authority,
        out IReadOnlyList<ModelKey> eligible)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.IsProductionBetaProjectionAuthorityExact ||
            !authority.AllCharacterCardPoolsUnlocked.HasValue || !authority.IsScrollBoxesAllowed.HasValue)
        {
            eligible = Array.Empty<ModelKey>();
            return false;
        }
        eligible = Prepare(authority.PlayersCount, authority.AllCharacterCardPoolsUnlocked,
            authority.IsScrollBoxesAllowed).EligibleCurses;
        return eligible.Count != 0;
    }

    internal static bool TryEstimatePositiveOfferProbability(RuntimeContextAuthoritySnapshot authority,
        ModelKey target, out double probability, out string evidence) =>
        TryEstimateOfferProbability(authority, (a, b, c) => a == target || b == target || c == target,
            out probability, out evidence);

    internal static bool TryEstimateOfferProbability(RuntimeContextAuthoritySnapshot authority,
        Func<ModelKey, ModelKey, ModelKey, bool> accepts, out double probability, out string evidence)
    {
        double sum = 0;
        bool exact = TryVisitOfferSpace(authority, (a, b, c, mass) => { if (accepts(a, b, c)) sum += mass; });
        probability = exact ? Math.Clamp(sum, 0, 1) : 0;
        evidence = "NeowOfferJoint;CurseConditionedPool;BinarySlotsAndUnorderedPositivePairs;OneSharedOffer";
        return exact;
    }

    /// <summary>Exact marginal space of one shared offer, conditioned on known authority and eligibility.</summary>
    internal static bool TryVisitOfferSpace(RuntimeContextAuthoritySnapshot authority,
        Action<ModelKey, ModelKey, ModelKey, double> visit)
    {
        if (!TryGetEligibleCursePool(authority, out var curses)) return false;
        var preparation = Prepare(authority.PlayersCount, authority.AllCharacterCardPoolsUnlocked, authority.IsScrollBoxesAllowed);
        foreach (ModelKey curse in curses)
        {
            ModernNeowCursePool pool = preparation.ForCurse(curse);
            int variantCount = 1 << pool.AppendChoices.Length;
            for (int variant = 0; variant < variantCount; variant++)
            {
                var positives = pool.FixedCandidates.ToList();
                for (int slot = 0; slot < pool.AppendChoices.Length; slot++)
                {
                    ModernNeowBinaryChoice choice = pool.AppendChoices[slot];
                    positives.Add((variant & (1 << (pool.AppendChoices.Length - slot - 1))) == 0
                        ? choice.TrueCandidate : choice.FalseCandidate);
                }
                int n = positives.Count;
                if (n < 2) return false;
                double mass = 2d / (curses.Count * variantCount * (double)n * (n - 1));
                for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    visit(positives[i], positives[j], curse, mass);
            }
        }
        return true;
    }

    // Top-level offers and Bones acquisition share eligibility, not top-level conflicts.
    // Null retains unknown candidates; each consumer keeps its own precision policy.
    internal static bool? IsAllowed(ModelKey key, int playersCount,
        bool? allCharacterCardPoolsUnlocked, bool? scrollBoxesAllowed)
    {
        if (key == BaseGameModelKeys.Relics.MassiveScroll) return playersCount > 1;
        if (key == BaseGameModelKeys.Relics.Kaleidoscope) return allCharacterCardPoolsUnlocked;
        if (key == BaseGameModelKeys.Relics.ScrollBoxes) return scrollBoxesAllowed;
        if (key == BaseGameModelKeys.Relics.WingedBoots || key == BaseGameModelKeys.Relics.SilverCrucible)
            return playersCount == 1;
        return true;
    }

    private static bool IsPositiveCandidate(ModelKey key) => PositiveCandidates.Contains(key) ||
        BinaryChoices.Any(choice => choice.TrueCandidate == key || choice.FalseCandidate == key);

    private static bool HasConflict(ModelKey curse, ModelKey positive) =>
        Conflicts.Any(pair => pair.Curse == curse && pair.Positive == positive) ||
        BinaryChoices.Any(choice => choice.SuppressedByCurse == curse &&
            (choice.TrueCandidate == positive || choice.FalseCandidate == positive));
}
