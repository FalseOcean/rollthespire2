using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

// Bounded correctness branch of the existing Production host. All sessions use
// real registered invocations and Production Exact, including the cancellation
// probe. No device profile substitution, outcome injection or timing claim.
internal static partial class CompatibilityChecks
{
    private static readonly JsonSerializerOptions Compact = new(ProductionSearchReplay.JsonOptions) { WriteIndented = false };

    // Diagnostic receipts are not imported replay DTOs. Do not send anonymous
    // evidence through ProductionSearchReplay.Write's strict DTO roundtrip.
    internal static void WriteEvidence(string path, object? value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, ProductionSearchReplay.JsonOptions));

    internal static void FlushReceipt(StreamWriter writer)
    {
        writer.Flush();
        if (writer.BaseStream is FileStream file) file.Flush(flushToDisk: true);
    }

    internal sealed record Invocation(string FamilyId, string Type, string[] Coverage, bool UsesGpu,
        string PhysicalRevision, string NumericRequirements, int Batches, long Inputs, long Outputs,
        bool Faulted, Invocation[] Children);

    private sealed record Receipt(string Label, string RequestSnapshotFingerprint, string ContinuationFingerprint,
        string[] SemanticRecords, string[] WarningRecords,
        SearchProgressSnapshot Progress, ulong SafeNextOrdinal, SearchContinuationSnapshot Continuation,
        Invocation[] Invocations);

    private static Invocation Describe(IFamilyInvocation family)
    {
        var observation = family.CapturePerformanceObservation();
        IEnumerable<IFamilyInvocation> children = family switch
        {
            AutomaticPrivateSerialInvocation serial => serial.PhysicalFamilies,
            PartyFamily party => party.PersonalStages.Select(stage => stage.Family),
            InitialRoomsInvocation initial => initial.Ordinary is { } ordinary ? [ordinary] : [],
            _ => []
        };
        return new(family.FamilyId, family.GetType().FullName!, family.Coverage.ToArray(),
            family.ConditionPerformance.UsesGpu, observation.Condition.PhysicalImplementationRevision,
            family.GpuNumericRequirements.ToString(), observation.PhysicalBatchCount,
            observation.InputCandidates, observation.OutputCandidates, family.ExecutionFaulted,
            children.Select(Describe).ToArray());
    }

    private static IEnumerable<Invocation> Flatten(IEnumerable<Invocation> rows) =>
        rows.SelectMany(row => new[] { row }.Concat(Flatten(row.Children)));

    internal static Invocation[] WriteInvocationReceipt(ProductionSearchSession session, string output,
        string label, bool compatNoFp64, FamilyGpuInitializationSnapshot? ownerInitialization = null)
    {
        var plan = session.ExecutionPlan;
        var rows = plan.OrderedFamilies.Select(Describe).ToArray();
        var actualDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        WriteEvidence(Path.Combine(output, label + ".invocations.json"), new
        {
            ForceNoFp64 = compatNoFp64, ActualDevice = actualDevice,
            DeviceInitialization = ownerInitialization ?? FamilyDeviceProfileFoundation.Initialization.Capture(),
            plan.PlanId, RankingAuthority = plan.RankingAuthority.ToString(), plan.DecisionEvidence, plan.MissingEvidence,
            plan.FilterRemainders, plan.FinalValidationSkipped, plan.ExpectedFamilyPipelineMsPerRoot,
            // Already selected public units, including those later bound into
            // private transport. This receipt performs no quoting or execution.
            PublicUnitReferences = FamilySearchEtaProjectorV1.EstimationStages(plan).Select(stage => new {
                stage.Family.FamilyId, stage.Coverage, stage.CompactAbi1Input,
                ExpectedInputFraction = double.IsFinite(stage.ExpectedInputFraction) ? (double?)stage.ExpectedInputFraction : null,
                Survival = stage.Survival is { } survival && double.IsFinite(survival) ? (double?)survival : null,
                PerInputTimeMs = stage.PerInputTimeMs is { } time && double.IsFinite(time) ? (double?)time : null,
                PhysicalRevision = stage.Performance.PhysicalImplementationRevision,
                PerformanceEvidence = stage.Performance.Evidence,
                Scope = "SelectedPublicUnitReference;NotPrivateTransportMeasurement",
                NonFiniteValuesReportedAsNull = true
            }).ToArray(),
            Invocations = rows, RequirementEvidence = "Declared requirements of actual selected bodies; assembled-source and SPIR-V hashes recorded separately by CompileShader; no bytecode capability inspection",
            TargetMetalExecutionClaimed = false, ThroughputClaimed = false
        });
        if (compatNoFp64 && Flatten(rows).Any(row => row.UsesGpu &&
            (row.NumericRequirements.Contains("Float64", StringComparison.Ordinal) ||
             row.NumericRequirements.Contains("Unspecified", StringComparison.Ordinal))))
            throw new InvalidDataException("CompatibilityHost.SelectedGpuRequiresFloat64OrUnspecified");
        return rows;
    }

    private static string SemanticRecord(SearchCandidate candidate) => JsonSerializer.Serialize(new
    {
        candidate.Seed, candidate.MatchEvidence, candidate.MatchedRouteIds, candidate.Witnesses,
        candidate.ProfileId, candidate.CharacterKey, candidate.Ascension,
        candidate.Authority.EffectSnapshotFingerprint, candidate.Authority.WorldSnapshotFingerprint,
        candidate.ValidationDisposition, candidate.ValidationIssue, candidate.SupportScope,
        candidate.IsUnverified, candidate.IsUnfiltered
    }, Compact);

    private static string FullRecord(SearchCandidate candidate) => JsonSerializer.Serialize(new
    {
        candidate.Seed, candidate.MatchEvidence, candidate.MatchedRouteIds, candidate.Witnesses,
        candidate.ProfileId, candidate.CharacterKey, candidate.Ascension, candidate.SnapshotFingerprint,
        candidate.ValidationDisposition, candidate.ValidationIssue, candidate.ExecutionWarnings,
        candidate.SupportScope, candidate.IsUnverified, candidate.IsUnfiltered
    }, Compact);

    private static string Digest(IEnumerable<string> records) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('\n', records.Order(StringComparer.Ordinal)))));

    internal static void ExportProductionNeowSources(string output)
    {
        string directory = Path.Combine(output, "production-neow-sources");
        Directory.CreateDirectory(directory);
        var rows = new List<object>();
        foreach (bool leafy in new[] { false, true })
        foreach (int stage in new[] { 0, 1, 2, 3 })
        {
            string name = (leafy ? "leafy-" : "") + (stage == 3 ? "shader" : "stage" + stage);
            string native = NeowFamilyGpuExecutor.ShaderSource(stage, leafyPreGate: leafy);
            string candidate = NeowFamilyGpuExecutor.ShaderSource(stage, leafyPreGate: leafy,
                numericalVariant: NeowGpuNumericalVariant.IntegerUnitFloat);
            File.WriteAllText(Path.Combine(directory, name + ".native.glsl"), native);
            File.WriteAllText(Path.Combine(directory, name + ".compat.glsl"), candidate);
            rows.Add(new { Name = name, NativeSha256 = Digest([native]), CompatibilitySha256 = Digest([candidate]),
                Factory = "NeowFamilyGpuExecutor.ShaderSource", NumericVariant = "IntegerUnitFloat", Int64Required = true });
        }
        WriteEvidence(Path.Combine(directory, "manifest.json"), new {
            CanonicalProductionFactoryExports = rows, Modules = 8,
            Scope = "Canonical ordinary solo/leafy defaults; runtime direct-mode/configuration sources may differ and retain their actual CompileShader hashes",
            ShaderCompilationClaimed = false, GpuExecutionClaimed = false,
            NextInt = "Inherited HistoricalShortcut unchanged", TargetMetalExecutionClaimed = false });
    }

    private static ExactSearchExecutionRequest Compile(ProductionSearchReplayWorkload workload, SearchContext context)
    {
        var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context), workload.Options);
        return compiled.Plan ?? throw new InvalidDataException("CompatibilityHost.Compile:" + compiled.Issue);
    }

    internal static async Task RunAsync(ProductionSearchReplayContext captured, ProductionSearchReplayWorkload source,
        string output, Action<ProductionSearchSession?> observeSession)
    {
        if (captured.Context.Party is not null || source.Options.SkipExactValidation)
            throw new ArgumentException("Compatibility checks require a solo captured context with Production Exact");
        ExportProductionNeowSources(output);
        if (FamilyDeviceProfileFoundation.NumericCapabilityCapture is { } capture) await capture;
        WriteEvidence(Path.Combine(output, "numeric-capability-capture.json"),
            new { Initialization = FamilyDeviceProfileFoundation.Initialization.Capture(),
                Vulkan = FamilyDeviceProfileFoundation.Initialization.Capture().VulkanCapabilities });
        await CheckGpuNumericsAsync(output);
        var bounded = source with { Options = source.Options with {
            ScanCount = Math.Min(source.Options.ScanCount, 4099), TargetMatchCount = int.MaxValue } };
        var initial = Compile(bounded, captured.Context);
        await CheckExtendedFamiliesAsync(initial, output);
        var root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, initial.CanonicalStartSeed);
        Require(ProductionExactSearchEvaluator.Evaluate(initial, root).IsMatch, "InputStartMustBeActualWholeRequestPositive");
        var shops = Beta111NormalMerchantColorlessSequenceProjector.Project(root.RootHash,
            Beta111MerchantColorlessAuthority.From(captured.Context.Authority), 1);
        var target = shops.Merchants.Single().UncommonColorless;
        var withShop = bounded with { Query = bounded.Query with { MerchantColorlessConditions =
            [..bounded.Query.MerchantColorlessConditions, new(1, MerchantColorlessSlot.Uncommon, target)] } };
        var wrong = Beta111MerchantColorlessAuthority.From(captured.Context.Authority).UncommonPool.First(key => key != target);
        var reject = withShop with { Query = withShop.Query with { MerchantColorlessConditions =
            [..bounded.Query.MerchantColorlessConditions, new(1, MerchantColorlessSlot.Uncommon, wrong)] },
            Options = withShop.Options with { ScanCount = 1 } };
        ProductionSearchReplay.Write(Path.Combine(output, "authored-all-families-positive.json"), withShop);
        ProductionSearchReplay.Write(Path.Combine(output, "authored-shop-rejection.json"), reject);
        WriteEvidence(Path.Combine(output, "authoring.json"), new {
            Source = "Caller workload and captured immutable context; no Mod content substitutions",
            OriginalOptions = source.Options, BoundedOptions = bounded.Options,
            AddedCondition = "First normal merchant uncommon colorless identity from Core projection at preserved start seed",
            PositiveTarget = target, RejectionTarget = wrong, ExistingConditionsRemoved = false,
            CapturedContextSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "context.json")))),
            SourceWorkloadSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "workload.json")))),
            OriginalQuerySha256 = Digest([JsonSerializer.Serialize(source.Query, Compact)]) });

        var receipts = new List<object>();
        foreach (var item in new[] { (Name: "original-positive", Workload: bounded, Positive: true),
            (Name: "all-families-positive", Workload: withShop, Positive: true),
            (Name: "shop-rejection", Workload: reject, Positive: false) })
        {
            var request = Compile(item.Workload, captured.Context);
            Require(ProductionExactSearchEvaluator.Evaluate(request, root).Evaluation.Disposition ==
                (item.Positive ? SearchDisposition.Match : SearchDisposition.NoMatch),
                "WholeRequestFixedRootTruth:" + item.Name);
            var cpu = await RunSession(request, output, item.Name + "-cpu", true, false, null, observeSession);
            var compatibility = await RunSession(request, output, item.Name + "-compat", false, false, null, observeSession);
            Compare(cpu, compatibility, item.Name);
            Require(item.Positive ? compatibility.Progress.MatchCount > 0 : compatibility.SemanticRecords.Length == 0 &&
                compatibility.Progress.MatchCount == 0 && compatibility.Progress.UnverifiedCandidateCount == 0,
                "ActualCompleteSessionTruth:" + item.Name);
            if (item.Name == "all-families-positive")
            {
                var explicitlyExecuted = await CheckPublicFamilyParityAsync(request, Path.Combine(output, "all-family-public-parity"));
                var actual = Flatten(compatibility.Invocations).ToArray();
                foreach (string family in new[] { "N.Neow", "R.Relic", "C.CombatReward", "W.World", "A.AncientOption", "E.EventResult", "S.MerchantShopColorless" })
                    Require(actual.Any(row => row.Coverage.Contains(family) && row.UsesGpu) && explicitlyExecuted.Contains(family),
                        "SelectedCompatibilityBodyAndActualIndependentGpuParity:" + family);
                // Production may hand a small population to original-query Exact
                // before every selected tail runs. Independent public calls above
                // still require actual native/integer dispatch for all seven.
                WriteEvidence(Path.Combine(output, "compatibility-family-participation.json"), new {
                    SelectedGpuCoverage = actual.Where(row => row.UsesGpu).SelectMany(row => row.Coverage).Distinct().ToArray(),
                    ProductionExecutedGpuCoverage = actual.Where(row => row.UsesGpu && row.Batches > 0 && row.Inputs > 0)
                        .SelectMany(row => row.Coverage).Distinct().ToArray(),
                    IndependentDenseGpuParityCoverage = explicitlyExecuted.Order(StringComparer.Ordinal).ToArray(),
                    UnexecutedProductionTails = "ActualSmallCandidateExactHandoff;OriginalQueryExactRetained;NotClaimedAsGpuDispatch"
                });
                var cancelled = await RunSession(request, output, "cancel-before-publication", false, true, null, observeSession);
                Require(cancelled.Progress.State == SearchRunState.Cancelled && cancelled.SemanticRecords.Length == 0,
                    "DeterministicCancellationAfterRealFamiliesBeforePublication");
                var persisted = ProductionSearchReplay.Read<SearchContinuationSnapshot>(Path.Combine(output, "cancel-before-publication.continuation.json"));
                Require(VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, request.CanonicalStartSeed,
                    out ulong first, out _, out _), "CanonicalOriginalStart");
                ulong end = checked(first + (ulong)request.ScanCount);
                Require(persisted.NextOrdinal >= first && persisted.NextOrdinal < end, "CancelledUnfinishedPrefixRetained");
                var resumedWorkload = item.Workload with { Options = item.Workload.Options with {
                    StartSeed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, persisted.NextOrdinal),
                    ScanCount = checked((long)(end - persisted.NextOrdinal)) } };
                var resumed = await RunSession(Compile(resumedWorkload, captured.Context), output, "resumed-compat", false,
                    false, persisted, observeSession);
                Require(compatibility.SemanticRecords.SequenceEqual(resumed.SemanticRecords) &&
                    compatibility.ContinuationFingerprint == resumed.ContinuationFingerprint &&
                    compatibility.SafeNextOrdinal == resumed.SafeNextOrdinal, "CancelledResumeSameWholeOutcomesAndCursor");
                receipts.Add(new { Case = "cancel-resume", Cancelled = cancelled.Progress, Resumed = resumed.Progress,
                    SemanticSha256 = Digest(resumed.SemanticRecords), ContinuationSerialized = true });
                receipts.Add(await CheckInitialCreationFailureAsync(request, cpu, output, observeSession));
            }
            receipts.Add(new { Case = item.Name, Cpu = cpu.Progress, Compatibility = compatibility.Progress,
                SemanticSha256 = Digest(cpu.SemanticRecords), CpuWarnings = cpu.WarningRecords,
                CompatibilityWarnings = compatibility.WarningRecords, compatibility.SafeNextOrdinal });
        }
        WriteEvidence(Path.Combine(output, "compatibility-checks.json"), new {
            Passed = true, RealProductionExact = true, RealSevenFamilyGpu = true,
            ActualBackend = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity(),
            TargetMetalExecutionClaimed = false, ThroughputClaimed = false, Cases = receipts });
        Console.WriteLine("COMPATIBILITY_CHECKS_PASS actualGpuNRCWAES=true;fullExact=true;positiveAndReject=true;cancelResume=true;injectedCreationFailureRealExact=true;targetMetalExecution=false;throughputClaim=false");
    }

    private static async Task<object> CheckInitialCreationFailureAsync(ExactSearchExecutionRequest request,
        Receipt cpuReference, string output, Action<ProductionSearchSession?> observeSession)
    {
        const string label = "initial-creation-failure-real-exact";
        var originalFailure = new InvalidOperationException("HostInjectedInitialDeviceCreationFailure",
            new IOException("HostOriginalDeviceCreationCause"));
        var actualEnvironment = FamilyDeviceProfileFoundation.Initialization.Capture();
        var localInitialization = new FamilyGpuInitialization();
        localInitialization.ObserveEnvironment("InjectedFailure|" + actualEnvironment.EnvironmentKey,
            actualEnvironment.MainDeviceAvailable, actualEnvironment.RenderingBackend);
        var selected = FamilyExecutionCoordinator.Plan(request, gpuFloat64Available: false);
        var originallySelected = selected.OrderedFamilies.Select(Describe).ToArray();
        Require(Flatten(originallySelected).Any(row => row.UsesGpu),
            "CreationFailureStartsWithActualGpuAllocation");
        int creations = 0;
        FamilyExecutionContext? executionContext = null;
        ProductionSearchReplay.LogSessionWorkload(request, label);
        await using var session = new ProductionSearchSession(request, selected,
            FamilySearchEtaProjectorV1.Project(request, selected),
            FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, selected.OrderedFamilies), 64, null, "",
            failOnPhysicalRecovery: false,
            executionContextFactory: () => executionContext = new FamilyExecutionContext(true,
                failOnPhysicalRecovery: false, cpuWorkerCount: request.WorkerCount,
                gpuOwnerFactory: () => new FamilyGpuExecutionOwner(() =>
                {
                    Interlocked.Increment(ref creations);
                    throw originalFailure;
                }, localInitialization)), allowInitialGpuCreationFallback: true);
        observeSession(session);
        var candidates = new List<SearchCandidate>();
        using var matches = new StreamWriter(Path.Combine(output, label + ".matches.jsonl"));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!session.Completion.IsCompleted)
        {
            Drain();
            if (watch.Elapsed > TimeSpan.FromSeconds(120))
            { session.Cancel(); throw new TimeoutException("CompatibilityHost.SessionTimeout:" + label); }
            await Task.WhenAny(session.Completion, Task.Delay(1)).ConfigureAwait(false);
        }
        await session.Completion.ConfigureAwait(false);
        Drain();
        var progress = session.GetProgress();
        var checkpoint = session.CaptureContinuation() ?? throw new InvalidDataException("CompatibilityHost.MissingContinuation:" + label);
        var failedOwner = localInitialization.Capture();
        var retired = selected.OrderedFamilies.Select(Describe).ToArray();
        var active = WriteInvocationReceipt(session, output, label, true, failedOwner);
        var semantics = candidates.Select(SemanticRecord).Order(StringComparer.Ordinal).ToArray();
        ProductionSearchReplay.Write(Path.Combine(output, label + ".continuation.json"), checkpoint);
        WriteEvidence(Path.Combine(output, label + ".summary.json"), new {
            DeviceCreationFailureInjected = true, ExactOutcomesInjected = false, progress,
            OriginallySelected = originallySelected, RetiredInvocations = retired, ActiveInvocations = active,
            session.ExecutionPlan.PlanId, LocalOwnerInitialization = failedOwner, CreationCalls = creations,
            CompletedGpuBatches = Flatten(retired).Where(row => row.UsesGpu).Sum(row => row.Batches),
            GpuActionsExecuted = 0,
            GpuActionEvidence = "Injected factory throws before returning any RenderingDevice/queue action; retired actual GPU bodies completed zero batches",
            safeNextOrdinal = session.GetSafeNextOrdinal(), DurableNextOrdinal = checkpoint.NextOrdinal,
            diagnostics = session.GetDiagnosticSummary(), SemanticSha256 = Digest(semantics),
            OriginalError = originalFailure.ToString(), TargetDeviceCreationSuccessClaimed = false });
        Require(progress.State == SearchRunState.Completed && progress.ScannedCount == request.ScanCount &&
            progress.MatchCount == cpuReference.Progress.MatchCount &&
            progress.UnverifiedCandidateCount == cpuReference.Progress.UnverifiedCandidateCount &&
            progress.PendingCandidateCount == 0 && progress.CleanupIssue.Length == 0 && progress.FailureCode.Length == 0,
            "CreationFailureCompletesOriginalRangeWithRealExact");
        Require(semantics.SequenceEqual(cpuReference.SemanticRecords) &&
            session.GetSafeNextOrdinal() == cpuReference.SafeNextOrdinal && checkpoint.NextOrdinal == cpuReference.SafeNextOrdinal,
            "CreationFailureSameCpuSemanticMultisetAndDurableCursor");
        Require(creations == 1 && failedOwner.State == FamilyGpuInitializationState.Failed && failedOwner.ActiveOwners == 0 &&
            executionContext!.TryGetInitialGpuCreationFailure(originalFailure, out var original) &&
            ReferenceEquals(original!.Error, originalFailure) &&
            failedOwner.LastFailure!.Details.Contains("HostOriginalDeviceCreationCause", StringComparison.Ordinal) &&
            Flatten(retired).Where(row => row.UsesGpu).All(row => row.Batches == 0),
            "SingleOriginalCreationFailureNoGpuWorkOrDeviceRetry");
        Require(active.Length == 0 && session.FamilyIds.Count == 0 &&
            session.ExecutionPlan.PlanId == "GpuInitializationFallback.DirectProductionExact" &&
            session.EtaProjection.Status == FamilySearchEtaStatus.Unavailable && session.EtaProjection.TargetStages.Count == 0,
            "CreationFailureActiveDirectExactRetiresGpuPlanAndPrice");
        bool RecoveryWarning(string warning) => warning.Contains("GpuInitializationUnavailable", StringComparison.Ordinal) &&
            warning.Contains("HostInjectedInitialDeviceCreationFailure", StringComparison.Ordinal) &&
            warning.Contains("replacement=DirectProductionExactCpu", StringComparison.Ordinal);
        Require(progress.ExecutionWarnings.Count == 1 && progress.ExecutionWarnings.All(RecoveryWarning) &&
            candidates.All(candidate => candidate.ExecutionWarnings.Count == 1 && candidate.ExecutionWarnings.All(RecoveryWarning)),
            "CreationFailurePreservesTruthfulWarningOnSessionAndActualOutputs");
        observeSession(null);
        return new { Case = "injected-initial-device-creation-failure-real-exact", Passed = true,
            DeviceCreationFailureInjected = true, ExactOutcomesInjected = false, CreationCalls = creations,
            GpuActionsExecuted = 0, ActivePlan = session.ExecutionPlan.PlanId,
            SemanticSha256 = Digest(semantics), progress, Warning = progress.ExecutionWarnings.Single() };

        void Drain()
        {
            var received = new List<SearchCandidate>();
            while (session.TryReadCandidate(out var candidate))
            {
                if (candidate is null) continue;
                matches.WriteLine(FullRecord(candidate));
                received.Add(candidate);
            }
            if (received.Count == 0) return;
            FlushReceipt(matches);
            foreach (var candidate in received)
            { session.AcknowledgeCandidate(candidate.Seed); candidates.Add(candidate); }
        }
    }

    private static async Task<Receipt> RunSession(ExactSearchExecutionRequest request, string output, string label,
        bool cpuOnly, bool cancelBeforePublication, SearchContinuationSnapshot? continuation,
        Action<ProductionSearchSession?> observeSession, bool nativeGpu = false)
    {
        int publicationGate = 0;
        async ValueTask BeforePublish(int _, CancellationToken token)
        {
            Interlocked.Increment(ref publicationGate);
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        }
        ProductionSearchSession session;
        if (cancelBeforePublication)
        {
            var plan = FamilyExecutionCoordinator.Plan(request, gpuFloat64Available: nativeGpu);
            ProductionSearchReplay.LogSessionWorkload(request, label);
            session = new(request, plan, FamilySearchEtaProjectorV1.Project(request, plan),
                FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, plan.OrderedFamilies), 64, null, "",
                failOnPhysicalRecovery: true, beforeCandidatePublish: BeforePublish, continuation: continuation);
        }
        else session = FamilyExecutionCoordinator.Start(request, failOnPhysicalRecovery: true,
            gpuAvailable: cpuOnly ? false : null, continuation: continuation, gpuFloat64Available: cpuOnly ? null : nativeGpu);
        await using var owned = session;
        observeSession(session);
        var semantics = new List<string>();
        var warnings = new List<string>();
        using var matches = new StreamWriter(Path.Combine(output, label + ".matches.jsonl"));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        bool cancelled = false;
        while (!session.Completion.IsCompleted)
        {
            if (cancelBeforePublication && !cancelled && Volatile.Read(ref publicationGate) != 0)
            { session.Cancel(); cancelled = true; }
            Drain();
            if (watch.Elapsed > TimeSpan.FromSeconds(120))
            { session.Cancel(); throw new TimeoutException("CompatibilityHost.SessionTimeout:" + label); }
            await Task.WhenAny(session.Completion, Task.Delay(1)).ConfigureAwait(false);
        }
        await session.Completion.ConfigureAwait(false);
        Drain();
        var progress = session.GetProgress();
        var checkpoint = session.CaptureContinuation() ?? throw new InvalidDataException("CompatibilityHost.MissingContinuation:" + label);
        var invocations = WriteInvocationReceipt(session, output, label, !cpuOnly && !nativeGpu);
        ProductionSearchReplay.Write(Path.Combine(output, label + ".continuation.json"), checkpoint);
        WriteEvidence(Path.Combine(output, label + ".summary.json"), new {
            progress, families = session.FamilyIds, safeNextOrdinal = session.GetSafeNextOrdinal(),
            diagnostics = session.GetDiagnosticSummary(), DurableNextOrdinal = checkpoint.NextOrdinal });
        Require(progress.State == (cancelBeforePublication ? SearchRunState.Cancelled : SearchRunState.Completed), "SessionState:" + label);
        Require(progress.PendingCandidateCount == 0 && progress.CleanupIssue.Length == 0 &&
            progress.ExecutionWarnings.Count == 0 && progress.FailureCode.Length == 0,
            "NoHiddenFailureIsolationOrCleanup:" + label);
        if (!cancelBeforePublication) Require(progress.ScannedCount == request.ScanCount &&
            checkpoint.NextOrdinal == session.GetSafeNextOrdinal(), "CompleteRangeAndDurableAcknowledgement:" + label);
        observeSession(null);
        return new(label, request.SnapshotFingerprint, SearchContinuationIdentity.For(request),
            semantics.Order(StringComparer.Ordinal).ToArray(), warnings.Order(StringComparer.Ordinal).ToArray(),
            progress, session.GetSafeNextOrdinal(), checkpoint, invocations);

        void Drain()
        {
            var received = new List<SearchCandidate>();
            while (session.TryReadCandidate(out var candidate))
            {
                if (candidate is null) continue;
                string expectedFingerprint = candidate.IsUnverified ? request.SnapshotFingerprint : string.Join("|", new[] {
                    request.SnapshotFingerprint, candidate.Authority.EffectSnapshotFingerprint,
                    candidate.Authority.WorldSnapshotFingerprint }.Where(value => !string.IsNullOrWhiteSpace(value)));
                Require(candidate.SnapshotFingerprint == expectedFingerprint, "PublishedAttemptIdentity:" + label);
                matches.WriteLine(FullRecord(candidate));
                received.Add(candidate);
            }
            if (received.Count == 0) return;
            FlushReceipt(matches);
            foreach (var candidate in received)
            {
                session.AcknowledgeCandidate(candidate.Seed);
                semantics.Add(SemanticRecord(candidate));
                warnings.Add(JsonSerializer.Serialize(new { candidate.Seed, candidate.ExecutionWarnings }, Compact));
            }
        }
    }

    private static void Compare(Receipt cpu, Receipt candidate, string name)
    {
        Require(cpu.RequestSnapshotFingerprint == candidate.RequestSnapshotFingerprint &&
            cpu.ContinuationFingerprint == candidate.ContinuationFingerprint && cpu.SemanticRecords.SequenceEqual(candidate.SemanticRecords) &&
            cpu.SafeNextOrdinal == candidate.SafeNextOrdinal && cpu.Progress.MatchCount == candidate.Progress.MatchCount &&
            cpu.Progress.UnverifiedCandidateCount == candidate.Progress.UnverifiedCandidateCount,
            "CpuCompatibilityCompleteSemanticMultisetAndCursor:" + name);
        Require(cpu.WarningRecords.All(row => row.Contains("\"ExecutionWarnings\":[]", StringComparison.Ordinal)) &&
            candidate.WarningRecords.All(row => row.Contains("\"ExecutionWarnings\":[]", StringComparison.Ordinal)),
            "WarningsReportedSeparatelyAndNoFailureFallback:" + name);
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException("CompatibilityHost." + reason);
    }
}
