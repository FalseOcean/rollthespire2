using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalReachability
{
    // Safe before worker-side reprojection on a selection change. Only already
    // prepared immutable slot supports and published all-orders conflicts are
    // read; an unprepared board/target supplies no new negative fact.
    internal ImmutableArray<CrystalRewardOption> RootCardExclusions(ImmutableArray<CrystalRewardOption> selected,
        ImmutableArray<CrystalRewardOption> candidates)
    {
        var bound=Volatile.Read(ref _offsetBound);
        var excluded=ImmutableArray.CreateBuilder<CrystalRewardOption>();
        foreach(var candidate in candidates)
        {
            if(selected.Contains(candidate)) continue;
            var goals=selected.Add(candidate);
            if(IsCertifiedConflict(goals) || rootCardSlots && bound?.PreparedRootCardSlotsConflict(goals)==true)
                excluded.Add(candidate);
        }
        return excluded.ToImmutable();
    }
}
