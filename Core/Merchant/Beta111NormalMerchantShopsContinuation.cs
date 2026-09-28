using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Merchant;

public sealed record Beta111NormalMerchantProjection(
    int MerchantOrdinal,
    ModelKey UncommonColorless,
    ModelKey RareColorless,
    Beta109RngStateSnapshot IncomingShopsState,
    Beta109RngStateSnapshot OutgoingShopsState,
    int UncommonAbsoluteCall,
    int RareAbsoluteCall);

/// <summary>
/// Exact Beta111 vanilla successful-initial-normal-merchant Shops continuation.
/// One merchant consumes exactly 28 xoshiro outputs; Colorless U/R identities are
/// calls 13/15 of that merchant. Burn-only operations deliberately discard values.
/// </summary>
public static class Beta111NormalMerchantShopsContinuation
{
    public const int CallsPerMerchant = 28;
    public const int UncommonCallWithinMerchant = 13;
    public const int RareCallWithinMerchant = 15;

    public static Beta111NormalMerchantProjection AdvanceOne(
        Beta109WorldRng shops,
        int merchantOrdinal,
        IReadOnlyList<ModelKey> uncommonPool,
        IReadOnlyList<ModelKey> rarePool)
    {
        ArgumentNullException.ThrowIfNull(shops);
        ArgumentNullException.ThrowIfNull(uncommonPool);
        ArgumentNullException.ThrowIfNull(rarePool);
        if (merchantOrdinal is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(merchantOrdinal));
        if (uncommonPool.Count == 0) throw new InvalidOperationException("MerchantColorlessUncommonPoolEmpty");
        if (rarePool.Count == 0) throw new InvalidOperationException("MerchantColorlessRarePoolEmpty");

        Beta109RngStateSnapshot incoming = shops.CaptureState();
        int baseCall = shops.CallCount;

        // 1: sale index; 2..11: five character picks/prices; 12: sale recalc.
        Burn(shops, 12, merchantOrdinal, "pre-colorless");
        ModelKey uncommon = shops.NextModelKey(uncommonPool, $"merchant-{merchantOrdinal}:colorless-uncommon");
        _ = shops.NextFloat($"merchant-{merchantOrdinal}:colorless-uncommon-price"); // call 14
        ModelKey rare = shops.NextModelKey(rarePool, $"merchant-{merchantOrdinal}:colorless-rare");

        // call 16 Rare price + 17..19 relic prices + 20..28 potion rarity/pick/price.
        Burn(shops, 13, merchantOrdinal, "post-colorless");

        if (shops.CallCount - baseCall != CallsPerMerchant)
            throw new InvalidOperationException("MerchantShopsContinuationCallCountInvariant");

        return new Beta111NormalMerchantProjection(
            merchantOrdinal,
            uncommon,
            rare,
            incoming,
            shops.CaptureState(),
            baseCall + UncommonCallWithinMerchant,
            baseCall + RareCallWithinMerchant);
    }

    private static void Burn(Beta109WorldRng shops, int count, int ordinal, string stage)
    {
        for (int index = 0; index < count; index++)
            _ = shops.NextDouble($"merchant-{ordinal}:{stage}:{index + 1}");
    }
}
