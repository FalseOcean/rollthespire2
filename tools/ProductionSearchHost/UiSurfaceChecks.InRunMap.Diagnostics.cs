using System.Reflection;
using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.InRunMap;

internal static partial class UiSurfaceChecks
{
    private sealed record InRunMapDiagnosticsReceipt(PredictorKnowledgeSnapshot Snapshot,
        PredictorKnowledgeFactId FactId, string ExplanationChoiceId, string OriginalNodeId,
        InRunMapPredictionSession Session, InRunMapNodePresenter Presenter,
        Guid ProductionForecastId, PredictorKnowledgeFactId ProductionDeckFactId);
    private static InRunMapPredictionSession InRunMapAuthoredDiagnostics(InRunMapPredictionSession sourceSession,
        PredictorMapRouteDraft draft)
    {
        // The ordinary panel keeps its approved defaults. This independent
        // source projection retains explicit unresolved choices for the DAG,
        // reason-button and stale-forecast checks.
        var captured = sourceSession.Snapshot!;
        var source = captured.Run;
        var run = new PredictorRun(source.Context, source.Working)
        { CombatDefaults = source.CombatDefaults, AutomaticRewardPickup = source.AutomaticRewardPickup };
        var visited = source.Working.Maps.FirstOrDefault(map => map.Act == 1)?.Visited ?? [];
        var path = visited.AddRange(draft.ReadyPrefix);
        if (run.IsLegalRoute(run.RouteMap(1), path, visited)) run.SetRoute(1, path);
        else run.SetRoutePreviewPrefix(1, path);
        var forecast = run.PreviewRouteContinuation([], maximumNodes: 64, scope: PredictorRoutePreviewScope.AllModeledNodes,
            policy: PredictorRoutePreviewPolicy.AuthoredChoices, allowSelectedPrefixes: true);
        var fixture = new InRunMapPredictionSession();
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), members)!.SetValue(fixture, captured);
        typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Forecast), members)!.SetValue(fixture, forecast);
        typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Nodes), members)!.SetValue(fixture,
            forecast.Nodes.ToImmutableDictionary(node =>
            {
                PredictorMapNodeIdentity.TryParse(node.Position.NodeId, out var identity);
                return (identity.Act - 1, identity.Point);
            }));
        return fixture;
    }
    private static object DiagnosticField(object owner, string key) => owner.GetType()
        .GetField(key, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static string DiagnosticText(IUiTextProvider text, string key) => text.Get("predictor.map.diagnostics." + key);
    private static Button DiagnosticButton(InRunMapNodeDetails view, string id) => Descendants(view).OfType<Button>()
        .Single(button => button.IsVisibleInTree() && button.HasMeta("choice_id") && button.GetMeta("choice_id").AsString() == id);
    private static async Task DiagnosticFrame(Node root)
    {
        for (int i = 0; i < 5; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static string RenderedDiagnostics(InRunMapNodeDetails details) => string.Join("\n",
        Descendants(details.FindChild("PersistentEditor", true, false)!).OfType<Label>().Select(label => label.Text));

    private static void CheckInRunMapPrerequisiteProjection(Action<bool, string> check)
    {
        const string source = "map:source:raw-identity", target = "map:target:raw-identity";
        var branch = PredictorRouteCauseEvidence.Create("EventOptionChoice", source,
            PredictorCombatDomain.GoldRewards | PredictorCombatDomain.CardRewards, occurrenceKey: "branch");
        var inherited = PredictorRouteCauseEvidence.CreateExplicit("derived-wrapper", target,
            PredictorCombatDomain.CardRewards, PredictorPendingReasonKind.DerivedDependency,
            "test: the same earlier choice reaches two output consumers", [], [branch]);
        var cutoff = PredictorRouteCauseEvidence.CreateExplicit("RouteRelicDeckRandomnessCutoff", source,
            PredictorCombatDomain.NicheRng, PredictorPendingReasonKind.FlowGate,
            "test: approved acquisition cutoff", ["rng.Niche.state"], occurrenceKey: "cutoff");
        var potionScope = PredictorRouteCauseEvidence.CreateExplicit("MapPotionInventoryNotProjected", source,
            PredictorCombatDomain.PotionInventory, PredictorPendingReasonKind.MissingObservation,
            "test: display drops without simulating slot receipts", ["potions.orderedSlots"]);
        PredictorReward Known(PredictorRewardKind kind) => new(1, kind, PredictorRewardOrigin.Combat,
            default, [], GoldAmount: 31);
        var node = new PredictorRouteNodeKnowledge(new(1, 7, target), PredictorRoomKind.Monster, null,
            PredictorPhase.Rewards,
            [new(PredictorRewardKind.Gold, null, [branch]),
             new(PredictorRewardKind.Card, null, [inherited, cutoff]),
             new(PredictorRewardKind.Potion, Known(PredictorRewardKind.Potion), [])], [], [], [],
            [branch, inherited, cutoff, potionScope]);
        foreach (string language in new[] { "zh", "en" })
        {
            var text = JsonUiTextProvider.CreatePredictorUi13(language);
            string Source(string? id) => text.Format("predictor.map.prerequisite.source", 4,
                text.Get("predictor.runtime.plan.room.Event"));
            var rows = InRunMapPrerequisiteProjection.Project(node, Source, text);
            var shared = rows.Single(row => row.Identity == branch.Identity);
            check(rows.Length == 2 && shared.AffectedResults.SequenceEqual(new[]
                { text.Get("predictor.runtime.reward.Gold"), text.Get("predictor.runtime.reward.Card") }) &&
                rows.Single(row => row.Identity == cutoff.Identity).AffectedResults.SequenceEqual(new[]
                { text.Get("predictor.runtime.reward.Card") }),
                "structured prerequisite projection merges shared real causes and keeps the cutoff limited to its actual card consumer in " + language);
            check(rows.All(row => !row.Text.Contains(source, StringComparison.Ordinal) &&
                !row.Text.Contains("EventOptionChoice", StringComparison.Ordinal) &&
                !row.Text.Contains("FlowGate", StringComparison.Ordinal) &&
                !row.Text.Contains("MapPotionInventoryNotProjected", StringComparison.Ordinal) &&
                !row.Text.Contains("Missing translation", StringComparison.Ordinal)) &&
                shared.Text.Contains(Source(source), StringComparison.Ordinal) &&
                shared.Text.Contains(text.Get("predictor.map.prerequisite.cause.EventOptionChoice"), StringComparison.Ordinal),
                "default prerequisites explain source and actual action without raw IDs, enums or cause codes in " + language);
            var resolved = node with { Rewards = [new(PredictorRewardKind.Gold, Known(PredictorRewardKind.Gold), []),
                new(PredictorRewardKind.Card, Known(PredictorRewardKind.Card), []), node.Rewards[2]] };
            check(InRunMapPrerequisiteProjection.Project(resolved, Source, text).IsEmpty,
                "known independent results do not inherit blanket warnings from prior consumption or unused potion scope in " + language);
            var second = PredictorRouteCauseEvidence.Create("EventOptionChoice", source,
                PredictorCombatDomain.CardRewards, occurrenceKey: "a different actual choice");
            var multiple = node with { Rewards = [new(PredictorRewardKind.Card, null, [branch, second])] };
            check(InRunMapPrerequisiteProjection.Project(multiple, Source, text).Length == 2 &&
                InRunMapPrerequisiteProjection.Pending([branch, second], text).Length == 2,
                "different source choices remain separate even when their translated labels match in " + language);
            var localEvent = new PredictorEventNodeKnowledge(null,
                [new("test", new PredictorEventOption("test"), null, [potionScope])
                    { AvailabilityPendingCauses = [potionScope] }], [], [], [], [], [potionScope]);
            var eventNode = resolved with { Event = localEvent };
            var local = InRunMapPrerequisiteProjection.Project(eventNode, Source, text).Single();
            check(local.Identity == potionScope.Identity && local.AffectedResults.SequenceEqual(new[]
                { text.Get("predictor.map.prerequisite.branch_availability") }) &&
                local.Text.Contains(text.Get("predictor.map.prerequisite.cause.MapPotionInventoryNotProjected"), StringComparison.Ordinal),
                "potion scope is shown when an actual event reader needs it and does not become a player-choice instruction in " + language);
            var health = PredictorRouteCauseEvidence.Create("EventHealthOutcomePending", source, PredictorCombatDomain.Health);
            check(InRunMapPrerequisiteProjection.Pending([health], text).Single() ==
                text.Get("predictor.map.prerequisite.cause.EventHealthOutcomePending"),
                "unmodeled health settlement keeps its actual model-scope meaning in " + language);
            var unknown = PredictorRouteCauseEvidence.CreateExplicit("UntranslatedFixtureCause", source,
                PredictorCombatDomain.CardRewards, PredictorPendingReasonKind.ExecutionFault,
                "test: original fault evidence", []);
            check(InRunMapPrerequisiteProjection.Pending([unknown], text).Single() ==
                text.Get("predictor.map.prerequisite.kind.ExecutionFault"),
                "an untranslated failure remains visible with its structured meaning rather than raw internal text in " + language);
            var noCause = resolved with { Rewards = [new(PredictorRewardKind.Card, null, [])] };
            check(InRunMapPrerequisiteProjection.Project(noCause, Source, text).Single().Text.Contains(
                text.Get("predictor.map.prerequisite.unexplained_result"), StringComparison.Ordinal),
                "an unresolved result without cause evidence is disclosed rather than omitted or invented in " + language);
            var operationOnly = PredictorRouteCauseEvidence.Create("EventCardsChoice", source,
                PredictorCombatDomain.Deck, occurrenceKey: "distinct operation");
            var alternatives = node with { Rewards = [new(PredictorRewardKind.Card, null, [branch])
                { Alternatives = [new("REROLL", 7, [branch, operationOnly])] }] };
            var alternativeRows = InRunMapPrerequisiteProjection.Project(alternatives, Source, text);
            check(alternativeRows.Single(row => row.Identity == branch.Identity).AffectedResults.SequenceEqual(new[]
                { text.Get("predictor.runtime.reward.Card") }) &&
                alternativeRows.Single(row => row.Identity == operationOnly.Identity).AffectedResults.SequenceEqual(new[]
                { text.Format("predictor.map.prerequisite.reward_operations", text.Get("predictor.runtime.reward.Card")) }),
                "an unresolved offer absorbs its same-cause operation scope while independent operation causes remain in " + language);
            var oldParent = new PredictorEventOptionResultKnowledge(PredictorEventNeededInput.Option,
                [], [], [], [], [branch, health]) { LocalPendingCauses = [branch, health] };
            var settledEvent = new PredictorEventNodeKnowledge(null, [], [], [], [],
                [new(PredictorRewardKind.Card, Known(PredictorRewardKind.Card), [])], [health])
                { Advanced = true, NeededInput = PredictorEventNeededInput.None };
            var settledNode = resolved with { Rewards = [], Event = settledEvent, PendingCauses = [health] };
            check(InRunMapPrerequisiteProjection.Project(settledNode, Source, text, oldParent).IsEmpty,
                "later applied actions retire parent preview input warnings while unused settled health scope stays in diagnostics in " + language);
            var dependentNode = settledNode with { Event = settledEvent with
                { Rewards = [new(PredictorRewardKind.Card, null, [health])] } };
            check(InRunMapPrerequisiteProjection.Project(dependentNode, Source, text, oldParent).Single().Identity == health.Identity,
                "a current unknown reward still displays its real health dependency after parent input warnings retire in " + language);
            var blockedEvent = settledEvent with { Rewards = [], Advanced = false,
                NeededInput = PredictorEventNeededInput.Obtain, PendingCauses = [unknown] };
            var blockedParent = oldParent with { NeededInput = PredictorEventNeededInput.Obtain,
                LocalPendingCauses = [unknown] };
            check(InRunMapPrerequisiteProjection.Project(settledNode with { Event = blockedEvent }, Source, text, blockedParent)
                .Single().Identity == unknown.Identity,
                "a surviving failure at the current selected operation boundary remains visible in " + language);
            var missingInput = blockedEvent with { PendingCauses = [potionScope] };
            var missingParent = blockedParent with { LocalPendingCauses = [potionScope] };
            check(InRunMapPrerequisiteProjection.Project(settledNode with { Event = missingInput }, Source, text, missingParent)
                .Single().Identity == potionScope.Identity,
                "a current non-health missing observation survives at an obtain boundary without requiring HP input in " + language);
            var diagnostic = InRunMapNodeDiagnosticsFormatter.Pending([potionScope], text).Single().Text;
            check(diagnostic.Contains(source, StringComparison.Ordinal) && diagnostic.Contains(potionScope.Code, StringComparison.Ordinal) &&
                diagnostic.Contains(potionScope.SourceEvidence, StringComparison.Ordinal) && diagnostic.Contains("potions.orderedSlots", StringComparison.Ordinal),
                "More diagnostics retain original cause, source identity, dependency property and producer evidence in " + language);
        }
    }

    private static (InRunMapNodeDetailData Before, InRunMapNodeDetailData After) CheckInRunMapPrerequisiteRecompute(InRunMapPredictionSession production,
        MapPosition first, MapPosition suffix,
        IGameContentNameResolver names, IUiTextProvider text, Func<string> signature, Action<bool, string> check)
    {
        // Controlled initial input, following the existing route-event fixture:
        // retain native catalogs/RNG and route geometry, fix only room types,
        // event queue, encounter queue and the held Wing Charm. Every observed
        // node below comes from real Recompute; no output node is substituted.
        string beforeNative = signature(), productionPlans = JsonSerializer.Serialize(production.Plans);
        var captured = production.Snapshot!;
        var source = captured.Run;
        var state = source.Working;
        int act = state.Position.Act;
        var configured = state with
        {
            NextInstanceId = state.NextInstanceId + 1,
            Relics = [new(state.NextInstanceId, new("RELIC", "WING_CHARM"), state.Position.Floor)],
            GoldBudget = 1000, VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
            Maps = state.Maps.Select(map => map.Act != act ? map : map with
            { Map = map.Map with { Points = map.Map.Points.Select(point => point.Position == first ? point with
                { Type = MapPointType.Unknown } : point.Position == suffix ? point with
                { Type = MapPointType.Monster } : point).ToArray() } }).ToImmutableArray(),
            Acts = state.Acts.Select(value => value.Act != act ? value : value with
            { RawEvents = [new("EVENT", "THE_LEGENDS_WERE_TRUE")], EventCursor = 0,
                NormalEncounters = [new("ENCOUNTER", "FABRICATOR_NORMAL")], NormalCursor = 0 }).ToImmutableArray()
        };
        var controlled = new PredictorRun(source.Context, configured)
        { CombatDefaults = new(), AutomaticRewardPickup = source.AutomaticRewardPickup };
        var session = new InRunMapPredictionSession();
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), members)!
            .SetValue(session, captured with { Run = controlled });
        typeof(InRunMapPredictionSession).GetField("_drafts", members)!
            .SetValue(session, DiagnosticField(production, "_drafts"));
        session.Recompute();
        check(session.Nodes.Count == 1 && session.Nodes[(act - 1, first)].Event is
            { Event.Entry: "THE_LEGENDS_WERE_TRUE", BusinessChoiceRequired: true, Advanced: false } &&
            !session.Nodes.ContainsKey((act - 1, suffix)) && session.Plans.IsEmpty,
            "actual detached recompute waits at the important Legends branch without inventing a suffix encounter or choice");
        var original = session.Nodes[(act - 1, first)];
        var causes = PredictorRouteCauseEvidence.OriginalCauses(original.PendingCauses);
        var shared = causes.Single(cause => cause.Code == "EventOptionChoice" && cause.NodeId == original.Position.NodeId);
        string Label(string? id) => id == original.Position.NodeId ? text.Format("predictor.map.prerequisite.source", original.Position.Floor,
            names.Resolve(original.Model!.Value, GameContentKind.Event)) : text.Format("predictor.map.prerequisite.source",
            session.Nodes[(act - 1, suffix)].Position.Floor,
            names.Resolve(session.Nodes[(act - 1, suffix)].Model!.Value, GameContentKind.Encounter));
        var rows = InRunMapPrerequisiteProjection.Project(original, Label, text);
        void Evidence(string stage, PredictorRouteNodeKnowledge node, ImmutableArray<InRunMapPrerequisiteView> projected)
            => Console.WriteLine("PREREQUISITE_RECOMPUTE " + JsonSerializer.Serialize(new
            { stage, node = node.Position.NodeId, node.Advanced, projected,
                pending = PredictorRouteCauseEvidence.OriginalCauses(node.PendingCauses)
                    .Select(cause => new { cause.Code, cause.Identity, cause.NodeId }),
                rewards = node.Rewards.Select(reward => new { reward.Kind, reward.IsKnown,
                    causes = PredictorRouteCauseEvidence.OriginalCauses(reward.PendingCauses)
                        .Select(cause => new { cause.Code, cause.Identity, cause.NodeId }),
                    alternatives = reward.Alternatives.Select(alternative => new { alternative.Key,
                        causes = PredictorRouteCauseEvidence.OriginalCauses(alternative.PendingCauses)
                            .Select(cause => new { cause.Code, cause.Identity, cause.NodeId }) }) }) }));
        Evidence("before-source", original, rows);
        var sharedRow = rows.Single(row => row.Identity == shared.Identity);
        var presenter = new InRunMapNodePresenter(session, names, text);
        var detail = presenter.Detail((act - 1, first))!;
        var waiting = presenter.DeferredDetails(act - 1, [first, suffix]);
        var beforeDetail = waiting[(act - 1, suffix)];
        check(sharedRow.AffectedResults.SequenceEqual(new[] { text.Get("predictor.map.prerequisite.node_continuation") }) &&
            waiting.Count == 1 && beforeDetail.Information.Any(line => line.Contains(detail.Title, StringComparison.Ordinal)) &&
            beforeDetail.Information.Contains(InRunMapPrerequisiteProjection.Action(shared, text)) &&
            beforeDetail.Sections.Count == 0 && beforeDetail.Premises == null && beforeDetail.EditorActions.Count == 0 &&
            beforeDetail.IsCurrent?.Invoke() == true && presenter.Detail((act - 1, suffix)) == null,
            "the real deferred suffix names the unresolved source and branch reason without fabricating reward or combat input observations");
        var branch = detail.Sections.SelectMany(section => section.Choices).Single(choice =>
            choice.Label == text.Get("predictor.runtime.option.NAB_THE_MAP"));
        long revision = session.Revision;
        var oldDiagnostics = original.Diagnostics!;
        var oldRoots = oldDiagnostics.Snapshot.Roots.ToArray();
        string oldReasons = string.Join("\n", InRunMapNodeDiagnosticsFormatter.Pending(original.PendingCauses, text).Select(row => row.Text));
        check(presenter.ApplyChoice(original.Position.NodeId, branch.Id) && session.Revision > revision &&
            session.Plans.Single().Event?.Actions.First().ExpectedOption?.Key == "NAB_THE_MAP",
            "the real presenter branch action authors SetPlan and recomputes the detached route");
        var updated = session.Nodes[(act - 1, suffix)];
        var local = PredictorRouteCauseEvidence.OriginalCauses(updated.PendingCauses)
            .Single(cause => cause.Code == "EnemyCreationCount" && cause.NodeId == updated.Position.NodeId);
        var updatedRows = InRunMapPrerequisiteProjection.Project(updated, Label, text);
        Evidence("after", updated, updatedRows);
        check(session.Nodes.Count == 2 && updated.Model?.Entry == "FABRICATOR_NORMAL" &&
            session.Nodes[(act - 1, first)].Advanced && presenter.DeferredDetails(act - 1, [first, suffix]).IsEmpty &&
            beforeDetail.IsCurrent?.Invoke() == false && updatedRows.All(row => row.Identity != shared.Identity) &&
            updatedRows.Single(row => row.Identity == local.Identity).AffectedResults.SequenceEqual(
                new[] { text.Get("predictor.map.prerequisite.card_enchantments") }) &&
            updated.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Gold && reward.IsKnown) &&
            updated.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && !reward.IsKnown && !reward.CardOptions.IsEmpty),
            "the actual NAB branch retires waiting feedback and computes Fabricator rewards with only its independent enchantment limitation pending");
        check(oldDiagnostics.Snapshot.Roots.Any(root => root.RootKey == shared.Identity) &&
            oldDiagnostics.Snapshot.Roots.SequenceEqual(oldRoots) &&
            string.Join("\n", InRunMapNodeDiagnosticsFormatter.Pending(original.PendingCauses, text).Select(row => row.Text)) == oldReasons &&
            updated.Diagnostics!.Results.SelectMany(id => updated.Diagnostics.Snapshot.Explain(id).Roots)
                .All(root => root.RootKey != shared.Identity) &&
            oldReasons.Contains(shared.Code, StringComparison.Ordinal) && oldReasons.Contains(original.Position.NodeId, StringComparison.Ordinal) &&
            InRunMapNodeDiagnosticsFormatter.Pending(updated.PendingCauses, text)
                .Any(row => row.Text.Contains(local.SourceEvidence, StringComparison.Ordinal)),
            "recomputed reward explanations lose the resolved root while the immutable original diagnostics retain its identity and causes");
        check(signature() == beforeNative && JsonSerializer.Serialize(production.Plans) == productionPlans &&
            JsonSerializer.Serialize(controlled.Working) == JsonSerializer.Serialize(configured),
            "real prerequisite authoring/recompute leave the native run, production plans and detached input snapshot unchanged");
        // Historical read-only presentation receipts; old actions never get rebound.
        InRunMapNodeDetailData Display(InRunMapNodeDetailData data) => data with { IsCurrent = null,
            EditorActions = [], DiagnosticsLabel = null, Premises = null,
            Sections = data.Sections.Select(section => section with { Choices = [], HeaderChoice = null }).ToArray() };
        return (Display(beforeDetail), Display(presenter.Detail((act - 1, suffix))!));
    }

    private static async Task<InRunMapDiagnosticsReceipt> RunInRunMapDiagnosticsSmoke(Node root, string output,
        NMapScreen screen, InRunMapPredictionPanel panel, InRunMapPredictionSession session,
        PredictorMapRouteDraft draft, MapPosition first, MapPosition suffix, Func<string> signature, Action<bool, string> check)
    {
        CheckInRunMapPrerequisiteProjection(check);
        var text = (IUiTextProvider)DiagnosticField(panel, "_text");
        var names = (IGameContentNameResolver)DiagnosticField(panel, "_names");
        var prerequisite = CheckInRunMapPrerequisiteRecompute(session, first, suffix, names, text, signature, check);
        var missingCount = CheckInRunMapMissingCountRecompute(session, first, suffix, names, text, signature, check);
        var product = (InRunMapNodeDetails)DiagnosticField(panel, "_details");
        var production = session.Nodes[(0, suffix)].Diagnostics!;
        var productionDeck = production.Before.Select(production.Snapshot.Fact).Single(value => value.PropertyKey == "deck.All.count");
        check(productionDeck.Status == PredictorKnowledgeStatus.Known &&
            production.Snapshot.Explain(productionDeck.Id).Roots.All(value => value.NodeId != session.Nodes[(0, first)].Position.NodeId ||
                value.Kind != PredictorKnowledgeRootKind.UserChoice),
            "ordinary LiveMapDefaults knows the suffix deck count without unresolved optional-card selection roots");
        session = InRunMapAuthoredDiagnostics(session, draft);
        var presenter = new InRunMapNodePresenter(session, names, text);
        var details = new InRunMapNodeDetails((IGameIconResolver)DiagnosticField(product, "_icons"), names);
        root.AddChild(details);
        // These are explicitly read-only photographs of detached receipts.
        // The OS pointer must not dismiss them between rendered frames.
        details.SetProcess(false);
        foreach (var sample in new[] { (Name: "before", Data: prerequisite.Before), (Name: "after", Data: prerequisite.After),
            (Name: "count-source", Data: missingCount.Source), (Name: "count-dependent", Data: missingCount.Dependent),
            (Name: "count-resolved", Data: missingCount.Resolved) })
        {
            details.ShowNode(sample.Data with { IsCurrent = null,
                Subtitle = sample.Data.Subtitle + " · Controlled detached dependency input" },
                new Rect2(new(120, 230), new(56, 56)), screen.GetGlobalRect());
            // Automatic hover processing is suspended for these photographs.
            // Still perform its real layout after each native width/text sort;
            // otherwise an initial unwrapped minimum freezes into a tall frame.
            for (int frame = 0; frame < 8; frame++)
            {
                typeof(InRunMapNodeDetails).GetMethod("LayoutHover", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(details, null);
                await DiagnosticFrame(root);
            }
            var hoverRoot = (VBoxContainer)DiagnosticField(details, "_hoverRoot");
            var hoverScroll = (ScrollContainer)DiagnosticField(details, "_hoverScroll");
            var hoverBody = (VBoxContainer)DiagnosticField(details, "_hoverBody");
            float pinned = hoverRoot.GetChildren().OfType<Control>().Where(child => child != hoverScroll)
                .Sum(child => child.GetCombinedMinimumSize().Y + 7) + 31;
            float body = Math.Max(hoverBody.GetCombinedMinimumSize().Y,
                Math.Max(40, 36 + hoverScroll.GetHScrollBar().GetCombinedMinimumSize().Y));
            check(details.HoverGlobalRegion.Size.Y <= pinned + body + 2,
                "settled dependency photograph has a compact content-bounded panel rather than a frozen initial text minimum: " + sample.Name);
            using var screenshot = root.GetViewport().GetTexture().GetImage();
            screenshot.SavePng(Path.Combine(output, "in-run-map-prerequisite-" + sample.Name + ".png"));
        }
        details.CloseHover();
        details.SetProcess(true);
        details.ChoiceRequested += (nodeId, choiceId) =>
        { if (presenter.ApplyChoice(nodeId, choiceId)) details.ShowEditor(presenter.Editor((0, suffix))!, screen.GetGlobalRect()); };
        var original = session.Nodes[(0, first)];
        var node = session.Nodes[(0, suffix)];
        var diagnostics = node.Diagnostics ?? throw new InvalidOperationException("InRunMap: native session lacks diagnostics");
        var fact = diagnostics.Before.Select(diagnostics.Snapshot.Fact).Single(value => value.PropertyKey == "deck.All.count");
        check(fact.Status is PredictorKnowledgeStatus.Partial or PredictorKnowledgeStatus.Unknown,
            "independent AuthoredChoices suffix retains its explicit unresolved first-node reward choice");
        var explanation = diagnostics.Snapshot.Explain(fact.Id);
        check(explanation.Roots.Any(value => value.NodeId == original.Position.NodeId && value.Kind == PredictorKnowledgeRootKind.UserChoice) &&
            explanation.CausalEdges.Length > 0 && explanation.Facts.Length > 1,
            "actual route diagnosis includes an original reward node and causal property chain");
        string before = signature(), plans = JsonSerializer.Serialize(session.Plans);
        long revision = session.Revision;
        var editor = presenter.Editor((0, suffix))!;
        check(!editor.Sections.Any(section => section.Title == DiagnosticText(text, "before")) &&
            !presenter.Detail((0, suffix))!.Sections.SelectMany(section => section.Choices)
                .Any(choice => choice.Label == DiagnosticText(text, "expand")),
            "diagnostics begin collapsed in the detailed editor and do not occupy ordinary hover");
        details.ShowEditor(editor, screen.GetGlobalRect()); await DiagnosticFrame(root);
        var expand = editor.Sections.SelectMany(section => section.Choices)
            .Single(choice => choice.Label == DiagnosticText(text, "expand"));
        DiagnosticButton(details, expand.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
        editor = presenter.Editor((0, suffix))!;
        check(editor.Sections.Any(section => section.Title == DiagnosticText(text, "before")) &&
            editor.Sections.Any(section => section.Title == DiagnosticText(text, "after")) &&
            RenderedDiagnostics(details).Contains(DiagnosticText(text, "operations"), StringComparison.Ordinal),
            "real editor button expands Before/After and actual operation read/write diagnostics");
        var consumed = diagnostics.After.Select(diagnostics.Snapshot.Fact).Where(value =>
            value.PropertyKey.StartsWith("rng.", StringComparison.Ordinal) && value.PropertyKey.EndsWith(".consumed", StringComparison.Ordinal)).ToArray();
        check(consumed.Length == Enum.GetValues<PredictorStream>().Length &&
            InRunMapNodeDiagnosticsFormatter.RngConsumption(diagnostics).SequenceEqual(consumed.Select(value => value.Id)) &&
            consumed.All(value => editor.Sections.Any(section => section.Title ==
                InRunMapNodeDiagnosticsFormatter.Fact(diagnostics.Snapshot, value.Id, text).Title)),
            "RNG section displays all eight actual After consumption facts, excluding cause-only properties");
        var reason = editor.Sections.Single(section => section.Title ==
            InRunMapNodeDiagnosticsFormatter.Fact(diagnostics.Snapshot, fact.Id, text).Title).Choices.Single();
        var oldScroll = (ScrollContainer)DiagnosticField(details, "_editorScroll");
        oldScroll.ScrollVertical = 180; await DiagnosticFrame(root);
        check(oldScroll.ScrollVertical > 0, "diagnostic reason probe begins below the top of the long fact list");
        DiagnosticButton(details, reason.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
        editor = presenter.Editor((0, suffix))!;
        var scroll = (ScrollContainer)DiagnosticField(details, "_editorScroll");
        var editorLabels = Descendants(details.FindChild("PersistentEditor", true, false)!).OfType<Label>().ToArray();
        string summary = InRunMapNodeDiagnosticsFormatter.Fact(diagnostics.Snapshot, fact.Id, text).Title;
        var summaryLabel = editorLabels.Single(label => label.Text == summary);
        var explanationTitle = editorLabels.Single(label => label.Text == DiagnosticText(text, "explanation"));
        var rootLabel = editorLabels.Single(label => label.Text.StartsWith(DiagnosticText(text, "original_roots"), StringComparison.Ordinal));
        var visibleRootStart = new Rect2(rootLabel.GetGlobalRect().Position,
            new Vector2(rootLabel.Size.X, Math.Min(rootLabel.Size.Y, 100)));
        check(scroll.ScrollVertical == 0 && editor.Sections[1].Title == summary &&
            editor.Sections[2].Title == DiagnosticText(text, "explanation") &&
            scroll.GetGlobalRect().Encloses(summaryLabel.GetGlobalRect()) &&
            scroll.GetGlobalRect().Encloses(explanationTitle.GetGlobalRect()) &&
            scroll.GetGlobalRect().Encloses(visibleRootStart),
            "one reason click scrolls to the top with selected property, explanation heading and original root text inside the viewport");
        string rendered = RenderedDiagnostics(details);
        check(rendered.Contains(DiagnosticText(text, "original_roots"), StringComparison.Ordinal) &&
            rendered.Contains(DiagnosticText(text, "dependency_graph"), StringComparison.Ordinal) &&
            explanation.Roots.All(value => rendered.Contains(value.NodeId, StringComparison.Ordinal) &&
                (value.ChoiceId == null || rendered.Contains(value.ChoiceId, StringComparison.Ordinal))) &&
            explanation.Facts.All(value => rendered.Contains(value.PropertyKey, StringComparison.Ordinal)) && rendered.Contains("→"),
            "real reason button renders original node/choice roots, intermediate properties and shared causal edges");
        check(signature() == before && session.Revision == revision && JsonSerializer.Serialize(session.Plans) == plans,
            "diagnostic expansion and reason inspection leave native RunState/RNG, detached plans and forecast unchanged");
        using (var image = root.GetViewport().GetTexture().GetImage())
            image.SavePng(Path.Combine(output, "in-run-map-diagnostics-chain.png"));
        var alternative = diagnostics.Before.Select(diagnostics.Snapshot.Fact)
            .First(value => value.Id != fact.Id && value.Status != PredictorKnowledgeStatus.Known);
        var alternativeReason = editor.Sections.Single(section => section.Title ==
            InRunMapNodeDiagnosticsFormatter.Fact(diagnostics.Snapshot, alternative.Id, text).Title).Choices.Single();
        scroll.ScrollVertical = 160; await DiagnosticFrame(root);
        DiagnosticButton(details, alternativeReason.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
        editor = presenter.Editor((0, suffix))!;
        scroll = (ScrollContainer)DiagnosticField(details, "_editorScroll");
        check(scroll.ScrollVertical == 0 && editor.Sections[1].Title ==
            InRunMapNodeDiagnosticsFormatter.Fact(diagnostics.Snapshot, alternative.Id, text).Title,
            "switching the selected reason moves the newly selected property explanation to the visible top");
        scroll.ScrollVertical = 160; await DiagnosticFrame(root);
        int retainedScroll = scroll.ScrollVertical;
        details.ShowEditor(editor, screen.GetGlobalRect()); await DiagnosticFrame(root);
        check(((ScrollContainer)DiagnosticField(details, "_editorScroll")).ScrollVertical == retainedScroll && retainedScroll > 0,
            "ordinary same-node refresh preserves reading position after the reason request was consumed once");
        var receipt = new InRunMapDiagnosticsReceipt(diagnostics.Snapshot, fact.Id, reason.Id, original.Position.NodeId, session, presenter,
            production.Snapshot.ForecastId, productionDeck.Id);
        var collapse = presenter.Editor((0, suffix))!.Sections.SelectMany(section => section.Choices)
            .Single(choice => choice.Label == DiagnosticText(text, "collapse"));
        DiagnosticButton(details, collapse.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
        check(!presenter.Editor((0, suffix))!.Sections.Any(section => section.Title == DiagnosticText(text, "before")),
            "real collapse button removes node diagnostics while retaining the ordinary editor");
        details.CloseEditor();
        details.QueueFree(); await DiagnosticFrame(root);
        return receipt;
    }

    private static void CheckInRunMapDiagnosticsRecomputed(InRunMapPredictionSession session,
        InRunMapNodePresenter presenter, MapPosition suffix, InRunMapDiagnosticsReceipt old, Action<bool, string> check)
    {
        var node = session.Nodes[(0, suffix)];
        var fresh = node.Diagnostics!;
        var fact = fresh.Before.Select(fresh.Snapshot.Fact).Single(value => value.PropertyKey == "deck.All.count");
        _ = presenter.Editor((0, suffix))!;
        check(fresh.Snapshot.ForecastId != old.ProductionForecastId && fact.Id != old.ProductionDeckFactId &&
            fresh.Snapshot.Explain(fact.Id).Roots.All(root => root.NodeId != old.OriginalNodeId || root.Kind != PredictorKnowledgeRootKind.UserChoice) &&
            old.Snapshot.Explain(old.FactId).Roots.Any(root => root.NodeId == old.OriginalNodeId),
            "real Take replaces production fact IDs while approved defaults avoid optional-card roots and the independent AuthoredChoices snapshot stays unchanged");
        old.Session.Recompute(); _ = old.Presenter.Editor((0, suffix));
        check(!old.Presenter.ApplyChoice(node.Position.NodeId, old.ExplanationChoiceId) &&
            ((System.Collections.IDictionary)DiagnosticField(old.Presenter, "_diagnosticExplanations")).Count == 0,
            "obsolete diagnostic action cannot bind its old forecast fact into the recomputed editor");
    }

    private static async Task RunInRunMapDiagnosticsOccurrenceSmoke(Node root, NMapScreen screen,
        InRunMapPredictionPanel panel, RunState native, PredictorMapRouteDraft draft, MapPosition first, MapPosition suffix,
        Func<string> signature, Action<bool, string> check)
    {
        // A separate detached fixture changes only its simulation input. The
        // native session, card Take/Skip receipts and real RunState are retained.
        string before = signature();
        var isolated = new InRunMapPredictionSession();
        isolated.Update(native, "0.111.0", true, new Dictionary<int, PredictorMapRouteDraft> { [0] = draft });
        var captured = isolated.Snapshot!;
        var source = captured.Run;
        var state = source.Working;
        long id = state.NextInstanceId;
        var controlled = new PredictorRun(source.Context, state with
        { NextInstanceId = id + 1,
            Relics = state.Relics.Add(new(id, new ModelKey("RELIC", "PRAYER_WHEEL"), state.Position.Floor)) })
        { CombatDefaults = source.CombatDefaults, AutomaticRewardPickup = source.AutomaticRewardPickup };
        typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot),
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(isolated, captured with { Run = controlled });
        isolated.Recompute();
        check(isolated.Nodes[(0, first)].Rewards.Count(reward => reward.Kind == PredictorRewardKind.Card &&
            reward.DefaultSkipped && reward.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault) == 2 && isolated.Plans.IsEmpty,
            "ordinary PrayerWheel preview defaults both independent card groups to Skip without authored plans");
        isolated = InRunMapAuthoredDiagnostics(isolated, draft);
        var origin = isolated.Nodes[(0, first)];
        var node = isolated.Nodes[(0, suffix)];
        var diagnostics = node.Diagnostics!;
        var fact = diagnostics.Before.Select(diagnostics.Snapshot.Fact).Single(value => value.PropertyKey == "deck.All.count");
        var explanation = diagnostics.Snapshot.Explain(fact.Id);
        var choices = explanation.Roots.Where(value => value.NodeId == origin.Position.NodeId &&
            value.Kind == PredictorKnowledgeRootKind.UserChoice).ToArray();
        check(origin.Rewards.Count(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card) == 2 &&
            choices.Length == 2 && choices.Select(value => value.ChoiceId).Distinct().Count() == 2,
            "actual detached PrayerWheel route supplies two independent reward occurrences at one original node");
        var text = (IUiTextProvider)DiagnosticField(panel, "_text");
        var names = (IGameContentNameResolver)DiagnosticField(panel, "_names");
        var product = (InRunMapNodeDetails)DiagnosticField(panel, "_details");
        var presenter = new InRunMapNodePresenter(isolated, names, text);
        var view = new InRunMapNodeDetails((IGameIconResolver)DiagnosticField(product, "_icons"), names);
        root.AddChild(view);
        view.ChoiceRequested += (nodeId, choiceId) =>
        {
            if (presenter.ApplyChoice(nodeId, choiceId)) view.ShowEditor(presenter.Editor((0, suffix))!, screen.GetGlobalRect());
        };
        try
        {
            var editor = presenter.Editor((0, suffix))!;
            view.ShowEditor(editor, screen.GetGlobalRect()); await DiagnosticFrame(root);
            var expand = editor.Sections.SelectMany(section => section.Choices)
                .Single(choice => choice.Label == DiagnosticText(text, "expand"));
            DiagnosticButton(view, expand.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
            editor = presenter.Editor((0, suffix))!;
            var reason = editor.Sections.Single(section => section.Title ==
                InRunMapNodeDiagnosticsFormatter.Fact(diagnostics.Snapshot, fact.Id, text).Title).Choices.Single();
            DiagnosticButton(view, reason.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
            string rendered = RenderedDiagnostics(view);
            var pending = InRunMapNodeDiagnosticsFormatter.Pending(node.DeckPendingCauses, text);
            check(choices.All(value => rendered.Contains(value.ChoiceId!, StringComparison.Ordinal)) &&
                pending.Count(value => choices.Any(choice => choice.RootKey == value.Identity)) == 2,
                "native rendered DAG and ordinary pending rows retain both same-node reward identities");
            check(signature() == before, "two-occurrence diagnostic rendering does not mutate native RunState or any RNG stream");
        }
        finally { view.CloseEditor(); view.QueueFree(); await DiagnosticFrame(root); }
    }
}
