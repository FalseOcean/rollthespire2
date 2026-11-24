using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

internal static partial class CompatibilityChecks
{
    // Reuse the actual captured contexts and source-checked authored queries
    // exported by InitialRoomGpuChecks. This branch exercises normal planning,
    // complete Production Exact, publication/ACK and serialized continuation.
    internal static async Task CheckInitialRoomsSessionsAsync(string output, Action<ProductionSearchSession?> observeSession)
    {
        var cases = new List<(string Name, string Context, string Workload)>();
        foreach (string name in new[] { "OwnedBoth", "OwnedTreasureOrdinaryShop", "OwnedUnknownWorld",
            "UnorderedTwo", "SecondWindow", "DuplicateConsumption" })
            cases.Add((name, Path.Combine(output, "context.json"), Path.Combine(output, name + "-workload.json")));
        foreach (int players in new[] { 2, 3, 4 })
        {
            string folder = Path.Combine(output, "party-unknown", players + "p");
            if (Directory.Exists(folder)) cases.Add(("Party" + players + "Mixed", Path.Combine(folder, "context.json"), Path.Combine(folder, "mixed-workload.json")));
        }
        string directory = Path.Combine(output, "complete-sessions");
        Directory.CreateDirectory(directory);
        var rows = new List<object>();
        foreach (var item in cases)
        {
            var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(item.Context);
            var source = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(item.Workload);
            var workload = source with { Options = source.Options with { ScanCount = 4099,
                TargetMatchCount = int.MaxValue, WorkerCount = 1, SkipExactValidation = false } };
            var request = Compile(workload, captured.Context);
            string folder = Path.Combine(directory, item.Name);
            Directory.CreateDirectory(folder);
            ProductionSearchReplay.Write(Path.Combine(folder, "context.json"), captured);
            ProductionSearchReplay.Write(Path.Combine(folder, "workload.json"), workload);
            var handoffs = new Dictionary<string, (string Summary, long ExactOutcomes)>();
            async Task<Receipt> RunObserved(string label, bool cpuOnly, bool nativeGpu = false)
            {
                ProductionSearchSession? actualSession = null;
                var receipt = await RunSession(request, folder, label, cpuOnly, false, null, session =>
                {
                    if (session != null) actualSession = session;
                    observeSession(session);
                }, nativeGpu);
                var policyField = typeof(ProductionSearchSession).GetField("_smallCandidateExactPolicy",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(nameof(ProductionSearchSession), "_smallCandidateExactPolicy");
                var policy = (SmallCandidateExactPolicy?)policyField.GetValue(actualSession!);
                handoffs.Add(label, (policy?.GetSummary() ?? "", actualSession!.GetDiagnosticSummary().DispositionCounts.Values.Sum()));
                return receipt;
            }
            var cpu = await RunObserved("cpu", true);
            var native = await RunObserved("native", false, nativeGpu: true);
            var integer = await RunObserved("integer", false);
            Compare(cpu, native, item.Name + ":Native");
            Compare(cpu, integer, item.Name + ":Integer");
            bool rejection = item.Name == "DuplicateConsumption";
            Require(rejection ? integer.Progress.MatchCount == 0 : integer.Progress.MatchCount > 0,
                "InitialRoomsWholeQueryPositiveOrProvenRejection:" + item.Name);
            Require(integer.Progress.UnverifiedCandidateCount == 0, "InitialRoomsCompleteTruthNotUnverified:" + item.Name);
            foreach (var result in new[] { native, integer })
            {
                var initial = Flatten(result.Invocations).Where(row =>
                    row.Coverage.Contains(InitialRoomsInvocation.TreasureCoverage, StringComparer.Ordinal) ||
                    row.Coverage.Contains(InitialRoomsInvocation.UnknownCoverage, StringComparer.Ordinal) ||
                    row.FamilyId == VariantOpeningFamily.Id).ToArray();
                Require(initial.Length > 0 && initial.All(row => row.UsesGpu) &&
                    initial.Any(row => row.Inputs > 0 && row.Batches > 0),
                    "InitialRoomsSelectedGpuAndActualInvocation:" + item.Name + ":" + result.Label);
                // A strong first InitialRooms predicate can leave <=8 roots for
                // whole-query Exact. The selected later owner remains unexecuted;
                // its independent full GPU oracle ran in InitialRoomGpuChecks.
                WriteEvidence(Path.Combine(folder, result.Label + ".initial-participation.json"), new {
                    SelectedInitialOwners = initial,
                    ActualFamilyOwners = result.Invocations,
                    SmallExactHandoffSummary = handoffs[result.Label].Summary,
                    CompletedExactOutcomes = handoffs[result.Label].ExactOutcomes,
                    HandoffProofScope = "TopLevelPublicPipeline;PartyChildBoundariesRetainedInRawSummary;NestedCandidateCountsNotAdditive",
                    IndependentPopulationEvidence = "initial-rooms-gpu-checks.json;FullCpuAndSourceRngOracle",
                    UnexecutedSelectedTailClaimedAsGpuWork = false
                });
                if (item.Name.Contains("Mixed", StringComparison.Ordinal))
                    foreach (string family in new[] { "N.Neow", "E.EventResult" })
                    {
                        var actual = Flatten(result.Invocations).ToArray();
                        var top = result.Invocations;
                        if (top.Any(row => row.FamilyId == family && row.UsesGpu && row.Inputs > 0 && row.Batches > 0)) continue;
                        // Selected suffixes can legitimately hand their <=8 roots
                        // to whole-query Exact. Require the actual policy receipt
                        // and matching completed GPU work, never infer a handoff
                        // merely from a selected body or an unexecuted tail.
                        const string marker = "smallCandidateExactStopsJson=";
                        string summary = handoffs[result.Label].Summary;
                        int markerIndex = summary.IndexOf(marker, StringComparison.Ordinal);
                        Require(markerIndex >= 0, "InitialRoomsMixedMissingHandoff:" + item.Name + ":" + family + ":" + result.Label);
                        using var stops = System.Text.Json.JsonDocument.Parse(summary[(markerIndex + marker.Length)..]);
                        long exactCandidates = 0;
                        bool coveredTail = false;
                        foreach (var stop in stops.RootElement.EnumerateArray())
                        {
                            int candidates = stop.GetProperty("candidates").GetInt32();
                            long batches = stop.GetProperty("batches").GetInt64();
                            if (candidates == 0) continue;
                            string completedPrefix = stop.GetProperty("completedPrefix").GetString()!;
                            // Party records a boundary between personal children
                            // using this same policy. Those are neither top-level
                            // coverage atoms nor additional whole-query Exact roots.
                            if (completedPrefix.StartsWith("Party:", StringComparison.Ordinal) || top.Any(owner =>
                                completedPrefix.StartsWith("Party." + owner.FamilyId + ".Slots=", StringComparison.Ordinal))) continue;
                            string[] prefix = completedPrefix.Split(',', StringSplitOptions.RemoveEmptyEntries);
                            string[] tail = stop.GetProperty("unexecutedTail").GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries);
                            // Public policy serializes coverage, not producer IDs.
                            // Reconstruct an exact boundary in the actual top-level
                            // owner order before attributing any input/output work.
                            int[] boundaries = Enumerable.Range(1, Math.Max(0, top.Length - 1)).Where(index =>
                                prefix.SequenceEqual(top.Take(index).SelectMany(owner => owner.Coverage)) &&
                                tail.SequenceEqual(top.Skip(index).SelectMany(owner => owner.Coverage).Distinct(StringComparer.Ordinal))).ToArray();
                            int boundary = boundaries.Length == 1 ? boundaries[0] : 0;
                            bool populationValid = candidates is >= 1 and <= SmallCandidateExactPolicy.MaximumCandidates && batches > 0;
                            bool producerWork = boundary > 0 && top.Take(boundary).All(row => row.UsesGpu &&
                                row.Batches >= batches && row.Inputs > 0 && row.Faulted != true);
                            bool outputWork = boundary > 0 && top[boundary - 1].Inputs >= candidates * batches &&
                                top[boundary - 1].Outputs >= candidates * batches;
                            string proof = System.Text.Json.JsonSerializer.Serialize(new { stop, boundaries, populationValid, producerWork, outputWork,
                                owners = top.Select(row => new { row.FamilyId, row.Coverage, row.UsesGpu, row.Batches, row.Inputs, row.Outputs, row.Faulted }) });
                            Require(populationValid && producerWork && outputWork,
                                "InitialRoomsMixedHandoffMatchesActualGpu:" + item.Name + ":" + family + ":" + result.Label + ";proof=" + proof);
                            exactCandidates += candidates * batches;
                            coveredTail |= tail.Contains(family, StringComparer.Ordinal);
                        }
                        Require(coveredTail && top.Any(row => row.FamilyId == family && row.UsesGpu && row.Batches == 0 &&
                                row.Inputs == 0 && row.Outputs == 0 && row.Faulted != true) &&
                            exactCandidates > 0 && handoffs[result.Label].ExactOutcomes >= exactCandidates &&
                            actual.All(row => row.Faulted != true) && result.Progress.PendingCandidateCount == 0 &&
                            result.Progress.UnverifiedCandidateCount == 0,
                            "InitialRoomsMixedActualGpuOrVerifiedExactHandoff:" + item.Name + ":" + family + ":" + result.Label + ";proof=" +
                            System.Text.Json.JsonSerializer.Serialize(new { coveredTail, exactCandidates, completedExact = handoffs[result.Label].ExactOutcomes,
                                pending = result.Progress.PendingCandidateCount, unverified = result.Progress.UnverifiedCandidateCount,
                                owners = top.Select(row => new { row.FamilyId, row.Coverage, row.UsesGpu, row.Batches, row.Inputs, row.Outputs, row.Faulted }) }));
                    }
            }
            if (item.Name == "OwnedBoth")
            {
                var cancelled = await RunSession(request, folder, "cancelled", false, true, null, observeSession);
                Require(cancelled.Progress.State == SearchRunState.Cancelled && cancelled.SemanticRecords.Length == 0,
                    "InitialRoomsCancelAfterRealExactBeforePublish");
                var checkpoint = ProductionSearchReplay.Read<SearchContinuationSnapshot>(Path.Combine(folder, "cancelled.continuation.json"));
                Require(VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, request.CanonicalStartSeed,
                    out ulong first, out _, out _), "InitialRoomsCanonicalStart");
                ulong end = checked(first + (ulong)request.ScanCount);
                Require(checkpoint.NextOrdinal >= first && checkpoint.NextOrdinal < end, "InitialRoomsUnfinishedCursorRetained");
                var resumedWorkload = workload with { Options = workload.Options with {
                    StartSeed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, checkpoint.NextOrdinal),
                    ScanCount = checked((long)(end - checkpoint.NextOrdinal)) } };
                var resumed = await RunSession(Compile(resumedWorkload, captured.Context), folder, "resumed", false, false, checkpoint, observeSession);
                Require(integer.SemanticRecords.SequenceEqual(resumed.SemanticRecords) && integer.SafeNextOrdinal == resumed.SafeNextOrdinal &&
                    integer.ContinuationFingerprint == resumed.ContinuationFingerprint, "InitialRoomsSerializedResumeWholeOutcomesAndCursor");
            }
            rows.Add(new { item.Name, ScanCount = request.ScanCount, Cpu = cpu.Progress, Native = native.Progress,
                Integer = integer.Progress, integer.SafeNextOrdinal, SemanticSha256 = Digest(integer.SemanticRecords),
                Invocations = integer.Invocations });
        }
        WriteEvidence(Path.Combine(directory, "initial-rooms-complete-sessions.json"), new {
            Passed = true, ActualBackend = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity(), Cases = rows,
            ActualGpuInitialRooms = true, RealProductionExact = true, CancelSerializedResume = true,
            TargetMetalExecutionClaimed = false, ThroughputClaimed = false });
        Console.WriteLine($"INITIAL_ROOMS_COMPLETE_SESSIONS_PASS cases={rows.Count};nativeIntegerCpu=true;fullExact=true;cancelResume=true;targetMetal=false");
    }
}
