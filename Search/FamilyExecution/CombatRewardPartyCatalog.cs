using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// C owns the mode-specific combat membership. The retained shared donor catalog
// has solo combat pools, while the captured opening pools already contain MP cards.
internal static class CombatRewardPartyCatalog
{
    internal static Beta110FastEffectCatalog Project(ExactSearchExecutionRequest request, Beta110FastEffectCatalog catalog)
    {
        var authority = request.Authority.EffectAuthority ?? throw new InvalidOperationException("C.PartyEffectAuthorityMissing");
        var cards = (authority.CharacterRewardPool ?? [])
            .Where(c => c.EligibleForPostCombatRewardByPoolMembership && c.IsUnlockedInCapturedPool &&
                c.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
            .OrderBy(c => c.PoolOrder).ToArray();
        ushort Id(ModelKey key) => catalog.TryGetDenseId(key, out ushort id) ? id :
            throw new InvalidOperationException("C.PartyPoolIdentityMissing:" + key);
        Beta110FastCardPool Pool(IEnumerable<NeowEffectCardSnapshot> source)
        {
            var rows = source.ToArray();
            return new(rows.Where(c => c.Rarity == EffectCardRarity.Common).Select(c => Id(c.CardKey)).ToArray(),
                rows.Where(c => c.Rarity == EffectCardRarity.Uncommon).Select(c => Id(c.CardKey)).ToArray(),
                rows.Where(c => c.Rarity == EffectCardRarity.Rare).Select(c => Id(c.CardKey)).ToArray());
        }
        var cardPool = Pool(cards);
        var powerPool = Pool(cards.Where(c => c.CardType == EffectCardType.Power));
        var potions = (authority.PotionPool ?? []).Where(p =>
            p.Rarity is EffectPotionRarity.Common or EffectPotionRarity.Uncommon or EffectPotionRarity.Rare)
            .OrderBy(p => p.PoolOrder).ToArray();
        var potionPool = new Beta110FastPotionPool(
            potions.Where(p => p.Rarity == EffectPotionRarity.Common).Select(p => Id(p.PotionKey)).ToArray(),
            potions.Where(p => p.Rarity == EffectPotionRarity.Uncommon).Select(p => Id(p.PotionKey)).ToArray(),
            potions.Where(p => p.Rarity == EffectPotionRarity.Rare).Select(p => Id(p.PotionKey)).ToArray(),
            potions.Select(p => Id(p.PotionKey)).ToArray());
        return catalog with
        {
            CombatRewardCardPool = cardPool,
            CombatRewardPowerPool = powerPool,
            CombatRewardPotionPool = potionPool,
            CombatRewardCardAuthorityExact = authority.HasExactCharacterRewardPool && cardPool.TotalCount > 0 &&
                powerPool.TotalCount > 0 && cards.All(c => c.CatalogProfileId == authority.CapturedProfileId &&
                    RuntimeProfilePolicies.UsesBeta110SharedAlgorithms(c.CatalogProfileId) && !string.IsNullOrWhiteSpace(c.EligibilityAuthority)),
            CombatRewardPotionAuthorityExact = catalog.PotionAuthorityExact,
            // Query-literal C does not observe latent relic bags.
            OrdinaryRelics = [], RelicBagAuthorityExact = false,
            SharedRelicConsumeShuffleLengths = [], PlayerRelicBuckets = [],
            Fingerprint = catalog.Fingerprint + ":C.MultiplayerCombatPools.QueryLiteral.20261027.v2"
        };
    }
}
