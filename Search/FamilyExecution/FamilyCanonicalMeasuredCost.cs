using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Predictability;

namespace RolltheSpire2.Search.FamilyExecution;

// Absolute, finite complete-Invoke references. They never enter the historical
// peak/ratio stores, learn from a running search, or supply numerical-only work.
// Key budgets follow the producer: CPU wrappers record their actual Workers
// (currently capped at 32), while GPU keys retain the legal request budget up to
// 64. Reader admission does not change either execution budget or scale prices.
internal sealed record FamilyCanonicalCostReference(int SchemaVersion, string Boundary,
    IReadOnlyList<FamilyCanonicalCostReferenceRow> Rows, JsonElement? CompleteCallCurves = null);

internal sealed record FamilyCanonicalCostReferenceRow(FamilyCanonicalCostKey Key, double MinimumInputs,
    double MaximumInputs, double InterceptMilliseconds, double NanosecondsPerInput, double? SetupMilliseconds,
    int MeasuredRepeats, double MeasuredMilliseconds, string TrainingRowsSha256, string FitMethod,
    IReadOnlyList<string> TrainingCases, string Evidence, bool HeldoutUsedForFit = false,
    // DenseEqualsInputRange declares both finite endpoints as measured Dense
    // N=span anchors. One anchor must retain MinimumInputs==MaximumInputs.
    string RootSpanMode = "Exact", string MeasurementCadence = "HistoricalUnspecified");

internal sealed record FamilyCanonicalStagePrice(double Milliseconds, double? SetupMilliseconds, string Evidence);

internal sealed class FamilyCanonicalCostSnapshot
{
    private readonly IReadOnlyDictionary<string, FamilyCanonicalCostReferenceRow[]> _byKey;
    private readonly IReadOnlyDictionary<string, FamilyCanonicalCostReferenceRow[]> _denseByShape;
    private readonly IReadOnlySet<string> _referencedShapes;
    private readonly FamilyCompleteCallSnapshot _completeCalls;
    internal string AssetEvidence { get; init; } = "";
    internal bool IsEmpty => _byKey.Count == 0 && _denseByShape.Count == 0 && _completeCalls.IsEmpty;
    internal FamilyCanonicalCostSnapshot(IEnumerable<FamilyCanonicalCostReferenceRow> rows, JsonElement? completeCallCurves = null)
    {
        var described = new List<FamilyCanonicalCostReferenceRow>();
        foreach (var row in rows)
        {
            if (!Valid(row)) throw new InvalidDataException("CanonicalCostReference.InvalidRow");
            if (!FamilyCanonicalMeasuredCost.KnownMeasurementCadence(row.MeasurementCadence))
            {
                FamilyAuxiliaryEvidence.Report(new InvalidDataException(
                    $"CanonicalCostReference.InvalidMeasurementCadence:value={row.MeasurementCadence ?? "null"};key={row.Key.StableKey}"),
                    nameof(FamilyCanonicalMeasuredCost), "LoadFiniteCanonicalCadence");
                continue;
            }
            described.Add(row);
        }
        // Historical GPU rows remain readable evidence, not prices for a live
        // Godot main loop. CPU collection has no GPU cadence requirement.
        var admitted = described.Where(row => row.Key.ExecutionClass == "CPU" ||
            row.MeasurementCadence == FamilyCanonicalMeasuredCost.LiveMainLoopCadence).ToArray();
        _byKey = Index(admitted.Where(row => row.RootSpanMode == "Exact"), key => key.StableKey);
        _denseByShape = Index(admitted.Where(row => row.RootSpanMode == "DenseEqualsInputRange"), DenseShapeKey);
        _referencedShapes = admitted.Select(row => DenseShapeKey(row.Key)).ToHashSet(StringComparer.Ordinal);
        _completeCalls = FamilyCompleteCallSnapshot.Parse(completeCallCurves);
    }

    // Only the price index omits RootWindow. Stored keys and runtime batch/RNG
    // geometry remain the actual measured/requested spans. Every other field,
    // including body, children, workers, device and work shape, remains exact.
    private static string DenseShapeKey(FamilyCanonicalCostKey key) => (key with { RootWindow = 0 }).StableKey;

    // Existence only: a known finite body may lack this population/span. This
    // never returns a price or relaxes the finite Quote admission contract.
    internal bool HasShapeReference(FamilyCanonicalCostKey? key, double? sourceSurvival = null) =>
        key is not null && (_referencedShapes.Contains(DenseShapeKey(key)) || _completeCalls.HasShapeReference(key, sourceSurvival));
    internal bool RequiresSourceSurvival(FamilyCanonicalCostKey? key) =>
        key is not null && _completeCalls.RequiresSourceSurvival(key);

    private static IReadOnlyDictionary<string, FamilyCanonicalCostReferenceRow[]> Index(
        IEnumerable<FamilyCanonicalCostReferenceRow> rows, Func<FamilyCanonicalCostKey, string> identity)
    {
        var groups = new Dictionary<string, FamilyCanonicalCostReferenceRow[]>(StringComparer.Ordinal);
        foreach (var group in rows.GroupBy(row => identity(row.Key), StringComparer.Ordinal))
        {
            var ranges = group.OrderBy(row => row.MinimumInputs).ToArray();
            for (int i = 1; i < ranges.Length; i++)
                if (ranges[i].MinimumInputs <= ranges[i - 1].MaximumInputs)
                    throw new InvalidDataException("CanonicalCostReference.AmbiguousOverlappingRanges");
            groups.Add(group.Key, ranges);
        }
        return groups;
    }

    internal static bool Valid(FamilyCanonicalCostReferenceRow row) => row is not null && row.Key is not null &&
        row.Key.SchemaVersion == FamilyCanonicalCostShape.SchemaVersion &&
        !string.IsNullOrWhiteSpace(row.Key.FamilyId) && !string.IsNullOrWhiteSpace(row.Key.PhysicalRevision) &&
        !string.IsNullOrWhiteSpace(row.Key.WorkShape) && !string.IsNullOrWhiteSpace(row.Key.RuntimeTarget) &&
        !string.IsNullOrWhiteSpace(row.Key.CpuIdentity) && !string.IsNullOrWhiteSpace(row.Key.Profile) &&
        row.Key.ExecutionClass is "CPU" or "GPU" && row.Key.ConfiguredWorkers is >= 1 and <= 64 &&
        row.Key.Players is >= 1 and <= 4 && row.Key.RootWindow is > 0 and <= 16777216 &&
        (row.Key.ExecutionClass != "GPU" || !string.IsNullOrWhiteSpace(row.Key.GpuIdentity) &&
            !string.IsNullOrWhiteSpace(row.Key.RenderingBackend)) &&
        double.IsFinite(row.MinimumInputs) && double.IsFinite(row.MaximumInputs) &&
        row.MinimumInputs >= 1 && row.MaximumInputs >= row.MinimumInputs &&
        (row.RootSpanMode == "Exact" && row.MaximumInputs <= row.Key.RootWindow ||
            row.RootSpanMode == "DenseEqualsInputRange" && !row.Key.CompactInput &&
            row.MinimumInputs == Math.Truncate(row.MinimumInputs) && row.MaximumInputs == Math.Truncate(row.MaximumInputs) &&
            row.MaximumInputs <= 16777216 && row.Key.RootWindow >= row.MinimumInputs && row.Key.RootWindow <= row.MaximumInputs) &&
        double.IsFinite(row.InterceptMilliseconds) && row.InterceptMilliseconds >= 0 &&
        double.IsFinite(row.NanosecondsPerInput) && row.NanosecondsPerInput >= 0 &&
        (row.InterceptMilliseconds > 0 || row.NanosecondsPerInput > 0) &&
        (!row.SetupMilliseconds.HasValue || double.IsFinite(row.SetupMilliseconds.Value) && row.SetupMilliseconds.Value >= 0) &&
        row.MeasuredRepeats >= 1 && double.IsFinite(row.MeasuredMilliseconds) && row.MeasuredMilliseconds > 0 &&
        row.TrainingRowsSha256 is { Length: 64 } && row.TrainingRowsSha256.All(Uri.IsHexDigit) &&
        !string.IsNullOrWhiteSpace(row.FitMethod) && row.TrainingCases is { Count: > 0 } &&
        !string.IsNullOrWhiteSpace(row.Evidence) && !row.HeldoutUsedForFit;

    internal FamilyPhysicalQuote? Quote(FamilyCanonicalCostKey? key, FamilyPhysicalQuoteRequest geometry, double? sourceSurvival = null)
    {
        if (key is null || !FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput ||
            geometry.CompactInput != key.CompactInput || geometry.MeanInputPopulation < 1) return null;
        if (_completeCalls.Quote(key, geometry, sourceSurvival, out string? selectorGap) is { } complete)
            return complete with { Evidence = complete.Evidence + AssetEvidence };
        var row = Find(_byKey, key.StableKey, geometry.MeanInputPopulation);
        // A Dense interval is an explicit finite fit over measured N=span
        // endpoints, not permission to infer span from Compact mean population.
        // Exact rows win even when an interval also covers this geometry.
        if (row is null && !key.CompactInput && geometry.MeanInputPopulation == key.RootWindow &&
            key.RootWindow is > 0 and <= 16777216)
            row = Find(_denseByShape, DenseShapeKey(key), geometry.MeanInputPopulation);
        if (row is null) return null;
        // The existing same-position comparison consumes ns at this geometry.
        // Fold the measured invocation floor into that value exactly once;
        // TryStage separately owns nonempty/full/tail invocation multiplicity.
        double ns = row.NanosecondsPerInput + row.InterceptMilliseconds * 1e6 / geometry.MeanInputPopulation;
        if (!double.IsFinite(ns) || ns <= 0) return null;
        return new(key.FamilyId, "CanonicalInvoke." + key.WorkShape, ns, key.RootWindow,
            row.SetupMilliseconds, FamilyCanonicalMeasuredCost.Boundary + ";AbsoluteFiniteReference;" + row.Evidence +
            $";ActualMeasurementCadence={row.MeasurementCadence};CurrentCadenceAdmission={(key.ExecutionClass == "CPU" ? "CpuCadenceUnrestricted" : "ExplicitLiveMainLoop")}" +
            $";RootSpanMode={row.RootSpanMode};ActualRootWindow={key.RootWindow};MeasuredInputRange={row.MinimumInputs:G17}..{row.MaximumInputs:G17};FitMethod={row.FitMethod};TrainingRowsSha256={row.TrainingRowsSha256};NoPeakRatio;NoSameRunRepricing" +
            (selectorGap is null ? "" : ";NewCompleteCallCurveUnavailable=" + selectorGap + ";LegacyFiniteReferenceFallback") + AssetEvidence,
            OutputElementBytes: 8, OutputAlreadyOrdered: true,
            PublicTransportClass: key.ExecutionClass == "CPU" ? "CpuOrderedAbi1" : "CompleteCanonicalAbi1")
        {
            FixedWindowMilliseconds = 0, LocalCostSource = "LocalMeasurement",
            ReferencePeakThroughput = 0, GpuCostRatio = 1, LocalExecutorCostRatio = 1
        };
    }

    private static FamilyCanonicalCostReferenceRow? Find(
        IReadOnlyDictionary<string, FamilyCanonicalCostReferenceRow[]> index, string key, double inputs) =>
        index.TryGetValue(key, out var rows)
            ? rows.SingleOrDefault(row => inputs >= row.MinimumInputs && inputs <= row.MaximumInputs) : null;
}

internal static class FamilyCanonicalMeasuredCost
{
    private static readonly IReadOnlySet<string> EmptyCoverage = new HashSet<string>();
    internal const string HistoricalCadence = "HistoricalUnspecified";
    internal const string BlockedMainLoopCadence = "BlockedMainLoop";
    internal const string LiveMainLoopCadence = "LiveMainLoop";
    internal static bool KnownMeasurementCadence(string? value) =>
        value is HistoricalCadence or BlockedMainLoopCadence or LiveMainLoopCadence;
    internal const string Boundary = "CompleteInvokeAsyncWall.FirstAndWarmupExcluded.20261021.v1";
    internal const string ResourceName = "RolltheSpire2.Search.FamilyExecution.CanonicalCostReference.json";
    private static readonly Lazy<FamilyCanonicalCostSnapshot> Reference = new(() =>
        FamilyAuxiliaryEvidence.Read(nameof(FamilyCanonicalMeasuredCost), "LoadFiniteCanonicalReference",
            LoadReference, new FamilyCanonicalCostSnapshot([])));

    private static FamilyCanonicalCostSnapshot LoadReference()
    {
        string? path = Environment.GetEnvironmentVariable("RT2_CANONICAL_COST_REFERENCE_PATH");
        if (!string.IsNullOrWhiteSpace(path)) return ParseLoaded(File.ReadAllBytes(path), "Override:" + Path.GetFullPath(path));
        using var stream = typeof(FamilyCanonicalMeasuredCost).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException("CanonicalCostReference.EmbeddedResourceMissing", ResourceName);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return ParseLoaded(bytes.ToArray(), "Embedded:" + ResourceName);
    }

    private static FamilyCanonicalCostSnapshot ParseLoaded(byte[] bytes, string source) =>
        ParseCore(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'),
            ";CanonicalAssetSource=" + source + ";CanonicalAssetSha256=" + Convert.ToHexString(SHA256.HashData(bytes)));

    internal static FamilyCanonicalCostSnapshot Parse(string json) => ParseCore(json, "");

    private static FamilyCanonicalCostSnapshot ParseCore(string json, string assetEvidence)
    {
        var document = JsonSerializer.Deserialize<FamilyCanonicalCostReference>(json)
            ?? throw new InvalidDataException("CanonicalCostReference.NullDocument");
        if (document.SchemaVersion != 1 || document.Boundary != Boundary || document.Rows is null)
            throw new InvalidDataException("CanonicalCostReference.SchemaOrBoundaryMismatch");
        return new(document.Rows, document.CompleteCallCurves) { AssetEvidence = assetEvidence };
    }

    internal static ExactSearchExecutionRequest? SourceRequest(IFamilyInvocation family) => family switch
    {
        FamilyCpuExecution cpu => cpu.PricingRequest,
        InitialRoomsInvocation initial => initial.PricingRequest,
        EventResultFamily events => events.PricingRequest,
        NeowFamily neow => neow.PricingRequest,
        MerchantShopColorlessFamily shop => shop.PricingRequest,
        RelicFamily relic => relic.PricingRequest,
        CombatRewardFamily combat => combat.PricingRequest,
        WorldFamily world => world.PricingRequest,
        AncientOptionFamily ancient => ancient.PricingRequest,
        TransformationAggregateFamily transform => transform.PricingRequest,
        _ => null
    };

    // Match reviewed standalone call owners; concrete revisions/shapes still
    // separate ordinary E/W and additional initial-room R/W calls.
    internal static bool InScope(ExactSearchExecutionRequest request, IFamilyInvocation family)
    {
        if (request.Authority.PlayersCount != 1 || request.CompiledSearch.Context.Party is not null ||
            request.CompiledSearch.NormalizedQuery.StandardMaps.Count != 0)
            return false;
        IFamilyInvocation owner = family is FamilyCpuExecution cpu ? cpu.PricingOwner ?? family : family;
        return owner switch
        {
            NeowFamily n => n.Coverage.Count == 1,
            MerchantShopColorlessFamily => true,
            RelicFamily => true,
            CombatRewardFamily => true,
            TransformationAggregateFamily => true,
            AncientOptionFamily => true,
            EventResultFamily or WorldFamily => true,
            InitialRoomsInvocation => family.ConditionPerformance.UsesGpu,
            _ => false
        };
    }

    internal static FamilyPhysicalQuote? TryQuote(IFamilyInvocation family, FamilyPhysicalQuoteRequest geometry,
        int rootPopulation, IReadOnlySet<string>? passedCoverage = null)
    {
        var request = SourceRequest(family);
        if (request is null || !InScope(request, family) || geometry.PrivateInput || geometry.PrivateOutput) return null;
        if (family is FamilyCpuExecution { RequiredPassedCoverage: { } required } &&
            (passedCoverage is null || !required.SetEquals(passedCoverage))) return null;
        if (Reference.Value.IsEmpty) return null;
        return FamilyAuxiliaryEvidence.Read<FamilyPhysicalQuote?>(family.FamilyId, "FiniteCanonicalQuote", () =>
        {
            var snapshot = Reference.Value;
            var key = FamilyCanonicalCostShape.Describe(request, family, geometry.CompactInput, rootPopulation);
            return FamilyGpuReadback.Reference(family, snapshot.Quote(key, geometry, snapshot.RequiresSourceSurvival(key)
                ? SourceSurvival(family, passedCoverage, request.SnapshotFingerprint) : null));
        },
            null, request.SnapshotFingerprint);
    }

    // Boundary identity covers finite historical rows and complete-call curves.
    // LocalMeasurement/transport alone also occur on unrelated legacy paths.
    internal static bool IsCanonicalQuote(FamilyPhysicalQuote? quote) => quote is not null &&
        quote.Evidence.StartsWith(Boundary + ";", StringComparison.Ordinal);

    internal static bool HasShapeReference(IFamilyInvocation family, bool compact, int rootWindow,
        IReadOnlySet<string>? passedCoverage = null)
    {
        if (rootWindow <= 0) return false;
        if (family is FamilyCpuExecution { RequiredPassedCoverage: { } required } &&
            (passedCoverage is null || !required.SetEquals(passedCoverage))) return false;
        var request = SourceRequest(family);
        return request is not null && InScope(request, family) && !Reference.Value.IsEmpty && FamilyAuxiliaryEvidence.Read(family.FamilyId, "FiniteCanonicalShapePresence",
            () =>
            {
                var snapshot = Reference.Value;
                var key = FamilyCanonicalCostShape.Describe(request, family, compact, rootWindow);
                // This is structural existence for finite-gap handling, not
                // admission of a price. A known N body with an unknown or
                // unmeasured source selector must not become a static CPS fit.
                // Quote remains responsible for selecting actual p or an
                // explicitly applicable legacy finite row.
                return snapshot.HasShapeReference(key) || snapshot.RequiresSourceSurvival(key);
            },
            false, request.SnapshotFingerprint);
    }

    private static double? SourceSurvival(IFamilyInvocation family, IReadOnlySet<string>? passed, string correlation) =>
        family.FamilyId != "N.Neow" ? null : FamilyAuxiliaryEvidence.Read<double?>(family.FamilyId, "FiniteCanonicalSourceSurvival",
            () => family.ResolveSurvival(passed ?? EmptyCoverage).SurvivalProbability, null, correlation);

    // The public quote request has no Compact root span. Only Dense implies its
    // own root count; Compact callers must use the explicit rootPopulation API.
    internal static FamilyPhysicalQuote? TryDenseQuote(IFamilyInvocation family, FamilyPhysicalQuoteRequest geometry)
    {
        double n = geometry.MeanInputPopulation;
        return geometry.CompactInput || !double.IsFinite(n) || n < 1 || n > 16777216 || n != Math.Truncate(n)
            ? null : TryQuote(family, geometry, (int)n);
    }

    internal static FamilyCanonicalStagePrice? TryInvocation(IFamilyInvocation family, double inputs, bool compact,
        int rootPopulation, IReadOnlySet<string>? passedCoverage = null)
    {
        var quote = TryQuote(family, new(compact, false, inputs), rootPopulation, passedCoverage);
        if (quote is null) return null;
        double ms = quote.FixedWindowMilliseconds + inputs * quote.NanosecondsPerInput / 1e6;
        return double.IsFinite(ms) && ms >= 0 ? new(ms, quote.SetupMilliseconds, quote.Evidence) : null;
    }

    // ETA may repeat one public unit reference over real root fragments. This
    // does not quote a chain or a private transport allocation. Never infer a
    // Compact root span from its expected survivor population.
    internal static FamilyCanonicalStagePrice? TryStage(IFamilyInvocation family, double roots, double fraction,
        bool compact, int rootWindow, IReadOnlySet<string>? passedCoverage = null)
    {
        if (!double.IsFinite(roots) || roots <= 0 ||
            !double.IsFinite(fraction) || fraction is <= 0 or > 1 || rootWindow <= 0) return null;
        double full = Math.Floor(roots / rootWindow), remainder = roots - full * rootWindow;
        // No fictitious fractional SearchBatch and no implicit tail extrapolation.
        if (remainder != Math.Truncate(remainder)) return null;
        double milliseconds = 0; double? setup = 0;
        var evidence = new List<string>();
        bool Add(int population, double repetitions)
        {
            if (population == 0 || repetitions == 0) return true;
            double reached = SearchPredictabilityMath.ProbabilityAtLeastOne(fraction, population);
            if (!(reached > 0) || !double.IsFinite(reached)) return false;
            double inputs = population * fraction / reached;
            var price = TryInvocation(family, inputs, compact, population, passedCoverage);
            if (price is null) return false;
            milliseconds += repetitions * reached * price.Milliseconds;
            setup = setup.HasValue && price.SetupMilliseconds.HasValue ? Math.Max(setup.Value, price.SetupMilliseconds.Value) : null;
            evidence.Add($"RootFragment={population};Windows={repetitions:G17};Reach={reached:G17};" + price.Evidence);
            return true;
        }
        if (!Add(rootWindow, full) || !Add((int)remainder, 1) || !double.IsFinite(milliseconds)) return null;
        return new(milliseconds, setup, string.Join(';', evidence));
    }
}
