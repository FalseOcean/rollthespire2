using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace RolltheSpire2.Ui.InRunConsole;

internal sealed partial class InRunConsolePanel
{
    private static IEnumerable<RelicModel> OrderedRelicCatalog()
    {
        var all = ModelDb.AllRelics.ToArray();
        var remaining = all.ToDictionary(model => model.Id);
        foreach (var model in EncyclopediaOrder())
            if (remaining.Remove(model.Id, out var available)) yield return available;
        // The console also exposes objects absent from the encyclopedia (None,
        // debug and additional content). Preserve their original catalog order.
        foreach (var model in all)
            if (remaining.Remove(model.Id)) yield return model;

        IEnumerable<RelicModel> EncyclopediaOrder()
        {
            // NRelicCollectionCategory.LoadRelics: two starter subcategories.
            var starters = ModelDb.AllCharacters.SelectMany(character => character.StartingRelics).ToArray();
            foreach (var starter in starters) yield return starter;
            var orobas = ModelDb.Relic<TouchOfOrobas>();
            foreach (var starter in starters)
            {
                var upgraded = orobas.GetUpgradedStarterRelic(starter);
                // The native method returns a mutable Circlet for an unknown
                // starter. That is not an upgraded starter catalog entry.
                if (upgraded.Rarity == RelicRarity.Starter) yield return upgraded;
            }
            foreach (var rarity in new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop })
                foreach (var model in OrdinaryCategory(rarity)) yield return model;
            var ancients = ModelDb.Acts.SelectMany(act => act.AllAncients).Concat(ModelDb.AllSharedAncients).Distinct();
            var ancientIds = all.Where(model => model.Rarity == RelicRarity.Ancient).Select(model => model.Id).ToHashSet();
            foreach (var ancient in ancients)
                foreach (var relic in ancient.AllPossibleOptions.Select(option => option.Relic?.CanonicalInstance).OfType<RelicModel>()
                    .Where(model => ancientIds.Contains(model.Id)).DistinctBy(model => model.Id)
                    .OrderBy(model => model.Title.GetFormattedText(), LocManager.Instance.StringComparer))
                    yield return relic;
            foreach (var model in OrdinaryCategory(RelicRarity.Event)) yield return model;
        }

        IEnumerable<RelicModel> OrdinaryCategory(RelicRarity rarity)
        {
            var category = all.Where(model => model.Rarity == rarity).ToArray();
            var characterItems = ModelDb.AllCharacterRelicPools.SelectMany(pool =>
                category.Where(model => pool.AllRelicIds.Contains(model.Id))).ToArray();
            var characterIds = characterItems.Select(model => model.Id).ToHashSet();
            // Native sorts only the neutral subsection; character pool order and
            // the catalog's ID order inside each pool remain unchanged.
            return category.Where(model => !characterIds.Contains(model.Id))
                .OrderBy(model => model.Title.GetFormattedText(), LocManager.Instance.StringComparer).Concat(characterItems);
        }
    }

    private static IEnumerable<PotionModel> OrderedPotionCatalog()
    {
        var all = ModelDb.AllPotions.ToArray();
        var remaining = all.ToDictionary(model => model.Id);
        foreach (var group in new[]
            {
                new[] { PotionRarity.Common }, new[] { PotionRarity.Uncommon }, new[] { PotionRarity.Rare },
                new[] { PotionRarity.Event, PotionRarity.Token }
            })
        {
            // NPotionLabCategory.LoadPotions combines Event and Token before
            // sorting: its neutral special entries can interleave both rarities.
            var category = all.Where(model => group.Contains(model.Rarity)).ToArray();
            var characterItems = ModelDb.AllCharacterPotionPools.SelectMany(pool =>
                category.Where(model => pool.AllPotionIds.Contains(model.Id))).ToArray();
            var characterIds = characterItems.Select(model => model.Id).ToHashSet();
            var ordered = category.Where(model => !characterIds.Contains(model.Id))
                .OrderBy(model => model.Title.GetFormattedText(), LocManager.Instance.StringComparer).Concat(characterItems);
            foreach (var model in ordered)
                if (remaining.Remove(model.Id, out var available)) yield return available;
        }
        foreach (var model in all)
            if (remaining.Remove(model.Id)) yield return model;
    }
}
