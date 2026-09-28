using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

/// <summary>
/// Authoritative player-facing sequence semantics for Normal Merchant sequence UI.
/// Ordered means positional slot matching: every non-empty authored slot is bound
/// to that exact Merchant ordinal. Empty slots are positional wildcards.
/// Unordered means target multiplicity within the first N Merchants; authored slot
/// positions have no meaning in that mode.
/// </summary>
internal static class ShopSequenceSemantics
{
    public static bool Matches(
        IReadOnlyList<ModelKey> actual,
        int count,
        CombatRewardSequenceOrderMode orderMode,
        IReadOnlyList<ModelKey?> slots)
    {
        if (count < 0 || actual.Count < count || slots.Count < count) return false;

        if (orderMode == CombatRewardSequenceOrderMode.Ordered)
        {
            for (int index = 0; index < count; index++)
            {
                ModelKey? target = slots[index];
                if (target.HasValue && actual[index] != target.Value) return false;
            }
            return true;
        }

        if (orderMode != CombatRewardSequenceOrderMode.Unordered) return false;
        for (int index = 0; index < count; index++)
        {
            ModelKey? target = slots[index];
            if (!target.HasValue) continue;
            int requested = 0;
            int available = 0;
            for (int probe = 0; probe < count; probe++)
            {
                if (slots[probe] == target) requested++;
                if (actual[probe] == target.Value) available++;
            }
            if (available < requested) return false;
        }
        return true;
    }

    public static bool MatchesDense(
        ReadOnlySpan<ushort> actual,
        int count,
        bool ordered,
        ReadOnlySpan<ushort> targets,
        ushort neutral)
    {
        if (count < 0 || actual.Length < count || targets.Length < count) return false;

        if (ordered)
        {
            for (int index = 0; index < count; index++)
            {
                ushort target = targets[index];
                if (target != neutral && actual[index] != target) return false;
            }
            return true;
        }

        for (int index = 0; index < count; index++)
        {
            ushort target = targets[index];
            if (target == neutral) continue;
            int requested = 0;
            int available = 0;
            for (int probe = 0; probe < count; probe++)
            {
                if (targets[probe] == target) requested++;
                if (actual[probe] == target) available++;
            }
            if (available < requested) return false;
        }
        return true;
    }
}
