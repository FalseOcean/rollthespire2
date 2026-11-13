using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorCardSortValues(int Energy, int ReplayCount, ImmutableSortedDictionary<string, decimal> Variables);

internal sealed record PredictorObservedCardSort(int UpgradeLevel, int? PermanentCostOverride, int IncreasedDamage,
    int IncreasedBlock, PredictorCardSortValues Values);

internal static class PredictorCardStableOrder
{
    // The native StableShuffle sorts with List<T>.Sort before consuming RNG.
    internal static void Sort(List<PredictorCard> cards, PredictorContext? context) =>
        cards.Sort((left, right) => Compare(left, right, context));

    private static int Compare(PredictorCard left, PredictorCard right, PredictorContext? context)
    {
        int order = CompareKeys(left.Key, right.Key);
        if (order == 0) order = left.UpgradeLevel.CompareTo(right.UpgradeLevel);
        if (order != 0 || context?.UsesNonCombatGeneration != true) return order;

        order = CompareKeys(left.Enchantment, right.Enchantment);
        if (order == 0) order = CompareKeys(left.Affliction, right.Affliction);
        if (order != 0) return order;

        var a = Values(left, context);
        var b = Values(right, context);
        order = a.Energy.CompareTo(b.Energy);
        if (order == 0) order = a.ReplayCount.CompareTo(b.ReplayCount);
        if (order != 0) return order;
        foreach (var (key, value) in a.Variables)
            if (b.Variables.TryGetValue(key, out decimal other) && (order = value.CompareTo(other)) != 0)
                return order;
        return 0;
    }

    private static PredictorCardSortValues Values(PredictorCard card, PredictorContext context)
    {
        var observed = card.ObservedSort;
        bool invariantLevel = PredictorCardLevelEffects.HasInvariantLevelMetadata(card.Key);
        var definition = context.Catalog?.CardDefinitions.FirstOrDefault(candidate => candidate.Prototype.Key == card.Key);
        var level = definition?.Levels.FirstOrDefault(metadata => metadata.UpgradeLevel == (invariantLevel ? 0 : card.UpgradeLevel));
        bool sameLevel = observed?.UpgradeLevel == card.UpgradeLevel;
        var values = (sameLevel || invariantLevel) && observed is not null ? observed.Values : level?.SortValues ?? observed?.Values;
        var variables = values?.Variables ?? ImmutableSortedDictionary.Create<string, decimal>(StringComparer.Ordinal);
        if (card.Key.Entry == "GENETIC_ALGORITHM" && variables.TryGetValue("Block", out var block))
            variables = variables.SetItem("Block", block + card.IncreasedBlock - (sameLevel ? observed!.IncreasedBlock : 0));
        if (card.Key.Entry == "THE_SCYTHE" && variables.TryGetValue("Damage", out var damage))
            variables = variables.SetItem("Damage", damage + card.IncreasedDamage - (sameLevel ? observed!.IncreasedDamage : 0));
        if (invariantLevel && !sameLevel && variables.TryGetValue("Damage", out var daggerDamage))
        {
            // Dingy Dagger adds 3 + the new level each time. Advance the
            // observed damage directly, including when the captured level is huge.
            decimal from = observed?.UpgradeLevel ?? 0;
            decimal to = card.UpgradeLevel;
            variables = variables.SetItem("Damage", daggerDamage + 3 * (to - from) + (to * (to + 1) - from * (from + 1)) / 2);
        }
        int energy = sameLevel && observed!.PermanentCostOverride == card.PermanentCostOverride ? observed.Values.Energy :
            level?.CostsX == true ? values?.Energy ?? 0 : Math.Max(0, card.PermanentCostOverride ?? values?.Energy ?? level?.LocalCost ?? 0);
        return new(energy, observed?.Values.ReplayCount ?? values?.ReplayCount ?? 0, variables);
    }

    private static int CompareKeys(ModelKey? a, ModelKey? b)
    {
        if (a is not { } left) return b.HasValue ? -1 : 0;
        if (b is not { } right) return 1;
        int category = string.CompareOrdinal(left.Category, right.Category);
        return category == 0 ? string.CompareOrdinal(left.Entry, right.Entry) : category;
    }
}
