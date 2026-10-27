using System.Collections.Immutable;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class PredictorOperationService
{
    // Safe for a worker: this boundary holds detached values only. Publication
    // still requires PredictorOperationCapture.IsCurrent on the main thread.
    internal static PredictorOperationResult Predict(PredictorOperationSnapshot snapshot,
        ImmutableArray<PredictorInput> inputs, CancellationToken token = default)
    {
        try { return PredictorOperation.Preview(snapshot, inputs, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            RuntimeLog.Fault("operationPredictionFailed=true;stage=detached-settlement;fingerprint=" +
                snapshot.Fingerprint + ";node=" + snapshot.State.Position.NodeId + ";inputs=" +
                (inputs.IsDefault ? "missing" : string.Join(',', inputs.Select(i => i?.GetType().Name ?? "null"))), exception);
            throw;
        }
    }
}
