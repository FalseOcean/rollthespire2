using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;

// The existing native source runner supplies the captured catalog. These are
// full detached route projections, including receipts and ordinary settlement.
internal static class PredictorDrawRewardContinuationChecks
{
    private static ModelKey R(string entry) => new("RELIC", entry);
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("DRAW_REWARDS " + message); }

    internal static void Run(PredictorRun captured)
    {
        TargetReceiptAndOptionalEdit(captured);
        TransformUsesItsActualCardPool(captured);
        RestTargetsKeepFollowingActionsAndRewards(captured);
        CompletedSiblingRerollsRemainAuthored(captured);
        CompletedNestedParentsKeepCurrentChildren(captured);
        Console.WriteLine("PREDICTOR_DRAW_REWARD_CONTINUATION_PASS optional target receipts; exact receipt address; editable targets; Tent suffix; Night Terrors independent rewards; concrete policy preserved");
    }

    internal static void RunDependencyBoundaries(PredictorRun captured)
    {
        RerollUsesNewOfferNicheScope(captured);
        PredictorCardRewardNicheKnowledgeChecks.RunRerollScopes(captured);
        PartialCancelPublishesActualDisposition(captured);
        HuntUpgradeDoesNotChangeDeclaredRewardCount(captured);
        Console.WriteLine("PREDICTOR_DRAW_DEPENDENCY_BOUNDARIES_PASS new reroll scope; real Niche guards; root/Orrery cancellation; declared Hunt count; real source membership uncertainty");
    }

    private static PredictorPlannedRewardChoice BoundChoice(PredictorCombatRewardKnowledge reward, int? index = null)
    {
        var binding = reward.OfferBinding ?? throw new InvalidOperationException("Expected identity-bound card offer");
        return new(binding.Value, reward.Occurrence, CardIndex: index)
            { UnresolvedEnchantmentIndices = binding.UnresolvedEnchantmentIndices };
    }

    // Controlled native definitions guarantee multiple eligible new targets;
    // these fixtures test dependency contracts, not natural seed reachability.
    private static PredictorRun EnchantableRewardSource(PredictorRun captured)
    {
        var catalog = captured.Context.Catalog!;
        var pool = catalog.CharacterCards.Where(card => card.IsUnlockedInCapturedPool && !card.IsMultiplayerOnly &&
            card.Rarity is (RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common or
                RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon or
                RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Rare) &&
            PredictorObtainSources.CanEnchant(captured.Context,
                PredictorCardChanges.Definition(captured.Context, card.CardKey).Prototype)).Take(3).ToImmutableArray();
        Check(pool.Length == 3, "dependency fixture requires three real enchantable reward definitions");
        return new(captured.Context with { Catalog = catalog with { CharacterCards = pool } }, captured.Checkpoints[0].State);
    }

    private static void RerollUsesNewOfferNicheScope(PredictorRun captured)
    {
        var source = EnchantableRewardSource(captured);
        PredictorRun Fixture(bool glitterFirst, bool otherInputUnknown = false)
        {
            string[] order = glitterFirst ? ["GLITTER", "WING_CHARM"] : ["WING_CHARM", "GLITTER"];
            return Route(source, rest: false, relic: "WHETSTONE", rerolls: true,
                held: order.Select((entry, index) => new PredictorRelic(10100 + index, R(entry), 1)).ToImmutableArray(),
                nicheUnknown: true, additionalCauses: otherInputUnknown ?
                [PredictorRouteCauseEvidence.CreateExplicit("ActualRewardHookStateUnavailable", "capture:reroll",
                    PredictorCombatDomain.CardRewardHookState, PredictorPendingReasonKind.MissingObservation,
                    "The reward listener state is unobserved.", ["relics.rewardHookState"])] : []);
        }
        var run = Fixture(glitterFirst: true);
        var baseline = Preview(run, []).Nodes[1];
        var offer = baseline.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        Check(offer.IsKnown && offer.Alternatives.Single(option => option.Key == "REROLL").IsKnown,
            "Glitter before Wing leaves no new eligible targets, so a real Whetstone Niche cutoff does not gate reroll");
        var reroll = BoundChoice(offer) with { Reroll = true };
        var result = Preview(run, [new(baseline.Position.NodeId) { RewardChoices = [reroll] }]).Nodes[1];
        Check(result.Advanced && result.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card) is
                { IsKnown: true, AppliedChoice.Reroll: true, Value.HasRerolled: true, DefaultSkipped: true },
            "the permitted reroll executes its real generation and retains the actual new offer");
        foreach (var guarded in new[] { Fixture(glitterFirst: false), Fixture(glitterFirst: true, otherInputUnknown: true) })
        {
            var node = Preview(guarded, []).Nodes[1];
            var partial = node.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
            Check(!partial.Alternatives.Single(option => option.Key == "REROLL").IsKnown,
                "real new-target Niche or listener-state uncertainty still gates reroll");
            var blocked = Preview(guarded, [new(node.Position.NodeId)
                { RewardChoices = [partial.OfferBinding != null ? BoundChoice(partial) with { Reroll = true } : reroll] }]).Nodes[1];
            Check(!blocked.Advanced && blocked.PendingCauses.Any(cause =>
                    cause.Code is "PlannedRewardAlternativeDependent" or "PlannedRewardPending") &&
                !blocked.Rewards.Any(reward => reward.AppliedChoice?.Reroll == true),
                "a detached reroll witness cannot publish or accept its private result when a real dependency is unresolved");
        }
        var consumed = Route(source, rest: false, relic: "GORGET", rerolls: true, nicheUnknown: true,
            held: [new(10100, R("SILKEN_TRESS"), 1), new(10101, R("WING_CHARM"), 1)]);
        var consumedNode = Preview(consumed, []).Nodes[1];
        var oldOffer = consumedNode.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        Check(oldOffer.IsKnown && !oldOffer.Alternatives.Single(option => option.Key == "REROLL").IsKnown,
            "an old zero-target scope from the now-consumed Silken Tress cannot certify the next reroll's new Wing targets");
    }

    private static void PartialCancelPublishesActualDisposition(PredictorRun captured)
    {
        var run = Route(EnchantableRewardSource(captured), rest: false, relic: "ORRERY", nicheUnknown: true,
            held: [new(10100, R("WING_CHARM"), 1)]);
        var baseline = Preview(run, []);
        foreach (bool root in new[] { true, false })
        {
            int nodeIndex = root ? 1 : 0;
            var node = baseline.Nodes[nodeIndex];
            var offer = node.Rewards.First(reward => reward.Kind == PredictorRewardKind.Card &&
                (root ? reward.CompletedEdit == null : reward.CompletedEdit?.Source == R("ORRERY")));
            Check(!offer.IsKnown && offer.DefaultSkipped && offer.OfferBinding?.Value.Status == PredictorRewardStatus.Skipped,
                "both root combat and completed Orrery publish actual default Skip on the masked offer");
            var take = BoundChoice(offer, 0);
            var plan = root ? new PredictorPlannedNode(node.Position.NodeId) { RewardChoices = [take] } :
                PredictorRun.ApplyCompletedRewardChoice(new(node.Position.NodeId), offer.CompletedEdit!, take);
            var taken = Preview(run, [plan]).Nodes[nodeIndex].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card &&
                reward.Occurrence == offer.Occurrence && reward.CompletedEdit?.Source == offer.CompletedEdit?.Source);
            Check(taken.AppliedChoice?.CardIndex == 0 && taken.OfferBinding?.Value.Status == PredictorRewardStatus.Taken,
                "the real Take receipt owns its actual status before cancellation");
            // This is the same leaf removal used by CancelRewardPlan; leave the
            // real treasure/obtain prefix intact for completed child rewards.
            var cancelled = root ? plan with { RewardChoices = [] } : plan with
            { Treasure = plan.Treasure! with { Actions = plan.Treasure!.Actions.Where(action =>
                action.Obtain?.Action != PredictorObtainChildAction.Reward).ToImmutableArray() } };
            var replay = Preview(run, [cancelled]);
            var restored = replay.Nodes[nodeIndex].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card &&
                reward.Occurrence == offer.Occurrence && reward.CompletedEdit?.Source == offer.CompletedEdit?.Source);
            Check(replay.Nodes[nodeIndex].Advanced && restored is { DefaultSkipped: true, AppliedChoice: null } &&
                restored.OfferBinding?.Value.Status == PredictorRewardStatus.Skipped && !restored.IsKnown &&
                !restored.OfferBinding!.UnresolvedEnchantmentIndices.IsEmpty,
                "cancellation replay proves Skip without exposing the discarded unknown enchantments");
            var changed = take with { ExpectedReward = take.ExpectedReward with { Cards = take.ExpectedReward.Cards.SetItem(0,
                take.ExpectedReward.Cards[0] with { FloorAdded = take.ExpectedReward.Cards[0].FloorAdded + 1 }) } };
            var stale = root ? plan with { RewardChoices = [changed] } :
                PredictorRun.ApplyCompletedRewardChoice(plan, offer.CompletedEdit!, changed);
            var rejected = Preview(run, [stale]).Nodes[nodeIndex];
            Check(!rejected.Advanced && rejected.PendingCauses.Any(cause =>
                    cause.Code is "PlannedRewardChanged" or "PlannedObtainRewardChanged"),
                "actual known offer drift remains rejected for root and Orrery rewards");
        }
    }

    private static void HuntUpgradeDoesNotChangeDeclaredRewardCount(PredictorRun captured)
    {
        var hunt = PredictorCardChanges.Definition(captured.Context, new("CARD", "THE_HUNT")).Prototype with
            { Id = 9000, FloorAdded = 1 };
        var entry = captured.Checkpoints[0].State;
        var source = new PredictorRun(captured.Context, entry with
            { Cards = entry.Cards.Add(hunt), Deck = entry.Deck.Add(hunt.Id), NextInstanceId = 10000 });
        var run = Route(source, rest: false, relic: "WHETSTONE", followingCombat: true);
        var ordinary = Preview(run, [], count: 3);
        Check(ordinary.Nodes.Length == 3 && ordinary.Nodes[1].Rewards.Count(reward => reward.Kind == PredictorRewardKind.Card) == 2 &&
            ordinary.Nodes.Skip(1).All(node => node.Advanced && node.Rewards.All(reward => reward.IsKnown)),
            "Hunt's default one declared fatal retains both exact offers and later ordinary rewards after Attack upgrade uncertainty");
        foreach (int triggers in new[] { 0, 1 })
        {
            var result = Preview(run, [new(ordinary.Nodes[1].Position.NodeId)
                { Combat = new() { CardTriggers = [new(hunt.Id, triggers)] } }], count: 3);
            Check(result.Nodes[1].Rewards.Count(reward => reward.Kind == PredictorRewardKind.Card) == 1 + triggers &&
                result.Nodes.Skip(1).All(node => node.Advanced && node.Rewards.All(reward => reward.IsKnown)),
                "per-node Hunt trigger declarations 0/1 survive pure upgrade uncertainty without contaminating later Rewards");
        }
        var catalog = captured.Context.Catalog!;
        var pool = catalog.TransformationPools.First(candidate => candidate.Cards.Contains(hunt.Key) &&
            catalog.CardDefinitions.Any(definition => definition.TransformationPool == candidate.Key &&
                definition.Rarity == "Basic" && (definition.IsStrike || definition.IsDefend)));
        var basic = catalog.CardDefinitions.First(definition => definition.TransformationPool == pool.Key &&
            definition.Rarity == "Basic" && (definition.IsStrike || definition.IsDefend)).Prototype with { Id = 9001, FloorAdded = 1 };
        var unknownSource = new PredictorRun(captured.Context, entry with
            { Cards = entry.Cards.Add(basic), Deck = [basic.Id], NextInstanceId = 10000 });
        var unknown = Preview(Route(unknownSource, rest: false, relic: "PANDORAS_BOX", followingCombat: true), [], count: 3);
        var frame = unknown.Nodes[1].Diagnostics!;
        Check(unknown.Nodes[1].PendingCauses.Any(cause => (cause.AffectedDomains & PredictorCombatDomain.ExtraCardRewards) != 0) &&
            frame.After.Select(frame.Snapshot.Fact).Single(fact => fact.PropertyKey == "rng.Rewards.calls").Status ==
                PredictorKnowledgeStatus.Unknown,
            "real possible Hunt membership still propagates extra-offer and following Rewards uncertainty");
    }

    // A bounded native pool makes every actual generated three-card offer
    // contain two non-block cards and one block card. Definitions, RNG, reward
    // population, live relic subscriptions and obtain handlers remain native.
    internal static void RunCardOperations(PredictorRun captured)
    {
        var catalog = captured.Context.Catalog!;
        var candidates = catalog.CharacterCards.Where(card => card.IsUnlockedInCapturedPool && !card.IsMultiplayerOnly &&
            card.Rarity is (RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common or
                RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon or
                RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Rare) &&
            PredictorObtainSources.CanEnchant(captured.Context,
                PredictorCardChanges.Definition(captured.Context, card.CardKey).Prototype)).ToArray();
        bool Block(RolltheSpire2.Core.Effects.Snapshots.NeowEffectCardSnapshot card) =>
            PredictorObtainSources.GainsBlockForFresnel(captured.Context,
                PredictorCardChanges.Definition(captured.Context, card.CardKey).Prototype);
        var pool = candidates.Where(card => !Block(card)).Take(2)
            .Concat(candidates.Where(Block).Take(1)).ToImmutableArray();
        Check(pool.Length == 3, "operation fixture requires real block/non-block card definitions");
        var source = new PredictorRun(captured.Context with
        {
            NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns),
            Catalog = catalog with { CharacterCards = pool }
        }, captured.Checkpoints[0].State);

        PredictorRun Fixture(bool knownOption = false, bool hooks = false, int sacrifices = -1,
            bool lensAfterWing = false, bool alternativePending = false, bool nicheKnown = false,
            string gift = "MANGO", int nicheOffset = 0)
        {
            var held = ImmutableArray.CreateBuilder<PredictorRelic>();
            void Add(string entry) => held.Add(new(10100 + held.Count, R(entry), 1));
            if (knownOption) Add("FRESNEL_LENS");
            Add("WING_CHARM");
            if (lensAfterWing) Add("FRESNEL_LENS");
            if (hooks)
            {
                Add("LUCKY_FYSH"); Add("BOOK_OF_FIVE_RINGS"); Add("BING_BONG");
                int book = held.ToImmutable().FindIndex(relic => relic.Key == R("BOOK_OF_FIVE_RINGS"));
                held[book] = held[book] with { BookOfFiveRingsCardsAdded = 4 };
            }
            if (sacrifices >= 0)
            {
                Add("PAELS_WING"); held[held.Count - 1] = held[held.Count - 1] with { RewardsSacrificed = sacrifices };
            }
            var shifted = source.Working;
            for (int offset = 0; offset < nicheOffset; offset++) shifted = shifted.Draw(PredictorStream.Niche, 0, 17).State;
            var routeSource = nicheOffset == 0 ? source : new PredictorRun(source.Context, shifted);
            var run = Route(routeSource, rest: false, relic: "ORRERY", nicheUnknown: !nicheKnown,
                held: held.ToImmutable(), hoarder: hooks, gift: gift, additionalCauses: alternativePending ?
                [PredictorRouteCauseEvidence.CreateExplicit("ActualPaelCounterUnavailable", "capture:card-operations",
                    PredictorCombatDomain.RewardAlternativeState, PredictorPendingReasonKind.MissingObservation,
                    "The actual Pael sacrifice counter is unobserved.", ["relics.PAELS_WING.rewardsSacrificed"])] : []);
            return run;
        }
        PredictorPlannedRewardChoice Choice(PredictorCombatRewardKnowledge offer, int? index = null, bool skip = false)
        {
            var binding = offer.OfferBinding!;
            Check(binding != null, "partially known offer must have its explicit semantic binding");
            return new(binding!.Value, offer.Occurrence, index, skip)
                { UnresolvedEnchantmentIndices = binding.UnresolvedEnchantmentIndices };
        }
        PredictorPlannedNode Edit(PredictorRouteNodeKnowledge node, PredictorCombatRewardKnowledge offer,
            PredictorPlannedRewardChoice choice) => PredictorRun.ApplyCompletedRewardChoice(
                new(node.Position.NodeId), offer.CompletedEdit!, choice);
        PredictorCombatRewardKnowledge Child(PredictorRouteNodeKnowledge node, int occurrence = 0) =>
            node.Rewards.Single(reward => reward.CompletedEdit?.Source == R("ORRERY") && reward.Occurrence == occurrence);
        PredictorKnowledgeFact Fact(PredictorRouteNodeKnowledge node, string key)
        {
            var frame = node.Diagnostics!;
            return frame.After.Select(frame.Snapshot.Fact).Single(fact => fact.PropertyKey == key);
        }
        int Number(PredictorRouteNodeKnowledge node, string key)
        {
            var fact = Fact(node, key);
            Check(fact.Status == PredictorKnowledgeStatus.Known, "expected independent known fact: " + key);
            return int.Parse(fact.DisplayValue!, System.Globalization.CultureInfo.InvariantCulture);
        }

        var run = Fixture(knownOption: true);
        var defaults = Preview(run, []);
        var first = defaults.Nodes[0];
        var offer = Child(first);
        Check(defaults.Nodes.Length == 2 && defaults.Nodes.All(node => node.Advanced) &&
            !offer.IsKnown && offer.CardOptions.Count(card => card.IsKnown) == 1 &&
            offer.CardOptions.Count(card => !card.IsKnown) == 2 && offer.DefaultSkipped &&
            offer.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault,
            "real Fresnel/Wing reward defaults to Skip while keeping its partially known payload and suffix");
        Check(offer.Alternatives.Single(alternative => alternative.Key == "SKIP").PendingCauses.IsEmpty,
            "Skip reads offer identity and skip availability, not discarded enchantments");
        var skipPlan = Edit(first, offer, Choice(offer, skip: true));
        var skipped = Preview(run, [skipPlan]);
        var explicitSkip = Child(skipped.Nodes[0]);
        Check(skipped.Nodes.All(node => node.Advanced) && explicitSkip.AppliedChoice?.Skip == true &&
            !explicitSkip.DefaultSkipped && explicitSkip.DispositionOrigin == PredictorKnowledgeOrigin.PlayerInput &&
            explicitSkip.OfferBinding?.Value.Status == PredictorRewardStatus.Skipped,
            "explicit Skip accepts the partial offer and preserves real player provenance separately from default Skip");
        int knownIndex = offer.CardOptions.FindIndex(card => card.IsKnown);
        Check(offer.CanChooseCard(knownIndex), "an unaffected card is selectable in a partial group");
        var takePlan = Edit(first, offer, Choice(offer, knownIndex));
        var taken = Preview(run, [takePlan]);
        var knownCard = offer.CardOptions[knownIndex].Value;
        Check(taken.Nodes[0].Advanced && taken.Nodes[0].Cards.Any(card => card.IsKnown &&
                card.Value.Key == knownCard.Key && card.Value.Enchantment == knownCard.Enchantment &&
                card.Value.FloorAdded == first.Position.Floor) &&
            Number(taken.Nodes[0], "deck.All.count") == run.Working.Deck.Length + 1,
            "taking the known Nimble card does not read either other offered card's unresolved enchantment");

        run.RestorePlannedNodes([takePlan]);
        var restored = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run),
            run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
        var rebound = Preview(restored, restored.PlannedNodes);
        Check(rebound.Nodes[0].Advanced && !rebound.Nodes[0].PendingCauses.Any(cause =>
                cause.Code is "PlannedRewardChanged" or "PlannedObtainRewardChanged") &&
            restored.PlannedNodes[0].Treasure!.Actions.Any(action =>
                action.Obtain?.Reward?.UnresolvedEnchantmentIndices.Length == 2),
            "nested partial offer binding and uncertainty mask survive persistence and regenerated scopes");
        var resolved = Preview(Fixture(knownOption: true, nicheKnown: true), [takePlan]).Nodes[0];
        Check(resolved.Advanced && !resolved.PendingCauses.Any(cause =>
                cause.Code is "PlannedRewardChanged" or "PlannedObtainRewardChanged"),
            "resolving the real Niche observation rebinds the saved partial intent without treating newly known enchantments as offer drift");
        var altered = Choice(offer, knownIndex);
        altered = altered with { ExpectedReward = altered.ExpectedReward with
            { Cards = altered.ExpectedReward.Cards.SetItem(knownIndex, knownCard with { FloorAdded = knownCard.FloorAdded + 1 }) } };
        var stale = Preview(run, [Edit(first, offer, altered)]).Nodes[0];
        Check(!stale.Advanced && stale.PendingCauses.Any(cause => cause.Code == "PlannedObtainRewardChanged"),
            "masking unresolved enchantments never permits changed known card metadata to rebind");
        Check(!Preview(run, [takePlan], PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0].Advanced,
            "authored simulation does not inherit drawing-only defaults for the other four Orrery opportunities");

        var hooks = Fixture(hooks: true);
        var hookBaseline = Preview(hooks, []).Nodes[0];
        var unknownOffer = Child(hookBaseline);
        Check(unknownOffer.CanChooseCard(0) && !unknownOffer.CardOptions[0].IsKnown,
            "identity-known own-unknown enchantment is an explicit supported drawing selection");
        var hooked = Preview(hooks, [Edit(hookBaseline, unknownOffer, Choice(unknownOffer, 0))]);
        var receipt = hooked.Nodes[0];
        Check(receipt.Advanced && Number(receipt, "deck.All.count") == hooks.Working.Deck.Length + 4 &&
            Number(receipt, "gold.balance") == Number(hookBaseline, "gold.balance") + 60 &&
            receipt.Inventory!.Relics.Single(relic => relic.Value.Key == R("BOOK_OF_FIVE_RINGS")) is
                { IsKnown: true, Value.BookOfFiveRingsCardsAdded: 8 } &&
            receipt.Cards.Count(card => card.Value.Key == unknownOffer.CardOptions[0].Value.Key && !card.IsKnown) == 4,
            "unknown payload still performs exactly four real Bing Bong/Hoarder additions, Lucky gold and Book receipts");
        Check(hooked.Nodes[1].Advanced && Fact(hooked.Nodes[1], "rng.Rewards.calls").Status == PredictorKnowledgeStatus.Known &&
            hooked.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Gold && reward.IsKnown),
            "own-unknown enchantment stays local to new card payloads while following Rewards continue");

        var lens = Fixture(lensAfterWing: true);
        var lensBaseline = Preview(lens, []).Nodes[0];
        var lensOffer = Child(lensBaseline);
        int blockIndex = lensOffer.CardOptions.FindIndex(card =>
            PredictorObtainSources.GainsBlockForFresnel(lens.Context, card.Value));
        var lensTaken = Preview(lens, [Edit(lensBaseline, lensOffer, Choice(lensOffer, blockIndex))]).Nodes[0];
        Check(lensTaken.Advanced && Number(lensTaken, "deck.All.count") == lens.Working.Deck.Length + 1 &&
            !lensTaken.Cards.Any(card => card.IsKnown && card.Value.FloorAdded == lensBaseline.Position.Floor &&
                card.Value.Key == lensOffer.CardOptions[blockIndex].Value.Key),
            "Fresnel after Wing preserves Swift-versus-Nimble receipt uncertainty instead of certifying the witness");

        foreach (int counter in new[] { 0, 1 })
        {
            var pael = Fixture(sacrifices: counter);
            var baseline = Preview(pael, []);
            var node = baseline.Nodes[1];
            var combatOffer = node.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
            long wing = pael.Working.Relics.Single(relic => relic.Key == R("PAELS_WING")).Id;
            Check(combatOffer.Alternatives.Single(alternative => alternative.Key == "SACRIFICE").PendingCauses.IsEmpty,
                "sacrifice does not require the old offered enchantment");
            var sacrifice = Choice(combatOffer) with { SacrificeRelicId = wing };
            var result = Preview(pael, [new(node.Position.NodeId) { RewardChoices = [sacrifice] }]).Nodes[1];
            var sourceReward = result.Rewards.Single(reward => reward.AppliedChoice?.SacrificeRelicId == wing);
            Check(result.Advanced && sourceReward.SacrificeCount == counter + 1 &&
                result.Inventory!.Relics.Single(relic => relic.Value.Id == wing).Value.RewardsSacrificed == counter + 1 &&
                (counter == 0 ? sourceReward.SacrificeRelic == null :
                    sourceReward.SacrificeRelic is { } gift && result.Inventory.Relics.Any(relic => relic.IsKnown && relic.Value.Key == gift)),
                "odd sacrifice advances the actual counter; even sacrifice performs the actual relic receipt");
        }
        var pendingPael = Fixture(sacrifices: 0, alternativePending: true);
        var pendingNode = Preview(pendingPael, []).Nodes[1];
        var pendingOffer = pendingNode.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        Check(!pendingOffer.Alternatives.Single(alternative => alternative.Key == "SACRIFICE").IsKnown &&
            pendingOffer.Alternatives.Single(alternative => alternative.Key == "SKIP").IsKnown,
            "actual unknown Pael counter gates only the alternative that reads it");
        long pendingWing = pendingPael.Working.Relics.Single(relic => relic.Key == R("PAELS_WING")).Id;
        var pendingSacrifice = Choice(pendingOffer) with { SacrificeRelicId = pendingWing };
        var blocked = Preview(pendingPael, [new(pendingNode.Position.NodeId) { RewardChoices = [pendingSacrifice] }]).Nodes[1];
        Check(!blocked.Advanced && blocked.Inventory!.Relics.Single(relic => relic.Value.Id == pendingWing).Value.RewardsSacrificed == 0,
            "removing the discarded enchantment requirement cannot bypass a real unresolved sacrifice input");
        // The gift is acquired while all five real Orrery subscriptions remain
        // open. Its clone-on-change listener can consume a different number of
        // instances depending on which original card actually received Swift.
        var late = Fixture(sacrifices: 1, gift: "FRESNEL_LENS");
        var lateInitial = Preview(late, []).Nodes[0];
        var lateParent = Child(lateInitial);
        long lateWing = late.Working.Relics.Single(relic => relic.Key == R("PAELS_WING")).Id;
        var latePlan = Edit(lateInitial, lateParent, Choice(lateParent) with { SacrificeRelicId = lateWing });
        var afterLens = Preview(late, [latePlan]).Nodes[0];
        var lateOffer = Child(afterLens, 1);
        int lateBlock = lateOffer.CardOptions.FindIndex(card =>
            PredictorObtainSources.GainsBlockForFresnel(late.Context, card.Value));
        var lateTake = PredictorRun.ApplyCompletedRewardChoice(latePlan, lateOffer.CompletedEdit!, Choice(lateOffer, lateBlock));
        var lateResult = Preview(late, [lateTake]);
        Check(lateResult.Nodes[0].Advanced && Number(lateResult.Nodes[0], "deck.All.count") == late.Working.Deck.Length + 1 &&
            Fact(lateResult.Nodes[0], "deck.All.candidates").Status != PredictorKnowledgeStatus.Known &&
            lateResult.Nodes[0].Cards.Where(card => card.Value.FloorAdded == afterLens.Position.Floor).All(card =>
                !card.IsKnown && card.PendingCauses.Any(cause => (cause.AffectedDomains & PredictorCombatDomain.InstanceIds) != 0)),
            "real late Fresnel receipt preserves known add count without promoting conditionally cloned offer instances to known deck targets");
        var lateIds = new HashSet<long>();
        var nextIds = new HashSet<long>();
        for (int offset = 0; offset < 8; offset++)
        {
            var witness = Fixture(sacrifices: 1, gift: "FRESNEL_LENS", nicheKnown: true, nicheOffset: offset);
            void Send(PredictorInput input) => Check(witness.Submit(witness.Request!, input) == PredictorInputResult.Accepted,
                "late subscription concrete witness input: " + input);
            Send(new OpenTreasure()); Send(new TakeTreasureRelic(witness.TreasureRelic!.Id));
            var firstOffer = witness.Rewards.First(reward => reward.Kind == PredictorRewardKind.Card);
            Send(new EnterCardSelection(firstOffer.Id)); Send(new SacrificeCardReward(firstOffer.Id, lateWing));
            var nextOffer = witness.Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card).ElementAt(1);
            var actualBlock = nextOffer.Cards.Single(card => PredictorObtainSources.GainsBlockForFresnel(witness.Context, card));
            nextIds.Add(witness.Working.NextInstanceId);
            lateIds.Add(actualBlock.Id);
            Send(new EnterCardSelection(nextOffer.Id)); Send(new TakeReward(nextOffer.Id, actualBlock.Id));
            Check(witness.Working.Deck.Contains(actualBlock.Id), "late listener receipt uses its actual post-clone offered instance");
        }
        Check(lateIds.Count > 1 && nextIds.Count > 1,
            "concrete native cursor witnesses demonstrate both late offered-instance and global allocation uncertainty");
        Console.WriteLine("PREDICTOR_CARD_OPERATIONS_PASS native partial offers; explicit/default Skip; independent/unknown Take; real copies/Lucky/Book; Fresnel order; Pael parity; nested save/rebind/stale offer; pure caller replay");
    }

    private static PredictorRun Route(PredictorRun captured, bool rest, bool health = false,
        string relic = "POMANDER", bool nicheUnknown = false, bool rerolls = false, bool nested = false,
        ImmutableArray<PredictorRelic> held = default, bool hoarder = false, string? gift = null,
        ImmutableArray<PredictorCombatPendingCause> additionalCauses = default, bool followingCombat = false)
    {
        const int act = 2;
        var types = new[] { MapPointType.Ancient, rest ? MapPointType.RestSite : MapPointType.Treasure,
            MapPointType.Monster }.Concat(followingCombat ? [MapPointType.Monster, MapPointType.Boss] :
                new[] { MapPointType.Boss }).ToArray();
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var source = captured.Checkpoints[0].State;
        var context = captured.Context with { Live = null, Operation = null,
            Modifiers = health ? [new(new("MODIFIER", "NIGHT_TERRORS"))] :
                hoarder ? [new(new("MODIFIER", "HOARDER"))] : [] };
        if (nicheUnknown) context = context with { Live = new(2, true,
            ImmutableArray.Create(PredictorRouteCauseEvidence.CreateExplicit("UnobservedNiche", "capture:draw", PredictorCombatDomain.NicheRng,
                PredictorPendingReasonKind.MissingObservation, "The incoming Niche cursor was not observed.", ["rng.Niche.state"]))
                .AddRange(additionalCauses.IsDefault ? [] : additionalCauses)) };
        var relicKeys = (rest ? new[] { R("MINIATURE_TENT"), R("DREAM_CATCHER"), R("TINY_MAILBOX") } : [])
            .Concat(rerolls ? [R("DRIFTWOOD")] : []).Concat(nested ? [R("PAELS_WING")] : []).ToArray();
        var state = source with
        {
            OpeningCompleted = true, Position = new(act, 1, "draw-rewards:entry"), GoldBudget = 1000,
            Relics = relicKeys.Select((key, index) => new PredictorRelic(10000 + index, key, 1)
                { RewardsSacrificed = key.Entry == "PAELS_WING" ? 1 : 0 })
                .Concat(held.IsDefault ? [] : held).ToImmutableArray(),
            NextInstanceId = 11000,
            Maps = [new(act, captured.RouteMap(act) with { Points = points }, [points[0].Position])],
            Visits = [new(act, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Ancient, null, 1)],
            VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
            SharedBag = new(false, [], [new("Rare", [R(relic), R(nested ? "ORRERY" : "GORGET")])], []),
            // Treasure draws the shared bag; Pael's reward draws the personal
            // bag. Keep the real producer's next gift explicit in this fixture.
            PersonalBag = gift != null || nested ? new(false, [], [new("Rare", [R(gift ?? "ORRERY")])], []) : source.PersonalBag,
            Acts = source.Acts.Select(a => a.Act == act ? a with
            { NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 } : a).ToImmutableArray()
        };
        var run = new PredictorRun(context, state);
        run.SetRoute(act, points.Select(point => point.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
    }

    private static void TransformUsesItsActualCardPool(PredictorRun captured)
    {
        var run = Route(captured, rest: false, relic: "NEW_LEAF", nicheUnknown: true);
        var result = Preview(run, []);
        Check(result.Nodes.Length == 2 && result.Nodes[0].Advanced && result.Nodes[1].Advanced &&
            result.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && !reward.CardOptions.IsEmpty),
            "an unknown Ironclad transform does not invent Hunt/Royalties from unrelated character pools");
        var frame = result.Nodes[1].Diagnostics!;
        Check(frame.After.Select(frame.Snapshot.Fact).Single(fact => fact.PropertyKey == "rng.Rewards.calls").Status == PredictorKnowledgeStatus.Known,
            "pool-scoped unknown transforms retain independent downstream Rewards progress");
    }

    private static PredictorRouteContinuation Preview(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.LiveMapDefaults, int count = 2)
    {
        string before = JsonSerializer.Serialize(run.Working), draft = JsonSerializer.Serialize(run.CaptureNodeDraft()),
            checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var result = run.PreviewRouteContinuation(plans, count, scope: PredictorRoutePreviewScope.AllModeledNodes, policy: policy);
        Check(before == JsonSerializer.Serialize(run.Working) && draft == JsonSerializer.Serialize(run.CaptureNodeDraft()) &&
            checkpoints == JsonSerializer.Serialize(run.Checkpoints),
            "drawing does not mutate the caller, RNG streams, checkpoints or accepted inputs");
        return result;
    }

    private static void TargetReceiptAndOptionalEdit(PredictorRun captured)
    {
        var run = Route(captured, rest: false);
        var unselected = Preview(run, []);
        Check(unselected.Nodes.Length == 2 && unselected.Nodes[0].Advanced && unselected.Nodes[1].Advanced &&
            unselected.Nodes[0].Treasure is { RelicTaken: true } &&
            unselected.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Gold && reward.IsKnown),
            "a target-only relic is really acquired without blocking later independent rewards");
        var optional = unselected.Nodes[0].OptionalObtainInputs.Single(input => input.Choice.Source == R("POMANDER"));
        Check(optional.Choice.Phase == PredictorPhase.ObtainDeckCards && optional.Choice.DeckCards.Length > 1,
            "an omitted target remains available as an optional real card picker");
        var card = optional.Choice.DeckCards.First(candidate => candidate.IsKnown).Value;
        var input = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseDeckCards, optional.Choice.Source)
        { DeckCards = [new(card.Id, card.Key, card.FloorAdded)] };
        var edited = PredictorRun.ApplyOptionalObtainInput(new(run.Working.Position.NodeId), optional, input);
        var selected = Preview(run, [edited]);
        Check(selected.Nodes[0].Cards.Any(candidate => candidate.IsKnown && candidate.Value.Id == card.Id &&
                candidate.Value.UpgradeLevel == card.UpgradeLevel + 1) &&
            !selected.Nodes[0].PendingCauses.Any(cause => cause.Code == "RouteObtainTargetPending"),
            "adding an optional target replays the actual upgrade and improves its card result");

        // Repeated same-source intent has distinct receipt addresses. It must
        // neither overwrite the first entry nor bind to its actual occurrence.
        var second = optional with { Address = optional.Address with
            { Occurrence = optional.Address.Occurrence + 1, SourceId = optional.Address.SourceId + 1 } };
        var twice = PredictorRun.ApplyOptionalObtainInput(edited, second, input);
        Check(twice.ObtainInputs.Length == 2 && twice.ObtainInputs[0].Address == optional.Address,
            "same-model receipt addresses remain distinct optional intentions");
        var changedPrefix = Preview(run, [new(run.Working.Position.NodeId) { ObtainInputs = [twice.ObtainInputs[1]] }]);
        Check(changedPrefix.Nodes[0].Advanced && changedPrefix.Nodes[0].PendingCauses.Any(cause =>
                cause.Code == "PlannedOptionalObtainSourcePending") &&
            changedPrefix.Nodes[0].Cards.Any(candidate => candidate.Value.Id == card.Id && !candidate.IsKnown),
            "a changed receipt context is retained as unbound instead of applying to the first same-source receipt");

        var take = new PredictorPlannedNode(run.Working.Position.NodeId) { Treasure = new()
        { Actions = [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = R("POMANDER") }] } };
        Check(!Preview(run, [take], PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0].Advanced,
            "the full authored simulator still requires the native card target");
        var legacy = take with { Treasure = take.Treasure! with { Actions = take.Treasure!.Actions.Add(
            new(PredictorTreasureActionKind.Obtain) { Obtain = input }) } };
        var oldShape = Preview(run, [legacy]);
        Check(oldShape.Nodes[0].Advanced && oldShape.Nodes[0].Cards.Any(candidate => candidate.Value.Id == card.Id &&
                candidate.Value.UpgradeLevel == card.UpgradeLevel && !candidate.IsKnown) && legacy.Treasure!.Actions[^1].Obtain == input,
            "legacy parent-target intent is preserved without borrowing a same-model target for an unaddressed receipt");
    }

    private static void RestTargetsKeepFollowingActionsAndRewards(PredictorRun captured)
    {
        var run = Route(captured, rest: true, health: true);
        var plan = new PredictorPlannedNode(run.Working.Position.NodeId)
        { RestActions = [new(PredictorRestAction.Smith), new(PredictorRestAction.Heal)], LeaveRest = true };
        var result = Preview(run, [plan]);
        Check(result.Nodes.Length == 2 && result.Nodes[0].Advanced && result.Nodes[0].AppliedRestActions == 2 &&
            result.Nodes[0].PendingCauses.Any(cause => cause.Code == "PlannedRestTargetPending") &&
            result.Nodes[0].PendingCauses.Where(cause => cause.Code == "RestMaximumHp").All(cause =>
                cause.AffectedDomains == PredictorCombatDomain.Health),
            "an unspecified Smith target retains Tent's selected Heal suffix without needing maximum HP");
        Check(result.Nodes[0].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && !reward.CardOptions.IsEmpty) &&
            result.Nodes[0].Rewards.Count(reward => reward.Kind == PredictorRewardKind.Potion && reward.IsKnown) == 2 &&
            result.Nodes[1].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Gold && reward.IsKnown),
            "Night Terrors health uncertainty does not suppress Dream Catcher, Mailbox or next-combat rewards");
        Check(!Preview(run, [plan], PredictorRoutePreviewPolicy.AuthoredChoices).Nodes[0].Advanced,
            "target omission remains drawing-only");
    }

    private static void CompletedSiblingRerollsRemainAuthored(PredictorRun captured)
    {
        var run = Route(captured, rest: false, relic: "ORRERY", rerolls: true);
        var baseline = Preview(run, []);
        var offers = baseline.Nodes[0].Rewards.Where(reward => reward.CompletedEdit != null &&
            reward.Kind == PredictorRewardKind.Card && reward.IsKnown).ToArray();
        Check(offers.Length == 5, "native Orrery obtain provides five editable completed opportunities");
        var first = new PredictorPlannedRewardChoice(offers[0].Value!, offers[0].Occurrence) { Reroll = true };
        var second = new PredictorPlannedRewardChoice(offers[1].Value!, offers[1].Occurrence) { Reroll = true };
        var firstPlan = PredictorRun.ApplyCompletedRewardChoice(new(run.Working.Position.NodeId), offers[0].CompletedEdit!, first);
        var firstResult = Preview(run, [firstPlan]);
        var firstReplacement = firstResult.Nodes[0].Rewards.Single(reward => reward.CompletedEdit != null && reward.Occurrence == offers[0].Occurrence);
        // Both UI surfaces can still hold the original observation receipt.
        // The current plan owns newer siblings and nested parent children.
        var both = PredictorRun.ApplyCompletedRewardChoice(firstPlan, offers[1].CompletedEdit!, second);
        var rerolled = Preview(run, [both]);
        Check(rerolled.Nodes[0].Advanced && rerolled.Nodes[0].Rewards.Count(reward => reward.CompletedEdit != null &&
            reward.AppliedChoice?.Reroll == true && reward.Value?.HasRerolled == true && reward.DefaultSkipped) == 2,
            "a stale completed receipt preserves the first sibling reroll while authoring the second");
        var selected = PredictorRun.ApplyCompletedRewardChoice(both, offers[0].CompletedEdit!, first with
            { ExpectedRerolledReward = firstReplacement.Value, CardIndex = 0 });
        var result = Preview(run, [selected]);
        Check(result.Nodes[0].Advanced && result.Nodes[1].Advanced &&
            result.Nodes[0].Rewards.Any(reward => reward.CompletedEdit != null && reward.Occurrence == offers[0].Occurrence &&
                reward.AppliedChoice?.CardIndex == 0 && reward.Value?.Status == PredictorRewardStatus.Taken) &&
            result.Nodes[0].Rewards.Any(reward => reward.CompletedEdit != null && reward.Occurrence == offers[1].Occurrence &&
                reward.AppliedChoice?.Reroll == true && reward.Value?.HasRerolled == true),
            "editing the earlier rerolled group preserves its later sibling and actual following continuation");
        Check(selected.Treasure!.Actions.Count(action => action.Obtain?.Action == PredictorObtainChildAction.Reward) == 2 &&
            selected.Treasure.Actions.Any(action => action.Action == PredictorTreasureActionKind.TakeRelic) &&
            selected.Treasure.Actions.Any(action => action.Action == PredictorTreasureActionKind.Leave),
            "current sibling edits retain the actual automatic obtain and return prefix");
    }

    private static void CompletedNestedParentsKeepCurrentChildren(PredictorRun captured)
    {
        var run = Route(captured, rest: false, relic: "GORGET", nested: true);
        var combat = Preview(run, []).Nodes[1];
        var offered = combat.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown);
        var sacrifice = new PredictorPlannedRewardChoice(offered.Value!, offered.Occurrence)
            { SacrificeRelicId = run.Working.Relics.Single(relic => relic.Key.Entry == "PAELS_WING").Id };
        var plan = new PredictorPlannedNode(combat.Position.NodeId) { RewardChoices = [sacrifice] };
        var completed = Preview(run, [plan]).Nodes[1];
        var children = completed.Rewards.Where(reward => reward.CompletedEdit?.Source == R("ORRERY")).ToArray();
        Check(children.Length == 5 && children.All(reward => reward.CompletedEdit!.RewardParents.Length == 1),
            "real Pael sacrifice acquires Orrery and exposes its actual nested edit parent; " +
            "advanced=" + completed.Advanced + "; phase=" + completed.Phase +
            "; causes=" + string.Join(',', completed.PendingCauses.Select(cause => cause.Code)) +
            "; rewards=" + string.Join('|', completed.Rewards.Select(reward =>
                $"{reward.Kind}/{reward.Value?.Key}/{reward.Value?.Status}/sac={reward.SacrificeRelic}/" +
                $"edit={reward.CompletedEdit?.Source}/parents={reward.CompletedEdit?.RewardParents.Length}")));
        var first = new PredictorPlannedRewardChoice(children[0].Value!, children[0].Occurrence, CardIndex: 0);
        var second = new PredictorPlannedRewardChoice(children[1].Value!, children[1].Occurrence, CardIndex: 0);
        var firstPlan = PredictorRun.ApplyCompletedRewardChoice(plan, children[0].CompletedEdit!, first);
        var both = PredictorRun.ApplyCompletedRewardChoice(firstPlan, children[1].CompletedEdit!, second);
        var revised = PredictorRun.ApplyCompletedRewardChoice(both, children[0].CompletedEdit!, first with
            { CardIndex = null, Skip = true });
        var result = Preview(run, [revised]).Nodes[1];
        Check(result.Advanced && revised.RewardChoices.Single().Children.Length == 2 &&
            result.Rewards.Any(reward => reward.CompletedEdit != null && reward.Occurrence == children[0].Occurrence &&
                reward.AppliedChoice?.Skip == true && reward.Value?.Status == PredictorRewardStatus.Skipped) &&
            result.Rewards.Any(reward => reward.CompletedEdit != null && reward.Occurrence == children[1].Occurrence &&
                reward.AppliedChoice?.CardIndex == 0 && reward.Value?.Status == PredictorRewardStatus.Taken),
            "editing an earlier nested leaf from an old receipt keeps the current parent's later sibling input");
    }
}
