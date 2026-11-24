using Godot;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Infrastructure.Diagnostics;

internal static class RenderingEnvironmentEvidence
{
    internal sealed record DriverEvidence(string Status, string? Name = null, string? Version = null);

    private static readonly DriverEvidence NotStarted = new("NotStarted");
    private static DriverEvidence _driver = NotStarted;

    internal static DriverEvidence CaptureDriver() => Volatile.Read(ref _driver);

    internal static string ConfiguredRenderingMethod() =>
        ProjectSettings.GetSettingWithOverride("rendering/renderer/rendering_method").AsString();

    internal static object CaptureHostHardware()
    {
        var memory = OS.GetMemoryInfo();
        long? Bytes(string key) => memory.TryGetValue(key, out var value) && value.AsInt64() > 0
            ? value.AsInt64() : null;
        return new { Processor = OS.GetProcessorName(), LogicalProcessors = OS.GetProcessorCount(),
            PhysicalMemoryBytes = Bytes("physical"), AvailableMemoryBytes = Bytes("available") };
    }

    internal static void RecordStartup()
    {
        try
        {
            RuntimeLog.Info("renderingEnvironmentJson=" + RuntimeLog.SafeJson(new
            {
                ConfiguredRenderingMethod = ConfiguredRenderingMethod(),
                RenderingMethod = RenderingServer.GetCurrentRenderingMethod(),
                RenderingDriver = RenderingServer.GetCurrentRenderingDriverName(),
                GPU = RenderingServer.GetVideoAdapterName(),
                AdapterApiVersion = RenderingServer.GetVideoAdapterApiVersion()
            }));
        }
        catch (Exception exception) { RuntimeLog.WarnException("renderingEnvironmentCaptureFailed=true", exception); }
        try { RuntimeLog.Info("hostHardwareJson=" + RuntimeLog.SafeJson(CaptureHostHardware())); }
        catch (Exception exception) { RuntimeLog.WarnException("hostHardwareCaptureFailed=true", exception); }
        OperationalFileLog.Flush();

        // Godot's OS driver probe can block for seconds. Start it once per process
        // during runtime initialization and report only the published snapshot.
        if (ReferenceEquals(Interlocked.CompareExchange(ref _driver, new("Pending"), NotStarted), NotStarted))
            _ = Task.Run(ProbeDriver);
    }

    private static void ProbeDriver()
    {
        DriverEvidence evidence;
        try
        {
            string[] details = OS.GetVideoAdapterDriverInfo();
            evidence = details.Length >= 2 && !string.IsNullOrWhiteSpace(details[1])
                ? new("Available", details[0], details[1])
                : new("Unavailable");
        }
        catch (Exception exception)
        {
            evidence = new("Failed");
            RuntimeLog.WarnException("renderingDriverProbeFailed=true", exception);
        }
        Volatile.Write(ref _driver, evidence);
        RuntimeLog.TryBackgroundInfo("renderingAdapterDriverJson=" + RuntimeLog.SafeJson(evidence));
        OperationalFileLog.Flush();
    }
}
