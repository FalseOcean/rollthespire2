namespace RolltheSpire2.Search.FamilyExecution;

// Quote inputs, not executable stages or a graph. Population is computed before
// execution from the probability model, never a same-run survivor observation.
public readonly record struct FamilyPhysicalQuoteRequest(
    bool CompactInput, bool PrivateOutput, double MeanInputPopulation, bool PrivateInput = false);

// GPU quotes own numerical work including device emission. CpuOrderedAbi1 quotes
// instead own the complete CPU canonical envelope (hash/partition/output included).
// GPU submit/header/transport,
// public readback/sort and Exact are composed separately. A missing quote is null,
// not Unsupported and not a zero-cost replacement for another physical shape.
public sealed record FamilyPhysicalQuote(
    string FamilyId, string Shape, double NanosecondsPerInput, int WindowCapacity,
    double? SetupMilliseconds, string Evidence,
    int OutputElementBytes = 4, bool OutputAlreadyOrdered = false,
    string PublicTransportClass = "Counted32")
{
    internal static bool AdmittedRequest(FamilyPhysicalQuoteRequest request) =>
        double.IsFinite(request.MeanInputPopulation) && request.MeanInputPopulation >= 0 &&
        request.MeanInputPopulation <= PrivateOrdinalBuffer.Capacity && (!request.PrivateInput || request.CompactInput);

    public double ReferencePeakThroughput { get; init; } = NanosecondsPerInput > 0 ? 1e9 / NanosecondsPerInput : 0;
    public double FixedWindowMilliseconds { get; init; }
    public double LocalExecutorCostRatio { get; init; } = 1;
    public string LocalCostSource { get; init; } = "ReferenceDefault";
    public double GpuCostRatio { get; init; } = 1;
    // Backend/revision measurement binding; GPU/CPU model names are provenance only.
    internal static bool HasReferenceBackend() =>
        Runtime.SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().RenderingBackend == "d3d12";
}
