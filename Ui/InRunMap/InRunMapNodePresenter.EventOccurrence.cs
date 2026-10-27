using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private void EventOccurrenceEditor(Context c, EditorParts parts, PredictorEventNodeKnowledge node)
    {
        if (node.AuthoredActionsNotApplied && node.AuthoredEvent is { } authored)
            parts.Sections.Add(new("", _text.Format("predictor.map.event_plan_retained",
                _names.Resolve(authored, GameContentKind.Event)), [], []));

        var candidates = node.Occurrence?.Candidates ?? [];
        bool show = candidates.Length > 1 || !c.Plan.EventOccurrenceExclusions.IsEmpty ||
            candidates.Any(candidate => candidate.Cursor == node.SelectedOccurrence?.Cursor &&
                !candidate.IsUnconditional && !candidate.IsCycleFallback) ||
            node.NeededInput == PredictorEventNeededInput.Occurrence;
        if (!show) return;
        bool first = true;
        foreach (var candidate in candidates)
        {
            var exclusion = new PredictorEventOccurrenceExclusion(candidate.QueueEvent, candidate.Cursor);
            bool current = node.SelectedOccurrence?.Cursor == candidate.Cursor &&
                node.SelectedOccurrence?.QueueEvent == candidate.QueueEvent;
            bool excluded = candidate.ExcludedByPlayer;
            bool retainedExclusion = c.Plan.EventOccurrenceExclusions.Contains(exclusion);
            var choices = new List<InRunMapDetailChoice>();
            if (retainedExclusion)
                choices.Add(Choice(c, M("event_restore"), context => SetEventExclusion(context, exclusion, false)));
            else if (current && !candidate.IsUnconditional && !candidate.IsCycleFallback)
                choices.Add(Choice(c, M("event_exclude"), context => SetEventExclusion(context, exclusion, true)));
            parts.Sections.Add(new(first ? M("event_occurrence") : "",
                excluded ? M("event_excluded") : retainedExclusion ? M("event_exclusion_inactive") : current ? M("event_current") : M("event_next"),
                [Object(candidate.Event) with { DisplayName = _text.Format("predictor.map.event_candidate",
                    _names.Resolve(candidate.Event, GameContentKind.Event), candidate.RawOrdinal) }], choices)
                { CollapsedByDefault = true, FoldoutId = "occurrence" });
            first = false;
        }
        // A changed arrival state may make an old exclusion inapplicable. Keep
        // the intent visible and reversible without changing today's default.
        foreach (var exclusion in c.Plan.EventOccurrenceExclusions.Where(exclusion =>
            !candidates.Any(candidate => candidate.QueueEvent == exclusion.QueueEvent && candidate.Cursor == exclusion.Cursor)))
        {
            parts.Sections.Add(new(first ? M("event_occurrence") : "", M("event_exclusion_inactive"),
                [Object(exclusion.QueueEvent)],
                [Choice(c, M("event_restore"), context => SetEventExclusion(context, exclusion, false))])
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
            Event = c.Plan.Event is { } authored ? authored with { Occurrence = null } : null });
    }
}
