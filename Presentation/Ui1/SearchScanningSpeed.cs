namespace RolltheSpire2.Presentation.Ui1;

// Presentation only. Never feeds Planner, probability or physical calibration.
internal sealed class SearchScanningSpeed
{
    private readonly Queue<(double Time, long Roots)> _samples = new();
    private double _lastTime = -1;
    private long _lastRoots;
    internal void Reset() { _samples.Clear(); _lastTime=-1; _lastRoots=0; }

    internal double? Observe(double seconds, long roots)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || roots < 0) return null;
        if (seconds < _lastTime || roots < _lastRoots) Reset();
        if (seconds == _lastTime) return Current(seconds, roots);
        _lastTime=seconds; _lastRoots=roots;
        // Cold initialization cannot become the baseline of the speed window.
        if (_samples.Count==0 && roots==0) return null;
        _samples.Enqueue((seconds,roots));
        // Keep the newest sample at/before the one-second boundary, without
        // assuming an exact UI polling cadence or inventing interpolation.
        while (_samples.Count>1 && _samples.ElementAt(1).Time <= seconds-1) _samples.Dequeue();
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
        if (_samples.Count==0) return null;
        var first=_samples.Peek();
        return seconds-first.Time>=1 ? Math.Max(0,roots-first.Roots)/(seconds-first.Time) : null;
    }
}
