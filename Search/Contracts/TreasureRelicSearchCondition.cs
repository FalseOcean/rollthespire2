using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Search.Contracts;

public enum TreasureRelicOrderMode { Ordered, Unordered }

/// <summary>Targets are relative to the selected window of the first two encountered chests.</summary>
public sealed record TreasureRelicSearchCondition(
    int StartOrdinal,
    int Count,
    TreasureRelicOrderMode OrderMode,
    IReadOnlyList<ModelKey?> Slots)
{
    public const int MaximumOrdinal = 2;
    public static TreasureRelicSearchCondition Empty { get; } = new(1, 1, TreasureRelicOrderMode.Ordered, [null, null]);
    public bool IsEmpty => !Slots.Take(Count).Any(key => key.HasValue);
    public bool IsValid => StartOrdinal is >= 1 and <= MaximumOrdinal && Count is >= 1 and <= MaximumOrdinal &&
        StartOrdinal + Count - 1 <= MaximumOrdinal && Enum.IsDefined(OrderMode) &&
        Slots is not null && Slots.Count >= Count && Slots.Count <= MaximumOrdinal &&
        Slots.All(key => key is null || key.Value.IsValid && key.Value.Category == BaseGameModelKeys.Categories.Relic);
}
