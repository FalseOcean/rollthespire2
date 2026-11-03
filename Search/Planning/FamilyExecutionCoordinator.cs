using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
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

    internal static string FamilyOrderKey(FilterBodyDescription family) =>
        string.Join('+', family.Coverage.Order(StringComparer.Ordinal));

    internal static bool IsDenseOnlyFamilyOrderUnit(FilterBodyDescription family) =>
        family.InputModes == FilterInputModes.Dense;

    internal static IReadOnlyList<string> ReconcileFamilyOrder(IReadOnlyList<string> previous,
        IReadOnlyList<FilterBodyDescription> currentUnits)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < previous.Count; index++)
            foreach (string coverage in previous[index].Split('+')) ranks.TryAdd(coverage, index);
        // V inherits the position of the duties previously carried by W. An
        // authored V position is already authoritative and must not move.
        bool inheritedVariant = !ranks.ContainsKey(VariantOpeningFamily.Id);
        if (inheritedVariant)
        {
            int inherited = Math.Min(ranks.GetValueOrDefault("W.World", int.MaxValue),
                ranks.GetValueOrDefault(InitialRoomsInvocation.UnknownCoverage, int.MaxValue));
            if (inherited != int.MaxValue) ranks.Add(VariantOpeningFamily.Id, inherited);
        }
        return currentUnits.Select((unit, index) => (Unit: unit, Index: index,
                Rank: unit.Coverage.Select(coverage => ranks.GetValueOrDefault(coverage, int.MaxValue)).Min()))
            .OrderBy(item => item.Rank)
            .ThenBy(item => inheritedVariant && item.Unit.Coverage.Contains(VariantOpeningFamily.Id) ? 0 : 1)
            .ThenBy(item => item.Index)
            .Select(item => FamilyOrderKey(item.Unit)).ToArray();
    }

    internal static FamilyExecutionPlan Plan(PreparedSearch plan, bool? gpuAvailable = null,
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
            bool mapCpuEquivalent = false;
            if (plan.RunOptions.SkipExactValidation || explicitControl)
            {
                var original = Plan(plan, gpuAvailable: gpu, gpuFloat64Available: true);
                if (original.OrderedFamilies.All(CanRunWithoutFloat64)) return original;
                var affected = original.ExecutionOrderUnits.Where(f => !CanRunWithoutFloat64(f)).ToArray();
                // M uses the identical Round1 proposal and full Final predicate on
                // CPU. This physical substitution needs no extra Exact authority.
                mapCpuEquivalent = !explicitControl && affected.Length > 0 && affected.All(f => f.FamilyId == StandardMapFamily.Id);
            }
            // Fused route-aware GPU filters can reject more than separate CPU
            // sieves. Only the unchanged final Exact can establish equivalence.
            if (plan.RunOptions.SkipExactValidation && !mapCpuEquivalent)
                throw new InvalidOperationException("GpuFloat64UnavailableWithExactSkipped:ExistingCpuFiltersRequireProductionExact");
            if (explicitControl)
                throw new InvalidOperationException("GpuFloat64PhysicalSelectionUnavailable:RequestedTrialRequiresUnavailableNumericImplementations");
        }
        FamilyExecutionPlan selected;
        if (plan.Model.CompiledSearch.Context.Party is not null) selected = PlanParty(plan, registered);
        else selected = PlanSingleplayer(plan, registered, gpu, allowFloat64Allocations: !numericRestriction);
        if (numericRestriction && selected.OrderedFamilies.Any(f => !CanRunWithoutFloat64(f)))
            throw new InvalidOperationException("GpuNumericRequirementsViolation:RestrictedPlannerSelectedUnavailableBody");
        if (numericRestriction)
            selected = selected with { DecisionEvidence = [..selected.DecisionEvidence,
                "GpuFloat64Unavailable;integerGpuPathsRetained=true;" +
                $"selectedGpu={string.Join(',', selected.OrderedFamilies.Where(f => f.ConditionPerformance.UsesGpu).Select(f => f.FamilyId))};" +
                $"selectedCpu={string.Join(',', selected.OrderedFamilies.Where(f => !f.ConditionPerformance.UsesGpu).Select(f => f.FamilyId))};" +
                (plan.RunOptions.SkipExactValidation ? "remainingQueryOwner=UnverifiedOutput;FinalValidationSkipped=true" : "remainingQueryOwner=ProductionExact")] };
        bool unfiltered = selected.Stages.Count == 0 && FamilyExactTimingDomains.HasQueryWork(plan.Model.CompiledSearch.NormalizedQuery);
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
    private static bool CanRunWithoutFloat64(FilterBodyDescription family) =>
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

    private static IReadOnlyList<string> DescribeFilterRemainders(PreparedSearch request,
        FamilyExecutionPlan selected, IReadOnlyList<FilterBodyDescription> registered)
    {
        var model = request.Model;
        bool party = model.CompiledSearch.Context.Party is not null;
        var expected = new List<FilterObligation>();
        void Need(bool required, string domain, int? slot = null, int? through = null)
        { if (required) expected.Add(new(domain, slot, through)); }
        void Personal(SearchModel child, int? slot)
        {
            var e = child.Evaluation;
            Need(e.HasNeowConstraints, "N.Neow", slot);
            Need(e.RelicSequenceConditions.Any(c => !c.IsEmpty) || e.RelicShopSequenceConditions.Any(c => !c.IsEmpty) ||
                NeowReplayPlan.HasCapsule(child), "R.Relic", slot);
            Need(e.RequiresNormalCombatRewardDomain, "C.CombatReward", slot);
            Need(e.CombatCardExclusion is { IsEmpty: false }, "CombatCardExclusion", slot);
            Need(e.TreasureRelics is { IsEmpty: false }, InitialRoomsInvocation.TreasureCoverage, slot);
            Need(e.AncientBranchConditions.Any(b => b.OptionAny.Count > 0 || b.SeaGlassTargetAny.Count > 0) ||
                e.AncientOptionFilters.Any(f => !f.IsEmpty) || e.AncientSeaGlassTargetFilters.Any(f => !f.IsEmpty), "A.AncientOption", slot);
            Need(e.RequiresEventResultDomain, "E.EventResult", slot);
            Need(e.RequiresMerchantColorlessDomain, "S.MerchantShopColorless", slot);
            Need(e.TransformationAggregate is not null, TransformationAggregateFamily.Id, slot);
            Need(e.FirstActUnknownEventPrefixCount > 0, InitialRoomsInvocation.UnknownCoverage, slot, e.FirstActUnknownEventPrefixCount);
        }
        if (party)
        {
            var players = model.CompiledSearch.NormalizedQuery.Players;
            foreach (var compiled in model.CompiledSearch.PlayerSearches.Take(players.Count))
            {
                var child = SearchPreparationCompiler.ForPlayer(model, compiled);
                int slot = child.Authority.PlayerSlotIndex;
                Personal(child, slot);
                Need(NeowFamily.PartyInitial.HasConditions(players[slot]), "N.Neow", slot);
            }
            int prefix = model.CompiledSearch.NormalizedQuery.FirstActUnknownEventPrefixCount;
            Need(prefix > 0, InitialRoomsInvocation.UnknownCoverage, through: prefix);
        }
        else Personal(model, null);
        Need(WorldFamilyReplay.HasConditions(model), "W.World");
        Need((model.VariantOpening ?? VariantOpeningPreparation.Prepare(model.CompiledSearch)).HasVariantRestrictions, VariantOpeningFamily.Id);
        var query = model.CompiledSearch.NormalizedQuery;
        Need(query.StandardMaps.Count > 0 || query.StandardMapRoutes.Count > 0, "M.StandardMap");
        var bodies = selected.OrderedFamilies;
        var covered = bodies.SelectMany(body => body.Preparation.EffectiveObligations).ToArray();
        bool Covers(FilterObligation obligation) => covered.Any(c => c.Domain == obligation.Domain &&
            (!party || c.PlayerSlot == obligation.PlayerSlot) &&
            (!obligation.ThroughOrdinal.HasValue || c.ThroughOrdinal >= obligation.ThroughOrdinal));
        string Scope(FilterObligation obligation) => party ? obligation.PlayerSlot is int slot ? $":slot={slot}" : ":Shared" : "";
        string Label(string domain) => domain == InitialRoomsInvocation.TreasureCoverage ? "TreasureRelics" :
            !party && domain == InitialRoomsInvocation.UnknownCoverage ? "FirstActUnknownEventPrefix" :
            !party && domain == VariantOpeningFamily.Id ? "ActVariants" : domain;
        var missing = expected.Distinct().Where(e => !Covers(e)).ToArray();
        var remainder = missing.Select(e => Label(e.Domain) + Scope(e) +
            (e.Domain is "CombatCardExclusion" or InitialRoomsInvocation.TreasureCoverage or InitialRoomsInvocation.UnknownCoverage or VariantOpeningFamily.Id
                ? ":ProductionExactOnly" : ":NoSelectedFilter")).ToList();
        // The retained final duty and the absent filtering unit are separate
        // facts. A partial V may still check Unknown rooms and report its own
        // variant cause; only an entirely absent V gets NoSelectedFilter.
        if (!bodies.Any(body => body.Coverage.Contains(VariantOpeningFamily.Id)))
            remainder.AddRange(missing.Where(e => e.Domain == VariantOpeningFamily.Id)
                .Select(e => VariantOpeningFamily.Id + Scope(e) + ":NoSelectedFilter"));
        foreach (var body in Descendants(bodies))
        {
            remainder.AddRange(body.Preparation.Remainders.Select(r => body.Coverage.Any(domain =>
                r.StartsWith(domain + ":", StringComparison.Ordinal)) ? r : body.FamilyId + ":" + r));
            remainder.AddRange(body.Preparation.Issues.Where(i => !IsGpuPreparationIssue(i))
                .Select(i => body.FamilyId + ":" + i.Cause));
        }
        if (party && expected.Count > 0) remainder.Add(PartyFilterScopeInfo);
        return remainder.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsGpuPreparationIssue(FilterPreparationIssue issue) =>
        issue.Stage is "GpuPreparation" or "GpuPlanConstruction";

    private static IEnumerable<FilterBodyDescription> Descendants(IEnumerable<FilterBodyDescription> bodies)
    {
        foreach (var body in bodies)
        {
            yield return body;
            foreach (var child in Descendants(body.PhysicalBodies)) yield return child;
        }
    }

    internal static IReadOnlyList<FamilyGpuPreparationFallback> SelectedPreparationFallbacks(
        IReadOnlyList<FilterBodyDescription> registered, FamilyExecutionPlan selected)
    {
        var selectedCpu = Descendants(selected.OrderedFamilies).Where(b => !b.UsesGpu).ToArray();
        return Descendants(registered).SelectMany(body => body.Preparation.Issues
            .Where(issue => IsGpuPreparationIssue(issue) && selectedCpu.Any(cpu =>
                cpu.FamilyId == body.FamilyId && cpu.InputIdentity == body.InputIdentity &&
                body.Coverage.All(cpu.Coverage.Contains)))
            .Select(issue => new FamilyGpuPreparationFallback(body.FamilyId,
                $"coverage={string.Join(',', body.Coverage)};slots={string.Join(',', body.Preparation.Obligations.Select(o => o.PlayerSlot).Where(slot => slot.HasValue).Distinct())};replacement=CpuExistingFilter;remainingQueryOwner=ProductionExact", issue.Detail)))
            .Distinct().ToArray();
    }
    private static bool GpuAvailability(bool? requested)
    {
        if (requested.HasValue) return requested.Value;
        var snapshot = FamilyDeviceProfileFoundation.Initialization.Capture();
        return snapshot.GpuEligible && snapshot.State != FamilyGpuInitializationState.Creating &&
            FamilyDeviceProfileFoundation.RetryTask is not { IsCompleted: false };
    }
    private static FamilyExecutionPlan PlanSingleplayer(PreparedSearch request,
        IReadOnlyList<FilterBodyDescription> registered, bool gpu, bool allowFloat64Allocations = true)
    {
        RuntimeLog.TryBackgroundInfo($"searchPhysicalAvailability=true;gpuAvailable={gpu};cpuQuotes=UnitReferences;unpricedNotUnsupported=true");
        if (HasExplicitPhysicalControl())
        {
            if (request.RunOptions.ManualFamilyOrder is { Count: > 0 })
                throw new InvalidOperationException("ManualFamilyOrder.ConflictsWithExplicitPhysicalControl");
            if (!gpu && HasLocalGpuPhysicalControl())
                throw new InvalidOperationException("ExplicitPhysicalSelectionGpuUnavailable:RequestedGpuNumericalBodyUnavailable");
            var trial = PlanExplicitPhysicalTrial(request, registered);
            if (!gpu && trial.OrderedFamilies.Any(f => f.ConditionPerformance.UsesGpu))
                throw new InvalidOperationException("ExplicitPhysicalSelectionGpuUnavailable:RequestedGpuNumericalBodyUnavailable");
            return trial with { DecisionEvidence = [..trial.DecisionEvidence, ExplicitPhysicalSelection] };
        }
        return PlanSimpleSingleplayer(request, registered, gpu, allowFloat64Allocations);
    }
    private static IReadOnlyList<FilterBodyDescription> CpuAllocation(IReadOnlyList<FilterBodyDescription> registered)
    {
        var selected = new List<FilterBodyDescription>();
        foreach (var family in registered)
        {
            var cpu = family.CpuAlternatives.FirstOrDefault(c => c.CpuWorkers == family.CpuWorkers) ?? family.CpuAlternatives.FirstOrDefault();
            if (cpu is not null) selected.Add(cpu);
            else if (!family.ConditionPerformance.UsesGpu) selected.Add(family);
            else RuntimeLog.TryBackgroundWarning($"filterPhysicalUnavailable=true;family={family.FamilyId};requested=CPU;replacement=None;remainingQueryOwner=ProductionExact;replacementCompleteness=NotClaimed");
        }
        return selected;
    }
    private static FamilyExecutionPlan PlanExplicitPhysicalTrial(PreparedSearch request,
        IReadOnlyList<FilterBodyDescription> registered)
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
        PreparedSearch plan,
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
        IReadOnlyList<FilterBodyDescription> families = executionPlan.OrderedFamilies;
        int executionWindowSize = ResolveExecutionWindowSize(plan, families);
        var unavailableEta = new FamilySearchEtaProjectionV1(FamilySearchEtaStatus.Unavailable, null,
            "EtaPreparationFailed", executionPlan, Math.Max(1, plan.TargetMatchCount), plan.ScanCount,
            null, null, null, null, null, null, null, null, null, null, null, [], ["EtaPreparationFailed"]);
        FamilySearchEtaProjectionV1 eta = FamilyAuxiliaryEvidence.Read(nameof(FamilyExecutionCoordinator), "EtaPreparation",
            () => FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(plan, executionPlan), executionPlan),
            unavailableEta, plan.SnapshotFingerprint);
        return new ProductionSearchSession(
            plan, executionPlan, eta, executionWindowSize, queueCapacity, diagnosticSink, traceSingleCandidateSeed, failOnPhysicalRecovery,
            continuation: continuation, allowInitialGpuCreationFallback: !HasExplicitPhysicalControl());
    }

    internal static int ResolveExecutionWindowSize(PreparedSearch plan, IReadOnlyList<FilterBodyDescription> families)
    {
        if (families.Count == 0)
            return plan.Model.CompiledSearch.Context.Party is not null ? CpuFilterExecution.Capacity :
                Math.Min(ProductionSearchSession.ExactOnlyMaximumBatchSize, Math.Max(256, plan.WorkerCount * 128));
        bool gpu = families.Any(f => f.UsesGpu);
        // A public root preference is distinct from private CPU chunks and GPU
        // groups. CPU-only bodies may use their own declared window when no
        // smaller public boundary was selected.
        int fallback = gpu ? FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize : families.Min(f => f.WindowCapacity);
        return families.Where(f => !gpu || f.UsesGpu).Select(f => f.PreferredRootWindowSize).Where(size => size.HasValue)
            .Select(size => size!.Value).Append(fallback).Min();
    }

    internal static IReadOnlyList<FilterBodyDescription> CreateRegisteredFamilies(PreparedSearch plan, bool? gpuAvailable = null,
        bool? gpuFloat64Available = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        bool gpu = GpuAvailability(gpuAvailable);
        bool float64 = !gpu || (gpuFloat64Available ?? FamilyDeviceProfileFoundation.Initialization.Capture().CanAttemptFloat64);
        var preferences = FilterExecutionPreferences.From(plan.RunOptions);
        var registered = plan.Model.CompiledSearch.Context.Party is not null
            ? CreatePartyFamilies(plan.Model, preferences, gpu, float64)
            : CreateSingleplayerFamilies(plan.Model, preferences, gpu, float64);
        if (!gpu || float64) return registered;
        var compatible = new List<FilterBodyDescription>();
        foreach (var body in registered)
        {
            if (CanRunWithoutFloat64(body)) { compatible.Add(body); continue; }
            var cpu = body.CpuAlternatives.FirstOrDefault(c => c.CpuWorkers == body.CpuWorkers && CanRunWithoutFloat64(c)) ?? body.CpuAlternatives.FirstOrDefault(CanRunWithoutFloat64);
            if (cpu is not null) compatible.Add(cpu);
            RuntimeLog.TryBackgroundWarning($"filterNumericUnavailable=true;family={body.FamilyId};" +
                $"requirements={body.NumericRequirements};replacement={(cpu is null ? "None" : "ExistingCpuFilter")};" +
                "originalQueryRetained=true;remainingQueryOwner=ProductionExact;filterCompletenessNotClaimed=true");
        }
        return compatible;
    }

    private static IReadOnlyList<FilterBodyDescription> CreateSingleplayerFamilies(SearchModel model,
        FilterExecutionPreferences preferences, bool preferGpu, bool preferGpuFloat64)
    {
        if (model.Authority.PlayersCount != 1 || model.Authority.PlayerSlotIndex != 0)
            throw new InvalidOperationException("SingleplayerFamilyRegistrationRequiresSoloContext");
        long started = Stopwatch.GetTimestamp();
        var families = new List<FilterBodyDescription>();
        var numerical = preferGpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne;
        if (TransformationAggregateFamily.TryCreate(model, preferences, out var transformation, preferGpu, numerical) && transformation is not null)
            families.Add(transformation);
        if (NeowFamily.TryCreate(model, preferences, out var neow, preferGpu,
            gpuPlanFactory: preferGpu && !preferGpuFloat64 ? NeowFamilyGpuPlan.TryCreateIntegerUnitFloat : null) && neow is not null)
            families.Add(neow);
        if (MerchantShopColorlessFamily.TryCreate(model, preferences, out var shop, preferGpu) && shop is not null) families.Add(shop);
        bool unresolvedRelic = Semantics.TransformationAggregateCondition.HasUnresolvedRelicContinuation(model.CompiledSearch.NormalizedQuery);
        bool unresolvedReward = Semantics.TransformationAggregateCondition.HasUnresolvedRewardContinuation(model.CompiledSearch.NormalizedQuery);
        FilterBodyDescription? relic = null;
        if (!unresolvedRelic)
        {
            if (NeowReplayPlan.HasCapsule(model)) CapsuleRelicFamily.TryCreate(model, preferences, out relic, preferGpu, numerical);
            else RelicFamily.TryCreate(model, preferences, out relic, preferGpu, numerical);
        }
        else RuntimeLog.TryBackgroundInfo("relicFilterUnavailable=true;cause=TransformationContinuationUnresolved;remainingQueryOwner=ProductionExact;matchClaimed=false");
        // Treasure is an independent bounded predicate. Ordinary-R failure must
        // not discard it or substitute an unrelated continuation.
        if (InitialRoomsPlan.TryCompileTreasure(model, numerical) is { } treasure)
            relic = InitialRoomsInvocation.Prepare(model, preferences, "R.Relic", treasure, relic, preferGpu);
        if (relic is not null) families.Add(relic);
        if (!unresolvedReward && CombatRewardFamily.TryCreate(model, preferences, out var combat, preferGpu, numerical) && combat is not null)
            families.Add(combat);
        if (unresolvedReward) RuntimeLog.TryBackgroundInfo("combatRewardFilterUnavailable=true;cause=TransformationContinuationUnresolved;remainingQueryOwner=ProductionExact;matchClaimed=false");
        VariantOpeningFamily.TryPrepare(model, preferences, out var variant, preferGpu, numerical);
        if (variant is not null) families.Add(variant.Body);
        WorldFamily.TryCreate(model, preferences, out var world, preferGpu, numerical, variant?.Plan.CheckedPreparation);
        int coveredPrefix = variant?.Plan.UnknownPrefixCount ?? 0;
        if (coveredPrefix < model.Evaluation.FirstActUnknownEventPrefixCount && InitialRoomsPlan.TryCompileUnknown(model, numerical) is { } unknown)
            world = InitialRoomsInvocation.Prepare(model, preferences, "W.World", unknown, world, preferGpu);
        if (world is not null) families.Add(world);
        if (AncientOptionFamily.TryCreate(model, preferences, out var ancient, preferGpu, numerical) && ancient is not null) families.Add(ancient);
        if (EventResultFamily.TryCreate(model, preferences, out var events, preferGpu, numerical) && events is not null) families.Add(events);
        if ((model.CompiledSearch.NormalizedQuery.StandardMaps.Count > 0 || model.CompiledSearch.NormalizedQuery.StandardMapRoutes.Count > 0) &&
            model.ProfileId == RuntimeProfileId.Beta111)
            families.Add(StandardMapFamily.Prepare(model, preferences, preferGpu, preferGpuFloat64));
        RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=FamilyDescriptionsPrepared;elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F4};families={string.Join(',',families.Select(f=>f.FamilyId))};gpuResourcesCreated=false");
        return families;
    }
}
