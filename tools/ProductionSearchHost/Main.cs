using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Search.Runtime;
using Godot;
using System.Text.Json;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

public partial class Main : Node
{
    protected Task? _run;
    protected ProductionSearchSession? _session;
    protected Exception? _failure;
    public override void _Ready()
    {
        GetTree().AutoAcceptQuit = false;
        if (OS.GetCmdlineUserArgs().Contains("--card-collection-editor-only")) GetWindow().Unfocusable = true;
        ProductionSearchReplay.InitializeHostOnMainThread();
        _run = RunAsync();
    }
    public override void _Process(double delta)
    {
        if (_run is not { IsCompleted: true }) return;
        ProductionSearchReplay.FlushHostOnMainThread();
        if (_failure is not null) GD.PrintErr(_failure);
        GetTree().Quit(_failure is null ? 0 : 1);
        SetProcess(false);
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) _session?.Cancel();
    }
    protected async Task RunAsync(string[]? requestArgs = null)
    {
        try
        {
            string[] args = requestArgs ?? OS.GetCmdlineUserArgs();
            var mod = typeof(SearchCompiler).Assembly;
            string location = mod.Location;
            string hash = string.IsNullOrEmpty(location) ? "unavailable:byte-loaded" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(location)));
            GD.Print($"Host=ProductionSearchHost;args={string.Join(' ', args.Where(a => a.StartsWith("--", StringComparison.Ordinal)))};mod={(string.IsNullOrEmpty(location) ? "unavailable:byte-loaded" : location)};modMvid={mod.ManifestModule.ModuleVersionId};modSha256={hash}");
            if (args.Contains("--compat-no-fp64") || args.Contains("--compatibility-checks"))
            {
                string[] incompatible = ["--cpu-only", "--cpu-family-workers", "--family-pricing-inventory",
                    "--family-physical-matrix", "--direct-neow-checks", "--private-ordinal-checks", "--y2-probe",
                    "--logging-exact-fault", "--cpu-fault"];
                if (incompatible.Any(args.Contains))
                    throw new ArgumentException("Compatibility validation requires the complete normal Production session branch");
            }
            if(args.Contains("--logging-checks")) { await OperationalLoggingChecks.RunAsync(args.Contains("--logging-io-failure")); return; }
            string Arg(string key, string? fallback = null)
            {
                int i = Array.IndexOf(args, key);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback ?? throw new ArgumentException("Missing " + key);
            }
            if (args.Contains("--canonical-family", StringComparer.Ordinal) && !args.Contains("--neow-canonical-cost", StringComparer.Ordinal))
                throw new ArgumentException("--canonical-family requires --neow-canonical-cost (the dedicated N/T collector)");
            if (args.Contains("--panel-open-performance"))
            {
                await UiSurfaceChecks.RunPanelOpenPerformanceAsync(this, Path.GetFullPath(Arg("--output")),
                    Arg("--game-pck"), Arg("--panel-open-scene", "main-menu"));
                return;
            }
            if (args.Contains("--predictor-surface-checks"))
            {
                await UiSurfaceChecks.RunPredictorAsync(this, Path.GetFullPath(Arg("--output")), Arg("--game-pck"));
                return;
            }
            if (args.Contains("--card-collection-flow-phase"))
            {
                await UiSurfaceChecks.RunCardCollectionFlowAsync(this, Path.GetFullPath(Arg("--output")),
                    Arg("--card-collection-flow-phase"));
                return;
            }
            if (args.Contains("--preset-library-surface-checks"))
            {
                await UiSurfaceChecks.RunPresetLibraryAsync(this, Path.GetFullPath(Arg("--output")), Arg("--game-pck"));
                return;
            }
            if (args.Contains("--compatibility-party-checks") || args.Contains("--party-phase1-checks") || args.Contains("--party-phase1b-checks") || args.Contains("--party-family-checks") ||
                args.Contains("--party-ancient-event-coverage") || args.Contains("--party-bones-pool") || args.Contains("--party-neow-authoring") || args.Contains("--party-cpu-corpus") || args.Contains("--party-kaleidoscope-order") || args.Contains("--run-entry"))
            {
                GD.Print("HostGroup=party;requires=game-pck;replayInputs=false");
                await PartyPhase1Checks.RunAsync(this, Path.GetFullPath(Arg("--output")), Arg("--game-pck"));
                if (args.Contains("--party-phase1b-checks")) await PartyPhase1BChecks.RunAsync(this, Path.GetFullPath(Arg("--output")));
                return;
            }
            if (args.Contains("--startup-checks"))
            {
                GD.Print("HostGroup=startup;bootstrap=native-workspace;generalUiSuite=false");
                await UiSurfaceChecks.InitializeNativeWorkspaceAsync(this, Path.GetFullPath(Arg("--output")));
                await StartupSurfaceChecks.RunAsync(this, Path.GetFullPath(Arg("--output")), Arg("--resource-baseline", ""));
                return;
            }
            if (Array.IndexOf(args, "--ui-surface-checks") >= 0)
            {
                GD.Print("HostGroup=ui-surface");
                if (args.Contains("--gpu-initialization"))
                {
                    await UiSurfaceChecks.RunGpuInitializationAsync(this, Path.GetFullPath(Arg("--output")), Arg("--game-pck"));
                    return;
                }
                await UiSurfaceChecks.RunAsync(this, System.IO.Path.GetFullPath(Arg("--output")));
                return;
            }
            GD.Print("HostGroup=production-replay;requires=context,workload");
            var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Arg("--context"));
            var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Arg("--workload"));
            if (Array.IndexOf(args, "--draft-snapshot") >= 0)
            {
                // Import an actual saved UI draft through the current UI builder;
                // never maintain a second draft-to-query mapping in this host.
                var type = typeof(SearchCompiler).Assembly.GetType("RolltheSpire2.Ui.Workspace.WorkbenchQueryCompiler", true)!;
                var method = type.GetMethod("BuildSearchQuery", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    ?? throw new InvalidOperationException("UiQueryBuilderMissing");
                var draft = JsonSerializer.Deserialize(File.ReadAllText(Arg("--draft-snapshot")), method.GetParameters()[0].ParameterType, ProductionSearchReplay.JsonOptions)
                    ?? throw new InvalidDataException("UiDraftMissing");
                object?[] values = [draft, null, null];
                method.Invoke(null, values);
                if (JsonSerializer.Serialize(values[2], ProductionSearchReplay.JsonOptions) != JsonSerializer.Serialize(context.Context.EvaluationAssumptions.AncientEligibilityAssumptions, ProductionSearchReplay.JsonOptions))
                    throw new InvalidDataException("UiDraftContextAssumptionsMismatch");
                workload = workload with { Query = (SearchQuery)values[1]! };
            }
            if (context.SchemaVersion != 1 || workload.SchemaVersion != 1) throw new InvalidDataException("ReplaySchemaMismatch");
            string output = System.IO.Path.GetFullPath(Arg("--output"));
            Directory.CreateDirectory(output);
            int warmup = int.Parse(Arg("--warmup", "0")), repeat = int.Parse(Arg("--repeat", "1"));
            int cancelAfterMs = int.Parse(Arg("--cancel-after-ms", "0"));
            if (cancelAfterMs < 0) throw new ArgumentException("Invalid cancellation delay");
            if (warmup < 0 || repeat < 1) throw new ArgumentException("Invalid repeat/warmup");
            if (!args.Contains("--cpu-only") && RenderingServer.GetRenderingDevice() is not null)
            using (var device = RenderingServer.CreateLocalRenderingDevice()
                ?? throw new InvalidOperationException("GPU device unavailable: use a rendering-enabled Godot host, not --headless."))
                Console.WriteLine($"standaloneProduction=true;gpu={device.GetDeviceName()};provenance={context.Provenance};gameProcessRequired=false");
            ProductionSearchReplay.Write(System.IO.Path.Combine(output, "context.json"), context);
            ProductionSearchReplay.Write(System.IO.Path.Combine(output, "workload.json"), workload);
            if (args.Contains("--initial-room-gpu-checks"))
            {
                var initial = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context.Context), workload.Options);
                await InitialRoomGpuChecks.RunAsync(initial.Plan ?? throw new InvalidDataException("InitialRoomsHost.Compile:" + initial.Issue), output);
                await CompatibilityChecks.CheckInitialRoomsSessionsAsync(output, session => _session = session);
                _session = null;
                return;
            }
            if (args.Contains("--map-lossy-gpu"))
            {
                var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context.Context), workload.Options);
                await MapLossyChecks.RunAsync(compiled.Plan ?? throw new InvalidDataException(compiled.Issue), output);
                return;
            }
            if (args.Contains("--compatibility-checks"))
            {
                await CompatibilityChecks.RunAsync(context, workload, output, session => _session = session);
                _session = null;
                return;
            }
            if (args.Contains("--compat-no-fp64")) CompatibilityChecks.ExportProductionNeowSources(output);
            for (int run = -warmup; run < repeat; run++)
            {
                var preparationWatch = System.Diagnostics.Stopwatch.StartNew();
                var compiled = SearchCompiler.Compile(workload.Query, context.Context);
                var result = ExactSearchExecutionRequestFactory.Compile(compiled, workload.Options);
                if (!result.Success || result.Plan is null) throw new InvalidOperationException("ProductionCompile:" + result.Issue);
                double compilePreparationMs = preparationWatch.Elapsed.TotalMilliseconds;
                var rewardPoolPreparationBefore = result.Plan.RewardPoolPreparation?.Capture();
                if (args.Contains("--neow-canonical-cost", StringComparer.Ordinal))
                {
                    string canonicalFamily = Arg("--canonical-family", "N");
                    if (canonicalFamily is not ("N" or "T")) throw new ArgumentException("FamilyCanonical.InvalidFamily;expected=N|T");
                    await RunMeasurementCollectorAsync([..args, "--measurement-live-main-loop"], output,
                        canonicalFamily == "N" ? "NeowCanonicalPublicInvoke" : "FamilyCanonicalPublicInvoke.T",
                        () => FamilyPhysicalMatrix.MeasureNeowCanonical(result.Plan, args, output));
                    return;
                }
                if (Array.IndexOf(args, "--family-pricing-inventory") >= 0)
                {
                    FamilyPricingInventory.Run(result.Plan, output);
                    return;
                }
                if (Array.IndexOf(args, "--family-physical-matrix") >= 0)
                {
                    FamilyPhysicalMatrix.Run(result.Plan, Arg("--family-physical-matrix"), output);
                    return;
                }
                if (Array.IndexOf(args, "--direct-neow-checks") >= 0)
                {
                    DirectNeowChecks.Run(result.Plan, output);
                    return;
                }
                if (Array.IndexOf(args, "--y2-probe") >= 0)
                {
                    Y2RootCostProbe.Run(result.Plan, Arg("--y2-probe"), output);
                    return;
                }
                if (Array.IndexOf(args, "--private-ordinal-checks") >= 0)
                {
                    PrivateOrdinalChecks.Run(result.Plan);
                    return;
                }
                if (Array.IndexOf(args, "--small-candidate-exact-checks") >= 0)
                {
                    SmallCandidateExactGpuChecks.Run(result.Plan);
                    await SmallCandidateExactProductionGpuChecks.RunAsync(result.Plan);
                    return;
                }
                string label = run < 0 ? "warmup" + (-run) : "run" + run;
                Console.WriteLine($"standaloneRun={label};semanticFingerprint={compiled.SemanticFingerprint}");
                RolltheSpire2.Search.Runtime.ShionExactTrialMetrics.Reset();
                long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                var observationWatch = System.Diagnostics.Stopwatch.StartNew();
                double? firstCandidateMs = null, cancellationRequestedMs = null;
                var completedScanSamples = new List<object> { new { scanned = 0L, elapsedMs = 0d, afterCompletion = false } };
                long lastSampledScanned = 0;
                double nextScanObservationMs = 0;
                await using var session = args.Contains("--cpu-family-workers")
                    ? FamilyPhysicalMatrix.StartCpuWorkerSession(result.Plan, Arg("--cpu-family-workers"), args.Contains("--cpu-only"))
                    : (args.Contains("--logging-exact-fault") || args.Contains("--cpu-fault")) ? OperationalLoggingChecks.CreateExactFaultSession(result.Plan,args.Contains("--cpu-fault")) : FamilyExecutionCoordinator.Start(result.Plan, failOnPhysicalRecovery: true, gpuAvailable: args.Contains("--cpu-only") ? false : null,
                        gpuFloat64Available: args.Contains("--compat-no-fp64") ? false : null);
                _session = session;
                double sessionConstructionMs = observationWatch.Elapsed.TotalMilliseconds;
                var cancelWatch = System.Diagnostics.Stopwatch.StartNew();
                bool cancelIssued = false;
                using var matches = new StreamWriter(System.IO.Path.Combine(output, label + ".matches.jsonl"));
                while (!session.Completion.IsCompleted)
                {
                    if (observationWatch.Elapsed.TotalMilliseconds >= nextScanObservationMs)
                    {
                        ObserveCompletedScan(afterCompletion: false);
                        nextScanObservationMs = observationWatch.Elapsed.TotalMilliseconds + 25;
                    }
                    if (cancelAfterMs > 0 && !cancelIssued && cancelWatch.ElapsedMilliseconds >= cancelAfterMs)
                    { cancellationRequestedMs = observationWatch.Elapsed.TotalMilliseconds; session.Cancel(); cancelIssued = true; }
                    Drain();
                    // Drain on the thread pool; a Godot frame must not throttle
                    // the bounded Production candidate channel.
                    await Task.WhenAny(session.Completion, Task.Delay(1)).ConfigureAwait(false);
                }
                await session.Completion.ConfigureAwait(false);
                ObserveCompletedScan(afterCompletion: true);
                Drain();
                double completionAndDrainMs = observationWatch.Elapsed.TotalMilliseconds;
                var progress = session.GetProgress();
                CompatibilityChecks.WriteInvocationReceipt(session, output, label, args.Contains("--compat-no-fp64"));
                CompatibilityChecks.WriteEvidence(System.IO.Path.Combine(output, label + ".continuation.json"), session.CaptureContinuation());
                File.WriteAllText(System.IO.Path.Combine(output, label + ".summary.json"), JsonSerializer.Serialize(new
                { progress, families = session.FamilyIds, safeNextOrdinal = session.GetSafeNextOrdinal(), diagnostics = session.GetDiagnosticSummary(),
                    exactTrialMetrics = RolltheSpire2.Search.Runtime.ShionExactTrialMetrics.Snapshot(),
                    hostObservations = new { compilePreparationMs, sessionConstructionMs, completionAndDrainMs,
                        requestedGpuOptimizations = System.Environment.GetEnvironmentVariable("RT2_SHION_GPU_TRIAL") ?? "production-default",
                        requestedExactOptimizations = System.Environment.GetEnvironmentVariable("RT2_SHION_EXACT_TRIAL") ?? "production-default",
                        requestedCpuPrefilter = System.Environment.GetEnvironmentVariable("RT2_CPU_FOUR_WAY_PREFILTER") ?? "production-default",
                        requestedPreparedPools = System.Environment.GetEnvironmentVariable("RT2_C_PREPARED_REWARD_POOLS") ?? "production-default",
                        rewardPoolPreparationBefore, rewardPoolPreparationAfter = session.CaptureRewardPoolPreparationEvidence(),
                        firstCandidateMs, cancellationRequestedMs,
                        cancellationCompletionMs = cancellationRequestedMs.HasValue ? observationWatch.Elapsed.TotalMilliseconds - cancellationRequestedMs.Value : (double?)null,
                        managedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore,
                        processPeakWorkingSetBytes = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64,
                        completedScanSamples, scanObservationCadenceMs = 25,
                        boundary = "SessionConstructionToDrain;ProcessPeakIncludesEarlierHostWork;AllocationIncludesAllManagedThreads" }
                }, ProductionSearchReplay.JsonOptions));
                Console.WriteLine($"standaloneCompleted={label};state={progress.State};scanned={progress.ScannedCount};matches={progress.MatchCount};cps={progress.SeedsPerSecond}");
                if(args.Contains("--logging-exact-fault") || args.Contains("--cpu-fault"))
                { if(progress.State!=SearchRunState.Faulted || !progress.FailureCode.Contains(args.Contains("--cpu-fault") ? "CpuSelectedFaultFixture" : "LoggingExactFaultFixture")) throw new InvalidOperationException("Expected logging fault fixture missing"); }
                else if (progress.State == SearchRunState.Faulted) throw new InvalidOperationException(progress.FailureCode);
                _session = null;
                void ObserveCompletedScan(bool afterCompletion)
                {
                    long scanned = session.GetProgress().ScannedCount;
                    if (scanned <= lastSampledScanned) return;
                    completedScanSamples.Add(new { scanned, elapsedMs = observationWatch.Elapsed.TotalMilliseconds, afterCompletion });
                    lastSampledScanned = scanned;
                }
                void Drain()
                {
                    var received = new List<string>();
                    while (session.TryReadCandidate(out var candidate))
                        if (candidate is not null)
                        {
                            firstCandidateMs ??= observationWatch.Elapsed.TotalMilliseconds;
                            matches.WriteLine(JsonSerializer.Serialize(new
                        { candidate.Seed, candidate.MatchEvidence, candidate.MatchedRouteIds, candidate.Witnesses,
                            candidate.ProfileId, candidate.CharacterKey, candidate.Ascension, candidate.SnapshotFingerprint,
                            candidate.ValidationDisposition, candidate.ValidationIssue, candidate.ExecutionWarnings,
                            candidate.SupportScope, candidate.IsUnverified, candidate.IsUnfiltered },
                            new JsonSerializerOptions(ProductionSearchReplay.JsonOptions) { WriteIndented = false }));
                            received.Add(candidate.Seed);
                        }
                    if (received.Count == 0) return;
                    CompatibilityChecks.FlushReceipt(matches);
                    foreach (string seed in received) session.AcknowledgeCandidate(seed);
                }
            }
        }
        catch (Exception ex) { _failure = ex; }
    }
}
