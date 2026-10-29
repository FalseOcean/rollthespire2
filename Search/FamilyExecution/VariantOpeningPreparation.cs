using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record VariantOpeningActAllowance(
    int Act,
    IReadOnlyList<ModelKey> ExplicitAllowed,
    IReadOnlyList<ModelKey> EventNecessaryAllowed,
    IReadOnlyList<ModelKey> Allowed,
    IReadOnlyList<string> Evidence);

// Request-local necessary conditions, never additional authored predicates. All
// lists are copied and frozen; neither catalog refresh nor execution changes them.
internal sealed record VariantOpeningPreparation(
    IReadOnlyList<VariantOpeningActAllowance> Acts,
    int ExplicitUnknownPrefixCount,
    string AuthorityFingerprint,
    IReadOnlyList<string> Evidence)
{
    internal bool HasVariantRestrictions => Acts.Count > 0;
    internal bool HasConditions => HasVariantRestrictions || ExplicitUnknownPrefixCount > 0;
    internal bool Allows(int act, ModelKey selected) =>
        Acts.FirstOrDefault(row => row.Act == act) is not { } row || row.Allowed.Contains(selected);

    internal static VariantOpeningPreparation Prepare(CompiledSearch compiled)
    {
        var query = compiled.NormalizedQuery;
        var generation = compiled.Context.Authority.WorldAuthority?.Beta109Generation;
        var evidence = new List<string>();
        var rows = new List<VariantOpeningActAllowance>();
        int prefix = Math.Max(query.FirstActUnknownEventPrefixCount,
            query.Players.Select(player => player.Conditions.FirstActUnknownEventPrefixCount).DefaultIfEmpty(0).Max());
        var requestedActs = query.VariantBossBranches.Select(branch => branch.Act)
            .Concat(query.EventSequenceConstraints.Where(condition => !condition.IsEmpty &&
                condition.Keys.All.Count + condition.Keys.Any.Count > 0).Select(condition => condition.Act))
            .Distinct().Order().ToArray();
        foreach (int act in requestedActs)
        {
            ModelKey[] explicitKeys = query.VariantBossBranches.Where(branch => branch.Act == act)
                .Select(branch => branch.VariantKey).Distinct(ModelKeyComparer.Instance).ToArray();
            var matchingGroups = generation?.ActSelectionGroups.Where(item => item.Act == act).ToArray() ?? [];
            var group = matchingGroups.Length == 1 ? matchingGroups[0] : null;
            ModelKey[] universe = group is null ? [] : generation!.ActSelectionAuthorityExact &&
                group.EligibilityAndOrderExact && group.EligibleActsInSourceOrder.Count > 0
                ? WorldProbabilityEstimator.ResolveVariantPriors(generation, group).Select(item => item.Key).ToArray()
                : group.EligibleActsInSourceOrder.Distinct(ModelKeyComparer.Instance).ToArray();
            if (universe.Length == 0 && group is not null)
                universe = group.EligibleActsInSourceOrder.Distinct(ModelKeyComparer.Instance).ToArray();
            var rowEvidence = new List<string>();
            var events = query.EventSequenceConstraints.Where(condition => !condition.IsEmpty && condition.Act == act &&
                condition.Keys.All.Count + condition.Keys.Any.Count > 0).ToArray();
            // Missing hook/closed-membership authority means absence from the
            // captured list cannot exclude a possible variant. Custom identities
            // already in a closed captured list need no hard-coded vanilla table.
            bool closed = generation is { DirectSourceAudited: true, WorldGenerationHooksExact: true,
                NoUnknownHooksOrModifiers: true, SharedEventCatalogExact: true } &&
                EventStaticEligibilityCatalog.ProjectionPrecision(compiled.Context.ProfileId) == PredictionPrecision.Exact;
            var eventAllowed = new List<ModelKey>();
            foreach (ModelKey key in universe)
            {
                bool possible = true;
                var variant = generation!.OrderedActCatalog.FirstOrDefault(item => item.Act == act && item.ActKey == key);
                if (events.Length > 0 && closed && variant is not null &&
                    EventPoolSequenceProjector.TryPrepare(act, key, variant.EventRngConsumptionExact,
                        variant.OrderedRawEvents, generation.EventAuthority.OrderedSharedEventsRaw,
                        generation.EventAuthority, variant.OrderedEligibleEvents, out var prepared, out _) && prepared is not null)
                {
                    var immutable = new EventImmutableEligibilityContext(generation.GameMode, generation.ModeFactsExact,
                        generation.PlayerCount, generation.ModeFactsExact && generation.IsMultiplayerExact,
                        generation.EventAuthority.CharacterCardPoolCount, generation.EventAuthority.CharacterCardPoolCountExact);
                    var candidates = prepared.Candidates.Where(candidate => !EventStaticEligibilityCatalog.Evaluate(
                        candidate.EventKey, act, compiled.Context.ProfileId, candidate.Source, immutable).ShouldReject).ToArray();
                    foreach (var condition in events)
                    {
                        var available = candidates.Where(candidate => !condition.Source.HasValue || candidate.Source == condition.Source.Value)
                            .Select(candidate => candidate.EventKey).ToHashSet(ModelKeyComparer.Instance);
                        // Rows are conjunctive. Any is a union within its own row;
                        // Ban never excludes a whole variant by simple membership.
                        if (condition.Keys.All.Any(target => !available.Contains(target)) ||
                            condition.Keys.Any.Count > 0 && !condition.Keys.Any.Any(available.Contains))
                        {
                            possible = false;
                            rowEvidence.Add($"Act{act}:{key.Serialized}:PositiveEventMembershipImpossible:{condition.Source}:{condition.RangeMode}:{condition.RangeValue}");
                            break;
                        }
                    }
                }
                else if (events.Length > 0)
                    rowEvidence.Add($"Act{act}:{key.Serialized}:EventMembershipUnknownRetained");
                if (possible) eventAllowed.Add(key);
            }
            bool inferredRestriction = universe.Length > 0 && eventAllowed.Count != universe.Length;
            bool explicitRestriction = explicitKeys.Length > 0 && (universe.Length == 0 ||
                universe.Any(key => !explicitKeys.Contains(key)));
            if (!explicitRestriction && !inferredRestriction) continue;
            ModelKey[] allowed = universe.Length == 0 ? explicitKeys : universe.Where(key =>
                (explicitKeys.Length == 0 || explicitKeys.Contains(key)) && eventAllowed.Contains(key)).ToArray();
            if (explicitKeys.Length > 0) rowEvidence.Add($"Act{act}:ExplicitVariantAlternatives;BossRowsRetainedByWAndExact");
            rows.Add(new(act, Freeze(explicitKeys), Freeze(eventAllowed), Freeze(allowed), Freeze(rowEvidence)));
            evidence.AddRange(rowEvidence);
        }
        return new(Freeze(rows), prefix, compiled.Context.WorldSnapshotFingerprint,
            Freeze(evidence.Append("OriginalQueryUnchanged;PositiveNecessaryMembershipOnly;NoFirstNToUnknownRoomInference")));
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}
