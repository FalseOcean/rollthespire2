using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// W preparation owns domain inputs and prices. Selected executions separately
// own CPU workspaces and lazy GPU resources; neither retains a Search model.
internal static partial class WorldFamily
{
    internal const string Id = "W.World";
    internal const string CpuRevision = "W.World.Cpu.Progression.20261028.v3";

    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64,
        VariantOpeningPreparation? checkedVariants = null)
    {
        body = null;
        if (!WorldFamilyReplay.HasConditions(model)) return false;
        var plan = WorldFamilyReplay.Compile(model);
        if (!plan.Enabled)
        {
            RuntimeLog.TryBackgroundInfo($"worldFamilyInapplicable=true;reason={plan.DisableReason};selectionTime=true;predicatesRetainedByExact=true;replacementCompleteness=NotClaimed");
            if (RuntimeLog.SessionId.Length > 0)
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("WorldFilterUnavailable",
                    nameof(WorldFamily), "Filter.Construction", "issues.filter_incomplete",
                    $"cause={plan.DisableReason};predicatesRetained=true;finalValidationSkipped={preferences.SkipExactValidation};matchClaimed=false");
            return false;
        }
        var replay = new WorldFamilyReplay(model, plan);
        var survival = PrepareSurvival(model);
        var variants = model.VariantOpening ?? VariantOpeningPreparation.Prepare(model.CompiledSearch);
        var conditional = variants.HasConditions
            ? PrepareConditionalSurvival(model, checkedVariants ?? variants with { Acts = [] }, replay.Plan) : null;
        var issues = new List<FilterPreparationIssue>();
        WorldFamilyGpuPlan? gpu = null;
        if (preferGpu && WorldFamilyGpuPlan.Supports(replay))
            try { gpu = new(replay, model.Authority.PlayersCount == 1 &&
                model.Authority.WorldAuthority?.Beta109Generation is { IsMultiplayer: false }, numericalVariant); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                issues.Add(new("GpuPreparation", "WorldGpuPreparationFailed", error.ToString()));
                RuntimeLog.TryBackgroundWarning("worldGpuPreparationFailed=true;selected=CpuProgression;exceptionJson=" + RuntimeLog.SafeJson(error.ToString()));
            }
        var prepared = new PreparedFilter(Id)
        {
            AnalyticalCost = new(Id, gpu?.Capacity ?? 65536, [], "W.ProgressionCostUnresolved"),
            Survival = conditional?.ResolveWorld(new HashSet<string>(StringComparer.Ordinal)) ?? survival,
            HasConditionalProjections = conditional is not null,
            SurvivalResolver = conditional is null ? null : conditional.ResolveWorld,
            Issues = issues,
            Remainders = WorldFamilyReplay.EncounterRemainders(model)
        };
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var authority = FilterPricingAuthority.From(model);
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        string identity = model.SnapshotFingerprint;
        var cpu = Enumerable.Range(1, workers).Select(n => PrepareCpu(prepared, replay, gpu,
            profile, authority, identity, n)).ToArray();
        if (gpu is null) { body = cpu[^1] with { CpuAlternatives = cpu }; return true; }
        var gpuPlan = gpu;
        body = new(prepared, gpuPlan.Revision(false), model.ProfileId)
        {
            UsesGpu = true, WindowCapacity = gpuPlan.Capacity * gpuPlan.GroupingK, CpuWorkers = workers,
            PlayersCount = authority.PlayersCount, InputIdentity = model.SnapshotFingerprint,
            NumericRequirements = gpuPlan.NumericRequirements, CpuAlternatives = cpu,
            PerformanceProjection = compact => new(Id, gpuPlan.Revision(compact),
                gpuPlan.NumericalVariant == GpuNumericalVariant.IntegerRne ? "W.IntegerRne.20261025.v1" : "W.Neutral.20260907.v1", 1,
                gpuPlan.NumericalVariant == GpuNumericalVariant.IntegerRne ? "IntegerRne;NoAcceptedPhysicalPrice;CanonicalAbi1Ready" :
                    "NoAcceptedWithinPathCurve;CanonicalAbi1Ready", true),
            Cost = new()
            {
                CanonicalScope = true,
                MinimumComparableInput = _ => gpuPlan.Capacity * gpuPlan.GroupingK,
                PartyChild = new(1 + Math.Max(0, replay.Plan.MaxRequiredAct - 2) * .5 + replay.Plan.Acts.Sum(a => a.EventPredicates.Length) * .25,
                    numericalVariant == GpuNumericalVariant.NativeFloat64,
                    NumericalRevision: numericalVariant == GpuNumericalVariant.NativeFloat64 ? "" : ".IntegerRne.20261025.v1"),
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeWorld(replay, gpuPlan),
                Quote = (self, geometry, _) => FamilyGpuReadback.Reference(self,
                    FamilyCanonicalMeasuredCost.TryDenseQuote(self, geometry) ??
                    WorldPhysicalPricing.Quote(replay, gpuPlan, geometry) ?? WorldWorkEstimate.Gpu(replay, gpuPlan, geometry)),
                CalibrationQuote = geometry => WorldPhysicalPricing.Quote(replay, gpuPlan, geometry) ?? WorldWorkEstimate.Gpu(replay, gpuPlan, geometry)
            },
            BindPrivate = gpuPlan.NumericalVariant == GpuNumericalVariant.NativeFloat64
                ? (device, input, output) => Bind(gpuPlan, device, input, output) : null,
            CreateExecution = (self, _) => new GpuFilterExecution<WorldFamilyGpuExecutor>(self,
                device => new(device, gpuPlan), (resource, input, token) =>
                {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalMs, metrics.DispatchSyncMs, metrics.HeaderReadbackMs,
                        metrics.HeaderReadBytes + metrics.PayloadReadBytes);
                }, resource => resource.Dispose(), replay.Matches, profile,
                resource => (resource.Device, resource.SetupMs), paritySample: 4096)
        };
        RuntimeLog.TryBackgroundInfo($"worldFamilyReady=true;family={Id};physicalRevision={body.ConditionPerformance.PhysicalImplementationRevision};actHorizon={replay.Plan.MaxRequiredAct};finalHorizon={replay.Plan.RequiredFarthestStage};groupingK={gpuPlan.GroupingK};groupingWorkspaceBytes={gpuPlan.GroupingWorkspaceBytes};capacity={gpuPlan.Capacity};workspaceBytes={gpuPlan.WorkspaceBytes};scratchBytes={gpuPlan.ScratchBytes};recovery=None");
        return true;
    }

    // Auxiliary reads finish during preparation. Their closures cannot be
    // retained by the runtime factories below through a shared display class.
    private static FamilySurvivalProjection PrepareSurvival(SearchModel model) =>
        FamilyAuxiliaryEvidence.Read(Id, "InitialSurvival", () => WorldPhysicalPricing.ResolveSurvival(model),
            FamilySurvivalProjection.Unresolved(Id, "AuxiliarySurvivalFailed"), model.SnapshotFingerprint);

    private static VariantOpeningWorldSurvival? PrepareConditionalSurvival(SearchModel model,
        VariantOpeningPreparation variants, Beta110WorldFastPlan plan) =>
        FamilyAuxiliaryEvidence.Read<VariantOpeningWorldSurvival?>(Id, "VariantConditionalSurvival",
            () => VariantOpeningWorldSurvival.Create(model, variants, plan),
            new(null, null, null, false, "W.ActualConditionalProjectionFailed"), model.SnapshotFingerprint);

    private static FamilyPrivateGpuExecution Bind(WorldFamilyGpuPlan plan, RenderingDevice device,
        PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output)
    {
        var gpu = new WorldFamilyGpuExecutor(device, plan, input?.Buffer, output);
        return new(gpu, gpu.Device, gpu.SetupMs, input, output, (batch, count, token) =>
        {
            FamilyCandidateSet? result = null; WorldFamilyGpuMetrics metrics;
            if (output is not null) gpu.ExecutePrivateStage(batch, count, token, out metrics);
            else result = input is null ? gpu.Execute(FamilyCandidateSet.Dense(batch), token, out metrics) :
                gpu.ExecutePrivate(batch, count, token, out metrics);
            return new(metrics.Output, result, metrics.DispatchSyncMs, metrics.HeaderReadbackMs,
                metrics.HeaderReadBytes + metrics.PayloadReadBytes, metrics.CanonicalMs, metrics.Dispatches);
        });
    }
}
