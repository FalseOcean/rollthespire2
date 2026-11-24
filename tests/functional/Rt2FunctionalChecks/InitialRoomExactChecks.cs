using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.Prediction.Profiles;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class InitialRoomExactChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string name)
        {
            checks++;
            if (!value) throw new InvalidDataException("InitialRoomExact: " + name);
        }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var source = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/classic-product-v1/B-SourceCapsules-positive.json");
        var profile = Beta111Profile.Instance;
        string seed = source.Options.StartSeed;
        PreparedSearch Compile(SearchQuery query, string? inputSeed = null)
        {
            var result = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context),
                source.Options with { StartSeed = inputSeed ?? seed, ScanCount = 1, WorkerCount = 1 });
            return result.Plan ?? throw new InvalidDataException("InitialRoomExact compile:" + result.Issue);
        }
        ProductionExactSearchResult Evaluate(SearchQuery query, string? inputSeed = null) =>
            ProductionExactSearchEvaluator.Evaluate(Compile(query, inputSeed).Model,
                TrustedRootHashInput.FromCanonicalSeed(profile, inputSeed ?? seed));
        TreasureRelicSearchCondition Condition(int start, int count, TreasureRelicOrderMode order, params ModelKey?[] slots) =>
            new(start, count, order, slots);
        SearchQuery Treasure(TreasureRelicSearchCondition condition) => SearchQuery.Empty with { TreasureRelics = condition };

        var bags = RelicSequencePredictor.Predict(profile, seed, context.Authority.WorldAuthority);
        var baseline = TreasureRelicPredictor.Predict(profile, profile.ComputeRootSeed(seed), bags, []);
        Check(baseline.Precision == PredictionPrecision.Exact && baseline.Entries.Count == 2 &&
            baseline.Entries.All(entry => entry.RelicKey.HasValue), "actual corpus baseline chest projection");
        ModelKey first = baseline.Entries[0].RelicKey!.Value, second = baseline.Entries[1].RelicKey!.Value;
        Check(first != second, "corpus baseline has distinct chest opportunities");
        Check(Evaluate(Treasure(Condition(1, 2, TreasureRelicOrderMode.Ordered, first, second))).IsMatch,
            "root-alone Exact matches both actual ordered chest results");
        Check(Evaluate(Treasure(Condition(1, 2, TreasureRelicOrderMode.Ordered, second, first))).Evaluation.Disposition == SearchDisposition.NoMatch,
            "ordered reversal rejects");
        Check(Evaluate(Treasure(Condition(1, 2, TreasureRelicOrderMode.Unordered, second, first))).IsMatch,
            "unordered reversal matches inside selected window");
        Check(Evaluate(Treasure(Condition(2, 1, TreasureRelicOrderMode.Ordered, second, null))).IsMatch,
            "independent start two and range one match second chest");
        Check(Evaluate(Treasure(Condition(2, 1, TreasureRelicOrderMode.Unordered, first, null))).Evaluation.Disposition == SearchDisposition.NoMatch,
            "unordered range one cannot borrow first chest");
        var empty = Evaluate(Treasure(Condition(1, 1, TreasureRelicOrderMode.Ordered, null, second)));
        Check(empty.IsMatch && !empty.Evaluation.Evidence.Any(e => e.ConditionId?.StartsWith("TreasureRelics.", StringComparison.Ordinal) == true),
            "empty active window leaves trailing draft target inactive");
        foreach (string unknownSeed in new[] { "000000000000", "000000000006", "000000067C0S", "SKDNXGNU2A16" })
        foreach (int count in new[] { 1, 2, 3 })
        {
            bool expected = OrdinaryUnknownRoomPredictor.ForAct1Filter(profile.ComputeRootSeed(unknownSeed), count, false)
                .Steps.All(step => step.Room == UnknownRoomCategory.Event);
            var actual = Evaluate(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = count }, unknownSeed);
            Check(actual.Evaluation.Disposition == (expected ? SearchDisposition.Match : SearchDisposition.NoMatch),
                "standalone unknown Event prefix:" + unknownSeed + ":" + count);
        }

        var actualOpening = Evaluate(source.Query);
        Check(actualOpening.IsMatch && actualOpening.Facts != null && actualOpening.Evaluation.Witnesses.Count > 0,
            "real capsule opening fixture");
        var bones = actualOpening.Facts!.Neow!.Choices
            .Single(choice => choice.RelicKey == BaseGameModelKeys.Relics.NeowsBones);
        var routes = bones.BonesOutcome!.OriginalRoutes;
        var exactNested = routes.SelectMany(route => route.RelicScopedResults).SelectMany(scope => scope.EffectGroups)
            .SelectMany(group => group.OrderedItems).First(effect => effect.Kind == PredictedEffectKind.AddRelic &&
                effect.Relation == PredictedEffectRelation.NestedRelic && effect.Precision == PredictionPrecision.Exact && effect.TargetKey.HasValue);
        ModelKey acquired = exactNested.TargetKey!.Value;
        var early = Evaluate(source.Query with { TreasureRelics = Condition(1, 1, TreasureRelicOrderMode.Ordered, acquired, null) });
        Check(early.IsMatch && early.Evaluation.Evidence.Any(e => e.Code == "TreasureRelicObtainedAtOpening" && e.RelatedKey == acquired &&
            early.Evaluation.Witnesses.Any(w => w.OpeningRouteId == e.RouteId)),
            "same real qualifying opening acquisition satisfies treasure target with truthful source evidence");
        // The combined request also captures the shared-bag section used below.
        actualOpening = early;

        // Typed boundary fixtures keep the real document's context and bags.
        // Only the per-route/per-outcome acquisition facts are replaced; these
        // assertions establish binding, not additional native obtain semantics.
        SeedPredictionFacts DocumentAt(string inputSeed, params NeowChoiceResult[] choices) => actualOpening.Facts! with
        {
            OriginalSeed = inputSeed, CanonicalSeed = inputSeed,
            Neow = actualOpening.Facts.Neow! with { Choices = choices },
            Relics = RelicSequencePredictor.Predict(profile, inputSeed, context.Authority.WorldAuthority)
        };
        SeedPredictionFacts Document(params NeowChoiceResult[] choices) => DocumentAt(seed, choices);
        SearchQueryEvaluation Validate(SearchQuery query, SeedPredictionFacts document) =>
            ProductionQueryValidator.Evaluate(Compile(query, document.CanonicalSeed).Model, document, actualOpening.Authority);
        var routeTarget = new ModelKey("RELIC", "INITIAL_ROOM_ROUTE_ONLY");
        PredictedEffectGroup AcquiredGroup(ModelKey sourceKey, ModelKey key) => new("initial-room-acquisition", 0,
            EffectSelectionPolicy.ForcedObtain, EffectPredictionDomain.Neow, EffectPredictionScope.NestedObtain,
            "Typed actual acquisition", [new(PredictedEffectKind.AddRelic, key, null, PredictionPrecision.Exact, [],
                "initial-room.typed-acquisition", SourceRelicKey: sourceKey, Relation: PredictedEffectRelation.NestedRelic)],
            SourceRelicKey: sourceKey);
        BonesAcquisitionRouteResult RouteContents(BonesAcquisitionRouteResult route, IReadOnlyList<ModelKey> contents)
        {
            int index = 0;
            return route with
            {
                RelicScopedResults = route.RelicScopedResults.Select(scope => scope with {
                    EffectGroups = scope.EffectGroups.Select(group => group with { OrderedItems = group.OrderedItems
                        .Where(effect => effect.Kind == PredictedEffectKind.AddRelic && effect.Relation == PredictedEffectRelation.NestedRelic)
                        .Select(effect => effect with { TargetKey = index < contents.Count ? contents[index++] :
                            new ModelKey("RELIC", "INITIAL_ROOM_OTHER_" + index++), SourceRelicKey = scope.SourceRelicKey }).ToArray() })
                        .Where(group => group.OrderedItems.Count > 0).ToArray() }).ToArray(),
                PlayerChoiceSelections = []
            };
        }
        BonesAcquisitionRouteResult Route(BonesAcquisitionRouteResult route, bool containsTarget) =>
            RouteContents(route, containsTarget ? [routeTarget] : []);
        Check(routes.Count >= 2 && !routes[0].AcquisitionOrder.SequenceEqual(routes[1].AcquisitionOrder), "real two-order boundary fixture");
        var withTarget = Route(routes[0], true);
        var withoutTarget = Route(routes[1], false);
        var orderDocument = Document(bones with { BonesOutcome = bones.BonesOutcome with {
            OriginalRoutes = [withTarget, withoutTarget], PlayerChoiceImpact = PlayerChoiceImpactAnalysis.Empty } });
        SearchQuery Pinned(BonesAcquisitionRouteResult route) => SearchQuery.Empty with
        {
            OpeningRoute = new(bones.RelicKey),
            OpeningRouteRelicRequirement = new(bones.RelicKey, route.AcquisitionOrder, BonesRouteOrderMode.ExactOrder),
            TreasureRelics = Condition(1, 1, TreasureRelicOrderMode.Ordered, routeTarget, null)
        };
        Check(Validate(Pinned(withTarget), orderDocument).Disposition == SearchDisposition.Match,
            "selected acquisition order owns its early relic");
        Check(Validate(Pinned(withoutTarget), orderDocument).Disposition == SearchDisposition.NoMatch,
            "selected order cannot borrow relic from different acquisition order");

        var curseA = new ModelKey("CARD", "DECAY");
        var curseB = new ModelKey("CARD", "DOUBT");
        var outcomeSource = withoutTarget.AcquisitionOrder[0];
        DistinctPlayerChoiceOutcome Outcome(string id, ModelKey curse, bool containsTarget) => new(id, withoutTarget.RouteId,
            outcomeSource, new(id, PlayerChoicePolicyKind.ChooseOfferedCard, [], 0),
            containsTarget ? [AcquiredGroup(outcomeSource, routeTarget)] : [], [], "typed-deck", curse,
            PredictionPrecision.Exact, PredictionPrecision.Exact, ProductRelevantProjectionStatus.Evaluated, [],
            ["initial-room.typed-outcome"], SourceAuthority.AuditedStaticExact, SnapshotCompleteness.Complete,
            EffectPredictionScope.FinitePlayerChoiceRoutes, 1, 1, 1);
        var outcomeA = Outcome("initial-room.outcome-a", curseA, false);
        var outcomeB = Outcome("initial-room.outcome-b", curseB, true);
        var outcomeDocument = Document(bones with { BonesOutcome = bones.BonesOutcome with {
            OriginalRoutes = [withoutTarget], PlayerChoiceImpact = new([new("initial-room.outcomes", withoutTarget.AcquisitionOrder,
                outcomeSource, withoutTarget.RouteId, 2, 2, [outcomeA, outcomeB])]) } });
        var outcomeQuery = Pinned(withoutTarget);
        Check(Validate(outcomeQuery with { LegacyNeow = outcomeQuery.LegacyNeow with { RequiredFinalCurse = curseA } }, outcomeDocument)
            .Disposition == SearchDisposition.NoMatch, "qualifying outcome cannot borrow another outcome's acquisition");
        var sameOutcome = Validate(outcomeQuery with { LegacyNeow = outcomeQuery.LegacyNeow with { RequiredFinalCurse = curseB } }, outcomeDocument);
        Check(sameOutcome.Disposition == SearchDisposition.Match && sameOutcome.Evidence.Any(e =>
            e.Code == "TreasureRelicObtainedAtOpening" && e.OutcomeId == outcomeB.OutcomeId), "chosen outcome owns acquisition evidence");

        var silverKey = new ModelKey("RELIC", "SILVER_CRUCIBLE");
        var silver = RolltheSpire2.Core.Neow.NeowChoiceFacts.Choice(1, silverKey, actualOpening.Request!, PredictionPrecision.Exact,
            "initial-room.silver-offered", NeowEffectProjection.ProductExactNoVisibleEffects(silverKey, "initial-room.silver-acquired"));
        var silverDocument = Document(silver);
        var silverQuery = SearchQuery.Empty with { OpeningRoute = new(silverKey),
            TreasureRelics = Condition(1, 1, TreasureRelicOrderMode.Ordered, first, null) };
        Check(Validate(silverQuery, silverDocument).Disposition == SearchDisposition.NoMatch,
            "Silver Crucible suppressed first encounter cannot yield baseline first relic");
        Check(Validate(silverQuery with { TreasureRelics = Condition(2, 1, TreasureRelicOrderMode.Ordered, first, null) }, silverDocument)
            .Disposition == SearchDisposition.Match, "Silver Crucible second encounter uses first rarity draw");

        // Find a small pure witness where declared Juzu possession changes this
        // prefix's disposition. The typed document's bag is rebuilt for its seed.
        string? juzuSeed = null;
        int juzuPrefix = 0;
        for (ulong ordinal = 0; ordinal < 256 && juzuSeed is null; ordinal++)
        {
            string candidate = VisibleSeedCandidateCodec.FormatOrdinal(profile, ordinal);
            ulong root = profile.ComputeRootSeed(candidate);
            for (int count = 1; count <= 3; count++)
                if (!OrdinaryUnknownRoomPredictor.ForAct1Filter(root, count, false).Steps.All(step => step.Room == UnknownRoomCategory.Event) &&
                    OrdinaryUnknownRoomPredictor.ForAct1Filter(root, count, true).Steps.All(step => step.Room == UnknownRoomCategory.Event))
                {
                    juzuSeed = candidate; juzuPrefix = count; break;
                }
        }
        Check(juzuSeed is not null, "bounded pure scan finds Juzu Event-prefix witness");
        var juzuKey = new ModelKey("RELIC", "JUZU_BRACELET");
        var offeredJuzu = RolltheSpire2.Core.Neow.NeowChoiceFacts.Choice(2, juzuKey, actualOpening.Request!, PredictionPrecision.Exact,
            "initial-room.juzu-offered", NeowEffectProjection.ProductExactNoVisibleEffects(juzuKey, "initial-room.juzu-typed"));
        var offerDocument = DocumentAt(juzuSeed!, silver, offeredJuzu);
        var offeredOnly = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = juzuPrefix };
        Check(Validate(offeredOnly, offerDocument).Disposition == SearchDisposition.NoMatch,
            "offered-only Juzu identity retains the fresh no-Juzu prefix");
        Check(Validate(SearchQuery.Empty with { OpeningRoute = new(silverKey), FirstActUnknownEventPrefixCount = juzuPrefix }, offerDocument)
            .Disposition == SearchDisposition.NoMatch, "chosen sibling opening cannot borrow an offered Juzu");
        var directJuzu = offeredOnly with { OpeningRoute = new(juzuKey) };
        var directMatch = Validate(directJuzu, offerDocument);
        Check(directMatch.Disposition == SearchDisposition.Match && directMatch.Evidence.Count(e =>
            e.Code == "FirstActUnknownEventPrefixMatched" && e.RelatedKey == juzuKey && e.RouteId is null && e.OutcomeId is null) == 1,
            "declared direct Juzu uses one independent prefix observation");
        ulong juzuRoot = profile.ComputeRootSeed(juzuSeed!);
        SearchQueryEvaluation Prefix(SearchQuery query) => ProductionQueryValidator.EvaluateInitialRoomBaseline(
            Compile(query, juzuSeed).Model, null, juzuRoot);
        foreach (var opening in new[] { BaseGameModelKeys.Relics.NeowsBones, BaseGameModelKeys.Relics.SmallCapsule,
                     BaseGameModelKeys.Relics.LargeCapsule, silverKey })
            Check(Prefix(offeredOnly with { OpeningRoute = new(opening) }).Disposition == SearchDisposition.NoMatch,
                "unspecified opening has no Juzu influence even without pickup observations:" + opening);
        var bonesJuzu = offeredOnly with { OpeningRoute = new(bones.RelicKey),
            OpeningRouteRelicRequirement = new(bones.RelicKey, [BaseGameModelKeys.Relics.SmallCapsule], BonesRouteOrderMode.AnyOrder),
            LegacyNeow = offeredOnly.LegacyNeow with { CapsuleContainedRelics = new([], [juzuKey], []) } };
        var capsuleJuzu = offeredOnly with { OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule),
            LegacyNeow = offeredOnly.LegacyNeow with { CapsuleContainedRelics = new([], [juzuKey], []) } };
        Check(Prefix(bonesJuzu).Disposition == SearchDisposition.Match && Prefix(capsuleJuzu).Disposition == SearchDisposition.Match,
            "declared Bones and Capsule Juzu prefix needs no generated pickup observations");
        Check(Prefix(capsuleJuzu with { LegacyNeow = capsuleJuzu.LegacyNeow with {
            CapsuleContainedRelics = new([juzuKey, silverKey], [], []) } }).Disposition == SearchDisposition.NoMatch,
            "multi-Any nested candidates do not declare held Juzu");
        Check(Prefix(offeredOnly with { RelicSequenceConstraints = [new(RelicSequenceKind.Common,
            SearchSequenceRangeMode.FirstN, 3, new([juzuKey], [], []))] }).Disposition == SearchDisposition.NoMatch,
            "future relic sequence does not declare held Juzu");
        var unknownNeow = DocumentAt(juzuSeed!, silver, offeredJuzu with { IdentityPrecision = PredictionPrecision.Unknown });
        Check(Validate(directJuzu, unknownNeow).Disposition == SearchDisposition.Unknown,
            "declared prefix success cannot confirm an independently unknown N observation");
        Check(Validate(directJuzu, DocumentAt(juzuSeed!, silver)).Disposition == SearchDisposition.NoMatch,
            "declared prefix success cannot override N route identity mismatch");

        var juzuOnlyRoute = RouteContents(routes[0], [juzuKey]);
        var treasureOnlyRoute = RouteContents(routes[1], [routeTarget]);
        var splitRoutes = DocumentAt(juzuSeed!, bones with { BonesOutcome = bones.BonesOutcome with {
            OriginalRoutes = [juzuOnlyRoute, treasureOnlyRoute], PlayerChoiceImpact = PlayerChoiceImpactAnalysis.Empty } });
        var undeclaredRouteQuery = Pinned(juzuOnlyRoute) with { TreasureRelics = null, FirstActUnknownEventPrefixCount = juzuPrefix };
        Check(Validate(undeclaredRouteQuery, splitRoutes).Disposition == SearchDisposition.NoMatch,
            "actual nested Juzu is ignored when not declared");
        var juzuRouteQuery = undeclaredRouteQuery with { LegacyNeow = undeclaredRouteQuery.LegacyNeow with {
            CapsuleContainedRelics = new([], [juzuKey], []) } };
        var juzuRouteMatch = Validate(juzuRouteQuery, splitRoutes);
        Check(juzuRouteMatch.Disposition == SearchDisposition.Match && juzuRouteMatch.Evidence.Any(e =>
            e.Code == "FirstActUnknownEventPrefixMatched" && e.RelatedKey == juzuKey && e.RouteId is null) &&
            juzuRouteMatch.Witnesses.Any(w => w.AcquisitionOrder.SequenceEqual(juzuOnlyRoute.AcquisitionOrder)),
            "declared nested Juzu controls prefix while N retains its actual route witness");
        Check(Validate(Pinned(treasureOnlyRoute) with { TreasureRelics = null, FirstActUnknownEventPrefixCount = juzuPrefix }, splitRoutes)
            .Disposition == SearchDisposition.NoMatch, "unspecified Bones route remains no-Juzu");
        var jointQuery = SearchQuery.Empty with { OpeningRoute = new(bones.RelicKey), FirstActUnknownEventPrefixCount = juzuPrefix,
            TreasureRelics = Condition(1, 1, TreasureRelicOrderMode.Ordered, routeTarget, null) };
        Check(Validate(jointQuery, splitRoutes).Disposition == SearchDisposition.NoMatch,
            "actual Juzu from any qualifying order cannot change an undeclared prefix");
        var jointRoute = RouteContents(routes[0], [juzuKey, routeTarget]);
        var jointDocument = DocumentAt(juzuSeed!, bones with { BonesOutcome = bones.BonesOutcome with {
            OriginalRoutes = [jointRoute], PlayerChoiceImpact = PlayerChoiceImpactAnalysis.Empty } });
        Check(Validate(jointQuery, jointDocument).Disposition == SearchDisposition.NoMatch,
            "actual joint treasure and Juzu pickups do not invent a Juzu declaration");
        var jointDeclared = jointQuery with { LegacyNeow = jointQuery.LegacyNeow with { CapsuleContainedRelics = new([], [juzuKey], []) } };
        var jointMatch = Validate(jointDeclared, jointDocument);
        Check(jointMatch.Disposition == SearchDisposition.Match && jointMatch.Witnesses.Any(w =>
            jointMatch.Evidence.Any(e => e.Code == "TreasureRelicObtainedAtOpening" && e.RouteId == w.OpeningRouteId)) &&
            jointMatch.Evidence.Count(e => e.Code == "FirstActUnknownEventPrefixMatched" && e.RouteId is null) == 1,
            "treasure keeps its actual route witness alongside one declared prefix observation");
        Check(Validate(jointDeclared, splitRoutes).Disposition == SearchDisposition.NoMatch,
            "declared Juzu and treasure acquisition still require one qualifying actual N route");

        var optionalJuzu = outcomeB with { OutcomeId = "initial-room.optional-juzu", FinalCurseKey = curseB,
            ChoiceEffectGroups = [AcquiredGroup(outcomeSource, juzuKey)] };
        var optionalTarget = outcomeA with { OutcomeId = "initial-room.optional-target", FinalCurseKey = curseA,
            ChoiceEffectGroups = [AcquiredGroup(outcomeSource, routeTarget)] };
        SeedPredictionFacts OutcomeDocument(BonesAcquisitionRouteResult route, params DistinctPlayerChoiceOutcome[] outcomes) => DocumentAt(juzuSeed!, bones with {
            BonesOutcome = bones.BonesOutcome with { OriginalRoutes = [route], PlayerChoiceImpact = new([
                new("initial-room.juzu-outcomes", route.AcquisitionOrder, outcomeSource, route.RouteId,
                    outcomes.Length, outcomes.Length, outcomes)]) } });
        var splitOutcomes = OutcomeDocument(withoutTarget, optionalTarget, optionalJuzu);
        var targetOutcomeQuery = jointQuery with { LegacyNeow = jointQuery.LegacyNeow with { RequiredFinalCurse = curseA } };
        Check(Validate(targetOutcomeQuery, splitOutcomes).Disposition == SearchDisposition.NoMatch,
            "selected treasure outcome cannot invent a Juzu declaration from its sibling");
        Check(Validate(jointQuery, splitOutcomes).Disposition == SearchDisposition.NoMatch,
            "actual optional Juzu cannot change an undeclared prefix");
        var chosenJuzu = Validate(jointQuery with { TreasureRelics = null,
            LegacyNeow = jointQuery.LegacyNeow with { RequiredFinalCurse = curseB } }, splitOutcomes);
        Check(chosenJuzu.Disposition == SearchDisposition.NoMatch,
            "even the selected actual optional Juzu outcome has no undeclared prefix influence");
        var declaredOutcome = jointDeclared with { LegacyNeow = jointDeclared.LegacyNeow with { RequiredFinalCurse = curseB } };
        var optionalOnlyJuzu = Validate(declaredOutcome with { TreasureRelics = null }, splitOutcomes);
        Check(optionalOnlyJuzu.Disposition == SearchDisposition.NoMatch,
            "declared prefix cannot override N's unsatisfied guaranteed Capsule output predicate;actual=" +
            System.Text.Json.JsonSerializer.Serialize(optionalOnlyJuzu, ProductionSearchReplay.JsonOptions));
        // The Capsule declaration concerns its guaranteed route output. Optional
        // acquisitions retain their own outcome ownership for treasure below.
        var declaredOutcomeDocument = OutcomeDocument(RouteContents(withoutTarget, [juzuKey]), optionalTarget, optionalJuzu);
        var declaredOptional = Validate(declaredOutcome with { TreasureRelics = null }, declaredOutcomeDocument);
        Check(declaredOptional.Disposition == SearchDisposition.Match && declaredOptional.Evidence.Count(e =>
            e.Code == "FirstActUnknownEventPrefixMatched" && e.RelatedKey == juzuKey && e.OutcomeId is null) == 1,
            "declared route Juzu uses independent prefix evidence after N validates its optional outcome;actual=" +
            System.Text.Json.JsonSerializer.Serialize(declaredOptional, ProductionSearchReplay.JsonOptions));
        Check(Validate(declaredOutcome, declaredOutcomeDocument).Disposition == SearchDisposition.NoMatch,
            "declared prefix does not let one actual outcome borrow another outcome's treasure");
        var declaredTargetOutcome = Validate(declaredOutcome with { LegacyNeow = declaredOutcome.LegacyNeow with {
            RequiredFinalCurse = curseA } }, declaredOutcomeDocument);
        Check(declaredTargetOutcome.Disposition == SearchDisposition.Match && declaredTargetOutcome.Evidence.Any(e =>
            e.Code == "TreasureRelicObtainedAtOpening" && e.OutcomeId == optionalTarget.OutcomeId),
            "declared prefix preserves the selected actual treasure outcome witness;actual=" +
            System.Text.Json.JsonSerializer.Serialize(declaredTargetOutcome, ProductionSearchReplay.JsonOptions));
        Console.WriteLine($"INITIAL_ROOM_EXACT_PASS checks={checks};baseline-two-chests;window-order;unknown-prefix;actual-N-acquisition;route-outcome-binding;SilverCrucible;DeclaredJuzu seed={juzuSeed};prefix={juzuPrefix}");
        return 0;
    }
}
