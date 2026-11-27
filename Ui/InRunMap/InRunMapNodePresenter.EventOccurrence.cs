using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private void EventOccurrenceEditor(Context c, EditorParts parts, PredictorEventNodeKnowledge node)
    {
        var candidates = node.Occurrence?.Candidates ?? [];
        var currentOccurrence = node.SelectedOccurrence ?? node.PendingOccurrence;
        var actions = new List<InRunMapDetailChoice>();
        if (currentOccurrence is { CanExclude: true, ExcludedByPlayer: false })
        {
            var exclusion = new PredictorEventOccurrenceExclusion(currentOccurrence.QueueEvent, currentOccurrence.Cursor);
            actions.Add(Choice(c, M("event_exclude"), context => SetEventExclusion(context, exclusion, true)));
        }
        foreach (var exclusion in c.Plan.EventOccurrenceExclusions)
        {
            var candidate = candidates.FirstOrDefault(candidate => candidate.QueueEvent == exclusion.QueueEvent && candidate.Cursor == exclusion.Cursor);
            string name = _names.Resolve(candidate?.Event ?? exclusion.QueueEvent, GameContentKind.Event);
            actions.Add(Choice(c, M("event_restore") + " · " + name, context => SetEventExclusion(context, exclusion, false)));
        }
        if (actions.Count != 0) parts.Sections.Add(new("", "", [], actions));

        if (node.AuthoredActionsNotApplied && node.AuthoredEvent is { } authored)
            parts.Sections.Add(new("", _text.Format("predictor.map.event_plan_retained",
                _names.Resolve(authored, GameContentKind.Event)), [], []));

        bool show = candidates.Length > 1 || currentOccurrence is { EligibilityAssumed: true } or { IsCycleFallback: true } ||
            node.NeededInput == PredictorEventNeededInput.Occurrence;
        if (!show) return;
        bool first = true;
        foreach (var candidate in candidates)
        {
            var exclusion = new PredictorEventOccurrenceExclusion(candidate.QueueEvent, candidate.Cursor);
            bool current = currentOccurrence?.Cursor == candidate.Cursor && currentOccurrence?.QueueEvent == candidate.QueueEvent;
            bool excluded = candidate.ExcludedByPlayer;
            bool retainedExclusion = c.Plan.EventOccurrenceExclusions.Contains(exclusion);
            var choices = new List<InRunMapDetailChoice>();
            var occurrence = new ChooseEventOccurrence(candidate.Event, candidate.QueueEvent, candidate.RawOrdinal,
                node.Occurrence!.Cursor, candidate.Cursor);
            if (!excluded && c.Plan.Event?.Occurrence != occurrence &&
                (!current || candidate.EligibilityAssumed))
                choices.Add(Choice(c, M("event_assume_occurrence"), context =>
                {
                    var plan = context.Plan.Event;
                    var receipt = plan?.Occurrence ?? plan?.ExpectedOccurrence;
                    var selected = plan?.ExpectedEvent == candidate.Event && (receipt == occurrence || receipt == null && current)
                        ? plan : new PredictorPlannedEvent(candidate.Event);
                    return Set(context, context.Plan with { Event = selected with { Occurrence = occurrence, ExpectedOccurrence = null } });
                }));
            else if (c.Plan.Event?.Occurrence == occurrence)
                choices.Add(Choice(c, M("event_auto_occurrence"), context => Set(context, context.Plan with
                    { Event = context.Plan.Event! with { Occurrence = null, ExpectedOccurrence = occurrence } })));
            parts.Sections.Add(new(first ? M("event_occurrence") : "",
                excluded ? M("event_excluded") : retainedExclusion ? M("event_exclusion_inactive") :
                    candidate.IsCycleFallback ? M("event_exhaustion_fallback") : current ? M(node.SelectedOccurrence == null ? "event_occurrence_pending" : "event_current") :
                    candidate.OmittedByPotionDefault ? M("event_potion_default") : M("event_next"),
                [Object(candidate.Event) with { DisplayName = _text.Format("predictor.map.event_candidate",
                    _names.Resolve(candidate.Event, GameContentKind.Event), candidate.RawOrdinal) }], choices)
                { CollapsedByDefault = true, FoldoutId = "occurrence" });
            first = false;
        }
    }

    private bool SetEventExclusion(Context c, PredictorEventOccurrenceExclusion exclusion, bool excluded)
    {
        var exclusions = c.Plan.EventOccurrenceExclusions.Remove(exclusion);
        if (excluded) exclusions = exclusions.Add(exclusion);
        return Set(c, c.Plan with { EventOccurrenceExclusions = exclusions,
            // This explicit edit replaces an old appearance lock, not its
            // event-specific operations or local observations.
            Event = c.Plan.Event is { } authored ? authored with
                { Occurrence = null, ExpectedOccurrence = authored.Occurrence ?? authored.ExpectedOccurrence } : null });
    }
}
