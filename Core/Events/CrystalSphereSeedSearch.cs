using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

[Flags]
public enum CrystalSphereSeedCards
{
    None = 0,
    Common = 1,
    Uncommon = 2,
    Rare = 4
}

internal readonly record struct CrystalSphereSeedTargets(CrystalSphereSeedCards Cards, EffectRelicRarity? RelicRarity)
{
    private int AdditionalRelicRarities { get; init; }

    internal int RequiredRelicRarities => AdditionalRelicRarities | (RelicRarity is { } rarity ? RelicBit(rarity) : 0);

    internal static CrystalSphereSeedTargets WithRelicRarities(CrystalSphereSeedCards cards,
        IEnumerable<EffectRelicRarity> rarities) =>
        new(cards, null) { AdditionalRelicRarities = rarities.Aggregate(0, (mask, rarity) => mask | RelicBit(rarity)) };

    internal static int RelicBit(EffectRelicRarity rarity) => rarity switch
    {
        EffectRelicRarity.Common => 1,
        EffectRelicRarity.Uncommon => 2,
        EffectRelicRarity.Rare => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(rarity))
    };
}

internal enum CrystalSphereSeedVerdict { Match, NoMatch, Unknown }

internal sealed record CrystalSphereSeedReward(int ItemIndex, string Kind, int Copies,
    ImmutableArray<EffectRelicRarity> RelicRolls);

internal sealed record CrystalSphereSeedCardTake(int ItemIndex, int SubscriptionIndex, ModelKey Card);
internal sealed record CrystalSphereSeedCardReroll(int ItemIndex, int SubscriptionIndex);

internal sealed record CrystalSphereSeedWitness(int EventCallsAfterPlacement,
    ImmutableArray<PredictorCrystalStep> Steps, ImmutableArray<CrystalSphereSeedReward> Rewards)
{
    internal ImmutableArray<CrystalSphereSeedCardTake> CardTakes { get; init; } = [];
    internal ImmutableArray<CrystalSphereSeedCardReroll> CardRerolls { get; init; } = [];
}

internal sealed record CrystalSphereSeedResult(CrystalSphereSeedVerdict Verdict,
    CrystalSphereSeedWitness? Witness = null, string? Reason = null);

/// <summary>
/// Conditional reward opportunity on a fresh ordinary Crystal Sphere board.
/// Card objectives refer to the authored reward rarity. Relic objectives refer
/// to the roll before the player's relic bag picks or substitutes its contents.
/// </summary>
internal static class CrystalSphereSeedSearch
{
    // A single reveal reaches at most one corner of the 4x4 relic.
    internal const int MinimumRelicDivinations = 4;

    internal const CrystalSphereSeedCards AllCards =
        CrystalSphereSeedCards.Common | CrystalSphereSeedCards.Uncommon | CrystalSphereSeedCards.Rare;

    internal static CrystalSphereSeedResult Evaluate(ulong rootHash, int slot, RngSemanticProfileId semanticProfile,
        int divinations, CrystalSphereSeedTargets targets, CancellationToken token = default,
        int stateLimit = 24_000) => Evaluate(rootHash, slot, semanticProfile, divinations, targets, null, false, token, stateLimit);

    /// <summary>Find a route that lets one player take the specified card identities.
    /// Supply the player's mode-specific unlocked pool in native order after applying authored pool effects.</summary>
    internal static CrystalSphereSeedResult EvaluateCards(ulong rootHash, int slot, RngSemanticProfileId semanticProfile,
        int divinations, IReadOnlyList<NeowEffectCardSnapshot> orderedCharacterPool, ImmutableArray<ModelKey> cards,
        CancellationToken token = default, CrystalSphereSeedTargets rarityTargets = default, bool driftwood = false,
        int stateLimit = 24_000)
    {
        token.ThrowIfCancellationRequested();
        if (cards.IsDefaultOrEmpty) throw new ArgumentOutOfRangeException(nameof(cards));
        var definition = CrystalSphereSeedCardTargets.Create(orderedCharacterPool, cards);
        if (definition == null)
            return new(CrystalSphereSeedVerdict.Unknown, Reason: "CrystalSphereSeedCardPoolUnavailable");
        return Evaluate(rootHash, slot, semanticProfile, divinations, rarityTargets, definition, driftwood, token, stateLimit);
    }

    private static CrystalSphereSeedResult Evaluate(ulong rootHash, int slot, RngSemanticProfileId semanticProfile,
        int divinations, CrystalSphereSeedTargets targets, CrystalSphereSeedCardTargets? cardTargets, bool driftwood,
        CancellationToken token, int stateLimit)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        if (divinations is not (3 or 6)) throw new ArgumentOutOfRangeException(nameof(divinations));
        if (stateLimit < 0) throw new ArgumentOutOfRangeException(nameof(stateLimit));
        int relicRarities = targets.RequiredRelicRarities;
        if ((targets.Cards & ~AllCards) != 0 || (targets.Cards == 0 && relicRarities == 0 && cardTargets == null))
            throw new ArgumentOutOfRangeException(nameof(targets));

        token.ThrowIfCancellationRequested();
        if (semanticProfile is not (RngSemanticProfileId.Modern110 or RngSemanticProfileId.Modern113))
            return new(CrystalSphereSeedVerdict.Unknown, Reason: "CrystalSphereSeedProfileUnavailable");

        if (divinations < MinimumRelicDivinations && relicRarities != 0)
            return new(CrystalSphereSeedVerdict.NoMatch);

        var rng = new Xoshiro256StarStar(Beta109WorldRng.DeriveEventLocalSeed(rootHash, slot, false, "CRYSTAL_SPHERE"));
        // Both branches generate the price before beginning the minigame.
        Beta111EventRules.CrystalSphereGold(rng);
        var board = PredictorCrystalBoard.Generate(rng);
        return new CrystalSphereSeedSolver(board, rng, divinations, targets, token, stateLimit, cardTargets, driftwood).Solve();
    }

    internal static CrystalSphereSeedCards CardKind(PredictorCrystalItemKind kind) => kind switch
    {
        PredictorCrystalItemKind.CommonCard => CrystalSphereSeedCards.Common,
        PredictorCrystalItemKind.UncommonCard => CrystalSphereSeedCards.Uncommon,
        PredictorCrystalItemKind.RareCard => CrystalSphereSeedCards.Rare,
        _ => CrystalSphereSeedCards.None
    };

    internal static bool IsPotion(PredictorCrystalItemKind kind) =>
        kind is PredictorCrystalItemKind.CommonPotion or PredictorCrystalItemKind.RarePotion;

    internal static int PopulationCalls(PredictorCrystalItemKind kind) => kind switch
    {
        PredictorCrystalItemKind.CommonCard or PredictorCrystalItemKind.UncommonCard or PredictorCrystalItemKind.RareCard => 6,
        PredictorCrystalItemKind.Relic or PredictorCrystalItemKind.SmallGold or PredictorCrystalItemKind.BigGold => 1,
        _ => 0
    };
}
