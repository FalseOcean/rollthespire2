using System.Collections.Immutable;
using System.Reflection;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorRoomResultRecordingChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static PredictorCombatPendingCause Cause(string code, PredictorCombatDomain domains) =>
        PredictorRouteCauseEvidence.CreateExplicit(code, "source:" + code, domains, PredictorPendingReasonKind.MissingObservation,
            "Fixture observation unavailable: " + code, ["fixture." + code]);
    private static PredictorCard Card(string key, int level = 0) => new(12, new("CARD", key), EffectCardType.Attack, level, 1, true, 1);
    private static PredictorRouteNodeKnowledge Node(string id, PredictorRoomKind room) =>
        new(new(1, 2, id), room, null, PredictorPhase.Complete, [], [], [], [], []);

    internal static void Run()
    {
        // Invoke the actual private room recorder over DTOs without adding a
        // production testing API or introducing a game/native object.
        var context = new PredictorContext("SKDPGF6RC1PC", "0.111.0", new("CHARACTER", "IRONCLAD"), 0,
            "room-result-fixture", "unlocks", [], [], [], 1) { Catalog = new([], []) };
        var state = PredictorSeedState.Create(context, [Card("STRIKE_IRONCLAD")], [], 99, 2, new(-.05f, 0));
        var ownerType = typeof(PredictorRun).GetNestedType("RoutePartialState", BindingFlags.NonPublic)!;
        var dependencyType = typeof(PredictorRun).GetNestedType("RouteDependency", BindingFlags.NonPublic)!;
        var dependencies = Activator.CreateInstance(typeof(List<>).MakeGenericType(dependencyType))!;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var owner = Activator.CreateInstance(ownerType, members, null,
            [context, state, dependencies, new PredictorRouteFactRecorder()], null)!;
        var record = ownerType.GetMethod("RecordRoomResults", members)!;
        var facts = (PredictorRouteFactRecorder)ownerType.GetField("Facts", members)!.GetValue(owner)!;
        void Record(PredictorRouteNodeKnowledge node)
        {
            try { record.Invoke(owner, [node]); }
            catch (TargetInvocationException exception) { throw exception.InnerException!; }
        }
        var identity = Cause("OfferIdentity", PredictorCombatDomain.ShopsRng);
        var gold = Cause("GoldBudget", PredictorCombatDomain.Gold);
        var prepared = Cause("PreparedCard", PredictorCombatDomain.RewardsRng);
        var target = Cause("StarterTarget", PredictorCombatDomain.Deck);
        var treasureRng = Cause("TreasureCursor", PredictorCombatDomain.TreasureRng);
        var unsafeCard = Card("UNSAFE_WITNESS_CARD");
        var shop = new PredictorShopNodeKnowledge([
            new(0, PredictorShopEntryKind.CharacterCard, new(0, 1, PredictorShopEntryKind.CharacterCard, unsafeCard.Key, unsafeCard, 987654, 987654, false, true),
                [identity]) { Affordable = true, AffordabilityPendingCauses = [identity] },
            new(1, PredictorShopEntryKind.Relic, new(1, 1, PredictorShopEntryKind.Relic, new("RELIC", "WHETSTONE"), null, 150, 150, false, true),
                []) { Affordable = null, AffordabilityPendingCauses = [gold] }
        ], [identity, gold]);
        Record(Node("shop", PredictorRoomKind.Shop) with { Shop = shop });
        var ancient = new PredictorAncientNodeKnowledge(new("EVENT", "NEOW"), [
            new(0, new(0, new("RELIC", "DUSTY_TOME"), true, null, 987654, 987654, unsafeCard.Key, null), [prepared, target])
            { PreparedCardPendingCauses = [prepared], TargetPendingCauses = [target] },
            new(1, null, [])
        ], [new(PredictorPhase.ObtainChooseCard, new("RELIC", "DUSTY_TOME"), [], [unsafeCard], [], [], [prepared])], [], []);
        Record(Node("ancient", PredictorRoomKind.Ancient) with { Ancient = ancient });
        var branch = new PredictorEventOptionResultKnowledge(PredictorEventNeededInput.Cards, [], [],
            [new(PredictorPhase.ChooseBundle, new("RELIC", "SCROLL_BOXES"), [], [], [[unsafeCard]], [], [prepared])], [], [target])
        {
            GeneratedCards = [new(unsafeCard, [target])],
            ObtainedRelics = [new(99, new("RELIC", "UNSAFE_WITNESS_RELIC"), 2)], RelicPendingCauses = [target],
            GoldBalance = 987654, PendingCauses = [target, gold],
            PendingHealthCommands = [new(-5, 0)]
        };
        var eventNode = new PredictorEventNodeKnowledge(new("EVENT", "BRAIN_LEECH"), [
            new("MASKED", new("MASKED", GoldGain: 987654, CardTargetId: 987654), true, [identity])
            { IdentityPendingCauses = [identity], AvailabilityPendingCauses = [identity] },
            new("SAFE", new("SAFE", GoldCost: 12), null, [gold])
            { AvailabilityPendingCauses = [gold], ResultPreview = branch }
        ], [new(Card("SAFE_CANDIDATE"), []), new(unsafeCard, [target])], [unsafeCard], [], [], [])
        {
            OfferedCardPendingCauses = [prepared], GoldBalance = 987654, GoldPendingCauses = [gold]
        };
        Record(Node("event", PredictorRoomKind.Event) with { Event = eventNode });
        Record(Node("treasure", PredictorRoomKind.Treasure) with
        {
            Treasure = new(new("RELIC", "UNSAFE_WITNESS_RELIC"), [treasureRng], [], [], [])
            {
                GenerationAllowed = true, NormalGold = 987654, NormalGoldPendingCauses = [gold], SpoilsGold = 0
            }
        });
        var restCause = PredictorRouteCauseEvidence.Create("PendingCardChoice", "previous", PredictorCombatDomain.Deck);
        Record(Node("rest", PredictorRoomKind.RestSite) with
        {
            Cards = [new(Card("STRIKE_IRONCLAD", 1), [])], DeckPendingCauses = [restCause],
            RestOffers = [new(PredictorRestAction.Heal, true), new(PredictorRestAction.Smith, false)],
            RestOfferPendingCauses = [restCause]
        });
        var frames = facts.FreezeFrames();
        PredictorKnowledgeFact Fact(string node, string key) => frames[node].Snapshot.Fact(frames[node].Results[key]);
        void Unknown(string node, string key)
        { Check(Fact(node, key).Status == PredictorKnowledgeStatus.Unknown && Fact(node, key).DisplayValue == null, "Masked room payload leaked: " + key); }
        Unknown("shop", "result.shop.offer.0.identity"); Unknown("shop", "result.shop.offer.0.price");
        Check(Fact("shop", "result.shop.offer.1.identity").Status == PredictorKnowledgeStatus.Known &&
            Fact("shop", "result.shop.offer.1.price").DisplayValue == "150", "Unknown affordability hid an independent shop item or price.");
        Unknown("shop", "result.shop.offer.1.affordable");
        Check(Fact("ancient", "result.ancient.offer.0.identity").DisplayValue == "RELIC:DUSTY_TOME", "Prepared/target masks hid a known Ancient identity.");
        Unknown("ancient", "result.ancient.offer.0.preparedCard"); Unknown("ancient", "result.ancient.offer.0.starterCardTarget");
        Unknown("ancient", "result.ancient.child.0.offeredCard.offers");
        var missing = Fact("ancient", "result.ancient.offer.1.identity");
        Check(frames["ancient"].Snapshot.Explain(missing.Id).Roots.All(root =>
            root.Kind == PredictorKnowledgeRootKind.ImplementationGap && root.RequiredInputs.IsEmpty),
            "An absent value without source evidence invented a player input.");
        Unknown("event", "result.event.option.0.goldGain"); Unknown("event", "result.event.option.0.enabled");
        Check(Fact("event", "result.event.option.1.goldCost").DisplayValue == "12", "Unknown availability hid known option metadata.");
        Unknown("event", "result.event.option.1.enabled"); Unknown("event", "result.event.goldBalance");
        Unknown("event", "result.event.deckCandidate.1.identity");
        Check(Fact("event", "result.event.deckCandidate.0.identity").Status == PredictorKnowledgeStatus.Known,
            "An unrelated candidate's mask hid the guaranteed candidate subset.");
        Unknown("event", "result.event.option.1.branch.generatedCard.0.identity");
        Unknown("event", "result.event.option.1.branch.obtainedRelics");
        Unknown("event", "result.event.option.1.branch.child.0.bundles");
        Check(Fact("event", "result.event.option.1.branch.pendingHealthCommand.0").DisplayValue!.Contains("nominal", StringComparison.Ordinal),
            "A nominal health command was represented as a resulting HP fact.");
        Check(Fact("treasure", "result.treasure.generationAllowed").DisplayValue == "True",
            "Treasure RNG uncertainty hid independently known chest eligibility.");
        Unknown("treasure", "result.treasure.relic"); Unknown("treasure", "result.treasure.normalGold");
        Check(Fact("treasure", "result.treasure.spoilsGold").DisplayValue == "0", "A known zero Spoils payout became missing.");
        Unknown("rest", "result.rest.offer.1.enabled"); Unknown("rest", "result.rest.hatchMembership");
        Check(Fact("rest", "result.rest.offer.0.action").DisplayValue == "Heal", "Unknown Hatch membership hid a retained Rest action.");
        Check(facts.Snapshot().Facts.All(fact => fact.DisplayValue == null ||
            !fact.DisplayValue.Contains("UNSAFE_WITNESS", StringComparison.Ordinal) && !fact.DisplayValue.Contains("987654", StringComparison.Ordinal)),
            "A masked representative payload entered a room result snapshot.");
        Console.WriteLine("PREDICTOR_ROOM_RESULT_RECORDING_PASS independent shop/option observations; Ancient partial setup; candidate subsets; treasure eligibility; Rest qualification; masked payload safety; explicit missing evidence");
    }
}
