using System.Diagnostics;

namespace RolltheSpire2.Search.FamilyExecution;

// Diagnostic wall-clock phases. SyncWaitMs is host fence wait, not pure kernel
// duration. PrepareWorkMs can overlap device phases; phase sums are not TotalMs.
internal sealed record MapLossyGpuMetrics(long Calls, long Inputs, long Dispatches, long SubmissionGroups,
    double PlanSetupMs, double PrepareWorkMs, double CpuBudgetWaitMs, double PreparationWaitMs,
    double UploadRecordMs, double SubmitMs, double SyncWaitMs, double ReadbackDecodeMs, double TotalMs)
{
    internal static MapLossyGpuMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    internal MapLossyGpuMetrics Plus(MapLossyGpuMetrics other) => new(Calls + other.Calls, Inputs + other.Inputs,
        Dispatches + other.Dispatches, SubmissionGroups + other.SubmissionGroups, PlanSetupMs + other.PlanSetupMs,
        PrepareWorkMs + other.PrepareWorkMs, CpuBudgetWaitMs + other.CpuBudgetWaitMs,
        PreparationWaitMs + other.PreparationWaitMs, UploadRecordMs + other.UploadRecordMs,
        SubmitMs + other.SubmitMs, SyncWaitMs + other.SyncWaitMs,
        ReadbackDecodeMs + other.ReadbackDecodeMs, TotalMs + other.TotalMs);
}

internal sealed partial class MapLossyGpuExecutor
{
    private MapLossyGpuMetrics _lastMetrics = MapLossyGpuMetrics.Empty, _cumulativeMetrics = MapLossyGpuMetrics.Empty;
    internal MapLossyGpuMetrics LastMetrics => Volatile.Read(ref _lastMetrics);
    internal MapLossyGpuMetrics CumulativeMetrics => Volatile.Read(ref _cumulativeMetrics);
    private void PublishMetrics(MapLossyGpuMetrics metrics)
    {
        Volatile.Write(ref _lastMetrics, metrics);
        Volatile.Write(ref _cumulativeMetrics, _cumulativeMetrics.Plus(metrics));
    }
    private sealed class PhaseMeasurement(int inputs)
    {
        internal long PlanSetupTicks, PrepareWorkTicks, CpuBudgetWaitTicks, PreparationWaitTicks,
            UploadRecordTicks, SubmitTicks, SyncWaitTicks, ReadbackDecodeTicks, Dispatches, SubmissionGroups;
        private static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
        internal MapLossyGpuMetrics Snapshot(long totalTicks) => new(1, inputs, Dispatches, SubmissionGroups,
            Ms(PlanSetupTicks), Ms(PrepareWorkTicks), Ms(CpuBudgetWaitTicks), Ms(PreparationWaitTicks),
            Ms(UploadRecordTicks), Ms(SubmitTicks), Ms(SyncWaitTicks), Ms(ReadbackDecodeTicks), Ms(totalTicks));
    }
    private sealed record PreparedChunk(ulong[] Ordinals, byte[] Data);
    private PreparedChunk PrepareChunk(FamilyCandidateSet input, ReadOnlyMemory<ulong> compact, int first,
        MapLossyActPlan[] acts, int ascension, CancellationToken token, PhaseMeasurement metrics)
    {
        long phase = Stopwatch.GetTimestamp();
        try { _context?.EnterCpu(token); }
        finally { metrics.CpuBudgetWaitTicks += Stopwatch.GetTimestamp() - phase; }
        phase = Stopwatch.GetTimestamp();
        try
        {
            token.ThrowIfCancellationRequested();
            ExecutionProbe?.Invoke("PrepareStarting");
            int count = Math.Min(_capacity, input.Count - first);
            var ordinals = new ulong[count];
            if (input.IsDense) for (int i = 0; i < count; i++) ordinals[i] = (ulong)(first + i);
            else compact.Span.Slice(first, count).CopyTo(ordinals);
            byte[] data = Prepare(input.Batch, ordinals, acts, ascension, token);
            ExecutionProbe?.Invoke("Prepared");
            return new(ordinals, data);
        }
        finally
        {
            metrics.PrepareWorkTicks += Stopwatch.GetTimestamp() - phase;
            _context?.ExitCpu();
        }
    }
}
