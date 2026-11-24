namespace RolltheSpire2.Core.Prediction.Maps;

internal sealed partial class BoundedActMap
{
    private UInt128[]? _endpointPairs, _endpointStarts, _endpointReach;

    private void BuildEndpointCertificate()
    {
        _token.ThrowIfCancellationRequested();
        _endpointPairs ??= new UInt128[Slots * 7];
        _endpointStarts ??= new UInt128[Slots];
        _endpointReach ??= new UInt128[Slots];
        Array.Clear(_endpointPairs); Array.Clear(_endpointStarts); Array.Clear(_endpointReach);
        // Each path advances one row at a time. Reject a producer-contract
        // violation rather than interpreting an invalid table as a negative.
        for (int node = 0; node < Slots; node++)
        {
            if (!Active(node)) continue;
            for (int lane = 0; lane < _degree[node]; lane++)
            {
                int child = _children[node * 7 + lane];
                if (child >= Slots || !Active(child) || child / 7 != node / 7 + 1)
                    throw new InvalidOperationException("Endpoint certificate requires an active adjacent-row map DAG.");
            }
        }
        for (int row = _height; row >= 1; row--)
        for (int col = 0; col < 7; col++)
        {
            int a = row * 7 + col; if (!Active(a)) continue;
            for (int other = col + 1; other < 7; other++)
            {
                int b = row * 7 + other;
                if (!Active(b) || _type[a] != _type[b]) continue;
                UInt128 ends = 0;
                for (int i = 0; i < _degree[a]; i++)
                for (int j = 0; j < _degree[b]; j++)
                {
                    int x = _children[a * 7 + i], y = _children[b * 7 + j];
                    ends |= x == y ? Bit(x) : _endpointPairs[x * 7 + y % 7];
                }
                _endpointPairs[a * 7 + other] = _endpointPairs[b * 7 + col] = ends;
            }
        }
        foreach (int start in _discoveryStartOrder)
            if (Active(start)) for (int i = 0; i < _degree[start]; i++)
                for (int j = i + 1; j < _degree[start]; j++)
                {
                    int a = _children[start * 7 + i], b = _children[start * 7 + j];
                    _endpointStarts[start] |= _endpointPairs[a * 7 + b % 7];
                }
        for (int node = Slots - 1; node >= 0; node--)
            if (Active(node))
            {
                // Include self: reaching a candidate endpoint must not be cut.
                UInt128 reach = Bit(node);
                for (int lane = 0; lane < _degree[node]; lane++) reach |= _endpointReach[_children[node * 7 + lane]];
                _endpointReach[node] = reach;
            }
        // Pruning only deletes nodes/edges, so these sets remain conservative
        // supersets. ReplayRoot rebuilds after repair changes node labels and
        // before every root's first discovery. Tables never supply a match.
    }

    // Ordered DFS and greedy member retention mirror the complete eager replay.
    // Only endpoints proven unable to duplicate and unreachable suffixes are cut.
    private void DiscoverSuffixEndpointReach(int start, int node, int length, UInt128 sequence, UInt128 word, UInt128 interior, bool measure, UInt128 ends)
    {
        if ((ends & Bit(node)) != 0 && length >= 3 && Pop(_parents[node]) >= 2)
        {
            if (measure) { UniqueSegments++; SegmentOccurrences += checked((long)(_prefix[start] * _suffix[node])); }
            var key = new GroupKey(_endpointRank[start * Slots + node], word);
            if (!_groupIndex.TryGetValue(key, out int groupId))
            {
                if (_groupCount == _groups.Length)
                {
                    if (_groups.Length >= GroupCapacity * 8) throw new InvalidOperationException("Map group workspace exhausted at research memory ceiling");
                    Array.Resize(ref _groups, _groups.Length * 2);
                    Array.Resize(ref _duplicates, _groups.Length);
                }
                groupId = _groupCount++; _groupIndex.Add(key, groupId);
                _groups[groupId] = new Group { Key = key, First = -1, Last = -1 };
            }
            ref Group g = ref _groups[groupId]; bool overlaps = false;
            for (int m = g.First; m >= 0; m = _members[m].Next)
                if ((_members[m].Interior & interior) != 0) { overlaps = true; break; }
            if (!overlaps)
            {
                if (g.Count == 7) throw new InvalidOperationException("Map segment disjoint-width invariant exceeded");
                if (_memberCount == _members.Length)
                {
                    if (_members.Length >= MemberCapacity * 8) throw new InvalidOperationException("Map segment workspace exhausted at research memory ceiling");
                    Array.Resize(ref _members, _members.Length * 2);
                }
                int member = _memberCount++;
                // Sentinel encodes segment length without an extra field.
                _members[member] = new Member { Sequence = sequence | ((UInt128)1 << (7 * length)), Interior = interior, Next = -1 };
                if (g.First < 0) g.First = member; else _members[g.Last].Next = member;
                g.Last = member; if (++g.Count == 2) _duplicates[_duplicateCount++] = groupId;
            }
        }
        if (node == _boss) return;
        UInt128 nextInterior = length > 1 ? interior | Bit(node) : interior;
        for (int i = 0; i < _degree[node]; i++)
        {
            int child = _children[node * 7 + i];
            if ((_endpointReach![child] & ends) == 0) continue;
            DiscoverSuffixEndpointReach(start, child, length + 1, sequence | ((UInt128)(uint)child << (7 * length)),
                (word << 4) | _type[child], nextInterior, measure, ends);
        }
    }
}
