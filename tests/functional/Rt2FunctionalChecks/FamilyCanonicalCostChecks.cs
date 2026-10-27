using System.Text.Json;
using System.Security.Cryptography;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

internal static class FamilyCanonicalCostChecks
{
    // Verify a current CPU quote directly against its immutable measured source.
    // Legacy CPU anchors and finite canonical references have different evidence
    // vocabularies; neither may lend a price to a different body or population.
    internal static bool MatchesEmbeddedSingleInvocation(ExactSearchExecutionRequest request,
        IFamilyInvocation family, FamilyPhysicalQuote quote, int inputs) =>
        MatchesEmbeddedSingleInvocation(request, family, quote, inputs, out _);

    internal static bool MatchesEmbeddedSingleInvocation(ExactSearchExecutionRequest request,
        IFamilyInvocation family, FamilyPhysicalQuote quote, int inputs, out string reason)
    {
        reason = "CanonicalEnvelopeOrEmbeddedProvenanceMismatch";
        const string sha = "0B9A8C7831F015B92D6847FC57EF6933F95D713F2D5D09B6307DD53FB0F6C4FB";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RT2_CANONICAL_COST_REFERENCE_PATH")) ||
            !FamilyCanonicalMeasuredCost.IsCanonicalQuote(quote) || quote.GpuCostRatio != 1 ||
            !quote.OutputAlreadyOrdered || quote.OutputElementBytes != 8 || quote.FixedWindowMilliseconds != 0 ||
            !quote.Evidence.Contains("CanonicalAssetSource=Embedded:" + FamilyCanonicalMeasuredCost.ResourceName, StringComparison.Ordinal) ||
            !quote.Evidence.Contains("CanonicalAssetSha256=" + sha, StringComparison.Ordinal)) return false;
        using var stream = typeof(FamilyCanonicalMeasuredCost).Assembly.GetManifestResourceStream(FamilyCanonicalMeasuredCost.ResourceName)
            ?? throw new InvalidDataException("CanonicalCpuQuote.EmbeddedAssetMissing");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); byte[] bytes = buffer.ToArray();
        if (Convert.ToHexString(SHA256.HashData(bytes)) != sha) { reason = "EmbeddedBytesShaMismatch"; return false; }
        var document = JsonSerializer.Deserialize<FamilyCanonicalCostReference>(bytes)
            ?? throw new InvalidDataException("CanonicalCpuQuote.EmbeddedAssetNull");
        var key = FamilyCanonicalCostShape.Describe(request, family, false, inputs);
        if (key is null || key.ExecutionClass != "CPU") { reason = "ActualCpuKeyMissing"; return false; }
        bool rowQuote = quote.Shape == "CanonicalInvoke." + key.WorkShape;
        bool curveQuote = quote.Shape == "CompleteCallCurve." + key.WorkShape;
        if ((!rowQuote && !curveQuote) || quote.FamilyId != key.FamilyId || quote.WindowCapacity != key.RootWindow ||
            quote.PublicTransportClass != "CpuOrderedAbi1")
        { reason = "ActualBodyShapeOrDenseBoundaryMismatch"; return false; }
        bool SameBody(FamilyCanonicalCostKey other) => (other with { RootWindow = 0 }).StableKey == (key with { RootWindow = 0 }).StableKey;
        bool Source(string training) => quote.Evidence.Contains("TrainingRowsSha256=" + training, StringComparison.Ordinal);
        bool SameMs(double milliseconds) => Math.Abs(quote.NanosecondsPerInput * inputs / 1e6 - milliseconds) < 1e-9;
        if (rowQuote)
        {
            var rows = document.Rows.Where(row => Source(row.TrainingRowsSha256) &&
                (row.Key.StableKey == key.StableKey || row.RootSpanMode == "DenseEqualsInputRange" && SameBody(row.Key)) &&
                inputs >= row.MinimumInputs && inputs <= row.MaximumInputs).ToArray();
            if (rows.Length != 1) { reason = $"MatchingTrainingRows={rows.Length}"; return false; }
            reason = "ExactBodyTrainingRowNumericComparison";
            return SameMs(rows[0].InterceptMilliseconds + rows[0].NanosecondsPerInput * inputs / 1e6);
        }
        var curves = document.CompleteCallCurves?.Deserialize<FamilyCompleteCallCurve[]>() ?? [];
        var matching = curves.Where(curve => SameBody(curve.Key) && Source(curve.TrainingRowsSha256) &&
            (key.FamilyId != "N.Neow" || curve.SourceSurvival is double p &&
                family.Survival.SurvivalProbability is double actual && Math.Abs(p - actual) <= Math.Max(p, actual) * 1e-10)).ToArray();
        if (matching.Length != 1) { reason = $"MatchingMeasuredCurves={matching.Length}"; return false; }
        var points = matching[0].Points.OrderBy(point => point.N).ToArray();
        var exact = points.SingleOrDefault(point => point.N == inputs);
        if (exact is not null) { reason = "ExactActualKnotNumericComparison"; return SameMs(exact.CallMilliseconds); }
        var lower = points.LastOrDefault(point => point.N < inputs);
        var upper = points.FirstOrDefault(point => point.N > inputs);
        // Existing canonical curves permit only interpolation between actual
        // admitted anchors; no guessed tail or low-input extrapolation.
        reason = lower is null || upper is null ? "NoBracketingActualAnchors" : "AdjacentActualAnchorsNumericComparison";
        return lower is not null && upper is not null && SameMs(lower.CallMilliseconds +
            (upper.CallMilliseconds - lower.CallMilliseconds) * (inputs - lower.N) / (upper.N - lower.N));
    }

    internal static int RunReference(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var embedded = typeof(FamilyCanonicalMeasuredCost).Assembly.GetManifestResourceStream(FamilyCanonicalMeasuredCost.ResourceName)
            ?? throw new InvalidDataException("PublicReference.EmbeddedResourceMissing");
        using var buffer = new MemoryStream(); embedded.CopyTo(buffer);
        if (!bytes.AsSpan().SequenceEqual(buffer.ToArray())) throw new InvalidDataException("PublicReference.EmbeddedBytesDiffer");
        string json = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var snapshot = FamilyCanonicalMeasuredCost.Parse(json);
        using var document = JsonDocument.Parse(json);
        var curves = document.RootElement.GetProperty("CompleteCallCurves").EnumerateArray()
            .Select(c => c.Deserialize<FamilyCompleteCallCurve>() ?? throw new InvalidDataException("PublicReference.NullCurve")).ToArray();
        if (curves.Length == 0 || snapshot.IsEmpty) throw new InvalidDataException("PublicReference.TrainingCurvesRequired");
        int points = 0;
        foreach (var curve in curves)
        foreach (var point in curve.Points)
        {
            var key = curve.Key.CompactInput ? curve.Key : curve.Key with { RootWindow = checked((int)point.N) };
            var quote = snapshot.Quote(key, new(key.CompactInput, false, point.N), curve.SourceSurvival);
            double? milliseconds = quote is null ? null : quote.FixedWindowMilliseconds + point.N * quote.NanosecondsPerInput / 1e6;
            if (!milliseconds.HasValue || Math.Abs(milliseconds.Value - point.CallMilliseconds) > 1e-12 * Math.Max(1, point.CallMilliseconds) ||
                quote!.PublicTransportClass is not ("CompleteCanonicalAbi1" or "CpuOrderedAbi1"))
                throw new InvalidDataException("PublicReference.ActualAnchorNotReadable:" + curve.Key.FamilyId + ":" + point.N);
            points++;
        }
        Console.WriteLine($"FamilyCompleteCallReference: embedded bytes and {curves.Length} curves/{points} actual knots passed.");
        return 0;
    }

    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new InvalidDataException("FamilyCanonicalCost:" + reason); }
        void Reject(Action read, string reason)
        {
            bool rejected = false;
            try { read(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, reason);
        }
        var key = new FamilyCanonicalCostKey(1, "E.EventResult", "CPU", "FixtureRuntime", "FixtureCpu", "", "",
            "Beta111", "FixtureCurrentBody.P4.B65536.CompactAbi1", 4, 3, true, 65536,
            "FixtureWorkShape;NoRealMeasurement", "slot0:fixture-child");
        var row = new FamilyCanonicalCostReferenceRow(key, 1, 1024, .25, 80, null, 3, 3000,
            new string('A', 64), "FixtureOnly", ["fixture-training"], "FixtureOnly;NotAProductPrice");
        var snapshot = new FamilyCanonicalCostSnapshot([row]);
        var geometry = new FamilyPhysicalQuoteRequest(true, false, 32);
        var quote = snapshot.Quote(key, geometry);
        Check(quote is not null && quote.PublicTransportClass == "CpuOrderedAbi1" && quote.LocalCostSource == "LocalMeasurement",
            "CPU complete quote is absolute and ordered");
        Check(FamilyCanonicalMeasuredCost.IsCanonicalQuote(quote) &&
            !FamilyCanonicalMeasuredCost.IsCanonicalQuote(null) &&
            !FamilyCanonicalMeasuredCost.IsCanonicalQuote(quote! with { Evidence = "UnrelatedLocalMeasurement" }),
            "canonical boundary marker does not confuse transport or local measurement with finite reference authority");
        Check(Math.Abs(quote!.NanosecondsPerInput * 32 / 1e6 + quote.FixedWindowMilliseconds - (.25 + 32 * 80d / 1e6)) < 1e-12 &&
            quote.FixedWindowMilliseconds == 0, "floor is folded once at requested geometry for existing comparison consumers");
        Check(quote.SetupMilliseconds is null && quote.ReferencePeakThroughput == 0 && quote.GpuCostRatio == 1,
            "excluded startup does not invent setup or peak/ratio evidence");
        Check(quote.Evidence.Contains("ActualMeasurementCadence=HistoricalUnspecified;CurrentCadenceAdmission=CpuCadenceUnrestricted", StringComparison.Ordinal),
            "old CPU rows keep their real context and remain usable without a GPU cadence claim");
        foreach (double n in new[] { 0, .5, 1025, double.NaN, double.PositiveInfinity })
            Check(snapshot.Quote(key, geometry with { MeanInputPopulation = n }) is null, "finite measured population range:" + n);
        foreach (var other in new[] {
            key with { PhysicalRevision = "ChangedBody" }, key with { ConfiguredWorkers = 1 },
            key with { CpuIdentity = "AnotherCpu" }, key with { RuntimeTarget = "AnotherRuntime" },
            key with { Players = 4 }, key with { RootWindow = 1024 },
            key with { WorkShape = "AnotherSourceShape" }, key with { OrderedChildBodies = "ChangedChild" } })
            Check(snapshot.Quote(other, geometry) is null, "body/worker/device/players/rootspan/shape/child keys cannot borrow price");
        Check(snapshot.Quote(key, geometry with { PrivateInput = true }) is null &&
            snapshot.Quote(key, geometry with { PrivateOutput = true }) is null &&
            snapshot.Quote(key, geometry with { CompactInput = false }) is null,
            "public complete wrapper evidence cannot price private bind or another geometry");
        var point = new FamilyCanonicalCostSnapshot([row with { MinimumInputs = 32, MaximumInputs = 32 }]);
        Check(point.Quote(key, geometry) is not null && point.Quote(key, geometry with { MeanInputPopulation = 31 }) is null &&
            point.Quote(key, geometry with { MeanInputPopulation = 33 }) is null, "one population anchor never extrapolates");
        foreach (var invalid in new[] {
            row with { InterceptMilliseconds = -1 }, row with { NanosecondsPerInput = double.NaN },
            row with { SetupMilliseconds = double.PositiveInfinity }, row with { MinimumInputs = 0 },
            row with { MaximumInputs = 65537 }, row with { MeasuredMilliseconds = 0 },
            row with { MeasuredRepeats = 0 }, row with { HeldoutUsedForFit = true },
            row with { TrainingRowsSha256 = "missing" }, row with { TrainingCases = [] } })
            Reject(() => _ = new FamilyCanonicalCostSnapshot([invalid]), "invalid model row cannot become price");
        Reject(() => _ = new FamilyCanonicalCostSnapshot([row, row with { MinimumInputs = 1024, MaximumInputs = 2048 }]),
            "overlapping closed ranges are ambiguous");
        var document = new FamilyCanonicalCostReference(1, FamilyCanonicalMeasuredCost.Boundary, [row]);
        var parsed = FamilyCanonicalMeasuredCost.Parse(JsonSerializer.Serialize(document));
        Check(parsed.Quote(key, geometry) is not null, "finite reference JSON round trip");
        foreach (int workers in new[] { 33, 64 })
        {
            var budgetKey = key with { ConfiguredWorkers = workers };
            var budgetRow = row with { Key = budgetKey };
            var budgetSnapshot = FamilyCanonicalMeasuredCost.Parse(JsonSerializer.Serialize(document with { Rows = [budgetRow] }));
            var budgetQuote = budgetSnapshot.Quote(budgetKey, geometry);
            Check(budgetQuote is not null && budgetQuote.NanosecondsPerInput == quote.NanosecondsPerInput &&
                budgetQuote.FixedWindowMilliseconds == quote.FixedWindowMilliseconds,
                "Legal high-budget canonical evidence is not scaled:" + workers);
            Check(snapshot.Quote(budgetKey, geometry) is null && budgetSnapshot.Quote(key, geometry) is null,
                "Canonical budget keys never borrow another row:" + workers);
        }
        Reject(() => _ = FamilyCanonicalMeasuredCost.Parse(JsonSerializer.Serialize(document with {
            Rows = [row with { Key = key with { ConfiguredWorkers = 65 } }] })),
            "Canonical budget65 exceeds the request contract");
        Reject(() => _ = FamilyCanonicalMeasuredCost.Parse(JsonSerializer.Serialize(document with { Boundary = "DispatchSyncPeak" })),
            "another measurement boundary cannot load as canonical wall");
        Reject(() => _ = FamilyCanonicalMeasuredCost.Parse(JsonSerializer.Serialize(document with { SchemaVersion = 2 })),
            "unknown schema cannot silently load");
        var gpuKey = key with { ExecutionClass = "GPU", GpuIdentity = "FixtureGpu", RenderingBackend = "d3d12" };
        var gpuRow = row with { Key = gpuKey, MeasurementCadence = FamilyCanonicalMeasuredCost.LiveMainLoopCadence };
        var gpuSnapshot = new FamilyCanonicalCostSnapshot([gpuRow]);
        Check(gpuSnapshot.Quote(gpuKey, geometry)?.PublicTransportClass == "CompleteCanonicalAbi1" &&
            gpuSnapshot.Quote(gpuKey with { RenderingBackend = "vulkan" }, geometry) is null &&
            gpuSnapshot.Quote(gpuKey with { GpuIdentity = "AnotherGpu" }, geometry) is null,
            "GPU canonical boundary and exact backend/device remain separate");
        Check(gpuSnapshot.HasShapeReference(gpuKey) && gpuSnapshot.Quote(gpuKey, geometry)!.Evidence.Contains(
            "ActualMeasurementCadence=LiveMainLoop;CurrentCadenceAdmission=ExplicitLiveMainLoop", StringComparison.Ordinal),
            "GPU finite price and shape presence require and disclose explicit Live cadence");
        Check(gpuSnapshot.Quote(gpuKey, geometry with { PrivateInput = true }) is null &&
            gpuSnapshot.Quote(gpuKey, geometry with { PrivateOutput = true }) is null,
            "public single-family GPU measurement never becomes a measured private binding quote");
        var historicalGpu = row with { Key = gpuKey, InterceptMilliseconds = 50 };
        foreach (string cadence in new[] { FamilyCanonicalMeasuredCost.HistoricalCadence, FamilyCanonicalMeasuredCost.BlockedMainLoopCadence })
        {
            var history = historicalGpu with { MeasurementCadence = cadence };
            var historyOnly = new FamilyCanonicalCostSnapshot([history, history]);
            Check(historyOnly.IsEmpty && historyOnly.Quote(gpuKey, geometry) is null && !historyOnly.HasShapeReference(gpuKey),
                "historical GPU prices and even overlapping historical evidence do not enter current indexes:" + cadence);
            var combined = new FamilyCanonicalCostSnapshot([history, gpuRow]);
            Check(combined.Quote(gpuKey, geometry)!.NanosecondsPerInput == gpuSnapshot.Quote(gpuKey, geometry)!.NanosecondsPerInput &&
                combined.Quote(key, geometry) is null, "same-key historical and Live rows coexist without mixing prices:" + cadence);
        }
        Reject(() => _ = new FamilyCanonicalCostSnapshot([gpuRow, gpuRow]), "two Live GPU ranges still reject ambiguous overlap");
        var oldGpuJson = JsonSerializer.SerializeToNode(document with { Rows = [historicalGpu, row] })!;
        oldGpuJson["Rows"]![0]!.AsObject().Remove("MeasurementCadence");
        var oldGpuParsed = FamilyCanonicalMeasuredCost.Parse(oldGpuJson.ToJsonString());
        Check(oldGpuParsed.Quote(gpuKey, geometry) is null && !oldGpuParsed.HasShapeReference(gpuKey) &&
            oldGpuParsed.Quote(key, geometry) is not null && oldGpuParsed.HasShapeReference(key),
            "missing optional cadence parses as history without discarding another valid CPU row");
        foreach (string? invalid in new string?[] { "GuessLive", "", null })
        {
            var mixed = new FamilyCanonicalCostSnapshot([row, gpuRow, gpuRow with { MeasurementCadence = invalid! }]);
            Check(mixed.Quote(key, geometry) is not null && mixed.Quote(gpuKey, geometry) is not null,
                "invalid cadence is diagnosed and omitted without invalidating unrelated current rows");
        }

        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        FamilyCanonicalCostKey EventKey(TinkerRiderTarget secondRider)
        {
            var query = SearchQuery.Empty with { EventResultConditions = [
                new(EventResultConditionKind.TinkerTimeTypeAndRider, new ModelKey("CARD", "MAD_SCIENCE"))
                    { TinkerCardType = TinkerCardTypeTarget.Attack, TinkerRider = TinkerRiderTarget.Sapping },
                new(EventResultConditionKind.TinkerTimeTypeAndRider, new ModelKey("CARD", "MAD_SCIENCE"))
                    { TinkerCardType = TinkerCardTypeTarget.Skill, TinkerRider = secondRider }] };
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                new("000000000000", 65536, 30, 1)).Plan ?? throw new InvalidDataException("CanonicalCostFixture.EventCompile");
            if (!EventResultFamily.TryCreate(request, out var family, false) || family is null)
                throw new InvalidDataException("CanonicalCostFixture.EventFamily");
            return FamilyCanonicalCostShape.Describe(request, family, false, 65536)
                ?? throw new InvalidDataException("CanonicalCostFixture.EventDescriptor");
        }
        var sameRiderIndex = EventKey(TinkerRiderTarget.Energized);
        var differentRiderIndex = EventKey(TinkerRiderTarget.Wisdom);
        Check(sameRiderIndex.WorkShape.StartsWith("E.SourceGroups.v2", StringComparison.Ordinal) &&
            sameRiderIndex.StableKey != differentRiderIndex.StableKey,
            "shared post-type rider indices preserve source correlation rather than only alpha-renaming full enum values");

        EventResultSearchCondition[] CheapPair(TrialCaseTarget trial, TinkerCardTypeTarget type, TinkerRiderTarget? rider) => [
            new(EventResultConditionKind.TrialCase, new ModelKey("EVENT", "TRIAL")) { TrialCase = trial },
            new(EventResultConditionKind.TinkerTimeTypeAndRider, new ModelKey("CARD", "MAD_SCIENCE"))
                { TinkerCardType = type, TinkerRider = rider }];
        FamilyCanonicalCostKey CheapKey(EventResultSearchCondition[] conditions)
        {
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(
                SearchQuery.Empty with { EventResultConditions = conditions }, context), new("000000000000", 65536, 30, 1)).Plan
                ?? throw new InvalidDataException("CanonicalCostFixture.CheapCompile");
            if (!EventResultFamily.TryCreate(request, out var family, false) || family is null)
                throw new InvalidDataException("CanonicalCostFixture.CheapFamily");
            return FamilyCanonicalCostShape.Describe(request, family, false, 65536)
                ?? throw new InvalidDataException("CanonicalCostFixture.CheapDescriptor");
        }
        var firstPair = CheapPair(TrialCaseTarget.Merchant, TinkerCardTypeTarget.Attack, TinkerRiderTarget.Sapping);
        var renamedPair = CheapPair(TrialCaseTarget.Nondescript, TinkerCardTypeTarget.Power, TinkerRiderTarget.Improvement);
        var firstCheap = CheapKey(firstPair);
        Check(firstCheap.StableKey == CheapKey(renamedPair).StableKey && firstCheap.WorkShape.StartsWith(
            "E.CheapTrialTinker.SourceCost.20261021.v1;slot=0;", StringComparison.Ordinal),
            "one compatible Trial/Tinker pair shares a source cost class across comparison constants");
        var sourceClass = FamilyCanonicalCostShape.CheapTrialTinkerCostShape(0, firstPair);
        Check(sourceClass is not null && sourceClass != FamilyCanonicalCostShape.CheapTrialTinkerCostShape(1, firstPair) &&
            sourceClass != FamilyCanonicalCostShape.CheapTrialTinkerCostShape(0, Enumerable.Reverse(firstPair).ToArray()),
            "the cost class preserves actual RNG owner slot and condition order");
        Check(FamilyCanonicalCostShape.CheapTrialTinkerCostShape(0,
            CheapPair(TrialCaseTarget.Merchant, TinkerCardTypeTarget.Attack, null)) is null &&
            CheapKey(CheapPair(TrialCaseTarget.Merchant, TinkerCardTypeTarget.Attack, null)).WorkShape.StartsWith(
                "E.SourceGroups.v2", StringComparison.Ordinal), "rider absence retains its distinct old geometry");
        Check(FamilyCanonicalCostShape.CheapTrialTinkerCostShape(0,
            CheapPair(TrialCaseTarget.Merchant, TinkerCardTypeTarget.Attack, TinkerRiderTarget.Energized)) is null &&
            FamilyCanonicalCostShape.CheapTrialTinkerCostShape(0,
                CheapPair((TrialCaseTarget)3, TinkerCardTypeTarget.Attack, TinkerRiderTarget.Sapping)) is null &&
            FamilyCanonicalCostShape.CheapTrialTinkerCostShape(0,
                CheapPair(TrialCaseTarget.Merchant, (TinkerCardTypeTarget)3, (TinkerRiderTarget)9)) is null,
            "incompatible or undefined enum tuples cannot borrow the source cost class");
        Check(FamilyCanonicalCostShape.CheapTrialTinkerCostShape(0, [..firstPair,
            new(EventResultConditionKind.TinkerTimeTypeAndRider, new ModelKey("CARD", "MAD_SCIENCE")) {
                TinkerCardType = TinkerCardTypeTarget.Skill, TinkerRider = TinkerRiderTarget.Wisdom }]) is null &&
            sameRiderIndex.WorkShape.StartsWith("E.SourceGroups.v2", StringComparison.Ordinal),
            "additional predicates preserve existing correlation-sensitive descriptors");

        // Descriptor-only construction: no matcher, worker or GPU is invoked.
        // This is the tr09 source structure, independent of its positive seed.
        ExactSearchExecutionRequest CapsuleRequest(ModelKey otherRelic)
        {
            var filter = NeowSearchFilter.Empty with {
                NeowRoute = new(BaseGameModelKeys.Relics.LargeCapsule),
                StructuredNeowEffects = [new(BaseGameModelKeys.Relics.LargeCapsule,
                    NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.NestedRelics,
                    NeowStructuredOutputKind.Relic, [otherRelic, BaseGameModelKeys.OrdinaryRelics.Whetstone])] };
            return ExactSearchExecutionRequestFactory.Compile(
                SearchCompiler.Compile(LegacySearchQueryAdapter.FromFilter(filter), context),
                new("000000000000", 65536, 30, 4)).Plan
                ?? throw new InvalidDataException("CanonicalCostFixture.CapsuleCompile");
        }
        var capsuleRequest = CapsuleRequest(new ModelKey("RELIC", "BLOOD_VIAL"));
        var authoredPlan = NeowReplayPlan.Compile(capsuleRequest, false);
        var advances = authoredPlan.AuthoredUpgrades?.Advances
            ?? throw new InvalidDataException("CanonicalCostFixture.AuthoredContinuation");
        Check(advances.Length == 66 && advances.Any(n => n >= 0),
            "source compiler supplies the finite Small/Large prior-opcode operation vector");
        FamilyCanonicalCostKey? NeowKey(NeowReplayPlan plan) => FamilyCanonicalCostShape.Describe(
            capsuleRequest, new NeowFamily(capsuleRequest, plan, preferGpu: false), false, 65536);
        var authoredKey = NeowKey(authoredPlan)
            ?? throw new InvalidDataException("CanonicalCostFixture.AuthoredDescriptor");
        var legacyNeow = NeowKey(authoredPlan with { AuthoredUpgrades = null })
            ?? throw new InvalidDataException("CanonicalCostFixture.LegacyNeowDescriptor");
        const string continuationSuffix = ";AuthoredUpgradeContinuation.v1:";
        int suffixAt = authoredKey.WorkShape.IndexOf(continuationSuffix, StringComparison.Ordinal);
        Check(suffixAt > 0 && !legacyNeow.WorkShape.Contains(continuationSuffix, StringComparison.Ordinal) &&
            authoredKey.WorkShape[..suffixAt].Replace("\"HasAuthoredUpgrades\":true", "\"HasAuthoredUpgrades\":false",
                StringComparison.Ordinal) == legacyNeow.WorkShape,
            "non-authored N keeps its exact legacy JSON bytes and receives no null field or suffix");
        using (var vector = JsonDocument.Parse(authoredKey.WorkShape[(suffixAt + continuationSuffix.Length)..]))
        {
            var source = vector.RootElement;
            Check(source.GetProperty("Advances").EnumerateArray().Select(n => n.GetInt32()).SequenceEqual(advances) &&
                source.GetProperty("PriorCount").GetInt32() == 33 &&
                source.GetProperty("SmallSourceOpcode").GetInt32() == Beta110FastRelicCatalog.SmallCapsule &&
                source.GetProperty("LargeSourceOpcode").GetInt32() == Beta110FastRelicCatalog.LargeCapsule &&
                source.GetProperty("NoPriorOpcode").GetInt32() == 255 &&
                source.GetProperty("UpperBound").GetInt32() == authoredPlan.CapsuleUpgradeUpperBound,
                "authored suffix preserves actual operation counts, dispatch positions and unknown sentinel");
        }
        int changedAt = Array.FindIndex(advances, n => n >= 0);
        var changedAdvances = advances.ToArray(); changedAdvances[changedAt]++;
        var otherOpcodeAdvances = advances.ToArray(); otherOpcodeAdvances[(changedAt + 1) % advances.Length]++;
        var unknownAdvances = advances.ToArray(); unknownAdvances[changedAt] = -1;
        Check(NeowKey(authoredPlan with { AuthoredUpgrades = new(changedAdvances) })?.WorkShape != authoredKey.WorkShape &&
            NeowKey(authoredPlan with { AuthoredUpgrades = new(unknownAdvances) })?.WorkShape != authoredKey.WorkShape &&
            NeowKey(authoredPlan with { AuthoredUpgrades = new(unknownAdvances) }) is not null,
            "advance count changes and real conservative unknown branches have distinct structural keys");
        Check(NeowKey(authoredPlan with { AuthoredUpgrades = new(changedAdvances) })?.WorkShape !=
            NeowKey(authoredPlan with { AuthoredUpgrades = new(otherOpcodeAdvances) })?.WorkShape,
            "equal aggregate advances at different prior opcodes cannot alias");
        var malformedAdvances = advances.ToArray(); malformedAdvances[changedAt] = -2;
        Check(NeowKey(authoredPlan with { AuthoredUpgrades = new(advances[..65]) }) is null &&
            NeowKey(authoredPlan with { AuthoredUpgrades = new(malformedAdvances) }) is null,
            "unclosed operation vectors remain unpriced rather than filling missing or invalid operands");
        var renamedRequest = CapsuleRequest(new ModelKey("RELIC", "ANCHOR"));
        var renamedPlan = NeowReplayPlan.Compile(renamedRequest, false);
        var renamedKey = FamilyCanonicalCostShape.Describe(renamedRequest,
            new NeowFamily(renamedRequest, renamedPlan, preferGpu: false), false, 65536);
        Check(renamedKey?.WorkShape == authoredKey.WorkShape,
            "replacing a non-upgrade R output target does not memorize its identity in independent N work");
        var authoredOwner = new NeowFamily(capsuleRequest, authoredPlan, preferGpu: false);
        var configuredN = FamilyCpuExecution.ConfiguredRealizations(authoredOwner).Single();
        Check(FamilyCanonicalCostShape.Describe(capsuleRequest, configuredN, false, 65536)?.WorkShape ==
            authoredKey.WorkShape + ((FamilyCpuExecution)configuredN).OptimizationCostShape,
            "configured CPU wrapper preserves authored N work and describes its actual four-way gate");
        var gpuN = new NeowFamily(capsuleRequest, authoredPlan);
        Check(gpuN.ConditionPerformance.UsesGpu &&
            FamilyCanonicalCostShape.Describe(capsuleRequest, gpuN, false, 65536)?.WorkShape.Contains(
                continuationSuffix, StringComparison.Ordinal) == true,
            "GPU numeric plan gets the same authored operation-vector suffix without invoking GPU");
        CheckMeasuredNeowTransportEvidence(gpuN, authoredOwner, context, Check);
        foreach (string initialCoverage in new[] { InitialRoomsInvocation.TreasureCoverage, InitialRoomsInvocation.UnknownCoverage })
        {
            var passed = new HashSet<string>(StringComparer.Ordinal) { "R.Relic", initialCoverage };
            Check(!gpuN.ResolveSurvival(passed).IsResolved && gpuN.ResolveExpectedFilteringCost(passed)?.IsResolved == false,
                "new initial-room passed population never borrows the legacy R conditional denominator: " + initialCoverage);
        }
        CheckCompleteCallCurves(Check);
        CheckIntegratedConsumers(Check);
        Console.WriteLine($"FamilyCanonicalCost checks={checks};PASS");
        return 0;
    }

    // Actual Main compile/registration/selection and ETA paths consume the
    // embedded asset. The reference key below is produced by the real owner.
    private static void CheckIntegratedConsumers(Action<bool, string> check)
    {
        const string expectedSha = "0B9A8C7831F015B92D6847FC57EF6933F95D713F2D5D09B6307DD53FB0F6C4FB";
        check(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RT2_CANONICAL_COST_REFERENCE_PATH")),
            "integrated consumer proof must use the actual embedded asset");
        using var stream = typeof(FamilyExecutionCoordinator).Assembly.GetManifestResourceStream(FamilyCanonicalMeasuredCost.ResourceName)
            ?? throw new InvalidDataException("CanonicalCostFixture.EmbeddedAssetMissing");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        check(Convert.ToHexString(SHA256.HashData(bytes)) == expectedSha, "embedded frozen candidate identity");
        var document = JsonSerializer.Deserialize<FamilyCanonicalCostReference>(bytes)
            ?? throw new InvalidDataException("CanonicalCostFixture.EmbeddedAssetNull");
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var query = SearchQuery.Empty with { EventResultConditions = [
            new(EventResultConditionKind.TrashHeapGrabCard, Beta111EventResultCatalog.TrashHeapGrabCards[0])] };
        var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
            new("000000000000", 65536, 30, 1)).Plan
            ?? throw new InvalidDataException("CanonicalCostFixture.IntegratedCompile");
        var plan = FamilyExecutionCoordinator.Plan(request, gpuAvailable: false);
        check(plan.OrderedFamilies.Count == 1 && plan.OrderedFamilies[0] is FamilyCpuExecution,
            "real default registration selects the existing CPU owner");
        var family = plan.OrderedFamilies.Single();
        var actualKey = FamilyCanonicalCostShape.Describe(request, family, false, 65536)
            ?? throw new InvalidDataException("CanonicalCostFixture.ActualKeyMissing");
        var curves = document.CompleteCallCurves?.Deserialize<FamilyCompleteCallCurve[]>() ?? [];
        CheckInitialRoomsConsumers(context, curves, check);
        CheckOrdinaryReferenceConsumers(context, curves, check);
        CheckCombatReferenceIdentity(context, curves, check);
        var curve = curves.Single(c => (c.Key with { RootWindow = actualKey.RootWindow }).StableKey == actualKey.StableKey);
        var knot = curve.Points.Single(point => point.N == 65536);
        var quote = family.QuotePhysicalWork(new(false, false, 65536));
        bool Provenance(string evidence) => evidence.Contains("ActualCompleteCallKnots", StringComparison.Ordinal) &&
            evidence.Contains("CanonicalAssetSource=Embedded:", StringComparison.Ordinal) &&
            evidence.Contains("CanonicalAssetSha256=" + expectedSha, StringComparison.Ordinal) &&
            evidence.Contains("TrainingRowsSha256=" + curve.TrainingRowsSha256, StringComparison.Ordinal) &&
            evidence.Contains("AnchorN=65536..65536", StringComparison.Ordinal);
        check(quote is not null && Provenance(quote.Evidence) &&
            Math.Abs(quote.NanosecondsPerInput * 65536 / 1e6 - knot.CallMilliseconds) < 1e-9,
            "actual CPU quote uses the embedded measured knot and row provenance");
        check(quote is not null && MatchesEmbeddedSingleInvocation(request, family, quote, 65536),
            "shared provenance check accepts the real CompleteCallCurve identity at its measured knot");
        var cpuGeometry = new FamilyPhysicalQuoteRequest(false, false, 65536);
        check(FamilyGpuReadback.ObservationRevision(family, cpuGeometry) == "" &&
            GpuReferenceCostAtlas.PhysicalRevision(family, cpuGeometry) == family.ConditionPerformance.PhysicalImplementationRevision &&
            ReferenceEquals(FamilyGpuReadback.Reference(family, quote), quote) &&
            ReferenceEquals(new GpuCostSnapshot([]).Local(family, quote!, cpuGeometry), quote) &&
            !quote!.Evidence.Contains(FamilyGpuReadback.HostTransferRevision, StringComparison.Ordinal) &&
            !quote.Evidence.Contains("CurrentHostTransportUnmeasured", StringComparison.Ordinal),
            "actual CPU canonical price and observation identity remain free of GPU transport revisions and disclaimers");
        var dense64 = family.QuotePhysicalWork(new(false, false, 64));
        check(dense64 is not null && MatchesEmbeddedSingleInvocation(request, family, dense64, 64) &&
            dense64.Evidence.Contains("AnchorN=1..1024", StringComparison.Ordinal),
            "Dense64 keeps its actual bracketing-anchor curve source without requiring a fitted row identity");
        var singleton = FamilyCanonicalMeasuredCost.TryInvocation(family, 65536, false, 65536);
        check(singleton is not null && Math.Abs(singleton.Milliseconds - knot.CallMilliseconds) < 1e-9 &&
            Provenance(singleton.Evidence), "real singleton unit consumes the complete measured invocation once");
        // The simple default ranks the single-call reference directly; a whole
        // allocation delegate is no longer required to admit or order filters.
        var defaultMs = plan.Stages.Single().PerInputTimeMs * 65536;
        check(defaultMs.HasValue && Math.Abs(defaultMs.Value - knot.CallMilliseconds) < 1e-9,
            "real default single-stage score consumes the same embedded knot");
        var eta = FamilySearchEtaProjectorV1.Project(request, plan);
        check(eta.TargetStages.Count == 1 && eta.TargetStages[0].CostEvidence.Contains(
            "CanonicalAssetSha256=" + expectedSha, StringComparison.Ordinal) &&
            eta.TargetStages[0].CostEvidence.Contains("ActualCompleteCallKnots", StringComparison.Ordinal) &&
            eta.TargetStages[0].CostEvidence.Contains("TrainingRowsSha256=" + curve.TrainingRowsSha256, StringComparison.Ordinal),
            "actual ETA projection retains the embedded complete-knot provenance");
        Console.WriteLine("FamilyCanonicalCost integratedConsumers=Default,Singleton,Eta;embeddedSha256=" + expectedSha +
            ";actualBody=" + actualKey.PhysicalRevision + ";trainingRowsSha256=" + curve.TrainingRowsSha256 +
            ";knotN=65536;sourceCase=" + string.Join(',', curve.TrainingCases));
    }

    private static void CheckCombatReferenceIdentity(SearchContext context, FamilyCompleteCallCurve[] curves,
        Action<bool, string> check)
    {
        const int roots = 1 << 24;
        var references = curves.Where(c => c.Key.ExecutionClass == "GPU" &&
            c.TrainingCases.Contains("r43t-c-six-terminal")).ToArray();
        check(references.Length == 2, "retained solo C source has both public Dense and Compact references");
        var reference = references.Single(c => !c.Key.CompactInput);
        var oldDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        string? oldCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        try
        {
            // Replay only the frozen reference identity; no numerical dispatch
            // and no measurement or new same-run price is made here.
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", reference.Key.CpuIdentity);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(reference.Key.GpuIdentity, reference.Key.RenderingBackend);
            var card = context.Authority.EffectAuthority!.CharacterRewardPool!
                .First(c => c.IsUnlockedInCapturedPool && c.Rarity == EffectCardRarity.Common).CardKey;
            var query = SearchQuery.Empty with { CombatCardRewards = new(6,
                CombatRewardSequenceOrderMode.Ordered, [null, null, null, null, null, card]) };
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                new("000000000000", roots, 30, reference.Key.ConfiguredWorkers)).Plan
                ?? throw new InvalidDataException("CanonicalCombat.Compile");
            var family = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true, true)
                .OfType<CombatRewardFamily>().Single();
            check(!family.Replay.UsesQueryLiteralPartyOpening && !family.Replay.Plan.NeedsCompleteMatchingReference,
                "equivalent solo C remains outside both changed-body price scopes");
            foreach (bool compact in new[] { false, true })
            {
                var curve = references.Single(c => c.Key.CompactInput == compact);
                var key = FamilyCanonicalCostShape.Describe(request, family, compact, roots)!;
                check(key.StableKey == (curve.Key with { RootWindow = roots }).StableKey,
                    "retired latent runtime fields preserve the exact unchanged solo C measurement key");
                using var shape = JsonDocument.Parse(key.WorkShape[(key.WorkShape.IndexOf(':') + 1)..]);
                check(!shape.RootElement.GetProperty("ReplayActualBonesPair").GetBoolean() &&
                    !shape.RootElement.GetProperty("DynamicCapsuleNicheUnknown").GetBoolean() &&
                    !shape.RootElement.GetProperty("CapsuleHeldReplay").GetBoolean() &&
                    !shape.RootElement.TryGetProperty("UsesQueryLiteralPartyOpening", out _),
                    "legacy solo shape retains only constant-false historical fields without a new-body selector");
                var knot = curve.Points.Single(point => point.N == roots);
                var quote = FamilyCanonicalMeasuredCost.TryQuote(family, new(compact, false, roots), roots);
                var ranking = new GpuCostSnapshot([]).Reference(family, family.ResolveConditionPerformance(compact), compact);
                check(FamilyCanonicalMeasuredCost.IsCanonicalQuote(quote) &&
                    Math.Abs(quote!.NanosecondsPerInput * roots / 1e6 - knot.CallMilliseconds) < 1e-9 &&
                    ranking.Evidence.Contains("TrainingRowsSha256=" + curve.TrainingRowsSha256, StringComparison.Ordinal) &&
                    Math.Abs(ranking.CurrentQueryReference - roots * 1000d / knot.CallMilliseconds) < 1e-6,
                    "unchanged C Dense/Compact quote and ranking still consume the original measured source");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", oldCpu);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(oldDevice.GpuIdentity, oldDevice.RenderingBackend);
        }
    }

    private static void CheckInitialRoomsConsumers(SearchContext context, FamilyCompleteCallCurve[] curves,
        Action<bool, string> check)
    {
        // Replay the finite reference's declared device identity without creating
        // a GPU. These assertions check price lookup, not hardware execution.
        var reference = curves.First(c => c.Key.PhysicalRevision.Contains(".InitialUnknown.", StringComparison.Ordinal));
        var oldDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        string? oldCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        try
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", reference.Key.CpuIdentity);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(reference.Key.GpuIdentity, reference.Key.RenderingBackend);
            var generation = context.Authority.WorldAuthority!.Beta109Generation!;
            var rare = generation.SharedRelicBuckets.Single(b => b.BucketId.EndsWith(":Rare", StringComparison.Ordinal)).OrderedRelics[0];
            var common = generation.SharedRelicBuckets.Single(b => b.BucketId.EndsWith(":Common", StringComparison.Ordinal)).OrderedRelics[0];
            var treasure = SearchQuery.Empty with { TreasureRelics = new(1, 2, TreasureRelicOrderMode.Unordered, [rare, common]) };
            var relic = treasure with { RelicSequenceConstraints = [new(RelicSequenceKind.Common,
                SearchSequenceRangeMode.ExactSlot, 2, new([], [common], []))] };
            var unknown = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 1 };
            var variant = SearchQuery.Empty with { VariantBossBranches = [new(1,
                generation.ActSelectionGroups.Single(g => g.Act == 1).EligibleActsInSourceOrder[0],
                new([], [], []), new([], [], []), false)] };
            foreach (var query in new[] { treasure, relic, unknown, variant with { FirstActUnknownEventPrefixCount = 1 } })
            {
                var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                    new("000000000000", 65536, 30, 16)).Plan ?? throw new InvalidDataException("CanonicalInitialRooms.Compile");
                var native = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true, true).OfType<InitialRoomsInvocation>().Single();
                var integer = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true, false).OfType<InitialRoomsInvocation>().Single();
                foreach (bool compact in new[] { false, true })
                {
                    int roots = compact ? 1 << 24 : 65536;
                    var nativeKey = FamilyCanonicalCostShape.Describe(request, native, compact, roots)!;
                    var integerKey = FamilyCanonicalCostShape.Describe(request, integer, compact, roots)!;
                    var curve = curves.SingleOrDefault(c => (c.Key with { RootWindow = roots }).StableKey == nativeKey.StableKey)
                        ?? throw new InvalidDataException("CanonicalInitialRooms.ReferenceShapeMissing:" + nativeKey.StableKey);
                    var knot = curve.Points.Single(p => p.N == 65536);
                    var quote = FamilyCanonicalMeasuredCost.TryQuote(native, new(compact, false, 65536), roots);
                    check(quote is not null && Math.Abs(quote.FixedWindowMilliseconds +
                        quote.NanosecondsPerInput * 65536 / 1e6 - knot.CallMilliseconds) < 1e-9,
                        "native InitialRooms complete reference survives integration:" + nativeKey.PhysicalRevision);
                    if (compact && nativeKey.PhysicalRevision.Contains(".InitialTreasure.", StringComparison.Ordinal))
                    {
                        var fullKnot = curve.Points.Single(p => p.N == roots);
                        var ordinary = new GpuCostSnapshot([]).Reference(native, native.ResolveConditionPerformance(true), true);
                        check(Math.Abs(ordinary.CurrentQueryReference - roots * 1000d / fullKnot.CallMilliseconds) < 1e-6 &&
                            ordinary.Evidence.Contains("TrainingRowsSha256=" + curve.TrainingRowsSha256, StringComparison.Ordinal) &&
                            ordinary.Evidence.Contains("ReferenceRootWindow=16777216;ReferenceInputPopulation=16777216", StringComparison.Ordinal),
                            "ordinary Compact treasure reference consumes its real 16Mi root/population measured knot");
                        check(FamilyCanonicalMeasuredCost.TryDenseQuote(native, new(true, false, roots)) is null &&
                            FamilyCanonicalMeasuredCost.TryQuote(native, new(true, false, roots), 65536) is null,
                            "Compact treasure still needs the actual root window and never derives it from its mean");
                    }
                    check(nativeKey.PhysicalRevision != integerKey.PhysicalRevision && nativeKey.WorkShape != integerKey.WorkShape &&
                        FamilyCanonicalMeasuredCost.TryQuote(integer, new(compact, false, 65536), roots) is null &&
                        integer.QuotePhysicalWork(new(false, false, 65536)) is null,
                        "integer InitialRooms actual body/shape cannot borrow native price");
                    if (native.Ordinary is not null)
                    {
                        check(nativeKey.OrderedChildBodies.Length > 0 && nativeKey.OrderedChildBodies != integerKey.OrderedChildBodies ||
                            native.Ordinary.GpuNumericRequirements == GpuNumericRequirements.Int64,
                            "wrapper cost names its actual child body");
                        if (nativeKey.OrderedChildBodies != integerKey.OrderedChildBodies)
                        {
                            var mixed = new InitialRoomsInvocation(request, native.FamilyId, native.Plan, integer.Ordinary, true,
                                ordinaryRequest: integer.PricingOrdinaryRequest);
                            var mixedKey = FamilyCanonicalCostShape.Describe(request, mixed, compact, roots)!;
                            check(mixedKey.PhysicalRevision == nativeKey.PhysicalRevision && mixedKey.WorkShape == nativeKey.WorkShape &&
                                mixedKey.OrderedChildBodies != nativeKey.OrderedChildBodies &&
                                FamilyCanonicalMeasuredCost.TryQuote(mixed, new(compact, false, 65536), roots) is null,
                                "changed child alone invalidates the complete native wrapper price");
                        }
                    }
                }
                var cpu = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false).OfType<InitialRoomsInvocation>().Single();
                var fallback = FamilyCpuExecution.ConfiguredRealizations(integer).Single();
                check(cpu.ConditionPerformance.PhysicalImplementationRevision == fallback.ConditionPerformance.PhysicalImplementationRevision &&
                    !cpu.ConditionPerformance.PhysicalImplementationRevision.Contains("IntegerRne", StringComparison.Ordinal) &&
                    cpu.QuotePhysicalWork(new(false, false, 65536)) is null && fallback.QuotePhysicalWork(new(false, false, 65536)) is null,
                    "unchanged CPU realization retains its identity and existing unpriced boundary");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", oldCpu);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(oldDevice.GpuIdentity, oldDevice.RenderingBackend);
        }
    }

    private static void CheckOrdinaryReferenceConsumers(SearchContext context, FamilyCompleteCallCurve[] curves,
        Action<bool, string> check)
    {
        const int roots = 1 << 24, cpuInputs = 65536;
        var reference = curves.First(c => c.TrainingCases.Contains("r44t-e-grab") && !c.Key.CompactInput);
        var oldDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        string? oldCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        try
        {
            // Only replay price identity. No GPU execution or new measurement.
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", reference.Key.CpuIdentity);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(reference.Key.GpuIdentity, reference.Key.RenderingBackend);
            ExactSearchExecutionRequest Request(SearchQuery query, int workers = 16) =>
                ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                    new("000000000000", roots, 30, workers)).Plan ?? throw new InvalidDataException("CanonicalOrdinary.Compile");
            var grab = SearchQuery.Empty with { EventResultConditions = [
                new(EventResultConditionKind.TrashHeapGrabCard, Beta111EventResultCatalog.TrashHeapGrabCards[0])] };
            var generation = context.Authority.WorldAuthority!.Beta109Generation!;
            var variant = SearchQuery.Empty with { VariantBossBranches = [new(1,
                generation.ActSelectionGroups.Single(g => g.Act == 1).EligibleActsInSourceOrder[0],
                new([], [], []), new([], [], []), false)] };
            foreach (var query in new[] { grab, variant })
            {
                var request = Request(query);
                var family = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true, true).Single();
                var key = FamilyCanonicalCostShape.Describe(request, family, false, roots)!;
                var curve = curves.Single(c => !c.Key.CompactInput &&
                    (c.Key with { RootWindow = roots }).StableKey == key.StableKey);
                var knot = curve.Points.Single(p => p.N == roots);
                var quote = family.QuotePhysicalWork(new(false, false, roots));
                var ordinary = new GpuCostSnapshot([]).Reference(family, family.ResolveConditionPerformance(false), false);
                check(FamilyCanonicalMeasuredCost.IsCanonicalQuote(quote) &&
                    Math.Abs(quote!.NanosecondsPerInput * roots / 1e6 - knot.CallMilliseconds) < 1e-9 &&
                    Math.Abs(ordinary.CurrentQueryReference - roots * 1000d / knot.CallMilliseconds) < 1e-6 &&
                    ordinary.Evidence.Contains("TrainingRowsSha256=" + curve.TrainingRowsSha256, StringComparison.Ordinal),
                    "ordinary E/W Dense quote and ranking reference prefer the current measured complete call:" + family.FamilyId);
                var geometry = new FamilyPhysicalQuoteRequest(false, false, roots);
                var frozen = new FamilyCanonicalCostSnapshot([], JsonSerializer.SerializeToElement(new[] { curve }))
                    .Quote(key, geometry)!;
                var retained = FamilyCanonicalMeasuredCost.TryQuote(family, geometry, roots)!;
                var local = new GpuCostSnapshot([]).Local(family, retained, geometry);
                check((retained with { Evidence = frozen.Evidence }) == frozen &&
                    HasTransportProvenance(retained.Evidence) && HasTransportProvenance(ordinary.Evidence) &&
                    local == retained,
                    "GPU canonical lookup, Local and ranking preserve every frozen numeric/geometry field and disclose unmeasured current transport once:" + family.FamilyId);

                // Feed only this test-local accumulator, never the calibration
                // store. Exercise the ordinary Observe entry and cold exclusion
                // with a real registered family and its actual physical quote.
                var samples = new GpuCostSamples();
                samples.Observe(family, geometry, roots, 1000);
                check(samples.Peaks.Count == 0, "GPU normal observation still excludes its cold call:" + family.FamilyId);
                samples.Observe(family, geometry, roots, 1000);
                string observedKey = samples.Peaks.Single().Key;
                string legacyKey = string.Join("|", quote!.FamilyId, quote.Shape,
                    family.ResolveConditionPerformance(false).PhysicalImplementationRevision,
                    "Dense", "PublicInput", "PublicOutput", GpuReferenceCostAtlas.Boundary);
                check(observedKey == GpuReferenceCostAtlas.Key(family, quote, geometry) && observedKey != legacyKey &&
                    observedKey.Contains(FamilyGpuReadback.HostTransferRevision, StringComparison.Ordinal) &&
                    GpuReferenceCostAtlas.Peak(observedKey) is null,
                    "GPU normal observations use the new transport identity without aliasing historical local/reference keys:" + family.FamilyId);

                var compactGeometry = new FamilyPhysicalQuoteRequest(true, false, 65536);
                var fallback = family.QuotePhysicalWork(compactGeometry)!;
                var fallbackLocal = new GpuCostSnapshot([]).Local(family, fallback, compactGeometry);
                check(!FamilyCanonicalMeasuredCost.IsCanonicalQuote(fallback) &&
                    Math.Abs(fallbackLocal.NanosecondsPerInput - fallback.NanosecondsPerInput) < 1e-12 &&
                    fallbackLocal.WindowCapacity == fallback.WindowCapacity &&
                    fallbackLocal.OutputElementBytes == fallback.OutputElementBytes &&
                    HasTransportProvenance(fallbackLocal.Evidence),
                    "actual Compact legacy fallback retains numerical work and gains transport provenance at Local consumption:" + family.FamilyId);
                Console.WriteLine($"FamilyCanonicalCost transportFallback={family.FamilyId};directQuoteMarked={HasTransportProvenance(fallback.Evidence)};localQuoteMarked={HasTransportProvenance(fallbackLocal.Evidence)};shape={fallback.Shape};ns={fallback.NanosecondsPerInput:G17}");
                var integer = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true, false).Single();
                check(FamilyCanonicalMeasuredCost.TryQuote(integer, new(false, false, roots), roots) is null,
                    "ordinary E/W Integer body cannot borrow Native canonical data:" + family.FamilyId);
            }

            var pool = Beta111MerchantColorlessAuthority.From(context.Authority).UncommonPool;
            var fixedSlots = grab with { MerchantColorlessConditions = Enumerable.Range(1, 4)
                .Select(i => new MerchantColorlessSlotCondition(i, MerchantColorlessSlot.Uncommon, pool[i - 1])).ToArray() };
            var sequence = grab with { MerchantColorlessSequenceConditions = [new(4,
                CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Uncommon, [pool[0], null, pool[1], pool[2]])] };
            foreach (var query in new[] { fixedSlots, sequence })
            {
                var request = Request(query);
                if (!EventResultFamily.TryCreate(request, out var entry) || entry is null ||
                    !MerchantShopColorlessFamily.TryCreate(request, out var shop) || shop is null)
                    throw new InvalidDataException("CanonicalOrdinary.SFamily");
                var cpu = (FamilyCpuExecution)FamilyCpuExecution.ConfiguredRealizations(shop).Single();
                var key = FamilyCanonicalCostShape.Describe(request, cpu, true, roots)!;
                var curve = curves.Single(c => c.Key.StableKey == key.StableKey);
                var knot = curve.Points.Single(p => p.N == cpuInputs);
                // Force the already supported Dense entry only to exercise the
                // real subsequent CPU consumer, without predicting a best order.
                var plan = FamilyPlanner.PlanSimple([entry, cpu], denseEntry: entry);
                var stage = plan.Stages.Single(s => ReferenceEquals(s.Family, cpu));
                check(stage.CompactAbi1Input && stage.PerInputTimeMs.HasValue &&
                    Math.Abs(stage.PerInputTimeMs.Value * cpuInputs - knot.CallMilliseconds) < 1e-9 &&
                    stage.Performance.Evidence.Contains("TrainingRowsSha256=" + curve.TrainingRowsSha256, StringComparison.Ordinal) &&
                    stage.Performance.Evidence.Contains("ReferenceRootWindow=16777216;ReferenceInputPopulation=65536", StringComparison.Ordinal),
                    "ordinary S P16 Compact ranking uses real 16Mi/65536 reference geometry:" + key.PhysicalRevision);
                check(FamilyCanonicalMeasuredCost.TryDenseQuote(cpu, new(true, false, cpuInputs)) is null &&
                    FamilyCanonicalMeasuredCost.TryQuote(cpu, new(true, false, cpuInputs), cpuInputs) is null &&
                    FamilyCanonicalMeasuredCost.TryQuote(cpu, new(true, false, 32768), cpuInputs) is not null &&
                    FamilyCanonicalMeasuredCost.TryQuote(cpu, new(true, false, cpuInputs + 1), roots) is null &&
                    FamilyCanonicalMeasuredCost.TryQuote(cpu, new(true, false, cpuInputs), roots - 1) is null,
                    "S Compact retains its exact root spans and finite population endpoints, including 65536-root max32768");
                var snapshot = new FamilyCanonicalCostSnapshot([], JsonSerializer.SerializeToElement(curves));
                foreach (var other in new[] { key with { PhysicalRevision = "UnmeasuredBody" },
                    key with { ConfiguredWorkers = 3 }, key with { CpuIdentity = "AnotherCpu" } })
                    check(snapshot.Quote(other, new(true, false, cpuInputs)) is null,
                        "real S Compact reference retains body/worker/hardware admission");
                var missingRequest = Request(query, 3);
                MerchantShopColorlessFamily.TryCreate(missingRequest, out var missingShop);
                var missingCpu = FamilyCpuExecution.ConfiguredRealizations(missingShop!).Single();
                var missingPlan = FamilyPlanner.PlanSimple([entry, missingCpu], denseEntry: entry);
                check(missingPlan.Stages.Single(s => ReferenceEquals(s.Family, missingCpu)).PerInputTimeMs is null,
                    "ordinary S Compact remains Unknown when the actual configured worker has no data");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", oldCpu);
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(oldDevice.GpuIdentity, oldDevice.RenderingBackend);
        }
    }

    private static void CheckMeasuredNeowTransportEvidence(NeowFamily gpuN, NeowFamily cpuN,
        SearchContext context, Action<bool, string> check)
    {
        string host = "MeasuredHostTransferRevision=" + FamilyGpuReadback.HostTransferRevision;
        string boundary = "MeasuredNeowPublicBoundaryRevision=" + NeowFamilyGpuExecutor.PublicBoundaryRevision;
        var measured = new FamilyPhysicalQuote("N.Neow", "FixtureCurrentPublicN", 123, 65536, null,
            FamilyCanonicalMeasuredCost.Boundary + ";FixtureOnly;" + host + ";" + boundary + ";",
            OutputElementBytes: 8, OutputAlreadyOrdered: true, PublicTransportClass: "CompleteCanonicalAbi1") {
            LocalCostSource = "LocalMeasurement", ReferencePeakThroughput = 0, FixedWindowMilliseconds = 0
        };
        var current = FamilyGpuReadback.Reference(gpuN, measured)!;
        check((current with { Evidence = measured.Evidence }) == measured &&
            current.Evidence.Contains("CurrentNeowPublicInvokeHostTransportMeasured", StringComparison.Ordinal) &&
            current.Evidence.Contains("RootGroupSharedWaitNotLocalQuote", StringComparison.Ordinal) &&
            !current.Evidence.Contains("RetainedHistoricalCallEstimate", StringComparison.Ordinal) &&
            !current.Evidence.Contains("CurrentHostTransportUnmeasured", StringComparison.Ordinal),
            "matching explicit current public N transport evidence preserves every numeric field without a historical disclaimer");
        check(FamilyGpuReadback.Reference(gpuN, current) == current,
            "current measured N transport evidence is appended only once");
        foreach (string evidence in new[] {
            measured.Evidence.Replace(host, "MeasuredHostTransferRevision=stale", StringComparison.Ordinal),
            measured.Evidence.Replace(boundary, "MeasuredNeowPublicBoundaryRevision=stale", StringComparison.Ordinal),
            measured.Evidence.Replace(host, "MissingHost", StringComparison.Ordinal),
            measured.Evidence.Replace(boundary, "MissingBoundary", StringComparison.Ordinal),
            measured.Evidence.Replace(host, host + ".Other", StringComparison.Ordinal),
            FamilyCanonicalMeasuredCost.Boundary + ";HistoricalCompleteCallWithoutTransportMarkers"
        })
            check(HasTransportProvenance(FamilyGpuReadback.Reference(gpuN, measured with { Evidence = evidence })!.Evidence),
                "missing, stale, prefix-only or historical transport evidence retains the original disclaimer");
        foreach (string transport in new[] { "LargeResidentAppend32", "Counted32", "CpuOrderedAbi1" })
            check(HasTransportProvenance(FamilyGpuReadback.Reference(gpuN,
                measured with { PublicTransportClass = transport })!.Evidence),
                "transport markers do not promote a numerical/private/non-public quote:" + transport);
        check(FamilyGpuReadback.Reference(cpuN, measured) == measured,
            "GPU transport evidence does not modify a CPU invocation quote");
        var query = SearchQuery.Empty with { EventResultConditions = [
            new(EventResultConditionKind.TrashHeapGrabCard, Beta111EventResultCatalog.TrashHeapGrabCards[0])] };
        var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
            new("000000000000", 65536, 30, 1)).Plan ?? throw new InvalidDataException("MeasuredTransportFixture.EventCompile");
        if (!EventResultFamily.TryCreate(request, out var events) || events is null || !events.ConditionPerformance.UsesGpu)
            throw new InvalidDataException("MeasuredTransportFixture.EventGpuFamily");
        check(HasTransportProvenance(FamilyGpuReadback.Reference(events,
            measured with { FamilyId = events.FamilyId })!.Evidence),
            "current N transport markers cannot exempt another actual GPU family");
    }

    private static bool HasTransportProvenance(string evidence) =>
        evidence.Contains("RetainedHistoricalCallEstimate", StringComparison.Ordinal) &&
        evidence.Contains("CurrentHostTransportUnmeasured", StringComparison.Ordinal) &&
        evidence.Contains("HostTransferRevision=" + FamilyGpuReadback.HostTransferRevision, StringComparison.Ordinal) &&
        evidence.Contains("RootGroupSharedWaitNotLocalQuote", StringComparison.Ordinal);

    private static void CheckCompleteCallCurves(Action<bool, string> check)
    {
        var key = new FamilyCanonicalCostKey(1, "S.MerchantShopColorless", "GPU", "FixtureRuntime", "FixtureCpu", "FixtureGpu",
            "d3d12", "Beta111", "FixtureBody", 16, 1, false, 100, "FixtureSource", "");
        var curve = new FamilyCompleteCallCurve(key, [new(1, 4, 2), new(8, 2, 2), new(100, 10, 2)],
            "PublicAbi1Canonical", "training", FamilyCanonicalMeasuredCost.Boundary, FamilyCanonicalMeasuredCost.LiveMainLoopCadence,
            ["fixture-training"], new string('B', 64), ["fixture-source"], "SyntheticOnly;NotAnAccuracyClaim",
            JsonSerializer.SerializeToElement(new { Raw = "synthetic-complete-call-proof", Split = "training" }));
        var legacy = new FamilyCanonicalCostReferenceRow(key, 1, 100, 99, 0, null, 1, 99, new string('A', 64),
            "FixtureOnly", ["fixture-legacy"], "FixtureOnly", RootSpanMode: "DenseEqualsInputRange",
            MeasurementCadence: FamilyCanonicalMeasuredCost.LiveMainLoopCadence);
        FamilyCanonicalCostSnapshot Read(params FamilyCompleteCallCurve[] curves) => FamilyCanonicalMeasuredCost.Parse(
            JsonSerializer.Serialize(new FamilyCanonicalCostReference(1, FamilyCanonicalMeasuredCost.Boundary, [legacy],
                JsonSerializer.SerializeToElement(curves))));
        FamilyPhysicalQuote? Quote(FamilyCanonicalCostSnapshot snapshot, double n, FamilyCanonicalCostKey? k = null) =>
            snapshot.Quote(k ?? key with { RootWindow = (int)n }, new(false, false, n));
        double? Ms(FamilyPhysicalQuote? q, double n) => q is null ? null : q.NanosecondsPerInput * n / 1e6;
        var snapshot = Read(curve);
        check(Math.Abs(Ms(Quote(snapshot, 4), 4)!.Value - 22d / 7) < 1e-12,
            "actual-knot adjacent interpolation preserves a decreasing positive full-call segment");
        var actual = Quote(snapshot, 100);
        check(Ms(actual, 100) == 10 && actual is { FixedWindowMilliseconds: 0, SetupMilliseconds: null, GpuCostRatio: 1 } &&
            actual.Evidence.Contains("ActualCompleteCallKnots", StringComparison.Ordinal),
            "new actual complete-call curve wins legacy row without extra floor, setup or peak scaling");
        check(Quote(snapshot, 101) is null && Quote(snapshot, 0) is null && snapshot.HasShapeReference(key),
            "curve retains finite region and shape presence; zero remains caller-owned no invocation");
        foreach (var other in new[] { key with { CpuIdentity = "other" }, key with { GpuIdentity = "other" },
            key with { ConfiguredWorkers = 4 }, key with { PhysicalRevision = "other" }, key with { WorkShape = "other" },
            key with { OrderedChildBodies = "other" }, key with { CompactInput = true }, key with { Players = 2 } })
            check(Quote(snapshot, 100, other) is null, "complete curves retain exact device/body/workers/shape/children/players/mode");
        var compactKey = key with { CompactInput = true };
        var compact = new FamilyCanonicalCostSnapshot([], JsonSerializer.SerializeToElement(new[] { curve with { Key = compactKey } }));
        check(compact.Quote(compactKey, new(true, false, 4.5)) is not null &&
            compact.Quote(compactKey with { RootWindow = 101 }, new(true, false, 4.5)) is null &&
            compact.Quote(compactKey, new(true, true, 4.5)) is null && compact.Quote(compactKey, new(true, false, 4.5, true)) is null,
            "compact fractional conditional mean is bounded while root span and public ABI1 role remain hard");
        var point = new FamilyCanonicalCostSnapshot([], JsonSerializer.SerializeToElement(new[] { curve with { Points = [new(8, 2)] } }));
        check(Ms(Quote(point, 8), 8) == 2 && Quote(point, 7) is null && Quote(point, 9) is null,
            "one real anchor supplies only that population and never a fabricated zero floor");
        foreach (var invalid in new[] { curve with { Split = "validation" }, curve with { Role = "PrivateCountReady" },
            curve with { HeldoutUsedForFit = true }, curve with { Boundary = "kernel" },
            curve with { MeasurementCadence = FamilyCanonicalMeasuredCost.BlockedMainLoopCadence },
            curve with { Points = [new(1, 0)] }, curve with { Points = [new(1, 1), new(1, 2)] },
            curve with { TrainingRowsSha256 = "missing" }, curve with { SourceGroups = [] },
            curve with { SourceSurvival = .1 },
            curve with { Provenance = JsonSerializer.SerializeToElement(new { Role = "heldout" }) },
            curve with { Key = key with { FamilyId = "M.StandardMap" } }, curve with { Key = key with { Players = 2 } } })
            check(Ms(Quote(Read(invalid), 100), 100) == 99, "invalid optional curve is diagnosed and preserves valid legacy price");
        check(Ms(Quote(Read(curve, curve), 100), 100) == 99,
            "duplicate normalized curve identity has no arbitrary winner and retains legacy fallback");
        var broken = JsonSerializer.SerializeToElement(new object[] { new { Points = "wrong-json-type" }, curve });
        var partial = FamilyCanonicalMeasuredCost.Parse(JsonSerializer.Serialize(new FamilyCanonicalCostReference(
            1, FamilyCanonicalMeasuredCost.Boundary, [legacy], broken)));
        check(Ms(Quote(partial, 100), 100) == 10, "one malformed optional DTO cannot discard unrelated valid curve or legacy rows");
        var wrongSection = FamilyCanonicalMeasuredCost.Parse(JsonSerializer.Serialize(new FamilyCanonicalCostReference(
            1, FamilyCanonicalMeasuredCost.Boundary, [legacy], JsonSerializer.SerializeToElement("invalid-array"))));
        check(Ms(Quote(wrongSection, 100), 100) == 99, "malformed optional section is auxiliary failure rather than whole-document loss");

        var nKey = key with { FamilyId = "N.Neow" };
        var nLegacy = legacy with { Key = nKey };
        var nCurve = curve with { Key = nKey, SourceSurvival = .125 };
        var nOther = nCurve with { SourceSurvival = .25, Points = [new(1, 8), new(100, 20)] };
        FamilyCanonicalCostSnapshot ReadN(params FamilyCompleteCallCurve[] curves) => new([nLegacy], JsonSerializer.SerializeToElement(curves));
        var nSnapshot = ReadN(nCurve, nOther);
        check(nSnapshot.RequiresSourceSurvival(nKey) && nSnapshot.RequiresSourceSurvival(nKey with { RootWindow = 8 }) &&
            !ReadN().RequiresSourceSurvival(nKey) && !snapshot.RequiresSourceSurvival(key) &&
            !nSnapshot.RequiresSourceSurvival(null) && !nSnapshot.RequiresSourceSurvival(nKey with { Players = 2 }) &&
            !nSnapshot.RequiresSourceSurvival(nKey with { GpuIdentity = "other" }) &&
            !nSnapshot.RequiresSourceSurvival(nKey with { PhysicalRevision = "other" }),
            "source survival is needed only for an admitted solo N curve shape; legacy, multiplayer and foreign keys avoid the model read");
        var nCompact = ReadN(nCurve with { Key = nKey with { CompactInput = true } });
        check(nCompact.RequiresSourceSurvival(nKey with { CompactInput = true }) &&
            !nCompact.RequiresSourceSurvival(nKey with { CompactInput = true, RootWindow = 101 }),
            "source-survival requirement retains the actual Compact root span");
        FamilyPhysicalQuote? NQuote(FamilyCanonicalCostSnapshot value, double? p) => value.Quote(nKey, new(false, false, 100), p);
        check(Ms(NQuote(nSnapshot, .125), 100) == 10 && Ms(NQuote(nSnapshot, .25), 100) == 20,
            "same canonical N source shape has distinct curves for distinct modeled survival without scaling");
        check(NQuote(nSnapshot, .125)!.Evidence.Contains("SourceModelSelector;PositiveRelativeTolerance=1e-10;ZeroExact;NotObserved;NoSurvivalScaling", StringComparison.Ordinal),
            "N selector evidence distinguishes source model from observed survival");
        foreach (double? unknown in new double?[] { null, double.NaN, double.PositiveInfinity })
            check(Ms(NQuote(nSnapshot, unknown), 100) == 99 && NQuote(nSnapshot, unknown)!.Evidence.Contains(
                "N.SourceSurvivalUnavailable;NoCurveAlias", StringComparison.Ordinal) &&
                NQuote(nSnapshot, unknown)!.Evidence.Contains("LegacyFiniteReferenceFallback", StringComparison.Ordinal),
                "unknown N source survival retains legacy finite row with explicit missing selector evidence");
        check(Ms(NQuote(nSnapshot, .5), 100) == 99 && NQuote(nSnapshot, .5)!.Evidence.Contains(
            "N.SourceSurvivalNotMeasured", StringComparison.Ordinal), "unmeasured N probability retains an explicit legacy fallback");
        double adjacentP = Math.BitIncrement(.125);
        var adjacentQuote = NQuote(nSnapshot, adjacentP);
        check(Ms(adjacentQuote, 100) == 10 && adjacentQuote!.Evidence.Contains("SourceSurvival=0.125;QuerySourceSurvival=" +
            adjacentP.ToString("R", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal),
            "one positive modeled probability curve tolerates source summation rounding and preserves both original probabilities");
        var ambiguousN = ReadN(nCurve, nCurve with { SourceSurvival = adjacentP }, nOther);
        check(Ms(NQuote(ambiguousN, .125), 100) == 99 && NQuote(ambiguousN, .125)!.Evidence.Contains(
            "N.SourceSurvivalAmbiguous;NoCurveAlias;QuerySourceSurvival=0.125;StoredSourceSurvival=", StringComparison.Ordinal) &&
            Ms(NQuote(ambiguousN, .25), 100) == 20,
            "distinct nearby stored N probabilities remain separate and multiple matches fall back without an exact or nearest winner");
        var tinyN = ReadN(nCurve with { SourceSurvival = 1e-300 }, nOther with { SourceSurvival = 2e-300 },
            nCurve with { SourceSurvival = 0, Points = [new(1, 30), new(100, 30)] });
        check(Ms(NQuote(tinyN, 1e-300), 100) == 10 && Ms(NQuote(tinyN, 2e-300), 100) == 20 &&
            Ms(NQuote(tinyN, 0), 100) == 30 && Ms(NQuote(tinyN, double.Epsilon), 100) == 99 &&
            Ms(NQuote(tinyN, 4e-300), 100) == 99,
            "relative-only N matching preserves tiny factor-two classes and zero never aliases a positive probability");
        check(Ms(NQuote(ReadN(nCurve with { SourceSurvival = null }), .125), 100) == 99,
            "new N curve requires explicit finite source survival even though old canonical rows remain readable");
        var nDuplicates = ReadN(nCurve, nCurve, nOther);
        check(Ms(NQuote(nDuplicates, .125), 100) == 99 && Ms(NQuote(nDuplicates, .25), 100) == 20,
            "duplicate N key-plus-probability rejects only that identity and preserves other measured probability class");
        var nOnly = new FamilyCanonicalCostSnapshot([], JsonSerializer.SerializeToElement(new[] { nCurve }));
        check(nOnly.HasShapeReference(nKey, .125) && nOnly.HasShapeReference(nKey, adjacentP) &&
            !nOnly.HasShapeReference(nKey) && !nOnly.HasShapeReference(nKey, .25),
            "N optional curve presence requires the actual measured source survival class");
        var ambiguousOnly = new FamilyCanonicalCostSnapshot([], JsonSerializer.SerializeToElement(new[] {
            nCurve, nCurve with { SourceSurvival = adjacentP } }));
        check(!ambiguousOnly.HasShapeReference(nKey, .125) && NQuote(ambiguousOnly, .125) is null,
            "ambiguous optional N curves cannot advertise availability or manufacture a price without legacy rows");
        check(nSnapshot.Quote(nKey with { GpuIdentity = "other" }, new(false, false, 100), .125) is null &&
            nSnapshot.Quote(nKey with { PhysicalRevision = "other" }, new(false, false, 100), .125) is null,
            "N probability selector does not relax existing device or physical body identity");
    }

}
