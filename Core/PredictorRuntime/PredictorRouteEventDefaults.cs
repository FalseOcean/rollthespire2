using System.Collections.Immutable;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private static PredictorEventOccurrenceObservation? ObserveRouteEventOccurrence(PredictorRun run,
        List<RouteDependency> dependencies, ImmutableArray<PredictorEventOccurrenceExclusion> exclusions)
    {
        var observation = run.ObserveEventOccurrence();
        if (observation == null || run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults) return observation;
        var candidates = ImmutableArray.CreateBuilder<PredictorEventOccurrenceCandidate>();
        foreach (var candidate in observation.Candidates)
        {
            // One source computation owns both admission and its input facts.
            var appearance = EvaluateEventAppearance(run, candidate.QueueEvent, dependencies);
            if (!candidate.IsCycleFallback && appearance.Allowed == false) continue;
            // A source predicate proved true by arrival facts closes this queue
            // just like a statically unconditional event. A missing/custom
            // predicate remains assumed; a handler being modeled is not proof.
            bool assumed = !candidate.IsUnconditional &&
                (appearance.HealthAssumed || appearance.DeckAssumed || appearance.Allowed != true ||
                 !RouteEventAppearancePredicateKnown(candidate.QueueEvent));
            bool unconditional = !candidate.IsCycleFallback && !assumed;
            candidates.Add(candidate with
            {
                IsUnconditional = unconditional,
                HealthEligibilityAssumed = !candidate.IsUnconditional && appearance.HealthAssumed,
                DeckEligibilityAssumed = !candidate.IsUnconditional && appearance.DeckAssumed,
                EligibilityAssumed = assumed,
                ExcludedByPlayer = !unconditional && !candidate.IsCycleFallback &&
                    exclusions.Any(e => e.QueueEvent == candidate.QueueEvent && e.Cursor == candidate.Cursor),
                EligibilityPendingCauses = appearance.PendingCauses
            });
            if (unconditional) break;
        }
        var ordered = candidates.ToImmutable();
        return observation with { Candidates = ordered,
            ExhaustionFallbackAvailable = ordered.Any(c => c.IsCycleFallback) };
    }

    private static bool RouteEventAppearancePredicateKnown(ModelKey key) => key.Entry is
        "MORPHIC_GROVE" or "CRYSTAL_SPHERE" or "WELCOME_TO_WONGOS" or "WHISPERING_HOLLOW" or
        "ENDLESS_CONVEYOR" or "TEA_MASTER" or "ZEN_WEAVER" or "WATERLOGGED_SCRIPTORIUM" or
        "PUNCH_OFF" or "SLIPPERY_BRIDGE" or "THE_FUTURE_OF_POTIONS" or "STONE_OF_ALL_TIME" or
        "RANWID_THE_ELDER" or "RELIC_TRADER" or "FAKE_MERCHANT" or "BYRDONIS_NEST" or "LUMINOUS_CHOIR";
}
