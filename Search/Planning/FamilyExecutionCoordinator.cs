using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Predictability;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Registers necessary filters, orders their available numerical units,
/// and starts the Search runtime. Range/cursor/result lifecycle lives in ProductionSearchSession.
/// </summary>
public static partial class FamilyExecutionCoordinator
{
    private const string PartyFilterScopeInfo = "Party:OnlySelectedBoundedFiltersParticipate;SlotAndSharedRemaindersRetainedByQuery";
    private const string ExplicitPhysicalSelection = "ExplicitPhysicalSelection=true";

    // Selection bound for callers: reserve one logical processor, while keeping
    // a usable single-worker choice on single-processor environments.
    public static int MaximumCpuSearchWorkers(int logicalProcessors) => Math.Clamp(logicalProcessors - 1, 1, 32);

    internal static FamilyExecutionPlan Plan(ExactSearchExecutionRequest plan, bool? gpuAvailable = null,
        bool? gpuFloat64Available = null)
    {
        using var logScope = RuntimeLog.PlanningScope();
        bool gpu = GpuAvailability(gpuAvailable);
        var numeric = FamilyDeviceProfileFoundation.Initialization.Capture();
        bool float64 = gpuFloat64Available ?? numeric.CanAttemptFloat64;
        var registered = CreateRegisteredFamilies(plan, gpu, float64);
        // The restriction belongs to the backend, even after registration has
        // replaced every affected body. Otherwise later selectors rebuild FP64.
        bool numericRestriction = gpu && !float64;
        if (numericRestriction)
        {
            // An affected semantic family can already be CPU-only because its
            // numerical authority/shape has no GPU implementation. Preserve that
            // existing plan, including SkipExact and integer-only private trials.
            // This speculative planning creates no native device or GPU resources.
            bool explicitControl = HasExplicitPhysicalControl();
            if (plan.RunOptions.SkipExactValidation || explicitControl)
            {
                var original = Plan(plan, gpuAvailable: gpu, gpuFloat64Available: true);
                if (original.OrderedFamilies.All(CanRunWithoutFloat64)) return original;
            }
            // Fused route-aware GPU filters can reject more than separate CPU
            // sieves. Only the unchanged final Exact can establish equivalence.
            if (plan.RunOptions.SkipExactValidation)
                throw new InvalidOperationException("GpuFloat64UnavailableWithExactSkipped:ExistingCpuFiltersRequireProductionExact");
            if (explicitControl)
                throw new InvalidOperationException("GpuFloat64PhysicalSelectionUnavailable:RequestedTrialRequiresUnavailableNumericImplementations");
        }
        FamilyExecutionPlan selected;
        if (plan.CompiledSearch.Context.Party is not null) selected = PlanParty(plan, registered);
        else selected = PlanSingleplayer(plan, registered, gpu, allowFloat64Allocations: !numericRestriction);
        if (numericRestriction && selected.OrderedFamilies.Any(f => !CanRunWithoutFloat64(f)))
            throw new InvalidOperationException("GpuNumericRequirementsViolation:RestrictedPlannerSelectedUnavailableBody");
        if (numericRestriction)
            selected = selected with { DecisionEvidence = [..selected.DecisionEvidence,
                "GpuFloat64Unavailable;integerGpuPathsRetained=true;" +
                $"selectedGpu={string.Join(',', selected.OrderedFamilies.Where(f => f.ConditionPerformance.UsesGpu).Select(f => f.FamilyId))};" +
                $"selectedCpu={string.Join(',', selected.OrderedFamilies.Where(f => !f.ConditionPerformance.UsesGpu).Select(f => f.FamilyId))};" +
                "remainingQueryOwner=ProductionExact"] };
        bool unfiltered = selected.Stages.Count == 0 && FamilyExactTimingDomains.HasQueryWork(plan.CompiledSearch.NormalizedQuery);
        return selected with
        {
            AllConditionsUnfiltered = unfiltered,
            FinalValidationSkipped = plan.RunOptions.SkipExactValidation,
            GpuPreparationFallbacks = SelectedPreparationFallbacks(registered, selected),
            FilterRemainders = DescribeFilterRemainders(plan, selected, registered),
            DecisionEvidence = [..selected.DecisionEvidence,
                $"AllConditionsUnfiltered={unfiltered};SelectedFilterCount={selected.Stages.Count};FinalValidation={(plan.RunOptions.SkipExactValidation ? "Skipped" : "ProductionExact")}"]
        };
    }
    private static bool CanRunWithoutFloat64(IFamilyInvocation family) =>
        (family.GpuNumericRequirements & (GpuNumericRequirements.Float64 | GpuNumericRequirements.Unspecified)) == 0;

    private static bool HasLocalGpuPhysicalControl()
    {
        string direct = Environment.GetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT") ?? "default";
        // Match the effective controls in the N/T GPU plan factories. Pair-local
        // (N Bones) and optimized (T Bones) are aliases of their unset defaults;
        // ignored values do not become backend-forcing requests here.
        bool localTrial = direct is "baseline" or "replay" or "continue" or "both" or "local" or
            "new-replay" or "new-pass" or "scroll-first" or "scroll-local" or "identity-direct" or
            "lost-local" or "lost-replay" or "lost-continue" or "lost-potion-first" or "lost-direct" or
            "lead-local" or "lead-first" or "lead-direct" or "arcane-first" or "arcane-index-first" or
            "arcane-direct" or "arcane-index-local" or "phial-first" or "phial-local" or "phial-direct" ||
            Environment.GetEnvironmentVariable("RT2_N_BONES_EXPERIMENT") is "baseline" or "early-accept" or "rare-local" ||
            Environment.GetEnvironmentVariable("RT2_T_FULL_TARGET_EXPERIMENT") is "baseline" or "full1m" ||
            Environment.GetEnvironmentVariable("RT2_T_BONES_EXPERIMENT") == "baseline" ||
            Environment.GetEnvironmentVariable("RT2_N_TRANSFORM_AGGREGATE_EXPERIMENT") == "baseline";
        return localTrial;
    }

    private static bool HasExplicitPhysicalControl()
    {
        string bones = Environment.GetEnvironmentVariable("RT2_BONES_CAPSULE_BOUNDARY") ?? "";
        return HasLocalGpuPhysicalControl() || bones is not ("" or "default" or "ordinary") || PrivateOrdinalSelection.IsRequested ||
            new[] { "RT2_PRIVATE_SERIAL_EXPERIMENT", "RT2_DIRECT_CAPSULE_EXPERIMENT", "RT2_RSW_EXPERIMENT",
                "RT2_NWAE_EXPERIMENT", "RT2_NC_EXPERIMENT", "RT2_EW_EXPERIMENT" }
                .Any(name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)));
    }

    private static IReadOnlyList<string> DescribeFilterRemainders(ExactSearchExecutionRequest request,
        FamilyExecutionPlan selected, IReadOnlyList<IFamilyInvocation> registered)
    {
        if (request.CompiledSearch.Context.Party is not null)
            return DescribePartyFilterRemainders(request, selected);
        var expected = new List<string>();
        var e = request.Evaluation;
        void Need(bool required, string family) { if (required) expected.Add(family); }
        Need(e.HasNeowConstraints || request.CompiledSearch.NormalizedQuery.Players.Any(p => !p.Offers.IsEmpty || NeowFamily.PartyInitial.HasConditions(p)), "N.Neow");
        Need(e.RelicSequenceConditions.Any(c => !c.IsEmpty) || e.RelicShopSequenceConditions.Any(c => !c.IsEmpty) || NeowReplayPlan.HasCapsule(request) || request.CompiledSearch.PlayerSearches.Any(p => p.NormalizedQuery.RelicSequenceConstraints.Count > 0 || p.NormalizedQuery.RelicShopSequenceConditions.Count > 0), "R.Relic");
        Need(e.RequiresNormalCombatRewardDomain || request.CompiledSearch.PlayerSearches.Any(p => p.NormalizedQuery.HasCombatRewardConstraints), "C.CombatReward");
        Need(WorldFamilyReplay.HasConditions(request), "W.World");
        Need(e.AncientBranchConditions.Any(b => b.OptionAny.Count > 0 || b.SeaGlassTargetAny.Count > 0) || e.AncientOptionFilters.Any(f => !f.IsEmpty) || e.AncientSeaGlassTargetFilters.Any(f => !f.IsEmpty), "A.AncientOption");
        Need(e.RequiresEventResultDomain, "E.EventResult"); Need(e.RequiresMerchantColorlessDomain, "S.MerchantShopColorless");
        Need(e.TransformationAggregate is not null, TransformationAggregateFamily.Id);
        Need(request.CompiledSearch.NormalizedQuery.StandardMaps.Count > 0, "M.StandardMap");
        var covered = selected.Stages.SelectMany(s => s.Coverage).ToHashSet(StringComparer.Ordinal);
        var remainder = expected.Where(f => !covered.Contains(f)).Select(f => f + ":NoSelectedFilter").ToList();
        // These are Production Exact obligations even when R/N filters cover other predicates.
        if (e.CombatCardExclusion is { IsEmpty: false }) remainder.Add("CombatCardExclusion:ProductionExactOnly");
        if (e.TreasureRelics is { IsEmpty: false } && !covered.Contains(InitialRoomsInvocation.TreasureCoverage))
            remainder.Add("TreasureRelics:ProductionExactOnly");
        if (e.FirstActUnknownEventPrefixCount > 0 && !covered.Contains(InitialRoomsInvocation.UnknownCoverage))
            remainder.Add("FirstActUnknownEventPrefix:ProductionExactOnly");
        foreach (var n in registered.OfType<NeowFamily>().Where(n => covered.Contains(n.FamilyId)))
            remainder.AddRange(n.PricingReplayPlan.ExactOnly.Select(c => "N.Neow:" + c));
        foreach (var c in registered.OfType<CombatRewardFamily>().Where(c => covered.Contains(c.FamilyId) && c.Replay.ConservativelyKeeps))
            remainder.Add("C.CombatReward:" + c.Replay.ConservativeOpeningReason);
        return remainder.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> DescribePartyFilterRemainders(ExactSearchExecutionRequest request,
        FamilyExecutionPlan selected)
    {
        var expected = new HashSet<(int? Slot, string Family)>();
        var covered = new HashSet<(int? Slot, string Family)>();
        var query = request.CompiledSearch.NormalizedQuery;
        var players = query.Players;
        void Need(int? slot, bool required, string family) { if (required) expected.Add((slot, family)); }
        // Party compilation binds shared world/map declarations once at the table
        // and keeps personal obligations in each compiled child. The final child
        // is the shared projection, even though its authority uses slot zero.
        Need(null, WorldFamilyReplay.HasConditions(request), "W.World");
        Need(null, query.FirstActUnknownEventPrefixCount > 0, InitialRoomsInvocation.UnknownCoverage);
        Need(null, query.StandardMaps.Count > 0, "M.StandardMap");
        foreach (var player in request.CompiledSearch.PlayerSearches.Take(players.Count))
        {
            var child = Compilation.ExactSearchExecutionRequestFactory.ForPlayer(request, player);
            int slot = child.Authority.PlayerSlotIndex;
            var e = child.Evaluation;
            Need(slot, e.HasNeowConstraints || NeowFamily.PartyInitial.HasConditions(players[slot]), "N.Neow");
            Need(slot, e.RelicSequenceConditions.Any(c => !c.IsEmpty) || e.RelicShopSequenceConditions.Any(c => !c.IsEmpty) || NeowReplayPlan.HasCapsule(child), "R.Relic");
            Need(slot, e.CombatCardExclusion is { IsEmpty: false }, "CombatCardExclusion");
            Need(slot, e.TreasureRelics is { IsEmpty: false }, "TreasureRelics");
            Need(slot, e.FirstActUnknownEventPrefixCount > 0, InitialRoomsInvocation.UnknownCoverage);
            Need(slot, e.RequiresNormalCombatRewardDomain, "C.CombatReward");
            Need(slot, e.AncientBranchConditions.Any(b => b.OptionAny.Count > 0 || b.SeaGlassTargetAny.Count > 0) ||
                e.AncientOptionFilters.Any(f => !f.IsEmpty) || e.AncientSeaGlassTargetFilters.Any(f => !f.IsEmpty), "A.AncientOption");
            Need(slot, e.RequiresEventResultDomain, "E.EventResult");
            Need(slot, e.RequiresMerchantColorlessDomain, "S.MerchantShopColorless");
        }
        foreach (var family in selected.OrderedFamilies)
        {
            if (family is PartyFamily party)
            {
                foreach (var (slot, child) in party.PersonalStages)
                    foreach (string coverage in child.Coverage)
                        covered.Add((coverage == "W.World" ? null : slot, coverage));
            }
            else if (family is NeowFamily.PartyInitial)
            {
                // PartyInitial is one table invocation covering the requested N
                // obligations for every seat; ordinary PartyFamily stages are not.
                foreach (var player in players.Where(NeowFamily.PartyInitial.HasConditions))
                    covered.Add((player.Slot, "N.Neow"));
            }
            else if (family is InitialRoomsInvocation initial)
            {
                if (initial.Coverage.Contains("W.World")) covered.Add((null, "W.World"));
                if (initial.Coverage.Contains(InitialRoomsInvocation.UnknownCoverage))
                    foreach (var source in PartyInitialRooms.Prefixes(request))
                        covered.Add((source.Slot, InitialRoomsInvocation.UnknownCoverage));
            }
        }
        var remainder = expected.Where(e => !covered.Contains(e)).Select(e =>
            e.Family + (e.Slot is int slot ? $":slot={slot}" : ":Shared") +
            (e.Family is "TreasureRelics" or InitialRoomsInvocation.UnknownCoverage or "CombatCardExclusion" ? ":ProductionExactOnly" : ":NoSelectedFilter")).ToList();
        if (expected.Count > 0)
            remainder.Add(PartyFilterScopeInfo);
        return remainder.Distinct(StringComparer.Ordinal).ToArray();
    }
    internal static IReadOnlyList<FamilyGpuPreparationFallback> SelectedPreparationFallbacks(
        IReadOnlyList<IFamilyInvocation> registered, FamilyExecutionPlan selected)
    {
        var evidence = new List<FamilyGpuPreparationFallback>();
        string Detail(IFamilyInvocation family) => family switch
        {
            NeowFamily n => n.GpuPreparationFailureDetail,
            NeowFamily.PartyInitial n => n.GpuPreparationFailureDetail,
            AncientOptionFamily a => a.Plan.GpuPreparationFailureDetail,
            EventResultFamily e => e.Plan.GpuPreparationFailureDetail,
            WorldFamily w => w.GpuPreparationFailureDetail,
            CombatRewardFamily c => c.GpuPreparationFailureDetail,
            TransformationAggregateFamily t => t.GpuPreparationFailureDetail,
            CapsuleRelicFamily r => r.GpuPreparationFailureDetail,
            _ => ""
        };
        foreach (var family in registered)
        {
            if (family is PartyFamily party)
            {
                // A party stage may retain GPU work for other slots. Report only
                // the failed preparation whose own existing CPU body was selected.
                if (!selected.OrderedFamilies.Contains(party)) continue;
                foreach (var (slot, child) in party.PersonalStages)
                {
                    string detail = Detail(child);
                    if (detail.Length > 0 && !child.ConditionPerformance.UsesGpu)
                        evidence.Add(new(child.FamilyId, $"slot={slot};coverage={string.Join(',', child.Coverage)};replacement=CpuExistingFilter", detail));
                }
                continue;
            }
            string failure = Detail(family);
            if (failure.Length == 0) continue;
            if (!selected.Stages.Any(s => !s.ConditionPerformance.UsesGpu &&
                family.Coverage.All(s.Coverage.Contains))) continue;
            evidence.Add(new(family.FamilyId, $"coverage={string.Join(',', family.Coverage)};replacement=CpuExistingFilter;remainingQueryOwner=ProductionExact", failure));
        }
        return evidence;
    }
    private static bool GpuAvailability(bool? requested)
    {
        if (requested.HasValue) return requested.Value;
        var snapshot = FamilyDeviceProfileFoundation.Initialization.Capture();
        return snapshot.GpuEligible && snapshot.State != FamilyGpuInitializationState.Creating &&
            FamilyDeviceProfileFoundation.RetryTask is not { IsCompleted: false };
    }
    private static FamilyExecutionPlan PlanSingleplayer(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered, bool gpu, bool allowFloat64Allocations = true)
    {
        RuntimeLog.TryBackgroundInfo($"searchPhysicalAvailability=true;gpuAvailable={gpu};cpuQuotes=UnitReferences;unpricedNotUnsupported=true");
        if (HasExplicitPhysicalControl())
        {
            if (!gpu && HasLocalGpuPhysicalControl())
                throw new InvalidOperationException("ExplicitPhysicalSelectionGpuUnavailable:RequestedGpuNumericalBodyUnavailable");
            var trial = PlanExplicitPhysicalTrial(request, registered);
            if (!gpu && trial.OrderedFamilies.Any(f => f.ConditionPerformance.UsesGpu))
                throw new InvalidOperationException("ExplicitPhysicalSelectionGpuUnavailable:RequestedGpuNumericalBodyUnavailable");
            return trial with { DecisionEvidence = [..trial.DecisionEvidence, ExplicitPhysicalSelection] };
        }
        return PlanSimpleSingleplayer(request, registered, gpu, allowFloat64Allocations);
    }
    private static IReadOnlyList<IFamilyInvocation> CpuAllocation(IReadOnlyList<IFamilyInvocation> registered)
    {
        var selected = new List<IFamilyInvocation>();
        foreach (var family in registered)
        {
            var cpu = FamilyCpuExecution.ConfiguredRealizations(family).FirstOrDefault();
            if (cpu is not null) selected.Add(cpu);
            else if (!family.ConditionPerformance.UsesGpu) selected.Add(family);
            else RuntimeLog.TryBackgroundWarning($"filterPhysicalUnavailable=true;family={family.FamilyId};requested=CPU;replacement=None;remainingQueryOwner=ProductionExact;replacementCompleteness=NotClaimed");
        }
        return selected;
    }
    private static FamilyExecutionPlan PlanExplicitPhysicalTrial(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered)
    {
        // Explicit diagnostics select their requested numerical bodies directly.
        // No automatic allocation optimizer or complete-chain quote participates.
        if (AutomaticPrivateSerialInvocation.TrySelectExplicit(request, registered, out var serial)) return serial!;
        var specified = FamilyPlanner.InOrder(registered);
        if (BonesCapsuleAllocation.TrySelect(request, registered, out var bones)) return bones!;
        if (DirectCapsuleAllocation.TrySelect(request, registered, out var capsule)) return capsule!;
        if (RswPhysicalExperiment.TrySelect(request, registered, specified, out var rsw)) return rsw!;
        if (NwaePhysicalExperiment.TrySelect(request, registered, specified, out var nwae)) return nwae!;
        if (PrivateOrdinalSelection.TrySelect(request, registered, specified, out var ordinal)) return ordinal!;
        if (NcPhysicalExperiment.TrySelect(request, registered, specified, out var nc)) return nc!;
        if (EwPhysicalExperiment.TrySelect(request, registered, specified, out var ew)) return ew!;
        return specified;
    }

    public static ProductionSearchSession Start(
        ExactSearchExecutionRequest plan,
        int queueCapacity = 64,
        IRuntimePredictionDiagnosticSink? diagnosticSink = null,
        string traceSingleCandidateSeed = "",
        bool failOnPhysicalRecovery = false,
        bool? gpuAvailable = null,
        SearchContinuationSnapshot? continuation = null,
        bool? gpuFloat64Available = null)
    {
        using var logScope = RuntimeLog.SearchScope(RuntimeLog.SessionId.Length > 0 ? RuntimeLog.SessionId : Guid.NewGuid().ToString("N"));
        // Device eligibility constrains planning. A prior GPU failure does not
        // reject an allocation that can execute entirely on admitted CPU filters.
        FamilyAuxiliaryEvidence.Read(nameof(FamilyExecutionCoordinator), "SaveWorkloadDiagnostic", () =>
        { Infrastructure.Snapshots.ProductionSearchReplay.LogSessionWorkload(plan, RuntimeLog.SessionId); return true; }, false,
            plan.SnapshotFingerprint);
        long started=Stopwatch.GetTimestamp();
        FamilyExecutionPlan executionPlan = Plan(plan, gpuAvailable, gpuFloat64Available);
        FamilyAuxiliaryEvidence.Read(nameof(FamilyExecutionCoordinator), "AllocationDiagnostics", () =>
        {
            if (executionPlan.DecisionEvidence.Any(e => e.StartsWith("GpuFloat64Unavailable;", StringComparison.Ordinal)))
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("GpuFloat64Unavailable",
                    nameof(FamilyExecutionCoordinator), "AllocationSelected", "issues.cpu_for_gpu_numeric_limit",
                    $"reason=ShaderFloat64Unavailable;originalQueryRetained=true;plan={executionPlan.FormatSummary()}");
            foreach (var fallback in executionPlan.GpuPreparationFallbacks)
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("GpuPlanConstructionFailed",
                    fallback.FamilyId, "AllocationSelected", "issues.cpu_after_gpu_preparation_failure",
                    $"{fallback.Scope};plan={executionPlan.FormatSummary()};exception={fallback.Detail}");
            // Deliberate Exact obligations are normal allocation information.
            // Missing/partial filters still warn even with Exact enabled; when
            // Exact is skipped, every remaining obligation needs disclosure.
            var warningRemainders = executionPlan.FilterRemainders.Where(remainder =>
                remainder != PartyFilterScopeInfo && (plan.RunOptions.SkipExactValidation ||
                    !remainder.EndsWith(":ProductionExactOnly", StringComparison.Ordinal))).ToArray();
            if (warningRemainders.Length > 0)
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("SearchFilterIncomplete", nameof(FamilyExecutionCoordinator),
                    "AllocationSelected", plan.RunOptions.SkipExactValidation ? "issues.conditions_unchecked" : "issues.filter_incomplete",
                    $"remainders={string.Join('|', warningRemainders)};finalValidationSkipped={plan.RunOptions.SkipExactValidation};matchClaimed=false;plan={executionPlan.FormatSummary()}");
            var device = FamilyDeviceProfileFoundation.Initialization.Capture();
            if (gpuAvailable != false && device.State == FamilyGpuInitializationState.Failed &&
                !executionPlan.OrderedFamilies.Any(f => f.ConditionPerformance.UsesGpu))
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("GpuInitializationUnavailable",
                    nameof(FamilyExecutionCoordinator), "AllocationSelected", "issues.cpu_after_gpu_failure",
                    $"stage={device.Stage};reason={device.LastFailure?.Reason};details={device.LastFailure?.Details};plan={executionPlan.FormatSummary()}");
            if (executionPlan.AllConditionsUnfiltered && plan.RunOptions.SkipExactValidation)
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("AllConditionsUnfiltered",
                    nameof(FamilyExecutionCoordinator), "AllocationSelected", "issues.raw_seeds", executionPlan.FormatSummary());
            RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=AllocationSelected;elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F4};plan={executionPlan.PlanId};processId={Environment.ProcessId}");
            return true;
        }, false, plan.SnapshotFingerprint);
        IReadOnlyList<IFamilyInvocation> families = executionPlan.OrderedFamilies;
        int executionWindowSize = ResolveExecutionWindowSize(plan, families);
        FamilySearchEtaProjectionV1 eta = FamilySearchEtaProjectorV1.Project(plan, executionPlan);
        return new ProductionSearchSession(
            plan, executionPlan, eta, executionWindowSize, queueCapacity, diagnosticSink, traceSingleCandidateSeed, failOnPhysicalRecovery,
            continuation: continuation, allowInitialGpuCreationFallback: !HasExplicitPhysicalControl());
    }

    internal static int ResolveExecutionWindowSize(ExactSearchExecutionRequest plan, IReadOnlyList<IFamilyInvocation> families)
    {
        if (plan.CompiledSearch.Context.Party is not null)
        {
            if (families.Any(f => f.ConditionPerformance.UsesGpu))
                return FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
            return families.Count == 1 && families[0] is NeowFamily.PartyInitial neow
                ? neow.CpuWindowCapacity : FamilyCpuExecution.Capacity;
        }
        if (families.Count > 0 && families.All(f=>f is FamilyCpuExecution)) return 65536;
        MerchantShopColorlessFamily? shop = families.OfType<MerchantShopColorlessFamily>().SingleOrDefault();
        RelicFamily? relic = families.OfType<RelicFamily>().SingleOrDefault();

        int executionWindowSize = families.Count == 0
            ? Math.Min(ProductionSearchSession.ExactOnlyMaximumBatchSize, Math.Max(256, plan.WorkerCount * 128))
            : FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
        if (shop is not null) executionWindowSize = Math.Min(executionWindowSize, shop.PreferredExecutionWindowSize);
        if (relic is not null) executionWindowSize = Math.Min(executionWindowSize, relic.PreferredExecutionWindowSize);
        // CPU replay chunks its admitted ordinals privately. It must not shrink
        // the root batch of an upstream/downstream GPU in the same linear chain.
        if (!families.Any(family => family.ConditionPerformance.UsesGpu) &&
            families.Any(family => family is StandardMapFamily or NeowFamily or CapsuleRelicFamily or CombatRewardFamily or WorldFamily or AncientOptionFamily or EventResultFamily or TransformationAggregateFamily))
            executionWindowSize = Math.Min(executionWindowSize, FamilyCpuExecution.Capacity);
        return executionWindowSize;
    }

    internal static IReadOnlyList<IFamilyInvocation> CreateRegisteredFamilies(ExactSearchExecutionRequest plan, bool? gpuAvailable = null,
        bool? gpuFloat64Available = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        bool gpu = GpuAvailability(gpuAvailable);
        // Numerical variants describe executable GPU bodies. CPU-only registration
        // retains the native identity independently of backend numeric evidence.
        bool float64 = !gpu || (gpuFloat64Available ?? FamilyDeviceProfileFoundation.Initialization.Capture().CanAttemptFloat64);
        var registered = plan.CompiledSearch.Context.Party is not null
            ? CreatePartyFamilies(plan, gpu, float64)
            : CreateSingleplayerFamilies(plan, gpu, float64);
        if (!gpu || float64) return registered;
        // Inspect actual wrappers/selected bodies, not family letters. In
        // particular an R wrapper may carry an FP64 InitialRooms body.
        var compatible = new List<IFamilyInvocation>();
        foreach (var family in registered)
        {
            if (CanRunWithoutFloat64(family)) { compatible.Add(family); continue; }
            var cpu = FamilyCpuExecution.ConfiguredRealizations(family).FirstOrDefault(CanRunWithoutFloat64);
            if (cpu is not null) compatible.Add(cpu);
            RuntimeLog.TryBackgroundWarning($"filterNumericUnavailable=true;family={family.FamilyId};" +
                $"requirements={family.GpuNumericRequirements};replacement={(cpu is null ? "None" : "ExistingCpuFilter")};" +
                "originalQueryRetained=true;remainingQueryOwner=ProductionExact;filterCompletenessNotClaimed=true");
        }
        return compatible;
    }

    // Whole-query entry only. Party child requests go through the party registrar,
    // which owns shared predicates, slot conjunction and opening continuation.
    private static IReadOnlyList<IFamilyInvocation> CreateSingleplayerFamilies(ExactSearchExecutionRequest plan, bool preferGpu, bool preferGpuFloat64)
    {
        if (plan.Authority.PlayersCount != 1 || plan.Authority.PlayerSlotIndex != 0)
            throw new InvalidOperationException("SingleplayerFamilyRegistrationRequiresSoloContext");
        long started = Stopwatch.GetTimestamp();
        var families = new List<IFamilyInvocation>(4);
        var numericalVariant = preferGpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne;
        if (TransformationAggregateFamily.TryCreate(plan, preferGpu, out var transformation, numericalVariant) && transformation is not null) families.Add(transformation);
        if (NeowFamily.TryCreate(plan, out IFamilyInvocation? neow, preferGpu,
            gpuPlanFactory: preferGpu && !preferGpuFloat64 ? NeowFamilyGpuPlan.TryCreateIntegerUnitFloat : null) && neow is not null) families.Add(neow);
        if (MerchantShopColorlessFamily.TryCreate(plan, out IFamilyInvocation? shopFamily) &&
            shopFamily is MerchantShopColorlessFamily createdShop)
        {
            families.Add(createdShop);
        }
        bool unresolvedRelic = Semantics.TransformationAggregateCondition.HasUnresolvedRelicContinuation(plan.CompiledSearch.NormalizedQuery);
        bool unresolvedReward = Semantics.TransformationAggregateCondition.HasUnresolvedRewardContinuation(plan.CompiledSearch.NormalizedQuery);
        if (unresolvedRelic)
            RuntimeLog.TryBackgroundInfo("relicFilterUnavailable=true;cause=TransformationContinuationUnresolved;remainingQueryOwner=ProductionExact;matchClaimed=false");
        else if (NeowReplayPlan.HasCapsule(plan))
        {
            if (CapsuleRelicFamily.TryCreate(plan, out IFamilyInvocation? capsule, preferGpu, numericalVariant) && capsule is not null)
                families.Add(capsule);
            // Partial R sequence coverage must not impersonate the Capsule R
            // predicate when N requests a conditional projection by Coverage.
        }
        else if (RelicFamily.TryCreate(plan, out IFamilyInvocation? relicFamily, preferGpu, numericalVariant) && relicFamily is not null)
        {
            families.Add(relicFamily);
        }
        if (!unresolvedReward && CombatRewardFamily.TryCreate(plan, out IFamilyInvocation? combat, preferGpu, numericalVariant) && combat is not null)
            families.Add(combat);
        if (unresolvedReward)
            RuntimeLog.TryBackgroundInfo("combatRewardFilterUnavailable=true;cause=TransformationContinuationUnresolved;remainingQueryOwner=ProductionExact;matchClaimed=false");
        if (WorldFamily.TryCreate(plan, out IFamilyInvocation? world, preferGpu, numericalVariant) && world is not null) families.Add(world);
        if (AncientOptionFamily.TryCreate(plan, out IFamilyInvocation? ancient, preferGpu, numericalVariant) && ancient is not null) families.Add(ancient);
        if (EventResultFamily.TryCreate(plan, out IFamilyInvocation? events, preferGpu, numericalVariant) && events is not null) families.Add(events);
        // Unpriced fallback order: bounded card/relic/world sieves precede full map replay.
        // This is not a fabricated calibrated map cost or a change to map semantics.
        if (plan.CompiledSearch.NormalizedQuery.StandardMaps.Count > 0 && plan.ProfileId == RuntimeProfileId.Beta111) families.Add(new StandardMapFamily(plan));
        RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=FamilyInvocationsConstructed;elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F4};families={string.Join(',',families.Select(f=>f.FamilyId))};gpuResourcesCreated=false");
        return families;
    }
}
