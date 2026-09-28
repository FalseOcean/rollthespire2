namespace RolltheSpire2.Core.Effects.Snapshots;

/// <summary>
/// Pure validation helpers for immutable effect snapshots. Reward generation excludes
/// already offered cards by InstanceId, so IDs must be non-empty and unique within
/// every ordered source pool.
/// </summary>
public static class NeowEffectSnapshotInvariants
{
    public static bool HasUniqueCardInstanceIds(IEnumerable<NeowEffectCardSnapshot>? cards)
    {
        if (cards is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (NeowEffectCardSnapshot card in cards)
        {
            if (string.IsNullOrWhiteSpace(card.InstanceId) || !seen.Add(card.InstanceId))
            {
                return false;
            }
        }

        return true;
    }

    public static int CountUniqueCardInstanceIds(IEnumerable<NeowEffectCardSnapshot>? cards)
    {
        if (cards is null)
        {
            return -1;
        }

        return cards
            .Where(card => !string.IsNullOrWhiteSpace(card.InstanceId))
            .Select(card => card.InstanceId)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }
}
