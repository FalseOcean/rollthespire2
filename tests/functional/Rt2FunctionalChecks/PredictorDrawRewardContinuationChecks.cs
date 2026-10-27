using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

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

    private static PredictorRun Route(PredictorRun captured, bool rest, bool health = false,
        string relic = "POMANDER", bool nicheUnknown = false, bool rerolls = false, bool nested = false)
    {
        const int act = 2;
        var types = new[] { MapPointType.Ancient, rest ? MapPointType.RestSite : MapPointType.Treasure,
            MapPointType.Monster, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : [new(3, row + 1)])).ToArray();
        var source = captured.Checkpoints[0].State;
        var context = captured.Context with { Live = null, Operation = null,
            Modifiers = health ? [new(new("MODIFIER", "NIGHT_TERRORS"))] : [] };
        if (nicheUnknown) context = context with { Live = new(2, true,
            [PredictorRouteCauseEvidence.CreateExplicit("UnobservedNiche", "capture:draw", PredictorCombatDomain.NicheRng,
                PredictorPendingReasonKind.MissingObservation, "The incoming Niche cursor was not observed.", ["rng.Niche.state"])]) };
        var relicKeys = (rest ? new[] { R("MINIATURE_TENT"), R("DREAM_CATCHER"), R("TINY_MAILBOX") } : [])
            .Concat(rerolls ? [R("DRIFTWOOD")] : []).Concat(nested ? [R("PAELS_WING")] : []).ToArray();
        var state = source with
        {
            OpeningCompleted = true, Position = new(act, 1, "draw-rewards:entry"), GoldBudget = 1000,
            Relics = relicKeys.Select((key, index) => new PredictorRelic(10000 + index, key, 1)
                { RewardsSacrificed = key.Entry == "PAELS_WING" ? 1 : 0 }).ToImmutableArray(),
            NextInstanceId = 11000,
            Maps = [new(act, captured.RouteMap(act) with { Points = points }, [points[0].Position])],
            Visits = [new(act, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Ancient, null, 1)],
            VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
            SharedBag = new(false, [], [new("Rare", [R(relic), R(nested ? "ORRERY" : "GORGET")])], []),
            // Treasure draws the shared bag; Pael's reward draws the personal
            // bag. Keep the real producer's next gift explicit in this fixture.
            PersonalBag = nested ? new(false, [], [new("Rare", [R("ORRERY")])], []) : source.PersonalBag,
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
        PredictorRoutePreviewPolicy policy = PredictorRoutePreviewPolicy.LiveMapDefaults)
    {
        string before = JsonSerializer.Serialize(run.Working), draft = JsonSerializer.Serialize(run.CaptureNodeDraft());
        var result = run.PreviewRouteContinuation(plans, 2, scope: PredictorRoutePreviewScope.AllModeledNodes, policy: policy);
        Check(before == JsonSerializer.Serialize(run.Working) && draft == JsonSerializer.Serialize(run.CaptureNodeDraft()),
            "drawing does not mutate the caller or its accepted inputs");
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
