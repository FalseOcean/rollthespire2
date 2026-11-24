using System.Collections.Immutable;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// RawOrdinal follows EventPoolSequenceProjector's one-based raw queue ordinal.
// Cursor is the absolute source cursor, including skipped entries and cycles.
// QueueEvent remains distinct from the actual Event replaced by LanternKey.
internal sealed record PredictorEventOccurrenceCandidate(ModelKey Event, ModelKey QueueEvent,
    int RawOrdinal, int Cursor, EventCandidateEligibilityKind EligibilityKind,
    bool IsUnconditional, bool IsCycleFallback)
{
    // Assumed admission never supplies a concrete health/inventory fact.
    public bool HealthEligibilityAssumed { get; init; }
    public bool DeckEligibilityAssumed { get; init; }
    public bool EligibilityAssumed { get; init; }
    public bool ExcludedByPlayer { get; init; }
    public ImmutableArray<PredictorCombatPendingCause> EligibilityPendingCauses { get; init; } = [];
}

// Node-owned intent: rejecting this queue occurrence does not name a successor
// or make this event unavailable at another route node or queue cycle.
internal sealed record PredictorEventOccurrenceExclusion(ModelKey QueueEvent, int Cursor);

internal sealed record PredictorEventOccurrenceObservation(string NodeId, int Act, int Cursor,
    ImmutableArray<PredictorEventOccurrenceCandidate> Candidates, bool ExhaustionFallbackAvailable);

// Bind the authored hypothesis to the displayed identity and queue boundary.
// A raw ordinal alone cannot distinguish a first pass from exhausted repetition.
internal sealed record ChooseEventOccurrence(ModelKey Event, ModelKey QueueEvent,
    int RawOrdinal, int ExpectedCursor, int SelectedCursor) : PredictorInput;
