using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record EventResultPreparation(EventResultFamilyPlan Plan, FilterBodyDescription Body);

// Root-local event facts and immutable numerical preparation, independent of a
// search lifetime. World occurrence and final full-request judgment stay separate.
internal static partial class EventResultFamily
{
    internal const string Id = "E.EventResult";
    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        bool accepted = TryPrepare(model, preferences, out var preparation, preferGpu, numericalVariant);
        body = preparation?.Body;
        return accepted;
    }

    internal static bool TryPrepare(SearchModel model, FilterExecutionPreferences preferences,
        out EventResultPreparation? result, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        result = null;
        if (model.Evaluation.EventResultConditions.Count == 0) return false;
        var plan = new EventResultFamilyPlan(model, preferGpu, numericalVariant);
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var authority = FilterPricingAuthority.From(model);
        string identity = model.SnapshotFingerprint;
        var preparation = new PreparedFilter(Id) {
            AnalyticalCost = new(Id, plan.Capacity, [], "EventLocalExpectedWorkUnavailable"),
            Survival = plan.Survival,
            Obligations = [new(Id, model.Authority.PlayerSlotIndex)],
            Issues = plan.GpuPreparationFailureDetail.Length == 0 ? [] :
                [new("GpuPreparation", "GpuPlanConstructionFailed", plan.GpuPreparationFailureDetail)]
        };
        var compiled = EventResultCpuPlan.TryCompile(model, plan);
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        var ordinary = PrepareCpu(preparation, plan, null, authority, profile, identity, workers);
        var cpu = compiled is null ? ordinary : PrepareCpu(preparation, plan, compiled, authority, profile, identity, workers);
        if (!plan.GpuSupported) { result = new(plan, cpu); return true; }
        bool finalHeader = plan.NumericalVariant == GpuNumericalVariant.NativeFloat64;
        string ruleRevision = plan.Authority.UsesChildShuffle ? ".Modern113" : "";
        var body = new FilterBodyDescription(preparation, "E.EventResult.Gpu.Whitelist" + ruleRevision, model.ProfileId) {
            UsesGpu = true, InputIdentity = identity, PlayersCount = model.Authority.PlayersCount,
            CpuWorkers = preferences.WorkerCount, WindowCapacity = plan.Capacity,
            NumericRequirements = plan.GpuNumericRequirements,
            CpuAlternatives = compiled is null ? [ordinary] : [cpu, ordinary],
            PerformanceProjection = compact => new(Id,
                $"E.EventResult.Gpu.Whitelist.{(compact ? "CompactAbi1" : "Dense")}.CanonicalAbi1Ready.20260919.v1" +
                    (plan.NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne.20261025.v1" : "") +
                    (plan.UsesPreparedGroups ? ".RequestGroups.20261030.v1" : "") + ruleRevision + GpuRngRotation.Revision,
                "E.EventResult.Neutral.20260907.v1", 1, plan.NumericalVariant == GpuNumericalVariant.IntegerRne
                    ? "NoAcceptedWithinPathCurve;CanonicalAbi1Ready;PhysicalQuoteUnavailable=UnmeasuredIntegerRneVariant"
                    : "NoAcceptedWithinPathCurve;CanonicalAbi1Ready", true),
            Cost = new() {
                MinimumComparableInput = compactInput => plan.Capacity,
                PartyChild = new(Math.Max(1, plan.Conditions.Sum(c => c.Kind is EventResultConditionKind.MorphicGroveGroupInitialBasicsContains ? 5 : c.Kind > EventResultConditionKind.MorphicGroveGroupInitialBasicsContains ? 3 : 1)),
                    !plan.Authority.UsesChildShuffle && plan.NumericalVariant != GpuNumericalVariant.IntegerRne,
                    plan.NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne.20261025.v1" : ""),
                CanonicalScope = authority.PlayersCount == 1 && !plan.Authority.UsesChildShuffle,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeEvent(plan),
                PrivateOutputRevision = finalHeader ? EventResultFamilyGpuExecutor.FinalPrivateHeaderRevision : "",
                PrivateOutputPathSuffix = finalHeader ? ".PrivateFinalHeaderReadback" : "",
                Quote = (selected, geometry, _) => {
                    var quote = FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                        (plan.Authority.UsesChildShuffle ? null : EventResultPhysicalPricing.Quote(plan, geometry)) ?? EventResultWorkEstimate.Gpu(plan, geometry);
                    if (plan.UsesPreparedGroups && quote is not null)
                        quote = quote with { Evidence = quote.Evidence + ";RequestGroupsRetainedNumericalReference;NoNewGroupBodyMeasurement" };
                    return FamilyGpuReadback.Reference(selected, geometry.PrivateOutput && finalHeader && quote is not null
                        ? quote with { Evidence = quote.Evidence + ";RetainedNumericalEstimate;PrivateFinalHeaderTransportUnmeasured" } : quote);
                }
            },
            CreateExecution = (selected, _) => new GpuFilterExecution<EventResultFamilyGpuExecutor>(selected,
                device => new(device, plan), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalMs, metrics.DispatchSyncMs, metrics.ReadbackMs, metrics.ReadbackBytes);
                }, resource => resource.Dispose(), plan.Reference, profile, resource => (resource.Device, resource.SetupMs)),
            BindPrivate = !finalHeader ? null : (device, input, output) => {
                var resource = new EventResultFamilyGpuExecutor(device, plan, input, output);
                return new(resource, resource.Device, resource.SetupMs, input, output, (batch, count, token) => {
                    FamilyCandidateSet? filtered = null; EventResultFamilyGpuMetrics metrics;
                    if (output is not null) resource.ExecutePrivateStage(batch, count, token, out metrics);
                    else filtered = input is null ? resource.Execute(FamilyCandidateSet.Dense(batch), token, out metrics) :
                        resource.ExecutePrivate(batch, count, token, out metrics);
                    return new(metrics.Output, filtered, metrics.DispatchSyncMs, metrics.ReadbackMs,
                        metrics.ReadbackBytes, metrics.CanonicalMs, metrics.Dispatches);
                });
            }
        };
        result = new(plan, body);
        return true;
    }

    private static Func<ulong, bool> CreateMatcher(EventResultFamilyPlan plan)
    {
        var reported = new System.Collections.Concurrent.ConcurrentDictionary<EventResultConditionKind, byte>();
        return root => plan.MatchesObserved(root, (kind, cause) => {
            if (reported.TryAdd(kind, 0))
                RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation(
                    "EventResultFilterIncomplete", nameof(EventResultFamilyPlan), "Filter." + kind,
                    "issues.filter_incomplete", $"condition={kind};cause=E.ReferenceAuthorityUnavailable:{cause};retained=true;filterDecision=Unknown;matchClaimed=false");
        });
    }
}
