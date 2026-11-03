using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

internal static partial class CompatibilityChecks
{
    internal static async Task<IReadOnlySet<string>> CheckPublicFamilyParityAsync(PreparedSearch request, string output,
        Func<bool, IReadOnlyList<FilterBodyDescription>>? createFamilies = null)
    {
        if (request.Model.CompiledSearch.Context.Party is not null)
            throw new ArgumentException("Public family parity requires an actual solo request; party wrappers use their existing suite");
        var profile = RuntimeProfileRegistry.Select(request.Model.Detection);
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(profile, request.CanonicalStartSeed, out ulong start, out _, out string issue))
            throw new InvalidDataException("CompatibilityFamilyParity.Start:" + issue);
        int count = (int)Math.Min(4099UL, VisibleSeedCandidateCodec.SpaceSize(profile) - start);
        if (count < 2) throw new InvalidDataException("CompatibilityFamilyParity.InsufficientVisibleSeedRange");
        var batch = new SearchBatch(start, count);
        FamilyCandidateSet[] inputs = [FamilyCandidateSet.Dense(batch),
            FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, count)
                .Where(i => i == 0 || i == 1 || i == count - 1 || i % 7 == 0).Select(i => (ulong)i).ToArray()),
            FamilyCandidateSet.Dense(new SearchBatch(start, 0))];
        string[] roles = ["dense-tail", "sparse-compact", "zero"];
        Directory.CreateDirectory(output);
        string sourceDirectory = Path.Combine(output, "family-parity-sources");
        Directory.CreateDirectory(sourceDirectory);

        var owned = new Dictionary<FilterBodyDescription, FilterRuntimeServices>(ReferenceEqualityComparer.Instance);
        var disposed = new HashSet<FilterBodyDescription>(ReferenceEqualityComparer.Instance);
        var rows = new List<object>();
        var executedGpuFamilies = new HashSet<string>(StringComparer.Ordinal);
        var sources = new List<object>();
        var cleanupFailures = new List<Exception>();
        FilterRuntimeServices? cpuContext = null, gpuContext = null;
        Exception? primary = null;
        int comparedFamilies = 0;
        try
        {
            cpuContext = new(requiresGpu: false, failOnPhysicalRecovery: true, cpuWorkerCount: request.WorkerCount);
            gpuContext = new(requiresGpu: true, failOnPhysicalRecovery: true, cpuWorkerCount: request.WorkerCount);
            var initialExact = ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.FromCanonicalSeed(profile, request.CanonicalStartSeed));
            // Families own a session input-mode contract (in particular S).
            // Each role gets new actual invocations; do not switch a running
            // family from dense input to compact input merely for this check.
            for (int role = 0; role < inputs.Length; role++)
            {
            var native = createFamilies?.Invoke(true) ??
                FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true, gpuFloat64Available: true);
            foreach (var family in native) owned.Add(family, gpuContext);
            var compatibility = createFamilies?.Invoke(false) ??
                FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true, gpuFloat64Available: false);
            foreach (var family in compatibility) owned.Add(family, gpuContext);
            var compatibleById = compatibility.ToDictionary(family => family.FamilyId, StringComparer.Ordinal);
            foreach (var nativeFamily in native)
            {
                if (!nativeFamily.ConditionPerformance.UsesGpu)
                {
                    rows.Add(new { nativeFamily.FamilyId, Status = "NativeGpuNotAdmitted",
                        NativeRevision = nativeFamily.ConditionPerformance.PhysicalImplementationRevision });
                    continue;
                }
                if (!compatibleById.TryGetValue(nativeFamily.FamilyId, out var compatibleFamily) ||
                    !compatibleFamily.ConditionPerformance.UsesGpu)
                    throw new InvalidDataException("CompatibilityFamilyParity.GpuCoverageMissing:" + nativeFamily.FamilyId);
                if ((compatibleFamily.GpuNumericRequirements & (GpuNumericRequirements.Float64 | GpuNumericRequirements.Unspecified)) != 0)
                    throw new InvalidDataException("CompatibilityFamilyParity.IncompatibleGpuRequirements:" + nativeFamily.FamilyId);
                if (!nativeFamily.Coverage.SequenceEqual(compatibleFamily.Coverage))
                    throw new InvalidDataException("CompatibilityFamilyParity.CoverageChanged:" + nativeFamily.FamilyId);
                if ((nativeFamily.GpuNumericRequirements & GpuNumericRequirements.Float64) != 0 &&
                    nativeFamily.ConditionPerformance.PhysicalImplementationRevision == compatibleFamily.ConditionPerformance.PhysicalImplementationRevision)
                    throw new InvalidDataException("CompatibilityFamilyParity.VariantPhysicalIdentityReused:" + nativeFamily.FamilyId);

                var (cpuFamily, cpuSelection) = UnconditionedCpuFamily(nativeFamily, request);
                owned.Add(cpuFamily, cpuContext);
                if (cpuFamily.ConditionPerformance.UsesGpu || cpuFamily.RequiredPassedCoverage is not null ||
                    !cpuFamily.Coverage.SequenceEqual(nativeFamily.Coverage))
                    throw new InvalidDataException("CompatibilityFamilyParity.CpuPredicateNotUnconditioned:" + nativeFamily.FamilyId);
                string label = nativeFamily.FamilyId.Replace('.', '-');
                if (role == 0)
                {
                    ExportActualFamilySources(request, nativeFamily, "native", label, sourceDirectory, sources);
                    ExportActualFamilySources(request, compatibleFamily, "integer", label, sourceDirectory, sources);
                }
                ulong[] expected = await InvokePublicFamily(cpuFamily, cpuContext, request, inputs[role]);
                ulong[] nativeOutput = await InvokePublicFamily(nativeFamily, gpuContext, request, inputs[role]);
                var nativeObservation = nativeFamily.CapturePerformanceObservation();
                string nativeDevice = nativeObservation.DeviceName;
                // Release each native executor before allocating its alternate;
                // source plans remain available for the final receipt.
                await DisposeOne(nativeFamily);
                {
                    ulong[] integerOutput = await InvokePublicFamily(compatibleFamily, gpuContext, request, inputs[role]);
                    bool nativeIntegerEqual = nativeOutput.SequenceEqual(integerOutput);
                    bool cpuNativeEqual = expected.SequenceEqual(nativeOutput);
                    bool passed = nativeIntegerEqual && cpuNativeEqual;
                    if (role != 2 && initialExact.IsMatch && !nativeOutput.Contains(0UL)) passed = false;
                    string caseLabel = label + "." + roles[role];
                    File.WriteAllBytes(Path.Combine(output, caseLabel + ".cpu.abi1.bin"), PublicOrdinalBytes(expected));
                    File.WriteAllBytes(Path.Combine(output, caseLabel + ".native.abi1.bin"), PublicOrdinalBytes(nativeOutput));
                    File.WriteAllBytes(Path.Combine(output, caseLabel + ".integer.abi1.bin"), PublicOrdinalBytes(integerOutput));
                    var row = new
                    {
                        Family = nativeFamily.FamilyId, Role = roles[role], Passed = passed,
                        NativeIntegerEqual = nativeIntegerEqual, CpuNativeEqual = cpuNativeEqual,
                        InputCount = inputs[role].Count, BatchBase = inputs[role].Batch.BatchBase,
                        RootPopulation = inputs[role].Batch.BatchCandidateCount,
                        CpuCount = expected.Length, NativeCount = nativeOutput.Length, IntegerCount = integerOutput.Length,
                        CpuRevision = cpuFamily.ConditionPerformance.PhysicalImplementationRevision, CpuSelection = cpuSelection,
                        NativeRevision = nativeFamily.ResolveConditionPerformance(role == 1).PhysicalImplementationRevision,
                        IntegerRevision = compatibleFamily.ResolveConditionPerformance(role == 1).PhysicalImplementationRevision,
                        NativeDevice = nativeDevice,
                        IntegerDevice = compatibleFamily.CapturePerformanceObservation().DeviceName,
                        NativeRequirements = nativeFamily.GpuNumericRequirements.ToString(), IntegerRequirements = compatibleFamily.GpuNumericRequirements.ToString(),
                        CpuSha256 = PublicOrdinalHash(expected), NativeSha256 = PublicOrdinalHash(nativeOutput),
                        IntegerSha256 = PublicOrdinalHash(integerOutput), WholeQueryStartDisposition = initialExact.Evaluation.Disposition.ToString(),
                        StartSurvivesNative = nativeOutput.Contains(0UL),
                        OnlyCpu = expected.Except(nativeOutput).Take(16).ToArray(),
                        OnlyNativeAgainstCpu = nativeOutput.Except(expected).Take(16).ToArray(),
                        OnlyNativeAgainstInteger = nativeOutput.Except(integerOutput).Take(16).ToArray(),
                        OnlyInteger = integerOutput.Except(nativeOutput).Take(16).ToArray()
                    };
                    rows.Add(row);
                    WriteEvidence(Path.Combine(output, caseLabel + ".parity.json"), row);
                    if (!passed) throw new InvalidDataException("CompatibilityFamilyParity.OrderedAbi1Mismatch:" + caseLabel);
                    if (role == 0)
                    {
                        var integerObservation = compatibleFamily.CapturePerformanceObservation();
                        if (nativeObservation.PhysicalBatchCount <= 0 || nativeObservation.InputCandidates <= 0 ||
                            integerObservation.PhysicalBatchCount <= 0 || integerObservation.InputCandidates <= 0)
                            throw new InvalidDataException("CompatibilityFamilyParity.NoActualDenseGpuWork:" + caseLabel);
                        executedGpuFamilies.UnionWith(compatibleFamily.Coverage);
                    }
                }
                await DisposeOne(compatibleFamily);
                await DisposeOne(cpuFamily);
                if (role == 0) comparedFamilies++;
            }
            // Also release registered non-GPU and otherwise uninvoked bodies
            // before the next role constructs a new independent session.
            foreach (var family in native.Concat(compatibility)) await DisposeOne(family);
            }
            if (comparedFamilies == 0) throw new InvalidDataException("CompatibilityFamilyParity.NoGpuFamilies");
        }
        catch (Exception error) { primary = error; }

        // All actual invocations remain owned here even after a failed compile,
        // dispatch or comparison. Keep secondary cleanup causes alongside it.
        foreach (var family in owned.Keys)
            try { await DisposeOne(family); } catch (Exception error) { cleanupFailures.Add(error); }
        if (gpuContext is not null)
            try { await gpuContext.DisposeAsync(); } catch (Exception error) { cleanupFailures.Add(error); }
        if (cpuContext is not null)
            try { await cpuContext.DisposeAsync(); } catch (Exception error) { cleanupFailures.Add(error); }

        WriteEvidence(Path.Combine(sourceDirectory, "manifest.json"), sources);
        WriteEvidence(Path.Combine(output, "public-family-parity.json"), new
        {
            Passed = primary is null && cleanupFailures.Count == 0, ComparedGpuFamilies = comparedFamilies,
            RequestSnapshotFingerprint = request.SnapshotFingerprint, QueryFingerprint = request.Model.CompiledSearch.SemanticFingerprint,
            StartOrdinal = start, DenseRootCount = count, SparseIncludesOrdinalZero = true, IncludesZeroInput = true,
            CompleteOrderedAbi1Compared = true, ActualDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity(),
            DeviceInitialization = FamilyDeviceProfileFoundation.Initialization.Capture(),
            Rows = rows, Failure = primary?.ToString(), CleanupFailures = cleanupFailures.Select(error => error.ToString()).ToArray(),
            TargetMetalExecutionClaimed = false, ThroughputClaimed = false, CalibrationSubmitted = false,
            Scope = "Actual registered solo public calls; integer variant selection on actual driver; no profile or Exact outcome substitution"
        });
        if (primary is not null)
        {
            if (cleanupFailures.Count != 0) throw new AggregateException("CompatibilityFamilyParity.FailedAndCleanupFailed", [primary, ..cleanupFailures]);
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
        if (cleanupFailures.Count != 0) throw new AggregateException("CompatibilityFamilyParity.CleanupFailed", cleanupFailures);
        GD.Print($"COMPATIBILITY_PUBLIC_FAMILY_PARITY_PASS families={comparedFamilies};denseRoots={count};compactIncludesZero=true;zeroInput=true;targetMetalClaimed=false");
        return executedGpuFamilies;

        async ValueTask DisposeOne(FilterBodyDescription family)
        {
            if (disposed.Add(family)) await family.DisposeAsync(owned[family]);
        }
    }

    private static async Task<ulong[]> InvokePublicFamily(FilterBodyDescription family, FilterRuntimeServices context,
        PreparedSearch request, FamilyCandidateSet input)
    {
        var actual = await family.ExecuteAsync(context, input, CancellationToken.None);
        if (actual.Batch != input.Batch || HostFilterBindings.Faulted(family))
            throw new InvalidDataException("CompatibilityFamilyParity.InvocationBoundary:" + family.FamilyId);
        ulong[] rows = actual.EnumerateLogicalOrdinals().ToArray();
        var source = input.EnumerateLogicalOrdinals().ToHashSet();
        for (int i = 0; i < rows.Length; i++)
            if (!source.Contains(rows[i]) || i > 0 && rows[i] <= rows[i - 1])
                throw new InvalidDataException("CompatibilityFamilyParity.NotOrderedInputSubset:" + family.FamilyId);
        return rows;
    }

    private static (FilterBodyDescription Family, string Selection) UnconditionedCpuFamily(FilterBodyDescription owner,
        PreparedSearch request)
    {
        FilterBodyDescription? cpu = null;
        // Prefer complete public owner calls over optional cheaper necessary
        // pre-gates. In particular, never clear Capsule's GivenN requirement.
        var model = request.Model;
        var preferences = FilterExecutionPreferences.From(request.RunOptions);
        switch (owner.FamilyId)
        {
            case "N.Neow": NeowFamily.TryCreate(model, preferences, out cpu, preferGpu: false); break;
            case "R.Relic" when NeowReplayPlan.HasCapsule(model):
                if (CapsuleRelicFamily.TryPrepare(model, preferences, out var capsule, preferGpu: false) && capsule is not null)
                {
                    // This oracle is the complete original replay. It owns no
                    // conditional price or historical N-after-input calibration.
                    const string revision = "R.Relic.Cpu.CapsuleAndSequenceReplay.20260905.v1";
                    var profile = RuntimeProfileRegistry.Select(model.Detection);
                    int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
                    bool numericalRoots = model.ProfileId == RuntimeProfileId.Beta111;
                    cpu = new(capsule.Body.Preparation, revision, model.ProfileId)
                    {
                        InputIdentity = model.SnapshotFingerprint, PlayersCount = model.Authority.PlayersCount,
                        WindowCapacity = 65536, CpuChunkCapacity = CpuFilterExecution.Capacity, CpuWorkers = workers,
                        PerformanceProjection = compact => CpuFilterExecution.Describe(owner.FamilyId, revision,
                            workers, 65536, numericalRoots, false, compact),
                        CreateExecution = (self, _) => new CpuFilterExecution(self, profile, capsule.Replay.Matches, numericalRoots)
                    };
                }
                break;
            case "R.Relic": RelicFamily.TryCreate(model, preferences, out cpu, preferGpu: false); break;
            case "C.CombatReward": CombatRewardFamily.TryCreate(model, preferences, out cpu, preferGpu: false); break;
            case "W.World": WorldFamily.TryCreate(model, preferences, out cpu, preferGpu: false); break;
            case "A.AncientOption": AncientOptionFamily.TryCreate(model, preferences, out cpu, preferGpu: false); break;
            case "E.EventResult": EventResultFamily.TryCreate(model, preferences, out cpu, preferGpu: false); break;
            case "T.TransformationAggregate": TransformationAggregateFamily.TryCreate(model, preferences, out cpu, preferGpu: false); break;
        }
        if (cpu is not null) return (cpu, "ActualOwnerCpuFactory;CompleteUnconditionedPublicCall");
        cpu = HostFilterBindings.ConfiguredCpuBodies(owner)
            .FirstOrDefault(family => family.RequiredPassedCoverage is null);
        return cpu is not null ? (cpu, "ConfiguredUnconditionedCpuRealization") :
            throw new InvalidDataException("CompatibilityFamilyParity.UnconditionedCpuUnavailable:" + owner.FamilyId);
    }

    private static void ExportActualFamilySources(PreparedSearch request, FilterBodyDescription family, string variant, string label,
        string directory, List<object> evidence)
    {
        var bodies = new List<(string Name, string Source, string? PriorNative)>();
        var variantMode = variant == "native" ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne;
        var model = request.Model;
        var prefs = FilterExecutionPreferences.From(request.RunOptions);
        bool preparedEventGroups = false;
        switch (family.FamilyId)
        {
            case "E.EventResult":
            {
                var events = new EventResultFamilyPlan(model, numericalVariant: variantMode);
                preparedEventGroups = events.UsesPreparedGroups;
                string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta111GpuEventResult.comp.glsl");
                string numerical = donor[donor.IndexOf("uint64_t s0;", StringComparison.Ordinal)..donor.IndexOf("void append_output", StringComparison.Ordinal)];
                if (events.UsesPreparedGroups) numerical = "#define RT2_E_PREPARED_GROUPS\n" + numerical;
                string prior = FamilyGpuComputeUtility.LoadEmbeddedShader("EventResultFamily.comp.glsl").Replace("/*__DONOR__*/", numerical, StringComparison.Ordinal);
                bodies.Add(("module", events.ShaderSource(), prior)); break;
            }
            case "A.AncientOption":
            {
                var ancient = new AncientOptionFamilyPlan(model, numericalVariant: variantMode);
                string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta110GpuAncientOptionPreGateP3A.comp.glsl");
                string numerical = donor[donor.IndexOf("struct RngState", StringComparison.Ordinal)..donor.IndexOf("void main()", StringComparison.Ordinal)];
                string prior = FamilyGpuComputeUtility.LoadEmbeddedShader("AncientOptionFamily.comp.glsl").Replace("/*__DONOR__*/", numerical, StringComparison.Ordinal);
                bodies.Add(("module", ancient.ShaderSource(), prior)); break;
            }
            case "C.CombatReward":
                if (!CombatRewardFamily.TryPrepare(model,prefs,out var combat,true,variantMode) || combat?.GpuPlan is not {} combatPlan)
                    throw new InvalidDataException("CompatibilityFamilyParity.SourcePlanMissing:C");
                bodies.Add(("module", combatPlan.ShaderSource(), PriorNativeCombatSource(combatPlan))); break;
            case "W.World":
                var worldPlan = new WorldFamilyGpuPlan(new WorldFamilyReplay(model),model.Authority.PlayersCount == 1 &&
                    model.Authority.WorldAuthority?.Beta109Generation is { IsMultiplayer:false },variantMode);
                bodies.Add(("module", worldPlan.ShaderSource(), PriorNativeWorldSource(worldPlan))); break;
            case "T.TransformationAggregate":
                if (!TransformationAggregateFamily.TryPrepare(model,prefs,out var transform,true,variantMode) || transform is null)
                    throw new InvalidDataException("CompatibilityFamilyParity.SourcePlanMissing:T");
                bodies.Add(("module", transform.GpuPlan.ShaderSource(), null)); break;
            case "R.Relic" when NeowReplayPlan.HasCapsule(model):
                if (!CapsuleRelicFamily.TryPrepare(model,prefs,out var capsule,true,variantMode) || capsule?.GpuPlan is not {} capsulePlan)
                    throw new InvalidDataException("CompatibilityFamilyParity.SourcePlanMissing:R");
            {
                string body = RelicFamilyGpuExecutor.ShaderSource(true, capsulePlan.UsesBonesKBoundary,
                    genericReplay: capsulePlan.GenericReplay, localStateCapacity: capsulePlan.SequencePlan.LocalStateCapacity,
                    numericalVariant: capsulePlan.NumericalVariant);
                if (capsulePlan.GenericReplay) body = body.Replace("#version 450", "#version 450\n#define RT2_R_GENERIC_BAG_CAPACITY " +
                    capsulePlan.ConfiguredGenericBagCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
                bodies.Add(("module", body, null)); break;
            }
            case "N.Neow":
                var factory = variant == "native" ? (NeowFamily.GpuPlanFactory)NeowFamilyGpuPlan.TryCreate : NeowFamilyGpuPlan.TryCreateIntegerUnitFloat;
                if (!NeowFamily.TryPrepare(model,prefs,out var neowPrepared,gpuPlanFactory:factory) || neowPrepared?.GpuPlan is not {} neowPlan)
                    throw new InvalidDataException("CompatibilityFamilyParity.SourcePlanMissing:N");
                foreach (int stage in neowPlan.UsesStagedDense ? new[] { 0, 1, 2, 3 } : new[] { 3 })
                    bodies.Add(("stage" + stage, NeowFamilyGpuExecutor.ShaderSource(stage, leafyPreGate: neowPlan.UsesLeafyPreGate,
                        directNestedMode: neowPlan.DirectNestedMode, authoredUpgrades: neowPlan.HasAuthoredUpgrades,
                        localResults: neowPlan.HasLocalResults, multiplayer: neowPlan.IsMultiplayer,
                        jointTransformMetaOffset: neowPlan.JointTransformMetaOffset, numericalVariant: neowPlan.NumericalVariant), null));
                break;
        }
        foreach (var (name, source, priorNative) in bodies)
        {
            string file = label + "." + variant + "." + name + ".glsl";
            File.WriteAllText(Path.Combine(directory, file), source);
            bool? nativeMatchesPriorAssembler = variant == "native" && priorNative is not null ? source == priorNative : null;
            evidence.Add(new { Family = family.FamilyId, Variant = variant, File = file,
                SourceSha256 = PublicSourceHash(source), NativeMatchesPriorAssembler = nativeMatchesPriorAssembler,
                PriorAssemblerBaseline = priorNative is null ? null : family.FamilyId == "C.CombatReward"
                    ? "C native assembly with captured DenseCarry8/opening-prefix/observed-opening/neutral-proof operands; same production resources; not historical numerical-source identity"
                    : preparedEventGroups
                    ? "E request-groups native assembly with the selected immutable plan; same production resources; not historical numerical-source identity"
                    : "413899657e6748b69acfc129865edc8360686b0e native expression; same production resources",
                PriorAssemblerSha256 = priorNative is null ? null : PublicSourceHash(priorNative),
                PhysicalRevision = family.ConditionPerformance.PhysicalImplementationRevision });
            if (nativeMatchesPriorAssembler == false)
                throw new InvalidDataException("CompatibilityFamilyParity.NativeSourceChanged:" + file);
            if (variant == "integer") GpuIntegerNumerics.RequireNoFloat64(source, family.FamilyId);
        }
    }

    // Baseline factory expressions retained as narrow source-identity checks.
    // C reconstructs the selected immutable plan, including the newer opening
    // and real-neutral proof operands; it does not assert historical source
    // bytes. These expressions supply no executable substitutes or oracles.
    private static string PriorNativeCombatSource(CombatRewardGpuPlan plan)
    {
        string source = FamilyGpuComputeUtility.LoadEmbeddedShader("CombatRewardFamily.comp.glsl")
            .Replace("/*__C_OBSERVED_OPENING__*/", plan.UsesObservedOpening
                ? "#define RT2_C_OBSERVED_OPENING 1\n#define RT2_C_OBSERVED_HOT " + (plan.UsesObservedOpeningHotLoop ? "1\n" : "0\n") +
                  "#define RT2_C_BAG_CAPACITY " + Math.Max(1, plan.ObservedOpeningData[8]) + "\n" +
                  FamilyGpuComputeUtility.LoadEmbeddedShader("CombatRewardOpening.glsl")
                : "#define RT2_C_OBSERVED_OPENING 0\n", StringComparison.Ordinal)
            .Replace("/*__C_DENSE_HELPERS__*/", plan.UsesDenseCarry8 ?
                "uint64_t make_u64(uint lo,uint hi){return cr_make_u64(lo,hi);}\n" +
                "uint64_t rotl64(uint64_t x,uint n){return cr_rotl64(x,n);}\n" +
                FamilyGpuComputeUtility.LoadEmbeddedShader("VisibleSeedRootHashCommon.glsl") : "", StringComparison.Ordinal)
            .Replace("/*__C_TERMINAL_CARD_GATE__*/", plan.TerminalCardGateSource, StringComparison.Ordinal)
            .Replace("/*__C_CAPSULE_DEFINES__*/", "#define RT2_C_ASSIGNMENT " + (plan.Meta[56] + plan.Meta[59] > 0 ? "1\n" : "0\n") +
                "#define RT2_C_MULTIPLAYER " + (plan.Meta[3] > 1 ? "1" : "0"), StringComparison.Ordinal)
            .Replace("/*__C_HOT_DEFINES__*/", "#define RT2_C_DENSE_CARRY8 " + (plan.UsesDenseCarry8 ? "1\n" : "0\n") +
                "#define RT2_C_HOT_OPENING_PREFIX " + (plan.UsesOpeningPrefixHotLoop ? "1\n" : "0\n") +
                Beta110GpuCombatRewardHotLoopCompiler.BuildShaderDefines(plan.HotLoop,
                Beta110GpuCombatRewardRoutePolicy.UnpinnedAssumeUnperturbed, Beta110GpuCombatRewardHotLoopVariant.OptimizedSingleRoute), StringComparison.Ordinal)
            .Replace("/*__C_COMMON__*/", FamilyGpuComputeUtility.LoadEmbeddedShader("Beta110GpuCombatRewardCommon.glsl"), StringComparison.Ordinal)
            .Replace("/*__C_STREAMING__*/", FamilyGpuComputeUtility.LoadEmbeddedShader("Beta110GpuCombatRewardStreamingP10A.glsl"), StringComparison.Ordinal);
        source = source.Replace("/*__C_NEUTRAL_WITNESS__*/", plan.UsesNeutralWitness ?
            CombatRewardNeutralWitness.ShaderGuard : "", StringComparison.Ordinal);
        if (plan.UsesNeutralWitness)
            source = source.Replace("bool c_keep_root(uint64_t root){", "bool c_numeric_keep_root(uint64_t root){",
                StringComparison.Ordinal).Replace("bool c_keep_root_proven(uint64_t root){", "bool c_keep_root(uint64_t root){",
                    StringComparison.Ordinal);
        return source;
    }

    private static string PriorNativeWorldSource(WorldFamilyGpuPlan plan)
    {
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta110GpuWorldBossG1.comp.glsl");
        string rng = donor[donor.IndexOf("uint64_t s0;", StringComparison.Ordinal)..donor.IndexOf("void write_shared_state", StringComparison.Ordinal)];
        string numerical = donor[donor.IndexOf("bool encounter_eligible", StringComparison.Ordinal)..donor.IndexOf("void main()", StringComparison.Ordinal)];
        string body = FamilyGpuComputeUtility.LoadFamilyShaderWithVisibleSeedRootHash("WorldFamily.comp.glsl")
            .Replace("/*__W_DONOR__*/", rng + numerical, StringComparison.Ordinal)
            .Replace("uint sharedIds[512]", $"uint sharedIds[{Math.Max(1, plan.Buffers[2][5])}]", StringComparison.Ordinal)
            .Replace("uint maxAct=plan_meta_buffer.values[6u];", plan.VariantOnly ? "return true; uint maxAct=0u;" : "uint maxAct=plan_meta_buffer.values[6u];", StringComparison.Ordinal);
        if (plan.Revision(false).Contains(".SoloConstants.", StringComparison.Ordinal))
            body = body.Replace("uint maxAct=plan_meta_buffer.values[6u];", "uint maxAct=" +
                plan.Buffers[2][6].ToString(System.Globalization.CultureInfo.InvariantCulture) + "u;", StringComparison.Ordinal)
                .Replace("bool second=plan_meta_buffer.values[7u]!=0u;", "bool second=" +
                    (plan.Buffers[2][7] != 0 ? "true" : "false") + ";", StringComparison.Ordinal);
        return body;
    }

    private static byte[] PublicOrdinalBytes(ulong[] ordinals)
    {
        var bytes = new byte[checked(ordinals.Length * sizeof(ulong))];
        Buffer.BlockCopy(ordinals, 0, bytes, 0, bytes.Length);
        return bytes;
    }
    private static string PublicOrdinalHash(ulong[] ordinals) => Convert.ToHexString(SHA256.HashData(PublicOrdinalBytes(ordinals)));
    private static string PublicSourceHash(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}
