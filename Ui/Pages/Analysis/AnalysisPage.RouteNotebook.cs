using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private readonly MapRouteNotebook _notebook = MapRouteNotebook.Local();
    private RouteBook? _routeBook;
    private RouteBookKey? _liveRouteKey;
    private bool _hasSpoilsCard;
    private int[] _playedUnknownCounts = [];
    private string? _liveRouteStamp;

    private RouteBook LoadRouteBook()
    {
        var r = LastRequest!;
        var key = _liveRouteKey ?? new RouteBookKey(LastFacts!.CanonicalSeed, r.Ascension, r.PlayersCount, "");
        if (_routeBook?.Key != key) _routeBook = _notebook.Read(key);
        return _routeBook;
    }

    private bool SpoilsPreview => LoadRouteBook().Spoils ?? _hasSpoilsCard;
    private string RouteVariant(int act) => SeenMap(act) is not null ? $"{act}/played" :
        act == 2 && SpoilsPreview ? "2/spoils" : act.ToString();

    private SeenAct? SeenMap(int act) => _liveRouteKey is null ? null : LoadRouteBook().Seen.GetValueOrDefault(act);

    private void StoreDrawnRoute(int act)
    {
        var book = LoadRouteBook();
        var variant = RouteVariant(act);
        var route = _overviewRoutes.GetValueOrDefault(act);
        var coords = route?.Coordinates.ToArray();
        string? shape = route is null ? null : ShapeOf(route.Map);
        void Save(RouteBook b)
        {
            if (coords is null && SeenMap(act) is not null) b.Drawn[variant] = [];
            else if (coords is null) b.Drawn.Remove(variant);
            else b.Drawn[variant] = coords;
            if (shape is null) b.MapShapes.Remove(variant);
            else b.MapShapes[variant] = shape;
        }
        Save(book);
        _notebook.Edit(book.Key, Save);
    }

    private void RestoreDrawnRoute(MapPrediction map)
    {
        int act = map.ActIndex + 1;
        if (_overviewRoutes.ContainsKey(act)) return;
        var book = LoadRouteBook();
        var drawn = book.Drawn;
        var saved = drawn.GetValueOrDefault(RouteVariant(act));
        if (saved is null && SeenMap(act) is not null)
        {
            var shape = ShapeOf(map);
            foreach (var variant in act == 2 ? new[] { "2", "2/spoils" } : new[] { act.ToString() })
                if (book.MapShapes.GetValueOrDefault(variant) == shape) { saved = drawn.GetValueOrDefault(variant); break; }
        }
        var walked = SeenMap(act)?.Walked ?? [];
        if (saved is not null && new MapRouteSet(map).AllowsObservedPrefix(saved, walked))
            _overviewRoutes[act] = new(map, saved, walked);
    }

    private static string ShapeOf(MapPrediction map)
    {
        var ordered = map.Points.OrderBy(p => p.Position.Row).ThenBy(p => p.Position.Column).Select(p =>
            new { p.Position, p.Type, Children = p.Children.OrderBy(c => c.Row).ThenBy(c => c.Column) });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(ordered)));
    }

    private void SetSpoilsPreview(bool enabled)
    {
        var book = LoadRouteBook();
        book.Spoils = enabled;
        _notebook.Edit(book.Key, b => b.Spoils = enabled);
        InvalidateRouteMap(2);
        RenderWorkbench();
    }

    private void InvalidateRouteMap(int act)
    {
        _overviewMaps.Remove(act);
        _overviewRoutes.Remove(act);
        _overviewRouteMetrics.Remove(act);
        if (act is >= 1 and <= 3) _unknownMaximums[act - 1] = null;
    }

    internal void SyncLiveRoute(IRunState run)
    {
        if (_liveRunPremises is null || LastFacts is null || LastRequest?.OriginalSeed != run.Rng.StringSeed ||
            run.Map is null or MegaCrit.Sts2.Core.Map.NullActMap) return;
        var key = RouteBookKey.ForRun(run);
        var stamp = $"{run.CurrentActIndex}/{run.VisitedMapCoords.Count}/{run.CurrentMapCoord}/{run.MapPointHistory.ElementAtOrDefault(run.CurrentActIndex)?.Count}/{run.CurrentMapPointHistoryEntry?.Rooms.Count}";
        bool hasSpoils = run.Players.SelectMany(p => p.Deck.Cards).OfType<MegaCrit.Sts2.Core.Models.Cards.SpoilsMap>().Any(c => c.SpoilsActIndex == 1);
        if (key == _liveRouteKey && stamp == _liveRouteStamp && _hasSpoilsCard == hasSpoils &&
            ReferenceEquals(_observedNativeMap, run.Map)) return;
        bool changedBinding = key != _liveRouteKey;
        if ((changedBinding || run.CurrentActIndex + 1 != _lastSeenAct && _wbPage == _lastSeenAct - 1) &&
            run.CurrentActIndex is > 0 and < 3)
            _wbPage = run.CurrentActIndex;
        if (changedBinding || run.CurrentActIndex + 1 != _lastSeenAct)
        {
            _routeBook = _notebook.Read(key);
            for (int i = 1; i <= 3; i++) InvalidateRouteMap(i);
        }
        _lastSeenAct = run.CurrentActIndex + 1;
        _observedNativeMap = run.Map;
        _liveRouteKey = key;
        _liveRouteStamp = stamp;
        var book = LoadRouteBook();
        int act = run.CurrentActIndex + 1;
        if (_hasSpoilsCard != hasSpoils) InvalidateRouteMap(2);
        _hasSpoilsCard = hasSpoils;
        var seen = SeenAct.Capture(run);
        book.Seen[act] = seen;
        _notebook.Edit(key, b => b.Seen[act] = seen);
        _playedUnknownCounts = run.MapPointHistory.Take(act - 1).Select(h =>
            h.Count(p => p.MapPointType == MegaCrit.Sts2.Core.Map.MapPointType.Unknown)).ToArray();
        InvalidateRouteMap(act);
        RenderWorkbench();
    }
    private MegaCrit.Sts2.Core.Map.ActMap? _observedNativeMap;
    private int _lastSeenAct;
}
