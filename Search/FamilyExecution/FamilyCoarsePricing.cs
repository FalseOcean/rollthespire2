namespace RolltheSpire2.Search.FamilyExecution;

// Unit/transport composition stays with the existing consumer. These factories
// only label family-owned work estimates and reuse historical CPU budget ratios.
// They do not admit execution, infer a predicate, or manufacture a measurement.
internal static class FamilyCoarsePricing
{
    internal const string Revision = "FamilyLocalWorkEstimate.20261028.v1";

    internal static FamilyPhysicalQuote? Gpu(string familyId, string workClass,
        double nanosecondsPerInput, int capacity, FamilyPhysicalQuoteRequest geometry, string basis,
        double? setupMilliseconds = null, int outputElementBytes = 4, bool outputAlreadyOrdered = false,
        string publicTransportClass = "Counted32") =>
        !Valid(geometry, nanosecondsPerInput, capacity) ? null : new(familyId,
            Revision + "." + workClass, nanosecondsPerInput, capacity, setupMilliseconds,
            Evidence(basis) + ";ExecutionClass=GPU;ReferenceDevice=RTX4060Laptop;ReferenceBackend=d3d12;" +
            "NumericalAndDeviceEmission;SubmitAndHostTransportSeparate;NotCurrentDeviceOrBodyMeasurement",
            outputElementBytes, outputAlreadyOrdered, publicTransportClass);

    internal static FamilyPhysicalQuote? Cpu(string familyId, string workClass,
        double singleWorkerNanoseconds, int workers, FamilyPhysicalQuoteRequest geometry, string basis,
        int capacity = 65536)
    {
        if (!Valid(geometry, singleWorkerNanoseconds, capacity) || geometry.PrivateInput || geometry.PrivateOutput ||
            workers is < 1 or > 32) return null;
        var scale = geometry.MeanInputPopulation < 4096
            ? (Ratio: 1d, Evidence: "WorkerScale=SmallPopulationP1Envelope")
            : FamilyCanonicalMeasuredCost.CpuWorkerRatio(familyId, workers, geometry.CompactInput);
        return new(familyId, Revision + "." + workClass + ".P" + workers,
            singleWorkerNanoseconds * scale.Ratio, capacity, null,
            Evidence(basis) + ";ExecutionClass=CPU;ReferenceDevice=i9-13900HX;" +
            $"Workers={workers};WorkerRatio={scale.Ratio:G17};" + scale.Evidence +
            ";CpuCanonicalAbi1;metric=ns/ActualStageInput;NotCurrentDeviceOrBodyMeasurement",
            OutputElementBytes: 8, OutputAlreadyOrdered: true, PublicTransportClass: "CpuOrderedAbi1");
    }

    internal static bool IsEstimate(FamilyPhysicalQuote? quote) =>
        quote?.Evidence.Contains("PriceQuality=Estimate", StringComparison.Ordinal) == true;

    internal static FamilyPhysicalQuote LocalComputation(IFamilyInvocation family, FamilyPhysicalQuote quote,
        FamilyPhysicalQuoteRequest geometry) => !family.ConditionPerformance.UsesGpu ? quote :
        FamilyAuxiliaryEvidence.Read(family.FamilyId, "LocalComputationReference",
            () => (geometry.CostSnapshot ?? GpuCostCalibration.Capture()).Local(family, quote,
                geometry with { ComputationOnly = true }), quote);

    // Family computation includes device emission, or CPU's own canonical
    // output. A GPU complete wall call has no proven numerical decomposition.
    internal static double? ComputationMilliseconds(FamilyPhysicalQuote quote, FamilyPhysicalQuoteRequest geometry)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || quote.WindowCapacity <= 0 ||
            quote.PublicTransportClass == "CompleteCanonicalAbi1" || !double.IsFinite(quote.NanosecondsPerInput) ||
            quote.NanosecondsPerInput < 0 || geometry.MeanInputPopulation > 0 && quote.NanosecondsPerInput == 0)
            return null;
        double windows = Math.Ceiling(geometry.MeanInputPopulation / quote.WindowCapacity);
        return geometry.MeanInputPopulation * quote.NanosecondsPerInput / 1e6 +
            (quote.PublicTransportClass == "CpuOrderedAbi1" ? windows * quote.FixedWindowMilliseconds : 0);
    }

    // Timing-only host/control envelope, outside family c. Public payload is
    // absent on private intermediate outputs; device rates never scale this.
    // Legacy fixed windows remain explicitly unattributed call allowances.
    internal static double? HostTransferMilliseconds(FamilyPhysicalQuote quote, FamilyPhysicalQuoteRequest geometry,
        double survival, bool includeInvokeOverhead = true)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || quote.WindowCapacity <= 0 ||
            !double.IsFinite(survival) || survival is < 0 or > 1 ||
            quote.PublicTransportClass == "CompleteCanonicalAbi1") return null;
        if (quote.PublicTransportClass == "CpuOrderedAbi1") return 0;
        double input = geometry.MeanInputPopulation, windows = Math.Ceiling(input / quote.WindowCapacity);
        if (windows == 0) return 0;
        double output = input * survival;
        bool large = quote.PublicTransportClass == "LargeResidentAppend32";
        double payload = geometry.PrivateOutput ? 0 :
            windows * (1 - Math.Exp(-output / windows)) * (large ? 4 : .15) +
            output * (quote.OutputAlreadyOrdered ? 6 : large ? 38 : 25) / 1e6;
        return windows * (.15 + quote.FixedWindowMilliseconds) + payload + (includeInvokeOverhead ? .6 : 0);
    }

    // Existing public-call composition, also used for same-family wrappers.
    // Already-complete calls own their own output; do not add it a second time.
    internal static double? PublicMilliseconds(FamilyPhysicalQuote quote, FamilyPhysicalQuoteRequest geometry,
        double survival)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput ||
            !double.IsFinite(survival) || survival is < 0 or > 1 || quote.WindowCapacity <= 0) return null;
        double input = geometry.MeanInputPopulation;
        double windows = Math.Ceiling(input / quote.WindowCapacity);
        double numerical = input * quote.NanosecondsPerInput / 1e6;
        if (quote.PublicTransportClass is "CompleteCanonicalAbi1" or "CpuOrderedAbi1")
            return numerical + windows * quote.FixedWindowMilliseconds;
        return numerical + (HostTransferMilliseconds(quote, geometry, survival) ?? double.NaN);
    }

    private static bool Valid(FamilyPhysicalQuoteRequest geometry, double ns, int capacity) =>
        FamilyPhysicalQuote.AdmittedRequest(geometry) && double.IsFinite(ns) && ns > 0 && capacity > 0;

    private static string Evidence(string basis) => Revision +
        ";Model=ReusableCoarse;PriceQuality=Estimate;FamilyLocal;NoSameRunFitting;Basis=" + basis;
}
