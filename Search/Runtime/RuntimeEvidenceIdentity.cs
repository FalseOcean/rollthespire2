using System.Runtime.InteropServices;

namespace RolltheSpire2.Search.Runtime;

internal readonly record struct SearchPerformanceDeviceIdentity(
    string RuntimeTarget,
    string CpuIdentity,
    string GpuIdentity,
    string RenderingBackend)
{
    public bool HasKnownGpu => !string.IsNullOrWhiteSpace(GpuIdentity);
    public string Summary =>
        $"runtime={RuntimeTarget};cpu={CpuIdentity};gpu={(HasKnownGpu ? GpuIdentity : "Unknown")};rendering={(string.IsNullOrWhiteSpace(RenderingBackend) ? "Unknown" : RenderingBackend)}";
}

/// <summary>
/// Compatibility identity shared by current hardware calibration and Exact timing evidence.
/// This deliberately contains no legacy performance profile, Planner pricing, or persistence logic.
/// </summary>
internal static class SearchPerformanceProfileFoundation
{
    public const int MeasurementAttributionSchemaVersion = 2;
    public const string RuntimeTarget = "Modern110-Physical-v1";

    private static readonly object DeviceGate = new();
    private static string _lastObservedGpu = string.Empty;
    private static string _lastObservedRenderingBackend = string.Empty;

    public static SearchPerformanceDeviceIdentity CaptureKnownDeviceIdentity()
    {
        string cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString();
        lock (DeviceGate)
        {
            return new SearchPerformanceDeviceIdentity(RuntimeTarget, San(cpu), _lastObservedGpu, _lastObservedRenderingBackend);
        }
    }

    public static void ObserveRenderingBackend(string renderingBackend)
    {
        if (string.IsNullOrWhiteSpace(renderingBackend)) return;
        lock (DeviceGate) _lastObservedRenderingBackend = San(renderingBackend);
    }

    public static void ObserveGpuIdentity(string gpuDevice, string renderingBackend)
    {
        if (string.IsNullOrWhiteSpace(gpuDevice)) return;
        lock (DeviceGate)
        {
            _lastObservedGpu = San(gpuDevice);
            if (!string.IsNullOrWhiteSpace(renderingBackend))
                _lastObservedRenderingBackend = San(renderingBackend);
        }
    }

    public static string San(string value) =>
        (value ?? string.Empty).Replace(';', '_').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
