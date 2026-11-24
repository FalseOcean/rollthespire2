using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class Beta110FastRelicCatalog
{
    public const byte InvalidId = byte.MaxValue;
    public const byte CursedPearl = 0;
    public const byte DowsingRod = 1;
    public const byte HeftyTablet = 2;
    public const byte LargeCapsule = 3;
    public const byte LeafyPoultice = 4;
    public const byte NeowsBones = 5;
    public const byte NeowsSacrifice = 6;
    public const byte PrecariousShears = 7;
    public const byte SilkenTress = 8;
    public const byte SilverCrucible = 9;
    public const byte ArcaneScroll = 10;
    public const byte BoomingConch = 11;
    public const byte FishingRod = 12;
    public const byte GoldenPearl = 13;
    public const byte Kaleidoscope = 14;
    public const byte LeadPaperweight = 15;
    public const byte LostCoffer = 16;
    public const byte MassiveScroll = 17;
    public const byte NeowsTorment = 18;
    public const byte NewLeaf = 19;
    public const byte PhialHolster = 20;
    public const byte PreciseScissors = 21;
    public const byte ScrollBoxes = 22;
    public const byte WingedBoots = 23;
    public const byte LavaRock = 24;
    public const byte NeowsTalisman = 25;
    public const byte NutritiousOyster = 26;
    public const byte Pomander = 27;
    public const byte SmallCapsule = 28;
    public const byte StoneHumidifier = 29;

    private static readonly ModelKey[] Keys = BaseGameModelKeys.Relics.AllNeow.ToArray();

    public static bool IsAbiCompatible { get; } = ValidateAbi();

    public static bool TryGetId(ModelKey key, out byte id)
    {
        if (!IsAbiCompatible)
        {
            id = InvalidId;
            return false;
        }
        for (int index = 0; index < Keys.Length; index++)
        {
            if (Keys[index] == key)
            {
                id = checked((byte)index);
                return true;
            }
        }
        id = InvalidId;
        return false;
    }


    private static bool ValidateAbi()
    {
        ModelKey[] expected =
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
            BaseGameModelKeys.Relics.SilverCrucible,
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
            BaseGameModelKeys.Relics.WingedBoots,
            BaseGameModelKeys.Relics.LavaRock,
            BaseGameModelKeys.Relics.NeowsTalisman,
            BaseGameModelKeys.Relics.NutritiousOyster,
            BaseGameModelKeys.Relics.Pomander,
            BaseGameModelKeys.Relics.SmallCapsule,
            BaseGameModelKeys.Relics.StoneHumidifier
        };
        return Keys.Length == expected.Length && Keys.SequenceEqual(expected, ModelKeyComparer.Instance);
    }

    public static ModelKey KeyOf(byte id) => id < Keys.Length ? Keys[id] : default;
    public static ulong Bit(byte id) => id < 64 ? 1UL << id : 0UL;
}
