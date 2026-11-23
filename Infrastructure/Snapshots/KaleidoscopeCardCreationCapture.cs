using System.Collections;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class KaleidoscopeCardCreationCapture
{
    // Earlier builds save per-stream counters; newer builds save the full RNG state.
    private static readonly PropertyInfo PlayerRngStreams = SavedStreams(typeof(SerializablePlayerRngSet));
    private static readonly PropertyInfo RunRngStreams = SavedStreams(typeof(SerializableRunRngSet));

    private static PropertyInfo SavedStreams(Type saveType) =>
        saveType.GetProperty("Rngs") ?? saveType.GetProperty("Counters")
        ?? throw new MissingMemberException(saveType.FullName, "Rngs / Counters");

    private static readonly Lazy<Func<IRunState, IEnumerable<CardModel>, IEnumerable<CardModel>>> FilterForRun = new(() =>
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var method = typeof(CardFactory).GetMethod("FilterForRun", flags) ?? typeof(CardFactory).GetMethod("FilterForPlayerCount", flags)
            ?? throw new MissingMethodException(typeof(CardFactory).FullName, "FilterForRun / FilterForPlayerCount");
        return method.CreateDelegate<Func<IRunState, IEnumerable<CardModel>, IEnumerable<CardModel>>>();
    });

    internal static Player CreateDetachedPlayer(CharacterModel character, UnlockState unlockState, int playersCount,
        GameMode gameMode = GameMode.Standard, int ascension = 0, int playerSlotIndex = 0, string? seed = null,
        IReadOnlyList<(CharacterModel Character, UnlockState Unlocks)>? roster = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if ((uint)playerSlotIndex >= (uint)playersCount || playersCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(playerSlotIndex));
        var manager = (AscensionManager)(typeof(AscensionManager).GetConstructor([typeof(GameMode), typeof(int)])?.Invoke([gameMode, ascension])
            ?? Activator.CreateInstance(typeof(AscensionManager), ascension)!);
        if (playersCount == 1) roster ??= [(character, unlockState)];
        Player player;
        if (roster is not null && seed is not null)
        {
            if (roster.Count != playersCount || roster[playerSlotIndex].Character.Id != character.Id)
                throw new ArgumentException("Kaleidoscope opening roster does not match the selected player.", nameof(roster));
            var rng = new RunRngSet(seed);
            var run = RunState.FromSerializable(new SerializableRun
            {
                Players = roster.Select((seat, slot) => OpeningPlayer(seat.Character, seat.Unlocks, manager, slot)).ToList(),
                GameMode = gameMode,
                Ascension = ascension,
                SerializableRng = rng.ToSerializable(),
                SerializableOdds = new MegaCrit.Sts2.Core.Odds.RunOddsSet(rng.UnknownMapPoint).ToSerializable(),
                SerializableSharedRelicGrabBag = new()
            });
            foreach (var member in run.Players) member.InitializeSeed(seed);
            player = run.Players[playerSlotIndex];
            run.AppendToMapPointHistory(MapPointType.Ancient, RoomType.Event, ModelDb.AncientEvent<Neow>().Id);
        }
        else
        {
            player = Player.FromSerializable(OpeningPlayer(character, unlockState, manager, playerSlotIndex));
            foreach (var card in player.Deck.Cards) card.Owner = player;
            var run = DispatchProxy.Create<IRunState, CardSelectionRun>();
            var context = (CardSelectionRun)(object)run;
            context.Party = new PartialParty(player, playerSlotIndex, playersCount);
            context.Mode = gameMode;
            context.Ascension = ascension;
            player.RunState = run;
            if (seed is not null) player.InitializeSeed(seed);
        }
        AddKaleidoscope(player, 1);
        return player;
    }

    // Player.CreateForNewRun records starting relics in the user's progress. Restore the
    // starting inventory privately; offline slot IDs are local to this run.
    private static SerializablePlayer OpeningPlayer(CharacterModel character, UnlockState unlockState, AscensionManager ascension, int slot)
    {
        var save = new SerializablePlayer
        {
            NetId = (ulong)slot + 1,
            CharacterId = character.Id,
            CurrentHp = character.StartingHp,
            MaxHp = character.StartingHp,
            MaxEnergy = character.MaxEnergy,
            Gold = character.StartingGold,
            BaseOrbSlotCount = character.BaseOrbSlotCount,
            MaxPotionSlotCount = ascension.HasLevel(AscensionLevel.TightBelt) ? 2 : 3,
            Deck = character.StartingDeck.Select(c =>
            {
                var card = c.ToMutable();
                card.FloorAddedToDeck = 1;
                return card.ToSerializable();
            }).ToList(),
            Relics = character.StartingRelics.Select(r =>
            {
                var relic = r.ToMutable();
                relic.FloorAddedToDeck = 1;
                return relic.ToSerializable();
            }).ToList(),
            Potions = character.StartingPotions.Select((p, i) => p.ToMutable().ToSerializable(i)).ToList(),
            UnlockState = unlockState.ToSerializable(),
            Rng = new(),
            Odds = new(),
            RelicGrabBag = new(),
            ExtraFields = new()
        };
        if (ascension.HasLevel(AscensionLevel.AscendersBane))
            save.Deck.Add(new SerializableCard { Id = ModelDb.Card<AscendersBane>().Id, FloorAddedToDeck = 1 });
        return save;
    }

    internal static Player CreateDetachedPlayer(Player live)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var run = (RunState)live.RunState;
        // The native run DTO shares some mutable history entries. Round-trip it in memory
        // before giving third-party run subscribers the clone. Never enter a native room.
        var save = new SerializableRun
        {
            Players = run.Players.Select(p => p.ToSerializable()).ToList(),
            Acts = run.Acts.Select(a => a.ToSave()).ToList(),
            Modifiers = run.Modifiers.Select(m => m.ToSerializable()).ToList(),
            GameMode = run.GameMode, Ascension = run.AscensionLevel, CurrentActIndex = run.CurrentActIndex,
            EventsSeen = run.VisitedEventIds.ToList(), VisitedMapCoords = run.VisitedMapCoords.ToList(),
            MapPointHistory = run.MapPointHistory.Select(entries => entries.ToList()).ToList(),
            SerializableRng = run.Rng.ToSerializable(), SerializableOdds = run.Odds.ToSerializable(),
            SerializableSharedRelicGrabBag = run.SharedRelicGrabBag.ToSerializable(),
            ExtraFields = run.ExtraFields.ToSerializable()
        };
        var custom = typeof(SerializableRun).GetProperty("IsCustomGame");
        custom?.SetValue(save, typeof(RunState).GetProperty("IsCustomGame")!.GetValue(run));
        if (run.Map is not NullActMap) save.Acts[run.CurrentActIndex].SavedMap = SerializableActMap.FromActMap(run.Map);
        var type = JsonSerializationUtility.GetTypeInfo<SerializableRun>();
        var copy = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(save, type), type)!;
        var detached = RunState.FromSerializable(copy);
        detached.ActFloor = run.ActFloor;
        if (copy.Acts.ElementAtOrDefault(copy.CurrentActIndex)?.SavedMap is { } map) detached.Map = new SavedActMap(map);
        var player = detached.Players.Single(p => p.NetId == live.NetId);
        AddKaleidoscope(player, run.TotalFloor);
        return player;
    }

    private static void AddKaleidoscope(Player player, int floor)
    {
        var relic = ModelDb.Relic<Kaleidoscope>().ToMutable();
        relic.FloorAddedToDeck = floor;
        player.AddRelicInternal(relic, silent: true);
    }

    internal sealed class PartialParty(Player player, int slot, int count) : IReadOnlyList<Player>
    {
        public int Count => count;
        internal Player Owner => player;
        internal int Slot => slot;
        public Player this[int index] => index == slot ? player :
            throw new NotSupportedException($"Kaleidoscope card selection requires the character in party slot {index}.");
        public IEnumerator<Player> GetEnumerator() => Enumerable.Range(0, count).Select(i => this[i]).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // With no complete lobby roster, native player-count rules need only Count.
    // A selector requesting an unknown teammate receives an explicit missing input.
    public class CardSelectionRun : DispatchProxy
    {
        internal PartialParty Party { get; set; } = null!;
        internal GameMode Mode { get; set; }
        internal int Ascension { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "get_" + nameof(IRunState.CardMultiplayerConstraint) => Party.Count > 1 ? CardMultiplayerConstraint.MultiplayerOnly : CardMultiplayerConstraint.SingleplayerOnly,
                "get_" + nameof(IRunState.Players) => Party,
                "get_" + nameof(IRunState.GameMode) => Mode,
                "get_" + nameof(IRunState.AscensionLevel) => Ascension,
                "get_" + nameof(IRunState.TotalFloor) or "get_" + nameof(IRunState.ActFloor) => 1,
                "get_" + nameof(IRunState.CurrentActIndex) => 0,
                "GetPlayerSlotIndex" when args?[0] == Party.Owner || Equals(args?[0], Party.Owner.NetId) => Party.Slot,
                nameof(IRunState.IterateHookListeners) when args?[0] == null => KnownListeners(Party.Owner),
                _ => throw new NotSupportedException($"Kaleidoscope card pool requested run member {targetMethod?.Name}.")
            };

        private static IEnumerable<AbstractModel> KnownListeners(Player player)
        {
            foreach (var card in player.Deck.Cards)
            {
                yield return card;
                if (card.Enchantment is { } enchantment) yield return enchantment;
            }
            foreach (var relic in player.Relics.Where(r => !r.IsMelted)) yield return relic;
            foreach (var potion in player.Potions) yield return potion;
        }
    }

    // An interceptor here receives the already rolled cards. Its replacements are
    // invisible even when the candidate list and its order were captured perfectly.
    internal static void CheckRewardResultHooks(Player player)
    {
        var stage = AccessTools.Method(typeof(Hook), nameof(Hook.TryModifyCardRewardOptions));
        if (Harmony.GetPatchInfo(stage)?.Owners is { Count: > 0 } owners)
            throw new NotSupportedException($"KaleidoscopeRewardResultPatched:{string.Join(",", owners)}");

        foreach (var listenerType in player.RunState.IterateHookListeners(null).Select(model => model.GetType()).Distinct())
        {
            foreach (var name in RewardResultMethods)
            {
                var implementation = listenerType.GetMethod(name);
                if (implementation?.DeclaringType is { } type && type.Assembly != typeof(AbstractModel).Assembly)
                    throw new NotSupportedException($"KaleidoscopeRewardResultListener:{type.FullName}.{name}");
            }
        }
    }

    private static readonly string[] RewardResultMethods =
        [nameof(AbstractModel.TryModifyCardRewardOptions), nameof(AbstractModel.TryModifyCardRewardOptionsLate)];

    internal static IReadOnlyList<CardModel> GetPossibleCards(Player player, CardPoolModel pool)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var initial = new CardCreationOptions([pool], CardCreationSource.Other, CardRarityOddsType.RegularEncounter)
            .WithFlags(CardCreationFlags.NoCardPoolModifications);
        var rng = player.PlayerRng;
        var before = rng.ToSerializable();
        var runRng = (player.RunState as RunState)?.Rng;
        var runBefore = runRng?.ToSerializable();
        try
        {
            var options = Hook.ModifyCardRewardCreationOptions(player.RunState, player, initial);
            // The numerical replay uses the relic's regular odds and reward stream.
            // A hook can change those independently of its candidate-list changes.
            if (options.Source != CardCreationSource.Other || options.RarityOdds != CardRarityOddsType.RegularEncounter ||
                options.RngOverride is not null || options.Flags.HasFlag(CardCreationFlags.ForceRarityOddsChange) ||
                initial.Flags.HasFlag(CardCreationFlags.NoUpgradeRoll))
                throw new NotSupportedException($"KaleidoscopeRollRulesChanged:{pool.Id}:{options.Source}:{options.RarityOdds}:{options.Flags}");

            // The one-card factory materializes Except(empty blacklist) before it applies the run filter.
            var candidates = options.GetPossibleCards(player).Distinct().ToList();
            var filtered = FilterForRun.Value(player.RunState, candidates).ToArray();
            if (!SameRngStreams(PlayerRngStreams, before, rng.ToSerializable()) ||
                runBefore is not null && !SameRngStreams(RunRngStreams, runBefore, runRng!.ToSerializable()))
                throw new NotSupportedException($"KaleidoscopeCandidateFilterConsumedRng:{pool.Id}");
            return filtered;
        }
        catch
        {
            // A lazy candidate filter can consume a stream and then throw.
            rng.LoadFromSerializable(before);
            if (runBefore is not null) runRng!.LoadFromSerializable(runBefore);
            throw;
        }
    }

    private static bool SameRngStreams(PropertyInfo streams, object first, object second)
    {
        var left = (IDictionary)streams.GetValue(first)!;
        var right = (IDictionary)streams.GetValue(second)!;
        if (left.Count != right.Count) return false;
        foreach (DictionaryEntry pair in left)
            if (!Equals(pair.Value, right[pair.Key])) return false;
        return true;
    }
}
