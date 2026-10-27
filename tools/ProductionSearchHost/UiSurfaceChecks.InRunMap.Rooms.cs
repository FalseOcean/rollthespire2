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
    private static (InRunMapNodeDetailData Campfire, InRunMapNodeDetailData Shop, InRunMapNodeDetailData UnresolvedSmith) CheckInRunMapActualRooms(
        RunState native, PredictorMapRouteDraft draft, MapPosition first, IGameContentNameResolver names,
        IUiTextProvider text, Func<string> nativeSignature, Action<bool, string> check)
    {
        string nativeBefore = nativeSignature();
        var production = new InRunMapPredictionSession();
        production.Update(native, "0.111.0", true, new Dictionary<int, PredictorMapRouteDraft> { [0] = draft });
        var captured = production.Snapshot!;
        var source = captured.Run;
        var state = source.Working;
        int act = state.Position.Act;
        var location = (act - 1, first);
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        string R(string key) => text.Get("predictor.runtime." + key);
        string M(string key) => text.Get("predictor.map." + key);
        var timings = new List<object>();

        InRunMapPredictionSession RoomSession(MapPointType kind, string[] relics, int gold = 10000)
        {
            // This controls detached input only. Native catalog, room entry,
            // rest qualification, shop stock and authored-plan replay are real.
            var configured = state with
            {
                NextInstanceId = state.NextInstanceId + relics.Length,
                GoldBudget = gold,
                Relics = relics.Select((entry, index) => new PredictorRelic(state.NextInstanceId + index,
                    new("RELIC", entry), state.Position.Floor)).ToImmutableArray(),
                Maps = state.Maps.Select(map => map.Act != act ? map : map with
                { Map = map.Map with { Points = map.Map.Points.Select(point => point.Position == first
                    ? point with { Type = kind } : point).ToArray() } }).ToImmutableArray()
            };
            var controlled = new PredictorRun(source.Context, configured)
            { CombatDefaults = source.CombatDefaults, AutomaticRewardPickup = source.AutomaticRewardPickup };
            var session = new InRunMapPredictionSession();
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), members)!
                .SetValue(session, captured with { Run = controlled });
            typeof(InRunMapPredictionSession).GetField("_drafts", members)!
                .SetValue(session, DiagnosticField(production, "_drafts"));
            session.Recompute();
            return session;
        }
        Action ReadOnlyWitness(InRunMapPredictionSession session)
        {
            var snapshot = session.Snapshot;
            var nodes = session.Nodes;
            var forecast = session.Forecast;
            string serialized = PredictorRunStore.Serialize(snapshot!.Run), plans = JsonSerializer.Serialize(session.Plans);
            long revision = session.Revision;
            int history = session.HistoryCount;
            return () => check(ReferenceEquals(snapshot, session.Snapshot) && ReferenceEquals(nodes, session.Nodes) &&
                ReferenceEquals(forecast, session.Forecast) && PredictorRunStore.Serialize(snapshot.Run) == serialized &&
                JsonSerializer.Serialize(session.Plans) == plans && session.Revision == revision && session.HistoryCount == history &&
                nativeSignature() == nativeBefore,
                "room detail/hover preserves native state, captured input, plans, revision, history and published forecast");
        }
        InRunMapDetailChoice Branch(InRunMapNodeDetailData data, PredictorRestAction action) =>
            data.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices)
                .Concat((data.Premises?.Sections ?? []).SelectMany(section => section.Choices))
                .First(choice => choice.Label.StartsWith(R("rest." + action), StringComparison.Ordinal));

        var restSession = RoomSession(MapPointType.RestSite, ["MINIATURE_TENT", "GIRYA"]);
        var rest = new InRunMapNodePresenter(restSession, names, text);
        var restInitial = rest.Detail(location)!;
        check(restSession.Nodes[location].Room == PredictorRoomKind.RestSite &&
            Branch(restInitial, PredictorRestAction.Lift).Enabled && Branch(restInitial, PredictorRestAction.Heal).Enabled,
            "actual detached Tent rest exposes its native qualified actions through the main detail and retained premises");
        check(rest.ApplyChoice(restInitial.NodeId, Branch(restInitial, PredictorRestAction.Lift).Id) &&
            restSession.Nodes[location].AppliedRestActions == 1 &&
            restSession.GetPlan(act - 1, first)!.RestActions.Single().Action == PredictorRestAction.Lift,
            "main rest branch authors the actual Lift action and receives its applied receipt");
        var assertRestReadOnly = ReadOnlyWitness(restSession);
        var timer = Stopwatch.StartNew();
        var afterLift = rest.Detail(location)!;
        double restColdMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var warmRest = rest.Detail(location)!;
        double restWarmMs = timer.Elapsed.TotalMilliseconds;
        var liftPlan = restSession.GetPlan(act - 1, first)!;
        var repeatLiftPlan = liftPlan with { RestActions = liftPlan.RestActions.Add(new(PredictorRestAction.Lift)), LeaveRest = false };
        var repeatLift = restSession.PreviewPlan(act - 1, first, repeatLiftPlan, restSession.Revision);
        var tentFlags = new
        {
            Cached = ReferenceEquals(afterLift, warmRest), LiftDisabled =
                !(afterLift.Premises?.Sections ?? []).SelectMany(section => section.Choices).Any(choice => choice.Label.StartsWith(R("rest.Lift"), StringComparison.Ordinal)),
            HealEnabled = Branch(afterLift, PredictorRestAction.Heal).Enabled,
            SmithEnabled = Branch(afterLift, PredictorRestAction.Smith).Enabled,
            SmithOnlyInPremises = !afterLift.Sections.SelectMany(section => section.Choices).Any(choice => choice.Label == R("rest.Smith")),
            AppliedRow = afterLift.Sections.Any(section => section.Text == M("rest_applied")),
            RepeatLiftActuallyUnavailable = repeatLift is { AppliedRestActions: 1 } &&
                repeatLift.PendingCauses.Any(cause => cause.Code == "PlannedRestActionUnavailable")
        };
        var tentEvidence = new
        {
            flags = tentFlags, Plan = liftPlan,
            Node = new { restSession.Nodes[location].Position, restSession.Nodes[location].Phase,
                restSession.Nodes[location].Advanced, restSession.Nodes[location].AppliedRestActions,
                restSession.Nodes[location].RestOffers, restSession.Nodes[location].PendingCauses,
                Relics = restSession.Nodes[location].Inventory?.Relics },
            Main = afterLift.Sections.Select(section => new { section.Title, section.Text,
                Choices = section.Choices.Select(choice => new { choice.Id, choice.Label, choice.Enabled, choice.Selected }) }),
            Premises = afterLift.Premises!.Sections.Select(section => new { section.Title, section.Text,
                Choices = section.Choices.Select(choice => new { choice.Id, choice.Label, choice.Enabled, choice.Selected }) }),
            RepeatLiftPlan = repeatLiftPlan,
            RepeatLift = repeatLift == null ? null : new { repeatLift.Position, repeatLift.Phase,
                repeatLift.AppliedRestActions, repeatLift.PendingCauses }
        };
        string tentDiagnostic = JsonSerializer.Serialize(tentEvidence);
        Console.WriteLine("IN_RUN_MAP_TENT_REPLAY " + tentDiagnostic);
        check(tentFlags.Cached && tentFlags.LiftDisabled && tentFlags.HealEnabled && tentFlags.SmithEnabled &&
            tentFlags.SmithOnlyInPremises && tentFlags.AppliedRow && tentFlags.RepeatLiftActuallyUnavailable,
            "Tent replay disables consumed Lift, retains real next Heal and keeps optional Smith in its premise surface; evidence=" + tentDiagnostic);
        assertRestReadOnly();
        timings.Add(new { stage = "rest-after-first-action", coldMs = restColdMs, warmMs = restWarmMs,
            offers = restSession.Nodes[location].RestOffers.Length });
        check(rest.ApplyChoice(afterLift.NodeId, Branch(afterLift, PredictorRestAction.Smith).Id),
            "optional Smith remains an explicit action entrance through the retained premise callback");
        var pendingSmith = rest.Detail(location)!;
        var smithTarget = pendingSmith.Premises!.Sections.Where(section => section.Title == R("rest.Smith"))
            .SelectMany(section => section.Choices).First(choice => choice.Object?.Kind == GameContentKind.Card && choice.Enabled);
        check(restSession.Nodes[location].AppliedRestActions == 2 &&
            restSession.GetPlan(act - 1, first)!.RestActions[1].SmithCard == null &&
            restSession.Nodes[location].PendingCauses.Any(cause => cause.Code == "PlannedRestTargetPending") &&
            smithTarget.Object!.CardSnapshot != null,
            "targetless Smith continues the Tent action with explicit local uncertainty and retains its real optional target controls");
        check(Branch(pendingSmith, PredictorRestAction.Heal).Enabled &&
            !pendingSmith.Premises!.Sections.Any(section => section.Title.StartsWith(R("rest.Heal"), StringComparison.Ordinal) &&
                section.Text.Contains(text.Get("predictor.map.prerequisite.cause.PlannedRestTargetPending"), StringComparison.Ordinal)),
            "Tent can continue with Heal after adopted targetless Smith without assigning Smith's missing target to the additional choice");
        check(rest.ApplyChoice(pendingSmith.NodeId, smithTarget.Id) && restSession.Nodes[location].AppliedRestActions == 2 &&
            restSession.GetPlan(act - 1, first)!.RestActions[1].SmithCard?.Key == smithTarget.Object!.Key,
            "actual Smith target completes a second Tent action through the existing plan callback");
        var restPicture = rest.Detail(location)!;

        var oneUse = RoomSession(MapPointType.RestSite, ["GIRYA"]);
        var noTent = new InRunMapNodePresenter(oneUse, names, text);
        var noTentInitial = noTent.Detail(location)!;
        check(noTent.ApplyChoice(noTentInitial.NodeId, Branch(noTentInitial, PredictorRestAction.Lift).Id),
            "actual non-Tent rest accepts its one Lift action");
        var exhausted = noTent.Detail(location)!;
        var exhaustedPlan = oneUse.GetPlan(act - 1, first)!;
        var unavailableSmithPlan = exhaustedPlan with { RestActions = exhaustedPlan.RestActions.Add(new(PredictorRestAction.Smith)), LeaveRest = false };
        var unavailableSmith = oneUse.PreviewPlan(act - 1, first, unavailableSmithPlan, oneUse.Revision);
        var exhaustedSmith = noTent.Editor(location)!.Sections.SelectMany(section => section.Choices)
            .First(choice => choice.Label.StartsWith(R("rest.Smith"), StringComparison.Ordinal));
        string exhaustedEvidence = JsonSerializer.Serialize(new
        {
            Plan = exhaustedPlan, oneUse.Nodes[location].AppliedRestActions, oneUse.Nodes[location].RestOffers,
            oneUse.Nodes[location].PendingCauses, LiftEnabled = noTent.Editor(location)!.Sections.SelectMany(section => section.Choices)
                .First(choice => choice.Label.StartsWith(R("rest.Lift"), StringComparison.Ordinal)).Enabled,
            SmithEnabled = exhaustedSmith.Enabled, ActualPreviewPlan = unavailableSmithPlan,
            ActualPreview = unavailableSmith == null ? null : new { unavailableSmith.AppliedRestActions, unavailableSmith.PendingCauses },
            Choices = (exhausted.Premises?.Sections ?? []).SelectMany(section => section.Choices)
                .Select(choice => new { choice.Id, choice.Label, choice.Enabled, choice.Selected })
        });
        Console.WriteLine("IN_RUN_MAP_EXHAUSTED_REST " + exhaustedEvidence);
        check(exhausted.Premises == null &&
            !exhausted.Sections.SelectMany(section => section.Choices).Any(choice => choice.Label == R("rest.Smith")) &&
            !exhaustedSmith.Enabled && !noTent.ApplyChoice(exhausted.NodeId, exhaustedSmith.Id) &&
            oneUse.Nodes[location].AppliedRestActions == 1 &&
            oneUse.GetPlan(act - 1, first) == exhaustedPlan && unavailableSmith is { AppliedRestActions: 1 } &&
            unavailableSmith.PendingCauses.Any(cause => cause.Code == "PlannedRestActionUnavailable"),
            "exhausted non-Tent rest omits empty premises while its complete editor still rejects unavailable callbacks and preserves the actual replay cause; evidence=" + exhaustedEvidence);

        var plainSession = RoomSession(MapPointType.RestSite, []);
        var plain = new InRunMapNodePresenter(plainSession, names, text);
        var plainInitial = plain.Detail(location)!;
        check(plain.ApplyChoice(plainInitial.NodeId, Branch(plainInitial, PredictorRestAction.Smith).Id),
            "ordinary rest explicitly adopts targetless Smith through its actual premise callback");
        var plainSmith = plain.Detail(location)!;
        var plainNode = plainSession.Nodes[location];
        var plainPlan = plainSession.GetPlan(act - 1, first)!;
        var assertPlainReadOnly = ReadOnlyWitness(plainSession);
        var plainTarget = plainSmith.Premises!.Sections.Where(section => section.Title == R("rest.Smith"))
            .SelectMany(section => section.Choices).First(choice => choice.Object?.Kind == GameContentKind.Card && choice.Enabled);
        string unavailableText = M("rest_additional_unavailable");
        check(plainNode.AppliedRestActions == 1 && plainPlan.RestActions.Single().SmithCard == null &&
            !plainSmith.Premises!.Sections.SelectMany(section => section.Choices).Any(choice =>
                choice.Label.StartsWith(R("rest.Smith"), StringComparison.Ordinal) || choice.Label.StartsWith(R("rest.Heal"), StringComparison.Ordinal)) &&
            !plainSmith.Premises.Sections.Any(section => section.Text == unavailableText) &&
            plainSmith.Sections.Concat(plainSmith.Premises!.Sections).SelectMany(section => section.Choices)
                .Count(choice => choice.Label.StartsWith(R("plan.remove_action"), StringComparison.Ordinal)) == 1,
            "ordinary used Smith retains its optional target picker and one main removal entrance, without unavailable extra actions in premises");
        check(plain.Editor(location)!.Sections.SelectMany(section => section.Choices)
                .Count(choice => choice.Label.StartsWith(R("plan.remove_action"), StringComparison.Ordinal)) == 1,
            "standalone complete rest editor retains its own per-action removal entrance");
        check(!plainSmith.Premises!.Sections.Any(section => section.Title.StartsWith(R("rest.Heal"), StringComparison.Ordinal) &&
                section.Text.Contains(text.Get("predictor.map.prerequisite.cause.PlannedRestTargetPending"), StringComparison.Ordinal)),
            "additional Heal availability never claims the adopted Smith target uncertainty as its own selection");
        var extraPlan = plainPlan with { RestActions = plainPlan.RestActions.Add(new(PredictorRestAction.Smith)), LeaveRest = false };
        var extra = plainSession.PreviewPlan(act - 1, first, extraPlan, plainSession.Revision)!;
        var inherited = plainNode.PendingCauses.First(cause => cause.Code == "PlannedRestTargetPending");
        var unrelated = new PredictorCombatPendingCause("EventChoice", "unrelated event input", PredictorCombatDomain.Deck)
            { NodeId = plainNode.Position.NodeId, Identity = "rooms-unrelated-event" };
        var foreign = new PredictorCombatPendingCause("RestRelicRngPending", "another source rest", PredictorCombatDomain.RewardsRng)
            { NodeId = "another-rest", Identity = "rooms-foreign-rest" };
        var mixed = extra with { PendingCauses = extra.PendingCauses.Add(inherited).Add(unrelated).Add(foreign) };
        var nextCauses = InRunMapNodePresenter.NextRestActionCauses(plainNode, mixed);
        check(nextCauses.Length == 1 && nextCauses[0].Code == "PlannedRestActionUnavailable" &&
            mixed.PendingCauses.Contains(inherited) && mixed.PendingCauses.Contains(unrelated),
            "next rest action explanation isolates its true refusal while retaining inherited target, event and foreign-source obligations in the model");
        assertPlainReadOnly();
        check(plain.ApplyChoice(plainSmith.NodeId, plainTarget.Id) &&
            plainSession.GetPlan(act - 1, first)!.RestActions.Single().SmithCard?.Key == plainTarget.Object!.Key &&
            plain.Detail(location)!.Premises!.Sections.Where(section => section.Title == R("rest.Smith"))
                .SelectMany(section => section.Choices).Any(choice => choice.Selected && choice.Object?.Key == plainTarget.Object.Key),
            "ordinary adopted Smith remains editable and visibly selected after replay");
        var invalidPlan = plainSession.GetPlan(act - 1, first)! with
            { RestActions = plainSession.GetPlan(act - 1, first)!.RestActions.Add(new(PredictorRestAction.Smith)) };
        check(plainSession.SetPlan(act - 1, first, invalidPlan, plainSession.Revision),
            "invalid authored suffix is retained explicitly for rest regression");
        var retainedInvalid = plain.Detail(location)!;
        check(plainSession.GetPlan(act - 1, first) == invalidPlan && plainSession.Nodes[location].AppliedRestActions == 1 &&
            retainedInvalid.Premises!.Sections.Count(section => section.Title == R("rest.Smith") &&
                section.Choices.Any(choice => choice.Object?.Kind == GameContentKind.Card)) == 1,
            "unavailable new Smith preserves its plan without requesting a target for an action it cannot perform");

        var shopSession = RoomSession(MapPointType.Shop, ["THE_COURIER"]);
        var shopPresenter = new InRunMapNodePresenter(shopSession, names, text);
        var assertShopReadOnly = ReadOnlyWitness(shopSession);
        var shopInitial = shopPresenter.Detail(location)!;
        var actualShop = shopSession.Nodes[location].Shop!;
        var tiles = shopInitial.Sections.Single(section => section.ItemFlow).Choices;
        check(tiles.Count == actualShop.Offers.Length && actualShop.Offers.All(offer =>
            tiles[offer.Slot].Price == offer.Value?.Cost &&
            (offer.Value?.Card is not { } card || tiles[offer.Slot].Object?.CardSnapshot == card)),
            "actual main shop tiles retain every stock slot, quoted price and original card snapshot");
        var purchase = actualShop.Offers.First(offer => offer.IsKnown && offer.Affordable == true &&
            offer.Value is { Stocked: true, Card: not null });
        var tile = tiles[purchase.Slot];
        check(tile.Enabled && tile.PreviewFactory != null, "actual Courier card tile exposes its existing buy callback and lazy restock preview");
        timer.Restart();
        var preview = tile.PreviewFactory!();
        double shopColdMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var warm = tile.PreviewFactory();
        double shopWarmMs = timer.Elapsed.TotalMilliseconds;
        var restock = preview?.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
            .SingleOrDefault(row => row.Action == OperationPreviewAction.Restock);
        check(preview is { Faults.IsEmpty: true } && ReferenceEquals(preview, warm) && restock != null,
            "actual Courier hover returns one receipt-owned restock result and caches repeated hover");
        assertShopReadOnly();
        check(shopPresenter.ApplyChoice(shopInitial.NodeId, tile.Id), "hovered actual shop tile remains purchasable");
        var acceptedShop = shopSession.Nodes[location].Shop!;
        var replacement = acceptedShop.Offers.Single(offer => offer.Slot == purchase.Slot).Value!;
        check(acceptedShop.AppliedActions == 1 && acceptedShop.ResolvedActions[0].ExpectedOffer is { } bound &&
            bound.Slot == purchase.Slot && bound.StockRevision == purchase.Value!.StockRevision &&
            replacement.StockRevision > bound.StockRevision && restock!.Price == replacement.Cost &&
            restock.Cards.SequenceEqual(replacement.Card == null ? [] : new[] { replacement.Card }) &&
            restock.Models.SequenceEqual(replacement.Card == null && replacement.Key is { } key ? [key] : []),
            "Courier hover and clicked plan agree on actual stock occurrence, replacement identity and quoted price");
        check(tile.PreviewFactory() == null && shopInitial.IsCurrent?.Invoke() == false,
            "old Courier factory and detail retire after the actual purchase revision");
        var shopPicture = shopPresenter.Detail(location)!;
        var nextTile = shopPicture.Sections.Single(section => section.ItemFlow).Choices[purchase.Slot];
        check(nextTile.Enabled && nextTile.PreviewFactory != null && nextTile.Price == replacement.Cost &&
            shopPicture.Sections.SelectMany(section => section.Choices).Any(choice => choice.Label == M("remove_shop_step")),
            "replacement shop tile and purchase-plan editing remain available after actual restock");
        check(shopPresenter.ApplyChoice(shopPicture.NodeId, nextTile.Id) &&
            shopSession.GetPlan(act - 1, first)!.Shop!.Actions.Count(action => action.Action == PredictorShopActionKind.Buy) == 2 &&
            shopSession.Nodes[location].Shop!.AppliedActions == 2,
            "the replacement Courier stock can be bought through the newly bound same-slot callback");
        shopPicture = shopPresenter.Detail(location)!;
        timings.Add(new { stage = "courier-hover", coldMs = shopColdMs, warmMs = shopWarmMs, slot = purchase.Slot });

        var poorSession = RoomSession(MapPointType.Shop, ["THE_COURIER"], 0);
        var poor = new InRunMapNodePresenter(poorSession, names, text).Detail(location)!;
        var poorTiles = poor.Sections.Single(section => section.ItemFlow).Choices;
        var unaffordable = poorSession.Nodes[location].Shop!.Offers.Where(offer => offer.Affordable == false).ToArray();
        check(unaffordable.Length > 0 && unaffordable.All(offer =>
            !poorTiles[offer.Slot].Enabled && poorTiles[offer.Slot].Status!.Contains(M("shop_insufficient_gold"), StringComparison.Ordinal)),
            "actual shop affordability remains disabled with its localized cause in the main tile");
        check(nativeSignature() == nativeBefore && production.Plans.IsEmpty,
            "controlled room generation, hover and explicit detached edits preserve the native game and original session plans");
        Console.WriteLine("IN_RUN_MAP_ACTUAL_ROOMS " + JsonSerializer.Serialize(timings));
        return (restPicture, shopPicture, plainSmith);
    }
}
