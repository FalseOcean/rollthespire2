using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility.ModPrediction;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationHoverController
{
    private ModOperationSnapshot? _modSnapshot;

    private ModOperationSnapshot CaptureCurrentModOperation() =>
        _request!.Mod!.Capture(_owner!, _run!);

    private bool CaptureModOperation()
    {
        _snapshot = null; _potionObservation = null; _directTransformSelector = null;
        try
        {
            _modSnapshot = CaptureCurrentModOperation();
            _stamp = _modSnapshot.Fingerprint;
            return true;
        }
        catch (Exception error)
        {
            _modSnapshot = null;
            _stamp = ModCaptureFailureStamp(error);
            RuntimeLog.Fault("operationPredictionFailed=true;stage=mod-operation-capture;feature=" +
                _request!.Mod!.Setting.Id + ";source=" + _request.Source, error);
            PresentPreview(new(_request.Source, [], [], false, [], [])
            { Faults = [new("ModOperationCapture", [], error)] });
            PositionView();
            _poll = .5;
            return false;
        }
    }

    private string ModOperationObservationStamp()
    {
        // Publication and polling use the adapter's full input fingerprint,
        // including recovery from a previously failed capture.
        try { return CaptureCurrentModOperation().Fingerprint; }
        catch (Exception error) { return ModCaptureFailureStamp(error); }
    }

    private static string ModCaptureFailureStamp(Exception error) =>
        "ModCaptureFailure:" + error.GetType().FullName + ":" + error.Message +
        (error.InnerException is { } cause ? ":" + ModCaptureFailureStamp(cause) : "");
}
