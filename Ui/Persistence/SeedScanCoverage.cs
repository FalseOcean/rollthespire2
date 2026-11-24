using System.Text.Json.Serialization;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Ui.Persistence;

internal readonly record struct SeedScanRange(ulong Start, ulong EndExclusive);

// Kept per history identity. Adjacent completed scans normally cost one range,
// even across restarts. The count is derived from the union on load.
internal sealed class SeedScanCoverage
{
    private readonly List<SeedScanRange> _ranges = [];
    public IReadOnlyList<SeedScanRange> Ranges => _ranges;
    internal ulong Count { get; private set; }

    public SeedScanCoverage() { }

    [JsonConstructor]
    public SeedScanCoverage(IReadOnlyList<SeedScanRange>? ranges)
    {
        if (ranges is not null)
            foreach (var range in ranges) Add(range.Start, range.EndExclusive);
    }

    internal void Add(ulong start, ulong endExclusive)
    {
        if (start >= endExclusive || endExclusive > Beta110SeedCodec.SpaceSize) return;
        int low = 0, high = _ranges.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (_ranges[mid].EndExclusive < start) low = mid + 1;
            else high = mid;
        }
        int first = low;
        if (first < _ranges.Count && _ranges[first].Start <= start && _ranges[first].EndExclusive >= endExclusive)
            return;
        while (low < _ranges.Count && _ranges[low].Start <= endExclusive)
        {
            var existing = _ranges[low++];
            start = Math.Min(start, existing.Start);
            endExclusive = Math.Max(endExclusive, existing.EndExclusive);
            Count -= existing.EndExclusive - existing.Start;
        }
        if (first == low) _ranges.Insert(first, new(start, endExclusive));
        else
        {
            _ranges[first] = new(start, endExclusive);
            _ranges.RemoveRange(first + 1, low - first - 1);
        }
        Count += endExclusive - start;
    }
}
