using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects.Shadow;

public sealed class NeowShadowDeck
{
    private readonly List<NeowEffectCardSnapshot> _cards;

    public NeowShadowDeck(IEnumerable<NeowEffectCardSnapshot> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        _cards = cards.OrderBy(card => card.PoolOrder).ToList();
    }

    private NeowShadowDeck(List<NeowEffectCardSnapshot> cards) => _cards = cards;

    public IReadOnlyList<NeowEffectCardSnapshot> Cards => _cards;

    public NeowShadowDeck Clone() => new(new List<NeowEffectCardSnapshot>(_cards));


    public void Add(NeowEffectCardSnapshot card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _cards.Add(card);
    }

    public bool TryRemove(string instanceId)
    {
        int index = _cards.FindIndex(card => string.Equals(card.InstanceId, instanceId, StringComparison.Ordinal));
        if (index < 0 || !_cards[index].CanRemove)
        {
            return false;
        }

        _cards.RemoveAt(index);
        return true;
    }

    public bool TryUpgrade(string instanceId, out NeowEffectCardSnapshot upgraded)
    {
        int index = _cards.FindIndex(card => string.Equals(card.InstanceId, instanceId, StringComparison.Ordinal));
        if (index < 0 || !_cards[index].CanUpgrade || _cards[index].UpgradeLevel >= _cards[index].MaxUpgradeLevel)
        {
            upgraded = default!;
            return false;
        }

        NeowEffectCardSnapshot current = _cards[index];
        upgraded = current with
        {
            CardKey = current.UpgradeTargetKey ?? current.CardKey,
            UpgradeLevel = current.UpgradeLevel + 1,
            CanUpgrade = current.UpgradeLevel + 1 < current.MaxUpgradeLevel
        };
        _cards[index] = upgraded;
        return true;
    }

    public bool TryTransform(string instanceId, ModelKey replacement)
    {
        int index = _cards.FindIndex(card => string.Equals(card.InstanceId, instanceId, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        _cards[index] = _cards[index] with
        {
            CardKey = replacement,
            IsBasic = false,
            IsStrike = false,
            IsDefend = false,
            UpgradeLevel = 0
        };
        return true;
    }

    public string Fingerprint() => string.Join("|", _cards.Select(card =>
        $"{card.InstanceId}:{card.CardKey.Serialized}:{card.UpgradeLevel}"));
}
