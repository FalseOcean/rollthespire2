using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

internal static class GpuInitializationChecks
{
    internal static bool Run()
    {
        int checks = 0;
        void Require(bool ok, string message)
        { if (!ok) throw new InvalidOperationException(message); checks++; }
        try
        {
            var state = new FamilyGpuInitialization();
            Require(state.Capture().GpuEligible && state.Capture().State == FamilyGpuInitializationState.Unverified,
                "unknown remains eligible, without claiming a successful creation");
            state.ObserveEnvironment("D3D12|adapter-A|device-1", true);
            int creations = 0;
            async Task FailCreation(bool retry = false)
            {
                await using var owner = new FamilyGpuExecutionOwner(() => { creations++; return null; }, state, retry);
                try { await owner.ExecuteAsync(_ => 1, CancellationToken.None); }
                catch (InvalidOperationException error)
                { Require(error.Message.StartsWith("FamilyGpuRenderingDeviceUnavailable"), "null device keeps creation fault"); }
            }
            FailCreation().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var failure = state.Capture();
            Require(failure.State == FamilyGpuInitializationState.Failed && !failure.GpuEligible &&
                failure.ActiveOwners == 0 && failure.CanRetry, "creation failure blocks GPU until explicit retry");
            Require(failure.LastFailure is { Stage: "CreateLocalRenderingDevice" } &&
                failure.LastFailure.Reason.Contains("Godot log") && failure.LastFailure.Details.Contains("ThreadMain"),
                "stage, managed reason, stack and native-log boundary retained");
            for (int i = 0; i < 3; i++)
            {
                state.ObserveEnvironment("D3D12|adapter-A|device-1", true);
                try
                {
                    _ = new FamilyGpuExecutionOwner(() => { creations++; return null; }, state);
                    throw new InvalidOperationException("blocked creation was admitted");
                }
                catch (InvalidOperationException error) when (error.Message == "FamilyGpuInitializationRetryRequired") { }
            }
            Require(creations == 1 && state.Capture().LastFailure == failure.LastFailure,
                "reopening the same environment and repeated admission do not create devices");
            FailCreation(retry: true).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Require(creations == 2 && state.Capture().Attempt > failure.Attempt && !state.Capture().GpuEligible,
                "explicit retry actually attempts creation, without assuming success");

            long retryAttempt = state.BeginCreation(explicitRetry: true);
            try { state.BeginCreation(explicitRetry: true); throw new InvalidOperationException("concurrent retry admitted"); }
            catch (InvalidOperationException error) when (error.Message == "FamilyGpuInitializationBusy") { }
            Require(!state.Capture().CanRetry, "retry disabled while an owner is active");
            state.CreationSucceeded(retryAttempt);
            state.OwnerStopped();
            Require(state.Capture().GpuEligible && state.Capture().State == FamilyGpuInitializationState.Available &&
                state.Capture().LastFailure is not null, "success restores eligibility and preserves previous diagnostic evidence");

            long oldAttempt = state.BeginCreation();
            state.ObserveEnvironment("Vulkan|adapter-A|device-2", true);
            state.CreationFailed(oldAttempt, new InvalidOperationException("old environment failure"));
            state.OwnerStopped();
            Require(state.Capture().State == FamilyGpuInitializationState.Unverified &&
                state.Capture().GpuEligible && state.Capture().LastFailure is null,
                "new environment is unverified; stale callback cannot poison it");
            state.ObserveEnvironment("Compatibility|none", false);
            Require(!state.Capture().GpuEligible && !state.Capture().CanRetry, "missing main device is distinct from failed local creation");
            Require(new FamilyGpuInitialization().Capture().LastFailure is null, "failure is not a persisted device ban");

            state.ObserveEnvironment("D3D12|adapter-B|device-3", true);
            var injected = new InvalidOperationException("native wrapper threw");
            var throwingOwner = new FamilyGpuExecutionOwner(() => throw injected, state);
            try { throwingOwner.ExecuteAsync(_ => 1, CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch (InvalidOperationException error) { Require(ReferenceEquals(error, injected), "original creation exception preserved"); }
            throwingOwner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Require(state.Capture().LastFailure?.Reason == injected.Message, "thrown creation failure retained as well as null failure");

            // Inject only query receipts: no Vulkan loader/native call and no
            // successful local-device creation is manufactured by these checks.
            const string numericEnvironment = "Vulkan|observed-main|numeric-device";
            var numeric = new FamilyGpuInitialization();
            numeric.ObserveEnvironment(numericEnvironment, true, "vulkan");
            var numericBefore = numeric.Capture();
            var noFloat64 = new VulkanNumericCapabilitySnapshot(numericEnvironment, "ObservedMainRenderingDevice", "vulkan",
                "observed-main", 1, 2, false, false, "VulkanPhysicalDeviceFeaturesQueried",
                "physicalSupportOnly=true;logicalEnablementUnverified=true;kernelExecutionUnverified=true");
            Require(numeric.ObserveVulkanCapabilities(noFloat64), "current Vulkan environment accepts physical capability receipt");
            var limited = numeric.Capture();
            Require(!limited.CanAttemptFloat64 && limited.Float64Support == "ObservedDeviceUnsupported" &&
                limited.GpuEligible == numericBefore.GpuEligible && limited.Attempt == numericBefore.Attempt &&
                limited.State == FamilyGpuInitializationState.Unverified && limited.ActiveOwners == 0 && limited.LastFailure is null,
                "negative main FP64 observation selects compatibility without disabling GPU, including when observed Int64 is false");
            Require(limited.VulkanCapabilities is { Scope: "ObservedMainRenderingDevice" } mainEvidence && ReferenceEquals(mainEvidence, noFloat64),
                "receipt retains its observed-main scope rather than certifying the local compute adapter");
            var withFloat64 = noFloat64 with { ShaderFloat64 = true, ShaderInt64 = true };
            Require(numeric.ObserveVulkanCapabilities(withFloat64) && numeric.Capture() is
                { CanAttemptFloat64: true, Float64Support: "Unverified", State: FamilyGpuInitializationState.Unverified, LastFailure: null },
                "positive physical support permits the native attempt but does not claim local creation or execution");
            string queryFailureDetails = new InvalidOperationException("Vulkan query wrapper failed",
                new EntryPointNotFoundException("vkGetPhysicalDeviceFeatures")).ToString();
            var unknown = noFloat64 with { ShaderFloat64 = null, ShaderInt64 = null,
                Reason = "ResolveVulkanQueryFailed", Details = queryFailureDetails, CleanupDetails = "loader-reference cleanup evidence" };
            Require(numeric.ObserveVulkanCapabilities(unknown) && numeric.Capture() is
                { CanAttemptFloat64: true, Float64Support: "Unverified", State: FamilyGpuInitializationState.Unverified,
                    GpuEligible: true, LastFailure: null },
                "failed capability query remains unknown and cannot become a device-creation failure");
            numeric.ObserveEnvironment(numericEnvironment, true, "vulkan");
            Require(numeric.Capture().VulkanCapabilities is { } retained && retained.Details == queryFailureDetails &&
                retained.Reason == "ResolveVulkanQueryFailed" && retained.CleanupDetails == unknown.CleanupDetails &&
                numeric.Capture().Float64Reason == unknown.Reason,
                "same-environment refresh retains original query exception chain and separate cleanup details");
            numeric.ObserveEnvironment(numericEnvironment + "-new", true, "vulkan");
            var newNumericEnvironment = numeric.Capture();
            Require(newNumericEnvironment.VulkanCapabilities is null && newNumericEnvironment.CanAttemptFloat64 &&
                newNumericEnvironment.Float64Support == "Unverified", "new environment clears old numeric capability evidence");
            Require(!numeric.ObserveVulkanCapabilities(noFloat64) && numeric.Capture() == newNumericEnvironment,
                "late old-environment receipt is rejected without modifying the current snapshot");
            numeric.ObserveEnvironment(numericEnvironment, true, "d3d12");
            Require(!numeric.ObserveVulkanCapabilities(noFloat64) && numeric.Capture().VulkanCapabilities is null,
                "Vulkan evidence is not applied to another backend even when its environment key matches");

            // Exercise real planning/admission with an existing tiny CPU corpus,
            // not a substitute scheduler. This process has no native GPU device.
            var global = FamilyDeviceProfileFoundation.Initialization;
            var original = global.Capture();
            try
            {
                global.ObserveEnvironment("GpuInitializationChecks", true);
                long failedAttempt = global.BeginCreation();
                global.CreationFailed(failedAttempt, injected); global.OwnerStopped();
                var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
                var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/v1/cases/S-three-shops.json");
                var compiled = SearchCompiler.Compile(workload.Query, context.Context);
                var request = ExactSearchExecutionRequestFactory.Compile(compiled, workload.Options with { ScanCount = 16 }).Plan!;
                var plan = FamilyExecutionCoordinator.Plan(request);
                Require(plan.OrderedFamilies.All(f => !f.ConditionPerformance.UsesGpu), "known creation failure excluded from planning");
                foreach (bool? availability in new bool?[] { null, false })
                {
                    var session = FamilyExecutionCoordinator.Start(request, gpuAvailable: availability);
                    try
                    {
                        session.Completion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                        Require(session.GetProgress().State == SearchRunState.Completed, "Auto and explicit CPU execute admitted CPU search after device failure");
                        Require(global.Capture().Attempt == failedAttempt && global.Capture().State == FamilyGpuInitializationState.Failed,
                            "CPU search does not create a GPU or pretend it was repaired");
                    }
                    finally { session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
                }
                global.ObserveEnvironment(numericEnvironment, true, "vulkan");
                Require(global.ObserveVulkanCapabilities(noFloat64), "planner receives current negative observed-main FP64 receipt");
                var beforePlanning = global.Capture();
                var neowWorkload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/v1/cases/N-entry.json");
                var neowRequest = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(neowWorkload.Query, context.Context),
                    neowWorkload.Options with { ScanCount = 16 }).Plan!;
                var normalPlan = FamilyExecutionCoordinator.Plan(neowRequest);
                var selectedNeow = normalPlan.OrderedFamilies.OfType<NeowFamily>().SingleOrDefault();
                Require(selectedNeow is { GpuPlan.NumericalVariant: NeowGpuNumericalVariant.IntegerUnitFloat } &&
                    selectedNeow.ConditionPerformance.UsesGpu && ((IFamilyInvocation)selectedNeow).GpuNumericRequirements == GpuNumericRequirements.Int64 &&
                    selectedNeow.QuotePhysicalWork(new(false, false, 16)) is null &&
                    normalPlan.OrderedFamilies.All(f => (f.GpuNumericRequirements &
                        (GpuNumericRequirements.Float64 | GpuNumericRequirements.Unspecified)) == 0),
                    "normal planning selects the actual unpriced integer N GPU body from observed Vulkan FP64=false");
                Require(global.Capture() == beforePlanning && normalPlan.DecisionEvidence.Any(e =>
                    e.StartsWith("GpuFloat64Unavailable;", StringComparison.Ordinal)),
                    "managed planning preserves unknown local creation state and records the numeric selection without GPU execution");
            }
            finally
            {
                global.ObserveEnvironment(original.EnvironmentKey, original.MainDeviceAvailable, original.RenderingBackend);
                if (original.VulkanCapabilities is { } numericEvidence) global.ObserveVulkanCapabilities(numericEvidence);
            }
            Console.WriteLine($"gpu_initialization=passed;checks={checks}");
            return true;
        }
        catch (Exception error)
        {
            Console.WriteLine($"gpu_initialization=failed;checks={checks};error={error}");
            return false;
        }
    }
}
