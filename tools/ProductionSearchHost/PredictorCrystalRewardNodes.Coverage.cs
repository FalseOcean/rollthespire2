using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record CrystalIslandCoverageUpdate(CrystalOptionProjection Projection, bool Complete,
    long States, long ClosedStates, long Merged, long Transitions, long LocalCacheHits,
    long RuntimeRows, double Seconds, bool MemoSaturated);

internal sealed partial class PredictorCrystalRewardNodes
{
    // Coverage experiment for direct Small/Big reveals followed by the existing
    // supported take semantics. No rerolls or mid-reward relic-obtain simulation.
    // Unlike the bounded suggestion search, this traversal discards NO work.
    private sealed class CoverageQuery(ImmutableArray<CrystalRewardOption> selected)
    {
        internal readonly Query Output = new(selected);
        internal readonly HashSet<(UInt128 Fog, int Left, string Physical)> Visited = [];
        internal readonly HashSet<string> Outcomes = [];
        internal readonly List<CrystalRewardRoute> Rows = [];
        internal IEnumerator<bool>? Cursor;
        internal ExceptionDispatchInfo? Fault;
        internal bool Complete, MemoSaturated;
        internal long States, ClosedStates, Merged, Transitions;
        internal double Seconds;
    }

    private readonly Dictionary<string, CoverageQuery> _coverage = [];

    internal CrystalIslandCoverageUpdate AdvanceCoverage(ImmutableArray<CrystalRewardOption> selected,
        TimeSpan budget, CancellationToken token = default, int memoLimit = 100_000)
    {
        if (memoLimit < 0) throw new ArgumentOutOfRangeException(nameof(memoLimit));
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        string key = Selection(selected);
        if (!_coverage.TryGetValue(key, out var query))
        {
            // Explicit per-query replay, with shared geometric fragments. Eviction
            // forgets progress; it never creates a completion receipt.
            if (_coverage.Count >= 4)
            {
                var oldest = _coverage.First();
                oldest.Value.Cursor?.Dispose();
                _coverage.Remove(oldest.Key);
            }
            query = new(selected);
            _coverage.Add(key, query);
            query.Cursor = Cover(new(_initialFog, _source.Revealed, [], 0), [], query, memoLimit).GetEnumerator();
        }
        query.Fault?.Throw();
        try
        {
            while (!query.Complete && !token.IsCancellationRequested && watch.Elapsed < budget)
                if (!query.Cursor!.MoveNext()) query.Complete = true;
        }
        catch (Exception error)
        {
            // A faulted iterator subsequently returns false. Never let retry turn
            // that into an exhaustion certificate.
            query.Fault = ExceptionDispatchInfo.Capture(error);
            throw;
        }
        finally { query.Seconds += watch.Elapsed.TotalSeconds; }
        return new(new(query.Output.Available.ToImmutableDictionary(), query.Output.Plan, query.Output.Gold),
            query.Complete, query.States, query.ClosedStates, query.Merged, query.Transitions,
            _cacheHits, query.Output.RuntimeRows, query.Seconds, query.MemoSaturated);
    }

    internal ImmutableArray<CrystalRewardRoute> CoverageRows(ImmutableArray<CrystalRewardOption> selected) =>
        _coverage.TryGetValue(Selection(selected), out var query) ? query.Rows.ToImmutableArray() : [];

    // Covering argument: every legal action either touches a hidden item cell,
    // hence appears in at least one residual fragment, or touches only blank
    // cells, covered by the second loop. Fragments are NOT solved in isolation:
    // each transition applies its full boundary effect to the common fog.
    private IEnumerable<LocalStep> CoverageMoves(UInt128 fog)
    {
        var seenMoves = new HashSet<Move>();
        var seenEffects = new HashSet<(UInt128 Removed, string Physical)>();
        foreach (var mask in _items)
        foreach (var part in Parts(fog & mask))
        foreach (var step in Fragment(part, fog))
            if (seenMoves.Add(step.Move) && seenEffects.Add((step.Removed, string.Join(',', step.Physical))))
                yield return step;

        foreach (var move in _moves)
        {
            if ((fog & move.Center) == 0 || (move.Mask & fog & _occupied) != 0) continue;
            UInt128 removed = move.Mask & fog;
            if (seenEffects.Add((removed, ""))) yield return new(move, removed, [], []);
        }
    }

    private IEnumerable<bool> Cover(Node node, ImmutableArray<int> physical, CoverageQuery query, int memoLimit)
    {
        int left = _source.Remaining - node.Path.Length;
        string history = string.Join(',', physical);
        var key = (node.Fog, left, history);
        // Terminal fog has no continuation. Do not let millions of irrelevant
        // final board variants evict the useful nonterminal-state memo budget.
        if (left == 0 ? query.Outcomes.Contains(history) : query.Visited.Contains(key))
        { query.Merged++; yield return true; yield break; }
        if (left > 0)
        {
            if (query.Visited.Count < memoLimit) query.Visited.Add(key);
            else query.MemoSaturated = true; // recompute instead of dropping this branch
        }
        query.States++;
        yield return true;

        if (left == 0)
        {
            // Geometry no longer affects future choices once the reveal budget
            // ends. Exact physical completion order determines callback order and
            // immediate curse additions, even for a zero-subscription item.
            if (query.Outcomes.Add(history))
            {
                var run = PredictorCrystalExplorer.Replay(_source, node.Path);
                query.Output.RuntimeRows++;
                if (run.Phase == PredictorPhase.Rewards)
                {
                    if (!run.ExportCrystal().Revealed.SequenceEqual(node.Order))
                        throw new InvalidOperationException("CrystalIslandCoverageCallbackMismatch");
                    var row = new CrystalRewardRoute(node.Path, run.Rewards);
                    query.Rows.Add(row);
                    Consider(query.Output, row);
                }
            }
        }
        else
        {
            foreach (var step in CoverageMoves(node.Fog))
            {
                query.Transitions++;
                var next = new Node(node.Fog & ~step.Removed, node.Order.AddRange(step.Completed),
                    node.Path.Add(step.Move.Step), node.Centers | step.Move.Center);
                foreach (bool tick in Cover(next, physical.AddRange(step.Physical), query, memoLimit)) yield return tick;
            }
        }
        query.ClosedStates++;
    }
}
