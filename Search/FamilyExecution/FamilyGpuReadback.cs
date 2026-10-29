using System.Runtime.ExceptionServices;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// Queue before Submit; collect only after the owner's Sync. Callback bodies
// retain this invocation's bytes only and never reenter the device or validate.
internal static class FamilyGpuReadback
{
    internal const string Revision = ".QueuedReadback.20261027.v1";
    internal const string HostTransferRevision = ".HostTransfer.DirectPackingAndReadback.20261027.v1";
    // Current public T Invoke includes context dispatch, queued 32-byte headers,
    // synchronous compact payload readback, uint-to-ABI1 widening, sorting and
    // subset validation plus family bookkeeping. First-call setup/parity are
    // excluded by the complete-Invoke collector's warmup boundary. Physical
    // revision still separates numerical variants, prefix and FullTarget bodies.
    internal const string TransformationPublicBoundaryRevision =
        ".TransformationPublicInvoke.QueuedHeader32SyncPayloadWidenSortValidate.20261027.v1";

    internal static string ObservationRevision(IFamilyInvocation family, FamilyPhysicalQuoteRequest geometry) =>
        !family.ConditionPerformance.UsesGpu ? "" : HostTransferRevision +
        (family switch {
            NeowFamily when !geometry.PrivateOutput => NeowFamilyGpuExecutor.PublicBoundaryRevision,
            TransformationAggregateFamily when !geometry.PrivateInput && !geometry.PrivateOutput => TransformationPublicBoundaryRevision,
            _ => ""
        });

    internal static FamilyPhysicalQuote? Reference(IFamilyInvocation family, FamilyPhysicalQuote? quote)
    {
        if (quote is null || !family.ConditionPerformance.UsesGpu) return quote;
        // Legacy shader coefficients remain useful estimates after the equivalent
        // rotation rewrite. Exact canonical keys retain their measured authority;
        // inherited numerical references do not become measurements of this body.
        if (!FamilyCanonicalMeasuredCost.IsCanonicalQuote(quote) &&
            !quote.Evidence.Contains("CurrentGpuRotationRevision=", StringComparison.Ordinal))
            quote = quote with
            {
                LocalCostSource = quote.LocalCostSource == "LocalMeasurement" ? "HistoricalLocalReference" : quote.LocalCostSource,
                Evidence = quote.Evidence + ";PriceQuality=Estimate;HistoricalGpuCoefficientsRemainCoarseReference;" +
                    "NotCurrentBodyMeasurement;CurrentGpuRotationRevision=" + GpuRngRotation.Revision
            };
        // Complete public N and T measurements each need their own explicit
        // matching boundary identity. Neither family borrows the other's token.
        // Historical canonical calls and numerical/private quotes keep their
        // original scope and disclaimer.
        (string boundaryToken, string measuredMarker) = family switch {
            NeowFamily => ("MeasuredNeowPublicBoundaryRevision=" + NeowFamilyGpuExecutor.PublicBoundaryRevision,
                "CurrentNeowPublicInvokeHostTransportMeasured"),
            TransformationAggregateFamily => ("MeasuredTransformationPublicBoundaryRevision=" + TransformationPublicBoundaryRevision,
                "CurrentTransformationPublicInvokeHostTransportMeasured"),
            _ => ("", "")
        };
        if (boundaryToken.Length > 0 && quote.FamilyId == family.FamilyId &&
            quote.PublicTransportClass == "CompleteCanonicalAbi1")
        {
            string[] tokens = quote.Evidence.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Contains("MeasuredHostTransferRevision=" + HostTransferRevision, StringComparer.Ordinal) &&
                tokens.Contains(boundaryToken, StringComparer.Ordinal))
                return tokens.Contains(measuredMarker, StringComparer.Ordinal) ? quote :
                    quote with { Evidence = quote.Evidence + ";" + measuredMarker + ";RootGroupSharedWaitNotLocalQuote" };
        }
        if (quote.Evidence.Contains("CurrentHostTransportUnmeasured", StringComparison.Ordinal)) return quote;
        return quote with { Evidence = quote.Evidence + ";RetainedHistoricalCallEstimate;CurrentHostTransportUnmeasured;HostTransferRevision=" +
            HostTransferRevision + ";RootGroupSharedWaitNotLocalQuote" };
    }
    internal static Pending Queue(RenderingDevice rd, Rid buffer, uint offset, uint bytes, string scope) =>
        new(rd, buffer, offset, bytes, scope);

    internal sealed class Pending
    {
        private readonly int _owner = System.Environment.CurrentManagedThreadId;
        private readonly uint _length;
        private readonly string _scope;
        private readonly Callable _callback;
        private readonly Error _error;
        private readonly Exception? _queueFailure;
        private byte[]? _bytes;
        private int _callbacks, _callbackThread;

        internal Pending(RenderingDevice rd, Rid buffer, uint offset, uint bytes, string scope)
        {
            _length = bytes;
            _scope = scope;
            try
            {
                _callback = Callable.From<byte[]>(data =>
                {
                    _bytes = data;
                    _callbackThread = System.Environment.CurrentManagedThreadId;
                    Interlocked.Increment(ref _callbacks);
                });
                _error = rd.BufferGetDataAsync(buffer, _callback, offset, bytes);
            }
            catch (Exception exception)
            {
                // The caller must still submit/drain recorded work. Preserve
                // this original failure until the completed group is collected.
                _queueFailure = exception;
            }
        }

        internal byte[] GetData()
        {
            GC.KeepAlive(_callback);
            if (_queueFailure is not null) ExceptionDispatchInfo.Capture(_queueFailure).Throw();
            if (_error != Error.Ok) throw new InvalidOperationException(_scope + ".ReadbackSchedule:" + _error);
            if (System.Environment.CurrentManagedThreadId != _owner || Volatile.Read(ref _callbacks) != 1 ||
                _callbackThread != _owner || _bytes is null || _bytes.Length != _length)
                throw new InvalidDataException($"{_scope}.ReadbackCompletion:callbacks={_callbacks};owner={_owner};callbackThread={_callbackThread};bytes={_bytes?.Length};expected={_length}");
            return _bytes;
        }
    }
}
