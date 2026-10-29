namespace RolltheSpire2.Search.FamilyExecution;

// Quote inputs, not executable stages or a graph. Population is computed before
// execution from the probability model, never a same-run survivor observation.
public readonly record struct FamilyPhysicalQuoteRequest(
    bool CompactInput, bool PrivateOutput, double MeanInputPopulation, bool PrivateInput = false)
{
    // Internal consumer purpose, not execution admission or a different query.
    // GPU complete-call references cannot supply this numerical/device boundary.
    internal bool ComputationOnly { get; init; }
    // Frozen by the caller; same-family compositions reuse this snapshot.
    internal GpuCostSnapshot? CostSnapshot { get; init; }
}

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
    private static readonly IReadOnlySet<string> EmptyCoverage = new HashSet<string>(StringComparer.Ordinal);

    // The shared numerical/device-work entrance. Shape, population and optional
    // CPU root geometry remain explicit; a public GPU call is never a body price.
    internal static FamilyPhysicalQuote? Computation(IFamilyInvocation family, FamilyPhysicalQuoteRequest geometry,
        GpuCostSnapshot? costs, IReadOnlySet<string>? passedCoverage = null, int? referenceRootWindow = null)
    {
        geometry = geometry with { ComputationOnly = true, CostSnapshot = costs };
        passedCoverage ??= EmptyCoverage;
        FamilyPhysicalQuote? quote = null;
        if (family is FamilyCpuExecution cpu)
        {
            if (referenceRootWindow is int roots)
                quote = FamilyCanonicalMeasuredCost.TryQuote(cpu, geometry, roots, passedCoverage);
            quote ??= cpu.Quote(geometry, passedCoverage);
        }
        else quote = family is PartyFamily party
            ? party.QuotePhysicalWork(geometry, passedCoverage) : family.QuotePhysicalWork(geometry);
        if (quote is null || family.ConditionPerformance.UsesGpu && quote.PublicTransportClass == "CompleteCanonicalAbi1")
            return null;
        return costs?.Local(family, quote, geometry) ?? quote;
    }

    // Timing for one selected family invocation. Root/fraction, when supplied,
    // describe the real reached fragment, not a new planner candidate or q.
    internal static FamilyUnitCost? CompleteCall(IFamilyInvocation family, FamilyPhysicalQuoteRequest geometry,
        GpuCostSnapshot? costs, double survival, IReadOnlySet<string>? passedCoverage = null,
        int? rootPopulation = null, double inputFraction = 1, bool includeInvokeOverhead = true,
        bool privateInvocation = false, string? correlationId = null)
    {
        geometry = geometry with { ComputationOnly = false, CostSnapshot = costs };
        passedCoverage ??= EmptyCoverage;
        bool privateReference = privateInvocation || geometry.PrivateInput || geometry.PrivateOutput;
        double reach = rootPopulation is int roots
            ? Predictability.SearchPredictabilityMath.ProbabilityAtLeastOne(inputFraction, roots) : 1;
        if (!(reach > 0) || !double.IsFinite(reach)) return null;
        var price = privateReference || rootPopulation is not int rootWindow ? null :
            FamilyCanonicalMeasuredCost.TryStage(family, rootWindow, inputFraction, geometry.CompactInput,
                rootWindow, passedCoverage);
        if (price is not null) return new(price.Milliseconds, price.SetupMilliseconds, price.Evidence)
            { Source = FamilyPerformanceEvidenceSource.Condition, Origin = FamilyUnitCostOrigin.CanonicalCompleteReference };
        FamilyUnitCost? partyPrice = privateReference ? null : family switch {
            PartyFamily party => party.QuoteUnit(geometry.MeanInputPopulation, geometry.CompactInput, passedCoverage),
            NeowFamily.PartyInitial party => party.QuoteUnit(geometry.MeanInputPopulation, geometry.CompactInput),
            _ => null
        };
        if (partyPrice is not null) return partyPrice with {
            Milliseconds = partyPrice.Milliseconds * reach,
            Source = partyPrice.Evidence.Contains("PriceQuality=Estimate", StringComparison.Ordinal)
                ? FamilyPerformanceEvidenceSource.Reference : FamilyPerformanceEvidenceSource.Condition
        };
        var complete = privateReference ? null : FamilyAuxiliaryEvidence.Read<FamilyPhysicalQuote?>(
            family.FamilyId, "EtaPublicQuote", () => family.QuotePhysicalWork(geometry), null, correlationId);
        if (complete is { PublicTransportClass: "CompleteCanonicalAbi1" } &&
            (!FamilyCanonicalMeasuredCost.IsCanonicalQuote(complete) || rootPopulation is null))
        {
            var milliseconds = FamilyCoarsePricing.PublicMilliseconds(complete, geometry, survival);
            bool measured = FamilyCanonicalMeasuredCost.IsCanonicalQuote(complete);
            return milliseconds is double ms ? new FamilyUnitCost(ms * reach, complete.SetupMilliseconds,
                (measured ? "" : "PublicCompleteCallReferenceApproximation;NotFamilyComputation;") + complete.Evidence)
                { Source = measured ? FamilyPerformanceEvidenceSource.Condition : FamilyPerformanceEvidenceSource.Reference,
                    Origin = measured ? FamilyUnitCostOrigin.CanonicalCompleteReference : FamilyUnitCostOrigin.FamilyCompleteReference } : null;
        }
        var local = FamilyAuxiliaryEvidence.Read<FamilyPhysicalQuote?>(family.FamilyId,
            "EtaComputationQuote", () => Computation(family, geometry, costs, passedCoverage), null, correlationId);
        var computation = local is null ? null : FamilyCoarsePricing.ComputationMilliseconds(local, geometry);
        var transportQuote = privateReference && local is not null ? local with { FixedWindowMilliseconds = 0 } : local;
        var transfer = transportQuote is null ? null : FamilyCoarsePricing.HostTransferMilliseconds(
            transportQuote, geometry, survival, includeInvokeOverhead);
        if (!computation.HasValue || !transfer.HasValue) return null;
        return new((computation.Value + transfer.Value) * reach, local!.SetupMilliseconds,
            $"FamilyComputationMs={computation:G17};HostTransferEstimateMs={transfer:G17};" +
            "HostTransferOutsideFamilyCost;NoGpuDeviceRatioOnHost;NotPrivateTransportMeasurement;" +
            (privateReference ? "ActualPrivateInputOutputRoles;PublicCompleteCallNotReused;" : "PublicTransferEnvelope;") + local.Evidence)
            { Origin = FamilyUnitCostOrigin.ComputationAndTransferEstimate };
    }

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
