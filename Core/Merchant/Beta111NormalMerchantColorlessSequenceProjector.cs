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
    public bool HasExactV1Inputs =>
        ProfileId == RuntimeProfileId.Beta111 &&
        PlayersCount >= 1 &&
        PlayerSlotIndex >= 0 && PlayerSlotIndex < PlayersCount &&
        OrderedPoolExact &&
        PoolIdentityHooksExact &&
        RarityIdentityHooksExact &&
        SuccessfulInitialInventoryShapeExact &&
        UncommonPool.Count > 0 &&
        RarePool.Count > 0;

    public static Beta111MerchantColorlessAuthority From(RuntimeContextAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        NeowEffectAuthoritySnapshot? effects = authority.EffectAuthority;
        bool modeled = authority.UsesBestEffortModel && effects?.HasCapturedIdentityFoundation == true &&
            effects.CharacterRewardPool is not null && (effects.MerchantColorlessOrderedPool ?? effects.ColorlessRewardPool) is not null;
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
            effects?.MerchantColorlessOrderedPoolExact == true || modeled,
            effects?.MerchantColorlessPoolIdentityHooksExact == true || modeled,
            effects?.MerchantColorlessRarityIdentityHooksExact == true || modeled,
            effects?.MerchantInitialInventoryShapeExact == true || modeled,
            authority.ProfileId,
            authority.PlayerSlotIndex,
            authority.PlayersCount,
            effects?.SnapshotFingerprint ?? authority.CatalogFingerprint);
    }
}

public sealed record Beta111ShopColorlessProjection(
    IReadOnlyList<Beta111NormalMerchantProjection> Merchants,
    PredictionPrecision Precision,
    string EvidenceCode)
{
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
        if (!authority.HasExactV1Inputs)
            return Beta111ShopColorlessProjection.Unknown("MerchantColorlessV1AuthorityIncomplete");
        if (merchantCount is < 1 or > MerchantCount)
            return Beta111ShopColorlessProjection.Unsupported("MerchantColorlessV1OrdinalOutsideRange");

        unchecked
        {
            ulong playerSeed = (ulong)((long)rootHash + authority.PlayerSlotIndex);
            Beta109WorldRng shops = Beta109WorldRng.CreateNamed(playerSeed, "shops");
            var merchants = new List<Beta111NormalMerchantProjection>(merchantCount);
            for (int ordinal = 1; ordinal <= merchantCount; ordinal++)
            {
                merchants.Add(Beta111NormalMerchantShopsContinuation.AdvanceOne(
                    shops,
                    ordinal,
                    authority.UncommonPool,
                    authority.RarePool));
            }
            return new Beta111ShopColorlessProjection(
                merchants,
                PredictionPrecision.Exact,
                "beta111.normal-merchant-colorless-v1.source-audited");
        }
    }
}
