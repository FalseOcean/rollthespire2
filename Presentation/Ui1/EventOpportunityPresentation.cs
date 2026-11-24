using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Presentation.Ui1;

internal static class EventOpportunityPresentation
{
    internal const string Prefix = "query.event.opportunity.";
    internal static IReadOnlyList<EffectRelicRarity> RelicRarities { get; } =
        [EffectRelicRarity.Common, EffectRelicRarity.Uncommon, EffectRelicRarity.Rare];
    internal static IReadOnlyList<CrystalSphereSeedCards> CardRarities { get; } =
        [CrystalSphereSeedCards.Common, CrystalSphereSeedCards.Uncommon, CrystalSphereSeedCards.Rare];

    internal static string Rarity(IUiTextProvider text, Enum value) =>
        text.Get("rarity." + value.ToString().ToLowerInvariant());

    internal static string Branch(IUiTextProvider text, EventRelicRarityBranch branch) =>
        text.Get(Prefix + "branch." + branch);

    internal static string RollSelection(IUiTextProvider text, EventRelicRarityBranch branch,
        EventRelicRaritySelection selection)
    {
        string key = branch == EventRelicRarityBranch.TradeOffers &&
            selection is EventRelicRaritySelection.First or EventRelicRaritySelection.Second or EventRelicRaritySelection.Third
            ? "trade." + selection : "selection." + selection;
        return text.Get(Prefix + key);
    }

    internal static string RelicChoice(IUiTextProvider text, EventResultConditionKind kind) =>
        text.Get(Prefix + "choice." + kind);

    internal static string CrystalChoice(IUiTextProvider text, int divinations) =>
        text.Get(Prefix + "crystal." + divinations);

    internal static string CrystalRelic(IGameContentNameResolver names, CrystalSphereSearchRelics relic) =>
        names.Resolve(new ModelKey("RELIC", relic switch
        {
            CrystalSphereSearchRelics.Driftwood => "DRIFTWOOD",
            CrystalSphereSearchRelics.PrismaticGem => "PRISMATIC_GEM",
            CrystalSphereSearchRelics.DingyRug => "DINGY_RUG",
            _ => throw new ArgumentOutOfRangeException(nameof(relic))
        }), GameContentKind.Relic);

    internal static bool TryDescribe(IUiTextProvider text, IGameContentNameResolver names,
        EventResultSearchCondition condition, out string description)
    {
        if (!EventOpportunitySemantics.TryGetEventKey(condition, out ModelKey eventKey))
        {
            description = string.Empty;
            return false;
        }

        string eventName = names.Resolve(eventKey, GameContentKind.Event);
        if (condition.Kind == EventResultConditionKind.EventRelicRarityRoll &&
            condition.RelicRarityBranch is { } branch && condition.RelicRaritySelection is { } selection &&
            condition.RelicRarity is { } rarity)
        {
            bool multipleRolls = EventOpportunityCatalog.RarityBranches.Any(item =>
                item.Branch == branch && item.RollCount > 1);
            string option = Branch(text, branch);
            if (multipleRolls) option += " · " + RollSelection(text, branch, selection);
            description = eventName + " · " + option + " · " + text.Format(Prefix + "roll_summary", Rarity(text, rarity));
            return true;
        }
        if (condition.Kind == EventResultConditionKind.CrystalSphereRewards && condition.CrystalDivinations is { } divinations)
        {
            var goals = new List<string>();
            if (condition.CrystalTargetCards is { Count: > 0 } targets)
                goals.Add(string.Join(" + ", targets.GroupBy(key => key).Select(g =>
                    names.Resolve(g.Key, GameContentKind.Card) + (g.Count() > 1 ? " ×" + g.Count() : ""))));
            if (condition.CrystalCards is { } cards)
                foreach (var card in CardRarities.Where(card => cards.HasFlag(card)))
                    goals.Add(text.Format(Prefix + "card_summary", Rarity(text, card)));
            if (condition.RelicRarity is { } relic)
                goals.Add(text.Format(Prefix + "roll_summary", Rarity(text, relic)));
            description = eventName + " · " + CrystalChoice(text, divinations) + " · " + string.Join(" + ", goals);
            var premises = condition.CrystalRelics.GetValueOrDefault();
            if (premises != CrystalSphereSearchRelics.None)
                description += " · " + text.Format(Prefix + "crystal_relic_summary", string.Join(" + ",
                    Enum.GetValues<CrystalSphereSearchRelics>().Where(flag => flag != 0 && premises.HasFlag(flag))
                        .Select(flag => CrystalRelic(names, flag))));
            return true;
        }
        description = eventName + " · " + RelicChoice(text, condition.Kind) + " · " +
            names.Resolve(condition.TargetKey, GameContentKind.Relic);
        return true;
    }
}
