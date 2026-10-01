using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.FamilyExecution;

// Measurements are inputs to Cost, not another device/Planner authority.
internal static class GpuReferenceCostAtlas
{
    internal const string Boundary = "DispatchSync.FullWindow.20260912.v1";
    internal const string Provenance = "RTX4060Laptop;D3D12;ReferenceMeasurement";
    internal sealed record Measurement(string Key, long Inputs, double ActiveMs, string Evidence)
    { internal double PeakThroughput => Inputs * 1000d / ActiveMs; }
    // Only comparable eligible peaks enter this table. Existing rounded numerical
    // prices remain bounded legacy references until an ordinary reference run replaces them.
    internal static readonly Measurement[] Measurements = [
        new("N.Neow|N.FirstDrawOrBoundedProjection|N.Neow.Gpu.LocalDonor.DenseCarry8.CanonicalAbi1Ready.20260905.v2|Dense|PublicInput|PublicOutput|DispatchSync.FullWindow.20260912.v1", 3858759680L, 1003.1892000000003, "CalibrationRecovery.20260912;leafy1;FullBatch;FirstExcluded;OneSecondPeak;RTX4060Laptop;D3D12"),
        new("N.Neow|N.FixedPair28.Identity|N.Neow.Gpu.LocalDonor.DensePreBonesCarry8.CanonicalAbi1Ready.20260905.v2|Dense|PublicInput|PrivateOutput|DispatchSync.FullWindow.20260912.v1", 8187281408L, 1000.5154000000001, "HistoricalMining.20260912;AppData.rt2_20260912_024152;Lines6056-7031;ShaderHashesMatched;FullBatch;FirstExcluded;OneSecondPeak;RTX4060Laptop;D3D12"),
        new("DirectCapsule|Small|3:0|rarity-serial|N.Neow.Composite.RCapsule.TargetRank.DenseCarry8.CanonicalAbi1Ready.20260906.v2|N.Neow.Composite.RCapsule.DirectExperiment.rarity-serial.CanonicalAbi1Ready.20260909.v1|DirectNr.CanonicalFullWindow.20260912.v1", 1912602624L, 1006.9712999999999, "CalibrationRecovery.20260912;small1;CanonicalNr;OneSecondPeak;RTX4060Laptop;D3D12")
    ];
    internal static double? Peak(string key) => Measurements.FirstOrDefault(x => x.Key == key)?.PeakThroughput;
    internal static string Key(IFamilyInvocation family, FamilyPhysicalQuote quote, FamilyPhysicalQuoteRequest geometry) =>
        string.Join("|", quote.FamilyId, quote.Shape,
            family.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision,
            geometry.CompactInput ? "Compact" : "Dense", geometry.PrivateInput ? "PrivateInput" : "PublicInput",
            geometry.PrivateOutput ? "PrivateOutput" : "PublicOutput", Boundary);
}

internal sealed record GpuLocalPeak(string Device, string ExecutionClass, string Key,
    double PeakThroughput, double SampleActiveMs, long SampleInputs, DateTimeOffset ObservedAtUtc);

internal sealed class GpuCostSnapshot
{
    private readonly IReadOnlyDictionary<string, double> _ratios;
    internal double GlobalRatio { get; }
    internal int ConditionCount => _ratios.Count;
    internal GpuCostSnapshot(IEnumerable<GpuLocalPeak> peaks, Func<string, double?>? reference = null)
    {
        reference ??= GpuReferenceCostAtlas.Peak;
        _ratios = peaks.Where(p => p.ExecutionClass == "GPU" && p.PeakThroughput > 0 && double.IsFinite(p.PeakThroughput))
            .Select(p => (p.Key, Ratio: reference(p.Key) / p.PeakThroughput))
            .Where(p => p.Ratio is > 0 && double.IsFinite(p.Ratio.Value))
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Min(x => x.Ratio!.Value));
        double[] values = _ratios.Values.Order().ToArray();
        GlobalRatio = values.Length == 0 ? 1 : values.Length % 2 == 1 ? values[values.Length / 2] :
            values[values.Length / 2 - 1] / 2 + values[values.Length / 2] / 2;
    }
    internal double Ratio(string key) => _ratios.GetValueOrDefault(key, GlobalRatio);
    internal FamilyPhysicalQuote Local(IFamilyInvocation family, FamilyPhysicalQuote quote, FamilyPhysicalQuoteRequest geometry)
    {
        if (!family.ConditionPerformance.UsesGpu) return quote;
        if (quote.LocalCostSource == "LocalMeasurement") return quote;
        string key = GpuReferenceCostAtlas.Key(family, quote, geometry);
        double referencePeak = GpuReferenceCostAtlas.Peak(key) ?? quote.ReferencePeakThroughput;
        double ratio = Ratio(key);
        return quote with { ReferencePeakThroughput = referencePeak, NanosecondsPerInput = (referencePeak > 0 ? 1e9 / referencePeak : quote.NanosecondsPerInput) * ratio,
            GpuCostRatio = ratio, LocalExecutorCostRatio = ratio, LocalCostSource = _ratios.ContainsKey(key) ? "Condition" : _ratios.Count > 0 ? "Global" : "ReferenceDefault", Evidence = quote.Evidence + $";GpuCostRatio={ratio:G17};RatioSource={(_ratios.ContainsKey(key) ? "Condition" : _ratios.Count > 0 ? "GlobalMedian" : "ReferenceDefault")};ReferenceQuality={(GpuReferenceCostAtlas.Peak(key).HasValue ? "EligiblePeak" : "BoundedLegacy")};Provenance={GpuReferenceCostAtlas.Provenance}" };
    }
    internal FamilyPerformanceReference Reference(IFamilyInvocation family, FamilyConditionPerformanceProjection condition, bool compact)
    {
        var geometry = new FamilyPhysicalQuoteRequest(compact, false, PrivateOrdinalBuffer.Capacity);
        var raw = family.QuotePhysicalWork(geometry);
        if (!condition.UsesGpu && raw is { NanosecondsPerInput: > 0 })
            return new(condition.FamilyId,condition.PhysicalImplementationRevision,1,1e9/raw.NanosecondsPerInput,
                raw.LocalCostSource=="Condition"?FamilyPerformanceEvidenceSource.Condition:
                    raw.LocalCostSource=="Global"?FamilyPerformanceEvidenceSource.Global:FamilyPerformanceEvidenceSource.Reference,"CPU","CPU",raw.Evidence);
        if (condition.UsesGpu && raw is { NanosecondsPerInput: > 0, PublicTransportClass: "CompleteCanonicalAbi1" })
            return new(condition.FamilyId, condition.PhysicalImplementationRevision, 1,
                1e9 / raw.NanosecondsPerInput, FamilyPerformanceEvidenceSource.Condition,
                SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().GpuIdentity,
                SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().RenderingBackend, raw.Evidence);
        if (raw is null || !condition.UsesGpu) return GpuCostCalibration.ResolveReference(condition);
        var local = Local(family, raw, geometry);
        double? survival = family.ResolveSurvival(new HashSet<string>()).SurvivalProbability;
        if (survival is not (>= 0 and <= 1)) return GpuCostCalibration.ResolveReference(condition);
        // Same numerical + output boundary composition as a single ordinary stage.
        double input = PrivateOrdinalBuffer.Capacity, output = input * survival.Value;
        double windows = Math.Ceiling(input / local.WindowCapacity);
        bool large = local.PublicTransportClass == "LargeResidentAppend32";
        double materialization = windows * (.15 + local.FixedWindowMilliseconds) + windows * (1 - Math.Exp(-output / windows)) * (large ? 4 : .15) +
            output * (local.OutputAlreadyOrdered ? 6 : large ? 38 : 25) / 1e6;
        double ms = input * local.NanosecondsPerInput / 1e6 + (materialization + .6) * local.GpuCostRatio;
        double cps = input * 1000 / ms;
        return new(condition.FamilyId, condition.PhysicalImplementationRevision, 1, cps,
            _ratios.ContainsKey(GpuReferenceCostAtlas.Key(family, raw, geometry)) ? FamilyPerformanceEvidenceSource.Condition :
                ConditionCount > 0 ? FamilyPerformanceEvidenceSource.Global : FamilyPerformanceEvidenceSource.Reference, "GPU", SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().RenderingBackend, local.Evidence);
    }
}

// One small accumulator owned by an existing Family/allocation invocation.
// No UI publication timestamps, no observed population feeds a running quote.
internal sealed class GpuCostSamples
{
    private sealed class Window { internal long Inputs; internal double Ms; internal bool Warmed; internal int CompletedWindows; }
    private readonly Dictionary<string, Window> _windows = new();
    private readonly Dictionary<(IFamilyInvocation, bool, bool, bool), FamilyPhysicalQuote?> _quotes = new();
    private readonly Dictionary<string, GpuLocalPeak> _peaks = new();
    // Log-only totals never participate in Peaks, ratios or any quote.
    private sealed class Totals {
        internal long Inputs, Outputs, Batches, Zero, Underfilled, Full, Bytes;
        internal double Dispatch, Canonical, Readback;
        internal bool DispatchKnown=true, ReadbackKnown=true, BytesKnown=true;
        // Log-only complete window totals after this physical key's first call.
        // These never participate in peak selection or calibration/pricing.
        internal long SteadyInputs, SteadyOutputs, SteadyBatches, SteadyFull, SteadyUnderfilled;
        internal double SteadyDispatch, SteadyCanonical, SteadyReadback;
        internal bool SteadyDispatchKnown=true, SteadyReadbackKnown=true;
    }
    private readonly Dictionary<string, Totals> _totals = new();
    internal void Record(string key, int capacity, int inputs, int outputs, double dispatch, double canonical, double readback, long bytes)
    {
        if (!_totals.TryGetValue(key, out var t)) _totals[key] = t = new();
        bool full=inputs>0 && (inputs >= capacity && inputs%capacity==0 || inputs==PrivateOrdinalBuffer.Capacity);
        if(t.Batches>0) {
            t.SteadyBatches++; t.SteadyInputs+=inputs; t.SteadyOutputs+=outputs;
            t.SteadyDispatchKnown &= dispatch>=0; t.SteadyReadbackKnown &= readback>=0;
            t.SteadyDispatch+=Math.Max(0,dispatch); t.SteadyCanonical+=canonical; t.SteadyReadback+=Math.Max(0,readback);
            if(full)t.SteadyFull++; else if(inputs>0)t.SteadyUnderfilled++;
        }
        t.Batches++; t.Inputs += inputs; t.Outputs += outputs; t.DispatchKnown &= dispatch>=0; t.ReadbackKnown &= readback>=0; t.BytesKnown &= bytes>=0; t.Dispatch += Math.Max(0,dispatch); t.Canonical += canonical; t.Readback += Math.Max(0,readback); t.Bytes += Math.Max(0,bytes);
        if(inputs==0)t.Zero++; else if(inputs >= capacity && inputs%capacity==0 || inputs==PrivateOrdinalBuffer.Capacity)t.Full++; else t.Underfilled++;
    }
    private static string Metric(double value, bool known) => known ? value.ToString("G17",System.Globalization.CultureInfo.InvariantCulture) : "unavailable";
    internal void WriteSummary(bool calibration = true)
    {
        foreach(var (key,t) in _totals) {
            RuntimeLog.TryBackgroundInfo($"physicalExecutionSummary=true;key={key};device={GpuCostCalibration.DeviceKey()};batches={t.Batches};inputs={t.Inputs};outputs={t.Outputs};zeroWindows={t.Zero};fullWindows={t.Full};underfilledWindows={t.Underfilled};dispatchSyncMs={Metric(t.Dispatch,t.DispatchKnown)};canonicalMs={t.Canonical:G17};readbackMs={Metric(t.Readback,t.ReadbackKnown)};readbackBytes={Metric(t.Bytes,t.BytesKnown)};reportedReadbackScope=ExecutorMetric;unattributedCanonicalMs={Metric(Math.Max(0,t.Canonical-t.Dispatch-t.Readback),t.DispatchKnown&&t.ReadbackKnown)};observedDispatchRate={Metric(t.Dispatch>0?t.Inputs*1000/t.Dispatch:0,t.DispatchKnown)};observedCanonicalRate={(t.Canonical>0?t.Inputs*1000/t.Canonical:0):G17};timings=CompletedWorkOnly");
            RuntimeLog.TryBackgroundInfo($"physicalSteadyExecutionSummary=true;key={key};batches={t.SteadyBatches};inputs={t.SteadyInputs};outputs={t.SteadyOutputs};fullWindows={t.SteadyFull};underfilledWindows={t.SteadyUnderfilled};dispatchSyncMs={Metric(t.SteadyDispatch,t.SteadyBatches>0&&t.SteadyDispatchKnown)};canonicalMs={Metric(t.SteadyCanonical,t.SteadyBatches>0)};readbackMs={Metric(t.SteadyReadback,t.SteadyBatches>0&&t.SteadyReadbackKnown)};observedCanonicalRate={Metric(t.SteadyCanonical>0?t.SteadyInputs*1000/t.SteadyCanonical:0,t.SteadyBatches>0&&t.SteadyCanonical>0)};window=AllCompletedCallsAfterFirstForThisPhysicalKey;peakSelection=false;calibrationAuthority=false;kernelTimestamp=false");
        }
        if(!calibration) return;
        foreach(var key in _windows.Keys.Concat(_totals.Keys).Distinct())
        {
            var w = _windows.GetValueOrDefault(key) ?? new Window();
            _peaks.TryGetValue(key, out var best);
            RuntimeLog.TryBackgroundInfo("gpuCostObservation=true;observationJson=" + RuntimeLog.SafeJson(new {key,device=GpuCostCalibration.DeviceKey(),eligibleWindows=w.CompletedWindows,coldObservationExcluded=w.Warmed,pendingInputs=w.Inputs,pendingActiveMs=w.Ms,bestWindow=best,referencePeak=GpuReferenceCostAtlas.Peak(key),sameRunRepricing=false}));
        }
    }
    internal IReadOnlyCollection<GpuLocalPeak> Peaks => _peaks.Values;
    internal void Observe(IFamilyInvocation family, FamilyPhysicalQuoteRequest geometry, int inputs, double dispatchMs,
        bool verification = false, int outputs = 0, double canonicalMs = 0, double readbackMs = 0, long readbackBytes = 0)
    {
        var binding = (family, geometry.CompactInput, geometry.PrivateInput, geometry.PrivateOutput);
        if (!_quotes.TryGetValue(binding, out var quote)) _quotes[binding] = quote = family.QuotePhysicalWork(geometry);
        string physicalKey = quote is null ? family.FamilyId + "|" + family.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision + "|Unpriced|" + (geometry.CompactInput?"Compact":"Dense") + "|" + (geometry.PrivateInput?"PrivateInput":"PublicInput") + "|" + (geometry.PrivateOutput?"PrivateOutput":"PublicOutput") : GpuReferenceCostAtlas.Key(family, quote, geometry);
        Record(physicalKey, quote?.WindowCapacity ?? PrivateOrdinalBuffer.Capacity, inputs, outputs, dispatchMs, canonicalMs, readbackMs, readbackBytes);
        if (quote is null || !family.ConditionPerformance.UsesGpu || verification ||
            (inputs != PrivateOrdinalBuffer.Capacity && (inputs < quote.WindowCapacity || inputs % quote.WindowCapacity != 0)) || !(dispatchMs > 0) || !double.IsFinite(dispatchMs)) return;
        string key = GpuReferenceCostAtlas.Key(family, quote, geometry);
        Observe(key, inputs, dispatchMs, GpuCostCalibration.DeviceKey());
    }
    internal void Observe(string key, int inputs, double activeMs, string device)
    {
        if (string.IsNullOrWhiteSpace(device) || inputs <= 0 || !(activeMs > 0) || !double.IsFinite(activeMs)) return;
        if (!_windows.TryGetValue(key, out var window)) _windows[key] = window = new();
        if (!window.Warmed) { window.Warmed = true; return; }
        window.Inputs += inputs; window.Ms += activeMs;
        if (window.Ms < 1000) return;
        window.CompletedWindows++;
        double throughput = window.Inputs * 1000d / window.Ms;
        if (!_peaks.TryGetValue(key, out var peak) || throughput > peak.PeakThroughput)
            _peaks[key] = new(device, "GPU", key, throughput, window.Ms, window.Inputs, DateTimeOffset.UtcNow);
        if (RuntimeLog.DetailEnabled) RuntimeLog.TryBackgroundDetail($"gpuCostPeakSample=true;key={key};inputs={window.Inputs};activeMs={window.Ms:G17};throughput={throughput:G17};referenceAvailable={GpuReferenceCostAtlas.Peak(key).HasValue};sameRunRepricing=false");
        window.Inputs = 0; window.Ms = 0;
    }
}

internal static class GpuCostCalibration
{
    private sealed record Document(int SchemaVersion, List<GpuLocalPeak> Peaks);
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Dictionary<(string Device, string Key), GpuLocalPeak> Peaks = new();
    private static bool _loaded, _dirty;
    internal static string StorePath => System.Environment.GetEnvironmentVariable("RT2_GPU_COST_EVIDENCE_PATH") ?? Path.Combine(Path.GetDirectoryName(OperationalFileLog.LogDirectory)!, "cache", "gpu_cost_peaks_v1.json");
    internal static string DeviceKey()
    {
        var device = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        return device.HasKnownGpu ? string.Join("|", device.RuntimeTarget, device.GpuIdentity, device.RenderingBackend) : "";
    }
    internal static void InitializeOnMainThread()
    {
        lock (Gate)
        {
            if (_loaded) return;
            try
            {
                if (File.Exists(StorePath) && JsonSerializer.Deserialize<Document>(File.ReadAllText(StorePath), Json) is { SchemaVersion: 1 } data)
                    foreach (var peak in data.Peaks.Where(Valid))
                        if (!Peaks.TryGetValue((peak.Device, peak.Key), out var old) || peak.PeakThroughput > old.PeakThroughput)
                            Peaks[(peak.Device, peak.Key)] = peak;
            }
            catch (Exception ex) { RuntimeLog.Warn("gpuCostStoreLoadFailed=" + ex.GetType().Name); }
            _loaded = true;
            RuntimeLog.Info($"gpuCostStore=true;entries={Peaks.Count};path={StorePath};oldFamilyCache=Obsolete;dedicatedBenchmark=false");
        }
    }
    internal static GpuCostSnapshot Capture()
    {
        string device = DeviceKey();
        lock (Gate)
        {
            var snapshot = new GpuCostSnapshot(Peaks.Values.Where(p => p.Device == device).ToArray());
            RuntimeLog.TryBackgroundInfo($"gpuCostPricingSnapshot=true;globalRatio={snapshot.GlobalRatio:G17};conditions={snapshot.ConditionCount};sameRunFrozen=true");
            return snapshot;
        }
    }
    // Read-only UI projection. No owner creation, sampling, logging or disk I/O.
    internal static (double Ratio, int Comparable, int Recorded) DisplaySummary()
    {
        string device = DeviceKey();
        lock (Gate)
        {
            var peaks = Peaks.Values.Where(p => p.Device == device).ToArray();
            var snapshot = new GpuCostSnapshot(peaks);
            return (snapshot.GlobalRatio, snapshot.ConditionCount, peaks.Length);
        }
    }
    internal static void Submit(IEnumerable<GpuLocalPeak> observations)
    {
        lock (Gate)
        {
            if (!_loaded) return;
            foreach (var peak in observations.Where(Valid))
            {
                var key = (peak.Device, peak.Key);
                bool updated = !Peaks.TryGetValue(key, out var old) || peak.PeakThroughput > old.PeakThroughput;
                if (updated) { Peaks[key] = peak; _dirty = true; }
                RuntimeLog.TryBackgroundInfo("gpuCostPeakDecision=true;decisionJson=" + RuntimeLog.SafeJson(new { peak.Key, peak.Device, previousPeak=old?.PeakThroughput, observedPeak=peak.PeakThroughput, updated, finalPeak=Peaks[key].PeakThroughput, derivedRatio=GpuReferenceCostAtlas.Peak(peak.Key)/Peaks[key].PeakThroughput, persistence=updated?"PendingMainThreadFlush":"Unchanged",sameRunRepricing=false }));
            }
        }
    }
    internal static void TryFlushPendingOnMainThread()
    {
        lock (Gate)
        {
            if (!_loaded || !_dirty) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath + ".tmp", JsonSerializer.Serialize(new Document(1, Peaks.Values.ToList()), Json));
                File.Move(StorePath + ".tmp", StorePath, true); _dirty = false;
                RuntimeLog.Info($"gpuCostStoreFlush=true;entries={Peaks.Count};diskIoThread=GodotMain");
            }
            catch (Exception ex) { RuntimeLog.Warn("gpuCostStoreFlushFailed=" + ex.GetType().Name); }
        }
    }
    private static bool Valid(GpuLocalPeak p) => p is not null && p.ExecutionClass == "GPU" &&
        !string.IsNullOrWhiteSpace(p.Device) && !string.IsNullOrWhiteSpace(p.Key) &&
        p.SampleActiveMs >= 1000 && double.IsFinite(p.SampleActiveMs) && p.SampleInputs > 0 &&
        p.PeakThroughput > 0 && double.IsFinite(p.PeakThroughput);

    // Unpriced legacy plans remain Unknown. No obsolete local Family peak is
    // multiplied a second time or silently promoted into a work-shape reference.
    internal static FamilyPerformanceReference ResolveReference(FamilyConditionPerformanceProjection condition) =>
        new(condition.FamilyId, condition.PhysicalImplementationRevision, condition.Factor, 0,
            FamilyPerformanceEvidenceSource.Unavailable, "", "", "WorkShapeQuoteRequired");
}
