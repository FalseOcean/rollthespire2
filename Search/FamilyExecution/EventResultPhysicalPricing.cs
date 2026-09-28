using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class EventResultPhysicalPricing
{
    internal static FamilyPhysicalQuote? QuoteCpuCompiledColor(ExactSearchExecutionRequest request,
        EventResultFamilyPlan plan, EventResultCpuPlan compiled, FamilyPhysicalQuoteRequest g)
    {
        if (!request.Authority.CanUseCurrentModel || request.Authority.PlayersCount != 1 || plan.PlayerSlot != 0 ||
            !FamilyPhysicalQuote.AdmittedRequest(g) || g.CompactInput || g.PrivateInput || g.PrivateOutput ||
            g.MeanInputPopulation < 1024 || compiled.Remaining.Length > 4) return null;
        double residual = 0;
        foreach (var condition in compiled.Remaining)
        {
            double? cost = condition.Kind switch {
                EventResultConditionKind.FakeMerchantOfferedFakeRelic => 100,
                EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic => 45,
                _ => null };
            if (cost is null) return null;
            residual += cost.Value; // No invented early-rejection probability.
        }
        // Compiled mask is deterministic modeled reach, never observed survivors.
        double reach = System.Numerics.BitOperations.PopCount(compiled.AllowedRemovalMask) / 4d;
        return new("E.EventResult", "E.CompiledColor.20260913.v1.P1", 25 + reach * residual,
            65536, null, "CpuNumericalClosure.20260913;CompiledFourColorRemoval;ResidualOperatorSum0..4;" +
            (compiled.Remaining.Length > 1 ? "ConservativeCoarse;" : "") +
            "Workers=1;MinInput=1024;CpuCanonicalAbi1;metric=ns/ActualStageInput", OutputElementBytes:8,
            OutputAlreadyOrdered:true,PublicTransportClass:"CpuOrderedAbi1");
    }

    internal static PrivateOrdinalAllocationPricing.Work LegacyEwWork => new(.85, 1, 1 << 20);
    internal static FamilyPhysicalQuote? Quote(EventResultFamilyPlan plan, FamilyPhysicalQuoteRequest geometry)
        => QuoteMeasured(plan, geometry) ?? QuoteModel(plan, geometry);

    private static FamilyPhysicalQuote? QuoteMeasured(EventResultFamilyPlan plan, FamilyPhysicalQuoteRequest geometry)
    {
        if (!plan.GpuSupported || !FamilyPhysicalQuote.AdmittedRequest(geometry) ||
            !FamilyPhysicalQuote.HasReferenceBackend()) return null;
        if (plan.Conditions.Length == 0 || plan.Conditions.GroupBy(c => c.Kind).Any(g => g.Count() != 1)) return null;
        var kinds = plan.Conditions.Select(c => c.Kind).Order().ToArray();
        if (kinds.Contains(EventResultConditionKind.ColorfulPhilosophersOfferedColor) &&
            plan.Authority.UnlockedCharacterCardPoolKeys.Count != 5) return null;
        double? ns = kinds switch {
            [EventResultConditionKind.TrashHeapGrabCard] => geometry.CompactInput ? .7 : .65,
            [EventResultConditionKind.TrashHeapDiveRelic] => .67,
            [EventResultConditionKind.FakeMerchantOfferedFakeRelic] => geometry.CompactInput ? 1.15 : 1.10,
            [EventResultConditionKind.ColorfulPhilosophersOfferedColor] => .64,
            [EventResultConditionKind.TrashHeapGrabCard, EventResultConditionKind.TrashHeapDiveRelic] => .63,
            [EventResultConditionKind.TrashHeapGrabCard, EventResultConditionKind.FakeMerchantOfferedFakeRelic] => 1.03,
            _ => null
        };
        if (ns is null) return null;
        return new("E.EventResult", "E.EventLocal." + string.Join('+', kinds), ns.Value,
            plan.Capacity, 40, "FamilyMatrix.20260910.EventOperators;NumericalDeviceEmission;NoPublicMaterialization");
    }

    private static bool ModelSupported(EventResultFamilyPlan plan) => plan.GpuSupported &&
        plan.PlayerSlot == 0 && plan.Conditions.Length is >= 1 and <= 8 &&
        plan.Authority.UnlockedCharacterCardPoolKeys.Count == 5;

    private static FamilyPhysicalQuote? QuoteModel(EventResultFamilyPlan plan, FamilyPhysicalQuoteRequest g)
    {
        if (!ModelSupported(plan) || !FamilyPhysicalQuote.AdmittedRequest(g) ||
            !FamilyPhysicalQuote.HasReferenceBackend() || g.MeanInputPopulation < 1048576) return null;
        // Each operator generates its facts once before target comparisons. GPU
        // Grab/Dive share the Trash draw; additional targets do not replay Fake.
        bool fake = plan.Conditions.Any(c => c.Kind == EventResultConditionKind.FakeMerchantOfferedFakeRelic);
        double extra = Math.Max(0, plan.Conditions.Length - 2) * .08;
        return new("E.EventResult", "E.OperatorGroups.20260913.v1", (fake ? .95 : .55) + extra,
            plan.Capacity, 40, "FamilyCostClosure.20260913;Model=BoundedCoarse;OperatorFactsOnce;" +
            "Predicates1..8;ExtraComparisonEnvelope;MinInput=1048576;NumericalDeviceEmission;HostPayloadExcluded;metric=ns/ActualStageInput;" +
            (extra > 0 ? "ConservativeCoarse" : "ValidatedOperatorRegion"));
    }

    internal static FamilyPhysicalQuote? QuoteCpu(ExactSearchExecutionRequest request, EventResultFamilyPlan plan, FamilyPhysicalQuoteRequest g)
    {
        if (!request.Authority.CanUseCurrentModel || request.Authority.PlayersCount != 1 ||
            !ModelSupported(plan) || !FamilyPhysicalQuote.AdmittedRequest(g) || g.CompactInput ||
            g.PrivateInput || g.PrivateOutput || g.MeanInputPopulation < 4096) return null;
        int scalar = plan.Conditions.Select(c => c.Kind).Distinct().Count(k =>
            k is EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic);
        int fake = plan.Conditions.Count(c => c.Kind == EventResultConditionKind.FakeMerchantOfferedFakeRelic);
        int color = plan.Conditions.Count(c => c.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor);
        // CPU currently initializes both Trash projections separately. These are
        // canonical CPU costs, including ordered output, not GPU coefficients.
        double ns = 80 + 20 * scalar + (fake > 0 ? 120 : 0) + (color > 0 ? 360 : 0) +
            20 * (Math.Max(0, fake - 1) + Math.Max(0, color - 1));
        return new("E.EventResult", "E.OperatorGroups.20260913.v1.P1", ns, 65536, null,
            "FamilyCostClosure.20260913;Model=BoundedCoarse;Workers=1;MinInput=4096;" +
            "CpuCanonicalAbi1;metric=ns/ActualStageInput;" + (plan.Conditions.Length > 2 ? "ConservativeCoarse" : "ValidatedOperatorRegion"), OutputElementBytes: 8,
            OutputAlreadyOrdered: true, PublicTransportClass: "CpuOrderedAbi1");
    }
}
