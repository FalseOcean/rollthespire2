using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation;

namespace RolltheSpire2.Infrastructure.Snapshots;

// Main-thread identity admission only. Never calls native selection, purchase,
// reward population, option generation, Cost getters or alternative delegates.
internal static class OperationSurfaceHoverCapture
{
    private static readonly System.Reflection.PropertyInfo NativeState = AccessTools.Property(typeof(RunManager), "State")
        ?? throw new MissingMemberException(typeof(RunManager).FullName, "State");
    private static readonly Dictionary<Type, bool> RelicObtainInterest = [];
    // Audited native overrides whose only obtain consequences are already
    // public HP/gold, empty potion slots or KindleCount. The projection emits
    // none of these values. This is an exact-type omission list, not a claim
    // that deterministic obtains in general cannot create hidden output.
    private static readonly HashSet<Type> PublicObtainOverrides = [
        typeof(Strawberry), typeof(Pear), typeof(Mango), typeof(NutritiousOyster),
        typeof(OldCoin), typeof(GoldenPearl), typeof(SignetRing),
        typeof(PotionBelt), typeof(PumpkinCandle)];

    internal static bool EventMayDisplay(EventModel ev, PredictorEventOption option)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        return OperationPreviewEligibility.EventMayDisplay(ev.Id.Entry, option.Key,
            ev.Id.Entry == "TINKER_TIME" && ev.Description?.LocEntryKey.Contains("CHOOSE_RIDER", StringComparison.Ordinal) == true,
            ev.Id.Entry == "TABLET_OF_TRUTH" && option.Key == "DECIPHER"
                ? PredictorOperationCapture.Read<int>(ev, "_decipherCount") : 0,
            option.OfferedRelic != null);
    }

    // Native RelicCmd.Obtain calls AfterObtained; Player.AddRelicInternal also
    // notifies already generated CardRewards. Preserve both early and late
    // modifiers, even when the acquired relic has no AfterObtained override.
    // Inspect metadata only, never call any obtain/hook method or model getter.
    // Unknown types retain their existing capture/simulation path.
    internal static bool MayDisplayRelicObtain(RelicModel relic)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var type = relic.GetType();
        if (!RelicObtainInterest.TryGetValue(type, out bool interested))
        {
            interested = type.Assembly != typeof(RelicModel).Assembly || !type.IsSealed ||
                type.Namespace != "MegaCrit.Sts2.Core.Models.Relics" ||
                (type.GetMethod(nameof(RelicModel.AfterObtained))?.DeclaringType != typeof(RelicModel) &&
                    !PublicObtainOverrides.Contains(type)) ||
                type.GetMethod(nameof(AbstractModel.TryModifyCardRewardOptions))?.DeclaringType != typeof(AbstractModel) ||
                type.GetMethod(nameof(AbstractModel.TryModifyCardRewardOptionsLate))?.DeclaringType != typeof(AbstractModel);
            RelicObtainInterest.Add(type, interested);
        }
        return interested;
    }

    internal static RunState? CurrentRun()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        return NativeState.GetValue(RunManager.Instance) as RunState;
    }

    internal static bool TryShop(NMerchantSlot owner, out RunState? run, out long entryId, out ModelKey source)
    {
        run = null; entryId = 0; source = default;
        if (!Visible(owner) || NOverlayStack.Instance?.Peek() != null ||
            owner is not (NMerchantCard or NMerchantPotion or NMerchantRelic)) return false;
        var inventoryNode = Ancestor<NMerchantInventory>(owner);
        if (inventoryNode is not { IsOpen: true, Inventory: { } inventory } || !owner.Entry.IsStocked ||
            inventory.Player.RunState is not RunState actual || !ReferenceEquals(LocalContext.GetMe(actual), inventory.Player) ||
            actual.CurrentRoom is not MerchantRoom merchant || !ReferenceEquals(merchant.GetLocalInventory(), inventory)) return false;
        var entry = owner.Entry;
        AbstractModel? model = entry switch
        {
            MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry card => card.CreationResult?.Card,
            MegaCrit.Sts2.Core.Entities.Merchant.MerchantPotionEntry potion => potion.Model,
            MegaCrit.Sts2.Core.Entities.Merchant.MerchantRelicEntry relic => relic.Model,
            _ => null
        };
        if (model == null) return false;
        // A public card/bottle has no hidden purchase output without restock.
        // Relic obtains can additionally open rewards or change other rewards.
        bool courier = inventory.Player.Relics.Any(r => !r.IsMelted && r.Id.Entry == "THE_COURIER");
        if (!courier && model.Id.Entry != "THE_COURIER" &&
            (model is not RelicModel acquiredRelic || !MayDisplayRelicObtain(acquiredRelic))) return false;
        run = actual; entryId = PredictorLiveRunCapture.Id(entry); source = PredictorLiveRunCapture.Key(model);
        return true;
    }

    internal static bool TryRest(NRestSiteButton owner, out RunState? run, out PredictorRestAction action)
    {
        run = null; action = default;
        if (!Visible(owner) || !owner.IsEnabled || !owner.Option.IsEnabled ||
            NOverlayStack.Instance?.Peek() != null || PredictorOperationCapture.Read<bool>(owner, "_executingOption") ||
            !Enum.TryParse(owner.Option.OptionId, true, out action) ||
            action is not (PredictorRestAction.Heal or PredictorRestAction.Dig) ||
            CurrentRun() is not RunState actual || actual.CurrentRoom is not RestSiteRoom room ||
            !room.Options.Any(o => ReferenceEquals(o, owner.Option))) return false;
        var player = LocalContext.GetMe(actual);
        if (player == null || action == PredictorRestAction.Heal && !player.Relics.Any(r => !r.IsMelted &&
                r.Id.Entry is "DREAM_CATCHER" or "TINY_MAILBOX")) return false;
        run = actual; return true;
    }

    internal static bool TryRewardAlternative(NCardRewardAlternativeButton owner, out RunState? run,
        out long rewardId, out string optionId, out long? wingId)
    {
        run = null; rewardId = 0; optionId = ""; wingId = null;
        if (!Visible(owner) || !owner.IsEnabled || Ancestor<NCardRewardSelectionScreen>(owner) is not { } screen ||
            !ReferenceEquals(NOverlayStack.Instance?.Peek(), screen) ||
            CurrentRun() is not RunState actual || LocalContext.GetMe(actual) is not { } player)
            return false;
        var buttons = owner.GetParent().GetChildren().OfType<NCardRewardAlternativeButton>()
            .Where(n => !n.IsQueuedForDeletion()).ToArray();
        int index = Array.IndexOf(buttons, owner);
        var alternatives = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(screen, "_extraOptions");
        if (index < 0 || buttons.Length != alternatives.Count || index >= alternatives.Count) return false;
        optionId = alternatives[index].OptionId;
        if (optionId is not ("REROLL" or "SACRIFICE")) return false;
        var rewards = PredictorOperationCapture.CurrentNativeRewards(actual, player, NRun.Instance);
        var reward = rewards == null ? null : PredictorOperationCapture.NativeRewardLeaves(rewards.Rewards).OfType<CardReward>()
            .SingleOrDefault(r => ReferenceEquals(PredictorOperationCapture.ReadNullable(r, "_currentlyShownScreen"), screen));
        if (reward == null || reward.SuccessfullySelected || optionId == "REROLL" && !reward.CanReroll) return false;
        if (optionId == "SACRIFICE")
        {
            var wings = player.Relics.Where(r => !r.IsMelted && r.Id.Entry == "PAELS_WING").ToArray();
            if (wings.Length != 1) return false;
            wingId = PredictorLiveRunCapture.Id(wings[0]);
        }
        run = actual; rewardId = PredictorLiveRunCapture.Id(reward); return true;
    }

    internal static bool TryReward(NRewardButton owner, out RunState? run, out long rewardId, out ModelKey source)
    {
        run = null; rewardId = 0; source = default;
        if (!Visible(owner) || !owner.IsEnabled || owner.Reward is not RelicReward { Relic: { } relic } reward ||
            reward.SuccessfullySelected || Ancestor<NRewardsScreen>(owner) is not { } screen ||
            !ReferenceEquals(NOverlayStack.Instance?.Peek(), screen) || CurrentRun() is not RunState actual ||
            LocalContext.GetMe(actual) is not { } player || !ReferenceEquals(reward.Player, player)) return false;
        var set = PredictorOperationCapture.CurrentNativeRewards(actual, player, NRun.Instance);
        if (set == null || !ReferenceEquals(PredictorOperationCapture.Read<RewardsSet>(screen, "_rewardsSet"), set) ||
            !PredictorOperationCapture.NativeContainsReward(set, reward)) return false;
        // Only a direct terminal combat set proves the pickup has no parent
        // obtain to resume. Event/rest/shop/linked/nested rewards remain eligible
        // even for passive items: taking the last one can release hidden output.
        if (!MayDisplayRelicObtain(relic) && reward.GetType() == typeof(RelicReward) && set.GetType() == typeof(RewardsSet) &&
            set.Room is CombatRoom && ReferenceEquals(set.Room, actual.CurrentRoom) &&
            set.Rewards.Contains(reward) && PredictorOperationCapture.NativeRewardSets(actual, player, NRun.Instance).Length == 1 &&
            PredictorOperationCapture.ObservedRewardSetObtainSource(set) == null) return false;
        run = actual; rewardId = PredictorLiveRunCapture.Id(reward); source = PredictorLiveRunCapture.Key(relic);
        return true;
    }

    internal static bool TryPotion(NPotionHolder owner, out RunState? run, out int slot, out ModelKey source)
    {
        bool admitted = TryPotion(owner, out run, out var actualSource);
        slot = actualSource?.HeldSlot ?? -1; source = actualSource?.Potion.Key ?? default;
        return admitted;
    }

    internal static bool TryPotion(NPotionHolder owner, out RunState? run, out PredictorOperationPotionSource? source)
    {
        run = null; source = null;
        if (!Visible(owner) || owner.Potion?.Model is not { } potion ||
            PredictorOperationCapture.Read<bool>(owner, "_disabledUntilPotionRemoved") ||
            CurrentRun() is not RunState actual || LocalContext.GetMe(actual) is not { } player ||
            Ancestor<NPotionContainer>(owner) is not { } potionContainer ||
            !ReferenceEquals(potionContainer, NRun.Instance?.GlobalUi?.TopBar?.PotionContainer)) return false;
        // Match the native instance, never its model ID: another player's
        // identical potion or a public shop/reward icon cannot supply a slot.
        var matching = player.PotionSlots.Select((value, index) => (value, index))
            .Where(p => ReferenceEquals(p.value, potion)).Select(p => p.index).ToArray();
        if (matching.Length != 1) return false;
        var key = PredictorLiveRunCapture.Key(potion);
        if (!PredictorOperationPotionTargetCapture.Supported(key)) return false;
        run = actual; source = new(new(PredictorLiveRunCapture.Id(potion), key), HeldSlot: matching[0]); return true;
    }

    internal static bool TryPotionReward(NRewardButton owner, out RunState? run, out PredictorOperationPotionSource? source)
    {
        run = null; source = null;
        if (!Visible(owner) || !owner.IsEnabled || owner.Reward is not PotionReward { Potion: { } potion } reward ||
            reward.SuccessfullySelected || !PredictorOperationPotionTargetCapture.Supported(PredictorLiveRunCapture.Key(potion)) ||
            Ancestor<NRewardsScreen>(owner) is not { } screen || !ReferenceEquals(NOverlayStack.Instance?.Peek(), screen) ||
            CurrentRun() is not RunState actual || LocalContext.GetMe(actual) is not { } player ||
            !ReferenceEquals(reward.Player, player)) return false;
        var set = PredictorOperationCapture.CurrentNativeRewards(actual, player, NRun.Instance);
        if (set == null || !ReferenceEquals(PredictorOperationCapture.Read<RewardsSet>(screen, "_rewardsSet"), set) ||
            !PredictorOperationCapture.NativeContainsReward(set, reward)) return false;
        run = actual;
        source = new(new(PredictorLiveRunCapture.Id(potion), PredictorLiveRunCapture.Key(potion)),
            RewardId: PredictorLiveRunCapture.Id(reward));
        return true;
    }

    internal static bool TryCardTarget(NCardHolder owner, out RunState? run, out long cardId)
        => TryCardTarget(owner, out run, out cardId, out _);

    internal static bool TryCardTarget(NCardHolder owner, out RunState? run, out long cardId, out string reason)
    {
        run = null; cardId = 0;
        reason = "OwnerNotVisible";
        if (!Visible(owner)) return false;
        reason = "OwnerNotGridCard";
        if (owner is not NGridCardHolder) return false;
        reason = "CardMissing";
        if (owner.CardModel is not { } card) return false;
        reason = "CardNotClickable";
        if (!PredictorOperationCapture.Read<bool>(owner, "_isClickable")) return false;
        reason = "CombatInProgress";
        if (CombatManager.Instance.IsInProgress) return false;
        reason = "SelectorAncestorMissing";
        if (Ancestor<NCardGridSelectionScreen>(owner) is not { } screen) return false;
        reason = "SelectorNotTopOverlay";
        if (!ReferenceEquals(NOverlayStack.Instance?.Peek(), screen)) return false;
        reason = "SelectionNotPending";
        if (!PredictorOperationCapture.SelectionIsPending(screen)) return false;
        reason = "CurrentRunMissing";
        if (CurrentRun() is not RunState actual) return false;
        reason = "LocalPlayerMissing";
        if (LocalContext.GetMe(actual) is not { } player) return false;
        reason = "CardNotInLocalDeck";
        if (!player.Deck.Cards.Any(c => ReferenceEquals(c, card))) return false;
        // Transform targets may originate in a shop/reward/ancient obtain.
        // Other deck selectors belong here only while an event owns the choice.
        reason = "SelectorNotOwnedByEvent";
        if (screen is not NDeckTransformSelectScreen &&
            (actual.CurrentRoom is not EventRoom eventRoom || !ReferenceEquals(eventRoom.LocalMutableEvent.Owner, player))) return false;
        var candidates = PredictorOperationCapture.Read<IReadOnlyList<CardModel>>(screen, "_cards");
        reason = "CardNotInSelector";
        if (!candidates.Any(c => ReferenceEquals(c, card))) return false;
        run = actual; cardId = PredictorLiveRunCapture.Id(card); reason = "Admitted"; return true;
    }

    private static bool Visible(Control owner)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        return GodotObject.IsInstanceValid(owner) && owner.IsInsideTree() && owner.IsVisibleInTree() && !owner.IsQueuedForDeletion();
    }

    private static T? Ancestor<T>(Node node) where T : Node
    {
        for (var parent = node.GetParent(); parent != null; parent = parent.GetParent())
            if (parent is T found) return found;
        return null;
    }
}
