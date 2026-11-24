using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalExplorer
{
    // Necessary resource and offer-variant bound. Which relics come from the
    // board remains optimistic until ordered endpoint settlement.
    private bool WingMaySupport(Position node, CrystalRewardOption? probe)
    {
        var goals = probe == null ? _selected : _selected.Add(probe);
        if (goals.IsEmpty) return true;
        int left = _source.Remaining - node.Path.Length;
        var slots = new List<(string Kind, UInt128 Need)>();
        foreach (int item in node.Order) slots.Add((_source.Items[item].Kind, 0));
        for (int i = 0; i < _items.Length; i++)
        {
            UInt128 need = node.Fog & _items[i]; var item = _source.Items[i];
            if (need == 0 || item.Kind == "CURSE" || !CanCover(need, left)) continue;
            for (int n = 0; n < item.Subscriptions; n++) slots.Add((item.Kind, need));
        }
        int cardSlots = slots.Count(s => s.Kind.StartsWith("CARD_", StringComparison.Ordinal));
        var requested = goals.Where(g => g.Kind == PredictorRewardKind.Card || g.Kind == PredictorRewardKind.Potion).ToArray();
        int cardGoals = requested.Count(g => g.Kind == PredictorRewardKind.Card);
        if (cardGoals > cardSlots) return false;
        int relicGoals = goals.Where(g => g.Kind == PredictorRewardKind.Relic && g.Key.IsValid).Select(g => g.Key).Distinct().Count();
        if (relicGoals == 0 && goals.Any(g => g.Kind == PredictorRewardKind.Relic)) relicGoals = 1;
        if (relicGoals > slots.Count(s => s.Kind == "RELIC") + (cardSlots - cardGoals + PredictorCrystalWing.Parity(_source.State)) / 2)
            return false;
        var edges = new int[requested.Length][]; UInt128 required = 0;
        for (int g = 0; g < requested.Length; g++)
        {
            var goal = requested[g]; var matches = new List<int>(); UInt128 intersection = UInt128.MaxValue;
            for (int s = 0; s < slots.Count; s++)
            {
                var slot = slots[s];
                bool matchesGoal = goal.Kind == PredictorRewardKind.Card
                    ? slot.Kind.StartsWith("CARD_", StringComparison.Ordinal) && !_reachability!.CardOffsets(slot.Kind, goal).IsZero
                    : slot.Kind.StartsWith("POTION_", StringComparison.Ordinal) && !_reachability!.PotionOffsets(slot.Kind, goal.Key).IsZero;
                if (matchesGoal) { matches.Add(s); intersection &= slot.Need; }
            }
            if (matches.Count == 0) return false;
            edges[g] = matches.ToArray(); required |= intersection;
        }
        if (!CanCover(required, left)) return false;
        var owners = Enumerable.Repeat(-1, slots.Count).ToArray();
        bool Assign(int goal, bool[] seen)
        {
            foreach (int slot in edges[goal]) if (!seen[slot])
            { seen[slot] = true; if (owners[slot] < 0 || Assign(owners[slot], seen)) { owners[slot] = goal; return true; } }
            return false;
        }
        return Enumerable.Range(0, requested.Length).All(g => Assign(g, new bool[slots.Count]));
    }
}
