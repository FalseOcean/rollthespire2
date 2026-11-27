namespace RolltheSpire2.Search.FamilyExecution;

// One instance per search. Only contiguous, completed foreground windows are comparable.
internal sealed class AdaptiveGpuRootWindow
{
    private const int Small = PrivateOrdinalBuffer.Capacity;
    private const int Groups = 3;
    private const double MinimumGroupMs = 32;
    private readonly int _maximum;
    private readonly double[] _costs = new double[Groups];
    private readonly Dictionary<int, double> _acceptedCosts = new();
    private int _current = Small, _accepted = Small, _groups;
    private long _groupRoots, _measuredRoots, _referenceRoots;
    private double _groupMs, _referenceCost, _peakMatchRate;
    private bool _first = true, _ready, _finished;
    private double _warmMs;

    internal AdaptiveGpuRootWindow(int maximum)
    {
        _maximum = maximum >= 8 * Small ? 8 * Small : maximum >= 4 * Small ? 4 * Small :
            maximum >= 2 * Small ? 2 * Small : Small;
        if (_maximum == Small) State = "PathLimit";
    }

    internal int Selected => _accepted;
    internal int Current => _current;
    internal string State { get; private set; } = "Warmup";

    internal int Choose(long remainingRoots, int remainingTargetMatches)
    {
        if (_maximum == Small || (_finished && _current == Small)) return _current;
        // At least three groups and a binding window must remain. The match rate
        // is conservative because the Exact consumer can report hits in bursts.
        int next = Math.Min(_maximum, _accepted * 2);
        long trialRoots = Math.Max(7L * next, _referenceRoots * 2 + next);
        bool nearTarget = remainingTargetMatches <= Math.Ceiling(_peakMatchRate * trialRoots);
        if (nearTarget)
        {
            StopAt(Small, "NearTarget");
            return _current;
        }
        if (!_finished && _ready && _accepted < _maximum)
        {
            if (remainingRoots >= 2 * trialRoots)
            {
                _current = next;
                State = "Trial";
                _first = true;
            }
            else { _finished = true; State = "InsufficientRemaining"; }
            _ready = false;
        }
        return _current;
    }

    internal void Refuse()
    {
        // Promotion is adjacent, so every smaller rung was already accepted.
        int fallback = _current > _accepted ? _accepted : Math.Max(Small, _current / 2);
        StopAt(fallback, "BindingRefused");
    }

    private void StopAt(int window, string state)
    {
        State = state;
        _current = _accepted = window;
        _referenceCost = _acceptedCosts.GetValueOrDefault(window);
        _groups = 0;
        _measuredRoots = _groupRoots = 0;
        _groupMs = 0;
        _first = true;
        _ready = false;
        _finished = true;
    }

    internal void Observe(int windowRootCount, double elapsedMilliseconds, int deliveredMatches)
    {
        if ((_finished && _current == Small) || _maximum == Small || windowRootCount != _current ||
            !double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds <= 0) return;
        _peakMatchRate = Math.Max(_peakMatchRate, (double)deliveredMatches / windowRootCount);
        if (_first) { _first = false; return; }
        if (_warmMs < 96) { _warmMs += elapsedMilliseconds; return; }
        if (_ready) return;
        if (State == "Warmup") State = "Reference";
        _groupMs += elapsedMilliseconds;
        _groupRoots += windowRootCount;
        if (_groupRoots < 2L * _current || _groupMs < MinimumGroupMs) return;
        _costs[_groups++] = _groupMs * Small / _groupRoots;
        _measuredRoots += _groupRoots;
        _groupMs = 0;
        _groupRoots = 0;
        if (_groups < Groups) return;
        Array.Sort(_costs);
        double median = _costs[1];
        if (_current > _accepted && median > _referenceCost * 0.95)
        {
            State = "TrialRejected";
            _current = _accepted;
            _first = true;
            _finished = true;
        }
        else if (_current == _accepted && _accepted > Small && median > _acceptedCosts[_accepted / 2] * 1.10)
        {
            // Compare with the previous rung. A temporary spike relative to the
            // best observed cost can still be faster than stepping down.
            StopAt(_accepted / 2, "DeliverySlowdown");
        }
        else if (_current > _accepted || _accepted == Small)
        {
            _accepted = _current;
            _acceptedCosts[_current] = _referenceCost = median;
            _referenceRoots = _measuredRoots;
            _ready = _accepted < _maximum;
            State = _ready ? "ReferenceReady" : "Selected";
        }
        _groups = 0;
        _measuredRoots = 0;
    }
}
