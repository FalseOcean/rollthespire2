using System.Collections.Immutable;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.Icons;

internal static partial class UiSurfaceChecks
{
    private static async Task RunInRunMapPlayerInputsSmoke(Node root, string output, NMapScreen screen,
        InRunMapPredictionPanel panel, PredictorRouteNodeKnowledge observed, MapPosition point, Action<bool, string> check)
    {
        // Source-shaped observations isolate presenter intent from simulation.
        // SetPlan still validates every callback's authored plan, but a session
        // without a Snapshot cannot change the production forecast/native run.
        var text = (IUiTextProvider)DiagnosticField(panel, "_text");
        var names = (IGameContentNameResolver)DiagnosticField(panel, "_names");
        var product = (InRunMapNodeDetails)DiagnosticField(panel, "_details");
        var session = new InRunMapPredictionSession();
        var presenter = new InRunMapNodePresenter(session, names, text);
        var pictures = new List<(string File, InRunMapNodeEditorData? Editor, InRunMapNodeDetailData? Detail)>();
        string nodeId = observed.Position.NodeId;
        string R(string key) => text.Get("predictor.runtime." + key);
        string M(string key) => text.Get("predictor.map." + key);
        void Observe(PredictorRouteNodeKnowledge node)
        {
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Nodes),
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
                ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty.Add((0, point), node));
            // A production recompute advances this generation. Match that
            // boundary when replacing the isolated observation as well.
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Revision),
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, session.Revision + 1);
            presenter = new(session, names, text);
        }
        InRunMapNodeEditorData Editor() => presenter.Editor((0, point))!;
        InRunMapDetailChoice[] EditorChoices() => Editor().Sections.SelectMany(section => section.Choices).ToArray();
        bool Apply(InRunMapDetailChoice choice) => presenter.ApplyChoice(nodeId, choice.Id);
        PredictorRouteNodeKnowledge Room(PredictorRoomKind kind) => observed with
        {
            Room = kind, Model = null, Phase = PredictorPhase.Complete, Advanced = true,
            Rewards = [], PendingCauses = [], CombatRequirements = null, CombatCards = [], CombatRelics = [],
            RestOffers = [], Shop = null, Treasure = null, Ancient = null, Event = null, PotionBoundaries = [],
            Diagnostics = null, RewardObtainChoice = null
        };

        var relic = new ModelKey("RELIC", "GORGET");
        Observe(Room(PredictorRoomKind.Treasure) with
        { Treasure = new(relic, [], [], [], []) { Opened = true, RelicTaken = true, Advanced = true, NormalGold = 47, GoldBalance = 547 } });
        var treasureChoices = EditorChoices();
        check(treasureChoices.Count(choice => choice.Label == R("plan.take")) == 1 &&
            treasureChoices.Count(choice => choice.Label == R("plan.skip")) == 1 &&
            !treasureChoices.Any(choice => choice.Label == R("open_treasure") || choice.Label == R("leave")),
            "completed treasure retains exactly one Take and Skip with no procedural Open/Leave control");
        check(Apply(treasureChoices.Single(choice => choice.Label == R("plan.take"))) &&
            session.Plans.Single().Treasure!.Actions.Select(action => action.Action)
                .SequenceEqual([PredictorTreasureActionKind.Open, PredictorTreasureActionKind.TakeRelic]) &&
            session.Plans.Single().Treasure!.Actions[1].ExpectedRelic == relic,
            "treasure Take authors opening and the actual relic receipt without a closing action");
        check(Apply(EditorChoices().Single(choice => choice.Label == R("plan.skip"))) &&
            session.Plans.Single().Treasure!.Actions.Select(action => action.Action)
                .SequenceEqual([PredictorTreasureActionKind.Open, PredictorTreasureActionKind.Leave]),
            "treasure Skip replaces receipt intent while preserving opening gold/quest settlement");
        check(Apply(EditorChoices().Single(choice => choice.Label == R("plan.take"))) &&
            session.Plans.Single().Treasure!.Actions[^1].Action == PredictorTreasureActionKind.TakeRelic,
            "an automatically completed treasure can be changed from Skip back to Take");
        pictures.Add(("in-run-map-player-inputs-treasure.png", Editor(), null));

        session = new();
        var target = observed.Cards.First(card => card.IsKnown && card.Value.Id > 0).Value;
        var mirror = new ModelKey("RELIC", "DOLLYS_MIRROR");
        var circlet = new ModelKey("RELIC", "CIRCLET");
        PredictorShopOfferSnapshot Stock(int slot, PredictorShopEntryKind kind, ModelKey? key, bool stocked = true) =>
            new(slot, 0, kind, key, null, 100, 100, false, stocked);
        var mirrorStock = Stock(0, PredictorShopEntryKind.Relic, mirror);
        var circletStock = Stock(1, PredictorShopEntryKind.Relic, circlet);
        var removalStock = Stock(2, PredictorShopEntryKind.CardRemoval, null);
        var buyMirror = new PredictorPlannedShopAction(PredictorShopActionKind.Buy) { ExpectedOffer = mirrorStock };
        var mirrorChild = new PredictorPlannedShopAction(PredictorShopActionKind.Obtain)
        { Obtain = new(PredictorObtainChildAction.ChooseDeckCards, mirror)
            { DeckCards = [new(target.Id, target.Key, target.FloorAdded)] } };
        var beginRemoval = new PredictorPlannedShopAction(PredictorShopActionKind.BeginRemoval) { ExpectedOffer = removalStock };
        var removalChild = new PredictorPlannedShopAction(PredictorShopActionKind.ChooseRemoval)
        { Card = new(target.Id, target.Key, target.FloorAdded) };
        var shopNode = Room(PredictorRoomKind.Shop) with
        {
            Shop = new([new(0, PredictorShopEntryKind.Relic, mirrorStock with { Stocked = false }, []) { Affordable = true },
                new(1, PredictorShopEntryKind.Relic, circletStock, []) { Affordable = true },
                new(2, PredictorShopEntryKind.CardRemoval, removalStock with { Stocked = false }, []) { Affordable = true }], [])
            { Advanced = true, AppliedActions = 4, GoldBalance = 300 }
        };
        Observe(shopNode);
        check(session.SetPlan(0, point, new(nodeId) { Shop = new() { Actions = [buyMirror, mirrorChild, beginRemoval, removalChild] } }),
            "completed shop fixture records two source-bound operation groups");
        var shopChoices = EditorChoices();
        var circletPurchase = Editor().Sections.Single(section => section.Objects.Any(item => item.Key == circlet))
            .Choices.Single(choice => choice.Label == R("plan.take"));
        check(!shopChoices.Any(choice => choice.Label == R("leave")) &&
            circletPurchase.Enabled,
            "completed shop retains one actionable remaining offer without an extra Leave control");
        check(Apply(circletPurchase) &&
            session.Plans.Single().Shop!.Actions.Length == 5 &&
            session.Plans.Single().Shop!.Actions.Take(4).SequenceEqual([buyMirror, mirrorChild, beginRemoval, removalChild]) &&
            session.Plans.Single().Shop!.Actions[^1].ExpectedOffer == circletStock,
            "buying again from a completed shop appends to all accepted purchases and necessary children");
        var buyCirclet = session.Plans.Single().Shop!.Actions[^1];
        Observe(shopNode with { Shop = shopNode.Shop! with { AppliedActions = 5 } });
        var shopSteps = Editor().Sections.Where(section => section.Choices.Any(choice => choice.Label == M("remove_shop_step"))).ToArray();
        check(shopSteps.Length == 3 && shopSteps.All(section => section.Choices.Count(choice => choice.Label == M("remove_shop_step")) == 1),
            "shop editor shows each buy/removal group exactly once rather than listing child actions as independent steps");
        pictures.Add(("in-run-map-player-inputs-shop.png", Editor(), null));
        check(Apply(shopSteps[1].Choices.Single(choice => choice.Label == M("move_earlier"))) &&
            session.Plans.Single().Shop!.Actions.SequenceEqual([beginRemoval, removalChild, buyMirror, mirrorChild, buyCirclet]),
            "moving removal earlier keeps its target child attached and preserves purchase/obtain order");
        shopSteps = Editor().Sections.Where(section => section.Choices.Any(choice => choice.Label == M("remove_shop_step"))).ToArray();
        check(Apply(shopSteps[0].Choices.Single(choice => choice.Label == M("move_later"))) &&
            session.Plans.Single().Shop!.Actions.SequenceEqual([buyMirror, mirrorChild, beginRemoval, removalChild, buyCirclet]),
            "moving removal later restores the full authored operation order");
        shopSteps = Editor().Sections.Where(section => section.Choices.Any(choice => choice.Label == M("remove_shop_step"))).ToArray();
        check(Apply(shopSteps[0].Choices.Single(choice => choice.Label == M("remove_shop_step"))) &&
            session.Plans.Single().Shop!.Actions.SequenceEqual([beginRemoval, removalChild, buyCirclet]),
            "revoking a relic purchase removes its necessary obtain child as one operation");
        shopSteps = Editor().Sections.Where(section => section.Choices.Any(choice => choice.Label == M("remove_shop_step"))).ToArray();
        check(Apply(shopSteps[0].Choices.Single(choice => choice.Label == M("remove_shop_step"))) &&
            session.Plans.Single().Shop!.Actions.SequenceEqual([buyCirclet]),
            "revoking removal removes both begin and selected target without touching the later purchase");

        session = new();
        var resolvedMirror = buyMirror with { ExpectedOffer = mirrorStock with { StockRevisionIsOccurrence = true } };
        var resolvedRemoval = beginRemoval with { ExpectedOffer = removalStock with { StockRevisionIsOccurrence = true } };
        Observe(shopNode with { Shop = shopNode.Shop! with { ResolvedActions =
            [resolvedMirror, mirrorChild, resolvedRemoval, removalChild] } });
        session.SetPlan(0, point, new(nodeId) { Shop = new() { Actions = [buyMirror, mirrorChild, beginRemoval, removalChild] } });
        shopSteps = Editor().Sections.Where(section => section.Choices.Any(choice => choice.Label == M("remove_shop_step"))).ToArray();
        check(session.Plans.Single().Shop!.Actions[0].ExpectedOffer!.StockRevisionIsOccurrence == false,
            "observing a legacy shopping plan does not silently rewrite its saved binding");
        check(Apply(shopSteps[1].Choices.Single(choice => choice.Label == M("move_earlier"))) &&
            session.Plans.Single().Shop!.Actions.SequenceEqual([resolvedRemoval, removalChild, resolvedMirror, mirrorChild]),
            "explicit reordering uses the successfully replayed stable bindings and preserves child actions");

        session = new();
        var conditional = new ModelKey("EVENT", "ENDLESS_CONVEYOR");
        var fallback = new ModelKey("EVENT", "BRAIN_LEECH");
        var current = new PredictorEventOccurrenceCandidate(conditional, conditional, 2, 11,
            EventCandidateEligibilityKind.RuntimeDependent, false, false) { EligibilityAssumed = true };
        var later = new PredictorEventOccurrenceCandidate(fallback, fallback, 3, 12,
            EventCandidateEligibilityKind.StaticEligible, true, false);
        var occurrence = new PredictorEventOccurrenceObservation(nodeId, observed.Position.Act, 10, [current, later], false);
        var eventNode = Room(PredictorRoomKind.Event) with { Model = conditional,
            Event = new(conditional, [], [], [], [], [], [])
            { Occurrence = occurrence, SelectedOccurrence = current, NeededInput = PredictorEventNeededInput.Option } };
        Observe(eventNode);
        var legacyAction = new PredictorPlannedEventAction(PredictorEventActionKind.Option) { ExpectedOption = new("LEAVE") };
        var legacyPotions = new PredictorEventPotionInput(2);
        check(session.SetPlan(0, point, new(nodeId) { Event = new(conditional)
            { Occurrence = new(conditional, conditional, 2, 10, 11), Actions = [legacyAction], Potions = legacyPotions } }),
            "event fixture records legacy occurrence intent plus event-owned actions and local potions");
        var eventData = presenter.Detail((0, point))!;
        var exclusion = eventData.Sections.SelectMany(section => section.Choices).Where(choice => choice.Label == M("event_exclude")).ToArray();
        check(exclusion.Length == 1 && exclusion[0].Enabled &&
            eventData.Sections.SelectMany(section => section.Objects).Where(item => item.Kind == GameContentKind.Event)
                .Select(item => item.Key).SequenceEqual([conditional, fallback]) &&
            !eventData.Sections.SelectMany(section => section.Choices).Any(choice => choice.Object?.Key == fallback),
            "ordered occurrence display exposes only the current conditional exclusion and no direct future-event selector");
        pictures.Add(("in-run-map-player-inputs-events.png", null, eventData));
        check(Apply(exclusion.Single()) && session.Plans.Single() is { } excluded &&
            excluded.EventOccurrenceExclusions.SequenceEqual([new PredictorEventOccurrenceExclusion(conditional, 11)]) &&
            excluded.Event is { Occurrence: null } retained && retained.ExpectedEvent == conditional &&
            retained.Actions.SequenceEqual([legacyAction]) && retained.Potions == legacyPotions,
            "excluding the current candidate clears only legacy occurrence binding and retains old event actions/potions");
        var excludedCandidate = current with { ExcludedByPlayer = true };
        var unchangedExclusion = new PredictorEventOccurrenceExclusion(new("EVENT", "ZEN_WEAVER"), 7);
        var plan = session.Plans.Single();
        check(session.SetPlan(0, point, plan with { EventOccurrenceExclusions = plan.EventOccurrenceExclusions.Add(unchangedExclusion) }),
            "restore fixture retains another exact queue occurrence exclusion");
        Observe(eventNode with { Model = fallback, Event = eventNode.Event! with
            { Event = fallback, Occurrence = occurrence with { Candidates = [excludedCandidate, later] }, SelectedOccurrence = later,
                AuthoredEvent = conditional, AuthoredActionsNotApplied = true } });
        eventData = presenter.Detail((0, point))!;
        check(!eventData.Sections.SelectMany(section => section.Choices).Any(choice => choice.Label == M("event_exclude")) &&
            eventData.Sections.SelectMany(section => section.Choices).Count(choice => choice.Label == M("event_restore")) == 2,
            "excluded and inactive occurrences each offer restore while the proven unconditional successor cannot be excluded");
        var restore = eventData.Sections.Single(section => section.Objects.Any(item => item.Key == conditional))
            .Choices.Single(choice => choice.Label == M("event_restore"));
        check(Apply(restore) &&
            session.Plans.Single().EventOccurrenceExclusions.SequenceEqual([unchangedExclusion]) &&
            session.Plans.Single().Event!.Actions.SequenceEqual([legacyAction]) && session.Plans.Single().Event!.Potions == legacyPotions,
            "restore removes only its exact node/queue occurrence and leaves independent exclusion and event intent intact");

        var retainedSession = session;
        session = new();
        Observe(eventNode with { Event = eventNode.Event! with { Occurrence = occurrence with { Candidates = [current] } } });
        eventData = presenter.Detail((0, point))!;
        check(session.Plans.IsEmpty && eventData.Sections.Count(section => section.Objects.Any(item => item.Key == conditional)) == 1 &&
            eventData.Sections.SelectMany(section => section.Choices).Count(choice => choice.Label == M("event_exclude")) == 1,
            "a lone current conditional candidate remains visible and excludable without requiring an authored plan or a second candidate");

        session = retainedSession;
        plan = session.Plans.Single();
        var retainedExclusion = new PredictorEventOccurrenceExclusion(conditional, 11);
        check(session.SetPlan(0, point, plan with { EventOccurrenceExclusions = plan.EventOccurrenceExclusions.Add(retainedExclusion) }),
            "changed-admission fixture retains the player's original exact exclusion");
        var nowUnconditional = current with { IsUnconditional = true, ExcludedByPlayer = false,
            EligibilityAssumed = false, EligibilityKind = EventCandidateEligibilityKind.StaticEligible };
        Observe(eventNode with { Event = eventNode.Event! with
            { Occurrence = occurrence with { Candidates = [nowUnconditional] }, SelectedOccurrence = nowUnconditional } });
        eventData = presenter.Detail((0, point))!;
        var retainedRow = eventData.Sections.Single(section => section.Objects.Any(item => item.Key == conditional));
        check(retainedRow.Text == M("event_exclusion_inactive") &&
            retainedRow.Choices.Single().Label == M("event_restore") &&
            !eventData.Sections.SelectMany(section => section.Choices).Any(choice => choice.Label == M("event_exclude")) &&
            session.Plans.Single().EventOccurrenceExclusions.Contains(retainedExclusion),
            "a saved exclusion now matching a proven unconditional candidate retains inactive player provenance and only offers undo");
        check(Apply(retainedRow.Choices.Single()) &&
            session.Plans.Single().EventOccurrenceExclusions.SequenceEqual([unchangedExclusion]) &&
            session.Plans.Single().Event!.Actions.SequenceEqual([legacyAction]) && session.Plans.Single().Event!.Potions == legacyPotions,
            "undoing an inactive now-unconditional occurrence removes only that saved exclusion and preserves event actions/potions");

        // Matching option names across events must not make retained intent
        // look applied or let a stale callback overwrite it. Choosing a newly
        // displayed current-event option remains an explicit user replacement.
        // This source-shaped observation deliberately retains a business choice;
        // optional low-value branches now belong to the premise editor instead.
        var sharedOption = new PredictorEventOptionKnowledge("LEAVE", new("LEAVE"), true, [])
        { ResultPreview = new(PredictorEventNeededInput.None, [], [], [], [], []) };
        Observe(eventNode with { Event = eventNode.Event! with { EntryOptions = [sharedOption], Options = [sharedOption],
            AppliedActions = 1, NeededInput = PredictorEventNeededInput.None, BusinessChoiceRequired = true } });
        var oldPresenter = presenter;
        var oldHeading = presenter.Detail((0, point))!.Sections.Where(section => section.BranchRow)
            .SelectMany(section => section.Choices).Single(choice => choice.Selected);
        check(oldHeading.Selected, "matching authored event highlights its own selected option before the default changes");
        Observe(eventNode with { Model = fallback, Event = eventNode.Event! with
            { Event = fallback, EntryOptions = [sharedOption], Options = [sharedOption], AppliedActions = 0,
                AuthoredEvent = conditional, AuthoredActionsNotApplied = true, BusinessChoiceRequired = true } });
        eventData = presenter.Detail((0, point))!;
        var headings = eventData.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices).ToArray();
        check(headings.Length == 1 && !headings[0].Selected &&
            eventData.Sections.Any(section => section.Text.Contains(names.Resolve(conditional, GameContentKind.Event), StringComparison.Ordinal)),
            "a changed default event displays retained old-event intent and never highlights a coincidentally named old option");
        var preserved = session.Plans.Single();
        check(!oldPresenter.ApplyChoice(nodeId, oldHeading.Id) && session.Plans.Single() == preserved,
            "a callback from the old event observation cannot replace retained event intent after recomputation");
        check(Apply(headings.Single()) && session.Plans.Single().Event is { } replacement &&
            replacement.ExpectedEvent == fallback && replacement.Potions == null &&
            replacement.Actions.Length == 1 && replacement.Actions[0].ExpectedOption == sharedOption.Value &&
            session.Plans.Single().EventOccurrenceExclusions.SequenceEqual([unchangedExclusion]),
            "an explicit current-event option authors only that event's operation and preserves independent exclusions");

        var view = new InRunMapNodeDetails((IGameIconResolver)DiagnosticField(product, "_icons"), names);
        root.AddChild(view); view.SetProcess(false);
        bool visible = product.Visible;
        try
        {
            product.Hide();
            foreach (var picture in pictures)
            {
                if (picture.Editor is { } editor) view.ShowEditor(editor, screen.GetGlobalRect());
                else
                {
                    view.CloseEditor();
                    // Historical display-only pictures do not carry a current
                    // action witness after the fixture has edited its session.
                    view.ShowNode(picture.Detail! with { IsCurrent = null }, new Rect2(new(530, 260), new(30, 30)), screen.GetGlobalRect());
                    if (view.FindChild("NodeSection_occurrence", true, false) is Button occurrenceToggle)
                    {
                        check(!Descendants(view).OfType<Label>().Any(label => label.IsVisibleInTree() &&
                            label.Text.Contains(names.Resolve(fallback, GameContentKind.Event), StringComparison.Ordinal)),
                            "event occurrence judgments start collapsed in the ordinary hover");
                        occurrenceToggle.EmitSignal(Button.SignalName.Pressed);
                    }
                }
                await DiagnosticFrame(root);
                view.UpdateAnchor(new Rect2(new(530, 260), new(30, 30)), screen.GetGlobalRect());
                await DiagnosticFrame(root);
                check(!Descendants(view).OfType<Label>().Any(label => label.Text.Contains("Missing translation", StringComparison.Ordinal)),
                    "player input render fixture has localized labels: " + picture.File);
                if (picture.Detail != null)
                    check(Descendants(view).OfType<Label>().Any(label => label.IsVisibleInTree() &&
                        label.Text.Contains(names.Resolve(fallback, GameContentKind.Event), StringComparison.Ordinal)),
                        "ordered event successors remain rendered instead of being removed as redundant node identity");
                using var image = root.GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(output, picture.File));
            }
        }
        finally { product.Visible = visible; view.QueueFree(); await DiagnosticFrame(root); }
    }
}
