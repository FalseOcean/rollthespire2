using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record RelicPreparation(RelicFamilyPlan Plan, FilterBodyDescription Body);

// One initial up_front progression shared by ordinary and Shop predicates.
// InitialTreasure is composed separately by the query's preparation owner.
internal static partial class RelicFamily
{
    internal const string Id = "R.Relic";
    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        bool accepted = TryPrepare(model, preferences, out var prepared, preferGpu, numericalVariant);
        body = prepared?.Body;
        return accepted;
    }
    internal static bool TryPrepare(SearchModel model, FilterExecutionPreferences preferences,
        out RelicPreparation? prepared, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        prepared = null;
        if (!model.Evaluation.RelicSequenceConditions.Any(c => !c.IsEmpty) &&
            !model.Evaluation.RelicShopSequenceConditions.Any(c => !c.IsEmpty)) return false;
        if (!RelicFamilyPlanCompiler.TryCompile(model, out var plan, out string issue) || plan is null)
        {
            RuntimeLog.TryBackgroundWarning($"rFamilyUnavailable=true;issue={issue};recovery=ExactOnly;legacyFallback=false");
            return false;
        }
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var authority = FilterPricingAuthority.From(model);
        var evaluation = RelicReferencePlan.ProjectEvaluation(model);
        var reference = new RelicReferencePlan(evaluation, model.Authority.WorldAuthority, profile);
        var analytical = RelicFamilyAnalyticalCost.Project(plan);
        var survival = RelicFamilySurvival.Project(model);
        var expected = RelicExpectedFilteringCost.Project(model, plan, analytical, survival);
        var preparation = new PreparedFilter(Id) {
            AnalyticalCost = analytical, Survival = survival, ExpectedFilteringCost = expected,
            Obligations = [new(Id, model.Authority.PlayerSlotIndex)]
        };
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        string identity = model.SnapshotFingerprint;
        var cpu = PrepareCpu(preparation, plan, reference, authority, profile, identity, workers);
        if (!preferGpu) { prepared = new(plan, cpu); return true; }
        string ruleRevision = plan.Pool.UsesChildShuffle ? ".Modern113" : "";
        var dense = RelicConditionPerformance.Project(plan, FamilyPhysicalImplementationRevisions.RelicDensePerformance + ruleRevision);
        var compact = RelicConditionPerformance.Project(plan, FamilyPhysicalImplementationRevisions.RelicCompactPerformance + ruleRevision);
        var body = new FilterBodyDescription(preparation, dense.PhysicalImplementationRevision, profile.ProfileId) {
            UsesGpu = true, NumericRequirements = GpuNumericRequirements.Int64,
            InputIdentity = identity, PlayersCount = authority.PlayersCount, CpuWorkers = preferences.WorkerCount,
            WindowCapacity = RelicFamilyGpuExecutor.Capacity, PreferredRootWindowSize = RelicFamilyGpuExecutor.Capacity, CpuAlternatives = [cpu],
            PerformanceProjection = isCompact => isCompact ? compact : dense,
            Cost = new() {
                CanonicalScope = authority.PlayersCount == 1,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeRelic(plan),
                MinimumComparableInput = isCompact => isCompact
                    ? FamilyPerformanceEvidenceEligibility.MinimumDurableCompactInput(RelicFamilyGpuExecutor.CompactInputCapacity)
                    : RelicFamilyGpuExecutor.Capacity,
                Quote = (selected, geometry, _) => FamilyGpuReadback.Reference(selected,
                    FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ?? RelicPhysicalPricing.Quote(authority, plan, geometry, expected))
            },
            CreateExecution = (selected, _) => new GpuFilterExecution<RelicFamilyGpuExecutor>(selected,
                device => RelicFamilyGpuExecutor.Create(device, identity, plan), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalAbi1ReadyMs, metrics.CommandMs + metrics.SubmitMs + metrics.SyncMs,
                        metrics.ReadbackMs, metrics.ReadbackBytes);
                }, resource => resource.Dispose(), reference.Reference, profile, resource => (resource.DeviceName, resource.SetupMs), 256),
            BindPrivate = (device, input, output) => RelicFamilyGpuExecutor.Create(device, identity, plan, input, output).BindPrivateSerial()
        };
        prepared = new(plan, body);
        return true;
    }
    private static Func<ulong, bool> CreateReferenceMatcher(RelicReferencePlan reference)
    {
        int reported = 0;
        return root => reference.Matches(root, cause => {
            if (Interlocked.Exchange(ref reported, 1) == 0)
                RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation(
                    "RelicFilterIncomplete", nameof(RelicFamily), "Filter.Reference", "issues.filter_incomplete",
                    $"cause={cause};retained=true;filterDecision=Unknown;matchClaimed=false");
        });
    }
}
