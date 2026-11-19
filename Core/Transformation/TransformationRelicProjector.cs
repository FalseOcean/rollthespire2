using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Transformation;

internal readonly record struct TransformationRelicCard(ModelKey CardKey, EffectCardRarity Rarity, bool Upgraded);

internal sealed record OpeningTransformationContinuation(int? StreamOffset, int? BasicCount, int? BasicStrikeDefendCount)
{
    internal static OpeningTransformationContinuation Initial(NeowEffectAuthoritySnapshot? authority) =>
        FromDeck(0, authority is { HasExactDeck: true } ? authority.OrderedDeck : null);

    internal static OpeningTransformationContinuation FromDeck(int? streamOffset, IReadOnlyList<NeowEffectCardSnapshot>? deck) =>
        new(streamOffset, deck?.Count(IsBasicTarget), deck?.Count(IsBasicStrikeDefendTarget));

    internal static bool IsBasicTarget(NeowEffectCardSnapshot card) => card.IsBasic && card.CanRemove;

    private static bool IsBasicStrikeDefendTarget(NeowEffectCardSnapshot card) =>
        IsBasicTarget(card) && (card.IsStrike || card.IsDefend);
}

/// <summary>
/// Projects raw replacements for Standard play under a common ordered character
/// pool premise. One native draw selects each replacement, including for singleton pools.
/// </summary>
internal sealed class TransformationRelicProjector
{
    internal const int AstrolabeTargetCount = 3;
    internal const int ProjectionDrawBudget = 4096;
    private readonly NeowEffectCardSnapshot[] _orderedCandidates;
    internal ReadOnlySpan<NeowEffectCardSnapshot> OrderedCandidates => _orderedCandidates;

    private TransformationRelicProjector(NeowEffectCardSnapshot[] orderedCandidates) =>
        _orderedCandidates = orderedCandidates;

    internal static TransformationRelicProjector? Capture(NeowEffectAuthoritySnapshot? authority)
    {
        if (authority is not { HasExactTransformPool: true }) return null;
        var candidates = authority.TransformPool!
            .Where(card => card.IsUnlockedInCapturedPool && !card.IsMultiplayerOnly &&
                card.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
            .OrderBy(card => card.PoolOrder)
            .ToArray();

        if (candidates.Length == 0) return null;
        // Initial Basic targets and the Capsule's added Basic Strike/Defend all
        // use the captured character pool. Decline a mixed or conflicting capture.
        string poolId = candidates[0].PoolId;
        if (candidates.Any(card => card.PoolId != poolId)) return null;
        if (authority.OrderedDeck is { } deck && deck.Any(card =>
                OpeningTransformationContinuation.IsBasicTarget(card) &&
                (card.PoolId != poolId || candidates.Any(candidate => candidate.CardKey == card.CardKey)))) return null;
        return new(candidates);
    }

    internal TransformationRelicCard[] Project(IRuntimeProfile profile, ulong rootHash, int playerSlot,
        RngSemanticProfileId semantics, int streamOffset, int targetCount, bool upgrade)
    {
        if (playerSlot < 0) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        if (streamOffset < 0) throw new ArgumentOutOfRangeException(nameof(streamOffset));
        if (targetCount < 0) throw new ArgumentOutOfRangeException(nameof(targetCount));
        string stream = semantics switch
        {
            RngSemanticProfileId.Modern113 => "transformations",
            RngSemanticProfileId.Modern110 => "niche",
            _ => throw new ArgumentOutOfRangeException(nameof(semantics))
        };
        ulong streamRoot = semantics == RngSemanticProfileId.Modern113
            ? unchecked(rootHash + (ulong)playerSlot)
            : rootHash;
        var rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(streamRoot, stream));
        rng.Advance(streamOffset);
        var result = new TransformationRelicCard[targetCount];
        for (int index = 0; index < result.Length; index++)
        {
            var card = _orderedCandidates[rng.NextInt(_orderedCandidates.Length)];
            result[index] = new(card.CardKey, card.Rarity, upgrade && card.CanUpgrade);
        }
        return result;
    }
}
