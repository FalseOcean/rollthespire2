using System.Numerics;

namespace RolltheSpire2.Core.Prediction.Maps;

/// <summary>
/// Immutable positive grouped route predicate. Compilation snapshots the request;
/// the table does not observe or mutate a game RNG or RunState.
/// </summary>
internal sealed class MapDrawingProgram
{
    private const int Slots = 119, Start = 3, Types = 9, ChildLanes = 7;
    private static readonly MapPointType[] Selectable =
        [MapPointType.Monster, MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop, MapPointType.Unknown];
    private readonly uint[] _transitions;

    private MapDrawingProgram(int stateCount, bool alwaysReject, uint[] transitions)
    {
        StateCount = stateCount;
        WordCount = (stateCount + 31) / 32;
        AcceptState = stateCount - 1;
        AlwaysReject = alwaysReject;
        _transitions = transitions;
    }

    internal int StateCount { get; }
    internal int WordCount { get; }
    internal int AcceptState { get; }
    internal bool AlwaysReject { get; }

    // GPU uploaders own their buffers; no consumer can mutate this program.
    internal uint[] ExportTransitions() => (uint[])_transitions.Clone();

    // Scalar daily thresholds may be arbitrarily large. Preserve that threshold's
    // proof without first materializing one query node per requested occurrence.
    internal static MapDrawingProgram RejectAboveProvenBudget(long required, int provenCountableBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(provenCountableBudget);
        if (required <= provenCountableBudget)
            throw new ArgumentOutOfRangeException(nameof(required), "The requirement must exceed the proven room budget.");
        return new MapDrawingProgram(1, true, new uint[Types]);
    }

    internal static MapDrawingProgram Compile(MapRouteSequenceQuery query, int provenCountableBudget)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(provenCountableBudget);
        if (query.AdditionalCounts.Count != 0)
            throw new NotSupportedException("MapDrawingProgram supports positive groups only; additional count conditions are not implemented.");

        // The caller must prove this bound for its actual map context. In the
        // supported SP laws it is height - 3: first Monster, fixed treasure row
        // (including its replacement), and terminal Rest cannot contribute.
        long required = 0;
        foreach (var group in query.Groups)
        {
            required += group.Nodes.Count;
            if (required > provenCountableBudget)
                return RejectAboveProvenBudget(required, provenCountableBudget);
        }

        var groups = new List<(int[] Counts, int[] Strides, int Product)>();
        long totalStates = 1;
        foreach (var group in query.Groups)
        {
            if (group.Nodes.Count == 0) continue;
            var counts = new int[Selectable.Length];
            foreach (var type in group.Nodes)
            {
                int dimension = Array.IndexOf(Selectable, type);
                if (dimension < 0) throw new ArgumentException("Unsupported drawing room type.", nameof(query));
                counts[dimension]++;
            }
            var strides = new int[Selectable.Length];
            long product = 1;
            for (int dimension = 0; dimension < counts.Length; dimension++)
            {
                strides[dimension] = checked((int)product);
                long radix = (long)counts[dimension] + 1;
                if (product > (Array.MaxLength / Types) / radix)
                    throw new NotSupportedException("Drawing state table exceeds the supported array size.");
                product *= radix;
            }
            totalStates += product - 1;
            if (totalStates > Array.MaxLength / Types)
                throw new NotSupportedException("Drawing state table exceeds the supported array size.");
            groups.Add((counts, strides, (int)product));
        }

        // One incomplete tuple per group. Completion shares the next group's
        // zero tuple; the final state is absorbing. Keep whole tuples when paths
        // merge: independently combining dimension maxima invents a route.
        int stateCount = (int)totalStates;
        var transitions = new uint[checked(stateCount * Types)];
        for (int state = 0; state < stateCount; state++)
            for (int type = 0; type < Types; type++) transitions[state * Types + type] = (uint)state;
        int groupBase = 0;
        foreach (var group in groups)
        {
            for (int local = 0; local < group.Product - 1; local++)
            {
                int state = groupBase + local;
                for (int dimension = 0; dimension < Selectable.Length; dimension++)
                {
                    int consumed = local / group.Strides[dimension] % (group.Counts[dimension] + 1);
                    if (consumed < group.Counts[dimension])
                        transitions[state * Types + (int)Selectable[dimension]] =
                            (uint)(groupBase + local + group.Strides[dimension]);
                }
            }
            groupBase += group.Product - 1;
        }
        return new MapDrawingProgram(stateCount, false, transitions);
    }

    /// <summary>
    /// Match compact row-major SP map arrays. Child edges must advance to a later
    /// row. Scratch belongs to the caller and may be reused after this call.
    /// Projection is explicit Round1 proposal interpretation, never Full truth.
    /// </summary>
    internal bool Match(byte[] type, byte[] degree, byte[] children, UInt128 modifiable,
        int boss, uint[] scratch, bool projectUnassignedToMonster, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(degree);
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(scratch);
        token.ThrowIfCancellationRequested();
        if (type.Length < Slots || degree.Length < Slots || children.Length < Slots * ChildLanes)
            throw new ArgumentException("Expected 119 node slots and seven child lanes per slot.");
        if ((uint)boss >= Slots || boss == Start) throw new ArgumentOutOfRangeException(nameof(boss));
        int scratchSize = checked(Slots * WordCount);
        if (scratch.Length < scratchSize) throw new ArgumentException("Insufficient state scratch.", nameof(scratch));
        if (AlwaysReject) return false;

        Array.Clear(scratch, 0, scratchSize);
        scratch[Start * WordCount] = 1;
        for (int node = 0; node < Slots; node++)
        {
            token.ThrowIfCancellationRequested();
            if (node == boss) continue;
            int source = node * WordCount;
            bool live = false;
            for (int word = 0; word < WordCount; word++) live |= scratch[source + word] != 0;
            if (!live) continue;
            if (degree[node] > ChildLanes) throw new ArgumentException("Invalid compact child degree.", nameof(degree));
            for (int lane = 0; lane < degree[node]; lane++)
            {
                int child = children[node * ChildLanes + lane];
                if (child >= Slots || child / ChildLanes <= node / ChildLanes)
                    throw new ArgumentException("Compact child edges must advance to a later map row.", nameof(children));
                bool countable = child != Start && child != boss && (modifiable & ((UInt128)1 << child)) != 0;
                int room = countable ? type[child] : 0;
                if (projectUnassignedToMonster && countable && room == 0) room = (int)MapPointType.Monster;
                if ((uint)room >= Types) throw new ArgumentException("Invalid compact room type.", nameof(type));
                Advance(scratch.AsSpan(source, WordCount), scratch.AsSpan(child * WordCount, WordCount), room);
            }
        }
        return HasAccept(scratch.AsSpan(boss * WordCount, WordCount));
    }

    private bool HasAccept(ReadOnlySpan<uint> states) =>
        (states[AcceptState >> 5] & (1u << (AcceptState & 31))) != 0;

    private void Advance(ReadOnlySpan<uint> source, Span<uint> destination, int room)
    {
        for (int word = 0; word < WordCount; word++)
        {
            uint bits = source[word];
            while (bits != 0)
            {
                int state = word * 32 + BitOperations.TrailingZeroCount(bits);
                int next = (int)_transitions[state * Types + room];
                destination[next >> 5] |= 1u << (next & 31);
                bits &= bits - 1;
            }
        }
    }
}
