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
}
