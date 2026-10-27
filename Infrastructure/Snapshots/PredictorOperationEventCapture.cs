using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rewards;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Bootstrap;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorOperationCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class PredictorOperationEventCapture
{
    private sealed class EventTransaction(EventModel ev, EventOption option)
    {
        internal readonly EventModel Event = ev;
        internal readonly EventOption Option = option;
        internal int RootObtains;
    }
    private sealed record EventObtainMarker(EventModel Event, EventOption Option, int Ordinal);
    private static readonly AsyncLocal<EventTransaction?> CurrentTransaction = new();
    private static readonly AsyncLocal<int> ObtainDepth = new();
    private static readonly ConditionalWeakTable<RelicModel, EventObtainMarker> ObtainMarkers = new();

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(EventOption), nameof(EventOption.Chosen)),
            prefix: new(typeof(PredictorOperationEventCapture), nameof(BeforeEventOption)),
            postfix: new(typeof(PredictorOperationEventCapture), nameof(AfterEventOption)),
            finalizer: new(typeof(PredictorOperationEventCapture), nameof(FinalizeEventOption)));
        harmony.Patch(AccessTools.Method(typeof(RelicCmd), nameof(RelicCmd.Obtain), [typeof(RelicModel), typeof(Player), typeof(int)]),
            prefix: new(typeof(PredictorOperationEventCapture), nameof(BeforeEventObtain)),
            postfix: new(typeof(PredictorOperationEventCapture), nameof(AfterEventObtain)),
            finalizer: new(typeof(PredictorOperationEventCapture), nameof(FinalizeEventObtain)));
    }

    private static void BeforeEventOption(EventOption __instance, out EventTransaction? __state)
    {
        __state = CurrentTransaction.Value;
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            var ev = Captured<EventModel>(Callback(__instance)?.Target);
            if (ev == null || !Supported.Contains(ev.Id.Entry) ||
                ReadNullable(ev, "_currentOptions") is not IReadOnlyList<EventOption> options || !options.Contains(__instance)) return;
            CurrentTransaction.Value = new(ev, __instance);
        }
        catch (Exception ex) { RuntimeLog.WarnException("operationEventChoiceObservationFailed=true", ex); }
    }
    private static void AfterEventOption(EventTransaction? __state) => CurrentTransaction.Value = __state;
    private static Exception? FinalizeEventOption(Exception? __exception, EventTransaction? __state)
    {
        AfterEventOption(__state);
        return __exception;
    }
    private static void BeforeEventObtain(RelicModel relic, Player player, out int __state)
    {
        __state = ObtainDepth.Value;
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            if (CurrentTransaction.Value is not { } transaction || transaction.Event.Owner != player) return;
            if (__state == 0)
            {
                ObtainMarkers.Remove(relic);
                ObtainMarkers.Add(relic, new(transaction.Event, transaction.Option, ++transaction.RootObtains));
            }
            ObtainDepth.Value = __state + 1;
        }
        catch (Exception ex) { RuntimeLog.WarnException("operationEventObtainObservationFailed=true", ex); }
    }
    private static void AfterEventObtain(int __state) => ObtainDepth.Value = __state;
    private static Exception? FinalizeEventObtain(Exception? __exception, int __state)
    {
        AfterEventObtain(__state);
        return __exception;
    }

    internal static bool Supports(EventModel ev) => Supported.Contains(ev.Id.Entry);

    private static readonly ImmutableHashSet<string> Supported = new[]
    {
        "BRAIN_LEECH", "COLORFUL_PHILOSOPHERS", "INFESTED_AUTOMATON", "ROOM_FULL_OF_CHEESE",
        "THE_FUTURE_OF_POTIONS", "TINKER_TIME", "DOLL_ROOM", "LUMINOUS_CHOIR", "POTION_COURIER",
        "RANWID_THE_ELDER", "ROUND_TEA_PARTY", "THE_LEGENDS_WERE_TRUE", "THIS_OR_THAT", "TRASH_HEAP",
        "UNREST_SITE", "WAR_HISTORIAN_REPY", "WELLSPRING", "DOORS_OF_LIGHT_AND_DARK", "REFLECTIONS",
        "TABLET_OF_TRUTH", "SLIPPERY_BRIDGE", "AROMA_OF_CHAOS", "MORPHIC_GROVE", "SYMBIOTE",
        "WHISPERING_HOLLOW", "ENDLESS_CONVEYOR", "TRIAL", "WELCOME_TO_WONGOS", "DENSE_VEGETATION",
        "BATTLEWORN_DUMMY", "PUNCH_OFF", "RELIC_TRADER", "HUNGRY_FOR_MUSHROOMS", "THE_LANTERN_KEY"
    }.ToImmutableHashSet(StringComparer.Ordinal);

    internal static PredictorOperationScene Capture(RunState run, EventModel ev, PredictorCatalog catalog, Node? root = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var owner = ev.Owner ?? throw new InvalidOperationException("OperationEventOwnerMissing");
        if (!Supported.Contains(ev.Id.Entry)) throw new NotSupportedException("OperationEventSourcePending:" + ev.Id);
        // Read the backing list: CurrentOptions would allocate on an uninitialized model.
        var nativeOptions = (ReadNullable(ev, "_currentOptions") as IReadOnlyList<EventOption>)
            ?? throw new InvalidOperationException("OperationEventOptionsMissing:" + ev.Id);
        var rewards = CaptureRewards(run, owner, catalog, root);
        var obtainOrigin = ObservedObtainOrigin(owner, root);
        var currentSet = CurrentNativeRewards(run, owner, root);
        var rewardOrigin = currentSet == null ? null : ObservedRewardSetObtainSource(currentSet);
        bool childRewardScreen = rewardOrigin != null;
        if (obtainOrigin != null && rewardOrigin != null && obtainOrigin.Relic != rewardOrigin.Relic)
        {
            static bool Contains(NativeObtainOrigin node, RelicModel relic)
            {
                for (NativeObtainOrigin? current = node; current != null; current = current.Parent)
                    if (ReferenceEquals(current.Relic, relic)) return true;
                return false;
            }
            if (Contains(obtainOrigin, rewardOrigin.Relic)) childRewardScreen = false;
            else if (Contains(rewardOrigin, obtainOrigin.Relic)) obtainOrigin = rewardOrigin;
            else throw new InvalidOperationException("OperationEventObtainSourceAmbiguous");
        }
        obtainOrigin ??= rewardOrigin;
        var rootOrigin = obtainOrigin == null ? null : OutermostObtainOrigin(obtainOrigin);
        var childRewards = childRewardScreen ? rewards : null;
        if (rootOrigin?.ParentReward is { } sourceReward)
        {
            var parentSet = NativeRewardSets(run, owner, root).SingleOrDefault(s => NativeContainsReward(s, sourceReward));
            if (parentSet == null || ReferenceEquals(parentSet, currentSet) && childRewards != null)
                throw new InvalidOperationException("OperationEventObtainRewardParentMissing");
            rewards = CaptureRewardsSet(parentSet, owner, catalog);
        }
        else if (obtainOrigin != null) rewards = null;
        var grids = Nodes<NCardGridSelectionScreen>(root).Where(n => n.IsVisibleInTree()).ToArray();
        if (grids.Length > 1) throw new InvalidOperationException("OperationEventAmbiguousSelector");
        bool trialConfirmation = ev.Id.Entry == "TRIAL" && Nodes<NAbandonRunConfirmPopup>(root).Any(n => n.IsVisibleInTree());
        bool ActiveChoice(EventOption option) => option.WasChosen &&
            (Read<bool>(option, "<DisableOnChosen>k__BackingField") || trialConfirmation);
        var chosen = nativeOptions.Where(ActiveChoice).ToArray();
        if (chosen.Length > 1) throw new InvalidOperationException("OperationEventAmbiguousActiveChoice");
        if (chosen.Length == 1 && grids.Length == 0 && rewards == null && obtainOrigin == null && !trialConfirmation)
            throw new NotSupportedException("OperationEventInFlight:" + ev.Id);
        if (ev.IsFinished && rewards == null && grids.Length == 0 && obtainOrigin == null)
            throw new NotSupportedException("OperationEventAlreadyFinished:" + ev.Id);
        var options = nativeOptions.Where(o => (!o.IsProceed || !ev.IsFinished) && (!ActiveChoice(o) || trialConfirmation))
            .Select(o => Option(ev, o)).ToImmutableArray();
        string? branch = chosen.Length == 1 ? Option(ev, chosen[0]).Key : null;
        if (ev.Id.Entry == "WAR_HISTORIAN_REPY" && ev.Description?.LocEntryKey == "WAR_HISTORIAN_REPY.pages.EXTRA_UNLOCK_CHEST.description")
            branch = "SECOND_UNLOCK_CHEST";
        if (rewards != null && rootOrigin?.ParentReward == null && (ev.Id.Entry, branch) is not (
            ("THE_FUTURE_OF_POTIONS", _) or ("COLORFUL_PHILOSOPHERS", _) or
            ("BRAIN_LEECH", "RIP") or ("POTION_COURIER", "GRAB_POTIONS" or "RANSACK") or
            ("THE_LEGENDS_WERE_TRUE", "SLOWLY_FIND_AN_EXIT") or ("WELLSPRING", "BOTTLE") or
            ("WHISPERING_HOLLOW", "GOLD") or ("TRIAL", "NONDESCRIPT_GUILTY") or
            ("PUNCH_OFF", "NAB") or
            ("DENSE_VEGETATION", "REST") or
            ("WAR_HISTORIAN_REPY", "INITIAL_UNLOCK_CHEST" or "SECOND_UNLOCK_CHEST") or
            ("ENDLESS_CONVEYOR", "GRAB_SUSPICIOUS_CONDIMENT")))
            throw new NotSupportedException("OperationEventRewardObtainParentPending:" + ev.Id + ":" + branch);
        var scene = new PredictorOperationEventScene(Key(ev), CrystalSphereLiveCapture.RngState(PredictorStream.Niche, ev.Rng),
            options, new(owner.Creature.CurrentHp, owner.Creature.MaxHp))
        {
            Branch = branch,
            RewardScene = rewards,
            TrialAbandonPending = trialConfirmation,
            MimicRestRewards = ev.Id.Entry == "DENSE_VEGETATION" && branch == "REST" && rewards != null,
            GoldAmount = ev.Id.Entry is "THIS_OR_THAT" or "DENSE_VEGETATION" or "LUMINOUS_CHOIR" or "WHISPERING_HOLLOW" or "PUNCH_OFF"
                ? ev.DynamicVars.Gold.IntValue : 0,
            LayoutEncounter = ev.Id.Entry switch
            {
                "PUNCH_OFF" => new("ENCOUNTER", "PUNCH_OFF_EVENT_ENCOUNTER"),
                "THE_LANTERN_KEY" => new("ENCOUNTER", "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER"),
                _ => null
            }
        };
        switch (ev.Id.Entry)
        {
            case "SLIPPERY_BRIDGE":
                scene = scene with
                {
                    BridgeCard = ReadNullable(ev, "_randomCardToLose") is CardModel card ? Id(card) : null,
                    BridgeHoldOns = Read<int>(ev, "_numberOfHoldOns"),
                    BridgeSkipped = (ReadNullable(ev, "_skippedRemovals") as IEnumerable<CardModel> ?? []).Select(Id).ToImmutableHashSet()
                };
                scene = scene with { Options = scene.Options.Select(o => o.Key == "OVERCOME" ? o with { CardTargetId = scene.BridgeCard } :
                    o.Key == "HOLD_ON" ? o with { HealthDamage = 3 + scene.BridgeHoldOns } : o).ToImmutableArray() };
                break;
            case "TABLET_OF_TRUTH":
                int decipherCount = Read<int>(ev, "_decipherCount");
                int cost = decipherCount switch { 0 => 3, 1 => 6, 2 => 12, 3 => 24, 4 => owner.Creature.MaxHp - 1,
                    _ => throw new InvalidOperationException("OperationTabletStageInvalid") };
                scene = scene with { TabletCount = decipherCount, Options = scene.Options.Select(o => o.Key == "DECIPHER"
                    ? o with { MaxHpCost = cost, WillKill = cost >= owner.Creature.MaxHp } : o).ToImmutableArray() };
                break;
            case "TINKER_TIME":
                if (ev.Description?.LocEntryKey.Contains("CHOOSE_RIDER", StringComparison.Ordinal) == true)
                    scene = scene with { TinkerType = Enum.Parse<EffectCardType>(Read<CardType>(ev, "_chosenCardType").ToString()) };
                break;
            case "THE_FUTURE_OF_POTIONS":
                var types = ReadNullable(ev, "_cardTypes") as IDictionary
                    ?? throw new InvalidOperationException("OperationFuturePotionMappingMissing");
                scene = scene with { FuturePotionTypes = types.Keys.Cast<object>().ToImmutableDictionary(
                    potion => Id((PotionModel)potion), potion => Enum.Parse<EffectCardType>(types[potion]!.ToString()!)),
                    FuturePotionRarities = types.Keys.Cast<PotionModel>().ToImmutableDictionary(Id, FutureRarity) };
                break;
            case "RANWID_THE_ELDER":
                scene = scene with { RanwidPotion = nativeOptions.Select(o => Captured<PotionModel>(Callback(o)?.Target)).FirstOrDefault(p => p != null) is { } p ? Id(p) : null,
                    RanwidRelic = nativeOptions.Select(o => Captured<RelicModel>(Callback(o)?.Target)).FirstOrDefault(r => r != null) is { } r ? Id(r) : null };
                break;
            case "WELCOME_TO_WONGOS":
                scene = scene with { WongoFeatured = ReadNullable(ev, "_featuredItem") is RelicModel featured ? Key(featured) : null,
                    WongoPointsEarned = branch switch { "BARGAIN_BIN" => 32, "FEATURED_ITEM" => 16, "MYSTERY_BOX" => 8, _ => 0 } };
                break;
            case "WAR_HISTORIAN_REPY":
                scene = scene with { RepySecondPending = run.Players.Count == 1 && owner.Deck.Cards.Any(c => c.Id.Entry == "LANTERN_KEY") &&
                    branch?.StartsWith("INITIAL_", StringComparison.Ordinal) == true };
                break;
            case "ENDLESS_CONVEYOR":
                object dish = Read<object>(ev, "_currentDish");
                string dishId = Read<string>(dish, "id");
                scene = scene with { ConveyorDish = dishId, ConveyorLastDish = Read<string>(ev, "_lastDishId"),
                    ConveyorGrabs = Read<int>(ev, "_numOfGrabs"), ConveyorHadGrab =
                        nativeOptions.Any(o => o.TextKey.EndsWith(".LEAVE", StringComparison.Ordinal)) || branch?.StartsWith("GRAB_", StringComparison.Ordinal) == true };
                scene = scene with { Options = scene.Options.Select(o => o.Key == "LOCKED" ? o with { Key = "GRAB_" + dishId } : o).ToImmutableArray() };
                break;
        }
        if (obtainOrigin != null)
        {
            if (rootOrigin!.ParentReward is { } parentReward)
                return CaptureObtainChild(owner, root, catalog, scene,
                    parentReward is CardReward ? PredictorObtainReturn.RewardAlternative : PredictorObtainReturn.Reward,
                    Id(parentReward), obtainOrigin, childRewards);
            long sourceId = EventObtainSource(ev, branch, rootOrigin.Relic);
            return CaptureObtainChild(owner, root, catalog, scene with { Options = [], RewardScene = null },
                PredictorObtainReturn.Event, sourceId, obtainOrigin, childRewards);
        }
        if (grids.SingleOrDefault() is { } grid)
        {
            if (branch == null) throw new NotSupportedException("OperationEventSelectorParentMissing:" + ev.Id);
            // A relic acquired by an event can own this screen instead. Never
            // reinterpret that child as the event's own transform operation.
            if ((ev.Id.Entry, branch) is not (
                ("AROMA_OF_CHAOS", "LET_GO" or "MAINTAIN_CONTROL") or
                ("MORPHIC_GROVE", "GROUP") or ("WHISPERING_HOLLOW", "HUG") or
                ("SYMBIOTE", "APPROACH" or "KILL_WITH_FIRE") or
                ("TRIAL", "MERCHANT_INNOCENT" or "NONDESCRIPT_INNOCENT") or
                ("DOORS_OF_LIGHT_AND_DARK", "DARK") or ("WELLSPRING", "BATHE") or
                ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH") or
                ("ROOM_FULL_OF_CHEESE", "GORGE") or ("BRAIN_LEECH", "SHARE_KNOWLEDGE") or
                ("ENDLESS_CONVEYOR", "GRAB_JELLY_LIVER")))
                throw new NotSupportedException("OperationEventObtainChildCapturePending:" + ev.Id + ":" + branch);
            var nativeCards = Read<IReadOnlyList<CardModel>>(grid, "_cards");
            var prefs = Read<CardSelectorPrefs>(grid, "_prefs");
            if (prefs.MinSelect != prefs.MaxSelect || prefs.MinSelect < 1)
                throw new NotSupportedException("OperationEventVariableSelectionPending:" + ev.Id);
            bool generated = (ev.Id.Entry, branch) is ("ROOM_FULL_OF_CHEESE", "GORGE") or ("BRAIN_LEECH", "SHARE_KNOWLEDGE");
            if (!generated && nativeCards.Any(c => !owner.Deck.Cards.Contains(c)))
                throw new InvalidOperationException("OperationEventSelectorContainsForeignCard");
            scene = scene with { Options = [], AwaitingCards = true, CardsRequired = prefs.MinSelect,
                CardCandidates = nativeCards.Select(Id).ToImmutableArray(),
                SelectedCardIds = Read<IEnumerable<CardModel>>(grid, "_selectedCards").Select(Id).ToImmutableArray(),
                OfferedCards = generated ? nativeCards.Select(c => Card(c, catalog)).ToImmutableArray() : [] };
        }
        if (scene.Options.IsEmpty && scene.RewardScene == null && !scene.AwaitingCards)
            throw new NotSupportedException("OperationEventStageUnavailable:" + ev.Id);
        return scene;
    }

    private static Delegate? Callback(EventOption option) => ReadNullable(option, "<OnChosen>k__BackingField") as Delegate;

    private static long EventObtainSource(EventModel ev, string? branch, RelicModel obtained) => (ev.Id.Entry, branch) switch
    {
        ("TRASH_HEAP", "DIVE_IN") => 1, ("THIS_OR_THAT", "ORNATE") => 4, ("UNREST_SITE", "KILL") => 5,
        ("ROUND_TEA_PARTY", "CONTINUE_FIGHT") => 10, ("ROUND_TEA_PARTY", "ENJOY_TEA") => 11,
        ("LUMINOUS_CHOIR", "OFFER_TRIBUTE") => 13,
        ("RANWID_THE_ELDER", "POTION") => 16, ("RANWID_THE_ELDER", "GOLD") => 17,
        ("RELIC_TRADER", "TOP" or "MIDDLE" or "BOTTOM") => 20,
        ("HUNGRY_FOR_MUSHROOMS", "BIG_MUSHROOM") => 25,
        ("HUNGRY_FOR_MUSHROOMS", "FRAGRANT_MUSHROOM") => 26,
        ("ROOM_FULL_OF_CHEESE", "SEARCH") => 24, ("DOLL_ROOM", _) => 27,
        ("WELCOME_TO_WONGOS", _) when obtained.Id.Entry == "WONGO_CUSTOMER_APPRECIATION_BADGE" => 31,
        ("WELCOME_TO_WONGOS", "BARGAIN_BIN") => 28, ("WELCOME_TO_WONGOS", "FEATURED_ITEM") => 29,
        ("WELCOME_TO_WONGOS", "MYSTERY_BOX") => 30,
        ("WAR_HISTORIAN_REPY", "INITIAL_UNLOCK_CAGE") => 32, ("WAR_HISTORIAN_REPY", "SECOND_UNLOCK_CAGE") => 33,
        ("TRIAL", "MERCHANT_GUILTY") => 1 + ObservedOrdinal(ev, obtained),
        ("RANWID_THE_ELDER", "RELIC") => 17 + ObservedOrdinal(ev, obtained),
        _ => throw new NotSupportedException("OperationEventObtainContinuationNotCaptured:" + ev.Id.Entry + ":" + branch)
    };

    private static int ObservedOrdinal(EventModel ev, RelicModel obtained)
    {
        if (!ObtainMarkers.TryGetValue(obtained, out var marker) || marker.Event != ev || marker.Ordinal is < 1 or > 2 ||
            ReadNullable(ev, "_currentOptions") is not IReadOnlyList<EventOption> options || !options.Contains(marker.Option))
            throw new NotSupportedException("OperationEventObtainOrdinalMissing:" + ev.Id.Entry);
        return marker.Ordinal;
    }

    private static EffectCardRarity FutureRarity(PotionModel potion) => potion.Rarity.ToString() switch
    {
        "Rare" or "Event" => EffectCardRarity.Rare, "Uncommon" => EffectCardRarity.Uncommon,
        "Common" or "Token" => EffectCardRarity.Common,
        _ => throw new InvalidOperationException("OperationFuturePotionRarityInvalid:" + potion.Id)
    };

    // Only compiler-generated closure fields are inspected; never invoke a callback.
    private static T? Captured<T>(object? owner, int depth = 0) where T : class
    {
        if (owner is T found) return found;
        if (owner == null || depth > 3 || owner is AbstractModel || owner is Node || owner is Delegate) return null;
        var type = owner.GetType();
        if (!type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false) && !type.IsValueType) return null;
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (Captured<T>(field.GetValue(owner), depth + 1) is { } value) return value;
        return null;
    }

    internal static PredictorEventOption Option(EventModel ev, EventOption option)
    {
        string key = option.TextKey.Split('.').Last();
        if (key.EndsWith("_LOCKED", StringComparison.Ordinal)) key = key[..^7];
        if (ev.Id.Entry == "RELIC_TRADER")
        {
            if (key == "PROCEED") return new("DONE", !option.IsLocked);
            int index = key switch { "TOP" => 0, "MIDDLE" => 1, "BOTTOM" => 2,
                _ => throw new NotSupportedException("OperationRelicTraderChoiceMissing:" + key) };
            // Read initialized backing lists; their getters lazily shuffle and
            // pull relics and therefore must never be used during capture.
            var owned = ReadNullable(ev, "_ownedRelics") as IReadOnlyList<RelicModel>;
            var offered = ReadNullable(ev, "_newRelics") as IReadOnlyList<RelicModel>;
            if (owned == null || offered == null || index >= owned.Count || index >= offered.Count)
                throw new InvalidOperationException("OperationRelicTraderOffersMissing");
            return new(key, !option.IsLocked, RelicId: Id(owned[index]), OfferedRelic: Key(offered[index]));
        }
        if (ev.Id.Entry == "TRIAL" && key is "GUILTY" or "INNOCENT")
            key = option.TextKey.Split('.')[2] + "_" + key;
        if (ev.Id.Entry == "SLIPPERY_BRIDGE" && key.StartsWith("HOLD_ON", StringComparison.Ordinal)) key = "HOLD_ON";
        if (ev.Id.Entry == "TABLET_OF_TRUTH" && key.StartsWith("DECIPHER", StringComparison.Ordinal)) key = "DECIPHER";
        if (ev.Id.Entry == "WAR_HISTORIAN_REPY")
            key = Callback(option)?.Method.Name switch
            {
                "InitialUnlockCage" => "INITIAL_UNLOCK_CAGE", "InitialUnlockChest" => "INITIAL_UNLOCK_CHEST",
                "SecondUnlockCage" => "SECOND_UNLOCK_CAGE", "SecondUnlockChest" => "SECOND_UNLOCK_CHEST",
                _ => throw new NotSupportedException("OperationRepyChoiceParentMissing")
            };
        if (ev.Id.Entry == "COLORFUL_PHILOSOPHERS")
        {
            var pool = Captured<CardPoolModel>(Callback(option)?.Target)
                ?? throw new InvalidOperationException("OperationColorfulPoolMissing");
            key = ModelDb.AllCharacters.Single(c => c.CardPool.Id == pool.Id).Id.Entry;
        }
        if (ev.Id.Entry == "DOLL_ROOM" && Captured<RelicModel>(Callback(option)?.Target) is { } doll)
            return new("DOLL_" + doll.Id.Entry, !option.IsLocked, OfferedRelic: Key(doll));
        if (ev.Id.Entry == "THE_FUTURE_OF_POTIONS")
        {
            var potion = Captured<PotionModel>(Callback(option)?.Target)
                ?? throw new InvalidOperationException("OperationFuturePotionChoiceMissing");
            var types = (IDictionary)Read<object>(ev, "_cardTypes");
            return new("POTION:" + Id(potion), !option.IsLocked, PotionId: Id(potion),
                CardType: Enum.Parse<EffectCardType>(types[potion]!.ToString()!));
        }
        if (ev.Id.Entry == "ENDLESS_CONVEYOR" && key is not ("LEAVE" or "OBSERVE_CHEF" or "LOCKED")) key = "GRAB_" + key;
        int? goldCost = (ev.Id.Entry, key) switch
        {
            ("WHISPERING_HOLLOW", "GOLD") or ("LUMINOUS_CHOIR", "OFFER_TRIBUTE") => ev.DynamicVars.Gold.IntValue,
            ("WELCOME_TO_WONGOS", "BARGAIN_BIN") => 100, ("WELCOME_TO_WONGOS", "FEATURED_ITEM") => 200,
            ("WELCOME_TO_WONGOS", "MYSTERY_BOX") => 300, ("RANWID_THE_ELDER", "GOLD") => 100,
            _ => null
        };
        return new(key, !option.IsLocked, GoldCost: goldCost);
    }
}
