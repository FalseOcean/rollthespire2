using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorLiveRunCapture
{
    internal static HeldRelicOperationObservation CaptureHeldRelic(RunState run, string version,
        long sourceId, HeldRelicOperationObservation? previous)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var player = HeldRelicPlayer(run, sourceId);
        bool modern = RuntimeVersionCompatibility.Resolve(version).SemanticProfileId == RngSemanticProfileId.Modern113;
        var combat = HeldRelicCombatPhase(run);
        var peers = PriorSharedStreamPlayers(run, player, sourceId, modern);
        string stamp = HeldRelicStamp(run, player, peers, combat, modern);
        var levels = new CatalogLevels(previous?.Context);
        var models = peers.Append(player).SelectMany(seat => seat.Deck.Cards.Select(card => card.Id)
            .Concat(seat.Relics.OfType<PaelsTooth>().SelectMany(tooth => tooth.SerializableCards)
                .Select(card => card.Id ?? throw new InvalidOperationException("PredictorLiveStoredCardIdentityMissing"))))
            .Distinct().Select(ModelDb.GetById<CardModel>).ToArray();
        var catalog = new PredictorCatalog([], [])
        {
            CardDefinitions = models.Select(card => CrystalSphereCatalogCapture.CaptureCardDefinition(card, levels.Capture))
                .ToImmutableArray(),
            ShopGainsBlock = models.Where(card => card.Id.Entry is not ("SOVEREIGN_BLADE" or "MAD_SCIENCE") && card.GainsBlock)
                .Select(Key).ToImmutableArray()
        };
        var context = new PredictorContext(run.Rng.StringSeed, version, Key(player.Character), run.AscensionLevel,
            "", "", [], [], [], 0)
        {
            Catalog = catalog,
            Modifiers = run.Modifiers.Select(modifier => new PredictorRunModifier(Key(modifier))).ToImmutableArray()
        };
        var hoarder = run.Modifiers.OfType<Hoarder>().FirstOrDefault();
        var hoarderCards = hoarder == null ? [] : (HoarderSkipCards(hoarder) ?? []).Select(Id).ToImmutableArray();
        var state = CaptureHeldRelicPlayer(run, player, context, catalog, hoarderCards);
        levels.Remember(context);
        return new(context, state, sourceId, combat, stamp)
        {
            PriorSharedStreamPlayers = peers.Select(peer => CaptureHeldRelicPlayer(run, peer, context, catalog, hoarderCards))
                .ToImmutableArray()
        };
    }

    private static PredictorState CaptureHeldRelicPlayer(RunState run, Player player, PredictorContext context,
        PredictorCatalog catalog, ImmutableArray<long> hoarderCards)
    {
        bool modern = context.UsesNonCombatGeneration;
        var cards = player.Deck.Cards.Select(card => Card(card, catalog, captureSort: modern)).ToImmutableArray();
        var relics = player.Relics.Select(relic => Relic(relic, catalog, Id)).ToImmutableArray();
        var cursor = modern
            ? CrystalSphereLiveCapture.RngState(PredictorStream.NonCombatGeneration, PredictorOperationCapture.NonCombatGeneration(player))
            : CrystalSphereLiveCapture.RngState(PredictorStream.Niche, run.Rng.Niche);
        return new(NextInstanceId, new(run.CurrentActIndex + 1, run.TotalFloor, "held-relic"), player.Gold,
            cards, cards.Select(card => card.Id).ToImmutableArray(), relics, [],
            [cursor, CrystalSphereLiveCapture.RngState(PredictorStream.Rewards, player.PlayerRng.Rewards)],
            new(0, 0), PredictorRelicBag.Empty(false), PredictorRelicBag.Empty(false))
        {
            HoarderCardsToSkip = hoarderCards
        };
    }

    internal static string HeldRelicObservationStamp(RunState run, HeldRelicOperationObservation observation)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var player = HeldRelicPlayer(run, observation.RelicId);
        bool modern = observation.Context.UsesNonCombatGeneration;
        return HeldRelicStamp(run, player, PriorSharedStreamPlayers(run, player, observation.RelicId, modern),
            HeldRelicCombatPhase(run), modern);
    }

    private static Player HeldRelicPlayer(RunState run, long sourceId)
    {
        var player = LocalContext.GetMe(run);
        return player != null && player.Relics.Any(relic => Id(relic) == sourceId)
            ? player : throw new InvalidOperationException("OperationHeldRelicSourceChanged");
    }

    private static Player[] PriorSharedStreamPlayers(RunState run, Player player, long sourceId, bool modern)
    {
        if (modern || run.Players.Count <= 1) return [];
        var source = player.Relics.Single(relic => Id(relic) == sourceId);
        if (source is PaelsTooth) return [];
        var order = run.CurrentRoom is CombatRoom { IsPreFinished: false } room && CombatManager.Instance.IsInProgress
            ? room.CombatState.Allies.Select(ally => ally.Player).OfType<Player>()
            : run.Players;
        return order.TakeWhile(seat => !ReferenceEquals(seat, player))
            .Where(seat => seat.Relics.Any(relic => !relic.IsMelted && relic.Id == source.Id)).ToArray();
    }

    private static string HeldRelicStamp(RunState run, Player player, Player[] peers, HeldRelicCombat combat, bool modern) => Hash(new
    {
        Room = run.CurrentRoom is { } room ? Id(room) : 0, Combat = combat,
        Players = peers.Append(player).Select(seat => new
        {
            seat.NetId,
            Deck = seat.Deck.Cards.Select(card => new
            {
                Id = Id(card), Value = card.ToSerializable(), Affliction = card.Affliction?.Id,
                Sort = modern && UsesBaseCardComparison(card) ? CardSortValues(card) : null
            }).ToArray(),
            Relics = seat.Relics.Select(relic => new { Id = Id(relic), Value = relic.ToSerializable() }).ToArray(),
            Rewards = seat.PlayerRng.Rewards.ToSerializable(),
            BingBong = seat.Relics.OfType<BingBong>().Select(relic => BingBongSkipCards(relic)?.Select(Id).Order().ToArray()).ToArray()
        }).ToArray(),
        Upgrades = (modern ? PredictorOperationCapture.NonCombatGeneration(player) : run.Rng.Niche).ToSerializable(),
        Hoarder = run.Modifiers.OfType<Hoarder>().Select(modifier => HoarderSkipCards(modifier)?.Select(Id).Order().ToArray()).ToArray()
    });

    private static HeldRelicCombat HeldRelicCombatPhase(RunState run) => run.CurrentRoom switch
    {
        not CombatRoom => HeldRelicCombat.None,
        CombatRoom { IsPreFinished: true } => HeldRelicCombat.None,
        _ when !CombatManager.Instance.IsInProgress => HeldRelicCombat.Transition,
        CombatRoom { RoomType: RoomType.Monster } => HeldRelicCombat.Monster,
        CombatRoom { RoomType: RoomType.Elite } => HeldRelicCombat.Elite,
        CombatRoom { RoomType: RoomType.Boss } => HeldRelicCombat.Boss,
        _ => HeldRelicCombat.None
    };
}
