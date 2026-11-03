using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// V owns its short numerical preparation. Runtime resources and cost observations
// are bound only after a physical body is selected.
internal sealed record VariantOpeningPreparedBody(VariantOpeningPlan Plan, FilterBodyDescription Body);

internal static class VariantOpeningFamily
{
    internal const string Id = "V.VariantOpening";
    private const string CpuRevision = "V.VariantOpening.Cpu.DeclaredOpeningJuzu.20261029.v2";

    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        bool accepted = TryPrepare(model, preferences, out var prepared, preferGpu, numericalVariant);
        body = prepared?.Body;
        return accepted;
    }

    internal static bool TryPrepare(SearchModel model, FilterExecutionPreferences preferences,
        out VariantOpeningPreparedBody? result, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        result = null;
        FilterBodyDescription? body;
        var preparation = model.VariantOpening ?? VariantOpeningPreparation.Prepare(model.CompiledSearch);
        if (!preparation.HasConditions || VariantOpeningPlan.TryCompile(model, preparation, numericalVariant) is not { } plan)
            return false;
        double? unknown = plan.UnknownPrefixCount == 0 ? 1 : plan.UnknownPrefixCount is >= 1 and <= 3
            ? FirstActUnknownEventProbability.ConditionalPrefix(plan.UnknownPrefixCount, plan.UnknownJuzuAcquired) : null;
        var conditional = PrepareConditionalSurvival(model, plan);
        FamilySurvivalProjection Survival(IReadOnlySet<string> passed) => conditional.ResolveVariant(passed, unknown);
        var obligations = new List<FilterObligation>();
        if (plan.CoversVariants) obligations.Add(new(Id));
        if (plan.UnknownPrefixCount > 0)
        {
            var query = model.CompiledSearch.NormalizedQuery;
            if (query.FirstActUnknownEventPrefixCount > 0)
                obligations.Add(new(InitialRoomsInvocation.UnknownCoverage, ThroughOrdinal: plan.UnknownPrefixCount));
            obligations.AddRange(query.Players.Where(p => p.Conditions.FirstActUnknownEventPrefixCount > 0)
                .Select(p => new FilterObligation(InitialRoomsInvocation.UnknownCoverage, p.Slot, plan.UnknownPrefixCount)));
        }
        var prepared = new PreparedFilter(Id)
        {
            AnalyticalCost = new(Id, preferGpu ? VariantOpeningGpuPlan.WindowCapacity : 65536, [],
                "V.ShortSelectionAndOrdinaryUnknown;PhysicalWorkEstimateAvailable"),
            Survival = Survival(new HashSet<string>(StringComparer.Ordinal)), HasConditionalProjections = true,
            SurvivalResolver = Survival, Obligations = obligations,
            Issues = string.IsNullOrEmpty(plan.VariantUnavailableReason) ? [] :
                [new("VariantPreparation", plan.VariantUnavailableReason)]
        };
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var profileId = model.ProfileId;
        int players = model.Authority.PlayersCount, workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        string identity = model.SnapshotFingerprint;
        bool numericalRoots = profileId == RuntimeProfileId.Beta111;
        var cpus = Enumerable.Range(1, workers).Select(n => new FilterBodyDescription(prepared, CpuRevision, profileId)
        {
            WindowCapacity = 65536, PreferredRootWindowSize = players > 1 ? CpuFilterExecution.Capacity : null,
            CpuWorkers = n, PlayersCount = players, InputIdentity = identity,
            PerformanceProjection = compact => CpuFilterExecution.Describe(Id, CpuRevision, n, 65536, numericalRoots, false, compact),
            Cost = new() { Quote = (_, geometry, passed) => Quote(plan, geometry, false, n, numericalVariant),
                CalibrationQuote = geometry => Quote(plan, geometry, false, n, numericalVariant) },
            CreateExecution = (self, _) => new CpuFilterExecution(self, profile, plan.Matches, numericalRoots)
        }).ToArray();
        if (!preferGpu) { body = cpus[^1] with { CpuAlternatives = cpus }; result = new(plan, body); return true; }
        var gpu = new VariantOpeningGpuPlan(plan, numericalVariant);
        body = new(prepared, gpu.Revision(false), profileId)
        {
            UsesGpu = true, WindowCapacity = gpu.Capacity * gpu.GroupingK, CpuWorkers = workers,
            PlayersCount = players, InputIdentity = identity, NumericRequirements = gpu.NumericRequirements,
            CpuAlternatives = cpus,
            PerformanceProjection = compact => new(Id, gpu.Revision(compact), "V.Neutral.20261028.v1", 1,
                "ActualNecessaryVariantAndAdmittedOrdinaryUnknown;CanonicalAbi1Ready;EstimateOnly", true),
            Cost = new() { MinimumComparableInput = _ => gpu.Capacity * gpu.GroupingK,
                Quote = (_, geometry, passed) => Quote(plan, geometry, true, workers, numericalVariant),
                CalibrationQuote = geometry => Quote(plan, geometry, true, workers, numericalVariant) },
            BindPrivate = numericalVariant == GpuNumericalVariant.NativeFloat64
                ? (device, input, output) => Bind(gpu, device, input, output) : null,
            CreateExecution = (self, _) => new GpuFilterExecution<WorldFamilyGpuExecutor>(self,
                device => new(device, gpu), (resource, input, token) =>
                {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalMs, metrics.DispatchSyncMs, metrics.HeaderReadbackMs,
                        metrics.HeaderReadBytes + metrics.PayloadReadBytes);
                }, resource => resource.Dispose(), plan.Matches, profile,
                resource => (resource.Device, resource.SetupMs), paritySample: 4096)
        };
        RuntimeLog.TryBackgroundInfo($"variantOpeningFamilyReady=true;family={Id};variantGroups={plan.Groups.Length};selectionDraws={plan.SelectionDraws};unknownPrefix={plan.UnknownPrefixCount};declaredJuzu={plan.UnknownJuzuAcquired};unknownFirst={plan.UnknownFirst};variantUnavailableReason={plan.VariantUnavailableReason};gpu=True;coverage=ActualNecessaryChecks;recovery=None");
        result = new(plan, body);
        return true;
    }

    private static VariantOpeningWorldSurvival PrepareConditionalSurvival(SearchModel model, VariantOpeningPlan plan) =>
        FamilyAuxiliaryEvidence.Read(Id, "WorldConditionalProjection",
            () => VariantOpeningWorldSurvival.Create(model, plan.CheckedPreparation,
                WorldFamilyReplay.HasConditions(model) ? WorldFamilyReplay.Compile(model) : null),
            new VariantOpeningWorldSurvival(plan.CoversVariants ? null : 1, null, null,
                !plan.CoversVariants, "V.AuxiliaryWorldProjectionFailed"), model.SnapshotFingerprint);

    private static FamilyPhysicalQuote? Quote(VariantOpeningPlan plan, FamilyPhysicalQuoteRequest geometry,
        bool gpu, int workers, GpuNumericalVariant numericalVariant)
    {
        // One root hash and one output. The second short predicate adds only its
        // own draws/comparisons, preserving the existing single-call envelope.
        double draws = plan.SelectionDraws, comparisons = plan.VariantComparisons;
        double ns = plan.CoversVariants
            ? gpu ? .62 + .08 * Math.Max(0, draws - 1) + .02 * comparisons : 55 + 8 * Math.Max(0, draws - 1) + 2 * comparisons
            : gpu ? .12 : 30;
        ns += (gpu ? .04 : 12) * plan.UnknownPrefixCount;
        if (!gpu && geometry.CompactInput) ns += 17;
        string evidence = $"V.ShortWork;Variant062nsCpu55nsAndInitialUnknownExistingEnvelopes;SelectionDraws={draws};VariantComparisons={comparisons};UnknownPrefix={plan.UnknownPrefixCount};DeclaredJuzu={plan.UnknownJuzuAcquired};UnknownFirst={plan.UnknownFirst};OneHashAndOutput;NoWorldProgression;NoNewMeasurement;NumericalVariant={numericalVariant}";
        return gpu ? FamilyCoarsePricing.Gpu(Id, "V.ShortSelectionUnknown", ns,
            VariantOpeningGpuPlan.WindowCapacity * 4, geometry, evidence) :
            FamilyCoarsePricing.Cpu(Id, "V.ShortSelectionUnknown", ns, workers, geometry, evidence);
    }

    private static FamilyPrivateGpuExecution Bind(VariantOpeningGpuPlan plan, RenderingDevice device,
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
