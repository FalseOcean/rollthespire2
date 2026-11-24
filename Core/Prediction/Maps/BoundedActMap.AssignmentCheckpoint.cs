namespace RolltheSpire2.Core.Prediction.Maps;

internal sealed partial class BoundedActMap
{
    private uint[] _assignmentProgramScratch = [];

    // Private lossy checkpoint only. Never marks or publishes a completed map.
    internal void ReplayAssignmentRound1Root(ulong rootHash, CancellationToken cancellationToken = default)
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
        Assignment(stopAfterFirstRound: true);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal bool MatchAssignmentProgram(MapDrawingProgram program, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        token.ThrowIfCancellationRequested();
        if (program.AlwaysReject) return false;
        int size = checked(Slots * program.WordCount);
        if (_assignmentProgramScratch.Length < size) _assignmentProgramScratch = new uint[size];
        // Assignment Round1 intentionally leaves Monster slots unassigned. Only
        // this proposal match projects them; _type and RNG state stay untouched.
        return program.Match(_type, _degree, _children, _modifiable, _boss,
            _assignmentProgramScratch, projectUnassignedToMonster: true, token);
    }
}
