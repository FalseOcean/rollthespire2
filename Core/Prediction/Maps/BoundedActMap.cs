using System.Diagnostics;
using System.Numerics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Core.Prediction.Maps;



/// <summary>
/// Worker-owned complete StandardActMap numerical replay; adopted from the audited Map research donor. No managed graph, complete paths or
/// string keys in replay. Never shared concurrently. Used only by the M StandardMap CPU invocation.
/// Slots are raw row*7+column; coordinates may move only during final postprocessing.
/// Parent masks are unordered; child lanes retain successful edge-insertion order.
/// Graph slots stay fixed. Group/member buffers grow to explicit memory ceilings;
/// exhaustion is an error, never a semantic fallback.
/// </summary>
internal sealed partial class BoundedActMap
{
    private const int Slots = 119, Start = 3, GroupCapacity = 32768, MemberCapacity = 65536;
    private readonly ActContext _context;
    private readonly int _height, _boss;
    private readonly ulong _streamOffset;
    private readonly byte[] _type = new byte[Slots], _col = new byte[Slots], _degree = new byte[Slots];
    private readonly byte[] _children = new byte[Slots * 7];
    private readonly UInt128[] _parents = new UInt128[Slots];
    private readonly int[] _grid = new int[Slots], _candidates = new int[Slots];
    private readonly byte[] _queue = new byte[64];
    private readonly int[] _targets = new int[9], _endpointRank = new int[Slots * Slots];
    private Group[] _groups = new Group[GroupCapacity];
    private Member[] _members = new Member[MemberCapacity];
    private int[] _duplicates = new int[GroupCapacity];
    private readonly Dictionary<GroupKey, int> _groupIndex = new(GroupCapacity);
    private readonly IComparer<int> _groupComparer;
    private readonly ulong[] _suffix = new ulong[Slots], _prefix = new ulong[Slots];
    private UInt128 _active, _modifiable;
    private BoundedMapRng _rng;
    private CancellationToken _token;
    private int _groupCount, _memberCount, _duplicateCount, _queueHead, _queueCount;
    private bool _complete;

    private readonly record struct GroupKey(int Endpoint, UInt128 Word);
    private struct Group { internal GroupKey Key; internal int First, Last, Count; }
    private struct Member { internal UInt128 Sequence, Interior; internal int Next; }

    internal BoundedActMap(ActContext context)
    {
        int index = context.Act switch { ActKind.Overgrowth or ActKind.Underdocks => 0,
            ActKind.Hive => 1, ActKind.Glory => 2, _ => throw new ArgumentOutOfRangeException(nameof(context)) };
        if (context.ActIndex != index || context.HasSecondBoss)
            throw new ArgumentException("Expected a standard-map context ending at the first Boss.", nameof(context));
        _context = context;
        _height = Beta111StandardMapGenerator.GetNumberOfRooms(context.Act, context.IsMultiplayer);
        _boss = (_height + 1) * 7 + 3;
        _streamOffset = Beta111Profile.Instance.DeriveNamedStreamSeed(0, $"act_{index + 1}_map");
        // Source group key = endpoint prefix then CSV of single-digit PointTypes.
        // Prefix strings are constructed/sorted once, never during a root replay.
        var endpoints = new List<(string Key, int Index)>();
        for (int a = 0; a < Slots; a++)
        for (int b = 0; b < Slots; b++)
            if (b / 7 >= a / 7 + 2)
                endpoints.Add((a / 7 == 0 ? $"0-{b % 7},{b / 7}-" : $"{a % 7},{a / 7}-{b % 7},{b / 7}-", a * Slots + b));
        endpoints.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
        for (int i = 0; i < endpoints.Count; i++) _endpointRank[endpoints[i].Index] = i;
        _groupComparer = Comparer<int>.Create((a, b) =>
        {
            int c = _groups[a].Key.Endpoint.CompareTo(_groups[b].Key.Endpoint);
            return c != 0 ? c : _groups[a].Key.Word.CompareTo(_groups[b].Key.Word);
        });
    }

    public int RngCalls => _rng.Calls;
    public (ulong S0, ulong S1, ulong S2, ulong S3) RngState => _rng.State;
    public long PruneTicks { get; private set; }
    public long GateTicks { get; private set; }
    public int Mutations { get; private set; }
    public int Discoveries { get; private set; }
    public int PeakGroups { get; private set; }
    public int PeakMembers { get; private set; }
    public long UniqueSegments { get; private set; }
    public long CompletePathVisits { get; private set; }
    public long SegmentOccurrences { get; private set; }
    public long RetainedSegments { get; private set; }
    public long GroupVisits { get; private set; }
    public int DeletePoints { get; private set; }
    public int BreakAttempts { get; private set; }
    public int SuccessfulBreaks { get; private set; }
    public int BrokenEdges { get; private set; }
    public long FixedArrayPayloadBytes => 3L * Slots + Slots * 7L + Slots * 16L + 2L * Slots * sizeof(int)
        + 64 + 9L * sizeof(int) + Slots * Slots * sizeof(int)
        + _groups.Length * (long)System.Runtime.CompilerServices.Unsafe.SizeOf<Group>()
        + _members.Length * (long)System.Runtime.CompilerServices.Unsafe.SizeOf<Member>()
        + _duplicates.Length * sizeof(int) + 2L * Slots * sizeof(ulong)
        + Slots * sizeof(ulong);

    internal void ReplayRoot(ulong rootHash, CancellationToken cancellationToken = default, bool measureWork = false)
    {
        _complete = false;
        _token = cancellationToken; _token.ThrowIfCancellationRequested();
        _rng = new BoundedMapRng(unchecked(rootHash + _streamOffset));
        Array.Clear(_type); Array.Clear(_degree); Array.Clear(_parents); Array.Fill(_grid, -1);
        for (int i = 0; i < Slots; i++) _col[i] = (byte)(i % 7);
        _active = Bit(Start) | Bit(_boss); _modifiable = _active;
        PruneTicks = GateTicks = UniqueSegments = CompletePathVisits = SegmentOccurrences = RetainedSegments = GroupVisits = 0;
        Mutations = Discoveries = PeakGroups = PeakMembers = DeletePoints = BreakAttempts = SuccessfulBreaks = BrokenEdges = 0;
        _targets[(int)MapPointType.RestSite] = _context.Act switch
        {
            ActKind.Overgrowth or ActKind.Underdocks => _rng.Gaussian(7, 6, 7),
            ActKind.Hive => _rng.Gaussian(6, 6, 7), _ => _rng.Int(2) + 5
        };
        _targets[(int)MapPointType.Unknown] = _rng.Gaussian(12, 10, 14) - (_context.ActIndex > 0 ? 1 : 0);
        _targets[(int)MapPointType.Elite] = _context.HasSwarmingElites ? 8 : 5;
        _targets[(int)MapPointType.Shop] = 3;
        Topology();
        Assignment();
        for (int pass = 0; pass < 3; pass++)
        {
            long stamp = measureWork ? Stopwatch.GetTimestamp() : 0;
            int mutations = 0;
            Discover(measureWork);
            while (Prune())
            {
                Mutations++; if (++mutations > 50) throw new InvalidOperationException("Unable to prune matching segments in 51 iterations");
                Discover(measureWork);
            }
            if (measureWork) PruneTicks += Stopwatch.GetTimestamp() - stamp;
            bool repaired = Repair(2) | Repair(6) | Repair(4) | Repair(1);
            if (!repaired) break;
        }
        PostProcess(); _token.ThrowIfCancellationRequested(); _complete = true;

    }

    private static UInt128 Bit(int node) => (UInt128)1 << node;
    private static int Pop(UInt128 x) => BitOperations.PopCount((ulong)x) + BitOperations.PopCount((ulong)(x >> 64));
    private static int Take(ref UInt128 bits)
    {
        ulong lo = (ulong)bits;
        int n = lo != 0 ? BitOperations.TrailingZeroCount(lo) : 64 + BitOperations.TrailingZeroCount((ulong)(bits >> 64));
        bits &= bits - 1; return n;
    }
    private bool Active(int node) => (_active & Bit(node)) != 0;
    private int Cell(int col, int row) => _grid[col * 17 + row];
    private int Create(int col, int row)
    {
        int n = row * 7 + col;
        _grid[col * 17 + row] = n; _active |= Bit(n); _modifiable |= Bit(n); return n;
    }
    private void AddEdge(int parent, int child)
    {
        if ((_parents[child] & Bit(parent)) != 0) return;
        _children[parent * 7 + _degree[parent]++] = (byte)child; _parents[child] |= Bit(parent);
    }
    private void RemoveEdge(int parent, int child)
    {
        for (int i = 0; i < _degree[parent]; i++)
            if (_children[parent * 7 + i] == child)
            {
                for (int j = i; j + 1 < _degree[parent]; j++) _children[parent * 7 + j] = _children[parent * 7 + j + 1];
                _degree[parent]--; break;
            }
        _parents[child] &= ~Bit(parent);
    }
    private void Topology()
    {
        int first = -1;
        for (int attempt = 0; attempt < 7; attempt++)
        {
            int col; do { col = _rng.Int(7); } while (attempt == 1 && col == first);
            if (attempt == 0) first = col;
            int current = Create(col, 1);
            for (int row = 1; row < _height; row++)
            {
                int next = NextColumn(current);
                int child = Create(next, row + 1); AddEdge(current, child); current = child;
            }
        }
        for (int col = 0; col < 7; col++)
        {
            if (Cell(col, _height) >= 0) AddEdge(Cell(col, _height), _boss);
            if (Cell(col, 1) >= 0) AddEdge(Start, Cell(col, 1));
        }
    }
    private int NextColumn(int node)
    {
        Span<int> direction = stackalloc int[3] { -1, 0, 1 };
        for (int i = 2; i > 0; i--) { int j = _rng.Int(i + 1); (direction[i], direction[j]) = (direction[j], direction[i]); }
        int col = node % 7, row = node / 7;
        foreach (int delta in direction)
        {
            int target = Math.Clamp(col + delta, 0, 6), actual = target - col;
            int other = Cell(target, row); bool bad = false;
            if (actual != 0 && other >= 0)
                for (int i = 0; i < _degree[other]; i++)
                    if (_children[other * 7 + i] % 7 - target == -actual) { bad = true; break; }
            if (!bad) return target;
        }
        throw new InvalidOperationException("Cannot find next node");
    }
    private void Assignment()
    {
        for (int col = 0; col < 7; col++)
        {
            Fix(col, _height, 4); Fix(col, _height - 6, _context.ReplaceTreasureWithElites ? 6 : 3); Fix(col, 1, 5);
        }
        _queueHead = _queueCount = 0;
        ReadOnlySpan<int> assignmentOrder = [4, 2, 6, 1];
        foreach (int type in assignmentOrder)
            for (int i = 0; i < _targets[type]; i++) Enqueue((byte)type);
        int rounds = 0;
        for (int pass = 0; pass < 3 && _queueCount > 0; pass++)
        {
            int count = Candidates(0, false); ShuffleCandidates(count);
            for (int i = 0; i < count && _queueCount > 0; i++)
            {
                int node = _candidates[i], attempts = _queueCount;
                for (int j = 0; j < attempts; j++)
                {
                    byte type = _queue[_queueHead]; _queueHead = (_queueHead + 1) & 63; _queueCount--;
                    if (Valid(node, type)) { _type[node] = type; break; }
                    Enqueue(type);
                }
            }
            rounds = pass + 1;
        }
        for (int node = 7; node < _boss; node++) if (Active(node) && _type[node] == 0) _type[node] = 5;
        _type[Start] = 8; _type[_boss] = 7;
    }
    private void Enqueue(byte type) { _queue[(_queueHead + _queueCount) & 63] = type; _queueCount++; }
    private void Fix(int col, int row, int type)
    {
        int n = Cell(col, row); if (n < 0) return;
        _type[n] = (byte)type; _modifiable &= ~Bit(n);
    }
    private int Candidates(int type, bool modifiable)
    {
        int count = 0;
        // Column-major grid order is already exactly the source StableShuffle sort.
        for (int col = 0; col < 7; col++)
        for (int row = 1; row <= _height; row++)
        {
            int n = Cell(col, row);
            if (n >= 0 && _type[n] == type && (!modifiable || (_modifiable & Bit(n)) != 0)) _candidates[count++] = n;
        }
        return count;
    }
    private void ShuffleCandidates(int count)
    {
        for (int i = count - 1; i > 0; i--) { int j = _rng.Int(i + 1); (_candidates[i], _candidates[j]) = (_candidates[j], _candidates[i]); }
    }
    private bool Valid(int node, int type)
    {
        int row = node / 7;
        if (type == 4 && row >= _height - 2) return false;
        if ((type == 4 || type == 6) && row < 6) return false;
        bool adjacentRestricted = type is 2 or 3 or 4 or 6;
        if (adjacentRestricted)
        {
            UInt128 parents = _parents[node];
            while (parents != 0) if (_type[Take(ref parents)] == type) return false;
            for (int i = 0; i < _degree[node]; i++) if (_type[_children[node * 7 + i]] == type) return false;
        }
        if (type is 1 or 2 or 4 or 5 or 6)
        {
            UInt128 parents = _parents[node];
            while (parents != 0)
            {
                int p = Take(ref parents);
                for (int i = 0; i < _degree[p]; i++)
                {
                    int sibling = _children[p * 7 + i]; if (sibling != node && _type[sibling] == type) return false;
                }
            }
        }
        return true;
    }
    private bool Repair(int type)
    {
        int have = 0; for (int n = 7; n < _boss; n++) if (Active(n) && _type[n] == type) have++;
        int missing = _targets[type] - have; if (missing <= 0) return false;
        int count = Candidates(5, true); ShuffleCandidates(count); bool changed = false;
        for (int i = 0; i < count && missing > 0; i++)
            if (Valid(_candidates[i], type)) { _type[_candidates[i]] = (byte)type; missing--; changed = true; }
        return changed;
    }

    // For fixed endpoints, source first-occurrence order equals a DFS from the
    // start endpoint. Every suffix can follow the earliest prefix reaching it.
    // Other source paths only repeat the same concrete segment. Groups with
    // different endpoints cannot affect overlap suppression. Sort duplicate
    // groups by exact source key only after retaining each group's members.
    private void Discover(bool measure)
    {
        _token.ThrowIfCancellationRequested(); Discoveries++;
        _groupIndex.Clear(); _groupCount = _memberCount = _duplicateCount = 0;
        if (measure) CountPaths();
        for (int start = 0; start < Slots; start++)
            if (Active(start) && (_degree[start] > 1 || start == Start))
                DiscoverSuffix(start, start, 1, (UInt128)(uint)start, _type[start], 0, measure);
        if (measure)
        {
            CompletePathVisits += (long)_suffix[Start]; RetainedSegments += _memberCount; GroupVisits += _groupCount;
        }
        PeakGroups = Math.Max(PeakGroups, _groupCount); PeakMembers = Math.Max(PeakMembers, _memberCount);
        Array.Sort(_duplicates, 0, _duplicateCount, _groupComparer);
    }
    private void DiscoverSuffix(int start, int node, int length, UInt128 sequence, UInt128 word, UInt128 interior, bool measure)
    {
        if (length >= 3 && Pop(_parents[node]) >= 2)
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
            DiscoverSuffix(start, child, length + 1, sequence | ((UInt128)(uint)child << (7 * length)),
                (word << 4) | _type[child], nextInterior, measure);
        }
    }
    private void CountPaths()
    {
        Array.Clear(_suffix); Array.Clear(_prefix); _suffix[_boss] = 1; _prefix[Start] = 1;
        for (int n = _boss - 1; n >= 0; n--)
            for (int i = 0; i < _degree[n]; i++) _suffix[n] = checked(_suffix[n] + _suffix[_children[n * 7 + i]]);
        for (int n = 0; n < _boss; n++)
            for (int i = 0; i < _degree[n]; i++)
            {
                int child = _children[n * 7 + i]; _prefix[child] = checked(_prefix[child] + _prefix[n]);
            }
    }
    private bool Prune()
    {
        Span<UInt128> matches = stackalloc UInt128[7];
        for (int i = 0; i < _duplicateCount; i++)
        {
            var g = _groups[_duplicates[i]]; int count = 0;
            for (int m = g.First; m >= 0; m = _members[m].Next) matches[count++] = _members[m].Sequence;
            for (int j = count - 1; j > 0; j--) { int k = _rng.Int(j + 1); (matches[j], matches[k]) = (matches[k], matches[j]); }
            int removed = 0;
            for (int j = 0; j < count && removed < count - 1; j++) if (PruneSegment(matches[j])) removed++;
            if (removed != 0) return true;
            for (int j = 0; j < count; j++) if (BreakSegment(matches[j])) return true;
        }
        return false;
    }
    private static int Length(UInt128 sequence)
    {
        ulong hi = (ulong)(sequence >> 64);
        int bit = hi != 0 ? 127 - BitOperations.LeadingZeroCount(hi) : 63 - BitOperations.LeadingZeroCount((ulong)sequence);
        return bit / 7;
    }
    private static int At(UInt128 sequence, int i) => (int)((sequence >> (7 * i)) & 127);
    private bool PruneSegment(UInt128 sequence)
    {
        int length = Length(sequence); bool result = false;
        for (int i = 0; i < length - 1; i++)
        {
            int node = At(sequence, i); if (!Active(node)) return true;
            if (_degree[node] > 1 || Pop(_parents[node]) > 1) continue;
            UInt128 parents = _parents[node]; bool protectedNode = false;
            while (parents != 0)
            {
                int p = Take(ref parents); if (_degree[p] == 1 && p != Start && Active(p)) { protectedNode = true; break; }
            }
            if (protectedNode) continue;
            for (int j = i; j < length; j++)
            {
                int tail = At(sequence, j); if (_degree[tail] > 1 && Pop(_parents[tail]) == 1) { protectedNode = true; break; }
            }
            if (protectedNode) continue;
            if (Pop(_parents[At(sequence, length - 1)]) == 1) return false;
            for (int j = 0; j < _degree[node]; j++)
            {
                int child = _children[node * 7 + j]; bool contained = false;
                for (int k = 0; k < length; k++) if (At(sequence, k) == child) { contained = true; break; }
                if (!contained && Pop(_parents[child]) == 1) { protectedNode = true; break; }
            }
            if (!protectedNode) { Delete(node); result = true; }
        }
        return result;
    }
    private void Delete(int node)
    {
        DeletePoints++; _grid[_col[node] * 17 + node / 7] = -1;
        if (node != Start && node != _boss) _active &= ~Bit(node);
        while (_degree[node] > 0) RemoveEdge(node, _children[node * 7]);
        UInt128 parents = _parents[node]; while (parents != 0) RemoveEdge(Take(ref parents), node);
    }
    private bool BreakSegment(UInt128 sequence)
    {
        BreakAttempts++; bool result = false; int length = Length(sequence);
        for (int i = 0; i < length - 1; i++)
        {
            int node = At(sequence, i), child = At(sequence, i + 1);
            if (_degree[node] >= 2 && Pop(_parents[child]) != 1) { RemoveEdge(node, child); BrokenEdges++; result = true; }
        }
        if (result) SuccessfulBreaks++; return result;
    }

    private void PostProcess()
    {
        bool leftEmpty = EmptyColumn(0) && EmptyColumn(1), rightEmpty = EmptyColumn(6) && EmptyColumn(5);
        int shift = leftEmpty && !rightEmpty ? -1 : !leftEmpty && rightEmpty ? 1 : 0;
        if (shift != 0)
            for (int row = 0; row <= _height; row++)
                for (int k = 0; k < 7; k++)
                {
                    int col = shift > 0 ? 6 - k : k, node = Cell(col, row);
                    _grid[col * 17 + row] = -1;
                    if (col + shift is >= 0 and < 7)
                    {
                        _grid[(col + shift) * 17 + row] = node; if (node >= 0) _col[node] = (byte)(col + shift);
                    }
                }
        Span<int> rowNodes = stackalloc int[7];
        for (int row = 0; row <= _height; row++)
        {
            int count = 0; for (int col = 0; col < 7; col++) if (Cell(col, row) >= 0) rowNodes[count++] = Cell(col, row);
            bool moved;
            do
            {
                moved = false;
                for (int i = 0; i < count; i++)
                {
                    int node = rowNodes[i], old = _col[node], best = old, gap = Gap(old, rowNodes[..count], node), allowed = 127;
                    UInt128 parents = _parents[node]; while (parents != 0) allowed &= Neighbors(_col[Take(ref parents)]);
                    for (int j = 0; j < _degree[node]; j++) allowed &= Neighbors(_col[_children[node * 7 + j]]);
                    for (int col = 0; col < 7; col++)
                        if ((allowed & (1 << col)) != 0 && col != old && Cell(col, row) < 0)
                        {
                            int nextGap = Gap(col, rowNodes[..count], node); if (nextGap > gap) { gap = nextGap; best = col; }
                        }
                    if (best != old) { Move(node, best); moved = true; }
                }
            } while (moved);
        }
        for (int row = 0; row <= _height; row++)
        for (int col = 0; col < 7; col++)
        {
            int node = Cell(col, row); if (node < 0 || Pop(_parents[node]) != 1 || _degree[node] != 1) continue;
            UInt128 parents = _parents[node]; int p = Take(ref parents), child = _children[node * 7];
            bool left = _col[node] < _col[p] && _col[node] < _col[child], right = _col[node] > _col[p] && _col[node] > _col[child];
            if (left && col < 6 && Cell(col + 1, row) < 0) Move(node, col + 1);
            if (right && col > 0 && Cell(col - 1, row) < 0) Move(node, col - 1);
        }
    }
    private void Move(int node, int col) { int row = node / 7; _grid[_col[node] * 17 + row] = -1; _grid[col * 17 + row] = node; _col[node] = (byte)col; }
    private bool EmptyColumn(int col) { for (int row = 0; row <= _height; row++) if (Cell(col, row) >= 0) return false; return true; }
    private static int Neighbors(int col) => ((1 << col) | (col > 0 ? 1 << (col - 1) : 0) | (col < 6 ? 1 << (col + 1) : 0));
    private int Gap(int col, ReadOnlySpan<int> nodes, int current)
    {
        int gap = int.MaxValue; foreach (int n in nodes) if (n != current) gap = Math.Min(gap, Math.Abs(col - _col[n])); return gap;
    }

}
