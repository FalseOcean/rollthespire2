using System.Collections.Immutable;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorCrystalItemKind
{
    Relic, CommonPotion, RarePotion, CommonCard, UncommonCard, RareCard, Curse, SmallGold, BigGold
}

internal readonly record struct PredictorCrystalBoardItem(PredictorCrystalItemKind Kind,
    int X, int Y, int Width, int Height, UInt128 Cells, int Subscriptions)
{
    internal string Code => Kind switch
    {
        PredictorCrystalItemKind.Relic => "RELIC",
        PredictorCrystalItemKind.CommonPotion => "POTION_COMMON",
        PredictorCrystalItemKind.RarePotion => "POTION_RARE",
        PredictorCrystalItemKind.CommonCard => "CARD_COMMON",
        PredictorCrystalItemKind.UncommonCard => "CARD_UNCOMMON",
        PredictorCrystalItemKind.RareCard => "CARD_RARE",
        PredictorCrystalItemKind.Curse => "CURSE",
        PredictorCrystalItemKind.SmallGold => "GOLD_SMALL",
        PredictorCrystalItemKind.BigGold => "GOLD_BIG",
        _ => throw new ArgumentOutOfRangeException()
    };
}

internal sealed record PredictorCrystalBoard(ImmutableArray<PredictorCrystalBoardItem> Items, bool PlacedAllItems)
{
    internal const int Side = 11;
    internal const int CellCount = Side * Side;
    private const int MaxAttempts = 10;

    private static readonly (PredictorCrystalItemKind Kind, int Width, int Height)[] Shapes =
    [
        (PredictorCrystalItemKind.Relic, 4, 4),
        (PredictorCrystalItemKind.CommonPotion, 1, 3), (PredictorCrystalItemKind.CommonPotion, 1, 3),
        (PredictorCrystalItemKind.RarePotion, 2, 2),
        (PredictorCrystalItemKind.CommonCard, 2, 2), (PredictorCrystalItemKind.UncommonCard, 2, 2),
        (PredictorCrystalItemKind.RareCard, 2, 2), (PredictorCrystalItemKind.Curse, 2, 2),
        (PredictorCrystalItemKind.SmallGold, 1, 1), (PredictorCrystalItemKind.SmallGold, 1, 1),
        (PredictorCrystalItemKind.SmallGold, 1, 1), (PredictorCrystalItemKind.SmallGold, 1, 1),
        (PredictorCrystalItemKind.SmallGold, 1, 1),
        (PredictorCrystalItemKind.BigGold, 2, 1), (PredictorCrystalItemKind.BigGold, 2, 1)
    ];

    internal static UInt128 InitialFog { get; } = MakeInitialFog();

    private static readonly (int X, int Y, UInt128 Cells)[][] Origins = Shapes
        .Select(shape => Enumerable.Range(0, Side - shape.Width + 1)
            .SelectMany(x => Enumerable.Range(0, Side - shape.Height + 1)
                .Select(y => (x, y, Rectangle(x, y, shape.Width, shape.Height))))
            .Where(origin => (origin.Item3 & InitialFog) == origin.Item3).ToArray())
        .ToArray();

    // Matches native PopulateItems, including short-circuiting placement
    // after a failure and retaining placed items and subscriptions on retry.
    internal static PredictorCrystalBoard Generate(Xoshiro256StarStar rng)
    {
        var items = ImmutableArray.CreateBuilder<PredictorCrystalBoardItem>(Shapes.Length);
        Span<int> candidates = stackalloc int[CellCount];
        UInt128 occupied = 0;
        bool placedAll = false;
        int attempt = 0;
        for (; attempt < MaxAttempts && !placedAll; attempt++)
        {
            placedAll = true;
            for (int index = 0; index < Shapes.Length; index++)
            {
                var (kind, width, height) = Shapes[index];
                var origins = Origins[index];
                int count = 0;
                if (placedAll)
                    for (int i = 0; i < origins.Length; i++)
                        if ((origins[i].Cells & occupied) == 0)
                            candidates[count++] = i;
                if (count == 0)
                {
                    placedAll = false;
                    items.Add(new(kind, -1, -1, width, height, 0, 0));
                    continue;
                }
                var (x, y, cells) = origins[candidates[rng.NextInt(count)]];
                occupied |= cells;
                items.Add(new(kind, x, y, width, height, cells, 0));
            }
        }
        for (int i = 0; i < items.Count; i++)
            items[i] = items[i] with { Subscriptions = attempt - i / Shapes.Length };
        return new(items.ToImmutable(), placedAll);
    }

    internal void WriteOccupancy(Span<int> occupancy)
    {
        occupancy.Fill(-1);
        for (int i = 0; i < Items.Length; i++)
            for (UInt128 cells = Items[i].Cells; cells != 0; cells &= cells - 1)
                occupancy[FirstCell(cells)] = i;
    }

    internal static int Index(int x, int y) => x * Side + y;

    internal static UInt128 Bit(int x, int y) => (UInt128)1 << Index(x, y);

    internal static int FirstCell(UInt128 cells) => (int)UInt128.TrailingZeroCount(cells);

    internal static UInt128 Rectangle(int x, int y, int width, int height)
    {
        UInt128 cells = 0;
        for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < height; dy++)
                cells |= Bit(x + dx, y + dy);
        return cells;
    }

    private static UInt128 MakeInitialFog()
    {
        UInt128 fog = 0;
        for (int x = 0; x < Side; x++)
            for (int y = 0; y < Side; y++)
                if (Math.Min(x, Side - 1 - x) + Math.Min(y, Side - 1 - y) > 2)
                    fog |= Bit(x, y);
        return fog;
    }
}
