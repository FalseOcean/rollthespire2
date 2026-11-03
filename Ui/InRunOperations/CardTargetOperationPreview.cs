using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation;
namespace RolltheSpire2.Ui.InRunOperations;
internal static class CardTargetOperationPreview
{
    internal static OperationPreview Build(PredictorOperationSnapshot snapshot,long hoveredCardId,
        CancellationToken token=default,bool includeContinuation=true,bool includeCombat=true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Scene is PredictorOperationEventScene { AwaitingCards:true } scene &&
            !OperationPreviewEligibility.EventMayDisplay(scene.Event.Entry, scene.Branch ?? "",
                scene.TinkerType != null, scene.TabletCount,
                scene.Options.Any(o => o.Key == scene.Branch && o.OfferedRelic != null)))
            return new(scene.Event, [], [], false, [], []);
        return OperationForecastProjection.Project(CardTargetOperationForecast.Build(snapshot,hoveredCardId,token,includeContinuation,includeCombat));
    }
}
