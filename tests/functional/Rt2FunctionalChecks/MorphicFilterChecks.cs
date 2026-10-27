using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class MorphicFilterChecks
{
    internal static int Run()
    {
        var rootDirectory = new DirectoryInfo(Environment.CurrentDirectory);
        while (rootDirectory is not null && !File.Exists(Path.Combine(rootDirectory.FullName, "RolltheSpire2.csproj"))) rootDirectory = rootDirectory.Parent;
        if (rootDirectory is not null) Environment.CurrentDirectory = rootDirectory.FullName;
        int checks = 0;
        void Check(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException(name); }
        var json = ProductionSearchReplay.JsonOptions;
        var replay = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        var context = replay.Context;
        using var source = JsonDocument.Parse(File.ReadAllText("docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json"));
        var saved = source.RootElement.GetProperty("Evidence")[0];
        var targets = saved.GetProperty("Targets").Deserialize<MorphicGroveTarget[]>(json)!;
        targets = targets.Select((t, i) => t with { InstanceId = "basic:" + i }).ToArray();
        var premises = new MorphicGrovePremises("Authored event-entry scenario: Morphic Grove Group, two legal initial basics remain; source pools valid at entry. No path/deck history proof.", true, false, null);
        var scenario = new MorphicGroveScenario(context.Authority, premises, targets);
        var condition = new EventResultSearchCondition(EventResultConditionKind.MorphicGroveGroupInitialBasicsContains,
            new("CARD", "THUNDERCLAP")) { MorphicGroveScenario = scenario };
        var query = LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with { EventResultConditions = [condition] });
        var options = new SearchRunOptions("000000000000", 4099, int.MaxValue, 1);
        ExactSearchExecutionRequest Request(SearchQuery q, SearchRunOptions? o = null)
        {
            var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q, context), o ?? options);
            if (!compiled.Success) throw new InvalidOperationException(compiled.Issue);
            return compiled.Plan!;
        }
        var request = Request(query);
        var plan = new EventResultFamilyPlan(request);
        Check(plan.GpuSupported, "Closed E GPU admitted");
        var roundtrip = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(query, json), json)!;
        Check(Request(roundtrip).SnapshotFingerprint == request.SnapshotFingerprint, "Query/scenario replay fingerprint");
        var changed = condition with { MorphicGroveScenario = new(context.Authority, premises with { EventOccurrenceBasis = "Different authored premise" }, targets) };
        Check(Request(query with { EventResultConditions = [changed] }).SnapshotFingerprint != request.SnapshotFingerprint, "Premise fingerprint");
        changed = condition with { MorphicGroveScenario = new(context.Authority, premises, targets.Select(t => t with { OrderedSourceCandidates = t.OrderedSourceCandidates!.Reverse().ToArray() }).ToArray()) };
        Check(Request(query with { EventResultConditions = [changed] }).SnapshotFingerprint != request.SnapshotFingerprint, "Pool order fingerprint");
        var wrong = condition with { MorphicGroveScenario = new(RolltheSpire2.Tests.FastSearch.Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 9, 0, 1), premises, targets) };
        try { Request(query with { EventResultConditions = [wrong] }); throw new Exception("Missing context rejection"); }
        catch (ArgumentException) { checks++; }
        Check(!(condition with { MorphicGroveScenario = new(context.Authority, premises, [targets[0], targets[1] with { Original = targets[1].Original with { Rarity = "Rare" } }]) }).IsValid, "Nonbasic rejected");
        Check(!(condition with { MorphicGroveScenario = new(context.Authority, premises, [targets[0], targets[1] with { OrderedSourceCandidates = targets[1].OrderedSourceCandidates!.Reverse().ToArray() }]) }).IsValid, "Heterogeneous pools rejected");
        var expected = new List<ulong>();
        var exactSeeds = new List<string>();
        var watch = Stopwatch.StartNew();
        for (ulong i = 0; i < (ulong)options.ScanCount; i++)
        {
            string seed = Beta110SeedCodec.FormatOrdinal(i);
            var root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
            var exact = ProductionExactSearchEvaluator.Evaluate(request, root);
            Check(exact.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch, "Exact resolved");
            Check(plan.Matches(root.RootHash) == exact.IsMatch && plan.Reference(root.RootHash) == exact.IsMatch, "Full E population parity");
            if (exact.IsMatch) { expected.Add(i); exactSeeds.Add(seed); Check(exact.Evaluation.Witnesses.Count > 0, "Exact Witness"); }
        }
        Check(expected.TakeWhile(i => i < 128).SequenceEqual(new ulong[] {74}), "Frozen source interval ordinal 74");
        Check(expected.Count > 1 && expected.Count < options.ScanCount, "Discriminating interval");
        double one = 1d / targets[0].OrderedSourceCandidates!.Count;
        Check(Math.Abs(plan.Survival.SurvivalProbability!.Value - (1 - (1 - one) * (1 - one))) < 1e-12, "Modeled two-draw survival");
        Console.WriteLine($"MORPHIC_E_PARITY roots={options.ScanCount};accepted={expected.Count};falseNegative=0;referenceMs={watch.Elapsed.TotalMilliseconds:F2}");

        var execution = new FamilyExecutionContext(false);
        EventResultFamily.TryCreate(request, out var family);
        var cpu = family!.CpuRealizations.Single();
        var batch = new SearchBatch(0, (int)options.ScanCount);
        var dense = FamilyCandidateSet.Dense(batch);
        var actual = cpu.InvokeAsync(execution, new(batch, request), dense, default).GetAwaiter().GetResult();
        Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), "Real CPU E invocation ABI1");
        var sparse = FamilyCandidateSet.FromAbi1(batch, expected.Where((_, i) => i % 2 == 0).ToArray());
        Check(cpu.InvokeAsync(execution, new(batch, request), sparse, default).GetAwaiter().GetResult().Count == sparse.Count, "Compact input");
        var zero = FamilyCandidateSet.FromAbi1(batch, []);
        Check(cpu.InvokeAsync(execution, new(batch, request), zero, default).GetAwaiter().GetResult().Count == 0, "Zero input");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { cpu.InvokeAsync(execution, new(batch, request), dense, cancel.Token).GetAwaiter().GetResult(); throw new Exception("Missing cancellation"); }
        catch (OperationCanceledException) { checks++; }
        Check(cpu.InvokeAsync(execution, new(batch, request), dense, default).GetAwaiter().GetResult().Count == expected.Count, "Invocation reuse after cancel");
        cpu.DisposeAsync(execution).GetAwaiter().GetResult();
        execution.DisposeAsync().GetAwaiter().GetResult();

        var unknown = condition with { MorphicGroveScenario = new(context.Authority, premises with { VanillaLocalDependenciesBound = false }, targets) };
        var unpriced = Request(query with { EventResultConditions = [unknown] });
        Check(!new EventResultFamilyPlan(unpriced).GpuSupported && new EventResultFamilyPlan(unpriced).Matches(123), "Missing facts conservative CPU only");
        Check(ProductionExactSearchEvaluator.Evaluate(unpriced, TrustedRootHashInput.RootOnly(123)).Evaluation.Disposition == SearchDisposition.Unknown, "Unknown cannot become Match");
        Check(EventResultPhysicalPricing.Quote(plan, new(false, false, 1048576)) is null, "New work cannot inherit old one-draw quote");

        // Independent captured source pools across all five characters. This uses
        // synthetic context identity for the pure reference probe, not runtime timing evidence.
        foreach (var entry in source.RootElement.GetProperty("Evidence").EnumerateArray()
                     .Where(e => e.GetProperty("PlayerCount").GetInt32() == 1 && e.GetProperty("Unlocks").GetString() == "all")
                     .GroupBy(e => e.GetProperty("Character").GetString()).Select(g => g.First()))
        {
            var a = RolltheSpire2.Tests.FastSearch.Beta110FastSearchFixtureAuthority.CreateBeta111(new("CHARACTER", entry.GetProperty("Character").GetString()!), 10, 0, 1);
            var ts = entry.GetProperty("Targets").Deserialize<MorphicGroveTarget[]>(json)!.Select((t, i) => t with { InstanceId = "basic:" + i }).ToArray();
            var sc = new MorphicGroveScenario(a, premises, ts);
            foreach (var key in ts[0].OrderedSourceCandidates!.GroupBy(c => c.Rarity).Select(g => g.First().CardKey).Append(new("CARD", "ABSENT")))
            {
                var co = new EventResultSearchCondition(condition.Kind, key) { MorphicGroveScenario = sc };
                var q = query with { EventResultConditions = [co] };
                var c = SearchCompiler.Compile(q, context with { Authority = a, CharacterKey = a.Character.CharacterKey });
                var rq = ExactSearchExecutionRequestFactory.Compile(c, options).Plan!;
                var ep = new EventResultFamilyPlan(rq);
                for (ulong i = 1234567; i < 1234824; i++)
                {
                    var root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, Beta110SeedCodec.FormatOrdinal(i));
                    var exact = ProductionExactSearchEvaluator.Evaluate(rq, root);
                    Check(exact.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch, "Five-character Exact resolved");
                    Check(ep.Matches(root.RootHash) == exact.IsMatch, "Five-character common/uncommon/rare/absent parity");
                }
            }
        }

        var output = ".build-evidence/morphic-filter";
        Directory.CreateDirectory(output);
        File.WriteAllText(output + "/E.json", JsonSerializer.Serialize(new ProductionSearchReplayWorkload(1, query, options), json));
        File.WriteAllText(output + "/expected-E.json", JsonSerializer.Serialize(exactSeeds, json));
        // Existing W queue membership is the supported occurrence/identity surface;
        // this does not assert map reachability or event-entry deck survival.
        var we = query with { EventSequenceConstraints = [new(1, null, SearchSequenceRangeMode.FirstN, 3,
            new ModelKeySetFilter([], [new("EVENT", "MORPHIC_GROVE")], []))] };
        var weRequest = Request(we);
        var wePlan = FamilyExecutionCoordinator.Plan(weRequest, gpuAvailable: false);
        Check(wePlan.OrderedFamilies.Select(f => f.FamilyId).Order().SequenceEqual(new[] {"E.EventResult", "W.World"}), "Normal W/E registration and CPU allocation");
        var weExpected = new List<string>();
        foreach (string seed in exactSeeds)
        {
            var exact = ProductionExactSearchEvaluator.Evaluate(weRequest, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed));
            Check(exact.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch, "WE Exact resolved");
            if (exact.IsMatch) weExpected.Add(seed);
        }
        Check(weExpected.Count > 0, "Positive W+E composition");
        File.WriteAllText(output + "/WE.json", JsonSerializer.Serialize(new ProductionSearchReplayWorkload(1, we, options), json));
        File.WriteAllText(output + "/expected-WE.json", JsonSerializer.Serialize(weExpected, json));
        var positiveRoot = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, weExpected[0]);
        var positive = Beta111MorphicGroveProjector.Project(positiveRoot.RootHash, MorphicGroveCommitment.InitialBasics, premises, targets);
        var cardA = positive.Transforms[0].RawReplacement!.CardKey;
        var cardB = positive.Transforms[1].RawReplacement!.CardKey;
        Check(cardA != cardB, "Discriminating different-card pair fixture");
        foreach (bool duplicate in new[] {false, true})
        {
            var pair = condition with { TargetKey = cardA, MorphicGroveSecondCard = duplicate ? cardA : cardB };
            var pairQuery = query with { EventResultConditions = [pair] };
            var pairOptions = options with { ScanCount = 65539 };
            var pairRequest = Request(pairQuery, pairOptions);
            var pairPlan = new EventResultFamilyPlan(pairRequest);
            var reversed = pair with { TargetKey = pair.MorphicGroveSecondCard!.Value, MorphicGroveSecondCard = pair.TargetKey };
            Check(Request(query with { EventResultConditions = [reversed] }, pairOptions).SnapshotFingerprint == pairRequest.SnapshotFingerprint, "Unordered pair fingerprint");
            Check(Request(query with { EventResultConditions = [pair with { MorphicGroveSecondCard = null }] }, pairOptions).SnapshotFingerprint != pairRequest.SnapshotFingerprint, "Multiplicity changes Query identity");
            Check(Math.Abs(pairPlan.Survival.SurvivalProbability!.Value - (duplicate ? one * one : 2 * one * one)) < 1e-12, "Unordered pair probability");
            var accepted = new List<string>();
            for (ulong i = 0; i < (ulong)pairOptions.ScanCount; i++)
            {
                var root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, Beta110SeedCodec.FormatOrdinal(i));
                var exact = ProductionExactSearchEvaluator.Evaluate(pairRequest, root);
                Check(exact.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch, "Pair Exact resolved");
                Check(pairPlan.Matches(root.RootHash) == exact.IsMatch, "Full pair population/Exact parity");
                if (exact.IsMatch) accepted.Add(root.CanonicalSeed);
            }
            Check(accepted.Count > 0, "Positive pair and duplicate witnesses");
            string name = duplicate ? "pair-duplicate" : "pair-distinct";
            File.WriteAllText(output + "/" + name + ".json", JsonSerializer.Serialize(new ProductionSearchReplayWorkload(1, pairQuery, pairOptions), json));
            File.WriteAllText(output + "/expected-" + name + ".json", JsonSerializer.Serialize(accepted, json));
            if (!duplicate)
            {
                var mixed = pairQuery with { EventSequenceConstraints = we.EventSequenceConstraints };
                var mixedRequest = Request(mixed, pairOptions);
                var both = accepted.Where(seed => ProductionExactSearchEvaluator.Evaluate(mixedRequest,
                    TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch).ToArray();
                Check(both.Length > 0, "Positive WE pair");
                File.WriteAllText(output + "/pair-WE.json", JsonSerializer.Serialize(new ProductionSearchReplayWorkload(1, mixed, pairOptions), json));
                File.WriteAllText(output + "/expected-pair-WE.json", JsonSerializer.Serialize(both, json));
            }
            Console.WriteLine($"MORPHIC_PAIR {name};roots={pairOptions.ScanCount};accepted={accepted.Count};falseNegative=0");
        }
        Check(Beta111MorphicGroveProjector.ContainsPair(positive, cardA, cardA, false) == MorphicGrovePredicateResult.NoMatch, "One copy is not two copies");
        var partialPair = positive with { Transforms = [positive.Transforms[0], positive.Transforms[1] with { RawReplacement = null }] };
        Check(Beta111MorphicGroveProjector.ContainsPair(partialPair, cardA, cardB, false) == MorphicGrovePredicateResult.Unknown, "Partial pair remains Unknown");
        Check(Beta111MorphicGroveProjector.ContainsPair(partialPair, cardB, cardB, false) == MorphicGrovePredicateResult.NoMatch, "Known mismatching card disproves pair");
        Console.WriteLine($"MORPHIC_WE_EXACT roots={options.ScanCount};accepted={weExpected.Count};plan={wePlan.FormatSummary()}");
        EventSemanticClosure(context, query, Check);
        EventWhitelistClosure(context, scenario, Check);
        Console.WriteLine($"MORPHIC_FILTER PASS checks={checks};corpus={output}");
        return 0;
    }
}
