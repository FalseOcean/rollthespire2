using System.Collections.Immutable;

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
            // RoomSet uses the event at the next cursor after one exhausted
            // cycle without asking its ordinary IsAllowed again.
            if (candidate.IsCycleFallback)
            {
                candidates.Add(candidate);
                continue;
            }
            var appearance = EvaluateEventAppearance(run, candidate, dependencies);
            if (appearance.Allowed == false) continue;
            // A dynamic condition can be proved at this arrival and still be
            // an editable premise. Only the source queue's unconditional
            // closure limits which later occurrences are reachable.
            int minimumPotions = candidate.QueueEvent.Entry switch
            {
                "STONE_OF_ALL_TIME" or "RANWID_THE_ELDER" => 1, _ => 0
            };
            bool omittedByPotionDefault = run.Context.UsesNonCombatGeneration &&
                minimumPotions > 0 && run.RouteEventPotionInput(candidate.QueueEvent) == null &&
                appearance.PendingCauses.Any(c => c.Code == "MapPotionInventoryNotProjected") &&
                run.Working.PotionSlots.Count(potion => potion != null) < minimumPotions;
            candidates.Add(candidate with
            {
                HealthEligibilityAssumed = candidate.CanExclude && appearance.HealthAssumed,
                DeckEligibilityAssumed = candidate.CanExclude && appearance.DeckAssumed,
                EligibilityAssumed = candidate.CanExclude &&
                    (appearance.HealthAssumed || appearance.DeckAssumed || appearance.Allowed != true),
                OmittedByPotionDefault = omittedByPotionDefault,
                ExcludedByPlayer = candidate.CanExclude &&
                    exclusions.Any(e => e.QueueEvent == candidate.QueueEvent && e.Cursor == candidate.Cursor),
                EligibilityPendingCauses = appearance.PendingCauses
            });
        }
        var ordered = candidates.ToImmutable();
        return observation with { Candidates = ordered,
            ExhaustionFallbackAvailable = ordered.Any(c => c.IsCycleFallback) };
    }

}
