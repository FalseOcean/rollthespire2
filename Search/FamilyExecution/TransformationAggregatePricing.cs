namespace RolltheSpire2.Search.FamilyExecution;

// Public T cost entry points retained for focused fixtures and other callers.
// Family instances cache the immutable work projection at construction.
internal static class TransformationAggregatePricing
{
    internal static FamilyPhysicalQuote? Cpu(TransformationAggregateNumericalPlan plan, TransformationAggregateProbability probability,
        FamilyPhysicalQuoteRequest geometry, int workers = 1) =>
        TransformationAggregateWorkEstimate.Cpu(plan, probability, TransformationAggregateWorkEstimate.Describe(plan), geometry, workers);

    internal static FamilyPhysicalQuote? Gpu(TransformationAggregateNumericalPlan plan, TransformationAggregateProbability probability,
        TransformationAggregateGpuPlan physical, FamilyPhysicalQuoteRequest geometry) =>
        TransformationAggregateWorkEstimate.Gpu(plan, probability, physical, TransformationAggregateWorkEstimate.Describe(plan), geometry);
}
