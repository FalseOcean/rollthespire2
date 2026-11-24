using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.World.Beta109;

/// <summary>
/// RT2-owned, DTO-only replay of the source GrabBag encounter selection shape.
/// It samples the complete current bag, preserves rejected entries, and performs
/// the source fallback draw only when the predicate has no acceptable item.
/// </summary>
public sealed class Beta109EncounterGrabBag
{
    private readonly List<Beta109EncounterEntrySnapshot> _items;

    public Beta109EncounterGrabBag(IEnumerable<Beta109EncounterEntrySnapshot> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _items = source.ToList();
    }

    public int Count => _items.Count;

    public Beta109EncounterGrabResult GrabAndRemove(
        Beta109WorldRng rng,
        Func<Beta109EncounterEntrySnapshot, bool>? predicate,
        string stage,
        WorldRngConsumptionShape consumptionShape)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);

        if (predicate is not null && !_items.Any(predicate))
        {
            return new Beta109EncounterGrabResult(
                Item: null,
                RejectedDraws: 0,
                PredicateHadEligibleItem: false,
                ConsumedDraws: 0);
        }

        int rejected = 0;
        int attempt = 0;
        while (true)
        {
            int index = GrabIndex(
                rng,
                $"{stage}:attempt{attempt++}",
                consumptionShape);
            if (index < 0)
            {
                return new Beta109EncounterGrabResult(
                    Item: null,
                    RejectedDraws: rejected,
                    PredicateHadEligibleItem: predicate is null || _items.Any(predicate),
                    ConsumedDraws: rejected + 1);
            }

            Beta109EncounterEntrySnapshot candidate = _items[index];
            rng.AnnotateLastSelection(candidate.EncounterKey);
            if (predicate is null || predicate(candidate))
            {
                _items.RemoveAt(index);
                return new Beta109EncounterGrabResult(
                    candidate,
                    rejected,
                    PredicateHadEligibleItem: true,
                    ConsumedDraws: rejected + 1);
            }

            rejected++;
        }
    }

    private int GrabIndex(
        Beta109WorldRng rng,
        string stage,
        WorldRngConsumptionShape consumptionShape)
    {
        double target = rng.NextDouble(stage, consumptionShape) * _items.Count;
        if (_items.Count == 0)
        {
            return -1;
        }

        double cumulative = 0d;
        for (int index = 0; index < _items.Count; index++)
        {
            cumulative += 1d;
            if (target < cumulative)
            {
                return index;
            }
        }

        return _items.Count - 1;
    }
}


public static class Beta109EncounterReplayRules
{
    public static bool DoesNotRepeat(
        Beta109EncounterEntrySnapshot candidate,
        Beta109EncounterEntrySnapshot? previous)
    {
        if (previous is null) return true;

        if (candidate.ReferenceIdentityExact &&
            previous.ReferenceIdentityExact &&
            candidate.ReferenceIdentityId >= 0 &&
            candidate.ReferenceIdentityId == previous.ReferenceIdentityId)
        {
            return false;
        }

        if (candidate.TagComparisonExact &&
            previous.SourceOrdinal >= 0 &&
            candidate.ExactTagConflictSourceOrdinals.Contains(previous.SourceOrdinal))
        {
            return false;
        }

        // Exact production replay must never depend on this fallback. It remains
        // available only for explicitly partial/synthetic diagnostics.
        if (!candidate.TagComparisonExact || !previous.TagComparisonExact)
        {
            if (candidate.EncounterKey == previous.EncounterKey) return false;
            if (candidate.Tags.Count == 0 || previous.Tags.Count == 0) return true;
            var priorTags = new HashSet<string>(previous.Tags, StringComparer.Ordinal);
            return !candidate.Tags.Any(priorTags.Contains);
        }

        return true;
    }
}

public sealed record Beta109EncounterGrabResult(
    Beta109EncounterEntrySnapshot? Item,
    int RejectedDraws,
    bool PredicateHadEligibleItem,
    int ConsumedDraws);
