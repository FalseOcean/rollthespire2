using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorAncientChecks
{
    internal static void Run(PredictorRun captured)
    {
        var catalog = captured.Context.Catalog ?? throw new InvalidOperationException("Ancient catalog missing");
        if (catalog.EventPetRelics.IsDefaultOrEmpty || catalog.StarterRelics.IsDefaultOrEmpty)
            throw new InvalidOperationException("Ancient pet/starter authority missing");

        // Player.HasEventPet inspects retained relics, including melted wax.
        long id = captured.Working.NextInstanceId;
        var meltedPet = captured.Working with { Relics = [
            new PredictorRelic(id, catalog.EventPetRelics[0], 1, Melted: true)] };
        if (PredictorRun.AncientConditions(captured.Context, meltedPet).PaelAllowLegionNoEventPet)
            throw new InvalidOperationException("Melted event pet incorrectly permits PaelsLegion");

        var starter = captured.Working with { Relics = [
            new PredictorRelic(id, catalog.StarterRelics[0], 1, Melted: true)] };
        if (!PredictorRun.AncientConditions(captured.Context, starter).OrobasTouchOfOrobasConditionMet)
            throw new InvalidOperationException("Melted starter incorrectly disables TouchOfOrobas setup");

        var removable = captured.Working.Deck.Select(captured.Working.Card)
            .Where(c => !PredictorCardLevelEffects.Resolve(captured.Context, c).Eternal).Take(5).ToArray();
        if (removable.Length != 5) throw new InvalidOperationException("Ancient removable fixture missing");
        var five = captured.Working with { Deck = removable.Select(c => c.Id).ToImmutableArray() };
        if (!PredictorRun.AncientConditions(captured.Context, five).PaelRemovableCardsAtLeast5)
            throw new InvalidOperationException("Five removable cards should permit PaelsTooth");
        var first = removable[0];
        five = five.WithCard(first with { Enchantment = new ModelKey("ENCHANTMENT", "TEZCATARAS_EMBER"),
            EnchantmentAmount = 1, PermanentCostOverride = 0 });
        if (PredictorRun.AncientConditions(captured.Context, five).PaelRemovableCardsAtLeast5)
            throw new InvalidOperationException("Eternal enchanted card incorrectly counts as removable");
    }
}
