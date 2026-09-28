using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class Beta110CombatRewardFastPlanCompiler
{

    internal static Beta110CombatRewardFastPlan CompileForFamily(
        ExactSearchExecutionRequest request, Beta110FastEffectCatalog catalog) => CompileCore(
            request.Evaluation.NormalCombatRewardConditions.Where(c => !c.IsEmpty).ToArray(),
            CombatRewardFastRoutePolicy.UnpinnedAssumeUnperturbed,
            Beta110CombatRewardExplicitContextProjector.Project(request.CompiledSearch),
            Beta110CombatRewardOpeningConsumptionProjector.Project(request.CompiledSearch), catalog, false);

    private static Beta110CombatRewardFastPlan CompileCore(
        NormalCombatRewardSearchCondition[] requested, CombatRewardFastRoutePolicy routePolicy,
        Beta110CombatRewardExplicitContext explicitContext,
        Beta110CombatRewardOpeningConsumptionProjection openingConsumption,
        Beta110FastEffectCatalog catalog, bool requireAuthority)
    {
        if (requested.Length == 0)
            return Beta110CombatRewardFastPlan.Disabled("NoCombatRewardConditions", "disabled");
        if (routePolicy is CombatRewardFastRoutePolicy.NotApplicable)
            return Beta110CombatRewardFastPlan.Disabled("CombatRewardRoutePolicyMissing", "disabled-route-policy");

        var predicates = new List<Beta110CombatRewardFastPredicate>(requested.Length);
        string disableReason = string.Empty;
        int cardPredicates = 0;
        int potionDropPredicates = 0;
        int potionIdentityPredicates = 0;
        int goldPredicates = 0;
        int maximumBattleOrdinal = 1;

        foreach (NormalCombatRewardSearchCondition condition in requested)
        {
            if (condition.BattleOrdinal is < 0 or > 3)
            {
                disableReason = "BattleOrdinalOutsideSupportedRange:" + condition.BattleOrdinal;
                break;
            }
            if (!TryCompileKeys(condition.Cards.Any, catalog, out ushort[] cardAny) ||
                !TryCompileKeys(condition.Cards.All, catalog, out ushort[] cardAll) ||
                !TryCompileKeys(condition.Cards.Ban, catalog, out ushort[] cardBan) ||
                !TryCompileKeys(condition.Potions.Any, catalog, out ushort[] potionAny) ||
                !TryCompileKeys(condition.Potions.All, catalog, out ushort[] potionAll) ||
                !TryCompileKeys(condition.Potions.Ban, catalog, out ushort[] potionBan))
            {
                disableReason = "CombatRewardPredicateModelKeyUnavailable";
                break;
            }

            bool hasCard = cardAny.Length > 0 || cardAll.Length > 0 || cardBan.Length > 0;
            bool hasPotionIdentity = potionAny.Length > 0 || potionAll.Length > 0 || potionBan.Length > 0;
            if (requireAuthority && hasCard && !catalog.CombatRewardCardAuthorityExact)
            {
                disableReason = "CombatRewardCardPoolAuthorityIncomplete";
                break;
            }
            if (requireAuthority && hasPotionIdentity && !catalog.CombatRewardPotionAuthorityExact)
            {
                disableReason = "CombatRewardPotionPoolAuthorityIncomplete";
                break;
            }

            if (hasCard) cardPredicates++;
            if (condition.PotionRequirement != NormalCombatPotionRequirement.Any) potionDropPredicates++;
            if (hasPotionIdentity) potionIdentityPredicates++;
            if (condition.MinimumGold.HasValue || condition.MaximumGold.HasValue) goldPredicates++;
            maximumBattleOrdinal = Math.Max(maximumBattleOrdinal, condition.BattleOrdinal == 0 ? 3 : condition.BattleOrdinal);

            predicates.Add(new Beta110CombatRewardFastPredicate(
                checked((byte)condition.BattleOrdinal),
                cardAny,
                cardAll,
                cardBan,
                condition.PotionRequirement,
                potionAny,
                potionAll,
                potionBan,
                condition.MinimumGold ?? 0,
                condition.MaximumGold ?? 0,
                condition.MinimumGold.HasValue,
                condition.MaximumGold.HasValue));
        }

        string fingerprint = Fingerprint(new[]
        {
            "combat-reward-fast-plan-v4-query-literal-opening-replay",
            routePolicy.ToString(),
            explicitContext.Fingerprint,
            openingConsumption.Fingerprint,
            ((ushort)explicitContext.InfluenceFlags).ToString(System.Globalization.CultureInfo.InvariantCulture),
            explicitContext.AdditionalCardRewardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            explicitContext.FixedGoldAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            catalog.Fingerprint,
            catalog.CombatRewardCardAuthorityExact.ToString(),
            catalog.CombatRewardPotionAuthorityExact.ToString(),
            disableReason,
            string.Join(";", predicates.Select(Descriptor))
        });
        if (!string.IsNullOrWhiteSpace(disableReason))
            return Beta110CombatRewardFastPlan.Disabled(disableReason, fingerprint);

        return new Beta110CombatRewardFastPlan(
            true,
            routePolicy,
            explicitContext,
            openingConsumption,
            string.Empty,
            predicates.ToArray(),
            maximumBattleOrdinal,
            cardPredicates,
            potionDropPredicates,
            potionIdentityPredicates,
            goldPredicates,
            catalog.CombatRewardCardAuthorityExact,
            catalog.CombatRewardPotionAuthorityExact,
            fingerprint);
    }

    public static IEnumerable<ModelKey> EnumerateRequestedKeys(NeowSearchFilter filter) =>
        filter.NormalCombatRewardConditions
            .Where(condition => !condition.IsEmpty)
            .SelectMany(condition => condition.Cards.Any
                .Concat(condition.Cards.All)
                .Concat(condition.Cards.Ban)
                .Concat(condition.Potions.Any)
                .Concat(condition.Potions.All)
                .Concat(condition.Potions.Ban))
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance);

    private static bool TryCompileKeys(
        IEnumerable<ModelKey> keys,
        Beta110FastEffectCatalog catalog,
        out ushort[] denseIds)
    {
        var output = new List<ushort>();
        foreach (ModelKey key in keys.Distinct(ModelKeyComparer.Instance))
        {
            if (!catalog.TryGetDenseId(key, out ushort id))
            {
                denseIds = Array.Empty<ushort>();
                return false;
            }
            output.Add(id);
        }
        denseIds = output.ToArray();
        return true;
    }

    private static string Descriptor(Beta110CombatRewardFastPredicate predicate) =>
        $"{predicate.BattleOrdinal}:" +
        $"ca={string.Join(',', predicate.CardAny)}:cl={string.Join(',', predicate.CardAll)}:cb={string.Join(',', predicate.CardBan)}:" +
        $"pr={predicate.PotionRequirement}:pa={string.Join(',', predicate.PotionAny)}:pl={string.Join(',', predicate.PotionAll)}:pb={string.Join(',', predicate.PotionBan)}:" +
        $"min={predicate.HasMinimumGold}:{predicate.MinimumGold}:max={predicate.HasMaximumGold}:{predicate.MaximumGold}";

    private static string Fingerprint(IEnumerable<string> values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values)))).ToLowerInvariant();
}
