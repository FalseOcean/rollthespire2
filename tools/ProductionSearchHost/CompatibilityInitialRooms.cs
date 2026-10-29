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
            var cpu = await RunSession(request, folder, "cpu", true, false, null, observeSession);
            var native = await RunSession(request, folder, "native", false, false, null, observeSession, nativeGpu: true);
            var integer = await RunSession(request, folder, "integer", false, false, null, observeSession);
            Compare(cpu, native, item.Name + ":Native");
            Compare(cpu, integer, item.Name + ":Integer");
            bool rejection = item.Name == "DuplicateConsumption";
            Require(rejection ? integer.Progress.MatchCount == 0 : integer.Progress.MatchCount > 0,
                "InitialRoomsWholeQueryPositiveOrProvenRejection:" + item.Name);
            Require(integer.Progress.UnverifiedCandidateCount == 0, "InitialRoomsCompleteTruthNotUnverified:" + item.Name);
            foreach (var result in new[] { native, integer })
            {
                var initial = Flatten(result.Invocations).Where(row =>
                    row.Type.EndsWith(".InitialRoomsInvocation", StringComparison.Ordinal) ||
                    row.FamilyId == VariantOpeningFamily.Id).ToArray();
                Require(initial.Length > 0 && initial.All(row => row.UsesGpu) &&
                    initial.Any(row => row.Inputs > 0 && row.Batches > 0),
                    "InitialRoomsSelectedGpuAndActualInvocation:" + item.Name + ":" + result.Label);
                // A strong first InitialRooms predicate can leave <=8 roots for
                // whole-query Exact. The selected later owner remains unexecuted;
                // its independent full GPU oracle ran in InitialRoomGpuChecks.
                WriteEvidence(Path.Combine(folder, result.Label + ".initial-participation.json"), new {
                    SelectedInitialOwners = initial,
                    IndependentPopulationEvidence = "initial-rooms-gpu-checks.json;FullCpuAndSourceRngOracle",
                    UnexecutedSelectedTailClaimedAsGpuWork = false
                });
                if (item.Name.Contains("Mixed", StringComparison.Ordinal))
                    foreach (string family in new[] { "N.Neow", "E.EventResult" })
                        Require(Flatten(result.Invocations).Any(row => row.FamilyId == family && row.UsesGpu && row.Inputs > 0 && row.Batches > 0),
                            "InitialRoomsMixedActualGpu:" + item.Name + ":" + family + ":" + result.Label);
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
