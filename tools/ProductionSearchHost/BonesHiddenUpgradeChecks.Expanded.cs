using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

internal static partial class BonesHiddenUpgradeChecks
{
    private static readonly ModelKey Small = BaseGameModelKeys.Relics.SmallCapsule;
    private static readonly ModelKey Leaf = BaseGameModelKeys.Relics.NewLeaf;
    private sealed record ExpandedSource(string Seed, IReadOnlyList<BonesAcquisitionRouteResult> Routes,
        BonesAcquisitionRouteResult Route);

    // An expanded mode of the same generator/manifest/comparator. The original
    // 36 cases and their independently computed expected files remain valid.
    internal static void GenerateExpanded(string output, int window, int discoveryLimit)
    {
        if (window is < 67 or > 1048576 || discoveryLimit is < 262144 or > 67108864)
            throw new ArgumentOutOfRangeException(nameof(discoveryLimit));
        string Corpus(string relative) => ProjectSettings.GlobalizePath("res://Corpus/" + relative);
        var capture = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Corpus("n-direct-v1/context-defect-a10.json"));
        var template = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Corpus("bones-k-sameroute-v1/counterexample.json"));
        var options = template.Options with { StartSeed = Format(0), ScanCount = window, WorkerCount = 1, TargetMatchCount = int.MaxValue };
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var neutral = BaseGameModelKeys.Relics.GoldenPearl;
        var mutations = new[] { BaseGameModelKeys.Relics.Pomander, BaseGameModelKeys.Relics.PreciseScissors,
            BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NeowsTalisman, BaseGameModelKeys.Relics.HeftyTablet };
        var wanted = new Dictionary<string, Func<BonesAcquisitionRouteResult, bool>>(StringComparer.Ordinal);
        string Tag(ModelKey capsule, ModelKey companion) => capsule.Entry + "+" + companion.Entry;
        bool Pair(BonesAcquisitionRouteResult route, ModelKey a, ModelKey b) => route.AcquisitionOrder.Count == 2 && route.AcquisitionOrder.Contains(a) && route.AcquisitionOrder.Contains(b);
        bool Complete(BonesAcquisitionRouteResult route) => route.SharedContinuation.FinalCurseKey.HasValue &&
            route.SharedContinuation.FinalCursePrecision == PredictionPrecision.Exact &&
            route.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Evaluated &&
            !string.IsNullOrWhiteSpace(route.FinalShadowDeckFingerprint);
        foreach (var capsule in new[] { Small, Large })
        {
            foreach (var consumer in new[] { K, Leaf })
            foreach (bool capsuleFirst in new[] { true, false })
            {
                ModelKey requiredUpgrade = capsuleFirst ? WP : W;
                wanted[Tag(capsule, consumer) + (capsuleFirst ? ":capsule-first" : ":consumer-first")] = r =>
                    Pair(r, capsule, consumer) && r.AcquisitionOrder[0] == (capsuleFirst ? capsule : consumer) &&
                    NestedFrom(r, capsule).Contains(requiredUpgrade) && Complete(r) && HasConsumerTrace(r, consumer);
            }
            wanted[Tag(capsule, neutral)] = r => Pair(r, capsule, neutral) && r.AcquisitionOrder[0] == capsule &&
                NestedFrom(r, capsule).Any(IsUpgrade) && Complete(r);
            foreach (var mutation in mutations)
                wanted[Tag(capsule, mutation)] = r => Pair(r, capsule, mutation) && r.AcquisitionOrder[0] == mutation &&
                    NestedFrom(r, capsule).Any(IsUpgrade) && Complete(r) && (mutation != BaseGameModelKeys.Relics.HeftyTablet ||
                    r.PlayerChoiceSelections?.Any(t => t.ChoiceRelicKey == mutation && t.ChoicePolicy.SourceCardKeys.Count == 1 &&
                        capture.Context.Authority.EffectAuthority!.CharacterRewardPool!.Any(c => c.CardKey == t.ChoicePolicy.SourceCardKeys[0] &&
                            c.CanUpgrade && c.CardType is EffectCardType.Attack or EffectCardType.Skill)) == true);
        }
        // Large's fixed cards exist before the later Small obtain. The reverse
        // witness observes Large's hooks before those fixed cards are added.
        wanted["dual:large-first-tail-upgrade"] = r => Pair(r, Small, Large) && r.AcquisitionOrder[0] == Large && NestedFrom(r, Small).Any(IsUpgrade) && Complete(r);
        wanted["dual:small-first-tail-upgrade"] = r => Pair(r, Small, Large) && r.AcquisitionOrder[0] == Small && NestedFrom(r, Large).Any(IsUpgrade) && Complete(r);
        var found = new Dictionary<string, ExpandedSource>(StringComparer.Ordinal);
        var identityRequest = Compile(capture, SearchQuery.Empty with { OpeningRoute = new(bones) }, options);
        var identity = NeowReplayPlan.Compile(identityRequest.Model, false);
        byte Id(ModelKey key) => Beta110FastRelicCatalog.TryGetId(key, out byte value) ? value : throw new InvalidDataException("Expanded.IdentityKey:" + key);
        var interestingPairs = new HashSet<ulong>();
        void Interesting(ModelKey a, ModelKey b) => interestingPairs.Add((1UL << Id(a)) | (1UL << Id(b)));
        foreach (var capsule in new[] { Small, Large })
        foreach (var companion in new[] { K, Leaf, neutral }.Concat(mutations)) Interesting(capsule, companion);
        Interesting(Small, Large);
        int projected = 0, scanned = 0;
        for (int ordinal = 0; ordinal < discoveryLimit && found.Count < wanted.Count; ordinal++)
        {
            scanned = ordinal + 1;
            string seed = Format((ulong)ordinal); ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
            var observation = NeowFamilyReplay.Observe(root, identity);
            if (!observation.IdentityPass || !interestingPairs.Contains((1UL << observation.BonesFirst) | (1UL << observation.BonesSecond))) continue;
            // Discovery is seeded solely by identity, then the actual Core
            // projector with complex choices. No N/R result gate participates.
            var projectedBones = new NeowEffectProjectionEngine(Beta111Profile.Instance, "expanded-capsule-discovery", root,
                capture.Context.Authority, true).Project(bones).BonesOutcome;
            projected++;
            if (projectedBones is null) continue;
            foreach (var route in projectedBones.OriginalRoutes)
            foreach (var requirement in wanted)
                if (!found.ContainsKey(requirement.Key) && requirement.Value(route))
                    found.Add(requirement.Key, new(seed, projectedBones.OriginalRoutes, route));
        }
        var missing = wanted.Keys.Except(found.Keys).ToArray();
        CompatibilityChecks.WriteEvidence(Path.Combine(output, "expanded-discovery.json"), new {
            Complete = missing.Length == 0, Scanned = scanned, ProjectedIdentityPairs = projected,
            MissingPositiveRequirements = missing, Found = found.Select(x => new { Requirement = x.Key, x.Value.Seed,
                x.Value.Route.AcquisitionOrder, x.Value.Route.RouteId, x.Value.Route.FinalShadowDeckFingerprint,
                Choices = x.Value.Route.PlayerChoiceSelections, SmallRelics = NestedFrom(x.Value.Route, Small), LargeRelics = NestedFrom(x.Value.Route, Large) }) });
        if (missing.Length > 0) throw new InvalidDataException("Expanded.MissingRealPositive:" + string.Join(',', missing));

        SearchQuery Query(IReadOnlyList<ModelKey> pair, IReadOnlyList<NeowStructuredEffectSearchCondition> rows,
            bool any, ModelKey? curse = null) => SearchQuery.Empty with {
                OpeningRoute = new(bones), OpeningRouteRelicRequirement = new(bones, pair,
                    any ? BonesRouteOrderMode.AnyOrder : BonesRouteOrderMode.ExactOrder),
                StructuredOpeningEffects = rows, LegacyNeow = SearchQuery.Empty.LegacyNeow with { RequiredFinalCurse = curse } };
        var cases = new List<Case>();
        void Add(string id, ExpandedSource source, SearchQuery query, string coverage)
        {
            id = id.Replace('_', '-'); // Shared Resident job identifiers use letters, digits and hyphens.
            ulong ordinal = Ordinal(source.Seed), start = ordinal - Math.Min(ordinal, 31UL);
            var request = Compile(capture, query, options with { StartSeed = Format(start) });
            var positive = Exact(request, source.Seed);
            if (!positive.IsMatch || positive.Evaluation.Witnesses.Count == 0)
                throw new InvalidDataException("Expanded.DerivedQueryNotExactPositive:" + id + ":" + source.Seed + ":" + positive.FailureCode);
            string directory = Path.Combine(output, id); Directory.CreateDirectory(directory);
            string contextPath = Path.Combine(directory, "context.json"), workloadPath = Path.Combine(directory, "workload.json"), expectedPath = Path.Combine(directory, "expected.json");
            ProductionSearchReplay.Write(contextPath, capture);
            ProductionSearchReplay.Write(workloadPath, new ProductionSearchReplayWorkload(1, query, request.RunOptions));
            CompatibilityChecks.WriteEvidence(Path.Combine(directory, "positive-source.json"), new {
                source.Seed, coverage, DerivedFromActualCoreRoute = source.Route.RouteId,
                source.Route.AcquisitionOrder, source.Route.FinalShadowDeckFingerprint, source.Route.PlayerChoiceSelections,
                ExactEvaluation = positive.Evaluation,
                Scope = "Source discovery route and fresh Exact witnesses retained separately; equal RNG shifts do not merge decks or create witnesses" });
            var pairIdentity = NeowReplayPlan.Compile(request.Model, false);
            var expected = new List<OracleRow>(); int pairs = 0, negativeAudits = 0;
            for (int offset = 0; offset < window; offset++)
            {
                string seed = Format(start + (ulong)offset); ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
                bool admitted = NeowFamilyReplay.Observe(root, pairIdentity).IdentityPass;
                if (!admitted && !(negativeAudits < 32 && offset % Math.Max(1, window / 32) == 0)) continue;
                var truth = Exact(request, seed);
                if (!admitted)
                {
                    negativeAudits++;
                    if (truth.Evaluation.Disposition != SearchDisposition.NoMatch) throw new InvalidDataException("Expanded.IdentityPrerequisite:" + id + ":" + seed);
                    continue;
                }
                pairs++;
                if (truth.Evaluation.Disposition == SearchDisposition.NoMatch) continue;
                if (truth.Evaluation.Disposition is not (SearchDisposition.Match or SearchDisposition.Unknown or SearchDisposition.Unsupported))
                    throw new InvalidDataException("Expanded.UnexpectedDisposition:" + truth.Evaluation.Disposition);
                expected.Add(new(seed, truth.Evaluation.Disposition, ValidationIssue(truth), truth.Evaluation.MatchedRouteIds, truth.Evaluation.Witnesses));
            }
            ProductionSearchReplay.Write(expectedPath, expected);
            cases.Add(new(id, contextPath, workloadPath, expectedPath, start, window, pairs, negativeAudits, 0,
                expected.Count(x => x.ValidationDisposition == SearchDisposition.Match), expected.Count(x => x.ValidationDisposition != SearchDisposition.Match), source.Seed, coverage));
            Console.WriteLine($"EXPANDED_CAPSULE_ORACLE case={id};roots={window};identityPairs={pairs};outputs={expected.Count}");
        }

        foreach (var capsule in new[] { Small, Large })
        foreach (var consumer in new[] { K, Leaf })
        foreach (bool capsuleFirst in new[] { true, false })
        {
            string tag = Tag(capsule, consumer), order = capsuleFirst ? "capsule-first" : "consumer-first";
            var source = found[tag + ":" + order];
            var condition = ConsumerCondition(source.Route, consumer);
            string id = "expanded-" + capsule.Entry + "-" + consumer.Entry + "-" + order;
            Add(id + "-no-r-pinned", source, Query(source.Route.AcquisitionOrder, [condition], false), "Hidden upgrade; unconstrained capsule; N-only; actual consumer trace; pinned");
            Add(id + "-no-r-any", source, Query(source.Route.AcquisitionOrder, [condition], true), "Same authored request with Any order; no R obligation");
            var outputs = NestedFrom(source.Route, capsule);
            Add(id + "-full-pinned", source, Query(source.Route.AcquisitionOrder, [condition, CapsuleCondition(capsule, outputs)], false), "Full actual capsule outputs; pinned");
            if (capsule == Large)
            {
                var hidden = capsuleFirst ? WP : W;
                Add(id + "-partial-pinned", source, Query(source.Route.AcquisitionOrder,
                    [condition, CapsuleCondition(capsule, outputs.Where(x => x != hidden).ToArray())], false), "Partial Large output hides actual upgrade; pinned");
            }
            if (capsuleFirst) Add(id + "-no-r-final-curse", source, Query(source.Route.AcquisitionOrder, [condition], false,
                source.Route.SharedContinuation.FinalCurseKey), "Hidden upgrade plus authored consumer plus actual same-route FinalCurse; no R");
        }
        foreach (var capsule in new[] { Small, Large })
        {
            var source = found[Tag(capsule, neutral)];
            foreach (var order in new[] { 0, 1, 2 })
            {
                var route = source.Routes.First(r => r.AcquisitionOrder[0] == (order == 2 ? neutral : capsule));
                var orderedSource = source with { Route = route };
                Add("expanded-" + capsule.Entry + "-neutral-final-" + order, orderedSource,
                    Query(route.AcquisitionOrder, [], order == 0, route.SharedContinuation.FinalCurseKey), "Hidden upgrade; FinalCurse only; no capsule output or consumer condition");
            }
            Add("expanded-" + capsule.Entry + "-neutral-final-full", source,
                Query(source.Route.AcquisitionOrder, [CapsuleCondition(capsule, NestedFrom(source.Route, capsule))], false,
                    source.Route.SharedContinuation.FinalCurseKey), "Explicit actual capsule plus same-route FinalCurse");
            foreach (var mutation in mutations)
            {
                var changed = found[Tag(capsule, mutation)];
                NeowStructuredEffectSearchCondition[] mutationRows = mutation == BaseGameModelKeys.Relics.HeftyTablet
                    ? [new(mutation, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.SelectableOfferGroups,
                        NeowStructuredOutputKind.Card, changed.Route.PlayerChoiceSelections!.First(t => t.ChoiceRelicKey == mutation).ChoicePolicy.SourceCardKeys)] : [];
                foreach (bool any in new[] { false, true })
                    Add("expanded-" + capsule.Entry + "-" + mutation.Entry + "-final-" + (any ? "any" : "pinned"), changed,
                        Query(changed.Route.AcquisitionOrder, mutationRows, any, changed.Route.SharedContinuation.FinalCurseKey),
                        "Real mutating companion before hidden upgrade; FinalCurse; actual deck/choice witness retained");
            }
        }
        var dual = found["dual:large-first-tail-upgrade"];
        var dualReverse = found["dual:small-first-tail-upgrade"];
        foreach (int smallCount in new[] { 0, 1 })
        foreach (int largeCount in new[] { 0, 1, 2 })
        {
            var rows = new List<NeowStructuredEffectSearchCondition>();
            if (smallCount > 0) rows.Add(CapsuleCondition(Small, NestedFrom(dual.Route, Small)));
            if (largeCount > 0) rows.Add(CapsuleCondition(Large, NestedFrom(dual.Route, Large).Take(largeCount).ToArray()));
            Add($"expanded-dual-source-s{smallCount}-l{largeCount}-any", dual,
                Query(dual.Route.AcquisitionOrder, rows, true, dual.Route.SharedContinuation.FinalCurseKey), "Three actual shared-bag draws; source-constrained or unconstrained capsule outputs; real FinalCurse");
        }
        foreach (var (orderName, source) in new[] { ("large-first", dual), ("small-first", dualReverse) })
        foreach (bool full in new[] { false, true })
        {
            NeowStructuredEffectSearchCondition[] rows = full ? [CapsuleCondition(Small, NestedFrom(source.Route, Small)), CapsuleCondition(Large, NestedFrom(source.Route, Large))] : [];
            Add("expanded-dual-source-" + orderName + (full ? "-full" : "-none"), source,
                Query(source.Route.AcquisitionOrder, rows, false, source.Route.SharedContinuation.FinalCurseKey),
                "Pinned dual capsule; later capsule really obtains W/WP; Large fixed-card timing remains observable");
        }
        var all = NestedFrom(dual.Route, Small).Concat(NestedFrom(dual.Route, Large)).ToArray();
        foreach (int count in new[] { 1, 2, 3 })
        {
            var group = new NeowStructuredEffectSearchCondition(bones, NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
                NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, all.Take(count).ToArray());
            Add("expanded-dual-grouped-" + count + "-any", dual, Query(dual.Route.AcquisitionOrder, [group], true,
                dual.Route.SharedContinuation.FinalCurseKey), "Grouped bag containment does not merge route/deck/FinalCurse witnesses");
        }
        foreach (var first in new[] { Small, Large })
        {
            var route = dual.Routes.First(r => r.AcquisitionOrder[0] == first);
            var group = new NeowStructuredEffectSearchCondition(bones, NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
                NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, all);
            Add("expanded-dual-grouped-full-" + first.Entry, dual with { Route = route },
                Query(route.AcquisitionOrder, [group], false, route.SharedContinuation.FinalCurseKey), "Same grouped contents; exact individual acquisition/deck state and FinalCurse");
        }
        ProductionSearchReplay.Write(Path.Combine(output, "manifest.json"), new Manifest(1,
            "Expanded solo capsule/Niche recall. Every root receives identity-only prerequisite; every admitted identity receives Production Exact; source traces/decks are not synthesized from shifts. Original 36-case manifests remain unchanged.",
            CheckShuffleArithmetic(), cases, []));
        Console.WriteLine("EXPANDED_CAPSULE_ORACLE_READY cases=" + cases.Count + ";discoveryRoots=" + scanned + ";manifest=" + Path.Combine(output, "manifest.json"));
    }

    private static ModelKey[] NestedFrom(BonesAcquisitionRouteResult route, ModelKey source) => route.RelicScopedResults.Where(x => x.SourceRelicKey == source)
        .SelectMany(x => x.EffectGroups).SelectMany(g => g.OrderedItems)
        .Where(i => i.Kind == PredictedEffectKind.AddRelic && i.Relation == PredictedEffectRelation.NestedRelic && i.TargetKey.HasValue)
        .Select(i => i.TargetKey!.Value).ToArray();
    private static bool HasConsumerTrace(BonesAcquisitionRouteResult route, ModelKey consumer) => route.PlayerChoiceSelections?.Any(t => t.ChoiceRelicKey == consumer &&
        (consumer == K ? t.ChoicePolicy.SourceCardKeys.Count == 1 : t.ChoiceEffects.Any(e => e.Kind == PredictedEffectKind.TransformCard && e.TargetKey.HasValue))) == true;
    private static NeowStructuredEffectSearchCondition ConsumerCondition(BonesAcquisitionRouteResult route, ModelKey consumer)
    {
        var trace = route.PlayerChoiceSelections!.First(t => t.ChoiceRelicKey == consumer);
        return consumer == K ? new(consumer, NeowStructuredConditionKind.IndependentOfferGroupTargets,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, trace.ChoicePolicy.SourceCardKeys, AllowDuplicateOutputs: true)
            : new(consumer, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card,
                trace.ChoiceEffects.Where(e => e.Kind == PredictedEffectKind.TransformCard && e.TargetKey.HasValue).Select(e => e.TargetKey!.Value).ToArray());
    }
    private static NeowStructuredEffectSearchCondition CapsuleCondition(ModelKey source, ModelKey[] outputs) => new(source,
        source == Small ? NeowStructuredConditionKind.ExactSingle : NeowStructuredConditionKind.ExactUnorderedPair,
        NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, outputs);
}
