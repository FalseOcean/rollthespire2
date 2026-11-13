using System.Diagnostics;
using System.Text.Json;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class CompatibilityChecks
{
    // An exhaustive bounded whole-query comparison. Performance workloads are exported only;
    // they retain Exact and require a separately chosen device/time window.
    internal static async Task CheckEncounterQueueSessionsAsync(ProductionSearchReplayContext captured,
        ProductionSearchReplayWorkload source, string output, Action<ProductionSearchSession?> observeSession,
        int count = 129, bool publicParity = false)
    {
        if (captured.Context.Party is not null || count < 2) throw new ArgumentException("EncounterHost.SoloSourceAndBoundedWindowRequired");
        string startSeed = source.Options.StartSeed;
        var profile = RuntimeProfileRegistry.Select(captured.Context.Detection);
        Require(profile.ProfileId == RuntimeProfileId.Beta111 && VisibleSeedCandidateCodec.TryParseOrdinal(profile,
            startSeed, out _, out _, out _), "EncounterHost.Beta111VisibleStart");
        var world = captured.Context.Authority.WorldAuthority ?? throw new InvalidDataException("EncounterHost.WorldMissing");
        var cases = new List<(string Name, SearchContext Context, SearchQuery Query)>();
        WorldPredictionResult Observe(SearchContext context) => Beta109WorldPredictionProvider.Predict(startSeed,
            context.Ascension, 0, context.CharacterKey, context.Authority.WorldAuthority!, AncientOptionConditionProfile.BroadDefault);
        SearchQuery QueueQuery(SearchContext context, int actNumber, EncounterQueueKind kind, int horizon,
            SearchSequenceRangeMode mode = SearchSequenceRangeMode.FirstN)
        {
            var observed = Observe(context);
            var sequence = observed.EncounterSequences.Single(a => a.Act == actNumber);
            Require(sequence.Precision == PredictionPrecision.Exact, "EncounterHost.SourceQueueExact");
            var act = context.Authority.WorldAuthority!.Beta109Generation!.OrderedActCatalog.Single(a => a.Act == actNumber && a.ActKey == sequence.ActKey);
            var entries = (kind == EncounterQueueKind.Elite ? sequence.Elite : kind == EncounterQueueKind.Weak
                ? sequence.Normal.Take(act.WeakEncounterSlots) : sequence.Normal.Skip(act.WeakEncounterSlots)).ToArray();
            int n = Math.Min(horizon, entries.Length);
            var allowed = (mode == SearchSequenceRangeMode.FirstN ? entries.Take(n) : entries.Skip(n - 1).Take(1))
                .Select(e => e.EncounterKey).Distinct().ToArray();
            return SearchQuery.Empty with { EncounterQueues = [new(actNumber, kind, mode, n, allowed)] };
        }
        SearchQuery WithBoss(SearchContext context, SearchQuery query, int actNumber)
        {
            var boss = Observe(context).Bosses.First(b => b.Act == actNumber && b.Ordinal == 1);
            return query with { LegacyWorld = query.LegacyWorld with { BossOrdinalFilters = [new(actNumber, 1, new([boss.BossKey], [], []))] } };
        }
        var normal = captured.Context with { Authority = captured.Context.Authority.WithWorldAuthority(world with { EncounterNumberOfRuns = 1 }) };
        cases.Add(("OldBossBaseline", normal, WithBoss(normal, SearchQuery.Empty, 3)));
        foreach (var kind in Enum.GetValues<EncounterQueueKind>())
        foreach (int n in new[] { 1, 3 }) cases.Add(($"Act1{kind}{n}", normal, QueueQuery(normal, 1, kind, n)));
        cases.Add(("Act2Strong1", normal, QueueQuery(normal, 2, EncounterQueueKind.Strong, 1)));
        cases.Add(("Act3Elite3WithBoss", normal, WithBoss(normal, QueueQuery(normal, 3, EncounterQueueKind.Elite, 3), 3)));
        cases.Add(("WeakStrongEliteConjunction", normal, SearchQuery.Empty with { EncounterQueues = Enum.GetValues<EncounterQueueKind>()
            .SelectMany(kind => QueueQuery(normal, 1, kind, 1).EncounterQueues).ToArray() }));
        var firstRun = captured.Context with { Authority = captured.Context.Authority.WithWorldAuthority(world with { EncounterNumberOfRuns = 0 }) };
        var firstRunWorld = firstRun.Authority.WorldAuthority!;
        firstRun = firstRun with { Authority = firstRun.Authority.WithWorldAuthority(firstRunWorld with
        {
            Beta109Generation = firstRunWorld.Beta109Generation! with
            {
                Act1OverrideRaw = "Overgrowth",
                Act1OverrideResolvedKey = firstRunWorld.Beta109Generation.OrderedActCatalog.Single(a => a.Act == 1 && a.ActKey.Entry == "OVERGROWTH").ActKey
            }
        }) };
        cases.Add(("FirstRunWeak3", firstRun, QueueQuery(firstRun, 1, EncounterQueueKind.Weak, 3)));
        cases.Add(("FirstRunStrongTail", firstRun, QueueQuery(firstRun, 1, EncounterQueueKind.Strong, int.MaxValue, SearchSequenceRangeMode.ExactSlot)));
        cases.Add(("FirstRunElite3", firstRun, QueueQuery(firstRun, 1, EncounterQueueKind.Elite, 3)));
        cases.Add(("MissingDiscovery", captured.Context with { Authority = captured.Context.Authority.WithWorldAuthority(world with { EncounterNumberOfRuns = null }) },
            SearchQuery.Empty with { EncounterQueues = [new(1, EncounterQueueKind.Weak, SearchSequenceRangeMode.ExactSlot, 1, [new("ENCOUNTER", "NEVER")])] }));
        var allMissing = firstRun with { Authority = firstRun.Authority.WithWorldAuthority(firstRun.Authority.WorldAuthority! with { EncounterNumberOfRuns = null }) };
        cases.Add(("MissingDiscoveryNoParticipatingFilter", allMissing, SearchQuery.Empty with
        { EncounterQueues = [new(1, EncounterQueueKind.Weak, SearchSequenceRangeMode.ExactSlot, 1, [new("ENCOUNTER", "NEVER")])] }));
        // Native initialization completes on the Godot main thread before this
        // method. Capture every party fixture before any session can yield.
        RuntimeSnapshotThreadGuard.RequireMainThread();
        foreach (int players in new[] { 2, 4 })
        {
            var unlocks = Enumerable.Range(0, players).Select(_ => UnlockState.all.ToSerializable()).ToArray();
            var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, startSeed,
                BaseGameModelKeys.Characters.All.Take(players).ToArray(), unlocks, captured.Context.Ascension, "0.111.0");
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
                captured.Context.Ascension, party.Players[0], captured.Context.Detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
            var shared = WithBoss(context, QueueQuery(context, 3, EncounterQueueKind.Elite, 3), 3);
            cases.Add(($"Party{players}SharedEncounter", context, shared with { Players = Enumerable.Range(0, players)
                .Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)).ToArray() }));
        }
        var manifest = new List<object>();
        var publicCases = new List<(PreparedSearch Request, string Folder)>();
        foreach (var item in cases)
        {
            string folder = Path.Combine(output, "encounter-sessions", item.Name);
            Directory.CreateDirectory(folder);
            var context = new ProductionSearchReplayContext(1,
                "H01 encounter semantic extension; copied captured authority with explicit first-run fixture context; authored source queue at original start", item.Context);
            var workload = new ProductionSearchReplayWorkload(1, item.Query, source.Options with
            { ScanCount = count, TargetMatchCount = int.MaxValue, WorkerCount = 1, SkipExactValidation = false, ManualFamilyOrder = null, MapLossy = null });
            ProductionSearchReplay.Write(Path.Combine(folder, "context.json"), context);
            ProductionSearchReplay.Write(Path.Combine(folder, "workload.json"), workload);
            ProductionSearchReplay.Write(Path.Combine(folder, "performance-workload.json"), workload with
            { Options = workload.Options with { ScanCount = 1L << 24, WorkerCount = 4 } });
            var request = Compile(workload, item.Context);
            Require(VisibleSeedCandidateCodec.TryParseOrdinal(profile, request.CanonicalStartSeed, out ulong start, out _, out _), "EncounterHost.Start");
            var exact = new List<object>();
            var deliverable = new List<string>();
            for (ulong offset = 0; offset < (ulong)count; offset++)
            {
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(profile, start + offset);
                var result = ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.FromCanonicalSeed(profile, seed));
                exact.Add(new { Seed = seed, result.Evaluation.Disposition, result.Evaluation.FailureCode });
                if (result.Evaluation.Disposition != SearchDisposition.NoMatch) deliverable.Add(seed + "|" + result.Evaluation.Disposition);
            }
            WriteEvidence(Path.Combine(folder, "exact-population.json"), exact);
            var timings = new List<object>();
            async Task<Receipt> Run(string label, bool cpuOnly, bool native = false)
            {
                var watch = Stopwatch.StartNew();
                var receipt = await RunSession(request, folder, label, cpuOnly, false, null, observeSession, native);
                timings.Add(new { Label = label, EndToEndMs = watch.Elapsed.TotalMilliseconds, receipt.Progress, receipt.Invocations });
                var actual = receipt.SemanticRecords.Select(record =>
                {
                    using var parsed = JsonDocument.Parse(record);
                    return parsed.RootElement.GetProperty("Seed").GetString() + "|" + parsed.RootElement.GetProperty("ValidationDisposition").GetString();
                }).Order(StringComparer.Ordinal).ToArray();
                Require(actual.SequenceEqual(deliverable.Order(StringComparer.Ordinal)), "EncounterHost.ExhaustiveExactPopulation:" + item.Name + ":" + label);
                return receipt;
            }
            var cpu = await Run("cpu", true);
            var nativeGpu = await Run("native", false, true);
            var integer = await Run("integer", false);
            Compare(cpu, nativeGpu, item.Name + ":Native"); Compare(cpu, integer, item.Name + ":Integer");
            if (publicParity && item.Context.Party is null && item.Name is "Act1Weak1" or "Act1Strong3" or "Act1Elite3" or "FirstRunStrongTail")
                publicCases.Add((request, Path.Combine(folder, "public-family-parity")));
            WriteEvidence(Path.Combine(folder, "comparison.json"), new { Passed = true, CompleteExactPopulationCompared = true,
                CpuNativeIntegerCompleteSemanticRecordsEqual = true, PendingCandidates = integer.Progress.PendingCandidateCount,
                Timings = timings, PerformanceWorkloadExecuted = false });
            manifest.Add(new { item.Name, Folder = folder, Conditions = item.Query, DirectExactRoots = count,
                DirectExactDeliverable = deliverable.Count, CpuRecords = cpu.SemanticRecords.Length,
                NativeRecords = nativeGpu.SemanticRecords.Length, IntegerRecords = integer.SemanticRecords.Length });
        }
        WriteEvidence(Path.Combine(output, "encounter-complete-sessions.json"), new { Passed = true, Cases = manifest,
            DirectExactPopulation = true, RealProductionSessions = true, PerformanceWorkloadsOnlyExported = true });
        Console.WriteLine($"ENCOUNTER_COMPLETE_SESSIONS_PASS cases={cases.Count};rootsPerCase={count};cpuNativeInteger=true;directExactPopulation=true");
        // Supplemental public-module checks run only after every whole-query
        // session has its independent durable receipt.
        foreach (var item in publicCases) await CheckPublicFamilyParityAsync(item.Request, item.Folder);
    }

    internal static async Task CheckEncounterPublicParityAsync(string priorCompleteOutput, string output)
    {
        var passed = new List<string>();
        foreach (string name in new[] { "Act1Weak1", "Act1Strong3", "Act1Elite3", "FirstRunStrongTail" })
        {
            string folder = Path.Combine(priorCompleteOutput, "encounter-sessions", name);
            var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(folder, "context.json"));
            var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(folder, "workload.json"));
            await CheckPublicFamilyParityAsync(Compile(workload, captured.Context), Path.Combine(output, name));
            passed.Add(name);
        }
        WriteEvidence(Path.Combine(output, "encounter-public-parity.json"), new { Passed = true, Cases = passed,
            Source = Path.GetFullPath(priorCompleteOutput), ExistingCompleteWorkloadsReused = true });
        Console.WriteLine($"ENCOUNTER_PUBLIC_PARITY_PASS cases={passed.Count};cpuNativeInteger=true;denseSparseZero=true");
    }
}
