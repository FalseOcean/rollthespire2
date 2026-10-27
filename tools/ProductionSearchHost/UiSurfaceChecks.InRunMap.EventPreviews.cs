using System.Collections.Immutable;
using System.Reflection;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static InRunMapNodeDetailData CheckInRunMapEventPreviewText(InRunMapPredictionPanel panel,
        PredictorRouteNodeKnowledge observed, MapPosition point, Action<bool, string> check)
    {
        // Isolated display/action contract fixtures. SetPlan validates and
        // records intent; without a Snapshot it cannot simulate an event or
        // change the production prediction session or native game.
        var text = (IUiTextProvider)DiagnosticField(panel, "_text");
        var names = (IGameContentNameResolver)DiagnosticField(panel, "_names");
        var inherited = new PredictorCombatPendingCause("PendingCardChoice", "Unrelated upstream card choice",
            PredictorCombatDomain.Deck) { NodeId = "unrelated-upstream-node", Identity = "fixture.upstream" };
        var health = new PredictorCombatPendingCause("EventHealthOutcomePending", "Deferred health settlement",
            PredictorCombatDomain.Health) { NodeId = observed.Position.NodeId, Identity = "fixture.health" };
        var result = new PredictorEventOptionResultKnowledge(PredictorEventNeededInput.None, [], [], [], [], [inherited, health])
        { PendingHealthCommands = [new(-7, 0)], LocalPendingCauses = [health] };
        var option = new PredictorEventOptionKnowledge("DIVE_INTO_WATER", new("DIVE_INTO_WATER", GoldGain: 47), true, [])
        { ResultPreview = result };
        var cost = new PredictorEventOptionKnowledge("OFFER_TRIBUTE", new("OFFER_TRIBUTE", GoldCost: 50), true, [])
        { ResultPreview = result with { PendingHealthCommands = [], LocalPendingCauses = [] } };
        var session = new InRunMapPredictionSession();
        var missingPotions = new PredictorCombatPendingCause("MapPotionInventoryNotProjected", "Event needs a local potion observation",
            PredictorCombatDomain.PotionInventory) { NodeId = observed.Position.NodeId, Identity = "fixture.local-potions" };
        void SetEvent(ModelKey key, ImmutableArray<PredictorEventOptionKnowledge> options,
            PredictorEventNeededInput needed = PredictorEventNeededInput.Option, int applied = 0,
            ImmutableArray<PredictorRouteCardKnowledge> candidates = default, ImmutableArray<PredictorCard> offered = default,
            PredictorEventPotionRequirement potionRequirement = PredictorEventPotionRequirement.None,
            PredictorRouteInventoryKnowledge? inventory = null, bool potionUnresolved = false, bool authoredStale = false,
            bool advanced = false, PredictorConveyorInput? conveyorInput = null)
        {
            var node = observed with
            {
                Room = PredictorRoomKind.Event, Model = key, Rewards = [], PendingCauses = [], CombatRequirements = null,
                PotionBoundaries = [], Inventory = inventory ?? observed.Inventory,
                Event = new(key, options, candidates.IsDefault ? [] : candidates, offered.IsDefault ? [] : offered, [], [], potionUnresolved ? [missingPotions] : [])
                { EntryOptions = options, NeededInput = needed, AppliedActions = applied,
                    BusinessChoiceRequired = !advanced && needed == PredictorEventNeededInput.Option &&
                        PredictorRouteEventPolicy.For(key).BranchRequired,
                    CardsRequired = needed == PredictorEventNeededInput.Cards ? 1 : 0, PotionInputRequirement = potionRequirement,
                    AuthoredActionsNotApplied = authoredStale, Advanced = advanced, ConveyorInput = conveyorInput }
            };
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Nodes),
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
                ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty.Add((0, point), node));
        }
        void SetOptions(params PredictorEventOptionKnowledge[] options) => SetEvent(new("EVENT", "SUNKEN_STATUE"), options.ToImmutableArray());
        SetOptions(option, cost);
        InRunMapNodeDetailData FullDetail(InRunMapNodePresenter owner)
        {
            var detail = owner.Detail((0, point))!;
            // These fixtures inspect the complete branch editor, whose inputs
            // remain available independently of the selected-only premises.
            // Public branch/premise visibility is checked on the actual main
            // surface by the focused drawing checks.
            var projectionText = (IUiTextProvider)DiagnosticField(owner, "_text");
            return detail with { Sections = owner.Editor((0, point))!.Sections.Where(section =>
                !section.Choices.Any(choice => choice.Label == projectionText.Get("predictor.runtime.plan.clear") ||
                    choice.Label == projectionText.Get("predictor.map.diagnostics.expand"))).ToArray() };
        }
        var presenter = new InRunMapNodePresenter(session, names, text);
        var sections = FullDetail(presenter).Sections;
        check(sections[0].Text.Contains(text.Format("predictor.map.gold_gain", 47), StringComparison.Ordinal) &&
            !sections[0].Text.Contains(text.Format("predictor.map.health_command", -7, 0), StringComparison.Ordinal) &&
            sections[1].Text.Contains(text.Format("predictor.map.gold_cost", 50), StringComparison.Ordinal) &&
            sections[0].HeaderChoice?.Label == text.Get("predictor.runtime.option.DIVE_INTO_WATER") &&
            sections[1].HeaderChoice?.Label == text.Get("predictor.runtime.option.OFFER_TRIBUTE") &&
            sections.All(section => !section.Text.Contains("unrelated-upstream-node", StringComparison.Ordinal) &&
                !section.Text.Contains(text.Get("predictor.runtime.plan.pending.EventHealthOutcomePending"), StringComparison.Ordinal) &&
                !section.Text.Contains(text.Get("predictor.map.event_result_pending"), StringComparison.Ordinal)),
            "event option headings precede their concrete gold values while fixed HP commands and inherited gaps stay out of ordinary output");
        var local = new PredictorCombatPendingCause("EventNonHealthActionDependent", "This output needs a missing relic fact",
            PredictorCombatDomain.Relics) { NodeId = observed.Position.NodeId, Identity = "fixture.local" };
        SetOptions(option with { ResultPreview = result with { LocalPendingCauses = [local] } });
        presenter = new InRunMapNodePresenter(session, names, text);
        string relevant = FullDetail(presenter).Sections.Single().Text;
        check(relevant.Contains(text.Get("predictor.runtime.plan.pending.EventNonHealthActionDependent"), StringComparison.Ordinal) &&
            !relevant.Contains("unrelated-upstream-node", StringComparison.Ordinal),
            "ordinary event output keeps its local dependency hint while detailed ancestral causes stay out of the option text");

        foreach (string language in new[] { "zh", "en" })
        {
            var translated = JsonUiTextProvider.CreatePredictorUi13(language);
            var translatedNames = RuntimeGameContentNameResolver.Create(language);
            var missing = local with { Code = "UnregisteredEventDisplayCause", Description = "Original event diagnostic" };
            session = new(); SetOptions(option with { ResultPreview = result with { LocalPendingCauses = [missing] } });
            presenter = new(session, translatedNames, translated);
            var unknownDetail = presenter.Detail((0, point))!;
            check(unknownDetail.Sections.All(section => !section.Text.Contains(missing.Description)) &&
                unknownDetail.Sections.Single(section => section.BranchRow).Choices.Single().Preview!.Notices
                    .All(line => !line.Contains(missing.Description)),
                "unselected branch and operation preview keep unknown model causes localized without exposing raw diagnostics");
            var dolls = new[] { "DAUGHTER_OF_THE_WIND", "MR_STRUGGLES", "BING_BONG" }
                .Select(entry => new PredictorEventOptionKnowledge("DOLL_" + entry,
                    new("DOLL_" + entry, OfferedRelic: new("RELIC", entry)), true, [])
                { ResultPreview = new(PredictorEventNeededInput.None, [], [], [], [], []) }).ToImmutableArray();
            foreach (string parent in new[] { "TAKE_SOME_TIME", "EXAMINE" })
            {
                session = new();
                SetEvent(new("EVENT", "DOLL_ROOM"), [new(parent, new(parent), true, [])
                { ResultPreview = new(PredictorEventNeededInput.Option, [], [], [], [], [])
                    { Options = dolls.Take(parent == "TAKE_SOME_TIME" ? 2 : 3).ToImmutableArray() } }]);
                presenter = new(session, translatedNames, translated);
                var preview = presenter.Detail((0, point))!.Sections.Single(section => section.BranchRow).Choices.Single().Preview!;
                var expected = dolls.Take(parent == "TAKE_SOME_TIME" ? 2 : 3)
                    .Select(doll => translated.Get("predictor.runtime.plan.take") + " · " +
                        translatedNames.Resolve(doll.Value!.OfferedRelic!.Value, GameContentKind.Relic));
                check(preview.Branches.Select(branch => branch.Label).SequenceEqual(expected) &&
                    preview.Branches.All(branch => !branch.Label!.Contains("Missing translation", StringComparison.Ordinal)) &&
                    session.Plans.IsEmpty,
                    "Doll nested previews use offered relic identity for " + parent + " in " + language + " without authoring a choice");
            }
        }

        var cards = observed.Cards.Where(card => card.IsKnown && card.Value.Id > 0).Take(5).Select(card => card.Value).ToImmutableArray();
        if (cards.Length < 5) throw new InvalidOperationException("EventPreviewUiFixtureNeedsFiveKnownDeckCards");
        var targets = cards.Select(card => new PredictorRouteCardKnowledge(card, [])).ToImmutableArray();
        var brain = new ModelKey("EVENT", "BRAIN_LEECH");
        var share = new PredictorEventOptionKnowledge("SHARE_KNOWLEDGE", new("SHARE_KNOWLEDGE"), true, [])
        { ResultPreview = new(PredictorEventNeededInput.Cards, cards, [], [], [], []) { CardsRequired = 1 } };
        session = new(); SetEvent(brain, [share]);
        presenter = new(session, names, text);
        sections = FullDetail(presenter).Sections;
        var grid = sections.Single(section => section.Choices.Any(choice => choice.Object?.Kind == GameContentKind.Card));
        var targetChoice = grid.Choices.First(choice => choice.Object?.Kind == GameContentKind.Card);
        check(sections[0].HeaderChoice?.Label == text.Get("predictor.runtime.option.SHARE_KNOWLEDGE") &&
            grid.CardColumns == 4 && grid.Choices.Count(choice => choice.Object?.Kind == GameContentKind.Card) == cards.Length,
            "Brain Leech option heading owns one compact target grid with at most four columns");
        check(presenter.ApplyChoice(observed.Position.NodeId, targetChoice.Id) && session.Plans.Single().Event is { } brainPlan &&
            brainPlan.Actions.Length == 2 && brainPlan.Actions[0].ExpectedOption == share.Value &&
            brainPlan.Actions[1].Action == PredictorEventActionKind.Cards && brainPlan.Actions[1].ExpectedCards.SequenceEqual(cards) &&
            brainPlan.Actions[1].CardIndices.SequenceEqual([0]) && brainPlan.Actions[1].DeckCards.IsEmpty &&
            !presenter.ApplyChoice(observed.Position.NodeId, targetChoice.Id),
            "clicking a Brain Leech offer authors its parent and exact offered-card binding atomically and rejects the obsolete choice");

        // A separately selected parent uses the same inline target list; it
        // must not add a second, global card picker after all event options.
        session = new(); SetEvent(brain, [share]);
        session.SetPlan(0, point, new(observed.Position.NodeId)
        { Event = new(brain) { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = share.Value }] } });
        SetEvent(brain, [share], PredictorEventNeededInput.Cards, applied: 1, offered: cards);
        presenter = new(session, names, text);
        check(!presenter.Detail((0, point))!.Sections.Any(section => section.Text == text.Get("predictor.map.event_branch_required")),
            "the required-branch prompt disappears once that event's branch action has applied");
        sections = FullDetail(presenter).Sections;
        check(sections[0].HeaderChoice?.Selected == true &&
            sections.Count(section => section.Choices.Any(choice => choice.Object?.Kind == GameContentKind.Card)) == 1 &&
            sections.Sum(section => section.Choices.Count(choice => choice.Object?.Kind == GameContentKind.Card)) == cards.Length,
            "an immediately selected event parent retains one inline target grid without duplicating its candidates");
        session.SetPlan(0, point, new(observed.Position.NodeId) { Event = new(brain) { Actions =
            [new(PredictorEventActionKind.Option) { ExpectedOption = share.Value },
             new(PredictorEventActionKind.Cards) { ExpectedCards = cards, CardIndices = [1] }] } });
        SetEvent(brain, [share], PredictorEventNeededInput.Cards, applied: 1, offered: cards);
        presenter = new(session, names, text);
        check(!FullDetail(presenter).Sections.SelectMany(section => section.Choices).Any(choice => choice.Selected),
            "a saved event target remains unhighlighted until its child action is actually consumed");
        SetEvent(brain, [share], PredictorEventNeededInput.None, applied: 2);
        presenter = new(session, names, text);
        grid = FullDetail(presenter).Sections.Single(section => section.Choices.Any(choice => choice.Object?.Kind == GameContentKind.Card));
        check(grid.Choices[1].Selected && grid.Choices.Count(choice => choice.Selected) == 1 &&
            FullDetail(presenter).Sections.Count(section => section.Choices.Any(choice => choice.Object?.Kind == GameContentKind.Card)) == 1,
            "an accepted event child highlights its exact offered target once, inside the existing option group");

        session = new();
        var plant = share with { Key = "PLANT", Value = new("PLANT"),
            ResultPreview = new(PredictorEventNeededInput.Cards, [], targets, [], [], []) { CardsRequired = 1 } };
        SetEvent(new("EVENT", "SAPPHIRE_SEED"), [plant]);
        presenter = new(session, names, text);
        targetChoice = FullDetail(presenter).Sections.SelectMany(section => section.Choices)
            .First(choice => choice.Object?.Kind == GameContentKind.Card);
        check(presenter.ApplyChoice(observed.Position.NodeId, targetChoice.Id) && session.Plans.Single().Event is { } plantPlan &&
            plantPlan.Actions.Length == 2 && plantPlan.Actions[0].ExpectedOption == plant.Value &&
            plantPlan.Actions[1].DeckCards.Single().InstanceId == cards[0].Id && plantPlan.Actions[1].ExpectedCards.IsEmpty,
            "an event deck target authors its parent plus stable deck identity without inventing an offered-card index");

        session = new();
        var generated = share with
        { Key = "STUDY", Value = new("STUDY"), ResultPreview = new(PredictorEventNeededInput.None, [], [], [], [], []) { GeneratedCards = targets } };
        SetEvent(new("EVENT", "INFESTED_AUTOMATON"), [generated]);
        presenter = new(session, names, text);
        sections = FullDetail(presenter).Sections;
        check(sections.Single().CardColumns == 4 && sections.Single().Objects.Count == cards.Length &&
            !sections.SelectMany(section => section.Choices).Any(choice => choice.Object?.Kind == GameContentKind.Card),
            "generated event cards use the compact four-column display and have no invented target action");
        var manyGenerated = Enumerable.Range(0, 25).Select(index => targets[index % targets.Length]).ToImmutableArray();
        var unspecified = InRunMapOptionPreview.Build(new("EVENT", "SAPPHIRE_SEED"), "PLANT",
            generated.ResultPreview! with { GeneratedCards = manyGenerated }, []);
        var deckGroups = unspecified.Branches.SelectMany(branch => branch.Sections).Where(section => section.Rows.Length > 0).ToArray();
        check(deckGroups.Length == 1 && deckGroups[0].Style?.Cards == OperationCardLayout.Groups &&
            deckGroups[0].Rows.Select(row => row.Cards.Length).SequenceEqual([10, 10, 5]) &&
            deckGroups[0].Rows.SelectMany(row => row.Cards).SequenceEqual(manyGenerated.Select(item => item.Value)),
            "otherwise unspecified deck changes keep exact ordered snapshots in groups of at most ten");
        var dedicated = InRunMapOptionPreview.Build(new("EVENT", "INFESTED_AUTOMATON"), "STUDY",
            generated.ResultPreview! with { GeneratedCards = manyGenerated }, []);
        check(dedicated.Branches.Single().Sections.Single().Rows.Single().Cards.Length == 25 &&
            OperationDisplayPolicy.Resolve(dedicated).Text == OperationTextMode.None,
            "the map default does not override an existing explicit D source layout");

        session = new();
        var conveyor = new ModelKey("EVENT", "ENDLESS_CONVEYOR");
        SetEvent(conveyor, [], PredictorEventNeededInput.Health);
        presenter = new(session, names, text);
        var localHealth = FullDetail(presenter).Sections.Single().EmbeddedEditor!;
        check(localHealth != null && localHealth.Numbers.Count == 2,
            "Conveyor keeps exactly empty potion slots and lost HP in its atomic premise surface");
        var lostHp = localHealth!.Numbers.Single(number => number.Label == text.Get("predictor.map.conveyor.lost_hp"));
        check(presenter.ApplyField(observed.Position.NodeId, lostHp.Id, new(Number: 20)), "Conveyor lost HP edits its own draft");
        presenter.RebindLocalization(RuntimeGameContentNameResolver.Create(text.LanguageCode == "zh" ? "en" : "zh"),
            JsonUiTextProvider.CreatePredictorUi13(text.LanguageCode == "zh" ? "en" : "zh"));
        check(FullDetail(presenter).Sections.Single().EmbeddedEditor!.Numbers.Any(number => number.Value == 20) && session.Plans.IsEmpty,
            "translation preserves the unconfirmed Conveyor draft without authoring or recomputing a plan");
        presenter.RebindLocalization(names, text);
        localHealth = FullDetail(presenter).Sections.Single().EmbeddedEditor!;
        var emptySlots = localHealth.Numbers.Single(number => number.Label == text.Get("predictor.map.event_potions.empty_count"));
        int suppliedSlots = emptySlots.Value == 1 ? 0 : 1;
        check(presenter.ApplyField(observed.Position.NodeId, emptySlots.Id, new(Number: suppliedSlots)), "Conveyor empty slots edit the same atomic draft");
        localHealth = FullDetail(presenter).Sections.Single().EmbeddedEditor!;
        var confirm = localHealth.Sections.SelectMany(section => section.Choices).Single();
        check(confirm.Enabled && presenter.ApplyChoice(observed.Position.NodeId, confirm.Id) &&
            session.Plans.Single().Event is { Potions: null, Actions.Length: 1 } atomicConveyor &&
            atomicConveyor.Actions[0] is { Action: PredictorEventActionKind.ConveyorInputs, Health: null, Conveyor: { } adoptedConveyor } &&
            adoptedConveyor == new PredictorConveyorInput(suppliedSlots, 20),
            "Conveyor confirmation authors both minimal facts once, without inventing health or whole-route bottle observations");
        var jelly = plant with { Key = "GRAB_JELLY_LIVER", Value = new("GRAB_JELLY_LIVER", GoldCost: 40) };
        SetEvent(conveyor, [jelly], applied: 1, conveyorInput: new(suppliedSlots, 20));
        presenter = new(session, names, text);
        targetChoice = FullDetail(presenter).Sections.SelectMany(section => section.Choices)
            .First(choice => choice.Object?.Kind == GameContentKind.Card);
        check(presenter.ApplyChoice(observed.Position.NodeId, targetChoice.Id) && session.Plans.Single().Event is { } conveyorPlan &&
            conveyorPlan.Actions.Length == 3 && conveyorPlan.Actions[0].Conveyor == new PredictorConveyorInput(suppliedSlots, 20) &&
            conveyorPlan.Actions[1].ExpectedOption == jelly.Value && conveyorPlan.Actions[2].DeckCards[0].InstanceId == cards[0].Id,
            "direct Conveyor target selection preserves its atomic premise prefix and authors the dish plus target in one plan edit");

        session = new();
        SetEvent(conveyor, [], potionRequirement: PredictorEventPotionRequirement.EmptySlots, potionUnresolved: true);
        presenter = new(session, names, text);
        var potionInput = FullDetail(presenter).Sections.Single().EmbeddedEditor!;
        int localSlotCount = potionInput.Numbers.Single(number => number.Label == text.Get("predictor.map.event_potions.empty_count")).Value == 1 ? 0 : 1;
        check(potionInput.Numbers.Count == 2 && potionInput.Selections.Count == 0 &&
            presenter.ApplyField(observed.Position.NodeId, potionInput.Numbers.Single(number =>
                number.Label == text.Get("predictor.map.event_potions.empty_count")).Id, new(Number: localSlotCount)),
            "Conveyor's empty-slot edit stays local to its two-field atomic draft without inventing occupied bottles");
        potionInput = FullDetail(presenter).Sections.Single().EmbeddedEditor!;
        confirm = potionInput.Sections.SelectMany(section => section.Choices).Single();
        check(presenter.ApplyChoice(observed.Position.NodeId, confirm.Id) &&
            session.Plans.Single().Event is { Potions: null, Actions.Length: 1 } localSlots &&
            localSlots.Actions[0].Conveyor == new PredictorConveyorInput(localSlotCount, 0),
            "empty-slot confirmation saves its actual Conveyor tuple without creating a whole-route inventory");
        SetEvent(conveyor, [jelly], potionRequirement: PredictorEventPotionRequirement.EmptySlots, applied: 1, conveyorInput: new(localSlotCount, 0));
        presenter = new(session, names, text);
        targetChoice = FullDetail(presenter).Sections.SelectMany(section => section.Choices)
            .First(choice => choice.Object?.Kind == GameContentKind.Card);
        check(presenter.ApplyChoice(observed.Position.NodeId, targetChoice.Id) &&
            session.Plans.Single().Event is { Potions: null } localConveyor && localConveyor.Actions.Length == 3 &&
            localConveyor.Actions[0].Conveyor == new PredictorConveyorInput(localSlotCount, 0),
            "direct event-result selection retains the confirmed local atomic premise");

        session = new();
        var firstBottle = new ModelKey("POTION", "STRENGTH_POTION");
        var secondBottle = new ModelKey("POTION", "BLOCK_POTION");
        var bottleInventory = new PredictorRouteInventoryKnowledge(PredictorPhase.EventChoice, [], [], [], [], 2,
            [new(0, new PredictorPotion(201, firstBottle), [], false, false), new(1, new PredictorPotion(202, secondBottle), [], false, false)], []);
        var future = new ModelKey("EVENT", "THE_FUTURE_OF_POTIONS");
        SetEvent(future, [], potionRequirement: PredictorEventPotionRequirement.OrderedSlots, inventory: bottleInventory);
        presenter = new(session, names, text);
        check(!FullDetail(presenter).Sections.Any(section => section.EmbeddedEditor != null),
            "a potion-consuming event with sufficient known bottles does not force an extra input form");
        SetEvent(future, [], potionRequirement: PredictorEventPotionRequirement.OrderedSlots, inventory: bottleInventory, potionUnresolved: true);
        presenter = new(session, names, text);
        potionInput = FullDetail(presenter).Sections.Single().EmbeddedEditor!;
        check(potionInput.Numbers.Count == 0 && potionInput.Selections.Select(field => field.Value)
            .SequenceEqual([firstBottle.Serialized, secondBottle.Serialized]) && session.Plans.IsEmpty,
            "available known bottles preset a required local hypothesis without silently authoring it or requiring capacity");
        check(presenter.ApplyField(observed.Position.NodeId, potionInput.Selections[0].Id, new(Selected: secondBottle.Serialized)),
            "local potion selector changes the first bottle hypothesis");
        presenter.RebindLocalization(RuntimeGameContentNameResolver.Create(text.LanguageCode == "zh" ? "en" : "zh"),
            JsonUiTextProvider.CreatePredictorUi13(text.LanguageCode == "zh" ? "en" : "zh"));
        check(FullDetail(presenter).Sections.Single().EmbeddedEditor!.Selections[0].Value == secondBottle.Serialized && session.Plans.IsEmpty,
            "translation preserves unconfirmed ordered bottle draft without authoring a plan");
        presenter.RebindLocalization(names, text);
        potionInput = FullDetail(presenter).Sections.Single().EmbeddedEditor!;
        check(presenter.ApplyField(observed.Position.NodeId, potionInput.Selections[1].Id, new(Selected: firstBottle.Serialized)),
            "local potion selector changes the second bottle hypothesis");
        potionInput = FullDetail(presenter).Sections.Single().EmbeddedEditor!;
        confirm = potionInput.Sections.SelectMany(section => section.Choices).Single(choice => choice.Label == text.Get("predictor.runtime.confirm"));
        check(presenter.ApplyChoice(observed.Position.NodeId, confirm.Id) && session.Plans.Single().Event?.Potions is { } ordered &&
            ordered.EmptySlots == null && ordered.OrderedSlots.SequenceEqual(new ModelKey?[] { secondBottle, firstBottle }),
            "local potion confirmation preserves bottle population order and supplies no slot-capacity fact");
        var localBottleOption = new PredictorEventOptionKnowledge("POTION:LOCAL:0",
            new("POTION:LOCAL:0", CardType: EffectCardType.Attack, LocalPotionOrdinal: 0, LocalPotionKey: secondBottle), true, [])
        { ResultPreview = new(PredictorEventNeededInput.None, [], [], [], [], []) };
        SetEvent(future, [localBottleOption], potionRequirement: PredictorEventPotionRequirement.OrderedSlots, inventory: bottleInventory);
        presenter = new(session, names, text);
        var bottleHeading = FullDetail(presenter).Sections.Single(section => section.HeaderChoice != null).HeaderChoice!;
        check(bottleHeading.Label.Contains(names.Resolve(secondBottle, GameContentKind.Potion), StringComparison.Ordinal) &&
            bottleHeading.Label.Contains(text.Get("predictor.runtime.option.ATTACK"), StringComparison.Ordinal) &&
            presenter.ApplyChoice(observed.Position.NodeId, bottleHeading.Id) && session.Plans.Single().Event is { } futurePlan &&
            futurePlan.Potions!.OrderedSlots.SequenceEqual(new ModelKey?[] { secondBottle, firstBottle }) &&
            futurePlan.Actions.Single().ExpectedOption is { PotionId: null, LocalPotionOrdinal: 0, LocalPotionKey: { } chosenBottle } && chosenBottle == secondBottle,
            "Future's generated option names its hypothetical bottle and retains the local list without fabricating a persistent potion ID");

        // A route-local candidate list contains possible earlier receipts,
        // never the global generation pool or this event's own future output.
        session = new();
        SetEvent(future, [], potionRequirement: PredictorEventPotionRequirement.OrderedSlots, inventory: bottleInventory, potionUnresolved: true);
        var targetNode = session.Nodes[(0, point)] with { Position = observed.Position with { Floor = 8 } };
        var previousBottle = new ModelKey("POTION", "FIRE_POTION");
        var branchBottle = new ModelKey("POTION", "ENERGY_POTION");
        var futureBottle = new ModelKey("POTION", "SWIFT_POTION");
        var poolOnlyBottle = new ModelKey("POTION", "CULTIST_POTION");
        var capturedBottle = new ModelKey("POTION", "DEXTERITY_POTION");
        PredictorCombatRewardKnowledge PotionReward(ModelKey potion) => new(PredictorRewardKind.Potion,
            new(901, PredictorRewardKind.Potion, PredictorRewardOrigin.Event, potion, []), []);
        var priorNode = targetNode with
        { Position = new(1, 7, "fixture.prior-potionevent"), Inventory = null, Rewards = [PotionReward(previousBottle)],
            Event = targetNode.Event! with { EntryOptions = [new("POSSIBLE", new("POSSIBLE"), true, [])
                { ResultPreview = new(PredictorEventNeededInput.None, [], [], [], [PotionReward(branchBottle)], []) }] } };
        targetNode = targetNode with { Rewards = [PotionReward(futureBottle)] };
        var incomingNodes = session.Nodes.SetItem((0, point), targetNode)
            .Add((0, new(101, 7)), priorNode)
            .Add((0, new(101, 9)), priorNode with { Position = new(1, 9, "fixture.future"), Rewards = [PotionReward(futureBottle)], Event = null })
            .Add((1, new(101, 7)), priorNode with { Position = new(2, 7, "fixture.other-act"), Rewards = [PotionReward(poolOnlyBottle)], Event = null });
        typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Nodes), BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, incomingNodes);
        var nativeSnapshot = ((InRunMapPredictionSession)DiagnosticField(panel, "_prediction")).Snapshot!;
        var captured = nativeSnapshot.Run.Working;
        var detachedCapture = new PredictorRun(nativeSnapshot.Run.Context with
        { PotionPool = [new(poolOnlyBottle, PredictorPotionRarity.Common)] }, captured with
        { PotionSlots = [new(captured.NextInstanceId, capturedBottle), null], NextInstanceId = captured.NextInstanceId + 1 });
        typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, nativeSnapshot with { Run = detachedCapture });
        var captureBeforePreview = detachedCapture.Working;
        presenter = new(session, names, text);
        potionInput = presenter.Detail((0, point))!.Sections.Single(section => section.EmbeddedEditor != null).EmbeddedEditor!;
        var actualCandidates = potionInput.Selections[0].Options.Select(candidate => candidate.Id).ToHashSet();
        check(actualCandidates.SetEquals(new[] { firstBottle, secondBottle, capturedBottle, previousBottle, branchBottle }.Select(potion => potion.Serialized)) &&
            !actualCandidates.Contains(futureBottle.Serialized) && !actualCandidates.Contains(poolOnlyBottle.Serialized) &&
            ReferenceEquals(session.Nodes, incomingNodes) && ReferenceEquals(detachedCapture.Working, captureBeforePreview) && session.Plans.IsEmpty,
            "event potion candidates include captured/entry bottles and possible earlier same-act rewards, excluding pool-only/current/future/other-act output without writing source dictionaries");
        check(!potionInput.Sections.SelectMany(section => section.Choices).Single(choice => choice.Label == text.Get("predictor.map.event_potions.add")).Enabled,
            "an ordered local potion list cannot add bottles beyond known incoming capacity");

        session = new();
        SetEvent(future, [], potionRequirement: PredictorEventPotionRequirement.OrderedSlots, inventory: bottleInventory, potionUnresolved: true);
        session.SetPlan(0, point, new(observed.Position.NodeId) { Event = new(future)
        { Potions = new() { OrderedSlots = [firstBottle, null, poolOnlyBottle] } } });
        presenter = new(session, names, text);
        potionInput = presenter.Detail((0, point))!.Sections.Single(section => section.EmbeddedEditor != null).EmbeddedEditor!;
        check(potionInput.Selections.Count == 3 && potionInput.Selections[1].Value == "retained-empty" &&
            potionInput.Selections[2].Options.Single(candidate => candidate.Id == poolOnlyBottle.Serialized)
            .Label == text.Format("predictor.map.event_potions.saved_hypothesis", names.Resolve(poolOnlyBottle, GameContentKind.Potion)) &&
            potionInput.Sections.Any(section => section.Text == text.Format("predictor.map.event_potions.capacity_mismatch", 3, 2)) &&
            !potionInput.Sections.SelectMany(section => section.Choices).Single(choice => choice.Label == text.Get("predictor.runtime.confirm")).Enabled &&
            session.Plans.Single().Event!.Potions!.OrderedSlots is { Length: 3 } retainedSlots && retainedSlots[1] == null,
            "an overcapacity saved potion hypothesis remains visible with its original identities, empty slots and local mismatch until explicitly edited");
        session = new();
        SetEvent(conveyor, [], potionRequirement: PredictorEventPotionRequirement.EmptySlots, inventory: bottleInventory, potionUnresolved: true);
        presenter = new(session, names, text);
        potionInput = presenter.Detail((0, point))!.Sections.Single(section => section.EmbeddedEditor != null).EmbeddedEditor!;
        var capacityField = potionInput.Numbers.Single(number => number.Label == text.Get("predictor.map.event_potions.empty_count"));
        check(capacityField.Maximum == 2 &&
            !presenter.ApplyField(observed.Position.NodeId, capacityField.Id, new(Number: 3)) && session.Plans.IsEmpty,
            "empty-slot hypotheses reject values above known incoming capacity without authoring a plan");

        session = new();
        var stoneLift = new PredictorEventOptionKnowledge("LIFT",
            new("LIFT", LocalPotionOrdinal: 1, LocalPotionKey: firstBottle), true, [])
        { ResultPreview = new(PredictorEventNeededInput.None, [], [], [], [], []) };
        SetEvent(new("EVENT", "STONE_OF_ALL_TIME"), [stoneLift]);
        presenter = new(session, names, text);
        check(FullDetail(presenter).Sections.Single().Objects.Any(item => item.Key == firstBottle && item.Kind == GameContentKind.Potion),
            "Stone exposes the actual randomly selected source bottle from the local hypothesis beside its option");

        session = new(); SetOptions(option);
        presenter = new(session, names, text);
        var requiredBranch = presenter.Detail((0, point))!;
        check(!requiredBranch.Sections.Any(section => section.Text == text.Get("predictor.map.event_branch_required")) &&
            requiredBranch.Sections.Single(section => section.BranchRow).Choices.Count == 1 &&
            session.Nodes[(0, point)].Event!.BusinessChoiceRequired && session.Plans.IsEmpty,
            "an unselected required event retains its actual branch entrance and requirement without a redundant instruction paragraph");
        var sapphire = new ModelKey("EVENT", "SAPPHIRE_SEED");
        session = new();
        SetEvent(sapphire, [plant], PredictorEventNeededInput.Cards, applied: 1, candidates: targets, advanced: true);
        check(session.SetPlan(0, point, new(observed.Position.NodeId) { Event = new(sapphire)
            { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = plant.Value }] } }),
            "the optional-target fixture adopts its existing displayed Sapphire parent before checking applied feedback");
        presenter = new(session, names, text);
        var compactOptional = presenter.Detail((0, point))!;
        check(!compactOptional.Sections.Any(section => section.Text == text.Get("predictor.map.event_branch_required")) &&
            !compactOptional.Sections.SelectMany(section => section.Choices).Any(choice => choice.Object?.Kind == GameContentKind.Card) &&
            compactOptional.Sections.Single(section => section.BranchRow).Choices.Single().Preview != null &&
            FullDetail(presenter).Sections.SelectMany(section => section.Choices).Any(choice => choice.Object?.Kind == GameContentKind.Card),
            "an advanced optional target keeps hover/adopt entry and detailed target editing without forcing a normal-surface picker");
        SetEvent(sapphire, [plant], PredictorEventNeededInput.Cards, applied: 1, candidates: targets);
        presenter = new(session, names, text);
        check(presenter.Detail((0, point))!.Sections.SelectMany(section => section.Choices)
            .Any(choice => choice.Object?.Kind == GameContentKind.Card),
            "a real unresolved target still exposes its input when the event cannot advance");
        session = new(); SetEvent(new("EVENT", "TABLET_OF_TRUTH"), [], PredictorEventNeededInput.Health, advanced: true);
        presenter = new(session, names, text);
        check(!presenter.Detail((0, point))!.Sections.Any(section => section.EmbeddedEditor != null) &&
            FullDetail(presenter).Sections.Any(section => section.EmbeddedEditor?.Numbers.Count == 2),
            "the advanced optional Tablet leaves numerical HP adjustments in the detailed editor");
        session = new(); SetEvent(conveyor, [], PredictorEventNeededInput.Health);
        presenter = new(session, names, text);
        check(presenter.Detail((0, point))!.Sections.Any(section => section.EmbeddedEditor?.Numbers.Count == 2),
            "Conveyor's real weighted-pool HP requirement remains on the ordinary surface");

        // Representative render fixture: the concrete Brain handler offers
        // five cards for SHARE_KNOWLEDGE and a three-card reward for RIP.
        // RIP's ordinary reward defaults to Skip but remains a true picker;
        // automatically generated card outcomes above remain display-only.
        session = new();
        var ripReward = observed.Rewards.First(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card).Value! with
        { Cards = cards.Take(3).ToImmutableArray(), Origin = PredictorRewardOrigin.Event, Status = PredictorRewardStatus.Skipped };
        var rip = new PredictorEventOptionKnowledge("RIP", new("RIP", HealthDamage: 5), true, [])
        { ResultPreview = new(PredictorEventNeededInput.None, [], [], [], [new(PredictorRewardKind.Card, ripReward, [])
            { DefaultSkipped = true, Occurrence = 0 }], []) };
        SetEvent(brain, [share, rip]);
        presenter = new(session, names, text);
        long previewRevision = session.Revision;
        var layered = presenter.Detail((0, point))!;
        check(!layered.Sections.Any(section => section.Text == text.Get("predictor.map.event_branch_required")) &&
            session.Nodes[(0, point)].Event!.BusinessChoiceRequired &&
            layered.Sections.Single(section => section.BranchRow).Choices.Count == 2,
            "required Brain branch retains both actual choices and its model requirement without a redundant instruction paragraph");
        var offeredPreview = layered.Sections.Single(section => section.BranchRow).Choices[0].Preview;
        check(layered.Sections.Single(section => section.BranchRow).Choices.Count == 2 &&
            !layered.Sections.SelectMany(section => section.Choices).Any(choice => choice.Object?.Kind == GameContentKind.Card) &&
            offeredPreview?.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups)
                .Any(group => group.Cards.SequenceEqual(cards)) == true && session.Revision == previewRevision && session.Plans.IsEmpty,
            "ordinary node offers read-only per-option previews from that route node without unadopted target grids or authored plan writes");
        var renderFixture = FullDetail(presenter);
        check(renderFixture.Sections.Count(section => section.HeaderChoice != null) == 2 &&
            renderFixture.Sections.Sum(section => section.Choices.Count(choice => choice.Object?.Kind == GameContentKind.Card)) == 8 &&
            renderFixture.Sections.Sum(section => section.Objects.Count(item => item.Kind == GameContentKind.Card)) == 3 &&
            !renderFixture.Sections.SelectMany(section => section.Choices).Any(choice => choice.Selected),
            "Brain render fixture contains two option groups with five selectable event offers and three selectable ordinary reward cards");
        var ripCard = renderFixture.Sections.Single(section => section.RewardActionsRow).Choices.Where(choice => choice.Object?.Kind == GameContentKind.Card).ElementAt(1);
        check(presenter.ApplyChoice(observed.Position.NodeId, ripCard.Id) && session.Plans.Single().Event is { } directRip &&
            directRip.Actions.Length == 2 && directRip.Actions[0].ExpectedOption?.Key == "RIP" &&
            directRip.Actions[1].Action == PredictorEventActionKind.Reward && directRip.Actions[1].RewardChoice?.CardIndex == 1,
            "clicking RIP's default-skipped ordinary reward directly authors its option plus event-owned reward choice atomically");
        SetEvent(brain, [share, rip], PredictorEventNeededInput.None, applied: 2);
        var compactRenderFixture = presenter = new(session, names, text);
        var stableRip = compactRenderFixture.Detail((0, point))!;
        check(stableRip.Sections.Single(section => section.BranchRow).Choices.Count(choice => choice.Selected) == 1 &&
            stableRip.Sections.Single(section => section.BranchRow).Choices.Single(choice => choice.Selected).Label ==
                text.Get("predictor.runtime.option.RIP") &&
            stableRip.Sections.Count(section => section.RewardActionsRow) == 1 &&
            stableRip.Sections.Single(section => section.RewardActionsRow).Objects.Count == 3 &&
            !stableRip.Sections.Any(section => section.HeaderChoice != null),
            "adopted branch remains in one horizontal row above only its own stable reward opportunity");
        var oldBranch = stableRip.Sections.Single(section => section.BranchRow).Choices[1];
        var switchBranch = stableRip.Sections.Single(section => section.BranchRow).Choices[0];
        check(presenter.ApplyChoice(stableRip.NodeId, switchBranch.Id) && !presenter.ApplyChoice(stableRip.NodeId, oldBranch.Id) &&
            session.Plans.Single().Event!.Actions.Single().ExpectedOption == share.Value,
            "switching an isolated branch replaces its authored plan and invalidates old action IDs");
        SetEvent(brain, [share, rip], PredictorEventNeededInput.Cards, applied: 1, offered: cards);
        presenter = new(session, names, text);
        check(presenter.Detail((0, point))!.Sections.All(section => !section.RewardActionsRow) &&
            presenter.Detail((0, point))!.Sections.Single(section => section.BranchRow).Choices[0].Selected,
            "recomputed branch replaces the previous reward group with its actual target controls");

        // Output-contract fixture: the core owns these distinct opportunities;
        // the presenter must never merge them because their kind matches.
        session = new();
        var philosopherKey = new ModelKey("EVENT", "COLORFUL_PHILOSOPHERS");
        var philosopherRewards = Enumerable.Range(0, 3).Select(index =>
            new PredictorCombatRewardKnowledge(PredictorRewardKind.Card, ripReward with { Id = ripReward.Id + index,
                Cards = cards.Skip(index).Take(3).ToImmutableArray() }, [])
            { DefaultSkipped = true, Occurrence = index }).ToImmutableArray();
        var philosopher = new PredictorEventOptionKnowledge("IRONCLAD", new("IRONCLAD"), true, [])
        { ResultPreview = new(PredictorEventNeededInput.None, [], [], [], philosopherRewards, []) };
        SetEvent(philosopherKey, [philosopher], PredictorEventNeededInput.None, applied: 1);
        check(session.SetPlan(0, point, new(observed.Position.NodeId) { Event = new(philosopherKey)
            { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = philosopher.Value }] } }),
            "the isolated multi-opportunity fixture binds an existing displayed node before adopting a branch");
        presenter = new(session, names, text);
        var grouped = presenter.Detail((0, point))!.Sections.Where(section => section.RewardActionsRow).ToArray();
        check(grouped.Length == 3 && grouped.All(section => section.Objects.Count == 3 && section.StableId != null) &&
            grouped.Select(section => section.StableId).Distinct().Count() == 3 &&
            grouped.SelectMany(section => section.Objects).All(item => item.CardSnapshot != null),
            "the output-contract fixture keeps three distinct opportunity rows and full native card snapshots");

        var fight = new PredictorEventOptionKnowledge("FIGHT", new("FIGHT"), true, [])
        { ResultPreview = new(PredictorEventNeededInput.Combat, [], [], [], [], []) };
        session = new();
        SetEvent(brain, [fight], PredictorEventNeededInput.Combat, applied: 1);
        check(session.SetPlan(0, point, new(observed.Position.NodeId) { Event = new(brain)
            { Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = fight.Value }] } }),
            "the isolated combat contract fixture adopts its existing node before rendering results");
        var eventLocation = (0, point);
        var eventObserved = session.Nodes[eventLocation];
        var originalCombat = observed.CombatRequirements!;
        var routeCombat = new PredictorCombatPreview(observed.Position, philosopherRewards, [], [], new(0, 0, []), originalCombat);
        typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Nodes), BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, session.Nodes.SetItem(eventLocation, eventObserved with { Event = eventObserved.Event! with { Combat = routeCombat } }));
        presenter = new(session, names, text);
        check(presenter.Detail(eventLocation)!.Sections.Count(section => section.CombatPanel && section.RewardActionsRow) == 3 &&
            presenter.Detail(eventLocation)!.Sections.Where(section => section.CombatPanel).All(section => section.Choices.Count == 0),
            "event combat extends the adopted branch with route-owned reward rows without inventing current-room inputs");
        session.SetPlan(0, point, new(observed.Position.NodeId) { Event = new(brain)
        { Actions = [new(PredictorEventActionKind.Health) { Health = new(20, 60) },
            new(PredictorEventActionKind.Option) { ExpectedOption = share.Value }] } });
        SetEvent(brain, [share, rip], authoredStale: true);
        presenter = new(session, names, text);
        var replacement = presenter.Detail((0, point))!.Sections.Single(section => section.BranchRow).Choices.ToArray();
        check(replacement.All(choice => !choice.Selected) && presenter.ApplyChoice(observed.Position.NodeId, replacement[1].Id) &&
            session.Plans.Single().Event is { Potions: null, Actions.Length: 1 } replaced && replaced.Actions[0].ExpectedOption == rip.Value,
            "clicking a same-name event with a stale occurrence starts that instance's new choice without carrying old HP input");
        session = new();
        SetEvent(future, [localBottleOption], potionRequirement: PredictorEventPotionRequirement.OrderedSlots, inventory: bottleInventory);
        session.SetPlan(0, point, new(observed.Position.NodeId) { Event = new(future)
        { Potions = new() { OrderedSlots = [secondBottle, firstBottle] },
            Actions = [new(PredictorEventActionKind.Option) { ExpectedOption = localBottleOption.Value }] } });
        var actualBottle = bottleInventory.PotionSlots.First(slot => slot.Value != null).Value!;
        var actualBottleOption = new PredictorEventOptionKnowledge("POTION:" + actualBottle.Id,
            new("POTION:" + actualBottle.Id, CardType: EffectCardType.Attack, PotionId: actualBottle.Id), true, [])
        { ResultPreview = new(PredictorEventNeededInput.None, [], [], [], [], []) };
        SetEvent(future, [actualBottleOption], potionRequirement: PredictorEventPotionRequirement.OrderedSlots,
            inventory: bottleInventory, authoredStale: true);
        presenter = new(session, names, text);
        var replacementBottle = FullDetail(presenter).Sections.Single(section => section.HeaderChoice != null).HeaderChoice!;
        check(!replacementBottle.Selected && presenter.ApplyChoice(observed.Position.NodeId, replacementBottle.Id) &&
            session.Plans.Single().Event is { Potions: null, Actions.Length: 1 } freshPotionEvent &&
            freshPotionEvent.Actions[0].ExpectedOption == actualBottleOption.Value,
            "a newly clicked choice at a stale same-name Future event does not carry the old legal ordered-bottle hypothesis");
        // The isolated session has intentionally moved through later cases;
        // this saved payload is only a frozen rendering fixture, not live input.
        return stableRip with { IsCurrent = null, Sections = stableRip.Sections.Select(section => section with
            { Objects = section.Objects.ToImmutableArray(), Choices = section.Choices.ToImmutableArray() }).ToImmutableArray(), EditorActions = [] };
    }
}
