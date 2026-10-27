using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Tests.FastSearch;

internal static partial class FamilyNeowChecks
{
    private static void CheckBonesArcaneComposite(ExactSearchExecutionRequest source, List<object> fixtures)
    {
        var request = WithSmallRelicLanes(source);
        // Match Core's captured source pools to R's World pool, as in the existing
        // grouped Exact fixture. Small immutable lanes make positive tests cheap.
        var world = request.Authority.WorldAuthority!;
        var generation = world.Beta109Generation!;
        NeowEffectRelicSnapshot[] Relics(IEnumerable<Beta109RelicBucketSnapshot> buckets) => buckets
            .SelectMany(bucket => bucket.OrderedEntries.Select((entry, index) => new NeowEffectRelicSnapshot(
                entry.RelicKey, index, Enum.Parse<EffectRelicRarity>(bucket.BucketId.Split(':')[^1]),
                NestedRelicEffectKind.NoTrackedImmediateEffect, RarityCode: bucket.BucketId.Split(':')[^1],
                IsAllowedInShops: entry.IsAllowedInShops, ShopEligibilityExact: true))).ToArray();
        var effects = request.Authority.EffectAuthority! with {
            SharedRelicPoolSource = Relics(generation.SharedRelicBuckets),
            CharacterRelicPoolSource = Relics(generation.PlayerRelicBuckets), CapturedProfileId = RuntimeProfileId.Beta111,
            CharacterRewardIdentityPoolExact = true
        };
        var players = generation.PlayerRelicBuckets.Select(bucket => {
            var shared = generation.SharedRelicBuckets.Single(b => b.BucketId.Split(':')[^1] == bucket.BucketId.Split(':')[^1]);
            var entries = shared.OrderedEntries.Concat(bucket.OrderedEntries).DistinctBy(entry => entry.RelicKey).ToArray();
            return bucket with { OrderedEntries = entries, OrderedRelics = entries.Select(e => e.RelicKey).ToArray() };
        }).ToArray();
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(request.Authority.Character.CharacterKey,
            request.Authority.Ascension, 0, 1).WithEffectAuthority(effects).WithWorldAuthority(world with {
                CapturedProfileId = RuntimeProfileId.Beta111,
                Beta109Generation = generation with { Profile = RuntimeProfileId.Beta111, PlayerRelicBuckets = players }
            });
        var context = request.CompiledSearch.Context with { Authority = authority, ProfileId = RuntimeProfileId.Beta111,
            Detection = new GameVersionDetection(RuntimeVersionCompatibility.Beta111Version, RuntimeVersionCompatibility.Beta111Version,
                "beta", "bones-arcane-nr-fixture", true, string.Empty) };
        ModelKey large = BaseGameModelKeys.Relics.LargeCapsule, arcane = BaseGameModelKeys.Relics.ArcaneScroll;
        var identity = NeowSearchFilter.Empty with { NeowRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            RequiredBonesCombination = [large, arcane] };
        ExactSearchExecutionRequest Query(NeowSearchFilter filter) =>
            CompileFixtureRequest(filter, context, request.RunOptions);
        request = Query(identity);
        var identityPlan = NeowReplayPlan.Compile(request, false);
        var seenOffers = new HashSet<byte>();
        int checks = 0;
        for (ulong ordinal = 0; ordinal < 200_000 && seenOffers.Count < 2; ordinal++)
        {
            string seed = Beta110SeedCodec.FormatOrdinal(ordinal);
            ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
            var observation = NeowFamilyReplay.Observe(root, identityPlan);
            if (!observation.IdentityPass || seenOffers.Contains(observation.BonesFirst)) continue;
            if (!SearchAuthorityProjector.TryProjectForRootHash(request, root, seed, out var projected, out _, out string issue))
                throw new Exception(issue);
            var routes = Beta111NeowEffectProjector.ProjectFromRootHash(BaseGameModelKeys.Relics.NeowsBones,
                PredictionPrecision.Exact, projected, root).BonesOutcome!.OriginalRoutes;
            var routeA = routes.Single(r => r.RelicScopedResults[0].SourceRelicKey == large);
            var routeB = routes.Single(r => r.RelicScopedResults[0].SourceRelicKey == arcane);
            ModelKey[] Outputs(IEnumerable<PredictedEffectGroup> groups, PredictedEffectKind kind) => groups
                .SelectMany(g => g.OrderedItems).Where(i => i.Kind == kind && i.TargetKey.HasValue &&
                    (kind != PredictedEffectKind.AddRelic || i.Relation == PredictedEffectRelation.NestedRelic))
                .Select(i => i.TargetKey!.Value).ToArray();
            var relicsA = Outputs(routeA.RelicScopedResults.Single(r => r.SourceRelicKey == large).EffectGroups, PredictedEffectKind.AddRelic);
            var relicsB = Outputs(routeB.RelicScopedResults.Single(r => r.SourceRelicKey == large).EffectGroups, PredictedEffectKind.AddRelic);
            var cardsA = Outputs(routeA.RelicScopedResults.Single(r => r.SourceRelicKey == arcane).EffectGroups, PredictedEffectKind.AddCard);
            var cardsB = Outputs(routeB.RelicScopedResults.Single(r => r.SourceRelicKey == arcane).EffectGroups, PredictedEffectKind.AddCard);
            if (relicsA.Length != 2 || relicsB.Length != 2 || cardsA.Length != 1 || cardsB.Length != 1)
                throw new Exception($"Core Bones Arcane fixture output shape changed: relics={relicsA.Length}/{relicsB.Length};cards={cardsA.Length}/{cardsB.Length}");
            if (cardsA[0] == cardsB[0] || relicsA.ToHashSet().SetEquals(relicsB)) continue;
            seenOffers.Add(observation.BonesFirst);
            if (!TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance, root, seed, out var input, out issue)) throw new Exception(issue);
            ModelKey neitherCard = effects.CharacterRewardPool!.First(c => c.Rarity == EffectCardRarity.Rare &&
                c.CardKey != cardsA[0] && c.CardKey != cardsB[0]).CardKey;
            foreach (var shape in new[] { "A", "B", "Cross", "NReject", "RReject", "PinnedA", "PinnedB" })
            {
                var targets = shape is "B" or "PinnedB" ? relicsB : relicsA;
                if (shape == "RReject")
                    targets = effects.CharacterRelicPoolSource!.Select(r => r.RelicKey)
                        .Concat(effects.SharedRelicPoolSource!.Select(r => r.RelicKey)).Distinct()
                        .Where(k => !relicsA.Contains(k) && !relicsB.Contains(k)).Take(2).ToArray();
                var card = shape is "B" or "Cross" or "PinnedB" ? cardsB[0] : shape == "NReject" ? neitherCard : cardsA[0];
                var capsule = source.Evaluation.ToLegacyFilter().StructuredNeowEffects.Single() with { OutputKeys = targets };
                var condition = new NeowStructuredEffectSearchCondition(arcane, NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.ProductRelevantEffects, NeowStructuredOutputKind.Card, [card]);
                var filter = identity with { StructuredNeowEffects = [capsule, condition],
                    RequiredBonesAcquisitionOrder = shape == "PinnedA" ? [large, arcane] : shape == "PinnedB" ? [arcane, large] : [] };
                var query = Query(filter);
                var n = NeowReplayPlan.Compile(query, false);
                var r = NeowReplayPlan.Compile(query, true);
                bool compositeReady = NeowCapsuleComposite.TryCreate(query, n, out var nr);
                bool gpuReady = NeowFamilyGpuPlan.TryCreate(n, out var gpu, out issue);
                if (!compositeReady || nr?.UsesBonesArcane != true || !gpuReady || gpu is null)
                    throw new Exception($"Bones Arcane bounded admission failed: {shape};composite={compositeReady};gpu={gpuReady};issue={issue};exactOnly={string.Join(',', n.ExactOnly)};structured={n.StructuredConditions.Length}");
                bool keep = shape is not ("NReject" or "RReject");
                bool exactExpected = keep && shape != "Cross";
                if ((NeowFamilyReplay.Matches(root, n) && nr.Reference.Matches(root)) != keep ||
                    ProductionExactSearchEvaluator.Evaluate(query, input).IsMatch != exactExpected)
                    throw new Exception("Bones Arcane reference/Exact witness failed: " + shape);
                if (shape == "Cross")
                {
                    bool na = NeowFamilyReplay.Matches(root, n with { First = 3, Second = 10 });
                    bool nb = NeowFamilyReplay.Matches(root, n with { First = 10, Second = 3 });
                    bool ra = new CapsuleRelicReplay(query, r with { First = 3, Second = 10 }).Matches(root);
                    bool rb = new CapsuleRelicReplay(query, r with { First = 10, Second = 3 }).Matches(root);
                    if (na || !nb || !ra || rb) throw new Exception("Fixture is not a cross-route witness");
                }
                if (shape == "A" && seenOffers.Count == 1)
                {
                    if (!FamilyExecutionCoordinator.Plan(query).Stages.SelectMany(s => s.Coverage).ToHashSet().IsSupersetOf(["N.Neow", "R.Relic"]))
                        throw new Exception("Bones Arcane NR allocation missing");
                    if (RelicFullGpuPlan.TryCreate(query, r, out _, out _)) throw new Exception("Standalone Rfull admission widened");
                    var family = new NeowFamily(query, n, composite: nr);
                    var noGpu = new FamilyExecutionContext(requiresGpu: false);
                    var batch = new SearchBatch(ordinal, 1);
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        bool faulted = false;
                        try { family.InvokeAsync(noGpu, new(batch, query), FamilyCandidateSet.Dense(batch), CancellationToken.None).AsTask().GetAwaiter().GetResult(); }
                        catch (InvalidOperationException) { faulted = true; }
                        if (!faulted) throw new Exception("Selected Bones Arcane physical silently recovered");
                    }
                    foreach (var rejected in new[] {
                        query with { Evaluation = query.Evaluation with { StructuredNeowEffects = [capsule with { OutputKeys = targets.Take(1).ToArray() }, condition] } },
                        query with { Evaluation = query.Evaluation with { RelicSequenceConditions = [new(RolltheSpire2.Core.Relics.RelicSequenceKind.Common,
                            SearchSequenceRangeMode.FirstN, 1, new([], [targets[0]], []))] } },
                        query with { Evaluation = query.Evaluation with { RequiredFinalCurse = BaseGameModelKeys.Relics.CursedPearl } }
                    })
                        if (NeowCapsuleComposite.TryCreate(rejected, NeowReplayPlan.Compile(rejected, false), out _))
                            throw new Exception("Bones Arcane out-of-scope admission");
                }
                foreach (bool compact in new[] { false, true })
                    fixtures.Add(new { name = $"Nr.BonesArcane.ExactWitness.{shape}.Offer{observation.BonesFirst}",
                        bonesComposite = true, bonesArcane = true, composite = nr.Metadata, compact, start = ordinal,
                        count = 1, ordinals = new uint[]{0}, expected = keep ? new uint[]{0} : [], gpu });
                // One broad positive population per offer layout checks stage
                // transport and ordinary rejected roots, not only handpicked roots.
                if (shape == "A")
                {
                    const int count = 65536;
                    ulong start = ordinal / count * count;
                    uint[] ordinals = Enumerable.Range(0, count).Select(i => (uint)i).ToArray();
                    var expected = ordinals.Where(i => { ulong h = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + i));
                        return NeowFamilyReplay.Matches(h, n) && nr.Reference.Matches(h); }).ToArray();
                    foreach (bool compact in new[] { false, true })
                        fixtures.Add(new { name = $"Nr.BonesArcane.Window.Offer{observation.BonesFirst}", bonesComposite = true, bonesArcane = true,
                            composite = nr.Metadata, compact, start, count, ordinals, expected, gpu });
                    var tinyN = n with { Authority = n.Authority with { BonesEligibleRelicIds = [3, 10] } };
                    var tinyR = new CapsuleRelicReplay(query, r with { Authority = r.Authority with { BonesEligibleRelicIds = [3, 10] } });
                    if (!NeowFamilyGpuPlan.TryCreate(tinyN, out var tinyGpu, out _) || tinyGpu is null || tinyGpu.UsesStagedDense)
                        throw new Exception("Bones Arcane fused fixture packing failed");
                    var tinyOrdinals = ordinals.Take(4096).ToArray();
                    var tinyExpected = tinyOrdinals.Where(i => { ulong h = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i));
                        return NeowFamilyReplay.Matches(h, tinyN) && tinyR.Matches(h); }).ToArray();
                    if (tinyExpected.Length == 0) throw new Exception("Bones Arcane fused fixture lacks positive roots");
                    fixtures.Add(new { name = $"Nr.BonesArcane.Fused.Offer{observation.BonesFirst}", bonesComposite = true, bonesArcane = true,
                        composite = nr.Metadata, compact = false, start = 0, count = 4096, ordinals = tinyOrdinals, expected = tinyExpected, gpu = tinyGpu });
                }
                checks++;
                Console.WriteLine($"Bones Arcane Exact/reference: seed={seed};shape={shape};family={keep};exact={exactExpected};PASS");
            }
        }
        if (seenOffers.Count != 2) throw new Exception("Bones Arcane fixtures lack both offer orders");
        Console.WriteLine($"Bones Arcane Nr: {checks} Core Exact/reference witnesses; two offers, two pickups, cross-route, rejection: PASS");
    }
}
