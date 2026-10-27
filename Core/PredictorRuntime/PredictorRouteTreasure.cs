using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private static PredictorTreasureNodeKnowledge ProjectRouteTreasure(PredictorRun run, PredictorPlannedTreasure? plan,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (plan != null) ValidatePlannedTreasure(plan);
        run._preview.Partial?.RecordTreasureEntryCoverage(run);
        // Recover the actual entry for observations even when the active node
        // has already accepted opening, pickup, or an obtain child.
        var entry = run.CreateEntryReplay(PreviewReplayPurpose.RoomObservation);
        entry.RestoreRoutes(run._nodeEntryRoutes, run._preview.Settings.AcceptsPrefixes);
        entry.AdvanceRoute(token);
        var offeredRelic = entry.TreasureRelic?.Key;
        bool generationAllowed = entry._treasureAllowed;
        var generationPending = RouteTreasureRelicCauses(dependencies);
        var relicPending = !generationAllowed && generationPending.IsEmpty ? [] :
            generationPending.AddRange(CausesFor(dependencies, PredictorCombatDomain.TreasureRng)).Distinct().ToImmutableArray();
        var normalPending = !entry._treasureAllowed && generationPending.IsEmpty ? [] :
            generationPending.AddRange(CausesFor(dependencies, PredictorCombatDomain.RewardsRng)).Distinct().ToImmutableArray();
        var point = entry.Working.Visits.Last().Point;
        var spoilsCards = entry.Working.Deck.Select(entry.Working.Card).Where(c => c.Key.Entry == "SPOILS_MAP").ToImmutableArray();
        bool quest = spoilsCards.Any(c => c.SpoilsCoord == point);
        // Cards added after this map was generated cannot create its quest
        // marker. They increase payout only if an attached quest already exists.
        var spoilsDependencies = quest ? dependencies.Where(d => RouteCardMayVary(entry, [d], "SPOILS_MAP")).ToArray() : [];
        bool spoilsMayVary = spoilsDependencies.Length > 0 && (generationAllowed || !generationPending.IsEmpty);
        var spoilsPending = !quest || !generationAllowed && generationPending.IsEmpty ? [] :
            generationPending.AddRange(spoilsMayVary ? spoilsDependencies.Select(d => d.Cause) : [])
            .Distinct().ToImmutableArray();
        int normal = 0, spoils = 0;
        if (entry._treasureAllowed)
        {
            normal = entry.Working.Draw(PredictorStream.Rewards, 42, 53).Value;
            if (run.Context.Ascension >= 3) normal = (int)(normal * .75);
            if (quest) spoils = checked(600 * spoilsCards.Length);
        }
        int applied = ActiveRouteTreasurePlanPrefix(run, entry, plan?.Actions ?? [], dependencies, knownInstanceIds, local, token);
        var actions = plan?.Actions ?? [];
        // CompleteNode clears its replay buffer. Keep these accepted source
        // facts locally while the projection completes this node.
        bool opened = run._nodeReplayInputs.Any(i => i is OpenTreasure);
        bool taken = run._nodeReplayInputs.Any(i => i is TakeTreasureRelic);
        bool propagated = false;
        void PropagateOpening()
        {
            if (propagated || !opened) return;
            propagated = true;
            foreach (var cause in normalPending.Concat(spoilsPending).Distinct())
                AddDependency(dependencies, new(cause with { AffectedDomains =
                    (generationPending.IsEmpty && PredictorSettlementEffects.Has(run.Working, "ECTOPLASM") ? PredictorCombatDomain.None : PredictorCombatDomain.Gold) |
                    ((cause.AffectedDomains & PredictorCombatDomain.Relics) != 0 ? PredictorCombatDomain.Health |
                        PredictorCombatDomain.RewardsRng : PredictorCombatDomain.None) }));
            if (spoilsMayVary)
                foreach (var dependency in spoilsDependencies)
                    AddDependency(dependencies, dependency with { MayChangeExistingCards = true,
                        PossibleCards = spoilsCards });
        }
        PropagateOpening();
        for (; applied < actions.Length && local.Count == 0; applied++)
        {
            ConfigureCompletedRewardEdits(run, new(run.Working.Position.NodeId) { Treasure = plan }, PredictorRewardEditOwner.Treasure, applied);
            if (actions[applied].Action == PredictorTreasureActionKind.Leave && run.HasObtainChild)
            {
                // All authored child actions precede this parent closure. Let
                // remaining approved card defaults drain their obtain first.
                CaptureCompletedCardRewardEdits(run, dependencies);
                ContinueLiveDefaultCardRewardChildren(run, dependencies, token);
            }
            if (run.Phase == PredictorPhase.Complete ||
                !ApplyRouteTreasureAction(run, actions[applied], dependencies, knownInstanceIds, local, token)) break;
            opened |= actions[applied].Action == PredictorTreasureActionKind.Open;
            taken |= actions[applied].Action == PredictorTreasureActionKind.TakeRelic;
            PropagateOpening();
        }
        if (local.Count == 0)
        {
            var editPlan = plan;
            if (editPlan == null && (taken || run._treasureObtaining) && offeredRelic is { } actualRelic && relicPending.IsEmpty)
                editPlan = new() { Actions = [new(PredictorTreasureActionKind.Open),
                    new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = actualRelic },
                    new(PredictorTreasureActionKind.Leave)] };
            ConfigureCompletedRewardEdits(run, new(run.Working.Position.NodeId) { Treasure = editPlan }, PredictorRewardEditOwner.Treasure,
                plan == null ? editPlan == null ? 0 : 2 : applied);
            CaptureCompletedCardRewardEdits(run, dependencies);
            ContinueLiveDefaultCardRewardChildren(run, dependencies, token);
        }
        bool advanced = run.Phase == PredictorPhase.Complete && local.Count == 0;
        if (!advanced && local.Count == 0 && plan == null && run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults)
        {
            if (run.Phase == PredictorPhase.TreasureClosed)
            {
                ApplyRouteTreasureAction(run, new(PredictorTreasureActionKind.Open), dependencies, knownInstanceIds, local, token, authored: false);
                opened = true;
                PropagateOpening();
            }
            if (run.Phase == PredictorPhase.TreasureRelic && !run._treasureObtaining && run.TreasureRelic is { } relic && local.Count == 0)
            {
                ConfigureCompletedRewardEdits(run, new(run.Working.Position.NodeId) { Treasure = new()
                    { Actions = [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = relic.Key },
                        new(PredictorTreasureActionKind.Leave)] } },
                    PredictorRewardEditOwner.Treasure, 2);
                taken = ApplyRouteTreasureAction(run, new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = relic.Key },
                    dependencies, knownInstanceIds, local, token, authored: false);
                if (local.Count == 0) ContinueLiveDefaultCardRewardChildren(run, dependencies, token);
            }
        }
        // Taking the relic already expresses the meaningful choice. Its
        // source obtain must finish before this purely procedural closure;
        // no additional authored Leave is required after that return.
        if (!advanced && local.Count == 0 && run.Phase == PredictorPhase.TreasureRelic &&
            !run._treasureObtaining && run.TreasureRelic == null)
            advanced = ApplyRouteTreasureAction(run, new(PredictorTreasureActionKind.Leave), dependencies, knownInstanceIds, local, token, authored: false);
        if (!advanced && local.Count == 0)
            local.Add(RouteCause(run.Phase == PredictorPhase.TreasureClosed ? "TreasureOpenOrLeaveChoice" :
                run.Phase == PredictorPhase.TreasureRelic ? "TreasureRelicOrLeaveChoice" : "TreasureObtainChoice",
                run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
        var child = advanced ? null : ObserveRouteObtainChoice(run, dependencies, knownInstanceIds);
        var goldPending = CausesFor(dependencies, PredictorCombatDomain.Gold);
        return new(relicPending.IsEmpty ? offeredRelic : null, relicPending,
            child == null ? [] : [child], ObserveRouteRewards(run, dependencies),
            dependencies.Select(d => d.Cause).Concat(local).Distinct().ToImmutableArray())
        {
            GenerationAllowed = generationPending.IsEmpty ? generationAllowed : null, Opened = opened, RelicTaken = taken,
            NormalGold = normalPending.IsEmpty ? normal : null, NormalGoldPendingCauses = normalPending,
            SpoilsGold = spoilsPending.IsEmpty ? spoils : null, SpoilsPendingCauses = spoilsPending,
            GoldBalance = goldPending.IsEmpty ? run.Working.GoldBudget : null, GoldPendingCauses = goldPending,
            Advanced = advanced, AppliedActions = applied
        };
    }

    private static bool ApplyRouteTreasureAction(PredictorRun run, PredictorPlannedTreasureAction action,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token, bool authored = true)
    {
        var previous = run._treasurePlanTrace;
        var trace = run._preview.Partial?.BeginTreasurePlan(run, action, authored);
        run._treasurePlanTrace = trace;
        int before = local.Count;
        try
        {
            bool applied = ApplyRouteTreasureActionCore(run, action, dependencies, knownInstanceIds, local, token);
            trace?.PlanReturn(applied, local.Skip(before).ToImmutableArray());
            return applied;
        }
        finally { run._treasurePlanTrace = previous; }
    }

    private static bool ApplyRouteTreasureActionCore(PredictorRun run, PredictorPlannedTreasureAction action,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (action.Obtain?.Action != PredictorObtainChildAction.Health) CompleteLiveObtainHealth(run, dependencies, token);
        if (action.Obtain == null) CompleteLivePaelsTooth(run, dependencies, token);
        if (action.Action == PredictorTreasureActionKind.Obtain)
            return ApplyRouteObtainChild(run, action.Obtain!, dependencies, knownInstanceIds, local, token);
        var input = ResolveRouteTreasureInput(run, action, dependencies, local);
        if (input == null) return false;
        if (run.Submit(run.Request!, input, token) == PredictorInputResult.Accepted) return true;
        local.Add(RouteCause("PlannedTreasureInputPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
        return false;
    }

    private static PredictorInput? ResolveRouteTreasureInput(PredictorRun run, PredictorPlannedTreasureAction action,
        List<RouteDependency> dependencies, List<PredictorCombatPendingCause> local)
    {
        PredictorInput? input = action.Action switch
        {
            PredictorTreasureActionKind.Open when run.Phase == PredictorPhase.TreasureClosed => new OpenTreasure(),
            PredictorTreasureActionKind.Leave when run.Phase is PredictorPhase.TreasureClosed or PredictorPhase.TreasureRelic && !run._treasureObtaining => new LeaveTreasure(),
            PredictorTreasureActionKind.TakeRelic when run.Phase == PredictorPhase.TreasureRelic && !run._treasureObtaining &&
                run.TreasureRelic is { } relic && relic.Key == action.ExpectedRelic &&
                RouteTreasureRelicCauses(dependencies).IsEmpty &&
                CausesFor(dependencies, PredictorCombatDomain.TreasureRng).IsEmpty &&
                ObtainCauses(run, relic.Key, dependencies, entryOnly: true).IsEmpty => new TakeTreasureRelic(relic.Id),
            _ => null
        };
        if (input == null)
        {
            // Only a matching offered relic can have an upstream reader mask.
            // A changed phase/model is a plan binding failure in its own right.
            var parents = action.Action == PredictorTreasureActionKind.TakeRelic && run.Phase == PredictorPhase.TreasureRelic &&
                !run._treasureObtaining && run.TreasureRelic is { } offered && offered.Key == action.ExpectedRelic
                ? RouteTreasureRelicCauses(dependencies).AddRange(CausesFor(dependencies, PredictorCombatDomain.TreasureRng))
                    .AddRange(ObtainCauses(run, offered.Key, dependencies, entryOnly: true)) : [];
            local.Add(RouteCause("PlannedTreasureChoiceChanged", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                parents, PredictorRouteCauseEvidence.Key("treasure-action", run._inputs.Length.ToString(), action.Action.ToString())));
        }
        return input;
    }

    // FirstPlayerTurn is the maintained combat producer for a Bone Tea
    // lifetime counter. It cannot change chest eligibility, bags, gold hooks,
    // or Silver Crucible's independent treasure-entry counter.
    private static ImmutableArray<PredictorCombatPendingCause> RouteTreasureRelicCauses(List<RouteDependency> dependencies)
        => dependencies.Where(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Relics) != 0 &&
            d.Cause.Code != "FirstPlayerTurn").Select(d => d.Cause).Distinct().ToImmutableArray();

    private static int ActiveRouteTreasurePlanPrefix(PredictorRun run, PredictorRun entry,
        ImmutableArray<PredictorPlannedTreasureAction> actions, List<RouteDependency> dependencies,
        HashSet<long> knownInstanceIds, List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        int applied = 0;
        foreach (var actual in run._nodeReplayInputs)
        {
            // EnterCardSelection is a navigation input generated inside the
            // semantic reward child, not another authored treasure action.
            bool actionInput = actual is OpenTreasure or TakeTreasureRelic or LeaveTreasure || IsRouteObtainChildInput(actual);
            if (actionInput)
            {
                if (applied < actions.Length)
                {
                    var action = actions[applied];
                    var expected = action.Action == PredictorTreasureActionKind.Obtain
                        ? ResolveRouteObtainChildInput(entry, action.Obtain!, dependencies, knownInstanceIds, local)
                        : ResolveRouteTreasureInput(entry, action, dependencies, local);
                    if (expected == null) return Math.Min(applied, actions.Length);
                    if (!SameRouteObtainInput(expected, actual))
                    {
                        local.Add(RouteCause("PlannedTreasureAcceptedPrefixChanged", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
                        return Math.Min(applied, actions.Length);
                    }
                }
                applied++;
            }
            if (entry.Request is not { } request || entry.Submit(request, actual, token) is PredictorInputResult.Invalid or PredictorInputResult.Stale)
                throw new InvalidOperationException("PredictorTreasureAcceptedPrefixReplayMismatch");
        }
        return Math.Min(applied, actions.Length);
    }

    private static void ValidatePlannedTreasure(PredictorPlannedTreasure plan)
    {
        RequirePlan(!plan.Actions.IsDefault);
        foreach (var action in plan.Actions)
        {
            RequirePlan(action != null && Enum.IsDefined(action.Action));
            RequirePlan(action.Action == PredictorTreasureActionKind.TakeRelic
                ? action.ExpectedRelic is { IsValid: true, Category: "RELIC" } : action.ExpectedRelic == null);
            RequirePlan(action.Action == PredictorTreasureActionKind.Obtain ? action.Obtain != null : action.Obtain == null);
            if (action.Obtain != null) ValidatePlannedObtainChild(action.Obtain);
        }
    }
}
