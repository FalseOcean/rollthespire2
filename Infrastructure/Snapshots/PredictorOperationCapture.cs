using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

// The operation boundary copies the current player and actual generated scene.
// It neither establishes route authority nor executes native lifecycle/hooks.
internal static partial class PredictorOperationCapture
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            // Operation DTOs intentionally keep extra phase fields internal.
            // Their copied values still participate in freshness, including
            // event substage and RNG overrides; no native object is serialized.
            if (!info.Type.Name.StartsWith("PredictorOperation", StringComparison.Ordinal) && info.Type != typeof(PredictorShopEntry)) return;
            foreach (var property in info.Type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (property.Name == "EqualityContract" || property.GetIndexParameters().Length != 0 || property.GetMethod == null ||
                    info.Properties.Any(p => p.Name == property.Name)) continue;
                var jsonProperty = info.CreateJsonPropertyInfo(property.PropertyType, property.Name);
                jsonProperty.Get = property.GetValue;
                info.Properties.Add(jsonProperty);
            }
        });
        // Optional scene arrays use default to mean "no active selection";
        // preserve that absence instead of asking the array converter to enumerate it.
        // An explicitly empty array remains serialized and has a distinct fingerprint.
        return new() { IncludeFields = true, TypeInfoResolver = resolver,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault };
    }

    internal static object? ReadNullable(object owner, string name)
    {
        for (Type? type = owner.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Members) is { } field) return field.GetValue(owner);
        throw new MissingFieldException(owner.GetType().FullName, name);
    }

    internal static T Read<T>(object owner, string name) => (T)(ReadNullable(owner, name)
        ?? throw new InvalidOperationException("OperationCaptureMissingValue:" + owner.GetType().Name + "." + name));

    internal static IEnumerable<T> Nodes<T>(Node? root) where T : Node
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (root == null) yield break;
        if (root is T found && found.IsInsideTree()) yield return found;
        foreach (Node child in root.GetChildren())
            foreach (var match in Nodes<T>(child)) yield return match;
    }

    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));

    internal static string SceneFingerprint(PredictorOperationScene scene) => Hash((object)scene);

    internal static PredictorOperationSnapshot Capture(RunState run, string version, Node? root = null)
        => Capture(run, version, root, false, out _);

    internal static PredictorOperationSnapshot Capture(RunState run, string version, Node? root, out string observationStamp)
        => Capture(run, version, root, true, out observationStamp);

    internal sealed class CaptureTiming
    {
        public double NativeStampMs, CatalogMs, CompleteCatalogMs, HashMs;
        public long AllocatedBytes;
        public int PoolReads, PoolReuses, PoolProjectionReuses;
        public int CatalogLevelReads, CatalogLevelReuses, SnapshotReuses;
        public int DirectPoolReads, ContentObservationReads;
    }

    internal static PredictorOperationSnapshot Capture(RunState run, string version, Node? root,
        out string observationStamp, CaptureTiming timing)
        => Capture(run, version, root, true, out observationStamp, timing);

    internal static PredictorOperationSnapshot Capture(RunState run, string version, Node? root, CaptureTiming timing)
        => Capture(run, version, root, false, out _, timing);

    private static PredictorOperationSnapshot Capture(RunState run, string version, Node? root,
        bool includeObservationStamp, out string observationStamp, CaptureTiming? timing = null,
        PredictorOperationSnapshot? previous = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ArgumentNullException.ThrowIfNull(run);
        root ??= MegaCrit.Sts2.Core.Nodes.NRun.Instance;
        var player = LocalContext.GetMe(run) ?? throw new InvalidOperationException("OperationLocalPlayerMissing");
        if (!run.Players.Contains(player)) throw new InvalidOperationException("OperationLocalPlayerMismatch");
        long allocated = timing == null ? 0 : GC.GetAllocatedBytesForCurrentThread();
        long stage = Stopwatch.GetTimestamp();
        string before = NativeStamp(run, player, root);
        if (timing != null) timing.NativeStampMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        var unlocks = player.UnlockState.ToSerializable();
        var pools = new CrystalSphereCatalogCapture.CapturePools(run, player);
        var levels = new CatalogLevels(previous, timing);
        stage = Stopwatch.GetTimestamp();
        var (catalog, _) = CrystalSphereCatalogCapture.Capture(run, player, pools, levels.Capture);
        if (timing != null) timing.CatalogMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        stage = Stopwatch.GetTimestamp();
        catalog = CompleteCatalog(catalog, run, player, pools);
        if (timing != null) timing.CompleteCatalogMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        var opaque = ModelDb.All.Where(m => m.GetType().Assembly != typeof(CardModel).Assembly).Select(Key)
            .Concat(player.Deck.Cards.Where(c => c.GetType().Assembly != typeof(CardModel).Assembly).Select(Key))
            .Concat(player.Relics.Where(r => r.GetType().Assembly != typeof(CardModel).Assembly).Select(Key))
            .Concat(run.Modifiers.Where(m => m.GetType().Assembly != typeof(CardModel).Assembly).Select(Key))
            .Concat(player.Deck.Cards.Where(c => c.Enchantment != null && c.Enchantment.GetType().Assembly != typeof(CardModel).Assembly)
                .Select(c => Key(c.Enchantment!)))
            .Concat(player.Character.GetType().Assembly != typeof(CardModel).Assembly ? [Key(player.Character)] : [])
            .Distinct().ToImmutableArray();
        var combatRng = run.Rng.CombatCardGeneration.ToSerializable();
        var visited = run.VisitedMapCoords.Select(Position).ToImmutableArray();
        var (visits, previousVisits) = Visits(run, visited);
        stage = Stopwatch.GetTimestamp();
        string catalogHash = Hash(catalog), unlockHash = Hash(unlocks);
        if (timing != null) timing.HashMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        var context = new PredictorContext(run.Rng.StringSeed, version, Key(player.Character), run.AscensionLevel,
            catalogHash, unlockHash, unlocks.UnlockedEpochs.ToImmutableArray(),
            unlocks.EncountersSeen.Select(Key).ToImmutableArray(), run.Acts.Select(Key).ToImmutableArray(), unlocks.NumberOfRuns)
        {
            Catalog = catalog,
            OpeningEligibleRelics = CaptureBonesPool(run, player, pools),
            Operation = new(player.NetId, run.Players.ToList().IndexOf(player), run.Players.Count, opaque)
            {
                CombatCardRng = new(combatRng.state0, combatRng.state1, combatRng.state2, combatRng.state3, combatRng.counter),
                PreviousVisits = previousVisits,
                CombatCardPools = CapturePotionPools(run, player, catalog, pools)
            },
            AccountWongoPoints = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.WongoPoints,
            Modifiers = run.Modifiers.Select(m => new PredictorRunModifier(Key(m),
                m is MegaCrit.Sts2.Core.Models.Modifiers.CharacterCards cc ? Key(cc.CharacterModel) : null)).ToImmutableArray(),
            PotionPool = MegaCrit.Sts2.Core.Factories.PotionFactory.GetPotionOptions(player).Select(p =>
                new PredictorPotionOption(Key(p), Enum.Parse<PredictorPotionRarity>(p.Rarity.ToString()), p.CanBeGeneratedInCombat)).ToImmutableArray()
        };
        var cards = player.Deck.Cards.Select(c => Card(c, catalog)).ToImmutableArray();
        var extras = new List<PredictorCard>();
        long CardId(CardModel card)
        {
            long id = Id(card);
            if (!cards.Any(c => c.Id == id) && !extras.Any(c => c.Id == id)) extras.Add(Card(card, catalog) with { Removed = true });
            return id;
        }
        var relics = player.Relics.Select(r => Relic(r, catalog, CardId)).ToImmutableArray();
        var hoarder = run.Modifiers.OfType<MegaCrit.Sts2.Core.Models.Modifiers.Hoarder>().SingleOrDefault();
        var hoarderIds = hoarder == null ? ImmutableArray<long>.Empty : Read<HashSet<CardModel>>(hoarder, "_cardsToSkip")
            .Select(CardId).ToImmutableArray();
        var scene = CaptureScene(run, player, catalog, root);
        if (HasPendingNativeObtain(player) && scene is not PredictorOperationObtainScene &&
            scene is not PredictorOperationShopScene { PendingRelic: not null })
            throw new NotSupportedException("OperationActiveObtainContinuationNotCaptured");
        var sceneRewardIds = SceneRewardIds(scene).ToHashSet();
        var detached = CardRewardSubscriptions(player).Where(r => !sceneRewardIds.Contains(Id(r)))
            .Select(r => CaptureReward(r, catalog)).ToImmutableArray();
        var odds = player.PlayerOdds.ToSerializable();
        var unknown = run.Odds.ToSerializable();
        var state = new PredictorState(NextInstanceId, new(run.CurrentActIndex + 1, run.TotalFloor,
            "operation:" + Id(run) + ":" + (run.CurrentRoom == null ? 0 : Id(run.CurrentRoom))), player.Gold,
            cards.AddRange(extras), cards.Select(c => c.Id).ToImmutableArray(), relics,
            player.PotionSlots.Select(p => p == null ? null : new PredictorPotion(Id(p), Key(p))).ToImmutableArray(),
            Streams(run, player), new(odds.CardRarityOddsValue, odds.PotionRewardOddsValue), Bag(player.RelicGrabBag), Bag(run.SharedRelicGrabBag))
        {
            Acts = run.Acts.Select((a, i) => Act(a, i + 1)).ToImmutableArray(),
            Maps = run.Map == null ? [] : [new(run.CurrentActIndex + 1, Map(run), visited)], Visits = visits,
            OpeningCompleted = run.TotalFloor > 0, StartedWithNeow = run.ExtraFields.StartedWithNeow,
            CurrentRoomIsCombat = run.CurrentRoom is CombatRoom, DetachedCardRewards = detached,
            CardShopRemovalsUsed = player.ExtraFields.CardShopRemovalsUsed, WongoPoints = player.ExtraFields.WongoPoints,
            HoarderCardsToSkip = hoarderIds,
            VisitedEvents = run.VisitedEventIds.Select(Key).ToImmutableArray(),
            UnknownOdds = new(unknown.UnknownMapPointMonsterOddsValue, unknown.UnknownMapPointEliteOddsValue,
                unknown.UnknownMapPointTreasureOddsValue, unknown.UnknownMapPointShopOddsValue),
            CombatCount = run.MapPointHistory.SelectMany(h => h).SelectMany(h => h.Rooms)
                .Count(r => r.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss)
        };
        // Scene and slot copying may allocate stable native identity numbers.
        state = state with { NextInstanceId = NextInstanceId };
        state.Validate();
        stage = Stopwatch.GetTimestamp();
        if (NativeStamp(run, player, root) != before) throw new InvalidOperationException("OperationSnapshotChanged");
        if (timing != null) timing.NativeStampMs += Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        stage = Stopwatch.GetTimestamp();
        // The complete catalog has already been serialized into CatalogFingerprint.
        // Keep that digest in the context identity without serializing its full
        // payload a second time. Publication still captures/hashes it anew.
        string fingerprint = Hash(new { Native = before, Context = context with { Catalog = null }, Operation = context.Operation, State = state, Scene = (object)scene,
            Hp = player.Creature.CurrentHp, MaxHp = player.Creature.MaxHp });
        // These are exactly ObservationStamp's inputs, already read and checked
        // by this capture. Do not traverse/copy the same native scene again.
        observationStamp = includeObservationStamp ? Hash(new { Native = before, Scene = (object)scene }) : "";
        if (timing != null)
        {
            timing.HashMs += Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
            timing.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            timing.PoolReads = pools.Reads; timing.PoolReuses = pools.Reuses; timing.PoolProjectionReuses = pools.ProjectionReuses;
        }
        var snapshot = new PredictorOperationSnapshot(fingerprint, context, state, scene, player.Creature.CurrentHp, player.Creature.MaxHp)
        {
            SelectedCardIds = Nodes<NCardGridSelectionScreen>(root).Where(n => n.IsVisibleInTree())
                .SelectMany(n => Read<IEnumerable<CardModel>>(n, "_selectedCards")).Select(Id).ToImmutableArray()
        };
        levels.Remember(snapshot);
        return snapshot;
    }

    internal static bool IsCurrent(PredictorOperationSnapshot snapshot, RunState run, string version, Node? root = null) =>
        Capture(run, version, root, false, out _, previous: snapshot).Fingerprint == snapshot.Fingerprint;

    internal static bool IsCurrent(PredictorOperationSnapshot snapshot, RunState run, string version, Node? root, CaptureTiming timing) =>
        Capture(run, version, root, false, out _, timing, snapshot).Fingerprint == snapshot.Fingerprint;

    internal static PredictorOperationPotionPools CapturePotionPools(RunState run, Player player, PredictorCatalog catalog,
        CrystalSphereCatalogCapture.CapturePools? pools = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ImmutableArray<PredictorOperationPotionCard> Copy(IEnumerable<CardModel> pool) => pool.Select(c =>
            new PredictorOperationPotionCard(Card(c, catalog) with { Id = 0 }, c.Rarity.ToString(), c.CanBeGeneratedInCombat,
                c.MultiplayerConstraint.ToString())).ToImmutableArray();
        IEnumerable<CardModel> Pool(CardPoolModel pool) => pools == null
            ? pool.GetUnlockedCards(player.UnlockState, ((IRunState)run).CardMultiplayerConstraint) : pools.Read(pool);
        return new(Copy(Pool(player.Character.CardPool)), Copy(Pool(ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>())));
    }

    private static IEnumerable<long> SceneRewardIds(PredictorOperationScene scene) => scene switch
    {
        PredictorOperationRewardsScene rewards => rewards.Rewards.Select(r => r.Id),
        PredictorOperationEventScene ev => ev.RewardScene?.Rewards.Select(r => r.Id) ?? [],
        PredictorOperationRestRewardsScene rest => rest.Rewards.Rewards.Select(r => r.Id),
        PredictorOperationObtainScene obtain => SceneRewardIds(obtain.Parent).Concat(
            obtain.RewardScene?.Rewards.Select(r => r.Id) ?? []),
        PredictorOperationNestedObtainScene nested => SceneRewardIds(nested.Parent),
        _ => []
    };

    internal static ImmutableArray<RolltheSpire2.Core.Identity.ModelKey> CaptureBonesPool(RunState run, Player player,
        CrystalSphereCatalogCapture.CapturePools? pools = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        // Native 0.111 Neow.AllPossibleOptions is CurseOptions, PositiveOptions,
        // then these six alternatives, in this exact order. Reading the shared
        // identity assets avoids constructing mutable relic offers or invoking
        // any custom IsAllowedAtNeow implementation on the real player.
        var pool = RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.BaseCursePoolForFiltering
            .Concat(RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.BasePositivePoolForAuthority)
            .Concat(new[] { "LAVA_ROCK", "NEOWS_TALISMAN", "NUTRITIOUS_OYSTER", "POMANDER", "SMALL_CAPSULE", "STONE_HUMIDIFIER" }
                .Select(e => new RolltheSpire2.Core.Identity.ModelKey("RELIC", e)));
        var cards = pools == null
            ? player.Character.CardPool.GetUnlockedCards(player.UnlockState, ((IRunState)run).CardMultiplayerConstraint).ToArray()
            : pools.Read(player.Character.CardPool).ToArray();
        bool scrolls = cards.Count(c => c.Rarity == MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Common) >= 4 &&
            cards.Count(c => c.Rarity == MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon) >= 2;
        bool colors = player.UnlockState.CharacterCardPools.Count() == ModelDb.AllCharacters.Count();
        return pool.Where(k => k.Entry != "NEOWS_BONES" &&
            RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.IsAllowed(k, run.Players.Count, colors, scrolls) == true).ToImmutableArray();
    }

    private static ImmutableArray<PredictorStreamState> Streams(RunState run, Player player) =>
        Enum.GetValues<PredictorStream>().Select(s => CrystalSphereLiveCapture.RngState(s, s switch
        {
            PredictorStream.Rewards => player.PlayerRng.Rewards,
            PredictorStream.Shops => player.PlayerRng.Shops,
            PredictorStream.Transformations => player.PlayerRng.Transformations,
            PredictorStream.UpFront => run.Rng.UpFront,
            PredictorStream.Niche => run.Rng.Niche,
            PredictorStream.UnknownMapPoint => run.Rng.UnknownMapPoint,
            PredictorStream.TreasureRoomRelics => run.Rng.TreasureRoomRelics,
            PredictorStream.CombatPotionGeneration => run.Rng.CombatPotionGeneration,
            _ => throw new InvalidOperationException("OperationStreamUnmapped")
        })).ToImmutableArray();

    private static string NativeStamp(RunState run, Player player, Node? root) => Hash(new
    {
        Run = Id(run), Room = run.CurrentRoom == null ? 0 : Id(run.CurrentRoom), run.CurrentActIndex, run.TotalFloor,
        run.CurrentMapCoord, Players = run.Players.Select(p => p.ToSerializable()).ToArray(),
        Rng = run.Rng.ToSerializable(), PersonalBag = Bag(player.RelicGrabBag), SharedBag = Bag(run.SharedRelicGrabBag),
        Odds = run.Odds.ToSerializable(), Cards = player.Deck.Cards.Select(Id).ToArray(),
        Map = run.Map == null ? null : Map(run), Visited = run.VisitedMapCoords, History = run.MapPointHistory,
        Relics = player.Relics.Select(Id).ToArray(), Potions = player.PotionSlots.Select(p => p == null ? 0 : Id(p)).ToArray(),
        Hoarder = run.Modifiers.OfType<MegaCrit.Sts2.Core.Models.Modifiers.Hoarder>()
            .Select(m => Read<HashSet<CardModel>>(m, "_cardsToSkip").Select(Id).Order().ToArray()).ToArray(),
        Selectors = Nodes<NCardGridSelectionScreen>(root).Select(n => new
        {
            Id = Id(n), Type = n.GetType().Name,
            Pending = SelectionIsPending(n),
            Candidates = Read<IReadOnlyList<CardModel>>(n, "_cards").Select(Id).ToArray(),
            Selected = Read<IEnumerable<CardModel>>(n, "_selectedCards").Select(Id).ToArray(),
            Prefs = Read<MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs>(n, "_prefs") is var prefs
                ? new { prefs.MinSelect, prefs.MaxSelect, prefs.Cancelable, prefs.RequireManualConfirmation } : null
        }).ToArray()
    });
}
