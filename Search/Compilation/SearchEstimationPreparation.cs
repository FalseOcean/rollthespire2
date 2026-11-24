using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
namespace RolltheSpire2.Search.Compilation;

// Optional derived analysis receives only its resolved inputs. It cannot change
// the prepared obligations, selected execution, or returned model disposition.
internal static class SearchEstimationPreparation
{
    internal static SearchEtaInput ForRun(PreparedSearch request,FamilyExecutionPlan selected) => new(
        JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request.Model)),
        FamilyExactTimingDomains.Resolve(request.Model),FamilyExactTimingDomains.ImplementationRevision(request.Model),
        request.ScanCount,request.TargetMatchCount,request.WorkerCount,request.RunOptions.SkipExactValidation,
        FamilyExecutionCoordinator.ResolveExecutionWindowSize(request,selected.OrderedFamilies),request.SnapshotFingerprint);
}
