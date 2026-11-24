using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Neow;

internal static class Stable107NeowIdentityAnalyzer
{
    private static readonly ModelKey[] CursePool =
    {
        BaseGameModelKeys.Relics.CursedPearl,
        BaseGameModelKeys.Relics.HeftyTablet,
        BaseGameModelKeys.Relics.LargeCapsule,
        BaseGameModelKeys.Relics.LeafyPoultice,
        BaseGameModelKeys.Relics.NeowsBones,
        BaseGameModelKeys.Relics.PrecariousShears,
        BaseGameModelKeys.Relics.SilkenTress,
        BaseGameModelKeys.Relics.SilverCrucible
    };

    private static readonly ModelKey[] PositivePool =
    {
        BaseGameModelKeys.Relics.ArcaneScroll,
        BaseGameModelKeys.Relics.BoomingConch,
        BaseGameModelKeys.Relics.FishingRod,
        BaseGameModelKeys.Relics.GoldenPearl,
        BaseGameModelKeys.Relics.Kaleidoscope,
        BaseGameModelKeys.Relics.LeadPaperweight,
        BaseGameModelKeys.Relics.LostCoffer,
        BaseGameModelKeys.Relics.NeowsTorment,
        BaseGameModelKeys.Relics.NewLeaf,
        BaseGameModelKeys.Relics.PhialHolster,
        BaseGameModelKeys.Relics.PreciseScissors,
        BaseGameModelKeys.Relics.ScrollBoxes,
        BaseGameModelKeys.Relics.WingedBoots
    };

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

    public static IReadOnlyList<ModelKey> Analyze(string canonicalSeed, int playerSlotIndex, out int rngCalls)
    {
        Stable107Profile profile = Stable107Profile.Instance;
        ulong rootSeed = profile.ComputeRootSeed(canonicalSeed);
        ulong eventSeed = profile.DeriveEventStreamSeed(rootSeed, playerSlotIndex, "NEOW");
        var rng = new Xoshiro256StarStar(eventSeed);

        ModelKey curse = CursePool[rng.NextInt(CursePool.Length)];
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
        rng.UnstableShuffle(positives);

        rngCalls = rng.CallCount;
        return new[] { positives[0], positives[1], curse };
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
        curse == BaseGameModelKeys.Relics.PrecariousShears && positive == BaseGameModelKeys.Relics.PreciseScissors;
}
