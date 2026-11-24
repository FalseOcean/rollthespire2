using Godot;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

// Bounded independent recall oracle in the existing Production host. Neither
// the old N predicate, R predicate, nor either candidate repair selects truth.
internal static partial class BonesHiddenUpgradeChecks
{
    private static readonly ModelKey Large = BaseGameModelKeys.Relics.LargeCapsule;
    private static readonly ModelKey K = BaseGameModelKeys.Relics.Kaleidoscope;
    private static readonly ModelKey W = BaseGameModelKeys.OrdinaryRelics.Whetstone;
    private static readonly ModelKey WP = BaseGameModelKeys.OrdinaryRelics.WarPaint;
    private sealed record OracleRow(string Seed, SearchDisposition ValidationDisposition,
        string ValidationIssue, IReadOnlyList<string> MatchedRouteIds, IReadOnlyList<SearchMatchWitness> Witnesses);
    private sealed record Case(string Id, string Context, string Workload, string Expected,
        ulong StartOrdinal, long ScanCount, int PairCandidates, int IdentityNegativeAudits, int ActualMaskChecks,
        int ConfirmedMatches, int Unverified, string Source, string Coverage);
    private sealed record Manifest(int SchemaVersion, string Oracle, int MathChecks,
        IReadOnlyList<Case> Cases, IReadOnlyList<object> PerformanceCases);

    internal static void Generate(string output, int window)
    {
        if (window is < 67 or > 1048576) throw new ArgumentOutOfRangeException(nameof(window));
        string Corpus(string relative) => ProjectSettings.GlobalizePath("res://Corpus/" + relative);
        var template = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Corpus("bones-k-sameroute-v1/counterexample.json"));
        CheckContinuationEdges(ProductionSearchReplay.Read<ProductionSearchReplayContext>(Corpus("n-direct-v1/context-defect-a10.json")), template, output);
        var cases = new List<Case>();
        int mathChecks = CheckShuffleArithmetic();
        void Add(string id, ProductionSearchReplayContext capture, SearchQuery query,
            string witnessSeed, string source, string coverage, int count = 0, bool arbitrary = false)
        {
            count = count == 0 ? window : count;
            ulong ordinal = Ordinal(witnessSeed);
            ulong start = arbitrary ? ordinal : ordinal - Math.Min(ordinal, 31UL);
            var options = template.Options with { StartSeed = Format(start), ScanCount = count,
                TargetMatchCount = int.MaxValue, WorkerCount = 1, IncludeDiagnostics = true };
            var request = Compile(capture, query, options);
            if (!arbitrary && !Exact(request, witnessSeed).IsMatch)
                throw new InvalidDataException("BonesHidden.ExpectedPositive:" + id + ":" + witnessSeed);
            string directory = Path.Combine(output, id); Directory.CreateDirectory(directory);
            string contextPath = Path.Combine(directory, "context.json"), workloadPath = Path.Combine(directory, "workload.json"),
                expectedPath = Path.Combine(directory, "expected.json");
            ProductionSearchReplay.Write(contextPath, capture);
            ProductionSearchReplay.Write(workloadPath, new ProductionSearchReplayWorkload(1, query, options));
            // Observe evaluates only Neow offer/Bones pair identity. Its code
            // never invokes StructuredConditions, capsule bags or continuation.
            var identity = NeowReplayPlan.Compile(request.Model, false);
            RelicFullGpuPlan? capsulePlan = null;
            BonesHiddenUpgradeContinuation? detector = null;
            if (RelicFullGpuPlan.TryCreateBonesKBoundary(request.Model, NeowReplayPlan.Compile(request.Model, true), out capsulePlan, out _, allowPinned: true) && capsulePlan is not null)
                detector = BonesHiddenUpgradeContinuation.Compile(request.Model, capsulePlan, 1);
            var expected = new List<OracleRow>();
            int pairs = 0, negativeAudits = 0, maskChecks = 0;
            for (int offset = 0; offset < count; offset++)
            {
                string seed = Format(start + (ulong)offset);
                ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
                bool pair = NeowFamilyReplay.Observe(root, identity).IdentityPass;
                if (!pair && !(negativeAudits < 32 && offset % Math.Max(1, count / 32) == 0)) continue;
                var truth = Exact(request, seed);
                if (!pair)
                {
                    negativeAudits++;
                    if (truth.Evaluation.Disposition != SearchDisposition.NoMatch)
                        throw new InvalidDataException("BonesHidden.IdentityPrerequisiteFailed:" + id + ":" + seed);
                    continue;
                }
                pairs++;
                var actualLargeFirst = truth.Facts?.Neow?.Choices.FirstOrDefault(c => c.BonesOutcome is not null)?.BonesOutcome?.OriginalRoutes
                    .FirstOrDefault(r => r.AcquisitionOrder.FirstOrDefault() == Large && Nested(r).Length == 2);
                if (pairs == 1) CompatibilityChecks.WriteEvidence(Path.Combine(directory, "first-identity-facts.json"), new {
                    Seed = seed, truth.Evaluation, Neow = truth.Facts?.Neow,
                    DirectCore = actualLargeFirst is null ? new NeowEffectProjectionEngine(Beta111Profile.Instance,
                        "bones-hidden-independent-diagnostic", root, capture.Context.Authority, true).Project(BaseGameModelKeys.Relics.NeowsBones) : null });
                if (detector is { Ready: true } && actualLargeFirst is not null)
                {
                    uint expectedMask = (Nested(actualLargeFirst).Contains(W) ? 1u : 0u) | (Nested(actualLargeFirst).Contains(WP) ? 2u : 0u);
                    uint actualMask = detector.ActualMask(root, NeowFamilyReplay.Observe(root, identity).RewardsAfterBones, capsulePlan!.CapsuleMetadata);
                    if (actualMask != expectedMask) throw new InvalidDataException("BonesHidden.ActualMaskMismatch:" + id + ":" + seed);
                    maskChecks++;
                }
                if (truth.Evaluation.Disposition == SearchDisposition.NoMatch) continue;
                if (truth.Evaluation.Disposition is not (SearchDisposition.Match or SearchDisposition.Unknown or SearchDisposition.Unsupported))
                    throw new InvalidDataException("BonesHidden.UnexpectedExactDisposition:" + truth.Evaluation.Disposition);
                expected.Add(new(seed, truth.Evaluation.Disposition, ValidationIssue(truth),
                    truth.Evaluation.MatchedRouteIds, truth.Evaluation.Witnesses));
            }
            if (detector is { Ready: true } && (pairs == 0 || maskChecks != pairs))
                throw new InvalidDataException("BonesHidden.IncompleteActualMaskCoverage:" + id + ":" + maskChecks + "/" + pairs);
            ProductionSearchReplay.Write(expectedPath, expected);
            cases.Add(new(id, contextPath, workloadPath, expectedPath, start, count, pairs, negativeAudits, maskChecks,
                expected.Count(r => r.ValidationDisposition == SearchDisposition.Match),
                expected.Count(r => r.ValidationDisposition != SearchDisposition.Match), source, coverage));
            Console.WriteLine($"BONES_HIDDEN_ORACLE case={id};roots={count};identityPairs={pairs};matches={expected.Count};actualMaskChecks={maskChecks};source=ExactAllIdentityPairs");
        }

        var contexts = new[] { ("ironclad", "v1/context.json"), ("defect", "n-direct-v1/context-defect-a10.json"),
            ("necro", "n-rewards-v1/context-necro-a0.json") };
        foreach (var (label, path) in contexts)
        {
            var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Corpus(path));
            var card = context.Context.Authority.EffectAuthority!.OtherCharacterPools!.SelectMany(p => p.Cards)
                .First(c => c.Rarity == EffectCardRarity.Common && c.CardType == EffectCardType.Attack).CardKey;
            var query = template.Query with { StructuredOpeningEffects = [
                template.Query.StructuredOpeningEffects[0] with { OutputKeys = [card] },
                template.Query.StructuredOpeningEffects[1] ] };
            Add(label + "-arbitrary-common", context, query, Format(0), path,
                "Unselected shared 2^20 root interval; common K target; partial neutral Large target; Any order", 1 << 20, arbitrary: true);
            var identity = NeowReplayPlan.Compile(Compile(context, query, template.Options).Model, false);
            bool neutralFound = false;
            for (ulong ordinal = 0; ordinal < (1UL << 18) && !neutralFound; ordinal++)
            {
                string seed = Format(ordinal);
                if (!NeowFamilyReplay.Observe(Beta111Profile.Instance.ComputeRootSeed(seed), identity).IdentityPass) continue;
                var projection = new NeowEffectProjectionEngine(Beta111Profile.Instance, "bones-hidden-neutral-discovery",
                    Beta111Profile.Instance.ComputeRootSeed(seed), context.Context.Authority, true).Project(BaseGameModelKeys.Relics.NeowsBones);
                var route = projection.BonesOutcome?.OriginalRoutes
                    .FirstOrDefault(r => Nested(r).Length == 2 && !Nested(r).Any(IsUpgrade));
                if (route is null) continue;
                var offered = route.RelicScopedResults.Where(r => r.SourceRelicKey == K).SelectMany(r => r.EffectGroups)
                    .SelectMany(g => g.OrderedItems).First(i => i.Kind == PredictedEffectKind.AddCard && i.TargetKey.HasValue).TargetKey!.Value;
                var neutral = query with { StructuredOpeningEffects = [query.StructuredOpeningEffects[0] with { OutputKeys = [offered] },
                    query.StructuredOpeningEffects[1] with { OutputKeys = [Nested(route)[0]] }] };
                Add(label + "-actual-no-wwp", context, neutral, seed, path,
                    "Outcome-derived ordinary neutral capsule control; actual neither W nor WP; Any order");
                neutralFound = true;
            }
            if (!neutralFound) throw new InvalidDataException("BonesHidden.NoNeutralWitness:" + label);
        }

        var sourceCases = new[] {
            ("authored-wp-positive", "n-direct-v1/context-defect-a10.json"),
            ("w-only-large-first", "n-direct-v1/context-defect-a10.json"),
            ("w-only-k-first", "n-direct-v1/context-defect-a10.json"),
            ("k-first-0", "n-direct-v1/context-defect-a10.json"),
            ("k-first-1", "n-direct-v1/context-defect-a10.json"),
            ("k-first-2", "n-direct-v1/context-defect-a10.json"),
            ("both-final-curse", "n-direct-v1/context-defect-a10.json"),
            ("context-IRONCLAD", "v1/context.json"),
            ("context-NECROBINDER", "n-rewards-v1/context-necro-a0.json") };
        foreach (var (name, contextPath) in sourceCases)
        {
            var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Corpus(contextPath));
            var source = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Corpus("wwp-commitment-v1/" + name + ".json"));
            var original = Compile(context, source.Query, source.Options);
            var truth = Exact(original, source.Options.StartSeed);
            if (!truth.IsMatch) throw new InvalidDataException("BonesHidden.SourceNotExactPositive:" + name);
            var bones = truth.Facts!.Neow!.Choices.First(c => c.BonesOutcome is not null).BonesOutcome!;
            var route = bones.OriginalRoutes.First(r =>
                (source.Query.OpeningRouteRelicRequirement!.OrderMode == BonesRouteOrderMode.AnyOrder ||
                 r.AcquisitionOrder.SequenceEqual(source.Query.OpeningRouteRelicRequirement.RequiredRelicKeys)) &&
                source.Query.StructuredOpeningEffects.Where(c => c.SourceRelicKey == Large).SelectMany(c => c.OutputKeys).All(Nested(r).Contains) &&
                Nested(r).Any(IsUpgrade));
            ModelKey[] actualRelics = Nested(route);
            if (actualRelics.Length != 2) throw new InvalidDataException("BonesHidden.ExpectedTwoRealNestedRelics:" + name);
            // Retain authored K commitments; when the old fixture explicitly
            // skipped K, author one of that route's actual offered cards. The
            // fresh Exact positive check above Add owns resulting truth.
            var authoredK = source.Query.StructuredOpeningEffects.FirstOrDefault(c => c.SourceRelicKey == K);
            var k = authoredK ?? template.Query.StructuredOpeningEffects[0] with { OutputKeys = [
                route.RelicScopedResults.Where(r => r.SourceRelicKey == K).SelectMany(r => r.EffectGroups)
                    .SelectMany(g => g.OrderedItems).First(i => i.Kind == PredictedEffectKind.AddCard && i.TargetKey.HasValue).TargetKey!.Value ] };
            var clean = source.Query with { LegacyNeow = source.Query.LegacyNeow with { RequiredFinalCurse = null },
                StructuredOpeningEffects = [k, template.Query.StructuredOpeningEffects[1] with { OutputKeys = actualRelics }] };
            Add(name + "-full-explicit", context, clean, source.Options.StartSeed, name,
                "Full actual capsule target; authored W/WP; original acquisition order; real K commitment");
            foreach (var upgrade in actualRelics.Where(IsUpgrade))
            {
                var hidden = clean with { StructuredOpeningEffects = [k,
                    template.Query.StructuredOpeningEffects[1] with { OutputKeys = actualRelics.Where(r => r != upgrade).ToArray() }] };
                string suffix = upgrade == W ? "hidden-w" : "hidden-wp";
                Add(name + "-" + suffix + "-pinned", context, hidden, source.Options.StartSeed, name,
                    "Partial capsule hides actual " + upgrade + "; original acquisition order");
                var any = hidden with { OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                    [Large, K], BonesRouteOrderMode.AnyOrder) };
                Add(name + "-" + suffix + "-any", context, any, source.Options.StartSeed, name,
                    "Partial capsule hides actual " + upgrade + "; Any acquisition; bounded Nr candidate where admitted");
            }
        }
        // Keep the literal reported query as well as outcome-derived cases.
        var defect = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Corpus(contexts[1].Item2));
        Add("owner-original-hidden-wp", defect, template.Query, template.Options.StartSeed,
            "bones-k-sameroute-v1/counterexample.json", "Literal reported hidden WP query; 67+ roots forces actual filters");
        object Performance(string id, string context, string workload) => new { Id = id,
            Context = Corpus(context), Workload = Corpus(workload), Oracle = "Full Search output comparison; not bounded Exact exhaustive" };
        var manifest = new Manifest(1,
            "Every seed in each closed range receives identity-only prerequisite; every admitted Bones pair receives Production Exact. No old N/R survivor oracle. Complete delivered seed/disposition/route/witness sets are compared.", mathChecks, cases,
            [ Performance("partial-2pow36", contexts[1].Item2, "bones-k-sameroute-v1/K-partial-long.json"),
              Performance("full-2pow34", contexts[1].Item2, "bones-capsule-boundary-v1/K-full.json"),
              Performance("common-2pow30", contexts[1].Item2, "bones-k-sameroute-v1/scoped-both-route.json") ]);
        ProductionSearchReplay.Write(Path.Combine(output, "manifest.json"), manifest);
        Console.WriteLine($"BONES_HIDDEN_ORACLE_READY cases={cases.Count};mathChecks={mathChecks};manifest={Path.Combine(output, "manifest.json")}");
    }

    internal static void Compare(string manifestPath, string output)
    {
        var manifest = ProductionSearchReplay.Read<Manifest>(manifestPath);
        var rows = new List<object>(); var failures = new List<string>();
        foreach (var item in manifest.Cases)
        {
            var expected = ProductionSearchReplay.Read<List<OracleRow>>(item.Expected).ToDictionary(x => x.Seed, StringComparer.Ordinal);
            string directory = Path.Combine(output, item.Id);
            string matchesPath = Path.Combine(directory, "run0.matches.jsonl");
            string summaryPath = Path.Combine(directory, "run0.summary.json");
            if (!File.Exists(matchesPath) || !File.Exists(summaryPath))
            { failures.Add(item.Id + ":MissingCompleteSearchOutput"); continue; }
            using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            var progress = summary.RootElement.GetProperty("progress");
            bool complete = progress.GetProperty("State").GetString() == "Completed" &&
                progress.GetProperty("ScannedCount").GetInt64() == item.ScanCount &&
                summary.RootElement.GetProperty("safeNextOrdinal").GetUInt64() == item.StartOrdinal + (ulong)item.ScanCount;
            var actual = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(matchesPath).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement.Clone();
                string seed = root.GetProperty("Seed").GetString()!;
                if (!actual.TryAdd(seed, root)) failures.Add(item.Id + ":Duplicate:" + seed);
            }
            var missing = expected.Keys.Except(actual.Keys).ToArray(); var extra = actual.Keys.Except(expected.Keys).ToArray();
            var witnessDifferences = new List<string>();
            foreach (string seed in actual.Keys.Intersect(expected.Keys))
            {
                var exp = expected[seed]; var got = actual[seed];
                string[] routes = got.GetProperty("MatchedRouteIds").Deserialize<string[]>(ProductionSearchReplay.JsonOptions)!;
                var witnesses = got.GetProperty("Witnesses").Deserialize<SearchMatchWitness[]>(ProductionSearchReplay.JsonOptions)!;
                if (got.GetProperty("ValidationDisposition").GetString() != exp.ValidationDisposition.ToString() ||
                    got.GetProperty("ValidationIssue").GetString() != exp.ValidationIssue ||
                    !routes.Order().SequenceEqual(exp.MatchedRouteIds.Order()) ||
                    !witnesses.Select(Witness).Order().SequenceEqual(exp.Witnesses.Select(Witness).Order()))
                    witnessDifferences.Add(seed);
            }
            bool pass = complete && missing.Length == 0 && extra.Length == 0 && witnessDifferences.Count == 0;
            if (!pass) failures.Add(item.Id);
            rows.Add(new { item.Id, Passed = pass, Complete = complete, Expected = expected.Count, Actual = actual.Count,
                Missing = missing, Extra = extra, WitnessDifferences = witnessDifferences });
        }
        CompatibilityChecks.WriteEvidence(Path.Combine(output, "bones-hidden-comparison.json"), new { Passed = failures.Count == 0, Cases = rows, Failures = failures });
        if (failures.Count > 0) throw new InvalidDataException("BonesHidden.CompleteSearchMismatch:" + string.Join(',', failures));
        Console.WriteLine("BONES_HIDDEN_COMPLETE_SEARCH_PASS cases=" + rows.Count + ";seeds=true;dispositions=true;routes=true;witnesses=true;cursors=true");
    }

    private static string Witness(SearchMatchWitness w) => JsonSerializer.Serialize(new { w.Seed, w.OpeningRouteId,
        AcquisitionOrder = w.AcquisitionOrder.Select(k => k.Serialized).ToArray(),
        MatchedConditions = w.MatchedConditions.Order().ToArray(), w.RewardContinuationFingerprint,
        w.MatchedBattleOrdinal, EvidenceCodes = w.EvidenceCodes.Select(e => e.Value).Order().ToArray(),
        w.RewardRouteGroupId, w.ChoicePolicyId, w.OutcomeId });
    private static ModelKey[] Nested(BonesAcquisitionRouteResult r) => r.RelicScopedResults.Where(x => x.SourceRelicKey == Large)
        .SelectMany(x => x.EffectGroups).SelectMany(g => g.OrderedItems)
        .Where(i => i.Kind == PredictedEffectKind.AddRelic && i.Relation == PredictedEffectRelation.NestedRelic && i.TargetKey.HasValue)
        .Select(i => i.TargetKey!.Value).ToArray();
    private static bool IsUpgrade(ModelKey key) => key == W || key == WP;
    private static string Format(ulong ordinal) => VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
    private static ulong Ordinal(string seed) => VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, seed, out ulong value, out _, out string issue)
        ? value : throw new InvalidDataException("BonesHidden.Seed:" + issue);
    private static PreparedSearch Compile(ProductionSearchReplayContext context, SearchQuery query, SearchRunOptions options) =>
        SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context.Context), options).Plan
        ?? throw new InvalidDataException("BonesHidden.Compile");
    private static ProductionExactSearchResult Exact(PreparedSearch request, string seed) =>
        ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed));
    private static string ValidationIssue(ProductionExactSearchResult result)
    {
        if (result.IsMatch) return "";
        string issue = string.IsNullOrWhiteSpace(result.Evaluation.FailureCode) ? result.FailureCode : result.Evaluation.FailureCode;
        if (string.IsNullOrWhiteSpace(issue)) issue = "Validation." + result.Evaluation.Disposition;
        if (result.Evaluation.RouteRejections.Count > 0)
            issue += "\n" + string.Join("\n", result.Evaluation.RouteRejections.Select(r => $"route={r.RouteId};disposition={r.Disposition};reason={r.Reason}"));
        return issue;
    }
    private static int CheckShuffleArithmetic()
    {
        int checks = 0;
        foreach (ulong seed in new ulong[] { 0, 19, ulong.MaxValue })
        for (int count = 0; count <= 16; count++)
        {
            var original = new Xoshiro256StarStar(seed); original.UnstableShuffle(Enumerable.Range(0, count).ToList());
            var fast = new Beta110FastRng(seed);
            for (int i = 0; i < NeowAuthoredUpgradeContinuation.Draws(count); i++) fast.NextInt(2);
            var state = fast.CaptureCheckpoint();
            if (original.State != (state.S0, state.S1, state.S2, state.S3))
                throw new InvalidDataException("BonesHidden.ShuffleState:" + count);
            checks++;
        }
        if (NeowAuthoredUpgradeContinuation.Draws(0) != 0 || NeowAuthoredUpgradeContinuation.Draws(1) != 0)
            throw new InvalidDataException("BonesHidden.ZeroOneEligibleDraws");
        return checks + 2;
    }

    // Artificial copied decks exercise the numerical preparation boundary only;
    // they are not captured game cases and do not enter the Search oracle.
    private static void CheckContinuationEdges(ProductionSearchReplayContext capture,
        ProductionSearchReplayWorkload template, string output)
    {
        var request = Compile(capture, template.Query, template.Options);
        var route = NeowReplayPlan.Compile(request.Model, true);
        if (!RelicFullGpuPlan.TryCreateBonesKBoundary(request.Model, route, out var capsule, out string issue) || capsule is null)
            throw new InvalidDataException("BonesHidden.HelperEdgePreparation:" + issue);
        var effects = request.Model.Authority.EffectAuthority!;
        var rows = new List<object>();
        SearchModel WithDeck(IReadOnlyList<NeowEffectCardSnapshot>? deck) => request.Model with {
            CompiledSearch = request.Model.CompiledSearch with { Context = capture.Context with {
                Authority = capture.Context.Authority.WithEffectAuthority(effects with { OrderedDeck = deck }) } } };
        for (int attacks = 0; attacks <= 2; attacks++)
        for (int skills = 0; skills <= 2; skills++)
        {
            var deck = effects.OrderedDeck!.Where(c => c.CardType == EffectCardType.Attack && c.CanUpgrade).Take(attacks)
                .Concat(effects.OrderedDeck!.Where(c => c.CardType == EffectCardType.Skill && c.CanUpgrade).Take(skills)).ToArray();
            var helper = BonesHiddenUpgradeContinuation.Compile(WithDeck(deck), capsule, 1);
            int attackDraws = Math.Max(0, attacks - 1), skillDraws = Math.Max(0, skills - 1);
            if (!helper.Ready || helper.AttackDraws != attackDraws || helper.SkillDraws != skillDraws ||
                helper.Advance(0) != 0 || helper.Advance(1) != attackDraws || helper.Advance(2) != skillDraws ||
                helper.Advance(3) != attackDraws + skillDraws ||
                !helper.PossibleMask(0) || !helper.PossibleMask(1) || !helper.PossibleMask(2) || helper.PossibleMask(3))
                throw new InvalidDataException($"BonesHidden.HelperZeroOneEdge:attack={attacks};skill={skills}");
            rows.Add(new { Synthetic = true, attacks, skills, helper.Ready, helper.AttackDraws, helper.SkillDraws,
                helper.ExplicitMask, helper.UnknownSlots });
        }
        ulong fixtureRoot = Beta111Profile.Instance.ComputeRootSeed(template.Options.StartSeed);
        if (!Exact(request, template.Options.StartSeed).IsMatch)
            throw new InvalidDataException("BonesHidden.HelperSourceMustBeRealPositive");
        var missingModel = WithDeck(null);
        var missingReplay = NeowReplayPlan.Compile(missingModel, false);
        var missingObservation = NeowFamilyReplay.Observe(fixtureRoot, missingReplay);
        var missing = BonesHiddenUpgradeContinuation.Compile(missingModel, capsule, 1);
        bool missingSharedRoute = NeowFamilyReplay.Route(fixtureRoot, missingReplay, Beta110FastRelicCatalog.LargeCapsule,
            Beta110FastRelicCatalog.Kaleidoscope, missingObservation.RewardsAfterBones);
        bool missingRetained = missing.LargeFirstMatches(fixtureRoot, missingReplay, missingObservation.RewardsAfterBones, capsule.CapsuleMetadata);
        var sparseMissingDeck = NeowCapsuleSparseRefinement.Compile(missingModel, missingReplay);
        var sparseMissingDeckOutcome = sparseMissingDeck.Evaluate(fixtureRoot);
        if (!sparseMissingDeckOutcome.Keep || sparseMissingDeckOutcome.Keep != NeowSingleplayerReplay.Matches(fixtureRoot, missingReplay))
            throw new InvalidDataException("CapsuleSparse.MissingDeckUnknownMustRetain");
        if (missing.Ready || !missingObservation.IdentityPass || !missingRetained ||
            missingReplay.CapsuleNicheContinuation is not null && missingRetained != missingSharedRoute)
            throw new InvalidDataException("BonesHidden.HelperMissingDeckMustRetain");
        object? sharedMissingDeck = null;
        if (BonesHiddenUpgradeContinuation.CaptureMode() != 0)
        {
            var incompleteReplay = missingReplay;
            var continuation = incompleteReplay.CapsuleNicheContinuation;
            if (continuation is null || !continuation.Advances(Beta110FastRelicCatalog.LargeCapsule, 255).SequenceEqual([-1]) ||
                !continuation.Remainders.Contains("CapsuleNicheUpgradeDeckAuthorityUnavailable"))
                throw new InvalidDataException("BonesHidden.SharedNicheMissingDeckRemainder");
            var observed = missingObservation;
            bool retained = NeowFamilyReplay.Route(fixtureRoot, incompleteReplay, Beta110FastRelicCatalog.LargeCapsule,
                Beta110FastRelicCatalog.Kaleidoscope, observed.RewardsAfterBones);
            if (!retained) throw new InvalidDataException("BonesHidden.SharedNicheUnknownArrivalMustRetain");
            sharedMissingDeck = new { Advances = continuation.Advances(Beta110FastRelicCatalog.LargeCapsule, 255),
                continuation.Remainders, Retained = retained };
        }
        var world = capture.Context.Authority.WorldAuthority!;
        var generation = world.Beta109Generation!;
        var missingPoolRows = new List<object>();
        foreach (var absent in new[] { W, WP })
        {
            // RelicPoolCompilation reads Beta109Generation's shared/player
            // ordered buckets, not EffectAuthority's source pool lists. Remove
            // this key from both scopes to exercise the absent dense-ID branch
            // while retaining otherwise compilable ordinary rarity lanes.
            var incompleteWorld = world with { Beta109Generation = generation with {
                SharedRelicBuckets = generation.SharedRelicBuckets.Select(b => b with {
                    OrderedRelics = b.OrderedRelics.Where(key => key != absent).ToArray(),
                    OrderedEntries = b.OrderedEntries.Where(e => e.RelicKey != absent).ToArray() }).ToArray(),
                PlayerRelicBuckets = generation.PlayerRelicBuckets.Select(b => b with {
                    OrderedRelics = b.OrderedRelics.Where(key => key != absent).ToArray(),
                    OrderedEntries = b.OrderedEntries.Where(e => e.RelicKey != absent).ToArray() }).ToArray() } };
            var incompleteModel = request.Model with { CompiledSearch = request.Model.CompiledSearch with {
                Context = capture.Context with { Authority = capture.Context.Authority.WithWorldAuthority(incompleteWorld) } } };
            bool poolCompiled = RelicFamilyPlanCompiler.TryCompilePool(incompleteModel, out _, out var dense, out _, out string poolIssue);
            if (!poolCompiled || dense.ContainsKey(absent))
                throw new InvalidDataException("BonesHidden.MissingPoolFixtureNotIsolated:" + absent + ":" + poolIssue);
            var unavailable = BonesHiddenUpgradeContinuation.Compile(incompleteModel, capsule, 1);
            var incompleteReplay = NeowReplayPlan.Compile(incompleteModel, false);
            var observed = NeowFamilyReplay.Observe(fixtureRoot, incompleteReplay);
            bool sharedRoute = NeowFamilyReplay.Route(fixtureRoot, incompleteReplay, Beta110FastRelicCatalog.LargeCapsule,
                Beta110FastRelicCatalog.Kaleidoscope, observed.RewardsAfterBones);
            bool retained = unavailable.LargeFirstMatches(fixtureRoot, incompleteReplay, observed.RewardsAfterBones, capsule.CapsuleMetadata);
            var sparseMissingPool = NeowCapsuleSparseRefinement.Compile(incompleteModel, incompleteReplay);
            var sparseMissingPoolOutcome = sparseMissingPool.Evaluate(fixtureRoot);
            if (sparseMissingPool.Ready || !sparseMissingPoolOutcome.Keep ||
                sparseMissingPoolOutcome.Keep != NeowSingleplayerReplay.Matches(fixtureRoot, incompleteReplay))
                throw new InvalidDataException("CapsuleSparse.MissingPoolObservationMustRetain:" + absent);
            if (unavailable.Ready || !observed.IdentityPass || !retained ||
                incompleteReplay.CapsuleNicheContinuation is not null && retained != sharedRoute)
                throw new InvalidDataException("BonesHidden.MissingPoolObservationMustRetain:" + absent);
            missingPoolRows.Add(new { Synthetic = true, Absent = absent, PoolCompiled = poolCompiled,
                DenseKeyPresent = dense.ContainsKey(absent), unavailable.Ready, Retained = retained, SharedRoute = sharedRoute,
                SparseReady = sparseMissingPool.Ready, SparseIssue = sparseMissingPool.Issue, SparseOutcome = sparseMissingPoolOutcome });
        }
        CompatibilityChecks.WriteEvidence(Path.Combine(output, "helper-edge-checks.json"), new {
            Passed = true, Scope = "Synthetic copied deck preparation and zero/one eligible arithmetic; no game reachability or Search claim",
            Cases = rows, MissingDeckReady = missing.Ready, MissingDeckRetained = missingRetained, MissingDeckSharedRoute = missingSharedRoute,
            SharedNicheMissingDeck = sharedMissingDeck,
            SparseMissingDeck = new { sparseMissingDeck.Ready, sparseMissingDeck.Issue, Outcome = sparseMissingDeckOutcome },
            MissingPoolObservations = missingPoolRows });
        Console.WriteLine("BONES_HIDDEN_HELPER_EDGES_PASS syntheticCases=9;missingDeckRetained=true;missingWAndWpPoolRetained=true");
    }
}
