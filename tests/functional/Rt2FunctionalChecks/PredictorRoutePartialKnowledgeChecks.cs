using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

// Full route calls over a small managed catalog, not private owner calls or
// fabricated diagnostics. The retained ordinary simulator produces the offers.
internal static class PredictorRoutePartialKnowledgeChecks
{
    private static readonly PredictorCard Attack = Card("STRIKE_IRONCLAD", EffectCardType.Attack);
    private static readonly PredictorCard Skill = Card("DEFEND_IRONCLAD", EffectCardType.Skill);
    private static readonly ModelKey Encounter = new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK");
    private static string FirstNode => "map:1:1:3:2";
    private static string FirstRest => "map:1:2:3:3";
    private static string SecondRest => "map:1:3:3:4";

    internal static void Run()
    {
        SkillOnlyChoiceLeavesWhetstoneKnown();
        CorrelatedWhetstoneThenWarPaint();
        UnknownSmithTargetAllowsUpgradeRelicAndBossRewards();
        RewardAlternativesKeepRouteIntentAndDependencies();
        LiveDefaultCardRewards();
        LivePotionDropsWithoutSlotManagement();
        CompletedCardChildrenRemainEditable();
        SameNodeRewardOccurrencesAndRecomputation();
        BeforeMeansBeforeRoomEntry();
        SkillOnlyChoiceLeavesTreasureWhetstoneKnown();
        NonBasicRewardLeavesPandoraTargetsKnown();
        PickerLowerBoundPreservesManualTargets();
        TerminalBoundaryBeforeEntry();
        Console.WriteLine("PREDICTOR_ROUTE_PARTIAL_KNOWLEDGE_PASS real route; selective rest/treasure obtains; correlated W/WP; reward roots; immutable recomputation; before-entry Niche; Pandora concrete branches; picker lower bounds/manual identities; terminal entry evidence");
    }

    private static PredictorCard Card(string key, EffectCardType type) => new(0, new("CARD", key), type, 0, 1, true, 1);
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static PredictorRun Route(bool onlySkills, bool twoRewards = false, bool treasureWhetstone = false,
        bool treasureCursorUnknown = false, string? treasureRelic = null, bool nicheCursorUnknown = false,
        bool unknownDowsingNext = false, string? restUpgradeRelic = null, bool potionDrops = false,
        bool potionSlotsUnknown = false)
    {
        var pool = onlySkills ? new[]
        {
            Card(unknownDowsingNext ? "DOWSING" : "CONTROLLED_SKILL_A", EffectCardType.Skill), Card("CONTROLLED_SKILL_B", EffectCardType.Skill),
            Card("CONTROLLED_SKILL_C", EffectCardType.Skill)
        } : new[]
        {
            Card("CONTROLLED_ATTACK", EffectCardType.Attack), Card("CONTROLLED_SKILL_A", EffectCardType.Skill),
            Card("CONTROLLED_SKILL_B", EffectCardType.Skill)
        };
        if (restUpgradeRelic != null)
            pool = pool.Concat(new[] { Card("CONTROLLED_RARE_A", EffectCardType.Attack),
                Card("CONTROLLED_RARE_B", EffectCardType.Skill), Card("CONTROLLED_RARE_C", EffectCardType.Power) }).ToArray();
        bool Rare(PredictorCard card) => card.Key.Entry.StartsWith("CONTROLLED_RARE_", StringComparison.Ordinal);
        var context = new PredictorContext("SKDPGF6RC1PC", "0.111.0", new("CHARACTER", "IRONCLAD"), 0,
            "route-partial-knowledge-fixture", "unlocks", [], [], [], 1)
        {
            Catalog = new(pool.Select((card, index) => new NeowEffectCardSnapshot("pool:" + index, card.Key,
                index, Rare(card) ? EffectCardRarity.Rare : EffectCardRarity.Common, card.Type, false, false, false, true, true)).ToImmutableArray(), [])
            {
                CardDefinitions = pool.Append(Attack).Append(Skill).Select(card => new PredictorCardDefinition(card,
                    card.Key == Attack.Key || card.Key == Skill.Key ? "Basic" : Rare(card) ? "Rare" : "Common",
                    new("CARD_POOL", "CONTROLLED"), false, card.Key == Attack.Key, card.Key == Skill.Key)
                {
                    Levels = Enumerable.Range(0, 2).Select(level => new PredictorCardLevelMetadata(level, 1,
                        false, false, false, false, false, false)).ToImmutableArray()
                }).ToImmutableArray(),
                Encounters = [new(Encounter, 10, 10) { IsMonsterEncounter = true }],
                TransformationPools = [new(new("CARD_POOL", "CONTROLLED"), pool.Select(card => card.Key).ToImmutableArray())]
            },
            PotionPool = Enum.GetValues<PredictorPotionRarity>().Where(rarity => rarity != PredictorPotionRarity.Event)
                .Select(rarity => new PredictorPotionOption(new("POTION", rarity switch
                { PredictorPotionRarity.Common => "ENERGY_POTION", PredictorPotionRarity.Uncommon => "BLOCK_POTION", _ => "FRUIT_JUICE" }), rarity))
                .ToImmutableArray()
        };
        if (treasureCursorUnknown) context = context with
        {
            Live = new(1, true, [PredictorRouteCauseEvidence.CreateExplicit("CapturedTreasureCursor", "capture:treasure-cursor",
                PredictorCombatDomain.TreasureRng, PredictorPendingReasonKind.MissingObservation,
                "The captured TreasureRoomRelics cursor was not observed.", ["rng.TreasureRoomRelics.state"])])
        };
        if (nicheCursorUnknown) context = context with
        {
            Live = new(1, true, [PredictorRouteCauseEvidence.CreateExplicit("CapturedNicheCursor", "capture:niche-cursor",
                PredictorCombatDomain.NicheRng, PredictorPendingReasonKind.MissingObservation,
                "The captured Niche cursor was not observed.", ["rng.Niche.state"])])
        };
        if (potionSlotsUnknown) context = context with
        {
            Live = new(1, true, [PredictorRouteCauseEvidence.CreateExplicit("CapturedPotionSlots", "capture:potions",
                PredictorCombatDomain.PotionInventory, PredictorPendingReasonKind.MissingObservation,
                "Potion occupancy was not observed.", ["potions.orderedSlots"])])
        };
        bool treasureNode = treasureWhetstone || treasureRelic != null;
        var types = new[] { MapPointType.Ancient, MapPointType.Monster,
            unknownDowsingNext ? MapPointType.Unknown : treasureNode ? MapPointType.Treasure : MapPointType.RestSite,
            MapPointType.RestSite, MapPointType.Boss };
        if (restUpgradeRelic != null)
            types = [MapPointType.Ancient, MapPointType.RestSite, MapPointType.Elite, MapPointType.RestSite, MapPointType.Boss];
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : new[] { new MapPosition(3, row + 1) })).ToArray();
        var act = new ModelKey("ACT", "OVERGROWTH");
        var map = new MapPrediction(context.Seed, act, 0, 7, points, 0);
        var relics = twoRewards ? new[] { new ModelKey("RELIC", "SHOVEL"), new ModelKey("RELIC", "PRAYER_WHEEL") }
            : new[] { new ModelKey("RELIC", "SHOVEL") };
        if (restUpgradeRelic != null) relics = [];
        if (potionDrops) relics = relics.Append(new("RELIC", "WHITE_BEAST_STATUE")).ToArray();
        var state = PredictorSeedState.Create(context, Enumerable.Repeat(Attack, 6).Concat(Enumerable.Repeat(Skill, 4)),
            relics, 99, 2, new(-.05f, 0f)) with
        {
            OpeningCompleted = true, Position = new(1, 1, "route-partial:entry"),
            Maps = [new(1, map, [points[0].Position])],
            Visits = [new(1, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
            Acts = [new(1, act, new("EVENT", "NEOW"), [Encounter], [Encounter], [Encounter], [])],
            // One real ordered bucket forces the two actual Dig obtains in order;
            // Common/Uncommon rolls follow the source fallback to this bucket.
            PersonalBag = restUpgradeRelic != null ? new(false, [], [new("Rare", [new("RELIC", restUpgradeRelic)])], [])
                : new(false, [], [new("Rare", [new("RELIC", "WHETSTONE"), new("RELIC", "WAR_PAINT")])], []),
            SharedBag = treasureNode ? new(false, [], [new("Rare", [new("RELIC", treasureRelic ?? "WHETSTONE")])], [])
                : PredictorRelicBag.Empty(true)
        };
        var run = new PredictorRun(context, state);
        run.SetRoute(1, points.Select(point => point.Position).ToImmutableArray());
        return run;
    }

    private static ImmutableArray<PredictorPlannedNode> DigPlans =>
    [
        new(FirstRest) { RestActions = [new(PredictorRestAction.Dig)], LeaveRest = true },
        new(SecondRest) { RestActions = [new(PredictorRestAction.Dig)], LeaveRest = true }
    ];

    private static PredictorRouteContinuation Preview(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans,
        int limit = 3, PredictorRoutePreviewScope scope = PredictorRoutePreviewScope.CombatAndRest,
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.AuthoredChoices)
    {
        var state = JsonSerializer.Serialize(run.Working);
        var checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var request = run.Request;
        var phase = run.Phase;
        // These checks exercise unresolved authored reward choices. Live map
        // card defaults have their own explicit policy check below.
        var result = run.PreviewRouteContinuation(plans, limit, scope: scope, policy: policy);
        Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            request == run.Request && phase == run.Phase, "Partial forecast mutated the caller's state, checkpoints or request.");
        Check(result.Nodes.All(node => node.Diagnostics != null) && result.Knowledge != null,
            "The actual route result must carry its immutable diagnostics.");
        return result;
    }

    private static PredictorRouteContinuation PreviewLive(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans,
        int limit = 3, PredictorRoutePreviewScope scope = PredictorRoutePreviewScope.CombatAndRest) =>
        Preview(run, plans, limit, scope, PredictorRoutePreviewPolicy.LiveMapDefaults);

    private static void LivePotionDropsWithoutSlotManagement()
    {
        var run = Route(onlySkills: false, potionDrops: true);
        var rests = new[] { new PredictorPlannedNode(FirstRest) { LeaveRest = true },
            new PredictorPlannedNode(SecondRest) { LeaveRest = true } }.ToImmutableArray();
        var authored = Preview(run, rests, 2);
        var live = PreviewLive(run, rests, 2);
        var authoredDrop = authored.Nodes[0].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Potion);
        var liveDrop = live.Nodes[0].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Potion);
        Check(authoredDrop.Value?.Status == PredictorRewardStatus.Taken && liveDrop.Value?.Status == PredictorRewardStatus.Open &&
            liveDrop.Value.Key == authoredDrop.Value.Key && live.Nodes.Length == 2 && live.Nodes[0].Advanced,
            "Live map keeps the source-generated potion drop visible and continues without receiving it into route slots.");
        Check(!live.PendingCauses.Any(cause => cause.Code is "PotionReceiptPending" or "PendingPotionChoice") &&
            live.Nodes[1].Inventory!.PotionSlots.IsEmpty && live.PendingCauses.Any(cause =>
                cause.Code == "MapPotionInventoryNotProjected" && cause.AffectedDomains == PredictorCombatDomain.PotionInventory),
            "Unprojected slot management is local inventory input, never a receipt choice, RNG uncertainty or continuation gate.");
        foreach (var stream in Enum.GetValues<PredictorStream>())
            Check(Known(Fact(live.Nodes[1], "rng." + stream + ".calls", before: true)) ==
                Known(Fact(authored.Nodes[1], "rng." + stream + ".calls", before: true)),
                "Potion receipt omission changed source RNG consumption: " + stream);
        Check(Fact(live.Nodes[1], "odds.potionDrop", before: true).DisplayValue ==
            Fact(authored.Nodes[1], "odds.potionDrop", before: true).DisplayValue,
            "Potion receipt omission changed source potion drop odds.");
        var explicitReceipt = PreviewLive(run, [new(FirstNode)
            { RewardChoices = [new(liveDrop.Value!, liveDrop.Occurrence)] }, .. rests], 2);
        Check(explicitReceipt.Nodes[0].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Potion).Value?.Status ==
                PredictorRewardStatus.Taken && explicitReceipt.Nodes[1].Inventory!.PotionSlots.Any(slot => slot.Value != null),
            "An explicit stored potion receipt still uses the complete simulator and preserves its observed inventory.");
        var unknown = PreviewLive(Route(onlySkills: false, potionDrops: true, potionSlotsUnknown: true), rests, 2);
        Check(unknown.Nodes.Length == 2 && unknown.Nodes[0].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Potion &&
                reward.IsKnown && reward.Value!.Status == PredictorRewardStatus.Open) &&
            !unknown.PendingCauses.Any(cause => cause.Code is "PotionReceiptPending" or "PendingPotionChoice"),
            "Unknown captured slots must not turn independent live map drops into pending receipt choices.");
        var blockedReceipt = PreviewLive(Route(onlySkills: false, potionDrops: true, potionSlotsUnknown: true),
            [new(FirstNode) { RewardChoices = [new(liveDrop.Value!, liveDrop.Occurrence)] }, .. rests], 2);
        Check(blockedReceipt.Nodes[0].PendingCauses.Any(cause => cause.Code == "PlannedPotionReceiptPending") &&
            !blockedReceipt.Nodes[0].Advanced,
            "Explicit potion receipt must still require actual slot knowledge instead of inventing an empty slot.");
        var child = PreviewLive(Route(onlySkills: false, treasureRelic: "CAULDRON"), [], 2,
            PredictorRoutePreviewScope.AllModeledNodes).Nodes[1];
        Check(child.Advanced && child.Rewards.Count(reward => reward.Kind == PredictorRewardKind.Potion &&
                reward.IsKnown && reward.Value!.Status == PredictorRewardStatus.Open) == 5,
            "Optional Cauldron child bottles remain displayed after private obtain completion without requiring potion slot actions.");
    }

    private static void LiveDefaultCardRewards()
    {
        var run = Route(onlySkills: false, twoRewards: true);
        var rests = new[] { new PredictorPlannedNode(FirstRest) { LeaveRest = true },
            new PredictorPlannedNode(SecondRest) { LeaveRest = true } }.ToImmutableArray();
        var authored = Preview(run, rests, 2);
        var live = PreviewLive(run, rests, 2);
        Check(authored.Nodes[0].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card)
                .All(reward => reward.Value?.Status == PredictorRewardStatus.Open && !reward.DefaultSkipped) &&
            live.Nodes[0].Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card)
                .All(reward => reward.Value?.Status == PredictorRewardStatus.Skipped && reward.DefaultSkipped &&
                    reward.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault && reward.AppliedChoice == null),
            "Live ordinary card defaults must publish Skip/ApprovedDefault without inventing authored actions.");
        Check(Known(Fact(live.Nodes[1], "deck.All.count", before: true)) == 10 &&
            !live.Nodes[1].DeckPendingCauses.Any(cause => cause.Code == "PendingCardChoice") &&
            Known(Fact(live.Nodes[1], "rng.Rewards.calls", before: true)) ==
                Known(Fact(authored.Nodes[1], "rng.Rewards.calls", before: true)),
            "Default Skip must retain generated reward RNG while leaving the deck known and unchanged.");
        Check(live.Nodes[0].Diagnostics!.Results.Select(live.Nodes[0].Diagnostics!.Snapshot.Fact)
                .Count(fact => fact.PropertyKey.StartsWith("reward.disposition.", StringComparison.Ordinal) &&
                    fact.Origin == PredictorKnowledgeOrigin.ApprovedDefault) == 2,
            "Both same-kind occurrences must expose their own approved default disposition facts.");

        var rerollRun = AlternativeRoute("DRIFTWOOD");
        var rest = new PredictorPlannedNode(FirstRest) { LeaveRest = true };
        var source = AlternativeCards(PreviewLive(rerollRun, [rest], 1).Nodes[0]);
        var reroll = new PredictorPlannedRewardChoice(source.Value!, source.Occurrence) { Reroll = true };
        var rerolled = PreviewLive(rerollRun, [new(FirstNode) { RewardChoices = [reroll] }, rest], 2);
        var replacement = AlternativeCards(rerolled.Nodes[0]);
        Check(replacement.Value is { HasRerolled: true, Status: PredictorRewardStatus.Skipped } &&
            replacement.AppliedChoice?.Reroll == true && replacement.DefaultSkipped &&
            replacement.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault,
            "An authored reroll-only action must run first, then default Skip its replacement.");
        var take = reroll with { ExpectedRerolledReward = replacement.Value, CardIndex = 0 };
        var taken = PreviewLive(rerollRun, [new(FirstNode) { RewardChoices = [take] }, rest], 2);
        Check(AlternativeCards(taken.Nodes[0]).Value?.Status == PredictorRewardStatus.Taken &&
            !AlternativeCards(taken.Nodes[0]).DefaultSkipped &&
            AlternativeCards(taken.Nodes[0]).DispositionOrigin == PredictorKnowledgeOrigin.PlayerInput &&
            Known(Fact(taken.Nodes[1], "deck.All.count", before: true)) == 11,
            "An authored Take must override default Skip and keep its real receipt.");

        var resting = AlternativeRoute("DRIFTWOOD", dreamCatcher: true);
        var healed = PreviewLive(resting, [new(FirstRest) { RestActions = [new(PredictorRestAction.Heal)], LeaveRest = true }], 3);
        var restReward = AlternativeCards(healed.Nodes[1]);
        Check(healed.Nodes[1].Advanced && restReward.DefaultSkipped &&
            restReward.Value?.Status == PredictorRewardStatus.Skipped &&
            Known(Fact(healed.Nodes[2], "deck.All.count", before: true)) == 10,
            "Dream Catcher reward defaults must complete their rest child while retaining final Skip disposition.");

        var accepted = AlternativeRoute("DRIFTWOOD");
        accepted.AdvanceRoute(); Send(accepted, accepted.DefaultCombatInput()!);
        var actual = accepted.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        Send(accepted, new EnterCardSelection(actual.Id)); Send(accepted, new SkipCardReward());
        var captured = AlternativeCards(PreviewLive(accepted, [rest], 1).Nodes[0]);
        Check(!captured.DefaultSkipped && captured.DispositionOrigin == PredictorKnowledgeOrigin.PlayerInput &&
            captured.Value?.Status == PredictorRewardStatus.Skipped,
            "An already accepted real Skip must retain player provenance over the policy default.");

        var nested = AlternativeRoute("PAELS_WING", sacrifices: 1, gift: "LOST_COFFER");
        var offered = AlternativeCards(PreviewLive(nested, [], 1).Nodes[0]);
        long wing = nested.Working.Relics.Single().Id;
        var sacrifice = new PredictorPlannedRewardChoice(offered.Value!, offered.Occurrence) { SacrificeRelicId = wing };
        var completed = PreviewLive(nested, [new(FirstNode) { RewardChoices = [sacrifice] }], 1).Nodes[0];
        var parent = completed.Rewards.Single(reward => reward.AppliedChoice?.SacrificeRelicId == wing);
        Check(completed.Advanced && parent.SacrificeCount == 2 && !parent.DefaultSkipped &&
            parent.DispositionOrigin == PredictorKnowledgeOrigin.PlayerInput && parent.RequiredChoices == null &&
            completed.Rewards.Any(reward => reward.DefaultSkipped &&
                reward.Value?.Status == PredictorRewardStatus.Skipped &&
                reward.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault) &&
            !completed.PendingCauses.Any(cause => cause.Code is "RewardChildChoice" or "PendingCardChoice"),
            "Authored sacrifice must take priority, then its ordinary obtained child card defaults to Skip and remains visible.");
    }

    private static void CompletedCardChildrenRemainEditable()
    {
        var run = AlternativeRoute("PAELS_WING", sacrifices: 1, gift: "ORRERY");
        long wing = run.Working.Relics.Single().Id;
        var offer = AlternativeCards(PreviewLive(run, [], 1).Nodes[0]);
        var parent = new PredictorPlannedRewardChoice(offer.Value!, offer.Occurrence) { SacrificeRelicId = wing };
        var plan = new PredictorPlannedNode(FirstNode) { RewardChoices = [parent] };
        var first = PreviewLive(run, [plan], 1).Nodes[0];
        var children = first.Rewards.Where(reward => reward.CompletedEdit != null).ToArray();
        Check(first.Advanced && children.Length == 5 && children.All(reward => reward.DefaultSkipped &&
            reward.CompletedEdit is { Source.Entry: "ORRERY", Owner: PredictorRewardEditOwner.Reward } &&
            reward.RequiredChoices == null), "Five completed Orrery rewards must retain editable source/parent paths without waiting choices.");
        var choice = new PredictorPlannedRewardChoice(children[2].Value!, children[2].Occurrence, CardIndex: 0);
        var takenPlan = PredictorRun.ApplyCompletedRewardChoice(plan, children[2].CompletedEdit!, choice);
        Check(takenPlan.RewardChoices.Length == 1 && takenPlan.RewardChoices[0].SacrificeRelicId == wing &&
            takenPlan.RewardChoices[0].Children.Single().ExpectedSource.Entry == "ORRERY",
            "Editing a completed child must update the existing sacrifice's Children instead of inventing another root reward.");
        var taken = PreviewLive(run, [takenPlan], 1).Nodes[0];
        var takenChild = taken.Rewards.Single(reward => reward.CompletedEdit != null && reward.Occurrence == 2);
        Check(taken.Advanced && takenChild.Value?.Status == PredictorRewardStatus.Taken && takenChild.CompletedEdit != null &&
            takenChild.AppliedChoice?.CardIndex == 0 && !takenChild.DefaultSkipped &&
            taken.Rewards.Count(reward => reward.DefaultSkipped && reward.CompletedEdit != null) == 4,
            "An explicit child Take must override only its own default and remain editable after completion.");
        var skippedPlan = PredictorRun.ApplyCompletedRewardChoice(takenPlan, takenChild.CompletedEdit!, choice with { CardIndex = null, Skip = true });
        var skipped = PreviewLive(run, [skippedPlan], 1).Nodes[0];
        var skippedChild = skipped.Rewards.Single(reward => reward.CompletedEdit != null && reward.Occurrence == 2);
        Check(skipped.Advanced && skippedChild.Value?.Status == PredictorRewardStatus.Skipped &&
            skippedChild.AppliedChoice?.Skip == true && !skippedChild.DefaultSkipped &&
            skippedChild.DispositionOrigin == PredictorKnowledgeOrigin.PlayerInput &&
            skippedPlan.RewardChoices[0].Children.Length == 1,
            "Revising a completed child Take to explicit Skip must replace that child action and retain player provenance.");

        var capturedRun = AlternativeRoute("PAELS_WING", sacrifices: 1, gift: "ORRERY");
        capturedRun.AdvanceRoute(); Send(capturedRun, capturedRun.DefaultCombatInput()!);
        var capturedParent = capturedRun.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        long capturedWing = capturedRun.Working.Relics.Single().Id;
        Send(capturedRun, new EnterCardSelection(capturedParent.Id));
        Send(capturedRun, new SacrificeCardReward(capturedParent.Id, capturedWing));
        var capturedNode = PreviewLive(capturedRun, [], 1).Nodes[0];
        var capturedChild = capturedNode.Rewards.First(reward => reward.CompletedEdit != null);
        var capturedPlan = PredictorRun.ApplyCompletedRewardChoice(new(FirstNode), capturedChild.CompletedEdit!,
            new(capturedChild.Value!, capturedChild.Occurrence, CardIndex: 0));
        var revisedCapture = PreviewLive(capturedRun, [capturedPlan], 1).Nodes[0];
        Check(revisedCapture.Advanced && revisedCapture.Rewards.Any(reward => reward.CompletedEdit != null &&
                reward.Value?.Status == PredictorRewardStatus.Taken) &&
            revisedCapture.Inventory!.Relics.Single(relic => relic.Value.Key.Entry == "PAELS_WING").Value.RewardsSacrificed == 2,
            "Editing an actual accepted sacrifice's child must bind its suspended source without sacrificing again.");

        var treasureBase = Route(onlySkills: true, treasureRelic: "ORRERY");
        var state = treasureBase.Working;
        var treasureRun = new PredictorRun(treasureBase.Context, state with
        { Relics = state.Relics.Add(new(state.NextInstanceId, new("RELIC", "DRIFTWOOD"), 1)), NextInstanceId = state.NextInstanceId + 1 });
        treasureRun.SetRoute(1, treasureBase.Routes.Single().Points);
        var treasure = PreviewLive(treasureRun, [], 2, PredictorRoutePreviewScope.AllModeledNodes).Nodes[1];
        var reward = treasure.Rewards.First(item => item.CompletedEdit != null && item.Value?.CanReroll == true);
        var reroll = new PredictorPlannedRewardChoice(reward.Value!, reward.Occurrence) { Reroll = true };
        var rerolledPlan = PredictorRun.ApplyCompletedRewardChoice(new(FirstRest), reward.CompletedEdit!, reroll);
        var rerolled = PreviewLive(treasureRun, [rerolledPlan], 2, PredictorRoutePreviewScope.AllModeledNodes).Nodes[1];
        var replacement = rerolled.Rewards.Single(item => item.CompletedEdit != null && item.Occurrence == reward.Occurrence);
        Check(rerolled.Advanced && replacement.Value is { HasRerolled: true, Status: PredictorRewardStatus.Skipped } &&
            replacement.DefaultSkipped && replacement.AppliedChoice?.Reroll == true && replacement.CompletedEdit != null,
            "A completed treasure child must reopen through its typed pickup prefix, reroll once, then default Skip the new offer. " +
            $"Phase={rerolled.Phase}; Advanced={rerolled.Advanced}; Reward={replacement.Value?.Status}; " +
            $"HasRerolled={replacement.Value?.HasRerolled}; DefaultSkipped={replacement.DefaultSkipped}; " +
            $"AppliedReroll={replacement.AppliedChoice?.Reroll}; Edit={replacement.CompletedEdit != null}.");
    }

    private static PredictorKnowledgeFact Fact(PredictorRouteNodeKnowledge node, string property, bool before = false)
    {
        var diagnostics = node.Diagnostics!;
        return (before ? diagnostics.Before : diagnostics.After).Select(diagnostics.Snapshot.Fact)
            .Single(fact => fact.PropertyKey == property);
    }
    private static long Known(PredictorKnowledgeFact fact)
    {
        Check(fact.Status == PredictorKnowledgeStatus.Known && fact.DisplayValue != null,
            "Expected a proven value for " + fact.PropertyKey + ", got " + fact.Status + ": " + fact.DisplayValue);
        return long.Parse(fact.DisplayValue!, CultureInfo.InvariantCulture);
    }
    private static ImmutableArray<PredictorPlannedRewardChoice> SkipCards(PredictorRouteNodeKnowledge node) =>
        node.Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown)
            .Select(reward => new PredictorPlannedRewardChoice(reward.Value!, reward.Occurrence, Skip: true)).ToImmutableArray();

    private static void SkillOnlyChoiceLeavesWhetstoneKnown()
    {
        var run = Route(onlySkills: true);
        var forecast = Preview(run, DigPlans, 2);
        Check(forecast.Nodes.Length == 2 && forecast.Nodes[1].Advanced && forecast.Nodes[1].AppliedRestActions == 1 &&
            !forecast.Nodes[1].PendingCauses.Any(cause => cause.Code == "RestObtainDependent"),
            "A Skill-only reward/Skip must not stop the later Whetstone obtain.");
        var attackTargets = Fact(forecast.Nodes[1], "deck.UpgradableAttack.candidates");
        Check(Known(Fact(forecast.Nodes[1], "deck.UpgradableAttack.count")) == 4 &&
            attackTargets.Status == PredictorKnowledgeStatus.Known &&
            !forecast.Nodes[1].Diagnostics!.Snapshot.Explain(attackTargets.Id).Roots.Any(root => root.NodeId == FirstNode),
            "The independent Whetstone targets must remain determined despite a Skill-only reward/Skip.");
        Check(Known(Fact(forecast.Nodes[1], "rng.Niche.calls")) == 6,
            "One initial creature and six Attack candidates must consume one plus five Niche draws.");
    }

    private static void TerminalBoundaryBeforeEntry()
    {
        var baseline = Route(onlySkills: true);
        var missing = PredictorRouteCauseEvidence.CreateExplicit("CapturedDepartureGate", "capture:departure",
            PredictorCombatDomain.Continuation, PredictorPendingReasonKind.MissingObservation,
            "The captured departure prerequisite is unavailable.", ["flow.departure.allowed"]);
        var blocked = new PredictorRun(baseline.Context with { Live = new(1, true, [missing]) }, baseline.Working);
        blocked.RestoreRoutes(baseline.Routes);
        var first = Preview(blocked, [], scope: PredictorRoutePreviewScope.AllModeledNodes);
        var terminal = first.TerminalBoundary;
        Check(first.Nodes.IsEmpty && terminal != null && terminal.TargetPosition.NodeId == FirstNode && !first.ReachedLimit &&
            terminal.DeparturePosition == blocked.Working.Position && terminal.Diagnostics != null,
            "A first-entry blocker disappeared or fabricated an entered node.");
        var diagnostics = terminal!.Diagnostics!;
        Check(!diagnostics.Before.IsEmpty && diagnostics.After.IsEmpty && ReferenceEquals(first.Knowledge, diagnostics.Snapshot),
            "A terminal must share the forecast snapshot and retain only its entry-before frame.");
        var entered = diagnostics.Results.Select(diagnostics.Snapshot.Fact).Single(fact => fact.PropertyKey == "route.entry.entered");
        var allowed = diagnostics.Results.Select(diagnostics.Snapshot.Fact).Single(fact => fact.PropertyKey == "route.entry.allowed");
        Check(entered.DisplayValue == "False" && allowed.DisplayValue == null &&
            diagnostics.Snapshot.Explain(allowed.Id).Roots.Any(root => root.RootKey == missing.Identity) &&
            diagnostics.Before.Select(diagnostics.Snapshot.Fact).Single(fact => fact.PropertyKey == "rng.Niche.calls").DisplayValue == "0",
            "First-entry evidence lost the actual source root or consumed creature creation RNG before entry.");

        // An actual prior card reward may add Dowsing. CanProjectNextRouteNode
        // blocks only the subsequent ? entry, leaving the completed combat node
        // and its original card-choice identity intact.
        var pendingRun = Route(onlySkills: true, unknownDowsingNext: true);
        var pending = Preview(pendingRun, [], scope: PredictorRoutePreviewScope.AllModeledNodes);
        Check(pending.Nodes.Length == 1 && pending.Nodes[0].Advanced &&
            pending.TerminalBoundary?.TargetPosition.NodeId == FirstRest &&
            pending.TerminalBoundary.PendingCauses.Any(cause => cause.Code == "UnknownRoomDowsingPending"),
            "A genuine optional Dowsing reward failed to preserve the blocked next-room boundary.");
        var gap = pending.TerminalBoundary!.Diagnostics!.Results.Select(pending.Knowledge!.Fact)
            .Single(fact => fact.PropertyKey == "route.entry.allowed");
        Check(pending.Knowledge.Explain(gap.Id).Roots.Any(root => root.NodeId == FirstNode &&
            root.Kind == PredictorKnowledgeRootKind.UserChoice) && pending.TerminalBoundary.Diagnostics.After.IsEmpty &&
            ReferenceEquals(pending.Nodes[0].Diagnostics!.Snapshot, pending.TerminalBoundary.Diagnostics.Snapshot),
            "The terminal cause chain detached from the original reward choice or published an after-entry witness.");
        var noDowsing = pending.Nodes[0].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown);
        Check(noDowsing.Value!.Cards.Any(card => card.Key.Entry == "DOWSING"), "The controlled source offer lacks actual Dowsing.");

        // Recompute only through the already completed combat. This selected
        // prefix stops normally, without pretending another entry was blocked.
        var prefix = new PredictorRun(baseline.Context, baseline.Working);
        prefix.SetRoutePreviewPrefix(1, baseline.Routes.Single().Points.Take(2).ToImmutableArray());
        var resolved = prefix.PreviewRouteContinuation([], 3, scope: PredictorRoutePreviewScope.AllModeledNodes,
            policy: PredictorRoutePreviewPolicy.LiveMapDefaults, allowSelectedPrefixes: true);
        Check(resolved.Nodes.Length == 1 && resolved.TerminalBoundary == null,
            "Normal ready-prefix exhaustion was mislabeled as a blocked entry.");
        PredictorTerminalBoundaryUiChecks.Run(first, resolved);
    }

    private static void CorrelatedWhetstoneThenWarPaint()
    {
        var run = Route(onlySkills: false);
        var forecast = Preview(run, DigPlans);
        Check(forecast.Nodes.Length == 3 && forecast.Nodes.Skip(1).All(node => node.Advanced && node.AppliedRestActions == 1),
            "Both typed random-upgrade obtains must continue on the partial deck.");
        var whetstone = forecast.Nodes[1];
        var warPaint = forecast.Nodes[2];
        var attackCount = Fact(whetstone, "deck.UpgradableAttack.count");
        var skillCount = Fact(warPaint, "deck.UpgradableSkill.count");
        Check(attackCount.Status == PredictorKnowledgeStatus.Partial && attackCount.DisplayValue == "4 … 5" &&
            skillCount.Status == PredictorKnowledgeStatus.Partial && skillCount.DisplayValue == "2 … 3",
            "The actual W/WP sequence must retain its typed population counts after two upgrades each.");
        var niche = Fact(warPaint, "rng.Niche.calls");
        Check(niche.Status == PredictorKnowledgeStatus.Partial && niche.DisplayValue == "9 … 10",
            "One Attack/Skill/Skip choice permits only 8 or 9 shuffle draws plus one creature draw, never 10 shuffle draws.");
        var targetExplanation = whetstone.Diagnostics!.Snapshot.Explain(Fact(whetstone, "deck.UpgradableAttack.candidates").Id);
        Check(targetExplanation.Roots.Any(root => root.Kind == PredictorKnowledgeRootKind.UserChoice && root.NodeId == FirstNode) &&
            targetExplanation.Roots.All(root => root.Kind != PredictorKnowledgeRootKind.ImplementationGap),
            "Random upgrade target uncertainty must retain the original reward, not become an invented implementation gap.");
        var explanations = warPaint.Diagnostics!.Snapshot.Explain(niche.Id);
        Check(explanations.Roots.Count(root => root.Kind == PredictorKnowledgeRootKind.UserChoice && root.NodeId == FirstNode) == 1 &&
            explanations.Roots.All(root => root.Kind != PredictorKnowledgeRootKind.ImplementationGap),
            "Cumulative Niche consumption must refer to the one original correlated reward, with no invented full-deck input.");
    }

    private static void UnknownSmithTargetAllowsUpgradeRelicAndBossRewards()
    {
        foreach (string relic in new[] { "WAR_PAINT", "WHETSTONE" })
        {
            var run = Route(onlySkills: false, restUpgradeRelic: relic);
            Check(run.Working.Relics.IsEmpty, "The upgrade/Boss fixture must have no Wing Charm or optional rest reward hooks.");
            // Even without character starter metadata, the drawing retains
            // the upgrade opportunity without choosing a concrete target.
            var defaulted = PreviewLive(run, [], 2);
            Check(defaulted.Nodes[0].RestDefaultApplied && defaulted.Nodes[0].DefaultRestAction is
                { Action: PredictorRestAction.Smith, SmithCard: null } &&
                Fact(defaulted.Nodes[1], "deck.UpgradableAttack.count", before: true).Status == PredictorKnowledgeStatus.Unknown,
                "A default Smith leaves target-dependent knowledge unresolved without requiring starter identities.");
            var pendingSmith = new PredictorPlannedNode(FirstNode) { RestActions = [new(PredictorRestAction.Smith)] };
            string pendingSmithIntent = JsonSerializer.Serialize(pendingSmith);
            var offered = PreviewLive(run, [pendingSmith], 2);
            // The drawing has processed the selected Smith action, without
            // inventing a card target or rewriting the original targetless plan.
            Check(offered.Nodes.Length == 2 && offered.Nodes[0].Room == PredictorRoomKind.RestSite &&
                offered.Nodes[0].Advanced && offered.Nodes[0].AppliedRestActions == 1 &&
                !offered.Nodes[0].RestDefaultApplied &&
                offered.Nodes[0].PendingCauses.Any(cause => cause.Code == "PlannedRestTargetPending") &&
                !offered.Nodes[0].DeckPendingCauses.IsEmpty && JsonSerializer.Serialize(pendingSmith) == pendingSmithIntent &&
                offered.Nodes[0].RestOffers.Select(offer => offer.Action).ToHashSet().SetEquals(
                    new[] { PredictorRestAction.Heal, PredictorRestAction.Smith }),
                "The preceding explicit Smith must retain its unresolved target and original rest offers.");
            var eliteOffer = offered.Nodes[1];
            string scope = relic == "WAR_PAINT" ? "UpgradableSkill" : "UpgradableAttack";
            Check(eliteOffer.Room == PredictorRoomKind.Elite &&
                Fact(eliteOffer, "deck." + scope + ".count", before: true).Status == PredictorKnowledgeStatus.Unknown,
                "The preceding rest's unresolved deck operation must leave the typed upgrade count unproven.");
            var reward = eliteOffer.Rewards.Single(value => value.Kind == PredictorRewardKind.Relic && value.IsKnown);
            Check(reward.Value!.Key.Entry == relic, "The elite must generate the actual requested random-upgrade relic.");
            PredictorPlannedNode Plan(bool skip) => new(eliteOffer.Position.NodeId)
            { RewardChoices = [new(reward.Value!, reward.Occurrence, Skip: skip)] };
            var taken = PreviewLive(run, [pendingSmith, Plan(false)], 4);
            var skipped = PreviewLive(run, [pendingSmith, Plan(true)], 4);
            var expectedRooms = new[] { PredictorRoomKind.RestSite, PredictorRoomKind.Elite,
                PredictorRoomKind.RestSite, PredictorRoomKind.Boss };
            Check(taken.Nodes.Select(node => node.Room).SequenceEqual(expectedRooms) &&
                skipped.Nodes.Select(node => node.Room).SequenceEqual(expectedRooms) &&
                taken.Nodes.Take(3).All(node => node.Advanced) && skipped.Nodes.Take(3).All(node => node.Advanced),
                "Explicit Take and Skip must both continue through the later rest to the Boss despite unknown upgrade candidates.");
            var takenElite = taken.Nodes[1];
            var skippedElite = skipped.Nodes[1];
            Check(takenElite.Rewards.Single(value => value.Kind == PredictorRewardKind.Relic).Value?.Status == PredictorRewardStatus.Taken &&
                skippedElite.Rewards.Single(value => value.Kind == PredictorRewardKind.Relic).Value?.Status == PredictorRewardStatus.Skipped &&
                !takenElite.PendingCauses.Any(cause => cause.Code == "PlannedRelicObtainDependent"),
                "The forecast must preserve the explicit relic disposition rather than silently omit its obtain.");
            Check(Fact(takenElite, "rng.Niche.calls").Status == PredictorKnowledgeStatus.Unknown &&
                Fact(takenElite, "deck." + scope + ".candidates").Status == PredictorKnowledgeStatus.Unknown &&
                Known(Fact(skippedElite, "rng.Niche.calls")) == 1,
                "Taking an unknown-population upgrade must retain unknown Niche/targets; Skip consumes only the entry creature draw.");
            Check(!takenElite.Cards.Any(card => card.IsKnown && card.Value.UpgradeLevel > 0) &&
                PredictorRouteCauseEvidence.OriginalCauses(takenElite.PendingCauses).Any(cause =>
                    cause.Code == "PlannedRestTargetPending" && cause.NodeId == FirstNode),
                "Unproven upgrade identities must not be published as known cards or erase the original rest choice.");
            var takenBoss = taken.Nodes[^1];
            var skippedBoss = skipped.Nodes[^1];
            PredictorReward BossCards(PredictorRouteNodeKnowledge node) => node.Rewards.Single(value =>
                value.Kind == PredictorRewardKind.Card && value.IsKnown && value.Value!.RerollRecipe?.IsFromCombat == true).Value!;
            var takenCards = BossCards(takenBoss);
            var skippedCards = BossCards(skippedBoss);
            Check(takenCards.Cards.Length == 3 && takenCards.Cards.All(card =>
                    run.Context.Catalog!.CardDefinitions.Single(definition => definition.Prototype.Key == card.Key).Rarity == "Rare") &&
                takenCards.Cards.SequenceEqual(skippedCards.Cards),
                "Without Wing Charm the Boss's actual basic rare-card options must stay known and identical for Take and Skip.");
            Check(Known(Fact(takenBoss, "rng.Rewards.calls")) == Known(Fact(skippedBoss, "rng.Rewards.calls")) &&
                Fact(takenBoss, "rng.Rewards.state").Status == PredictorKnowledgeStatus.Known &&
                Fact(takenBoss, "rng.Rewards.state").DisplayValue == Fact(skippedBoss, "rng.Rewards.state").DisplayValue &&
                Fact(takenBoss, "rng.Niche.calls").Status == PredictorKnowledgeStatus.Unknown &&
                Fact(skippedBoss, "rng.Niche.calls").Status == PredictorKnowledgeStatus.Known &&
                PredictorRouteCauseEvidence.OriginalCauses(takenBoss.PendingCauses).Any(cause =>
                    cause.Code == "PlannedRestTargetPending" && cause.NodeId == FirstNode),
                "The unknown upgrade must remain confined to Niche/deck evidence while the Boss Rewards cursor remains proven.");
        }
    }

    private static PredictorRun AlternativeRoute(string relic, int sacrifices = 0, string gift = "CIRCLET", bool dreamCatcher = false)
    {
        var baseline = Route(onlySkills: true);
        var map = baseline.Working.Maps.Single();
        var state = baseline.Working with
        {
            Maps = [map with { Map = map.Map with { Points = map.Map.Points.Select(point =>
                point.Position.Row == 3 ? point with { Type = MapPointType.Monster } : point).ToArray() } }],
            Relics = [new(baseline.Working.NextInstanceId, new("RELIC", relic), 1) { RewardsSacrificed = sacrifices }],
            NextInstanceId = baseline.Working.NextInstanceId + 1,
            PersonalBag = new(false, [], [new("Rare", [new("RELIC", gift)])], [])
        };
        if (dreamCatcher) state = state with
        {
            Relics = state.Relics.Add(new(state.NextInstanceId, new("RELIC", "DREAM_CATCHER"), 1)),
            NextInstanceId = state.NextInstanceId + 1
        };
        var run = new PredictorRun(baseline.Context, state);
        run.SetRoute(1, baseline.Routes.Single().Points);
        return run;
    }

    private static PredictorCombatRewardKnowledge AlternativeCards(PredictorRouteNodeKnowledge node) =>
        node.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);

    private static void RewardAlternativesKeepRouteIntentAndDependencies()
    {
        var run = AlternativeRoute("DRIFTWOOD");
        var rest = new PredictorPlannedNode(FirstRest) { LeaveRest = true };
        var unselected = Preview(run, [rest], 3);
        var source = AlternativeCards(unselected.Nodes[0]);
        Check(source.IsKnown && source.Value!.CanReroll && !AlternativeCards(unselected.Nodes[2]).IsKnown,
            "An unresolved available reroll must preserve its source offer but mask affected later Rewards results.");
        Check(Fact(unselected.Nodes[1], "rng.Niche.calls", before: true).Status == PredictorKnowledgeStatus.Known &&
            PredictorRouteCauseEvidence.OriginalCauses(unselected.Nodes[2].PendingCauses).Any(cause => cause.NodeId == FirstNode),
            "Pending reroll must retain the original reward choice without contaminating independent Niche.");
        var reroll = new PredictorPlannedRewardChoice(source.Value!, source.Occurrence) { Reroll = true };
        PredictorPlannedNode First(PredictorPlannedRewardChoice choice) => new(FirstNode) { RewardChoices = [choice] };
        var rerolled = Preview(run, [First(reroll), rest], 3);
        var replacement = AlternativeCards(rerolled.Nodes[0]);
        Check(replacement.IsKnown && replacement.Value is { HasRerolled: true, CanReroll: false } &&
            replacement.AppliedChoice?.Reroll == true && AlternativeCards(rerolled.Nodes[2]).IsKnown,
            "A planned single reroll must publish its replacement, remain unselected, and restore determined Rewards progress.");
        var taken = Preview(run, [First(reroll with { ExpectedRerolledReward = replacement.Value, CardIndex = 0 }), rest], 3);
        var skipped = Preview(run, [First(reroll with { ExpectedRerolledReward = replacement.Value, Skip = true }), rest], 3);
        Check(AlternativeCards(taken.Nodes[0]).Value?.Status == PredictorRewardStatus.Taken &&
            AlternativeCards(skipped.Nodes[0]).Value?.Status == PredictorRewardStatus.Skipped &&
            Known(Fact(taken.Nodes[1], "deck.All.count", before: true)) == 11 &&
            Known(Fact(skipped.Nodes[1], "deck.All.count", before: true)) == 10 &&
            Known(Fact(taken.Nodes[1], "rng.Rewards.calls", before: true)) == Known(Fact(skipped.Nodes[1], "rng.Rewards.calls", before: true)),
            "Revising Take to Skip must preserve the reroll and its RNG consumption without keeping the prior card receipt.");
        var plainSkip = Preview(run, [First(new(source.Value!, source.Occurrence, Skip: true)), rest], 3);
        Check(Known(Fact(plainSkip.Nodes[1], "rng.Rewards.calls", before: true)) <
            Known(Fact(skipped.Nodes[1], "rng.Rewards.calls", before: true)), "Removing reroll must recompute the suffix without its draws.");
        var stale = Preview(run, [First(reroll with { ExpectedRerolledReward = replacement.Value! with
            { Cards = replacement.Value!.Cards.SetItem(0, replacement.Value.Cards[0] with { UpgradeLevel = 1 }) }, CardIndex = 0 }), rest], 3);
        Check(stale.Nodes[0].PendingCauses.Any(cause => cause.Code.Contains("Changed", StringComparison.Ordinal)) &&
            AlternativeCards(stale.Nodes[0]).Value?.Status != PredictorRewardStatus.Taken,
            "An obsolete post-reroll card binding must remain pending instead of taking a replacement silently.");

        var concrete = AlternativeRoute("DRIFTWOOD");
        concrete.AdvanceRoute(); Send(concrete, concrete.DefaultCombatInput()!);
        while (PredictorRewardPickupPolicy.Plan(concrete).NextInput is { } input) Send(concrete, input);
        var actual = concrete.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        Send(concrete, new EnterCardSelection(actual.Id)); Send(concrete, new RerollCardReward(actual.Id));
        Check(concrete.Rewards.Single(reward => reward.Id == actual.Id).Cards.Select(card => card with { Id = 0 })
            .SequenceEqual(replacement.Value!.Cards), "Planned reroll cards must agree with actual detached settlement.");

        var wing = AlternativeRoute("PAELS_WING");
        long wingId = wing.Working.Relics.Single().Id;
        var pending = Preview(wing, [rest], 3);
        var firstSource = AlternativeCards(pending.Nodes[0]);
        Check(firstSource.IsKnown && pending.Nodes[0].Advanced,
            "An unresolved sacrifice must keep its own source offer actionable and continue independent work.");
        var firstSacrifice = new PredictorPlannedRewardChoice(firstSource.Value!, firstSource.Occurrence) { SacrificeRelicId = wingId };
        var once = Preview(wing, [First(firstSacrifice), rest], 3);
        Check(AlternativeCards(once.Nodes[0]).SacrificeCount == 1 && AlternativeCards(once.Nodes[0]).SacrificeRelic == null &&
            Known(Fact(once.Nodes[1], "deck.All.count", before: true)) == 10,
            "First sacrifice must increment the real counter without adding a card or inventing a relic.");
        var second = AlternativeCards(once.Nodes[2]);
        Check(second.IsKnown, "Resolving the first sacrifice must make the later reward offer computable.");
        var twice = Preview(wing, [First(firstSacrifice), rest, new(SecondRest)
            { RewardChoices = [new(second.Value!, second.Occurrence) { SacrificeRelicId = wingId }] }], 3);
        Check(AlternativeCards(twice.Nodes[2]).SacrificeCount == 2 &&
            AlternativeCards(twice.Nodes[2]).SacrificeRelic?.Entry == "CIRCLET" && twice.Nodes[2].Advanced,
            "The second planned sacrifice must obtain the next relic and complete that reward.");
        var reversed = Preview(wing, [First(new(firstSource.Value!, firstSource.Occurrence, Skip: true)), rest], 3);
        Check(AlternativeCards(reversed.Nodes[0]).SacrificeCount == null &&
            AlternativeCards(reversed.Nodes[2]).IsKnown &&
            Fact(reversed.Nodes[1], "rng.Rewards.calls", before: true).Status == PredictorKnowledgeStatus.Known,
            "Changing sacrifice to Skip must discard its counter/receipt projection and clear obsolete pending causes.");

        var nestedRun = AlternativeRoute("PAELS_WING", sacrifices: 1, gift: "LOST_COFFER");
        long nestedWing = nestedRun.Working.Relics.Single().Id;
        var nestedOffer = AlternativeCards(Preview(nestedRun, [], 1).Nodes[0]);
        var parentChoice = new PredictorPlannedRewardChoice(nestedOffer.Value!, nestedOffer.Occurrence)
            { SacrificeRelicId = nestedWing };
        var childPending = Preview(nestedRun, [First(parentChoice)], 1);
        var child = AlternativeCards(childPending.Nodes[0]).RequiredChoices!;
        Check(child is { Source.Entry: "LOST_COFFER", Phase: PredictorPhase.Rewards },
            "Sacrifice must suspend the parent picker while its obtained relic owns child rewards.");
        var childOffer = child.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
        var nestedSelected = parentChoice with { Children =
        [
            new(PredictorObtainChildAction.Reward, child.Source)
                { Reward = new(childOffer.Value!, childOffer.Occurrence) { SacrificeRelicId = nestedWing } },
            new(PredictorObtainChildAction.LeaveRewards, child.Source)
        ] };
        var nestedComplete = Preview(nestedRun, [First(nestedSelected), rest], 2);
        Check(nestedComplete.Nodes[0].Advanced && AlternativeCards(nestedComplete.Nodes[0]).AppliedChildren == 2 &&
            AlternativeCards(nestedComplete.Nodes[0]).RequiredChoices == null &&
            AlternativeCards(nestedComplete.Nodes[0]).SacrificeCount == 2,
            "A child reward may sacrifice once more, then unwind to the original completed sacrifice without replacing its source.");

        foreach (string relic in new[] { "DRIFTWOOD", "PAELS_WING" })
        {
            var resting = AlternativeRoute(relic, dreamCatcher: true);
            var first = AlternativeCards(PreviewLive(resting, [], 1).Nodes[0]);
            var skipFirst = First(new(first.Value!, first.Occurrence, Skip: true));
            var heal = new PredictorPlannedNode(FirstRest) { RestActions = [new(PredictorRestAction.Heal)], LeaveRest = true };
            var rested = PreviewLive(resting, [skipFirst, heal], 2);
            var restOffer = AlternativeCards(rested.Nodes[1]);
            var restChoice = relic == "DRIFTWOOD"
                ? new PredictorPlannedRewardChoice(restOffer.Value!, restOffer.Occurrence) { Reroll = true }
                : new PredictorPlannedRewardChoice(restOffer.Value!, restOffer.Occurrence)
                    { SacrificeRelicId = resting.Working.Relics.Single(item => item.Key.Entry == relic).Id };
            var selected = PreviewLive(resting, [skipFirst, heal with { RewardChoices = [restChoice] }], 3);
            var result = AlternativeCards(selected.Nodes[1]);
            Check(selected.Nodes.Length == 3 && selected.Nodes[1].Advanced &&
                (relic == "DRIFTWOOD" ? result.Value?.HasRerolled == true : result.SacrificeCount == 1),
                "Dream Catcher rewards must share the same reroll/sacrifice intent and rest continuation.");
        }
    }

    private static void SameNodeRewardOccurrencesAndRecomputation()
    {
        var run = Route(onlySkills: false, twoRewards: true);
        ImmutableArray<PredictorPlannedNode> leave = [new(FirstRest) { LeaveRest = true }, new(SecondRest) { LeaveRest = true }];
        var old = Preview(run, leave, 2);
        var roots = PredictorRouteCauseEvidence.OriginalCauses(old.Nodes[1].PendingCauses)
            .Where(cause => cause.Code == "PendingCardChoice" && cause.NodeId == FirstNode).ToArray();
        Check(roots.Length == 2 && roots.Select(cause => cause.Identity).Distinct().Count() == 2 &&
            roots.Select(cause => cause.ChoiceId).Distinct().Count() == 2 &&
            roots.Select(cause => cause.Choice!.Occurrence).Order().SequenceEqual(new[] { 0, 1 }),
            "Two same-kind rewards from the same node must retain both occurrence causes.");
        var oldDiagnostics = old.Nodes[1].Diagnostics!;
        var oldCount = Fact(old.Nodes[1], "deck.All.count", before: true);
        var oldExplanation = oldDiagnostics.Snapshot.Explain(oldCount.Id);
        Check(oldExplanation.Roots.Count(root => root.Kind == PredictorKnowledgeRootKind.UserChoice && root.NodeId == FirstNode) == 2,
            "The actual next-node count diagnosis must retain both original reward choices.");
        var corrected = Preview(run, leave.Add(new(FirstNode) { RewardChoices = SkipCards(old.Nodes[0]) }), 2);
        Check(Known(Fact(corrected.Nodes[1], "deck.All.count", before: true)) == 10 &&
            !PredictorRouteCauseEvidence.OriginalCauses(corrected.Nodes[1].PendingCauses)
                .Any(cause => cause.Code == "PendingCardChoice" && cause.NodeId == FirstNode),
            "Explicitly skipping both earlier rewards must remove their recomputed suffix gaps.");
        Check(corrected.Nodes[1].Diagnostics!.Snapshot.ForecastId != oldDiagnostics.Snapshot.ForecastId &&
            ReferenceEquals(oldCount, oldDiagnostics.Snapshot.Fact(oldCount.Id)) &&
            oldDiagnostics.Snapshot.Explain(oldCount.Id).Roots.SequenceEqual(oldExplanation.Roots) &&
            oldCount.Status == PredictorKnowledgeStatus.Partial && oldCount.DisplayValue == "10 … 12",
            "New forecasts must not mutate the old diagnostic fact or its explanation.");
        // Removing the authored skips must recompute the unresolved result,
        // including both choice occurrences, rather than retaining the prior
        // forecast's resolved knowledge or a settled RNG witness.
        var replay = Preview(run, leave, 2);
        string Values(PredictorRouteContinuation forecast) => JsonSerializer.Serialize(forecast.Nodes.Select(node => new
        {
            node.Position, node.Phase, node.Advanced,
            Rewards = node.Rewards.Select(reward => new { reward.Value, reward.IsKnown, reward.Occurrence }),
            Facts = node.Diagnostics!.Before.Concat(node.Diagnostics.After).Select(node.Diagnostics.Snapshot.Fact)
                .Select(fact => new { fact.Boundary, fact.PropertyKey, fact.Status, fact.Origin, fact.DisplayValue }),
            Causes = PredictorRouteCauseEvidence.OriginalCauses(node.PendingCauses).Select(cause => new
            { cause.Code, cause.NodeId, cause.AffectedDomains, cause.ReasonKind, Occurrence = cause.Choice?.Occurrence })
        }));
        Check(Values(old) == Values(replay) && replay.Knowledge!.ForecastId != corrected.Knowledge!.ForecastId,
            "Reverting prior reward inputs must restore the same outputs, boundary knowledge, RNG and pending causes in a fresh forecast.");
    }

    private static void BeforeMeansBeforeRoomEntry()
    {
        var run = Route(onlySkills: true);
        var node = Preview(run, [], 1).Nodes.Single();
        Check(node.Position.NodeId == FirstNode && Known(Fact(node, "rng.Niche.calls", before: true)) == 0 &&
            Known(Fact(node, "rng.Niche.calls")) == 1,
            "The first node's Before frame must precede its actual initial-creature Niche draw.");
    }

    private static void SkillOnlyChoiceLeavesTreasureWhetstoneKnown()
    {
        var forecast = Preview(Route(onlySkills: true, treasureWhetstone: true), [AuthoredTreasure("WHETSTONE")], 2,
            PredictorRoutePreviewScope.AllModeledNodes);
        var treasure = forecast.Nodes[1];
        Check(treasure.Room == PredictorRoomKind.Treasure && treasure.Advanced &&
            treasure.Treasure is { RelicTaken: true } && treasure.Treasure.OfferedRelic == new ModelKey("RELIC", "WHETSTONE") &&
            !treasure.PendingCauses.Any(cause => cause.Code == "PlannedTreasureChoiceChanged"),
            "A Skill-only reward/Skip must permit the actual treasure Whetstone obtain and departure.");
        Check(Known(Fact(treasure, "deck.UpgradableAttack.count")) == 4 &&
            Fact(treasure, "deck.UpgradableAttack.candidates").Status == PredictorKnowledgeStatus.Known &&
            Known(Fact(treasure, "rng.Niche.calls")) == 6 &&
            Known(Fact(treasure, "rng.TreasureRoomRelics.calls", before: true)) == 0 &&
            Known(Fact(treasure, "rng.TreasureRoomRelics.calls")) == 1,
            "Treasure Whetstone uses the same typed Attack reader while retaining separate treasure generation RNG.");
        var blocked = Preview(Route(onlySkills: true, treasureWhetstone: true, treasureCursorUnknown: true), [AuthoredTreasure("WHETSTONE")], 2,
            PredictorRoutePreviewScope.AllModeledNodes).Nodes[1];
        Check(blocked.Treasure is { RelicTaken: false } &&
            PredictorRouteCauseEvidence.OriginalCauses(blocked.PendingCauses).Any(cause => cause.Code == "CapturedTreasureCursor") &&
            blocked.PendingCauses.Any(cause => cause.Code == "PlannedTreasureChoiceChanged"),
            "Proven independent Attack candidates must not erase a genuinely unresolved Treasure RNG input.");
    }

    private static void NonBasicRewardLeavesPandoraTargetsKnown()
    {
        var run = Route(onlySkills: true, treasureRelic: "PANDORAS_BOX");
        var forecast = Preview(run, [AuthoredTreasure("PANDORAS_BOX")], 2, PredictorRoutePreviewScope.AllModeledNodes);
        var treasure = forecast.Nodes[1];
        Check(treasure.Treasure is { RelicTaken: true } && treasure.Advanced,
            "An unresolved non-Basic reward must not block Pandora's known Basic targets.");
        Check(Known(Fact(treasure, "deck.BasicStrike.count", before: true)) == 6 &&
            Known(Fact(treasure, "deck.BasicDefend.count", before: true)) == 4 &&
            Known(Fact(treasure, "deck.BasicStrike.count")) == 0 &&
            Known(Fact(treasure, "deck.BasicDefend.count")) == 0 &&
            Known(Fact(treasure, "deck.Upgraded.count")) == 0 &&
            Known(Fact(treasure, "rng.Niche.calls")) == 11,
            "Pandora must transform all ten actual Basic targets and consume ten draws after the initial creature.");
        var guaranteed = Fact(treasure, "deck.All.candidates");
        Check(guaranteed.Status == PredictorKnowledgeStatus.Partial && guaranteed.DisplayValue != null &&
            PredictorRouteCauseEvidence.OriginalCauses(treasure.PendingCauses).Any(cause =>
                cause.Code == "PendingCardChoice" && cause.NodeId == FirstNode),
            "Pandora must retain the earlier optional non-Basic choice and expose only the guaranteed transformed subset.");
        var cardOffer = forecast.Nodes[0].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown).Value!;
        var initialIds = run.Working.Deck;
        // Execute every published card alternative and Skip through the real
        // runtime. The comparison uses its recorded accepted transformations,
        // not a copied transform/shuffle calculation.
        for (int branch = -1; branch < cardOffer.Cards.Length; branch++)
        {
            var actual = Route(onlySkills: true, treasureRelic: "PANDORAS_BOX");
            long? selected = CompleteFirstCombat(actual, branch);
            EnterAndTakeTreasure(actual, "PANDORAS_BOX");
            Send(actual, new LeaveTreasure());
            var transforms = actual.Checkpoints[^1].CompletedNode!.Inputs
                .Single(input => input.Action == "PandorasBoxTransform").Transformations;
            Check(transforms.Length == 10 && transforms.Select(change => change.OriginalId).Order()
                .SequenceEqual(initialIds.Order()) && initialIds.All(id => !actual.Working.Deck.Contains(id)),
                "Concrete Pandora execution did not target exactly the original Basic identities.");
            var labels = transforms.Select(change => CardLabel(actual.Working.Card(change.ReplacementId)));
            Check(guaranteed.DisplayValue == string.Join(" | ", labels),
                "Pandora's guaranteed forecast replacements differ from a concrete prior reward branch.");
            Check(transforms.All(change => actual.Working.Card(change.ReplacementId).UpgradeLevel == 0),
                "The known zero upgraded-card projection disagrees with a concrete Pandora replacement.");
            Check(actual.Working.Streams.Single(stream => stream.Stream == PredictorStream.Niche).Calls == 11 &&
                (selected == null || actual.Working.Deck.Contains(selected.Value)),
                "Pandora lost the selected non-Basic card or used a branch-dependent Niche count.");
        }
        var blocked = Preview(Route(onlySkills: true, treasureRelic: "PANDORAS_BOX", nicheCursorUnknown: true),
            [AuthoredTreasure("PANDORAS_BOX")], 2, PredictorRoutePreviewScope.AllModeledNodes).Nodes[1];
        Check(blocked.Treasure is { RelicTaken: false } && blocked.PendingCauses.Any(cause =>
                cause.Code == "PlannedTreasureChoiceChanged") &&
            PredictorRouteCauseEvidence.OriginalCauses(blocked.PendingCauses).Any(cause => cause.Code == "CapturedNicheCursor"),
            "Known Pandora targets must not erase its genuine incoming Niche dependency.");
    }

    private static void PickerLowerBoundPreservesManualTargets()
    {
        foreach (string relic in new[] { "NEW_LEAF", "EMPTY_CAGE" })
        {
            var run = Route(onlySkills: true, treasureRelic: relic);
            var forecast = Preview(run, [AuthoredTreasure(relic, leave: false)], 2, PredictorRoutePreviewScope.AllModeledNodes);
            var node = forecast.Nodes[1];
            var child = node.Treasure!.RequiredChoices.Single();
            var scope = relic == "NEW_LEAF" ? "Transformable" : "Removable";
            var population = Fact(node, "deck." + scope + ".count", before: true);
            Check(population.Status == PredictorKnowledgeStatus.Partial && population.DisplayValue == "10 … 11" &&
                node.Phase == (relic == "NEW_LEAF" ? PredictorPhase.TransformCards : PredictorPhase.ObtainDeckCards) &&
                !node.Advanced && node.Treasure.RelicTaken &&
                child.DeckCards.Count(card => card.IsKnown) == 10 &&
                Known(Fact(node, "rng.Niche.calls")) == 1 &&
                Known(Fact(node, "deck.BasicStrike.count")) == 6,
                "A sufficient unknown-count lower bound must open the picker without automatically consuming a target.");
            var originalTargets = run.Working.Deck.Take(relic == "NEW_LEAF" ? 1 : 2).Select(run.Working.Card).ToArray();
            Check(originalTargets.All(card => child.DeckCards.Any(candidate => candidate.IsKnown &&
                candidate.Value.Id == card.Id && candidate.Value.Key == card.Key && candidate.Value.FloorAdded == card.FloorAdded)),
                "The picker lost a guaranteed original target identity.");
            var selection = new PredictorPlannedObtainChild(relic == "NEW_LEAF" ?
                PredictorObtainChildAction.TransformCard : PredictorObtainChildAction.ChooseDeckCards, new("RELIC", relic))
            { DeckCards = originalTargets.Select(card => new PredictorPlannedCard(card.Id, card.Key, card.FloorAdded)).ToImmutableArray() };
            var plan = new PredictorPlannedNode(FirstRest)
            {
                Treasure = new() { Actions = [
                    new(PredictorTreasureActionKind.Open),
                    new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = new("RELIC", relic) },
                    new(PredictorTreasureActionKind.Obtain) { Obtain = selection },
                    new(PredictorTreasureActionKind.Leave)
                ] }
            };
            var selected = Preview(run, [plan], 2, PredictorRoutePreviewScope.AllModeledNodes).Nodes[1];
            var remainingOriginalIds = run.Working.Deck.Except(originalTargets.Select(card => card.Id));
            Check(selected.Advanced && remainingOriginalIds.All(id => selected.Cards.Any(candidate =>
                    candidate.IsKnown && candidate.Value.Id == id)) &&
                Known(Fact(selected, "deck.BasicStrike.count")) == (relic == "NEW_LEAF" ? 5 : 4) &&
                Known(Fact(selected, "rng.Niche.calls")) == (relic == "NEW_LEAF" ? 2 : 1) &&
                PredictorRouteCauseEvidence.OriginalCauses(selected.PendingCauses).Any(cause =>
                    cause.Code == "PendingCardChoice" && cause.NodeId == FirstNode),
                "Manual picker resolution must act on the original identities and preserve the unrelated reward choice.");
            var actual = Route(onlySkills: true, treasureRelic: relic);
            CompleteFirstCombat(actual, -1);
            EnterAndTakeTreasure(actual, relic);
            Check(originalTargets.All(card => actual.Working.Deck.Contains(card.Id)),
                "Concrete obtain entry automatically consumed a target despite the sufficient lower bound.");
            Send(actual, relic == "NEW_LEAF" ? new SelectTransformationCard(originalTargets[0].Id) :
                new ChooseObtainDeckCards(originalTargets.Select(card => card.Id).ToImmutableArray()));
            Send(actual, new LeaveTreasure());
            Check(originalTargets.All(card => !actual.Working.Deck.Contains(card.Id)) &&
                remainingOriginalIds.All(id => actual.Working.Deck.Contains(id)),
                "Concrete manual selection rebound to another target.");
            if (relic == "NEW_LEAF")
            {
                var change = actual.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(input => input.Transformations).Single();
                Check(change.OriginalId == originalTargets[0].Id &&
                    Fact(selected, "deck.All.candidates").DisplayValue!.Contains(CardLabel(actual.Working.Card(change.ReplacementId)), StringComparison.Ordinal),
                    "The selected New Leaf replacement does not match concrete execution for that original target.");
            }
        }
    }

    private static PredictorPlannedNode AuthoredTreasure(string relic, bool leave = true) => new(FirstRest)
    {
        Treasure = new() { Actions = ImmutableArray.Create(
            new PredictorPlannedTreasureAction(PredictorTreasureActionKind.Open),
            new PredictorPlannedTreasureAction(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = new("RELIC", relic) })
            .AddRange(leave ? ImmutableArray.Create(new PredictorPlannedTreasureAction(PredictorTreasureActionKind.Leave)) : []) }
    };

    private static long? CompleteFirstCombat(PredictorRun run, int cardIndex)
    {
        run.AdvanceRoute();
        Send(run, run.DefaultCombatInput()!);
        while (PredictorRewardPickupPolicy.Plan(run).NextInput is { } input) Send(run, input);
        long? selected = null;
        if (cardIndex >= 0)
        {
            var reward = run.Rewards.Single(value => value.Kind == PredictorRewardKind.Card && value.Status == PredictorRewardStatus.Open);
            selected = reward.Cards[cardIndex].Id;
            Send(run, new EnterCardSelection(reward.Id));
            Send(run, new TakeReward(reward.Id, selected));
        }
        Send(run, new LeaveRewards());
        return selected;
    }
    private static void EnterAndTakeTreasure(PredictorRun run, string relic)
    {
        run.AdvanceRoute();
        Check(run.TreasureRelic?.Key == new ModelKey("RELIC", relic), "Concrete treasure fixture pulled a different relic.");
        Send(run, new OpenTreasure());
        Send(run, new TakeTreasureRelic(run.TreasureRelic!.Id));
    }
    private static void Send(PredictorRun run, PredictorInput input) =>
        Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "Concrete route input rejected: " + input.GetType().Name);
    // All.candidates publishes model identity only; upgrade/floor fields do not
    // follow from that scope. Upgrade truth is checked via its own projection.
    private static string CardLabel(PredictorCard card) => card.Key.Serialized;
}
