using System.Collections.Concurrent;
using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.Predictability;

/// <summary>
/// Local CPU Exact timing/verification evidence store. Search workers only update the
/// in-memory aggregate; disk load/save remains on the AppShell main thread. Production
/// Analytical Cost consumes only ms/attempt timing. Exact acceptance remains diagnostic
/// legacy evidence and never becomes Planning Population authority.
/// </summary>
internal static class SearchPredictabilityVerificationStore
{
    private const int SchemaVersion = 1;
    private const int VerificationAbi = 1;
    private const string FileName = "search_predictability_verification_v1.json";
    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<TaskCompletionSource<bool>> FlushWaiters = new();
    private static StoreDocument _document = NewDocument();
    private static bool _loaded;
    private static bool _dirty;
    private static string _lastLoadEvidence = "NotLoaded";
    private static string _lastSaveEvidence = "NotSaved";

    private sealed class StoreDocument
    {
        public int SchemaVersion { get; set; }
        public int VerificationAbi { get; set; }
        public List<Entry> Entries { get; set; } = new();
    }

    private sealed class Entry
    {
        public string Family { get; set; } = string.Empty;
        public int SearchSampleCount { get; set; }
        public long ExactAttempts { get; set; }
        public long VerifiedMatches { get; set; }
        public double ExactAggregateWorkMs { get; set; }
        public int TimingSampleCount { get; set; }
        public long TimingExactAttempts { get; set; }
        public double TimingAggregateWorkMs { get; set; }
        public double SmoothedExactMsPerAttempt { get; set; }
        public DateTimeOffset LastObservedAtUtc { get; set; }
        public string RuntimeTarget { get; set; } = string.Empty;
        public string CpuIdentity { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    internal sealed record Snapshot(int CompatibleEntries, int TotalSearchSamples, string LoadEvidence, string SaveEvidence, string StorePath, bool Loaded, bool Dirty);

    public static string StorePath
    {
        get
        {
            string logDir = OperationalFileLog.LogDirectory;
            string root = Path.GetDirectoryName(logDir) ?? logDir;
            return Path.Combine(root, "cache", FileName);
        }
    }

    public static void InitializeOnMainThread()
    {
        lock (Gate) EnsureLoaded_NoThrow();
    }

    public static SearchVerificationFamily ResolveFamily(IReadOnlyList<ExactTimingDomain> domains)
    {
        ExactTimingDomain[] distinct = domains.Distinct().ToArray();
        if (distinct.Length == 1)
        {
            return distinct[0] switch
            {
                ExactTimingDomain.Neow => SearchVerificationFamily.NeowOnly,
                ExactTimingDomain.Relic => SearchVerificationFamily.RelicOnly,
                ExactTimingDomain.WorldEvent => SearchVerificationFamily.WorldOnly,
                ExactTimingDomain.CombatReward => SearchVerificationFamily.CombatRewardOnly,
                _ => SearchVerificationFamily.MixedWithoutReward
            };
        }
        return distinct.Contains(ExactTimingDomain.CombatReward)
            ? SearchVerificationFamily.MixedWithReward
            : SearchVerificationFamily.MixedWithoutReward;
    }

    public static void SubmitExactTimingSample(
        IReadOnlyList<ExactTimingDomain> domains,
        long exactAttempts,
        double exactAggregateWorkMs,
        string evidence)
    {
        if (exactAttempts <= 0 || !(exactAggregateWorkMs > 0d) || !double.IsFinite(exactAggregateWorkMs)) return;
        SearchPerformanceDeviceIdentity identity = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        if (string.IsNullOrWhiteSpace(identity.CpuIdentity)) return;
        SearchVerificationFamily family = ResolveFamily(domains);
        double observedMsPerAttempt = exactAggregateWorkMs / exactAttempts;
        if (!(observedMsPerAttempt > 0d) || !double.IsFinite(observedMsPerAttempt)) return;

        lock (Gate)
        {
            if (!_loaded) return;
            Entry entry = FindOrCreateEntry_NoLock(family, identity);
            UpdateTiming_NoLock(entry, exactAttempts, exactAggregateWorkMs, observedMsPerAttempt, evidence);
            _dirty = true;
        }
    }

    public static SearchExactTimingEvidence? TryGetExactTiming(SearchVerificationFamily family)
    {
        SearchPerformanceDeviceIdentity identity = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        lock (Gate)
        {
            if (!_loaded) return null;
            Entry? entry = _document.Entries.FirstOrDefault(item =>
                string.Equals(item.Family, family.ToString(), StringComparison.Ordinal) &&
                string.Equals(item.RuntimeTarget, identity.RuntimeTarget, StringComparison.Ordinal) &&
                string.Equals(item.CpuIdentity, identity.CpuIdentity, StringComparison.Ordinal));
            if (entry is null) return null;

            int samples = entry.TimingSampleCount > 0 ? entry.TimingSampleCount : entry.SearchSampleCount;
            long attempts = entry.TimingExactAttempts > 0 ? entry.TimingExactAttempts : entry.ExactAttempts;
            double workMs = entry.TimingAggregateWorkMs > 0d ? entry.TimingAggregateWorkMs : entry.ExactAggregateWorkMs;
            if (samples <= 0 || attempts <= 0 || !(workMs > 0d) || !(entry.SmoothedExactMsPerAttempt > 0d)) return null;
            return new SearchExactTimingEvidence(
                family, samples, attempts, workMs, entry.SmoothedExactMsPerAttempt,
                Confidence(samples, attempts), entry.Source, entry.LastObservedAtUtc);
        }
    }

    public static SearchVerificationEvidence? TryGet(SearchVerificationFamily family)
    {
        SearchPerformanceDeviceIdentity identity = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        lock (Gate)
        {
            if (!_loaded) return null;
            Entry? entry = _document.Entries.FirstOrDefault(item =>
                string.Equals(item.Family, family.ToString(), StringComparison.Ordinal) &&
                string.Equals(item.RuntimeTarget, identity.RuntimeTarget, StringComparison.Ordinal) &&
                string.Equals(item.CpuIdentity, identity.CpuIdentity, StringComparison.Ordinal));
            if (entry is null || entry.SearchSampleCount <= 0 || entry.ExactAttempts <= 0 || entry.VerifiedMatches <= 0) return null;
            double acceptance = Math.Clamp((double)entry.VerifiedMatches / entry.ExactAttempts, 0d, 1d);
            double attemptsPerVerified = (double)entry.ExactAttempts / entry.VerifiedMatches;
            return new SearchVerificationEvidence(
                family,
                entry.SearchSampleCount,
                entry.ExactAttempts,
                entry.VerifiedMatches,
                entry.ExactAggregateWorkMs,
                acceptance,
                attemptsPerVerified,
                entry.SmoothedExactMsPerAttempt,
                Confidence(entry.SearchSampleCount, entry.ExactAttempts),
                entry.Source,
                entry.LastObservedAtUtc);
        }
    }

    private static Entry FindOrCreateEntry_NoLock(SearchVerificationFamily family, SearchPerformanceDeviceIdentity identity)
    {
        Entry? entry = _document.Entries.FirstOrDefault(item =>
            string.Equals(item.Family, family.ToString(), StringComparison.Ordinal) &&
            string.Equals(item.RuntimeTarget, identity.RuntimeTarget, StringComparison.Ordinal) &&
            string.Equals(item.CpuIdentity, identity.CpuIdentity, StringComparison.Ordinal));
        if (entry is not null) return entry;
        entry = new Entry
        {
            Family = family.ToString(),
            RuntimeTarget = identity.RuntimeTarget,
            CpuIdentity = identity.CpuIdentity
        };
        _document.Entries.Add(entry);
        return entry;
    }

    private static void UpdateTiming_NoLock(
        Entry entry,
        long exactAttempts,
        double exactAggregateWorkMs,
        double observedMsPerAttempt,
        string evidence)
    {
        if (entry.TimingSampleCount <= 0 || !(entry.SmoothedExactMsPerAttempt > 0d))
        {
            entry.SmoothedExactMsPerAttempt = observedMsPerAttempt;
        }
        else
        {
            double baseline = Math.Max(0.001d, entry.SmoothedExactMsPerAttempt);
            double clipped = Math.Clamp(observedMsPerAttempt, baseline * 0.5d, baseline * 2d);
            double alpha = entry.TimingSampleCount < 3 ? 0.25d : 0.125d;
            entry.SmoothedExactMsPerAttempt += alpha * (clipped - entry.SmoothedExactMsPerAttempt);
        }
        entry.TimingSampleCount++;
        entry.TimingExactAttempts += exactAttempts;
        entry.TimingAggregateWorkMs += exactAggregateWorkMs;
        entry.LastObservedAtUtc = DateTimeOffset.UtcNow;
        entry.Source = San(evidence);
    }

    public static Snapshot GetSnapshot()
    {
        SearchPerformanceDeviceIdentity identity = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        lock (Gate)
        {
            int compatible = _loaded ? _document.Entries.Count(item => item.RuntimeTarget == identity.RuntimeTarget && item.CpuIdentity == identity.CpuIdentity) : 0;
            int samples = _loaded ? _document.Entries.Where(item => item.RuntimeTarget == identity.RuntimeTarget && item.CpuIdentity == identity.CpuIdentity).Sum(item => Math.Max(item.SearchSampleCount, item.TimingSampleCount)) : 0;
            return new Snapshot(compatible, samples, _lastLoadEvidence, _lastSaveEvidence, StorePath, _loaded, _dirty);
        }
    }

    public static Task RequestFlushAsync()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        FlushWaiters.Enqueue(tcs);
        return tcs.Task;
    }

    public static void TryFlushPendingOnMainThread()
    {
        bool hasWaiter = !FlushWaiters.IsEmpty;
        lock (Gate)
        {
            if (!_loaded) EnsureLoaded_NoThrow();
            if (_dirty || hasWaiter) Save_NoThrow();
        }
        while (FlushWaiters.TryDequeue(out TaskCompletionSource<bool>? waiter)) waiter.TrySetResult(true);
    }

    internal static void ResetMemoryForDiagnosticReload()
    {
        lock (Gate)
        {
            _document = NewDocument();
            _loaded = false;
            _dirty = false;
            _lastLoadEvidence = "DiagnosticResetPendingReload";
        }
    }

    internal static void ClearCompatibleForDiagnostic()
    {
        SearchPerformanceDeviceIdentity identity = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        lock (Gate)
        {
            if (!_loaded) return;
            _document.Entries.RemoveAll(item => item.RuntimeTarget == identity.RuntimeTarget && item.CpuIdentity == identity.CpuIdentity);
            _dirty = true;
        }
    }

    private static SearchPredictabilityConfidence Confidence(int searchSamples, long attempts) =>
        searchSamples >= 8 && attempts >= 64 ? SearchPredictabilityConfidence.High :
        searchSamples >= 3 && attempts >= 16 ? SearchPredictabilityConfidence.Medium :
        SearchPredictabilityConfidence.Low;

    private static StoreDocument NewDocument() => new() { SchemaVersion = SchemaVersion, VerificationAbi = VerificationAbi };

    private static void EnsureLoaded_NoThrow()
    {
        if (_loaded) return;
        _document = NewDocument();
        try
        {
            string path = StorePath;
            if (File.Exists(path))
            {
                StoreDocument? parsed = JsonSerializer.Deserialize<StoreDocument>(File.ReadAllText(path));
                if (parsed is not null && parsed.SchemaVersion == SchemaVersion && parsed.VerificationAbi == VerificationAbi)
                {
                    parsed.Entries ??= new List<Entry>();
                    parsed.Entries.RemoveAll(item => !Validate(item));
                    _document = parsed;
                    _lastLoadEvidence = $"PredictabilityVerificationStoreLoaded_entries={parsed.Entries.Count}";
                }
                else _lastLoadEvidence = "PredictabilityVerificationStoreSchemaMismatch_FailConservative";
            }
            else _lastLoadEvidence = "PredictabilityVerificationStoreMissing_FirstUse";
        }
        catch (Exception ex)
        {
            _document = NewDocument();
            _lastLoadEvidence = "PredictabilityVerificationStoreLoadFailed:" + ex.GetType().Name;
        }
        _loaded = true;
    }

    private static void Save_NoThrow()
    {
        try
        {
            string path = StorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_document, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
            _dirty = false;
            _lastSaveEvidence = $"PredictabilityVerificationStoreSaved_entries={_document.Entries.Count}";
        }
        catch (Exception ex)
        {
            _lastSaveEvidence = "PredictabilityVerificationStoreSaveFailed:" + ex.GetType().Name;
        }
    }

    private static bool Validate(Entry entry)
    {
        if (!Enum.TryParse<SearchVerificationFamily>(entry.Family, out _) ||
            string.IsNullOrWhiteSpace(entry.RuntimeTarget) || string.IsNullOrWhiteSpace(entry.CpuIdentity))
            return false;
        bool legacyValid = entry.SearchSampleCount > 0 && entry.ExactAttempts > 0 &&
            entry.VerifiedMatches >= 0 && entry.VerifiedMatches <= entry.ExactAttempts &&
            entry.ExactAggregateWorkMs > 0d && double.IsFinite(entry.ExactAggregateWorkMs);
        bool timingValid = entry.TimingSampleCount > 0 && entry.TimingExactAttempts > 0 &&
            entry.TimingAggregateWorkMs > 0d && double.IsFinite(entry.TimingAggregateWorkMs);
        return (legacyValid || timingValid) && entry.SmoothedExactMsPerAttempt > 0d &&
            double.IsFinite(entry.SmoothedExactMsPerAttempt);
    }

    private static string San(string value) => (value ?? string.Empty).Replace(';', '_').Replace('\r', ' ').Replace('\n', ' ');
}
