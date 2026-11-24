using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorEncounterEntry
{
    // Concrete GenerateMonsters counts from sts2_01110. Random variants have
    // the same count except Slithering Strangler.
    internal static int InitialEnemyCount(ModelKey encounter, ulong rootSeed, int totalFloor)
    {
        if (encounter.Category != "ENCOUNTER") throw new ArgumentException("PredictorEncounterKeyInvalid");
        return encounter.Entry switch
        {
            "AEONGLASS_BOSS" => 1,
            "AXEBOTS_NORMAL" => 1,
            "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER" => 1,
            "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER" => 1,
            "BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER" => 1,
            "BOWLBUGS_NORMAL" => 3,
            "BOWLBUGS_WEAK" => 2,
            "BYGONE_EFFIGY_ELITE" => 1,
            "BYRDONIS_ELITE" => 1,
            "CEREMONIAL_BEAST_BOSS" => 1,
            "CHOMPERS_NORMAL" => 2,
            "CONSTRUCT_MENAGERIE_NORMAL" => 3,
            "CORPSE_SLUGS_NORMAL" => 3,
            "CORPSE_SLUGS_WEAK" => 2,
            "CUBEX_CONSTRUCT_NORMAL" => 1,
            "CULTISTS_NORMAL" => 2,
            "DECIMILLIPEDE_ELITE" => 3,
            "DENSE_VEGETATION_EVENT_ENCOUNTER" => 4,
            "DEPRECATED_ENCOUNTER" => 0,
            "DEVOTED_SCULPTOR_WEAK" => 1,
            "ENTOMANCER_ELITE" => 1,
            "EXOSKELETONS_NORMAL" => 4,
            "EXOSKELETONS_WEAK" => 3,
            "FABRICATOR_NORMAL" => 1,
            "FAKE_MERCHANT_EVENT_ENCOUNTER" => 1,
            "FLYCONID_NORMAL" => 2,
            "FOGMOG_NORMAL" => 1,
            "FOSSIL_STALKER_NORMAL" => 1,
            "FROG_KNIGHT_NORMAL" => 1,
            "FUZZY_WURM_CRAWLER_WEAK" => 1,
            "GLOBE_HEAD_NORMAL" => 1,
            "GREMLIN_MERC_NORMAL" => 1,
            "HAUNTED_SHIP_NORMAL" => 1,
            "HUNTER_KILLER_NORMAL" => 1,
            "INFESTED_PRISMS_ELITE" => 1,
            "INKLETS_NORMAL" => 3,
            "KAISER_CRAB_BOSS" => 2,
            "KNIGHTS_ELITE" => 3,
            "KNOWLEDGE_DEMON_BOSS" => 1,
            "LAGAVULIN_MATRIARCH_BOSS" => 1,
            "LIVING_FOG_NORMAL" => 1,
            "LOUSE_PROGENITOR_NORMAL" => 1,
            "MAWLER_NORMAL" => 1,
            "MECHA_KNIGHT_ELITE" => 1,
            "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER" => 1,
            "MYTES_NORMAL" => 2,
            "NIBBITS_NORMAL" => 2,
            "NIBBITS_WEAK" => 1,
            "OVERGROWTH_CRAWLERS" => 2,
            "OVICOPTER_NORMAL" => 1,
            "OWL_MAGISTRATE_NORMAL" => 1,
            "PHANTASMAL_GARDENERS_ELITE" => 4,
            "PHROG_PARASITE_ELITE" => 1,
            "PUNCH_CONSTRUCT_NORMAL" => 1,
            "PUNCH_OFF_EVENT_ENCOUNTER" => 2,
            "QUEEN_BOSS" => 2,
            "RUBY_RAIDERS_NORMAL" => 3,
            "SCROLLS_OF_BITING_NORMAL" => 4,
            "SCROLLS_OF_BITING_WEAK" => 3,
            "SEAPUNK_NORMAL" => 2,
            "SEAPUNK_WEAK" => 1,
            "SEWER_CLAM_NORMAL" => 1,
            "SHRINKER_BEETLE_WEAK" => 1,
            "SKULKING_COLONY_ELITE" => 1,
            "SLIMED_BERSERKER_NORMAL" => 1,
            "SLIMES_NORMAL" => 4,
            "SLIMES_WEAK" => 3,
            "SLUDGE_SPINNER_WEAK" => 1,
            "SLUMBERING_BEETLE_NORMAL" => 3,
            "SNAPPING_JAXFRUIT_NORMAL" => 2,
            "SOUL_FYSH_BOSS" => 1,
            "SOUL_NEXUS_ELITE" => 1,
            "SPINY_TOAD_NORMAL" => 1,
            "TERROR_EEL_ELITE" => 1,
            "TEST_SUBJECT_BOSS" => 1,
            "THE_ARCHITECT_EVENT_ENCOUNTER" => 1,
            "THE_INSATIABLE_BOSS" => 1,
            "THE_KIN_BOSS" => 3,
            "THE_LOST_AND_FORGOTTEN_NORMAL" => 2,
            "THE_OBSCURA_NORMAL" => 1,
            "THIEVING_HOPPER_WEAK" => 1,
            "TOADPOLES_WEAK" => 2,
            "TUNNELER_NORMAL" => 2,
            "TUNNELER_WEAK" => 1,
            "TURRET_OPERATOR_WEAK" => 2,
            "TWO_TAILED_RATS_NORMAL" => 3,
            "VANTOM_BOSS" => 1,
            "VINE_SHAMBLER_NORMAL" => 1,
            "WATERFALL_GIANT_BOSS" => 1,
            "SLITHERING_STRANGLER_NORMAL" => StranglerCount(rootSeed, totalFloor, encounter.Entry),
            _ => throw new NotImplementedException("PredictorEncounterCountPending:" + encounter)
        };
    }

    private static int StranglerCount(ulong rootSeed, int totalFloor, string entry)
    {
        ulong hash = XxHash64.HashUtf8(entry, 0UL);
        var local = new Xoshiro256StarStar(unchecked(rootSeed + (ulong)totalFloor + hash));
        return local.NextInt(3) == 2 ? 3 : 2;
    }

    // CombatState.CreateCreature calls Creature.SetUniqueMonsterHpValue once for
    // each initial enemy. Both candidate selection and the exhausted-range
    // fallback consume exactly one Niche draw. HP itself has no later
    // out-of-combat reader in this product boundary.
    internal static PredictorState ConsumeInitialEnemyHp(PredictorState entry, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var state = entry;
        for (int index = 0; index < count; index++)
            state = state.Draw(PredictorStream.Niche, 0, 1).State;
        return state;
    }
}
