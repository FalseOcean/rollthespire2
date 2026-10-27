using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorStreamKnowledgeChecks
{
    private static readonly PredictorCard Strike = Card("STRIKE_IRONCLAD", EffectCardType.Attack);
    private static readonly PredictorCard Defend = Card("DEFEND_IRONCLAD", EffectCardType.Skill);
    private static readonly PredictorCard Common = Card("CONTROLLED_COMMON", EffectCardType.Skill);
    private static readonly PredictorCard CommonB = Card("CONTROLLED_COMMON_B", EffectCardType.Attack);
    private static readonly PredictorCard CommonC = Card("CONTROLLED_COMMON_C", EffectCardType.Skill);
    private static readonly PredictorCard Lantern = Card("LANTERN_KEY", EffectCardType.Skill);
    private static readonly ModelKey Encounter = new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK");
    private static readonly ModelKey Leafy = new("RELIC", "LEAFY_POULTICE");

    internal static void Run()
    {
        var domains = Enum.GetValues<PredictorStream>().Select(PredictorRun.StreamKnowledgeDomain).ToArray();
        Check(domains.Length == 8 && domains.All(d => d != PredictorCombatDomain.None) && domains.Distinct().Count() == 8,
            "Each real stream must have its own nonempty uncertainty domain");
        try { _ = PredictorRun.StreamKnowledgeDomain((PredictorStream)99); throw new InvalidOperationException("Invalid stream mapping was accepted"); }
        catch (ArgumentOutOfRangeException) { }
        UnknownRoomReadsAndTerminal();
        IndependentStreamsStayIsolated();
        LeafyUsesItsActualStream();
        Console.WriteLine("PREDICTOR_STREAM_KNOWLEDGE_PASS eight stream domains; real unknown-room terminal/forced/tutorial paths; independent stream diagnostics; Leafy actual Transformations reads");
    }

    private static PredictorCard Card(string key, EffectCardType type) => new(0, new("CARD", key), type, 0, 1, true, 1);
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static PredictorCombatPendingCause Missing(string name, PredictorCombatDomain domain) =>
        PredictorRouteCauseEvidence.CreateExplicit(name, "capture:" + name, domain, PredictorPendingReasonKind.MissingObservation,
            "The named captured property has not been observed.", ["fixture." + name]);

    private static PredictorRun Fixture(ImmutableArray<PredictorCombatPendingCause> pending, bool opening = false,
        bool basics = false, bool compass = false, bool lantern = false, int priorUnknowns = 0, bool firstRun = false)
    {
        int act = lantern ? 3 : 1;
        // Both actual Brain Leech options are observed: SHARE_KNOWLEDGE draws
        // five unique character cards; RIP's health-independent continuation
        // offers three unique colorless cards. Each rolled rarity needs enough
        // distinct definitions, not just enough entries in the other pool.
        var rewardCards = new[] { (Card: Common, Rarity: EffectCardRarity.Common),
            (Card: CommonB, Rarity: EffectCardRarity.Common), (Card: CommonC, Rarity: EffectCardRarity.Common) }
            .Concat(Enumerable.Range(0, 2).Select(i =>
                (Card: Card("CONTROLLED_COMMON_EXTRA_" + i, EffectCardType.Skill), Rarity: EffectCardRarity.Common)))
            .Concat(new[] { EffectCardRarity.Uncommon, EffectCardRarity.Rare }.SelectMany(rarity =>
                Enumerable.Range(0, 5).Select(i => (Card: Card("CONTROLLED_" + rarity + "_" + i, EffectCardType.Attack), Rarity: rarity))))
            .ToArray();
        var colorlessCards = new[] { EffectCardRarity.Uncommon, EffectCardRarity.Rare }.SelectMany(rarity =>
            Enumerable.Range(0, 3).Select(i =>
                (Card: Card("CONTROLLED_COLORLESS_" + rarity + "_" + i, EffectCardType.Skill), Rarity: rarity))).ToArray();
        var rewardRarities = rewardCards.Concat(colorlessCards).ToDictionary(c => c.Card.Key, c => c.Rarity.ToString());
        var context = new PredictorContext("SKDPGF6RC1PC", "0.111.0", new("CHARACTER", "IRONCLAD"), 0,
            "stream-knowledge-fixture", "unlocks", [], [], [], firstRun ? 0 : 1)
        {
            Catalog = new(rewardCards.Select((c, index) => new NeowEffectCardSnapshot(
                "stream-pool:" + index, c.Card.Key, index, c.Rarity, c.Card.Type, false, false, false, true, true)).ToImmutableArray(),
                colorlessCards.Select((c, index) => new NeowEffectCardSnapshot(
                    "stream-colorless:" + index, c.Card.Key, index, c.Rarity, c.Card.Type, false, false, false, true, true,
                    PoolId: "colorless")).ToImmutableArray())
            {
                // The third tutorial ? is the first real combat and uses the
                // source's manually assigned first reward, outside either pool.
                CardDefinitions = new[] { Strike, Defend, Lantern,
                    Card("SETUP_STRIKE", EffectCardType.Attack), Card("TREMBLE", EffectCardType.Skill),
                    Card("BLOOD_WALL", EffectCardType.Skill) }.Concat(rewardCards.Concat(colorlessCards).Select(c => c.Card)).Select(c => new PredictorCardDefinition(c,
                    c.Key == Strike.Key || c.Key == Defend.Key ? "Basic" : rewardRarities.GetValueOrDefault(c.Key, "Common"), new("CARD_POOL", "CONTROLLED"),
                    false, c.Key == Strike.Key, c.Key == Defend.Key)
                { Levels = Enumerable.Range(0, 2).Select(level => new PredictorCardLevelMetadata(level, 1,
                    false, false, false, false, false, false)).ToImmutableArray() }).ToImmutableArray(),
                TransformationPools = [new(new("CARD_POOL", "CONTROLLED"), [Common.Key])],
                Encounters = [new(Encounter, 10, 10) { IsMonsterEncounter = true }]
            },
            // The Act-3 Lantern case really opens WAR_HISTORIAN_REPY. Its
            // chest option previews two potion plans, independently of the
            // two Brain Leech branches and the still uncertain room cursor.
            PotionPool = [new(new("POTION", "ENERGY_POTION"), PredictorPotionRarity.Common),
                new(new("POTION", "BLOCK_POTION"), PredictorPotionRarity.Uncommon),
                new(new("POTION", "FRUIT_JUICE"), PredictorPotionRarity.Rare)],
            Live = new(act, true, pending)
            { PreviousVisits = Enumerable.Repeat(new PredictorVisitFact(MapPointType.Unknown, PredictorRoomKind.Event, []), priorUnknowns).ToImmutableArray() }
        };
        var types = new[] { MapPointType.Ancient, opening ? MapPointType.RestSite : MapPointType.Unknown, MapPointType.Boss };
        var points = types.Select((type, row) => new MapPredictionPoint(new(3, row), type, true,
            row == types.Length - 1 ? [] : new[] { new MapPosition(3, row + 1) })).ToArray();
        var actKey = new ModelKey("ACT", "OVERGROWTH");
        var map = new MapPrediction(context.Seed, actKey, 0, 7, points, 0);
        var deck = basics ? new[] { Strike, Defend } : lantern ? new[] { Lantern } : Array.Empty<PredictorCard>();
        var state = PredictorSeedState.Create(context, deck,
            compass ? [new ModelKey("RELIC", "GOLDEN_COMPASS")] : [], 99, 2, new(-.05f, 0f)) with
        {
            StartedWithNeow = opening, OpeningCompleted = !opening,
            OpeningOptions = opening ? [Leafy, new("RELIC", "OLD_COIN"), new("RELIC", "POTION_BELT")] : [],
            Position = new(act, opening ? 0 : 1, "stream-fixture:entry"),
            Maps = [new(act, map, opening ? [] : [points[0].Position])],
            Visits = opening ? [] : [new(act, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
            Acts = [new(act, actKey, new("EVENT", "NEOW"), [Encounter], [Encounter], [Encounter], [new("EVENT", "BRAIN_LEECH")])],
            UnknownOdds = new(0, -1, 0, 0)
        };
        if (compass) state = state with { Relics = state.Relics.Select(r => r with { GoldenPathAct = act - 1 }).ToImmutableArray() };
        var run = new PredictorRun(context, state);
        run.SetRoute(act, points.Select(p => p.Position).ToImmutableArray());
        return run;
    }

    private static PredictorRouteContinuation Preview(PredictorRun run, ImmutableArray<PredictorPlannedNode> plans = default)
    {
        var state = JsonSerializer.Serialize(run.Working);
        var checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var result = run.PreviewRouteContinuation(plans.IsDefault ? [] : plans, 1,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults);
        Check(JsonSerializer.Serialize(run.Working) == state && JsonSerializer.Serialize(run.Checkpoints) == checkpoints,
            "Stream knowledge preview mutated its caller");
        return result;
    }

    private static PredictorKnowledgeFact After(PredictorRouteNodeKnowledge node, string key) =>
        node.Diagnostics!.After.Select(node.Diagnostics.Snapshot.Fact).Single(f => f.PropertyKey == key);

    private static void UnknownRoomReadsAndTerminal()
    {
        var cursor = Missing("unknown-room-cursor", PredictorCombatDomain.UnknownMapPointRng);
        var odds = Missing("unknown-room-odds", PredictorCombatDomain.Map);
        var blocked = Preview(Fixture([cursor, odds]));
        Check(blocked.Nodes.IsEmpty && blocked.TerminalBoundary is { } terminal &&
            terminal.PendingCauses.Any(c => c.Code == "UnknownRoomRandomReadsPending" && c.ReasonKind == PredictorPendingReasonKind.DerivedDependency) &&
            PredictorRouteCauseEvidence.Roots(terminal.PendingCauses).Any(c => c.Identity == cursor.Identity) &&
            PredictorRouteCauseEvidence.Roots(terminal.PendingCauses).Any(c => c.Identity == odds.Identity) && terminal.Diagnostics!.After.IsEmpty,
            "An uncertain random room must publish a terminal boundary, original cursor/odds roots and no witness room or after-state");
        var relic = Missing("unknown-room-relic", PredictorCombatDomain.Relics);
        var coarse = Preview(Fixture([relic], compass: true));
        Check(coarse.Nodes.IsEmpty && coarse.TerminalBoundary!.PendingCauses.Any(c =>
                c.Code == "UnknownRoomRelicReadsPending" && c.ReasonKind == PredictorPendingReasonKind.DerivedDependency &&
                c.RequiredPropertyKeys.Contains("relics.GOLDEN_COMPASS.goldenPathAct")) &&
            PredictorRouteCauseEvidence.Roots(coarse.TerminalBoundary.PendingCauses).Any(c => c.Identity == relic.Identity),
            "A witness Golden Compass cannot settle a coarse unresolved relic read");
        foreach (int visited in new[] { 0, 1, 2 })
        {
            var tutorial = Preview(Fixture([cursor, odds, relic], priorUnknowns: visited, firstRun: true));
            Check(tutorial.Nodes.Length == 1 && tutorial.TerminalBoundary == null &&
                tutorial.Nodes[0].Room == (visited < 2 ? PredictorRoomKind.Event : PredictorRoomKind.Monster),
                "First-run fixed room outputs must ignore cursor, odds and relic selection dependencies");
            var recorded = tutorial.Nodes[0].Diagnostics!.Results.Select(tutorial.Knowledge!.Fact).ToArray();
            Check(recorded.Single(f => f.PropertyKey == "map.resolve.rng.UnknownMapPoint.consumed").DisplayValue == "0" &&
                !recorded.Any(f => f.PropertyKey == "map.resolve.read.rng.UnknownMapPoint.calls"),
                "Tutorial resolution must record zero consumption without inventing an RNG read.");
        }
        Check(Preview(Fixture([cursor], priorUnknowns: 3, firstRun: true)).Nodes.IsEmpty,
            "The tutorial exemption must end after the third unknown point");
        foreach (var forced in new[] { Fixture([cursor, odds], compass: true), Fixture([cursor, odds, relic], lantern: true) })
        {
            var result = Preview(forced);
            Check(result.Nodes.Length == 1 && result.Nodes[0].Room == PredictorRoomKind.Event && result.TerminalBoundary == null,
                "A proven forced Event must not be blocked by its unused UnknownMapPoint cursor or unknown-room odds");
            var recorded = result.Nodes[0].Diagnostics!.Results.Select(result.Knowledge!.Fact).ToArray();
            var resolved = recorded.Single(f => f.PropertyKey == "map.resolve.room");
            var consumed = recorded.Single(f => f.PropertyKey == "map.resolve.rng.UnknownMapPoint.consumed");
            var after = recorded.Single(f => f.PropertyKey == "map.resolve.rng.UnknownMapPoint.callsAfter");
            Check(resolved.Status == PredictorKnowledgeStatus.Known && consumed.Status == PredictorKnowledgeStatus.Known &&
                consumed.DisplayValue == "1" && after.Status == PredictorKnowledgeStatus.Unknown &&
                result.Knowledge!.Explain(after.Id).Roots.Any(root => root.NodeId == cursor.NodeId) &&
                recorded.Single(f => f.PropertyKey == "result.room.kind").AllReadFactIds.Contains(resolved.Id),
                "A forced Event proves its room and one draw, but not the final unknown cursor; publication must reference that calculation.");
        }
    }

    private static void IndependentStreamsStayIsolated()
    {
        foreach (var stream in new[] { PredictorStream.UpFront, PredictorStream.Transformations, PredictorStream.Niche })
        {
            var missing = Missing("cursor-" + stream, PredictorRun.StreamKnowledgeDomain(stream));
            var result = Preview(Fixture([missing]));
            Check(result.Nodes.Length == 1 && result.Nodes[0].Room == PredictorRoomKind.Event,
                "An unrelated RNG uncertainty must not block normal unknown-room resolution");
            var options = result.Nodes[0].Event!.EntryOptions;
            Check(options.Single(o => o.Value?.Key == "SHARE_KNOWLEDGE").ResultPreview!.OfferedCards.Length == 5 &&
                options.Single(o => o.Value?.Key == "RIP").ResultPreview!.Rewards.Any(r =>
                    r.Kind == PredictorRewardKind.Card && r.IsKnown && r.Value!.Cards.Length == 3),
                "Both real Brain Leech option previews must produce their complete character grid and colorless reward");
            var unknown = After(result.Nodes[0], "rng." + stream + ".state");
            var known = After(result.Nodes[0], "rng.UnknownMapPoint.state");
            Check(unknown.Status == PredictorKnowledgeStatus.Unknown && unknown.DisplayValue == null &&
                known.Status == PredictorKnowledgeStatus.Known && result.Knowledge!.Explain(unknown.Id).Roots.Any(r => r.NodeId == missing.NodeId),
                "A named uncertain stream must stay masked and retain its original root without contaminating UnknownMapPoint");
            Check(After(result.Nodes[0], "rng." + stream + ".capturedBaseCalls").Status == PredictorKnowledgeStatus.Known,
                "An uncertain continuation must preserve its separately captured exact base cursor");
        }
    }

    private static void LeafyUsesItsActualStream()
    {
        ImmutableArray<PredictorPlannedNode> plans =
        [new("map:1:0:3:1") { Ancient = new(new("EVENT", "NEOW"), new(0, Leafy, true, null, null, null, null, null))
            { Children = [new(PredictorObtainChildAction.Health, Leafy) { Health = new(50, 60) }] } }];
        var transformations = Missing("leafy-transformations", PredictorCombatDomain.TransformationsRng);
        var blocked = Preview(Fixture([transformations], opening: true, basics: true), plans);
        Check(blocked.Nodes.Single().Ancient is { Advanced: false } ancient &&
            ancient.RequiredChoices.Any(c => c.Phase == PredictorPhase.ObtainHealth &&
                c.PendingCauses.Any(p => p.Identity == transformations.Identity)),
            "Leafy must open its health phase without an RNG entry read, then block actual transforms on Transformations uncertainty");
        var blockedFacts = blocked.Nodes.Single().Diagnostics!.Snapshot;
        var disposition = blockedFacts.Facts.Single(f => f.PropertyKey == "ancient.child.1.projectionDisposition");
        var selection = blockedFacts.Facts.Single(f => f.PropertyKey == "ancient.child.1.resolvedSelection");
        Check(disposition.Status == PredictorKnowledgeStatus.Known && disposition.DisplayValue == "Blocked" &&
            !string.IsNullOrWhiteSpace(disposition.Derivation) && disposition.AllReadFactIds.Any(id => blockedFacts.Fact(id).Status == PredictorKnowledgeStatus.Unknown) &&
            selection.Status == PredictorKnowledgeStatus.Unknown && selection.DisplayValue == null &&
            blockedFacts.Explain(selection.Id).Roots.Any(root => root.RootKey == transformations.Identity) &&
            !blockedFacts.Facts.Any(f => f.PropertyKey == "ancient.child.1.write.phase"),
            "The forecast can prove its Blocked return while retaining the missing transform input and masking the unapplied selection; no phase write was executed.");
        var active = Fixture([transformations], opening: true, basics: true);
        active.AdvanceRoute();
        Check(active.Submit(active.Request!, new ChooseOpeningRelic(Leafy)) == PredictorInputResult.Accepted &&
            active.Phase == PredictorPhase.ObtainHealth, "The active Leafy child fixture did not open its actual health phase.");
        var activeFacts = Preview(active).Nodes.Single().Diagnostics!.Snapshot;
        Check(activeFacts.Facts.Single(f => f.PropertyKey == "ancient.binding.1.projectionDisposition") is
                { Status: PredictorKnowledgeStatus.Known, DisplayValue: "NotAuthored" } &&
            !activeFacts.Facts.Any(f => f.PropertyKey == "ancient.binding.1.acceptedSelection"),
            "An already accepted Ancient prefix with no saved plan must not invent an unresolved selection or ledger cause.");
        var empty = Preview(Fixture([transformations], opening: true), plans);
        Check(empty.Nodes.Single().Ancient!.Advanced,
            "A proof that both Basic families are absent must remove Leafy's unused transformation stream read");
        var niche = Missing("leafy-independent-niche", PredictorCombatDomain.NicheRng);
        var independent = Preview(Fixture([niche], opening: true, basics: true), plans);
        Check(independent.Nodes.Single().Ancient!.Advanced &&
            After(independent.Nodes[0], "rng.Transformations.calls").Status == PredictorKnowledgeStatus.Known &&
            After(independent.Nodes[0], "rng.Transformations.calls").DisplayValue == "2" &&
            After(independent.Nodes[0], "rng.Niche.state").Status == PredictorKnowledgeStatus.Unknown,
            "Leafy's two actual transforms must consume its own stream while an independent unknown Niche remains masked");
    }
}
