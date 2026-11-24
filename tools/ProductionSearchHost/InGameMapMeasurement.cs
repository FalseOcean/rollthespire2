using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Semantics;
using Environment = System.Environment;

// Temporary opt-in diagnostic mod entry. The game owns normal initialization;
// this node only replays portable search inputs through Main's existing path.
[ModInitializer(nameof(Initialize))]
public partial class InGameMapMeasurement : Main
{
    private const string EnableVariable = "RT2_MAP_GAME_MEASUREMENT_DIR";
    private const string ModeVariable = "RT2_MAP_GPU_PIPELINE";
    private const int SettleMs = 10000;
    private static string? _enabledDirectory;
    private static int _attached;
    private static Harmony? _harmony;
    private readonly List<object> _completed = new();
    private readonly Queue<Measurement> _queue = new();
    private readonly Stopwatch _elapsed = new();
    private Measurement? _active;
    private string _directory = "", _contextPath = "", _workloadPath = "";
    private string? _previousMode;
    private object? _identity;
    private bool _delay, _finished, _stopRequested, _capturedMode;
    private bool _measurementCompleted, _exitCleanupAttempted, _exitCleanupCompleted;
    private Exception? _exitCleanupFailure;
    private DateTimeOffset _started;
    private sealed record Measurement(string Label, string Mode, bool Pipeline = false);

    public static void Initialize()
    {
        string? requested = Environment.GetEnvironmentVariable(EnableVariable);
        if (string.IsNullOrWhiteSpace(requested)) return;
        if (!Path.IsPathFullyQualified(requested) || !Directory.Exists(requested))
            throw new InvalidDataException(EnableVariable + " must name an existing absolute directory.");
        _enabledDirectory = Path.GetFullPath(requested);
        _harmony = new Harmony("RolltheSpire2.ProductionSearchHost.InGameMapMeasurement");
        _harmony.Patch(AccessTools.Method(typeof(NMainMenu), nameof(NMainMenu._Ready)),
            postfix: new HarmonyMethod(typeof(InGameMapMeasurement), nameof(MenuReady)));
        GD.Print("inGameMapMeasurement=Armed;directory=" + _enabledDirectory);
    }

    private static void MenuReady(NMainMenu __instance)
    {
        if (_enabledDirectory is null || Interlocked.Exchange(ref _attached, 1) != 0) return;
        string directory = _enabledDirectory;
        // Both scene attachment and every RunAsync start stay on the Godot thread.
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(__instance))
            {
                GD.PrintErr("InGameMapMeasurement: main menu disappeared before deferred attachment.");
                return;
            }
            __instance.GetTree().Root.AddChild(new InGameMapMeasurement { _directory = directory });
        }).CallDeferred();
    }

    public override void _Ready()
    {
        // Never invoke Main._Ready/InitializeHostOnMainThread in the real game.
        if (_enabledDirectory is null || string.IsNullOrEmpty(_directory)) { QueueFree(); return; }
        _started = DateTimeOffset.UtcNow; _elapsed.Start();
        _previousMode = Environment.GetEnvironmentVariable(ModeVariable); _capturedMode = true;
        GetTree().AutoAcceptQuit = false;
        try
        {
            _identity = new
            {
                pid = Environment.ProcessId, executable = OS.GetExecutablePath(), commandLine = Environment.CommandLine,
                engine = Engine.GetVersionInfo().ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToString()),
                renderer = RenderingServer.GetCurrentRenderingMethod(), driver = RenderingServer.GetCurrentRenderingDriverName(),
                gpu = RenderingServer.GetVideoAdapterName(), displayServer = DisplayServer.GetName(),
                engineMaxFps = Engine.MaxFps, synchronizationContext = SynchronizationContext.Current?.GetType().FullName,
                godotThreadId = Environment.CurrentManagedThreadId,
                diagnosticHost = AssemblyIdentity(typeof(InGameMapMeasurement).Assembly),
                mod = AssemblyIdentity(typeof(SearchCompiler).Assembly), game = AssemblyIdentity(typeof(NMainMenu).Assembly),
                loadedMods = ModManager.Mods.Select(mod => new { id = mod.manifest?.id, version = mod.manifest?.version,
                    mod.path, state = mod.state.ToString(), errors = mod.errors?.Select(error => error.ToString()).ToArray() }).ToArray()
            };
            // Freeze the two submitted inputs; every session uses identical bytes.
            _contextPath = Path.Combine(_directory, "measurement-context.json");
            _workloadPath = Path.Combine(_directory, "measurement-workload.json");
            File.Copy(Path.Combine(_directory, "context.json"), _contextPath, overwrite: false);
            File.Copy(Path.Combine(_directory, "workload.json"), _workloadPath, overwrite: false);
            _ = ProductionSearchReplay.Read<ProductionSearchReplayContext>(_contextPath);
            _ = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(_workloadPath);
            if (Environment.GetEnvironmentVariable("RT2_MAP_GAME_MEASUREMENT_PIPELINE") == "1")
                _queue.Enqueue(new("pipeline-4m", "baseline", Pipeline: true));
            for (int round = 0; round < 3; round++)
                foreach (string mode in round % 2 == 0 ? new[] { "baseline", "prefetch" } : new[] { "prefetch", "baseline" })
                    _queue.Enqueue(new($"session-r{round}-{mode}", mode));
            WriteReceipt("waiting-ten-second-settle");
            // Fixed settling interval, not a claim that every background game
            // resource preload has been observed to finish.
            _delay = true; _run = Task.Delay(SettleMs);
        }
        catch (Exception error) { _failure = error; _run = Task.CompletedTask; }
    }

    public override void _Process(double delta)
    {
        if (_finished || _run is not { IsCompleted: true }) return;
        try
        {
            if (_run.IsFaulted) _failure ??= _run.Exception;
            if (_run.IsCanceled) _failure ??= new OperationCanceledException("Measurement task was cancelled.");
            if (_stopRequested) _failure ??= new OperationCanceledException("Explicit test process received a close request.");
            ProductionSearchReplay.FlushHostOnMainThread();
            if (_failure is not null) { Finish(); return; }
            if (_delay) _delay = false;
            else if (_active is not null)
            {
                ValidateAndRecord(_active);
                _active = null;
            }
            if (_queue.Count == 0) { Finish(); return; }
            _active = _queue.Dequeue();
            string output = Path.Combine(_directory, _active.Label);
            if (Directory.Exists(output)) throw new IOException("Measurement output already exists: " + output);
            Environment.SetEnvironmentVariable(ModeVariable, _active.Mode);
            var args = new List<string> { "--context", _contextPath, "--workload", _workloadPath,
                "--output", output, "--repeat", "1" };
            if (_active.Pipeline) args.Add("--map-lossy-pipeline");
            WriteReceipt("running");
            GD.Print("inGameMapMeasurement=Starting;label=" + _active.Label + ";mode=" + _active.Mode);
            _run = RunAsync(args.ToArray());
        }
        catch (Exception error) { _failure ??= error; Finish(); }
    }

    private void ValidateAndRecord(Measurement measurement)
    {
        string output = Path.Combine(_directory, measurement.Label);
        if (measurement.Pipeline)
        {
            string path = Path.Combine(output, "map-lossy-pipeline.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.GetProperty("orderedProposalEquality").GetBoolean())
                throw new InvalidDataException("InGameMapMeasurement.ProposalEqualityNotConfirmed");
            _completed.Add(new { measurement.Label, measurement.Mode, kind = "proposer", output,
                sinceTestStartMs = _elapsed.Elapsed.TotalMilliseconds, evidenceSha256 = Hash(path) });
        }
        else
        {
            string summaryPath = Path.Combine(output, "run0.summary.json");
            using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            var progress = summary.RootElement.GetProperty("progress");
            if (progress.GetProperty("State").GetString() != "Completed")
                throw new InvalidDataException("InGameMapMeasurement.SessionNotCompleted:" + progress.GetProperty("State"));
            if (progress.GetProperty("PendingCandidateCount").GetInt64() != 0 ||
                !string.IsNullOrEmpty(progress.GetProperty("FailureCode").GetString()) ||
                !string.IsNullOrEmpty(progress.GetProperty("CleanupIssue").GetString()))
                throw new InvalidDataException("InGameMapMeasurement.SessionHasPendingOrFailure:" + progress.GetRawText());
            string matches = Path.Combine(output, "run0.matches.jsonl");
            long candidateRecords = File.ReadLines(matches).LongCount(line => !string.IsNullOrWhiteSpace(line));
            if (candidateRecords != progress.GetProperty("MatchCount").GetInt64())
                throw new InvalidDataException("InGameMapMeasurement.CandidateReceiptCountMismatch");
            _completed.Add(new { measurement.Label, measurement.Mode, kind = "full-session", output,
                sinceTestStartMs = _elapsed.Elapsed.TotalMilliseconds, progress = progress.Clone(),
                safeNextOrdinal = summary.RootElement.GetProperty("safeNextOrdinal").Clone(),
                candidateRecords, matchesSha256 = Hash(matches), summarySha256 = Hash(summaryPath) });
        }
        WriteReceipt("measurement-completed");
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _measurementCompleted = _failure is null && !_delay && _active is null && _queue.Count == 0;
        if (_capturedMode) Environment.SetEnvironmentVariable(ModeVariable, _previousMode);
        try { WriteReceipt(_measurementCompleted ? "measurement-completed-awaiting-exit-cleanup" : _stopRequested ? "cancelled" : "failed"); }
        catch (Exception error) { _failure ??= error; GD.PrintErr("InGameMapMeasurement receipt failure: " + error); }
        SetProcess(false);
        var tree = GetTree();
        try
        {
            // Preserve the real game's settings/profile and native font-cache
            // cleanup. This explicit test process uses the isolated user path.
            _exitCleanupAttempted = true;
            (NGame.Instance ?? throw new InvalidOperationException("InGameMapMeasurement.NGameInstanceUnavailableAtExit")).Quit();
            _exitCleanupCompleted = true;
        }
        catch (Exception error)
        {
            _exitCleanupFailure = error;
            GD.PrintErr("InGameMapMeasurement game exit cleanup failed: " + error);
        }
        bool passed = _measurementCompleted && _failure is null && _exitCleanupFailure is null;
        try { WriteReceipt(_exitCleanupFailure is not null ? "exit-cleanup-failed" : passed ? "completed" : _stopRequested ? "cancelled" : "failed"); }
        catch (Exception error) { _failure ??= error; passed = false; GD.PrintErr("InGameMapMeasurement final receipt failure: " + error); }
        if (_failure is not null) GD.PrintErr("InGameMapMeasurement failed: " + _failure);
        GD.Print("inGameMapMeasurement=Finished;passed=" + passed + ";measurementCompleted=" + _measurementCompleted +
            ";exitCleanupCompleted=" + _exitCleanupCompleted + ";directory=" + _directory);
        // Reached only by the node armed by this process's explicit opt-in env.
        // NGame.Quit requests code 0; retain the diagnostic failure exit code.
        tree.Quit(passed ? 0 : 1);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest || _finished) return;
        _stopRequested = true; _session?.Cancel();
        // Wait for Main's session disposal/native drain before quitting. The
        // optional bounded proposer has no cancellation entry; let it finish.
    }
    public override void _ExitTree()
    {
        _session?.Cancel();
        if (_capturedMode) Environment.SetEnvironmentVariable(ModeVariable, _previousMode);
    }

    private void WriteReceipt(string state)
    {
        string path = Path.Combine(_directory, "in-game-measurement-receipt.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new
        {
            state, passed = state == "completed", error = _failure?.ToString(), startedAt = _started,
            measurementCompleted = _measurementCompleted, exitCleanupAttempted = _exitCleanupAttempted,
            exitCleanupCompleted = _exitCleanupCompleted, exitCleanupError = _exitCleanupFailure?.ToString(),
            settleMs = SettleMs, settleMeaning = "Fixed delay after input/identity capture;not an observed preload-completion barrier",
            observedAt = DateTimeOffset.UtcNow, elapsedMs = _elapsed.Elapsed.TotalMilliseconds,
            identity = _identity, active = _active, completed = _completed,
            previousPipelineMode = _previousMode, actualPipelineMode = Environment.GetEnvironmentVariable(ModeVariable),
            inputContextSha256 = File.Exists(_contextPath) ? Hash(_contextPath) : null,
            inputWorkloadSha256 = File.Exists(_workloadPath) ? Hash(_workloadPath) : null,
            scope = "Real-game engine/mod initialization;portable-input Production replay;no real RunState;fixed six fresh sessions in one warm process",
            candidateEvidence = "Full JSONL records retained per session;receipt count/hash only. Cross-mode semantic equality and positive correctness checks remain separate.",
            nativeDiagnostics = "This managed receipt does not certify clean native stderr. Preserve startup task errors and RID/resource leak diagnostics in process logs separately."
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static object AssemblyIdentity(Assembly assembly)
    {
        string path = assembly.Location;
        Guid mvid = assembly.ManifestModule.ModuleVersionId;
        if (string.IsNullOrEmpty(path))
            return new { assembly.FullName, mvid, path, sha256 = (string?)null, fileMatchesLoadedModule = false,
                limitation = "Loaded assembly Location unavailable; MVID retained without inventing a disk hash." };
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        bool matches = metadata.GetGuid(metadata.GetModuleDefinition().Mvid) == mvid;
        if (!matches) throw new InvalidDataException("Loaded module/file MVID mismatch: " + path);
        return new { assembly.FullName, mvid, path, sha256 = Hash(path), fileMatchesLoadedModule = true };
    }
}
