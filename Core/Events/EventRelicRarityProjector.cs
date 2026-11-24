using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

/// <summary>Modern113 rarity rolls conditional on ordinary eligible entry and the named branch.</summary>
internal static class EventRelicRarityProjector
{
    internal static bool Project(ulong root, int slot, EventRelicRarityOpportunity opportunity,
        Span<EffectRelicRarity> rarity)
    {
        var branch = opportunity.Branch;
        var rng = new Xoshiro256StarStar(Beta109WorldRng.DeriveEventLocalSeed(root, slot,
            branch == EventRelicRarityBranch.Setting3, opportunity.EventKey.Entry));
        switch (branch)
        {
            case EventRelicRarityBranch.OfferTribute:
                _ = Beta111EventRules.LuminousChoirGold(rng);
                break;
            case EventRelicRarityBranch.Ornate:
                _ = Beta111EventRules.ThisOrThatGold(rng);
                break;
            case EventRelicRarityBranch.GivePotion:
            case EventRelicRarityBranch.GiveGold:
            case EventRelicRarityBranch.GiveRelic:
                // Eligible Ranwid has a potion and a tradable relic; each menu pick consumes once.
                rng.Advance(2);
                break;
            case EventRelicRarityBranch.TradeOffers:
                // The owned-relic shuffle uses a child stream, so inventory size only affects that child.
                _ = rng.NextUInt64();
                break;
            case EventRelicRarityBranch.MerchantGuilty:
                if (Beta111EventRules.TrialCase(rng) != 0) return false;
                break;
        }
        for (int index = 0; index < opportunity.RollCount; index++)
            rarity[index] = NeowRewardGenerator.RollRelicRarity(rng);
        return true;
    }
}
