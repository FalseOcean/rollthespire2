using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Collections;

/// <summary>
/// A saved card identity set, or a captured revision selected by a consumer.
/// Matching is exact model identity; upgrades, enchantments and catalog availability
/// do not change membership. Consumers capture Cards rather than follow a live store.
/// </summary>
public sealed record CardCollectionSnapshot(string Id, string Name, int Revision, IReadOnlyList<ModelKey> Cards)
{
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public bool Contains(ModelKey card) => Cards.Contains(card, ModelKeyComparer.Instance);

    /// <summary>Detach caller-owned lists before retaining a request or store result.</summary>
    public CardCollectionSnapshot Copy() => this with { Cards = Array.AsReadOnly(Cards.ToArray()) };
}
