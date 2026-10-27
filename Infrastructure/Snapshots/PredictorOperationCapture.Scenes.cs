using System.Collections;
using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using MegaCrit.Sts2.Core.Entities.Cards;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    private static PredictorOperationScene CaptureScene(RunState run, Player player, PredictorCatalog catalog, Node? root)
    {
        var inventoryNode = Nodes<NMerchantInventory>(root).LastOrDefault(n => n.Inventory?.Player == player);
        if (inventoryNode?.Inventory is { } inventory) return CaptureLiveShop(run, inventory, catalog, inventoryNode.IsOpen, root);
        // Crystal's native minigame offers ordinary RewardsSet loot before its
        // event callback returns. That live reward stack owns reroll/sacrifice
        // and any child obtain; the finished board cannot represent them.
        bool crystalLoot = run.CurrentRoom is EventRoom crystal && crystal.LocalMutableEvent.Id.Entry == "CRYSTAL_SPHERE" &&
            CurrentNativeRewards(run, player, root) != null;
        if (run.CurrentRoom is EventRoom ev && !crystalLoot)
        {
            if (ev.LocalMutableEvent is MegaCrit.Sts2.Core.Models.Events.FakeMerchant { Inventory: { } fakeInventory } && fakeInventory.Player == player)
                return CaptureLiveShop(run, fakeInventory, catalog, true, root);
            return ev.LocalMutableEvent.Id.Entry == "CRYSTAL_SPHERE" ? CaptureCrystal(run, ev.LocalMutableEvent, root)
                : ev.LocalMutableEvent is AncientEventModel ancient
                ? PredictorOperationAncientCapture.Capture(run, ancient, catalog, root)
                : PredictorOperationEventCapture.Capture(run, ev.LocalMutableEvent, catalog, root);
        }
        if (run.CurrentRoom is MerchantRoom merchant) return CaptureLiveShop(run, merchant.GetLocalInventory(), catalog, false, root);
        if (run.CurrentRoom is RestSiteRoom rest) return CaptureRest(run, player, rest, catalog, root);
        if (run.CurrentRoom is TreasureRoom) return CaptureTreasure(run, player, catalog, root);
        if (CaptureRewards(run, player, catalog, root) is { } rewards)
        {
            var origin = ObservedObtainOrigin(player, root);
            if (origin != null && OutermostObtainOrigin(origin).ParentReward is { } parentReward)
            {
                var originalSet = NativeRewardSets(run, player, root).SingleOrDefault(s => NativeContainsReward(s, parentReward))
                    ?? throw new InvalidOperationException("OperationObtainParentRewardSetMissing");
                return CaptureObtainChild(player, root, catalog, CaptureRewardsSet(originalSet, player, catalog),
                    parentReward is CardReward ? PredictorObtainReturn.RewardAlternative : PredictorObtainReturn.Reward,
                    Id(parentReward), origin);
            }
            var currentSet = CurrentNativeRewards(run, player, root);
            if (currentSet != null && ObservedRewardSetObtainSource(currentSet) is { } rewardOrigin)
            {
                if (OutermostObtainOrigin(rewardOrigin).ParentReward is not { } originalReward)
                    throw new NotSupportedException("OperationGeneratedRewardParentContinuationNotCaptured");
                var originalSet = NativeRewardSets(run, player, root).SingleOrDefault(s => NativeContainsReward(s, originalReward));
                if (originalSet == null || ReferenceEquals(originalSet, currentSet))
                    throw new InvalidOperationException("OperationGeneratedRewardParentSetMissing");
                return CaptureObtainChild(player, root, catalog, CaptureRewardsSet(originalSet, player, catalog),
                    originalReward is CardReward ? PredictorObtainReturn.RewardAlternative : PredictorObtainReturn.Reward,
                    Id(originalReward), rewardOrigin, rewards);
            }
            return rewards;
        }
        throw new NotSupportedException("OperationSceneNotCaptured:" + run.CurrentRoom?.GetType().Name);
    }

    internal static PredictorOperationRewardsScene? CaptureRewards(RunState run, Player player, PredictorCatalog catalog, Node? root = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var set = CurrentNativeRewards(run, player, root);
        if (set == null) return null;
        return CaptureRewardsSet(set, player, catalog, root);
    }

    internal static RewardsSet? CurrentNativeRewards(RunState run, Player player, Node? root = null)
        => NativeRewardSets(run, player, root).LastOrDefault();

    internal static ImmutableArray<RewardsSet> NativeRewardSets(RunState run, Player player, Node? root = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var sets = ImmutableArray.CreateBuilder<RewardsSet>();
        var synchronizer = RunManager.Instance.RewardsSetSynchronizer;
        if (synchronizer != null)
        {
            var players = Read<IList>(synchronizer, "_rewardStates");
            int slot = run.Players.ToList().IndexOf(player);
            if (slot >= 0 && slot < players.Count)
            {
                var stack = Read<IList>(players[slot]!, "rewardsStack");
                foreach (var state in stack) sets.Add(Read<RewardsSet>(state!, "set"));
            }
        }
        if (sets.Count == 0)
            foreach (var screen in Nodes<NRewardsScreen>(root)) sets.Add(Read<RewardsSet>(screen, "_rewardsSet"));
        return sets.ToImmutable();
    }

    internal static PredictorOperationRewardsScene CaptureRewardsSet(RewardsSet set, Player player, PredictorCatalog catalog, Node? root = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (set.Player != player) throw new InvalidOperationException("OperationRewardOwnerMismatch");
        var groups = ImmutableArray.CreateBuilder<PredictorOperationLinkedRewardGroup>();
        var rows = ImmutableArray.CreateBuilder<PredictorReward>();
        void Add(Reward reward)
        {
            if (reward is LinkedRewardSet linked)
            {
                groups.Add(new(Id(linked), linked.Rewards.Select(Id).ToImmutableArray()));
                foreach (var child in linked.Rewards) Add(child);
            }
            else
            {
                var row = CaptureReward(reward, catalog);
                rows.Add(row.Kind == PredictorRewardKind.Opaque ? row with { CanSkip = !set.DisallowSkipping } : row);
            }
        }
        foreach (var reward in set.Rewards) Add(reward);
        var rewards = rows.ToImmutable();
        var nativeRewards = NativeRewardLeaves(set.Rewards).ToArray();
        RequireStableRewardSelection(nativeRewards, root ?? MegaCrit.Sts2.Core.Nodes.NRun.Instance);
        var selected = nativeRewards.OfType<CardReward>().Where(r =>
            ReadNullable(r, "_currentlyShownScreen") is NCardRewardSelectionScreen s && s.IsInsideTree())
            .Select(r => (long?)Id(r)).SingleOrDefault();
        var eventRngs = nativeRewards.OfType<CardReward>().Where(r => r.CanReroll)
            .Select(r => Read<CardCreationOptions>(r, "<RerollOptions>k__BackingField").RngOverride)
            .Where(r => r != null && !ReferenceEquals(r, player.PlayerRng.Rewards)).Distinct().ToArray();
        if (eventRngs.Length > 1) throw new NotSupportedException("OperationMultipleRewardRngOverridesNotCaptured");
        PredictorOperationEventRng? eventRng = null;
        if (eventRngs.FirstOrDefault() is { } rng)
        {
            var value = rng.ToSerializable();
            eventRng = new(value.state0, value.state1, value.state2, value.state3, value.counter);
        }
        return new(rewards, selected) { EventRng = eventRng, LinkedGroups = groups.ToImmutable(),
            PendingRemoval = CaptureRewardRemoval(set, root ?? MegaCrit.Sts2.Core.Nodes.NRun.Instance) };
    }

    internal static IEnumerable<Reward> NativeRewardLeaves(IEnumerable<Reward> rewards)
    {
        foreach (var reward in rewards)
            if (reward is LinkedRewardSet linked)
            { foreach (var child in NativeRewardLeaves(linked.Rewards)) yield return child; }
            else yield return reward;
    }

    internal static bool NativeContainsReward(RewardsSet set, Reward reward) =>
        set.Rewards.Contains(reward) || NativeRewardLeaves(set.Rewards).Contains(reward);

    internal static PredictorReward CaptureReward(Reward reward, PredictorCatalog catalog)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var status = reward.SuccessfullySelected ? PredictorRewardStatus.Taken : PredictorRewardStatus.Open;
        var origin = reward.Player.RunState.CurrentRoom is CombatRoom ? PredictorRewardOrigin.Combat : PredictorRewardOrigin.Event;
        PredictorReward Base(PredictorRewardKind kind, ModelKey key = default, ImmutableArray<PredictorCard> cards = default) =>
            new(Id(reward), kind, origin, key, cards.IsDefault ? [] : cards, Status: status);
        return reward switch
        {
            CardReward card => Base(PredictorRewardKind.Card, cards: card.Cards.Select(c => Card(c, catalog)).ToImmutableArray()) with
            {
                SubscriptionOrder = Id(card), CanSkip = card.CanSkip, CanReroll = card.CanReroll,
                HasRerolled = Read<bool>(card, "_hasBeenRerolled"),
                ManuallySetCards = Read<bool>(card, "_cardsWereManuallySet"),
                CardSelectionEntered = ReadNullable(card, "_currentlyShownScreen") is NCardRewardSelectionScreen s && s.IsInsideTree(),
                RerollRecipe = PredictorOperationRewardRecipeCapture.Capture(card),
                RelicSubscriptionActive = CardRewardSubscriptions(card.Player).Contains(card)
            },
            RelicReward relic when relic.Relic != null => Base(PredictorRewardKind.Relic, Key(relic.Relic)) with
                { ContentInstanceId = Id(relic.Relic), IsWaxRelic = relic.Relic.IsWax },
            PotionReward potion when potion.Potion != null => Base(PredictorRewardKind.Potion, Key(potion.Potion)) with
                { ContentInstanceId = Id(potion.Potion) },
            GoldReward gold when gold.IsPopulated => Base(PredictorRewardKind.Gold) with
                { GoldAmount = gold.Amount, WasGoldStolenBack = Read<bool>(gold, "_wasGoldStolenBack") },
            SpecialCardReward special => Base(PredictorRewardKind.SpecialCard, cards: [Card(Read<CardModel>(special, "_card"), catalog)]) with
                { PreserveCardInstance = true },
            CardRemovalReward => Base(PredictorRewardKind.RemoveCard),
            _ when reward.GetType().Assembly != typeof(Reward).Assembly => Base(PredictorRewardKind.Opaque) with
            {
                OpaqueSourceType = reward.GetType().FullName ?? reward.GetType().Name,
                OpaqueSourceAssembly = reward.GetType().Assembly.GetName().Name ?? ""
            },
            _ => throw new NotSupportedException("OperationRewardNotCaptured:" + reward.GetType().Name)
        };
    }

    internal static PredictorOperationShopScene CaptureShop(MerchantInventory inventory, PredictorCatalog catalog,
        bool inventoryOpen, Node? root = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var entries = ImmutableArray.CreateBuilder<PredictorShopEntry>();
        void Add(MerchantEntry native, PredictorShopEntryKind kind, AbstractModel? model, bool sale = false)
        {
            var card = model is CardModel c ? Card(c, catalog) : null;
            PredictorOperationShopRefill? refill = null;
            if (native is MerchantCardEntry)
            {
                var pool = Read<IEnumerable<CardModel>>(native, "_cardPool").Select((candidate, i) =>
                    new NeowEffectCardSnapshot(Key(candidate).Serialized, Key(candidate), i,
                        Enum.TryParse<EffectCardRarity>(candidate.Rarity.ToString(), out var rarity) ? rarity : EffectCardRarity.Special,
                        Enum.TryParse<EffectCardType>(candidate.Type.ToString(), out var type) ? type : EffectCardType.Other,
                        candidate.Rarity == CardRarity.Basic, candidate.Tags.Contains(CardTag.Strike), candidate.Tags.Contains(CardTag.Defend),
                        candidate.IsUpgradable, candidate.IsRemovable, candidate.CurrentUpgradeLevel, candidate.MaxUpgradeLevel,
                        PoolId: "captured-merchant-entry", SourceAssembly: candidate.GetType().Assembly.GetName().Name,
                        CanBeGeneratedByModifiers: candidate.CanBeGeneratedByModifiers,
                        IsMultiplayerOnly: candidate.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly,
                        CanBeGeneratedInCombat: candidate.CanBeGeneratedInCombat,
                        BehaviorMetadataExact: candidate.GetType().Assembly == typeof(CardModel).Assembly)).ToImmutableArray();
                var nativeType = ReadNullable(native, "_cardType");
                var nativeRarity = ReadNullable(native, "_cardRarity");
                refill = new(pool,
                    nativeType == null ? null : Enum.Parse<EffectCardType>(nativeType.ToString()!),
                    nativeRarity == null ? null : Enum.Parse<EffectCardRarity>(nativeRarity.ToString()!));
            }
            entries.Add(new PredictorShopEntry(Id(native), kind, model == null ? null : Key(model), card,
                Read<int>(native, "_cost"), sale, native.IsStocked, 0) { OperationRefill = refill });
        }
        foreach (var entry in inventory.CharacterCardEntries) Add(entry, PredictorShopEntryKind.CharacterCard, entry.CreationResult?.Card, entry.IsOnSale);
        foreach (var entry in inventory.ColorlessCardEntries) Add(entry, PredictorShopEntryKind.ColorlessCard, entry.CreationResult?.Card, entry.IsOnSale);
        foreach (var entry in inventory.RelicEntries) Add(entry, PredictorShopEntryKind.Relic, entry.Model);
        foreach (var entry in inventory.PotionEntries) Add(entry, PredictorShopEntryKind.Potion, entry.Model);
        if (inventory.CardRemovalEntry is { } removal) Add(removal, PredictorShopEntryKind.CardRemoval, null);
        return new(entries.ToImmutable(), inventoryOpen);
    }

    private static PredictorOperationScene CaptureLiveShop(RunState run, MerchantInventory inventory,
        PredictorCatalog catalog, bool inventoryOpen, Node? root)
    {
        var parent = CaptureShop(inventory, catalog, inventoryOpen, root);
        parent = parent with { IsMerchantRoom = run.CurrentRoom is MerchantRoom };
        if (CaptureShopRemoval(inventory, root) is { } removal) return parent with { PendingRemoval = removal };
        var receipt = CapturePaidPurchase(inventory);
        if (receipt == null)
        {
            if (inventory.RelicEntries.Any(e => e.Model != null && inventory.Player.Relics.Contains(e.Model)) ||
                Nodes<NCardGridSelectionScreen>(root).Any(n => n.IsVisibleInTree()) ||
                Nodes<NChooseACardSelectionScreen>(root).Any(n => n.IsVisibleInTree()) ||
                Nodes<NChooseABundleSelectionScreen>(root).Any(n => n.IsVisibleInTree()))
                throw new NotSupportedException("OperationShopParentObservationMissing");
            return parent;
        }
        parent = parent with { PendingPurchase = receipt };
        var origin = ObservedObtainOrigin(inventory.Player, root);
        var set = CurrentNativeRewards(run, inventory.Player, root);
        if (set != null && ObservedRewardSetObtainSource(set) is { } rewardOrigin)
            return CaptureObtainChild(inventory.Player, root, catalog, parent, PredictorObtainReturn.Shop,
                receipt.EntryId, rewardOrigin, CaptureRewardsSet(set, inventory.Player, catalog));
        if (origin != null)
            return CaptureObtainChild(inventory.Player, root, catalog, parent, PredictorObtainReturn.Shop, receipt.EntryId, origin);
        throw new InvalidOperationException("OperationShopSettling");
    }

    private static PredictorOperationScene CaptureRest(RunState run, Player player, RestSiteRoom rest,
        PredictorCatalog catalog, Node? root)
    {
        var options = rest.Options.Where(o => o.GetType().Assembly == typeof(CardModel).Assembly &&
            Enum.TryParse<PredictorRestAction>(o.OptionId, true, out _)).Select(o => Enum.Parse<PredictorRestAction>(o.OptionId, true)).ToImmutableArray();
        var opaque = rest.Options.Where(o => o.GetType().Assembly != typeof(CardModel).Assembly ||
            !Enum.TryParse<PredictorRestAction>(o.OptionId, true, out _))
            .Select(o => new PredictorOperationOpaqueAction(o.OptionId, o.GetType().FullName ?? o.GetType().Name,
                o.GetType().Assembly.GetName().Name ?? "")).ToImmutableArray();
        var parent = new PredictorOperationRestScene(options)
            { MendTargets = PredictorOperationMendCapture.CaptureTargets(run, player), OpaqueOptions = opaque };
        var restOrigin = ObservedRestOrigin(player);
        var selector = Nodes<NCardGridSelectionScreen>(root).LastOrDefault(n => n.IsVisibleInTree());
        var origin = ObservedObtainOrigin(player, root);
        var set = CurrentNativeRewards(run, player, root);
        if (restOrigin == null)
        {
            if (selector != null || origin != null || set != null)
                throw new NotSupportedException("OperationRestParentObservationMissing");
            return parent;
        }
        if (!Enum.TryParse<PredictorRestAction>(restOrigin.Action, true, out var action))
            throw new NotSupportedException("OperationRestActionNotModeled:" + restOrigin.Action);
        if (action == PredictorRestAction.Mend && MegaCrit.Sts2.Core.Nodes.Combat.NTargetManager.Instance?.IsInSelection == true)
            return parent with { MendTargetPending = true };
        if (action == PredictorRestAction.Heal && set != null)
        {
            var rewardOrigin = ObservedRewardSetObtainSource(set);
            var nested = origin ?? rewardOrigin;
            if (nested != null)
            {
                var sourceReward = OutermostObtainOrigin(nested).ParentReward
                    ?? throw new InvalidOperationException("OperationRestHealSourceRewardMissing");
                var originalSet = NativeRewardSets(run, player, root).SingleOrDefault(s => NativeContainsReward(s, sourceReward))
                    ?? throw new InvalidOperationException("OperationRestHealSourceSetMissing");
                var restParent = new PredictorOperationRestRewardsScene(parent, CaptureRewardsSet(originalSet, player, catalog));
                return CaptureObtainChild(player, root, catalog, restParent,
                    sourceReward is CardReward ? PredictorObtainReturn.RewardAlternative : PredictorObtainReturn.Reward,
                    Id(sourceReward), nested, origin == null ? CaptureRewardsSet(set, player, catalog) : null);
            }
            return new PredictorOperationRestRewardsScene(parent, CaptureRewardsSet(set, player, catalog));
        }
        if (action is PredictorRestAction.Dig or PredictorRestAction.Hatch)
        {
            long source = action == PredictorRestAction.Dig
                ? player.Relics.Where(r => r.Id.Entry == "SHOVEL" && !r.IsMelted).Select(Id).FirstOrDefault() : 0;
            if (set != null && ObservedRewardSetObtainSource(set) is { } rewardOrigin)
                return CaptureObtainChild(player, root, catalog, parent, PredictorObtainReturn.Rest, source,
                    rewardOrigin, CaptureRewardsSet(set, player, catalog), restAction: action);
            if (origin != null)
                return CaptureObtainChild(player, root, catalog, parent, PredictorObtainReturn.Rest, source, origin, restAction: action);
        }
        if (selector != null && action is PredictorRestAction.Smith or PredictorRestAction.Cook)
        {
            var prefs = Read<MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs>(selector, "_prefs");
            return parent with
            {
                Phase = action == PredictorRestAction.Smith ? PredictorPhase.SmithCards : PredictorPhase.RestCookCards,
                CandidateIds = Read<IReadOnlyList<CardModel>>(selector, "_cards").Select(Id).ToImmutableArray(),
                MinimumCount = prefs.MinSelect, MaximumCount = prefs.MaxSelect, Cancelable = prefs.Cancelable
            };
        }
        throw new InvalidOperationException("OperationRestSettling");
    }
}
