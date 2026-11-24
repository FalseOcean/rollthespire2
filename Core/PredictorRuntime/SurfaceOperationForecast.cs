using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Explicit requested obligations, frozen by the consumer before computation.
// No live settings or another mod's state is consulted by these forecasts.
internal sealed record OperationSurfaceForecastScope(bool IncludeOriginEffect = true, bool IncludeBasicRestock = true,
    bool IncludeRewardAlternative = true, bool IncludeCardTransforms = true,
    bool IncludeRewardSequence = true, bool IncludeSacrificeProgress = true,
    bool IncludeConditionalRestock = true, bool IncludeRestockWhetstone = true,
    bool IncludeObtainContinuation = true)
{
    internal static readonly OperationSurfaceForecastScope All = new();
    internal bool AnyRestock => IncludeBasicRestock || IncludeConditionalRestock || IncludeRestockWhetstone;
}

// Settles explicit operations and their bounded continuations from one captured
// offer. Keeps child facts and originating faults independent of presentation.
internal static class SurfaceOperationForecast
{
    internal static OperationForecast BuildShop(PredictorOperationSnapshot snapshot,long entryId,
        CancellationToken token=default,OperationSurfaceForecastScope? demand=null)
    {
        var worker = new Worker(snapshot);
        return worker.Finish(worker.BuildShop(snapshot,entryId,token,demand)) with {Activity=OperationForecastActivity.ShopPurchase};
    }
    internal static OperationForecast BuildRest(PredictorOperationSnapshot snapshot,PredictorRestAction action,
        CancellationToken token=default,OperationSurfaceForecastScope? demand=null)
    {
        var worker = new Worker(snapshot);
        return worker.Finish(worker.BuildRest(snapshot,action,token,demand)) with {Activity=OperationForecastActivity.RestAction,RestAction=action};
    }
    internal static OperationForecast BuildRewardAlternative(PredictorOperationSnapshot snapshot,long rewardId,
        string optionId,long? wingId,CancellationToken token=default,OperationSurfaceForecastScope? demand=null)
    {
        var worker = new Worker(snapshot);
        return worker.Finish(worker.BuildRewardAlternative(snapshot,rewardId,optionId,wingId,token,demand)) with {Activity=optionId=="REROLL"?OperationForecastActivity.Reroll:OperationForecastActivity.Sacrifice};
    }

    private sealed class Worker(PredictorOperationSnapshot captured)
    {
        private readonly OperationForecastExecution execution = new(captured);
        internal OperationForecast Finish(OperationForecast result) => result with
        { Fingerprint=captured.Fingerprint, Evaluations=result.Evaluations.AddRange(execution.Evaluations) };

        private readonly ModelKey Courier = new("RELIC", "THE_COURIER");
        private readonly ModelKey Whetstone = new("RELIC", "WHETSTONE");

        internal OperationForecast BuildShop(PredictorOperationSnapshot snapshot, long entryId,
            CancellationToken token = default, OperationSurfaceForecastScope? demand = null)
        {
            demand ??= OperationSurfaceForecastScope.All;
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
            var result = execution.Evaluate(inputs, token);
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
                result = execution.Evaluate(inputs.Add(new ChooseObtainDeckCards([target])), token);
                if (result.Disposition != PredictorInputResult.Accepted)
                    throw new InvalidOperationException("CourierMirrorContinuation:" + result.Disposition);
            }
            var preview = projectOrigin && !mirrorRestock
                ? RelicOperationForecast.Build(snapshot, inputs, source, token, demand.IncludeObtainContinuation) : Empty(source);
            // Preserve the purchased offer's kind separately from child relic facts.
            preview = preview with { PurchasedEntryKind = entry.Kind };
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
                    var settled = execution.Evaluate(continuation, token);
                    if (settled.Disposition != PredictorInputResult.Accepted || settled.Waiting.Phase != PredictorPhase.Shop)
                        throw new InvalidOperationException("CourierPurchaseContinuation:" + source.Entry + ":" + settled.Disposition + ":" + settled.Waiting.Phase);
                    return AppendShopRestock(snapshot, continuation, preview, settled, entryId, token, label,
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
            return AppendShopRestock(snapshot, inputs, preview, result, entryId, token, mirrorTargets: mirrorTargets, demand: demand);
        }

        private OperationForecast AppendShopRestock(PredictorOperationSnapshot snapshot, ImmutableArray<PredictorInput> inputs,
            OperationForecast preview, PredictorOperationResult result, long entryId,
            CancellationToken token, string? label = null, string? whetstoneKind = null, ImmutableArray<long> mirrorTargets = default,
            bool showPrice = true, OperationSurfaceForecastScope? demand = null, bool isConditional = false)
        {
            demand ??= OperationSurfaceForecastScope.All;
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
                ? new OperationOutcome(OperationOutcomeAction.Restock, [], [card], false)
                : new OperationOutcome(OperationOutcomeAction.Restock, [replacement.Key.Value], [], false);
            row = row with { Price = showPrice ? replacement.Cost : null };
            var section = new OperationOutcomeSource(Courier, [row], [])
            {
                Kind = label, Category = OperationOutcomeCategory.ShopRestock,
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
                        var settled = execution.Evaluate(effectInputs, token);
                        if (settled.Disposition != PredictorInputResult.Accepted || settled.Waiting.Phase != PredictorPhase.Shop)
                            throw new InvalidOperationException("CourierMirrorWhetstoneContinuation:" + settled.Disposition);
                        outcomes.Add(WhetstoneTargets(snapshot, settled, token));
                    }
                    if (outcomes.Skip(1).Any(cards => !cards.SequenceEqual(outcomes[0])))
                        whetstoneKind = "WhetstoneAfterMirrorChoice";
                }
                else outcomes.Add(WhetstoneTargets(snapshot, result, token));
                bool possible = outcomes.Skip(1).Any(cards => !cards.SequenceEqual(outcomes[0]));
                var targets = outcomes.SelectMany(cards => cards).Distinct().ToImmutableArray();
                var effect = new OperationOutcomeSource(Whetstone,
                    targets.IsEmpty ? [] : [new(OperationOutcomeAction.UpgradeCard, [], targets, possible)], [])
                {
                    Kind = whetstoneKind, Category = OperationOutcomeCategory.ShopRestock,
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

        private ImmutableArray<PredictorCard> WhetstoneTargets(PredictorOperationSnapshot original,
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
            var obtained = execution.Evaluate(hypothetical, [new TakeTreasureRelic(id)], token,
                "IfOfferedWhetstoneObtained;CurrentPurchaseSettled");
            if (obtained.Disposition != PredictorInputResult.Accepted)
                throw new InvalidOperationException("CourierWhetstoneObtain:" + obtained.Disposition);
            return obtained.RandomUpgrades.Where(upgrade => upgrade.Source == Whetstone)
                .SelectMany(upgrade => upgrade.After).ToImmutableArray();
        }

        internal OperationForecast BuildRest(PredictorOperationSnapshot snapshot, PredictorRestAction action,
            CancellationToken token = default, OperationSurfaceForecastScope? demand = null)
        {
            demand ??= OperationSurfaceForecastScope.All;
            if (action is not (PredictorRestAction.Heal or PredictorRestAction.Dig))
                throw new ArgumentException("RestPreviewActionHasNoRequestedDisplay", nameof(action));
            var source = new ModelKey("RELIC", action == PredictorRestAction.Dig ? "SHOVEL" : "DREAM_CATCHER");
            if (!demand.IncludeOriginEffect && (action != PredictorRestAction.Dig || !demand.IncludeCardTransforms))
                return Empty(source);
            ImmutableArray<PredictorInput> inputs = [new SelectRestAction(action)];
            var result = execution.Evaluate(inputs, token);
            if (result.Disposition != PredictorInputResult.Accepted)
                return Empty(source) with { Notices = ["RestAction:" + result.Disposition] };
            if (action == PredictorRestAction.Dig)
                return GeneratedRelic(snapshot, inputs, source, result, token, demand, demand.IncludeOriginEffect);

            var sections = ImmutableArray.CreateBuilder<OperationOutcomeSource>();
            var potions = result.Rewards.Where(r => r.Status == PredictorRewardStatus.Open && r.Kind == PredictorRewardKind.Potion)
                .Select(r => r.Key).ToImmutableArray();
            bool mailboxAdded = false;
            foreach (var reward in result.Rewards.Where(r => r.Status == PredictorRewardStatus.Open))
            {
                if (reward.Kind == PredictorRewardKind.Card)
                    sections.Add(new(new("RELIC", "DREAM_CATCHER"), [],
                        [new(reward.Id, reward.Cards, 1, 1, reward.CanSkip, false)])
                        { FeatureId = "relic:DREAM_CATCHER" });
                else if (reward.Kind == PredictorRewardKind.Potion && !mailboxAdded)
                {
                    sections.Add(new(new("RELIC", "TINY_MAILBOX"),
                        [new(OperationOutcomeAction.GeneratePotion, potions, [], false)], [])
                        { FeatureId = "relic:TINY_MAILBOX" });
                    mailboxAdded = true;
                }
            }
            if (mailboxAdded) source = new("RELIC", "TINY_MAILBOX");
            return Empty(source) with { Branches = [new([], sections.ToImmutable(), [], 1, true)] };
        }

        internal OperationForecast BuildRewardAlternative(PredictorOperationSnapshot snapshot, long rewardId,
            string optionId, long? wingId, CancellationToken token = default, OperationSurfaceForecastScope? demand = null)
        {
            demand ??= OperationSurfaceForecastScope.All;
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
            var before = execution.Evaluate(ImmutableArray<PredictorInput>.Empty, token);
            var current = before.Rewards.SingleOrDefault(r => r.Id == rewardId)
                ?? throw new ArgumentException("RewardAlternativeSourceMissing", nameof(rewardId));
            if (optionId == "REROLL")
            {
                var source = snapshot.State.Relics.FirstOrDefault(r => !r.Melted && r.Key.Entry == "DRIFTWOOD")?.Key
                    ?? current.Cards.First().Key;
                var result = execution.Evaluate([new RerollCardReward(rewardId)], token);
                if (result.Disposition != PredictorInputResult.Accepted)
                    return Empty(source) with { Notices = ["Reroll:" + result.Disposition] };
                var preview = Empty(source) with { OptionKey = optionId };
                if (demand.IncludeRewardSequence)
                    preview = WithCategory(RewardSequenceForecast.BuildReroll(snapshot, before, current, result, source, token),
                        OperationOutcomeCategory.RewardReroll, "extra:reroll-sequence");
                else if (demand.IncludeRewardAlternative)
                {
                    var reward = result.Rewards.Single(r => r.Id == rewardId);
                    var section = new OperationOutcomeSource(source, [],
                        [new(reward.Id, reward.Cards, 1, 1, reward.CanSkip, false)])
                    { Category = OperationOutcomeCategory.RewardReroll, FeatureId = "reward:reroll" };
                    preview = preview with { Branches = [new([], [section], [], 1, true)] };
                }
                return WithOrreryFinalActionRestock(snapshot, new RerollCardReward(rewardId), result, preview, token, demand);
            }
            if (optionId != "SACRIFICE" || wingId == null)
                throw new ArgumentException("RewardAlternativeNotRequested", nameof(optionId));
            var wing = snapshot.State.Relics.Single(r => r.Id == wingId && !r.Melted && r.Key.Entry == "PAELS_WING");
            ImmutableArray<PredictorInput> inputs = [new SacrificeCardReward(rewardId, wing.Id)];
            var sacrifice = execution.Evaluate(inputs, token);
            if (sacrifice.Disposition != PredictorInputResult.Accepted)
                return Empty(wing.Key) with { Notices = ["Sacrifice:" + sacrifice.Disposition] };
            var sacrificePreview = GeneratedRelic(snapshot, inputs, wing.Key, sacrifice, token, demand,
                demand.IncludeRewardAlternative || demand.IncludeRewardSequence, OperationOutcomeCategory.RewardSacrifice,
                demand.IncludeRewardSequence ? "extra:sacrifice-sequence" : "reward:sacrifice") with { OptionKey = optionId };
            if (demand.IncludeRewardSequence)
            {
                var sequence = WithCategory(RewardSequenceForecast.BuildSacrifice(snapshot, before, current, sacrifice, wing, token),
                    OperationOutcomeCategory.RewardSacrifice, "extra:sacrifice-sequence");
                foreach (var section in sequence.Branches.SelectMany(branch => branch.Sections))
                    sacrificePreview = Append(sacrificePreview, section);
                sacrificePreview = sacrificePreview with
                {
                    Faults = sacrificePreview.Faults.AddRange(sequence.Faults),
                    PendingReasons = sacrificePreview.PendingReasons.AddRange(sequence.PendingReasons),
                    Evaluations = sacrificePreview.Evaluations.AddRange(sequence.Evaluations),
                    CombatEvaluations = sacrificePreview.CombatEvaluations.AddRange(sequence.CombatEvaluations)
                };
            }
            if (demand.IncludeSacrificeProgress)
                sacrificePreview = sacrificePreview with
                { Notices = sacrificePreview.Notices.Add("RewardSacrificeProgress:" + wing.RewardsSacrificed % 2) };
            return WithOrreryFinalActionRestock(snapshot, inputs[0], sacrifice, sacrificePreview, token, demand);
        }

        private OperationForecast WithOrreryFinalActionRestock(PredictorOperationSnapshot snapshot,
            PredictorInput action, PredictorOperationResult result, OperationForecast preview, CancellationToken token,
            OperationSurfaceForecastScope demand)
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
                    settled = execution.Evaluate(inputs, token);
                }
                if (settled.Disposition != PredictorInputResult.Accepted || settled.Waiting.Phase != PredictorPhase.Shop)
                    throw new InvalidOperationException("CourierOrreryContinuation:" + settled.Disposition + ":" + settled.Waiting.Phase);
                var replacement = settled.ShopOffers.SingleOrDefault(offer => offer.EntryId == purchase.EntryId);
                if (replacement is not { Stocked: true, Kind: PredictorShopEntryKind.Relic, Key: { } key })
                    throw new InvalidOperationException("CourierOrreryRestockOfferMissing");
                if (result.State.Relics.Any(relic => !relic.Melted && relic.Key.Entry == (reroll ? "PAELS_WING" : "DRIFTWOOD")))
                    label += reroll ? "NoSacrifice" : "NoReroll";
                return AppendShopRestock(snapshot, inputs, preview, settled, purchase.EntryId, token, label,
                    "WhetstoneAfterSkippingCardRewards", showPrice: false, demand: demand, isConditional: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A continuation failure must not erase the independently known
                // action results. Existing display/diagnostics retain its cause.
                return preview with { Faults = preview.Faults.Add(new(label, inputs, ex)) };
            }
        }

        private OperationForecast GeneratedRelic(PredictorOperationSnapshot snapshot,
            ImmutableArray<PredictorInput> inputs, ModelKey source, PredictorOperationResult result, CancellationToken token,
            OperationSurfaceForecastScope demand, bool includeIdentity, OperationOutcomeCategory? category = null, string? featureId = null)
        {
            // The first newly acquired relic is the direct pull. Descendant relics
            // remain the obtain projection's responsibility and keep their source.
            var generated = result.State.Relics.FirstOrDefault(r => !snapshot.State.Relics.Any(old => old.Id == r.Id));
            if (generated == null) return Empty(source);
            var child = WantsRelicProjection(generated.Key, demand)
                ? RelicOperationForecast.Build(snapshot, inputs, generated.Key, token, demand.IncludeObtainContinuation) : Empty(source);
            if (!includeIdentity) return child with { Source = source };
            var first = new OperationOutcomeSource(source,
                [new(OperationOutcomeAction.ObtainRelic, [generated.Key], [], false)], [])
            { Category = category, FeatureId = featureId };
            return Prepend(child with { Source = source }, first);
        }

        private bool WantsRelicProjection(ModelKey source, OperationSurfaceForecastScope demand) =>
            demand.IncludeOriginEffect || demand.IncludeCardTransforms && source.Entry is
                "ASTROLABE" or "NEW_LEAF" or "PANDORAS_BOX" or "LEAFY_POULTICE" or "NEOWS_BONES" or "LARGE_CAPSULE";

        private OperationForecast WithCategory(OperationForecast preview, OperationOutcomeCategory category, string featureId) => preview with
        {
            Branches = preview.Branches.Select(branch => branch with
            { Sections = branch.Sections.Select(section => section with { Category = category, FeatureId = featureId }).ToImmutableArray() }).ToImmutableArray()
        };

        private OperationForecast Empty(ModelKey source) => new(source, [], [], false, [], []);

        private OperationForecast Append(OperationForecast preview, OperationOutcomeSource section) => preview with
        {
            Branches = preview.Branches.IsEmpty ? [new([], [section], [], 1, true)] :
                preview.Branches.Select(b => b with { Sections = b.Sections.Add(section) }).ToImmutableArray()
        };

        private OperationForecast Prepend(OperationForecast preview, OperationOutcomeSource section) => preview with
        {
            Branches = preview.Branches.IsEmpty ? [new([], [section], [], 1, true)] :
                preview.Branches.Select(b => b with { Sections = b.Sections.Insert(0, section) }).ToImmutableArray()
        };
    }
}
