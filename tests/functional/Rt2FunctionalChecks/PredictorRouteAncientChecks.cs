using System.Collections.Immutable;
using System.Text.Json;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Search.FamilyExecution;

internal static class PredictorRouteAncientChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static ModelKey K(string entry) => new("RELIC", entry);
    private static void Select(PredictorRun run, PredictorInput input)
        => Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "Ancient fixture input rejected: " + input);
    private static PredictorCard Display(PredictorCard card) => card with { Id = 0 };
    private static PredictorPlannedCard Target(PredictorCard card) => new(card.Id, card.Key, card.FloorAdded);

    internal static void Run(PredictorRun captured)
    {
        OpeningAndBundles(captured);
        AncientAcquisitionOutcomes(captured);
        LiveBundleContinuation(captured);
        CardTransformAndHealthChildren(captured);
        ActualAncientOffers(captured);
        FutureOrobasHypotheses(captured);
        OptionalAncientCardsAndTargets(captured);
        OtherNativeAncientRepresentatives(captured);
        LiveObtainHealthEffects(captured);
        PaelsToothCountProjection(captured);
        InheritedDependencies(captured);
        CompletedPotionDeparture(captured);
        Console.WriteLine("RUNTIME_ROUTE_ANCIENT_PASS native catalogs/options; Neow manual selection; bundles/card/transform/health/reward children; active-prefix binding; DustyTome entry RNG; independent branches; inherited Deck/Gold/Niche masks; detached replay");
    }

    // Fixtures retain native catalogs, pools, source settlement and RNG. Only
    // geometry and an explicitly controlled entry option are replaced where a
    // particular child branch is needed; actual Act-2 offers are tested below.
    private static PredictorRun Opening(PredictorRun captured, string option)
    {
        var state = captured.Working with
        {
            OpeningCompleted = false, StartedWithNeow = true, Visits = [], Maps = [],
            OpeningOptions = new[] { K(option), K("GOLDEN_PEARL"), K("STONE_HUMIDIFIER") }.Distinct().ToImmutableArray(),
            Position = new(1, 0, "route-ancient:opening-entry")
        };
        return Geometry(captured.Context, state, captured.RouteMap(1), 1, [MapPointType.Ancient, MapPointType.Monster, MapPointType.Boss]);
    }

    private static PredictorRun Geometry(PredictorContext context, PredictorState state, MapPrediction original,
        int act, MapPointType[] types, bool alreadyVisitedFirst = false, bool enterFirst = true, int startFloor = 0)
    {
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var map = original with { Points = points };
        state = state with
        {
            Maps = [new(act, map, alreadyVisitedFirst ? [points[0].Position] : [])],
            Visits = alreadyVisitedFirst ? [new(act, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Ancient, null, 1)] : [],
            Position = new(act, startFloor + (alreadyVisitedFirst ? 1 : 0), "route-ancient:entry"),
            Acts = state.Acts.Select(a => a.Act == act ? a with
                { NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 } : a).ToImmutableArray()
        };
        var run = new PredictorRun(context, state);
        run.SetRoute(act, points.Select(p => p.Position).ToImmutableArray());
        if (enterFirst) run.AdvanceRoute();
        return run;
    }

    private static PredictorRun Ancient(PredictorRun captured, string ancient, int ordinal = 0, bool enterFirst = true)
    {
        string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, (ulong)ordinal);
        var context = captured.Context with
        {
            Seed = seed, AncientAuthority = Beta109WorldSnapshotProjector.ProjectForSeed(captured.Context.AncientAuthority!, seed)
        };
        int act = context.AncientAuthority!.AncientEventContexts.Where(value => value.AncientKey.Entry == ancient && value.Act >= 2)
            .Select(value => value.Act).Order().First();
        var state = captured.Working with { OpeningCompleted = true,
            Acts = captured.Working.Acts.Select(a => a.Act == act ? a with { Ancient = new("EVENT", ancient) } : a).ToImmutableArray() };
        return Geometry(context, state, captured.RouteMap(act), act, [MapPointType.Ancient, MapPointType.Monster, MapPointType.Boss], enterFirst: enterFirst);
    }

    private static PredictorRun FindAncient(PredictorRun captured, string ancient, string relic)
    {
        for (int ordinal = 0; ordinal < 128; ordinal++)
        {
            var run = Ancient(captured, ancient, ordinal);
            if (run.AncientOffers.Any(o => o.Relic.Entry == relic)) return run;
        }
        throw new InvalidOperationException($"Native {ancient} offer fixture not found: {relic}");
    }

    private static PredictorRouteContinuation Pure(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans, int count = 2,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.AuthoredChoices)
    {
        string before = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var request = run.Request;
        var result = run.PreviewRouteContinuation(plans, count, scope: PredictorRoutePreviewScope.AllModeledNodes, policy: policy);
        Check(before == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) && run.Request == request,
            "Ancient continuation must preserve actual state, checkpoints and request");
        Check(result.Nodes.SelectMany(n => n.Ancient?.Offers ?? []).Where(o => o.Value != null).All(o => o.Value!.Id == 0) &&
            result.Nodes.SelectMany(n => n.Ancient?.RequiredChoices ?? []).All(c => c.Cards.All(card => card.Id == 0) &&
                c.Bundles.All(b => b.All(card => card.Id == 0)) && c.Rewards.Where(r => r.Value != null).All(r => r.Value!.Id == 0)),
            "Ancient generated observations must not export actionable offer/card/reward IDs");
        return result;
    }

    private static PredictorPlannedNode Plan(PredictorRun run, string relic)
    {
        var knowledge = Pure(run, [], 1).Nodes[0].Ancient!;
        var offer = knowledge.Offers.Single(o => o.Value?.Relic.Entry == relic).Value!;
        return new(run.Working.Position.NodeId) { Ancient = new(knowledge.Ancient, offer) };
    }

    private static void OpeningAndBundles(PredictorRun captured)
    {
        var run = Opening(captured, "SCROLL_BOXES");
        var initial = Pure(run, [], 1).Nodes.Single();
        Check(initial.Ancient is { Ancient.Entry: "NEOW" } && initial.Ancient.Offers.Length == 3 &&
            !initial.Advanced && initial.PendingCauses.Any(c => c.Code == "AncientChoice"),
            "Neow offers require a manual selection with no default");
        var plan = Plan(run, "SCROLL_BOXES");
        var pending = Pure(run, [plan]).Nodes[0];
        var choice = pending.Ancient!.RequiredChoices.Single();
        Check(choice.Phase == PredictorPhase.ChooseBundle && choice.Bundles.Length == 2 && !pending.Advanced,
            "ScrollBoxes exposes its actual bundle child");
        var child = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseBundle, K("SCROLL_BOXES"))
        { ExpectedBundles = choice.Bundles, BundleIndex = 1 };
        var completePlan = plan with { Ancient = plan.Ancient! with { Children = [child] } };
        var projected = Pure(run, [completePlan]);
        Check(projected.Nodes[0].Advanced && projected.Nodes[1].Room == PredictorRoomKind.Monster,
            "Authored Neow bundle continues into the actual next combat");
        var concrete = Opening(captured, "SCROLL_BOXES");
        Select(concrete, new ChooseOpeningRelic(K("SCROLL_BOXES")));
        Select(concrete, new ChooseScrollBoxBundle(1));
        Check(projected.Nodes[0].Cards.Select(c => Display(c.Value)).SequenceEqual(concrete.Working.Deck.Select(concrete.Working.Card).Select(Display)),
            "Bundle route projection equals concrete source settlement");
        var changed = child with { ExpectedBundles = child.ExpectedBundles.SetItem(1,
            child.ExpectedBundles[1].SetItem(0, child.ExpectedBundles[1][0] with { UpgradeLevel = child.ExpectedBundles[1][0].UpgradeLevel + 1,
                MaxUpgradeLevel = child.ExpectedBundles[1][0].MaxUpgradeLevel + 1 })) };
        Check(Pure(run, [plan with { Ancient = plan.Ancient! with { Children = [changed] } }]).Nodes[0].PendingCauses.Any(c => c.Code == "PlannedObtainBundleChanged"),
            "A changed generated bundle preserves its saved choice as pending");

        var fresh = Opening(captured, "OLD_COIN");
        var coin = Pure(fresh, [Plan(fresh, "OLD_COIN")]);
        Check(coin.Nodes[0].Advanced && !coin.Nodes[0].Cards.Any(c => c.Value.Key.Entry == "INJURY"),
            "Independent chosen branches start from the same untouched entry boundary");
        var wrongAncient = plan with { Ancient = plan.Ancient! with { ExpectedAncient = new("EVENT", "DARV") } };
        Check(Pure(run, [wrongAncient]).Nodes[0].PendingCauses.Any(c => c.Code == "PlannedAncientChanged"),
            "A changed Ancient model never rebinds saved offered intent");
    }

    private static void AncientAcquisitionOutcomes(PredictorRun captured)
    {
        foreach (string gift in new[] { "ARCANE_SCROLL", "PHIAL_HOLSTER" })
        {
            var run = Opening(captured, gift);
            var plan = Plan(run, gift);
            var result = Pure(run, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
            var concrete = Opening(captured, gift);
            var before = concrete.Working;
            Select(concrete, new ChooseOpeningRelic(K(gift)));
            var outcome = result.Ancient!.Outcome!;
            Check(outcome.Source == K(gift) && result.Advanced && outcome.ObtainedRelics.IsEmpty,
                "the selected Ancient header owns its gift once, separate from acquisition results " + gift);
            if (gift == "ARCANE_SCROLL")
                Check(outcome.AddedCards.SequenceEqual(concrete.Working.Deck.Where(id => !before.Deck.Contains(id))
                    .Select(concrete.Working.Card).Select(Display)) && !outcome.AddedCards.IsEmpty,
                    "Arcane Scroll publishes the actual generated rare card without actionable IDs");
            else
                Check(outcome.Potions.Length == 2 && outcome.Potions.Select(potion => potion.Key).SequenceEqual(
                    concrete.Working.PotionSlots.OfType<PredictorPotion>().Where(potion =>
                        !before.PotionSlots.OfType<PredictorPotion>().Any(old => old.Id == potion.Id)).Select(potion => potion.Key)) &&
                    outcome.Potions.All(potion => potion.Result == PredictorInputResult.Accepted),
                    "Phial Holster publishes its actual generated bottles outside an Operation context");
        }
        foreach (var (ancient, gift) in new[] { ("OROBAS", "ALCHEMICAL_COFFER"), ("VAKUU", "DISTINGUISHED_CAPE") })
        {
            var run = FindAncient(captured, ancient, gift);
            var plan = Plan(run, gift);
            var before = run.Working;
            var projected = Pure(run, [plan], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].Ancient!.Outcome!;
            Select(run, new ChooseAncientOffer(run.AncientOffers.Single(offer => offer.Relic == K(gift)).Id));
            if (gift == "DISTINGUISHED_CAPE")
                Check(projected.AddedCards.SequenceEqual(run.Working.Deck.Where(id => !before.Deck.Contains(id))
                        .Select(run.Working.Card).Select(Display)) && projected.AddedCards.Count(card =>
                        card.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Curse) == 2,
                    "Distinguished Cape displays both real random curses and the actual fixed additions");
            else
                Check(projected.Potions.Length == 4 && projected.Potions.Select(potion => potion.Key).SequenceEqual(
                        run.Working.PotionSlots.OfType<PredictorPotion>().Where(potion =>
                            !before.PotionSlots.OfType<PredictorPotion>().Any(old => old.Id == potion.Id)).Select(potion => potion.Key)),
                    "Alchemical Coffer displays the four actual bottle-generation attempts");
        }
        foreach (string gift in new[] { "ARCHAIC_TOOTH", "TOUCH_OF_OROBAS" })
        {
            var run = FindAncient(captured, "OROBAS", gift);
            var plan = Plan(run, gift);
            var entry = Pure(run, [], 1).Nodes[0].Ancient!.Offers.Single(offer => offer.Value?.Relic == K(gift));
            var result = Pure(run, [plan], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].Ancient!;
            if (gift == "ARCHAIC_TOOTH")
                Check(entry.StarterCard != null && entry.ReplacementCard != null &&
                    result.Outcome!.ChangedCards.Any(change => change.Action == "Transform" &&
                        change.Before.Key == entry.StarterCard.Key && change.After == entry.ReplacementCard),
                    "Archaic Tooth binds both source and transformed card from the actual entry, then reports its actual transformation");
            else
                Check(entry.StarterRelic != null && entry.ReplacementRelic != null &&
                    result.Outcome!.RemovedRelics.Contains(entry.StarterRelic.Value) &&
                    result.Outcome.ObtainedRelics.Contains(entry.ReplacementRelic.Value),
                    "Touch of Orobas exposes and acquires the actual starter replacement without repeating its gift header");
        }
        var bonesSource = new PredictorRun(captured.Context with
            { OpeningEligibleRelics = [K("VAJRA"), K("ANCHOR"), K("NEOWS_BONES")] }, captured.Working);
        var bones = Opening(bonesSource, "NEOWS_BONES");
        var bonesPlan = Plan(bones, "NEOWS_BONES");
        for (int index = 0; index < 2; index++)
        {
            var node = Pure(bones, [bonesPlan], 1).Nodes[0].Ancient!;
            var reward = node.RequiredChoices.Single().Rewards.First(observation => observation.IsKnown &&
                observation.Value is { Kind: PredictorRewardKind.Relic, Status: PredictorRewardStatus.Open });
            bonesPlan = bonesPlan with { Ancient = bonesPlan.Ancient! with { Children = bonesPlan.Ancient.Children.Add(
                new(PredictorObtainChildAction.Reward, K("NEOWS_BONES")) { Reward = new(reward.Value!, reward.Occurrence) }) } };
        }
        var bonesResult = Pure(bones, [bonesPlan], 1).Nodes[0].Ancient!;
        Check(bonesResult.Advanced && bonesResult.Outcome!.AddedCards.Count(card =>
                card.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Curse) == 1 &&
            bonesResult.Outcome.ObtainedRelics.Length + bonesResult.Rewards.Count(reward => reward.IsKnown &&
                reward.Value is { Kind: PredictorRewardKind.Relic, Status: PredictorRewardStatus.Taken }) == 2,
            "Neow's Bones keeps its final real random curse and displays each acquired nested relic exactly once");

        var creationState = captured.Working;
        long creationId = creationState.NextInstanceId;
        creationState = creationState with { Relics = creationState.Relics.Where(relic =>
                relic.Key.Entry is not ("MOLTEN_EGG" or "TOXIC_EGG" or "FROZEN_EGG" or "FRESNEL_LENS")).ToImmutableArray()
                .Add(new(creationId, K("MOLTEN_EGG"), 0)).Add(new(creationId + 1, K("TOXIC_EGG"), 0))
                .Add(new(creationId + 2, K("FRESNEL_LENS"), 0)), NextInstanceId = creationId + 3 };
        var prototype = creationState.Deck.Select(creationState.Card).First(card => card.Enchantment == null &&
            card.UpgradeLevel == 0 && card.MaxUpgradeLevel > 0 && PredictorObtainSources.GainsBlockForFresnel(captured.Context, card));
        string creationBefore = JsonSerializer.Serialize(creationState);
        var prepared = PredictorObtainSources.PrepareAddedCard(captured.Context, creationState,
            prototype with { Id = creationState.NextInstanceId, FloorAdded = creationState.Position.Floor });
        var acquiredCard = PredictorObtainSources.AddCard(captured.Context, creationState, prototype)
            .Card(creationState.NextInstanceId);
        Check(prepared == acquiredCard && prepared.UpgradeLevel > prototype.UpgradeLevel &&
            prepared.Enchantment?.Entry == "NIMBLE" && prepared.EnchantmentAmount == 2 &&
            creationBefore == JsonSerializer.Serialize(creationState),
            "pure Ancient replacement metadata shares the actual ordered Egg/Fresnel preparation without allocating or executing add hooks");
    }

    private static void LiveBundleContinuation(PredictorRun captured)
    {
        var run = Opening(captured, "SCROLL_BOXES");
        var plan = Plan(run, "SCROLL_BOXES");
        var pending = Pure(run, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var bundle = pending.Nodes[0].Ancient!.BundleChoice!;
        Check(pending.Nodes.Length == 2 && pending.Nodes[0].Advanced && bundle.SelectedBundle == null &&
            bundle.Choice.Bundles.Length == 2 && pending.Nodes[0].Ancient!.Outcome!.AddedCards.IsEmpty &&
            pending.Nodes[0].Cards.Where(card => !run.Working.Deck.Contains(card.Value.Id)).All(card => !card.IsKnown),
            "unselected Scroll Boxes retains both real candidates and continues without exposing its private bundle witness");
        string Cursor(PredictorRouteNodeKnowledge node, string stream) => node.Diagnostics!.After.Select(node.Diagnostics.Snapshot.Fact)
            .Single(fact => fact.PropertyKey == "rng." + stream + ".state").DisplayValue!;
        for (int index = 0; index < 2; index++)
        {
            var child = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseBundle, K("SCROLL_BOXES"))
                { ExpectedBundles = bundle.Choice.Bundles, BundleIndex = index };
            var selected = plan with { Ancient = plan.Ancient! with { Children = [child] } };
            var exact = Pure(run, [selected], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(exact.Nodes[0].Ancient!.BundleChoice is { } retained && retained.SelectedBundle == index &&
                JsonSerializer.Serialize(retained.Choice.Bundles) == JsonSerializer.Serialize(bundle.Choice.Bundles) &&
                Cursor(pending.Nodes[1], "Rewards") == Cursor(exact.Nodes[1], "Rewards") &&
                Cursor(pending.Nodes[1], "Niche") == Cursor(exact.Nodes[1], "Niche") &&
                JsonSerializer.Serialize(pending.Nodes[1].Rewards.Select(reward => reward.Value)) ==
                    JsonSerializer.Serialize(exact.Nodes[1].Rewards.Select(reward => reward.Value)),
                "each real bundle can be filled or changed later without changing independent reward cursors " + index);
        }
        var concrete = Opening(captured, "SCROLL_BOXES");
        Select(concrete, new ChooseOpeningRelic(K("SCROLL_BOXES")));
        var activePlan = plan with { Ancient = plan.Ancient! with { Children =
            [new(PredictorObtainChildAction.ChooseBundle, K("SCROLL_BOXES")) { ExpectedBundles = bundle.Choice.Bundles, BundleIndex = 1 }] } };
        Check(Pure(concrete, [activePlan], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].Ancient!.BundleChoice?.SelectedBundle == 1,
            "an active-node accepted parent retains the real bundle after its authored child completes");
        Check(!Pure(run, [plan], 1).Nodes[0].Advanced,
            "Authored Scroll Boxes preserves its native mandatory bundle picker");
        var state = captured.Working;
        long next = state.NextInstanceId;
        state = state with { Relics = state.Relics.Where(relic => relic.Key.Entry is not ("LUCKY_FYSH" or "BOOK_OF_FIVE_RINGS" or "BING_BONG"))
                .ToImmutableArray().Add(new(next, K("LUCKY_FYSH"), 0)).Add(new(next + 1, K("BOOK_OF_FIVE_RINGS"), 0)),
            NextInstanceId = next + 2 };
        var hooks = Opening(new PredictorRun(captured.Context, state), "SCROLL_BOXES");
        var hookResult = Pure(hooks, [Plan(hooks, "SCROLL_BOXES")], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
        Check(hookResult.Inventory!.Relics.Single(relic => relic.Value.Key == K("BOOK_OF_FIVE_RINGS")) is
                { IsKnown: true, Value.BookOfFiveRingsCardsAdded: 3 } &&
            !hookResult.PendingCauses.Any(cause => cause.Code == "AncientBundleUnselected" &&
                (cause.AffectedDomains & (PredictorCombatDomain.Gold | PredictorCombatDomain.Relics)) != 0),
            "unselected three-card bundles preserve common real Lucky Fysh and Book hooks");
    }

    private static void CompletedPotionDeparture(PredictorRun captured)
    {
        var state = captured.Working;
        var bottle = new PredictorPotion(state.NextInstanceId, new("POTION", "FRUIT_JUICE"));
        var source = new PredictorRun(captured.Context, state with
            { PotionSlots = [bottle, null], NextInstanceId = state.NextInstanceId + 1 });
        // The fixture keeps three distinct opening options: GOLDEN_PEARL is
        // already one of Opening's fixed alternatives.
        var run = Opening(source, "VAJRA");
        var plan = Plan(run, "VAJRA") with
        { PotionActions = [new(PredictorRoutePotionStage.BeforeLeave, PredictorRoutePotionActionKind.Use,
            0, bottle, PredictorPhase.Complete)] };
        var result = Pure(run, [plan]);
        var departure = result.Nodes[0].PotionBoundaries.Single(b => b.Stage == PredictorRoutePotionStage.BeforeLeave);
        Check(departure.Before.Phase == PredictorPhase.Complete && departure.AppliedActions == 1 &&
            departure.Before.PotionSlots[0].Value == bottle && departure.After.PotionSlots[0].Value == null &&
            result.Nodes[1].PotionBoundaries.Single(b => b.Stage == PredictorRoutePotionStage.BeforeChoices).Before.PotionSlots[0].Value == null &&
            run.Working.PotionSlots[0] == bottle,
            "Completed Ancient departure uses pure potion effects and commits only its witness checkpoint for later route nodes");
    }

    private static void CardTransformAndHealthChildren(PredictorRun captured)
    {
        var tablet = Opening(captured, "HEFTY_TABLET");
        var plan = Plan(tablet, "HEFTY_TABLET");
        var cards = Pure(tablet, [plan], 1).Nodes[0].Ancient!.RequiredChoices.Single().Cards;
        var child = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseCard, K("HEFTY_TABLET"))
        { ExpectedCards = cards, CardIndices = [2] };
        var full = plan with { Ancient = plan.Ancient! with { Children = [child] } };
        var projected = Pure(tablet, [full], 1).Nodes[0];
        Check(projected.Advanced && projected.Cards.Any(c => c.Value.Key == cards[2].Key) && projected.Cards.Any(c => c.Value.Key.Entry == "INJURY"),
            "Generated obtain card choice and its mandatory Injury settle in order");

        var leaf = Opening(captured, "NEW_LEAF");
        var leafPlan = Plan(leaf, "NEW_LEAF");
        var target = Target(leaf.Working.Card(leaf.Working.Deck[0]));
        var transform = new PredictorPlannedObtainChild(PredictorObtainChildAction.TransformCard, K("NEW_LEAF")) { DeckCards = [target] };
        var result = Pure(leaf, [leafPlan with { Ancient = leafPlan.Ancient! with { Children = [transform] } }]);
        Check(result.Nodes[0].Advanced && !result.Nodes[0].Cards.Any(c => c.Value.Id == target.InstanceId),
            "NewLeaf binds an existing card and completes its Niche transform");
        var changedTarget = transform with { DeckCards = [target with { Key = new("CARD", "UNAVAILABLE_MOD_CARD") }] };
        Check(Pure(leaf, [leafPlan with { Ancient = leafPlan.Ancient! with { Children = [changedTarget] } }]).Nodes[0].PendingCauses.Any(c => c.Code == "PlannedObtainDeckTargetPending"),
            "A changed card model retains the target as pending without substituting another card");

        Select(tablet, new ChooseOpeningRelic(K("HEFTY_TABLET")));
        Check(Pure(tablet, [full], 1).Nodes[0].Advanced, "An active child resumes after the already accepted offered choice");
        var lost = Opening(captured, "LOST_COFFER");
        var lostPlan = Plan(lost, "LOST_COFFER");
        var childRewards = Pure(lost, [lostPlan], 1).Nodes[0].Ancient!.RequiredChoices.Single().Rewards;
        var cardReward = childRewards.First(r => r.Kind == PredictorRewardKind.Card).Value!;
        var take = new PredictorPlannedObtainChild(PredictorObtainChildAction.Reward, K("LOST_COFFER"))
        { Reward = new(cardReward, 0, CardIndex: 0) };
        var leave = new PredictorPlannedObtainChild(PredictorObtainChildAction.LeaveRewards, K("LOST_COFFER"));
        var lostFull = lostPlan with { Ancient = lostPlan.Ancient! with { Children = [take, leave] } };
        Check(Pure(lost, [lostFull], 1).Nodes[0].Advanced, "Nested ordinary reward choice and explicit child closure complete Neow");
        Select(lost, new ChooseOpeningRelic(K("LOST_COFFER")));
        var actualReward = lost.Rewards.First(r => r.Kind == PredictorRewardKind.Card);
        Select(lost, new EnterCardSelection(actualReward.Id));
        Select(lost, new TakeReward(actualReward.Id, actualReward.Cards[0].Id));
        Check(Pure(lost, [lostFull], 1).Nodes[0].Advanced, "Active accepted child prefix does not replay the same card receipt");
        var stale = take with { Reward = take.Reward! with { CardIndex = 1 } };
        Check(Pure(lost, [lostPlan with { Ancient = lostPlan.Ancient! with { Children = [stale, leave] } }], 1).Nodes[0].PendingCauses
            .Any(c => c.Code == "PlannedObtainAcceptedPrefixChanged"), "A saved changed accepted child cannot silently rebind to the active prefix");

        var health = Opening(captured, "LEAFY_POULTICE");
        var hpPlan = Plan(health, "LEAFY_POULTICE");
        Check(Pure(health, [hpPlan], 1).Nodes[0].Ancient!.RequiredChoices.Single().Phase == PredictorPhase.ObtainHealth,
            "Leafy preserves its local health fact boundary");
        var hp = new PredictorPlannedObtainChild(PredictorObtainChildAction.Health, K("LEAFY_POULTICE")) { Health = new(50, 80) };
        Check(Pure(health, [hpPlan with { Ancient = hpPlan.Ancient! with { Children = [hp] } }], 1).Nodes[0].Advanced,
            "Authored local health runs the actual Leafy transform and node completion");
    }

    private static void ActualAncientOffers(PredictorRun captured)
    {
        var dusty = FindAncient(captured, "DARV", "DUSTY_TOME");
        var entry = dusty.Checkpoints[0].State;
        var stream = entry.Streams.Single(s => s.Stream == PredictorStream.Rewards);
        var native = new Rng(new SerializableRng { counter = stream.Calls, state0 = stream.S0,
            state1 = stream.S1, state2 = stream.S2, state3 = stream.S3 });
        var prepared = dusty.Context.Catalog!.DustyTomeCards[native.NextInt(dusty.Context.Catalog.DustyTomeCards.Length)];
        Check(dusty.AncientOffers.Single(o => o.Relic.Entry == "DUSTY_TOME").PreparedCard == prepared &&
            dusty.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls == stream.Calls + 1 &&
            dusty.Working.Deck.SequenceEqual(entry.Deck) && dusty.Working.Relics.SequenceEqual(entry.Relics),
            "DustyTome native Rewards setup happens on offer generation, before any relic/card receipt");
        var dustyResult = Pure(dusty, [Plan(dusty, "DUSTY_TOME")], 1).Nodes[0];
        Check(dustyResult.Advanced && dustyResult.Cards.Any(c => c.Value.Key == prepared && c.Value.UpgradeLevel == 1),
            "Choosing DustyTome receives its already prepared upgraded card");

        var astro = FindAncient(captured, "DARV", "ASTROLABE");
        var astroPlan = Plan(astro, "ASTROLABE");
        var astroChoice = Pure(astro, [astroPlan], 1).Nodes[0].Ancient!.RequiredChoices.Single();
        var three = astroChoice.DeckCards.Take(3).Select(c => Target(c.Value)).ToImmutableArray();
        var transform = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseDeckCards, K("ASTROLABE")) { DeckCards = three };
        var transformed = Pure(astro, [astroPlan with { Ancient = astroPlan.Ancient! with { Children = [transform] } }], 1).Nodes[0];
        Check(transformed.Advanced && three.All(t => transformed.Cards.All(c => c.Value.Id != t.InstanceId)),
            "Actual Darv Astrolabe supports ordered native deck transformations");

        var glass = FindAncient(captured, "OROBAS", "SEA_GLASS");
        var glassPlan = Plan(glass, "SEA_GLASS");
        var glassCards = Pure(glass, [glassPlan], 1).Nodes[0].Ancient!.RequiredChoices.Single().Cards;
        Check(glassCards.Length == 15 && glassPlan.Ancient!.ExpectedOffer.CharacterTarget != null,
            "Actual Orobas SeaGlass retains its character target and fifteen generated cards");
        var many = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseManyCards, K("SEA_GLASS"))
        { ExpectedCards = glassCards, CardIndices = [0, 14] };
        var manyResult = Pure(glass, [glassPlan with { Ancient = glassPlan.Ancient! with { Children = [many] } }], 1).Nodes[0];
        Check(manyResult.Advanced && manyResult.Cards.Length == glass.Working.Deck.Length + 2,
            "SeaGlass authored subset settles through native many-card obtain");
    }

    private static void FutureOrobasHypotheses(PredictorRun captured)
    {
        PredictorRun? run = null;
        PredictorRouteNodeKnowledge? initial = null;
        for (int ordinal = 0; ordinal < 128; ordinal++)
        {
            var candidate = Ancient(captured, "OROBAS", ordinal, enterFirst: false);
            var routes = candidate.Routes;
            candidate = new PredictorRun(candidate.Context with { Live = new(2, true, []) }, candidate.Working);
            candidate.RestoreRoutes(routes);
            var node = Pure(candidate, [], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
            if (!new[] { "SEA_GLASS", "DRIFTWOOD", "ARCHAIC_TOOTH" }.All(entry =>
                    node.Ancient!.Offers.Any(offer => offer.IsKnown && offer.Value is { Selectable: true } value && value.Relic.Entry == entry))) continue;
            run = candidate; initial = node; break;
        }
        Check(run != null && initial != null, "Native pre-entry Orobas three-gift fixture not found");
        Check(run!.Phase == PredictorPhase.Complete && run.Working.Visits.IsEmpty && !initial!.Advanced,
            "the caller remains before Ancient entry while the future offers are selectable hypotheses");
        foreach (var knowledge in initial!.Ancient!.Offers)
        {
            var offer = knowledge.Value!;
            var plan = new PredictorPlannedNode(initial.Position.NodeId) { Ancient = new(initial.Ancient.Ancient, offer) };
            var selected = Pure(run, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(selected.Nodes[0].Inventory!.Relics.Any(relic => relic.IsKnown && relic.Value.Key == offer.Relic),
                "pre-entry selection acquires the real Orobas gift " + offer.Relic.Entry);
            if (offer.Relic.Entry == "SEA_GLASS")
            {
                var optional = selected.Nodes[0].Ancient!.OptionalCards!;
                var picker = optional.Choice;
                Check(picker is { Phase: PredictorPhase.ObtainChooseManyCards, MinimumCards: 0, MaximumCards: 15 } &&
                    picker.Cards.Length == 15 && picker.Cards.All(card => card.Id == 0) &&
                    offer.CharacterTarget != null && picker.PendingCauses.IsEmpty && optional.DefaultSkipped &&
                    selected.Nodes.Length == 2 && selected.Nodes[0].Advanced && selected.Nodes[1].Advanced &&
                    selected.Nodes[0].Cards.Length == run.Working.Deck.Length &&
                    selected.Nodes[0].Ancient!.RequiredChoices.IsEmpty &&
                    selected.Nodes[0].Diagnostics!.Snapshot.Facts.Any(fact => fact.NodeId == initial.Position.NodeId &&
                        fact.PropertyKey == "ancient.default.optionalCards" && fact.Origin == PredictorKnowledgeOrigin.ApprovedDefault),
                    "pre-entry Sea Glass defaults to the real empty subset and retains its actual fifteen-card picker without blocking the suffix");
                var choose = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseManyCards, offer.Relic)
                    { ExpectedCards = picker.Cards, CardIndices = [0, 14] };
                plan = plan with { Ancient = plan.Ancient! with { Children = [choose] } };
                selected = Pure(run, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
                Check(selected.Nodes[0].Cards.Count(card => card.IsKnown) == run.Working.Deck.Length + 2 &&
                    selected.Nodes[0].Ancient!.OptionalCards is { DefaultSkipped: false } changed &&
                    changed.SelectedIndices.SequenceEqual([0, 14]),
                    "pre-entry Sea Glass takes exactly the two authored cards through its real obtain");
                Check(Pure(run, [plan with { Ancient = plan.Ancient with { Children = [] } }], 1).Nodes[0] is
                    { Advanced: false, Phase: PredictorPhase.ObtainChooseManyCards },
                    "full authored Sea Glass still requires its actual subset choice");
            }
            else if (offer.Relic.Entry == "ARCHAIC_TOOTH")
                Check(offer.StarterCardId is { } target && selected.Nodes[0].Cards.All(card => card.Value.Id != target),
                    "pre-entry Archaic Tooth executes the real starter-card transformation");
            Check(selected.Nodes.Length == 2 && selected.Nodes[0].Advanced && selected.Nodes[1].Advanced &&
                selected.Nodes[1].Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card && reward.DefaultSkipped),
                "each authored pre-entry Orobas hypothesis reaches the actual default-skipped combat reward " + offer.Relic.Entry);
            if (offer.Relic.Entry == "DRIFTWOOD")
                Check(selected.Nodes[1].Rewards.Any(reward => reward.IsKnown && reward.Value is { Kind: PredictorRewardKind.Card, CanReroll: true }),
                    "preselected Driftwood really enables the following combat reward reroll");
            var stale = plan with { Ancient = plan.Ancient! with
                { ExpectedOffer = offer with { Variant = "changed-preentry-offer" } } };
            Check(Pure(run, [stale], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].PendingCauses.Any(cause =>
                    cause.Code == "PlannedAncientOfferPending"),
                "pre-entry hypotheses retain the actual semantic offer guard");
        }
    }

    private static void OptionalAncientCardsAndTargets(PredictorRun captured)
    {
        foreach (string source in new[] { "HEFTY_TABLET", "LEAD_PAPERWEIGHT" })
        {
            var run = Opening(captured, source);
            var plan = Plan(run, source);
            var skipped = Pure(run, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            var optional = skipped.Nodes[0].Ancient!.OptionalCards!;
            Check(optional is { DefaultSkipped: true, Choice.Phase: PredictorPhase.ObtainChooseCard,
                    Choice.MinimumCards: 0, Choice.MaximumCards: 1 } && optional.Choice.Cards.Length == (source == "HEFTY_TABLET" ? 3 : 2) &&
                skipped.Nodes.Length == 2 && skipped.Nodes[0].Advanced && skipped.Nodes[1].Advanced &&
                skipped.Nodes[0].Cards.Count(card => card.Value.Key.Entry == "INJURY") ==
                    run.Working.Deck.Select(run.Working.Card).Count(card => card.Key.Entry == "INJURY") + (source == "HEFTY_TABLET" ? 1 : 0),
                "native optional single-card skip keeps the actual obtain tail and suffix " + source);
            Check(Pure(run, [plan], 1).Nodes[0] is { Advanced: false, Phase: PredictorPhase.ObtainChooseCard },
                "full authored single-card selection still waits for its actual choice " + source);
            var choose = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseCard, K(source))
                { ExpectedCards = optional.Choice.Cards, CardIndices = [0] };
            var changed = Pure(run, [plan with { Ancient = plan.Ancient! with { Children = [choose] } }], 2,
                PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(changed.Nodes[0].Advanced && changed.Nodes[1].Advanced &&
                changed.Nodes[0].Ancient!.OptionalCards is { DefaultSkipped: false } selected && selected.SelectedIndices.SequenceEqual([0]) &&
                changed.Nodes[0].Cards.Length == skipped.Nodes[0].Cards.Length + 1,
                "the real single-card opportunity remains editable after default skip " + source);
        }
        var claws = FindAncient(captured, "TANX", "CLAWS");
        var clawsPlan = Plan(claws, "CLAWS");
        var empty = Pure(claws, [clawsPlan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var targets = empty.Nodes[0].Ancient!.OptionalCards!;
        Check(targets is { DefaultSkipped: true, Choice.Phase: PredictorPhase.ObtainDeckCards,
                Choice.MinimumCards: 0, Choice.MaximumCards: 6 } && empty.Nodes[0].Advanced && empty.Nodes[1].Advanced &&
            empty.Nodes[0].Cards.Select(card => card.Value.Id).SequenceEqual(claws.Working.Deck),
            "native Claws empty transform keeps every original card and permits independent following rewards");
        var first = targets.Choice.DeckCards.First(card => card.IsKnown).Value;
        var transform = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseDeckCards, K("CLAWS"))
            { DeckCards = [Target(first)] };
        var changedClaws = Pure(claws, [clawsPlan with { Ancient = clawsPlan.Ancient! with { Children = [transform] } }], 2,
            PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(changedClaws.Nodes[0].Advanced && changedClaws.Nodes[1].Advanced &&
            changedClaws.Nodes[0].Cards.All(card => card.Value.Id != first.Id) &&
            changedClaws.Nodes[0].Cards.Any(card => card.IsKnown && card.Value.Key.Entry == "MAUL") &&
            changedClaws.Nodes[0].Ancient!.OptionalCards is { DefaultSkipped: false, SelectedIndices.Length: 1 },
            "Claws backfill applies the exact existing target through the real Maul transformation");
        var stale = transform with { ExpectedSource = K("PAELS_TOOTH") };
        Check(Pure(claws, [clawsPlan with { Ancient = clawsPlan.Ancient! with { Children = [stale] } }], 1,
                PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].PendingCauses.Any(cause => cause.Code == "PlannedObtainSourceChanged"),
            "an unrelated child source cannot consume the optional Claws picker");
        Check(Pure(claws, [clawsPlan], 1).Nodes[0] is { Advanced: false, Phase: PredictorPhase.ObtainDeckCards },
            "full authored Claws keeps its explicit zero-to-six choice");

        // The current native source was already selected. An empty Claws input
        // reads no target population, even when earlier deck facts are missing.
        var actualClawsOffer = claws.AncientOffers.Single(offer => offer.Relic == K("CLAWS"));
        Select(claws, new ChooseAncientOffer(actualClawsOffer.Id));
        var missing = PredictorRouteCauseEvidence.CreateExplicit("ClawsDeckNotObserved", claws.Working.Position.NodeId,
            PredictorCombatDomain.Deck, PredictorPendingReasonKind.MissingObservation,
            "The active Claws target population was not observed.", ["deck.All.candidates"]);
        var partial = new PredictorRun(claws.Context with { Live = new(claws.Working.Position.Act, true, [missing]) }, claws.Checkpoints, claws.Revision);
        partial.RestoreRoutes(claws.Routes);
        partial.RestoreNodeDraft(claws.CaptureNodeDraft()!);
        var omitted = Pure(partial, [], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
        Check(omitted.Advanced && omitted.Ancient!.OptionalCards is { DefaultSkipped: true } unavailable &&
            unavailable.SelectedIndices.IsEmpty && unavailable.Choice.DeckCards.Any(card => !card.IsKnown),
            "an actual empty Claws choice does not require or fabricate a known target population");
        var activeOptional = omitted.Ancient!.OptionalCards!;
        Check(activeOptional.AcceptedOffer == actualClawsOffer,
            "an active optional picker retains the exact accepted native parent offer without a saved plan");
        var activePlan = new PredictorPlannedAncient(omitted.Ancient.Ancient, activeOptional.AcceptedOffer)
        { Children = [new(PredictorObtainChildAction.ChooseDeckCards, activeOptional.Choice.Source)] };
        Check(Pure(partial, [new(partial.Working.Position.NodeId) { Ancient = activePlan }], 1,
                PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].Advanced,
            "the actual accepted parent binds an explicit empty edit on the active optional picker");
    }

    private static void OtherNativeAncientRepresentatives(PredictorRun captured)
    {
        foreach (var fixture in new[] { (Ancient: "TEZCATARA", Gift: "YUMMY_COOKIE"),
                     (Ancient: "NONUPEIPE", Gift: "JEWELRY_BOX"), (Ancient: "PAEL", Gift: "PAELS_GROWTH") })
        {
            var run = FindAncient(captured, fixture.Ancient, fixture.Gift);
            var selected = Pure(run, [Plan(run, fixture.Gift)], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(selected.Nodes.Length == 2 && selected.Nodes[0].Advanced && selected.Nodes[1].Advanced &&
                selected.Nodes[0].Inventory!.Relics.Any(relic => relic.IsKnown && relic.Value.Key == K(fixture.Gift)) &&
                selected.Nodes[1].Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card),
                "the native representative gift and its independent reward suffix remain usable " + fixture.Ancient);
            if (fixture.Gift == "JEWELRY_BOX")
                Check(selected.Nodes[0].Cards.Any(card => card.IsKnown && card.Value.Key.Entry == "APOTHEOSIS"),
                    "Nonupeipe Jewelry Box really adds Apotheosis");
            else
                Check(selected.Nodes[0].OptionalObtainInputs.Any(input => input.Choice.Source == K(fixture.Gift)),
                    "the acquired Ancient gift retains its optional real target opportunity " + fixture.Gift);
        }
        var tooth = FindAncient(captured, "PAEL", "PAELS_TOOTH");
        var toothPlan = Plan(tooth, "PAELS_TOOTH");
        var waiting = Pure(tooth, [toothPlan], 1).Nodes[0];
        var five = waiting.Ancient!.RequiredChoices.Single();
        Check(!waiting.Advanced && five is { Phase: PredictorPhase.ObtainDeckCards, MinimumCards: 5, MaximumCards: 5 } &&
            waiting.Ancient.OptionalCards == null, "full authored Pael's Tooth still requires five actual stored cards");
        var store = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseDeckCards, K("PAELS_TOOTH"))
            { DeckCards = five.DeckCards.Where(card => card.IsKnown).Take(5).Select(card => Target(card.Value)).ToImmutableArray() };
        var stored = Pure(tooth, [toothPlan with { Ancient = toothPlan.Ancient! with { Children = [store] } }], 2,
            PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(stored.Nodes[0].Advanced && stored.Nodes[1].Advanced && stored.Nodes[0].Inventory!.Relics.Single(relic =>
                relic.Value.Key == K("PAELS_TOOTH")).Value.StoredCards.Length == 5,
            "Pael's Tooth retains its real five-card storage and subsequent combat continuation");
        var sere = FindAncient(captured, "VAKUU", "SERE_TALON");
        var serePlan = Plan(sere, "SERE_TALON");
        var health = Pure(sere, [serePlan], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
        Check(health.Advanced && health.Ancient!.RequiredChoices.IsEmpty && health.ObtainHealthEffects.Single() is
                { Kind: PredictorObtainHealthEffectKind.LoseMaxHp, Amount: 9 },
            "Vakuu Sere Talon displays its health command while the independent Wishes continue");
        Check(!Pure(sere, [serePlan], 1).Nodes[0].Advanced,
            "full authored Sere Talon still requires its actual health premise");
        var hp = new PredictorPlannedObtainChild(PredictorObtainChildAction.Health, K("SERE_TALON")) { Health = new(60, 80) };
        var healthy = Pure(sere, [serePlan with { Ancient = serePlan.Ancient! with { Children = [hp] } }], 2,
            PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(healthy.Nodes[0].Advanced && healthy.Nodes[1].Advanced &&
            healthy.Nodes[0].Cards.Count(card => card.IsKnown && card.Value.Key.Entry == "WISH") ==
                sere.Working.Deck.Select(sere.Working.Card).Count(card => card.Key.Entry == "WISH") + 3,
            "the real Vakuu health operation adds exactly its three Wishes before the suffix");
        Check(healthy.Nodes[0].ObtainHealthEffects.IsEmpty,
            "an explicit Health child settles its own Sere receipt instead of using the drawing projection");
    }

    private static void LiveObtainHealthEffects(PredictorRun captured)
    {
        var leafy = Opening(captured, "LEAFY_POULTICE");
        var plan = Plan(leafy, "LEAFY_POULTICE");
        var projected = Pure(leafy, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var actualHealth = new PredictorPlannedObtainChild(PredictorObtainChildAction.Health, K("LEAFY_POULTICE")) { Health = new(50, 80) };
        var explicitPlan = plan with { Ancient = plan.Ancient! with { Children = [actualHealth] } };
        var exact = Pure(leafy, [explicitPlan], 1).Nodes[0];
        Check(projected.Nodes.Length == 2 && projected.Nodes.All(node => node.Advanced) &&
            projected.Nodes[0].Cards.Where(card => card.IsKnown).Select(card => card.Value).SequenceEqual(exact.Cards.Select(card => card.Value)) &&
            projected.Nodes[0].ObtainHealthEffects.Single() is { Source.Entry: "LEAFY_POULTICE", Amount: 12,
                Kind: PredictorObtainHealthEffectKind.LoseMaxHp, PendingCauses.Length: > 0 } effect &&
            effect.Address.Destination == PredictorObtainReturn.Opening &&
            !projected.Nodes[0].PendingCauses.Any(cause => (cause.AffectedDomains &
                (PredictorCombatDomain.NicheRng | PredictorCombatDomain.TransformationsRng | PredictorCombatDomain.PotionInventory)) != 0),
            "Live Leafy needs no HP input for its real independent transforms and suffix, without inventing health or RNG gaps");
        Check(Pure(leafy, [explicitPlan], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].ObtainHealthEffects.IsEmpty,
            "Live Leafy preserves its explicit parent Health child");

        var unknown = PredictorRouteCauseEvidence.CreateExplicit("LeafyDeckNotObserved", leafy.Working.Position.NodeId,
            PredictorCombatDomain.Deck, PredictorPendingReasonKind.MissingObservation, "The incoming basic-card population was not observed.", ["deck.All.candidates"]);
        var partial = new PredictorRun(leafy.Context with { Live = new(1, true, [unknown]) }, leafy.Checkpoints, leafy.Revision);
        partial.RestoreRoutes(leafy.Routes); partial.RestoreNodeDraft(leafy.CaptureNodeDraft()!);
        var masked = Pure(partial, [plan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(masked.Nodes[0].Advanced && masked.Nodes[0].ObtainHealthEffects.Length == 1 &&
            masked.Nodes[0].PendingCauses.Any(cause => cause.Code == "LeafyTransformOutcomePending") &&
            !masked.Nodes[0].PendingCauses.Any(cause => (cause.AffectedDomains & PredictorCombatDomain.NicheRng) != 0),
            "unknown Leafy basic cards mask their actual transformation stream without demanding HP or poisoning Niche");
        var missingStream = PredictorRouteCauseEvidence.CreateExplicit("LeafyTransformationsNotObserved", leafy.Working.Position.NodeId,
            PredictorCombatDomain.TransformationsRng, PredictorPendingReasonKind.MissingObservation,
            "The incoming Transformations cursor was not observed.", ["rng.Transformations.state"]);
        var streamPartial = new PredictorRun(leafy.Context with { Live = new(1, true, [missingStream]) }, leafy.Checkpoints, leafy.Revision);
        streamPartial.RestoreRoutes(leafy.Routes); streamPartial.RestoreNodeDraft(leafy.CaptureNodeDraft()!);
        var streamMasked = Pure(streamPartial, [plan], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
        Check(streamMasked.Advanced && streamMasked.PendingCauses.Single(cause => cause.Code == "LeafyTransformOutcomePending")
            .Parents.Any(parent => parent.Identity == missingStream.Identity),
            "Leafy's derived outcome retains the actual unknown Transformations origin even when both basic populations are known");

        // Explicit controlled receipt options exercise the native obtain
        // handlers. The Waffles and Mushroom are not claimed as Neow offers.
        foreach (var fixture in new[] { (Source: "LEES_WAFFLE", Kind: PredictorObtainHealthEffectKind.GainMaxHpAndFullHeal, Amount: 7),
            (Source: "FAKE_LEES_WAFFLE", Kind: PredictorObtainHealthEffectKind.HealMaxHpFraction, Amount: 10),
            (Source: "FRAGRANT_MUSHROOM", Kind: PredictorObtainHealthEffectKind.Damage, Amount: 15) })
        {
            var source = Opening(captured, fixture.Source);
            var sourcePlan = Plan(source, fixture.Source);
            var live = Pure(source, [sourcePlan], 2, PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(live.Nodes.Length == 2 && live.Nodes.All(node => node.Advanced) &&
                live.Nodes[0].ObtainHealthEffects.Single() is { } command && command.Source == K(fixture.Source) &&
                command.Kind == fixture.Kind && command.Amount == fixture.Amount && !command.PendingCauses.IsEmpty &&
                live.Nodes[0].Ancient!.RequiredChoices.IsEmpty,
                "the controlled native health receipt displays its real command and independent suffix " + fixture.Source);
            Check(Pure(source, [sourcePlan], 1).Nodes[0].Phase == PredictorPhase.ObtainHealth,
                "full authored health remains required " + fixture.Source);
            var healthChild = new PredictorPlannedObtainChild(PredictorObtainChildAction.Health, K(fixture.Source)) { Health = new(50, 80) };
            Check(Pure(source, [sourcePlan with { Ancient = sourcePlan.Ancient! with { Children = [healthChild] } }], 1,
                PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0] is { Advanced: true, ObtainHealthEffects.IsEmpty: true },
                "an explicit Health child still owns the real receipt " + fixture.Source);
        }
        var shears = Opening(captured, "PRECARIOUS_SHEARS");
        var shearsPlan = Plan(shears, "PRECARIOUS_SHEARS");
        var waiting = Pure(shears, [shearsPlan], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
        var selection = waiting.Ancient!.RequiredChoices.Single();
        var remove = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseDeckCards, K("PRECARIOUS_SHEARS"))
            { DeckCards = selection.DeckCards.Where(card => card.IsKnown).Take(2).Select(card => Target(card.Value)).ToImmutableArray() };
        var cut = Pure(shears, [shearsPlan with { Ancient = shearsPlan.Ancient! with { Children = [remove] } }], 2,
            PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(waiting.Phase == PredictorPhase.ObtainDeckCards && cut.Nodes.Length == 2 && cut.Nodes.All(node => node.Advanced) &&
            cut.Nodes[0].ObtainHealthEffects.Single() is { Kind: PredictorObtainHealthEffectKind.Damage, Amount: 16 } &&
            cut.Nodes[0].Cards.Length == shears.Working.Deck.Length - 2,
            "Precarious Shears retains its actual target responsibility while its health-only tail does not require HP");
        foreach (bool fairy in new[] { true, false })
        {
            long id = captured.Working.NextInstanceId;
            var state = captured.Working with { NextInstanceId = id + 1,
                Relics = captured.Working.Relics.Where(relic => relic.Key.Entry != "LIZARD_TAIL").ToImmutableArray(),
                PotionSlots = captured.Working.PotionSlots.Select(_ => (PredictorPotion?)null).ToImmutableArray() };
            state = fairy ? state with { PotionSlots = state.PotionSlots.SetItem(0, new(id, new("POTION", "FAIRY_IN_A_BOTTLE"))) } :
                state with { Relics = state.Relics.Add(new(id, K("LIZARD_TAIL"), 0)) };
            var prevented = Opening(new PredictorRun(captured.Context, state), "LEAFY_POULTICE");
            var unresolved = Pure(prevented, [Plan(prevented, "LEAFY_POULTICE")], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
            var affected = unresolved.ObtainHealthEffects.Single().PendingCauses.Single().AffectedDomains;
            Check(unresolved.Advanced && (affected & (fairy ? PredictorCombatDomain.PotionInventory : PredictorCombatDomain.Relics)) != 0 &&
                (affected & (fairy ? PredictorCombatDomain.Relics : PredictorCombatDomain.PotionInventory)) == 0,
                "unknown health retains only the real present death-preventer's unresolved consumption " + (fairy ? "Fairy" : "Tail"));
        }
    }

    private static void PaelsToothCountProjection(PredictorRun captured)
    {
        var native = FindAncient(captured, "PAEL", "PAELS_TOOTH");
        int act = native.Working.Position.Act;
        var types = new[] { MapPointType.Ancient }.Concat(Enumerable.Repeat(MapPointType.Monster, 7))
            .Append(MapPointType.Boss).ToArray();
        PredictorRun Fixture(PredictorState? state = null) => Geometry(native.Context, state ?? native.Checkpoints[0].State,
            native.RouteMap(act), act, types);
        var run = Fixture();
        var plan = Plan(run, "PAELS_TOOTH");
        var live = Pure(run, [plan], 8, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(live.Nodes.Length == 8 && live.Nodes.All(node => node.Advanced) &&
            live.Nodes[0].PaelsToothStorage.Single() is { InitialCount: 5, RemainingCount: 5 } &&
            live.Nodes[0].Ancient!.RequiredChoices.IsEmpty,
            "Pael's five unspecified storage targets no longer block the independent seven-combat route");
        var optional = live.Nodes[0].OptionalObtainInputs.Single(input => input.Choice.Source == K("PAELS_TOOTH"));
        var inventory = live.Nodes[0].Inventory!.Relics.Single(relic => relic.Value.Key == K("PAELS_TOOTH"));
        Check(inventory.IsKnown && !inventory.StoredCardsKnown && inventory.Value.StoredCards.IsEmpty &&
            inventory.PaelsToothStorage!.SourceId == inventory.Value.Id,
            "known Pael ownership exposes explicit unknown stored identities rather than a proven empty payload");
        var targets = optional.Choice.DeckCards.Where(card => card.IsKnown).Take(5).Select(card => Target(card.Value)).ToImmutableArray();
        var child = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseDeckCards, K("PAELS_TOOTH")) { DeckCards = targets };
        var authored = PredictorRun.ApplyOptionalObtainInput(plan, optional, child);
        var exact = Pure(run, [authored], 8, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(exact.Nodes.Length == 8 && exact.Nodes.All(node => node.Advanced) && exact.Nodes[0].PaelsToothStorage.IsEmpty &&
            exact.Nodes[0].Inventory!.Relics.Single(relic => relic.Value.Key == K("PAELS_TOOTH")).Value.StoredCards.Length == 5,
            "the real receipt's precise optional backfill restores concrete sorted storage");
        string Cursor(PredictorRouteNodeKnowledge node, string stream) => node.Diagnostics!.After
            .Select(node.Diagnostics.Snapshot.Fact).Single(fact => fact.PropertyKey == "rng." + stream + ".state").DisplayValue!;
        for (int index = 1; index < live.Nodes.Length; index++)
        {
            Check(Cursor(live.Nodes[index], "Rewards") == Cursor(exact.Nodes[index], "Rewards") &&
                Cursor(live.Nodes[index], "Niche") == Cursor(exact.Nodes[index], "Niche") &&
                live.Nodes[index].Rewards.All(reward => reward.IsKnown) &&
                JsonSerializer.Serialize(live.Nodes[index].Rewards.Select(reward => reward.Value)) ==
                    JsonSerializer.Serialize(exact.Nodes[index].Rewards.Select(reward => reward.Value)),
                "count-owned Pael uses the exact five/four/three/two/one bounds and exhausts before later rewards, combat " + index);
            int remaining = Math.Max(0, 5 - index);
            Check(live.Nodes[index].Inventory!.Relics.Single(relic => relic.Value.Key == K("PAELS_TOOTH"))
                .PaelsToothStorage!.RemainingCount == remaining, "projected remaining storage follows each actual combat hook");
        }
        Check(live.Nodes.Skip(1).All(node => node.PaelsToothStorage.IsEmpty),
            "the original acquisition alone owns the always-visible storage feedback");
        var lowerState = native.Checkpoints[0].State;
        var skill = lowerState.Deck.Select(lowerState.Card).First(card => card.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill &&
            card.Removable && card.UpgradeLevel < card.MaxUpgradeLevel);
        for (int added = 0; added < 5; added++) lowerState = PredictorObtainSources.AddCard(native.Context, lowerState, skill);
        var countProof = PredictorDeckKnowledge.Create(native.Context, lowerState).MarkExistingChangePending("paels-lower-bound",
            "The attacks' upgrade qualification was not observed.", PredictorDeckUncertainFact.Upgrade,
            instanceIds: lowerState.Deck.Select(lowerState.Card).Where(card => card.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack)
                .Select(card => card.Id).ToImmutableArray()).Project(PredictorDeckProjectionScope.PaelsTooth);
        Check(countProof.KnownCount == null && countProof.KnownCandidates.Length >= 5 && PredictorRun.PaelsToothStorageCount(countProof) == 5,
            "five genuine known eligible cards prove storage five despite additional unresolved qualification");
        var wrong = authored with { ObtainInputs = authored.ObtainInputs.Select(input => input with
            { Address = input.Address with { SourceId = input.Address.SourceId + 1 } }).ToImmutableArray() };
        Check(Pure(run, [wrong], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0].PendingCauses
            .Any(cause => cause.Code == "PlannedOptionalObtainSourcePending"), "another receipt cannot lend Pael its storage targets");
        Check(!Pure(run, [plan], 1).Nodes[0].Advanced, "Authored mode still requires real Pael targets");
        var hookState = native.Checkpoints[0].State;
        long hookId = hookState.NextInstanceId;
        hookState = hookState with { Relics = hookState.Relics.Where(relic => relic.Key.Entry is not
                ("LUCKY_FYSH" or "BOOK_OF_FIVE_RINGS" or "BING_BONG")).ToImmutableArray()
                .Add(new(hookId, K("LUCKY_FYSH"), 0)).Add(new(hookId + 1, K("BOOK_OF_FIVE_RINGS"), 0)),
            NextInstanceId = hookId + 2 };
        var hooks = Fixture(hookState);
        var hookPlan = Plan(hooks, "PAELS_TOOTH");
        var hookFirst = Pure(hooks, [hookPlan], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0];
        var hookOptional = hookFirst.OptionalObtainInputs.Single(input => input.Choice.Source == K("PAELS_TOOTH"));
        var hookChild = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseDeckCards, K("PAELS_TOOTH"))
            { DeckCards = hookOptional.Choice.DeckCards.Where(card => card.IsKnown).Take(5).Select(card => Target(card.Value)).ToImmutableArray() };
        var hookLive = Pure(hooks, [hookPlan], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var hookExact = Pure(hooks, [PredictorRun.ApplyOptionalObtainInput(hookPlan, hookOptional, hookChild)], 3,
            PredictorRoutePreviewPolicy.LiveMapDefaults);
        for (int index = 1; index < 3; index++)
        {
            var book = hookLive.Nodes[index].Inventory!.Relics.Single(relic => relic.Value.Key == K("BOOK_OF_FIVE_RINGS"));
            var snapshot = hookLive.Nodes[index].Diagnostics!;
            var exactSnapshot = hookExact.Nodes[index].Diagnostics!;
            Check(book.IsKnown && book.Value.BookOfFiveRingsCardsAdded == index &&
                snapshot.After.Select(snapshot.Snapshot.Fact).Single(fact => fact.PropertyKey == "gold.balance").DisplayValue ==
                    exactSnapshot.After.Select(exactSnapshot.Snapshot.Fact).Single(fact => fact.PropertyKey == "gold.balance").DisplayValue &&
                !hookLive.Nodes[index].PendingCauses.Any(cause => cause.Code == "PaelsToothReturnedCardPending" &&
                    (cause.AffectedDomains & (PredictorCombatDomain.Gold | PredictorCombatDomain.Relics)) != 0),
                "a forced single Pael return preserves actual identity-independent Lucky gold and Book count hooks");
        }

        foreach (int count in new[] { 0, 3 })
        {
            var state = native.Checkpoints[0].State;
            var eligible = state.Deck.Select(state.Card).Where(card => card.Removable && card.UpgradeLevel < card.MaxUpgradeLevel)
                .Take(count).Select(card => card.Id).ToHashSet();
            // Pael admits Tooth from five removable cards, independently of
            // whether those cards still have an available upgrade.
            var small = Fixture(state with { Cards = state.Cards.Select(card => state.Deck.Contains(card.Id) && !eligible.Contains(card.Id)
                ? PredictorCardLevelEffects.Reconcile(native.Context, card with { UpgradeLevel = card.MaxUpgradeLevel }) : card).ToImmutableArray() });
            var result = Pure(small, [Plan(small, "PAELS_TOOTH")], 8, PredictorRoutePreviewPolicy.LiveMapDefaults);
            Check(result.Nodes.Length == 8 && result.Nodes.All(node => node.Advanced) && result.Nodes[0].PaelsToothStorage.IsEmpty &&
                result.Nodes[0].Inventory!.Relics.Single(relic => relic.Value.Key == K("PAELS_TOOTH")).Value.StoredCards.Length == count &&
                result.Nodes[^1].Inventory!.Relics.Single(relic => relic.Value.Key == K("PAELS_TOOTH")).Value.StoredCards.IsEmpty,
                "a known eligible population below five retains native forced-all identities and exact exhaustion " + count);
        }
        Select(run, new ChooseAncientOffer(run.AncientOffers.Single(offer => offer.Relic == K("PAELS_TOOTH")).Id));
        var missing = PredictorRouteCauseEvidence.CreateExplicit("PaelsEligiblePopulationNotObserved", run.Working.Position.NodeId,
            PredictorCombatDomain.Deck, PredictorPendingReasonKind.MissingObservation,
            "The actual removable/upgradable target population was not observed.", ["deck.PaelsTooth.count"]);
        var partial = new PredictorRun(run.Context with { Live = new(act, true, [missing]) }, run.Checkpoints, run.Revision);
        partial.RestoreRoutes(run.Routes); partial.RestoreNodeDraft(run.CaptureNodeDraft()!);
        var unknown = Pure(partial, [], 8, PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(unknown.Nodes.Length == 8 && unknown.Nodes.All(node => node.Advanced) &&
            unknown.Nodes[0].PaelsToothStorage.Single().InitialCount == null &&
            unknown.Nodes[1].PendingCauses.Any(cause => cause.Code == "PaelsToothReturnCountPending") &&
            !unknown.Nodes[1].PendingCauses.Any(cause => (cause.AffectedDomains & PredictorCombatDomain.NicheRng) != 0) &&
            unknown.Nodes[6].Inventory!.Relics.Single(relic => relic.Value.Key == K("PAELS_TOOTH")).PaelsToothStorage!.RemainingCount == 0,
            "an unknown eligible count masks its real Rewards dependency and still reaches certain exhaustion by the sixth combat");

        var eventState = native.Checkpoints[0].State;
        eventState = eventState with { VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
            Acts = eventState.Acts.Select(value => value.Act == act ? value with
                { RawEvents = [new("EVENT", "NEOW"), new("EVENT", "PUNCH_OFF")], EventCursor = 0 } : value).ToImmutableArray() };
        var eventRun = Geometry(native.Context, eventState, native.RouteMap(act), act,
            [MapPointType.Ancient, MapPointType.Unknown, MapPointType.Monster, MapPointType.Boss], startFloor: 7);
        var gift = Plan(eventRun, "PAELS_TOOTH");
        var eventEntry = Pure(eventRun, [gift], 2, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[1];
        var fight = eventEntry.Event!.EntryOptions.Single(option => option.Key == "I_CAN_TAKE_THEM").Value!;
        var eventPlan = new PredictorPlannedNode(eventEntry.Position.NodeId) { Event = new(eventEntry.Event.Event!.Value)
            { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = fight }] } };
        var eventDefault = Pure(eventRun, [gift, eventPlan], 3, PredictorRoutePreviewPolicy.LiveMapDefaults);
        var eventOptional = Pure(eventRun, [gift], 1, PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes[0]
            .OptionalObtainInputs.Single(input => input.Choice.Source == K("PAELS_TOOTH"));
        var eventTargets = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseDeckCards, K("PAELS_TOOTH"))
            { DeckCards = eventOptional.Choice.DeckCards.Where(card => card.IsKnown).Take(5).Select(card => Target(card.Value)).ToImmutableArray() };
        var eventExact = Pure(eventRun, [PredictorRun.ApplyOptionalObtainInput(gift, eventOptional, eventTargets), eventPlan], 3,
            PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(eventDefault.Nodes.Length == 3 && eventDefault.Nodes.All(node => node.Advanced) &&
            eventDefault.Nodes[1].Inventory!.Relics.Single(relic => relic.Value.Key == K("PAELS_TOOTH")).PaelsToothStorage!.RemainingCount == 4 &&
            eventDefault.Nodes[2].Inventory!.Relics.Single(relic => relic.Value.Key == K("PAELS_TOOTH")).PaelsToothStorage!.RemainingCount == 3 &&
            Cursor(eventDefault.Nodes[2], "Rewards") == Cursor(eventExact.Nodes[2], "Rewards") &&
            eventDefault.Nodes[2].Rewards.All(reward => reward.IsKnown),
            "an adopted actual event battle and its observation forks retain the before-node Pael count, then continue the ordinary combat suffix");
    }

    private static void InheritedDependencies(PredictorRun captured)
    {
        PredictorRun Prior(string ancient, PredictorRun? template = null)
        {
            var source = template ?? captured;
            var state = source.Checkpoints[0].State with { OpeningCompleted = true,
                Acts = source.Checkpoints[0].State.Acts.Select(a => a.Act == 2 ? a with { Ancient = new("EVENT", ancient) } : a).ToImmutableArray() };
            var run = Geometry(source.Context, state, source.RouteMap(2), 2,
                [MapPointType.Ancient, MapPointType.Monster, MapPointType.Ancient, MapPointType.Boss], true);
            return run;
        }
        var tez = Prior("TEZCATARA");
        var incompleteDeck = Pure(tez, []);
        var offered = incompleteDeck.Nodes[1].Ancient!;
        Check(incompleteDeck.Nodes.Length == 2 && offered.Offers[0].Value == null && offered.Offers.Skip(1).All(o => o.IsKnown) &&
            offered.Offers[0].IdentityPendingCauses.Any(c => c.Code == "PendingCardChoice"),
            "Unpicked prior card masks Tezcatara's dynamic first pool while fixed later pools remain known");
        var corrected = Pure(tez, [new(tez.Working.Position.NodeId) { LeaveRewards = true }]);
        Check(corrected.Nodes[1].Ancient!.Offers.All(o => o.IsKnown),
            "Explicit previous reward closure redraws the Ancient with exact deck conditions");

        foreach (string encounter in new[] { "FABRICATOR_NORMAL", "GREMLIN_MERC_NORMAL" })
        {
            var baseRun = Prior("DARV");
            var state = baseRun.Checkpoints[0].State;
            state = state with { Acts = state.Acts.Select(a => a.Act == 2 ? a with
                { NormalEncounters = [new("ENCOUNTER", encounter)], NormalCursor = 0 } : a).ToImmutableArray() };
            var run = new PredictorRun(baseRun.Context, state);
            run.RestoreRoutes(baseRun.Routes); run.AdvanceRoute();
            var node = new PredictorPlannedNode(run.Working.Position.NodeId)
            { LeaveRewards = true, Combat = encounter == "GREMLIN_MERC_NORMAL" ? new()
                { GremlinGold = new(true, true, null) } : null };
            var result = Pure(run, [node]);
            var cause = encounter == "FABRICATOR_NORMAL" ? "EnemyCreationCount" : "StolenGoldLoss";
            Check(result.Nodes.Length == 2 && result.Nodes[1].PendingCauses.Any(c => c.Code == cause) &&
                result.Nodes[1].Ancient!.Offers.All(o => o.IsKnown),
                "Unresolved prior Niche/Gold facts preserve independent Darv option and prepared-card identities");
        }

        var astroSource = FindAncient(captured, "DARV", "ASTROLABE");
        var astroPrior = Prior("DARV", astroSource);
        var astroEntry = astroPrior.Checkpoints[0].State;
        astroEntry = astroEntry with { Acts = astroEntry.Acts.Select(a => a.Act == 2 ? a with
            { NormalEncounters = [new("ENCOUNTER", "FABRICATOR_NORMAL")], NormalCursor = 0 } : a).ToImmutableArray() };
        var uncertain = new PredictorRun(astroPrior.Context, astroEntry);
        uncertain.RestoreRoutes(astroPrior.Routes); uncertain.AdvanceRoute();
        var before = Pure(uncertain, []);
        var ancientNode = before.Nodes[1];
        var astrolabe = ancientNode.Ancient!.Offers.Single(o => o.Value?.Relic.Entry == "ASTROLABE").Value!;
        var three = uncertain.Working.Deck.Select(uncertain.Working.Card).Where(c => c.Removable).Take(3).Select(Target).ToImmutableArray();
        var planned = new PredictorPlannedNode(ancientNode.Position.NodeId)
        {
            Ancient = new(ancientNode.Ancient.Ancient, astrolabe)
            { Children = [new(PredictorObtainChildAction.ChooseDeckCards, K("ASTROLABE")) { DeckCards = three }] }
        };
        var partial = Pure(uncertain, [planned]).Nodes[1];
        Check(partial.Phase == PredictorPhase.ObtainDeckCards && partial.Ancient!.RequiredChoices.Single().DeckCards.Any(c => c.IsKnown) &&
            partial.PendingCauses.Any(c => c.Code == "PlannedObtainTransformPending") &&
            partial.Ancient.RequiredChoices.Single().PendingCauses.Any(c => c.Code == "EnemyCreationCount"),
            "Optional prior card and unknown Niche permit the guaranteed existing Astrolabe picker, then mask its actual transform dependency");
    }
}
