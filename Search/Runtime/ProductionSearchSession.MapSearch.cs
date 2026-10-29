using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Runtime;

public sealed partial class ProductionSearchSession
{
    private readonly string _mapAttemptId = Guid.NewGuid().ToString("N");
    private MapSearchProvenance? CaptureMapSearch(ulong start, ulong end)
    {
        var query = _plan.CompiledSearch.NormalizedQuery;
        if (!_plan.MapLossy.Enabled && query.StandardMaps.Count == 0 && query.StandardMapRoutes.Count == 0) return null;
        var family = ExecutionPlan.ExecutionOrderUnits.OfType<StandardMapFamily>().FirstOrDefault();
        var description = family?.LossyDescription;
        return new(_plan.MapLossy, description?.Applied == true, description?.Backend ?? "FullFinal",
            description?.Strategy ?? "FullFinal", description?.Reason ?? "NoMapPrefilterSelected",
            description?.Scope ?? "", description?.FullFinalRemainders ?? [], start, end, _plan.SnapshotFingerprint + ":" + _mapAttemptId)
        {
            ObservedProposalCalls = family?.ActualProposalCalls ?? 0,
            ObservedProposalInputs = family?.ActualProposalInputs ?? 0
        };
    }
}
