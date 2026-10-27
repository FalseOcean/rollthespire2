namespace RolltheSpire2.Presentation;

// Display admission only. These options already expose their complete output
// in the native page; this policy neither settles nor rejects a game action.
// Both live admission and the detached worker use the same omissions.
internal static class OperationPreviewEligibility
{
    internal static bool EventMayDisplay(string eventEntry, string optionKey,
        bool tinkerRiderStage = false, int tabletCount = 0, bool offeredRelic = false) =>
        !PublicOnly(eventEntry, optionKey) &&
        !(eventEntry == "TRIAL" && optionKey is not
            ("MERCHANT_GUILTY" or "NONDESCRIPT_GUILTY" or "NONDESCRIPT_INNOCENT")) &&
        !(eventEntry == "TINKER_TIME" && tinkerRiderStage) &&
        !(eventEntry == "TABLET_OF_TRUTH" && optionKey == "DECIPHER" && tabletCount == 4) &&
        !(eventEntry == "DOLL_ROOM" && offeredRelic);

    internal static bool RoyaltiesBattleOption(string eventEntry, string optionKey) =>
        (eventEntry, optionKey) is
            ("DENSE_VEGETATION", "REST" or "FIGHT") or
            ("PUNCH_OFF", "I_CAN_TAKE_THEM" or "FIGHT") or
            ("BATTLEWORN_DUMMY", "SETTING_1" or "SETTING_2" or "SETTING_3") or
            ("THE_LANTERN_KEY", "KEEP_THE_KEY" or "FIGHT") or
            ("FAKE_MERCHANT", "THROW_FOUL_POTION");

    private static bool PublicOnly(string ev, string option) => (ev, option) is
        ("THIS_OR_THAT", "PLAIN") or ("UNREST_SITE", "REST") or
        ("SLIPPERY_BRIDGE", "OVERCOME") or
        ("ROOM_FULL_OF_CHEESE", "SEARCH") or ("WAR_HISTORIAN_REPY", "INITIAL_UNLOCK_CAGE" or "SECOND_UNLOCK_CAGE") or
        ("HUNGRY_FOR_MUSHROOMS", "BIG_MUSHROOM") or ("THE_LANTERN_KEY", "RETURN_THE_KEY") or
        ("THE_LEGENDS_WERE_TRUE", "NAB_THE_MAP") or ("POTION_COURIER", "GRAB_POTIONS") or
        ("ROUND_TEA_PARTY", "ENJOY_TEA") or ("TABLET_OF_TRUTH", "SMASH" or "GIVE_UP") or
        ("WELCOME_TO_WONGOS", "FEATURED_ITEM" or "MYSTERY_BOX") or ("DENSE_VEGETATION", "TRUDGE_ON") or
        ("MORPHIC_GROVE", "LONER") or ("AROMA_OF_CHAOS", "MAINTAIN_CONTROL") or
        ("SYMBIOTE", "APPROACH") or ("DOORS_OF_LIGHT_AND_DARK", "DARK") or
        ("WELLSPRING", "BATHE") or ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH");
}
