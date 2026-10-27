using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunOperations;

// Captured on the UI thread with the request; the worker never re-reads live
// settings or another mod's state while projecting a detached operation.
internal sealed record OperationSurfaceDemand(bool IncludeOriginEffect = true, bool IncludeBasicRestock = true,
    bool IncludeRewardAlternative = true, bool IncludeCardTransforms = true,
    bool IncludeRewardSequence = true, bool IncludeSacrificeProgress = true,
    bool IncludeConditionalRestock = true, bool IncludeRestockWhetstone = true, string? RewardSequenceFeatureId = null,
    bool IncludeObtainContinuation = true)
{
    internal static readonly OperationSurfaceDemand All = new();
    internal bool AnyRestock => IncludeBasicRestock || IncludeConditionalRestock || IncludeRestockWhetstone;
}

// These projections describe one actual native offer. All settlement remains
// inside Operation; public offer contents are not repeated as predictions.
internal static class SurfaceOperationPreview
{
    private static readonly OperationDisplayStyle A1 = new(OperationTextMode.GroupRows, OperationCardLayout.Parallel);
    private static readonly OperationDisplayStyle D1 = new(OperationTextMode.None, OperationCardLayout.Parallel);
    private static readonly ModelKey Courier = new("RELIC", "THE_COURIER");
    private static readonly ModelKey Whetstone = new("RELIC", "WHETSTONE");

    internal static OperationPreview BuildShop(PredictorOperationSnapshot snapshot, long entryId,
        CancellationToken token = default, OperationSurfaceDemand? demand = null)
    {
        demand ??= OperationSurfaceDemand.All;
        if (snapshot.Scene is not PredictorOperationShopScene scene)
            throw new ArgumentException("ShopPreviewRequiresCurrentInventory", nameof(snapshot));
        var entry = scene.Entries.SingleOrDefault(e => e.Id == entryId)
            ?? throw new ArgumentException("ShopPreviewEntryMissing", nameof(entryId));
        var source = entry.Key ?? default;
        if (entry.Kind == PredictorShopEntryKind.CardRemoval) return Empty(source);
        bool projectOrigin = entry.Kind == PredictorShopEntryKind.Relic && WantsRelicProjection(source, demand);
        bool wantsRestock = demand.IncludeBasicRestock || demand.IncludeRestockWhetstone;
        if (!projectOrigin && !wantsRestock) return Empty(source);
        ImmutableArray<PredictorInput> inputs = [new BuyShopEntry(entryId, scene.Revision)];
        var result = PredictorOperation.Preview(snapshot, inputs, token);
        if (result.Disposition != PredictorInputResult.Accepted)
        {
            // An invalid purchase is not necessarily missing input. Use the
            // model's actual quoted offer (including discounts), not base cost.
            var offer = result.ShopOffers.SingleOrDefault(item => item.EntryId == entryId);
            return Empty(source) with { Notices = [offer is { Stocked: true, Affordable: false }
                ? "Purchase:InsufficientGold" : "Purchase:" + result.Disposition] };
        }

        bool courier = wantsRestock && scene.IsMerchantRoom && result.State.Relics.Any(r => !r.Melted && r.Key == Courier);
        bool mirrorRestock = courier && entry.Kind == PredictorShopEntryKind.Relic && source.Entry == "DOLLYS_MIRROR";
        ImmutableArray<long> mirrorTargets = [];
        if (mirrorRestock && result.Waiting.Phase == PredictorPhase.ObtainDeckCards)
        {
            // Vanilla copying (including deck-add hooks) does not change the
            // Rewards/Shops streams or relic bag eligibility. One legal target
            // therefore settles the shared replacement. It does not select for
            // the player; a Whetstone continuation below checks every copy.
            mirrorTargets = result.Waiting.Selection.CandidateCardIds;
            var target = mirrorTargets.First();
            result = PredictorOperation.Preview(snapshot, inputs.Add(new ChooseObtainDeckCards([target])), token);
            if (result.Disposition != PredictorInputResult.Accepted)
                throw new InvalidOperationException("CourierMirrorContinuation:" + result.Disposition);
        }
        var preview = projectOrigin && !mirrorRestock
            ? RelicOperationPreview.Build(snapshot, inputs, source, token, demand.IncludeObtainContinuation) : Empty(source);
        var style = entry.Kind is PredictorShopEntryKind.CharacterCard or PredictorShopEntryKind.ColorlessCard ? D1 : A1;
        // A shop slot owns purchase/restock, not the acquired relic's layout.
        // Preserve its source policy (e.g. Orrery A3 or Astrolabe D3).
        if (entry.Kind != PredictorShopEntryKind.Relic) preview = preview with { Style = style };
        if (!courier) return preview;
        if (source.Entry is "ORRERY" or "CAULDRON" && result.Waiting.Phase == PredictorPhase.Rewards)
        {
            // Ordinary taking/skipping leaves the replacement unchanged.
            // Cauldron's potions are already populated; vanilla procurement
            // only changes potion inventory (Belt Buckle's hook is combat-only).
            // This does not assume using a potion before finishing the purchase.
            bool reroll = result.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card &&
                reward.Status == PredictorRewardStatus.Open && reward.CanReroll);
            bool sacrifice = source.Entry == "ORRERY" && result.State.Relics.Any(relic => !relic.Melted && relic.Key.Entry == "PAELS_WING");
            string? label = reroll && sacrifice ? "OrreryPurchaseRestockNoAlternatives" :
                reroll ? "OrreryPurchaseRestockNoReroll" : sacrifice ? "OrreryPurchaseRestockNoSacrifice" : null;
            var continuation = inputs.Add(new LeaveRewards());
            try
            {
                var settled = PredictorOperation.Preview(snapshot, continuation, token);
                if (settled.Disposition != PredictorInputResult.Accepted || settled.Waiting.Phase != PredictorPhase.Shop)
                    throw new InvalidOperationException("CourierPurchaseContinuation:" + source.Entry + ":" + settled.Disposition + ":" + settled.Waiting.Phase);
                return AppendShopRestock(snapshot, continuation, preview, settled, entryId, style, token, label,
                    source.Entry == "ORRERY" ? "WhetstoneAfterSkippingCardRewards" : null, demand: demand);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return preview with { Faults = preview.Faults.Add(new("CourierPurchaseRestock:" + source.Entry, continuation, ex)) };
            }
        }
        // A relic transaction owns post-obtain restock. A paused child still
        // exposes the purchased slot; that public item is not its replacement.
        if (result.Waiting.Phase != PredictorPhase.Shop)
            return preview with { PendingReasons = preview.PendingReasons.Add(
                "CourierRestockAwaitingPlayerChoice:" + result.Waiting.Phase) };
        return AppendShopRestock(snapshot, inputs, preview, result, entryId, style, token, mirrorTargets: mirrorTargets, demand: demand);
    }

    private static OperationPreview AppendShopRestock(PredictorOperationSnapshot snapshot, ImmutableArray<PredictorInput> inputs,
        OperationPreview preview, PredictorOperationResult result, long entryId, OperationDisplayStyle style,
        CancellationToken token, string? label = null, string? whetstoneLabel = null, ImmutableArray<long> mirrorTargets = default,
        bool showPrice = true, OperationSurfaceDemand? demand = null, bool isConditional = false)
    {
        demand ??= OperationSurfaceDemand.All;
        if (!demand.AnyRestock) return preview;
        var replacement = result.ShopOffers.SingleOrDefault(o => o.EntryId == entryId);
        if (replacement is not { Stocked: true, Key: not null })
            throw new InvalidOperationException("CourierRestockOfferMissing");
        // Even when RF owns a simple replacement, its identity must be settled
        // once to discover a Whetstone supplement. Conditional replacements
        // also retain the offer as the context for their explicit premise.
        bool whetstone = demand.IncludeRestockWhetstone && replacement.Key == Whetstone;
        bool conditional = isConditional && demand.IncludeConditionalRestock;
        bool basic = !isConditional && demand.IncludeBasicRestock;
        if (!basic && !conditional && !whetstone) return preview;
        var row = replacement.Card is { } card
            ? new OperationPreviewRow(OperationPreviewAction.Restock, [], [card], false)
            : new OperationPreviewRow(OperationPreviewAction.Restock, [replacement.Key.Value], [], false);
        row = row with { Price = showPrice ? replacement.Cost : null };
        var section = new OperationPreviewSection(Courier, [row], [])
        {
            Style = style, Label = label, Category = OperationPredictionCategory.ShopRestock,
            FeatureId = conditional ? "extra:conditional-restock" : whetstone && !basic
                ? "extra:restock-whetstone" : "shop:restock"
        };
        bool hasOriginEffect = !preview.ContainedRelics.IsEmpty || !preview.CurseCandidates.IsEmpty ||
            preview.Branches.Any(branch => branch.Sections.Any(effect => !effect.Rows.IsEmpty || !effect.CardGroups.IsEmpty));
        preview = Append(preview with { Source = hasOriginEffect ? preview.Source : Courier }, section);
        if (!whetstone) return preview;
        var effectInputs = inputs;
        try
        {
            var outcomes = new List<ImmutableArray<PredictorCard>>();
            if (!mirrorTargets.IsDefaultOrEmpty)
            {
                // Copying is irrelevant to the replacement identity, but it is
                // relevant to Whetstone's future deck and stable shuffle. Never
                // reuse the arbitrary first copy as a promised upgrade target.
                foreach (long target in mirrorTargets)
                {
                    effectInputs = inputs.Add(new ChooseObtainDeckCards([target]));
                    var settled = PredictorOperation.Preview(snapshot, effectInputs, token);
                    if (settled.Disposition != PredictorInputResult.Accepted || settled.Waiting.Phase != PredictorPhase.Shop)
                        throw new InvalidOperationException("CourierMirrorWhetstoneContinuation:" + settled.Disposition);
                    outcomes.Add(WhetstoneTargets(snapshot, settled, token));
                }
                if (outcomes.Skip(1).Any(cards => !cards.SequenceEqual(outcomes[0])))
                    whetstoneLabel = "WhetstoneAfterMirrorChoice";
            }
            else outcomes.Add(WhetstoneTargets(snapshot, result, token));
            bool possible = outcomes.Skip(1).Any(cards => !cards.SequenceEqual(outcomes[0]));
            var targets = outcomes.SelectMany(cards => cards).Distinct().ToImmutableArray();
            var effect = new OperationPreviewSection(Whetstone,
                targets.IsEmpty ? [] : [new(OperationPreviewAction.UpgradeCard, [], targets, possible)], [])
            {
                Style = A1, Label = whetstoneLabel, Category = OperationPredictionCategory.ShopRestock,
                FeatureId = "extra:restock-whetstone",
                EmptyReason = targets.IsEmpty ? "WhetstoneNoUpgradeTargets" :
                    outcomes.Any(cards => cards.IsEmpty) ? "WhetstonePossibleNoUpgradeTargets" : null
            };
            return Append(preview, effect);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return preview with { Faults = preview.Faults.Add(new("CourierWhetstoneEffect", effectInputs, ex)) };
        }
    }

    private static ImmutableArray<PredictorCard> WhetstoneTargets(PredictorOperationSnapshot original,
        PredictorOperationResult settled, CancellationToken token)
    {
        // This is explicitly the effect *if obtained*, irrespective of whether
        // a second purchase is affordable. Restore the final detached state and
        // obtain the offered relic through the existing pure operation handler.
        // An already-open treasure offer adds no room generation or shop reroll.
        long id = settled.State.NextInstanceId;
        var hypothetical = original with
        {
            State = settled.State with { NextInstanceId = id + 1 },
            CurrentHp = settled.CurrentHp, MaximumHp = settled.MaximumHp,
            Scene = new PredictorOperationTreasureScene(new(id, Whetstone, settled.State.Position.Floor), true, true),
            SelectedCardIds = []
        };
        var obtained = PredictorOperation.Preview(hypothetical, new TakeTreasureRelic(id), token);
        if (obtained.Disposition != PredictorInputResult.Accepted)
            throw new InvalidOperationException("CourierWhetstoneObtain:" + obtained.Disposition);
        return obtained.RandomUpgrades.Where(upgrade => upgrade.Source == Whetstone)
            .SelectMany(upgrade => upgrade.After).ToImmutableArray();
    }

    internal static OperationPreview BuildRest(PredictorOperationSnapshot snapshot, PredictorRestAction action,
        CancellationToken token = default, OperationSurfaceDemand? demand = null)
    {
        demand ??= OperationSurfaceDemand.All;
        if (action is not (PredictorRestAction.Heal or PredictorRestAction.Dig))
            throw new ArgumentException("RestPreviewActionHasNoRequestedDisplay", nameof(action));
        var source = new ModelKey("RELIC", action == PredictorRestAction.Dig ? "SHOVEL" : "DREAM_CATCHER");
        if (!demand.IncludeOriginEffect && (action != PredictorRestAction.Dig || !demand.IncludeCardTransforms))
            return Empty(source);
        ImmutableArray<PredictorInput> inputs = [new SelectRestAction(action)];
        var result = PredictorOperation.Preview(snapshot, inputs, token);
        if (result.Disposition != PredictorInputResult.Accepted)
            return Empty(source) with { Notices = ["RestAction:" + result.Disposition] };
        if (action == PredictorRestAction.Dig)
            return GeneratedRelic(snapshot, inputs, source, result, token, demand, demand.IncludeOriginEffect);

        var sections = ImmutableArray.CreateBuilder<OperationPreviewSection>();
        var potions = result.Rewards.Where(r => r.Status == PredictorRewardStatus.Open && r.Kind == PredictorRewardKind.Potion)
            .Select(r => r.Key).ToImmutableArray();
        bool mailboxAdded = false;
        foreach (var reward in result.Rewards.Where(r => r.Status == PredictorRewardStatus.Open))
        {
            if (reward.Kind == PredictorRewardKind.Card)
                sections.Add(new(new("RELIC", "DREAM_CATCHER"), [],
                    [new(reward.Id, reward.Cards, 1, 1, reward.CanSkip, false)])
                    { Style = D1, FeatureId = "relic:DREAM_CATCHER" });
            else if (reward.Kind == PredictorRewardKind.Potion && !mailboxAdded)
            {
                sections.Add(new(new("RELIC", "TINY_MAILBOX"),
                    [new(OperationPreviewAction.GeneratePotion, potions, [], false)], [])
                    { Style = A1, FeatureId = "relic:TINY_MAILBOX" });
                mailboxAdded = true;
            }
        }
        if (mailboxAdded) source = new("RELIC", "TINY_MAILBOX");
        return Empty(source) with { Style = mailboxAdded ? A1 : D1,
            Branches = [new([], sections.ToImmutable(), [], 1, true)] };
    }

    internal static OperationPreview BuildRewardAlternative(PredictorOperationSnapshot snapshot, long rewardId,
        string optionId, long? wingId, CancellationToken token = default, OperationSurfaceDemand? demand = null)
    {
        demand ??= OperationSurfaceDemand.All;
        bool linkedRestock = demand.IncludeConditionalRestock || demand.IncludeRestockWhetstone;
        // A reroll has no separate relic/transform projection. If neither its
        // own result nor a linked restock is requested, do not execute it.
        if (optionId == "REROLL" && !demand.IncludeRewardAlternative && !demand.IncludeRewardSequence && !linkedRestock)
            return Empty(default) with { OptionKey = optionId };
        if (!demand.IncludeRewardAlternative && !demand.IncludeRewardSequence && !demand.IncludeSacrificeProgress &&
            !demand.IncludeOriginEffect && !demand.IncludeCardTransforms && !linkedRestock)
            return Empty(default) with { OptionKey = optionId };
        if (optionId == "SACRIFICE" && wingId != null && demand.IncludeSacrificeProgress &&
            !demand.IncludeRewardAlternative && !demand.IncludeRewardSequence && !demand.IncludeOriginEffect &&
            !demand.IncludeCardTransforms && !linkedRestock)
        {
            var progressWing = snapshot.State.Relics.Single(r => r.Id == wingId && !r.Melted && r.Key.Entry == "PAELS_WING");
            return Empty(progressWing.Key) with
            { OptionKey = optionId, Notices = ["RewardSacrificeProgress:" + progressWing.RewardsSacrificed % 2] };
        }
        var before = PredictorOperation.Preview(snapshot, ImmutableArray<PredictorInput>.Empty, token);
        var current = before.Rewards.SingleOrDefault(r => r.Id == rewardId)
            ?? throw new ArgumentException("RewardAlternativeSourceMissing", nameof(rewardId));
        if (optionId == "REROLL")
        {
            var source = snapshot.State.Relics.FirstOrDefault(r => !r.Melted && r.Key.Entry == "DRIFTWOOD")?.Key
                ?? current.Cards.First().Key;
            var result = PredictorOperation.Preview(snapshot, new RerollCardReward(rewardId), token);
            if (result.Disposition != PredictorInputResult.Accepted)
                return Empty(source) with { Notices = ["Reroll:" + result.Disposition] };
            var preview = Empty(source) with { OptionKey = optionId };
            if (demand.IncludeRewardSequence)
                preview = WithCategory(RewardSequencePreview.BuildReroll(snapshot, before, current, result, source, token),
                    OperationPredictionCategory.RewardReroll, demand.RewardSequenceFeatureId ??
                        (snapshot.Scene is PredictorOperationEventScene { Event.Entry: "COLORFUL_PHILOSOPHERS" }
                            ? "extra:philosopher-order" : "extra:reroll-sequence"));
            else if (demand.IncludeRewardAlternative)
            {
                var reward = result.Rewards.Single(r => r.Id == rewardId);
                var section = new OperationPreviewSection(source, [],
                    [new(reward.Id, reward.Cards, 1, 1, reward.CanSkip, false)])
                { Style = D1, Category = OperationPredictionCategory.RewardReroll, FeatureId = "reward:reroll" };
                preview = preview with { Style = D1, Branches = [new([], [section], [], 1, true)] };
            }
            return WithOrreryFinalActionRestock(snapshot, new RerollCardReward(rewardId), result, preview, token, demand);
        }
        if (optionId != "SACRIFICE" || wingId == null)
            throw new ArgumentException("RewardAlternativeNotRequested", nameof(optionId));
        var wing = snapshot.State.Relics.Single(r => r.Id == wingId && !r.Melted && r.Key.Entry == "PAELS_WING");
        ImmutableArray<PredictorInput> inputs = [new SacrificeCardReward(rewardId, wing.Id)];
        var sacrifice = PredictorOperation.Preview(snapshot, inputs, token);
        if (sacrifice.Disposition != PredictorInputResult.Accepted)
            return Empty(wing.Key) with { Notices = ["Sacrifice:" + sacrifice.Disposition] };
        var sacrificePreview = GeneratedRelic(snapshot, inputs, wing.Key, sacrifice, token, demand,
            demand.IncludeRewardAlternative || demand.IncludeRewardSequence, OperationPredictionCategory.RewardSacrifice,
            demand.IncludeRewardSequence ? "extra:sacrifice-sequence" : "reward:sacrifice") with { OptionKey = optionId };
        if (demand.IncludeRewardSequence)
        {
            var sequence = WithCategory(RewardSequencePreview.BuildSacrifice(snapshot, before, current, sacrifice, wing, token),
                OperationPredictionCategory.RewardSacrifice, "extra:sacrifice-sequence");
            foreach (var section in sequence.Branches.SelectMany(branch => branch.Sections))
                sacrificePreview = Append(sacrificePreview, section);
            sacrificePreview = sacrificePreview with
            {
                Faults = sacrificePreview.Faults.AddRange(sequence.Faults),
                PendingReasons = sacrificePreview.PendingReasons.AddRange(sequence.PendingReasons)
            };
        }
        if (demand.IncludeSacrificeProgress)
            sacrificePreview = sacrificePreview with
            { Notices = sacrificePreview.Notices.Add("RewardSacrificeProgress:" + wing.RewardsSacrificed % 2) };
        return WithOrreryFinalActionRestock(snapshot, inputs[0], sacrifice, sacrificePreview, token, demand);
    }

    private static OperationPreview WithOrreryFinalActionRestock(PredictorOperationSnapshot snapshot,
        PredictorInput action, PredictorOperationResult result, OperationPreview preview, CancellationToken token,
        OperationSurfaceDemand demand)
    {
        if (!(demand.IncludeConditionalRestock || demand.IncludeRestockWhetstone) || snapshot.Scene is not PredictorOperationObtainScene
            {
                Source.Entry: "ORRERY", Kind: PredictorOperationObtainChildKind.Rewards,
                Destination: PredictorObtainReturn.Shop,
                Parent: PredictorOperationShopScene { IsMerchantRoom: true, PendingPurchase: { } purchase }
            } || !result.State.Relics.Any(relic => !relic.Melted && relic.Key == Courier)) return preview;

        bool reroll = action is RerollCardReward;
        string label = reroll ? "OrreryFinalRerollRestock" : "OrreryFinalSacrificeRestock";
        // Ordinary taking/skipping is equivalent for restock. Vanilla Wing's
        // Common/Uncommon/Rare pickups complete automatically (including random
        // upgrades), so only the remaining Orrery rewards need finishing.
        // The final sacrifice may already have completed the paid shop parent.
        ImmutableArray<PredictorInput> inputs = [action];
        try
        {
            var settled = result;
            if (settled.Waiting.Phase != PredictorPhase.Shop)
            {
                if (settled.Waiting.Phase != PredictorPhase.Rewards)
                    throw new InvalidOperationException("CourierOrreryContinuationPending:" + settled.Waiting.Phase);
                // Reroll leaves the current picker open; sacrifice closes it.
                if (reroll) inputs = inputs.Add(new SkipCardReward());
                inputs = inputs.Add(new LeaveRewards());
                settled = PredictorOperation.Preview(snapshot, inputs, token);
            }
            if (settled.Disposition != PredictorInputResult.Accepted || settled.Waiting.Phase != PredictorPhase.Shop)
                throw new InvalidOperationException("CourierOrreryContinuation:" + settled.Disposition + ":" + settled.Waiting.Phase);
            var replacement = settled.ShopOffers.SingleOrDefault(offer => offer.EntryId == purchase.EntryId);
            if (replacement is not { Stocked: true, Kind: PredictorShopEntryKind.Relic, Key: { } key })
                throw new InvalidOperationException("CourierOrreryRestockOfferMissing");
            if (result.State.Relics.Any(relic => !relic.Melted && relic.Key.Entry == (reroll ? "PAELS_WING" : "DRIFTWOOD")))
                label += reroll ? "NoSacrifice" : "NoReroll";
            return AppendShopRestock(snapshot, inputs, preview, settled, purchase.EntryId, A1, token, label,
                "WhetstoneAfterSkippingCardRewards", showPrice: false, demand: demand, isConditional: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A continuation failure must not erase the independently known
            // action results. Existing display/diagnostics retain its cause.
            return preview with { Faults = preview.Faults.Add(new(label, inputs, ex)) };
        }
    }

    private static OperationPreview GeneratedRelic(PredictorOperationSnapshot snapshot,
        ImmutableArray<PredictorInput> inputs, ModelKey source, PredictorOperationResult result, CancellationToken token,
        OperationSurfaceDemand demand, bool includeIdentity, OperationPredictionCategory? category = null, string? featureId = null)
    {
        // The first newly acquired relic is the direct pull. Descendant relics
        // remain the obtain projection's responsibility and keep their source.
        var generated = result.State.Relics.FirstOrDefault(r => !snapshot.State.Relics.Any(old => old.Id == r.Id));
        if (generated == null) return Empty(source) with { Style = A1 };
        var child = WantsRelicProjection(generated.Key, demand)
            ? RelicOperationPreview.Build(snapshot, inputs, generated.Key, token, demand.IncludeObtainContinuation) : Empty(source);
        if (!includeIdentity) return child with { Source = source, Style = A1 };
        var first = new OperationPreviewSection(source,
            [new(OperationPreviewAction.ObtainRelic, [generated.Key], [], false)], [])
        { Style = A1, Category = category, FeatureId = featureId };
        return Prepend(child with { Source = source, Style = A1 }, first);
    }

    private static bool WantsRelicProjection(ModelKey source, OperationSurfaceDemand demand) =>
        demand.IncludeOriginEffect || demand.IncludeCardTransforms && source.Entry is
            "ASTROLABE" or "NEW_LEAF" or "PANDORAS_BOX" or "LEAFY_POULTICE" or "NEOWS_BONES" or "LARGE_CAPSULE";

    private static OperationPreview WithCategory(OperationPreview preview, OperationPredictionCategory category, string featureId) => preview with
    {
        Branches = preview.Branches.Select(branch => branch with
        { Sections = branch.Sections.Select(section => section with { Category = category, FeatureId = featureId }).ToImmutableArray() }).ToImmutableArray()
    };

    private static OperationPreview Empty(ModelKey source) => new(source, [], [], false, [], []);

    private static OperationPreview Append(OperationPreview preview, OperationPreviewSection section) => preview with
    {
        Branches = preview.Branches.IsEmpty ? [new([], [section], [], 1, true)] :
            preview.Branches.Select(b => b with { Sections = b.Sections.Add(section) }).ToImmutableArray()
    };

    private static OperationPreview Prepend(OperationPreview preview, OperationPreviewSection section) => preview with
    {
        Branches = preview.Branches.IsEmpty ? [new([], [section], [], 1, true)] :
            preview.Branches.Select(b => b with { Sections = b.Sections.Insert(0, section) }).ToImmutableArray()
    };
}
