using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record AncientOptionPreparation(AncientOptionFamilyPlan Plan, FilterBodyDescription Body);

// Prepares A's hypothetical event-local conditions and available numerical bodies.
// Actual Ancient occurrence remains a separate world/final-model obligation.
internal static partial class AncientOptionFamily
{
    internal const string Id = "A.AncientOption";
    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        bool accepted = TryPrepare(model, preferences, out var preparation, preferGpu, numericalVariant);
        body = preparation?.Body;
        return accepted;
    }

    internal static bool TryPrepare(SearchModel model, FilterExecutionPreferences preferences,
        out AncientOptionPreparation? result, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        result = null;
        var e = model.Evaluation;
        if (!(e.AncientBranchConditions.Any(b => b.OptionAny.Count > 0 || b.SeaGlassTargetAny.Count > 0) ||
            e.AncientOptionFilters.Any(f => !f.IsEmpty) || e.AncientSeaGlassTargetFilters.Any(f => !f.IsEmpty))) return false;
        AncientOptionFamilyPlan plan;
        try { plan = new(model, preferGpu, numericalVariant: numericalVariant); }
        catch (InvalidOperationException error) when (error.Message == "A.OptionContextUnavailable")
        {
            RuntimeLog.TryBackgroundWarning("ancientOptionFamilyUnavailable=true;phase=CpuCompilation;replacement=None;remainingQueryOwner=ProductionExact;replacementCompleteness=NotClaimed;exceptionJson=" + RuntimeLog.SafeJson(error.ToString()));
            return false;
        }
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var pricing = AncientOptionPricingInput.From(model);
        string identity = model.SnapshotFingerprint;
        var preparation = new PreparedFilter(Id) {
            AnalyticalCost = new(Id, plan.Capacity, [], "EventLocalExpectedWorkUnavailable"),
            Survival = plan.Survival,
            Obligations = [new(Id, model.Authority.PlayerSlotIndex)],
            Issues = plan.GpuPreparationFailureDetail.Length == 0 ? [] :
                [new("GpuPreparation", plan.GpuPreparationIssue, plan.GpuPreparationFailureDetail)]
        };
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        var cpu = PrepareCpu(preparation, plan, pricing, profile, identity, workers);
        if (!plan.GpuSupported) { result = new(plan, cpu); return true; }
        var cost = new FilterCostDescription {
            MinimumComparableInput = compactInput => plan.Capacity,
                PartyChild = new(Math.Max(1, plan.OptionPlans.Sum(p => 1 + Math.Max(0, p.MaxScratchCount - 3) * .1)), plan.NumericalVariant != GpuNumericalVariant.IntegerRne, plan.NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne.20261025.v1" : ""),
                CanonicalScope = model.Authority.PlayersCount == 1,
            CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeAncient(pricing.Evaluation, plan),
            Quote = (selected, geometry, _) => FamilyGpuReadback.Reference(selected,
                FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ?? AncientOptionPhysicalPricing.Quote(pricing, plan, geometry))
        };
        var body = new FilterBodyDescription(preparation, "A.AncientOption.Gpu.EventLocal", model.ProfileId) {
            UsesGpu = true, InputIdentity = identity, PlayersCount = model.Authority.PlayersCount,
            CpuWorkers = preferences.WorkerCount, WindowCapacity = plan.Capacity,
            NumericRequirements = plan.GpuNumericRequirements, Cost = cost, CpuAlternatives = [cpu],
            PerformanceProjection = compact => new(Id,
                $"A.AncientOption.Gpu.EventLocal.{(compact ? "CompactAbi1" : "Dense")}.CanonicalAbi1Ready.20260922.v2" +
                    (plan.NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne.20261025.v1" : "") + GpuRngRotation.Revision,
                "A.AncientOption.Neutral.20260907.v1", 1, plan.NumericalVariant == GpuNumericalVariant.IntegerRne
                    ? "NoAcceptedWithinPathCurve;CanonicalAbi1Ready;IntegerRneOperationEstimateAvailable;NoIntegerBodyMeasurement"
                    : "NoAcceptedWithinPathCurve;CanonicalAbi1Ready", true),
            CreateExecution = (selected, _) => new GpuFilterExecution<AncientOptionFamilyGpuExecutor>(selected,
                device => new(device, plan), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalMs, metrics.DispatchSyncMs, metrics.ReadbackMs, metrics.ReadbackBytes);
                }, resource => resource.Dispose(), plan.Reference, profile, resource => (resource.Device, resource.SetupMs)),
            BindPrivate = plan.NumericalVariant != GpuNumericalVariant.NativeFloat64 ? null :
                (device, input, output) => {
                    var resource = new AncientOptionFamilyGpuExecutor(device, plan, input, output);
                    return new(resource, resource.Device, resource.SetupMs, input, output, (batch, count, token) => {
                        FamilyCandidateSet? result = null; AncientOptionFamilyGpuMetrics metrics;
                        if (output is not null) resource.ExecutePrivateStage(batch, count, token, out metrics);
                        else result = input is null ? resource.Execute(FamilyCandidateSet.Dense(batch), token, out metrics) :
                            resource.ExecutePrivate(batch, count, token, out metrics);
                        return new(metrics.Output, result, metrics.DispatchSyncMs, metrics.ReadbackMs,
                            metrics.ReadbackBytes, metrics.CanonicalMs, metrics.Dispatches);
                    });
                }
        };
        result = new(plan, body);
        return true;
    }

    private static Func<ulong, bool> CreateMatcher(AncientOptionFamilyPlan plan)
    {
        var reported = new System.Collections.Concurrent.ConcurrentDictionary<(int Act, ModelKey? Ancient), byte>();
        return root => plan.MatchesObserved(root, (act, key, cause) => {
            if (reported.TryAdd((act, key), 0))
                RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation(
                    "AncientOptionFilterIncomplete", nameof(AncientOptionFamilyPlan), $"Filter.Act{act}.{key?.Serialized ?? "Unscoped"}",
                    "issues.filter_incomplete", $"act={act};ancient={key?.Serialized};cause={cause};retained=true;filterDecision=Unknown;requiresFinalCheck=true;matchClaimed=false");
        });
    }
}
