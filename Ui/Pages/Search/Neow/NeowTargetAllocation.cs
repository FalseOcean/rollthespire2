using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

// The three Bones result slots are small enough to enumerate legal assignments.
// Targets are occurrences, so repeated transform cards are never deduplicated.
internal static class NeowTargetAllocation
{
    internal static ModelKey?[]? Reconcile(IReadOnlyList<ModelKey?> previous,
        IReadOnlyList<ModelKey> targets, IReadOnlyList<IReadOnlyList<ModelKey>> pools)
    {
        if (previous.Count != pools.Count || previous.Count > 3 || targets.Count > previous.Count)
            throw new ArgumentException("NeowTargetAllocation.InvalidSlotCount");
        ModelKey?[] current = new ModelKey?[previous.Count];
        ModelKey?[]? best = null;
        int bestRetained = -1;
        void Assign(int slot, int used, int retained)
        {
            if (slot == current.Length)
            {
                if (used == (1 << targets.Count) - 1 && retained > bestRetained)
                { best = current.ToArray(); bestRetained = retained; }
                return;
            }
            for (int i = 0; i < targets.Count; i++)
            {
                if ((used & (1 << i)) != 0 || !pools[slot].Contains(targets[i])) continue;
                current[slot] = targets[i];
                Assign(slot + 1, used | (1 << i), retained + (previous[slot] == targets[i] ? 1 : 0));
            }
            current[slot] = null;
            Assign(slot + 1, used, retained);
        }
        Assign(0, 0, 0);
        return best;
    }
}
