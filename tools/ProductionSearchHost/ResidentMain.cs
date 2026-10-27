using System.Diagnostics;
using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Godot;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Infrastructure.Snapshots;
using Mutex = System.Threading.Mutex;
using Environment = System.Environment;

// An opt-in warm-process replay queue. Native UI/check groups still use their
// isolated hosts: many read OS arguments or change global game state.
public partial class ResidentMain : Main
{
    private const string PerformanceLock = @"Local\RT2_PerformanceBench_20261012";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private string _root = "", _sha = "", _activeFile = "";
    private Mutex? _singleton, _performance;
    private bool _ownsSingleton, _ownsPerformance, _stopping, _quarantined, _cancelled, _timedOut;
    private Job? _job;
    private Label _label = null!;
    private double _poll;
    private int _completed;
    private readonly Stopwatch _elapsed = new();
    private DateTimeOffset _started;

    public sealed record Job(string Id, string Owner, string ExpectedModSha256, string Context, string Workload,
        int Warmup = 0, int Repeat = 1, int TimeoutSeconds = 300, bool CpuOnly = false);

    public override void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int i = Array.IndexOf(args, "--resident-root");
            if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("Missing --resident-root");
            _root = Path.GetFullPath(args[i + 1]);
            _singleton = new Mutex(false, @"Local\RT2_ExperimentDesk");
            try { _ownsSingleton = _singleton.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsSingleton = true; }
            if (!_ownsSingleton) throw new InvalidOperationException("An experiment desk is already running.");
            foreach (string dir in new[] { "queue", "active", "results", "runs" }) Directory.CreateDirectory(Path.Combine(_root, dir));
            _performance = new Mutex(false, PerformanceLock);
            string assembly = typeof(SearchCompiler).Assembly.Location;
            // Godot loads assemblies from bytes. Bind the immutable prepared
            // file to the actually loaded module instead of inventing Location.
            if (string.IsNullOrEmpty(assembly))
                assembly = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/RolltheSpire2.dll");
            using (var file = File.OpenRead(assembly))
            using (var pe = new PEReader(file))
            {
                var metadata = pe.GetMetadataReader();
                if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != typeof(SearchCompiler).Assembly.ManifestModule.ModuleVersionId)
                    throw new InvalidDataException("Prepared candidate does not match loaded module");
            }
            _sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly)));
            // Never replay a job interrupted by an earlier process implicitly.
            foreach (string file in Directory.GetFiles(Path.Combine(_root, "active"), "*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                Write(Path.Combine(_root, "results", id + ".json"), new { id, state = "interrupted", reason = "Previous host exited before completion; resubmit explicitly with a new id." });
                File.Move(file, Path.Combine(_root, "results", id + ".request.json"), true);
            }
            GetTree().AutoAcceptQuit = false;
            GetWindow().Title = "RT2 Experiment Desk — warm replay";
            GetWindow().Size = new Vector2I(720, 250);
            GetWindow().Unfocusable = true;
            Engine.MaxFps = 10;
            var box = new VBoxContainer { Position = new Vector2(18, 18), Size = new Vector2(684, 210) };
            AddChild(box);
            _label = new Label(); box.AddChild(_label);
            var pause = new Button { Text = "Pause / resume queue" };
            pause.Pressed += () => { string path = Path.Combine(_root, "pause"); if (File.Exists(path)) File.Delete(path); else File.WriteAllText(path, "paused"); };
            box.AddChild(pause);
            var cancel = new Button { Text = "Cancel current replay" }; cancel.Pressed += Cancel; box.AddChild(cancel);
            var stop = new Button { Text = "Stop after cancelling current replay" }; stop.Pressed += Stop; box.AddChild(stop);
            ProductionSearchReplay.InitializeHostOnMainThread();
            Publish("idle");
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); SetProcess(false); }
    }

    public override void _Process(double delta)
    {
        // All Mutex acquire/release calls stay on this Godot thread; async replay
        // continuations must never release this thread-affine resource lock.
        try
        {
            if (_run is { IsCompleted: true }) Finish();
            if (_job is not null && _elapsed.Elapsed.TotalSeconds > _job.TimeoutSeconds && !_timedOut)
            { _timedOut = true; Cancel(); }
            if (_cancelled) _session?.Cancel(); // Covers cancellation during initial compilation.
            if (_stopping && _job is null) { Publish("stopped"); GetTree().Quit(); SetProcess(false); return; }
            _poll += delta;
            if (_poll < .5) return;
            _poll = 0;
            if (File.Exists(Path.Combine(_root, "stop"))) { File.Delete(Path.Combine(_root, "stop")); Stop(); }
            string cancelPath = Path.Combine(_root, "cancel");
            if (File.Exists(cancelPath))
            {
                string target = File.ReadAllText(cancelPath).Trim(); File.Delete(cancelPath);
                if (_job?.Id == target) Cancel();
            }
            if (_job is not null) { Publish(_timedOut ? "timeout-cancelling" : _cancelled ? "cancelling" : "running"); return; }
            if (_quarantined) { Publish("faulted-restart-required"); return; }
            if (_stopping) return;
            if (File.Exists(Path.Combine(_root, "pause"))) { Publish("paused"); return; }
            var games = Process.GetProcessesByName("SlayTheSpire2");
            bool gameRunning = games.Length != 0;
            foreach (var game in games) game.Dispose();
            if (gameRunning) { Publish("waiting-game-exit"); return; }
            string? next = Directory.GetFiles(Path.Combine(_root, "queue"), "*.json").Order(StringComparer.Ordinal).FirstOrDefault();
            if (next is null) { Publish("idle"); return; }
            try { _ownsPerformance = _performance!.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsPerformance = true; GD.Print("Previous performance lock owner exited; lock acquired."); }
            if (!_ownsPerformance) { Publish("waiting-performance-lock"); return; }
            StartJob(next);
        }
        catch (Exception ex)
        {
            GD.PrintErr(ex);
            // No safe next job after a bookkeeping/cleanup failure. Retain active
            // input for recovery. An ongoing replay is cancelled before exit.
            _quarantined = true; Stop();
            if (_run is null || _run.IsCompleted)
            { ReleasePerformance(); GetTree().Quit(1); SetProcess(false); }
        }
    }

    private void StartJob(string next)
    {
        string id = Path.GetFileNameWithoutExtension(next);
        try
        {
            var job = JsonSerializer.Deserialize<Job>(File.ReadAllText(next), Json) ?? throw new InvalidDataException("Empty job");
            if (job.Id != id || id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || string.IsNullOrWhiteSpace(job.Owner))
                throw new InvalidDataException("Invalid job identity/owner");
            if (!string.Equals(job.ExpectedModSha256, _sha, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("CandidateIdentityMismatch: restart with the intended prepared candidate");
            if (!Path.IsPathFullyQualified(job.Context) || !Path.IsPathFullyQualified(job.Workload) || !File.Exists(job.Context) || !File.Exists(job.Workload))
                throw new InvalidDataException("Missing absolute context/workload input");
            if (job.Warmup < 0 || job.Warmup > 100 || job.Repeat < 1 || job.Repeat > 1000 || job.TimeoutSeconds < 1 || job.TimeoutSeconds > 86400)
                throw new InvalidDataException("Invalid run limits");
            string output = Path.Combine(_root, "runs", id);
            if (Directory.Exists(output)) throw new InvalidDataException("Output already exists; use a new job id");
            Directory.CreateDirectory(output);
            // Freeze submitted input bytes before executing. Clients publish the
            // request last, after writing their immutable input files.
            string context = Path.Combine(output, "submitted-context.json"), workload = Path.Combine(output, "submitted-workload.json");
            File.Copy(job.Context, context); File.Copy(job.Workload, workload);
            _activeFile = Path.Combine(_root, "active", id + ".json");
            File.Move(next, _activeFile);
            _job = job; _failure = null; _cancelled = _timedOut = false;
            _started = DateTimeOffset.UtcNow; _elapsed.Restart();
            Write(Path.Combine(output, "host.json"), new { pid = Environment.ProcessId, modSha256 = _sha,
                hostMvid = typeof(ResidentMain).Assembly.ManifestModule.ModuleVersionId,
                mode = "warm-process-fresh-production-session", precedingJobs = _completed, startedAt = _started,
                note = "Session/device setup remains included. Not cold-process or guaranteed cached-GPU timing." });
            var args = new List<string> { "--context", context, "--workload", workload, "--output", output,
                "--warmup", job.Warmup.ToString(), "--repeat", job.Repeat.ToString() };
            if (job.CpuOnly) args.Add("--cpu-only");
            Publish("running");
            GD.Print($"residentJobStarted={id};owner={job.Owner};output={output}");
            _run = RunAsync(args.ToArray());
        }
        catch (Exception ex) when (_job is null)
        {
            Write(Path.Combine(_root, "results", id + ".json"), new { id, state = "rejected", error = ex.ToString() });
            if (File.Exists(next)) File.Move(next, Path.Combine(_root, "results", id + ".request.json"), true);
            ReleasePerformance();
        }
    }

    private void Finish()
    {
        ProductionSearchReplay.FlushHostOnMainThread();
        _elapsed.Stop();
        string id = _job!.Id;
        string state = _failure is not null || _run!.IsFaulted ? "failed" : _timedOut ? "timed-out" : _cancelled ? "cancelled" : "completed";
        Write(Path.Combine(_root, "results", id + ".json"), new { id, owner = _job.Owner, state,
            error = (_failure ?? _run!.Exception)?.ToString(), pid = Environment.ProcessId, modSha256 = _sha,
            startedAt = _started, finishedAt = DateTimeOffset.UtcNow, elapsedSeconds = _elapsed.Elapsed.TotalSeconds,
            output = Path.Combine(_root, "runs", id) });
        File.Move(_activeFile, Path.Combine(_root, "results", id + ".request.json"));
        GD.Print($"residentJobFinished={id};state={state}");
        _quarantined = state is "failed" or "timed-out"; // Do not assume global state clean after a fault.
        _run = null; _session = null; _job = null; _completed++;
        ReleasePerformance();
    }

    private void Cancel() { if (_job is not null) { _cancelled = true; _session?.Cancel(); } }
    private void Stop() { _stopping = true; Cancel(); }
    public override void _Notification(int what) { if (what == NotificationWMCloseRequest) Stop(); }
    private void ReleasePerformance() { if (_ownsPerformance) { _performance!.ReleaseMutex(); _ownsPerformance = false; } }
    public override void _ExitTree()
    {
        ReleasePerformance(); _performance?.Dispose();
        if (_ownsSingleton) _singleton!.ReleaseMutex();
        _singleton?.Dispose();
    }
    private void Publish(string state)
    {
        int queued = Directory.GetFiles(Path.Combine(_root, "queue"), "*.json").Length;
        _label.Text = $"{state} | queued: {queued} | completed: {_completed}\n{_job?.Owner ?? "No active owner"}\n{_job?.Id ?? "Ready for replay jobs"}\nCandidate: {_sha[..12]} | PID {Environment.ProcessId}";
        Write(Path.Combine(_root, "status.json"), new { state, pid = Environment.ProcessId, heartbeatUtc = DateTimeOffset.UtcNow,
            modSha256 = _sha, activeJob = _job?.Id, owner = _job?.Owner, queued, completed = _completed, performanceMutex = PerformanceLock });
    }
    private static void Write(string path, object value)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json)); File.Move(tmp, path, true);
    }
}
