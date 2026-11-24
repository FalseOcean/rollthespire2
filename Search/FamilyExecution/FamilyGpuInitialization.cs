using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum FamilyGpuInitializationState { Unverified, Creating, Available, Failed }

internal sealed record FamilyGpuInitializationFailure(
    string Stage, string ExceptionType, string Reason, string Details, DateTimeOffset OccurredUtc);

internal sealed record FamilyGpuInitializationSnapshot(
    long Revision, string EnvironmentKey, bool MainDeviceAvailable,
    FamilyGpuInitializationState State, long Attempt, int ActiveOwners,
    DateTimeOffset? AttemptedUtc, FamilyGpuInitializationFailure? LastFailure,
    string RenderingBackend = "Unknown")
{
    internal VulkanNumericCapabilitySnapshot? VulkanCapabilities { get; init; }
    public string Status => State.ToString();
    public string Stage => State switch
    {
        FamilyGpuInitializationState.Unverified => "NotAttempted",
        FamilyGpuInitializationState.Creating => "CreateLocalRenderingDevice",
        FamilyGpuInitializationState.Available => "DeviceCreated",
        _ => LastFailure?.Stage ?? "CreateLocalRenderingDevice"
    };
    public bool GpuEligible => MainDeviceAvailable && State != FamilyGpuInitializationState.Failed;
    public bool CanRetry => MainDeviceAvailable && ActiveOwners == 0;
    // Backend language boundary, not a GPU-model blacklist or a successful
    // capability probe. Godot 4.5 translates Metal shaders through MSL, whose
    // double type is unsupported. A queried Vulkan physical-device negative
    // selects the compatible body; positive/unknown remain an unverified attempt.
    // https://developer.apple.com/metal/Metal-Shading-Language-Specification.pdf
    // Godot 4.5 thirdparty/spirv-cross/spirv_msl.cpp: SPIRType::Double.
    public bool CanAttemptFloat64 => !string.Equals(RenderingBackend, "metal", StringComparison.OrdinalIgnoreCase) &&
        VulkanCapabilities?.ShaderFloat64 != false;
    public string Float64Support => string.Equals(RenderingBackend, "metal", StringComparison.OrdinalIgnoreCase)
        ? "KnownUnsupported" : VulkanCapabilities?.ShaderFloat64 == false ? "ObservedDeviceUnsupported" : "Unverified";
    public string Float64Reason => string.Equals(RenderingBackend, "metal", StringComparison.OrdinalIgnoreCase)
        ? "MetalMslDoubleUnsupported" : VulkanCapabilities?.ShaderFloat64 == false
        ? "ObservedVulkanPhysicalDeviceFloat64Unavailable" : VulkanCapabilities?.Reason ?? "NotVerified";
}

/// <summary>
/// Process-local Local RenderingDevice creation evidence and separately captured
/// backend identity. Shader, dispatch, readback and resource-release failures
/// never change the creation verdict.
/// Nothing here is persisted as a device ban or changes returned-result authority.
/// </summary>
internal sealed class FamilyGpuInitialization
{
    private readonly object _gate = new();
    private FamilyGpuInitializationSnapshot _snapshot = new(0, "Uncaptured", true,
        FamilyGpuInitializationState.Unverified, 0, 0, null, null);
    private long _nextAttempt;

    internal FamilyGpuInitializationSnapshot Capture() { lock (_gate) return _snapshot; }

    internal bool ObserveVulkanCapabilities(VulkanNumericCapabilitySnapshot evidence)
    {
        lock (_gate)
        {
            if (_snapshot.EnvironmentKey != evidence.EnvironmentKey ||
                !string.Equals(_snapshot.RenderingBackend, "vulkan", StringComparison.OrdinalIgnoreCase)) return false;
            // Main and local RD may choose different physical GPUs. A negative
            // main observation selects the compatible body conservatively;
            // positive/unknown observations never certify local execution.
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, VulkanCapabilities = evidence };
            return true;
        }
    }

    internal void ObserveEnvironment(string environmentKey, bool mainDeviceAvailable, string renderingBackend = "Unknown")
    {
        lock (_gate)
        {
            if (_snapshot.EnvironmentKey == environmentKey && _snapshot.MainDeviceAvailable == mainDeviceAvailable &&
                _snapshot.RenderingBackend == renderingBackend) return;
            _snapshot = new(_snapshot.Revision + 1, environmentKey, mainDeviceAvailable,
                FamilyGpuInitializationState.Unverified, ++_nextAttempt, _snapshot.ActiveOwners, null, null, renderingBackend);
        }
    }

    internal long BeginCreation(bool explicitRetry = false)
    {
        lock (_gate)
        {
            if (!_snapshot.MainDeviceAvailable)
                throw new InvalidOperationException("FamilyGpuMainRenderingDeviceUnavailable");
            if (explicitRetry && _snapshot.ActiveOwners != 0)
                throw new InvalidOperationException("FamilyGpuInitializationBusy");
            if (!explicitRetry && _snapshot.State == FamilyGpuInitializationState.Failed)
                throw new InvalidOperationException("FamilyGpuInitializationRetryRequired");
            long attempt = ++_nextAttempt;
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Attempt = attempt,
                State = FamilyGpuInitializationState.Creating, ActiveOwners = _snapshot.ActiveOwners + 1,
                AttemptedUtc = DateTimeOffset.UtcNow };
            RuntimeLog.TryBackgroundInfo($"familyGpuInitializationStarted=true;attempt={attempt};" +
                $"stage=CreateLocalRenderingDevice;explicitRetry={explicitRetry};environment={San(_snapshot.EnvironmentKey)}");
            return attempt;
        }
    }

    internal void CreationSucceeded(long attempt) => CompleteCreation(attempt, null);
    internal void CreationAborted(long attempt, FamilyGpuInitializationState previousState)
    {
        lock (_gate)
            if (_snapshot.Attempt == attempt)
                _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, State = previousState };
    }
    internal void CreationFailed(long attempt, Exception exception) => CompleteCreation(attempt,
        new("CreateLocalRenderingDevice", exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message, exception.ToString(), DateTimeOffset.UtcNow));

    private void CompleteCreation(long attempt, FamilyGpuInitializationFailure? failure)
    {
        lock (_gate)
        {
            // A callback from an older environment/attempt cannot replace newer evidence.
            if (_snapshot.Attempt != attempt) return;
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1,
                State = failure is null ? FamilyGpuInitializationState.Available : FamilyGpuInitializationState.Failed,
                LastFailure = failure ?? _snapshot.LastFailure };
            RuntimeLog.TryBackgroundInfo($"familyGpuInitialization=true;attempt={attempt};state={_snapshot.State};" +
                $"stage=CreateLocalRenderingDevice;reason={San(failure?.Reason ?? "Created")};" +
                $"environment={San(_snapshot.EnvironmentKey)};explicitRetryRequired={failure is not null}");
        }
    }

    internal void OwnerStopped()
    {
        lock (_gate)
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, ActiveOwners = _snapshot.ActiveOwners - 1 };
    }

    private static string San(string value) => value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
}
