using System.Collections.Immutable;
using System.Text.Json;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.InRunMap;

// A native before-entry observation owns current-node replay. Actual departure
// replaces past hypothetical outcomes with the next detached entry boundary.
// Entered coordinates never promote a midpoint inventory/RNG capture to entry.
internal sealed partial class InRunMapPredictionSession
{
    private sealed record DraftSnapshot(MapPrediction Map, MapPosition? Previous,
        ImmutableArray<MapPosition> Selected, ImmutableArray<MapPosition> ReadyPrefix);
    private sealed record RedrawUndo(string DraftFingerprint, ImmutableDictionary<string, PredictorPlannedNode> Plans);
    private RunState? _native;
    private string? _version, _draftFingerprint;
    private ImmutableDictionary<int, DraftSnapshot> _drafts = ImmutableDictionary<int, DraftSnapshot>.Empty;
    private ImmutableDictionary<string, PredictorPlannedNode> _plans = ImmutableDictionary<string, PredictorPlannedNode>.Empty;
    private RedrawUndo? _redrawUndo;
    private readonly InRunMapPlanHistory _history = new();
    internal int HistoryCount => _history.Count;

    internal PredictorLiveRunSnapshot? Snapshot { get; private set; }
    internal PredictorRouteContinuation? Forecast { get; private set; }
    internal ImmutableDictionary<(int ActIndex, MapPosition Point), PredictorRouteNodeKnowledge> Nodes { get; private set; }
        = ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty;
    internal ImmutableDictionary<(int ActIndex, MapPosition Point), PredictorRouteTerminalBoundary> TerminalBoundaries { get; private set; }
        = ImmutableDictionary<(int, MapPosition), PredictorRouteTerminalBoundary>.Empty;
    internal bool WaitingForBoundary { get; private set; }
    internal bool IsCurrentNodeReplay => Snapshot?.EntryPoint != null;
    internal long Revision { get; private set; }
    internal (int ActIndex, MapPosition Point)? FaultLocation => Forecast?.Fault is { } fault &&
        PredictorMapNodeIdentity.TryParse(fault.Position.NodeId, out var identity)
        ? (identity.Act - 1, identity.Point) : null;
    internal ImmutableArray<PredictorPlannedNode> Plans => _plans.Values.OrderBy(p => p.NodeId, StringComparer.Ordinal).ToImmutableArray();

    internal void ForgetRedrawUndo() => _redrawUndo = null;

    internal void ClearPlans()
    {
        _plans = ImmutableDictionary<string, PredictorPlannedNode>.Empty;
        _history.Clear();
        _drafts = ImmutableDictionary<int, DraftSnapshot>.Empty;
        _draftFingerprint = null; _redrawUndo = null;
        Forecast = null;
        Nodes = ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty;
        TerminalBoundaries = ImmutableDictionary<(int, MapPosition), PredictorRouteTerminalBoundary>.Empty;
        Revision++;
    }

    internal void Update(IRunState runState, string version, bool canLeaveCurrentNode,
        IReadOnlyDictionary<int, PredictorMapRouteDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(runState);
        ArgumentNullException.ThrowIfNull(drafts);
        if (runState is not RunState native) throw new NotSupportedException("PredictorLiveConcreteRunRequired");
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("PredictorLiveVersionMissing", nameof(version));
        bool newRun = _native != null && !ReferenceEquals(_native, native);
        if (newRun) ResetForRun();
        _native = native;
        var entry = PredictorLiveRunCapture.CurrentNodeEntry(native, version);
        // Travel availability is not completion: shops enable it immediately,
        // and combat enables it before pending reward choices are resolved.
        // Keep the reliable entry premise until actual travel supplies a new one.
        bool lostEntry = Snapshot?.EntryPoint != null && entry == null;
        if (lostEntry)
        {
            Snapshot = null; Forecast = null;
            Nodes = ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty;
            TerminalBoundaries = ImmutableDictionary<(int, MapPosition), PredictorRouteTerminalBoundary>.Empty;
        }
        bool waiting = !canLeaveCurrentNode && entry == null;
        bool waitingChanged = WaitingForBoundary != waiting;
        WaitingForBoundary = waiting;
        // An unfinished room never supplies inventory/RNG for a new snapshot.
        // Existing detached state remains a valid basis for hypothetical edits.
        string? fingerprint = entry?.Fingerprint ?? (canLeaveCurrentNode ? PredictorLiveRunCapture.Fingerprint(native, true) : null);
        bool changedCapture = entry != null ? !ReferenceEquals(entry, Snapshot) : canLeaveCurrentNode &&
            (Snapshot == null || Snapshot.Fingerprint != fingerprint || _version != version);
        // Freeze authored selection values; no live native map references are
        // retained in these dictionaries, and no absent rows are filled.
        var nextDrafts = drafts.Where(pair => pair.Key == native.CurrentActIndex).ToImmutableDictionary(pair => pair.Key, pair => new DraftSnapshot(pair.Value.Map,
            pair.Value.Previous, pair.Value.Selected, pair.Value.ReadyPrefix));
        if (changedCapture)
        {
            Snapshot = entry ?? PredictorLiveRunCapture.Capture(native, version, true);
            _version = version;
            _redrawUndo = null;
        }
        if (Snapshot != null) nextDrafts = FromSnapshotBoundary(native, nextDrafts);
        string draftFingerprint = JsonSerializer.Serialize(nextDrafts.OrderBy(p => p.Key).Select(p => new
            { ActIndex = p.Key, p.Value.Map, p.Value.Previous, p.Value.Selected, p.Value.ReadyPrefix }));
        bool changedDrafts = draftFingerprint != _draftFingerprint;
        if (!changedCapture && !changedDrafts && !lostEntry)
        {
            if (waitingChanged) Revision++;
            return;
        }
        if (!changedCapture && changedDrafts)
        {
            if (_redrawUndo is { } undo && undo.DraftFingerprint == draftFingerprint)
            {
                _plans = undo.Plans;
                _redrawUndo = null;
            }
            else _redrawUndo = _draftFingerprint == null ? null : new(_draftFingerprint, _plans);
        }
        _drafts = nextDrafts;
        _draftFingerprint = draftFingerprint;
        if (Snapshot == null) { Revision++; return; }
        RetainApplicablePlans();
        Recompute();
    }

    private void ResetForRun()
    {
        Snapshot = null; Forecast = null;
        Nodes = ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty;
        TerminalBoundaries = ImmutableDictionary<(int, MapPosition), PredictorRouteTerminalBoundary>.Empty;
        _plans = ImmutableDictionary<string, PredictorPlannedNode>.Empty;
        _history.Clear();
        _drafts = ImmutableDictionary<int, DraftSnapshot>.Empty;
        _draftFingerprint = null; _version = null; _redrawUndo = null;
        WaitingForBoundary = false;
        Revision++;
    }

    private ImmutableDictionary<int, DraftSnapshot> FromSnapshotBoundary(RunState native,
        ImmutableDictionary<int, DraftSnapshot> authored)
    {
        var source = Snapshot!.Run;
        int origin = source.Working.Position.Act - 1;
        var result = authored.ToBuilder();
        int actIndex = native.CurrentActIndex;
        // Scope follows the actual Act, including an observed Ancient entry in
        // Acts 2/3. Never bridge an old Act's simulation into the next one.
        if (actIndex != origin) return ImmutableDictionary<int, DraftSnapshot>.Empty;
        authored.TryGetValue(actIndex, out var draft);
        var capturedMap = source.Working.Maps.FirstOrDefault(m => m.Act == actIndex + 1);
        var visited = capturedMap?.Visited ?? [];
        var observed = native.VisitedMapCoords.Select(InRunMapSnapshot.Position).ToImmutableArray();
        if (observed.Length < visited.Length || !observed.Take(visited.Length).SequenceEqual(visited))
            throw new InvalidOperationException("PredictorLiveSnapshotTravelBridgeChanged");
        var bridge = observed.Skip(visited.Length).ToImmutableArray();
        MapPosition? previous = visited.IsEmpty ? null : visited[^1];
        var map = capturedMap?.Map ?? source.RouteMap(actIndex + 1);
        var travel = new PredictorMapTravelAllowance(source.Working.Relics.Where(r => !r.Melted && r.Key.Entry == "WINGED_BOOTS")
            .Select(r => Math.Max(0, 3 - r.TimesUsed)).DefaultIfEmpty(0).Max(), source.Context.HasRunModifier("FLIGHT"));
        var detached = new PredictorMapRouteDraft(map, previous, travel);
        int after = bridge.IsEmpty ? previous?.Row ?? -1 : bridge[^1].Row;
        var selection = bridge.AddRange(draft?.Selected.Where(p => p.Row > after) ?? []);
        ImmutableArray<MapPosition> ready;
        if (detached.ApplyStroke(selection, PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid)
        {
            // Actual travel can invalidate an authored future fragment. Keep
            // that intent in the UI; project only the factual entered bridge.
            if (detached.ApplyStroke(bridge, PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Invalid)
                throw new InvalidOperationException("PredictorLiveSnapshotRouteBridgeInvalid");
            ready = detached.ReadyPrefix;
        }
        else ready = detached.ReadyPrefix;
        // Observed entered coordinates are supplied as a route to replay from
        // the old completed boundary, never added to its Working.Visited state.
        // Thus the current room is still projected, including pending choices.
        if (draft != null || !bridge.IsEmpty)
            result[actIndex] = new(map, previous, selection, ready);
        return result.ToImmutable();
    }

    internal PredictorPlannedNode? GetPlan(int actIndex, MapPosition point)
    {
        if (Nodes.TryGetValue((actIndex, point), out var node) && _plans.TryGetValue(node.Position.NodeId, out var exact)) return exact;
        // A changed occurrence does not rewrite old semantic observations.
        // Keep the authored identity available for an explicit UI edit.
        return _plans.Values.FirstOrDefault(plan => PredictorMapNodeIdentity.TryParse(plan.NodeId, out var identity) &&
            identity.Act == actIndex + 1 && identity.Point == point);
    }

    internal bool SetPlan(int actIndex, MapPosition point, PredictorPlannedNode plan, long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (expectedRevision is { } revision && revision != Revision) return false;
        if (!Nodes.TryGetValue((actIndex, point), out var node) || plan.NodeId != node.Position.NodeId) return false;
        PredictorRun.ValidatePlannedNodes([plan]);
        plan = _history.Remember(plan, node);
        if (_plans.TryGetValue(plan.NodeId, out var current) && current == plan) return true;
        // An explicit replacement owns the active coordinate. Other event
        // instances remain history and can bind only to their own queue receipt.
        _plans = _plans.RemoveRange(_plans.Keys.Where(key => PredictorMapNodeIdentity.TryParse(key, out var identity) &&
            identity.Act == actIndex + 1 && identity.Point == point)).SetItem(plan.NodeId, plan);
        _redrawUndo = null;
        Recompute();
        return true;
    }

    internal bool RemovePlan(int actIndex, MapPosition point, long? expectedRevision = null)
    {
        if (expectedRevision is { } revision && revision != Revision) return false;
        var keys = _plans.Keys.Where(key => PredictorMapNodeIdentity.TryParse(key, out var identity) &&
            identity.Act == actIndex + 1 && identity.Point == point).ToArray();
        _history.Remove(actIndex, point);
        if (keys.Length == 0) return true;
        _plans = _plans.RemoveRange(keys);
        _redrawUndo = null;
        Recompute();
        return true;
    }

    private void RetainApplicablePlans()
    {
        var state = Snapshot!.Run.Working;
        bool Applicable(PredictorPlannedNode plan) => PredictorMapNodeIdentity.TryParse(plan.NodeId, out var identity) &&
            identity.Act == state.Position.Act && identity.Floor > state.Position.Floor &&
            _drafts.TryGetValue(identity.Act - 1, out var draft) && draft.Selected.Contains(identity.Point);
        var retained = _plans.Values.Where(Applicable).ToImmutableDictionary(plan => plan.NodeId);
        // Leaving a route retires only active simulation inputs. The history
        // survives arbitrarily many redraws and is retired only by explicit edits
        // or a new Run. Past operations never become active at a new boundary.
        _plans = _history.LatestPlans.Where(Applicable).ToImmutableDictionary(plan => plan.NodeId, plan =>
            retained.GetValueOrDefault(plan.NodeId) ?? plan).SetItems(retained);
    }

    internal void Recompute()
    {
        if (Snapshot == null) return;
        Revision++;
        try { RecomputeCore(); }
        catch
        {
            // A failed attempt must not leave a new capture/fingerprint paired
            // with old results, or make an identical explicit refresh a no-op.
            _draftFingerprint = null;
            Forecast = null;
            Nodes = ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty;
            TerminalBoundaries = ImmutableDictionary<(int, MapPosition), PredictorRouteTerminalBoundary>.Empty;
            throw;
        }
    }

    private void RecomputeCore()
    {
        var source = Snapshot!.Run;
        var run = new PredictorRun(source.Context, source.Working)
        { CombatDefaults = source.CombatDefaults, AutomaticRewardPickup = source.AutomaticRewardPickup };
        int currentAct = source.Working.Position.Act;
        var map = source.Working.Maps.FirstOrDefault(m => m.Act == currentAct);
        var visited = map?.Visited ?? [];
        bool alreadyComplete = !visited.IsEmpty && map!.Map.Points.Any(p => p.Position == visited[^1] &&
            p.Type == MapPointType.Boss && p.Children.Count == 0);
        if (alreadyComplete || !_drafts.TryGetValue(currentAct - 1, out var draft) || draft.ReadyPrefix.IsEmpty)
        {
            Forecast = new([], source.Context.Live?.PendingCauses ?? [], false);
            Nodes = ImmutableDictionary<(int, MapPosition), PredictorRouteNodeKnowledge>.Empty;
            TerminalBoundaries = ImmutableDictionary<(int, MapPosition), PredictorRouteTerminalBoundary>.Empty;
            return;
        }
        var path = visited.AddRange(draft.ReadyPrefix);
        // Current factual coordinates plus the connected ready selection are
        // the entire intent; only the actual Act receives a simulated route.
        if (run.IsLegalRoute(run.RouteMap(currentAct), path, visited)) run.SetRoute(currentAct, path);
        else run.SetRoutePreviewPrefix(currentAct, path);
        int maximumNodes = Math.Min(64, draft.ReadyPrefix.Length);
        PredictorRouteContinuation forecast;
        // The ordinary case takes one projection. When a redraw reveals a
        // different event instance, rebind the earliest differing saved intent
        // first; later instance identities depend on those earlier choices.
        int rebindings = 0;
        while (true)
        {
            run.RestorePlannedNodes(Plans);
            forecast = run.PreviewRouteContinuation(run.PlannedNodes, maximumNodes: maximumNodes,
                scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults,
                allowSelectedPrefixes: true, isolateNodeFailures: true);
            // Stopping at the authored current-Act prefix is ordinary route
            // completion, and prevents even a private next-Act entry attempt.
            if (draft.ReadyPrefix.Length <= 64 && forecast.ReachedLimit) forecast = forecast with { ReachedLimit = false };
            var replacement = forecast.Nodes.OrderBy(node => node.Position.Floor).Select(node =>
            {
                var current = _plans.GetValueOrDefault(node.Position.NodeId);
                var matching = _history.Matching(node, current);
                return (Current: current, Matching: matching);
            }).FirstOrDefault(pair => pair.Matching != null && pair.Matching != pair.Current);
            if (replacement.Matching == null) break;
            if (++rebindings > 64) throw new InvalidOperationException("PredictorLivePlanHistoryDidNotStabilize");
            _plans = _plans.SetItem(replacement.Matching.NodeId, replacement.Matching);
        }
        var nodes = forecast.Nodes.Where(n => PredictorMapNodeIdentity.TryParse(n.Position.NodeId, out _))
            .ToImmutableDictionary(n =>
            {
                PredictorMapNodeIdentity.TryParse(n.Position.NodeId, out var identity);
                return (identity.Act - 1, identity.Point);
            }, n => n);
        Forecast = forecast;
        Nodes = nodes;
        TerminalBoundaries = TerminalLocations(forecast);
        LogForecastFault(forecast);
    }

    private static void LogForecastFault(PredictorRouteContinuation forecast)
    {
        if (forecast.Fault is { } fault)
            RuntimeLog.Fault($"inRunRouteNodePredictionFailed=true;node={fault.Position.NodeId};stage={fault.Stage}", fault.Error);
    }

    internal static ImmutableDictionary<(int ActIndex, MapPosition Point), PredictorRouteTerminalBoundary> TerminalLocations(
        PredictorRouteContinuation forecast)
    {
        var terminal = forecast.TerminalBoundary;
        if (terminal == null) return ImmutableDictionary<(int, MapPosition), PredictorRouteTerminalBoundary>.Empty;
        // If no next coordinate is established, the departure point is the
        // visible anchor. TargetPosition still identifies the blocked boundary.
        if (!PredictorMapNodeIdentity.TryParse(terminal.TargetPosition.NodeId, out var identity) &&
            !PredictorMapNodeIdentity.TryParse(terminal.DeparturePosition.NodeId, out identity))
            return ImmutableDictionary<(int, MapPosition), PredictorRouteTerminalBoundary>.Empty;
        return ImmutableDictionary<(int, MapPosition), PredictorRouteTerminalBoundary>.Empty
            .Add((identity.Act - 1, identity.Point), terminal);
    }
}
