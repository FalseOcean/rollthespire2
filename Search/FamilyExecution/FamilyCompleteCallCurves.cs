using System.Text.Json;
using System.Globalization;

namespace RolltheSpire2.Search.FamilyExecution;

// Actual positive complete-call knots, not affine fitted rows or new numerical
// execution. The offline assembler owns repeat aggregation and source admission.
internal sealed record FamilyCompleteCallPoint(double N, double CallMilliseconds, int MeasuredRepeats = 1);
internal sealed record FamilyCompleteCallCurve(FamilyCanonicalCostKey Key,
    IReadOnlyList<FamilyCompleteCallPoint> Points, string Role, string Split, string Boundary,
    string MeasurementCadence, IReadOnlyList<string> TrainingCases, string TrainingRowsSha256,
    IReadOnlyList<string> SourceGroups, string Evidence, JsonElement Provenance, bool HeldoutUsedForFit = false,
    double? SourceSurvival = null);

internal sealed class FamilyCompleteCallSnapshot
{
    private const string PublicRole = "PublicAbi1Canonical";
    private const double SourceSurvivalRelativeTolerance = 1e-10;
    private readonly IReadOnlyDictionary<string, FamilyCompleteCallCurve> _curves;
    private readonly IReadOnlyDictionary<string, FamilyCompleteCallCurve[]> _byShape;
    private readonly IReadOnlyDictionary<(string Family, bool Compact, int Workers), double> _cpuWorkerRatios;
    internal bool IsEmpty => _curves.Count == 0;
    private FamilyCompleteCallSnapshot(IReadOnlyDictionary<string, FamilyCompleteCallCurve> curves)
    {
        _curves = curves;
        _byShape = curves.Values.GroupBy(c => Shape(c.Key)).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        _cpuWorkerRatios = CpuWorkerRatios(curves.Values);
    }

    // Relative scaling only, never an absolute price for another shape/body.
    // Match source work, device, population and survival within each measured
    // pair, then take a same-family median. No linear core-count extrapolation.
    private static IReadOnlyDictionary<(string Family, bool Compact, int Workers), double> CpuWorkerRatios(
        IEnumerable<FamilyCompleteCallCurve> curves)
    {
        static string Group(FamilyCompleteCallCurve c) => (c.Key with {
            ConfiguredWorkers = 1,
            PhysicalRevision = System.Text.RegularExpressions.Regex.Replace(c.Key.PhysicalRevision, @"\.P\d+(?=\.)", ".P1")
        }).StableKey + ";SourceSurvival=" + P(c.SourceSurvival);
        var ratios = new List<(string Family, bool Compact, int Workers, double Ratio)>();
        foreach (var group in curves.Where(c => c.Key.ExecutionClass == "CPU" && c.Key.Players == 1 &&
            c.Key.OrderedChildBodies == "").GroupBy(Group))
        {
            var single = group.FirstOrDefault(c => c.Key.ConfiguredWorkers == 1);
            if (single is null) continue;
            foreach (var parallel in group.Where(c => c.Key.ConfiguredWorkers > 1))
            {
                var common = single.Points.Join(parallel.Points, p => p.N, p => p.N,
                    (a, b) => (N: a.N, Ratio: b.CallMilliseconds / a.CallMilliseconds)).OrderByDescending(p => p.N).FirstOrDefault();
                if (Positive(common.N) && Positive(common.Ratio))
                    ratios.Add((parallel.Key.FamilyId, parallel.Key.CompactInput, parallel.Key.ConfiguredWorkers, common.Ratio));
            }
        }
        return ratios.GroupBy(r => (r.Family, r.Compact, r.Workers)).ToDictionary(g => g.Key, g => {
            var values = g.Select(r => r.Ratio).Order().ToArray();
            return values.Length % 2 == 1 ? values[values.Length / 2] :
                (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2;
        });
    }

    internal (double Ratio, string Evidence) CpuWorkerRatio(string family, int workers, bool compact)
    {
        if (workers <= 1) return (1, "WorkerScale=P1");
        var points = _cpuWorkerRatios.Where(p => p.Key.Family == family && p.Key.Compact == compact)
            .Select(p => (Workers: p.Key.Workers, Ratio: p.Value)).Append((Workers: 1, Ratio: 1d)).OrderBy(p => p.Workers).ToArray();
        if (points.Length == 1) return (1, "WorkerScale=UnmeasuredConservativeP1");
        var left = points.Last(p => p.Workers <= workers);
        var right = points.FirstOrDefault(p => p.Workers >= workers, points[^1]);
        double fraction = right.Workers == left.Workers ? 0 : (workers - left.Workers) / (double)(right.Workers - left.Workers);
        return (left.Ratio * (1 - fraction) + right.Ratio * fraction,
            $"WorkerScale=SameFamilyHistoricalMedian;WorkerAnchors={left.Workers}..{right.Workers};" +
            "NotCurrentShapeMeasurement;AboveLastAnchorCapped");
    }
    private static string Shape(FamilyCanonicalCostKey key) =>
        (key.CompactInput ? key : key with { RootWindow = 0 }).PriceKey;
    private static bool Probability(double? value) => value is double p && double.IsFinite(p) && p is >= 0 and <= 1;
    // Admission retains exact round-trip identities, normalizing only +/-0.
    // Pricing selection below tolerates rounding from equivalent source sums;
    // it never merges distinct stored curves or changes model probabilities.
    private static string Identity(FamilyCanonicalCostKey key, double? survival) => Shape(key) +
        (key.FamilyId == "N.Neow" ? "\nSourceSurvival=" + (survival == 0 ? 0d : survival!.Value).ToString("R", CultureInfo.InvariantCulture) : "");
    internal bool HasShapeReference(FamilyCanonicalCostKey key, double? survival) =>
        SelectCurve(key, survival, false, out _) is not null;
    internal bool RequiresSourceSurvival(FamilyCanonicalCostKey key) =>
        key is { FamilyId: "N.Neow", Players: 1 } && _byShape.ContainsKey(Shape(key));
    private static string P(double? value) => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "Unknown";
    private static bool SameSourceSurvival(double a, double b) => a == b ||
        a > 0 && b > 0 && Math.Abs(a - b) <= SourceSurvivalRelativeTolerance * Math.Max(a, b);
    private FamilyCompleteCallCurve? SelectCurve(FamilyCanonicalCostKey key, double? survival,
        bool reportAmbiguous, out string? gap)
    {
        gap = null;
        if (!_byShape.TryGetValue(Shape(key), out var curves)) return null;
        if (key.FamilyId != "N.Neow") return curves[0];
        if (!Probability(survival))
        { gap = "N.SourceSurvivalUnavailable;NoCurveAlias;QuerySourceSurvival=" + P(survival); return null; }
        var matches = curves.Where(c => SameSourceSurvival(c.SourceSurvival!.Value, survival!.Value)).ToArray();
        if (matches.Length == 1) return matches[0];
        gap = (matches.Length == 0 ? "N.SourceSurvivalNotMeasured" : "N.SourceSurvivalAmbiguous") +
            ";NoCurveAlias;QuerySourceSurvival=" + P(survival) + ";StoredSourceSurvival=" +
            string.Join(',', (matches.Length == 0 ? curves : matches).Select(c => P(c.SourceSurvival)));
        if (matches.Length > 1 && reportAmbiguous)
            Report(new InvalidDataException("CompleteCallCurves." + gap), "SelectCompleteCallCurve");
        return null;
    }

    internal static FamilyCompleteCallSnapshot Parse(JsonElement? optional)
    {
        var candidates = new List<FamilyCompleteCallCurve>();
        // Raw JSON prevents a malformed optional curve's DTO from discarding
        // valid legacy rows during the outer document deserialization.
        if (optional is null || optional.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new(new Dictionary<string, FamilyCompleteCallCurve>());
        if (optional.Value.ValueKind != JsonValueKind.Array)
        {
            Report(new InvalidDataException("CompleteCallCurves.ArrayRequired"), "LoadCompleteCallCurves");
            return new(new Dictionary<string, FamilyCompleteCallCurve>());
        }
        int index = 0;
        foreach (var value in optional.Value.EnumerateArray())
        {
            try
            {
                var curve = value.Deserialize<FamilyCompleteCallCurve>();
                if (curve is null || !Valid(curve)) throw new InvalidDataException("CompleteCallCurves.InvalidTrainingCurve");
                candidates.Add(curve with { Points = curve.Points.OrderBy(p => p.N).ToArray() });
            }
            catch (Exception error) when (error is not OutOfMemoryException and not OperationCanceledException)
            { Report(error, "LoadCompleteCallCurve.Index" + index); }
            index++;
        }
        var admitted = new Dictionary<string, FamilyCompleteCallCurve>(StringComparer.Ordinal);
        foreach (var group in candidates.GroupBy(c => Identity(c.Key, c.SourceSurvival)))
        {
            // No arbitrary winner or partial merging of independently authored
            // same-key evidence. The assembler must resolve duplicate sources.
            if (group.Count() != 1)
                Report(new InvalidDataException("CompleteCallCurves.AmbiguousSourceShape:" + group.First().Key.FamilyId +
                    ";body=" + group.First().Key.PhysicalRevision), "LoadCompleteCallCurveIdentity");
            else admitted.Add(group.Key, group.Single());
        }
        return new(admitted);
    }

    private static void Report(Exception error, string stage) =>
        FamilyAuxiliaryEvidence.Report(error, nameof(FamilyCanonicalMeasuredCost), stage);
    private static bool Names(IReadOnlyList<string>? names) => names is { Count: > 0 } && names.All(n => !string.IsNullOrWhiteSpace(n));
    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Valid(FamilyCompleteCallCurve c)
    {
        var k = c.Key;
        if (k is null || k.SchemaVersion != FamilyCanonicalCostShape.SchemaVersion || k.Players != 1 ||
            k.FamilyId is not ("N.Neow" or "R.Relic" or "S.MerchantShopColorless" or "C.CombatReward" or "E.EventResult" or
                "T.TransformationAggregate" or "W.World" or "A.AncientOption" or "I.InitialRooms") ||
            k.ExecutionClass is not ("CPU" or "GPU") || k.ConfiguredWorkers is < 1 or > 64 || k.RootWindow is < 1 or > 16777216 ||
            string.IsNullOrWhiteSpace(k.RuntimeTarget) || string.IsNullOrWhiteSpace(k.CpuIdentity) ||
            string.IsNullOrWhiteSpace(k.Profile) || string.IsNullOrWhiteSpace(k.PhysicalRevision) || string.IsNullOrWhiteSpace(k.WorkShape) ||
            k.ExecutionClass == "GPU" && (string.IsNullOrWhiteSpace(k.GpuIdentity) || string.IsNullOrWhiteSpace(k.RenderingBackend))) return false;
        if (c.Role != PublicRole || !string.Equals(c.Split, "training", StringComparison.OrdinalIgnoreCase) || c.HeldoutUsedForFit ||
            c.Boundary != FamilyCanonicalMeasuredCost.Boundary || !FamilyCanonicalMeasuredCost.KnownMeasurementCadence(c.MeasurementCadence) ||
            k.ExecutionClass == "GPU" && c.MeasurementCadence != FamilyCanonicalMeasuredCost.LiveMainLoopCadence ||
            !Names(c.TrainingCases) || !Names(c.SourceGroups) || string.IsNullOrWhiteSpace(c.Evidence) ||
            c.TrainingRowsSha256 is not { Length: 64 } || !c.TrainingRowsSha256.All(Uri.IsHexDigit) ||
            c.Provenance.ValueKind != JsonValueKind.Object || !c.Provenance.EnumerateObject().Any() || NontrainingProvenance(c.Provenance)) return false;
        if (k.FamilyId == "N.Neow" ? !Probability(c.SourceSurvival) : c.SourceSurvival.HasValue) return false;
        return c.Points is { Count: > 0 } && c.Points.All(p => p is not null && Positive(p.N) && p.N == Math.Truncate(p.N) &&
            p.N <= (k.CompactInput ? k.RootWindow : 16777216) && Positive(p.CallMilliseconds) && p.MeasuredRepeats >= 1) &&
            c.Points.Select(p => p.N).Distinct().Count() == c.Points.Count;
    }
    private static bool NontrainingProvenance(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Any(NontrainingProvenance);
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
        {
            if ((property.Name is "Split" or "DatasetRole" or "Role") && property.Value.ValueKind == JsonValueKind.String &&
                (property.Value.GetString()?.ToLowerInvariant() is "validation" or "heldout" or "holdout" or "acceptance" or "combination" or "test")) return true;
            if ((property.Name is "HeldoutUsedForFit" or "Derived") && property.Value.ValueKind == JsonValueKind.True) return true;
            if (NontrainingProvenance(property.Value)) return true;
        }
        return false;
    }

    internal FamilyPhysicalQuote? Quote(FamilyCanonicalCostKey key, FamilyPhysicalQuoteRequest geometry,
        double? sourceSurvival, out string? selectorGap)
    {
        double n = geometry.MeanInputPopulation;
        var curve = SelectCurve(key, sourceSurvival, true, out selectorGap);
        if (!Positive(n) || !key.CompactInput && n != key.RootWindow || curve is null) return null;
        var points = curve.Points;
        if (n < points[0].N || n > points[^1].N) return null;
        var left = points.Last(p => p.N <= n); var right = points.First(p => p.N >= n);
        double weight = right.N == left.N ? 0 : (n - left.N) / (right.N - left.N);
        double milliseconds = left.CallMilliseconds * (1 - weight) + right.CallMilliseconds * weight;
        double ns = milliseconds * (1e6 / n);
        if (!Positive(milliseconds) || !Positive(ns)) return null;
        return new(key.FamilyId, "CompleteCallCurve." + key.WorkShape, ns, key.RootWindow, null,
            FamilyCanonicalMeasuredCost.Boundary + ";ActualCompleteCallKnots;Method=AdjacentLinearActualAnchors;NoExtrapolation;" +
            $"AnchorN={left.N:G17}..{right.N:G17};MeasuredRepeats={left.MeasuredRepeats},{right.MeasuredRepeats};" +
            $"TrainingRowsSha256={curve.TrainingRowsSha256};TrainingCases={string.Join(',', curve.TrainingCases)};" +
            $"ActualMeasurementCadence={curve.MeasurementCadence};FirstRootInvokeAndWarmupsExcluded;FullColdSetupUnknown;NoPeakRatio;NoSameRunRepricing;" +
            FamilyCanonicalCostSnapshot.BudgetEvidence(key, curve.Key) +
            (curve.SourceSurvival.HasValue ? "SourceSurvival=" + P(curve.SourceSurvival) + ";QuerySourceSurvival=" + P(sourceSurvival) +
                ";SourceModelSelector;PositiveRelativeTolerance=1e-10;ZeroExact;NotObserved;NoSurvivalScaling;" : "") + curve.Evidence,
            OutputElementBytes: 8, OutputAlreadyOrdered: true,
            PublicTransportClass: key.ExecutionClass == "CPU" ? "CpuOrderedAbi1" : "CompleteCanonicalAbi1")
        { FixedWindowMilliseconds = 0, LocalCostSource = "LocalMeasurement", ReferencePeakThroughput = 0,
            GpuCostRatio = 1, LocalExecutorCostRatio = 1 };
    }
}
