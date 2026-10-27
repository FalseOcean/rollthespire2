using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

internal static partial class FamilyPhysicalMatrix
{
    // Bounded adaptation of the existing R43/R44 MeasurePartySustained public
    // collector. Uses current registered N and complete InvokeAsync wall time;
    // private transport, Exact, output hashing and setup are outside this price.
    internal static void MeasureNeowCanonical(ExactSearchExecutionRequest request, string[] args, string output)
    {
        string Arg(string key, string fallback)
        {
            int at = Array.IndexOf(args, key);
            return at < 0 ? fallback : at + 1 < args.Length ? args[at + 1] : throw new ArgumentException("Missing " + key);
        }
        int[] populations = Arg("--canonical-inputs", "16777216").Split(',').Select(int.Parse).ToArray();
        int repeats = int.Parse(Arg("--repeat", "3"));
        int warmups = int.Parse(Arg("--canonical-warmups", "2"));
        int minMs = int.Parse(Arg("--canonical-sample-ms", "3000"));
        int minBatches = int.Parse(Arg("--canonical-min-batches", "16"));
        string mode = Arg("--canonical-mode", "both");
        bool integer = args.Contains("--canonical-integer", StringComparer.Ordinal);
        string split = Arg("--canonical-split", "training");
        if (request.ProfileId != RuntimeProfileId.Beta111 || request.Authority.PlayersCount != 1 ||
            request.CompiledSearch.Context.Party is not null || populations.Length == 0 ||
            populations.Any(n => n < 1 || n > RootCapacity) || populations.Distinct().Count() != populations.Length ||
            repeats is < 1 or > 20 || warmups is < 2 or > 1024 || minMs is < 1 or > 60000 ||
            minBatches is < 1 or > 65536 || mode is not ("both" or "dense" or "compact") ||
            split is not ("training" or "validation"))
            throw new ArgumentException("NeowCanonical.InvalidSoloGeometryOrMeasurementConfiguration");
        string summaryPath = Path.Combine(output, "neow-canonical-summary.json");
        string recordsPath = Path.Combine(output, "neow-canonical-batches.jsonl");
        if (File.Exists(summaryPath) || File.Exists(recordsPath)) throw new IOException("NeowCanonical.ImmutableOutputExists");
        var profile = RuntimeProfileRegistry.Select(request.Detection);
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(profile, request.CanonicalStartSeed, out ulong nextBase, out _, out string issue))
            throw new ArgumentException("NeowCanonical.StartSeed:" + issue);
        ulong space = VisibleSeedCandidateCodec.SpaceSize(profile);
        var rows = new List<object>();
        var modes = mode == "both" ? new[] { false, true } : new[] { mode == "compact" };
        using var records = new StreamWriter(new FileStream(recordsPath, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var json = new JsonSerializerOptions(ProductionSearchReplay.JsonOptions) { WriteIndented = false };
        foreach (int population in populations)
        foreach (bool compact in modes)
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            int window = compact ? RootCapacity : population;
            ulong first = nextBase;
            ulong[]? compactOrdinals = compact ? Enumerable.Range(0, population)
                .Select(i => (ulong)i * RootCapacity / (uint)population).ToArray() : null;
            IReadOnlyList<IFamilyInvocation> owners = [];
            var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true, cpuWorkerCount: request.WorkerCount);
            Exception? primary = null;
            try
            {
                owners = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true, gpuFloat64Available: !integer);
                var family = owners.OfType<NeowFamily>().SingleOrDefault(f => f.Coverage.Count == 1 && f.GpuPlan is not null)
                    ?? throw new InvalidOperationException("NeowCanonical.OrdinaryRegisteredGpuNRequired");
                FamilyCanonicalCostKey? key = null;
                double? survival = family.ResolveSurvival(new HashSet<string>()).SurvivalProbability;
                if (survival is not (>= 0 and <= 1)) throw new InvalidOperationException("NeowCanonical.SourceSurvivalUnavailable");
                using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int batches = 0;
                long outputs = 0;
                double measuredMs = 0, warmupMs = 0;
                for (int index = -warmups; index < minBatches || measuredMs < minMs; index++)
                {
                    if (index >= 65536 || nextBase > space || (ulong)window > space - nextBase)
                        throw new InvalidOperationException("NeowCanonical.DurationOrSeedSpaceBoundReached;partialEvidenceRetained=true");
                    var batch = new SearchBatch(nextBase, window);
                    nextBase = checked(nextBase + (uint)window);
                    var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, compactOrdinals!) : FamilyCandidateSet.Dense(batch);
                    var observation = new FamilyObservationWindow(batch, request);
                    long started = Stopwatch.GetTimestamp();
                    var result = family.InvokeAsync(execution, observation, input, CancellationToken.None).GetAwaiter().GetResult();
                    double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    // Device identity is captured by the actual executor, after
                    // creation; never freeze an uninitialized GPU identity.
                    key ??= FamilyCanonicalCostShape.Describe(request, family, compact, window)
                        ?? throw new InvalidOperationException("NeowCanonical.StructuralPricingKeyUnavailable");
                    if (result.Batch != batch || result.Count > population || !double.IsFinite(ms) || ms <= 0 ||
                        ((IFamilyInvocation)family).ExecutionFaulted || execution.CaptureFilterIsolations(batch).Count != 0)
                        throw new InvalidDataException("NeowCanonical.InvocationInvalidOrIsolated");
                    // Hash the entire ordered ABI1 outside Invoke timing. This
                    // checks the stored evidence, not an independent CPU oracle.
                    var ordinals = result.ExportAbi1();
                    for (int i = 0; i < ordinals.Length; i++)
                        if (ordinals.Span[i] >= (ulong)window || i > 0 && ordinals.Span[i] <= ordinals.Span[i - 1])
                            throw new InvalidDataException("NeowCanonical.OrderedAbi1Invalid");
                    byte[] digest = SHA256.HashData(MemoryMarshal.AsBytes(ordinals.Span));
                    if (index < 0) warmupMs += ms;
                    else
                    {
                        batches++; measuredMs += ms; outputs += result.Count;
                        byte[] frame = new byte[24];
                        BinaryPrimitives.WriteUInt64LittleEndian(frame, batch.BatchBase);
                        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(8), window);
                        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(12), population);
                        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16), result.Count);
                        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20), index);
                        aggregate.AppendData(frame); aggregate.AppendData(digest);
                    }
                    records.WriteLine(JsonSerializer.Serialize(new { Repeat = repeat, CompactInput = compact, Warmup = index < 0,
                        BatchIndex = index, batch.BatchBase, BatchCandidateCount = window, Input = population, Output = result.Count,
                        InvokeWallMs = ms, Digest = Convert.ToHexString(digest), Key = key,
                        Boundary = FamilyCanonicalMeasuredCost.Boundary, VerificationOutsideTiming = true }, json));
                }
                rows.Add(new { Key = key, SourceSurvival = survival, Split = split, Role = "PublicAbi1Canonical", Repeat = repeat,
                    InputPerBatch = population, MeasuredBatches = batches, Input = (long)batches * population, Output = outputs,
                    InvokeWallMs = measuredMs, CallMilliseconds = measuredMs / batches, WarmupBatches = warmups, WarmupInvokeWallMs = warmupMs,
                    FirstBatchBaseIncludingWarmup = first, EndBatchBaseExclusive = nextBase,
                    Digest = Convert.ToHexString(aggregate.GetHashAndReset()), SourceSurvivalBasis = "Family.ResolveSurvival;NotObserved",
                    Boundary = FamilyCanonicalMeasuredCost.Boundary, FirstAndWarmupsExcluded = true, FullCpuOracle = false,
                    SameRunRepricing = false, ProductionE2E = false });
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                var cleanup = new List<Exception>();
                foreach (var owner in owners)
                    try { owner.DisposeAsync(execution).GetAwaiter().GetResult(); } catch (Exception error) { cleanup.Add(error); }
                try { execution.DisposeAsync().GetAwaiter().GetResult(); } catch (Exception error) { cleanup.Add(error); }
                if (cleanup.Count > 0)
                {
                    var failure = new AggregateException("NeowCanonical.CleanupFailed", cleanup);
                    if (primary is null) throw failure;
                    Console.Error.WriteLine(failure);
                }
            }
        }
        string AssemblySha(Assembly assembly)
        {
            string path = string.IsNullOrWhiteSpace(assembly.Location)
                ? ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/" + assembly.GetName().Name + ".dll") : assembly.Location;
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(new { SchemaVersion = 1,
            Collector = "R43R44.PublicInvoke.BoundedNeowAdaptation.20261027.v1", RuntimeAssemblySha256 = AssemblySha(typeof(IFamilyInvocation).Assembly),
            HostAssemblySha256 = AssemblySha(typeof(Main).Assembly), Boundary = FamilyCanonicalMeasuredCost.Boundary,
            IntegerVariant = integer, Split = split, TrainingProhibited = split != "training", MinimumInvokeWallMs = minMs,
            MinimumBatches = minBatches, Warmups = warmups, Repeats = repeats, Populations = populations,
            Rows = rows, ExactExecuted = false, PrivateTransportMeasured = false, PriceUpdated = false,
            CadenceReceipt = "measurement-host-cadence.json", Complete = true }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine("NEOW_CANONICAL_PASS rows=" + rows.Count + ";exact=false;priceUpdated=false");
    }
}
