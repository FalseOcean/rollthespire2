using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class MapWorkspace
{
    private PersistedMapRouteWitness? _pendingWitness;
    private string _pendingWitnessSeed = "";
    private bool _isWitnessPreview;
    private bool _witnessMapMode;
    private string _witnessIssue = "";
    internal bool IsWitnessPreview => _isWitnessPreview;

    internal void PreviewResultWitness(PersistedMapRouteWitness witness, string seed)
    {
        if (!_witnessMapMode) throw new InvalidOperationException("MapWitness.StandardMapViewRequired");
        _pendingWitness = witness;
        _pendingWitnessSeed = seed;
        if (_routeMap is not null) ApplyResultWitness();
    }

    private void ClearResultWitness()
    {
        _pendingWitness = null;
        _pendingWitnessSeed = "";
        _isWitnessPreview = false;
        _witnessIssue = "";
    }

    private void ApplyResultWitness()
    {
        if (_pendingWitness is not { } witness || _routeMap is null) return;
        var seed = _pendingWitnessSeed;
        _pendingWitness = null;
        try
        {
            var path = MapRouteWitnessProjection.Read(witness, seed, _routeMap);
            _suppressUnknownOverlay = true;
            _optimalRoutes = null; _optimalMetric = null;
            _preview = path;
            _isWitnessPreview = true;
            _witnessIssue = "";
        }
        catch (Exception exception)
        {
            _isWitnessPreview = false;
            _preview = null;
            _witnessIssue = exception.Message;
            RuntimeIssueLedger.Current.Record(exception, "MapWitnessPreviewFailed", nameof(MapWorkspace),
                "ResultMapWitness", "issues.capture_failed", correlationId: $"seed={seed};act={witness.Act}");
        }
        PresentRoute(true);
    }
}
