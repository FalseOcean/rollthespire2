using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Preserve the authored set; compile complements only against C's exact pool.
internal static class CombatCardCollectionFastProjection
{
    internal static string AuthorityIssue(SearchModel request)
    {
        if (request.Evaluation.CombatCardExclusion is not { IsEmpty: false } collection) return "";
        if (request.Authority.AvailableCardIdentities is not { } identities) return "CardIdentityAuthorityUnavailable";
        if (collection.Mode == CombatCardCollectionMode.Blacklist && collection.CardKeys.Except(identities, ModelKeyComparer.Instance).Any())
            return "CollectionMemberUnavailable";
        return request.Authority.EffectAuthority?.HasExactCharacterRewardPool == true ? "" : "CardPoolAuthorityIncomplete";
    }

    internal static NormalCombatRewardSearchCondition[] Build(SearchModel request,
        Beta110FastEffectCatalog catalog, out string issue)
    {
        issue = "";
        if (request.Evaluation.CombatCardExclusion is not { IsEmpty: false } collection) return [];
        issue = AuthorityIssue(request);
        if (issue.Length > 0) return [];
        if (!catalog.CombatRewardCardAuthorityExact)
        { issue = "CardPoolAuthorityIncomplete"; return []; }
        var pool = catalog.CombatRewardCardPool;
        if (pool.Common.Concat(pool.Uncommon).Concat(pool.Rare).Distinct().Count() < 3)
        { issue = "CardPoolDistinctCapacityBelowThree"; return []; }
        // Primary and every modeled extra group use the same normal pool;
        // Lasting Candy alone adds the power pool. ObservedOpening changes held
        // flags/counts and arrival, retaining unresolved effects for Exact.
        var ids = pool.Common.Concat(pool.Uncommon).Concat(pool.Rare)
            .Concat(catalog.CombatRewardPowerPool.Common).Concat(catalog.CombatRewardPowerPool.Uncommon)
            .Concat(catalog.CombatRewardPowerPool.Rare).Distinct().ToArray();
        if (ids.Any(id => id == Beta110FastDenseId.Invalid || id >= catalog.DenseKeys.Length))
        { issue = "CardPoolIdentityUnavailable"; return []; }
        var members = collection.CardKeys.ToHashSet(ModelKeyComparer.Instance);
        var banned = ids.Select(id => catalog.DenseKeys[id]).Where(key => collection.Mode == CombatCardCollectionMode.Whitelist
            ? !members.Contains(key) : members.Contains(key)).ToArray();
        // Fixed rows universally constrain every battle, unlike AnyBattle.
        return Enumerable.Range(1, collection.BattleCount).Select(ordinal =>
            new NormalCombatRewardSearchCondition(ordinal, new([], [], banned),
                NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, null, null)).ToArray();
    }
}
