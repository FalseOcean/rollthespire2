using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // This observes a player's possible event-appearance hypotheses. It never
    // executes a dynamic IsAllowed predicate or advances Working/RNG/request.
    internal PredictorEventOccurrenceObservation? ObserveEventOccurrence()
    {
        RequireRouteSimulation();
        if (Phase != PredictorPhase.EventEligibility) return null;
        var act = Working.Acts.Single(a => a.Act == Working.Position.Act);
        var candidates = ImmutableArray.CreateBuilder<PredictorEventOccurrenceCandidate>();
        bool stopped = false;
        int remaining = act.RawEvents.Length - _eligibilityExamined;
        for (int offset = 0; offset < remaining; offset++)
        {
            int cursor = checked(act.EventCursor + offset);
            var key = act.RawEvents[cursor % act.RawEvents.Length];
            var rule = EventOccurrenceStaticEligibility(key);
            if (rule.ShouldReject || Working.VisitedEvents.Contains(key)) continue;
            bool unconditional = rule.EligibilityKind == EventCandidateEligibilityKind.StaticEligible &&
                rule.Precision == RolltheSpire2.Core.Prediction.PredictionPrecision.Exact;
            candidates.Add(EventOccurrenceCandidate(act, key, cursor, rule.EligibilityKind, unconditional, false));
            if (unconditional) { stopped = true; break; }
        }
        // EnsureNextEventIsValid allows repetition after precisely one raw
        // cycle, including its original disallowed/visited entry. This is an
        // explicit hypothesis only when every earlier candidate may fail.
        if (!stopped)
        {
            int cursor = checked(act.EventCursor + remaining);
            var key = act.RawEvents[cursor % act.RawEvents.Length];
            candidates.Add(EventOccurrenceCandidate(act, key, cursor,
                EventOccurrenceStaticEligibility(key).EligibilityKind, false, true));
        }
        var observation = new PredictorEventOccurrenceObservation(Working.Position.NodeId, act.Act, act.EventCursor, candidates.ToImmutable(), !stopped);
        EventTrace()?.Occurrence(this, observation, _preview.Partial == null ? [] : RouteEventOccurrenceCauses(this, _preview.Partial.EventTraceDependencies));
        return observation;
    }

    private PredictorEventOccurrenceCandidate EventOccurrenceCandidate(PredictorActState act, ModelKey key,
        int cursor, EventCandidateEligibilityKind eligibility, bool unconditional, bool fallback) => new(
            LanternKeyReplacesEvent(act.Act) ? new("EVENT", "WAR_HISTORIAN_REPY") : key,
            key, cursor % act.RawEvents.Length + 1, cursor, eligibility, unconditional, fallback);

    private EventStaticEligibilityResult EventOccurrenceStaticEligibility(ModelKey key)
    {
        var source = Context.AncientAuthority;
        var immutable = source == null ? EventImmutableEligibilityContext.Unknown : new(
            source.GameMode, source.ModeFactsExact, source.PlayerCount, source.ModeFactsExact,
            source.EventAuthority.CharacterCardPoolCount, source.EventAuthority.CharacterCardPoolCountExact);
        // Runtime initialization admits Beta111 only. Shared evaluates every
        // audited act mask; RawEvents already owns act-local pool membership.
        var result = EventStaticEligibilityCatalog.Evaluate(key, Working.Position.Act, RuntimeProfileId.Beta111,
            EventPoolSourceKind.Shared, immutable);
        EventTrace()?.StaticEligibility(this, key, result.EligibilityKind + ":precision=" + result.Precision + ":reject=" + result.ShouldReject);
        return result;
    }

    private PredictorInputResult SubmitEventOccurrence(ChooseEventOccurrence input)
    {
        var observation = ObserveEventOccurrence();
        if (observation == null || input.ExpectedCursor != observation.Cursor) return PredictorInputResult.Invalid;
        var selected = observation.Candidates.FirstOrDefault(c => c.Event == input.Event &&
            c.QueueEvent == input.QueueEvent && c.RawOrdinal == input.RawOrdinal && c.Cursor == input.SelectedCursor);
        if (selected == null) return PredictorInputResult.Invalid;
        var trace = EventTrace();
        var traceReads = trace?.Action(this, input, PredictorCombatDomain.None, _preview.Partial?.EventTraceDependencies ?? []) ?? [];
        int index = Working.Acts.FindIndex(a => a.Act == Working.Position.Act);
        var act = Working.Acts[index];
        // Cursor advancement is queue bookkeeping, not a random-stream draw.
        _eligibilityExamined += selected.Cursor - act.EventCursor;
        Working = Working with { Acts = Working.Acts.SetItem(index, act with { EventCursor = selected.Cursor }) };
        FinishEventOccurrence(selected.QueueEvent);
        trace?.Stage(this, _preview.Partial?.EventTraceDependencies ?? [], traceReads);
        return PredictorInputResult.Accepted;
    }
}
