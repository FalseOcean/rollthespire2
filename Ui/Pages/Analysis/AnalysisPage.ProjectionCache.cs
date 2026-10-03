using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    // Scoped to one seed/context, including captured authority and player slot.
    // Navigation and Ancient premise revisions share in-flight independent work;
    // only a full context replacement/clear/disposal cancels it.
    private readonly Dictionary<int, Task<MapPrediction>> _overviewMaps = [];
    private readonly Dictionary<int, MapRouteCommitment> _overviewRoutes = [];
    private readonly Dictionary<int, MapPointType> _overviewRouteMetrics = [];
    private CancellationTokenSource? _overviewLifetime;
    private Beta111ShopColorlessProjection? _merchantProjection;
    private Beta111EventResultProjection? _eventProjection;
    private bool _merchantProjectionReady, _eventProjectionReady;

    private void ClearWorkbenchProjections()
    {
        _overviewLifetime?.Cancel();
        _overviewLifetime?.Dispose();
        _overviewLifetime = null;
        _overviewMaps.Clear();
        _overviewRoutes.Clear();
        _overviewRouteMetrics.Clear();
        _ancientPremiseVariants.Clear();
        _merchantProjection = null;
        _eventProjection = null;
        _eventTransformUnlocks = null;
        _eventTransformResults.Clear();
        _merchantProjectionReady = _eventProjectionReady = false;
    }

    private Task<MapPrediction> GetOverviewMap(int act, ModelKey actKey, bool secondBoss, CancellationToken cancellation)
    {
        if (_overviewMaps.TryGetValue(act, out var cached) && !cached.IsFaulted && !cached.IsCanceled)
            return cached;

        var request = LastRequest!;
        string seed = LastDocument!.CanonicalSeed;
        var task = Task.Run(() => Beta111MapPredictor.Predict(seed, request.Authority.ProfileId,
            actKey, act - 1, request.Ascension, request.PlayersCount, secondBoss, cancellation), cancellation);
        _overviewMaps[act] = task;
        return task;
    }
}
