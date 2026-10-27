using System.Collections.Immutable;
using System.Globalization;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

// A map-node identity is distinct from synthetic entry/opening node IDs. The
// floor disambiguates factual visits from future intent at the same coordinate.
internal readonly record struct PredictorMapNodeIdentity(int Act, MapPosition Point, int Floor)
{
    internal static bool TryParse(string nodeId, out PredictorMapNodeIdentity identity)
    {
        identity = default;
        var parts = nodeId.Split(':');
        if (parts.Length != 5 || parts[0] != "map" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int act) || act is < 1 or > 3 ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int row) ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int column) ||
            !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int floor) || floor < 1)
            return false;
        identity = new(act, new(column, row), floor);
        return true;
    }
}

internal sealed partial class PredictorRun
{
    // Only the immediately previous redraw can be undone. Obsolete plans live
    // here temporarily, never in the current route's persistence-ready plans.
    private sealed record RouteRedrawUndo(int Act, ImmutableArray<PredictorRoute> Routes,
        ImmutableArray<PredictorPlannedNode> Plans);
    private RouteRedrawUndo? _routeRedrawUndo;
    internal bool CanUndoRouteRedraw => _routeRedrawUndo != null;

    internal bool UndoRouteRedraw()
    {
        RequireRouteSimulation();
        if (_routeRedrawUndo is not { } undo) return false;
        // Validation precedes replacement even if a caller bypassed a normal
        // mutation boundary. Undo must never restore an illegal commitment.
        var route = undo.Routes.Single(r => r.Act == undo.Act);
        if (!IsLegalRoute(RouteMap(route.Act), route.Points,
                Working.Maps.FirstOrDefault(m => m.Act == route.Act)?.Visited ?? []))
            return false;
        Routes = undo.Routes;
        PlannedNodes = undo.Plans;
        _routeRedrawUndo = null;
        return true;
    }

    // Call only after a successful meaningful operation, never for observation,
    // rejected inputs, or a setter that leaves its value unchanged.
    internal void CompleteRouteRedrawUndo() => _routeRedrawUndo = null;

    private static bool SameRoutes(ImmutableArray<PredictorRoute> left, ImmutableArray<PredictorRoute> right) =>
        left.Length == right.Length && left.All(route => right.FirstOrDefault(r => r.Act == route.Act) is { } other &&
            route.Points.SequenceEqual(other.Points));

    private bool IsFactualMapNode(PredictorMapNodeIdentity node) => Working.Visits.Any(visit =>
        visit.Act == node.Act && visit.Point == node.Point && visit.TotalFloor == node.Floor);

    private ImmutableArray<PredictorPlannedNode> PlansForRoutes(ImmutableArray<PredictorPlannedNode> plans) =>
        plans.Where(plan => !PredictorMapNodeIdentity.TryParse(plan.NodeId, out var node) ||
            IsFactualMapNode(node) || Routes.FirstOrDefault(r => r.Act == node.Act) is not { } route ||
            route.Points.Contains(node.Point)).ToImmutableArray();

    // Map replacement is a consequence of an accepted operation (Golden
    // Compass), not an undoable user redraw. Visited facts remain authoritative.
    internal void DiscardRouteForMapChange(int act)
    {
        CompleteRouteRedrawUndo();
        Routes = Routes.RemoveAll(route => route.Act == act);
        PlannedNodes = PlannedNodes.Where(plan => !PredictorMapNodeIdentity.TryParse(plan.NodeId, out var node) ||
            node.Act != act || IsFactualMapNode(node)).ToImmutableArray();
    }
}
