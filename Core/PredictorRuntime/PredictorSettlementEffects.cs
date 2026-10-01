using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorSettlementEffects
{
    internal static bool Has(PredictorState state, string entry) => state.Relics.Any(r => !r.Melted && r.Key.Entry == entry);

    internal static void RequireImplementedRelic(ModelKey key)
    {
        if (key.Category != "RELIC" || key.Entry is not ("WHETSTONE" or "WAR_PAINT" or "POTION_BELT" or "EMPTY_CAGE" or "BURNING_BLOOD" or "BOWLER_HAT" or "ECTOPLASM" or "SOZU" or
            "LASTING_CANDY" or "PRAYER_WHEEL" or "WHITE_STAR" or "BLACK_STAR" or "WHITE_BEAST_STATUE" or "MOLTEN_EGG" or "TOXIC_EGG" or "FROZEN_EGG" or
            "DINGY_RUG" or "AMETHYST_AUBERGINE" or "WONGOS_MYSTERY_TICKET" or "CIRCLET" or "GOLDEN_PEARL" or "PRISMATIC_GEM" or "SILVER_CRUCIBLE" or "SILKEN_TRESS" or
            "NEW_LEAF" or "NEOWS_TALISMAN" or "DELICATE_FROND" or "PETRIFIED_TOAD" or "ALCHEMICAL_COFFER" or "MINIATURE_TENT" or "GIRYA" or
            "NEOWS_BONES" or "LARGE_CAPSULE" or "CALLING_BELL" or "LEAFY_POULTICE" or "ARCANE_SCROLL" or "PHIAL_HOLSTER" or "LOST_COFFER" or "SMALL_CAPSULE" or "SCROLL_BOXES" or "KALEIDOSCOPE" or
            "CURSED_PEARL" or "OLD_COIN" or "NUTRITIOUS_OYSTER" or "NEOWS_TORMENT" or "NEOWS_SACRIFICE" or "LEAD_PAPERWEIGHT" or "HEFTY_TABLET" or "PRECISE_SCISSORS" or "POMANDER" or
            "DUSTY_TOME" or "ARCHAIC_TOOTH" or "SEA_GLASS" or "SEAL_OF_GOLD" or "BONE_TEA" or "EMBER_TEA" or "TEA_OF_DISCOURTESY" or
            "SHOVEL" or "MEAT_CLEAVER" or "PAELS_GROWTH" or "PUMPKIN_CANDLE" or "VENERABLE_TEA_SET" or "FAKE_VENERABLE_TEA_SET" or
            "REGAL_PILLOW" or "DREAM_CATCHER" or "TINY_MAILBOX" or "STONE_HUMIDIFIER" or "DOWSING_ROD" or "MAW_BANK" or
            "TRI_BOOMERANG" or "PAELS_TOOTH" or "PAELS_CLAW" or "BEAUTIFUL_BRACELET" or "ASTROLABE" or "PANDORAS_BOX" or
            "YUMMY_COOKIE" or "PRESERVED_FOG" or "GLASS_EYE" or "ORRERY" or "SIGNET_RING" or "JEWELRY_BOX" or "BYRDPIP" or
            "WING_CHARM" or "FRESNEL_LENS" or "GLITTER" or "LAVA_LAMP" or "LAVA_ROCK" or "GOLDEN_COMPASS" or "WINGED_BOOTS" or "FORGOTTEN_SOUL" or
            "RING_OF_THE_SNAKE" or "DIVINE_RIGHT" or "BOUND_PHYLACTERY" or "CRACKED_CORE" or
            "SAND_CASTLE" or "LOOMING_FRUIT" or "FRAGRANT_MUSHROOM" or "PRECARIOUS_SHEARS" or
            "BIG_MUSHROOM" or "BIIIG_HUG" or "DISTINGUISHED_CAPE" or "SERE_TALON" or "ELECTRIC_SHRYMP" or "PAELS_WING" or "DRIFTWOOD" or "TOY_BOX" or "FUR_COAT" or
            "CLAWS" or "PAELS_LEGION" or "PAELS_EYE" or "SNECKO_EYE" or "BLOOD_SOAKED_ROSE" or "PAELS_HORN" or "TANXS_WHISTLE" or "STORYBOOK" or "FISHING_ROD" or "WAR_HAMMER" or
            "BLESSED_ANTLER" or "BOOMING_CONCH" or "BRILLIANT_SCARF" or "CHOICES_PARADOX" or "CROSSBOW" or "DIAMOND_DIADEM" or "FIDDLE" or "JEWELED_MASK" or "MUSIC_BOX" or
            "PAELS_BLOOD" or "PAELS_FLESH" or "PAELS_TEARS" or "PHILOSOPHERS_STONE" or "RADIANT_PEARL" or "RUNIC_PYRAMID" or "SAI" or "SPIKED_GAUNTLETS" or "THROWING_AXE" or
            "TOASTY_MITTENS" or "VELVET_CHOKER" or "VERY_HOT_COCOA" or "WHISPERING_EARRING" or "WONGO_CUSTOMER_APPRECIATION_BADGE" or "SWORD_OF_STONE" or "SWORD_OF_JADE" or
            "MANGO" or "PEAR" or "STRAWBERRY" or "CAULDRON" or "DOLLYS_MIRROR" or "GNARLED_HAMMER" or "KIFUDA" or "PUNCH_DAGGER" or "LEES_WAFFLE" or "ROYAL_STAMP" or "LUCKY_FYSH" or "DRAGON_FRUIT" or
            "ETERNAL_FEATHER" or "MEAL_TICKET" or "PLANISPHERE" or "PANTOGRAPH" or "CHOSEN_CHEESE" or "TUNGSTEN_ROD" or "BING_BONG" or "BOOK_OF_FIVE_RINGS" or "JUZU_BRACELET" or "LIZARD_TAIL" or
            "BLACK_BLOOD" or "DARKSTONE_PERIAPT" or "DAUGHTER_OF_THE_WIND" or "DIVINE_DESTINY" or "FAKE_ANCHOR" or "FAKE_BLOOD_VIAL" or "FAKE_HAPPY_FLOWER" or "FAKE_LEES_WAFFLE" or
            "FAKE_MANGO" or "FAKE_MERCHANTS_RUG" or "FAKE_ORICHALCUM" or "FAKE_SNECKO_EYE" or "FAKE_STRIKE_DUMMY" or "HAND_DRILL" or "HISTORY_COURSE" or "INFUSED_CORE" or "IRON_CLUB" or
            "LORDS_PARASOL" or "LOST_WISP" or "MEAT_ON_THE_BONE" or "MEMBERSHIP_CARD" or "MR_STRUGGLES" or "NUTRITIOUS_SOUP" or "PHYLACTERY_UNBOUND" or "POLLINOUS_CORE" or
            "RING_OF_THE_DRAKE" or "THE_BOOT" or "THE_COURIER" or "TOUCH_OF_OROBAS" or "ROYAL_POISON" or
            "AKABEKO" or "ANCHOR" or "ART_OF_WAR" or "BAG_OF_MARBLES" or "BAG_OF_PREPARATION" or "BEATING_REMNANT" or "BELLOWS" or "BELT_BUCKLE" or "BIG_HAT" or "BLOOD_VIAL" or
            "BONE_FLUTE" or "BOOKMARK" or "BOOK_REPAIR_KNIFE" or "BREAD" or "BRIMSTONE" or "BRONZE_SCALES" or "BURNING_STICKS" or "CANDELABRA" or "CAPTAINS_WHEEL" or
            "CENTENNIAL_PUZZLE" or "CHANDELIER" or "CHARONS_ASHES" or "CHEMICAL_X" or "CLOAK_CLASP" or "DATA_DISK" or "DEMON_TONGUE" or "EMOTION_CHIP" or "FENCING_MANUAL" or
            "FESTIVE_POPPER" or "FUNERARY_MASK" or "GALACTIC_DUST" or "GAMBLING_CHIP" or "GAME_PIECE" or "GHOST_SEED" or "GOLD_PLATED_CABLES" or "GORGET" or "GREMLIN_HORN" or
            "HAPPY_FLOWER" or "HELICAL_DART" or "HORN_CLEAT" or "ICE_CREAM" or "INTIMIDATING_HELMET" or "IVORY_TILE" or "JOSS_PAPER" or "KUNAI" or "KUSARIGAMA" or "LANTERN" or
            "LETTER_OPENER" or "LUNAR_PASTRY" or "MERCURY_HOURGLASS" or "METRONOME" or "MINIATURE_CANNON" or "MINI_REGENT" or "MUMMIFIED_HAND" or "MYSTIC_LIGHTER" or "NINJA_SCROLL" or
            "NUNCHAKU" or "ODDLY_SMOOTH_STONE" or "ORANGE_DOUGH" or "ORICHALCUM" or "ORNAMENTAL_FAN" or "PAPER_KRANE" or "PAPER_PHROG" or "PARRYING_SHIELD" or "PENDULUM" or "PEN_NIB" or
            "PERMAFROST" or "POCKETWATCH" or "POWER_CELL" or "RAINBOW_RING" or "RAZOR_TOOTH" or "RED_MASK" or "RED_SKULL" or "REGALITE" or "REPTILE_TRINKET" or "RINGING_TRIANGLE" or
            "RIPPLE_BASIN" or "RUINED_HELMET" or "RUNIC_CAPACITOR" or "SCREAMING_FLAGON" or "SELF_FORMING_CLAY" or "SHURIKEN" or "SLING_OF_COURAGE" or "SNECKO_SKULL" or
            "SPARKLING_ROUGE" or "STONE_CALENDAR" or "STONE_CRACKER" or "STRIKE_DUMMY" or "STURDY_CLAMP" or "SYMBIOTIC_VIRUS" or "THE_ABACUS" or "TINGSHA" or "TOOLBOX" or
            "TOUGH_BANDAGES" or "TUNING_FORK" or "TWISTED_FUNNEL" or "UNCEASING_TOP" or "UNDYING_SIGIL" or "UNSETTLING_LAMP" or "VAJRA" or "VAMBRACE" or "VEXING_PUZZLEBOX" or "VITRUVIAN_MINION"))
            throw new NotImplementedException($"Predictor relic hooks pending: {key}");
    }

    internal static void RequireImplementedCard(PredictorCard card)
    {
        // Source-specific lifetime and persistent fields are admitted only with
        // their handlers; ordinary identities do not need a basic-card whitelist.
        // Sown/Nimble/Corrupted affect energy, block and combat HP. They do not
        // mutate a master instance or an out-of-combat stream when played; combat
        // inventory/life consequences remain part of the battle fact contract.
        if (card.Key.Category != "CARD" || (card.Enchantment != null && card.Enchantment.Value.Entry is not ("INKY" or "SLUMBERING_ESSENCE" or "GLAM" or "SOWN" or "NIMBLE" or "CORRUPTED" or "VIGOROUS" or "CLONE" or "SWIFT" or "INSTINCT" or "GOOPY" or "PERFECT_FIT" or "SLITHER" or "SHARP" or "SPIRAL" or "TEZCATARAS_EMBER" or "SOULS_POWER" or "STEADY" or "IMBUED" or "ADROIT" or "MOMENTUM" or "ROYALLY_APPROVED")) ||
            (card.IncreasedBlock != 0 && card.Key.Entry != "GENETIC_ALGORITHM") || (card.IncreasedDamage != 0 && card.Key.Entry != "THE_SCYTHE"))
            throw new NotImplementedException($"Predictor card hooks pending: {card.Key}");
    }

    internal static void RequireImplementedInventory(PredictorState state)
    {
        foreach (var relic in state.Relics.Where(r => !r.Melted))
        {
            // Opening handlers completed these obtains before publishing state.
            if (relic.Key.Entry is "GOLDEN_PEARL" or "ARCANE_SCROLL" or "LOST_COFFER" or "PHIAL_HOLSTER" or "SMALL_CAPSULE") continue;
            RequireImplementedRelic(relic.Key);
        }
        foreach (long id in state.Deck) RequireImplementedCard(state.Card(id));
    }

    internal static PredictorState TakeGold(PredictorState state, int gold)
    {
        decimal amount = gold;
        foreach (var relic in state.Relics.Where(r => !r.Melted))
            amount = relic.Key.Entry switch { "BOWLER_HAT" => amount * 1.25m, "ECTOPLASM" => 0, _ => amount };
        if (amount <= 0) return state;
        state = state with { GoldBudget = checked(state.GoldBudget + (int)amount) };
        foreach (var relic in state.Relics.Where(r => !r.Melted && r.Key.Entry == "DRAGON_FRUIT"))
            state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(new(relic.Key, relic.Id, 1, 1)) };
        return state;
    }
}
