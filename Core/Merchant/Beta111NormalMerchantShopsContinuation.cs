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
/// Versioned Shops continuation for the first five successful initial normal
/// merchant inventories. Only the colorless identity calls consume draw values.
/// </summary>
public static class Beta111NormalMerchantShopsContinuation
{
    public const int CallsPerMerchant = 28;
    public const int UncommonCallWithinMerchant = 13;
    public const int RareCallWithinMerchant = 15;

    // 0.113 constructs relics/potions first (14), then five character
    // cards (rarity, identity, upgrade, price each), U/R and the sale.
    internal readonly record struct Calls(int Total, int Uncommon, int Rare)
    {
        internal int At(int ordinal, bool uncommon) => (ordinal - 1) * Total + (uncommon ? Uncommon : Rare);
    }

    internal static Calls For(bool modern) => modern ? new(42, 35, 38) :
        new(CallsPerMerchant, UncommonCallWithinMerchant, RareCallWithinMerchant);

    public static Beta111NormalMerchantProjection AdvanceOne(
        Beta109WorldRng shops,
        int merchantOrdinal,
        IReadOnlyList<ModelKey> uncommonPool,
        IReadOnlyList<ModelKey> rarePool) =>
        AdvanceOne(shops, merchantOrdinal, uncommonPool, rarePool, For(false));

    internal static Beta111NormalMerchantProjection AdvanceOne(Beta109WorldRng shops, int merchantOrdinal,
        IReadOnlyList<ModelKey> uncommonPool, IReadOnlyList<ModelKey> rarePool, Calls calls)
    {
        ArgumentNullException.ThrowIfNull(shops);
        ArgumentNullException.ThrowIfNull(uncommonPool);
        ArgumentNullException.ThrowIfNull(rarePool);
        if (merchantOrdinal is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(merchantOrdinal));
        if (uncommonPool.Count == 0) throw new InvalidOperationException("MerchantColorlessUncommonPoolEmpty");
        if (rarePool.Count == 0) throw new InvalidOperationException("MerchantColorlessRarePoolEmpty");

        Beta109RngStateSnapshot incoming = shops.CaptureState();
        int baseCall = shops.CallCount;

        Burn(shops, calls.Uncommon - 1, merchantOrdinal, "pre-colorless");
        ModelKey uncommon = shops.NextModelKey(uncommonPool, $"merchant-{merchantOrdinal}:colorless-uncommon");
        Burn(shops, calls.Rare - calls.Uncommon - 1, merchantOrdinal, "between-colorless");
        ModelKey rare = shops.NextModelKey(rarePool, $"merchant-{merchantOrdinal}:colorless-rare");

        Burn(shops, calls.Total - calls.Rare, merchantOrdinal, "post-colorless");

        if (shops.CallCount - baseCall != calls.Total)
            throw new InvalidOperationException("MerchantShopsContinuationCallCountInvariant");

        return new Beta111NormalMerchantProjection(
            merchantOrdinal,
            uncommon,
            rare,
            incoming,
            shops.CaptureState(),
            baseCall + calls.Uncommon,
            baseCall + calls.Rare);
    }

    private static void Burn(Beta109WorldRng shops, int count, int ordinal, string stage)
    {
        for (int index = 0; index < count; index++)
            _ = shops.NextDouble($"merchant-{ordinal}:{stage}:{index + 1}");
    }
}
