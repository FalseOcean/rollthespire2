using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
namespace RolltheSpire2.Ui.InRunOperations;
internal sealed record OperationPotionDemand(bool IncludeBasicResult=true,bool IncludeOrder=true,bool IncludeInventory=true)
{ internal static readonly OperationPotionDemand All=new(); }
internal static class PotionOperationPreview
{
    private static OperationPotionForecastScope Scope(OperationPotionDemand? value)
    { value ??= OperationPotionDemand.All;return new(value.IncludeBasicResult,value.IncludeOrder,value.IncludeInventory); }
    internal static OperationPreview Build(PredictorContext context,ModelKey potion,CancellationToken token=default,
        OperationPotionDemand? demand=null) => OperationForecastProjection.Project(PotionOperationForecast.Build(context,potion,token,Scope(demand)));
    internal static OperationPreview Build(PredictorOperationSnapshot snapshot,ModelKey potion,int slot,CancellationToken token=default,
        OperationPotionDemand? demand=null) => OperationForecastProjection.Project(PotionOperationForecast.Build(snapshot,potion,slot,token,Scope(demand)));
    internal static OperationPreview Build(PredictorOperationPotionObservation observation,CancellationToken token=default,
        OperationPotionDemand? demand=null) => OperationForecastProjection.Project(PotionOperationForecast.Build(observation,token,Scope(demand)));
}
