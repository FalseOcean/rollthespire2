using System.Collections.Immutable;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapPredictionSession
{
    // A hover changes only a private copy of the authored plans. In particular,
    // it neither observes the native room again nor invokes history rebinding.
    // The same captured boundary and selected route used by Recompute own the
    // future node; its final displayed inventory is never an operation input.
    internal PredictorRouteNodeKnowledge? PreviewPlan(int actIndex, MapPosition point,
        PredictorPlannedNode plan, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (expectedRevision != Revision || Snapshot is not { } snapshot ||
            !Nodes.TryGetValue((actIndex, point), out var observed) || observed.Position.NodeId != plan.NodeId)
            return null;
        var source = snapshot.Run;
        int act = source.Working.Position.Act;
        if (actIndex != act - 1 || !_drafts.TryGetValue(actIndex, out var draft)) return null;
        int target = draft.ReadyPrefix.IndexOf(point);
        if (target < 0 || target >= 64) return null;
        PredictorRun.ValidatePlannedNodes([plan]);
        var plans = Plans.Where(saved => !PredictorMapNodeIdentity.TryParse(saved.NodeId, out var identity) ||
            identity.Act != act || identity.Point != point).Append(plan).ToImmutableArray();
        var run = new PredictorRun(source.Context, source.Working)
        { CombatDefaults = source.CombatDefaults, AutomaticRewardPickup = source.AutomaticRewardPickup };
        var visited = source.Working.Maps.FirstOrDefault(map => map.Act == act)?.Visited ?? [];
        var path = visited.AddRange(draft.ReadyPrefix);
        if (run.IsLegalRoute(run.RouteMap(act), path, visited)) run.SetRoute(act, path);
        else run.SetRoutePreviewPrefix(act, path);
        var projection = run.PreviewRouteContinuation(plans, maximumNodes: target + 1,
            scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults,
            allowSelectedPrefixes: true);
        return projection.Nodes.FirstOrDefault(node => node.Position.NodeId == plan.NodeId);
    }
}
