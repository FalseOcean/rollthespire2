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
    internal static int Run(bool gpu = false)
    {
        if (gpu && Environment.GetEnvironmentVariable("RT2_AUTHORED_GODOT_RUNNER") != "1")
            throw new InvalidOperationException("Run --transformation-ancient-gpu in the rendering-enabled AuthoredEventChecks host.");
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

        void Compare(string label, SearchModel model, bool mixed, bool verifyPrivate = false)
        {
            var numerical = TransformationAncientNumericalPlan.TryCreate(model);
            Check(numerical is not null, label + " binds numerical execution");
            // Cross a cached eight-character prefix boundary in a high global range.
            var batch = gpu ? new SearchBatch((1UL << 28) * NeowPrefixHashCache.BlockSize - 73, 4099) : new SearchBatch(0, 4096);
            var accepted = new List<ulong>();
            for (ulong ordinal = 0; ordinal < (ulong)batch.BatchCandidateCount; ordinal++)
            {
                ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(batch.GlobalCandidate(ordinal)));
                var exact = TransformationAggregateValidator.Evaluate(model, root, model.Authority);
                Check(exact.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch &&
                    numerical!.Matches(root) == (exact.Disposition == SearchDisposition.Match), label + " exact parity at " + ordinal);
                if (exact.Disposition == SearchDisposition.Match) accepted.Add(ordinal);
                checkedSeeds++;
            }
            Check(mixed ? accepted.Count > 0 && accepted.Count < batch.BatchCandidateCount : accepted.Count == 0,
                label + " fixture exercises expected population");
            if (gpu)
                Task.Run(() => CompareGpuAsync(label, model, batch, accepted, verifyPrivate)).GetAwaiter().GetResult();
        }

        TransformationAncientNumericalPlan.WorkEstimate Estimate(TransformationAggregateCondition condition) =>
            TransformationAncientNumericalPlan.TryCreate(Model(condition))!.EstimateWork();
        var triple = Estimate(astro with
        {
            ResultRequirements = [new([astrolabe], Predicate.ContainsMultiset, 0, [banshee, banshee, banshee])]
        });
        Check(triple.Survival is { } pTriple && Math.Abs(pTriple - 0.064) < 1e-12 &&
            Math.Abs(triple.ExpectedDraws - 1.56) < 1e-12, "weighted repeated target stops at the first mismatch");
        Check(Estimate(astro).Survival is { } pJoint && Math.Abs(pJoint - 0.192) < 1e-12,
            "a named Rare draw also satisfies the rarity threshold");
        var remainder = Estimate(astro with
        {
            ResultRequirements = [new([astrolabe], Predicate.ContainsMultisetAndRemainingRare, 0, [common])]
        });
        Check(remainder.Survival is { } pRemainder && Math.Abs(pRemainder - 0.216) < 1e-12,
            "remaining Rare permits exactly one named non-Rare");
        var largeBox = Estimate(box with
        {
            PandoraCountOverride = TransformationRelicProjector.ProjectionDrawBudget,
            ResultRequirements = [new([pandora], Predicate.RareCountAtLeast, TransformationRelicProjector.ProjectionDrawBudget / 2, [])]
        });
        Check(largeBox.Survival is null && largeBox.ExpectedDraws == TransformationRelicProjector.ProjectionDrawBudget,
            "large manual count keeps a bounded scheduling estimate");

        var fiveMaterials = Model(box with { PandoraCountOverride = 5 });
        Check(TransformationAggregateFamily.TryCreate(fiveMaterials, new(16, false, MapLossyOptions.Default), out var bounded,
            preferGpu: false) && bounded is { UsesGpu: false },
            "five-material body binds for prefix-aware pricing");
        Check(TransformationAggregateFamily.TryCreate(fiveMaterials, new(16, false, MapLossyOptions.Default), out var preferred)
            && preferred is { UsesGpu: true } && preferred.CpuAlternatives.Any(body => body.PhysicalRevision == bounded!.PhysicalRevision),
            "the fixed-pool GPU body retains the numerical CPU alternative");
        const string prefixId = "Fixture.Prefix";
        foreach (bool known in new[] { true, false })
        {
            var prefix = new FilterBodyDescription(new(prefixId)
            {
                Survival = known ? FamilySurvivalProjection.Resolved(prefixId, 7d / 32, "fixture") :
                    FamilySurvivalProjection.Unresolved(prefixId, "fixture")
            }, "Fixture", RuntimeProfileId.Beta111)
            {
                Cost = new() { Quote = (_, _, _) => new(prefixId, "Fixture", .1, 65536, null, "fixture",
                    PublicTransportClass: "CpuOrderedAbi1") }
            };
            FilterBodyDescription[] families = [prefix, bounded!];
            var automatic = FamilyPlanner.PlanSimple(families, denseEntry: known ? null : prefix);
            var manual = FamilyPlanner.InManualOrder(families, families, [prefixId, TransformationAggregateFamily.Id]);
            foreach (var selected in new[] { automatic, manual })
            {
                var stage = selected.Stages[1];
                Check(stage.Family == bounded && stage.Performance.Evidence.Contains(
                    known ? "NumericalPartitions=14;" : "NumericalPartitions=16;", StringComparison.Ordinal),
                    "compact bounded CPU prices a known prefix and preserves the static quote for an unknown prefix");
            }
        }

        Compare("eight materials and three identical targets", Model(box), true);
        Compare("uses captured quantity when display cache differs", Model(box with { PandoraAutomaticCount = 12 }), true);
        Compare("legacy single predicate and manual count above auto", Model(box with
            { PandoraCountOverride = 11, ResultRequirements = [] }), true);
        Compare("three upgraded Astrolabe draws with mixed criteria", Model(astro), true);
        Compare("multiple predicates and exact non-Rare remainder", Model(needsRemainder), true, verifyPrivate: true);
        Compare("manual quantity and target count above 31", Model(box with
        {
            PandoraCountOverride = 80,
            ResultRequirements = [new([pandora], Predicate.ContainsMultiset, 0, Enumerable.Repeat(banshee, 32).ToArray())]
        }), true);
        Compare("Astrolabe rarity only", Model(astro with
        {
            ResultRequirements = [new([astrolabe], Predicate.RareCountAtLeast, 2, [])]
        }), true);
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

    private static async Task CompareGpuAsync(string label, SearchModel model, SearchBatch batch,
        List<ulong> accepted, bool verifyPrivate)
    {
        var dense = FamilyCandidateSet.Dense(batch);
        var sparse = FamilyCandidateSet.FromSortedAbi1(batch, dense.EnumerateLogicalOrdinals()
            .Where(i => i % 7 == 2 || i == (ulong)batch.BatchCandidateCount - 1).ToArray());
        var acceptedSet = accepted.ToHashSet();
        var sparseExpected = sparse.EnumerateLogicalOrdinals().Where(acceptedSet.Contains).ToArray();
        if (accepted.Count > 0 && (sparseExpected.Length == 0 || sparseExpected.Length == sparse.Count))
            throw new InvalidDataException(label + " compact fixture needs both matches and misses");

        await using var services = new FilterRuntimeServices(requiresGpu: true, failOnPhysicalRecovery: true);
        foreach (var variant in new[] { GpuNumericalVariant.NativeFloat64, GpuNumericalVariant.IntegerRne })
        {
            if (!TransformationAggregateFamily.TryCreate(model, new(1, false, MapLossyOptions.Default), out var body,
                    numericalVariant: variant) || body is not { UsesGpu: true } ||
                (variant == GpuNumericalVariant.IntegerRne) != (body.NumericRequirements == GpuNumericRequirements.Int64))
                throw new InvalidDataException(label + " missing GPU binding for " + variant);
            var execution = body.CreateExecution!(body, services);
            try
            {
                foreach (var input in new[] { dense, sparse })
                {
                    var actual = await execution.FilterAsync(services, input, default);
                    if (execution.ExecutionFaulted || actual.Batch != batch ||
                        !actual.EnumerateLogicalOrdinals().SequenceEqual(input.IsDense ? accepted : sparseExpected))
                        throw new InvalidDataException($"{label} {variant} dense={input.IsDense}: different ordered GPU population");
                }
                if (verifyPrivate)
                {
                    // Public compact batches can have a root population larger than the
                    // shared prefix table even though only a few ordinals reach T.
                    var largeBatch = new SearchBatch(batch.BatchBase, NeowPrefixHashCache.MaxCandidateCount + 73);
                    ulong[] ordinals = [.. sparse.EnumerateLogicalOrdinals(), (ulong)largeBatch.BatchCandidateCount - 1];
                    var input = FamilyCandidateSet.FromSortedAbi1(largeBatch, ordinals);
                    var expected = ordinals.Where(i => TransformationAggregateValidator.Evaluate(model,
                        Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(largeBatch.GlobalCandidate(i))),
                        model.Authority).Disposition == SearchDisposition.Match);
                    var actual = await execution.FilterAsync(services, input, default);
                    if (actual.Batch != largeBatch || !actual.EnumerateLogicalOrdinals().SequenceEqual(expected))
                        throw new InvalidDataException($"{label} {variant} oversized compact root hash parity");
                }
                Console.WriteLine($"Ancient GPU parity: {label};variant={variant};device={execution.ResourceInfo.Device};" +
                    $"origin={batch.BatchBase};dense={accepted.Count}/{dense.Count};compact={sparseExpected.Length}/{sparse.Count}");
            }
            finally { await execution.DisposeAsync(services); }

            if (!verifyPrivate || variant != GpuNumericalVariant.NativeFloat64) continue;
            await services.ExecuteGpuAsync(device =>
            {
                using var privateOutput = new PrivateOrdinalBuffer(device);
                using var privateInput = new PrivateOrdinalBuffer(device);
                var bind = body.BindPrivate ?? throw new InvalidDataException(label + " missing native private binding");
                using (var producer = bind(device, null, privateOutput))
                {
                    var result = producer.Execute(batch, dense.Count, default);
                    privateOutput.VerifyPopulation(batch, result.Count, FamilyCandidateSet.FromSortedAbi1(batch, accepted.ToArray()));
                }
                // A GPU append may produce any ordering. Verify that the next stage
                // follows these actual local ordinals, including both misses and matches.
                byte[] payload = FamilyGpuComputeUtility.ToBytes(sparse.EnumerateLogicalOrdinals().Reverse().Select(i => (uint)i).ToArray());
                if (device.BufferUpdate(privateInput.Buffer, 0, (uint)payload.Length, payload) != Godot.Error.Ok)
                    throw new InvalidOperationException(label + " private ordinal fixture upload failed");
                var expected = FamilyCandidateSet.FromSortedAbi1(batch, sparseExpected);
                using (var middle = bind(device, privateInput, privateOutput))
                {
                    var result = middle.Execute(batch, sparse.Count, default);
                    privateOutput.VerifyPopulation(batch, result.Count, expected);
                }
                using var terminal = bind(device, privateInput, null);
                var final = terminal.Execute(batch, sparse.Count, default).PublicOutput;
                if (final?.Batch != batch || !final.EnumerateLogicalOrdinals().SequenceEqual(sparseExpected))
                    throw new InvalidDataException(label + " private terminal output differs from domain oracle");
                Console.WriteLine($"Ancient native private producer/middle/terminal parity: {label};dense={accepted.Count};compact={sparseExpected.Length}");
                return true;
            }, default);
        }
    }
}
