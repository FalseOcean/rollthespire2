using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    // Called from the existing information fixture once it has its actual
    // selected first combat. All controlled inventory belongs to detached runs.
    private static void CheckInRunMapReadOnlyRewardPreviews(InRunMapPredictionPanel panel, RunState native,
        PredictorMapRouteDraft draft, MapPosition first, Func<string> signature, Action<bool, string> check)
    {
        string nativeBefore = signature();
        var text = (IUiTextProvider)DiagnosticField(panel, "_text");
        var names = (IGameContentNameResolver)DiagnosticField(panel, "_names");
        var timings = new List<object>();
        foreach (var fixture in new[] { (Relic: "DRIFTWOOD", Count: 0, Gift: ""),
            (Relic: "PAELS_WING", Count: 0, Gift: ""),
            (Relic: "PAELS_WING", Count: 1, Gift: "DOLLYS_MIRROR"),
            (Relic: "PAELS_WING", Count: 1, Gift: "ORRERY") })
        {
            var session = new InRunMapPredictionSession();
            session.Update(native, "0.111.0", true, new Dictionary<int, PredictorMapRouteDraft> { [0] = draft });
            var captured = session.Snapshot!;
            var source = captured.Run;
            var state = source.Working;
            long id = state.NextInstanceId;
            var controlled = new PredictorRun(source.Context, state with
            {
                NextInstanceId = id + 1,
                Odds = state.Odds with { PotionDropChance = 1f },
                Relics = state.Relics.Where(relic => relic.Key.Entry is not ("DRIFTWOOD" or "PAELS_WING"))
                    .Append(new PredictorRelic(id, new("RELIC", fixture.Relic), state.Position.Floor)
                    { RewardsSacrificed = fixture.Count }).ToImmutableArray(),
                PersonalBag = fixture.Count == 1 ? new(false, [], new[] { "Common", "Uncommon", "Rare" }
                    .Select(rarity => new PredictorBagBucket(rarity, [new("RELIC", fixture.Gift)])).ToImmutableArray(), []) : state.PersonalBag
            }) { CombatDefaults = source.CombatDefaults, AutomaticRewardPickup = source.AutomaticRewardPickup };
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot),
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, captured with { Run = controlled });
            session.Recompute();
            var presenter = new InRunMapNodePresenter(session, names, text);
            var data = presenter.Detail((0, first))!;
            var cards = data.Sections.First(section => section.RewardActionsRow && section.Choices.Any(choice => choice.Object != null));
            string alternative = fixture.Relic == "DRIFTWOOD" ? "REROLL" : "SACRIFICE";
            var take = cards.Choices.First(choice => choice.Object?.Kind == GameContentKind.Card);
            var special = cards.Choices.Single(choice => choice.Label == text.Get("predictor.runtime.alternative." + alternative));
            var forecast = session.Forecast;
            var nodes = session.Nodes;
            var snapshot = session.Snapshot;
            string stateBefore = PredictorRunStore.Serialize(controlled), plansBefore = JsonSerializer.Serialize(session.Plans);
            long revision = session.Revision;
            int history = session.HistoryCount;
            check(presenter.RewardPreviewComputationCount == 0, "building map choices does not eagerly project reward previews");
            check(data.IsCurrent?.Invoke() == true, "detached detail data carries its current node and revision witness");

            OperationPreview Preview(InRunMapDetailChoice action)
            {
                check(action.PreviewFactory != null, "combat choice exposes a lazy preview: " + action.Label);
                long computations = presenter.RewardPreviewComputationCount, hits = presenter.RewardPreviewCacheHits;
                var timer = Stopwatch.StartNew();
                var cold = action.PreviewFactory!();
                double coldMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var warm = action.PreviewFactory();
                double warmMs = timer.Elapsed.TotalMilliseconds;
                check(cold != null && cold.Faults.IsEmpty && ReferenceEquals(cold, warm) &&
                    presenter.RewardPreviewComputationCount == computations + 1 && presenter.RewardPreviewCacheHits == hits + 1,
                    "cold hover projects once and repeated hover reuses the same reward preview");
                timings.Add(new { fixture.Relic, fixture.Count, fixture.Gift, action = action.Label, coldMs, warmMs,
                    computations = presenter.RewardPreviewComputationCount, hits = presenter.RewardPreviewCacheHits });
                return cold!;
            }
            var taken = Preview(take);
            check(taken.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                .Any(row => row.Action == OperationPreviewAction.AddCard && row.Cards.Any(card => card.Key == take.Object!.Key)),
                "Take preview shows the actual selected future offer after pure replay");
            check(session.Nodes[(0, first)].PendingCauses.Any(cause => cause.Code == "MapPotionInventoryNotProjected") &&
                !cards.Choices.Any(choice => choice.Label == text.Get("predictor.runtime.plan.skip")) &&
                session.Nodes[(0, first)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.DefaultSkipped &&
                    reward.AppliedChoice == null),
                "ordinary default Skip needs no authored button or preview despite the real unrelated dropped-potion inventory annotation");
            var predicted = Preview(special);
            check(session.Revision == revision && session.HistoryCount == history &&
                ReferenceEquals(forecast, session.Forecast) && ReferenceEquals(nodes, session.Nodes) &&
                ReferenceEquals(snapshot, session.Snapshot) && JsonSerializer.Serialize(session.Plans) == plansBefore &&
                PredictorRunStore.Serialize(controlled) == stateBefore && signature() == nativeBefore,
                "all hover previews preserve native state, captured run, plans, revision, history and published forecast");
            check(presenter.ApplyChoice(data.NodeId, special.Id), "previewing an alternative leaves its actual click usable");
            var accepted = session.Nodes[(0, first)].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card &&
                (alternative == "REROLL" ? reward.AppliedChoice?.Reroll == true : reward.AppliedChoice?.SacrificeRelicId == id));
            if (alternative == "REROLL")
                check(predicted.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups)
                    .Any(group => group.Cards.Select(card => card.Key).SequenceEqual(accepted.Value!.Cards.Select(card => card.Key))),
                    "future reroll preview and clicked plan use the same real replacement offer");
            else
                check(predicted.Notices.Contains(text.Format("predictor.runtime.alternative.sacrifice_count", fixture.Count + 1)) &&
                    (fixture.Gift.Length == 0 || predicted.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                        .Any(row => row.Action == OperationPreviewAction.ObtainRelic && row.Models.Any(model => model.Entry == fixture.Gift))) &&
                    accepted.SacrificeCount == fixture.Count + 1 && (accepted.SacrificeRelic?.Entry ?? "") == fixture.Gift,
                    "future sacrifice preview uses the actual counter, bag identity and pending child scope");
            long after = presenter.RewardPreviewComputationCount;
            check(special.PreviewFactory!() == null && presenter.RewardPreviewComputationCount == after,
                "a retained hover factory expires after a plan revision instead of displaying stale results");
            check(data.IsCurrent?.Invoke() == false, "old detail data expires together with its option previews");
            if (fixture.Gift == "ORRERY")
            {
                var child = session.Nodes[(0, first)].Rewards.First(reward => reward.CompletedEdit != null &&
                    reward.Kind == PredictorRewardKind.Card && reward.DefaultSkipped);
                InRunMapDetailSection ChildSection() => presenter.Detail((0, first))!.Sections.First(section =>
                    section.Objects.Select(item => item.Key).SequenceEqual(child.Value!.Cards.Select(card => card.Key)));
                var childAlternative = ChildSection().Choices.Single(action => action.Label ==
                    text.Get("predictor.runtime.alternative.SACRIFICE"));
                check(presenter.ApplyChoice(data.NodeId, childAlternative.Id), "a completed Orrery reward can use its actual sacrifice alternative");
                var cancelChild = ChildSection().Choices.Single(action => action.Selected && action.Label ==
                    text.Get("predictor.runtime.alternative.SACRIFICE"));
                var childPreview = cancelChild.PreviewFactory!()!;
                check(childPreview.Notices.Contains(text.Get("predictor.map.preview_cancel_alternative")) && childPreview.Faults.IsEmpty,
                    "cancelling a completed child previews a real default offer without replacing its parent");
                check(presenter.ApplyChoice(data.NodeId, cancelChild.Id) && session.GetPlan(0, first)!.RewardChoices.Single() is
                    { SacrificeRelicId: not null, Children.IsEmpty: true } &&
                    session.Nodes[(0, first)].Rewards.Any(reward => reward.SacrificeRelic?.Entry == "ORRERY" && reward.SacrificeCount == 2) &&
                    session.Nodes[(0, first)].Rewards.Count(reward => reward.CompletedEdit != null && reward.DefaultSkipped) == 5,
                    "child cancellation deletes only its leaf and retains the parent sacrifice and all five actual Orrery offers");
            }
            var revised = presenter.Detail((0, first))!;
            var cancel = revised.Sections.SelectMany(section => section.Choices).Single(action => action.Selected &&
                action.Label == text.Get("predictor.runtime.alternative." + alternative));
            var cancellation = cancel.PreviewFactory!()!;
            check(cancellation.Faults.IsEmpty && cancellation.Notices.Contains(text.Get("predictor.map.preview_cancel_alternative")) &&
                cancellation.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups)
                    .Any(group => group.Cards.Select(card => card.Key).SequenceEqual(cards.Objects.Select(card => card.Key))),
                "hovering a selected alternative previews restoring its actual original offer: " + JsonSerializer.Serialize(new {
                    fixture, cancellation.Notices, faults = cancellation.Faults.Length,
                    groups = cancellation.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups)
                        .Select(group => group.Cards.Select(card => card.Key.Serialized).ToArray()).ToArray(),
                    expected = cards.Objects.Select(card => card.Key.Serialized).ToArray() }));
            check(presenter.ApplyChoice(revised.NodeId, cancel.Id), "selected alternative remains cancellable after preview");
            var restored = session.Nodes[(0, first)].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card);
            check(restored is { IsKnown: true, DefaultSkipped: true, AppliedChoice: null, SacrificeCount: null,
                    Value: { HasRerolled: false, Status: PredictorRewardStatus.Skipped } } &&
                restored.Value.Cards.Select(card => card.Key).SequenceEqual(cards.Objects.Select(card => card.Key)) &&
                session.Nodes.Values.All(node => !node.PendingCauses.Any(cause => cause.Code == "PlannedRewardChanged")) &&
                session.Nodes.Count == nodes.Count,
                "actual cancellation restores the original default-skipped offer and keeps the selected suffix reachable");
            var takeAgain = presenter.Detail((0, first))!.Sections.Where(section => section.RewardActionsRow)
                .SelectMany(section => section.Choices).First(choice => choice.Object?.Kind == GameContentKind.Card && choice.Enabled);
            check(presenter.ApplyChoice(data.NodeId, takeAgain.Id), "the restored real offer can still be taken after alternative cancellation");
            var cancelTake = presenter.Detail((0, first))!.Sections.SelectMany(section => section.Choices)
                .Single(choice => choice.Selected && choice.Object?.Kind == GameContentKind.Card && choice.Label == text.Get("predictor.runtime.cancel"));
            var cancelPreview = Preview(cancelTake);
            check(cancelPreview.OptionKey == "CANCEL_TAKE" && presenter.ApplyChoice(data.NodeId, cancelTake.Id) &&
                session.GetPlan(0, first)?.RewardChoices.Any(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card) != true &&
                session.Nodes[(0, first)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.DefaultSkipped &&
                    reward.AppliedChoice == null && reward.Value?.Status == PredictorRewardStatus.Skipped),
                "selected-card cancellation is pure/cacheable and removes the real Take while restoring default Skip without an explicit Skip action");
            check(signature() == nativeBefore, "preview and explicit detached selection leave native inventory and RNG unchanged");
        }
        Console.WriteLine("IN_RUN_MAP_READ_ONLY_PREVIEW " + JsonSerializer.Serialize(timings));
    }

    private static (InRunMapNodeDetailData Colorful, InRunMapNodeDetailData Fight) CheckInRunMapActualEventGroups(RunState native, PredictorMapRouteDraft draft, MapPosition first,
        IGameContentNameResolver names, IUiTextProvider text, Func<string> signature, Action<bool, string> check)
    {
        string nativeBefore = signature();
        var production = new InRunMapPredictionSession();
        production.Update(native, "0.111.0", true, new Dictionary<int, PredictorMapRouteDraft> { [0] = draft });
        var captured = production.Snapshot!;
        var source = captured.Run;
        var state = source.Working;
        int act = state.Position.Act;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        InRunMapPredictionSession EventSession(string entry)
        {
            // Only detached input is controlled. Real route entry, event
            // option projections, reward recipes and receipts run unchanged.
            var configured = state with
            {
                NextInstanceId = state.NextInstanceId + 2,
                Relics = [new(state.NextInstanceId, new("RELIC", "WING_CHARM"), state.Position.Floor),
                    new(state.NextInstanceId + 1, new("RELIC", "DRIFTWOOD"), state.Position.Floor)],
                GoldBudget = 1000, VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
                Maps = state.Maps.Select(map => map.Act != act ? map : map with
                { Map = map.Map with { Points = map.Map.Points.Select(point => point.Position == first ?
                    point with { Type = MapPointType.Unknown } : point).ToArray() } }).ToImmutableArray(),
                Acts = state.Acts.Select(value => value.Act != act ? value : value with
                { RawEvents = [new("EVENT", entry)], EventCursor = 0 }).ToImmutableArray()
            };
            var controlled = new PredictorRun(source.Context, configured)
            { CombatDefaults = new(), AutomaticRewardPickup = source.AutomaticRewardPickup };
            var isolated = new InRunMapPredictionSession();
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), members)!
                .SetValue(isolated, captured with { Run = controlled });
            typeof(InRunMapPredictionSession).GetField("_drafts", members)!
                .SetValue(isolated, DiagnosticField(production, "_drafts"));
            isolated.Recompute();
            return isolated;
        }
        var location = (act - 1, first);
        var session = EventSession("COLORFUL_PHILOSOPHERS");
        var actual = session.Nodes[location];
        check(actual.Event?.Event?.Entry == "COLORFUL_PHILOSOPHERS" && actual.Event.EntryOptions.Length >= 2,
            "real detached event route generates Colorful branch identities from the native catalog");
        var presenter = new InRunMapNodePresenter(session, names, text);
        var initial = presenter.Detail(location)!;
        var optionalBranches = initial.Premises!.Sections.Where(section => section.HeaderChoice != null)
            .Select(section => section.HeaderChoice!).ToArray();
        check(!actual.Event!.BusinessChoiceRequired && !initial.Sections.Any(section => section.BranchRow) &&
            optionalBranches.Length == actual.Event.EntryOptions.Length && optionalBranches.All(choice => !choice.Selected) &&
            !initial.Sections.Any(section => section.RewardActionsRow) && initial.Sections.Any(section => section.OutcomePreview != null),
            "unadopted real Colorful route shows conditional outcomes and optional original branches without silently selecting a reward pool");
        check(presenter.ApplyChoice(actual.Position.NodeId, optionalBranches[0].Id),
            "real optional Colorful premise authors its actual option and recomputes the route");
        actual = session.Nodes[location];
        var selected = presenter.Detail(location)!;
        var groups = selected.Sections.Where(section => section.RewardActionsRow).ToArray();
        check(actual.Event!.Rewards.Count(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) == 3 &&
            groups.Length == 3 && groups.Select(group => group.StableId).Distinct().Count() == 3 &&
            groups.All(group => group.Objects.Count == 3) &&
            groups.SelectMany(group => group.Objects).Select(item => item.CardSnapshot)
                .SequenceEqual(actual.Event.Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card)
                    .SelectMany(reward => reward.Value!.Cards)),
            "real selected Colorful recipe retains all three ordered rarity opportunities and exact cards");
        var firstTake = groups[0].Choices.First(choice => choice.Object?.Kind == GameContentKind.Card);
        check(presenter.ApplyChoice(actual.Position.NodeId, firstTake.Id),
            "a real first Colorful opportunity can be taken before editing a different group");
        var reroll = presenter.Detail(location)!.Sections.Where(section => section.RewardActionsRow).ElementAt(1).Choices
            .Single(choice => choice.Label == text.Get("predictor.runtime.alternative.REROLL"));
        var projected = reroll.PreviewFactory?.Invoke();
        check(projected != null && projected.Faults.IsEmpty && presenter.ApplyChoice(actual.Position.NodeId, reroll.Id),
            "a real event opportunity offers pure receipt-owned reroll preview and actual click");
        var rerolled = session.Nodes[location].Event!.Rewards.Single(reward => reward.AppliedChoice?.Reroll == true);
        check(rerolled.Occurrence == 1 && session.Nodes[location].Event!.Rewards.Any(reward => reward.Occurrence == 0 &&
                reward.AppliedChoice is { CardIndex: 0, Skip: false }) && projected!.Branches.SelectMany(branch => branch.Sections)
            .SelectMany(section => section.CardGroups).Any(group => group.Cards.SequenceEqual(rerolled.Value!.Cards)),
            "event reroll preview agrees with its second opportunity and preserves the already taken first group");
        var rerollOnlyPlan = session.GetPlan(act - 1, first)!;
        var badTarget = new PredictorPlannedEventAction(PredictorEventActionKind.Reward)
        { RewardChoice = new(session.Nodes[location].Event!.Rewards.Single(reward => reward.Occurrence == 2).Value!, 2) };
        check(session.SetPlan(act - 1, first, rerollOnlyPlan with { Event = rerollOnlyPlan.Event! with
            { Actions = rerollOnlyPlan.Event.Actions.Add(badTarget) } }) &&
            session.Nodes[location].Event!.Rewards.Single(reward => reward.Occurrence == 1).Value!.CardSelectionEntered &&
            session.Nodes[location].Event!.Rewards.Single(reward => reward.Occurrence == 2).AppliedChoice == null &&
            session.Nodes[location].PendingCauses.Any(cause => cause.Code == "PlannedObtainRewardChanged"),
            "a rejected Take without a concrete card preserves the prior real rerolled picker and its original failure");
        check(session.SetPlan(act - 1, first, rerollOnlyPlan),
            "removing the invalid target preserves the original real reroll-only plan");
        var pendingSuffix = new PredictorPlannedEventAction(PredictorEventActionKind.Option)
        { ExpectedOption = session.Nodes[location].Event!.EntryOptions[1].Value };
        var existingPlan = session.GetPlan(act - 1, first)!;
        check(session.SetPlan(act - 1, first, existingPlan with
            { Event = existingPlan.Event! with { Actions = existingPlan.Event.Actions.Add(pendingSuffix) } }) &&
            session.Nodes[location].Event!.AppliedActions < session.GetPlan(act - 1, first)!.Event!.Actions.Length,
            "a valid saved event action can remain pending after its current picker without being deleted");
        var nextReroll = presenter.Detail(location)!.Sections.Where(section => section.RewardActionsRow).ElementAt(2).Choices
            .Single(choice => choice.Label == text.Get("predictor.runtime.alternative.REROLL"));
        check(presenter.ApplyChoice(actual.Position.NodeId, nextReroll.Id) &&
            session.Nodes[location].Event!.Rewards.Count(reward => reward.AppliedChoice?.Reroll == true) == 2 &&
            session.GetPlan(act - 1, first)!.Event!.Actions.Last() == pendingSuffix &&
            session.Nodes[location].PendingCauses.All(cause => cause.Code != "PlannedObtainInputPending"),
            "navigation to another validated real reroll uses its own alternative and preserves the first reroll and pending suffix");
        var thirdTake = presenter.Detail(location)!.Sections.Where(section => section.RewardActionsRow).ElementAt(2).Choices
            .First(choice => choice.Object?.Kind == GameContentKind.Card);
        var thirdBefore = session.Nodes[location].Event!;
        bool thirdApplied = presenter.ApplyChoice(actual.Position.NodeId, thirdTake.Id);
        var thirdAfter = session.GetPlan(act - 1, first)!.Event!;
        Console.WriteLine("IN_RUN_MAP_EVENT_PENDING_SUFFIX " + JsonSerializer.Serialize(new {
            thirdApplied, beforeApplied = thirdBefore.AppliedActions, afterApplied = session.Nodes[location].Event!.AppliedActions,
            suffixRetained = thirdAfter.Actions.Last() == pendingSuffix,
            actions = thirdAfter.Actions.Select(action => new { action.Action, option = action.ExpectedOption?.Key,
                source = action.Obtain?.ExpectedSource.Serialized,
                reward = (action.RewardChoice ?? action.Obtain?.Reward) is { } choice ? new {
                    choice.Occurrence, choice.CardIndex, choice.Skip, choice.Reroll, choice.SacrificeRelicId } : null }),
            beforeGroups = thirdBefore.Rewards.Select(reward => new { reward.Occurrence,
                edit = reward.CompletedEdit == null ? null : new { reward.CompletedEdit.Owner,
                    source = reward.CompletedEdit.Source.Serialized, reward.CompletedEdit.InsertionIndex,
                    prefix = reward.CompletedEdit.Prefix.Event?.Actions.Length } }),
            afterGroups = session.Nodes[location].Event!.Rewards.Select(reward => new { reward.Occurrence,
                cardIndex = reward.AppliedChoice?.CardIndex, reroll = reward.AppliedChoice?.Reroll }),
            pending = session.Nodes[location].PendingCauses.Select(cause => cause.Code) }));
        check(thirdApplied && thirdAfter.Actions.Last() == pendingSuffix &&
            thirdAfter.Actions.Count(action => (action.RewardChoice ?? action.Obtain?.Reward) != null) == 3 &&
            session.Nodes[location].Event!.Rewards.Any(reward => reward.Occurrence == 2 && reward.AppliedChoice?.CardIndex == 0),
            "editing a third real opportunity inserts before the unapplied authored suffix and preserves all prior groups");
        check(session.Nodes[location].Event!.Rewards.Single(reward => reward.Occurrence == 1).AppliedChoice is
                { Reroll: true, CardIndex: null, Skip: false } &&
            session.GetPlan(act - 1, first)!.Event!.Actions.Where(action => action.RewardChoice?.Occurrence == 1)
                .Single().RewardChoice is { Reroll: true, Skip: false },
            "moving between real opportunity pickers preserves reroll-only intent without authoring a player Skip");
        var pendingPlan = session.GetPlan(act - 1, first)!;
        check(session.SetPlan(act - 1, first, pendingPlan with { Event = pendingPlan.Event! with
            { Actions = pendingPlan.Event.Actions.RemoveAt(pendingPlan.Event.Actions.Length - 1) } }) &&
            session.Nodes[location].Event!.Rewards.Single(reward => reward.Occurrence == 1) is
                { DefaultSkipped: true, DispositionOrigin: PredictorKnowledgeOrigin.ApprovedDefault,
                    AppliedChoice: { Reroll: true, Skip: false } } &&
            session.Nodes[location].Event!.Rewards.Count(reward => reward.AppliedChoice?.CardIndex == 0) == 2,
            "fixing the stale suffix lets existing LiveDefaults own Skip while both explicitly taken opportunities remain intact");
        var afterReroll = presenter.Detail(location)!;
        var alternate = afterReroll.Sections.Single(section => section.BranchRow).Choices[1];
        check(presenter.ApplyChoice(actual.Position.NodeId, alternate.Id) && !presenter.ApplyChoice(actual.Position.NodeId, reroll.Id) &&
            selected.IsCurrent?.Invoke() == false,
            "switching a real Colorful branch expires previous reward actions and outcome data");
        var switched = presenter.Detail(location)!;
        var switchedRows = switched.Sections.Where(section => section.RewardActionsRow).ToArray();
        check(switched.Sections.Single(section => section.BranchRow).Choices[1].Selected && switchedRows.Length == 3 &&
            switchedRows.SelectMany(group => group.Objects).Select(item => item.CardSnapshot).SequenceEqual(
                session.Nodes[location].Event!.Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card)
                    .SelectMany(reward => reward.Value!.Cards)) &&
            !switchedRows.SelectMany(group => group.Objects).Select(item => item.Key)
                .SequenceEqual(groups.SelectMany(group => group.Objects).Select(item => item.Key)),
            "the selected real branch replaces all prior pool rows with its own recomputed opportunity results");

        session = EventSession("DENSE_VEGETATION");
        presenter = new(session, names, text);
        actual = session.Nodes[location];
        var rest = presenter.Detail(location)!.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices)
            .Single(choice => choice.Label == text.Get("predictor.runtime.option.REST"));
        check(presenter.ApplyChoice(actual.Position.NodeId, rest.Id),
            "real Dense rest branch adopts its actual option and deterministic fight continuation");
        var restDetail = presenter.Detail(location)!;
        Console.WriteLine("IN_RUN_MAP_EVENT_REST_PREREQUISITES " + JsonSerializer.Serialize(new
        { session.Nodes[location].Event!.NeededInput, information = restDetail.Information,
            current = session.Nodes[location].Event!.PendingCauses.Select(cause => new
                { cause.Code, cause.ReasonKind, cause.NodeId, cause.Identity }) }));
        actual = session.Nodes[location];
        var fightDetail = presenter.Detail(location)!;
        var combatRewards = actual.Event!.Rewards;
        var combatRows = fightDetail.Sections.Where(section => section.StableId?.Contains("|reward|", StringComparison.Ordinal) == true).ToArray();
        check(actual.Event.Advanced && actual.Event.NeededInput == PredictorEventNeededInput.None && actual.Event.Combat == null &&
            session.GetPlan(act - 1, first)!.Event!.Actions.Single().ExpectedOption?.Key == "REST" &&
            !fightDetail.Sections.Any(section => section.CombatPanel),
            "Dense's deterministic fight completes without inventing a second authored choice or retaining a stale partial-combat panel");
        check(combatRewards.Length > 0 && combatRows.Length == combatRewards.Length &&
            combatRows.Select(row => row.StableId).Distinct().Count() == combatRows.Length &&
            combatRows.SelectMany(section => section.Objects).Select(item => item.Key).OrderBy(key => key.Serialized).SequenceEqual(
                combatRewards.Where(reward => reward.IsKnown).SelectMany(reward => reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard
                    ? reward.Value!.Cards.Select(card => card.Key) : reward.Kind is PredictorRewardKind.Relic or PredictorRewardKind.Potion
                    ? new[] { reward.Value!.Key } : Enumerable.Empty<ModelKey>()).OrderBy(key => key.Serialized)),
            "real completed Dense fight owns each actual combat reward once on the main result surface");
        check(combatRewards.All(reward => reward.IsKnown) &&
            fightDetail.Information.All(line => !line.Contains(
                text.Get("predictor.map.prerequisite.cause.EventOptionChoice"), StringComparison.Ordinal) &&
                !line.Contains(text.Get("predictor.map.prerequisite.cause.EventHealthOutcomePending"), StringComparison.Ordinal)),
            "applying the real nested fight removes resolved parent branch and unused health warnings from its known reward prerequisites");
        Console.WriteLine("IN_RUN_MAP_EVENT_FIGHT_PREREQUISITES " + JsonSerializer.Serialize(new
        { actual.Event!.AppliedActions, actual.Event.NeededInput, information = fightDetail.Information,
            current = actual.Event.PendingCauses.Select(cause => new { cause.Code, cause.ReasonKind }),
            parent = actual.Event.EntryOptions.Single(option => option.Value?.Key == "REST").ResultPreview?.LocalPendingCauses
                .Select(cause => new { cause.Code, cause.ReasonKind }) }));
        check(signature() == nativeBefore && production.Plans.IsEmpty,
            "actual event generation, branch switches and opportunity operations preserve the native game and original plans");
        return (switched, fightDetail);
    }
}
