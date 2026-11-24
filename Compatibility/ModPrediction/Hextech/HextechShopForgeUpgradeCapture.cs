using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Infrastructure.Snapshots;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

internal static class HextechShopForgeUpgradeCapture
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Static | BindingFlags.Instance;
    private static string? _lastFault;

    internal static ImmutableArray<HextechShopForgeUpgradePlan> Capture(Player player, Assembly assembly,
        HextechShopForgeSnapshot snapshot)
    {
        var choices = HextechShopForgePrediction.PredictCurrent(snapshot);
        var relevant = choices.DirectGrant is { } granted ? new[] { granted } : choices.Options.ToArray();
        var plans = ImmutableArray.CreateBuilder<HextechShopForgeUpgradePlan>();
        foreach (var key in relevant)
        {
            var canonical = ModelDb.GetById<RelicModel>(new ModelId(key.Category, key.Entry));
            var type = canonical.GetType();
            if (type.Assembly != assembly || type.FullName is not
                ("HextechRunes.UpgradeForge" or "HextechRunes.GoldUpgradeForge")) continue;
            string salt = type.Name == "UpgradeForge" ? "silver-upgrade-forge" : "gold-upgrade-forge";
            try
            {
                var owned = player.Relics.FirstOrDefault(r => r.GetType() == type);
                // Stacking calls AfterObtained on the existing forge; its Cards
                // variable is used directly and is never multiplied by stacks.
                int count = (owned ?? canonical).DynamicVars.Cards.IntValue;
                // Audited native query is read-only and includes the actual
                // death/network-local ownership rule. One copy per rune instance,
                // not its displayed stack count and not an exponential chain.
                var vision = assembly.GetType("HextechRunes.DoubleVisionRune", true)!;
                var active = vision.GetMethod("GetActiveRunes", Members, null, [typeof(Player)], null)
                    ?? throw new MissingMethodException(vision.FullName, "GetActiveRunes");
                int grants = checked(1 + ((IEnumerable)active.Invoke(null, [player])!).Cast<object>().Count());
                var deck = player.Deck.Cards.Select(card => CaptureCard(card, grants)).ToImmutableArray();
                plans.Add(new(key, salt, count, deck)
                { OwnedStackCount = owned?.StackCount ?? 0, GrantCount = grants });
            }
            catch (Exception error)
            {
                RecordFault(key, error);
                plans.Add(new(key, salt, 0, [], "HextechForgeUpgradeCaptureUnavailable"));
            }
        }
        return plans.ToImmutable();
    }

    private static HextechShopForgeDeckCard CaptureCard(CardModel card, int grants)
    {
        // Reuse the established operation card projection. A tiny local catalog
        // below supplies only the canonical local-cost comparisons it needs.
        var emptyCatalog = new PredictorCatalog([], []);
        var source = PredictorLiveRunCapture.Card(card, emptyCatalog) with
        { HasEtherealKeyword = card.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal) };
        if (!card.IsUpgradable) return new(source, []);
        if (card.GetType().Assembly != typeof(CardModel).Assembly ||
            card.Enchantment is { } enchantment && enchantment.GetType().Assembly != typeof(CardModel).Assembly)
            return new(source, [], "HextechForgeUpgradeCardNotAudited");
        try
        {
            // Serialization creates a separate, unattached native card. No
            // clone-for-player, CardCmd, actual upgrade or run attachment occurs.
            var model = CardModel.FromSerializable(card.ToSerializable());
            var baseline = CardModel.FromSerializable(new SerializableCard
            { Id = card.Id, CurrentUpgradeLevel = source.UpgradeLevel });
            if (model.RunState != null || model.CombatState != null)
                throw new InvalidOperationException("HextechForgeUpgradeCopyMustBeDetached");
            foreach (var keyword in Enum.GetValues<CardKeyword>())
            {
                bool actual = card.GetKeywordsWithSources(KeywordSources.Local).Contains(keyword);
                bool copied = model.GetKeywordsWithSources(KeywordSources.Local).Contains(keyword);
                if (actual && !copied) model.AddKeyword(keyword);
                else if (!actual && copied) model.RemoveKeyword(keyword);
            }
            source = Project(card, baseline, source.Id, source.MaxUpgradeLevel);
            // Custom permanent base cost is not in SerializableCard. Preserve
            // it on the detached producer before OnUpgrade changes that cost.
            if (source.PermanentCostOverride is { } customCost)
                model.EnergyCost.SetCustomBaseCost(customCost);
            var levels = ImmutableArray.CreateBuilder<PredictorCard>();
            // A card can be selected once per obtain, including each actual
            // Double Vision copy. Project only the levels this purchase can reach.
            for (int grant = 0; grant < grants && model.CurrentUpgradeLevel < source.MaxUpgradeLevel; grant++)
            {
                model = AdvanceDetached(model);
                baseline = AdvanceDetached(baseline);
                // Hextech starter max-level uses the owned player's rune. The
                // copy is deliberately unowned; preserve the captured owned cap
                // (999, or the current level once already at/above that cap).
                levels.Add(Project(model, baseline, source.Id, source.MaxUpgradeLevel));
            }
            return new(source, levels.ToImmutable());
        }
        catch (Exception error)
        {
            RecordFault(source.Key, error);
            return new(source, [], "HextechForgeUpgradeCardUnavailable");
        }
    }

    private static PredictorCard Project(CardModel model, CardModel baseline, long id, int max)
    {
        int cost = (int)(baseline.EnergyCost.GetType().GetField("_base", Members)?.GetValue(baseline.EnergyCost)
            ?? throw new MissingFieldException(baseline.EnergyCost.GetType().FullName, "_base"));
        var provisional = PredictorLiveRunCapture.Card(model, new([], []));
        var catalog = new PredictorCatalog([], [])
        {
            CardDefinitions = [new(provisional, "", default, false, false, false)
            { Levels = [new(model.CurrentUpgradeLevel, cost, false, false, false, false, false, false)] }]
        };
        return PredictorLiveRunCapture.Card(model, catalog) with
        {
            Id = id, MaxUpgradeLevel = max,
            HasEtherealKeyword = model.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal)
        };
    }

    private static CardModel AdvanceDetached(CardModel model)
    {
        if (model.CurrentUpgradeLevel < model.MaxUpgradeLevel)
        {
            model.UpgradeInternal(); model.FinalizeUpgradeInternal();
            return model;
        }
        // Native basic strikes/defends have a player-rune cap but an unowned
        // cap of one. The mod's official deserialization scope supports their
        // saved higher levels; replay through that path without assigning an
        // owner or temporarily changing any game-wide rule.
        if (model.GetType().Assembly != typeof(CardModel).Assembly || model.Rarity != CardRarity.Basic ||
            !model.Tags.Contains(CardTag.Strike) && !model.Tags.Contains(CardTag.Defend))
            throw new NotSupportedException("HextechForgeDetachedUpgradeBeyondNativeLimit: " + model.Id);
        var keywords = model.GetKeywordsWithSources(KeywordSources.Local).ToHashSet();
        int cost = (int)(model.EnergyCost.GetType().GetField("_base", Members)?.GetValue(model.EnergyCost)
            ?? throw new MissingFieldException(model.EnergyCost.GetType().FullName, "_base"));
        var save = model.ToSerializable();
        save.CurrentUpgradeLevel = checked(model.CurrentUpgradeLevel + 1);
        var result = CardModel.FromSerializable(save);
        // These audited native basic OnUpgrade methods only add three damage
        // or block. Their pre-existing local keywords/custom cost persist.
        foreach (var keyword in Enum.GetValues<CardKeyword>())
        {
            bool present = result.GetKeywordsWithSources(KeywordSources.Local).Contains(keyword);
            if (keywords.Contains(keyword) && !present) result.AddKeyword(keyword);
            else if (!keywords.Contains(keyword) && present) result.RemoveKeyword(keyword);
        }
        result.EnergyCost.SetCustomBaseCost(cost);
        return result;
    }

    private static void RecordFault(ModelKey source, Exception error)
    {
        string stamp = source.Serialized + error;
        if (_lastFault == stamp) return;
        _lastFault = stamp;
        RuntimeLog.Fault("operationPredictionFailed=true;stage=hextech-forge-upgrade-capture;source=" + source, error);
    }
}
