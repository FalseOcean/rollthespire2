namespace RolltheSpire2.Search.FamilyExecution;

// One selected Family invocation, including an existing whole-table wrapper.
// Origin distinguishes owned complete references from computation + transfer
// estimates, so reference-only tools preserve a missing reference as null.
internal enum FamilyUnitCostOrigin { FamilyCompleteReference, CanonicalCompleteReference, ComputationAndTransferEstimate }

internal sealed record FamilyUnitCost(double Milliseconds, double? SetupMilliseconds, string Evidence)
{
    internal FamilyPerformanceEvidenceSource Source { get; init; } = FamilyPerformanceEvidenceSource.Reference;
    internal FamilyUnitCostOrigin Origin { get; init; } = FamilyUnitCostOrigin.FamilyCompleteReference;
}
