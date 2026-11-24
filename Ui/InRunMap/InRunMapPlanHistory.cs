using System.Collections.Immutable;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunMap;

// Same-run authored intent only: no cached outcomes or live object references.
// NodeId identifies the map occurrence; the event queue receipt distinguishes
// repeated/replaced events even when their displayed model names are equal.
internal sealed class InRunMapPlanHistory
{
    private sealed record Entry(PredictorPlannedNode Plan, PredictorRoomKind Room, string? Model);
    private ImmutableArray<Entry> _entries = [];
    internal int Count => _entries.Length;
    internal IEnumerable<PredictorPlannedNode> LatestPlans => _entries.GroupBy(e => e.Plan.NodeId)
        .Select(group => group.Last().Plan);

    internal PredictorPlannedNode Remember(PredictorPlannedNode plan, PredictorRouteNodeKnowledge node)
    {
        if (plan.Event is { } authored)
        {
            if (authored.Occurrence is { } explicitChoice)
                plan = plan with { Event = authored with { ExpectedOccurrence = explicitChoice } };
            else if (authored.ExpectedOccurrence == null && node.Event is
                { Event: { } observed, Occurrence: { } occurrence, SelectedOccurrence: { } selected } &&
                authored.ExpectedEvent == observed && selected.Event == observed)
                plan = plan with { Event = authored with { ExpectedOccurrence = new(selected.Event, selected.QueueEvent,
                    selected.RawOrdinal, occurrence.Cursor, selected.Cursor) } };
        }
        var entry = new Entry(plan, node.Room, node.Model?.Serialized);
        // Appearance exclusions belong to the coordinate, while operations
        // belong to an event instance. Restoring another instance must not
        // restore its obsolete appearance policy and select the first one again.
        _entries = _entries.RemoveAll(old => SameInstance(old, entry)).Select(old =>
            old.Plan.NodeId == plan.NodeId ? old with
            { Plan = old.Plan with { EventOccurrenceExclusions = plan.EventOccurrenceExclusions } } : old).ToImmutableArray().Add(entry);
        return plan;
    }

    internal PredictorPlannedNode? Matching(PredictorRouteNodeKnowledge node, PredictorPlannedNode? preferred)
    {
        var matches = _entries.Where(entry => entry.Plan.NodeId == node.Position.NodeId && Matches(entry, node)).ToArray();
        return matches.FirstOrDefault(entry => entry.Plan == preferred)?.Plan ?? matches.LastOrDefault()?.Plan;
    }

    private static bool SameInstance(Entry a, Entry b) => a.Plan.NodeId == b.Plan.NodeId && a.Room == b.Room &&
        a.Model == b.Model && a.Plan.Event?.ExpectedEvent == b.Plan.Event?.ExpectedEvent &&
        Receipt(a.Plan) == Receipt(b.Plan);

    private static ChooseEventOccurrence? Receipt(PredictorPlannedNode plan) => plan.Event?.ExpectedOccurrence ?? plan.Event?.Occurrence;

    private static bool Matches(Entry entry, PredictorRouteNodeKnowledge node)
    {
        if (entry.Room != node.Room) return false;
        if (entry.Plan.Event is not { } plan) return entry.Model == node.Model?.Serialized;
        if (node.Event is not { } observed) return false;
        if (Receipt(entry.Plan) is not { } receipt) return observed.Event == plan.ExpectedEvent && observed.Occurrence == null;
        if (observed.Occurrence is not { } occurrence || occurrence.Cursor != receipt.ExpectedCursor) return false;
        if (plan.Occurrence != null)
            return occurrence.Candidates.Any(candidate => !candidate.ExcludedByPlayer && SameOccurrence(candidate, receipt));
        // A remembered automatic appearance is an observation, not an override.
        // Excluding an event still chooses today's next event automatically.
        return observed.SelectedOccurrence is { } selected && SameOccurrence(selected, receipt);
    }

    private static bool SameOccurrence(PredictorEventOccurrenceCandidate candidate, ChooseEventOccurrence receipt) =>
        candidate.Event == receipt.Event && candidate.QueueEvent == receipt.QueueEvent &&
        candidate.RawOrdinal == receipt.RawOrdinal && candidate.Cursor == receipt.SelectedCursor;

    internal void Remove(int actIndex, MapPosition point) => _entries = _entries.RemoveAll(entry =>
        PredictorMapNodeIdentity.TryParse(entry.Plan.NodeId, out var identity) && identity.Act == actIndex + 1 && identity.Point == point);
    internal void Clear() => _entries = [];
}
