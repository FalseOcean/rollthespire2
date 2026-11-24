namespace RolltheSpire2.Core.Prediction.Maps;

// Private Assignment candidate scalars. Each metric has its own path extremum;
// combining properties never invents a common witness. Final owns map truth.
internal static class MapPropertyProgram
{
    internal const int Slots = 119, Start = 3, MetricCount = 9, ScratchLength = Slots;

    // 0..3 = minimum Monster/Elite/Rest/Unknown; 4..7 = corresponding
    // maxima; 8 = minimum initial Monster prefix. Fixed rooms participate.
    // Only the observation projects Round1 Unassigned to Monster: types,
    // topology and generator RNG are never modified.
    internal static int Evaluate(byte[] type, byte[] degree, byte[] children,
        int boss, int metric, int[] scratch, bool projectUnassignedToMonster = false,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if ((uint)metric >= MetricCount) throw new ArgumentOutOfRangeException(nameof(metric));
        if (type.Length < Slots || degree.Length < Slots || children.Length < Slots * 7)
            throw new ArgumentException("Expected 119 slots and seven child lanes per slot.");
        if (scratch.Length < ScratchLength) throw new ArgumentException("Insufficient property scratch.", nameof(scratch));
        if (boss < 7 || boss >= Slots) throw new ArgumentOutOfRangeException(nameof(boss));
        Array.Fill(scratch, -1, 0, Slots);
        scratch[boss] = 0;
        bool maximum = metric is >= 4 and < 8;
        byte target = (byte)((metric % 4) switch
        {
            0 => MapPointType.Monster, 1 => MapPointType.Elite,
            2 => MapPointType.RestSite, _ => MapPointType.Unknown
        });
        for (int node = boss - 1; node >= Start; node--)
        {
            token.ThrowIfCancellationRequested();
            int best = -1;
            if (degree[node] > 7) throw new ArgumentException("Child degree exceeds seven.", nameof(degree));
            for (int lane = 0; lane < degree[node]; lane++)
            {
                int child = children[node * 7 + lane];
                if (child >= Slots || child / 7 <= node / 7)
                    throw new ArgumentException("Expected increasing-row DAG edges.", nameof(children));
                int suffix = scratch[child];
                if (suffix < 0) continue;
                best = best < 0 ? suffix : maximum ? Math.Max(best, suffix) : Math.Min(best, suffix);
            }
            if (best < 0) continue;
            if (node != Start)
            {
                byte observed = type[node];
                if (projectUnassignedToMonster && observed == (byte)MapPointType.Unassigned)
                    observed = (byte)MapPointType.Monster;
                if (metric == 8) best = observed == (byte)MapPointType.Monster ? best + 1 : 0;
                else if (observed == target) best++;
            }
            scratch[node] = best;
        }
        // Reverse reachability eliminates dead ends; reading Start then limits
        // the result to complete paths reachable from this map's real Start.
        if (scratch[Start] < 0) throw new InvalidOperationException("No legal Start to Boss path.");
        return scratch[Start];
    }
}
