using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal sealed record CrystalSphereLiveSnapshot(string Fingerprint, string Seed,
    ImmutableArray<(string Mode, PredictorCrystalSnapshot Snapshot)> Branches);

// Capture only; no native reward generation, mutable copies of RunState,
// vanilla constructors for minigames, or calls that advance a native RNG.
internal static class CrystalSphereLiveCapture
{
    private static readonly JsonSerializerOptions FingerprintJson = new() { IncludeFields = true };
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class Pending { internal readonly List<Task> Tasks = []; }
    private static readonly ConditionalWeakTable<CrystalSphereMinigame, Pending> PendingClicks = new();
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(CrystalSphereMinigame), nameof(CrystalSphereMinigame.CellClicked)),
        postfix: new HarmonyMethod(typeof(CrystalSphereLiveCapture), nameof(ObserveClick)));
    private static void ObserveClick(CrystalSphereMinigame __instance, Task __result)
    {
        var pending = PendingClicks.GetOrCreateValue(__instance);
        pending.Tasks.RemoveAll(t => t.IsCompleted); pending.Tasks.Add(__result);
    }
    internal static bool IsClickPending(CrystalSphereMinigame game) =>
        PendingClicks.TryGetValue(game, out var pending) && pending.Tasks.Any(t => !t.IsCompleted);
    private static T Read<T>(object owner, string field) =>
        (T)(owner.GetType().GetField(field, Fields)?.GetValue(owner) ?? throw new InvalidOperationException("CrystalCaptureField:" + field));
    private static int Counter(object owner, string property) => (int)(owner.GetType().GetProperty(property, Fields)?.GetValue(owner)
        ?? throw new InvalidOperationException("CrystalCaptureCounter:" + property));
    private static ModelKey Key(AbstractModel model) => new(model.Id.Category, model.Id.Entry);
    private static PredictorStreamState RngState(PredictorStream stream, Rng rng)
    {
        var r = rng.ToSerializable(); return new(stream, r.state0, r.state1, r.state2, r.state3, r.counter);
    }
    internal static CrystalSphereMinigame? FindGame(Node root)
    {
        if (root is NCrystalSphereScreen screen && screen.IsInsideTree()) return Read<CrystalSphereMinigame>(screen, "_entity");
        foreach (Node child in root.GetChildren()) if (FindGame(child) is { } game) return game;
        return null;
    }
    private static (Player Player, EventModel Event, CrystalSphereMinigame? Game) Scene(IRunState run, Node root)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (run.Players.Count != 1) throw new InvalidOperationException("CrystalSoloOnly");
        if (run.CurrentRoom is not EventRoom room || room.CanonicalEvent.Id.Entry != "CRYSTAL_SPHERE")
            throw new InvalidOperationException("CrystalNotCurrentScene");
        var ev = room.LocalMutableEvent;
        var game = FindGame(root);
        if (game != null && (game.IsFinished || IsClickPending(game)))
            throw new InvalidOperationException("CrystalSceneBusy");
        if (game == null && (typeof(EventModel).GetField("_currentOptions", Fields)?.GetValue(ev)
            is not List<MegaCrit.Sts2.Core.Events.EventOption> options || options.Count == 0 || options.Any(o => o.WasChosen)))
            throw new InvalidOperationException("CrystalSceneBusy");
        if (ev.IsFinished) throw new InvalidOperationException("CrystalSceneFinished");
        return (run.Players[0], ev, game);
    }
    internal static string Fingerprint(IRunState run, Node root)
    {
        var (player, ev, game) = Scene(run, root);
        return SceneFingerprint(run, player, ev, game);
    }
    internal static string SceneFingerprint(IRunState run, Player player, EventModel ev, CrystalSphereMinigame? game)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var parts = new StringBuilder();
        parts.Append(run.Rng.StringSeed).Append('/').Append(run.TotalFloor).Append('/').Append(RuntimeHelpers.GetHashCode(ev));
        parts.Append(JsonSerializer.Serialize(player.ToSerializable(), FingerprintJson));
        parts.Append(JsonSerializer.Serialize(run.Rng.ToSerializable(), FingerprintJson));
        parts.Append(JsonSerializer.Serialize(Bag(run.SharedRelicGrabBag), FingerprintJson));
        parts.Append(JsonSerializer.Serialize(ev.Rng.ToSerializable(), FingerprintJson));
        parts.Append(JsonSerializer.Serialize(Bag(player.RelicGrabBag), FingerprintJson));
        foreach (var modifier in run.Modifiers)
        {
            parts.Append(modifier.Id);
            if (modifier is MegaCrit.Sts2.Core.Models.Modifiers.CharacterCards characterCards) parts.Append(characterCards.CharacterModel);
        }
        foreach (var relic in player.Relics)
            if (relic.Id.Entry == "SILKEN_TRESS") parts.Append(Read<bool>(relic, "_isUsed"));
        if (game != null)
        {
            parts.Append(game.DivinationCount).Append('/').Append(game.CrystalSphereTool);
            foreach (var cell in game.cells) parts.Append(cell.IsHidden ? '1' : '0');
            foreach (var item in Read<List<CrystalSphereItem>>(game, "_revealed")) parts.Append(game.Items.ToList().IndexOf(item)).Append(',');
            foreach (var item in game.Items) parts.Append(item.Position).Append(JsonSerializer.Serialize(item.ToSerializable(), FingerprintJson));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString())));
    }
    internal static CrystalSphereLiveSnapshot Capture(IRunState run, Node root, string version)
    {
        var (player, ev, game) = Scene(run, root);
        return CaptureScene(run, player, ev, game, version);
    }
    internal static CrystalSphereLiveSnapshot CaptureScene(IRunState run, Player player, EventModel ev,
        CrystalSphereMinigame? game, string version)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        string before = SceneFingerprint(run, player, ev, game);
        var unlock = player.UnlockState.ToSerializable();
        var discovered = run.Acts.Select(Key).ToImmutableArray();
        var authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(RolltheSpire2.Compatibility.Beta111Profile.Instance,
            run.Rng.StringSeed, CharacterIdentity.FromKey(Key(player.Character)), run.AscensionLevel, version,
            predictionGameMode: RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer,
            predictionGameModeAuthority: RolltheSpire2.Core.Authority.PredictionGameModeAuthority.ExplicitRequest,
            explicitUnlockState: player.UnlockState);
        var context = new PredictorContext(run.Rng.StringSeed, version, Key(player.Character), run.AscensionLevel,
            authority.CatalogFingerprint, authority.UnlockSnapshotFingerprint, unlock.UnlockedEpochs.ToImmutableArray(),
            unlock.EncountersSeen.Select(k => new ModelKey(k.Category, k.Entry)).ToImmutableArray(), discovered, unlock.NumberOfRuns);
        context = PredictorSeedCapture.CaptureCatalog(context, authority) with { Modifiers = run.Modifiers.Select(m => new PredictorRunModifier(Key(m),
            m is MegaCrit.Sts2.Core.Models.Modifiers.CharacterCards characterCards
                ? new ModelKey(characterCards.CharacterModel.Category, characterCards.CharacterModel.Entry) : null)).ToImmutableArray() };
        long id = 1;
        var cards = player.Deck.Cards.Select(c => new PredictorCard(id++, Key(c),
            Enum.Parse<RolltheSpire2.Core.Effects.Snapshots.EffectCardType>(c.Type.ToString()), c.CurrentUpgradeLevel,
            c.MaxUpgradeLevel, c.IsRemovable, c.FloorAddedToDeck ?? 0, c.Enchantment == null ? null : Key(c.Enchantment), c.Enchantment?.Amount ?? 0)).ToImmutableArray();
        var relics = player.Relics.Select(r => new PredictorRelic(id++, Key(r), r.FloorAddedToDeck, r.IsMelted)
        {
            IsWax = r.IsWax,
            RewardUpgradeUses = r.Id.Entry == "SILVER_CRUCIBLE" ? Counter(r, "TimesUsed") : 0,
            TreasureRoomsEntered = r.Id.Entry == "SILVER_CRUCIBLE" ? Counter(r, "TreasureRoomsEntered") : 0,
            SilkenUsed = r.Id.Entry == "SILKEN_TRESS" && Read<bool>(r, "_isUsed"),
            BookOfFiveRingsCardsAdded = r.Id.Entry == "BOOK_OF_FIVE_RINGS" ? Counter(r, "CardsAdded") : 0,
            MawBankSpent = r.Id.Entry == "MAW_BANK" && r.IsUsedUp
        }).ToImmutableArray();
        var potions = player.PotionSlots.Select(p => p == null ? null : new PredictorPotion(id++, Key(p))).ToImmutableArray();
        var odds = player.PlayerOdds.ToSerializable();
        var streams = Enum.GetValues<PredictorStream>().Select(s => RngState(s, s switch
        {
            PredictorStream.Rewards => player.PlayerRng.Rewards,
            PredictorStream.Shops => player.PlayerRng.Shops,
            PredictorStream.Transformations => player.PlayerRng.Transformations,
            PredictorStream.UpFront => run.Rng.UpFront,
            PredictorStream.Niche => run.Rng.Niche,
            PredictorStream.UnknownMapPoint => run.Rng.UnknownMapPoint,
            PredictorStream.TreasureRoomRelics => run.Rng.TreasureRoomRelics,
            PredictorStream.CombatPotionGeneration => run.Rng.CombatPotionGeneration,
            _ => throw new InvalidOperationException("CrystalStreamUnmapped")
        })).ToImmutableArray();
        context = context with { PotionPool = MegaCrit.Sts2.Core.Factories.PotionFactory.GetPotionOptions(player)
            .Select(p => new PredictorPotionOption(Key(p), Enum.Parse<PredictorPotionRarity>(p.Rarity.ToString()), p.CanBeGeneratedInCombat)).ToImmutableArray() };
        var state = new PredictorState(id, new(run.CurrentActIndex + 1, run.TotalFloor, "live:crystal"), player.Gold,
            cards, cards.Select(c => c.Id).ToImmutableArray(), relics, potions, streams,
            new(odds.CardRarityOddsValue, odds.PotionRewardOddsValue), Bag(player.RelicGrabBag), Bag(run.SharedRelicGrabBag));
        state.Validate();
        var rng = RngState(PredictorStream.Rewards, ev.Rng);
        var branches = ImmutableArray.CreateBuilder<(string, PredictorCrystalSnapshot)>();
        if (game != null) branches.Add(("Remaining", CaptureBoard(context, state, game)));
        else
        {
            int price = ev.DynamicVars["UncoverFutureCost"].IntValue;
            branches.Add(("Three", PredictorRun.CreateCrystalBranch(context, state, rng, false, price)));
            branches.Add(("Six", PredictorRun.CreateCrystalBranch(context, state, rng, true, price)));
        }
        if (SceneFingerprint(run, player, ev, game) != before) throw new InvalidOperationException("CrystalSnapshotChanged");
        return new(before, run.Rng.StringSeed, branches.ToImmutable());
    }
    internal static PredictorCrystalSnapshot CaptureBoard(PredictorContext context, PredictorState state, CrystalSphereMinigame game)
    {
        var items = game.Items.ToList();
        var revealed = Read<List<CrystalSphereItem>>(game, "_revealed").Select(item => items.IndexOf(item)).ToImmutableArray();
        var entries = items.Select((item, i) =>
        {
            var info = item.ToSerializable();
            string kind = info.type.ToString() switch
            {
                "CardReward" => "CARD_" + info.cardRarity.ToString().ToUpperInvariant(),
                "Potion" => "POTION_" + info.potionRarity.ToString().ToUpperInvariant(),
                "Gold" => info.isBigGold ? "GOLD_BIG" : "GOLD_SMALL", "Curse" => "CURSE", "Relic" => "RELIC",
                _ => throw new InvalidOperationException("CrystalItemUnknown")
            };
            bool placed = game.cells.Cast<CrystalSphereCell>().Any(c => ReferenceEquals(c.Item, item));
            var callbacks = (Delegate?)typeof(CrystalSphereItem).GetField("Revealed", Fields)?.GetValue(item);
            int subscriptions = callbacks?.GetInvocationList().Count(d => ReferenceEquals(d.Target, game)) ?? 0;
            return new PredictorCrystalItem(kind, item.Size.X, item.Size.Y, placed ? item.Position.X : -1,
                placed ? item.Position.Y : -1, subscriptions, revealed.Contains(i));
        }).ToImmutableArray();
        return new(context, state, RngState(PredictorStream.Rewards, game.Rng), game.DivinationCount,
            game.CrystalSphereTool == CrystalSphereMinigame.CrystalSphereToolType.Small ? PredictorCrystalTool.Small : PredictorCrystalTool.Big,
            game.PlacedAllItems, game.cells.Cast<CrystalSphereCell>().Select(c => c.IsHidden).ToImmutableArray(),
            game.cells.Cast<CrystalSphereCell>().Select(c => c.Item == null ? -1 : items.IndexOf(c.Item)).ToImmutableArray(), entries, revealed);
    }
    private static PredictorRelicBag Bag(RelicGrabBag bag)
    {
        // Vanilla FromSerializable restores deques but intentionally leaves
        // the optional refill catalog null (and disables refresh).
        var originalField = typeof(RelicGrabBag).GetField("_originalRelics", Fields)
            ?? throw new InvalidOperationException("CrystalCaptureField:_originalRelics");
        var originals = (List<RelicModel>?)originalField.GetValue(bag);
        var fallback = Read<List<RelicModel>>(bag, "_mpFallbackDequeue");
        var deques = Read<Dictionary<RelicRarity, List<RelicModel>>>(bag, "_deques");
        return new(Read<bool>(bag, "_refreshAllowed"), (originals ?? []).Select(r => new PredictorBagEntry(Key(r), r.Rarity.ToString())).ToImmutableArray(),
            deques.Select(d => new PredictorBagBucket(d.Key.ToString(), d.Value.Select(Key).ToImmutableArray())).ToImmutableArray(),
            fallback.Select(Key).ToImmutableArray()) { HasOriginalCatalog = originals != null };
    }
}
