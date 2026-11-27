using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using HarmonyLib;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.InRunOperations;

internal readonly partial record struct RfFeatures
{
    internal bool Transform(NDeckTransformSelectScreen? selector) => Active && On(RfSwitch.DeckTransformPredictionEnabled) &&
        selector != null && AccessTools.Field(selector.GetType(), "_cardToTransformation")?.GetValue(selector) is Delegate callback &&
        callback.Target?.GetType().Assembly.GetName().Name == "RandomForeseer";

    // Use the native option key: different pages of one event can expose
    // different predictions, and RT2's detached action names sometimes differ.
    internal bool Event(NEventOptionButton? button)
    {
        if (!Active || button?.Event == null || button.Option == null) return false;
        string text = button.Option.TextKey;
        string option = text[(text.LastIndexOf('.') + 1)..];
        // These options reveal an existing relic. Its pickup settings govern
        // the hidden result, independently of the ordinary event switch.
        if (button.Event is RelicTrader trader)
        {
            int index = option switch { "TOP" => 0, "MIDDLE" => 1, "BOTTOM" => 2, _ => -1 };
            return index >= 0 && AccessTools.Field(trader.GetType(), "_newRelics")?.GetValue(trader) is IReadOnlyList<RelicModel> offers &&
                index < offers.Count && Pickup(offers[index]);
        }
        if (button.Event is ThermaeNonupeipe bath && option == "DRINK_WATERS")
            return Pickup(AccessTools.Field(bath.GetType(), "_drinkRelic")?.GetValue(bath) as RelicModel);
        if (!On(RfSwitch.EventOptionPredictionEnabled)) return false;
        if (button.Option.Relic is { } offered && OperationSurfaceHoverCapture.MayDisplayRelicObtain(offered) && !Pickup(offered))
            return false;
        return (button.Event.Id.Entry, option) switch
        {
            ("BATTLEWORN_DUMMY", "SETTING_1" or "SETTING_2" or "SETTING_3") or
            ("PUNCH_OFF", "I_CAN_TAKE_THEM" or "FIGHT") => Unrestricted,
            ("AROMA_OF_CHAOS", "LET_GO") or ("BRAIN_LEECH", "SHARE_KNOWLEDGE" or "RIP") or
            ("DENSE_VEGETATION", "REST") or ("DOLL_ROOM", "RANDOM" or "TAKE_SOME_TIME" or "EXAMINE") or
            ("ENDLESS_CONVEYOR", "OBSERVE_CHEF" or "FRIED_EEL" or "JELLY_LIVER" or "SUSPICIOUS_CONDIMENT" or "SPICY_SNAPPY") or
            ("INFESTED_AUTOMATON", "STUDY" or "TOUCH_CORE") or ("LUMINOUS_CHOIR", "OFFER_TRIBUTE") or
            ("MORPHIC_GROVE", "GROUP") or ("POTION_COURIER", "RANSACK") or ("PUNCH_OFF", "NAB") or
            ("RANWID_THE_ELDER", "POTION" or "GOLD" or "RELIC") or ("REFLECTIONS", "TOUCH_A_MIRROR") or
            ("ROOM_FULL_OF_CHEESE", "GORGE") or ("ROUND_TEA_PARTY", "PICK_FIGHT" or "CONTINUE_FIGHT") or
            ("SYMBIOTE", "KILL_WITH_FIRE") or ("TABLET_OF_TRUTH", "DECIPHER_1" or "DECIPHER") or
            ("THE_FUTURE_OF_POTIONS", "POTION") or ("THE_LEGENDS_WERE_TRUE", "SLOWLY_FIND_AN_EXIT") or
            ("THIS_OR_THAT", "ORNATE") or ("TINKER_TIME", "ATTACK" or "SKILL" or "POWER") or
            ("TRASH_HEAP", "DIVE_IN" or "GRAB") or ("UNREST_SITE", "KILL") or
            ("WAR_HISTORIAN_REPY", "UNLOCK_CHEST") or ("WATERFALL_OF_OBVIOUS_SECRETS", "BEAT_UP" or "NEGOTIATE") or
            ("WELCOME_TO_WONGOS", "BARGAIN_BIN" or "LEAVE") or ("WELLSPRING", "BOTTLE") or
            ("WHISPERING_HOLLOW", "GOLD" or "HUG") or ("WUPPY_IN_TROUBLE", "IGNORE") => true,
            ("COLORFUL_PHILOSOPHERS", _) => text.Contains(".pages.INITIAL.options.", StringComparison.Ordinal),
            ("SLIPPERY_BRIDGE", _) => option.Contains("HOLD_ON", StringComparison.Ordinal),
            ("TRIAL", _) => text is "TRIAL.pages.MERCHANT.options.GUILTY" or "TRIAL.pages.NONDESCRIPT.options.GUILTY"
                or "TRIAL.pages.NONDESCRIPT.options.INNOCENT",
            // The museum also includes the offered relic's pickup effect.
            ("MUSEUM_OF_TORTURE", "TAKE") => button.Event is MuseumOfTorture museum &&
                AccessTools.Field(museum.GetType(), "_offeredRelic")?.GetValue(museum) is RelicModel relic &&
                (!OperationSurfaceHoverCapture.MayDisplayRelicObtain(relic) || Pickup(relic)),
            _ => false
        };
    }
}
