using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class MorphicFilterChecks
{
    private static void EventSemanticClosure(SearchContext context, SearchQuery fragile, Action<bool, string> check)
    {
        var options = new SearchRunOptions("000000000000", 1027, int.MaxValue, 1);
        ExactSearchExecutionRequest Request(SearchQuery q, SearchContext? ctx = null) =>
            ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q, ctx ?? context), options).Plan!;
        ModelKey[] Queue(string seed) => WorldPredictionEngine.Predict(Beta111Profile.Instance, seed, context.Ascension,
            0, context.CharacterKey, context.Authority.WorldAuthority, context.Authority.EffectAuthority)
            .EventPoolSequencePrediction!.Acts.Single(a => a.Act == 1).Entries.OrderBy(e => e.Ordinal).Select(e => e.EventKey).ToArray();
        var initial = Queue(options.StartSeed);
        var a = initial[0]; var b = initial[1]; var c = initial[4];
        EventSequenceSearchCondition Row(SearchSequenceRangeMode mode, int n, ModelKey key, bool ban = false) =>
            new(1, null, mode, n, ban ? new([], [], [key]) : new([key], [], []));
        var empty = fragile with { EventResultConditions = [] };
        var ordered = empty with { EventSequenceConstraints = [Row(SearchSequenceRangeMode.ExactSlot, 1, a), Row(SearchSequenceRangeMode.ExactSlot, 2, b)] };
        var unordered = empty with { EventSequenceConstraints = [Row(SearchSequenceRangeMode.FirstN, 3, a), Row(SearchSequenceRangeMode.FirstN, 3, b)] };
        var advanced = empty with { EventSequenceConstraints = [Row(SearchSequenceRangeMode.FirstN, 3, a), Row(SearchSequenceRangeMode.FirstN, 2, c, true)] };
        var stable = empty with { EventResultConditions = [new(EventResultConditionKind.TrashHeapGrabCard, Beta111EventResultCatalog.TrashHeapGrabCards[0])] };
        var mixed = fragile with { EventSequenceConstraints = unordered.EventSequenceConstraints };
        var queries = new[] { ordered, unordered, advanced, stable, fragile, mixed };
        var requests = queries.Select(q => Request(q)).ToArray();
        check(requests.Select(r => r.CompiledSearch.SemanticFingerprint).Distinct().Count() == queries.Length, "Distinct W/E/order/advanced semantic fingerprints");
        foreach (var q in queries)
        {
            var restored = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(q, ProductionSearchReplay.JsonOptions), ProductionSearchReplay.JsonOptions)!;
            check(Request(q).SnapshotFingerprint == Request(restored).SnapshotFingerprint, "Event Query JSON/cursor identity roundtrip");
        }
        var replays = requests.Take(3).Select(r => new WorldFamilyReplay(r)).ToArray();
        var stablePlan = new EventResultFamilyPlan(requests[3]);
        var fragilePlan = new EventResultFamilyPlan(requests[4]);
        // E-only must still execute with all World authority removed. The scenario's
        // event-entry immutable authority remains its own explicit input.
        var noWorld = context with { Authority = context.Authority.WithWorldAuthority(null) };
        var noWorldStable = Request(stable, noWorld);
        var noWorldFragile = Request(fragile, noWorld);
        int[] hits = new int[6]; int eWithoutW = 0;
        var expected = Enumerable.Range(0, 6).Select(_ => new List<string>()).ToArray();
        for (ulong i = 0; i < (ulong)options.ScanCount; i++)
        {
            string seed = Beta110SeedCodec.FormatOrdinal(i);
            var root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
            var queue = Queue(seed);
            bool[] truth = [queue[0] == a && queue[1] == b,
                queue.Take(3).Contains(a) && queue.Take(3).Contains(b),
                queue.Take(3).Contains(a) && !queue.Take(2).Contains(c),
                stablePlan.Reference(root.RootHash), fragilePlan.Reference(root.RootHash), false];
            truth[5] = truth[1] && truth[4];
            for (int j = 0; j < requests.Length; j++)
            {
                var exact = ProductionExactSearchEvaluator.Evaluate(requests[j], root);
                check(exact.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch, "Event Exact resolved");
                check(exact.IsMatch == truth[j], "Authored Event predicate matches reference / W AND E only");
                if (j < 3) check(replays[j].Matches(root.RootHash) == truth[j], "W numerical slots/prefix parity");
                if (exact.IsMatch) { hits[j]++; expected[j].Add(seed); check(exact.Evaluation.Witnesses.Count > 0, "Event Exact Witness"); }
            }
            check(ProductionExactSearchEvaluator.Evaluate(noWorldStable, root).IsMatch == truth[3], "Stable E has no World proof obligation");
            check(ProductionExactSearchEvaluator.Evaluate(noWorldFragile, root).IsMatch == truth[4], "Fragile E has no World/deck-path proof obligation");
            if (truth[4] && !truth[1]) eWithoutW++;
        }
        check(hits.Take(5).All(n => n > 0), "Positive simple/advanced/stable/fragile witnesses");
        check(hits[1] > hits[0] && eWithoutW > 0, "Ordered/unordered discrimination and E matches outside W");
        string output = ".build-evidence/event-consolidation"; Directory.CreateDirectory(output);
        string[] names = ["ordered", "unordered", "advanced", "stable-E", "fragile-E", "WE"];
        for (int j = 0; j < queries.Length; j++)
        {
            File.WriteAllText($"{output}/{names[j]}.json", JsonSerializer.Serialize(new ProductionSearchReplayWorkload(1, queries[j], options), ProductionSearchReplay.JsonOptions));
            File.WriteAllText($"{output}/expected-{names[j]}.json", JsonSerializer.Serialize(expected[j], ProductionSearchReplay.JsonOptions));
        }
        Console.WriteLine($"EVENT_SEMANTIC_CLOSURE PASS roots={options.ScanCount};hits={string.Join(',', hits)};EOutsideW={eWithoutW};noWorldAuthority=PASS;JSON+fingerprint=PASS");
    }
}
