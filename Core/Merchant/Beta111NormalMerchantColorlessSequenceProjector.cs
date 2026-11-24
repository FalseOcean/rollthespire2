using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Merchant;

public sealed record Beta111MerchantColorlessAuthority(
    IReadOnlyList<ModelKey> UncommonPool,
    IReadOnlyList<ModelKey> RarePool,
    bool OrderedPoolExact,
    bool PoolIdentityHooksExact,
    bool RarityIdentityHooksExact,
    bool SuccessfulInitialInventoryShapeExact,
    RuntimeProfileId ProfileId,
    int PlayerSlotIndex,
    int PlayersCount,
    string AuthorityFingerprint)
{
    internal const string StandardRulesAssumption = "StandardMerchantColorlessRules";
    internal const string StandardLayoutAssumption = "StandardMerchantInventoryLayout";
    internal const string CapturedOrderAssumption = "CapturedColorlessPoolOrder";
    internal bool UsesModern113 { get; init; }
    internal IReadOnlyList<NeowEffectPotionSnapshot>? PotionStock { get; init; }
    internal Beta111NormalMerchantShopsContinuation.Calls Calls => Beta111NormalMerchantShopsContinuation.For(UsesModern113);
    internal string RuleRevision => UsesModern113 ? ".Modern113" : "";
    public bool HasExactV1Inputs => SuccessfulInitialInventoryShapeExact && HasColorlessInputs;
    internal bool HasColorlessInputs =>
        ProfileId == RuntimeProfileId.Beta111 &&
        PlayersCount >= 1 &&
        PlayerSlotIndex >= 0 && PlayerSlotIndex < PlayersCount &&
        OrderedPoolExact &&
        PoolIdentityHooksExact &&
        RarityIdentityHooksExact &&
        UncommonPool.Count > 0 &&
        RarePool.Count > 0;
    private bool HasKnownInventoryShape =>
        SuccessfulInitialInventoryShapeExact || UsesModern113 && PotionStock is not null;

    public static Beta111MerchantColorlessAuthority From(RuntimeContextAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        NeowEffectAuthoritySnapshot? effects = authority.EffectAuthority;
        bool modern = authority.VersionResolution.SemanticProfileId == RngSemanticProfileId.Modern113;
        IReadOnlyList<NeowEffectCardSnapshot> colorless = effects?.MerchantColorlessOrderedPool ??
            effects?.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>();
        ModelKey[] uncommon = colorless
            .Where(card => card.Rarity == EffectCardRarity.Uncommon)
            .OrderBy(card => card.PoolOrder)
            .Select(card => card.CardKey)
            .ToArray();
        ModelKey[] rare = colorless
            .Where(card => card.Rarity == EffectCardRarity.Rare)
            .OrderBy(card => card.PoolOrder)
            .Select(card => card.CardKey)
            .ToArray();
        return new Beta111MerchantColorlessAuthority(
            uncommon,
            rare,
            effects is { MerchantColorlessOrderedPoolExact: true, MerchantColorlessOrderedPool: not null },
            effects?.MerchantColorlessPoolIdentityHooksExact == true,
            effects?.MerchantColorlessRarityIdentityHooksExact == true,
            effects?.MerchantInitialInventoryShapeExact == true,
            authority.ProfileId,
            authority.PlayerSlotIndex,
            authority.PlayersCount,
            effects?.SnapshotFingerprint ?? authority.CatalogFingerprint)
        { UsesModern113 = modern, PotionStock = effects?.MerchantPotionStock };
    }

    internal static bool TryAssumeStandardRules(RuntimeContextAuthoritySnapshot captured,
        out NeowEffectAuthoritySnapshot? modeled, out string assumptions)
    {
        modeled = null;
        assumptions = string.Empty;
        if (captured.ProfileId != RuntimeProfileId.Beta111 || captured.EffectAuthority is not { } effects)
            return false;
        var pool = effects.MerchantColorlessOrderedPool ?? effects.ColorlessRewardPool;
        bool exactOrder = effects.MerchantColorlessOrderedPoolExact && effects.MerchantColorlessOrderedPool is not null;
        if (pool is null || !exactOrder && !effects.ColorlessRewardIdentityPoolExact &&
            !(captured.UsesBestEffortModel && effects.HasCapturedIdentityFoundation))
            return false;
        var merchant = From(captured);
        if (merchant.HasColorlessInputs && merchant.HasKnownInventoryShape ||
            merchant.UncommonPool.Count == 0 || merchant.RarePool.Count == 0)
            return false;
        var stock = merchant.PotionStock ?? effects.ObservedMerchantPotionStock;
        bool standardShape = merchant.SuccessfulInitialInventoryShapeExact;
        var reasons = new List<string>();
        if (!exactOrder)
            reasons.Add(CapturedOrderAssumption);
        if (!merchant.PoolIdentityHooksExact || !merchant.RarityIdentityHooksExact)
            reasons.Add(StandardRulesAssumption);
        if (!standardShape)
        {
            if (pool.Select(card => card.CardKey).Distinct().Count() != pool.Count)
                return false;
            if (effects.CharacterRewardPoolExact && effects.CharacterRewardPool is { } character)
            {
                var colored = character.Where(card => !card.IsBasic &&
                    card.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
                    .DistinctBy(card => card.CardKey).ToArray();
                if (colored.Count(card => card.CardType == EffectCardType.Attack) < 2 ||
                    colored.Count(card => card.CardType == EffectCardType.Skill) < 2 ||
                    !colored.Any(card => card.CardType == EffectCardType.Power) ||
                    colored.Select(card => card.CardKey).Intersect(pool.Select(card => card.CardKey)).Any())
                    return false;
            }
            standardShape = !merchant.UsesModern113 || stock is null ||
                Beta111NormalMerchantShopsContinuation.HasUnconditionalPotionStock(stock);
            reasons.Add(StandardLayoutAssumption);
        }
        assumptions = string.Join("|", reasons);
        modeled = effects with
        {
            MerchantColorlessOrderedPool = pool,
            MerchantColorlessOrderedPoolExact = true,
            MerchantColorlessPoolIdentityHooksExact = true,
            MerchantColorlessRarityIdentityHooksExact = true,
            MerchantInitialInventoryShapeExact = standardShape,
            MerchantPotionStock = stock
        };
        return true;
    }
}

public sealed record Beta111ShopColorlessProjection(
    IReadOnlyList<Beta111NormalMerchantProjection> Merchants,
    PredictionPrecision Precision,
    string EvidenceCode)
{
    internal string ReplayAssumption { get; init; } = string.Empty;

    public static Beta111ShopColorlessProjection Unknown(string code) => new(
        Array.Empty<Beta111NormalMerchantProjection>(),
        PredictionPrecision.Unknown,
        code);

    public static Beta111ShopColorlessProjection Unsupported(string code) => new(
        Array.Empty<Beta111NormalMerchantProjection>(),
        PredictionPrecision.Unsupported,
        code);
}

/// <summary>
/// Canonical v1 sequence: five consecutive successful vanilla Normal Merchant initial
/// inventories on the owning player's pristine named Shops stream. This is deliberately
/// not a route/reachability model and excludes Courier/FakeMerchant/purchases/restocks.
/// </summary>
public static class Beta111NormalMerchantColorlessSequenceProjector
{
    public const int MerchantCount = 5;

    public static Beta111ShopColorlessProjection Project(
        ulong rootHash,
        Beta111MerchantColorlessAuthority authority,
        int merchantCount = MerchantCount)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.ProfileId != RuntimeProfileId.Beta111)
            return Beta111ShopColorlessProjection.Unsupported("MerchantColorlessV1RequiresBeta111");
        if (merchantCount is < 1 or > MerchantCount)
            return Beta111ShopColorlessProjection.Unsupported("MerchantColorlessV1OrdinalOutsideRange");
        if (authority.PlayersCount < 1 || authority.PlayerSlotIndex < 0 || authority.PlayerSlotIndex >= authority.PlayersCount)
            return Beta111ShopColorlessProjection.Unsupported("MerchantColorlessPlayerSlotOutsideRange");
        if (!authority.OrderedPoolExact)
            return Beta111ShopColorlessProjection.Unknown("MerchantColorlessPoolOrderUnobserved");
        if (!authority.PoolIdentityHooksExact || !authority.RarityIdentityHooksExact)
            return Beta111ShopColorlessProjection.Unknown("MerchantColorlessRulesUnobserved");
        if (authority.UncommonPool.Count == 0 || authority.RarePool.Count == 0)
            return new([], PredictionPrecision.Exact, "MerchantColorlessPoolKnownEmpty");
        if (!authority.HasColorlessInputs || !authority.HasExactV1Inputs && !(authority.UsesModern113 && authority.PotionStock != null))
            return Beta111ShopColorlessProjection.Unknown("MerchantInventoryShapeUnobserved");

        unchecked
        {
            ulong playerSeed = (ulong)((long)rootHash + authority.PlayerSlotIndex);
            Beta109WorldRng shops = Beta109WorldRng.CreateNamed(playerSeed, "shops");
            var merchants = new List<Beta111NormalMerchantProjection>(merchantCount);
            for (int ordinal = 1; ordinal <= merchantCount; ordinal++)
            {
                if (!authority.SuccessfulInitialInventoryShapeExact)
                {
                    var state = shops.CaptureState();
                    var start = Core.Rng.Xoshiro256StarStar.FromState(state.S0, state.S1, state.S2, state.S3, state.CallCount);
                    if (Beta111NormalMerchantShopsContinuation.CheckPotionStock(authority.PotionStock, start, ordinal) is { } issue)
                        return new(merchants, merchants.Count == 0 ? PredictionPrecision.Unknown : PredictionPrecision.Partial, issue);
                }
                merchants.Add(Beta111NormalMerchantShopsContinuation.AdvanceOne(
                    shops,
                    ordinal,
                    authority.UncommonPool,
                    authority.RarePool,
                    authority.Calls));
            }
            return new Beta111ShopColorlessProjection(
                merchants,
                PredictionPrecision.Exact,
                authority.UsesModern113 ? "beta113.normal-merchant-colorless.source-audited" :
                    "beta111.normal-merchant-colorless-v1.source-audited");
        }
    }
}
