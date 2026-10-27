using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationHoverController
{
    private OperationDemand? _demand;
    private double _policyPoll;

    private static void ApplyOperationPolicy()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (_active != null && IsInstanceValid(_active) && _active._owner != null)
            _active.QueueRefresh();
        else if (_focusedOwner?.TryGetTarget(out var owner) == true && IsInstanceValid(owner))
            Focus(owner, _focusedVersion);
    }

    // Only public identities, inventory counts and observed source ownership.
    // No catalog/snapshot/RNG work is needed to suppress an unused prediction.
    private OperationDemand CaptureDemand() => CaptureDemand(RandomForeseerCompatibility.Capture());

    private OperationDemand CaptureDemand(RandomForeseerSnapshot rf)
    {
        var request = _request!;
        var player = LocalContext.GetMe(_run!)!;
        var preferences = InRunFeaturePreferences.CaptureOperationModes();
        var visible = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        bool Enabled(string id) => preferences.Get(id) != OperationPredictionMode.Off;
        bool Basic(string id) => OperationPredictionPolicy.Wants(preferences.Get(id), rf.IsLoaded, false);
        bool Extra(string id) => Enabled(id);
        void Show(string id, bool enabled) { if (enabled) visible.Add(id); }
        string primary = OperationPredictionPolicy.SourceFeature(request.Source);
        bool sourceEnabled = Enabled(primary);
        bool origin = false, basicRestock = false, alternative = false, sequence = false, progress = false;
        bool conditional = false, whetstone = false, transforms = false, may = false, showPrimary = false, chainOnly = false;
        bool transformSource = false, parentChain = false;
        int? publicProgress = null;
        bool chain = Extra("extra:obtain-chain");
        bool transformEffects = Extra("extra:transform-effects");
        bool eventContinuation = Extra("extra:event-continuation"), eventCombat = Extra("extra:event-combat");
        bool courier = player.Relics.Any(r => !r.IsMelted && r.Id.Entry == "THE_COURIER");
        string sequenceFeature = "extra:reroll-sequence";
        var potionDemand = new OperationPotionDemand(false, false, false);
        switch (request.Kind)
        {
            case Kind.Ancient:
            case Kind.Reward:
            {
                bool transformRoot = OperationPredictionPolicy.IsRelicTransform(request.Source.Entry);
                transformSource = transformRoot;
                bool composite = request.Source.Entry is "NEOWS_BONES" or "LARGE_CAPSULE";
                bool linked = request.Kind == Kind.Reward && HasLinkedRewardParent(player);
                parentChain = linked;
                bool transformExtra = transformRoot && transformEffects && HasTransformExtras(player);
                bool chainExtra = chain && (composite || linked);
                origin = sourceEnabled && (Basic(primary) || transformExtra || chainExtra);
                showPrimary = origin; may = origin; transforms = origin;
                chainOnly = origin && !Basic(primary) && chainExtra && !transformExtra;
                break;
            }
            case Kind.Event:
            {
                bool transformRoot = OperationPredictionPolicy.IsEventTransform(request.Source.Entry, request.Option);
                transformSource = transformRoot;
                bool transformExtra = transformRoot && transformEffects && HasTransformExtras(player);
                bool later = eventContinuation && HasEventContinuation(request.Source.Entry, request.Option);
                bool battle = eventCombat && OperationPreviewEligibility.RoyaltiesBattleOption(request.Source.Entry ?? "", request.Option);
                origin = sourceEnabled && (Basic(primary) || later || battle || transformExtra);
                showPrimary = origin; may = origin; transforms = origin;
                break;
            }
            case Kind.Shop:
            {
                bool relicSlot = _owner is NMerchantRelic;
                bool transformRoot = relicSlot && OperationPredictionPolicy.IsRelicTransform(request.Source.Entry);
                transformSource = transformRoot;
                bool composite = request.Source.Entry is "NEOWS_BONES" or "LARGE_CAPSULE";
                bool transformExtra = transformRoot && transformEffects && HasTransformExtras(player);
                origin = relicSlot && sourceEnabled && (Basic(primary) || composite && chain || transformExtra);
                chainOnly = origin && !Basic(primary) && composite && chain && !transformExtra;
                showPrimary = origin; transforms = origin;
                bool hasRestock = courier || request.Source.Entry == "THE_COURIER";
                basicRestock = hasRestock && Basic("shop:restock");
                whetstone = hasRestock && relicSlot && Extra("extra:restock-whetstone");
                may = origin || basicRestock || whetstone;
                break;
            }
            case Kind.Rest:
            {
                if (request.Rest == PredictorRestAction.Dig)
                {
                    origin = Basic("relic:SHOVEL");
                    showPrimary = origin;
                }
                else
                {
                    foreach (string entry in new[] { "DREAM_CATCHER", "TINY_MAILBOX" })
                    {
                        string id = "relic:" + entry;
                        bool wanted = player.Relics.Any(r => !r.IsMelted && r.Id.Entry == entry) && Basic(id);
                        Show(id, wanted); origin |= wanted;
                    }
                    showPrimary = origin;
                }
                may = origin; transforms = origin;
                break;
            }
            case Kind.RewardAlternative:
            {
                bool reroll = request.Option == "REROLL";
                primary = reroll ? "reward:reroll" : "reward:sacrifice";
                var set = PredictorOperationCapture.CurrentNativeRewards(_run!, player, NRun.Instance);
                var rewards = set == null ? [] : PredictorOperationCapture.NativeRewardLeaves(set.Rewards).OfType<CardReward>()
                    .Where(r => !r.SuccessfullySelected).ToArray();
                bool philosopher = _run!.CurrentRoom is EventRoom { LocalMutableEvent.Id.Entry: "COLORFUL_PHILOSOPHERS" };
                sequenceFeature = reroll ? philosopher ? "extra:philosopher-order" : "extra:reroll-sequence" : "extra:sacrifice-sequence";
                alternative = Basic(primary);
                sequence = Extra(sequenceFeature) && (reroll ? rewards.Count(r => r.CanReroll) > 1 : rewards.Length > 1);
                progress = !reroll && Extra("extra:sacrifice-sequence");
                bool shopParent = courier && set != null &&
                    PredictorOperationCapture.ObservedRewardSetObtainSource(set)?.Relic.Id.Entry == "ORRERY";
                conditional = shopParent && Extra("extra:conditional-restock");
                whetstone = shopParent && Extra("extra:restock-whetstone");
                // The first generated relic's visible effects remain part of
                // that requested sacrifice result, not a reason to run a hidden one.
                origin = !reroll && (alternative || sequence);
                transforms = origin;
                showPrimary = origin || progress;
                may = alternative || sequence || progress || conditional || whetstone;
                if (progress && !alternative && !sequence && !conditional && !whetstone)
                    publicProgress = player.Relics.OfType<PaelsWing>().Single(r => !r.IsMelted).RewardsSacrificed % 2;
                Show(primary, alternative);
                Show(sequenceFeature, sequence || progress);
                break;
            }
            case Kind.Potion:
            {
                if (!sourceEnabled) break;
                bool offered = request.PotionSource?.RewardId != null;
                bool entropic = request.Source.Entry == "ENTROPIC_BREW";
                int familyCount = player.PotionSlots.Count(p => p != null && (entropic
                    ? p.Id.Entry == "ENTROPIC_BREW"
                    : PredictorOperationPotionQueries.Supports(PredictorLiveRunCapture.Key(p))));
                bool multiple = offered ? familyCount > 0 && player.PotionSlots.Count > 1 : familyCount > 1;
                bool inventory = (offered || entropic) && Extra("extra:potion-inventory");
                bool full = offered && player.PotionSlots.All(p => p != null);
                bool order = multiple && Extra("extra:potion-order") && (!full || inventory);
                bool basic = Basic(primary);
                potionDemand = new(basic, order, inventory);
                may = basic || order || inventory; showPrimary = may;
                Show(primary, basic); Show("extra:potion-order", order); Show("extra:potion-inventory", inventory);
                break;
            }
            case Kind.CardTarget:
            {
                var obtain = PredictorOperationCapture.ObservedObtainOrigin(player, NGame.Instance);
                ModelKey source = obtain != null ? PredictorLiveRunCapture.Key(obtain.Relic) :
                    _run!.CurrentRoom is EventRoom room ? PredictorLiveRunCapture.Key(room.LocalMutableEvent) : default;
                primary = OperationPredictionPolicy.SourceFeature(source);
                bool selector = NOverlayStack.Instance?.Peek() is NDeckTransformSelectScreen;
                transformSource = selector;
                parentChain = obtain?.HasParentObtain == true;
                bool transformExtra = selector && transformEffects && HasTransformExtras(player);
                bool linked = chain && obtain?.HasParentObtain == true;
                showPrimary = !string.IsNullOrEmpty(primary) && Enabled(primary) && (Basic(primary) || transformExtra || linked);
                may = showPrimary; transforms = showPrimary;
                break;
            }
        }
        Show("shop:restock", basicRestock);
        Show("extra:conditional-restock", conditional);
        Show("extra:restock-whetstone", whetstone);
        var surface = new OperationSurfaceDemand(IncludeOriginEffect: origin, IncludeBasicRestock: basicRestock,
            IncludeRewardAlternative: alternative, IncludeCardTransforms: transforms, IncludeRewardSequence: sequence,
            IncludeSacrificeProgress: progress, IncludeConditionalRestock: conditional, IncludeRestockWhetstone: whetstone,
            RewardSequenceFeatureId: sequenceFeature, IncludeObtainContinuation: chain);
        return new(preferences, primary, may, surface, rf.Signature)
        {
            VisibleFeatures = visible.ToImmutable(), Potion = potionDemand, ShowPrimary = showPrimary,
            ChainOnly = chainOnly, IncludeChain = chain, IncludeTransformEffects = transformEffects,
            IncludeEventContinuation = eventContinuation, IncludeEventCombat = eventCombat,
            TransformSource = transformSource, HasParentChain = parentChain, PublicSacrificeProgress = publicProgress
        };
    }

    internal static bool HasTransformExtras(Player player) => player.Relics.Any(r => !r.IsMelted &&
        r.Id.Entry is "MOLTEN_EGG" or "TOXIC_EGG" or "FROZEN_EGG" or "FRESNEL_LENS" or "BING_BONG" or
        "LUCKY_FYSH" or "DARKSTONE_PERIAPT" or "BOOK_OF_FIVE_RINGS");

    private bool HasLinkedRewardParent(Player player)
    {
        var sets = PredictorOperationCapture.NativeRewardSets(_run!, player, NRun.Instance);
        return sets.Length > 1 || sets.Any(s => PredictorOperationCapture.ObservedRewardSetObtainSource(s) != null);
    }

    private static bool HasEventContinuation(string? source, string option) => source is
        "SLIPPERY_BRIDGE" or "TINKER_TIME" or "ROUND_TEA_PARTY" ||
        source == "WAR_HISTORIAN_REPY" && option == "SECOND_UNLOCK_CHEST";
}
