using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;

internal static partial class UiSurfaceChecks
{
    private sealed class InRunMapFailingNameResolver(IGameContentNameResolver inner, ModelKey failedKey,
        Exception error) : IGameContentNameResolver
    {
        public string LanguageCode => inner.LanguageCode;
        public string Resolve(ModelKey key, GameContentKind kind) => key == failedKey
            ? throw error : inner.Resolve(key, kind);
    }

    private static (InRunMapNodeDetailData Failed, InRunMapNodeDetailData Recovered)
        CheckInRunMapFailureBoundaries(InRunMapPredictionPanel panel, InRunMapPredictionSession session,
        IGameContentNameResolver names, IUiTextProvider text, Func<string> signature, Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var actualNodes = session.Nodes.OrderBy(pair => pair.Value.Position.Floor).ToArray();
        check(actualNodes.Length >= 2, "node failure isolation uses at least two actual selected native-route observations");
        var observed = actualNodes[0].Value;
        var point = actualNodes[0].Key.Point;
        string nativeBefore = signature(), plansBefore = JsonSerializer.Serialize(session.Plans),
            snapshotBefore = JsonSerializer.Serialize(session.Snapshot!.Run.Working),
            nodesBefore = JsonSerializer.Serialize(session.Nodes.Values.OrderBy(node => node.Position.Floor));
        long revisionBefore = session.Revision;

        // This exact null/null regression belongs to presentation: occurrence
        // candidates may describe potion requirements before an event exists.
        // No snapshot is attached, so rendering cannot simulate or enter one.
        foreach (var requirement in new[] { PredictorEventPotionRequirement.EmptySlots, PredictorEventPotionRequirement.OrderedSlots })
        foreach (int planMode in new[] { 0, 1, 2 })
        {
            var candidateKey = new ModelKey("EVENT", requirement == PredictorEventPotionRequirement.EmptySlots
                ? "ENDLESS_CONVEYOR" : "THE_FUTURE_OF_POTIONS");
            var candidate = new PredictorEventOccurrenceCandidate(candidateKey, candidateKey, 1, 0,
                EventCandidateEligibilityKind.RuntimeDependent, false, false);
            var pending = PredictorRouteCauseEvidence.Bind(new("MapPotionInventoryNotProjected",
                "Candidate needs a local potion observation", PredictorCombatDomain.PotionInventory), observed.Position.NodeId);
            var fixture = new InRunMapPredictionSession();
            var retainedKey = planMode == 1 ? new ModelKey("EVENT", "FAKE_MERCHANT") : candidateKey;
            var fixtureNode = observed with
            {
                Room = PredictorRoomKind.Event, Model = null, Rewards = [], RestOffers = [],
                PendingCauses = [pending], DeckPendingCauses = [], CombatRequirements = null,
                Shop = null, Ancient = null, Treasure = null, PotionBoundaries = [], Diagnostics = null,
                Event = new(null, [], [], [], [], [], [pending])
                {
                    PotionInputRequirement = requirement, NeededInput = PredictorEventNeededInput.Occurrence,
                    Occurrence = new(observed.Position.NodeId, observed.Position.Act, 0, [candidate], false),
                    AuthoredEvent = planMode == 0 ? null : retainedKey,
                    AuthoredActionsNotApplied = planMode == 2
                }
            };
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Nodes), members)!
                .SetValue(fixture, ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty
                    .Add(actualNodes[0].Key, fixtureNode));
            if (planMode != 0)
                check(fixture.SetPlan(actualNodes[0].Key.ActIndex, point, new(observed.Position.NodeId)
                { Event = new(retainedKey) { Potions = retainedKey.Entry == "ENDLESS_CONVEYOR" ? new(1) :
                    new() { OrderedSlots = [new ModelKey("POTION", "BLOCK_POTION")] } } }),
                    "null-event regression fixture retains valid authored potion intent without a snapshot");
            string authoredBefore = JsonSerializer.Serialize(fixture.Plans);
            var fixturePresenter = new InRunMapNodePresenter(fixture, names, text);
            var detail = fixturePresenter.Detail(actualNodes[0].Key)!;
            var editor = fixturePresenter.Editor(actualNodes[0].Key)!;
            string notice = text.Get("predictor.map.event_potions.event_required"),
                potionTitle = text.Get("predictor.map.event_potions.title"),
                confirmLabel = text.Get("predictor.runtime.confirm");
            check(detail.Sections.Any(section => section.Text == notice) &&
                editor.Sections.Any(section => section.Text == notice) &&
                detail.Sections.Concat(editor.Sections).All(section => section.Title != potionTitle || section.EmbeddedEditor == null) &&
                detail.Sections.Concat(editor.Sections).All(section => section.HeaderChoice?.Label != confirmLabel &&
                    section.Choices.All(choice => choice.Label != confirmLabel)) &&
                editor.Numbers.Count == 0 && editor.Selections.Count == 0,
                "null event with a potion-requiring occurrence candidate yields an unresolved notice and no potion editor: " +
                requirement + "/plan=" + planMode);
            check(JsonSerializer.Serialize(fixture.Plans) == authoredBefore &&
                (planMode != 0 || fixture.Plans.IsEmpty) && fixtureNode.Event!.Event == null,
                "Detail and Editor neither invent an event plan nor rewrite mismatched/retained potion intent");
        }

        var presenter = (InRunMapNodePresenter)DiagnosticField(panel, "_presenter");
        var originalNames = (IGameContentNameResolver)DiagnosticField(presenter, "_names");
        var nameField = typeof(InRunMapNodePresenter).GetField("_names", members)!;
        var render = typeof(InRunMapPredictionPanel).GetMethod("RenderInformation", members)!;
        var failedPair = actualNodes.First(pair => pair.Value.Model is { } model &&
            actualNodes.Any(other => other.Key != pair.Key && other.Value.Model != model));
        var goodPair = actualNodes.First(pair => pair.Key != failedPair.Key && pair.Value.Model != failedPair.Value.Model);
        var detailCache = (Dictionary<(int, MapPosition), InRunMapNodeDetailData>)DiagnosticField(presenter, "_details");
        var data = (Dictionary<MapPosition, InRunMapNodeDetailData>)DiagnosticField(panel, "_nodeDetails");
        var annotations = (Dictionary<MapPosition, Control>)DiagnosticField(panel, "_annotations");
        var failures = (HashSet<MapPosition>)DiagnosticField(panel, "_nodeInformationFailures");
        var error = new InvalidOperationException("FIX05 controlled single-node name resolution failure");
        InRunMapNodeDetailData? failedReceipt = null, recoveredReceipt = null;
        try
        {
            render.Invoke(panel, null);
            var previousBad = data[failedPair.Key.Point];
            var previousGood = data[goodPair.Key.Point];
            var previousEditor = presenter.Editor(failedPair.Key)!;
            var oldChoice = previousEditor.Sections.SelectMany(section => section.Choices).First(choice => choice.Enabled);
            annotations.TryGetValue(goodPair.Key.Point, out var previousGoodAnnotation);
            // Evict only the failed detail cache. Preserve its old editor and
            // callbacks so the actual failure handler must revoke them.
            detailCache.Remove(failedPair.Key);
            nameField.SetValue(presenter, new InRunMapFailingNameResolver(originalNames, failedPair.Value.Model!.Value, error));
            bool actualDetailFailed = false;
            try { presenter.Detail(failedPair.Key); }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex, error)) { actualDetailFailed = true; }
            check(actualDetailFailed, "injected resolver makes actual native-route Detail fail with the original exception");
            render.Invoke(panel, null);
            failedReceipt = data[failedPair.Key.Point];
            check(failures.SetEquals([failedPair.Key.Point]) &&
                failedReceipt.Information.Count > 0 && failedReceipt.Information.All(line => !line.Contains(error.Message, StringComparison.Ordinal)) &&
                failedReceipt.Sections.Count == 0 && failedReceipt.EditorActions.Count == 0 && failedReceipt.Premises == null &&
                !ReferenceEquals(failedReceipt, previousBad) && failedReceipt.IsCurrent?.Invoke() == true &&
                annotations[failedPair.Key.Point].Name.ToString().StartsWith("FailedObservation_", StringComparison.Ordinal),
                "only the failed node receives an explicit current failure placeholder with no stale rewards or actions");
            check(!presenter.ApplyChoice(failedPair.Value.Position.NodeId, oldChoice.Id) &&
                !((System.Collections.IDictionary)DiagnosticField(presenter, "_editors")).Contains(failedPair.Key),
                "actual per-node failure discards its old editor cache and revokes its old callbacks");
            check(ReferenceEquals(data[goodPair.Key.Point], previousGood) && previousGood.IsCurrent?.Invoke() == true &&
                annotations.ContainsKey(goodPair.Key.Point) == (previousGoodAnnotation != null) &&
                (previousGoodAnnotation == null || !ReferenceEquals(annotations[goodPair.Key.Point], previousGoodAnnotation)) &&
                DiagnosticField(panel, "_predictionFault") == null,
                "independent actual node detail and annotation survive rerender without a query-wide prediction fault");
            nameField.SetValue(presenter, originalNames);
            render.Invoke(panel, null);
            recoveredReceipt = data[failedPair.Key.Point];
            check(failures.Count == 0 && recoveredReceipt.Title == previousBad.Title &&
                recoveredReceipt.IsCurrent?.Invoke() == true && failedReceipt.IsCurrent?.Invoke() == false &&
                !annotations[failedPair.Key.Point].Name.ToString().StartsWith("FailedObservation_", StringComparison.Ordinal) &&
                DiagnosticField(panel, "_predictionFault") == null,
                "restoring the resolver recovers the failed actual node and expires its failure placeholder");
        }
        finally
        {
            nameField.SetValue(presenter, originalNames);
            presenter.DiscardNodeProjection(failedPair.Key);
            render.Invoke(panel, null);
        }
        check(signature() == nativeBefore && JsonSerializer.Serialize(session.Plans) == plansBefore &&
            session.Revision == revisionBefore && JsonSerializer.Serialize(session.Snapshot.Run.Working) == snapshotBefore &&
            JsonSerializer.Serialize(session.Nodes.Values.OrderBy(node => node.Position.Floor)) == nodesBefore,
            "null-event and injected presentation-failure checks leave native run/RNG, route knowledge, session plans and revision unchanged");
        return (failedReceipt!, recoveredReceipt!);
    }
}
