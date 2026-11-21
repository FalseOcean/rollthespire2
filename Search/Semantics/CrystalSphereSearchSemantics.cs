using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

internal static class CrystalSphereSearchSemantics
{
    // The premise is inventory at event entry, not proof of obtaining these relics.
    // Pools retain native enumeration order: Prismatic prefixes unlocked character
    // pools, Dingy appends colorless, then native Union removes duplicate identities.
    internal static IReadOnlyList<NeowEffectCardSnapshot>? Pool(NeowEffectAuthoritySnapshot? effects,
        int players, CrystalSphereSearchRelics relics, ModelKey ownerCharacter)
    {
        if (effects is null || !effects.HasExactCharacterRewardIdentityPool) return null;
        IEnumerable<NeowEffectCardSnapshot> pool = effects.CharacterRewardPool!;
        if (relics.HasFlag(CrystalSphereSearchRelics.PrismaticGem))
        {
            if (!(effects.OtherCharacterIdentityPoolsExact || effects.OtherCharacterPoolsExact) || effects.OtherCharacterPools is null ||
                effects.NativeCharacterPoolOrder is not { } order) return null;
            var pools = effects.OtherCharacterPools.ToDictionary(p => p.CharacterKey, p => p.Cards);
            pools[ownerCharacter] = effects.CharacterRewardPool!;
            if (order.Any(key => !pools.ContainsKey(key))) return null;
            pool = order.SelectMany(key => pools[key]).Concat(pool);
        }
        if (relics.HasFlag(CrystalSphereSearchRelics.DingyRug))
        {
            if (!(effects.ColorlessRewardIdentityPoolExact || effects.ColorlessRewardPoolExact) || effects.ColorlessRewardPool is null) return null;
            pool = pool.Concat(effects.ColorlessRewardPool!);
        }
        return pool.Where(card => players > 1 || !card.IsMultiplayerOnly)
            .Where(card => card.IsUnlockedInCapturedPool && card.EligibleForPostCombatRewardByPoolMembership)
            .DistinctBy(card => card.CardKey).ToArray();
    }
}
