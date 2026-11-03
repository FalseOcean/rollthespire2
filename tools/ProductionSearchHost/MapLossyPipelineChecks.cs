using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

// Bounded native measurement of the actual proposer; ordinary Host replay owns
// session/Exact/E2E measurements. All modes share the same inputs and build.
internal static class MapLossyPipelineChecks
{
    internal static readonly string[] Modes = ["baseline", "prefetch", "grouped", "combined"];

    internal static async Task RunAsync(PreparedSearch basis, string output)
    {
        Directory.CreateDirectory(output);
        if (basis.Model.Ascension != 10) throw new InvalidDataException("MapPipeline.ExpectedCapturedAscension10");
        const int count = 4 * 1024 * 1024, capacity = 8192, warmupCount = 65536;
        const string startSeed = "6Q6BSA6SFSEG";
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, startSeed, out ulong start, out _, out string issue))
            throw new InvalidDataException(issue);
        var query = SearchQuery.Empty with { StandardMaps = Enumerable.Range(1, 3).Select(act =>
            new StandardMapSearchCondition(act, StandardMapMetric.ReachableMaxRest, StandardMapComparison.AtMost, 2)).ToArray() };
        var request = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, basis.Model.CompiledSearch.Context),
            basis.RunOptions with { MapLossy = new(true, capacity) }).Plan ?? throw new InvalidDataException("MapPipeline.Compile");
        var plan = MapLossyPlan.Create(request.Model,request.MapLossy);
        var input = FamilyCandidateSet.Dense(new SearchBatch(start, count));
        var warmup = FamilyCandidateSet.Dense(new SearchBatch(start, warmupCount));
        string? previous = Environment.GetEnvironmentVariable("RT2_MAP_GPU_PIPELINE");
        await using var execution = new FilterRuntimeServices(requiresGpu: true, cpuWorkerCount: 16);
        var executors = new Dictionary<string, MapLossyGpuExecutor>();
        var rows = new List<object>();
        ulong[]? reference = null;
        try
        {
            foreach (string mode in Modes)
            {
                Environment.SetEnvironmentVariable("RT2_MAP_GPU_PIPELINE", mode);
                var gpu = await execution.ExecuteGpuAsync(rd => new MapLossyGpuExecutor(rd, capacity, execution), CancellationToken.None);
                executors.Add(mode, gpu);
                if (gpu.PipelineMode != mode) throw new InvalidDataException("MapPipeline.ModeNotSelected:" + mode);
                await execution.ExecuteGpuAsync(_ => gpu.Propose(warmup, plan, 10, CancellationToken.None), CancellationToken.None);
            }
            for (int round = 0; round < 3; round++)
            {
                // Rotate order rather than grouping all runs of one mode.
                foreach (string mode in Modes.Skip(round).Concat(Modes.Take(round)))
                {
                    var gpu = executors[mode];
                    var watch = Stopwatch.StartNew();
                    var actual = await execution.ExecuteGpuAsync(_ => gpu.Propose(input, plan, 10, CancellationToken.None), CancellationToken.None);
                    double wallMs = watch.Elapsed.TotalMilliseconds;
                    ulong[] ordinals = actual.EnumerateLogicalOrdinals().ToArray();
                    reference ??= ordinals;
                    if (!ordinals.SequenceEqual(reference)) throw new InvalidDataException("MapPipeline.OrderedProposalMismatch:" + mode);
                    var bytes = new byte[ordinals.Length * sizeof(ulong)];
                    Buffer.BlockCopy(ordinals, 0, bytes, 0, bytes.Length);
                    rows.Add(new { round, mode, inputs = count, candidates = ordinals.Length,
                        orderedOrdinalSha256 = Convert.ToHexString(SHA256.HashData(bytes)), wallMs,
                        rootsPerSecond = count * 1000d / wallMs, metrics = gpu.LastMetrics, retainedBufferBytes = gpu.RetainedBufferBytes });
                    Console.WriteLine($"mapPipelineSample=true;round={round};mode={mode};inputs={count};candidates={ordinals.Length};wallMs={wallMs:F3}");
                }
            }
            File.WriteAllText(Path.Combine(output, "map-lossy-pipeline.json"), JsonSerializer.Serialize(new {
                startSeed, count, capacity, warmupCount, ascension = 10, query,
                orderedProposalEquality = true, rows,
                Meaning = "Warmed same-build actual three-act proposal wall throughput, not Production Exact/session E2E. Stage work/wait timings may overlap and must not be summed as wall time. Empty proposals remain valid equality evidence; positive broad CPU/GPU checks are in MapLossyGpu." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RT2_MAP_GPU_PIPELINE", previous);
            foreach (var gpu in executors.Values)
                await execution.ExecuteGpuAsync(_ => { gpu.Dispose(); return true; }, CancellationToken.None);
        }
    }
}
