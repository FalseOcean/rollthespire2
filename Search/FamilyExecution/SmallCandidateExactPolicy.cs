using System.Text.Json;

namespace RolltheSpire2.Search.FamilyExecution;

// The production caller enables this only when the complete original query will
// reach Exact. This records execution boundaries, never checked filter coverage.
internal sealed class SmallCandidateExactPolicy
{
    internal const int MaximumCandidates = 8;
    private readonly object _gate = new();
    private readonly Dictionary<(string Prefix, string Tail, int Candidates), long> _stops = new();

    internal static bool ShouldHandoff(int count) => count is > 0 and <= MaximumCandidates;

    internal void Observe(string completedPrefix, IReadOnlyList<string> unexecutedCoverage, int count)
    {
        FamilyAuxiliaryEvidence.Read(nameof(SmallCandidateExactPolicy), "ObserveHandoff", () =>
        {
            if (count is < 0 or > MaximumCandidates) throw new ArgumentOutOfRangeException(nameof(count));
            if (unexecutedCoverage.Count == 0) return true;
            var key = (completedPrefix, string.Join(',', unexecutedCoverage), count);
            lock (_gate) _stops[key] = _stops.GetValueOrDefault(key) + 1;
            return true;
        }, false);
    }

    internal string GetSummary()
    {
        lock (_gate)
        {
            long batches = 0, candidates = 0, zeroBatches = 0;
            foreach (var (key, occurrences) in _stops)
            {
                if (key.Candidates == 0) zeroBatches += occurrences;
                else { batches += occurrences; candidates += key.Candidates * occurrences; }
            }
            var stops = _stops.OrderBy(item => item.Key.Prefix, StringComparer.Ordinal)
                .ThenBy(item => item.Key.Tail, StringComparer.Ordinal).ThenBy(item => item.Key.Candidates)
                .Select(item => new { completedPrefix = item.Key.Prefix, unexecutedTail = item.Key.Tail,
                    candidates = item.Key.Candidates, batches = item.Value }).ToArray();
            return $"smallCandidateExactThreshold={MaximumCandidates};smallCandidateExactBatches={batches};" +
                $"smallCandidateExactCandidates={candidates};zeroCandidateTailBatches={zeroBatches};" +
                "smallCandidateExactStopsJson=" + JsonSerializer.Serialize(stops);
        }
    }
}
