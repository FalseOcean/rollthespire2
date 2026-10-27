using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Settings;

internal sealed record OperationPredictionEntry(string Id, OperationPredictionCategory Category,
    ModelKey? Source = null, bool IsSupplement = false, string? NameKey = null);

// Product-owned inventory of existing operation previews, independent of RF.
// Sources follow admitted event scenes and the hidden outputs of relic obtains,
// card-reward hooks and rest effects. Public-only obtain text is not a preview.
internal static class OperationPredictionCatalog
{
    internal static readonly ImmutableArray<OperationPredictionEntry> Entries = Build();
    internal static readonly ImmutableDictionary<string, OperationPredictionEntry> ById =
        Entries.ToImmutableDictionary(entry => entry.Id, StringComparer.Ordinal);
    internal static IEnumerable<OperationPredictionEntry> ForCategory(OperationPredictionCategory category) =>
        Entries.Where(entry => entry.Category == category);
    internal static string ForSource(ModelKey source) => source.Category.ToLowerInvariant() + ":" + source.Entry;

    private static ImmutableArray<OperationPredictionEntry> Build()
    {
        var entries = ImmutableArray.CreateBuilder<OperationPredictionEntry>();
        void Named(string id, OperationPredictionCategory category, bool supplement = false) =>
            entries.Add(new(id, category, IsSupplement: supplement, NameKey: "settings.operation.entry." + id));
        void Sources(string kind, OperationPredictionCategory category, params string[] sources)
        {
            foreach (string source in sources)
            {
                var key = new ModelKey(kind, source);
                entries.Add(new(ForSource(key), category, key));
            }
        }
        Named("reward:reroll", OperationPredictionCategory.RewardReroll);
        Named("extra:reroll-sequence", OperationPredictionCategory.RewardReroll, true);
        Named("extra:philosopher-order", OperationPredictionCategory.RewardReroll, true);
        Named("reward:sacrifice", OperationPredictionCategory.RewardSacrifice);
        Named("extra:sacrifice-sequence", OperationPredictionCategory.RewardSacrifice, true);
        Named("shop:restock", OperationPredictionCategory.ShopRestock);
        Named("extra:conditional-restock", OperationPredictionCategory.ShopRestock, true);
        Named("extra:restock-whetstone", OperationPredictionCategory.ShopRestock, true);
        Sources("RELIC", OperationPredictionCategory.CardTransform,
            "NEW_LEAF", "ASTROLABE", "PANDORAS_BOX", "LEAFY_POULTICE");
        Named("extra:transform-effects", OperationPredictionCategory.CardTransform, true);
        Sources("RELIC", OperationPredictionCategory.RelicEffect,
            "ALCHEMICAL_COFFER", "ARCANE_SCROLL", "BEAUTIFUL_BRACELET", "CALLING_BELL", "CAULDRON",
            "DISTINGUISHED_CAPE", "FRAGRANT_MUSHROOM", "GLASS_EYE", "HEFTY_TABLET", "KALEIDOSCOPE",
            "LARGE_CAPSULE", "LEAD_PAPERWEIGHT", "LOST_COFFER", "MASSIVE_SCROLL", "NEOWS_BONES",
            "ORRERY", "PHIAL_HOLSTER", "SAND_CASTLE", "SCROLL_BOXES", "SEA_GLASS", "SMALL_CAPSULE",
            "TOY_BOX", "WAR_PAINT", "WHETSTONE", "SILKEN_TRESS", "WING_CHARM", "FRESNEL_LENS",
            "GLITTER", "LAVA_LAMP", "MOLTEN_EGG", "TOXIC_EGG", "FROZEN_EGG", "SILVER_CRUCIBLE",
            "DREAM_CATCHER", "TINY_MAILBOX", "SHOVEL");
        Named("extra:obtain-chain", OperationPredictionCategory.RelicEffect, true);
        Sources("EVENT", OperationPredictionCategory.EventOption,
            "BRAIN_LEECH", "COLORFUL_PHILOSOPHERS", "INFESTED_AUTOMATON", "ROOM_FULL_OF_CHEESE",
            "THE_FUTURE_OF_POTIONS", "TINKER_TIME", "DOLL_ROOM", "LUMINOUS_CHOIR", "POTION_COURIER",
            "RANWID_THE_ELDER", "ROUND_TEA_PARTY", "THE_LEGENDS_WERE_TRUE", "THIS_OR_THAT", "TRASH_HEAP",
            "UNREST_SITE", "WAR_HISTORIAN_REPY", "WELLSPRING", "DOORS_OF_LIGHT_AND_DARK", "REFLECTIONS",
            "TABLET_OF_TRUTH", "SLIPPERY_BRIDGE", "AROMA_OF_CHAOS", "MORPHIC_GROVE", "SYMBIOTE",
            "WHISPERING_HOLLOW", "ENDLESS_CONVEYOR", "TRIAL", "WELCOME_TO_WONGOS", "DENSE_VEGETATION",
            "BATTLEWORN_DUMMY", "PUNCH_OFF", "RELIC_TRADER", "HUNGRY_FOR_MUSHROOMS", "THE_LANTERN_KEY");
        Named("extra:event-continuation", OperationPredictionCategory.EventOption, true);
        Named("extra:event-combat", OperationPredictionCategory.EventOption, true);
        Sources("POTION", OperationPredictionCategory.PotionResult,
            "ATTACK_POTION", "SKILL_POTION", "POWER_POTION", "COLORLESS_POTION", "COSMIC_CONCOCTION", "OROBIC_ACID", "ENTROPIC_BREW");
        Named("extra:potion-order", OperationPredictionCategory.PotionResult, true);
        Named("extra:potion-inventory", OperationPredictionCategory.PotionResult, true);
        return entries.ToImmutable();
    }
}
