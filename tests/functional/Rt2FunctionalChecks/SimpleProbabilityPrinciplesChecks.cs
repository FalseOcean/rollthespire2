using System.Reflection;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

internal static class SimpleProbabilityPrinciplesChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        { checks++; if (!condition) throw new InvalidDataException("SimpleProbabilityPrinciples: " + name); }
        void Near(double? actual, double expected, string name) => Check(actual.HasValue &&
            Math.Abs(actual.Value - expected) < 1e-12, name + ";actual=" + actual + ";expected=" + expected);
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        SearchSelectivityInput Input(SearchQuery query, SearchContext? context = null) =>
            SearchSelectivityInput.From(SearchCompiler.Compile(query, context ?? captured));
        ModelKey a = new("RELIC", "ANCHOR"), b = new("RELIC", "BAG_OF_PREPARATION"),
            c = new("RELIC", "PANTOGRAPH"), d = new("RELIC", "PRAYER_WHEEL");
        SearchContext Bags(params ModelKey[][] lanes)
        {
            var world = captured.Authority.WorldAuthority!;
            string[] names = ["Common", "Uncommon", "Rare"];
            var generation = world.Beta109Generation! with { SharedRelicBuckets = names.Select((name, i) =>
                new Beta109RelicBucketSnapshot("shared:" + name, lanes[i], true)).ToArray() };
            return captured with { Authority = captured.Authority.WithWorldAuthority(world with { Beta109Generation = generation }) };
        }
        var tiny = Bags([a, b], [c], [d]);
        SearchQuery Treasure(int start, int count, TreasureRelicOrderMode order, params ModelKey?[] targets) =>
            SearchQuery.Empty with { TreasureRelics = new(start, count, order, targets) };
        double? Chest(SearchQuery q, SearchContext? context = null) => TreasureRelicProbability.Estimate(Input(q, context ?? tiny)).Probability;
        Near(Chest(Treasure(1, 1, TreasureRelicOrderMode.Ordered, a)), .25, "common target rarity times uniform shared bag");
        Near(Chest(Treasure(2, 1, TreasureRelicOrderMode.Ordered, a)), .25, "second encounter includes prior consumption");
        Near(Chest(Treasure(1, 2, TreasureRelicOrderMode.Ordered, a, b)), .125, "two common targets without replacement");
        Near(Chest(Treasure(1, 2, TreasureRelicOrderMode.Unordered, a, b)), .25, "unordered two-target union");
        Near(Chest(Treasure(1, 2, TreasureRelicOrderMode.Unordered, a, a)), 0, "one shared identity cannot fill two slots");
        Near(Chest(Treasure(1, 2, TreasureRelicOrderMode.Ordered, a, d)), .0425, "distinct rarity draws preserve their own masses");
        Near(Chest(Treasure(1, 1, TreasureRelicOrderMode.Ordered, c), Bags([], [c], [d])), .83, "empty common falls upward");
        Near(Chest(Treasure(1, 1, TreasureRelicOrderMode.Ordered, new ModelKey("RELIC", "CIRCLET")), Bags([], [], [])), 1, "known exhausted bags produce Circlet");
        var suppressed = Treasure(1, 1, TreasureRelicOrderMode.Ordered, a) with { OpeningRoute = new(new("RELIC", "SILVER_CRUCIBLE")) };
        Near(Chest(suppressed), 0, "explicit first-treasure suppression");
        var missing = tiny with { Authority = tiny.Authority.WithWorldAuthority(tiny.Authority.WorldAuthority! with { Beta109Generation = null }) };
        Check(Chest(Treasure(1, 1, TreasureRelicOrderMode.Ordered, a), missing) is null, "missing shared bags remain Unknown");
        var acquiredA = Treasure(1, 1, TreasureRelicOrderMode.Ordered, a) with {
            OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule), StructuredOpeningEffects = [new(
                BaseGameModelKeys.Relics.SmallCapsule, NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [a])] };
        Near(Chest(acquiredA), 1, "explicit acquired relic already satisfies its treasure goal");
        Near(Chest(acquiredA, missing), 1, "known opening acquisition does not need unrelated future bag authority");
        Near(Chest(acquiredA with { TreasureRelics = new(1, 2, TreasureRelicOrderMode.Unordered, [a, a]) }), 0,
            "one acquired identity cannot discharge repeated treasure goals twice");

        var prefix = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 };
        double neutral = FirstActUnknownEventProbability.ConditionalPrefix(3, false);
        foreach (var source in new[] { BaseGameModelKeys.Relics.NeowsBones, BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule })
            Near(FirstActUnknownEventProbability.Estimate(Input(prefix with { OpeningRoute = new(source) })).Probability,
                neutral, "unspecified opening slots do not invent Juzu: " + source);
        ModelKey juzu = new("RELIC", "JUZU_BRACELET");
        var explicitJuzu = prefix with { OpeningRoute = new(BaseGameModelKeys.Relics.LargeCapsule),
            LegacyNeow = prefix.LegacyNeow with { CapsuleContainedRelics = new([], [juzu], []) } };
        Near(FirstActUnknownEventProbability.Estimate(Input(explicitJuzu)).Probability,
            FirstActUnknownEventProbability.ConditionalPrefix(3, true), "explicit All Juzu changes room odds");
        Near(FirstActUnknownEventProbability.Estimate(Input(explicitJuzu with { LegacyNeow = explicitJuzu.LegacyNeow with {
            CapsuleContainedRelics = new([juzu, a], [], []) } })).Probability, neutral, "multi-Any does not guarantee Juzu");
        var contextFactory = typeof(CombinationRegressionChecks).GetMethod("PartyContext", BindingFlags.NonPublic | BindingFlags.Static)!;
        var party = (SearchContext)contextFactory.Invoke(null, [captured, 3, null, null])!;
        var sharedPrefix = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 1,
            Players = Enumerable.Range(0, 3).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) {
                Conditions = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = slot + 1 } }).ToArray() };
        Near(FirstActUnknownEventProbability.Estimate(Input(sharedPrefix, party)).Probability, neutral,
            "shared party prefix uses maximum once, not personal product");

        var effects = captured.Authority.EffectAuthority!;
        var template = effects.CharacterRewardPool!.First(card => card.Rarity == EffectCardRarity.Common && card.IsUnlockedInCapturedPool);
        ModelKey[] cards = Enumerable.Range(0, 4).Select(i => new ModelKey("CARD", "SIMPLE_PROBABILITY_" + i)).ToArray();
        var cardContext = captured with { Authority = captured.Authority.WithEffectAuthority(effects with {
            CharacterRewardIdentityPoolExact = true,
            CharacterRewardPool = cards.Select((key, i) => template with { CardKey = key, PoolOrder = i, CardType = EffectCardType.Attack }).ToArray() })
                .WithAvailableCardIdentities(cards) };
        SearchQuery Ban(int horizon, params ModelKey[] banned) => SearchQuery.Empty with { CombatCardExclusion = new(horizon, banned) };
        Near(CombatRewardProbabilityEstimator.Estimate(Input(Ban(1, cards[0]), cardContext)).Probability, .25,
            "three distinct choices avoid one of four with probability one quarter");
        Near(CombatRewardProbabilityEstimator.Estimate(Input(Ban(2, cards[0]), cardContext)).Probability, .0625,
            "each battle has its own distinct reward group");
        Near(CombatRewardProbabilityEstimator.Estimate(Input(Ban(1, cards), cardContext)).Probability, 0,
            "excluding captured full pool is impossible in declared distribution");
        var combined = Ban(1, cards[0]) with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [cards[1]]) };
        Near(CombatRewardProbabilityEstimator.EstimateOptimizedConditions(Input(combined, cardContext)).Probability, .75,
            "positive optimization strips Exact-only exclusion");
        Near(CombatRewardProbabilityEstimator.Estimate(Input(combined, cardContext)).Probability, .75 * .25,
            "positive and exclusion use Owner-approved independent factors");
        Near(CombatRewardProbabilityEstimator.EstimateOptimizedConditions(Input(Ban(1, cards[0]), cardContext)).Probability, 1,
            "exclusion-only request has no optimized C condition");
        var unknownCards = cardContext with { Authority = cardContext.Authority.WithEffectAuthority(effects with { CharacterRewardPool = null }) };
        Check(CombatRewardProbabilityEstimator.Estimate(Input(Ban(1, cards[0]), unknownCards)).Probability is null,
            "missing captured card pool never becomes successful absence");
        foreach (var identities in new IReadOnlyList<ModelKey>?[] { null, [], cards.Skip(1).ToArray() })
        {
            var missingIdentity = cardContext with { Authority = cardContext.Authority.WithAvailableCardIdentities(identities) };
            Check(CombatRewardProbabilityEstimator.Estimate(Input(Ban(1, cards[0]), missingIdentity)).Probability is null,
                "null, empty or incomplete registered identity authority retains Exact's Unknown boundary");
        }
        ModelKey ineligible = new("CARD", "SIMPLE_PROBABILITY_REGISTERED_OUTSIDE_POOL");
        var registeredOutsidePool = cardContext with { Authority = cardContext.Authority.WithAvailableCardIdentities([..cards, ineligible]) };
        Near(CombatRewardProbabilityEstimator.Estimate(Input(Ban(1, ineligible), registeredOutsidePool)).Probability, 1,
            "known registered identity absent from the captured reward pool has known absence probability");

        var aggregate = new TransformationAggregateCondition(TransformationOpening.LeafyPoultice,
            TransformationPickupOrder.Any, false, false, false, TransformationAggregatePredicate.RareCountAtLeast, 1, []);
        var parent = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice) };
        var extra = parent with { LegacyNeow = parent.LegacyNeow with {
            NeowRelics = new([], [BaseGameModelKeys.Relics.Pomander], []) } };
        var parentP = JointSelectivityEstimator.EstimateQuery(Input(parent));
        var extraP = JointSelectivityEstimator.EstimateQuery(Input(extra));
        var aggregateP = JointSelectivityEstimator.EstimateQuery(Input(parent with { TransformationAggregate = aggregate }));
        var combinedP = JointSelectivityEstimator.EstimateQuery(Input(extra with { TransformationAggregate = aggregate }));
        Check(parentP.Probability is > 0 && extraP.JointlyPriced && aggregateP.JointlyPriced, "T and extra N captured source marginals available");
        Near(combinedP.Probability, aggregateP.Probability!.Value * extraP.Probability!.Value / parentP.Probability!.Value,
            "T extra N uses approved independence and pays parent identity once");
        var companion = aggregate with { Opening = TransformationOpening.BonesLeafyOther,
            CompanionRelic = BaseGameModelKeys.Relics.SmallCapsule };
        var companionQuery = SearchQuery.Empty with { TransformationAggregate = companion,
            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                [BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.SmallCapsule]) };
        var compiledCompanion = SearchCompiler.Compile(companionQuery, captured);
        var request = new ExactSearchExecutionRequest(compiledCompanion, new("000000000000", 1, 1, 1), "000000000000", 1,
            CompiledSearchEvaluationProjector.Project(compiledCompanion).Evaluation, CombatRewardRoutePolicyContract.None, compiledCompanion.SemanticFingerprint);
        var numerical = new TransformationAggregateNumericalPlan(request);
        var companionProbability = TransformationAggregateProbability.Build(numerical);
        Check(!numerical.Closed && numerical.ProbabilityClosed && companionProbability.QueryHitProbability.HasValue,
            "unspecified companion permits declared marginal probability while runtime continuation proof remains unchanged");
        Check(companionProbability.StageSurvival is null && companionProbability.Evidence.Contains("RuntimeSurvivalUnmodeled", StringComparison.Ordinal),
            "query marginal does not claim the conservative runtime's population");
        var companionFamily = new TransformationAggregateFamily(request, preferGpu: false);
        Check(!companionFamily.Survival.IsResolved && !companionFamily.ResolveSurvival(new HashSet<string> { "N.Neow" }).IsResolved,
            "neither unconditional nor given-N physical survival can recover the query marginal");
        Check(companionProbability.QueryHitProbability is double companionQ && companionQ > 0 && companionQ < companionProbability.IdentityProbability &&
            Enumerable.Range(0, 8192).Select(i => (ulong)i).Any(numerical.MatchesCollected),
            "the real conservative collector admits a witness while the declared marginal includes a nontrivial transform factor");
        Console.WriteLine($"SimpleProbabilityPrinciples checks={checks};PASS");
        return 0;
    }
}
