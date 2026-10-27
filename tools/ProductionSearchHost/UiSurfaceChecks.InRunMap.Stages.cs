using System.Collections.Immutable;
using System.Reflection;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;

internal static partial class UiSurfaceChecks
{
    private static InRunMapNodeDetailData CheckInRunMapEventStages(PredictorRouteNodeKnowledge observed,
        MapPosition point, IGameContentNameResolver names, IUiTextProvider text, Action<bool, string> check)
    {
        // Existing presenter/session contracts with authored steps; this fixture
        // supplies display receipts and never pretends to simulate the real event.
        var session = new InRunMapPredictionSession();
        var presenter = new InRunMapNodePresenter(session, names, text);
        var key = new ModelKey("EVENT", "TRIAL");
        var end = new PredictorEventOptionResultKnowledge(PredictorEventNeededInput.None, [], [], [], [], []);
        PredictorEventOptionKnowledge Option(string id, PredictorEventOptionResultKnowledge result) =>
            new(id, new(id), true, []) { ResultPreview = result };
        var last = Option("SHARE_KNOWLEDGE", end);
        var otherLast = Option("RIP", end);
        var middle = Option("NONDESCRIPT_INNOCENT", end);
        var otherMiddle = Option("NONDESCRIPT_GUILTY", end);
        var first = Option("FIGHT", end with { Options = [middle, otherMiddle] });
        var otherFirst = Option("LEAVE", end);
        void Observe(int applied, PredictorEventNeededInput needed, ImmutableArray<PredictorEventOptionKnowledge> options)
        {
            var node = observed with { Room = PredictorRoomKind.Event, Model = key, Rewards = [], PendingCauses = [],
                CombatRequirements = null, PotionBoundaries = [], Event = new(key, options, [], [], [], [], [])
                { EntryOptions = [first, otherFirst], AppliedActions = applied, NeededInput = needed,
                    BusinessChoiceRequired = needed == PredictorEventNeededInput.Option } };
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Nodes),
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
                ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty.Add((0, point), node));
            // With no captured run this fixture cannot publish a real session
            // revision. Retire bindings as production does on a new receipt.
            typeof(InRunMapNodePresenter).GetMethod("Invalidate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(presenter, null);
        }
        InRunMapNodeDetailData Detail() => presenter.Detail((0, point))!;
        InRunMapDetailSection[] Rows(InRunMapNodeDetailData data) => data.Sections.Where(section => section.BranchRow).ToArray();
        Observe(0, PredictorEventNeededInput.Option, [first, otherFirst]);
        var initial = Detail();
        check(Rows(initial).Length == 1 && session.Plans.IsEmpty && initial.Sections.All(section => section.OutcomePreview == null),
            "stage projection and hover binding do not author choices or expose automatic branch outcomes");
        check(presenter.ApplyChoice(initial.NodeId, Rows(initial)[0].Choices[0].Id), "first stage uses its current opaque action");
        Observe(1, PredictorEventNeededInput.Option, [middle, otherMiddle]);
        var second = Detail();
        check(Rows(second).Length == 2 && Rows(second)[0].Choices[0].Selected && Rows(second)[1].Choices.All(choice => !choice.Selected) &&
            Rows(second).Select(row => row.StableId).Distinct().Count() == 2 &&
            Rows(second).SelectMany(row => row.Choices).Select(choice => choice.PreviewKey).Distinct().Count() == 4,
            "next event stage appends below the retained selected first row");
        check(presenter.ApplyChoice(second.NodeId, Rows(second)[1].Choices[0].Id), "second stage authors its explicit action prefix");
        var actions = session.Plans.Single().Event!.Actions;
        check(actions.Length == 2 && actions[0].ExpectedOption == first.Value && actions[1].ExpectedOption == middle.Value,
            "two stage choices preserve actual ordering");
        var health = new PredictorPlannedEventAction(PredictorEventActionKind.Health) { Health = new(20, 60) };
        session.SetPlan(0, point, session.Plans.Single() with { Event = new(key) { Actions = actions.Add(health) } });
        Observe(3, PredictorEventNeededInput.Option, [last, otherLast]);
        var third = Detail();
        check(Rows(third).Length == 3 && Rows(third).Take(2).All(row => row.Choices.Count(choice => choice.Selected) == 1),
            "a non-option input retains earlier stage rows and adds the next real page");
        string retired = Rows(third)[1].Choices[0].Id;
        check(presenter.ApplyChoice(third.NodeId, Rows(third)[2].Choices[0].Id), "third stage follows its existing health prefix");
        Observe(4, PredictorEventNeededInput.None, []);
        var completed = Detail();
        check(Rows(completed).Length == 3 && Rows(completed).All(row => row.Choices.Count(choice => choice.Selected) == 1) &&
            Rows(completed)[2].Choices[1].Preview!.Notices.Contains(text.Get("predictor.map.event_stage_history_hover")) &&
            !presenter.ApplyChoice(completed.NodeId, retired),
            "matched display history keeps selected rows, discloses unverified outcomes and rejects old callbacks");
        var selectedCard = observed.Rewards.First(reward => reward.IsKnown && reward.Value?.Cards.IsEmpty == false).Value!.Cards[0];
        var acceptedCards = new PredictorPlannedEventAction(PredictorEventActionKind.Cards) { ExpectedCards = [selectedCard], CardIndices = [0] };
        session.SetPlan(0, point, session.Plans.Single() with { Event = session.Plans.Single().Event! with
            { Actions = session.Plans.Single().Event!.Actions.Add(acceptedCards) } });
        Observe(5, PredictorEventNeededInput.None, []); completed = Detail();
        Console.WriteLine("IN_RUN_MAP_STAGE_HISTORY_TARGET " + System.Text.Json.JsonSerializer.Serialize(new {
            actions = session.Plans.Single().Event!.Actions.Select(action => new { action.Action, cards = action.ExpectedCards.Select(card => card.Key.Serialized) }),
            actual = completed.Sections.Select(section => section.Text), expected = text.Format("predictor.map.event_stage_selected_cards",
                names.Resolve(selectedCard.Key, GameContentKind.Card)) }));
        check(completed.Sections.Any(section => section.Text == text.Format("predictor.map.event_stage_selected_cards",
            names.Resolve(selectedCard.Key, GameContentKind.Card))),
            "a cached historical page retains its explicitly authored card target without inventing the old candidate set or artwork");
        var restored = new InRunMapNodePresenter(session, names, text).Detail((0, point))!;
        check(Rows(restored)[2].Choices.Count == 1 && !Rows(restored)[2].Choices[0].Enabled &&
            Rows(restored)[2].Text == text.Get("predictor.map.event_stage_history_unavailable"),
            "restored plans without sibling history retain the known selected option without inventing alternatives");
        check(presenter.ApplyChoice(completed.NodeId, Rows(completed)[2].Choices[1].Id) &&
            session.Plans.Single().Event!.Actions is { Length: 4 } edited && edited[2] == health && edited[3].ExpectedOption == otherLast.Value,
            "a matched historical sibling rebinds to the current context and edits the actual stage prefix");
        Observe(4, PredictorEventNeededInput.None, []);
        var changed = Detail();
        check(presenter.ApplyChoice(changed.NodeId, Rows(changed)[0].Choices[1].Id) &&
            session.Plans.Single().Event!.Actions is { Length: 1 } reset && reset[0].ExpectedOption == otherFirst.Value,
            "changing the first stage explicitly retires its later authored suffix");
        check(Rows(completed).SelectMany(row => row.Choices).All(choice => !choice.Label.Contains("Missing translation", StringComparison.OrdinalIgnoreCase)),
            "the display-only multi-stage fixture uses existing localized option keys");
        return completed with { Title = "Three-stage display fixture", Subtitle = "Synthetic receipts; not a real Trial route",
            IsCurrent = null, EditorActions = [] };
    }
}
