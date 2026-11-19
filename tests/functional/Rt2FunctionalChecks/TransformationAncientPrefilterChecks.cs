using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using Predicate = RolltheSpire2.Search.Semantics.TransformationAggregatePredicate;

internal static class TransformationAncientPrefilterChecks
{
    internal static int Run()
    {
        const string modern = "0.113.1";
        const string older = "0.111.0";
        const string pandora = "A.PANDORAS_BOX";
        const string astrolabe = "A.ASTROLABE";
        var hero = BaseGameModelKeys.Characters.Ironclad;
        var capture = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context.Authority.EffectAuthority!;
        var basic = capture.OrderedDeck!.First();
        var banshee = new ModelKey("CARD", "BANSHEES_CRY");
        var rare = new ModelKey("CARD", "CHECK_RARE");
        var common = new ModelKey("CARD", "CHECK_COMMON");
        var differentCommon = new ModelKey("CARD", "CHECK_OTHER_COMMON");
        var effects = capture with
        {
            TransformPool =
            [
                basic with { CardKey = differentCommon, PoolOrder = 3, Rarity = EffectCardRarity.Uncommon },
                basic with { CardKey = banshee, PoolOrder = 1, Rarity = EffectCardRarity.Rare },
                basic with { CardKey = common, PoolOrder = 0, Rarity = EffectCardRarity.Common },
                basic with { CardKey = rare, PoolOrder = 4, Rarity = EffectCardRarity.Rare },
                basic with { CardKey = banshee, PoolOrder = 2, Rarity = EffectCardRarity.Rare }
            ],
            OrderedDeck = capture.OrderedDeck!.Skip(1).ToArray()
        };
        SearchModel Model(TransformationAggregateCondition condition, string version = modern,
            NeowEffectAuthoritySnapshot? authorityEffects = null)
        {
            var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(hero, 10, 0, 1, version)
                .WithEffectAuthority(authorityEffects ?? effects);
            var context = new SearchContext(RuntimeProfileId.Beta111, hero, 10, authority,
                new(version, version, "beta", "ancient-prefilter-fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
            var compiled = SearchCompiler.Compile(SearchQuery.Empty, context);
            return new(compiled, ExactSearchEvaluationProjection.Empty with { TransformationAggregate = condition.Normalize() },
                CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint);
        }

        var box = new TransformationAggregateCondition(TransformationOpening.None, TransformationPickupOrder.Any,
            false, false, false, Predicate.ContainsMultiset, 0, [banshee, banshee, banshee])
        {
            AncientSource = TransformationAncientSource.PandorasBox,
            AncientAct = 2,
            PandoraAutomaticCount = 8,
            ResultRequirements = [new([pandora], Predicate.ContainsMultiset, 0, [banshee, banshee, banshee])]
        };
        var astro = box with
        {
            AncientSource = TransformationAncientSource.Astrolabe,
            PandoraAutomaticCount = 0,
            ResultRequirements = [new([astrolabe], Predicate.ContainsMultisetAndRareCount, 2, [common, banshee])]
        };
        var needsRemainder = box with
        {
            ResultRequirements =
            [
                new([pandora], Predicate.ContainsMultisetAndRemainingRare, 0, [common, common, banshee]),
                new([pandora], Predicate.ContainsMultisetAndRareCount, 2, [common, rare]),
                new([pandora], Predicate.RareCountAtLeast, 3, [])
            ]
        };
        int checkedSeeds = 0;
        void Check(bool ok, string reason)
        {
            if (!ok) throw new InvalidOperationException("Transformation ancient prefilter: " + reason);
        }

        void Compare(string label, SearchModel model, bool mixed)
        {
            var numerical = TransformationAncientNumericalPlan.TryCreate(model);
            Check(numerical is not null, label + " binds numerical execution");
            int hits = 0;
            for (ulong ordinal = 0; ordinal < 4096; ordinal++)
            {
                ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                var exact = TransformationAggregateValidator.Evaluate(model, root, model.Authority);
                Check(exact.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch &&
                    numerical!.Matches(root) == (exact.Disposition == SearchDisposition.Match), label + " exact parity at " + ordinal);
                if (exact.Disposition == SearchDisposition.Match) hits++;
                checkedSeeds++;
            }
            Check(mixed ? hits is > 0 and < 4096 : hits == 0, label + " fixture exercises expected population");
        }

        Compare("eight materials and three identical targets", Model(box), true);
        Compare("uses captured quantity when display cache differs", Model(box with { PandoraAutomaticCount = 12 }), true);
        Compare("legacy single predicate and manual count above auto", Model(box with
            { PandoraCountOverride = 11, ResultRequirements = [] }), true);
        Compare("three upgraded Astrolabe draws with mixed criteria", Model(astro), true);
        Compare("multiple predicates and exact non-Rare remainder", Model(needsRemainder), true);
        Compare("inconsistent bounds on one named non-Rare", Model(needsRemainder with
        {
            ResultRequirements =
            [
                new([pandora], Predicate.ContainsMultisetAndRemainingRare, 0, [common]),
                new([pandora], Predicate.ContainsMultiset, 0, [common, common])
            ]
        }), false);
        Compare("card absent from closed captured pool", Model(box with
            { ResultRequirements = [new([pandora], Predicate.ContainsMultiset, 0, [new("CARD", "NOT_IN_POOL")])] }), false);
        Compare("automatic materials missing but manual count closes the premise",
            Model(box with { PandoraCountOverride = 8 }, authorityEffects: effects with { OrderedDeck = null }), true);

        var unresolved = new[]
        {
            Model(box, older),
            Model(box, authorityEffects: effects with { OrderedDeck = null }),
            Model(box, authorityEffects: effects with { TransformPool = null }),
            Model(box with { PandoraCountOverride = TransformationRelicProjector.ProjectionDrawBudget + 1 }),
            Model(box, authorityEffects: effects with
            {
                TransformPool = [.. effects.TransformPool!, effects.TransformPool![1] with { Rarity = EffectCardRarity.Common }]
            })
        };
        Check(unresolved.All(model => TransformationAncientNumericalPlan.TryCreate(model) is null),
            "shared Niche, missing authority, over-budget queries and inconsistent rarity stay with semantic evaluation");
        var request = Model(box);
        var withOpening = request with
        {
            Evaluation = request.Evaluation with { NeowRoute = new(BaseGameModelKeys.Relics.LeafyPoultice) }
        };
        Check(TransformationAncientNumericalPlan.TryCreate(withOpening) is null,
            "an unowned but selected Leafy Poultice still changes the Ancient cursor");

        var grove = new ModelKey("EVENT", "MORPHIC_GROVE");
        var withEarlierEvent = request with
        {
            Evaluation = request.Evaluation with
            {
                EventResultConditions = [new(EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, common)],
                EventSequenceConditions = [new(1, null, SearchSequenceRangeMode.FirstN, 4, new([grove], [], []))]
            }
        };
        Check(TransformationAncientNumericalPlan.TryCreate(withEarlierEvent) is null,
            "an earlier event transform may change automatic material count");
        var later = withEarlierEvent with
        {
            Evaluation = withEarlierEvent.Evaluation with
                { EventSequenceConditions = [new(2, null, SearchSequenceRangeMode.FirstN, 4, new([grove], [], []))] }
        };
        Check(TransformationAncientNumericalPlan.TryCreate(later) is not null,
            "events in the same act occur after its Ancient");
        Check(TransformationAncientNumericalPlan.TryCreate(withEarlierEvent with
        {
            Evaluation = withEarlierEvent.Evaluation with
                { TransformationAggregate = box with { PandoraCountOverride = 8 } }
        }) is not null, "manual material count is independent of earlier event draws");

        Console.WriteLine($"Transformation ancient prefilter checks passed ({checkedSeeds} seed comparisons).");
        return 0;
    }
}
