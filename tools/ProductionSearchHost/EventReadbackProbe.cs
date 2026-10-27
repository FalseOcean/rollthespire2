using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Bounded diagnostics in the existing Host. Timing uses the real E private
// producer; full-population verification is separate from measured calls.
internal static class EventReadbackProbe
{
    internal static void Run(ExactSearchExecutionRequest request, string output)
    {
        var family = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true).OfType<EventResultFamily>().Single();
        var plan = family.Plan;
        var profile = RuntimeProfileRegistry.Select(request.Detection);
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(profile, request.CanonicalStartSeed, out ulong first, out _, out var issue))
            throw new InvalidDataException(issue);
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("EReadback.NoDevice");
        using var buffer = new PrivateOrdinalBuffer(rd);
        using var gpu = new EventResultFamilyGpuExecutor(rd, plan, privateOutput: buffer);
        bool parity = System.Environment.GetEnvironmentVariable("RT2_E_READBACK_MODE") == "parity";
        var rows = new List<object>();
        if (!parity)
        {
            const int windows = 128;
            for (int round = -1; round < 3; round++)
            {
                int n = round < 0 ? 8 : windows;
                long survivors = 0, bytes = 0;
                double canonical = 0, sync = 0, read = 0;
                var counts = new List<int>();
                for (int window = 0; window < n; window++)
                {
                    var batch = new SearchBatch(first + (ulong)window * PrivateOrdinalBuffer.Capacity, PrivateOrdinalBuffer.Capacity);
                    int count = gpu.ExecutePrivateStage(batch, batch.BatchCandidateCount, CancellationToken.None, out var m);
                    counts.Add(count); survivors += count; bytes += m.ReadbackBytes;
                    canonical += m.CanonicalMs; sync += m.DispatchSyncMs; read += m.ReadbackMs;
                }
                if (round >= 0) rows.Add(new { round, windows = n, inputs = (long)n * PrivateOrdinalBuffer.Capacity,
                    survivors, headerReadBytes = bytes, canonicalMs = canonical, dispatchSyncMs = sync, headerReadbackMs = read, counts });
            }
        }
        else
        {
            using var reference = new EventResultFamilyGpuExecutor(rd, plan);
            bool cpu(ulong candidate) => plan.Reference(profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, candidate)));
            var sizes = new[] { 0, 1, 63, 64, plan.Capacity - 1, plan.Capacity, plan.Capacity + 1, 2 * plan.Capacity + 7, PrivateOrdinalBuffer.Capacity };
            foreach (int size in sizes)
            {
                var batch = new SearchBatch(first, size);
                int count = gpu.ExecutePrivateStage(batch, size, CancellationToken.None, out var m);
                var full = buffer.ReadForVerification(batch, count);
                Equal(full, reference.Execute(FamilyCandidateSet.Dense(batch), CancellationToken.None, out _), "DenseFullParity");
                if (m.ReadbackBytes != (size == 0 ? 0 : 32)) throw new InvalidDataException("EReadback.FinalHeaderNotSingle");
                var ordinals = full.ExportAbi1().ToArray();
                for (ulong i = 0; i < (ulong)Math.Min(size, 1024); i++)
                    if (cpu(batch.GlobalCandidate(i)) != (Array.BinarySearch(ordinals, i) >= 0)) throw new InvalidDataException("EReadback.CpuParity");
                rows.Add(new { kind = "Dense", size, count, m.Dispatches, m.ReadbackBytes, digest = Digest(full) });
            }
            using var incoming = new PrivateOrdinalBuffer(rd);
            using (var compact = new EventResultFamilyGpuExecutor(rd, plan, incoming, buffer))
            {
                int count = plan.Capacity + 7;
                var batch = new SearchBatch(first, PrivateOrdinalBuffer.Capacity);
                uint[] source = Enumerable.Range(0, count).Select(i => (uint)((ulong)i * (uint)batch.BatchCandidateCount / (uint)count)).Reverse().ToArray();
                FamilyGpuComputeUtility.Update(rd, incoming.Buffer, source, "EReadback.CompactFixture");
                int n = compact.ExecutePrivateStage(batch, count, CancellationToken.None, out var m);
                var full = buffer.ReadForVerification(batch, n);
                var input = FamilyCandidateSet.FromAbi1(batch, source.Order().Select(i => (ulong)i).ToArray());
                Equal(full, reference.Execute(input, CancellationToken.None, out _), "CompactFullParity");
                if (m.ReadbackBytes != 32) throw new InvalidDataException("EReadback.CompactFinalHeaderNotSingle");
                rows.Add(new { kind = "Compact", size = count, count = n, m.Dispatches, m.ReadbackBytes, digest = Digest(full) });
                foreach (int index in new[] { 0, plan.Capacity })
                {
                    uint saved = source[index]; source[index] = uint.MaxValue;
                    FamilyGpuComputeUtility.Update(rd, incoming.Buffer, source, "EReadback.InvalidBorrowedOrdinal");
                    try { compact.ExecutePrivateStage(batch, count, CancellationToken.None, out _); throw new InvalidOperationException("EReadback.InvalidOrdinalAccepted"); }
                    catch (InvalidDataException ex) when (ex.Message.StartsWith("E.EventResult.HeaderFault:", StringComparison.Ordinal)) { }
                    source[index] = saved;
                }
                FamilyGpuComputeUtility.Update(rd, incoming.Buffer, source, "EReadback.RestoredCompact");
                int restored = compact.ExecutePrivateStage(batch, count, CancellationToken.None, out _);
                Equal(buffer.ReadForVerification(batch, restored), full, "FaultReuse");
            }
            var cancelBatch = new SearchBatch(first, PrivateOrdinalBuffer.Capacity);
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                try { gpu.ExecutePrivateStage(cancelBatch, cancelBatch.BatchCandidateCount, canceled.Token, out _); throw new InvalidOperationException("EReadback.PreCancelMissing"); }
                catch (OperationCanceledException) { }
            }
            using (var canceled = new CancellationTokenSource())
            {
                canceled.CancelAfter(1);
                try { gpu.ExecutePrivateStage(cancelBatch, cancelBatch.BatchCandidateCount, canceled.Token, out _); throw new InvalidOperationException("EReadback.WindowCancelNotObserved"); }
                catch (OperationCanceledException) { }
                var headerRid = ((Rid[])typeof(EventResultFamilyGpuExecutor).GetField("_header", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(gpu)!)[0];
                uint processed = FamilyGpuComputeUtility.FromUInt32Bytes(rd.BufferGetData(headerRid))[4];
                rows.Add(new { kind = "CanceledCall", processed, requested = cancelBatch.BatchCandidateCount, executionInFlightClaimed = processed > 0 && processed < cancelBatch.BatchCandidateCount });
            }
            int resumed = gpu.ExecutePrivateStage(cancelBatch, cancelBatch.BatchCandidateCount, CancellationToken.None, out _);
            Equal(buffer.ReadForVerification(cancelBatch, resumed), reference.Execute(FamilyCandidateSet.Dense(cancelBatch), CancellationToken.None, out _), "CancelReuse");
        }
        File.WriteAllText(Path.Combine(output, "event-readback.json"), JsonSerializer.Serialize(new { passed = true, parity,
            device = rd.GetDeviceName(), conditions = request.Evaluation.EventResultConditions, rows,
            diagnosticOnly = true, productionPrice = false, performanceSample = !parity }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"eventReadback=PASS;parity={parity};rows={rows.Count};numericalBodyChanged=false");
    }
    private static string Digest(FamilyCandidateSet set) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(set.ExportAbi1().Span)));
    private static void Equal(FamilyCandidateSet actual, FamilyCandidateSet expected, string issue)
    {
        if (actual.Batch != expected.Batch || !actual.EnumerateLogicalOrdinals().SequenceEqual(expected.EnumerateLogicalOrdinals()))
            throw new InvalidDataException("EReadback." + issue);
    }
}
