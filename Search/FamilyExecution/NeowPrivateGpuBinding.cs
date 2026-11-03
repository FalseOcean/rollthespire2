using System.Diagnostics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// Typed N workspace borrowing stays with its numerical producer. Transport sees
// only a resource lease and batch-local private/public ordinal outcomes.
internal static class NeowPrivateGpuBinding
{
    private sealed record NeowScratchLease(NeowFamilyGpuExecutor Executor) : IPrivateGpuScratchLease;

    internal static FamilyPrivateGpuExecution Bind(NeowFamilyGpuPlan plan, NeowGpuOptimizations options,
        RenderingDevice device, PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output,
        IPrivateGpuScratchLease? shared = null)
    {
        if (plan.NumericalVariant != NeowGpuNumericalVariant.NativeFloat64)
            throw new InvalidOperationException("N.PrivateSerialNotAdmitted");
        var donor = shared is null ? null : (shared as NeowScratchLease)?.Executor ??
            throw new InvalidOperationException("N.PrivateScratchLeaseMismatch");
        var gpu = new NeowFamilyGpuExecutor(device, plan, 42, privateInput: input, privateOutput: output,
            sharedDenseScratch: donor, optimizations: options);
        return new(gpu, gpu.Device, gpu.SetupMs, input, output, (batch, count, token) => {
            FamilyCandidateSet? filtered = null; NeowFamilyGpuMetrics metrics;
            if (output is not null) gpu.ExecutePrivateStage(batch, count, token, out metrics);
            else filtered = input is null ? gpu.Execute(FamilyCandidateSet.Dense(batch), token, out metrics) :
                gpu.ExecutePrivate(batch, count, token, out metrics);
            return new(metrics.Survivors, filtered, metrics.DispatchSyncMs, metrics.ReadbackMs,
                metrics.ReadbackBytes, metrics.CanonicalMs, metrics.Dispatches) { SmallOutput = metrics.SmallOutput };
        }) {
            ScratchLease = new NeowScratchLease(gpu),
            DensePair = input is not null || output is null || plan.DirectNestedMode != 0 || plan.JointTransformMetaOffset >= 0
                ? null : (rightBinding, first, second, token) => {
                    if (rightBinding.ScratchLease is not NeowScratchLease right)
                        throw new InvalidOperationException("N.PrivateDensePairLeaseMismatch");
                    var timer = Stopwatch.StartNew();
                    try {
                        var pair = NeowFamilyGpuExecutor.ExecuteDensePair(gpu, first, right.Executor, second, token);
                        return new(Outcome(pair.LeftMetrics), Outcome(pair.RightMetrics), null,
                            timer.Elapsed.TotalMilliseconds, pair.SharedMs);
                    }
                    catch (NeowDensePairRightFailure error) {
                        return new(Outcome(error.LeftMetrics), null, error.InnerException ?? error,
                            timer.Elapsed.TotalMilliseconds, error.SharedMs);
                    }
                }
        };
    }

    private static FamilyPrivateGpuOutcome Outcome(NeowFamilyGpuMetrics metrics) =>
        new(metrics.Survivors, null, 0, metrics.ReadbackMs, metrics.ReadbackBytes,
            metrics.CanonicalMs, metrics.Dispatches) { SmallOutput = metrics.SmallOutput };
}
