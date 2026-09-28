using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Selectivity;

internal sealed record JointSelectivityWorldFixture(
    int Act,
    ModelKey VariantA,
    ModelKey VariantB,
    ModelKey UniqueEventA,
    IReadOnlyList<ModelKey> VariantABosses,
    ModelKey VariantASingleBoss,
    ModelKey VariantBUniqueBoss,
    IReadOnlyList<ModelKey> OtherVariantOnlyBosses,
    IReadOnlyList<ModelKey> BossMixtureTargets,
    string Evidence);

/// <summary>
/// Runtime-authority fixture discovery for the one-click Joint Selectivity oracle.
/// It does not manufacture a representative seed: it inspects the immutable catalog
/// and selection groups captured on the main thread.
/// </summary>
internal static class JointSelectivityFixtureDiscovery
{
    public static bool TryFindWorldFixture(
        SearchExecutionRequest basePlan,
        out JointSelectivityWorldFixture fixture,
        out string issue)
    {
        ArgumentNullException.ThrowIfNull(basePlan);
        fixture = default!;
        issue = string.Empty;
        Beta109WorldGenerationSnapshot? generation = basePlan.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null || !generation.ActSelectionAuthorityExact)
        {
            issue = "WorldActSelectionAuthorityUnavailable";
            return false;
        }

        foreach (Beta109ActSelectionGroupSnapshot group in generation.ActSelectionGroups
                     .Where(group => group.EligibilityAndOrderExact &&
                                     group.SelectionMode == Beta109ActSelectionMode.RandomNextItem &&
                                     group.EligibleActsInSourceOrder.Distinct(ModelKeyComparer.Instance).Count() >= 2)
                     .OrderBy(group => group.Act == 1 ? 0 : group.Act == 2 ? 1 : 2))
        {
            // Act1 may be deterministically overridden after its normal random draw. Such a
            // group is not an effective multi-variant mixture and is unsuitable for this oracle.
            if (group.Act == 1 &&
                (!generation.Act1OverrideExact ||
                 generation.Act1OverrideResolvedKey is ModelKey act1Override && act1Override.IsValid))
                continue;

            // Avoid the Act3 A10 two-Boss act-level predicate shape in this fixture.
            if (group.Act == 3 && basePlan.Ascension >= 10) continue;
            Beta109ActGenerationSnapshot[] variants = group.EligibleActsInSourceOrder
                .Distinct(ModelKeyComparer.Instance)
                .Select(key => generation.OrderedActCatalog.FirstOrDefault(item => item.Act == group.Act && item.ActKey == key))
                .Where(item => item is not null)
                .Cast<Beta109ActGenerationSnapshot>()
                .Where(item => item.HasExactGenerationInputs && item.EventRngConsumptionExact && item.Bosses.Count >= 2)
                .ToArray();
            if (variants.Length < 2) continue;

            var eligibleEvents = new Dictionary<ModelKey, HashSet<ModelKey>>(ModelKeyComparer.Instance);
            foreach (Beta109ActGenerationSnapshot variant in variants)
            {
                if (!TryStaticEligibleEventSet(generation, variant, out HashSet<ModelKey> set))
                {
                    eligibleEvents.Clear();
                    break;
                }
                eligibleEvents[variant.ActKey] = set;
            }
            if (eligibleEvents.Count != variants.Length) continue;

            foreach (Beta109ActGenerationSnapshot a in variants)
            {
                // SharedContinuation-Unknown oracle needs an Event marginal strictly below 1.
                // At least two distinct static-eligible event identities guarantees the unique
                // target does not occupy every eligible occurrence class.
                if (eligibleEvents[a.ActKey].Count < 2) continue;
                HashSet<ModelKey> othersEvents = variants.Where(v => v.ActKey != a.ActKey)
                    .SelectMany(v => eligibleEvents[v.ActKey])
                    .ToHashSet(ModelKeyComparer.Instance);
                ModelKey uniqueEvent = eligibleEvents[a.ActKey]
                    .Where(key => key.IsValid && !othersEvents.Contains(key))
                    .OrderBy(key => key.Serialized, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (!uniqueEvent.IsValid) continue;

                HashSet<ModelKey> aBosses = a.Bosses.Where(key => key.IsValid).ToHashSet(ModelKeyComparer.Instance);
                foreach (Beta109ActGenerationSnapshot b in variants.Where(v => v.ActKey != a.ActKey))
                {
                    ModelKey bUnique = b.Bosses
                        .Where(key => key.IsValid && !aBosses.Contains(key))
                        .OrderBy(key => key.Serialized, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (!bUnique.IsValid) continue;

                    ModelKey aSingle = aBosses.OrderBy(key => key.Serialized, StringComparer.Ordinal).First();
                    ModelKey[] otherOnlyBosses = variants.Where(v => v.ActKey != a.ActKey)
                        .SelectMany(v => v.Bosses)
                        .Where(key => key.IsValid && !aBosses.Contains(key))
                        .Distinct(ModelKeyComparer.Instance)
                        .OrderBy(key => key.Serialized, StringComparer.Ordinal)
                        .ToArray();
                    if (otherOnlyBosses.Length == 0) continue;

                    ModelKey[] mixtureTargets = new[] { aSingle, bUnique }
                        .Distinct(ModelKeyComparer.Instance)
                        .ToArray();
                    fixture = new JointSelectivityWorldFixture(
                        group.Act,
                        a.ActKey,
                        b.ActKey,
                        uniqueEvent,
                        aBosses.OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray(),
                        aSingle,
                        bUnique,
                        otherOnlyBosses,
                        mixtureTargets,
                        $"Act{group.Act};variants={variants.Length};variantA={a.ActKey.Serialized};variantB={b.ActKey.Serialized};uniqueEvent={uniqueEvent.Serialized};variantABosses={aBosses.Count};otherOnlyBosses={otherOnlyBosses.Length}");
                    return true;
                }
            }
        }

        issue = "NoExactMultiVariantUniqueEventBossFixture";
        return false;
    }

    private static bool TryStaticEligibleEventSet(
        Beta109WorldGenerationSnapshot generation,
        Beta109ActGenerationSnapshot variant,
        out HashSet<ModelKey> events)
    {
        events = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        if (!EventPoolSequenceProjector.TryPrepare(
                variant.Act,
                variant.ActKey,
                variant.EventRngConsumptionExact,
                variant.OrderedRawEvents,
                generation.EventAuthority.OrderedSharedEventsRaw,
                generation.EventAuthority,
                variant.OrderedEligibleEvents,
                out EventPoolSequenceProjector.PreparedAct? prepared,
                out _) || prepared is null)
            return false;

        foreach (EventPoolSequenceProjector.Candidate candidate in prepared.Candidates)
        {
            EventStaticEligibilityResult rule = EventStaticEligibilityCatalog.Evaluate(
                candidate.EventKey,
                variant.Act,
                generation.Profile,
                candidate.Source);
            if (!rule.ShouldReject) events.Add(candidate.EventKey);
        }
        return events.Count != 0;
    }
}
