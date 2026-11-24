using RolltheSpire2.Core.PredictorRuntime;
namespace RolltheSpire2.Ui.InRunOperations;
internal sealed record OperationSurfaceDemand(bool IncludeOriginEffect = true, bool IncludeBasicRestock = true,
    bool IncludeRewardAlternative = true, bool IncludeCardTransforms = true,
    bool IncludeRewardSequence = true, bool IncludeSacrificeProgress = true,
    bool IncludeConditionalRestock = true, bool IncludeRestockWhetstone = true,
    bool IncludeObtainContinuation = true)
{
    internal static readonly OperationSurfaceDemand All = new();
    internal bool AnyRestock => IncludeBasicRestock || IncludeConditionalRestock || IncludeRestockWhetstone;
}

internal static class SurfaceOperationPreview
{
    private static OperationSurfaceForecastScope Scope(OperationSurfaceDemand? value)
    {
        value ??= OperationSurfaceDemand.All;
        return new(value.IncludeOriginEffect,value.IncludeBasicRestock,value.IncludeRewardAlternative,
            value.IncludeCardTransforms,value.IncludeRewardSequence,value.IncludeSacrificeProgress,
            value.IncludeConditionalRestock,value.IncludeRestockWhetstone,value.IncludeObtainContinuation);
    }
    internal static OperationPreview BuildShop(PredictorOperationSnapshot snapshot,long entryId,
        CancellationToken token=default,OperationSurfaceDemand? demand=null) =>
        OperationForecastProjection.Project(SurfaceOperationForecast.BuildShop(snapshot,entryId,token,Scope(demand)));
    internal static OperationPreview BuildRest(PredictorOperationSnapshot snapshot,PredictorRestAction action,
        CancellationToken token=default,OperationSurfaceDemand? demand=null) =>
        OperationForecastProjection.Project(SurfaceOperationForecast.BuildRest(snapshot,action,token,Scope(demand)));
    internal static OperationPreview BuildRewardAlternative(PredictorOperationSnapshot snapshot,long rewardId,
        string optionId,long? wingId,CancellationToken token=default,OperationSurfaceDemand? demand=null) =>
        OperationForecastProjection.Project(SurfaceOperationForecast.BuildRewardAlternative(snapshot,rewardId,optionId,wingId,token,Scope(demand)));
}
