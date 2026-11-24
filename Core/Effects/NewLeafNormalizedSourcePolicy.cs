using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects;

/// <summary>
/// Product-level New Leaf normalization. Search and ordinary production
/// projection intentionally use the first remaining Basic Strike for the
/// current character instead of branching over every legal player target.
/// Full complex-choice analysis may still enumerate the real finite choices.
/// </summary>
internal static class NewLeafNormalizedSourcePolicy
{
    public const string PolicyId = "character-first-basic-strike";

    public static NeowEffectCardSnapshot? Select(
        IReadOnlyList<NeowEffectCardSnapshot> cards,
        ModelKey? characterStrikeKey)
    {
        ArgumentNullException.ThrowIfNull(cards);

        bool hasExplicitStrikeKey = characterStrikeKey.HasValue && characterStrikeKey.Value.IsValid;
        NeowEffectCardSnapshot? best = null;
        foreach (NeowEffectCardSnapshot card in cards)
        {
            if (!card.IsBasic || !card.IsStrike ||
                card.CardType is EffectCardType.Curse or EffectCardType.Status)
            {
                continue;
            }

            if (hasExplicitStrikeKey && card.CardKey != characterStrikeKey!.Value)
            {
                continue;
            }

            if (best is null || card.PoolOrder < best.PoolOrder ||
                (card.PoolOrder == best.PoolOrder &&
                 string.CompareOrdinal(card.InstanceId, best.InstanceId) < 0))
            {
                best = card;
            }
        }

        if (best is not null || !hasExplicitStrikeKey)
        {
            return best;
        }

        // Best-effort mod compatibility: when a captured character strike key
        // does not match the deck snapshot, retain the explicit product policy
        // by selecting the first Basic Strike rather than branching.
        foreach (NeowEffectCardSnapshot card in cards)
        {
            if (!card.IsBasic || !card.IsStrike ||
                card.CardType is EffectCardType.Curse or EffectCardType.Status)
            {
                continue;
            }

            if (best is null || card.PoolOrder < best.PoolOrder ||
                (card.PoolOrder == best.PoolOrder &&
                 string.CompareOrdinal(card.InstanceId, best.InstanceId) < 0))
            {
                best = card;
            }
        }

        return best;
    }

    public static NeowEffectCardSnapshot[] BuildTransformCandidates(
        IReadOnlyList<NeowEffectCardSnapshot> transformPool,
        NeowEffectCardSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(transformPool);
        ArgumentNullException.ThrowIfNull(source);

        return transformPool
            .Where(candidate => string.Equals(candidate.PoolId, source.PoolId, StringComparison.Ordinal))
            .Where(candidate => candidate.CardKey != source.CardKey)
            .Where(candidate => candidate.Rarity is not EffectCardRarity.Basic and not EffectCardRarity.Ancient)
            .OrderBy(candidate => candidate.PoolOrder)
            .ThenBy(candidate => candidate.CardKey.Serialized, StringComparer.Ordinal)
            .ToArray();
    }
}
