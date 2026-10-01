using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorCardRewardRelicHooks
{
    // One late listener at a time, in inventory order. CardReward's live
    // RelicObtained subscription calls the same listener for already open
    // options, after the newly acquired relic has joined the inventory.
    internal static (PredictorState State, List<PredictorCard> Cards) ApplyLate(
        PredictorContext context, PredictorState entry, List<PredictorCard> cards,
        ModelKey relic, bool cloneOnChange, long? relicId = null)
    {
        var state = entry;
        switch (relic.Entry)
        {
            case "WING_CHARM":
            {
                var eligible = Enumerable.Range(0, cards.Count)
                    .Where(i => PredictorObtainSources.CanEnchant(context, cards[i])).ToArray();
                if (eligible.Length == 0) break;
                (int selected, state) = state.Draw(PredictorStream.Niche, 0, eligible.Length);
                int index = eligible[selected];
                cards[index] = CloneIfNeeded(state, cards[index] with
                { Enchantment = new("ENCHANTMENT", "SWIFT"), EnchantmentAmount = 1 }, cloneOnChange, out state);
                break;
            }
            case "FRESNEL_LENS":
            {
                for (int index = 0; index < cards.Count; index++)
                    if (PredictorObtainSources.GainsBlockForFresnel(context, cards[index]) &&
                        PredictorObtainSources.CanEnchant(context, cards[index]))
                        cards[index] = CloneIfNeeded(state, cards[index] with
                        { Enchantment = new("ENCHANTMENT", "NIMBLE"), EnchantmentAmount = 2 }, cloneOnChange, out state);
                break;
            }
            case "GLITTER":
                for (int index = 0; index < cards.Count; index++)
                    if (PredictorObtainSources.CanEnchant(context, cards[index]))
                        cards[index] = CloneIfNeeded(state, cards[index] with
                        { Enchantment = new("ENCHANTMENT", "GLAM"), EnchantmentAmount = 1 }, cloneOnChange, out state);
                break;
            case "LAVA_LAMP":
            {
                if (!state.CurrentRoomIsCombat) break;
                if (relicId is not long id) throw new InvalidOperationException("PredictorLavaLampInstanceMissing");
                var lamp = state.Relics.Single(r => r.Id == id && r.Key == relic);
                if (lamp.TookDamageThisCombat) break;
                for (int index = 0; index < cards.Count; index++)
                    if (cards[index].UpgradeLevel < cards[index].MaxUpgradeLevel)
                        cards[index] = CloneIfNeeded(state, cards[index] with
                        { UpgradeLevel = cards[index].UpgradeLevel + 1 }, cloneOnChange, out state);
                break;
            }
        }
        return (state, cards);
    }

    private static PredictorCard CloneIfNeeded(PredictorState entry, PredictorCard card,
        bool clone, out PredictorState state)
    {
        if (!clone) { state = entry; return card; }
        state = entry with { NextInstanceId = entry.NextInstanceId + 1 };
        return card with { Id = entry.NextInstanceId };
    }
}
