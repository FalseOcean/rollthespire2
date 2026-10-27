using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    private sealed class NativeOperations
    {
        internal readonly List<PurchaseObservation> Purchases = [];
        internal readonly List<ObtainObservation> Obtains = [];
        internal RestObservation? Rest;
        internal RemovalObservation? Removal;
    }
    private sealed record PurchaseObservation(MerchantEntry Entry, int BeforeGold, bool IgnoreCost)
    { internal Task? Task; }
    private sealed record ObtainObservation(RelicModel Relic, ObtainObservation? Parent, Reward? Reward)
    { internal Task? Task; internal int ChildrenStarted; internal int Ordinal; }
    private sealed record RestObservation(RestSiteOption Option, ImmutableArray<RestSiteOption> Options, int Index)
    { internal Task? Task; }
    private sealed record RemovalObservation(int Quote, bool Cancelable)
    { internal Task? Task; }
    private sealed record RewardObservation(Reward Reward)
    { internal Task? Task; }
    private sealed record RewardPrefixState(Reward? Previous, RewardObservation Current,
        ObtainObservation? PreviousObtain, RestObservation? PreviousRest);
    private static readonly ConditionalWeakTable<Player, NativeOperations> Observed = new();
    private static readonly AsyncLocal<ObtainObservation?> Obtaining = new();
    private static readonly AsyncLocal<Reward?> SelectingReward = new();
    private static readonly AsyncLocal<RestObservation?> Resting = new();
    private static readonly ConditionalWeakTable<Node, ObtainObservation> SelectionSources = new();
    private static readonly ConditionalWeakTable<Node, Reward> SelectionRewards = new();
    private static readonly ConditionalWeakTable<Reward, RewardObservation> RewardSelections = new();
    private static readonly ConditionalWeakTable<Reward, ObtainObservation> RewardObtainSources = new();
    private static readonly ConditionalWeakTable<Reward, RestObservation> RewardRestSources = new();
    private static readonly ConditionalWeakTable<RewardsSet, ObtainObservation> RewardSetSources = new();
    private static readonly ConditionalWeakTable<RewardsSet, RestObservation> RestRewardSources = new();
    private sealed record ChestOpening(Task Task);
    private static readonly ConditionalWeakTable<NTreasureRoom, ChestOpening> ChestOpenings = new();

    // Postfixes only retain task identity. Task completion is inspected later on
    // the main thread; no continuation reads game or Godot objects.
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper)),
            prefix: new(typeof(PredictorOperationCapture), nameof(BeforePurchase)),
            postfix: new(typeof(PredictorOperationCapture), nameof(AfterPurchase)));
        harmony.Patch(AccessTools.Method(typeof(RelicCmd), nameof(RelicCmd.Obtain), [typeof(RelicModel), typeof(Player), typeof(int)]),
            prefix: new(typeof(PredictorOperationCapture), nameof(BeforeObtain)),
            postfix: new(typeof(PredictorOperationCapture), nameof(AfterObtain)),
            finalizer: new(typeof(PredictorOperationCapture), nameof(FinalizeObtain)));
        foreach (var type in new[] { typeof(NCardGridSelectionScreen), typeof(NChooseACardSelectionScreen), typeof(NChooseABundleSelectionScreen) })
            harmony.Patch(AccessTools.Method(type, "CardsSelected"),
                prefix: new(typeof(PredictorOperationCapture), nameof(BeforeSelection)));
        harmony.Patch(AccessTools.Method(typeof(Reward), nameof(Reward.SelectUnsynchronized)),
            prefix: new(typeof(PredictorOperationCapture), nameof(BeforeReward)),
            postfix: new(typeof(PredictorOperationCapture), nameof(AfterReward)),
            finalizer: new(typeof(PredictorOperationCapture), nameof(FinalizeReward)));
        harmony.Patch(AccessTools.Method(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.BeginRewardsSet)),
            prefix: new(typeof(PredictorOperationCapture), nameof(BeforeRewardsSet)));
        harmony.Patch(AccessTools.Method(typeof(RestSiteSynchronizer), "ChooseOption"),
            prefix: new(typeof(PredictorOperationCapture), nameof(BeforeRest)),
            postfix: new(typeof(PredictorOperationCapture), nameof(AfterRest)),
            finalizer: new(typeof(PredictorOperationCapture), nameof(FinalizeRest)));
        harmony.Patch(AccessTools.Method(typeof(OneOffSynchronizer), "DoMerchantCardRemoval"),
            prefix: new(typeof(PredictorOperationCapture), nameof(BeforeRemoval)),
            postfix: new(typeof(PredictorOperationCapture), nameof(AfterRemoval)));
        harmony.Patch(AccessTools.Method(typeof(NTreasureRoom), "OpenChest"),
            postfix: new(typeof(PredictorOperationCapture), nameof(AfterChestOpening)));
    }

    private static void BeforePurchase(MerchantEntry __instance, bool ignoreCost, out PurchaseObservation? __state)
    {
        __state = null;
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            var player = Read<Player>(__instance, "_player");
            var observed = Observed.GetOrCreateValue(player);
            observed.Purchases.RemoveAll(p => p.Task?.IsCompleted == true);
            __state = new(__instance, player.Gold, ignoreCost);
            observed.Purchases.Add(__state);
        }
        catch (Exception ex) { RuntimeLog.WarnException("operationPurchaseObservationFailed=true", ex); }
    }
    private static void AfterPurchase(Task<bool> __result, PurchaseObservation? __state)
    { if (__state != null) __state.Task = __result; }

    private static void BeforeObtain(RelicModel relic, Player player, out ObtainObservation? __state)
    {
        __state = null;
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            var observed = Observed.GetOrCreateValue(player);
            observed.Obtains.RemoveAll(o => o.Task?.IsCompleted == true);
            __state = new(relic, Obtaining.Value, SelectingReward.Value);
            if (__state.Parent != null) __state.Ordinal = ++__state.Parent.ChildrenStarted;
            observed.Obtains.Add(__state);
            // The native async call captures this execution context at its
            // awaits. Restoring it in the postfix leaves callers untouched,
            // while a child selection can identify its real suspended source.
            Obtaining.Value = __state;
        }
        catch (Exception ex) { RuntimeLog.WarnException("operationObtainObservationFailed=true", ex); }
    }
    private static void AfterObtain(Task<RelicModel> __result, ObtainObservation? __state)
    {
        if (__state == null) return;
        __state.Task = __result;
        Obtaining.Value = __state.Parent;
    }
    // A synchronous native throw skips its postfix. Restore only our ambient
    // observation context and preserve the original exception unchanged.
    private static Exception? FinalizeObtain(Exception? __exception, ObtainObservation? __state)
    {
        if (__state != null) Obtaining.Value = __state.Parent;
        return __exception;
    }

    private static void BeforeSelection(Node __instance)
    {
        if (SelectingReward.Value is { } reward)
        {
            SelectionRewards.Remove(__instance);
            SelectionRewards.Add(__instance, reward);
        }
        if (Obtaining.Value is not { } source) return;
        SelectionSources.Remove(__instance);
        SelectionSources.Add(__instance, source);
    }

    private static PredictorOperationRewardRemoval? CaptureRewardRemoval(RewardsSet set, Node? root)
    {
        var selector = Nodes<NDeckCardSelectScreen>(root).LastOrDefault(n => n.IsVisibleInTree());
        if (selector == null || !SelectionRewards.TryGetValue(selector, out var reward) ||
            reward is not CardRemovalReward || !NativeContainsReward(set, reward)) return null;
        if (!SelectionIsPending(selector)) throw new InvalidOperationException("OperationRewardRemovalSettling");
        var prefs = Read<CardSelectorPrefs>(selector, "_prefs");
        return new(Id(reward), Read<IReadOnlyList<CardModel>>(selector, "_cards").Select(Id).ToImmutableArray(),
            prefs.MinSelect, prefs.MaxSelect, prefs.Cancelable);
    }

    private static void BeforeReward(Reward __instance, out RewardPrefixState __state)
    {
        var observation = new RewardObservation(__instance);
        __state = new(SelectingReward.Value, observation, Obtaining.Value, Resting.Value);
        RewardSelections.Remove(__instance); RewardSelections.Add(__instance, observation);
        // A later Godot button callback has a fresh execution context. Rebind
        // the actual producer recorded when its reward set was offered.
        if (RewardObtainSources.TryGetValue(__instance, out var obtain)) Obtaining.Value = obtain;
        if (RewardRestSources.TryGetValue(__instance, out var rest)) Resting.Value = rest;
        SelectingReward.Value = __instance;
    }
    private static void AfterReward(Task<bool> __result, RewardPrefixState __state)
    {
        __state.Current.Task = __result;
        RestoreRewardScope(__state);
    }
    private static void RestoreRewardScope(RewardPrefixState state)
    {
        SelectingReward.Value = state.Previous;
        Obtaining.Value = state.PreviousObtain;
        Resting.Value = state.PreviousRest;
    }
    private static Exception? FinalizeReward(Exception? __exception, RewardPrefixState? __state)
    {
        if (__state != null) RestoreRewardScope(__state);
        return __exception;
    }

    private static void RequireStableRewardSelection(IEnumerable<Reward> rewards, Node? root)
    {
        foreach (var reward in rewards)
        {
            if (!RewardSelections.TryGetValue(reward, out var observed) || observed.Task?.IsCompleted == true) continue;
            if (reward is CardReward card && ReadNullable(card, "_currentlyShownScreen") is Node screen &&
                screen.IsInsideTree() && SelectionIsPending(screen)) continue;
            if (Nodes<NCardGridSelectionScreen>(root).Any(n => n.IsVisibleInTree() && SelectionIsPending(n) &&
                SelectionRewards.TryGetValue(n, out var source) && ReferenceEquals(source, reward))) continue;
            if (Observed.TryGetValue(reward.Player, out var operations) && operations.Obtains.Any(o =>
                o.Task?.IsCompleted != true && ReferenceEquals(o.Reward, reward))) continue;
            throw new InvalidOperationException("OperationRewardSettling:" + reward.GetType().Name);
        }
    }
    private static void BeforeRewardsSet(RewardsSet set)
    {
        var rewards = NativeRewardLeaves(set.Rewards).Distinct<Reward>(ReferenceEqualityComparer.Instance).ToArray();
        RestRewardSources.Remove(set);
        RewardSetSources.Remove(set);
        foreach (var reward in rewards)
        { RewardRestSources.Remove(reward); RewardObtainSources.Remove(reward); }
        if (Resting.Value is { } rest)
        {
            RestRewardSources.Add(set, rest);
            foreach (var reward in rewards) RewardRestSources.Add(reward, rest);
        }
        if (Obtaining.Value is not { } source) return;
        RewardSetSources.Add(set, source);
        foreach (var reward in rewards) RewardObtainSources.Add(reward, source);
    }

    private sealed record RestPrefixState(RestObservation Current, RestObservation? Previous);
    private static void BeforeRest(RestSiteSynchronizer __instance, Player player, int optionIndex, out RestPrefixState? __state)
    {
        __state = null;
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            var sites = Read<System.Collections.IList>(__instance, "_restSites");
            int slot = player.RunState.Players.ToList().IndexOf(player);
            var options = Read<List<RestSiteOption>>(sites[slot]!, "options").ToImmutableArray();
            var observation = new RestObservation(options[optionIndex], options, optionIndex);
            __state = new(observation, Resting.Value);
            Observed.GetOrCreateValue(player).Rest = observation;
            Resting.Value = observation;
        }
        catch (Exception ex) { RuntimeLog.WarnException("operationRestObservationFailed=true", ex); }
    }
    private static void AfterRest(Task<bool> __result, RestPrefixState? __state)
    {
        if (__state == null) return;
        __state.Current.Task = __result;
        Resting.Value = __state.Previous;
    }
    private static Exception? FinalizeRest(Exception? __exception, RestPrefixState? __state)
    {
        if (__state != null) Resting.Value = __state.Previous;
        return __exception;
    }

    private static void BeforeRemoval(Player player, int goldCost, bool cancelable, out RemovalObservation? __state)
    {
        __state = null;
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            __state = new(goldCost, cancelable);
            Observed.GetOrCreateValue(player).Removal = __state;
        }
        catch (Exception ex) { RuntimeLog.WarnException("operationRemovalObservationFailed=true", ex); }
    }
    private static void AfterRemoval(Task<bool> __result, RemovalObservation? __state)
    { if (__state != null) __state.Task = __result; }
    private static void AfterChestOpening(NTreasureRoom __instance, Task __result)
    { ChestOpenings.Remove(__instance); ChestOpenings.Add(__instance, new(__result)); }
    private static bool IsTreasureOpening(NTreasureRoom room) =>
        ChestOpenings.TryGetValue(room, out var opening) && !opening.Task.IsCompleted;

    private static PredictorOperationShopRemoval? CaptureShopRemoval(MerchantInventory inventory, Node? root)
    {
        if (!Observed.TryGetValue(inventory.Player, out var observed) || observed.Removal is not { } removal ||
            removal.Task?.IsCompleted == true) return null;
        var selector = Nodes<NDeckCardSelectScreen>(root).LastOrDefault(n => n.IsVisibleInTree());
        if (selector == null || !SelectionIsPending(selector)) throw new InvalidOperationException("OperationShopRemovalSettling");
        if (inventory.CardRemovalEntry == null) throw new InvalidOperationException("OperationShopRemovalEntryMissing");
        return new(Id(inventory.CardRemovalEntry), removal.Quote,
            Read<IReadOnlyList<CardModel>>(selector, "_cards").Select(Id).ToImmutableArray(), removal.Cancelable);
    }

    internal static bool SelectionIsPending(Node selector)
    {
        var completion = ReadNullable(selector, "_completionSource");
        return completion?.GetType().GetProperty("Task")?.GetValue(completion) is Task { IsCompleted: false };
    }

    internal sealed record NativeRestOrigin(string Action, ImmutableArray<string> Options, int Index);
    internal static NativeRestOrigin? ObservedRestOrigin(Player player)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!Observed.TryGetValue(player, out var observed) || observed.Rest is not { } rest) return null;
        RequireSuccessfulObservationTask(rest.Task, "Rest");
        return rest.Task?.IsCompleted != true
            ? new(rest.Option.OptionId, rest.Options.Select(o => o.OptionId).ToImmutableArray(), rest.Index) : null;
    }

    internal sealed record NativeObtainOrigin(RelicModel Relic, Reward? ParentReward, bool HasParentObtain)
    {
        internal NativeObtainOrigin? Parent { get; init; }
        internal int ChildOrdinal { get; init; }
        internal int ChildrenStarted { get; init; }
    }
    private static NativeObtainOrigin Origin(ObtainObservation source) => new(source.Relic, source.Reward, source.Parent != null)
        { Parent = source.Parent == null ? null : Origin(source.Parent), ChildOrdinal = source.Ordinal, ChildrenStarted = source.ChildrenStarted };
    private static bool HasPendingNativeObtain(Player player) => Observed.TryGetValue(player, out var observed) &&
        observed.Obtains.Any(o => o.Task?.IsCompleted != true);
    internal static NativeObtainOrigin? ObservedRewardSetObtainSource(RewardsSet set)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!RewardSetSources.TryGetValue(set, out var source)) return null;
        RequireSuccessfulObservationTask(source.Task);
        return source.Task?.IsCompleted == false ? Origin(source) : null;
    }

    private static void RequireSuccessfulObservationTask(Task? task, string stage = "Obtain")
    {
        if (task?.IsFaulted == true) throw new InvalidOperationException("OperationNative" + stage + "Failed", task.Exception);
        if (task?.IsCanceled == true) throw new OperationCanceledException("OperationNative" + stage + "Canceled");
    }

    internal static NativeObtainOrigin? ObservedObtainOrigin(Player player, Node? root)
    {
        var sourceRelic = ObservedObtainSource(player, root);
        if (sourceRelic == null || !Observed.TryGetValue(player, out var observed)) return null;
        var source = observed.Obtains.Last(o => ReferenceEquals(o.Relic, sourceRelic));
        return Origin(source);
    }

    internal static RelicModel? ObservedObtainSource(Player player, Node? root)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var screens = Nodes<NCardGridSelectionScreen>(root).Cast<Node>()
            .Concat(Nodes<NChooseACardSelectionScreen>(root)).Concat(Nodes<NChooseABundleSelectionScreen>(root))
            .Where(n => n is Control c && c.IsVisibleInTree()).ToArray();
        if (screens.Length == 0) return null;
        if (screens.Length != 1) throw new InvalidOperationException("OperationAmbiguousObtainSelection");
        if (!SelectionIsPending(screens[0])) throw new InvalidOperationException("OperationObtainSelectionSettling");
        if (!SelectionSources.TryGetValue(screens[0], out var source)) return null;
        RequireSuccessfulObservationTask(source.Task);
        if (!player.Relics.Contains(source.Relic) || source.Task?.IsCompleted != false)
            throw new InvalidOperationException("OperationObtainSourceNotPending");
        return source.Relic;
    }

    internal static PredictorOperationDeckObtain? CaptureDeckObtain(Player player, Node? root)
    {
        var selector = Nodes<NCardGridSelectionScreen>(root).LastOrDefault(n => n.IsVisibleInTree());
        if (selector == null) return null;
        var relic = ObservedObtainSource(player, root)
            ?? throw new NotSupportedException("OperationObtainParentObservationMissing");
        return CaptureDeckObtainChoice(relic, Read<CardSelectorPrefs>(selector, "_prefs"),
            Read<IReadOnlyList<CardModel>>(selector, "_cards").Select(Id).ToImmutableArray());
    }

    internal static PredictorOperationDeckObtain CaptureDeckObtainChoice(RelicModel relic, CardSelectorPrefs prefs,
        ImmutableArray<long> candidates)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var operation = Key(relic).Entry switch
        {
            "ASTROLABE" => PredictorObtainDeckOperation.Astrolabe,
            "PAELS_TOOTH" => PredictorObtainDeckOperation.PaelsTooth,
            "CLAWS" => PredictorObtainDeckOperation.Claws,
            "PRECISE_SCISSORS" or "BIIIG_HUG" or "PRECARIOUS_SHEARS" or "EMPTY_CAGE" or "PRESERVED_FOG" => PredictorObtainDeckOperation.Remove,
            "POMANDER" or "YUMMY_COOKIE" => PredictorObtainDeckOperation.Upgrade,
            "TRI_BOOMERANG" or "ELECTRIC_SHRYMP" or "PAELS_GROWTH" or "GNARLED_HAMMER" or "KIFUDA" or
                "PUNCH_DAGGER" or "ROYAL_STAMP" => PredictorObtainDeckOperation.Enchant,
            "DOLLYS_MIRROR" => PredictorObtainDeckOperation.Clone,
            _ => throw new NotSupportedException("OperationObtainChoiceNotCaptured:" + Key(relic).Serialized)
        };
        return new(Id(relic), Key(relic), operation, prefs.MaxSelect, candidates)
            { MinimumCount = prefs.MinSelect, MaximumCount = prefs.MaxSelect, Cancelable = prefs.Cancelable };
    }

    private static PredictorOperationPaidPurchase? CapturePaidPurchase(MerchantInventory inventory)
    {
        if (!Observed.TryGetValue(inventory.Player, out var observed)) return null;
        observed.Purchases.RemoveAll(p => p.Task?.IsCompleted == true);
        var pending = observed.Purchases.Where(p => inventory.AllEntries.Contains(p.Entry)).ToArray();
        if (pending.Length == 0) return null;
        if (pending.Length != 1) throw new InvalidOperationException("OperationShopConcurrentPurchases");
        return new(Id(pending[0].Entry), pending[0].BeforeGold, pending[0].IgnoreCost);
    }
}
