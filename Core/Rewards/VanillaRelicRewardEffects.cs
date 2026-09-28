using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Rewards;

[Flags]
public enum RelicOnObtainRewardEffects
{
    None = 0,
    ConsumesRewardsRng = 1 << 0,
    ConsumesNicheRng = 1 << 1,
    ConsumesTransformationsRng = 1 << 2,
    ConsumesOtherRng = 1 << 3,
    ChangesPotionRewardState = 1 << 4,
    ChangesCardRewardPool = 1 << 5,
    ChangesPotionRewardPool = 1 << 6,
    NestedRelicObtain = 1 << 7,
    NestedCardGeneration = 1 << 8,
    NestedPotionGeneration = 1 << 9,
    RequiresPlayerChoice = 1 << 10,
    RuntimeConditional = 1 << 11,
    OtherDomainOnly = 1 << 12,
    Unknown = 1 << 13,
}

public enum HeldNormalCombatRewardImpactClass
{
    Neutral,
    KnownDeterministicImpact,
    KnownRewardsRngImpact,
    KnownRuntimeConditionalImpact,
    OtherRewardDomainOnly,
    UnknownModImpact,
}

[Flags]
public enum HeldNormalCombatRewardEffects
{
    None = 0,
    ForcesPotionReward = 1 << 0,
    ChangesPotionDropState = 1 << 1,
    ChangesPotionRewardPool = 1 << 2,
    ChangesCardRewardPool = 1 << 3,
    ChangesCardRewardCount = 1 << 4,
    ChangesCardRewardOptions = 1 << 5,
    DeterministicCardRewardMutation = 1 << 6,
    ChangesGoldReward = 1 << 7,
    AddsRewardEntries = 1 << 8,
    RemovesRewardEntries = 1 << 9,
    ReplacesRewardEntries = 1 << 10,
    ConsumesAdditionalRewardsRng = 1 << 11,
    ConsumesNicheRng = 1 << 12,
    AddsCardRewardAlternative = 1 << 13,
    NestedRelicObtain = 1 << 14,
    RequiresSavedCounter = 1 << 15,
    RequiresCombatOutcome = 1 << 16,
    RequiresPlayerChoice = 1 << 17,
    RequiresLivePowerState = 1 << 18,
    OtherRewardDomainOnly = 1 << 19,
    UnknownModHook = 1 << 20,
}

public enum RelicRewardPredictionSupport
{
    NeutralSupported,
    ContinuationSupported,
    SpecialImpactSupported,
    KnownImpactNotImplemented,
    ConditionalOutOfScope,
    VanillaCapabilityNotAudited,
    UnknownModContent,
}

public sealed record VanillaRelicRewardEffect(
    RuntimeProfileId ProfileId,
    ModelKey RelicKey,
    RelicOnObtainRewardEffects OnObtainCapabilities,
    string OnObtainDrawShape,
    HeldNormalCombatRewardImpactClass HeldImpactClass,
    HeldNormalCombatRewardEffects HeldCapabilities,
    IReadOnlyList<string> RuntimeDependencies,
    RelicRewardPredictionSupport PredictionSupport,
    string EvidenceCode)
{
    public bool IsHeldNeutralForNormalCombatReward =>
        HeldImpactClass is HeldNormalCombatRewardImpactClass.Neutral or
            HeldNormalCombatRewardImpactClass.OtherRewardDomainOnly;

    public bool NestedOnObtainPreservesRewardContinuation =>
        (OnObtainCapabilities & VanillaRelicRewardEffects.NestedRewardBlockingCapabilities) == 0;
}

/// <summary>
/// Generated compact production projection of the complete vanilla relic audit.
/// The full 596-record evidence catalog remains in docs/source_audits; runtime
/// code stores explicit profile membership plus only non-neutral overrides.
/// </summary>
public static class VanillaRelicRewardEffects
{
    public const string AuditCatalogSha256 = "9ec2b21569f601d4f576b6ad2e2efbf0dec3248a550668dbef325dc789bb21ab";
    public const int Stable107RecordCount = 297;
    public const int Beta109RecordCount = 299;

    public const RelicOnObtainRewardEffects NestedRewardBlockingCapabilities =
        RelicOnObtainRewardEffects.ConsumesRewardsRng |
        RelicOnObtainRewardEffects.ChangesPotionRewardState |
        RelicOnObtainRewardEffects.ChangesCardRewardPool |
        RelicOnObtainRewardEffects.ChangesPotionRewardPool |
        RelicOnObtainRewardEffects.NestedRelicObtain |
        RelicOnObtainRewardEffects.Unknown;

    private static readonly HashSet<string> Stable107Entries = new(StringComparer.Ordinal)
    {
        "AKABEKO", "ALCHEMICAL_COFFER", "AMETHYST_AUBERGINE", "ANCHOR",
        "ARCANE_SCROLL", "ARCHAIC_TOOTH", "ART_OF_WAR", "ASTROLABE",
        "BAG_OF_MARBLES", "BAG_OF_PREPARATION", "BEATING_REMNANT", "BEAUTIFUL_BRACELET",
        "BELLOWS", "BELT_BUCKLE", "BIG_HAT", "BIG_MUSHROOM",
        "BIIIG_HUG", "BING_BONG", "BLACK_BLOOD", "BLACK_STAR",
        "BLESSED_ANTLER", "BLOOD_SOAKED_ROSE", "BLOOD_VIAL", "BONE_FLUTE",
        "BONE_TEA", "BOOKMARK", "BOOK_OF_FIVE_RINGS", "BOOK_REPAIR_KNIFE",
        "BOOMING_CONCH", "BOUND_PHYLACTERY", "BOWLER_HAT", "BREAD",
        "BRILLIANT_SCARF", "BRIMSTONE", "BRONZE_SCALES", "BURNING_BLOOD",
        "BURNING_STICKS", "BYRDPIP", "CALLING_BELL", "CANDELABRA",
        "CAPTAINS_WHEEL", "CAULDRON", "CENTENNIAL_PUZZLE", "CHANDELIER",
        "CHARONS_ASHES", "CHEMICAL_X", "CHOICES_PARADOX", "CHOSEN_CHEESE",
        "CIRCLET", "CLAWS", "CLOAK_CLASP", "CRACKED_CORE",
        "CROSSBOW", "CURSED_PEARL", "DARKSTONE_PERIAPT", "DATA_DISK",
        "DAUGHTER_OF_THE_WIND", "DELICATE_FROND", "DEMON_TONGUE", "DEPRECATED_RELIC",
        "DIAMOND_DIADEM", "DINGY_RUG", "DISTINGUISHED_CAPE", "DIVINE_DESTINY",
        "DIVINE_RIGHT", "DOLLYS_MIRROR", "DRAGON_FRUIT", "DREAM_CATCHER",
        "DRIFTWOOD", "DUSTY_TOME", "ECTOPLASM", "ELECTRIC_SHRYMP",
        "EMBER_TEA", "EMOTION_CHIP", "EMPTY_CAGE", "ETERNAL_FEATHER",
        "FAKE_ANCHOR", "FAKE_BLOOD_VIAL", "FAKE_HAPPY_FLOWER", "FAKE_LEES_WAFFLE",
        "FAKE_MANGO", "FAKE_MERCHANTS_RUG", "FAKE_ORICHALCUM", "FAKE_SNECKO_EYE",
        "FAKE_STRIKE_DUMMY", "FAKE_VENERABLE_TEA_SET", "FENCING_MANUAL", "FESTIVE_POPPER",
        "FIDDLE", "FISHING_ROD", "FORGOTTEN_SOUL", "FRAGRANT_MUSHROOM",
        "FRESNEL_LENS", "FROZEN_EGG", "FUNERARY_MASK", "FUR_COAT",
        "GALACTIC_DUST", "GAMBLING_CHIP", "GAME_PIECE", "GHOST_SEED",
        "GIRYA", "GLASS_EYE", "GLITTER", "GNARLED_HAMMER",
        "GOLDEN_COMPASS", "GOLDEN_PEARL", "GOLD_PLATED_CABLES", "GORGET",
        "GREMLIN_HORN", "HAND_DRILL", "HAPPY_FLOWER", "HEFTY_TABLET",
        "HELICAL_DART", "HISTORY_COURSE", "HORN_CLEAT", "ICE_CREAM",
        "INFUSED_CORE", "INTIMIDATING_HELMET", "IRON_CLUB", "IVORY_TILE",
        "JEWELED_MASK", "JEWELRY_BOX", "JOSS_PAPER", "JUZU_BRACELET",
        "KALEIDOSCOPE", "KIFUDA", "KUNAI", "KUSARIGAMA",
        "LANTERN", "LARGE_CAPSULE", "LASTING_CANDY", "LAVA_LAMP",
        "LAVA_ROCK", "LEAD_PAPERWEIGHT", "LEAFY_POULTICE", "LEES_WAFFLE",
        "LETTER_OPENER", "LIZARD_TAIL", "LOOMING_FRUIT", "LORDS_PARASOL",
        "LOST_COFFER", "LOST_WISP", "LUCKY_FYSH", "LUNAR_PASTRY",
        "MANGO", "MASSIVE_SCROLL", "MAW_BANK", "MEAL_TICKET",
        "MEAT_CLEAVER", "MEAT_ON_THE_BONE", "MEMBERSHIP_CARD", "MERCURY_HOURGLASS",
        "METRONOME", "MINIATURE_CANNON", "MINIATURE_TENT", "MINI_REGENT",
        "MOLTEN_EGG", "MR_STRUGGLES", "MUMMIFIED_HAND", "MUSIC_BOX",
        "MYSTIC_LIGHTER", "NEOWS_BONES", "NEOWS_TALISMAN", "NEOWS_TORMENT",
        "NEW_LEAF", "NINJA_SCROLL", "NUNCHAKU", "NUTRITIOUS_OYSTER",
        "NUTRITIOUS_SOUP", "ODDLY_SMOOTH_STONE", "OLD_COIN", "ORANGE_DOUGH",
        "ORICHALCUM", "ORNAMENTAL_FAN", "ORRERY", "PAELS_BLOOD",
        "PAELS_CLAW", "PAELS_EYE", "PAELS_FLESH", "PAELS_GROWTH",
        "PAELS_HORN", "PAELS_LEGION", "PAELS_TEARS", "PAELS_TOOTH",
        "PAELS_WING", "PANDORAS_BOX", "PANTOGRAPH", "PAPER_KRANE",
        "PAPER_PHROG", "PARRYING_SHIELD", "PEAR", "PENDULUM",
        "PEN_NIB", "PERMAFROST", "PETRIFIED_TOAD", "PHIAL_HOLSTER",
        "PHILOSOPHERS_STONE", "PHYLACTERY_UNBOUND", "PLANISPHERE", "POCKETWATCH",
        "POLLINOUS_CORE", "POMANDER", "POTION_BELT", "POWER_CELL",
        "PRAYER_WHEEL", "PRECARIOUS_SHEARS", "PRECISE_SCISSORS", "PRESERVED_FOG",
        "PRISMATIC_GEM", "PUMPKIN_CANDLE", "PUNCH_DAGGER", "RADIANT_PEARL",
        "RAINBOW_RING", "RAZOR_TOOTH", "RED_MASK", "RED_SKULL",
        "REGALITE", "REGAL_PILLOW", "REPTILE_TRINKET", "RINGING_TRIANGLE",
        "RING_OF_THE_DRAKE", "RING_OF_THE_SNAKE", "RIPPLE_BASIN", "ROYAL_POISON",
        "ROYAL_STAMP", "RUINED_HELMET", "RUNIC_CAPACITOR", "RUNIC_PYRAMID",
        "SAI", "SAND_CASTLE", "SCREAMING_FLAGON", "SCROLL_BOXES",
        "SEAL_OF_GOLD", "SEA_GLASS", "SELF_FORMING_CLAY", "SERE_TALON",
        "SHOVEL", "SHURIKEN", "SIGNET_RING", "SILKEN_TRESS",
        "SILVER_CRUCIBLE", "SLING_OF_COURAGE", "SMALL_CAPSULE", "SNECKO_EYE",
        "SNECKO_SKULL", "SOZU", "SPARKLING_ROUGE", "SPIKED_GAUNTLETS",
        "STONE_CALENDAR", "STONE_CRACKER", "STONE_HUMIDIFIER", "STORYBOOK",
        "STRAWBERRY", "STRIKE_DUMMY", "STURDY_CLAMP", "SWORD_OF_JADE",
        "SWORD_OF_STONE", "SYMBIOTIC_VIRUS", "TANXS_WHISTLE", "TEA_OF_DISCOURTESY",
        "THE_ABACUS", "THE_BOOT", "THE_COURIER", "THROWING_AXE",
        "TINGSHA", "TINY_MAILBOX", "TOASTY_MITTENS", "TOOLBOX",
        "TOUCH_OF_OROBAS", "TOUGH_BANDAGES", "TOXIC_EGG", "TOY_BOX",
        "TRI_BOOMERANG", "TUNGSTEN_ROD", "TUNING_FORK", "TWISTED_FUNNEL",
        "UNCEASING_TOP", "UNDYING_SIGIL", "UNSETTLING_LAMP", "VAJRA",
        "VAMBRACE", "VELVET_CHOKER", "VENERABLE_TEA_SET", "VERY_HOT_COCOA",
        "VEXING_PUZZLEBOX", "VITRUVIAN_MINION", "WAR_HAMMER", "WAR_PAINT",
        "WHETSTONE", "WHISPERING_EARRING", "WHITE_BEAST_STATUE", "WHITE_STAR",
        "WINGED_BOOTS", "WING_CHARM", "WONGOS_MYSTERY_TICKET", "WONGO_CUSTOMER_APPRECIATION_BADGE",
        "YUMMY_COOKIE",
    };

    private static readonly HashSet<string> Beta109Entries = new(StringComparer.Ordinal)
    {
        "AKABEKO", "ALCHEMICAL_COFFER", "AMETHYST_AUBERGINE", "ANCHOR",
        "ARCANE_SCROLL", "ARCHAIC_TOOTH", "ART_OF_WAR", "ASTROLABE",
        "BAG_OF_MARBLES", "BAG_OF_PREPARATION", "BEATING_REMNANT", "BEAUTIFUL_BRACELET",
        "BELLOWS", "BELT_BUCKLE", "BIG_HAT", "BIG_MUSHROOM",
        "BIIIG_HUG", "BING_BONG", "BLACK_BLOOD", "BLACK_STAR",
        "BLESSED_ANTLER", "BLOOD_SOAKED_ROSE", "BLOOD_VIAL", "BONE_FLUTE",
        "BONE_TEA", "BOOKMARK", "BOOK_OF_FIVE_RINGS", "BOOK_REPAIR_KNIFE",
        "BOOMING_CONCH", "BOUND_PHYLACTERY", "BOWLER_HAT", "BREAD",
        "BRILLIANT_SCARF", "BRIMSTONE", "BRONZE_SCALES", "BURNING_BLOOD",
        "BURNING_STICKS", "BYRDPIP", "CALLING_BELL", "CANDELABRA",
        "CAPTAINS_WHEEL", "CAULDRON", "CENTENNIAL_PUZZLE", "CHANDELIER",
        "CHARONS_ASHES", "CHEMICAL_X", "CHOICES_PARADOX", "CHOSEN_CHEESE",
        "CIRCLET", "CLAWS", "CLOAK_CLASP", "CRACKED_CORE",
        "CROSSBOW", "CURSED_PEARL", "DARKSTONE_PERIAPT", "DATA_DISK",
        "DAUGHTER_OF_THE_WIND", "DELICATE_FROND", "DEMON_TONGUE", "DEPRECATED_RELIC",
        "DIAMOND_DIADEM", "DINGY_RUG", "DISTINGUISHED_CAPE", "DIVINE_DESTINY",
        "DIVINE_RIGHT", "DOLLYS_MIRROR", "DOWSING_ROD", "DRAGON_FRUIT",
        "DREAM_CATCHER", "DRIFTWOOD", "DUSTY_TOME", "ECTOPLASM",
        "ELECTRIC_SHRYMP", "EMBER_TEA", "EMOTION_CHIP", "EMPTY_CAGE",
        "ETERNAL_FEATHER", "FAKE_ANCHOR", "FAKE_BLOOD_VIAL", "FAKE_HAPPY_FLOWER",
        "FAKE_LEES_WAFFLE", "FAKE_MANGO", "FAKE_MERCHANTS_RUG", "FAKE_ORICHALCUM",
        "FAKE_SNECKO_EYE", "FAKE_STRIKE_DUMMY", "FAKE_VENERABLE_TEA_SET", "FENCING_MANUAL",
        "FESTIVE_POPPER", "FIDDLE", "FISHING_ROD", "FORGOTTEN_SOUL",
        "FRAGRANT_MUSHROOM", "FRESNEL_LENS", "FROZEN_EGG", "FUNERARY_MASK",
        "FUR_COAT", "GALACTIC_DUST", "GAMBLING_CHIP", "GAME_PIECE",
        "GHOST_SEED", "GIRYA", "GLASS_EYE", "GLITTER",
        "GNARLED_HAMMER", "GOLDEN_COMPASS", "GOLDEN_PEARL", "GOLD_PLATED_CABLES",
        "GORGET", "GREMLIN_HORN", "HAND_DRILL", "HAPPY_FLOWER",
        "HEFTY_TABLET", "HELICAL_DART", "HISTORY_COURSE", "HORN_CLEAT",
        "ICE_CREAM", "INFUSED_CORE", "INTIMIDATING_HELMET", "IRON_CLUB",
        "IVORY_TILE", "JEWELED_MASK", "JEWELRY_BOX", "JOSS_PAPER",
        "JUZU_BRACELET", "KALEIDOSCOPE", "KIFUDA", "KUNAI",
        "KUSARIGAMA", "LANTERN", "LARGE_CAPSULE", "LASTING_CANDY",
        "LAVA_LAMP", "LAVA_ROCK", "LEAD_PAPERWEIGHT", "LEAFY_POULTICE",
        "LEES_WAFFLE", "LETTER_OPENER", "LIZARD_TAIL", "LOOMING_FRUIT",
        "LORDS_PARASOL", "LOST_COFFER", "LOST_WISP", "LUCKY_FYSH",
        "LUNAR_PASTRY", "MANGO", "MASSIVE_SCROLL", "MAW_BANK",
        "MEAL_TICKET", "MEAT_CLEAVER", "MEAT_ON_THE_BONE", "MEMBERSHIP_CARD",
        "MERCURY_HOURGLASS", "METRONOME", "MINIATURE_CANNON", "MINIATURE_TENT",
        "MINI_REGENT", "MOLTEN_EGG", "MR_STRUGGLES", "MUMMIFIED_HAND",
        "MUSIC_BOX", "MYSTIC_LIGHTER", "NEOWS_BONES", "NEOWS_SACRIFICE",
        "NEOWS_TALISMAN", "NEOWS_TORMENT", "NEW_LEAF", "NINJA_SCROLL",
        "NUNCHAKU", "NUTRITIOUS_OYSTER", "NUTRITIOUS_SOUP", "ODDLY_SMOOTH_STONE",
        "OLD_COIN", "ORANGE_DOUGH", "ORICHALCUM", "ORNAMENTAL_FAN",
        "ORRERY", "PAELS_BLOOD", "PAELS_CLAW", "PAELS_EYE",
        "PAELS_FLESH", "PAELS_GROWTH", "PAELS_HORN", "PAELS_LEGION",
        "PAELS_TEARS", "PAELS_TOOTH", "PAELS_WING", "PANDORAS_BOX",
        "PANTOGRAPH", "PAPER_KRANE", "PAPER_PHROG", "PARRYING_SHIELD",
        "PEAR", "PENDULUM", "PEN_NIB", "PERMAFROST",
        "PETRIFIED_TOAD", "PHIAL_HOLSTER", "PHILOSOPHERS_STONE", "PHYLACTERY_UNBOUND",
        "PLANISPHERE", "POCKETWATCH", "POLLINOUS_CORE", "POMANDER",
        "POTION_BELT", "POWER_CELL", "PRAYER_WHEEL", "PRECARIOUS_SHEARS",
        "PRECISE_SCISSORS", "PRESERVED_FOG", "PRISMATIC_GEM", "PUMPKIN_CANDLE",
        "PUNCH_DAGGER", "RADIANT_PEARL", "RAINBOW_RING", "RAZOR_TOOTH",
        "RED_MASK", "RED_SKULL", "REGALITE", "REGAL_PILLOW",
        "REPTILE_TRINKET", "RINGING_TRIANGLE", "RING_OF_THE_DRAKE", "RING_OF_THE_SNAKE",
        "RIPPLE_BASIN", "ROYAL_POISON", "ROYAL_STAMP", "RUINED_HELMET",
        "RUNIC_CAPACITOR", "RUNIC_PYRAMID", "SAI", "SAND_CASTLE",
        "SCREAMING_FLAGON", "SCROLL_BOXES", "SEAL_OF_GOLD", "SEA_GLASS",
        "SELF_FORMING_CLAY", "SERE_TALON", "SHOVEL", "SHURIKEN",
        "SIGNET_RING", "SILKEN_TRESS", "SILVER_CRUCIBLE", "SLING_OF_COURAGE",
        "SMALL_CAPSULE", "SNECKO_EYE", "SNECKO_SKULL", "SOZU",
        "SPARKLING_ROUGE", "SPIKED_GAUNTLETS", "STONE_CALENDAR", "STONE_CRACKER",
        "STONE_HUMIDIFIER", "STORYBOOK", "STRAWBERRY", "STRIKE_DUMMY",
        "STURDY_CLAMP", "SWORD_OF_JADE", "SWORD_OF_STONE", "SYMBIOTIC_VIRUS",
        "TANXS_WHISTLE", "TEA_OF_DISCOURTESY", "THE_ABACUS", "THE_BOOT",
        "THE_COURIER", "THROWING_AXE", "TINGSHA", "TINY_MAILBOX",
        "TOASTY_MITTENS", "TOOLBOX", "TOUCH_OF_OROBAS", "TOUGH_BANDAGES",
        "TOXIC_EGG", "TOY_BOX", "TRI_BOOMERANG", "TUNGSTEN_ROD",
        "TUNING_FORK", "TWISTED_FUNNEL", "UNCEASING_TOP", "UNDYING_SIGIL",
        "UNSETTLING_LAMP", "VAJRA", "VAMBRACE", "VELVET_CHOKER",
        "VENERABLE_TEA_SET", "VERY_HOT_COCOA", "VEXING_PUZZLEBOX", "VITRUVIAN_MINION",
        "WAR_HAMMER", "WAR_PAINT", "WHETSTONE", "WHISPERING_EARRING",
        "WHITE_BEAST_STATUE", "WHITE_STAR", "WINGED_BOOTS", "WING_CHARM",
        "WONGOS_MYSTERY_TICKET", "WONGO_CUSTOMER_APPRECIATION_BADGE", "YUMMY_COOKIE",
    };

    private static readonly IReadOnlyDictionary<string, RelicOnObtainRewardEffects> OnObtainOverrides =
        new Dictionary<string, RelicOnObtainRewardEffects>(StringComparer.Ordinal)
        {
            ["ARCANE_SCROLL"] =
                RelicOnObtainRewardEffects.ConsumesRewardsRng |
                RelicOnObtainRewardEffects.NestedCardGeneration,
            ["HEFTY_TABLET"] =
                RelicOnObtainRewardEffects.ConsumesRewardsRng |
                RelicOnObtainRewardEffects.NestedCardGeneration |
                RelicOnObtainRewardEffects.RequiresPlayerChoice,
            ["KALEIDOSCOPE"] =
                RelicOnObtainRewardEffects.ConsumesRewardsRng |
                RelicOnObtainRewardEffects.ConsumesNicheRng |
                RelicOnObtainRewardEffects.NestedCardGeneration |
                RelicOnObtainRewardEffects.RequiresPlayerChoice,
            ["LARGE_CAPSULE"] =
                RelicOnObtainRewardEffects.ConsumesRewardsRng |
                RelicOnObtainRewardEffects.NestedRelicObtain |
                RelicOnObtainRewardEffects.NestedCardGeneration,
            ["LEAD_PAPERWEIGHT"] =
                RelicOnObtainRewardEffects.ConsumesRewardsRng |
                RelicOnObtainRewardEffects.NestedCardGeneration |
                RelicOnObtainRewardEffects.RequiresPlayerChoice,
            ["LOST_COFFER"] =
                RelicOnObtainRewardEffects.ConsumesRewardsRng |
                RelicOnObtainRewardEffects.NestedCardGeneration |
                RelicOnObtainRewardEffects.NestedPotionGeneration |
                RelicOnObtainRewardEffects.RequiresPlayerChoice,
            ["NEOWS_BONES"] =
                RelicOnObtainRewardEffects.ConsumesRewardsRng |
                RelicOnObtainRewardEffects.ConsumesNicheRng |
                RelicOnObtainRewardEffects.NestedRelicObtain |
                RelicOnObtainRewardEffects.NestedCardGeneration |
                RelicOnObtainRewardEffects.RequiresPlayerChoice |
                RelicOnObtainRewardEffects.RuntimeConditional,
            ["NEW_LEAF"] =
                RelicOnObtainRewardEffects.ConsumesNicheRng |
                RelicOnObtainRewardEffects.RequiresPlayerChoice,
            ["ORRERY"] =
                RelicOnObtainRewardEffects.ConsumesRewardsRng |
                RelicOnObtainRewardEffects.NestedCardGeneration |
                RelicOnObtainRewardEffects.RequiresPlayerChoice,
            ["SCROLL_BOXES"] =
                RelicOnObtainRewardEffects.ConsumesRewardsRng |
                RelicOnObtainRewardEffects.NestedCardGeneration |
                RelicOnObtainRewardEffects.RequiresPlayerChoice,
            ["SMALL_CAPSULE"] = RelicOnObtainRewardEffects.NestedRelicObtain,
            ["WAR_PAINT"] = RelicOnObtainRewardEffects.ConsumesNicheRng,
            ["WHETSTONE"] = RelicOnObtainRewardEffects.ConsumesNicheRng,
        };

    private static readonly IReadOnlyDictionary<string, string> OnObtainDrawShapes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ARCANE_SCROLL"] = "Fixed",
            ["HEFTY_TABLET"] = "Fixed",
            ["KALEIDOSCOPE"] = "Variable",
            ["LARGE_CAPSULE"] = "Nested",
            ["LEAD_PAPERWEIGHT"] = "Fixed",
            ["LOST_COFFER"] = "Variable",
            ["NEOWS_BONES"] = "Variable",
            ["NEW_LEAF"] = "Variable",
            ["ORRERY"] = "Fixed",
            ["SCROLL_BOXES"] = "Variable",
            ["SMALL_CAPSULE"] = "Nested",
            ["WAR_PAINT"] = "Variable",
            ["WHETSTONE"] = "Variable",
        };

    private static readonly IReadOnlyDictionary<string, HeldImpactOverride> HeldOverrides =
        new Dictionary<string, HeldImpactOverride>(StringComparer.Ordinal)
        {
            ["AMETHYST_AUBERGINE"] = new(
                HeldNormalCombatRewardImpactClass.KnownDeterministicImpact,
                HeldNormalCombatRewardEffects.AddsRewardEntries |
                HeldNormalCombatRewardEffects.ChangesGoldReward),
            ["BLACK_STAR"] = new(HeldNormalCombatRewardImpactClass.OtherRewardDomainOnly, HeldNormalCombatRewardEffects.OtherRewardDomainOnly),
            ["DINGY_RUG"] = new(HeldNormalCombatRewardImpactClass.KnownDeterministicImpact, HeldNormalCombatRewardEffects.ChangesCardRewardPool),
            ["DRIFTWOOD"] = new(
                HeldNormalCombatRewardImpactClass.KnownRuntimeConditionalImpact,
                HeldNormalCombatRewardEffects.ChangesCardRewardOptions |
                HeldNormalCombatRewardEffects.ConsumesAdditionalRewardsRng |
                HeldNormalCombatRewardEffects.RequiresPlayerChoice),
            ["FRESNEL_LENS"] = new(HeldNormalCombatRewardImpactClass.KnownDeterministicImpact, HeldNormalCombatRewardEffects.DeterministicCardRewardMutation),
            ["FROZEN_EGG"] = new(HeldNormalCombatRewardImpactClass.KnownDeterministicImpact, HeldNormalCombatRewardEffects.DeterministicCardRewardMutation),
            ["GLITTER"] = new(HeldNormalCombatRewardImpactClass.KnownDeterministicImpact, HeldNormalCombatRewardEffects.DeterministicCardRewardMutation),
            ["LASTING_CANDY"] = new(
                HeldNormalCombatRewardImpactClass.KnownRuntimeConditionalImpact,
                HeldNormalCombatRewardEffects.ChangesCardRewardOptions |
                HeldNormalCombatRewardEffects.ConsumesAdditionalRewardsRng |
                HeldNormalCombatRewardEffects.RequiresSavedCounter |
                HeldNormalCombatRewardEffects.RequiresCombatOutcome),
            ["LAVA_LAMP"] = new(
                HeldNormalCombatRewardImpactClass.KnownRuntimeConditionalImpact,
                HeldNormalCombatRewardEffects.DeterministicCardRewardMutation |
                HeldNormalCombatRewardEffects.RequiresCombatOutcome),
            ["LAVA_ROCK"] = new(HeldNormalCombatRewardImpactClass.OtherRewardDomainOnly, HeldNormalCombatRewardEffects.OtherRewardDomainOnly),
            ["MOLTEN_EGG"] = new(HeldNormalCombatRewardImpactClass.KnownDeterministicImpact, HeldNormalCombatRewardEffects.DeterministicCardRewardMutation),
            ["NEOWS_BONES"] = new(HeldNormalCombatRewardImpactClass.OtherRewardDomainOnly, HeldNormalCombatRewardEffects.OtherRewardDomainOnly),
            ["PAELS_WING"] = new(
                HeldNormalCombatRewardImpactClass.KnownRuntimeConditionalImpact,
                HeldNormalCombatRewardEffects.AddsCardRewardAlternative |
                HeldNormalCombatRewardEffects.NestedRelicObtain |
                HeldNormalCombatRewardEffects.RequiresPlayerChoice),
            ["PRAYER_WHEEL"] = new(
                HeldNormalCombatRewardImpactClass.KnownRewardsRngImpact,
                HeldNormalCombatRewardEffects.AddsRewardEntries |
                HeldNormalCombatRewardEffects.ChangesCardRewardCount |
                HeldNormalCombatRewardEffects.ConsumesAdditionalRewardsRng),
            ["PRISMATIC_GEM"] = new(HeldNormalCombatRewardImpactClass.KnownDeterministicImpact, HeldNormalCombatRewardEffects.ChangesCardRewardPool),
            ["SILKEN_TRESS"] = new(HeldNormalCombatRewardImpactClass.KnownDeterministicImpact, HeldNormalCombatRewardEffects.DeterministicCardRewardMutation),
            ["SILVER_CRUCIBLE"] = new(
                HeldNormalCombatRewardImpactClass.KnownRuntimeConditionalImpact,
                HeldNormalCombatRewardEffects.DeterministicCardRewardMutation |
                HeldNormalCombatRewardEffects.RequiresSavedCounter),
            ["TOXIC_EGG"] = new(HeldNormalCombatRewardImpactClass.KnownDeterministicImpact, HeldNormalCombatRewardEffects.DeterministicCardRewardMutation),
            ["WHITE_BEAST_STATUE"] = new(
                HeldNormalCombatRewardImpactClass.KnownRewardsRngImpact,
                HeldNormalCombatRewardEffects.ForcesPotionReward |
                HeldNormalCombatRewardEffects.ChangesPotionDropState),
            ["WHITE_STAR"] = new(HeldNormalCombatRewardImpactClass.OtherRewardDomainOnly, HeldNormalCombatRewardEffects.OtherRewardDomainOnly),
            ["WING_CHARM"] = new(
                HeldNormalCombatRewardImpactClass.KnownRewardsRngImpact,
                HeldNormalCombatRewardEffects.ChangesCardRewardOptions |
                HeldNormalCombatRewardEffects.ConsumesNicheRng),
            ["WONGOS_MYSTERY_TICKET"] = new(
                HeldNormalCombatRewardImpactClass.KnownRuntimeConditionalImpact,
                HeldNormalCombatRewardEffects.AddsRewardEntries |
                HeldNormalCombatRewardEffects.ConsumesAdditionalRewardsRng |
                HeldNormalCombatRewardEffects.RequiresSavedCounter),
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RuntimeDependencyOverrides =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["AMETHYST_AUBERGINE"] = new[] { "RoomType" },
            ["BLACK_STAR"] = new[] { "RoomType" },
            ["DINGY_RUG"] = new[] { "CharacterCardPool" },
            ["DRIFTWOOD"] = new[] { "PlayerChoice", "CurrentRewardEntries" },
            ["FRESNEL_LENS"] = new[] { "CardRewardOptions" },
            ["FROZEN_EGG"] = new[] { "CardRewardOptions" },
            ["GLITTER"] = new[] { "CardRewardOptions" },
            ["LASTING_CANDY"] = new[] { "RelicSavedCounter", "CombatOutcome" },
            ["LAVA_LAMP"] = new[] { "CombatOutcome", "TookDamageThisCombat" },
            ["LAVA_ROCK"] = new[] { "CurrentAct", "RoomType" },
            ["MOLTEN_EGG"] = new[] { "CardRewardOptions" },
            ["NEOWS_BONES"] = new[] { "RoomType" },
            ["PAELS_WING"] = new[] { "RelicSavedCounter", "PlayerChoice" },
            ["PRAYER_WHEEL"] = new[] { "RoomType" },
            ["PRISMATIC_GEM"] = new[] { "UnlockState", "CharacterCardPool" },
            ["SILKEN_TRESS"] = new[] { "CardRewardOptions" },
            ["SILVER_CRUCIBLE"] = new[] { "RelicSavedCounter", "CardRewardOptions" },
            ["TOXIC_EGG"] = new[] { "CardRewardOptions" },
            ["WHITE_BEAST_STATUE"] = new[] { "PotionRewardOddsState" },
            ["WHITE_STAR"] = new[] { "RoomType" },
            ["WING_CHARM"] = new[] { "CardRewardOptions" },
            ["WONGOS_MYSTERY_TICKET"] = new[] { "RelicSavedCounter", "RoomType" },
        };

    public static bool TryGet(
        RuntimeProfileId profileId,
        ModelKey relicKey,
        out VanillaRelicRewardEffect capability)
    {
        capability = default!;
        if (!relicKey.IsValid || relicKey.Category != BaseGameModelKeys.Categories.Relic)
        {
            return false;
        }

        HashSet<string>? entries = profileId switch
        {
            RuntimeProfileId.Stable107 => Stable107Entries,
            RuntimeProfileId.Beta109 or RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 => Beta109Entries,
            _ => null
        };
        if (entries is null || !entries.Contains(relicKey.Entry))
        {
            return false;
        }

        RelicOnObtainRewardEffects onObtain = OnObtainOverrides.TryGetValue(relicKey.Entry, out RelicOnObtainRewardEffects obtainOverride)
            ? obtainOverride
            : RelicOnObtainRewardEffects.None;
        string drawShape = OnObtainDrawShapes.TryGetValue(relicKey.Entry, out string? shape)
            ? shape
            : "None";
        HeldImpactOverride held = HeldOverrides.TryGetValue(relicKey.Entry, out HeldImpactOverride heldOverride)
            ? heldOverride
            : new HeldImpactOverride(
                HeldNormalCombatRewardImpactClass.Neutral,
                HeldNormalCombatRewardEffects.None);
        IReadOnlyList<string> dependencies = RuntimeDependencyOverrides.TryGetValue(relicKey.Entry, out IReadOnlyList<string>? dependencyOverride)
            ? dependencyOverride
            : Array.Empty<string>();
        RelicRewardPredictionSupport support =
            OpeningCombatRewardImpactAdapterRegistry.TryResolve(profileId, relicKey, out _)
                ? RelicRewardPredictionSupport.SpecialImpactSupported
                : ResolveProductionSupport(onObtain, held.ImpactClass);

        capability = new VanillaRelicRewardEffect(
            profileId,
            relicKey,
            onObtain,
            drawShape,
            held.ImpactClass,
            held.Capabilities,
            dependencies,
            support,
            $"vanilla-relic-reward-capability-audit:{profileId}:{relicKey.Entry}");
        return true;
    }

    public static bool IsKnownVanilla(RuntimeProfileId profileId, ModelKey relicKey) =>
        TryGet(profileId, relicKey, out _);

    private static RelicRewardPredictionSupport ResolveProductionSupport(
        RelicOnObtainRewardEffects onObtain,
        HeldNormalCombatRewardImpactClass heldImpactClass)
    {
        if (heldImpactClass is not HeldNormalCombatRewardImpactClass.Neutral and
            not HeldNormalCombatRewardImpactClass.OtherRewardDomainOnly)
        {
            return heldImpactClass == HeldNormalCombatRewardImpactClass.KnownRuntimeConditionalImpact
                ? RelicRewardPredictionSupport.ConditionalOutOfScope
                : RelicRewardPredictionSupport.KnownImpactNotImplemented;
        }

        return onObtain == RelicOnObtainRewardEffects.None
            ? RelicRewardPredictionSupport.NeutralSupported
            : RelicRewardPredictionSupport.ContinuationSupported;
    }

    private readonly record struct HeldImpactOverride(
        HeldNormalCombatRewardImpactClass ImpactClass,
        HeldNormalCombatRewardEffects Capabilities);
}
