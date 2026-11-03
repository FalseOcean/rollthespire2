using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

// Session-owned completed-work observations. Preparation and its frozen quotes
// never read these counters or same-run peaks.
internal sealed class FilterCostObserver
{
    private sealed class Totals
    {
        internal int Batches, SteadyBatches, ComparableBatches, SampleBatches;
        internal long Inputs, Outputs, LiveInputs, SampleInputs;
        internal double CanonicalMs, LiveMs, SampleMs, Peak, ValidPeak;
        internal CpuLocalPeak? CpuPeak;
    }

    private readonly object _sync = new();
    private readonly Dictionary<FilterBodyDescription, GpuCostSamples> _gpu = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FilterBodyDescription> _invalidBodies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FilterBodyDescription, Dictionary<bool, Totals>> _totals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FilterBodyDescription, bool> _latestModes = new(ReferenceEqualityComparer.Instance);
    private static readonly IReadOnlySet<string> EmptyCoverage = new HashSet<string>(StringComparer.Ordinal);

    internal void ObserveCompleted(FilterBodyDescription body, FamilyCandidateSet input, int outputs,
        double canonicalMs, double dispatchMs = -1, double readbackMs = -1, long readbackBytes = -1,
        bool verification = false, IReadOnlySet<string>? passedCoverage = null, double? modeledInput = null,
        bool privateInput = false, bool privateOutput = false)
    {
        FamilyAuxiliaryEvidence.Read(body.FamilyId, "ObserveCompletedFilter", () =>
        {
            lock (_sync)
            {
                bool compact = !input.IsDense;
                _latestModes[body] = compact;
                var total = GetTotals(body, compact);
                total.Batches++; total.Inputs += input.Count; total.Outputs += outputs; total.CanonicalMs += canonicalMs;
                if (total.Batches > 1)
                {
                    total.LiveInputs += input.Count; total.LiveMs += canonicalMs; total.SteadyBatches++;
                    int minimum = body.Cost.MinimumComparableInput?.Invoke(compact) ??
                        (compact ? body.CpuChunkCapacity / 4 : body.CpuChunkCapacity);
                    if (canonicalMs > 0)
                    {
                        double rate = input.Count * 1000d / canonicalMs;
                        if (body.UsesGpu || input.Count >= minimum) total.Peak = Math.Max(total.Peak, rate);
                        if (input.Count >= minimum)
                        {
                            total.ComparableBatches++;
                            total.ValidPeak = Math.Max(total.ValidPeak, rate);
                        }
                    }
                }
                if (body.UsesGpu)
                {
                    var samples = GpuSamples(body);
                    samples.Observe(body, new(compact, privateOutput, input.Count, privateInput), input.Count, dispatchMs,
                        verification, outputs, canonicalMs, readbackMs, readbackBytes);
                    if (!verification && body.Cost.CompleteCallPeakKey.Length > 0 && input.Count == body.WindowCapacity)
                        samples.Observe(body.Cost.CompleteCallPeakKey, input.Count, canonicalMs, GpuCostCalibration.DeviceKey());
                }
                else if (!_invalidBodies.Contains(body) && !verification && total.Batches > 1 && input.Batch.BatchCandidateCount == body.WindowCapacity &&
                    (input.IsDense || modeledInput.HasValue) && canonicalMs > 0 &&
                    (body.RequiredPassedCoverage is null || body.RequiredPassedCoverage.SetEquals(passedCoverage ?? EmptyCoverage)) &&
                    (input.IsDense || body.Cost.CalibrationRequiredPassedCoverage is null ||
                        body.Cost.CalibrationRequiredPassedCoverage.SetEquals(passedCoverage ?? EmptyCoverage)))
                {
                    var geometry = new FamilyPhysicalQuoteRequest(compact, false, input.IsDense ? body.WindowCapacity : modeledInput!.Value);
                    var raw = body.Cost.CalibrationQuote?.Invoke(geometry);
                    if (raw is not null)
                    {
                        total.SampleBatches++; total.SampleInputs += input.Count; total.SampleMs += canonicalMs;
                        if (total.SampleInputs >= 262144 && total.SampleMs >= 50)
                        {
                            string revision = body.ResolveConditionPerformance(compact).PhysicalImplementationRevision;
                            var peak = new CpuLocalPeak(CpuCostCalibration.DeviceKey(),
                                CpuCostCalibration.Key(raw.Shape, revision, body.CpuWorkers, compact), raw.Shape, revision,
                                body.CpuWorkers, total.SampleInputs, total.SampleMs, DateTimeOffset.UtcNow,
                                compact, total.SampleBatches, modeledInput);
                            if (CpuCostCalibration.Valid(peak) && (total.CpuPeak is null || peak.NanosecondsPerInput < total.CpuPeak.NanosecondsPerInput))
                                total.CpuPeak = peak;
                            total.SampleBatches = 0; total.SampleInputs = 0; total.SampleMs = 0;
                        }
                    }
                }
            }
            return true;
        }, false, body.InputIdentity);
    }

    // Shared-submit groups have a complete-work boundary of their own. A
    // member's local time omits the shared wait, so neither members nor the
    // aggregate are samples of a single canonical call. The caller retains its
    // existing first-group exclusion and reports only successfully completed roots.
    internal void ObserveCompletedGroup(FilterBodyDescription body, bool compact, long inputs, long outputs,
        int completedBatches, double wholeCanonicalMs, bool includeLive)
    {
        FamilyAuxiliaryEvidence.Read(body.FamilyId, "ObserveCompletedFilterGroup", () =>
        {
            lock (_sync)
            {
                _latestModes[body] = compact;
                var total = GetTotals(body, compact);
                total.Batches += completedBatches; total.Inputs += inputs; total.Outputs += outputs;
                total.CanonicalMs += wholeCanonicalMs;
                if (includeLive)
                {
                    total.LiveInputs += inputs; total.LiveMs += wholeCanonicalMs;
                    total.SteadyBatches += completedBatches;
                }
            }
            return true;
        }, false, body.InputIdentity);
    }

    // A physical fault retires only this selected body's calibration authority.
    // Completed-work counters remain evidence of work that actually finished.
    internal void Invalidate(FilterBodyDescription body)
    {
        lock (_sync) _invalidBodies.Add(body);
    }

    private GpuCostSamples GpuSamples(FilterBodyDescription body)
    {
        if (!_gpu.TryGetValue(body, out var samples)) _gpu[body] = samples = new();
        return samples;
    }

    private Totals GetTotals(FilterBodyDescription body, bool compact)
    {
        if (!_totals.TryGetValue(body, out var modes)) _totals[body] = modes = new();
        if (!modes.TryGetValue(compact, out var total)) modes[compact] = total = new();
        return total;
    }

    internal IReadOnlyList<GpuLocalPeak> GpuPeaks { get { lock (_sync) return _gpu
        .Where(pair => !_invalidBodies.Contains(pair.Key)).SelectMany(pair => pair.Value.Peaks).ToArray(); } }
    internal IReadOnlyList<CpuLocalPeak> CpuPeaks { get { lock (_sync) return _totals
        .Where(pair => !_invalidBodies.Contains(pair.Key)).SelectMany(pair => pair.Value.Values)
        .Where(total => total.CpuPeak is not null).Select(total => total.CpuPeak!).ToArray(); } }

    internal void ObserveGpuCompleted(FilterBodyDescription body, FamilyPhysicalQuoteRequest geometry,
        int inputs, int outputs, double dispatchMs, double canonicalMs, double readbackMs, long readbackBytes,
        bool verification = false)
    {
        lock (_sync) GpuSamples(body).Observe(body, geometry, inputs, dispatchMs, verification, outputs, canonicalMs, readbackMs, readbackBytes);
    }

    internal FamilyPerformanceObservation Capture(FilterBodyDescription body, bool compact, string device = "", double setupMs = 0)
    {
        lock (_sync)
        {
            var total = _totals.GetValueOrDefault(body)?.GetValueOrDefault(compact) ?? new Totals();
            bool invalid = _invalidBodies.Contains(body);
            return new(body.ResolveConditionPerformance(compact), device, setupMs, total.Peak, total.ValidPeak,
                total.Batches, total.ComparableBatches, total.Inputs, total.Outputs, total.CanonicalMs, !invalid && total.ComparableBatches > 0,
                invalid ? "PhysicalFailureOrRecovery" :
                    "CompletedCanonicalAbi1;ColdFirstCallOrGroupExcluded;GroupedWorkExcludedFromComparablePeaks;SessionObservationOnly");
        }
    }

    internal FamilyLivePerformanceSnapshot? Live(FilterBodyDescription body, bool compact)
    {
        lock (_sync)
        {
            if (_invalidBodies.Contains(body) || !_totals.TryGetValue(body, out var modes) ||
                !modes.TryGetValue(compact, out var total) || total.SteadyBatches == 0) return null;
            return new(body.FamilyId, body.ResolveConditionPerformance(compact).PhysicalImplementationRevision,
                total.LiveInputs, total.LiveMs, total.SteadyBatches);
        }
    }

    internal FamilyPerformanceObservation CaptureLatest(FilterBodyDescription body, string device = "", double setupMs = 0)
    {
        lock (_sync) return Capture(body, _latestModes.GetValueOrDefault(body), device, setupMs);
    }

    internal FamilyLivePerformanceSnapshot? LiveLatest(FilterBodyDescription body)
    {
        lock (_sync) return Live(body, _latestModes.GetValueOrDefault(body));
    }

    internal void WriteSummary()
    {
        lock (_sync)
        {
            foreach (var (body, samples) in _gpu) samples.WriteSummary(calibration: !_invalidBodies.Contains(body));
            foreach (var (body, modes) in _totals)
                foreach (var (compact, total) in modes)
                    RuntimeLog.TryBackgroundInfo($"filterCompletedWork=true;family={body.FamilyId};revision={body.PhysicalRevision};compact={compact};batches={total.Batches};inputs={total.Inputs};outputs={total.Outputs};canonicalMs={total.CanonicalMs:G17};observationOwner=Session;sameRunRepricing=false");
        }
    }
}
