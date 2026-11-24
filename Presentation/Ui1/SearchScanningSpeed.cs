namespace RolltheSpire2.Presentation.Ui1;

// Presentation only. Never feeds Planner, probability or physical calibration.
internal sealed class SearchScanningSpeed
{
    private const int RecentIntervals = 3;
    private const int MaximumSamples = 256;
    private readonly Queue<(double Time, long Roots)> _samples = new();
    private double _lastTime = -1;
    private long _lastRoots;
    private double _firstProgressTime;
    private double _longestInterval;
    internal void Reset()
    {
        _samples.Clear(); _lastTime = -1; _lastRoots = 0;
        _firstProgressTime = 0; _longestInterval = 0;
    }

    internal double? Observe(double seconds, long roots)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || roots < 0) return null;
        if (seconds < _lastTime || roots < _lastRoots) Reset();
        bool progressed = roots > _lastRoots;
        _lastTime = seconds; _lastRoots = roots;
        // Cold initialization cannot become the baseline of the speed window.
        if (progressed)
        {
            if (_samples.Count == 0) _firstProgressTime = seconds;
            _samples.Enqueue((seconds, roots));
            // Only completed progress intervals move the baseline. UI polls with
            // unchanged roots must not reduce an eight-second batch to one second.
            while (_samples.Count > RecentIntervals + 1 && _samples.ElementAt(1).Time <= seconds - 1)
                _samples.Dequeue();
            while (_samples.Count > MaximumSamples) _samples.Dequeue();
            _longestInterval = 0;
            double previous = _samples.Peek().Time;
            foreach (var sample in _samples)
            {
                _longestInterval = Math.Max(_longestInterval, sample.Time - previous);
                previous = sample.Time;
            }
        }
        return Current(seconds,roots);
    }

    internal static double? Terminal(RolltheSpire2.Search.Contracts.SearchProgressSnapshot progress,double? rolling)
    {
        if (progress.State == RolltheSpire2.Search.Contracts.SearchRunState.Running) return rolling;
        return progress.ObservedScanningRoots > 0 && progress.ObservedScanningSeconds > 0 && double.IsFinite(progress.ObservedScanningSeconds)
            ? progress.ObservedScanningRoots / progress.ObservedScanningSeconds : null;
    }
    private double? Current(double seconds,long roots)
    {
        if (_longestInterval <= 0 || seconds - _firstProgressTime < 1) return null;
        // Waiting belongs in the denominator, including delayed or stalled work.
        // Once two recent batch intervals pass without progress, withdraw the
        // observation rather than displaying an increasingly old positive rate.
        var last = _samples.Last();
        if (seconds - last.Time > Math.Max(1, 2 * _longestInterval)) return null;
        var first = _samples.Peek();
        double elapsed = seconds - first.Time;
        return elapsed > 0 ? (roots - first.Roots) / elapsed : null;
    }
}
