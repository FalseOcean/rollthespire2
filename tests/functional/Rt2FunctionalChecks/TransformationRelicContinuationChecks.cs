using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Shell;
using System.Text.Json;

internal static class TransformationRelicContinuationChecks
{
    internal static int Run()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var leafy = BaseGameModelKeys.Relics.LeafyPoultice;
        var leaf = BaseGameModelKeys.Relics.NewLeaf;
        var query = SearchQuery.Empty with
        {
            OpeningRoute = new(bones), OpeningRouteRelicRequirement = new(bones, [leafy, leaf]),
            TransformationAggregate = new(TransformationOpening.BonesLeafyNewLeaf, TransformationPickupOrder.Any,
                false, false, false, TransformationAggregatePredicate.ContainsMultiset, 0,
                [new("CARD", "DARK_EMBRACE"), new("CARD", "OFFERING"), new("CARD", "STOKE")]),
            RelicShopSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Unordered, [new("RELIC", "BELT_BUCKLE"), null, null])]
        };
        int checks = 0;
        void Check(bool value, string issue) { checks++; if (!value) throw new InvalidOperationException(issue); }
        const string seed = "0000000001FA";
        var profile = Beta111Profile.Instance;
        var root = TrustedRootHashInput.FromCanonicalSeed(profile, seed);
        var options = new SearchRunOptions(seed, 1, 30, 1);
        ExactSearchExecutionRequest Compile(SearchQuery q)
        {
            var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q, context), options);
            Check(result.Success && result.Plan is not null, "Compile/Exact admission: " + result.Issue);
            return result.Plan!;
        }
        var world = context.Authority.WorldAuthority!.Beta109Generation!;
        // Independent bag replay from the captured source buckets, following the
        // game's shared-then-personal UpFront shuffle and RelicCmd.Obtain removals.
        Dictionary<string, ModelKey[]> Bags(ulong hash, IEnumerable<ModelKey> acquired)
        {
            var rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(hash, "up_front"));
            var shared = world.SharedRelicBuckets.Select(b => b.OrderedEntries.ToList()).ToArray();
            foreach (var bucket in shared) rng.UnstableShuffle(bucket);
            var personal = world.PlayerRelicBuckets.Select(b => b.OrderedEntries.ToList()).ToArray();
            foreach (var bucket in personal) rng.UnstableShuffle(bucket);
            foreach (var key in acquired)
                foreach (var bucket in shared.Concat(personal)) bucket.RemoveAll(r => r.RelicKey == key);
            return world.PlayerRelicBuckets.Select((b, i) => new
                { b.BucketId, Keys = b.BucketId == "player:Shop" ? personal[i].Where(r => r.IsAllowedInShops).Reverse().Select(r => r.RelicKey).ToArray()
                    : personal[i].Select(r => r.RelicKey).ToArray() }).ToDictionary(b => b.BucketId["player:".Length..], b => b.Keys);
        }
        ModelKey[] Cards(IEnumerable<PredictedEffectGroup> groups) => groups.SelectMany(g => g.OrderedItems)
            .Where(i => i.Kind == PredictedEffectKind.TransformCard).Select(i => i.TargetKey!.Value).ToArray();
        var opening = new NeowEffectProjectionEngine(profile, "T.R.IndependentOpening", root.RootHash, context.Authority).Project(bones);
        var routes = opening.BonesOutcome!.OriginalRoutes;
        Check(routes.Count == 2 && routes.All(r => r.AcquisitionOrder.Contains(leafy) && r.AcquisitionOrder.Contains(leaf)), "Fixed real Bones witness");
        var cards = Cards(routes[0].RelicScopedResults.SelectMany(r => r.EffectGroups));
        var initial = Bags(root.RootHash, []);
        foreach (var route in routes)
        {
            var after = Bags(root.RootHash, new[] { bones }.Concat(route.AcquisitionOrder));
            Check(new[] { "Common", "Uncommon", "Rare", "Shop" }.All(lane => initial[lane].SequenceEqual(after[lane])),
                "Both actual pickup orders leave every observed R lane unchanged");
        }
        var shop = initial["Shop"].Take(3).ToArray();
        Check(cards.Length == 3 && shop.Length == 3, "Three real transformations and shop relics");
        Console.WriteLine("FIXED_WITNESS seed=" + seed + ";cards=" + string.Join(',', cards) + ";shop=" + string.Join(',', shop));
        var positive = query with { TransformationAggregate = query.TransformationAggregate! with { TargetMultiset = cards },
            RelicShopSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Unordered, shop.Select(k => (ModelKey?)k).ToArray())] };
        bool Expected(SearchQuery q, string candidate)
        {
            ulong hash = profile.ComputeRootSeed(candidate);
            if (!ModernNeowIdentityPredictor.PredictModernCore(hash, context.Authority, profile, true).RelicKeys.Contains(bones)) return false;
            var p = new NeowEffectProjectionEngine(profile, "T.R.Oracle", hash, context.Authority).Project(bones);
            if (p.BonesOutcome is not { } b || !b.OfferedRelics.Contains(leafy) || !b.OfferedRelics.Contains(leaf)) return false;
            var c = q.TransformationAggregate!;
            var lanes = Bags(hash, [bones, leafy, leaf]);
            return b.OriginalRoutes.Any(r =>
            {
                if (c.PickupOrder != TransformationPickupOrder.Any && r.AcquisitionOrder[0] != (c.LeafyFirst ? leafy : leaf)) return false;
                var actual = Cards(r.RelicScopedResults.SelectMany(s => s.EffectGroups)).ToList();
                foreach (var target in c.TargetMultiset) if (!actual.Remove(target)) return false;
                if (c.RequiresRareRemainder && actual.Any(k => context.Authority.EffectAuthority!.TransformPool!.First(x => x.CardKey == k).Rarity != EffectCardRarity.Rare)) return false;
                foreach (var s in q.RelicShopSequenceConditions)
                {
                    var range = lanes["Shop"].Take(s.Count).ToList();
                    if (s.OrderMode == CombatRewardSequenceOrderMode.Ordered)
                    { for (int i = 0; i < s.Count; i++) if (s.Slots[i] is { } target && range[i] != target) return false; }
                    else foreach (var target in s.Slots.OfType<ModelKey>()) if (!range.Remove(target)) return false;
                }
                return true;
            });
        }
        var positivePlan = Compile(positive);
        var positiveExact = ProductionExactSearchEvaluator.Evaluate(positivePlan, root);
        Check(positiveExact.IsMatch, "Real T/R positive: " + positiveExact.FailureCode);
        foreach (var order in Enum.GetValues<TransformationPickupOrder>().Take(3))
        foreach (int count in new[] { 1, 2, 3 })
        foreach (var shopOrder in Enum.GetValues<CombatRewardSequenceOrderMode>())
        {
            ModelKey[] pair = order == TransformationPickupOrder.NewLeafThenLeafy ? [leaf, leafy] : [leafy, leaf];
            var q = positive with { OpeningRouteRelicRequirement = new(bones, pair, order == TransformationPickupOrder.Any ? BonesRouteOrderMode.AnyOrder : BonesRouteOrderMode.ExactOrder),
                TransformationAggregate = positive.TransformationAggregate! with { PickupOrder = order, TargetMultiset = cards.Take(count).Reverse().ToArray() },
                RelicShopSequenceConditions = [new(3, shopOrder, (shopOrder == CombatRewardSequenceOrderMode.Ordered ? shop : Enumerable.Reverse(shop)).Select(k => (ModelKey?)k).ToArray())] };
            var plan = Compile(q);
            var exact = ProductionExactSearchEvaluator.Evaluate(plan, root);
            Check(Expected(q, seed) && exact.IsMatch, "Order/partial target/slot order: " + exact.FailureCode);
            Check(RelicFamilyPlanCompiler.TryCompile(plan, out var rPlan, out _) && RelicShopCpuPlan.TryCompile(rPlan!)!.Matches(root.RootHash) &&
                new TransformationAggregateNumericalPlan(plan).Matches(root.RootHash), "Necessary CPU T/R filters retain positive");
            Check(exact.Evaluation.Witnesses.All(w => w.MatchedConditions.Contains("relic-shop-sequence-0") &&
                w.MatchedConditions.Contains("transformation-aggregate") && w.MatchedConditions.Contains("fixed-bones-relic-continuation") &&
                (order == TransformationPickupOrder.Any || w.AcquisitionOrder.SequenceEqual(pair))), "T and R are attached to the same allowed N witness");
            var restored = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(q, ProductionSearchReplay.JsonOptions), ProductionSearchReplay.JsonOptions)!;
            Check(Compile(restored).SnapshotFingerprint == plan.SnapshotFingerprint, "Player-authored targets and order survive serialization");
        }
        var absentRelic = initial["Shop"].First(k => !shop.Contains(k));
        foreach (var shopOrder in Enum.GetValues<CombatRewardSequenceOrderMode>())
            Check(ProductionExactSearchEvaluator.Evaluate(Compile(positive with { RelicShopSequenceConditions = [new(3, shopOrder, [null, shop[1], null])] }), root).IsMatch,
                "Partially specified shop slots retain wildcard semantics");
        var absentCard = context.Authority.EffectAuthority!.TransformPool!.First(c => !cards.Contains(c.CardKey)).CardKey;
        var negatives = new[] {
            positive with { RelicShopSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Unordered, [absentRelic, null, null])] },
            positive with { RelicShopSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Ordered, Enumerable.Reverse(shop).Select(k => (ModelKey?)k).ToArray())] },
            positive with { TransformationAggregate = positive.TransformationAggregate! with { TargetMultiset = [absentCard] } },
            positive with { TransformationAggregate = positive.TransformationAggregate! with { TargetMultiset = [cards[0], cards[0], cards[0]] } },
            positive with { RelicShopSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Unordered, [shop[0], shop[0], shop[0]])] }
        };
        foreach (var q in negatives.Concat(new[] { query, positive with { TransformationAggregate = positive.TransformationAggregate! with {
            Predicate = TransformationAggregatePredicate.ContainsMultisetAndRemainingRare } } }))
        foreach (string candidate in new[] { seed, "000000000000", "000000000001", "0000000001FB" })
        {
            var exact = ProductionExactSearchEvaluator.Evaluate(Compile(q), TrustedRootHashInput.FromCanonicalSeed(profile, candidate));
            Check(exact.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch && exact.IsMatch == Expected(q, candidate),
                "Independent opening/bag replay at " + candidate + ":" + exact.FailureCode);
        }
        // Additional N targets remain conjunctive with T and R, including a false N target.
        var newLeafCard = Cards(routes[0].RelicScopedResults.Single(r => r.SourceRelicKey == leaf).EffectGroups).Single();
        foreach (var target in new[] { newLeafCard, absentCard })
        {
            var q = positive with { StructuredOpeningEffects = [new(leaf, NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card, [target])] };
            Check(ProductionExactSearchEvaluator.Evaluate(Compile(q), root).IsMatch == (target == newLeafCard), "Additional N shares the opening");
        }
        var witnesses = positiveExact.Evaluation.Witnesses;
        var aggregateEvidence = positiveExact.Evaluation.Evidence.Where(e => e.Code == "TransformationAggregateMatched").ToArray();
        Check(witnesses.Count == 2 && aggregateEvidence.Length == 2, "Any order retains both legal witnesses");
        var nFirst = SearchQueryEvaluation.Match([], [witnesses[0].OpeningRouteId], [witnesses[0]]);
        var tOther = SearchQueryEvaluation.Match(aggregateEvidence.Where(e => !e.AcquisitionOrder!.SequenceEqual(witnesses[0].AcquisitionOrder)).ToArray());
        Check(ProductionExactSearchEvaluator.MergeCanonicalGlobalEvidence(nFirst, tOther).Disposition == SearchDisposition.NoMatch,
            "Individually true N/R and T on opposite pickup witnesses cannot make a Match");
        var joined = ProductionExactSearchEvaluator.MergeCanonicalGlobalEvidence(nFirst, SearchQueryEvaluation.Match(aggregateEvidence));
        Check(joined.Witnesses.Count == 1 && joined.Evidence.Count(e => e.Code == "TransformationAggregateMatched") == 1,
            "Discarded T alternatives are not reported as accepted route evidence");

        // Ordinary R observations share the same unchanged bags; don't add a new replay.
        foreach (var lane in new[] { RelicSequenceKind.Common, RelicSequenceKind.Uncommon, RelicSequenceKind.Rare, RelicSequenceKind.Shop })
        {
            var q = positive with { RelicSequenceConstraints = [new(lane, SearchSequenceRangeMode.ExactSlot, 1, new([initial[lane.ToString()][0]], [], []))] };
            Check(ProductionExactSearchEvaluator.Evaluate(Compile(q), root).IsMatch, "Ordinary lane continuation " + lane);
            Check(ProductionExactSearchEvaluator.Evaluate(Compile(q with { TransformationAggregate = null }), root).IsMatch, "Existing ordinary N/R unchanged " + lane);
        }
        // Restore the report's additional A/W facts one at a time. These fixtures
        // reproduce typed intent; the original user log/draft is not available here.
        var ancient = new AncientSearchBranchCondition(2, new("EVENT", "OROBAS"), [new("RELIC", "DRIFTWOOD")], []);
        var crystal = new EventSequenceSearchCondition(1, null, SearchSequenceRangeMode.ExactSlot, 1, new([new("EVENT", "CRYSTAL_SPHERE")], [], []));
        foreach (var q in new[] { positive with { AncientBranches = [ancient] }, positive with { EventSequenceConstraints = [crystal] },
            positive with { AncientBranches = [ancient], EventSequenceConstraints = [crystal] },
            query with { AncientBranches = [ancient], EventSequenceConstraints = [crystal] } })
        {
            var plan = Compile(q);
            _ = FamilyExecutionCoordinator.Plan(plan, gpuAvailable: false);
            var exact = ProductionExactSearchEvaluator.Evaluate(plan, root);
            var withoutT = ProductionExactSearchEvaluator.Evaluate(Compile(q with { TransformationAggregate = null }), root);
            Check(exact.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch &&
                exact.IsMatch == (Expected(q, seed) && withoutT.IsMatch), "A/W are real independent predicates, not admission blockers: " + exact.FailureCode);
        }
        var combatProbe = Compile(positive with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered,
            [context.Authority.EffectAuthority.CharacterRewardPool![0].CardKey]) });
        var combatDocument = ProductionExactSearchEvaluator.Evaluate(combatProbe, root).Document;
        var reward = combatDocument!.Sections.First(s => s.NormalCombatRewardSequencePrediction is not null).NormalCombatRewardSequencePrediction!;
        var combatTarget = reward.Routes[0].Battles[0].Cards[0].CardKey;
        foreach (bool withR in new[] { false, true })
        {
            var q = positive with { RelicShopSequenceConditions = withR ? positive.RelicShopSequenceConditions : [],
                CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [combatTarget]) };
            var plan = Compile(q);
            Check(ProductionExactSearchEvaluator.Evaluate(plan, root).IsMatch && new CombatRewardReplay(plan).Matches(root.RootHash), "Existing T+C and joint T+C+R");
        }
        foreach (var q in new[] { positive, query })
        {
            var plan = Compile(q);
            var session = FamilyExecutionCoordinator.Start(plan, gpuAvailable: false);
            try
            {
                Check(session.Completion.Wait(TimeSpan.FromSeconds(20)) && session.GetProgress().State == SearchRunState.Completed,
                    "One-candidate CPU session completes successfully, not merely with an empty faulted result");
                var results = new List<SearchCandidate>();
                while (session.TryReadCandidate(out var candidate)) results.Add(candidate!);
                Check(results.Count == (Expected(q, seed) ? 1 : 0) && results.All(c => c.Seed == seed && !c.IsUnverified), "Production CPU publishes only expected Exact results");
            }
            finally { session.Cancel(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        var unsupported = query with { OpeningRoute = new(leaf), OpeningRouteRelicRequirement = null,
            TransformationAggregate = query.TransformationAggregate! with { Opening = TransformationOpening.NewLeaf, TargetMultiset = [cards[0]] } };
        foreach (var q in new[] { unsupported, unsupported with { RelicShopSequenceConditions = [], CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [combatTarget]) },
            unsupported with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [combatTarget]) } })
        {
            var unresolved = SearchCompiler.Compile(q, context);
            string issue = TransformationAggregateCondition.HasUnresolvedRelicContinuation(q)
                ? TransformationAggregateCondition.HasUnresolvedRewardContinuation(q) ? "TransformationAggregate.NeowDependentRewardRelicContinuationNotSupportedInV1" : "TransformationAggregate.NeowDependentRelicContinuationNotSupported"
                : "TransformationAggregate.NeowDependentCombatRewardContinuationNotSupported";
            string expectedKey = q.RelicShopSequenceConditions.Count == 0 ? "integration.transform.combat_continuation_conflict" :
                q.HasCombatRewardConstraints ? "integration.transform.continuation_conflict" : "integration.transform.relic_continuation_conflict";
            Check(QueryWorkbenchFrame.TransformationContinuationIssueKey(issue) == expectedKey && unresolved.NormalizedQuery.TransformationAggregate is not null,
                "Unresolved continuation preserves the condition and its specific cause");
            foreach (string language in new[] { "zh", "en" })
                Check(JsonUiTextProvider.CreateUi13(language).Get(expectedKey) != expectedKey, "Localized continuation reason " + language);
        }
        using var eventFixture = JsonDocument.Parse(File.ReadAllText("docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json"));
        var eventTargets = eventFixture.RootElement.GetProperty("Evidence")[0].GetProperty("Targets").Deserialize<RolltheSpire2.Core.Events.MorphicGroveTarget[]>(ProductionSearchReplay.JsonOptions)!
            .Select((t, i) => t with { InstanceId = "basic:" + i }).ToArray();
        var eventScenario = new MorphicGroveScenario(context.Authority, new("Fixed initial-Basic event fixture; occurrence assumed", true, false, null), eventTargets);
        foreach (var q in new[] {
            positive with { TransformationAggregate = positive.TransformationAggregate! with { MorphicGrove = true, EventScenario = eventScenario } },
            positive with { TransformationAggregate = positive.TransformationAggregate! with { Predicate = TransformationAggregatePredicate.RareCountAtLeast, MinimumRareCount = 1, TargetMultiset = [] } },
            positive with { OpeningRoute = null, OpeningRouteRelicRequirement = null },
            positive with { OpeningRouteRelicRequirement = new(bones, [leafy, BaseGameModelKeys.Relics.SmallCapsule]),
                TransformationAggregate = positive.TransformationAggregate! with { Opening = TransformationOpening.BonesLeafyOther,
                    CompanionRelic = BaseGameModelKeys.Relics.SmallCapsule, TargetMultiset = [cards[0]] } }
        })
        {
            var unresolved = SearchCompiler.Compile(q, context);
            Check(TransformationAggregateCondition.HasUnresolvedRelicContinuation(unresolved.NormalizedQuery),
                "Event contributors, pure rare-count, unbound opening and other companions retain unresolved R obligations");
        }
        ProductionSearchReplay.Write(".build-evidence/transformation-relic/reported-workload.json", new ProductionSearchReplayWorkload(1, query, options));
        ProductionSearchReplay.Write(".build-evidence/transformation-relic/positive-workload.json", new ProductionSearchReplayWorkload(1, positive, options));
        Console.WriteLine($"TRANSFORM_RELIC PASS checks={checks};fixture=CapturedIroncladA10+ReconstructedTypedReport;originalUserFiles=false;cpuSessionRoots=2;gpuChanged=false");
        return 0;
    }
}
