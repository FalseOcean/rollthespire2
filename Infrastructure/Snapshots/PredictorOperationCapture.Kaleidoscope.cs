using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    private sealed record KaleidoscopeSource(CardPoolModel Pool, IReadOnlyList<CardModel> Cards, string? Issue);

    private static KaleidoscopeSource[]? ReadKaleidoscopeSources(RunState run, Player player, bool logFailure = false)
    {
        if (run.CurrentRoom is not EventRoom { CanonicalEvent: Neow } room ||
            !room.LocalMutableEvent.CurrentOptions.Any(o => o.Relic?.Id.Entry is "KALEIDOSCOPE" or "NEOWS_BONES"))
            return null;

        var pools = player.UnlockState.CharacterCardPools.Where(pool => pool != player.Character.CardPool).Order().ToArray();
        Player detached;
        try
        {
            detached = KaleidoscopeCardCreationCapture.CreateDetachedPlayer(player);
            KaleidoscopeCardCreationCapture.CheckRewardResultHooks(detached);
        }
        catch (Exception ex)
        {
            if (logFailure) RuntimeLog.WarnException(
                $"operation=KaleidoscopePlayer;seed={run.Rng.StringSeed};player={player.NetId}", ex);
            return pools.Select(pool => new KaleidoscopeSource(pool, [], ex.GetType().Name + ": " + ex.Message)).ToArray();
        }
        return pools.Select(pool =>
            {
                try { return new KaleidoscopeSource(pool, KaleidoscopeCardCreationCapture.GetPossibleCards(detached, pool), null); }
                catch (Exception ex)
                {
                    if (logFailure) RuntimeLog.WarnException(
                        $"operation=KaleidoscopePossibleCards;seed={run.Rng.StringSeed};player={player.NetId};source={pool.Id}", ex);
                    return new KaleidoscopeSource(pool, [], ex.GetType().Name + ": " + ex.Message);
                }
            }).ToArray();
    }

    private static ImmutableArray<PredictorKaleidoscopePool>? CopyKaleidoscopePools(KaleidoscopeSource[]? sources) =>
        sources?.Select(source => new PredictorKaleidoscopePool(Key(source.Pool).Serialized,
            source.Cards.Select((card, index) => CrystalSphereCatalogCapture.CardSnapshot(card, index, source.Pool)).ToImmutableArray(),
            source.Issue)).ToImmutableArray();

    private static string OperationStamp(string native, PredictorOperationScene scene, KaleidoscopeSource[]? sources) =>
        Hash(new { Native = native, Scene = (object)scene, Kaleidoscope = sources?.Select(source => new {
            Source = Key(source.Pool), Cards = source.Cards.Select(card => new { Id = Key(card), card.Rarity,
                card.CurrentUpgradeLevel, card.MaxUpgradeLevel }), source.Issue }) });
}
