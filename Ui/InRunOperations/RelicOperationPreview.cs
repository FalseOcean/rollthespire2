using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunOperations;

internal static class RelicOperationPreview
{
    internal static OperationPreview Build(PredictorOperationSnapshot snapshot, long actualOfferId,
        CancellationToken token = default, bool includeContinuation = true) =>
        OperationForecastProjection.Project(RelicOperationForecast.Build(snapshot, actualOfferId, token, includeContinuation));

    internal static OperationPreview Build(PredictorOperationSnapshot snapshot, ImmutableArray<PredictorInput> rootInputs,
        ModelKey source, CancellationToken token = default, bool includeContinuation = true) =>
        OperationForecastProjection.Project(RelicOperationForecast.Build(snapshot, rootInputs, source, token, includeContinuation));

    internal static OperationPreview BuildTransformTargets(PredictorOperationSnapshot snapshot, ModelKey source,
        ImmutableArray<long> orderedPrefix, CancellationToken token = default) =>
        OperationForecastProjection.Project(RelicOperationForecast.BuildTransformTargets(snapshot, source, orderedPrefix, token));

    internal static ImmutableArray<OperationPreviewSection> ObserveResult(PredictorContext context,
        PredictorState before, PredictorOperationResult? previous, PredictorOperationResult after, ModelKey source) =>
        OperationForecastProjection.Project(RelicOperationForecast.ObserveResult(context, before, previous, after, source));
}
