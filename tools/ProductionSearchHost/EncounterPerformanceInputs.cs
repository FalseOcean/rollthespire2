using System.Security.Cryptography;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class CompatibilityChecks
{
    // Input authoring only. Every new queue target comes from Production Core at
    // a previously delivered N+C seed, and every composed anchor is checked by Exact.
    internal static void ExportEncounterPerformanceInputs(ProductionSearchReplayContext captured,
        ProductionSearchReplayWorkload source, string anchorSeed, string output, long longCount = 268435459)
    {
        Require(captured.Context.Party is null && source.Query.EncounterQueues.Count == 0 &&
            !source.Options.SkipExactValidation, "EncounterPerf.SoloExistingQueryWithExactRequired");
        string ProductHash() => typeof(SearchCompiler).Assembly.Location is { Length: > 0 } path
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "unavailable:byte-loaded";
        // Explicit later-run fixture fact, shared by both base and extended
        // requests. No missing discovery fact is silently treated as known.
        var world = captured.Context.Authority.WorldAuthority! with { EncounterNumberOfRuns = 1 };
        var context = captured.Context with { Authority = captured.Context.Authority.WithWorldAuthority(world) };
        var exportedContext = captured with { Context = context, Provenance = captured.Provenance +
            ";H01.W.PerformanceFixture.ExplicitEncounterNumberOfRuns=1;other captured inputs preserved" };
        var observed = Beta109WorldPredictionProvider.Predict(anchorSeed, context.Ascension, 0,
            context.CharacterKey, world, context.EvaluationAssumptions.AncientEligibilityAssumptions);
        var sequence = observed.EncounterSequences.Single(a => a.Act == 1);
        Require(sequence.Precision == PredictionPrecision.Exact, "EncounterPerf.AnchorEncounterQueueExact");
        var act = world.Beta109Generation!.OrderedActCatalog.Single(a => a.Act == 1 && a.ActKey == sequence.ActKey);
        EncounterQueueSearchCondition Queue(EncounterQueueKind kind, int requested)
        {
            var entries = (kind == EncounterQueueKind.Elite ? sequence.Elite : kind == EncounterQueueKind.Weak
                ? sequence.Normal.Take(act.WeakEncounterSlots) : sequence.Normal.Skip(act.WeakEncounterSlots)).ToArray();
            int count = Math.Min(requested, entries.Length);
            return new(1, kind, SearchSequenceRangeMode.FirstN, count, entries.Take(count).Select(e => e.EncounterKey).Distinct().ToArray());
        }
        var boss = observed.Bosses.Single(b => b.Act == 1 && b.Ordinal == 1);
        var bossQuery = source.Query with { LegacyWorld = source.Query.LegacyWorld with
        { BossOrdinalFilters = [..source.Query.LegacyWorld.BossOrdinalFilters, new(1, 1, new([boss.BossKey], [], []))] } };
        var cases = new (string Name, SearchQuery Query, string Control)[]
        {
            ("nc-control", source.Query, "nc-control"),
            ("nc-weak1", source.Query with { EncounterQueues = [Queue(EncounterQueueKind.Weak, 1)] }, "nc-control"),
            ("nc-strong3", source.Query with { EncounterQueues = [Queue(EncounterQueueKind.Strong, 3)] }, "nc-control"),
            ("nc-elite3", source.Query with { EncounterQueues = [Queue(EncounterQueueKind.Elite, 3)] }, "nc-control"),
            ("nc-boss-control", bossQuery, "nc-control"),
            ("nc-boss-all-queues3", bossQuery with { EncounterQueues = Enum.GetValues<EncounterQueueKind>().Select(kind => Queue(kind, 3)).ToArray() }, "nc-boss-control")
        };
        string inputs = Path.Combine(output, "inputs"); Directory.CreateDirectory(inputs);
        string contextPath = Path.Combine(inputs, "context.json");
        ProductionSearchReplay.Write(contextPath, exportedContext);
        var rows = new List<object>();
        foreach (var item in cases)
        {
            string folder = Path.Combine(inputs, item.Name); Directory.CreateDirectory(folder);
            var anchorOptions = source.Options with { StartSeed = anchorSeed, ScanCount = 1, TargetMatchCount = int.MaxValue,
                WorkerCount = 1, SkipExactValidation = false, ManualFamilyOrder = null, MapLossy = null };
            var anchorWorkload = new ProductionSearchReplayWorkload(1, item.Query, anchorOptions);
            var prepared = Compile(anchorWorkload, context);
            var exact = ProductionExactSearchEvaluator.Evaluate(prepared.Model, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, anchorSeed));
            Require(exact.Evaluation.Disposition == SearchDisposition.Match, "EncounterPerf.ComposedAnchorMustMatch:" + item.Name + ":" + exact.Evaluation.FailureCode);
            WriteEvidence(Path.Combine(folder, "anchor-proof.json"), new { Anchor = anchorSeed, exact.Evaluation,
                Source = "ProductionExactSearchEvaluator.Evaluate;all original N+C conditions retained" });
            var paths = new Dictionary<string, string>();
            void Save(string label, SearchRunOptions options)
            {
                string path = Path.Combine(folder, label + ".json");
                ProductionSearchReplay.Write(path, new ProductionSearchReplayWorkload(1, item.Query, options)); paths[label] = path;
            }
            Save("anchor-default", anchorOptions);
            Save("small-default", anchorOptions with { ScanCount = 16387 });
            Save("long-default", anchorOptions with { StartSeed = source.Options.StartSeed, ScanCount = longCount });
            bool hasWorld = WorldFamilyReplay.HasConditions(prepared.Model);
            if (hasWorld)
            {
                Save("small-w-first", anchorOptions with { ScanCount = 16387, ManualFamilyOrder = ["W.World", "N.Neow", "C.CombatReward"] });
                Save("long-w-first", anchorOptions with { StartSeed = source.Options.StartSeed, ScanCount = longCount,
                    ManualFamilyOrder = ["W.World", "N.Neow", "C.CombatReward"] });
                var replay = new WorldFamilyReplay(prepared.Model);
                foreach (var numerical in new[] { GpuNumericalVariant.NativeFloat64, GpuNumericalVariant.IntegerRne })
                {
                    var plan = new WorldFamilyGpuPlan(replay, specializeSolo: true, numericalVariant: numerical);
                    File.WriteAllText(Path.Combine(folder, numerical + ".glsl"), plan.ShaderSource());
                }
            }
            rows.Add(new { item.Name, item.Control, Context = contextPath, Inputs = paths,
                WholeOriginalQueryRetained = true, AnchorConfirmedByProductionExact = true, item.Query,
                NewQueuePredicates = item.Query.EncounterQueues.Count, WorldReplayPresent = hasWorld });
        }
        WriteEvidence(Path.Combine(output, "manifest.json"), new { Purpose = "Prepared W complete-search performance inputs; no execution or throughput claim",
            CandidateHashAtAuthoring = ProductHash(), Anchor = anchorSeed, Context = contextPath, LongScanCount = longCount,
            ContextPremise = "Explicit later-run fixture NumberOfRuns=1, shared by every control and queue request; actual source progress was absent",
            LongStartSeed = source.Options.StartSeed, ExistingNcQuery = source.Query,
            AnchorAct = sequence.ActKey, FullAnchorNormalQueue = sequence.Normal, FullAnchorEliteQueue = sequence.Elite,
            NoHistoricalBinaryUsedForNewSemantics = true, Cases = rows });
        Console.WriteLine($"ENCOUNTER_PERFORMANCE_INPUTS_READY cases={cases.Length};anchor={anchorSeed};longRoots={longCount};executed=false");
    }
}
