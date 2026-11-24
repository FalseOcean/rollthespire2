using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Runtime;

public sealed partial class ProductionSearchSession
{
    private readonly bool _initialGpuCreationFallbackAllowed;
    private int _initialGpuCreationRecoveryAttempted;
    // One immutable publication keeps the actual plan, timing disclosure and
    // window in agreement. Original invocations remain owned by session cleanup.
    private sealed record GpuCreationCpuRecovery(FamilyExecutionPlan Plan, FamilySearchEtaProjectionV1 Eta,
        int WindowSize, ulong FirstOrdinal, string Warning);
    private GpuCreationCpuRecovery? _gpuCreationCpuRecovery;

    private async Task<bool> TryRecoverInitialGpuCreationAsync(Exception propagated, SearchBatch abandonedBatch,
        CancellationToken token)
    {
        if (!_initialGpuCreationFallbackAllowed || _executionContext.FailOnPhysicalRecovery ||
            !_executionContext.TryGetInitialGpuCreationFailure(propagated, out var failure) || failure is null ||
            Interlocked.CompareExchange(ref _initialGpuCreationRecoveryAttempted, 1, 0) != 0) return false;

        string correlation = $"session={RuntimeLog.SessionId};attempt={failure.Attempt};environment={failure.EnvironmentKey};" +
            $"originalPlan={_executionPlan.PlanId};batchBase={abandonedBatch.BatchBase};safeCursor={GetSafeNextOrdinal()};context={_plan.SnapshotFingerprint}";
        TryDiagnostic(() => RuntimeIssueLedger.Current.Record(propagated, "GpuInitializationUnavailable",
            nameof(ProductionSearchSession), "CreateLocalRenderingDevice", "issues.cpu_after_gpu_failure",
            RuntimeIssueSeverity.Warning, correlation), "GpuCreationFailureEvidence");

        // This wait exists only after an actual creation failure. CPU prefixes
        // still overlap native initialization on every ordinary startup.
        await _executionContext.JoinFailedGpuOwnerAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        string warning = "GpuInitializationUnavailable;stage=CreateLocalRenderingDevice;" +
            $"replacement=DirectProductionExactCpu;attempt={failure.Attempt};reason={failure.Error.GetType().Name}:" +
            failure.Error.Message.Replace('\r', ' ').Replace('\n', ' ') +
            ";originalQueryRetained=true;gpuFiltering=false";
        var replacement = FamilyPlanner.Plan([]) with
        {
            PlanId = "GpuInitializationFallback.DirectProductionExact",
            SelectionPolicyId = "InitialGpuCreationFailure.DirectProductionExact.20261025.v1",
            AllConditionsUnfiltered = true,
            FinalValidationSkipped = false,
            FilterRemainders = ["AllConditions:ProductionExactAfterGpuCreationFailure"],
            DecisionEvidence = [warning],
            MissingEvidence = ["RecoveryTimingUnavailable"]
        };
        // Query probability remains the same. Every old allocation-dependent
        // time is removed instead of displaying the retired GPU plan's price.
        var eta = _etaProjection with
        {
            Status = FamilySearchEtaStatus.Unavailable, Plan = replacement,
            FirstResultMeanMs = null, FirstResultP99Ms = null, TargetMeanMs = null, FixedScanRangeMs = null,
            FirstResultSearchMeanMs = null, FirstResultSearchP99Ms = null, TargetSearchMeanMs = null,
            TargetFamilyPipelineMs = null, TargetExactTailMs = null, TargetStages = [],
            MissingEvidence = ["GpuInitializationUnavailable", "RecoveryTimingUnavailable"]
        };
        int window = Math.Min(ExactOnlyMaximumBatchSize, Math.Max(256, _plan.WorkerCount * 128));
        // No candidate from abandonedBatch was offered to Exact or published.
        // Its optional diagnostic slots can be released before smaller windows.
        _executionContext.ReleaseFilterIsolations(abandonedBatch);
        _executionWarnings.TryAdd(warning, 0);
        Volatile.Write(ref _gpuCreationCpuRecovery,
            new GpuCreationCpuRecovery(replacement, eta, window, abandonedBatch.BatchBase, warning));
        RuntimeLog.TryBackgroundWarning($"gpuCreationCpuRecovery=true;{correlation};" +
            $"executionWindowSize={window};retiredInvocations={_families.Count};nativeRetry=false;" +
            "originalQueryRetained=true;remainingConditionsOwner=ProductionExact;calibrationEligible=false");
        return true;
    }
}
