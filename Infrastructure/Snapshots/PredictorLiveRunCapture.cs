using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.World.Snapshots;
using GameMapType = MegaCrit.Sts2.Core.Map.MapPointType;
using MapType = RolltheSpire2.Core.Prediction.Maps.MapPointType;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal sealed record PredictorLiveRunSnapshot(string Fingerprint, PredictorRun Run,
    int CurrentHp, int MaximumHp)
{
    // Set only by the native before-entry observer, never by a midpoint capture.
    internal MapPosition? EntryPoint { get; init; }
    internal int? EntryActIndex { get; init; }
    internal bool CanLeaveCurrentNode => Run.Context.Live!.CanLeaveCurrentNode;
    internal ImmutableArray<PredictorCombatPendingCause> PendingCauses => Run.Context.Live!.PendingCauses;
}

// Main-thread read-only adapter. Only copied values escape; no native lifecycle,
// room/reward generation, bag population or RNG draw is invoked.
internal static partial class PredictorLiveRunCapture
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private sealed record InstanceIdentity(long Id);
    private static readonly ConditionalWeakTable<object, InstanceIdentity> InstanceIds = new();
    private static long _lastInstanceId;
    internal static long Id(object model) => InstanceIds.GetValue(model,
        _ => new(Interlocked.Increment(ref _lastInstanceId))).Id;
    internal static ModelKey Key(AbstractModel model) => Key(model.Id);
    internal static ModelKey Key(ModelId id) => new(id.Category, id.Entry);
    internal static long NextInstanceId => Volatile.Read(ref _lastInstanceId) + 1;
    private static bool Modded(AbstractModel model) => model.GetType().Assembly != typeof(CardModel).Assembly;
    private static T Field<T>(object owner, string name) => (T)(owner.GetType().GetField(name, Members)?.GetValue(owner)
        ?? throw new InvalidOperationException("PredictorLiveFieldMissing:" + owner.GetType().Name + "." + name));
    private static object? NullableField(object owner, string name)
    {
        var field = owner.GetType().GetField(name, Members)
            ?? throw new InvalidOperationException("PredictorLiveFieldMissing:" + owner.GetType().Name + "." + name);
        return field.GetValue(owner);
    }
    // The native lazy getter initializes the real relic; read its backing field
    // without confusing a changed field type with the legitimate null state.
    internal static HashSet<CardModel>? BingBongSkipCards(object relic)
    {
        object? value = NullableField(relic, "_cardsToSkip");
        return value switch
        {
            null => null,
            HashSet<CardModel> cards => cards,
            _ => throw new InvalidCastException($"PredictorLiveFieldTypeMismatch:{relic.GetType().FullName}._cardsToSkip;expected={typeof(HashSet<CardModel>).FullName};actual={value.GetType().FullName}")
        };
    }
    private static T Property<T>(object owner, string name) => (T)(owner.GetType().GetProperty(name, Members)?.GetValue(owner)
        ?? throw new InvalidOperationException("PredictorLivePropertyMissing:" + owner.GetType().Name + "." + name));
    internal static MapPosition Position(MegaCrit.Sts2.Core.Map.MapCoord coord) => new(coord.col, coord.row);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));

    // canLeaveCurrentNode is the observed native map travel flag (and false
    // while travel is running), not a guess based on AbstractRoom.IsPreFinished.
    // When true, prediction starts with the premise that the player leaves the
    // current room now, without making further purchases/rest/event actions.
    internal static PredictorLiveRunSnapshot Capture(RunState run, string version, bool canLeaveCurrentNode)
        => Capture(run, version, canLeaveCurrentNode, null, null);

    internal static PredictorLiveRunSnapshot Capture(RunState run, string version, bool canLeaveCurrentNode,
        PredictorLiveRunSnapshot? previous, PredictorOperationCapture.CaptureTiming? timing, bool captureOpeningOptions = false)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ArgumentNullException.ThrowIfNull(run);
        if (run.Players.Count != 1) throw new NotSupportedException("PredictorLiveMultiplayerRoutePending");
        var player = LocalContext.GetMe(run) ?? throw new InvalidOperationException("PredictorLiveLocalPlayerMissing");
        if (!run.Players.Contains(player)) throw new InvalidOperationException("PredictorLivePlayerMismatch");
        if (run.CurrentActIndex < 0 || run.CurrentActIndex >= run.Acts.Count)
            throw new InvalidOperationException("PredictorLiveActMissing");
        string before = Fingerprint(run, canLeaveCurrentNode);
        var unlocks = player.UnlockState.ToSerializable();
        var discovered = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.DiscoveredActs.Select(Key).ToImmutableArray();
        var authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
            RolltheSpire2.Compatibility.Beta111Profile.Instance, run.Rng.StringSeed,
            CharacterIdentity.FromKey(Key(player.Character)), run.AscensionLevel, version,
            predictionGameMode: WorldGameMode.Singleplayer,
            predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest,
            explicitUnlockState: player.UnlockState, explicitDiscoveredActs: discovered);
        // Only immutable native canonical upgrade metadata may outlive a
        // capture. Every pool, authority and live value is read at this entry.
        var levels = new CatalogLevels(previous?.Run, timing);
        var pools = new CrystalSphereCatalogCapture.CapturePools(run, player);
        var (catalog, unmodeled) = CrystalSphereCatalogCapture.Capture(run, player, pools, levels.Capture);
        catalog = CompleteCatalog(catalog, run, player, pools);
        var context = new PredictorContext(run.Rng.StringSeed, version, Key(player.Character), run.AscensionLevel,
            Hash(catalog), Hash(unlocks), unlocks.UnlockedEpochs.ToImmutableArray(),
            unlocks.EncountersSeen.Select(Key).ToImmutableArray(), discovered, unlocks.NumberOfRuns)
        {
            Catalog = catalog,
            AncientAuthority = authority.WorldAuthority?.Beta109Generation,
            OpeningEligibleRelics = authority.EffectAuthority?.BonesEligibleRelics?.ToImmutableArray() ?? [],
            AccountWongoPoints = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.WongoPoints,
            Modifiers = run.Modifiers.Select(m => new PredictorRunModifier(Key(m),
                m is MegaCrit.Sts2.Core.Models.Modifiers.CharacterCards cc ? Key(cc.CharacterModel) : null)).ToImmutableArray(),
            PotionPool = MegaCrit.Sts2.Core.Factories.PotionFactory.GetPotionOptions(player).Select(p =>
                new PredictorPotionOption(Key(p), Enum.Parse<PredictorPotionRarity>(p.Rarity.ToString()), p.CanBeGeneratedInCombat)).ToImmutableArray()
        };
        var cards = player.Deck.Cards.Select(c => Card(c, catalog)).ToImmutableArray();
        var extraCards = new List<PredictorCard>();
        long CardId(CardModel c)
        {
            long id = Id(c);
            if (!cards.Any(x => x.Id == id) && !extraCards.Any(x => x.Id == id))
                extraCards.Add(Card(c, catalog) with { Removed = true });
            return id;
        }
        var relics = player.Relics.Select(r => Relic(r, catalog, CardId)).ToImmutableArray();
        var potions = player.PotionSlots.Select(p => p == null ? null : new PredictorPotion(Id(p), Key(p))).ToImmutableArray();
        var odds = player.PlayerOdds.ToSerializable();
        var unknown = run.Odds.ToSerializable();
        var streams = Enum.GetValues<PredictorStream>().Select(s => CrystalSphereLiveCapture.RngState(s, s switch
        {
            PredictorStream.Rewards => player.PlayerRng.Rewards,
            PredictorStream.Shops => player.PlayerRng.Shops,
            PredictorStream.Transformations => player.PlayerRng.Transformations,
            PredictorStream.UpFront => run.Rng.UpFront,
            PredictorStream.Niche => run.Rng.Niche,
            PredictorStream.UnknownMapPoint => run.Rng.UnknownMapPoint,
            PredictorStream.TreasureRoomRelics => run.Rng.TreasureRoomRelics,
            PredictorStream.CombatPotionGeneration => run.Rng.CombatPotionGeneration,
            _ => throw new InvalidOperationException("PredictorLiveStreamUnmapped")
        })).ToImmutableArray();
        int act = run.CurrentActIndex + 1;
        string node = run.CurrentMapCoord is { } coord
            ? $"map:{act}:{coord.row}:{coord.col}:{run.TotalFloor}" : $"map:{act}:entry";
        var map = Map(run);
        var visited = run.VisitedMapCoords.Select(Position).ToImmutableArray();
        var (visits, previousVisits) = Visits(run, visited);
        var hoarder = run.Modifiers.OfType<MegaCrit.Sts2.Core.Models.Modifiers.Hoarder>().SingleOrDefault();
        var hoarderIds = hoarder == null ? ImmutableArray<long>.Empty
            : Field<HashSet<CardModel>>(hoarder, "_cardsToSkip").Select(CardId).ToImmutableArray();
        var pending = ImmutableArray.CreateBuilder<PredictorCombatPendingCause>();
        if (!canLeaveCurrentNode)
            pending.Add(new("LiveCurrentNodePending", "The current native room has not reached its departure boundary.",
                PredictorCombatDomain.Continuation) { NodeId = node });
        if (version != "0.111.0")
            pending.Add(new("LiveGameVersionUnverified", "Detached source behavior has not been verified for " + version,
                PredictorCombatDomain.Deck | PredictorCombatDomain.Relics | PredictorCombatDomain.Gold |
                PredictorCombatDomain.RewardsRng | PredictorCombatDomain.NicheRng | PredictorCombatDomain.ShopsRng |
                PredictorCombatDomain.TreasureRng | PredictorCombatDomain.CombatPotionRng | PredictorCombatDomain.Odds |
                PredictorCombatDomain.TransformationsRng | PredictorCombatDomain.UpFrontRng | PredictorCombatDomain.UnknownMapPointRng |
                PredictorCombatDomain.Health | PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Map |
                PredictorCombatDomain.CardRewards | PredictorCombatDomain.GoldRewards |
                PredictorCombatDomain.PotionRewards | PredictorCombatDomain.RelicRewards |
                PredictorCombatDomain.InstanceIds | PredictorCombatDomain.ReturnedCards |
                PredictorCombatDomain.ExtraCardRewards | PredictorCombatDomain.ExtraGoldRewards |
                PredictorCombatDomain.RemoveCardRewards) { NodeId = node });
        // Absence of an implemented hook is an observation dependency, not a
        // blanket admission rule based on game mode or a vanilla character ID.
        var activeUnmodeled = unmodeled.Where(k => k == Key(player.Character) ||
            run.Modifiers.Any(m => Key(m) == k) || player.Relics.Any(r => !r.IsMelted && Key(r) == k) ||
            player.Deck.Cards.Any(c => Key(c) == k || c.Enchantment != null && Key(c.Enchantment) == k));
        foreach (var key in activeUnmodeled)
            pending.Add(new("LiveSourceHooksPending:" + key.Serialized, "Detached hooks are unavailable for " + key.Serialized,
                PredictorCombatDomain.Deck | PredictorCombatDomain.Relics | PredictorCombatDomain.Gold |
                PredictorCombatDomain.Health | PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Map |
                PredictorCombatDomain.RewardsRng | PredictorCombatDomain.NicheRng | PredictorCombatDomain.ShopsRng |
                PredictorCombatDomain.TreasureRng | PredictorCombatDomain.CombatPotionRng |
                PredictorCombatDomain.TransformationsRng | PredictorCombatDomain.UpFrontRng | PredictorCombatDomain.UnknownMapPointRng |
                PredictorCombatDomain.Odds | PredictorCombatDomain.CardRewards | PredictorCombatDomain.GoldRewards |
                PredictorCombatDomain.PotionRewards | PredictorCombatDomain.RelicRewards |
                PredictorCombatDomain.InstanceIds | PredictorCombatDomain.ReturnedCards |
                PredictorCombatDomain.ExtraCardRewards | PredictorCombatDomain.ExtraGoldRewards |
                PredictorCombatDomain.RemoveCardRewards) { NodeId = node });
        context = context with { Live = new(act, canLeaveCurrentNode,
            PredictorRouteCauseEvidence.Normalize(pending)) { PreviousVisits = previousVisits } };
        var detachedRewards = CardRewardSubscriptions(player).Select(reward => new PredictorReward(Id(reward),
            PredictorRewardKind.Card, PredictorRewardOrigin.Event, default,
            reward.Cards.Select(c => Card(c, catalog)).ToImmutableArray())
            { SubscriptionOrder = Id(reward), CanSkip = reward.CanSkip, CanReroll = reward.CanReroll }).ToImmutableArray();
        var state = new PredictorState(Volatile.Read(ref _lastInstanceId) + 1, new(act, run.TotalFloor, node), player.Gold,
            cards.AddRange(extraCards), cards.Select(c => c.Id).ToImmutableArray(), relics, potions, streams,
            new(odds.CardRarityOddsValue, odds.PotionRewardOddsValue), Bag(player.RelicGrabBag), Bag(run.SharedRelicGrabBag))
        {
            Acts = run.Acts.Select((a, i) => Act(a, i + 1)).ToImmutableArray(),
            Maps = [new(act, map, visited)], Visits = visits,
            StartedWithNeow = run.ExtraFields.StartedWithNeow,
            OpeningCompleted = !run.ExtraFields.StartedWithNeow || run.TotalFloor > 0,
            VisitedEvents = run.VisitedEventIds.Select(Key).OrderBy(k => k.Serialized, StringComparer.Ordinal).ToImmutableArray(),
            UnknownOdds = new(unknown.UnknownMapPointMonsterOddsValue, unknown.UnknownMapPointEliteOddsValue,
                unknown.UnknownMapPointTreasureOddsValue, unknown.UnknownMapPointShopOddsValue),
            CombatCount = run.MapPointHistory.SelectMany(h => h).SelectMany(h => h.Rooms)
                .Count(r => r.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss),
            CurrentRoomIsCombat = run.CurrentRoom is CombatRoom,
            DetachedCardRewards = detachedRewards,
            CardShopRemovalsUsed = player.ExtraFields.CardShopRemovalsUsed,
            WongoPoints = player.ExtraFields.WongoPoints, HoarderCardsToSkip = hoarderIds
        };
        if (captureOpeningOptions && !state.OpeningCompleted && context.Modifiers.IsEmpty)
        {
            // The native first-entry prefix precedes Neow option generation.
            // Reuse the seed predictor's pure source algorithm and copied
            // eligibility; never generate offers on the real native event.
            if (authority.VanillaNeowCatalogExact != true)
                throw new InvalidOperationException("PredictorOpeningCatalogMissing");
            var opening = RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(
                context.Seed, authority, RolltheSpire2.Compatibility.Beta111Profile.Instance, true);
            if (opening.UnknownEligibilityAffectedPool)
                throw new InvalidOperationException("PredictorOpeningEligibilityMissing");
            state = state with { OpeningOptions = opening.RelicKeys.ToImmutableArray() };
        }
        var detached = PredictorLiveInitialization.FromSnapshot(context, state);
        if (Fingerprint(run, canLeaveCurrentNode) != before) throw new InvalidOperationException("PredictorLiveSnapshotChanged");
        levels.Remember(detached);
        if (timing != null)
        {
            timing.PoolReads = pools.Reads; timing.PoolReuses = pools.Reuses; timing.PoolProjectionReuses = pools.ProjectionReuses;
        }
        return new(before, detached, player.Creature.CurrentHp, player.Creature.MaxHp);
    }

    internal static string Fingerprint(RunState run, bool canLeaveCurrentNode)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var player = LocalContext.GetMe(run) ?? throw new InvalidOperationException("PredictorLiveLocalPlayerMissing");
        return Hash(new
        {
            run.Rng.StringSeed, run.CurrentActIndex, run.TotalFloor, run.CurrentMapCoord, canLeaveCurrentNode,
            run.CurrentRoomCount, Room = run.CurrentRoom?.GetType().FullName, Model = run.CurrentRoom?.ModelId,
            Player = player.ToSerializable(), Rng = run.Rng.ToSerializable(), Odds = run.Odds.ToSerializable(),
            CardIds = player.Deck.Cards.Select(Id).ToArray(), RelicIds = player.Relics.Select(Id).ToArray(),
            PotionIds = player.PotionSlots.Select(p => p == null ? 0 : Id(p)).ToArray(),
            CardRuntime = player.Deck.Cards.Select(c => new
                { BaseCost = Field<int>(c.EnergyCost, "_base"),
                    Keywords = c.GetKeywordsWithSources(KeywordSources.Local).OrderBy(k => (int)k).ToArray(),
                    SpoilsCoord = c is SpoilsMap spoils ? spoils.SpoilsCoord : null }).ToArray(),
            PersonalBag = Bag(player.RelicGrabBag), SharedBag = Bag(run.SharedRelicGrabBag),
            Acts = run.Acts.Select(a => a.ToSave()).ToArray(), Map = Map(run), Visited = run.VisitedMapCoords.ToArray(),
            History = run.MapPointHistory, Events = run.VisitedEventIds.OrderBy(k => k.ToString(), StringComparer.Ordinal).ToArray(),
            Extra = run.ExtraFields.ToSerializable(), Modifiers = run.Modifiers.Select(m => m.ToSerializable()).ToArray(),
            AccountWongoPoints = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.WongoPoints,
            DiscoveredActs = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.DiscoveredActs.Select(Key).ToArray(),
            // These lifetime sets are not saved properties. Read backing fields
            // rather than lazy getters which create a set on the real relic.
            Bing = player.Relics.OfType<BingBong>().Select(r => BingBongSkipCards(r)?
                .Select(Id).Order().ToArray()).ToArray(),
            Hoarder = run.Modifiers.OfType<MegaCrit.Sts2.Core.Models.Modifiers.Hoarder>()
                .Select(m => Field<HashSet<CardModel>>(m, "_cardsToSkip").Select(Id).Order().ToArray()).ToArray(),
            CardSubscriptions = CardRewardSubscriptions(player).Select(r => new
                { Id = Id(r), Cards = r.Cards.Select(c => c.ToSerializable()).ToArray(), r.CanSkip, r.CanReroll }).ToArray()
        });
    }

    internal static PredictorCard Card(CardModel card, PredictorCatalog catalog)
    {
        var keywords = card.GetKeywordsWithSources(KeywordSources.Local);
        int localBase = Field<int>(card.EnergyCost, "_base");
        int? normalCost = catalog.CardDefinitions.FirstOrDefault(d => d.Prototype.Key == Key(card))?.Levels
            .FirstOrDefault(l => l.UpgradeLevel == card.CurrentUpgradeLevel)?.LocalCost;
        return new(Id(card), Key(card), Enum.TryParse<EffectCardType>(card.Type.ToString(), out var type) ? type : EffectCardType.Other,
            card.CurrentUpgradeLevel, card.MaxUpgradeLevel, !keywords.Contains(CardKeyword.Eternal), card.FloorAddedToDeck ?? 0,
            card.Enchantment == null ? null : Key(card.Enchantment), card.Enchantment?.Amount ?? 0,
            card is GeneticAlgorithm ga ? ga.IncreasedBlock : 0, card is TheScythe scythe ? scythe.IncreasedDamage : 0)
        {
            HasEternalKeyword = keywords.Contains(CardKeyword.Eternal), HasExhaustKeyword = keywords.Contains(CardKeyword.Exhaust),
            HasInnateKeyword = keywords.Contains(CardKeyword.Innate), HasRetainKeyword = keywords.Contains(CardKeyword.Retain),
            PermanentCostOverride = normalCost == localBase ? null : localBase,
            RoomsEntered = card is Dowsing dowsing ? dowsing.RoomsEntered : 0,
            CombatsSeen = card is Guilty guilty ? guilty.CombatsSeen : 0,
            SpoilsActIndex = card is SpoilsMap spoils ? spoils.SpoilsActIndex : -1,
            SpoilsCoord = card is SpoilsMap { SpoilsCoord: { } sc } ? Position(sc) : null,
            TinkerTimeType = card is MadScience science ? Enum.Parse<EffectCardType>(science.TinkerTimeType.ToString()) : EffectCardType.Skill,
            TinkerTimeRider = card is MadScience rider ? Enum.Parse<PredictorMadScienceRider>(rider.TinkerTimeRider.ToString()) : PredictorMadScienceRider.None
        };
    }

    internal static PredictorRelic Relic(RelicModel relic, PredictorCatalog catalog, Func<CardModel, long> cardId)
    {
        var result = new PredictorRelic(Id(relic), Key(relic), relic.FloorAddedToDeck, relic.IsMelted) { IsWax = relic.IsWax };
        return relic.Id.Entry switch
        {
            "LASTING_CANDY" => result with { CombatRewardsSeen = Property<int>(relic, "CombatRewardsSeen") },
            "WONGOS_MYSTERY_TICKET" => result with { CombatsFinished = Property<int>(relic, "CombatsFinished"), GaveRelic = Property<bool>(relic, "GaveRelic") },
            "LAVA_ROCK" => result with { LavaRockTriggered = Property<bool>(relic, "HasTriggered") },
            "PAELS_WING" => result with { RewardsSacrificed = Property<int>(relic, "RewardsSacrificed") },
            "TOY_BOX" => result with { ToyBoxCombatsSeen = Property<int>(relic, "CombatsSeen") },
            "FISHING_ROD" => result with { FishingRodCombatsSeen = Property<int>(relic, "CombatsSeen") },
            "SWORD_OF_STONE" => result with { SwordElitesDefeated = Property<int>(relic, "ElitesDefeated") },
            "BOOK_OF_FIVE_RINGS" => result with { BookOfFiveRingsCardsAdded = Property<int>(relic, "CardsAdded") },
            "SILVER_CRUCIBLE" => result with { RewardUpgradeUses = Property<int>(relic, "TimesUsed"), TreasureRoomsEntered = Property<int>(relic, "TreasureRoomsEntered") },
            "SILKEN_TRESS" => result with { SilkenUsed = Field<bool>(relic, "_isUsed") },
            "GIRYA" => result with { TimesLifted = Property<int>(relic, "TimesLifted") },
            "WINGED_BOOTS" => result with { TimesUsed = Property<int>(relic, "TimesUsed") },
            "GOLDEN_COMPASS" => result with { GoldenPathAct = Property<int>(relic, "GoldenPathAct") },
            "MAW_BANK" => result with { MawBankSpent = relic.IsUsedUp },
            "LIZARD_TAIL" => result with { LizardTailUsed = Property<bool>(relic, "WasUsed") },
            "EMBER_TEA" or "BONE_TEA" or "TEA_OF_DISCOURTESY" => result with { CombatsLeft = Property<int>(relic, "CombatsLeft") },
            "VENERABLE_TEA_SET" or "FAKE_VENERABLE_TEA_SET" => result with { GainEnergyInNextCombat = Property<bool>(relic, "GainEnergyInNextCombat") },
            "PUMPKIN_CANDLE" => result with { KindleCount = Property<int>(relic, "KindleCount") },
            "BING_BONG" => result with { BingBongCardsToSkip = (BingBongSkipCards(relic) ?? []).Select(cardId).ToImmutableArray() },
            "FUR_COAT" => result with { FurCoatAct = Property<int>(relic, "FurCoatActIndex"),
                FurCoatCoordsSet = Property<bool>(relic, "FurCoatCoordsSet"),
                FurCoatQuestCoords = ((FurCoat)relic).GetMarkedCoords()?.Select(Position).ToImmutableArray() ?? [] },
            "PAELS_TOOTH" => result with { StoredCards = ((PaelsTooth)relic).SerializableCards.Select(c => StoredCard(c, catalog)).ToImmutableArray() },
            _ => result
        };
    }

    private static PredictorCard StoredCard(SerializableCard card, PredictorCatalog catalog)
    {
        var key = Key(card.Id ?? throw new InvalidOperationException("PredictorLiveStoredCardIdentityMissing"));
        var definition = catalog.CardDefinitions.Single(d => d.Prototype.Key == key);
        var level = definition.Levels.Single(l => l.UpgradeLevel == card.CurrentUpgradeLevel);
        int SavedInt(string name) => card.Props?.ints?.FirstOrDefault(p => p.name == name).value ?? 0;
        var tinkerType = key.Entry == "MAD_SCIENCE"
            ? Enum.Parse<EffectCardType>(((CardType)SavedInt("TinkerTimeType")).ToString()) : EffectCardType.Skill;
        return definition.Prototype with { Id = 0, UpgradeLevel = card.CurrentUpgradeLevel,
            FloorAdded = card.FloorAddedToDeck ?? 0, Enchantment = card.Enchantment?.Id is { } enchantment ? Key(enchantment) : null,
            EnchantmentAmount = card.Enchantment?.Amount ?? 0, IncreasedBlock = SavedInt("IncreasedBlock"), IncreasedDamage = SavedInt("IncreasedDamage"),
            RoomsEntered = SavedInt("RoomsEntered"), CombatsSeen = SavedInt("CombatsSeen"),
            SpoilsActIndex = key.Entry == "SPOILS_MAP" ? SavedInt("SpoilsActIndex") : -1,
            Type = key.Entry == "MAD_SCIENCE" ? tinkerType : definition.Prototype.Type,
            TinkerTimeType = tinkerType,
            TinkerTimeRider = key.Entry == "MAD_SCIENCE" ? (PredictorMadScienceRider)SavedInt("TinkerTimeRider") : PredictorMadScienceRider.None,
            HasEternalKeyword = level.Eternal, HasExhaustKeyword = level.LocalExhaust, HasInnateKeyword = level.Innate, HasRetainKeyword = level.Retain };
    }

    internal static PredictorActState Act(ActModel act, int ordinal)
    {
        var rooms = act.ToSave().SerializableRooms;
        return new(ordinal, Key(act), rooms.AncientId is { } ancient ? Key(ancient) : default,
            new[] { rooms.BossId, rooms.SecondBossId }.OfType<ModelId>().Select(Key).ToImmutableArray(),
            rooms.NormalEncounterIds.Select(Key).ToImmutableArray(), rooms.EliteEncounterIds.Select(Key).ToImmutableArray(),
            rooms.EventIds.Select(Key).ToImmutableArray(), rooms.NormalEncountersVisited, rooms.EliteEncountersVisited,
            rooms.EventsVisited, rooms.BossEncountersVisited);
    }

    internal static MapPrediction Map(RunState run)
    {
        var points = run.Map.GetAllMapPoints().Append(run.Map.StartingMapPoint).Append(run.Map.BossMapPoint);
        if (run.Map.SecondBossMapPoint is { } second) points = points.Append(second);
        return new(run.Rng.StringSeed, Key(run.Act), run.CurrentActIndex, run.Map.GetColumnCount(),
            Array.AsReadOnly(points.DistinctBy(p => p.coord).Select(p => new MapPredictionPoint(Position(p.coord),
                (MapType)(int)p.PointType, p.CanBeModified,
                Array.AsReadOnly(p.Children.Select(c => Position(c.coord)).ToArray()))).ToArray()), 0);
    }

    internal static (ImmutableArray<PredictorVisit> Visits, ImmutableArray<PredictorVisitFact> Previous) Visits(
        RunState run, ImmutableArray<MapPosition> visited)
    {
        var history = run.MapPointHistory.ElementAtOrDefault(run.CurrentActIndex);
        var previous = run.MapPointHistory.Take(run.CurrentActIndex).SelectMany((h, index) => h.Select(entry => VisitFact(entry, index))).ToImmutableArray();
        if (history == null)
        {
            if (!visited.IsEmpty) throw new InvalidOperationException("PredictorLiveRoomHistoryMissing");
            return ([], previous);
        }
        bool aligned = history.Count == visited.Length;
        // Map replacement/debug travel can discard old coordinates. The last
        // coordinate and current history entry remain native facts; earlier
        // room facts retain their counts without guessing coordinate pairs.
        int captured = aligned ? history.Count : run.CurrentMapCoord == null || history.Count == 0 ? 0 : 1;
        previous = previous.AddRange(history.Take(history.Count - captured).Select(entry => VisitFact(entry, run.CurrentActIndex)));
        int offset = run.TotalFloor - captured;
        var facts = history.Skip(history.Count - captured).Select((entry, i) =>
        {
            var fact = VisitFact(entry, run.CurrentActIndex);
            return new PredictorVisit(run.CurrentActIndex + 1, aligned ? visited[i] : Position(run.CurrentMapCoord!.Value),
                fact.PointType, fact.Room, entry.Rooms[0].ModelId is { } model ? Key(model) : null, offset + i + 1)
                { CombatKinds = fact.CombatKinds };
        }).ToImmutableArray();
        return (facts, previous);
    }

    private static PredictorVisitFact VisitFact(MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry entry, int actIndex)
    {
        var first = entry.Rooms.FirstOrDefault() ?? throw new InvalidOperationException("PredictorLiveRoomHistoryMissing");
        var type = first.RoomType switch
        {
            RoomType.Monster => PredictorRoomKind.Monster, RoomType.Elite => PredictorRoomKind.Elite,
            RoomType.Boss => PredictorRoomKind.Boss, RoomType.RestSite => PredictorRoomKind.RestSite,
            RoomType.Shop => PredictorRoomKind.Shop, RoomType.Treasure => PredictorRoomKind.Treasure,
            RoomType.Event when entry.MapPointType == GameMapType.Ancient => actIndex == 0 ? PredictorRoomKind.Opening : PredictorRoomKind.Ancient,
            RoomType.Event => PredictorRoomKind.Event,
            _ => throw new InvalidOperationException("PredictorLiveRoomHistoryTypeUnknown:" + first.RoomType)
        };
        return new((MapType)(int)entry.MapPointType, type, entry.Rooms.Where(r => r.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss)
            .Select(r => r.RoomType == RoomType.Elite ? PredictorCombatKind.Elite : r.RoomType == RoomType.Boss ? PredictorCombatKind.Boss : PredictorCombatKind.Monster).ToImmutableArray());
    }

    internal static IEnumerable<CardReward> CardRewardSubscriptions(Player player) =>
        (NullableField(player, "RelicObtained") as Delegate)?.GetInvocationList().Select(d => d.Target)
            .OfType<CardReward>().Distinct() ?? [];

    internal static PredictorRelicBag Bag(RelicGrabBag bag)
    {
        var original = (List<RelicModel>?)NullableField(bag, "_originalRelics");
        return new(Field<bool>(bag, "_refreshAllowed"), (original ?? []).Select(r => new PredictorBagEntry(Key(r), r.Rarity.ToString())).ToImmutableArray(),
            Field<Dictionary<RelicRarity, List<RelicModel>>>(bag, "_deques").Select(d =>
                new PredictorBagBucket(d.Key.ToString(), d.Value.Select(Key).ToImmutableArray())).ToImmutableArray(),
            Field<List<RelicModel>>(bag, "_mpFallbackDequeue").Select(Key).ToImmutableArray()) { HasOriginalCatalog = original != null };
    }

    internal static PredictorCatalog CompleteCatalog(PredictorCatalog catalog, RunState run, Player player,
        CrystalSphereCatalogCapture.CapturePools? pools = null)
    {
        var unlocks = player.UnlockState;
        var constraint = ((IRunState)run).CardMultiplayerConstraint;
        IEnumerable<CardModel> Pool(CardPoolModel pool) => pools == null
            ? pool.GetUnlockedCards(unlocks, constraint) : pools.Read(pool);
        var definitions = catalog.CardDefinitions.Select(d =>
        {
            var canonical = ModelDb.GetById<CardModel>(new(d.Prototype.Key.Category, d.Prototype.Key.Entry));
            var keywords = canonical.GetKeywordsWithSources(KeywordSources.Local);
            return d with { Prototype = d.Prototype with
            {
                SpoilsActIndex = canonical.Id.Entry == "SPOILS_MAP" ? 1 : -1,
                HasExhaustKeyword = keywords.Contains(CardKeyword.Exhaust), HasEternalKeyword = keywords.Contains(CardKeyword.Eternal),
                HasInnateKeyword = keywords.Contains(CardKeyword.Innate), HasRetainKeyword = keywords.Contains(CardKeyword.Retain)
            } };
        }).ToImmutableArray();
        var curses = Pool(ModelDb.CardPool<CurseCardPool>()).Where(c => c.CanBeGeneratedByModifiers).Select(Key).ToImmutableArray();
        EncounterModel[] extras = [ModelDb.Encounter<DenseVegetationEventEncounter>(), ModelDb.Encounter<PunchOffEventEncounter>(),
            ModelDb.Encounter<MysteriousKnightEventEncounter>(), ModelDb.Encounter<BattlewornDummyEventV1Encounter>(),
            ModelDb.Encounter<BattlewornDummyEventV2Encounter>(), ModelDb.Encounter<BattlewornDummyEventV3Encounter>(), ModelDb.Encounter<FakeMerchantEventEncounter>()];
        return catalog with
        {
            CardDefinitions = definitions,
            Encounters = ModelDb.AllEncounters.Concat(extras).DistinctBy(e => e.Id).Select(e =>
            {
                var range = e.RoomType switch { RoomType.Monster => (10, 20), RoomType.Elite => (35, 45), RoomType.Boss => (100, 100), _ => (0, 0) };
                double factor = run.AscensionLevel >= 3 ? .75 : 1;
                int minimum = e.Id.Entry == "FAKE_MERCHANT_EVENT_ENCOUNTER" ? 300 : (int)(range.Item1 * factor);
                int maximum = e.Id.Entry == "FAKE_MERCHANT_EVENT_ENCOUNTER" ? 300 : (int)(range.Item2 * factor);
                return new PredictorEncounterDefinition(Key(e), minimum, maximum, e.ShouldGiveRewards)
                { IsMonsterEncounter = e.RoomType == RoomType.Monster,
                    EnemyCreationSources = PredictorCombatEnemyFacts.Sources(e.AllPossibleMonsters.Select(Key)),
                    CanStealCards = e.AllPossibleMonsters.Any(m => m.Id.Entry == "THIEVING_HOPPER") };
            }).ToImmutableArray(),
            TransformationPools = definitions.Select(d => d.TransformationPool).Distinct().Select(k => new PredictorCardPool(k,
                Pool(ModelDb.GetById<CardPoolModel>(new(k.Category, k.Entry))).Select(Key).ToImmutableArray())).ToImmutableArray(),
            UnlockedCurseCards = curses.OrderBy(k => k.Entry, StringComparer.Ordinal).ToImmutableArray(), ModifierCurseCards = curses,
            ShopRelics = ModelDb.AllRelics.Select(r => new PredictorShopRelicOption(Key(r), r.Rarity.ToString(), r.MerchantCost, r.IsAllowedInShops)).ToImmutableArray(),
            CharacterStrikeKey = catalog.CharacterCards.FirstOrDefault(c => c.IsStrike && c.IsBasic)?.CardKey,
            CharacterDefendKey = catalog.CharacterCards.FirstOrDefault(c => c.IsDefend && c.IsBasic)?.CardKey,
            StarterRelics = ModelDb.AllRelics.Where(r => r.Rarity == RelicRarity.Starter).Select(Key).ToImmutableArray(),
            EventPetRelics = ModelDb.AllRelics.Where(r => r.AddsPet).Select(Key).ToImmutableArray(),
            TradableRelicTypes = ModelDb.AllRelics.Where(r => !r.HasUponPickupEffect && !r.SpawnsPets &&
                r.Rarity is not (RelicRarity.Starter or RelicRarity.Event or RelicRarity.Ancient)).Select(Key).ToImmutableArray(),
            DustyTomeCards = Pool(player.Character.CardPool).Where(c => c.Rarity == CardRarity.Ancient &&
                !ArchaicTooth.TranscendenceCards.Contains(c)).Select(Key).ToImmutableArray()
        };
    }
}
