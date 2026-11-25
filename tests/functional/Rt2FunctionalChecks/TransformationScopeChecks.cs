using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Pages.Search;
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
        var nextVersion = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 10, 0, 1, "0.114.99");
        var effects = nextVersion.EffectAuthority!;
        var nativePool = effects.SharedRelicPoolSource!.Select(relic => relic with { SourceAssembly = "sts2" }).ToArray();
        var nativeEffects = effects with { SharedRelicPoolSource = nativePool, CharacterRelicPoolSource = [] };
        Check(OpeningTransformationContinuation.HasNativeCapsuleCursor(nextVersion.WithEffectAuthority(nativeEffects)),
            "a newer build with the Modern113 profile inherits the native Capsule cursor rule");
        Check(!OpeningTransformationContinuation.HasNativeCapsuleCursor(nextVersion.WithEffectAuthority(nativeEffects with
            { CharacterRelicPoolSource = [nativePool[0] with { SourceModId = "custom" }] })),
            "an unknown modded pickup in the captured pool can change the Capsule cursor");
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
        var missingPool = context with { Authority = authority.WithEffectAuthority(authority.EffectAuthority! with { TransformPool = null }) };
        var quickView = SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(sharedOpeningQuery, missingPool));
        Check(quickView.Rows.Any(row => row.Kind == SearchProbabilityRowKind.Neow && row.Probability is > 0) &&
            quickView.Rows.Single(row => row.Kind == SearchProbabilityRowKind.TransformationAggregate).Probability is null,
            "a missing replacement pool leaves the shared opening offer probability available");

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
        var characterPool = effects.TransformPool!.Where(card => card.PoolId == "character");
        var pool = new[] { characterPool.First(card => card.Rarity == EffectCardRarity.Common),
            characterPool.First(card => card.Rarity == EffectCardRarity.Rare) };
        var target = pool[0].CardKey;
        var other = pool[1].CardKey;
        var authority = context.Authority.WithEffectAuthority(effects with { TransformPool = pool });
        var compiled = SearchCompiler.Compile(SearchQuery.Empty, context with { Authority = authority });
        ScopedTransformationCountPlan? TryBind(TransformationAggregateCondition condition) =>
            ScopedTransformationCountPlan.TryCreate(new(compiled,
                ExactSearchEvaluationProjection.Empty with { TransformationAggregate = condition.Normalize() },
                CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint));
        ScopedTransformationCountPlan Bind(TransformationAggregateCondition condition) => TryBind(condition) ??
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

        var grovePool = new[] { starter with { CardKey = target, Rarity = "Common" }, starter with { CardKey = other, Rarity = "Rare" } };
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
        var capturedWorld = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context.Authority.WorldAuthority;
        var probabilityContext = context with { Authority = authority.WithWorldAuthority(capturedWorld) };
        CheckScopedProbability(probabilityContext, jointCondition, check);
        CheckBonesOrderProbabilities(probabilityContext, jointCondition, check);

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
        CheckScopedEventStreams(jointCondition, target, TryBind, check);
    }

    private static void CheckScopedProbability(SearchContext context, TransformationAggregateCondition joint,
        Action<bool, string> check)
    {
        SearchProbabilityQuickView View(SearchQuery query) =>
            SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(query, context));
        double? TransformationRow(SearchProbabilityQuickView view) =>
            view.Rows.Single(row => row.Kind == SearchProbabilityRowKind.TransformationAggregate).Probability;

        ModelKey darv = new("EVENT", "DARV");
        ModelKey box = new("RELIC", "PANDORAS_BOX");
        var prerequisites = SearchQuery.Empty with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.NewLeaf),
            AncientBranches = [new(2, darv, [box], [])]
        };
        var query = prerequisites with { TransformationAggregate = joint with { Opening = TransformationOpening.NewLeaf } };
        // New Leaf advances the stream. Only Grove's two draws and Pandora's one
        // contribute to this requirement, and all three must hit the target.
        var explicitView = View(query);
        var explicitMass = View(prerequisites).TotalProbability;
        check(TransformationRow(explicitView) == 0.125 && explicitMass is > 0 and < 1 &&
            explicitView.TotalProbability is { } total && Math.Abs(total / explicitMass.Value - 0.125) < 1e-12,
            $"the quick view counts the combined Grove and Pandora draws once, conditional on New Leaf and Darv offering Pandora (parents {explicitMass:R}, total {explicitView.TotalProbability:R}, rows {string.Join(", ", explicitView.Rows)})");
        var impliedView = View(query with { OpeningRoute = null, AncientBranches = [new(2, darv, [], [])] });
        check(impliedView.TotalProbability is { } implied && explicitView.TotalProbability is { } authored &&
            Math.Abs(implied / authored - 1) < 1e-12 &&
            impliedView.Rows.Single(r => r.Kind == SearchProbabilityRowKind.Neow).Probability ==
            explicitView.Rows.Single(r => r.Kind == SearchProbabilityRowKind.Neow).Probability,
            "T's mandatory opening and menu option have the same mass when authored only on T");

        var partialView = View(query with { FishingRodDrawResearch = true });
        check(partialView.Status == SearchProbabilityQuickViewStatus.Partial && partialView.TotalProbability is null &&
            TransformationRow(partialView) == 0.125,
            "the quick view retains the known conditional T row when another requirement has no probability model");

        var groveOpening = prerequisites with
        {
            AncientBranches = [],
            EventSequenceConstraints = [new(1, null, SearchSequenceRangeMode.FirstN, 3, new([new("EVENT", "MORPHIC_GROVE")], [], []))]
        };
        var openingAndGrove = View(groveOpening).TotalProbability;
        var implicitMenu = View(groveOpening with { TransformationAggregate = query.TransformationAggregate });
        var menuOnly = View(SearchQuery.Empty with { LegacyWorld = SearchQuery.Empty.LegacyWorld with
        {
            AncientOptionFilters = [new(2, new([], [box], []))]
        } }).TotalProbability;
        check(menuOnly is > 0 and < 1 && implicitMenu.Rows.Single(r => r.Kind == SearchProbabilityRowKind.Ancient).Probability is { } menuRow &&
            Math.Abs(menuRow / menuOnly.Value - 1) < 1e-12,
            "an implicit Act 2 Pandora menu without an authored Ancient identity has its own unconditional offer row");
        check(openingAndGrove is > 0 && TransformationRow(implicitMenu) == 0.125 &&
            (implicitMenu.TotalProbability is null || implicitMenu.TotalProbability < openingAndGrove * 0.125),
            $"T still requires the Act 2 Pandora offer when only Grove's first-three position is authored on W (before T {openingAndGrove:R}, total {implicitMenu.TotalProbability:R})");

        var trial = joint with { TrialNondescript = true };
        var trialConflict = View(query with
        {
            TransformationAggregate = trial,
            EventResultConditions = [new(EventResultConditionKind.EventRelicRarityRoll, new("EVENT", "TRIAL"))
            {
                RelicRarityBranch = EventRelicRarityBranch.MerchantGuilty, RelicRaritySelection = EventRelicRaritySelection.First,
                RelicRarity = EffectRelicRarity.Uncommon
            }]
        });
        check(trialConflict.TotalProbability == 0, "the Trial cannot both transform initial cards and find the merchant guilty");

        CheckOverlappingProbabilities(query, explicitMass!.Value, View, check);
    }

    private static void CheckOverlappingProbabilities(SearchQuery query, double parentMass, Func<SearchQuery, SearchProbabilityQuickView> view,
        Action<bool, string> check)
    {
        const string grove = "E.MORPHIC_GROVE";
        var aggregate = query.TransformationAggregate!;
        var pool = aggregate.EventScenario!.Targets[0].OrderedSourceCandidates!;
        var common = pool.Single(card => card.Rarity == "Common").CardKey;
        var rare = pool.Single(card => card.Rarity == "Rare").CardKey;
        string[] both = [grove, Pandora];

        // Enumerate the two Grove draws, one Pandora draw and three possible Trial cases.
        // Each oracle describes the physical cards, independently of the production requirement matcher.
        void Compare(string name, TransformationResultRequirement[] requirements, Func<int, bool, bool> accepts, bool trial = false)
        {
            var outcomes = from first in pool from second in pool from box in pool from trialCase in Enumerable.Range(0, 3)
                           let groveRares = (first.CardKey == rare ? 1 : 0) + (second.CardKey == rare ? 1 : 0)
                           select (!trial || trialCase == 2) && accepts(groveRares, box.CardKey == rare);
            double expected = outcomes.Count(hit => hit) / 24d;
            var result = view(query with { TransformationAggregate = aggregate with
            {
                ResultRequirements = requirements, TrialNondescript = trial
            } });
            var actual = result.Rows.Single(row => row.Kind == SearchProbabilityRowKind.TransformationAggregate).Probability;
            check(actual is { } local && Math.Abs(local - expected) < 1e-12 &&
                result.TotalProbability is { } total && Math.Abs(total / parentMass - expected) < 1e-12,
                $"{name}: 24 equiprobable outcomes give {expected:R}, T row {actual:R}, total {result.TotalProbability:R}");
        }

        Compare("one Grove card satisfies both overlapping target scopes",
            [new([grove], Predicate.ContainsMultiset, 0, [rare]), new(both, Predicate.ContainsMultiset, 0, [rare])],
            (groveRares, _) => groveRares >= 1);
        Compare("a single Pandora card cannot satisfy two distinct identities",
            [new([Pandora], Predicate.ContainsMultiset, 0, [common]), new([Pandora], Predicate.ContainsMultiset, 0, [rare])],
            (_, _) => false);
        Compare("two rare copies can be supplied across sources alongside a Grove common",
            [new([grove], Predicate.ContainsMultiset, 0, [common]), new(both, Predicate.ContainsMultiset, 0, [rare, rare])],
            (groveRares, boxRare) => groveRares == 1 && boxRare);
        Compare("different named targets share a rare-count requirement",
            [new(both, Predicate.ContainsMultisetAndRareCount, 2, [common, rare])],
            (groveRares, boxRare) => groveRares + (boxRare ? 1 : 0) == 2);
        Compare("named cards are assigned before requiring all remaining cards to be rare",
            [new(both, Predicate.ContainsMultisetAndRemainingRare, 0, [common, rare]), new([Pandora], Predicate.RareCountAtLeast, 1, [])],
            (groveRares, boxRare) => groveRares == 1 && boxRare);
        Compare("the Trial case is required once even when only Pandora's card is counted",
            [new([Pandora], Predicate.RareCountAtLeast, 1, []), new([Pandora], Predicate.ContainsMultiset, 0, [rare])],
            (_, boxRare) => boxRare, trial: true);
    }

    private static void CheckBonesOrderProbabilities(SearchContext context, TransformationAggregateCondition joint, Action<bool, string> check)
    {
        const string bones = "N.BonesLeafyNewLeaf";
        var relics = BaseGameModelKeys.Relics.NeowsBones;
        var leafy = BaseGameModelKeys.Relics.LeafyPoultice;
        var newLeaf = BaseGameModelKeys.Relics.NewLeaf;
        var effects = context.Authority.EffectAuthority!;
        var pool = effects.TransformPool!;
        var common = pool.Single(card => card.Rarity == EffectCardRarity.Common);
        var rare = pool.Single(card => card.Rarity == EffectCardRarity.Rare);
        SearchQuery Request(TransformationAggregateCondition condition, ModelKey companion)
        {
            bool reverse = condition.PickupOrder is TransformationPickupOrder.NewLeafThenLeafy or TransformationPickupOrder.CompanionThenLeafy;
            return SearchQuery.Empty with
            {
                OpeningRoute = new(relics),
                OpeningRouteRelicRequirement = new(relics, reverse ? [companion, leafy] : [leafy, companion],
                    condition.PickupOrder == TransformationPickupOrder.Any ? BonesRouteOrderMode.AnyOrder : BonesRouteOrderMode.ExactOrder),
                AncientBranches = condition.UsesAncient ? [new(2, new("EVENT", "DARV"), [condition.RequiredAncientKey], [])] : [],
                TransformationAggregate = condition
            };
        }
        var opening = joint with
        {
            Opening = TransformationOpening.BonesLeafyNewLeaf, MorphicGrove = false, EventScenario = null,
            AncientSource = TransformationAncientSource.None, AncientAct = 0, PandoraCountOverride = null,
            ResultRequirements = [new([bones], Predicate.ContainsMultiset, 0, [rare.CardKey, rare.CardKey])]
        };
        // Tied ordinals retain capture order for Leafy and Pandora. New Leaf
        // resolves the tie by card identity, producing the opposite rank order.
        var orderedPools = effects with
        {
            TransformPool = [rare with { PoolOrder = 0 }, common with { PoolOrder = 0 }],
            SnapshotFingerprint = effects.SnapshotFingerprint + ":tied-transform-ordinals"
        };
        var different = context with { Authority = context.Authority.WithEffectAuthority(orderedPools) };
        foreach (bool samePools in new[] { true, false })
        foreach (bool laterSources in new[] { false, true })
        {
            var selected = samePools ? context : different;
            var condition = laterSources ? joint with
            {
                Opening = opening.Opening,
                ResultRequirements = [.. opening.ResultRequirements,
                    new([bones, "E.MORPHIC_GROVE"], Predicate.RareCountAtLeast, 3, []),
                    new([bones, Pandora], Predicate.ContainsMultiset, 0, [common.CardKey, common.CardKey])]
            } : opening;

            // Encode each word by Leafy's rare outcome. New Leaf maps that same
            // word to the opposite outcome in the tied-ordinal capture.
            // Grove and the subsequent Pandora draw are shared by both witnesses.
            var paths = (from a in new[] { 0, 1 } from b in new[] { 0, 1 } from c in new[] { 0, 1 }
                         from e1 in new[] { 0, 1 } from e2 in new[] { 0, 1 }
                         from box in new[] { 0, 1 }
                         let left = a + b + (samePools ? c : 1 - c)
                         let right = (samePools ? a : 1 - a) + b + c
                         let leftEnough = left >= 2 && (!laterSources || left + e1 + e2 >= 3)
                         let rightEnough = right >= 2 && (!laterSources || right + e1 + e2 >= 3)
                         let leftCommons = !laterSources || 4 - left - box >= 2
                         let rightCommons = !laterSources || 4 - right - box >= 2
                         select (Left: leftEnough && leftCommons, Right: rightEnough && rightCommons,
                             EachRowFindsAnOrder: (leftEnough || rightEnough) && (leftCommons || rightCommons),
                             FourRaresWithGrove: left + e1 + e2 >= 4 || right + e1 + e2 >= 4)).ToArray();
            check(paths.Any(p => p.Left && p.Right) && (samePools ? paths.All(p => p.Left == p.Right) :
                paths.Any(p => p.Left && !p.Right) && paths.Any(p => !p.Left && p.Right)),
                "the shared-word oracle exercises equivalent, distinct and overlapping acquisition witnesses");
            if (!samePools && laterSources)
                check(paths.Any(p => p.EachRowFindsAnOrder && !p.Left && !p.Right),
                    "some roots have an individual witness for each scope but no acquisition order that satisfies all scopes");
            foreach (var order in new[] { TransformationPickupOrder.LeafyThenNewLeaf, TransformationPickupOrder.NewLeafThenLeafy, TransformationPickupOrder.Any })
            {
                double expected = paths.Count(p => order switch
                {
                    TransformationPickupOrder.LeafyThenNewLeaf => p.Left,
                    TransformationPickupOrder.NewLeafThenLeafy => p.Right,
                    _ => p.Left || p.Right
                }) / (double)paths.Length;
                var query = Request(condition with { PickupOrder = order }, newLeaf);
                var compiled = SearchCompiler.Compile(query, selected);
                var preview = !samePools && laterSources && order == TransformationPickupOrder.Any
                    ? WorkbenchProbabilityPreview.Compute(compiled, 1, 1, true).View!
                    : SearchProbabilityPresentationBuilder.Build(compiled);
                var parents = SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(query with { TransformationAggregate = null }, selected));
                var t = preview.Rows.Single(row => row.Kind == SearchProbabilityRowKind.TransformationAggregate).Probability;
                check(t is { } local && Math.Abs(local - expected) < 1e-12 && parents.TotalProbability is > 0 &&
                    preview.TotalProbability is { } total && Math.Abs(total / parents.TotalProbability.Value - expected) < 1e-12,
                    $"Bones {order}, equal pools {samePools}, shared E/A {laterSources}: expected {expected:R}, T {t:R}, parents {parents.TotalProbability:R}, total {preview.TotalProbability:R}");
                if (order != TransformationPickupOrder.Any || laterSources) continue;
                var tOnly = SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(query with
                    { OpeningRoute = null, OpeningRouteRelicRequirement = null }, selected));
                var namedPair = parents.Rows.Single(row => row.Kind == SearchProbabilityRowKind.Neow).Probability;
                check(tOnly.Rows.Single(row => row.Kind == SearchProbabilityRowKind.Neow).Probability is { } implicitPair &&
                    namedPair is > 0 && Math.Abs(implicitPair / namedPair.Value - 1) < 1e-12 &&
                    tOnly.TotalProbability is { } implicitTotal && preview.TotalProbability is { } explicitTotal &&
                    Math.Abs(implicitTotal / explicitTotal - 1) < 1e-12,
                    "selecting the Bones pair only on T pays the same two-relic offer and shows it on the N row");
                foreach (bool grove in new[] { false, true })
                {
                    var legacy = opening with
                    {
                        ResultRequirements = [], Predicate = Predicate.RareCountAtLeast, MinimumRareCount = grove ? 4 : 2,
                        TargetMultiset = [], MorphicGrove = grove, EventScenario = grove ? joint.EventScenario : null
                    };
                    var old = SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(Request(legacy, newLeaf), selected));
                    double legacyExpected = grove ? paths.Count(p => p.FourRaresWithGrove) / (double)paths.Length : expected;
                    check(old.Rows.Single(row => row.Kind == SearchProbabilityRowKind.TransformationAggregate).Probability is { } oldLocal &&
                        Math.Abs(oldLocal - legacyExpected) < 1e-12 && old.TotalProbability is { } oldTotal &&
                        Math.Abs(oldTotal / parents.TotalProbability!.Value - legacyExpected) < 1e-12,
                        $"legacy unrestricted Bones rare-count, with Grove {grove}, equal pools {samePools}: expected {legacyExpected:R}, total {old.TotalProbability:R}");
                }
            }
        }
        var capsule = BaseGameModelKeys.Relics.LargeCapsule;
        var unknown = opening with
        {
            Opening = TransformationOpening.BonesLeafyOther, CompanionRelic = capsule, PickupOrder = TransformationPickupOrder.CompanionThenLeafy,
            ResultRequirements = [new(["N.BonesLeafyOther"], Predicate.ContainsMultiset, 0, [rare.CardKey])]
        };
        var partial = SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(Request(unknown, capsule), context));
        check(partial.TotalProbability is null && partial.Rows.Any(r => r.Kind == SearchProbabilityRowKind.Neow && r.Probability > 0) &&
            partial.Rows.Single(r => r.Kind == SearchProbabilityRowKind.TransformationAggregate).Probability is null,
            "unmodeled Capsule effects before Leafy leave the conditional transform unknown and preserve the Bones offer probability");
        foreach (var (ancient, source, draws) in new[] { (TransformationAncientSource.PandorasBox, Pandora, 1),
                                                         (TransformationAncientSource.Astrolabe, Astrolabe, 3) })
        {
            var ancientOnly = joint with
            {
                Opening = TransformationOpening.None, AncientSource = ancient, MorphicGrove = false, EventScenario = null,
                PandoraCountOverride = ancient == TransformationAncientSource.PandorasBox ? draws : null,
                ResultRequirements = [new([source], Predicate.ContainsMultiset, 0, [rare.CardKey])]
            };
            var query = Request(ancientOnly, newLeaf);
            var parents = SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(query with { TransformationAggregate = null }, different));
            var view = SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(query, different));
            double expected = 1 - Math.Pow(0.5, draws);
            check(view.Rows.Single(row => row.Kind == SearchProbabilityRowKind.TransformationAggregate).Probability == expected &&
                parents.TotalProbability is > 0 && view.TotalProbability is { } total &&
                Math.Abs(total / parents.TotalProbability.Value - expected) < 1e-12,
                $"Bones selected only on N advances the shared stream while T counts just {draws} {source} draws");
        }
    }

    private static void CheckScopedEventStreams(TransformationAggregateCondition joint, ModelKey target,
        Func<TransformationAggregateCondition, ScopedTransformationCountPlan?> tryBind, Action<bool, string> check)
    {
        const string aroma = "E.AROMA_OF_CHAOS", hollow = "E.WHISPERING_HOLLOW", symbiote = "E.SYMBIOTE", trial = "E.TRIAL";
        var scenario = joint.EventScenario!;
        var first = scenario.Targets[0];
        var second = scenario.Targets[1] with { OrderedSourceCandidates = [first.OrderedSourceCandidates![1]] };
        var singles = joint with
        {
            MorphicGrove = false, AromaOfChaos = true, WhisperingHollow = true, Symbiote = true,
            EventScenario = new(scenario.Authority, scenario.Premises, [first, second])
        };
        ScopedTransformationCountPlan Scope(TransformationAggregateCondition source, int minimum, params string[] counted) =>
            tryBind(source with { ResultRequirements = [new(counted, Predicate.ContainsMultiset, 0,
                Enumerable.Repeat(target, minimum).ToArray())] }) ??
            throw new InvalidOperationException("Synthetic event combination must bind");
        var aromaPair = Scope(singles, 2, Pandora, aroma);
        var hollowPair = Scope(singles, 2, Pandora, hollow);
        var symbiotePair = Scope(singles, 2, Pandora, symbiote);
        var aromaWork = aromaPair.EstimateWork()!.Value;
        var hollowWork = hollowPair.EstimateWork()!.Value;
        check(aromaWork.Survival == 0.25 && hollowWork.Survival == 0.25 && symbiotePair.EstimateWork()?.Survival == 0.25,
            "only the chosen one-draw event and Pandora contribute from the two fair pools");
        check(hollowWork.Draws > aromaWork.Draws, "Hollow prices the gold roll ahead of its replacement");

        var withTrial = singles with { TrialNondescript = true, EventScenario = scenario };
        var missingSecondPool = withTrial with
        {
            EventScenario = new(scenario.Authority, scenario.Premises,
                [first, scenario.Targets[1] with { OrderedSourceCandidates = null }]),
            ResultRequirements = [new([trial, Pandora], Predicate.ContainsMultiset, 0, [target])]
        };
        check(tryBind(missingSecondPool) is null, "Trial cards need both ordered pools before the GPU can reject a root");
        var independentGate = Scope(missingSecondPool, 1, Pandora);
        var trialAndSymbiote = Scope(withTrial, 3, Pandora, trial, symbiote);
        var trialGateOnly = Scope(withTrial, 1, Pandora);
        check(Math.Abs(trialAndSymbiote.EstimateWork()!.Value.Survival - 5d / 48) < 1e-12 &&
            Math.Abs(trialGateOnly.EstimateWork()!.Value.Survival - 1d / 6) < 1e-12,
            "the mandatory Trial case applies once, including when no Trial card is counted");

        int earlyHitRejected = 0, jointHit = 0, distinguishHollowPrefix = 0;
        for (ulong root = 0; root < 128; root++)
        {
            var personal = new Xoshiro256StarStar(Beta111Profile.Instance.DeriveNamedStreamSeed(root, "transformations"));
            bool boxHit = personal.NextInt(2) == 0;
            bool aromaHit = Beta109WorldRng.CreateEventLocal(root, 0, false, "AROMA_OF_CHAOS").NextInt(2, "LetGo") == 0;
            bool symbioteHit = Beta109WorldRng.CreateEventLocal(root, 0, false, "SYMBIOTE").NextInt(2, "transform") == 0;
            var hug = Beta109WorldRng.CreateEventLocal(root, 0, false, "WHISPERING_HOLLOW");
            hug.NextInt(19, "CalculateVars");
            bool hollowHit = hug.NextInt(2, "Hug") == 0;
            bool withoutPrefix = Beta109WorldRng.CreateEventLocal(root, 0, false, "WHISPERING_HOLLOW").NextInt(2, "Hug") == 0;
            if (boxHit && withoutPrefix != hollowHit) distinguishHollowPrefix++;
            check(aromaPair.Matches(root) == (boxHit && aromaHit) && hollowPair.Matches(root) == (boxHit && hollowHit) &&
                symbiotePair.Matches(root) == (boxHit && symbioteHit),
                "single-card events use their own seed, prefix and first target even with a different second pool");

            var court = Beta109WorldRng.CreateEventLocal(root, 0, false, "TRIAL");
            bool nondescript = court.NextInt(3, "Accept:case") == 2;
            int trialHits = (court.NextInt(2, "transform:0") == 0 ? 1 : 0) + (court.NextInt(2, "transform:1") == 0 ? 1 : 0);
            bool jointMatch = nondescript && trialHits + (boxHit ? 1 : 0) + (symbioteHit ? 1 : 0) >= 3;
            if (nondescript && trialHits == 1 && boxHit && symbioteHit) jointHit++;
            if (!nondescript && boxHit) earlyHitRejected++;
            check(trialAndSymbiote.Matches(root) == jointMatch && trialGateOnly.Matches(root) == (nondescript && boxHit),
                "an earlier card hit cannot bypass Trial, and excluded event cards cannot satisfy the threshold");
            check(independentGate.Matches(root) == (nondescript && boxHit),
                "an uncounted Trial case gate remains decidable when its second card pool is unknown");
        }
        check(earlyHitRejected > 0 && jointHit > 0 && distinguishHollowPrefix > 0,
            "the sample exercises early hits rejected by Trial, three-source hits and Hollow's consumed prefix");
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
