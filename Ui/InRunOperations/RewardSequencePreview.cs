using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
namespace RolltheSpire2.Ui.InRunOperations;
internal static class RewardSequencePreview
{
    internal static OperationPreview BuildReroll(PredictorOperationSnapshot snapshot,
        PredictorOperationResult before,PredictorReward current,PredictorOperationResult firstResult,
        ModelKey source,CancellationToken token=default) =>
        OperationForecastProjection.Project(RewardSequenceForecast.BuildReroll(snapshot,before,current,firstResult,source,token));
    internal static OperationPreview BuildSacrifice(PredictorOperationSnapshot snapshot,
        PredictorOperationResult before,PredictorReward current,PredictorOperationResult firstResult,
        PredictorRelic wing,CancellationToken token=default) =>
        OperationForecastProjection.Project(RewardSequenceForecast.BuildSacrifice(snapshot,before,current,firstResult,wing,token));
}
