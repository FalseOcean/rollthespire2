using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorCombatTraceChecks
{
    private static readonly ModelKey Encounter = new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK");
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("COMBAT_TRACE " + message); }
    internal static void Run()
    {
        MissingCountsStayUnobserved();
        NodeAndSettingsProvenance();
        RewardKindsDoNotReadEachOthersOdds();
        GuaranteedPotionDoesNotReadDropOdds();
        GenerationIdentityRejectsReplacement();
        Console.WriteLine("PREDICTOR_COMBAT_TRACE_PASS real route; pre-witness source inputs; creation count/cursor writes; settings/plan provenance; bounded reward read edges");
    }

    private static PredictorRun Route(bool healthAndTheft = false, string? missingOdds = null, bool guaranteedPotion = false)
    {
        PredictorCard Card(string key, EffectCardType type) => new(0, new("CARD", key), type, 0, 1, true, 1);
        var pool = new[] { Card("CONTROLLED_A", EffectCardType.Attack), Card("CONTROLLED_B", EffectCardType.Skill), Card("CONTROLLED_C", EffectCardType.Power) };
        var hunt = Card("THE_HUNT", EffectCardType.Attack);
        var royalties = Card("ROYALTIES", EffectCardType.Skill);
        var context = new PredictorContext("SKDPGF6RC1PC", "0.111.0", new("CHARACTER", "IRONCLAD"), 0,
            "combat-trace", "unlocks", [], [], [], 1)
        {
            Catalog = new(pool.Select((card, i) => new NeowEffectCardSnapshot("pool:" + i, card.Key, i,
                EffectCardRarity.Common, card.Type, false, false, false, true, true)).ToImmutableArray(), [])
            {
                CardDefinitions = pool.Concat([hunt, royalties]).Select(card => new PredictorCardDefinition(card,
                    "Common", new("CARD_POOL", "CONTROLLED"), false, false, false)
                { Levels = Enumerable.Range(0, 2).Select(level => new PredictorCardLevelMetadata(level, 1,
                    false, false, false, false, false, false)).ToImmutableArray() }).ToImmutableArray(),
                Encounters = [new(Encounter, 10, 10) { IsMonsterEncounter = true, CanStealCards = healthAndTheft,
                    EnemyCreationSources = [PredictorEnemyCreationSource.FabricatorBots, PredictorEnemyCreationSource.FogmogIllusions] }]
            },
            PotionPool = [new(new("POTION", "ENERGY_POTION"), PredictorPotionRarity.Common),
                new(new("POTION", "BLOCK_POTION"), PredictorPotionRarity.Uncommon), new(new("POTION", "FRUIT_JUICE"), PredictorPotionRarity.Rare)]
        };
        if (missingOdds != null) context = context with { Live = new(1, true,
            [PredictorRouteCauseEvidence.CreateExplicit("CapturedOdds", "capture:odds", PredictorCombatDomain.Odds,
                PredictorPendingReasonKind.MissingObservation, "Captured reward odds were not observed.",
                [missingOdds])]) };
        var points = new[]
        {
            new MapPredictionPoint(new(3, 0), MapPointType.Ancient, true, [new(3, 1)]),
            new MapPredictionPoint(new(3, 1), MapPointType.Monster, true, [new(3, 2)]),
            new MapPredictionPoint(new(3, 2), MapPointType.Boss, true, [])
        };
        var act = new ModelKey("ACT", "OVERGROWTH");
        var map = new MapPrediction(context.Seed, act, 0, 7, points, 0);
        var state = PredictorSeedState.Create(context, [hunt, royalties],
            healthAndTheft ? [new("RELIC", "MEAT_ON_THE_BONE")] :
                guaranteedPotion ? [new("RELIC", "WHITE_BEAST_STATUE")] : [], 99, 2, new(-.05f, 0f)) with
        {
            OpeningCompleted = true, Position = new(1, 1, "combat-trace:entry"),
            Maps = [new(1, map, [points[0].Position])],
            Visits = [new(1, points[0].Position, MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
            Acts = [new(1, act, new("EVENT", "NEOW"), [Encounter], [Encounter], [Encounter], [])]
        };
        var run = new PredictorRun(context, state); run.SetRoute(1, points.Select(point => point.Position).ToImmutableArray()); return run;
    }
    private static PredictorRouteNodeKnowledge Preview(PredictorRun run, PredictorCombatNodeOverrides? input = null)
    {
        string before = JsonSerializer.Serialize(run.Working);
        var phase = run.Phase; var request = run.Request;
        var node = run.PreviewRouteContinuation(input == null ? [] : [new("map:1:1:3:2") { Combat = input }],
            maximumNodes: 1, policy: PredictorRoutePreviewPolicy.LiveMapDefaults).Nodes.Single();
        Check(before == JsonSerializer.Serialize(run.Working) && phase == run.Phase && request == run.Request,
            "Trace hooks changed the caller's execution state or request.");
        return node;
    }
    private static PredictorKnowledgeFact Fact(PredictorRouteNodeKnowledge node, string key) =>
        node.Diagnostics!.Snapshot.Facts.Single(fact => fact.PropertyKey == key);

    private static void MissingCountsStayUnobserved()
    {
        var node = Preview(Route(healthAndTheft: true));
        var snapshot = node.Diagnostics!.Snapshot;
        var missing = Fact(node, "combat.input.enemyCreation.FabricatorBots.count");
        var approved = Fact(node, "combat.input.enemyCreation.FogmogIllusions.count");
        var consumption = Fact(node, "combat.compute.enemyCreation.Niche.consumed");
        Check(missing.Status == PredictorKnowledgeStatus.NotObserved && missing.DisplayValue == null &&
            approved.Status == PredictorKnowledgeStatus.Known && approved.DisplayValue == "1" && approved.Origin == PredictorKnowledgeOrigin.ApprovedDefault &&
            consumption.Status == PredictorKnowledgeStatus.Unknown && consumption.DisplayValue == null &&
            consumption.UnresolvedReadFactIds.Contains(missing.Id), "Missing creation count leaked scratch zero or lost its exact read dependency.");
        Check(snapshot.Explain(consumption.Id).Roots.All(root => root.NodeId == node.Position.NodeId) &&
            snapshot.Explain(consumption.Id).Roots.Any(root => root.Kind == PredictorKnowledgeRootKind.MissingObservation),
            "Enemy-consumption diagnosis did not preserve the actual source node.");
        foreach (string key in new[] { "stolenCards", "victoryHealth.current", "victoryHealth.maximum" })
            Check(Fact(node, "combat.input." + key).Status == PredictorKnowledgeStatus.NotObserved &&
                Fact(node, "combat.input." + key).DisplayValue == null, "Missing source input leaked a placeholder: " + key);
        Check(Fact(node, "combat.compute.rng.Rewards.consumed").Status == PredictorKnowledgeStatus.Known,
            "A missing creature count or health declaration should not mask an independent Rewards consumption count.");
    }
    private static void NodeAndSettingsProvenance()
    {
        var run = Route();
        var hunt = run.Working.Cards.Single(card => card.Key.Entry == "THE_HUNT");
        var royalties = run.Working.Cards.Single(card => card.Key.Entry == "ROYALTIES");
        run.CombatDefaults = new() { CardRules = [new(royalties.Key, 2)] };
        var node = Preview(run, new()
        {
            EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, 2)],
            CardTriggers = [new(hunt.Id, 2)]
        });
        var snapshot = node.Diagnostics!.Snapshot;
        var declared = Fact(node, "combat.input.enemyCreation.FabricatorBots.count");
        var consumed = Fact(node, "combat.compute.enemyCreation.Niche.consumed");
        Check(declared.Origin == PredictorKnowledgeOrigin.PlayerInput && declared.DisplayValue == "2" &&
            declared.Derivation == "node.Combat.EnemyCreations" && consumed.DisplayValue == "3" &&
            consumed.AllReadFactIds.Contains(declared.Id) && snapshot.Explain(consumed.Id).Roots.IsEmpty,
            "Declared and approved creation counts did not produce the actual sum of draws.");
        Check(Fact(node, "combat.input.card." + hunt.Id + ".triggers").Derivation == "node.Combat.CardTriggers" &&
            Fact(node, "combat.input.card." + royalties.Id + ".triggers").Derivation == "settings.CombatDefaults.CardRules",
            "Node-specific trigger overrides were conflated with saved default settings.");
        var reward = Fact(node, "combat.compute.reward.0");
        Check(reward.AllReadFactIds.Any(id => snapshot.Fact(id).PropertyKey == "combat.input.rng.Rewards.calls") &&
            reward.AllReadFactIds.Any(id => snapshot.Fact(id).PropertyKey == "combat.input.card." + royalties.Id + ".triggers"),
            "Reward population did not retain the input cursor and actual Royalties trigger read versions.");
    }
    private static void RewardKindsDoNotReadEachOthersOdds()
    {
        var node = Preview(Route(missingOdds: "odds.cardRarity"), new()
        { EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, 2)] });
        var snapshot = node.Diagnostics!.Snapshot;
        foreach (int index in Enumerable.Range(0, node.Rewards.Length))
        {
            var reward = node.Rewards[index];
            var computed = Fact(node, "combat.compute.reward." + index);
            var properties = computed.AllReadFactIds.Select(snapshot.Fact).Select(fact => fact.PropertyKey).ToArray();
            if (reward.Kind == PredictorRewardKind.Gold)
                Check(computed.Status == PredictorKnowledgeStatus.Known &&
                    properties.All(key => !key.EndsWith("odds.cardRarity", StringComparison.Ordinal) &&
                        !key.EndsWith("odds.potionDrop", StringComparison.Ordinal)),
                    "Known Gold inherited unrelated reward-odds uncertainty.");
            if (reward.Kind == PredictorRewardKind.Card)
                Check(properties.All(key => !key.EndsWith("odds.potionDrop", StringComparison.Ordinal)),
                    "With a known drop decision, card rarity uncertainty acquired the unrelated potion-drop input.");
        }
        Check(Fact(node, "combat.input.odds.potionDrop").Status == PredictorKnowledgeStatus.Known &&
            Fact(node, "combat.input.odds.cardRarity").Status == PredictorKnowledgeStatus.Unknown &&
            Fact(node, "combat.compute.rng.Rewards.consumed").Status == PredictorKnowledgeStatus.Known,
            "Card rarity uncertainty contaminated the independent drop state or fixed bounded-draw count.");
        Check(Fact(node, "combat.compute.odds.cardRarity").AllReadFactIds.All(id =>
            !snapshot.Fact(id).PropertyKey.EndsWith("odds.potionDrop", StringComparison.Ordinal)) &&
            Fact(node, "combat.compute.odds.potionDrop").AllReadFactIds.All(id =>
            !snapshot.Fact(id).PropertyKey.EndsWith("odds.cardRarity", StringComparison.Ordinal)),
            "An odds endpoint acquired the other distribution's unresolved input.");
        var dropNode = Preview(Route(missingOdds: "odds.potionDrop"), new()
        { EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, 2)] });
        var dropSnapshot = dropNode.Diagnostics!.Snapshot;
        Check(Fact(dropNode, "combat.input.odds.cardRarity").Status == PredictorKnowledgeStatus.Known &&
            Fact(dropNode, "combat.input.odds.potionDrop").Status == PredictorKnowledgeStatus.Unknown,
            "Potion drop uncertainty contaminated the independent incoming card rarity state.");
        Check(dropNode.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Potion && reward.UnknownMultiplicity && reward.Value == null),
            "Unobserved potion probability omitted its existence gap when the zero-chance witness produced no potion.");
        var dropConsumption = Fact(dropNode, "combat.compute.potionDrop.Rewards.consumed");
        Check(dropConsumption.Status == PredictorKnowledgeStatus.Partial && dropConsumption.DisplayValue == "0 … 2" &&
            dropSnapshot.Explain(dropConsumption.Id).Roots.Any(root => root.NodeId == "capture:odds"),
            "Optional potion population lost its bounded two-draw consumption or original capture source.");
        foreach (int index in Enumerable.Range(0, dropNode.Rewards.Length))
        {
            var reward = dropNode.Rewards[index];
            var computed = Fact(dropNode, "combat.compute.reward." + index);
            if (reward.Kind == PredictorRewardKind.Gold)
                Check(reward.IsKnown && computed.Status == PredictorKnowledgeStatus.Known,
                    "Potion existence masked Gold populated before the optional potion.");
            if (reward.Kind == PredictorRewardKind.Card)
                Check(!reward.IsKnown && computed.UnresolvedReadFactIds.Contains(dropConsumption.Id) &&
                    dropSnapshot.Explain(computed.Id).Roots.Any(root => root.NodeId == "capture:odds"),
                    "Card population failed to follow its actual earlier optional-potion cursor dependency.");
        }
        Check(Fact(dropNode, "combat.compute.odds.cardRarity").UnresolvedReadFactIds.Contains(dropConsumption.Id) &&
            Fact(dropNode, "combat.compute.rng.Rewards.consumed").Status == PredictorKnowledgeStatus.Unknown,
            "Unknown potion population was omitted from later card-rarity/cursor writes.");
    }

    private static void GenerationIdentityRejectsReplacement()
    {
        var run = Route();
        var node = Preview(run, new() { EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, 2)] });
        var next = Preview(run, new() { EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, 3)] });
        Check(node.Rewards.All(reward => reward.SourceIdentity != null) &&
            node.Rewards.Select(reward => reward.SourceIdentity).Distinct().Count() == node.Rewards.Length &&
            !node.Rewards.Select(reward => reward.SourceIdentity).Intersect(next.Rewards.Select(reward => reward.SourceIdentity)).Any(),
            "Generated reward identities collided between sources or recomputations.");
        for (int i = 0; i < node.Rewards.Length; i++)
            Check(Fact(node, "result.reward." + i).AllReadFactIds.Contains(Fact(node, "combat.compute.reward." + i).Id),
                "An unchanged generated reward lost the matching computation source.");
        var offer = node.Rewards.First(reward => reward.Value != null).Value! with { Id = 17 };
        Check(PredictorRun.CombatRewardSourceMatches(offer, offer with { Status = PredictorRewardStatus.Taken }) &&
            !PredictorRun.CombatRewardSourceMatches(offer, offer with { Id = 18 }) &&
            !PredictorRun.CombatRewardSourceMatches(offer, offer with { GoldAmount = offer.GoldAmount + 1 }),
            "Read-only reward disposition broke source identity, or a replaced/modified offer reused the old computation.");
    }
    private static void GuaranteedPotionDoesNotReadDropOdds()
    {
        var node = Preview(Route(missingOdds: "odds.potionDrop", guaranteedPotion: true), new()
        { EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, 2)] });
        var snapshot = node.Diagnostics!.Snapshot;
        int index = Enumerable.Range(0, node.Rewards.Length).Single(i => node.Rewards[i].Kind == PredictorRewardKind.Potion);
        var computed = Fact(node, "combat.compute.reward." + index);
        Check(node.Rewards[index].IsKnown && computed.Status == PredictorKnowledgeStatus.Known &&
            computed.AllReadFactIds.All(id => snapshot.Fact(id).PropertyKey != "combat.input.odds.potionDrop"),
            "A guaranteed White Beast Statue potion inherited an unused drop-probability input.");
        Check(Fact(node, "combat.compute.rng.Rewards.consumed").Status == PredictorKnowledgeStatus.Known &&
            Fact(node, "combat.compute.rng.Rewards.consumed").AllReadFactIds.All(id =>
                snapshot.Fact(id).PropertyKey != "combat.input.odds.potionDrop") &&
            Fact(node, "combat.compute.odds.potionDrop").Status == PredictorKnowledgeStatus.Unknown &&
            Fact(node, "combat.compute.odds.potionDrop").DisplayValue == null,
            "A skipped drop roll tainted its fixed consumption or exposed the unchanged unknown drop state.");
    }
}
