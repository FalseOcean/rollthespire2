namespace RolltheSpire2.Core.Identity;

/// <summary>
/// Audited base-game identities used by the current prediction/filter identity catalog.
/// These are exact Category + Entry values, not display aliases.
/// </summary>
public static class BaseGameModelKeys
{
    public static class Categories
    {
        public const string Relic = "RELIC";
        public const string Card = "CARD";
        public const string Potion = "POTION";
        public const string Character = "CHARACTER";
        public const string Event = "EVENT";
        public const string Encounter = "ENCOUNTER";
        public const string Ancient = "ANCIENT";
        public const string Act = "ACT";
    }

    public static class Characters
    {
        public static readonly ModelKey Ironclad = new(Categories.Character, "IRONCLAD");
        public static readonly ModelKey Silent = new(Categories.Character, "SILENT");
        public static readonly ModelKey Defect = new(Categories.Character, "DEFECT");
        public static readonly ModelKey Regent = new(Categories.Character, "REGENT");
        public static readonly ModelKey Necrobinder = new(Categories.Character, "NECROBINDER");

        public static IReadOnlyList<ModelKey> All { get; } =
            new[] { Ironclad, Silent, Defect, Regent, Necrobinder };
    }

    public static class Relics
    {
        public static readonly ModelKey CursedPearl = new(Categories.Relic, "CURSED_PEARL");
        public static readonly ModelKey DowsingRod = new(Categories.Relic, "DOWSING_ROD");
        public static readonly ModelKey HeftyTablet = new(Categories.Relic, "HEFTY_TABLET");
        public static readonly ModelKey LargeCapsule = new(Categories.Relic, "LARGE_CAPSULE");
        public static readonly ModelKey LeafyPoultice = new(Categories.Relic, "LEAFY_POULTICE");
        public static readonly ModelKey NeowsBones = new(Categories.Relic, "NEOWS_BONES");
        public static readonly ModelKey NeowsSacrifice = new(Categories.Relic, "NEOWS_SACRIFICE");
        public static readonly ModelKey PrecariousShears = new(Categories.Relic, "PRECARIOUS_SHEARS");
        public static readonly ModelKey SilkenTress = new(Categories.Relic, "SILKEN_TRESS");
        public static readonly ModelKey SilverCrucible = new(Categories.Relic, "SILVER_CRUCIBLE");
        public static readonly ModelKey ArcaneScroll = new(Categories.Relic, "ARCANE_SCROLL");
        public static readonly ModelKey BoomingConch = new(Categories.Relic, "BOOMING_CONCH");
        public static readonly ModelKey FishingRod = new(Categories.Relic, "FISHING_ROD");
        public static readonly ModelKey GoldenPearl = new(Categories.Relic, "GOLDEN_PEARL");
        public static readonly ModelKey Kaleidoscope = new(Categories.Relic, "KALEIDOSCOPE");
        public static readonly ModelKey LeadPaperweight = new(Categories.Relic, "LEAD_PAPERWEIGHT");
        public static readonly ModelKey LostCoffer = new(Categories.Relic, "LOST_COFFER");
        public static readonly ModelKey MassiveScroll = new(Categories.Relic, "MASSIVE_SCROLL");
        public static readonly ModelKey NeowsTorment = new(Categories.Relic, "NEOWS_TORMENT");
        public static readonly ModelKey NewLeaf = new(Categories.Relic, "NEW_LEAF");
        public static readonly ModelKey PhialHolster = new(Categories.Relic, "PHIAL_HOLSTER");
        public static readonly ModelKey PreciseScissors = new(Categories.Relic, "PRECISE_SCISSORS");
        public static readonly ModelKey ScrollBoxes = new(Categories.Relic, "SCROLL_BOXES");
        public static readonly ModelKey WingedBoots = new(Categories.Relic, "WINGED_BOOTS");
        public static readonly ModelKey LavaRock = new(Categories.Relic, "LAVA_ROCK");
        public static readonly ModelKey NeowsTalisman = new(Categories.Relic, "NEOWS_TALISMAN");
        public static readonly ModelKey NutritiousOyster = new(Categories.Relic, "NUTRITIOUS_OYSTER");
        public static readonly ModelKey Pomander = new(Categories.Relic, "POMANDER");
        public static readonly ModelKey SmallCapsule = new(Categories.Relic, "SMALL_CAPSULE");
        public static readonly ModelKey StoneHumidifier = new(Categories.Relic, "STONE_HUMIDIFIER");

        public static IReadOnlyList<ModelKey> AllNeow { get; } = new[]
        {
            CursedPearl, DowsingRod, HeftyTablet, LargeCapsule, LeafyPoultice,
            NeowsBones, NeowsSacrifice, PrecariousShears, SilkenTress, SilverCrucible,
            ArcaneScroll, BoomingConch, FishingRod, GoldenPearl, Kaleidoscope,
            LeadPaperweight, LostCoffer, MassiveScroll, NeowsTorment, NewLeaf,
            PhialHolster, PreciseScissors, ScrollBoxes, WingedBoots, LavaRock,
            NeowsTalisman, NutritiousOyster, Pomander, SmallCapsule, StoneHumidifier
        };
    }

    public static class Cards
    {
        public static readonly ModelKey Greed = new(Categories.Card, "GREED");
        public static readonly ModelKey NeowsFury = new(Categories.Card, "NEOWS_FURY");
        public static readonly ModelKey Guilty = new(Categories.Card, "GUILTY");
        public static readonly ModelKey Dowsing = new(Categories.Card, "DOWSING");
        public static readonly ModelKey Injury = new(Categories.Card, "INJURY");
        public static readonly ModelKey Claw = new(Categories.Card, "CLAW");
    }

    public static class OrdinaryRelics
    {
        public static readonly ModelKey Whetstone = new(Categories.Relic, "WHETSTONE");
        public static readonly ModelKey WarPaint = new(Categories.Relic, "WAR_PAINT");
        public static readonly ModelKey Circlet = new(Categories.Relic, "CIRCLET");
    }

    public static class Potions
    {
        public static readonly ModelKey Ambergris = new(Categories.Potion, "AMBERGRIS");
    }
}
