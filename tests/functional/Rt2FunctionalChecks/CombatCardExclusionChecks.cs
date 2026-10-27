using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;

internal static class CombatCardExclusionChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string name)
        { if (!value) throw new InvalidDataException("CombatCardExclusion: " + name); checks++; }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var source = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
            "tools/ProductionSearchHost/Corpus/classic-product-v1/B-SourceCapsules-positive.json");
        var absent = new ModelKey("CARD", "STRIKE_IRONCLAD");
        var target = new ModelKey("CARD", "HEADBUTT");
        var missing = new ModelKey("CARD", "EXCLUSION_TEST_UNAVAILABLE");
        ModelKey[] identities = [absent, target];
        context = context with { Authority = context.Authority.WithAvailableCardIdentities(identities) };
        SearchQuery Exclude(int n, params ModelKey[] keys) => SearchQuery.Empty with { CombatCardExclusion = new(n, keys) };
        ExactSearchExecutionRequest Compile(SearchQuery query)
        {
            var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                source.Options with { StartSeed = "000000000000", ScanCount = 1, WorkerCount = 1 });
            return result.Plan ?? throw new InvalidDataException("Exclusion compile: " + result.Disposition + ":" + result.Issue);
        }
        var query = Exclude(6, absent);
        var compiled = SearchCompiler.Compile(query, context);
        var plan = Compile(query);
        Check(compiled.NormalizedQuery.HasCombatRewardConstraints && plan.Evaluation.RequiresNormalCombatRewardDomain,
            "standalone negative condition requires reward preparation and Exact");
        Check(plan.Evaluation.CombatCardExclusion!.BattleCount == 6, "projection retains full horizon");
        var physical = FamilyExecutionCoordinator.Plan(plan, gpuAvailable: false);
        Check(physical.FilterRemainders.Contains("CombatCardExclusion:ProductionExactOnly"), "negative condition is explicitly Exact-only");
        var skipped = FamilyExecutionCoordinator.Plan(plan with { RunOptions = plan.RunOptions with { SkipExactValidation = true } }, false);
        Check(skipped.FinalValidationSkipped && skipped.FilterRemainders.Contains("CombatCardExclusion:ProductionExactOnly"),
            "skip Exact keeps the unverified obligation visible");
        var probability = CombatRewardProbabilityEstimator.Estimate(SearchSelectivityInput.From(compiled));
        Check(probability.IsPriced && probability.Probability == 1,
            "an available Basic outside the captured reward pool has exclusion probability one; execution remains Exact-only");
        Check(compiled.SemanticFingerprint != SearchCompiler.Compile(Exclude(5, absent), context).SemanticFingerprint &&
              compiled.SemanticFingerprint != SearchCompiler.Compile(Exclude(6, target), context).SemanticFingerprint,
            "horizon and card members participate in cache identity");
        Check(SearchCompiler.Compile(Exclude(6, target, absent, target), context).SemanticFingerprint ==
              SearchCompiler.Compile(Exclude(6, absent, target), context).SemanticFingerprint,
            "set order and duplicate identities canonicalize");
        Check(compiled.SemanticFingerprint != SearchCompiler.Compile(query, context with {
            Authority = context.Authority.WithAvailableCardIdentities(null) }).SemanticFingerprint, "identity authority participates in cache identity");
        foreach (var invalid in new[] { Exclude(0, absent), Exclude(7, absent), Exclude(1), Exclude(1, new ModelKey("RELIC", "INVALID")) })
        {
            bool rejected = false;
            try { SearchCompiler.Compile(invalid, context); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "malformed exclusion is not silently discarded");
        }
        Check(context.Authority.WithEffectAuthority(context.Authority.EffectAuthority).WithWorldAuthority(context.Authority.WorldAuthority)
            .AvailableCardIdentities!.SequenceEqual(context.Authority.AvailableCardIdentities!), "authority copies preserve all registered identities");
        identities[0] = missing;
        Check(!context.Authority.AvailableCardIdentities!.Contains(missing), "authority owns a defensive identity copy");

        // Reuse a current corpus authority and the actual predictor for preparation;
        // local route fixtures below replace only reward facts to test conjunction binding.
        // Existing CombinationRegressionChecks.CheckCombatAssignments establishes
        // v1/context + zero seed as a real positive neutral-continuation fixture.
        var baseline = ProductionExactSearchEvaluator.Evaluate(plan, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, plan.CanonicalStartSeed));
        Check(baseline.IsMatch, "standalone negative condition completes Production Exact: " + baseline.FailureCode);
        var section = baseline.Document!.Sections.Single(s => s.Kind == PredictionSectionKind.NormalCombatRewardSequence);
        var sequence = section.NormalCombatRewardSequencePrediction!;
        var original = sequence.Routes.First(r => r.Battles.Count == 6);
        Check(original.Battles.Select(b => b.BattleOrdinal).SequenceEqual(Enumerable.Range(1, 6)), "Exact actually predicts six battles");
        var first = original.Battles[0];
        var card = first.CardRewards.SelectMany(r => r.Cards).First();
        var targetCard = card with { CardKey = target, Precision = PredictionPrecision.Exact };
        var absentCard = card with { CardKey = absent, Precision = PredictionPrecision.Exact };
        var clear = original with { Battles = original.Battles.Select(b => b with { Cards = [targetCard], CardRewards =
            [new NormalCombatRewardCardRewardResult(1, true, [targetCard], b.EvidenceCode)] }).ToArray() };
        SearchDisposition Match(CombatCardExclusionSearchCondition c, NormalCombatRewardRoutePredictionResult r,
            IReadOnlyList<ModelKey>? available) => CombatCardExclusionMatcher.Evaluate(c, r, available).Disposition;
        var condition = query.CombatCardExclusion!;
        Check(Match(condition, clear, context.Authority.AvailableCardIdentities) == SearchDisposition.Match, "all initial options exclude the registered non-reward member");
        var sixthHit = clear with { Battles = clear.Battles.Select(b => b.BattleOrdinal == 6 ? b with { CardRewards =
            [new(1, true, [targetCard], b.EvidenceCode), new(2, false, [absentCard with { UpgradeState = NormalCombatCardUpgradeState.Upgraded }], b.EvidenceCode)] } : b).ToArray() };
        Check(Match(condition, sixthHit, null) == SearchDisposition.NoMatch, "sixth battle additional reward hits even upgraded and identity catalogue unknown");
        Check(Match(new(5, [absent]), sixthHit, context.Authority.AvailableCardIdentities) == SearchDisposition.Match, "N does not inspect later battles");
        Check(Match(new(6, [absent, missing]), sixthHit, context.Authority.AvailableCardIdentities) == SearchDisposition.NoMatch,
            "known hit disproves even with missing collection member");
        Check(Match(new(6, [absent, missing]), clear, context.Authority.AvailableCardIdentities) == SearchDisposition.Unknown,
            "unavailable member cannot prove no hit");
        Check(Match(condition, clear, null) == SearchDisposition.Unknown, "old frozen identity authority is unknown");
        Check(Match(condition, clear with { Battles = clear.Battles.Take(5).ToArray() }, context.Authority.AvailableCardIdentities) == SearchDisposition.Unknown,
            "missing battle is unknown");
        Check(Match(condition, clear with { Battles = clear.Battles.Select(b => b.BattleOrdinal == 2
            ? b with { Cards = [], CardRewards = [] } : b).ToArray() }, context.Authority.AvailableCardIdentities) == SearchDisposition.Unknown,
            "empty card observation cannot prove complete absence");
        var uncertain = clear with { Battles = clear.Battles.Select(b => b.BattleOrdinal == 2 ? b with { CardRewards =
            [new(1, true, [absentCard with { Precision = PredictionPrecision.Unknown }], b.EvidenceCode)] } : b).ToArray() };
        Check(Match(condition, uncertain, context.Authority.AvailableCardIdentities) == SearchDisposition.Unknown, "unknown identity is not absence");

        var positiveAndNegative = Compile(query with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [target]) });
        var conflicting = sixthHit with { RouteGroupId = "exclusion.A", EquivalentRoutes = original.EquivalentRoutes.Select(d => d with { RouteId = "exclusion.A" }).ToArray() };
        var noTarget = clear with { RouteGroupId = "exclusion.B", EquivalentRoutes = original.EquivalentRoutes.Select(d => d with { RouteId = "exclusion.B" }).ToArray(),
            Battles = clear.Battles.Select(b => b with { Cards = [], CardRewards =
                [new(1, true, [targetCard with { CardKey = new("CARD", "ANGER") }], b.EvidenceCode)] }).ToArray() };
        SearchQueryEvaluation EvaluateRoutes(params NormalCombatRewardRoutePredictionResult[] routes)
        {
            var document = new SeedPredictionDocument { Context = baseline.Document!.Context, PredictorId = baseline.Document.PredictorId,
                OriginalSeed = baseline.Document.OriginalSeed, CanonicalSeed = baseline.Document.CanonicalSeed,
                Warnings = baseline.Document.Warnings, Diagnostics = baseline.Document.Diagnostics, OverallStatus = baseline.Document.OverallStatus,
                Sections = baseline.Document.Sections.Select(s => s == section
                    ? s with { NormalCombatRewardSequencePrediction = sequence with { Routes = routes } } : s).ToArray() };
            return ProductionQueryValidator.EvaluateRewards(positiveAndNegative, document,
                SearchQueryEvaluation.Match(witnesses: [new(plan.CanonicalStartSeed, "", [], [], "", null, [])]));
        }
        Check(EvaluateRoutes(conflicting, noTarget).Disposition == SearchDisposition.NoMatch,
            "positive target and negative pass cannot borrow different opening witnesses");
        var joint = clear with { RouteGroupId = "exclusion.C", EquivalentRoutes = original.EquivalentRoutes.Select(d => d with { RouteId = "exclusion.C" }).ToArray() };
        Check(EvaluateRoutes(conflicting, joint).Disposition == SearchDisposition.Match, "another complete same-route witness may match");
        checks += CombinationRegressionChecks.CheckCardExclusionParty(context);
        Console.WriteLine($"COMBAT_CARD_EXCLUSION checks={checks};failures=0;sameWitness=preserved;unknowns=preserved");
        return 0;
    }
}


internal static partial class CombinationRegressionChecks
{
    internal static int CheckCardExclusionParty(SearchContext captured)
    {
        int checks = 0;
        void Assert(bool value, string message)
        { if (!value) throw new InvalidDataException("CombatCardExclusion.Party: " + message); checks++; }
        var context = PartyContext(captured, 2);
        var rawParty = context.Party!;
        var players = rawParty.Players.Select(p => p.WithAvailableCardIdentities(captured.Authority.AvailableCardIdentities)).ToArray();
        var party = new OrderedPartyAuthority(players, rawParty.UnlockProvenance, rawParty.World, rawParty.UnlockSources);
        context = context with { Authority = players[0], Party = party };
        var member = captured.Authority.AvailableCardIdentities!.First(k => k.Entry == "STRIKE_IRONCLAD");
        var q = Table(2, (1, SearchQuery.Empty with { CombatCardExclusion = new(6, [member]) }));
        var compiled = SearchCompiler.Compile(q, context);
        var plan = Compile(context, q, "000000000000");
        var child0 = ExactSearchExecutionRequestFactory.ForPlayer(plan, compiled.PlayerSearches[0]);
        var child1 = ExactSearchExecutionRequestFactory.ForPlayer(plan, compiled.PlayerSearches[1]);
        Assert(!child0.Evaluation.RequiresNormalCombatRewardDomain && child1.Evaluation.CombatCardExclusion?.BattleCount == 6,
            "negative-only condition belongs to selected seat");
        var physical = FamilyExecutionCoordinator.Plan(plan, false);
        Assert(physical.FilterRemainders.Contains("CombatCardExclusion:slot=1:ProductionExactOnly"), "per-seat Exact remainder is visible");
        var unknownPlayers = players.Select((p, i) => i == 1 ? p.WithAvailableCardIdentities(null) : p).ToArray();
        var unknownParty = new OrderedPartyAuthority(unknownPlayers, rawParty.UnlockProvenance, rawParty.World, rawParty.UnlockSources);
        Assert(compiled.SemanticFingerprint != SearchCompiler.Compile(q, context with { Party = unknownParty }).SemanticFingerprint,
            "table fingerprint includes relevant seat's global identity authority");
        var result = ProductionExactSearchEvaluator.Evaluate(plan, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, plan.CanonicalStartSeed));
        Assert(result.IsMatch, "negative-only seat participates in joint Exact: " + result.FailureCode);
        return checks;
    }
}
