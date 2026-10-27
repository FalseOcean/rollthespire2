using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using RolltheSpire2.Ui.InRunConsole;

internal static partial class UiSurfaceChecks
{
    private static void CheckConsoleCatalogOrder(InRunConsolePanel panel, Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Static | BindingFlags.NonPublic;
        T[] Ordered<T>(string method) => ((IEnumerable<T>)panel.GetType().GetMethod(method, members)!.Invoke(null, null)!).ToArray();
        var relics = Ordered<RelicModel>("OrderedRelicCatalog");
        var potions = Ordered<PotionModel>("OrderedPotionCatalog");
        var allRelics = ModelDb.AllRelics.ToArray();
        var allPotions = ModelDb.AllPotions.ToArray();
        void Complete<T>(T[] actual, T[] available, string kind) where T : AbstractModel
        {
            check(actual.Length == available.Length && actual.Select(model => model.Id).Distinct().Count() == actual.Length &&
                actual.Select(model => model.Id).ToHashSet().SetEquals(available.Select(model => model.Id)),
                "encyclopedia-ordered " + kind + " retain the complete console catalog exactly once");
            check(actual.All(model => available.Any(candidate => ReferenceEquals(model, candidate))),
                "encyclopedia-ordered " + kind + " retain canonical catalog identities");
        }
        Complete(relics, allRelics, "relics"); Complete(potions, allPotions, "potions");
        RelicModel[] nativeStarters =
        [
            ModelDb.Relic<BurningBlood>(), ModelDb.Relic<RingOfTheSnake>(), ModelDb.Relic<DivineRight>(),
            ModelDb.Relic<BoundPhylactery>(), ModelDb.Relic<CrackedCore>(), ModelDb.Relic<BlackBlood>(),
            ModelDb.Relic<RingOfTheDrake>(), ModelDb.Relic<DivineDestiny>(), ModelDb.Relic<PhylacteryUnbound>(), ModelDb.Relic<InfusedCore>()
        ];
        check(relics.Take(nativeStarters.Length).Select(model => model.Id).SequenceEqual(nativeStarters.Select(model => model.Id)),
            "native starter rows follow Ironclad/Silent/Regent/Necrobinder/Defect then their upgraded starters");

        // Assert observable subsection properties instead of rebuilding the
        // producer's full expected output or relying only on rarity sorting.
        void Subsections<T>(T[] group, T[] available, IEnumerable<HashSet<ModelId>> pools,
            Func<T, string> title, string label) where T : AbstractModel
        {
            var poolIds = pools.ToArray();
            var characterIds = poolIds.SelectMany(ids => ids).ToHashSet();
            var neutral = group.Where(model => !characterIds.Contains(model.Id)).ToArray();
            check(group.Take(neutral.Length).SequenceEqual(neutral), "neutral entries precede character entries " + label);
            check(neutral.Zip(neutral.Skip(1)).All(pair => LocManager.Instance.StringComparer.Compare(title(pair.First), title(pair.Second)) <= 0),
                "neutral entries use the current native localized title comparer " + label);
            var groupIds = group.Select(model => model.Id).ToHashSet();
            var expectedCharacterIds = poolIds.SelectMany(ids => available.Where(model => groupIds.Contains(model.Id) && ids.Contains(model.Id)))
                .Select(model => model.Id).Distinct().ToArray();
            check(group.Skip(neutral.Length).Select(model => model.Id).SequenceEqual(expectedCharacterIds),
                "character subsection follows native character pools and original catalog order " + label);
        }
        foreach (var rarity in new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop, RelicRarity.Event })
            Subsections(relics.Where(model => model.Rarity == rarity).ToArray(), allRelics,
                ModelDb.AllCharacterRelicPools.Select(pool => pool.AllRelicIds.ToHashSet()),
                model => model.Title.GetFormattedText(), "relic " + rarity);
        foreach (var rarity in new[] { PotionRarity.Common, PotionRarity.Uncommon, PotionRarity.Rare })
            Subsections(potions.Where(model => model.Rarity == rarity).ToArray(), allPotions,
                ModelDb.AllCharacterPotionPools.Select(pool => pool.AllPotionIds.ToHashSet()),
                model => model.Title.GetFormattedText(), "potion " + rarity);
        var special = potions.Where(model => model.Rarity is PotionRarity.Event or PotionRarity.Token).ToArray();
        check(special.Any(model => model.Rarity == PotionRarity.Event) && special.Any(model => model.Rarity == PotionRarity.Token),
            "native special potion fixture includes both Event and Token");
        Subsections(special, allPotions, ModelDb.AllCharacterPotionPools.Select(pool => pool.AllPotionIds.ToHashSet()),
            model => model.Title.GetFormattedText(), "combined Event/Token special potions");
        int PotionGroup(PotionModel model) => model.Rarity switch
        { PotionRarity.Common => 0, PotionRarity.Uncommon => 1, PotionRarity.Rare => 2, PotionRarity.Event or PotionRarity.Token => 3, _ => 4 };
        check(potions.Zip(potions.Skip(1)).All(pair => PotionGroup(pair.First) <= PotionGroup(pair.Second)),
            "potion categories are Common/Uncommon/Rare/combined special/extras");
        check(potions.Where(model => PotionGroup(model) == 4).SequenceEqual(allPotions.Where(model => PotionGroup(model) == 4)),
            "potion entries outside encyclopedia groups retain their original catalog order");
        check(relics.Where(model => model.Rarity == RelicRarity.None).SequenceEqual(allRelics.Where(model => model.Rarity == RelicRarity.None)),
            "None/debug relics remain available in stable original catalog order");
    }
}
