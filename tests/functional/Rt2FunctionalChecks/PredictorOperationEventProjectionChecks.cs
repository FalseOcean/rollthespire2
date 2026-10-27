using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class PredictorOperationEventChecks
{
    private static void CheckEventProjection(PredictorContext context, PredictorState state, PredictorStreamState rng)
    {
        PredictorOperationSnapshot Snapshot(string entry, params PredictorEventOption[] options) =>
            new("event-projection:" + entry, context, state,
                new PredictorOperationEventScene(new("EVENT", entry), rng, options.ToImmutableArray(), new(60, 80)), 60, 80);
        static IEnumerable<OperationPreviewRow> Rows(OperationPreview preview) =>
            preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows);
        static IEnumerable<OperationPreviewCardGroup> Groups(OperationPreview preview) =>
            preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups);
        static PredictorCombatFacts NoExtraActions(PredictorState current) => new(AutomaticCardEffects: false)
        {
            CardTriggers = current.Deck.Select(current.Card).Where(PredictorCombatConsequences.IsTriggeredSource)
                .Select(c => new PredictorCardTrigger(c.Id, 0)).ToImmutableArray(),
            Potions = PredictorCombatPotionFacts.NoAdditional
        };
        static void CheckBattleRewards(OperationPreview preview, PredictorOperationResult expected)
        {
            var sections = preview.Branches.SelectMany(b => b.Sections).Where(s => s.Label == "EventCombatRewards").ToArray();
            var cards = sections.SelectMany(s => s.CardGroups).SelectMany(g => g.Cards).Select(c => c with { Id = 0 });
            var wantedCards = expected.Rewards.Where(r => r.Kind == PredictorRewardKind.Card)
                .SelectMany(r => r.Cards).Select(c => c with { Id = 0 });
            var models = sections.SelectMany(s => s.Rows).SelectMany(r => r.Models).OrderBy(k => k.Serialized);
            var wantedModels = expected.Rewards.Where(r => r.Kind is PredictorRewardKind.Potion or PredictorRewardKind.Relic)
                .Select(r => r.Key).OrderBy(k => k.Serialized);
            Require(preview.Faults.IsEmpty && expected.Disposition == PredictorInputResult.Accepted &&
                cards.SequenceEqual(wantedCards) && models.SequenceEqual(wantedModels),
                "Stable-context battle forecast differs from explicit zero-action detached reward settlement");
        }
        void CheckTransformSourceRanks(PredictorOperationSnapshot snapshot, string option, bool requireCounterexample)
        {
            var root = ImmutableArray.Create<PredictorInput>(new ChooseEventOption(option));
            var picker = PredictorOperation.Preview(snapshot, root);
            var candidates = picker.State.Deck.Where(picker.Waiting.Selection.CandidateCardIds.Contains).ToImmutableArray();
            var observed = new[] { new List<(long Source, PredictorCard Card)>(), new List<(long Source, PredictorCard Card)>() };
            foreach (long first in candidates)
                foreach (long second in candidates.Where(id => id != first))
                {
                    var ids = ImmutableArray.Create(first, second);
                    var result = PredictorOperation.Preview(snapshot, root.Add(new SelectEventCards(ids)));
                    Require(result.Disposition == PredictorInputResult.Accepted, "source-ranked explicit event target input rejected");
                    var changes = result.Effects.Skip(picker.Effects.Length).SelectMany(effect => effect.Transformations).ToArray();
                    for (int slot = 0; slot < ids.Length; slot++)
                        observed[slot].Add((ids[slot], result.State.Card(changes.Single(change => change.OriginalId == ids[slot]).ReplacementId) with { Id = 0 }));
                }
            var wanted = observed.Select(slot => slot.OrderBy(item => picker.State.Deck.IndexOf(item.Source))
                .Select(item => item.Card).Distinct().ToArray()).ToArray();
            if (requireCounterexample) Require(!observed[1].Select(item => item.Card).Distinct().SequenceEqual(wanted[1]),
                "event source-order fixture did not expose tuple traversal's incorrect second-slot order");
            var preview = EventOperationPreview.Build(snapshot, option);
            var actual = Groups(preview).ToArray();
            Require(preview.Faults.IsEmpty && actual.Length == 2 && actual.Select((group, slot) =>
                group.Cards.Select(card => card with { Id = 0 }).SequenceEqual(wanted[slot])).All(value => value),
                "event overview reordered a later slot by tuple visit instead of its own original's deck rank");
        }

        var cheese = Snapshot("ROOM_FULL_OF_CHEESE", new PredictorEventOption("GORGE"), new PredictorEventOption("SEARCH"));
        var cheeseResult = PredictorOperation.Preview(cheese, new ChooseEventOption("GORGE"));
        Require(cheeseResult.Waiting.Cards.Length == 8 && cheeseResult.Waiting.Cards.All(c => !state.Deck.Contains(c.Id)) &&
            cheeseResult.Waiting.Selection.RequiredCardCount == 2,
            "Event generated candidates were not exported separately from existing deck targets");
        var cheesePreview = EventOperationPreview.Build(cheese, "GORGE");
        Require(cheesePreview.Faults.IsEmpty && Groups(cheesePreview).Single().Cards.Length == 8 &&
            Groups(cheesePreview).Single().MinimumSelect == 2 &&
            cheesePreview.Style?.Cards == OperationCardLayout.Spread, "Cheese eight-card prototype lost selection relationship");
        var brain = EventOperationPreview.Build(Snapshot("BRAIN_LEECH", new PredictorEventOption("SHARE_KNOWLEDGE")), "SHARE_KNOWLEDGE");
        Require(Groups(brain).Single().Cards.Length == 5 && Groups(brain).Single().MaximumSelect == 1,
            "Brain Leech generated five-card selection not projected");

        var tinker = Snapshot("TINKER_TIME", new PredictorEventOption("CHOOSE_CARD_TYPE"));
        var tinkerPreview = EventOperationPreview.Build(tinker, "CHOOSE_CARD_TYPE");
        var science = Rows(tinkerPreview).Where(r => r.Action == OperationPreviewAction.AddCard)
            .SelectMany(r => r.Cards).Where(c => c.Key.Entry == "MAD_SCIENCE").ToArray();
        Require(tinkerPreview.Faults.IsEmpty && tinkerPreview.Branches.Length == 4 && science.Length == 4 &&
            science.All(c => c.TinkerTimeRider != PredictorMadScienceRider.None) &&
            science.GroupBy(c => c.TinkerTimeType).Count() == 2,
            "Tinker did not preserve two types with two rider results each");
        var tinkerTypes = PredictorOperation.Preview(tinker, new ChooseEventOption("CHOOSE_CARD_TYPE"));
        var tinkerTypeScene = (PredictorOperationEventScene)tinker.Scene;
        var typeSnapshot = tinker with { Scene = tinkerTypeScene with { Options = tinkerTypes.Waiting.EventOptions } };
        var typePreview = EventOperationPreview.Build(typeSnapshot, tinkerTypes.Waiting.EventOptions[0].Key);
        Require(typePreview.Faults.IsEmpty && typePreview.Branches.Length == 2 && Rows(typePreview)
            .Count(r => r.Action == OperationPreviewAction.AddCard) == 2,
            "Tinker type-stage two hidden rider outcomes were suppressed");
        var riderSnapshot = tinker with { Scene = tinkerTypeScene with
        { Options = [new("SAPPING"), new("VIOLENCE")], TinkerType = RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack } };
        Require(EventOperationPreview.Build(riderSnapshot, "SAPPING").Branches.IsEmpty,
            "Tinker native final card preview was duplicated");

        var targets = state.Deck.Where(id => state.Card(id).Removable).Take(2).ToImmutableArray();
        var morphic = Snapshot("MORPHIC_GROVE") with { Scene = new PredictorOperationEventScene(
            new("EVENT", "MORPHIC_GROVE"), rng, [], new(60, 80))
            { Branch = "GROUP", AwaitingCards = true, CardCandidates = targets, CardsRequired = 2 } };
        var selector = PredictorOperation.Preview(morphic, ImmutableArray<PredictorInput>.Empty);
        Require(selector.Waiting.Cards.IsEmpty && selector.Waiting.Selection.CandidateCardIds.SequenceEqual(targets),
            "Existing deck target picker was mislabeled as generated card results");
        var transformed = EventOperationPreview.BuildSelected(morphic, targets);
        Require(transformed.Faults.IsEmpty && Groups(transformed).Count(g => g.Cards.Length == 1) == 2,
            "Selected Morphic per-slot transform results missing");
        var reverseTargets = targets.Reverse().ToImmutableArray();
        var reverseResult = PredictorOperation.Preview(morphic, new SelectEventCards(reverseTargets));
        var reverseChanges = reverseResult.Effects.SelectMany(e => e.Transformations).ToArray();
        var reversePreview = EventOperationPreview.BuildSelected(morphic, reverseTargets);
        Require(Groups(reversePreview).Select(g => g.Cards.Single().Id).SequenceEqual(reverseTargets.Select(id =>
            reverseChanges.Single(change => change.OriginalId == id).ReplacementId)),
            "Event transformation slots followed deck reinsert order instead of actual selection order");
        var morphicOverview = EventOperationPreview.Build(Snapshot("MORPHIC_GROVE", new PredictorEventOption("GROUP")), "GROUP");
        var overviewSlots = Groups(morphicOverview).ToArray();
        Require(morphicOverview.Faults.IsEmpty && morphicOverview.Branches.Length == 1 && overviewSlots.Length == 2 &&
            overviewSlots.Select(g => g.Label).SequenceEqual(["1", "2"]) && overviewSlots.All(g => g.Possible &&
                g.Cards.Select(c => c with { Id = 0 }).Distinct().Count() == g.Cards.Length) &&
            morphicOverview.Notices.Contains("EventTransformSlotAlternativesRelated"),
            "Morphic overview must group distinct full visible card states by transform slot, not target-order branches");
        // Different native transformation pools expose the second-slot
        // counterexample: tuple traversal visits source b before source a.
        var rankedState = state;
        var starterPool = PredictorCardChanges.Definition(context, state.Card(state.Deck[0]).Key).TransformationPool;
        var rankDefinitions = context.Catalog!.CardDefinitions.Where(definition => definition.Prototype.Removable && !definition.IsQuest &&
            definition.TransformationPool != starterPool && context.Catalog.TransformationPools.Any(pool =>
                pool.Key == definition.TransformationPool && pool.Cards.Count(key => key != definition.Prototype.Key) > 0))
            .DistinctBy(definition => definition.TransformationPool).Take(2).ToArray();
        Require(rankDefinitions.Length == 2, "event source-order fixture requires two additional native transform pools");
        foreach (var definition in rankDefinitions)
            rankedState = PredictorObtainSources.AddCard(context, rankedState, definition.Prototype);
        rankedState = rankedState with { Deck = rankedState.Deck.Reverse().ToImmutableArray() };
        CheckTransformSourceRanks(Snapshot("MORPHIC_GROVE", new PredictorEventOption("GROUP")) with { State = rankedState }, "GROUP", true);
        var cappedState = state;
        while (cappedState.Deck.Count(id => cappedState.Card(id).Removable) < 18)
            cappedState = PredictorObtainSources.AddCard(context, cappedState, state.Card(targets[0]));
        var cappedOverview = EventOperationPreview.Build(Snapshot("MORPHIC_GROVE", new PredictorEventOption("GROUP")) with
            { State = cappedState }, "GROUP");
        Require(cappedOverview.Faults.IsEmpty && cappedOverview.PendingReasons.Contains("EventTargetAlternativesLimit:256") &&
            cappedOverview.Branches.All(branch => !branch.FullyEnumerated && branch.PendingReasons.IsEmpty),
            "Capped overview must keep its actual enumeration limit without inventing a missing player input");

        var tablet = Snapshot("TABLET_OF_TRUTH", new PredictorEventOption("DECIPHER"), new PredictorEventOption("SMASH"));
        var tabletPreview = EventOperationPreview.Build(tablet, "DECIPHER");
        var tabletCards = Rows(tabletPreview).Where(r => r.Action == OperationPreviewAction.UpgradeCard)
            .SelectMany(r => r.Cards).ToArray();
        var inputs = ImmutableArray<PredictorInput>.Empty;
        var expected = new List<long>();
        int seenEffects = 0;
        for (int i = 0; i < 4; i++)
        {
            inputs = inputs.Add(new ChooseEventOption("DECIPHER"));
            var result = PredictorOperation.Preview(tablet, inputs);
            expected.AddRange(result.Effects.Skip(seenEffects).Where(e => e.Action == "TabletUpgrade").Select(e => e.ObjectId));
            seenEffects = result.Effects.Length;
            if (!result.Waiting.EventOptions.Any(o => o.Key == "DECIPHER")) break;
        }
        Require(tabletPreview.Faults.IsEmpty && tabletCards.Select(c => c.Id).SequenceEqual(expected) &&
            tabletCards.All(c => c.UpgradeLevel > 0), "Tablet random upgrades were omitted, reordered or mixed with the public fifth upgrade");
        var fifthTablet = tablet with { Scene = ((PredictorOperationEventScene)tablet.Scene) with { TabletCount = 4 } };
        Require(EventOperationPreview.Build(fifthTablet, "DECIPHER").Branches.IsEmpty &&
            PredictorOperation.Preview(fifthTablet, new ChooseEventOption("DECIPHER")).Effects.Any(e => e.Action == "TabletUpgrade"),
            "Tablet fifth-step display suppression changed numerical settlement");

        var trialEntry = EventOperationPreview.Build(Snapshot("TRIAL", new PredictorEventOption("ACCEPT")), "ACCEPT");
        Require(trialEntry.Branches.IsEmpty && trialEntry.PendingReasons.IsEmpty,
            "Trial entrance exposed future case or retained the obsolete display deferral");
        var trialRewards = Snapshot("TRIAL", new PredictorEventOption("NONDESCRIPT_GUILTY"));
        var trialRewardResult = PredictorOperation.Preview(trialRewards, new ChooseEventOption("NONDESCRIPT_GUILTY"));
        var trialRewardPreview = EventOperationPreview.Build(trialRewards, "NONDESCRIPT_GUILTY");
        Require(trialRewardPreview.Faults.IsEmpty && Groups(trialRewardPreview).Count() == 2 &&
            Groups(trialRewardPreview).SelectMany(g => g.Cards).SequenceEqual(trialRewardResult.Rewards.SelectMany(r => r.Cards)),
            "Trial verdict did not preserve the two ordered card reward groups");
        var trialTransforms = Snapshot("TRIAL", new PredictorEventOption("NONDESCRIPT_INNOCENT"));
        var trialPicker = PredictorOperation.Preview(trialTransforms, new ChooseEventOption("NONDESCRIPT_INNOCENT"));
        var doubt = trialPicker.State.Deck.Single(id => !state.Deck.Contains(id) && trialPicker.State.Card(id).Key.Entry == "DOUBT");
        Require(trialPicker.Waiting.Selection.CandidateCardIds.Contains(doubt),
            "Trial excluded the newly added Doubt from legal transform targets");
        var trialTransformPreview = EventOperationPreview.Build(trialTransforms, "NONDESCRIPT_INNOCENT");
        Require(trialTransformPreview.Faults.IsEmpty && Groups(trialTransformPreview).Count() == 2,
            "Trial transform verdict did not expose two ordered transform slots");
        CheckTransformSourceRanks(trialTransforms with { State = rankedState }, "NONDESCRIPT_INNOCENT", false);
        var trialPickerScene = new PredictorOperationEventScene(new("EVENT", "TRIAL"), rng, [], new(60, 80))
        { Branch = "NONDESCRIPT_INNOCENT", AwaitingCards = true, CardsRequired = 2,
            CardCandidates = trialPicker.Waiting.Selection.CandidateCardIds };
        var selectedDoubt = EventOperationPreview.BuildSelected(trialTransforms with
            { State = trialPicker.State, Scene = trialPickerScene }, [doubt, targets[0]]);
        Require(selectedDoubt.Faults.IsEmpty && Groups(selectedDoubt).Count(g => g.Cards.Length == 1) == 2,
            "Trial newly added curse could not participate in an exact selected-target preview");
        var trialRelics = Snapshot("TRIAL", new PredictorEventOption("MERCHANT_GUILTY"));
        var trialRelicResult = PredictorOperation.Preview(trialRelics, new ChooseEventOption("MERCHANT_GUILTY"));
        var trialRelicPreview = EventOperationPreview.Build(trialRelics, "MERCHANT_GUILTY");
        Require(trialRelicPreview.Faults.IsEmpty && Rows(trialRelicPreview).Where(r => r.Action == OperationPreviewAction.ObtainRelic)
            .SelectMany(r => r.Models).SequenceEqual(trialRelicResult.State.Relics.Where(r => !state.Relics.Any(old => old.Id == r.Id)).Select(r => r.Key)),
            "Trial merchant verdict lost or reordered its hidden relics");

        var bridge = Snapshot("SLIPPERY_BRIDGE", new PredictorEventOption("OVERCOME", CardTargetId: targets[0]), new PredictorEventOption("HOLD_ON")) with
        { Scene = new PredictorOperationEventScene(new("EVENT", "SLIPPERY_BRIDGE"), rng,
            [new("OVERCOME", CardTargetId: targets[0]), new("HOLD_ON")], new(60, 80)) { BridgeCard = targets[0] } };
        var publicBridge = EventOperationPreview.Build(bridge, "OVERCOME");
        Require(publicBridge.Branches.IsEmpty && publicBridge.Notices.IsEmpty && publicBridge.Faults.IsEmpty,
            "Bridge public current removal should not create prediction content");
        var bridgePreview = EventOperationPreview.Build(bridge, "HOLD_ON");
        var proposals = Rows(bridgePreview).Where(r => r.Action == OperationPreviewAction.RemoveCard).SelectMany(r => r.Cards).ToArray();
        var afterHold = PredictorOperation.Preview(bridge, new ChooseEventOption("HOLD_ON"));
        Require(bridgePreview.Faults.IsEmpty && proposals.Length > 1 &&
            proposals[0].Id == afterHold.Waiting.EventOptions.Single(o => o.Key == "OVERCOME").CardTargetId &&
            bridgePreview.Notices.Contains("BridgeAlternativeRemovalProposals"),
            "Bridge successive alternatives were replaced by simulated repeated removals");
        var longBridge = bridge with { State = state with { Deck = targets }, CurrentHp = 1000, MaximumHp = 1000,
            Scene = ((PredictorOperationEventScene)bridge.Scene) with { Health = new(1000, 1000) } };
        var longBridgePreview = EventOperationPreview.Build(longBridge, "HOLD_ON");
        var longProposals = Rows(longBridgePreview).Where(r => r.Action == OperationPreviewAction.RemoveCard)
            .SelectMany(r => r.Cards).ToArray();
        Require(longBridgePreview.Faults.IsEmpty && longProposals.Length > 15 &&
            longProposals.Select(c => c.Id).Distinct().Count() < longProposals.Length &&
            longBridgePreview.Branches.Single().Sections.Select(s => s.Label)
                .SequenceEqual(Enumerable.Range(1, longProposals.Length).Select(n => n.ToString())),
            "Bridge producer truncated, deduplicated or reordered its sequence before the display limit");
        var longInputs = ImmutableArray<PredictorInput>.Empty;
        var longExpected = new List<long>();
        while (true)
        {
            longInputs = longInputs.Add(new ChooseEventOption("HOLD_ON"));
            var next = PredictorOperation.Preview(longBridge, longInputs);
            if (next.Waiting.EventOptions.FirstOrDefault(o => o.Key == "OVERCOME")?.CardTargetId is not { } id) break;
            longExpected.Add(id);
            if (!next.Waiting.EventOptions.Any(o => o.Key == "HOLD_ON" && o.Enabled)) break;
        }
        Require(longProposals.Select(card => card.Id).SequenceEqual(longExpected),
            "Bridge full future proposal sequence differs from explicit repeated HOLD_ON settlements");

        var mirrorState = state;
        foreach (long id in targets)
            mirrorState = mirrorState.WithCard(PredictorCardLevelEffects.Reconcile(context,
                mirrorState.Card(id) with { UpgradeLevel = 1 }));
        var mirror = EventOperationPreview.Build(Snapshot("REFLECTIONS", new PredictorEventOption("TOUCH_A_MIRROR")) with
            { State = mirrorState }, "TOUCH_A_MIRROR");
        Require(Rows(mirror).Where(r => r.Action == OperationPreviewAction.DowngradeCard)
            .SelectMany(r => r.Cards).Count(c => c.UpgradeLevel == 0) == 2 &&
            Rows(mirror).Any(r => r.Action == OperationPreviewAction.UpgradeCard),
            "Reflections lost the downgrade stage behind later final upgrade values");

        var restState = state with { Relics = [new(state.NextInstanceId, new("RELIC", "TINY_MAILBOX"), 1),
            new(state.NextInstanceId + 1, new("RELIC", "DREAM_CATCHER"), 1),
            new(state.NextInstanceId + 2, new("RELIC", "WHITE_BEAST_STATUE"), 1)], NextInstanceId = state.NextInstanceId + 3 };
        var denseSnapshot = Snapshot("DENSE_VEGETATION", new PredictorEventOption("REST")) with { State = restState };
        var dense = EventOperationPreview.Build(denseSnapshot, "REST");
        var restRows = dense.Branches.SelectMany(b => b.Sections).Where(s => s.Label == "EventRestRewards").SelectMany(s => s.Rows);
        Require(dense.Faults.IsEmpty && restRows.Where(r => r.Action == OperationPreviewAction.GeneratePotion)
            .SelectMany(r => r.Models).Count() == 2 && Groups(dense).Any(g => g.Cards.Length == 3),
            "Dense rest reward hooks were omitted or confused with combat rewards");
        var denseExpected = PredictorOperation.Preview(denseSnapshot,
            [new ChooseEventOption("REST"), new LeaveRewards(), new ChooseEventOption("FIGHT"), new ResolveCombat(NoExtraActions(restState))]);
        Require(denseExpected.Rewards.Any(r => r.Kind == PredictorRewardKind.Card) &&
            denseExpected.Rewards.Any(r => r.Kind == PredictorRewardKind.Potion), "Dense expected stable relic rewards fixture empty");
        CheckBattleRewards(dense, denseExpected);
        var punchSnapshot = Snapshot("PUNCH_OFF", new PredictorEventOption("I_CAN_TAKE_THEM")) with
        { Scene = new PredictorOperationEventScene(new("EVENT", "PUNCH_OFF"), rng,
            [new("I_CAN_TAKE_THEM")], new(60, 80)) { LayoutEncounter = new("ENCOUNTER", "PUNCH_OFF_EVENT_ENCOUNTER") } };
        var punch = EventOperationPreview.Build(punchSnapshot, "I_CAN_TAKE_THEM");
        var punchExpected = PredictorOperation.Preview(punchSnapshot, [new ChooseEventOption("I_CAN_TAKE_THEM"),
            new ChooseEventOption("FIGHT"), new ResolveCombat(NoExtraActions(state))]);
        Require(punchExpected.Rewards.Any(r => r.Kind == PredictorRewardKind.Card) &&
            punchExpected.Rewards.Any(r => r.Kind == PredictorRewardKind.Potion) &&
            punchExpected.Rewards.Any(r => r.Kind == PredictorRewardKind.Relic), "Punch expected card/potion/relic fixture empty");
        CheckBattleRewards(punch, punchExpected);
        Require(dense.Notices.Contains("EventCombatStableContextNoExtraActions") &&
            punch.Notices.Contains("EventCombatStableContextNoExtraActions"), "Combat forecast premise missing");

        var huntState = PredictorObtainSources.AddCard(context, state,
            new RolltheSpire2.Core.Identity.ModelKey("CARD", "THE_HUNT"));
        var huntSnapshot = punchSnapshot with { State = huntState };
        var noHunt = EventOperationPreview.Build(huntSnapshot, "I_CAN_TAKE_THEM");
        Require(noHunt.Faults.IsEmpty && noHunt.PendingReasons.IsEmpty,
            "Operation Hunt forecast must resolve its actual event child without using the forbidden route view: " +
            string.Join(";", noHunt.Faults.Select(fault => fault.Stage + ":" + fault.Exception)));
        var huntExpected = PredictorOperation.Preview(huntSnapshot, [new ChooseEventOption("I_CAN_TAKE_THEM"),
            new ChooseEventOption("FIGHT"), new ResolveCombat(NoExtraActions(huntState))]);
        Require(huntExpected.Effects.Single(e => e.Action == "CombatBoundary").CombatCardTriggers
            .Any(t => t.InstanceId == huntState.Deck[^1] && t.EffectiveTriggers == 0),
            "No-action premise fixture did not explicitly suppress relevant card trigger");
        CheckBattleRewards(noHunt, huntExpected);
        var huntFacts = NoExtraActions(huntState) with { CardTriggers = NoExtraActions(huntState).CardTriggers
            .Select(t => t.InstanceId == huntState.Deck[^1] ? t with { EffectiveTriggers = 1 } : t).ToImmutableArray() };
        var withHunt = PredictorOperation.Preview(huntSnapshot, [new ChooseEventOption("I_CAN_TAKE_THEM"),
            new ChooseEventOption("FIGHT"), new ResolveCombat(huntFacts)]);
        var firstHunt = withHunt.Rewards.Where(r => r.Kind == PredictorRewardKind.Card &&
            r.RerollRecipe is { EncounterSource: true, IsFromCombat: false }).OrderBy(r => r.SubscriptionOrder).First();
        var conditionalHunt = Groups(noHunt).Single(g => g.Label == "EventHuntConditional:1");
        Require(conditionalHunt.Possible && conditionalHunt.Cards.Select(c => c with { Id = 0 })
                .SequenceEqual(firstHunt.Cards.Select(c => c with { Id = 0 })),
            "Hunt conditional group differs from the native-ordered full combat settlement");

        var doll = Snapshot("DOLL_ROOM", new PredictorEventOption("TAKE_SOME_TIME"));
        var hiddenDolls = EventOperationPreview.Build(doll, "TAKE_SOME_TIME");
        Require(hiddenDolls.Faults.IsEmpty && Rows(hiddenDolls).Count(row => row.Action == OperationPreviewAction.Choose) == 2,
            "Doll hidden candidates lost their Choose action");
        var dollOffers = PredictorOperation.Preview(doll, new ChooseEventOption("TAKE_SOME_TIME"));
        var revealedDoll = doll with { State = dollOffers.State, CurrentHp = dollOffers.CurrentHp,
            Scene = ((PredictorOperationEventScene)doll.Scene) with { Options = dollOffers.Waiting.EventOptions,
                Health = new(dollOffers.CurrentHp, dollOffers.MaximumHp) } };
        Require(dollOffers.Waiting.EventOptions.All(o => EventOperationPreview.Build(revealedDoll, o.Key).Branches.IsEmpty),
            "Doll actual offered options must not repeat their public relic prediction");

        var lampState = state with { Relics = [new(state.NextInstanceId, new("RELIC", "LAVA_LAMP"), 1)],
            NextInstanceId = state.NextInstanceId + 1 };
        var lampSnapshot = punchSnapshot with { State = lampState };
        var lampPreview = EventOperationPreview.Build(lampSnapshot, "I_CAN_TAKE_THEM");
        Require(lampPreview.Faults.IsEmpty && lampPreview.Branches.Length == 2 && lampPreview.Branches
            .SelectMany(b => b.Sections).Where(s => s.Label == "EventCombatRewards").SelectMany(s => s.CardGroups)
            .All(g => g.Possible), "Unknown Lava Lamp damage was not retained as possible reward alternatives");
        foreach (bool damage in new[] { false, true })
        {
            var explicitLamp = PredictorOperation.Preview(lampSnapshot, [new ChooseEventOption("I_CAN_TAKE_THEM"),
                new ChooseEventOption("FIGHT"), new ResolveCombat(NoExtraActions(lampState) with { TookDamageThisCombat = damage })]);
            var branch = lampPreview.Branches.Single(b => b.Label == (damage ? "EventCombatDamageTaken" : "EventCombatNoDamage"));
            CheckBattleRewards(lampPreview with { Branches = [branch] }, explicitLamp);
        }

        var independentState = state with { Relics = [new(state.NextInstanceId, new("RELIC", "MEAT_ON_THE_BONE"), 1),
            new(state.NextInstanceId + 1, new("RELIC", "SEAL_OF_GOLD"), 1)], NextInstanceId = state.NextInstanceId + 2 };
        var independentSnapshot = punchSnapshot with { State = independentState,
            Context = context with { SealOfGoldFixedDeduction = null } };
        var independent = EventOperationPreview.Build(independentSnapshot, "I_CAN_TAKE_THEM");
        Require(independent.Faults.IsEmpty && independent.Branches.All(b => b.PendingReasons.IsEmpty) &&
            !Groups(independent).Any(g => g.Cards.IsEmpty),
            "Unknown healing or gold deduction incorrectly blocked independent reward identities");
        // Both sides of the only conditional healing threshold and materially
        // different budget deductions must leave every displayed identity equal.
        foreach (int hp in new[] { 1, 80 })
        foreach (int deduction in new[] { 0, 137 })
        {
            var explicitIndependent = PredictorOperation.Preview(independentSnapshot with
                { Context = context with { SealOfGoldFixedDeduction = deduction } },
                [new ChooseEventOption("I_CAN_TAKE_THEM"), new ChooseEventOption("FIGHT"),
                    new ResolveCombat(NoExtraActions(independentState) with { VictoryHealth = new(hp, 80) })]);
            CheckBattleRewards(independent, explicitIndependent);
        }

        foreach (string setting in new[] { "SETTING_1", "SETTING_2", "SETTING_3" })
        {
            var dummy = Snapshot("BATTLEWORN_DUMMY", new PredictorEventOption(setting));
            var predicted = EventOperationPreview.Build(dummy, setting);
            var expectedDummy = PredictorOperation.Preview(dummy,
                [new ChooseEventOption(setting), new ResolveCombat(NoExtraActions(state), false)]);
            CheckBattleRewards(predicted, expectedDummy);
            if (setting == "SETTING_2") Require(Rows(predicted).Where(r => r.Action == OperationPreviewAction.UpgradeCard)
                .SelectMany(r => r.Cards).Select(c => c.Id).SequenceEqual(expectedDummy.Effects
                    .Where(e => e.Action == "BattlewornDummyUpgrade").SelectMany(e => e.Targets)),
                "Dummy defeated branch upgrade targets missing");
        }
        Console.WriteLine("OPERATION_EVENT_PROJECTION grids + Tinker children + Tablet sequence + Bridge alternatives + stable-context Dense/Punch/Dummy exact rewards PASS");
    }
}
