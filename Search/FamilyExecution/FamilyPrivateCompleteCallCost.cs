using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RolltheSpire2.Search.FamilyExecution;

// Standalone complete binding calls only. Resident input preparation and binding
// construction are excluded; these knots do not describe a multi-family chain.
internal sealed record FamilyPrivateCompleteCallKey(FamilyCanonicalCostKey Invocation,
    bool PrivateInput, bool PrivateOutput, string BindingPath, string BindingPhysicalRevision)
{
    [JsonIgnore] internal string StableKey => JsonSerializer.Serialize(this);
}

internal sealed record FamilyPrivateCompleteCallPoint(double N, double CallMilliseconds, int MeasuredRepeats = 1);
internal sealed record FamilyPrivateCompleteCallCurve(FamilyPrivateCompleteCallKey Key,
    IReadOnlyList<FamilyPrivateCompleteCallPoint> Points, string Role, string Split, string Boundary,
    string MeasurementCadence, IReadOnlyList<string> TrainingCases, string TrainingRowsSha256,
    IReadOnlyList<string> SourceGroups, string Evidence, JsonElement Provenance, bool HeldoutUsedForFit = false,
    double? SourceSurvival = null);
internal sealed record FamilyPrivateCompleteCallReference(int SchemaVersion, string Boundary,
    JsonElement? CompleteCallCurves = null);

internal sealed class FamilyPrivateCompleteCallSnapshot
{
    private const double SourceSurvivalRelativeTolerance = 1e-10;
    private readonly IReadOnlyDictionary<string, FamilyPrivateCompleteCallCurve[]> _byShape;
    internal string AssetEvidence { get; }
    internal bool IsEmpty => _byShape.Count == 0;
    private FamilyPrivateCompleteCallSnapshot(IEnumerable<FamilyPrivateCompleteCallCurve> curves, string assetEvidence)
    {
        AssetEvidence = assetEvidence;
        _byShape = curves.GroupBy(c => Shape(c.Key), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }

    // Dense inputs imply N=root span. Compact population never implies its root
    // span, so the complete key including RootWindow remains exact in that case.
    private static string Shape(FamilyPrivateCompleteCallKey key) =>
        (key.PrivateInput ? key : key with { Invocation = key.Invocation with { RootWindow = 0 } }).StableKey;
    private static string P(double? value) => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "Unknown";
    private static string Identity(FamilyPrivateCompleteCallCurve c) => Shape(c.Key) +
        (c.Key.Invocation.FamilyId == "N.Neow" ? "\nSourceSurvival=" + P(c.SourceSurvival == 0 ? 0d : c.SourceSurvival) : "");
    private static bool Positive(double n) => double.IsFinite(n) && n > 0;
    private static bool Probability(double? n) => n is double p && double.IsFinite(p) && p is >= 0 and <= 1;
    private static bool Names(IReadOnlyList<string>? names) => names is { Count: > 0 } && names.All(n => !string.IsNullOrWhiteSpace(n));
    private static void Report(Exception error, string stage) =>
        FamilyAuxiliaryEvidence.Report(error, nameof(FamilyPrivateCompleteCallCost), stage);

    internal static FamilyPrivateCompleteCallSnapshot Parse(JsonElement? optional, string assetEvidence = "")
    {
        var candidates = new List<FamilyPrivateCompleteCallCurve>();
        if (optional is null || optional.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new(candidates, assetEvidence);
        if (optional.Value.ValueKind != JsonValueKind.Array)
        {
            Report(new InvalidDataException("PrivateCompleteCallCurves.ArrayRequired"), "LoadPrivateCompleteCallCurves");
            return new(candidates, assetEvidence);
        }
        int index = 0;
        foreach (var value in optional.Value.EnumerateArray())
        {
            try
            {
                var curve = value.Deserialize<FamilyPrivateCompleteCallCurve>();
                if (curve is null || !Valid(curve)) throw new InvalidDataException("PrivateCompleteCallCurves.InvalidTrainingCurve");
                candidates.Add(curve with { Points = curve.Points.OrderBy(p => p.N).ToArray() });
            }
            catch (Exception error) when (error is not OutOfMemoryException and not OperationCanceledException)
            { Report(error, "LoadPrivateCompleteCallCurve.Index" + index); }
            index++;
        }
        var admitted = new List<FamilyPrivateCompleteCallCurve>();
        foreach (var group in candidates.GroupBy(Identity, StringComparer.Ordinal))
        {
            // Resolve duplicate evidence offline; never pick a convenient winner.
            if (group.Count() == 1) admitted.Add(group.Single());
            else Report(new InvalidDataException("PrivateCompleteCallCurves.AmbiguousSourceShape;key=" +
                group.First().Key.StableKey + ";SourceSurvival=" + P(group.First().SourceSurvival)), "LoadPrivateCompleteCallCurveIdentity");
        }
        return new(admitted, assetEvidence);
    }

    private static bool ValidKey(FamilyPrivateCompleteCallKey? key)
    {
        if (key?.Invocation is not { } k || k.SchemaVersion != FamilyCanonicalCostShape.SchemaVersion ||
            k.ExecutionClass != "GPU" || k.Players != 1 || k.CompactInput != key.PrivateInput ||
            !key.PrivateInput && !key.PrivateOutput ||
            k.FamilyId is not ("N.Neow" or "R.Relic" or "S.MerchantShopColorless" or "C.CombatReward" or
                "E.EventResult" or "T.TransformationAggregate" or "W.World" or "A.AncientOption") ||
            k.ConfiguredWorkers is < 1 or > 64 || k.RootWindow is < 1 or > 16777216) return false;
        return !string.IsNullOrWhiteSpace(k.RuntimeTarget) && !string.IsNullOrWhiteSpace(k.CpuIdentity) &&
            !string.IsNullOrWhiteSpace(k.GpuIdentity) && !string.IsNullOrWhiteSpace(k.RenderingBackend) &&
            !string.IsNullOrWhiteSpace(k.Profile) && !string.IsNullOrWhiteSpace(k.PhysicalRevision) &&
            !string.IsNullOrWhiteSpace(k.WorkShape) && k.OrderedChildBodies is not null &&
            !string.IsNullOrWhiteSpace(key.BindingPath) && !string.IsNullOrWhiteSpace(key.BindingPhysicalRevision);
    }

    private static bool Valid(FamilyPrivateCompleteCallCurve c)
    {
        if (!ValidKey(c.Key) || c.Role != FamilyPrivateCompleteCallCost.Role(c.Key.PrivateInput, c.Key.PrivateOutput) ||
            !string.Equals(c.Split, "training", StringComparison.OrdinalIgnoreCase) || c.HeldoutUsedForFit ||
            c.Boundary != FamilyPrivateCompleteCallCost.Boundary ||
            c.MeasurementCadence != FamilyCanonicalMeasuredCost.LiveMainLoopCadence ||
            !Names(c.TrainingCases) || !Names(c.SourceGroups) || string.IsNullOrWhiteSpace(c.Evidence) ||
            c.TrainingRowsSha256 is not { Length: 64 } || !c.TrainingRowsSha256.All(Uri.IsHexDigit) ||
            c.Provenance.ValueKind != JsonValueKind.Object || !c.Provenance.EnumerateObject().Any() ||
            NontrainingProvenance(c.Provenance)) return false;
        var k = c.Key.Invocation;
        if (k.FamilyId == "N.Neow" ? !Probability(c.SourceSurvival) : c.SourceSurvival.HasValue) return false;
        return c.Points is { Count: > 0 } && c.Points.All(p => p is not null && Positive(p.N) &&
            p.N == Math.Truncate(p.N) && p.N <= (k.CompactInput ? k.RootWindow : 16777216) &&
            Positive(p.CallMilliseconds) && p.MeasuredRepeats >= 1) &&
            c.Points.Select(p => p.N).Distinct().Count() == c.Points.Count;
    }

    private static bool NontrainingProvenance(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Any(NontrainingProvenance);
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
        {
            string name = property.Name.ToLowerInvariant();
            if ((name is "split" or "datasetrole") && (property.Value.ValueKind != JsonValueKind.String ||
                !string.Equals(property.Value.GetString(), "training", StringComparison.OrdinalIgnoreCase))) return true;
            if (name == "role" && property.Value.ValueKind == JsonValueKind.String &&
                property.Value.GetString()?.ToLowerInvariant() is "validation" or "heldout" or "holdout" or "acceptance" or "combination" or "test") return true;
            if ((name is "heldoutusedforfit" or "derived") && property.Value.ValueKind == JsonValueKind.True) return true;
            if (NontrainingProvenance(property.Value)) return true;
        }
        return false;
    }

    internal bool RequiresSourceSurvival(FamilyPrivateCompleteCallKey key) =>
        key.Invocation.FamilyId == "N.Neow" && _byShape.ContainsKey(Shape(key));

    private FamilyPrivateCompleteCallCurve? Select(FamilyPrivateCompleteCallKey key, double? survival)
    {
        if (!_byShape.TryGetValue(Shape(key), out var curves)) return null;
        if (key.Invocation.FamilyId != "N.Neow") return curves[0];
        if (!Probability(survival)) return null;
        var matches = curves.Where(c => c.SourceSurvival == survival || c.SourceSurvival > 0 && survival > 0 &&
            Math.Abs(c.SourceSurvival!.Value - survival.Value) <= SourceSurvivalRelativeTolerance *
            Math.Max(c.SourceSurvival.Value, survival.Value)).ToArray();
        if (matches.Length > 1)
            Report(new InvalidDataException("PrivateCompleteCallCurves.N.SourceSurvivalAmbiguous;QuerySourceSurvival=" + P(survival) +
                ";StoredSourceSurvival=" + string.Join(',', matches.Select(c => P(c.SourceSurvival)))),
                "SelectPrivateCompleteCallCurve");
        return matches.Length == 1 ? matches[0] : null;
    }

    internal FamilyCanonicalStagePrice? Quote(FamilyPrivateCompleteCallKey? key, double inputs, double? sourceSurvival = null)
    {
        if (!ValidKey(key) || !Positive(inputs) || inputs > key!.Invocation.RootWindow ||
            !key.PrivateInput && inputs != key.Invocation.RootWindow) return null;
        var curve = Select(key, sourceSurvival);
        if (curve is null || inputs < curve.Points[0].N || inputs > curve.Points[^1].N) return null;
        var left = curve.Points.Last(p => p.N <= inputs);
        var right = curve.Points.First(p => p.N >= inputs);
        double weight = left.N == right.N ? 0 : (inputs - left.N) / (right.N - left.N);
        double milliseconds = left.CallMilliseconds * (1 - weight) + right.CallMilliseconds * weight;
        if (!Positive(milliseconds)) return null;
        return new(milliseconds, null, FamilyPrivateCompleteCallCost.Boundary +
            ";ActualCompleteCallKnots;StandaloneSingleFamily;Method=AdjacentLinearActualAnchors;NoExtrapolation;" +
            "FirstAndWarmupExcluded;IncludesDispatchSyncAndValidatedCountHeader;" +
            (key.PrivateOutput ? "Completion=CountReady;" : "Completion=PublicCanonicalAbi1Ready;IncludesPayloadSortAndAbi1Materialization;") +
            "ExcludesConstructionAndResidentInputPreparationAndPrivateVerification;SetupUnknown;NoPeakRatio;NoSameRunRepricing;" +
            $"Role={curve.Role};PrivateInput={key.PrivateInput};PrivateOutput={key.PrivateOutput};BindingPath={key.BindingPath};BindingPhysicalRevision={key.BindingPhysicalRevision};" +
            $"ActualRootSpan={key.Invocation.RootWindow};Inputs={inputs:G17};AnchorN={left.N:G17}..{right.N:G17};MeasuredRepeats={left.MeasuredRepeats},{right.MeasuredRepeats};" +
            $"TrainingRowsSha256={curve.TrainingRowsSha256};TrainingCases={string.Join(',', curve.TrainingCases)};SourceGroups={string.Join(',', curve.SourceGroups)};" +
            $"ActualMeasurementCadence={curve.MeasurementCadence};CurrentCadenceAdmission=ExplicitLiveMainLoop;" +
            (curve.SourceSurvival.HasValue ? "SourceSurvival=" + P(curve.SourceSurvival) + ";QuerySourceSurvival=" + P(sourceSurvival) +
                ";SourceModelSelector;PositiveRelativeTolerance=1e-10;ZeroExact;NotObserved;NoSurvivalScaling;" : "") +
            curve.Evidence + AssetEvidence);
    }
}

internal static class FamilyPrivateCompleteCallCost
{
    internal const string Boundary = "CompleteBindingExecuteWall.CountOrCanonicalAbi1Ready";
    internal const string ResourceName = "RolltheSpire2.Search.FamilyExecution.FamilyPrivateCompleteCallCostReference.json";
    internal const string DenseCountReadyRole = "DensePublicInput.PrivateOutput.CountReady";
    internal const string CompactPublicRole = "PrivateInput.PublicOutput.CanonicalAbi1";
    internal const string CompactCountReadyRole = "PrivateInput.PrivateOutput.CountReady";

    internal static string? Role(bool privateInput, bool privateOutput) => (privateInput, privateOutput) switch
    {
        (false, true) => DenseCountReadyRole,
        (true, false) => CompactPublicRole,
        (true, true) => CompactCountReadyRole,
        _ => null
    };

    // Retained for explicit offline evidence inspection. Runtime planning no
    // longer loads this private-call asset or composes its knots into chain cost.
    internal static FamilyPrivateCompleteCallSnapshot Parse(string json) => ParseCore(json, "");

    private static FamilyPrivateCompleteCallSnapshot ParseCore(string json, string assetEvidence)
    {
        var document = JsonSerializer.Deserialize<FamilyPrivateCompleteCallReference>(json)
            ?? throw new InvalidDataException("PrivateCompleteCallReference.NullDocument");
        if (document.SchemaVersion != 1 || document.Boundary != Boundary)
            throw new InvalidDataException("PrivateCompleteCallReference.SchemaOrBoundaryMismatch");
        return FamilyPrivateCompleteCallSnapshot.Parse(document.CompleteCallCurves, assetEvidence);
    }

    internal static FamilyPrivateCompleteCallKey? Describe(IFamilyInvocation family, bool privateInput, bool privateOutput, int rootSpan)
    {
        if (Role(privateInput, privateOutput) is null || rootSpan is < 1 or > 16777216 ||
            !family.CanBindPrivateSerial || !family.ConditionPerformance.UsesGpu ||
            FamilyCanonicalMeasuredCost.SourceRequest(family) is not { } request ||
            request.Authority.PlayersCount != 1 || request.CompiledSearch.Context.Party is not null ||
            request.CompiledSearch.NormalizedQuery.StandardMaps.Count != 0 ||
            family is NeowFamily n && n.Coverage.Count != 1) return null;
        var invocation = FamilyCanonicalCostShape.Describe(request, family, privateInput, rootSpan);
        if (invocation is null) return null;
        string path = "CurrentFamilyBinding", body = invocation.PhysicalRevision;
        if (family is MerchantShopColorlessFamily)
        {
            // Any private port forces Atomic even if public Dense chose Stable.
            string suffix = MerchantShopColorlessGpuExecutor.PartyEarlyRejectionRevision(request.Evaluation, request.Authority.PlayersCount);
            invocation = invocation with { PhysicalRevision = (privateInput ?
                FamilyPhysicalImplementationRevisions.MerchantShopColorlessCompactPerformance :
                FamilyPhysicalImplementationRevisions.MerchantShopColorlessAtomicDensePerformance) + suffix };
            path = "AtomicAppendHostSort";
            body = FamilyPhysicalImplementationRevisions.MerchantShopColorlessAtomicAppendHostSort + suffix;
        }
        if (privateOutput && family is NeowFamily { UsesSmallPrivateOutput: true })
        {
            path += ".PrivateSmallHeader8";
            body += NeowFamilyGpuExecutor.SmallOutputRevision;
        }
        if (privateOutput && family is EventResultFamily { UsesFinalPrivateHeader: true })
        {
            path += ".PrivateFinalHeaderReadback";
            body += EventResultFamilyGpuExecutor.FinalPrivateHeaderRevision;
        }
        return new(invocation, privateInput, privateOutput, path, body);
    }

}
