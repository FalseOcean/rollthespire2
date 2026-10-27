using System.Diagnostics;
using System.Text.Json;
using Godot;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;

public partial class Main
{
    // Host measurement A/B only. Each synchronous collector keeps its existing
    // local-RD/owner creation, execution and disposal on the same thread(s).
    private static async Task RunMeasurementCollectorAsync(string[] args, string output,
        string collector, Action collect)
    {
        bool live = args.Contains("--measurement-live-main-loop", StringComparer.Ordinal);
        int callerThread = Environment.CurrentManagedThreadId;
        SynchronizationContext? callerContext = SynchronizationContext.Current;
        long framesBefore = checked((long)Engine.GetProcessFrames());
        int collectorThread = 0;
        double collectorWallMs = 0;
        Exception? primaryFailure = null;
        var wholeWall = Stopwatch.StartNew();
        void Invoke()
        {
            collectorThread = Environment.CurrentManagedThreadId;
            var timer = Stopwatch.StartNew();
            try { collect(); }
            finally { collectorWallMs = timer.Elapsed.TotalMilliseconds; }
        }

        try
        {
            if (live)
            {
                if (callerContext is null)
                    throw new InvalidOperationException("MeasurementLiveMainLoop.RequiresGodotSynchronizationContext");
                // Capture Godot's synchronization context deliberately: the
                // post-collector frame observation must run on the main thread.
                await Task.Run(Invoke);
            }
            else Invoke();
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally
        {
            wholeWall.Stop();
            try
            {
                int observationThread = Environment.CurrentManagedThreadId;
                if (observationThread != callerThread)
                    throw new InvalidOperationException("MeasurementCadence.MainThreadObservationNotResumed");
                long framesAfter = checked((long)Engine.GetProcessFrames());
                string folder = Path.GetFullPath(output);
                Directory.CreateDirectory(folder);
                // Preserve a prior receipt if the original collector rejects
                // an already-used output. Auxiliary failures never change it.
                using var stream = new FileStream(Path.Combine(folder, "measurement-host-cadence.json"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(stream, new
                {
                    SchemaVersion = 1, Source = "HostMeasurementCollectorCadence", Collector = collector,
                    Mode = live ? "LiveMainLoop" : "BlockedMainLoop",
                    CallerManagedThreadId = callerThread, CollectorManagedThreadId = collectorThread,
                    ObservationManagedThreadId = observationThread,
                    CallerSynchronizationContext = callerContext?.GetType().FullName,
                    ProcessFramesBefore = framesBefore, ProcessFramesAfter = framesAfter,
                    ProcessFramesDelta = framesAfter - framesBefore,
                    CollectorWallMs = collectorWallMs, CallerWholeWallMs = wholeWall.Elapsed.TotalMilliseconds,
                    Success = primaryFailure is null, Exception = primaryFailure?.ToString(),
                    ProductExecutionChanged = false, PriceUpdated = false,
                    Boundary = "OriginalSynchronousCollector;CadenceReceiptOutsideCollectorTiming"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception diagnosticFailure)
            {
                // Do not replace a computation exception, or turn a successful
                // measurement into a failure because its optional receipt failed.
                try { Console.Error.WriteLine("measurementCadenceDiagnosticFailed=true;cause=" + diagnosticFailure); }
                catch { }
            }
        }
    }
}
