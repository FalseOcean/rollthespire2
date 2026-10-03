using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalReachability
{
    // Owned by this frozen board, reveal budget, curse/reroll policy and table.
    // Stronger selections inherit certified contradictions, never timeouts.
    private sealed record Conflict(ImmutableArray<CrystalRewardOption> Goals,string Source);
    private readonly List<Conflict> _conflicts=[];
    private Conflict[] _readableConflicts=[];
    internal int ConflictCount { get { lock(_sync) return _conflicts.Count; } }
    // UI can retire a known contradiction on selection immediately, without
    // waiting for a traversal lock or reward/gold reprojection to finish.
    internal bool IsCertifiedConflict(ImmutableArray<CrystalRewardOption> goals)=>
        Volatile.Read(ref _readableConflicts).Any(c=>c.Goals.All(goals.Contains));
    private bool IsRootCardConflict(ImmutableArray<CrystalRewardOption> goals)=>
        Volatile.Read(ref _readableConflicts).Any(c=>c.Source=="root-card-slots" && c.Goals.All(goals.Contains));
    private bool ConflictsWith(ImmutableArray<CrystalRewardOption> goals)=>
        _conflicts.Any(c=>c.Goals.All(goals.Contains));
    private void RememberConflict(ImmutableArray<CrystalRewardOption> goals,string source)
    {
        goals=goals.Distinct().ToImmutableArray();
        if(goals.IsEmpty || ConflictsWith(goals)) return;
        _conflicts.RemoveAll(c=>goals.All(c.Goals.Contains));
        if(_conflicts.Count>=4096) return;
        _conflicts.Add(new(goals,source));
        Volatile.Write(ref _readableConflicts,_conflicts.ToArray());
    }
    private IEnumerable<CrystalRewardOption> ConflictingCandidates(ImmutableArray<CrystalRewardOption> selected)=>
        _table!.Candidates.Where(c=>!selected.Contains(c) && ConflictsWith(selected.Add(c)));
}
