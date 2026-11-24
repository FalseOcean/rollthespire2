using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Persistence;
using Output = RolltheSpire2.Search.Runtime.TransformationAggregateValidator.Output;
using Predicate = RolltheSpire2.Search.Semantics.TransformationAggregatePredicate;

internal static class TransformationScopeChecks
{
    private const string Leafy = "N.LeafyPoultice";
    private const string Astrolabe = "A.ASTROLABE";
    private const string Pandora = "A.PANDORAS_BOX";
    private static readonly ModelKey Rare = new("CARD", "OFFERING");
    private static readonly ModelKey OtherRare = new("CARD", "FEED");
    private static readonly ModelKey Common = new("CARD", "ANGER");

    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string issue)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("TransformationScope: " + issue);
        }

        var baseline = new TransformationAggregateCondition(TransformationOpening.LeafyPoultice,
            TransformationPickupOrder.Any, false, false, false, Predicate.ContainsMultiset, 0, [OtherRare]);
        var combined = baseline with
        {
            AncientSource = TransformationAncientSource.Astrolabe,
            AncientAct = 2,
            ResultRequirements =
            [
                new([Leafy], Predicate.ContainsMultiset, 0, [Rare]),
                new([Leafy], Predicate.RareCountAtLeast, 1, []),
                new([Leafy, Astrolabe], Predicate.ContainsMultisetAndRareCount, 2, [Rare, Rare])
            ]
        };
        Output[] result =
        [
            new(Rare, true, Leafy), new(Common, false, Leafy),
            new(Rare, true, Astrolabe), new(Common, false, Astrolabe), new(Common, false, Astrolabe)
        ];
        SearchDisposition Evaluate(TransformationAggregateCondition condition, Output[] outputs) =>
            TransformationAggregateValidator.EvaluateOutputs(condition.Normalize(), outputs);
        Check(Evaluate(combined, result) == SearchDisposition.Match,
            "groups reuse actual cards and named rare cards count toward the scoped rare threshold");
        var wronglyScoped = combined with { ResultRequirements = [new([Leafy], Predicate.ContainsMultiset, 0, [Rare, Rare])] };
        Check(Evaluate(wronglyScoped, result) == SearchDisposition.NoMatch,
            "a duplicate outside the scoped source cannot supply the second named instance");
        var pending = result.ToArray();
        pending[0] = new(null, null, Leafy);
        Check(Evaluate(combined, pending) == SearchDisposition.Unknown, "missing in-scope card remains unresolved");
        Check(Evaluate(wronglyScoped, pending) == SearchDisposition.NoMatch,
            "even the unknown in-scope card cannot fill both named instances");

        var requiredTogether = combined with
        {
            ResultRequirements =
            [
                new([Leafy], Predicate.ContainsMultiset, 0, [Rare]),
                new([Astrolabe], Predicate.ContainsMultiset, 0, [OtherRare])
            ]
        };
        var otherPickupOrder = result.ToArray();
        otherPickupOrder[0] = new(Common, false, Leafy);
        otherPickupOrder[2] = new(OtherRare, true, Astrolabe);
        Check(Evaluate(requiredTogether with { ResultRequirements = [requiredTogether.ResultRequirements[0]] }, result) == SearchDisposition.Match &&
            Evaluate(requiredTogether with { ResultRequirements = [requiredTogether.ResultRequirements[1]] }, otherPickupOrder) == SearchDisposition.Match,
            "each condition has an individual witness in a different outcome");
        Check(Evaluate(requiredTogether, result) == SearchDisposition.NoMatch &&
            Evaluate(requiredTogether, otherPickupOrder) == SearchDisposition.NoMatch,
            "every group must hold in the same outcome");

        var large = new TransformationAggregateCondition(TransformationOpening.None, TransformationPickupOrder.Any,
            false, false, false, Predicate.RareCountAtLeast, 0, [])
        {
            AncientSource = TransformationAncientSource.PandorasBox,
            AncientAct = 3,
            PandoraAutomaticCount = 8,
            PandoraCountOverride = 40,
            ResultRequirements = [new([Pandora], Predicate.ContainsMultisetAndRemainingRare, 0,
                Enumerable.Repeat(Common, 36).ToArray())]
        };
        Output[] largeResults = [.. Enumerable.Repeat(new Output(Common, false, Pandora), 36), .. Enumerable.Repeat(new Output(Rare, true, Pandora), 4)];
        Check(Evaluate(large, largeResults) == SearchDisposition.Match, "above-auto override supports more than 31 named targets");
        largeResults[^1] = new(OtherRare, false, Pandora);
        Check(Evaluate(large, largeResults) == SearchDisposition.NoMatch, "unrequested non-rare remainder invalidates that witness");
        largeResults[^1] = new(null, null, Pandora);
        Check(Evaluate(large, largeResults) == SearchDisposition.Unknown, "unknown remainder stays unresolved");

        var oldRemainder = baseline with
        {
            Opening = TransformationOpening.BonesLeafyNewLeaf,
            Predicate = Predicate.ContainsMultisetAndRemainingRare
        };
        CheckRemainderOracle(oldRemainder, 1, Check);
        CheckRemainderOracle(large with
        {
            PandoraCountOverride = 4,
            Predicate = Predicate.ContainsMultisetAndRemainingRare,
            ResultRequirements = []
        }, 4, Check);

        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 10, 0, 1, "0.113.1");
        var context = new SearchContext(RuntimeProfileId.Beta111, authority.Character.CharacterKey, 10, authority,
            new("0.113.1", "0.113.1", "beta", "transformation-scope-fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
        SearchQuery Query(TransformationAggregateCondition condition) => SearchQuery.Empty with { TransformationAggregate = condition };
        CompiledSearch Compile(TransformationAggregateCondition condition) => SearchCompiler.Compile(Query(condition), context);
        var starter = new MorphicGroveCard(new("CARD", "STRIKE_IRONCLAD"), "IRONCLAD", "Attack", "Basic", 0, 1, true, true, null);
        CheckScopedCountStreams(context, starter, Check);
        var offered = starter with { CardKey = Common, Rarity = "Common" };
        TransformationAggregateCondition MixedEvents(bool closedPool, params string[] sources) =>
            large with
            {
                Symbiote = true,
                EventScenario = new MorphicGroveScenario(authority, new("test", closedPool, false, null),
                    [new("basic:0", starter, [offered])]),
                ResultRequirements = [new(sources, Predicate.ContainsMultiset, 0, [Rare])]
            };
        Func<ulong, List<Output>?>? PrepareEventCheck(TransformationAggregateCondition c)
        {
            var compiled = Compile(c);
            var model = new SearchModel(compiled, CompiledSearchEvaluationProjector.Project(compiled).Evaluation,
                CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint);
            return TransformationAggregateValidator.PrepareIndependentEventCheck(model);
        }
        var knownMiss = PrepareEventCheck(MixedEvents(true, "E.SYMBIOTE"));
        var unknownEvent = PrepareEventCheck(MixedEvents(false, "E.SYMBIOTE"));
        Check(knownMiss is not null && knownMiss(1) is null, "an independent known event miss rejects every Ancient witness");
        Check(unknownEvent is not null && unknownEvent(1) is [{ Card: null, Rare: null }],
            "an unresolved event remains available for the full mixed replay");
        Check(PrepareEventCheck(MixedEvents(true, "E.SYMBIOTE", Pandora)) is null,
            "a jointly scoped card may still come from the Ancient");


        var json = SearchPresetStore.CreateJsonOptions();
        var original = Compile(combined);
        void Reject(TransformationResultRequirement requirement, string issue)
        {
            bool rejected = false;
            try { _ = Compile(combined with { ResultRequirements = [requirement] }); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, issue);
        }
        Reject(new([Leafy, Leafy], Predicate.RareCountAtLeast, 3, []), "the group's capacity never counts a source twice");
        Reject(new([Pandora], Predicate.RareCountAtLeast, 1, []), "unselected source references are rejected instead of dropped");
        Reject(new([], Predicate.RareCountAtLeast, 1, []), "empty group scope is rejected instead of widened to all sources");
        var restored = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(Query(combined), json), json)!;
        var recompiled = SearchCompiler.Compile(restored, context);
        Check(restored.TransformationAggregate!.ResultRequirements.Count == 3 &&
            restored.TransformationAggregate.ResultRequirements[2].TargetMultiset.SequenceEqual([Rare, Rare]),
            "scope requirements round trip with duplicate targets");
        Check(original.SemanticFingerprint == recompiled.SemanticFingerprint &&
            SearchContinuationIdentity.For(original) == SearchContinuationIdentity.For(recompiled),
            "semantic and continuation identity survive source-scope serialization");
        var reordered = Compile(combined with
        {
            ResultRequirements = [combined.ResultRequirements[0], combined.ResultRequirements[1],
                combined.ResultRequirements[2] with { Sources = [Astrolabe, Leafy, Leafy] }]
        });
        Check(original.SemanticFingerprint == reordered.SemanticFingerprint &&
            SearchContinuationIdentity.For(original) == SearchContinuationIdentity.For(reordered),
            "scope membership has canonical order and cannot count the same source twice");
        var edited = Compile(combined with
        {
            ResultRequirements = [combined.ResultRequirements[0] with { Sources = [Astrolabe] },
                .. combined.ResultRequirements.Skip(1)]
        });
        Check(original.SemanticFingerprint != edited.SemanticFingerprint &&
            SearchContinuationIdentity.For(original) != SearchContinuationIdentity.For(edited),
            "changing only a requirement's scope changes both identities");
        var beforeOverride = Compile(large);
        var overrideEdited = Compile(large with { PandoraCountOverride = 41 });
        Check(beforeOverride.SemanticFingerprint != overrideEdited.SemanticFingerprint &&
            SearchContinuationIdentity.For(beforeOverride) != SearchContinuationIdentity.For(overrideEdited),
            "the effective Pandora count participates in both identities");
        var automaticBox = large with
        {
            PandoraCountOverride = null,
            ResultRequirements = [new([Pandora], Predicate.ContainsMultiset, 0, [Common])]
        };
        var eightRolls = Compile(automaticBox);
        var sevenRolls = Compile(automaticBox with { PandoraAutomaticCount = 7 });
        Check(eightRolls.SemanticFingerprint != sevenRolls.SemanticFingerprint &&
            SearchContinuationIdentity.For(eightRolls) != SearchContinuationIdentity.For(sevenRolls),
            "automatic Pandora counts are distinct search and continuation premises");

        ModelKey astrolabeKey = new("RELIC", "ASTROLABE");
        ModelKey pandoraKey = new("RELIC", "PANDORAS_BOX");
        ModelKey darv = new("EVENT", "DARV");
        ModelKey alternateAncient = new("EVENT", "TEZCATARA");
        var ancientQuery = Query(combined) with
        {
            AncientBranches = [new(2, darv, [pandoraKey], []), new(2, alternateAncient, [], [])]
        };
        var projection = CompiledSearchEvaluationProjector.Project(SearchCompiler.Compile(ancientQuery, context)).Evaluation;
        Check(projection.RequiresWorldDomain && projection.AncientOptionFilters.Any(filter =>
                filter.Act == 2 && filter.Keys.All.SequenceEqual([astrolabeKey]) && filter.Keys.Any.Count == 0),
            "the selected Ancient item is a mandatory offer in the selected act");
        Check(projection.AncientBranchConditions.Count == 2 && projection.AncientBranchConditions.Any(branch =>
            branch.Act == 2 && branch.AncientKey == darv && branch.OptionAny.SequenceEqual([pandoraKey])),
            "the mandatory offer leaves independently authored Ancient OR branches intact");

        var capsule = SearchCompiler.Compile(Query(large) with { OpeningRoute = new(BaseGameModelKeys.Relics.LargeCapsule) }, context);
        var capsuleProjection = CompiledSearchEvaluationProjector.Project(capsule).Evaluation;
        Check(capsuleProjection.RequiresEffectColdPath && !CompiledSearchEvaluationProjector.Project(beforeOverride).Evaluation.RequiresEffectColdPath,
            "an authored capsule opening requires effect continuation for otherwise identical Pandora goals");
        var capsuleModel = new SearchModel(capsule, capsuleProjection, CombatRewardRoutePolicyContract.None, capsule.SemanticFingerprint);
        var run = new PreparedSearch(capsuleModel, new("000000000000", 1 << 24, 1, 16),
            "000000000000", 1 << 24, capsule.SemanticFingerprint);
        var slowCpu = new FilterBodyDescription(new("Test.Cpu"), "test", RuntimeProfileId.Beta111)
            { WindowCapacity = 65536, PreferredRootWindowSize = 65536 };
        var gpuBody = new FilterBodyDescription(new("Test.Gpu"), "test", RuntimeProfileId.Beta111) { UsesGpu = true };
        Check(FamilyExecutionCoordinator.ResolveExecutionWindowSize(run, [slowCpu, gpuBody]) == 1 << 24,
            "an ordinary CPU private chunk must not underfill an established GPU root batch");
        Check(FamilyExecutionCoordinator.ResolveExecutionWindowSize(run,
                [slowCpu with { MaximumRootWindowSize = 1024 }, gpuBody]) == 1024 &&
            FamilyExecutionCoordinator.ResolveExecutionWindowSize(run,
                [gpuBody, slowCpu with { MaximumRootWindowSize = 1024 }]) == 1024,
            "expensive replay's explicit public bound survives GPU mixing and manual order");
        var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(run, gpuAvailable: false);
        Check(registered.Last().FamilyId == TransformationAggregateFamily.Id &&
            registered.Last().MaximumRootWindowSize is > 0 and <= 65536,
            "scoped replay registers after existing rejection and declares a bounded completed batch");
        var scopedCpu = registered.Last();
        Check(FamilyExecutionCoordinator.ResolveExecutionWindowSize(run, [scopedCpu, gpuBody]) == 1024 &&
            FamilyExecutionCoordinator.ResolveExecutionWindowSize(run, [gpuBody, scopedCpu]) == 65536 &&
            scopedCpu.WindowCapacity == 1024,
            "scoped replay keeps a bounded dense entry and partitions survivors inside a larger GPU-led batch");
        Check(SearchAuthorityProjector.TryProjectForSeed(capsuleModel, "000000000000", out var capsuleAuthority, out _, out _) &&
            capsuleAuthority.EffectAuthority is { HasExactRelicBag: true, CaptureDiagnosticCode: "SearchPerSeedRelicBagProjected" },
            "an unowned capsule opening still replays its per-seed nested relic bag for Pandora");

        var sharedOpeningQuery = Query(combined) with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice) };
        var quickView = SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(sharedOpeningQuery, context));
        Check(quickView.Rows.Any(row => row.Kind == SearchProbabilityRowKind.Neow) &&
            quickView.Rows.Single(row => row.Kind == SearchProbabilityRowKind.TransformationAggregate).Probability is null,
            "unpriced scoped transformation keeps the opening row and has no legacy marginal");

        var legacy = JsonSerializer.SerializeToNode(Query(baseline), json)!;
        var legacyCondition = legacy[nameof(SearchQuery.TransformationAggregate)]!.AsObject();
        foreach (string name in new[] { "AncientSource", "AncientAct", "PandoraCountOverride", "PandoraAutomaticCount", "ResultRequirements",
            "UsesAncient", "UsesExtendedRequirements", "AncientOpportunityCount" })
            legacyCondition.Remove(name);
        var legacyQuery = legacy.Deserialize<SearchQuery>(json)!;
        Check(legacyQuery.TransformationAggregate is { UsesExtendedRequirements: false, ResultRequirements.Count: 0 } &&
            SearchContinuationIdentity.For(Compile(baseline)) == SearchContinuationIdentity.For(SearchCompiler.Compile(legacyQuery, context)),
            "legacy single-predicate JSON loads and retains its continuation identity");

        Console.WriteLine($"TransformationScopeChecks passed: {checks}");
        return 0;
    }

    private static void CheckScopedCountStreams(SearchContext context, MorphicGroveCard starter, Action<bool, string> check)
    {
        var effects = context.Authority.EffectAuthority!;
        var pool = effects.TransformPool!.Where(card => card.PoolId == "character").Take(2).ToArray();
        var target = pool[0].CardKey;
        var other = pool[1].CardKey;
        var authority = context.Authority.WithEffectAuthority(effects with { TransformPool = pool });
        var compiled = SearchCompiler.Compile(SearchQuery.Empty, context with { Authority = authority });
        ScopedTransformationCountPlan Bind(TransformationAggregateCondition condition) =>
            ScopedTransformationCountPlan.TryCreate(new(compiled,
                ExactSearchEvaluationProjection.Empty with { TransformationAggregate = condition.Normalize() },
                CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint)) ??
            throw new InvalidOperationException("Synthetic scoped transformation count must bind");

        var pandora = new TransformationAggregateCondition(TransformationOpening.NewLeaf, TransformationPickupOrder.Any,
            false, false, false, Predicate.ContainsMultiset, 0, [target])
        {
            AncientSource = TransformationAncientSource.PandorasBox, AncientAct = 2, PandoraCountOverride = 1,
            ResultRequirements = [new([Pandora], Predicate.ContainsMultiset, 0, [target])]
        };
        var afterNewLeaf = Bind(pandora);
        var cursorMeta = afterNewLeaf.PackGpu()[0];
        check(cursorMeta[2] == 1 && cursorMeta[5] == 2 && cursorMeta[8] == 1 && cursorMeta[9] == 0 &&
            cursorMeta[12] == 1 && cursorMeta[13] == 1,
            "the personal stream packs one uncounted New Leaf draw before the counted Pandora draw");

        var grovePool = new[] { starter with { CardKey = target, Rarity = "Common" }, starter with { CardKey = other, Rarity = "Common" } };
        var jointCondition = pandora with
        {
            Opening = TransformationOpening.None, MorphicGrove = true,
            EventScenario = new(authority, new("synthetic Grove", true, true, 4),
                [new("basic:0", starter, grovePool), new("basic:1", starter, grovePool)]),
            ResultRequirements = [new(["E.MORPHIC_GROVE", Pandora], Predicate.ContainsMultiset, 0, [target, target, target])]
        };
        var joint = Bind(jointCondition);
        check(joint.PackGpu()[0][2] == 2, "Grove and Pandora pack separate streams");

        check(joint.EstimateWork() is { Survival: 0.125 }, "three fair draws must supply all three targets from the two streams");

        var star = jointCondition with
        {
            Opening = TransformationOpening.NewLeaf, AncientSource = TransformationAncientSource.Astrolabe,
            PandoraCountOverride = null,
            ResultRequirements = [new(["N.NewLeaf", Astrolabe, "E.MORPHIC_GROVE"], Predicate.ContainsMultiset, 0, [target, target, target])]
        };
        var starJoint = Bind(star);
        var starOnly = Bind(star with { ResultRequirements = [new([Astrolabe], Predicate.ContainsMultiset, 0, [target])] });
        check(starJoint.EstimateWork() is { Survival: 0.65625 } && starOnly.EstimateWork() is { Survival: 0.875 },
            "Astrolabe counts three rolls after New Leaf; Grove contributes two independent rolls only when included");

        int aboveDeck = effects.OrderedDeck!.Count + 1;
        var overridePlan = Bind(pandora with
        {
            Opening = TransformationOpening.None, PandoraCountOverride = aboveDeck,
            ResultRequirements = [new([Pandora], Predicate.ContainsMultiset, 0, [target])]
        });
        check(overridePlan.PackGpu()[0][8] == aboveDeck, "the authored Pandora count can exceed the captured deck size");
        int changed = 0, shared = 0;
        for (ulong root = 0; root < 64; root++)
        {
            var personal = new Xoshiro256StarStar(Beta111Profile.Instance.DeriveNamedStreamSeed(root, "transformations"));
            bool first = personal.NextInt(2) == 0;
            bool second = personal.NextInt(2) == 0;
            if (first != second) changed++;
            check(afterNewLeaf.Matches(root) == second, "New Leaf advances Pandora even when only Pandora counts");
            var local = Beta109WorldRng.CreateEventLocal(root, 0, true, Beta111MorphicGroveProjector.EventEntry);
            bool grove = local.NextInt(2, "first committed basic") == 0 && local.NextInt(2, "second committed basic") == 0;
            if (first && grove) shared++;
            check(joint.Matches(root) == (first && grove), "three targets require both Grove draws and the independent Pandora draw");
            bool supplied = first || second;
            for (int i = 2; i < aboveDeck; i++) supplied |= personal.NextInt(2) == 0;
            check(overridePlan.Matches(root) == supplied, "authored Pandora draws execute without a deck-size veto");

            var starRng = new Xoshiro256StarStar(Beta111Profile.Instance.DeriveNamedStreamSeed(root, "transformations"));
            int leafHits = starRng.NextInt(2) == 0 ? 1 : 0;
            int starHits = 0;
            for (int draw = 0; draw < 3; draw++) if (starRng.NextInt(2) == 0) starHits++;
            var groveRng = Beta109WorldRng.CreateEventLocal(root, 0, true, Beta111MorphicGroveProjector.EventEntry);
            int eventHits = (groveRng.NextInt(2, "first committed basic") == 0 ? 1 : 0) +
                (groveRng.NextInt(2, "second committed basic") == 0 ? 1 : 0);
            check(starJoint.Matches(root) == (leafHits + starHits + eventHits >= 3),
                "scoped New Leaf, Astrolabe and Grove count each included source on its own stream");
            check(starOnly.Matches(root) == (starHits > 0), "Astrolabe-only scope skips the New Leaf draw and excludes Grove");
        }
        check(changed > 0 && shared is > 0 and < 64, "fixture distinguishes the opening cursor and exercises joint hits and misses");
    }

    private static void CheckRemainderOracle(TransformationAggregateCondition condition, int minimumTargetCount,
        Action<bool, string> check)
    {
        var sample = (from card in new ModelKey?[] { Common, Rare, null }
            from rarity in new bool?[] { false, true, null }
            select new Output(card, rarity)).ToArray();
        var outputs = new Output[condition.OpportunityCount];
        var targetLists = (from count in Enumerable.Range(minimumTargetCount, outputs.Length - minimumTargetCount + 1)
            from rareCount in Enumerable.Range(0, count + 1)
            select Enumerable.Repeat(Common, count - rareCount).Concat(Enumerable.Repeat(Rare, rareCount)).ToArray()).ToArray();

        void VisitOutputMultisets(int position, int firstSample)
        {
            if (position == outputs.Length)
            {
                foreach (var targets in targetLists)
                {
                    var expected = AssignmentOracle(targets, outputs);
                    var actual = TransformationAggregateValidator.EvaluateOutputs(condition with { TargetMultiset = targets }, outputs);
                    check(actual == expected, $"remaining-rare instance assignment expected {expected}, got {actual} for " +
                        $"[{string.Join(',', targets)}] with [{string.Join(',', outputs)}]");
                }
                return;
            }

            for (int index = firstSample; index < sample.Length; index++)
            {
                outputs[position] = sample[index];
                VisitOutputMultisets(position + 1, index);
            }
        }
        VisitOutputMultisets(0, 0);
    }

    private static SearchDisposition AssignmentOracle(ModelKey[] targets, Output[] outputs)
    {
        // Explore individual assignments. This deliberately does not use counts
        // or share the production matcher's rules for interchangeable copies.
        SearchDisposition Visit(int outputIndex, int filled, bool proven)
        {
            if (outputIndex == outputs.Length)
                return filled != (1 << targets.Length) - 1 ? SearchDisposition.NoMatch :
                    proven ? SearchDisposition.Match : SearchDisposition.Unknown;

            var output = outputs[outputIndex];
            var result = output.Rare == false ? SearchDisposition.NoMatch :
                Visit(outputIndex + 1, filled, proven && output.Rare == true);
            for (int target = 0; target < targets.Length && result != SearchDisposition.Match; target++)
            {
                int slot = 1 << target;
                if ((filled & slot) != 0 || output.Card is { } card && card != targets[target]) continue;
                var candidate = Visit(outputIndex + 1, filled | slot, proven && output.Card.HasValue);
                if (candidate != SearchDisposition.NoMatch) result = candidate;
            }
            return result;
        }

        return Visit(0, 0, true);
    }
}
