using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Prediction.Profiles;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Predictability;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class ShionExactTrialChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("ShionExactTrial: " + message); checks++; }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var c = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
            "tools/ProductionSearchHost/Corpus/v1/cases/C-three-potions.json");
        var bones = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
            "tools/ProductionSearchHost/Corpus/classic-product-v1/B-SourceCapsules-positive.json");
        var absentRewardCard = new ModelKey("CARD", "STRIKE_IRONCLAD");
        context = context with { Authority = context.Authority.WithAvailableCardIdentities([absentRewardCard]) };
        // Existing CombatCardExclusionChecks establishes this six-battle neutral
        // continuation as a positive. No discovery scan or synthetic reward facts.
        var positive = c with { Query = SearchQuery.Empty with { CombatCardExclusion = new(6, [absentRewardCard]) } };
        var off = ShionExactTrialOptions.Off;
        var on = new ShionExactTrialOptions(OpeningReuse: true);
        Check(ShionExactTrialOptions.Parse(null) == on && ShionExactTrialOptions.Parse("") == on &&
            ShionExactTrialOptions.Parse("off") == off && ShionExactTrialOptions.Parse("opening-reuse") == on,
            "identity reuse defaults on with an explicit off comparison control");
        try { ShionExactTrialOptions.Parse("boss-demand"); throw new Exception("Deferred trial accepted"); }
        catch (ArgumentException ex) { Check(ex.Message.Contains("supported values: off, opening-reuse"), "deferred boss trial is rejected explicitly"); }
        string? oldMode = Environment.GetEnvironmentVariable("RT2_SHION_EXACT_TRIAL");
        string? oldPoolMode = Environment.GetEnvironmentVariable("RT2_C_PREPARED_REWARD_POOLS");
        try
        {
            ExactSearchExecutionRequest Compile() => ExactSearchExecutionRequestFactory.Compile(
                SearchCompiler.Compile(positive.Query, context),
                positive.Options with { ScanCount = 1, WorkerCount = 1 }).Plan!;
            Environment.SetEnvironmentVariable("RT2_SHION_EXACT_TRIAL", null);
            Environment.SetEnvironmentVariable("RT2_C_PREPARED_REWARD_POOLS", "on");
            var defaultPlan = Compile();
            Environment.SetEnvironmentVariable("RT2_SHION_EXACT_TRIAL", "off");
            var offPlan = Compile();
            Check(defaultPlan.ExactOptions == on && offPlan.ExactOptions == off,
                "compilation captures the production default and explicit off mode");
            var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, positive.Options.StartSeed);
            ShionExactTrialMetrics.Reset();
            var actualDefault = ProductionExactSearchEvaluator.Evaluate(defaultPlan, input);
            Check(ShionExactTrialMetrics.Snapshot().IdentityReuses > 0,
                "compiled default still reuses identity after the environment changes to off");
            ShionExactTrialMetrics.Reset();
            var actualOff = ProductionExactSearchEvaluator.Evaluate(offPlan, input);
            Check(ShionExactTrialMetrics.Snapshot().OpeningReuseCandidates == 0 &&
                JsonSerializer.Serialize(actualDefault.Evaluation) == JsonSerializer.Serialize(actualOff.Evaluation) &&
                DocumentSignature(actualDefault.Document) == DocumentSignature(actualOff.Document),
                "compiled off keeps the original path with equal full result and document");
            CheckTimingIdentity(defaultPlan, offPlan, Check);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RT2_SHION_EXACT_TRIAL", oldMode);
            Environment.SetEnvironmentVariable("RT2_C_PREPARED_REWARD_POOLS", oldPoolMode);
        }
        ShionExactTrialMetrics.Reset();
        foreach (var workload in new[] { c, bones, positive })
        foreach (string seed in new[] { workload.Options.StartSeed, "000000000000", "0000000001FA" }.Distinct())
        foreach (bool diagnostics in new[] { false, true })
        {
            var plan = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context),
                workload.Options with { StartSeed = seed, ScanCount = 1, WorkerCount = 1, IncludeDiagnostics = diagnostics }).Plan!;
            var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
            var baseline = ProductionExactSearchEvaluator.Evaluate(plan, input, CancellationToken.None, off);
            var trial = ProductionExactSearchEvaluator.Evaluate(plan, input, CancellationToken.None, on);
            Check(JsonSerializer.Serialize(baseline.Evaluation) == JsonSerializer.Serialize(trial.Evaluation),
                "complete Exact dispositions, causes, witnesses and evidence match the unmodified path");
            Check(DocumentSignature(baseline.Document) == DocumentSignature(trial.Document),
                "complete numeric outputs, statuses, warnings and diagnostics match (only request id normalized)");
            if (workload == positive && seed == workload.Options.StartSeed)
                Check(baseline.IsMatch && trial.IsMatch, "existing C positive actually reaches rewards");
        }
        Check(ShionExactTrialMetrics.Snapshot().IdentityReuses > 0,
            "real Production Exact exercised the identity reuse, rather than only option reuse");

        // Same-request JSON comparison needs no normalization: this is the
        // existing pure profile as oracle, not a second implementation of the optimization.
        foreach (bool complex in new[] { false, true })
        foreach (bool diagnostics in new[] { false, true })
        foreach (bool missingEffects in new[] { false, true })
        {
            var authority = missingEffects ? context.Authority.WithEffectAuthority(null) : context.Authority;
            var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, bones.Options.StartSeed);
            SeedPredictionRequest Request(SeedPredictionDomainSelection domains,
                RuntimeContextAuthoritySnapshot? owner = null, TrustedRootHashInput? root = null,
                bool? diag = null, NormalCombatRewardProjectionRequest? projection = null,
                int preview = SeedPredictionInputLimits.DefaultRelicSequencePreviewCount)
            {
                var captured = owner ?? authority;
                Check(SeedPredictionRequest.TryCreateFromRootHash(root ?? input, captured.Character, captured.Ascension,
                    captured.PlayersCount, captured.PlayerSlotIndex, captured, AncientOptionConditionProfile.BroadDefault,
                    domains, preview, diag ?? diagnostics,
                    projection ?? new(NormalCombatRewardProjectionScope.SearchExact,
                        NormalCombatRewardRouteSelectionMode.ProvablyNeutralRealRoutes), out var request, out _), "profile request");
                return request!.WithComplexBonesDeckInteractions(complex);
            }
            var source = Request(SeedPredictionDomainSelection.Neow);
            var reward = Request(SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards);
            var reuse = new Beta111NeowIdentityReuse();
            var cheap = Beta111PredictionProfile.Instance.PredictFromRootHash(source, context.Detection, input, identityReuse: reuse);
            var choices = cheap.Sections.SelectMany(s => s.NeowChoices).ToArray();
            string frozenChoices = ChoiceSignature(choices);
            var baseline = Beta111PredictionProfile.Instance.PredictFromRootHash(reward, context.Detection, input, choices);
            var trial = Beta111PredictionProfile.Instance.PredictFromRootHash(reward, context.Detection, input, choices, reuse);
            Check(reuse.WasReused, "same captured authority/root/context reuses identity including complex Bones mode");
            Check(JsonSerializer.Serialize(baseline) == JsonSerializer.Serialize(trial), "same-request full document equality");
            Check(ChoiceSignature(choices) == frozenChoices, "opening choices and internal RNG continuation states remain unchanged");
            if (missingEffects)
                Check(trial.Sections.Single(s => s.Kind == PredictionSectionKind.NormalCombatRewardSequence)
                    .NormalCombatRewardSequencePrediction!.Status == SeedDomainEvaluationStatus.Unknown,
                    "missing effects retain Unknown rather than a fabricated reward result");
            bool CanReuse(SeedPredictionRequest request, TrustedRootHashInput root) =>
                reuse.TryReuse(Beta111PredictionProfile.Instance, request, root,
                    request.Authority.IsBeta111NeowIdentityAuthorityExact, out _);
            Check(!CanReuse(Request(reward.Domains, owner: authority.WithEffectAuthority(authority.EffectAuthority)), input),
                "a distinct captured authority cannot borrow the proof even if values match");
            var other = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, "000000000000");
            Check(!CanReuse(Request(reward.Domains, root: other), other), "different seed cannot borrow identity");
            Check(!reuse.TryReuse(Beta111PredictionProfile.Instance, reward, input, false, out _),
                "different exactAuthority argument cannot borrow the cached result");
            Check(!new Beta111NeowIdentityReuse().TryReuse(Beta111PredictionProfile.Instance, reward, input,
                authority.IsBeta111NeowIdentityAuthorityExact, out _), "a new candidate has no inherited identity proof");
            var badDetection = context.Detection with { NormalizedVersion = "0.110.0" };
            var badBaseline = Beta111PredictionProfile.Instance.PredictFromRootHash(reward, badDetection, input);
            var badTrial = Beta111PredictionProfile.Instance.PredictFromRootHash(reward, badDetection, input, identityReuse: reuse);
            Check(JsonSerializer.Serialize(badBaseline) == JsonSerializer.Serialize(badTrial) &&
                badTrial.Warnings.Any(w => w.Code == PredictionWarningCode.InvalidRequestContext),
                "real profile entry preserves version/context rejection before identity reuse");
            var party = Request(reward.Domains); party.PartyOpeningChoices = choices;
            Check(!CanReuse(party, input), "party supplied choices cannot borrow solo identity");
            var roots = Request(reward.Domains); roots.NeowEffectRoots = new HashSet<ModelKey>();
            void CheckIndependentRequest(SeedPredictionRequest current, string setting)
            {
                var independentReuse = new Beta111NeowIdentityReuse();
                Beta111PredictionProfile.Instance.PredictFromRootHash(source, context.Detection, input,
                    identityReuse: independentReuse);
                // No prepared choices: the current request must generate its own
                // effects and reward continuation. Only identity may be reused.
                var expected = Beta111PredictionProfile.Instance.PredictFromRootHash(current, context.Detection, input);
                var actual = Beta111PredictionProfile.Instance.PredictFromRootHash(current, context.Detection, input,
                    identityReuse: independentReuse);
                Check(independentReuse.WasReused, setting + " does not affect pure identity");
                Check(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(actual),
                    setting + " preserves the current request's full freshly generated document");
            }
            CheckIndependentRequest(Request(reward.Domains, diag: !diagnostics), "diagnostics");
            CheckIndependentRequest(reward.WithComplexBonesDeckInteractions(!complex), "complex Bones mode");
            CheckIndependentRequest(roots, "effect projection roots");
            CheckIndependentRequest(reward.WithAncientOptionConditions(
                AncientOptionConditionProfile.BroadDefault with { TezcataraHasBasicStrike = false }), "Ancient premises");
            CheckIndependentRequest(Request(reward.Domains, preview: SeedPredictionInputLimits.DefaultRelicSequencePreviewCount + 1),
                "relic preview length");
            CheckIndependentRequest(Request(reward.Domains, projection: NormalCombatRewardProjectionRequest.RichAnalysis),
                "reward projection scope");

            int calls = 0;
            var injected = new Beta111PredictionProfile((root, captured, profile, exact) =>
            { calls++; return ModernNeowIdentityPredictor.PredictModernCore(root, captured, profile, exact); });
            injected.PredictFromRootHash(reward, context.Detection, input, choices, reuse);
            Check(calls == 1, "injected stateful delegate is always executed");
            var failed = new Beta111PredictionProfile((_, _, _, _) => throw new InvalidOperationException("ShionIdentityOriginalFailure"))
                .PredictFromRootHash(reward, context.Detection, input, choices, reuse);
            Check(failed.Warnings.Any(w => w.Code == PredictionWarningCode.AnalysisFailed) &&
                failed.Diagnostics.Any(d => d.Value.Contains("ShionIdentityOriginalFailure")), "original failure evidence survives reuse and diagnostics off");
            try
            {
                new Beta111PredictionProfile((_, _, _, _) => throw new OperationCanceledException("ShionIdentityCancellation"))
                    .PredictFromRootHash(reward, context.Detection, input, choices, reuse);
                throw new Exception("Cancellation swallowed");
            }
            catch (OperationCanceledException ex) { Check(ex.Message == "ShionIdentityCancellation", "original cancellation preserved"); }
        }
        foreach (bool? vanillaCatalog in new bool?[] { false, null })
        {
            var a = context.Authority;
            var reduced = new RuntimeContextAuthoritySnapshot(a.Character, a.Ascension, 0, 1, a.NoRunModifiers,
                vanillaCatalog, a.AllCharacterCardPoolsUnlocked, a.UnlockedCommonCards, a.UnlockedUncommonCards,
                a.GameVersion, a.ProfileId, a.IsVanilla, a.SourceModId, a.SourceAssembly,
                a.SourceAuthority, a.Completeness, a.ResolutionStatus, a.EffectAuthority, a.WorldAuthority,
                a.PredictionGameMode, a.PredictionGameModeAuthority);
            var reducedContext = context with { Authority = reduced };
            var plan = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(c.Query, reducedContext),
                c.Options with { ScanCount = 1, WorkerCount = 1 }).Plan!;
            var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, c.Options.StartSeed);
            var baseline = ProductionExactSearchEvaluator.Evaluate(plan, input, CancellationToken.None, off);
            var trial = ProductionExactSearchEvaluator.Evaluate(plan, input, CancellationToken.None, on);
            Check(JsonSerializer.Serialize(baseline.Evaluation) == JsonSerializer.Serialize(trial.Evaluation) &&
                DocumentSignature(baseline.Document) == DocumentSignature(trial.Document),
                "Unknown/Unsupported identity authority preserves complete cause and document");
            Check(trial.Evaluation.Disposition is SearchDisposition.Unknown or SearchDisposition.Unsupported,
                "nonexact authority is never promoted to Match");
        }
        Console.WriteLine($"SHION_EXACT_TRIAL_PASS checks={checks};metrics={JsonSerializer.Serialize(ShionExactTrialMetrics.Snapshot())}");
        return 0;
    }

    private static void CheckTimingIdentity(ExactSearchExecutionRequest optimized,
        ExactSearchExecutionRequest poolsOnly, Action<bool, string> check)
    {
        var legacy = poolsOnly with { RewardPoolPreparation = null };
        var identityOnly = optimized with { RewardPoolPreparation = null };
        string revision = FamilyExactTimingDomains.ImplementationRevision(optimized);
        string poolsRevision = FamilyExactTimingDomains.ImplementationRevision(poolsOnly);
        string identityRevision = FamilyExactTimingDomains.ImplementationRevision(identityOnly);
        check(optimized.RewardPoolPreparation?.Pools is not null && revision.Length > 0 &&
            new[] { "", revision, poolsRevision, identityRevision }.Distinct().Count() == 4 &&
            FamilyExactTimingDomains.ImplementationRevision(legacy) == "",
            "actual prepared pools and enabled identity reuse distinguish every changed Exact implementation from legacy");
        var empty = ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(SearchQuery.Empty, optimized.CompiledSearch.Context), optimized.RunOptions).Plan!;
        check(FamilyExactTimingDomains.ImplementationRevision(empty) == "",
            "unrelated non-reward timing retains its existing identity");

        // Isolate the existing in-memory store and restore it without disk I/O.
        // Reflection keeps test-only replacement of private persistence state
        // out of the production API; this check runs without background sessions.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var store = typeof(SearchPredictabilityVerificationStore);
        var fields = new[] { "_document", "_loaded", "_dirty" }.Select(name => store.GetField(name, flags)!).ToArray();
        object?[] saved = fields.Select(field => field.GetValue(null)).ToArray();
        try
        {
            fields[0].SetValue(null, JsonSerializer.Deserialize(
                "{\"SchemaVersion\":1,\"VerificationAbi\":1,\"Entries\":[]}", fields[0].FieldType));
            fields[1].SetValue(null, true);
            fields[2].SetValue(null, false);
            var family = FamilyExactTimingDomains.Resolve(optimized);
            SearchPredictabilityVerificationStore.SubmitExactTimingSample(family, 10, 100, "legacy-fixture");
            SearchPredictabilityVerificationStore.SubmitExactTimingSample(SearchVerificationFamily.RelicOnly, 10, 70, "unrelated-fixture");
            check(SearchPredictabilityVerificationStore.TryGetExactTiming(family, revision) is null,
                "old reward timing cannot price the optimized implementation");
            var before = FamilySearchEtaProjectorV1.Project(optimized, FamilyPlanner.InOrder([]));
            check(before.TargetExactTailMs is null && before.MissingEvidence.Any(e => e.Contains(revision)),
                "ETA explicitly identifies the missing new-body timing without borrowing legacy evidence");
            SearchPredictabilityVerificationStore.SubmitExactTimingSample(family, 10, 20, "optimized-fixture", revision);
            SearchPredictabilityVerificationStore.SubmitExactTimingSample(family, 10, 40, "pool-fixture", poolsRevision);
            var measured = SearchPredictabilityVerificationStore.TryGetExactTiming(family, revision)!;
            check(measured.AverageExactMsPerAttempt == 2 && measured.ImplementationRevision == revision &&
                measured.Source == "optimized-fixture" &&
                SearchPredictabilityVerificationStore.TryGetExactTiming(family)!.AverageExactMsPerAttempt == 10 &&
                SearchPredictabilityVerificationStore.TryGetExactTiming(family, poolsRevision)!.AverageExactMsPerAttempt == 4 &&
                SearchPredictabilityVerificationStore.TryGetExactTiming(family, identityRevision) is null &&
                SearchPredictabilityVerificationStore.TryGetExactTiming(SearchVerificationFamily.RelicOnly)!.AverageExactMsPerAttempt == 7,
                "new samples populate only their exact revision while legacy and unrelated timing survive");
            var after = FamilySearchEtaProjectorV1.Project(optimized, FamilyPlanner.InOrder([]));
            var legacyEta = FamilySearchEtaProjectorV1.Project(legacy, FamilyPlanner.InOrder([]));
            check(after.TargetExactTailMs is > 0 && after.TargetExactTailMs == legacyEta.TargetExactTailMs * .2 &&
                after.ExactTimingImplementationRevision == revision && after.FormatSummary().Contains(revision),
                "unchanged ETA math consumes the matching new timing and reports its implementation");

            var encoded = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(fields[0].GetValue(null), fields[0].FieldType))!;
            foreach (var entry in encoded["Entries"]!.AsArray())
                if (entry!["ImplementationRevision"]!.GetValue<string>().Length == 0)
                    entry.AsObject().Remove("ImplementationRevision");
            fields[0].SetValue(null, JsonSerializer.Deserialize(encoded.ToJsonString(), fields[0].FieldType));
            check(SearchPredictabilityVerificationStore.TryGetExactTiming(family)!.AverageExactMsPerAttempt == 10 &&
                SearchPredictabilityVerificationStore.TryGetExactTiming(family, revision)!.AverageExactMsPerAttempt == 2 &&
                SearchPredictabilityVerificationStore.TryGetExactTiming(SearchVerificationFamily.RelicOnly)!.AverageExactMsPerAttempt == 7,
                "v1 records without a revision round-trip alongside new records without deleting saved evidence");
        }
        finally { for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]); }
    }

    private static string? DocumentSignature(SeedPredictionDocument? d) => d is null ? null : JsonSerializer.Serialize(new
    {
        Context = d.Context with { RequestId = default }, d.PredictorId, d.OriginalSeed, d.CanonicalSeed,
        d.Sections, d.Warnings, Diagnostics = d.Diagnostics.Where(x => x.Code != PredictionDiagnosticCodes.RequestId),
        d.OverallStatus, d.ProductRelevantProjectionPrecision, d.ProductRelevantProjectionStatus, d.FullEffectSemanticsCompleteness
    });

    private static string ChoiceSignature(IReadOnlyList<NeowChoiceResult> choices) => JsonSerializer.Serialize(new
    {
        choices,
        continuationRng = choices.Select(c => new
        {
            direct = c.OpeningRewardContinuations?.Routes.Select(r => new { r.RewardsRngState, r.NicheState, r.CombatPotionGenerationState }),
            bones = c.BonesOutcome?.OriginalRoutes.Select(r => new { rewards = r.OpeningRewardContinuation?.RewardsRngState,
                niche = r.OpeningRewardContinuation?.NicheState, potions = r.OpeningRewardContinuation?.CombatPotionGenerationState })
        })
    });
}
