using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class StandardMapExactCost
{
    // Disjoint 64-root Beta111 Production Exact runs after 64-root warmup.
    // Multiplayer 2/3/4 seats: one Act is about 500-570 ms per 64 roots
    // after warmup (one 2-seat 882 ms outlier); three Acts 970-1100 ms.
    // Solo: one Act 782/791 ms; three Acts 1623/1780 ms on the same i9.
    // Map-only CPU Exact is priced independently of empirical map hit coverage;
    // mixed Exact work awaits its own timing evidence.
    internal static double? MillisecondsPerAttempt(ExactSearchExecutionRequest request)
    {
        if (request.ProfileId != RuntimeProfileId.Beta111 || !request.Authority.IsVanilla ||
            request.WorkerCount is < 1 or > 8 ||
            !SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().CpuIdentity
                .Contains("Intel64 Family 6 Model 183", StringComparison.Ordinal) ||
            !MapOnly(request.CompiledSearch.NormalizedQuery)) return null;
        SearchQuery q = request.CompiledSearch.NormalizedQuery;
        int acts = q.StandardMaps.Any(c => c.Scope == 0) ? 3 :
            q.StandardMaps.Select(c => c.Scope).Distinct().Count();
        if (acts is < 1 or > 3) return null;
        return request.CompiledSearch.Context.Party is null ? 5.5 + 6.8 * acts : 4 + 4.2 * acts;
    }

    internal static bool MapOnly(SearchQuery query)
    {
        if (query.StandardMaps.Count == 0 || query.Players.Count is not (0 or 2 or 3 or 4) ||
            query.Players.Any(p => !p.Offers.IsEmpty || p.SelectedOption is not null || p.Results.Count > 0 ||
                JsonSerializer.Serialize(p.Conditions) != JsonSerializer.Serialize(SearchQuery.Empty))) return false;
        SearchQuery withoutMap = query with { StandardMaps = [], Players = [] };
        return JsonSerializer.Serialize(withoutMap) == JsonSerializer.Serialize(SearchQuery.Empty);
    }
}
