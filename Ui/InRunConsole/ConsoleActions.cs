using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.InRunConsole;

internal sealed record ConsoleActionResult(bool Success, string Message, bool Queued = false);

/// <summary>Explicit live-game mutations. Call only from the Godot main thread.</summary>
internal static class ConsoleActions
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static readonly FieldInfo ConsoleField = typeof(NDevConsole).GetField("_devConsole", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ConsoleInstanceField = typeof(NDevConsole).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo CommandsField = typeof(DevConsole).GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static DevConsole NativeConsole => (DevConsole)(ConsoleField.GetValue(NDevConsole.Instance)
        ?? throw new InvalidOperationException("The original game console is not ready."));

    public static bool IsRunActive => RunManager.Instance.IsInProgress;
    public static bool IsCombatActive => CombatManager.Instance.IsInProgress;
    public static bool CanUseConsole => IsRunActive && RunManager.Instance.NetService?.Type == NetGameType.Singleplayer &&
        RunManager.Instance.DebugOnlyGetState()?.Players.Count == 1 && GetLocalPlayer() != null;
    public static Player? GetLocalPlayer() => IsRunActive ? LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState()) : null;
    public static bool IsTravelEnabled => NMapScreen.Instance?.IsDebugTravelEnabled == true;
    public static bool? IsGodModeEnabled
    {
        get
        {
            var node = ConsoleInstanceField.GetValue(null);
            if (node == null || ConsoleField.GetValue(node) is not DevConsole console) return null;
            var commands = (Dictionary<string, AbstractConsoleCmd>)CommandsField.GetValue(console)!;
            return commands.TryGetValue("godmode", out var command)
                ? typeof(GodModeConsoleCmd).GetField("_godModeActive", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(command) as bool?
                : null;
        }
    }

    public static Task<ConsoleActionResult> AddRelicsAsync(IEnumerable<RelicModel> relics) => Prepare(() =>
        Request("relic-add", relics.Select(r => r.Id.ToString()).ToArray()));

    public static Task<ConsoleActionResult> RemoveRelicsAsync(IEnumerable<RelicModel> relics) => Prepare(() =>
    {
        var player = RequirePlayer();
        var selected = relics.Distinct().ToArray();
        var all = player.Relics.ToArray();
        if (selected.Any(r => !all.Contains(r))) throw new InvalidOperationException("Relic selection has changed. Refresh the inventory.");
        var request = Request("relic-remove", selected.Select(r => r.Id.ToString()).ToArray());
        request.Indices = selected.Select(r => Array.IndexOf(all, r)).ToArray();
        request.Snapshot = RelicSnapshot(player);
        return request;
    });

    public static Task<ConsoleActionResult> AddCardsAsync(IEnumerable<CardModel> cards, PileType pile, int quantity, bool upgraded) => Prepare(() =>
    {
        RequireSupportedPile(pile);
        if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        var request = Request("card-add", cards.Select(c => c.Id.ToString()).ToArray());
        request.Pile = pile;
        request.Quantity = quantity;
        request.Upgraded = upgraded;
        return request;
    });

    public static Task<ConsoleActionResult> RemoveCardsAsync(IEnumerable<CardModel> cards, PileType pile) => Prepare(() =>
    {
        var player = RequirePlayer();
        var all = GetCards(player, pile).ToArray();
        var selected = cards.Distinct().ToArray();
        if (selected.Any(c => !all.Contains(c))) throw new InvalidOperationException("Card selection has changed. Refresh the inventory.");
        var request = Request("card-remove", selected.Select(c => c.Id.ToString()).ToArray());
        request.Pile = pile;
        request.Indices = selected.Select(c => Array.IndexOf(all, c)).ToArray();
        request.Snapshot = CardSnapshot(player, pile);
        if (pile == PileType.Hand) request.CombatIds = selected.Select(c => NetCombatCard.FromModel(c).CombatCardIndex).ToArray();
        return request;
    });

    public static Task<ConsoleActionResult> AddPotionsAsync(IEnumerable<PotionModel> potions) => Prepare(() =>
        Request("potion-add", potions.Select(p => p.Id.ToString()).ToArray()));

    public static Task<ConsoleActionResult> RemovePotionAsync(int slot, PotionModel expected) => Prepare(() =>
        PotionRequest("potion-remove", slot, expected, null));

    public static Task<ConsoleActionResult> ReplacePotionAsync(int slot, PotionModel? expected, PotionModel replacement) => Prepare(() =>
        PotionRequest("potion-replace", slot, expected, replacement));

    public static Task<ConsoleActionResult> EnterAncientAsync(AncientEventModel ancient, string? optionTextKey = null) => PrepareNative(() =>
    {
        if (optionTextKey == null) return "ancient " + ancient.Id.Entry;
        var options = ancient.AllPossibleOptions.ToArray();
        if (!options.Any(o => o.TextKey == optionTextKey))
            throw new InvalidOperationException("The selected original Ancient option is unavailable.");
        // The native command uppercases its argument before a case-sensitive
        // TextKey.Contains lookup. Full keys contain lowercase .pages/.options.
        // Use its own completion token, checking that native first-match lookup
        // still addresses precisely the original option selected by the UI.
        string token = optionTextKey.Split('.').Last().ToUpperInvariant();
        if (options.FirstOrDefault(o => o.TextKey.Contains(token, StringComparison.Ordinal))?.TextKey != optionTextKey)
            throw new NotSupportedException("The original game console cannot uniquely address the selected Ancient option.");
        return "ancient " + ancient.Id.Entry + " " + token;
    });

    public static Task<ConsoleActionResult> FightAsync(EncounterModel encounter) => PrepareNative(() => "fight " + encounter.Id.Entry);
    public static Task<ConsoleActionResult> EnterEventAsync(EventModel eventModel) => PrepareNative(() => "event " + eventModel.Id.Entry);
    public static Task<ConsoleActionResult> ToggleTravelAsync() => PrepareNative(() => "travel");
    public static Task<ConsoleActionResult> ToggleGodModeAsync() => PrepareNative(() => "godmode");
    public static Task<ConsoleActionResult> WinAsync() => PrepareNative(() => "win");
    public static Task<ConsoleActionResult> AdjustGoldAsync(int amount) => Prepare(() =>
    {
        var request = Request("gold-adjust", []);
        request.GoldDelta = amount;
        return request;
    });

    private static async Task<ConsoleActionResult> Prepare(Func<ConsoleActionRequest> create)
    {
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            RequireConsoleSupport();
            var request = create();
            if (request.Models.Length == 0 && request.Operation is not ("potion-remove" or "potion-replace" or "gold-adjust"))
                return new(false, "Select at least one item.");
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json)));
            return await SubmitAsync("rt2_action " + payload);
        }
        catch (Exception ex) { return Failure("prepare", ex); }
    }

    private static async Task<ConsoleActionResult> PrepareNative(Func<string> create)
    {
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            RequireConsoleSupport();
            return await SubmitAsync(create());
        }
        catch (Exception ex) { return Failure("native-command", ex); }
    }

    private static async Task<ConsoleActionResult> SubmitAsync(string command)
    {
        RequireConsoleSupport();
        var result = NativeConsole.ProcessCommand(command);
        if (!result.success) return new(false, result.msg);
        if (result.task != null) await result.task;
        return new(true, result.msg);
    }

    private static ConsoleActionRequest Request(string operation, string[] models)
    {
        var player = RequirePlayer();
        return new()
        {
            Operation = operation, Models = models, RequestId = Guid.NewGuid().ToString("N"),
            Seed = player.RunState.Rng.StringSeed, Floor = player.RunState.TotalFloor,
            InCombat = IsCombatActive, Location = player.RunState.RunLocation.ToString()
        };
    }

    private static ConsoleActionRequest PotionRequest(string operation, int slot, PotionModel? expected, PotionModel? replacement)
    {
        var player = RequirePlayer();
        if (slot < 0 || slot >= player.PotionSlots.Count || !ReferenceEquals(player.PotionSlots[slot], expected))
            throw new InvalidOperationException("Potion slot selection has changed. Refresh the inventory.");
        var request = Request(operation, replacement == null ? [] : [replacement.Id.ToString()]);
        request.Slot = slot;
        request.Snapshot = PotionSnapshot(player, slot);
        return request;
    }

    private static Player RequirePlayer() => GetLocalPlayer() ?? throw new InvalidOperationException("No local player in an active run.");

    internal static IReadOnlyList<CardModel> GetCards(Player player, PileType pile)
    {
        RequireSupportedPile(pile);
        if (pile == PileType.Deck) return player.Deck.Cards;
        return IsCombatActive && player.PlayerCombatState != null ? player.PlayerCombatState.Hand.Cards
            : throw new InvalidOperationException("Hand operations require active combat.");
    }

    private static void RequireSupportedPile(PileType pile)
    {
        if (pile is not (PileType.Deck or PileType.Hand)) throw new ArgumentException("Choose Deck or Hand explicitly.", nameof(pile));
    }

    internal static string CardSnapshot(Player player, PileType pile) => Signature(GetCards(player, pile).Select(c => c.ToSerializable()).ToArray());
    private static string RelicSnapshot(Player player) => Signature(player.Relics.Select(r => r.ToSerializable()).ToArray());
    private static string PotionSnapshot(Player player, int slot) => Signature(player.PotionSlots[slot]?.ToSerializable(slot));
    private static string Signature<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));

    internal static CmdResult ProcessCustom(Player? player, string[] args)
    {
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            RequireConsoleSupport();
            if (args.Length != 1) throw new ArgumentException("Expected exactly one RT2 action payload.");
            var request = JsonSerializer.Deserialize<ConsoleActionRequest>(Convert.FromBase64String(args[0]), Json)
                ?? throw new ArgumentException("Empty RT2 action payload.");
            ValidateRun(player, request);
            return new(ExecuteCustomAsync(player!, request), true, "RT2 action: " + request.Operation);
        }
        catch (Exception ex)
        {
            RuntimeLog.Fault("inRunConsole;stage=command-decode", ex);
            return new(false, ex.Message);
        }
    }

    private static void ValidateRun(Player? player, ConsoleActionRequest request)
    {
        RequireConsoleSupport();
        if (!IsRunActive || player == null || !ReferenceEquals(player.RunState, RunManager.Instance.DebugOnlyGetState()) ||
            player.RunState.Rng.StringSeed != request.Seed || player.RunState.TotalFloor != request.Floor || IsCombatActive != request.InCombat)
            throw new InvalidOperationException("The run or room changed after selection. Refresh and retry.");
        if (player.RunState.RunLocation.ToString() != request.Location)
            throw new InvalidOperationException("The room changed after selection. Refresh and retry.");
    }

    private static void RequireConsoleSupport()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!CanUseConsole)
            throw new NotSupportedException("RT2 局内测试控制台仅限进行中的单人游戏；多人及模拟多人游戏不可用。");
    }

    private static async Task ExecuteCustomAsync(Player player, ConsoleActionRequest request)
    {
        int completed = 0;
        try
        {
            switch (request.Operation)
            {
                case "gold-adjust":
                    ValidateRun(player, request);
                    // Native `gold -N` is a no-op: GainGold discards nonpositive amounts.
                    // Use the native loss API for an explicit negative adjustment.
                    if (request.GoldDelta > 0) await PlayerCmd.GainGold(request.GoldDelta, player);
                    else if (request.GoldDelta < 0) await PlayerCmd.LoseGold(-(decimal)request.GoldDelta, player);
                    RequireConsoleSupport();
                    completed++;
                    break;
                case "relic-add":
                    foreach (string id in request.Models)
                    {
                        ValidateRun(player, request);
                        var obtained = await RelicCmd.Obtain(ModelDb.GetById<RelicModel>(ModelId.Deserialize(id)).ToMutable(), player);
                        RequireConsoleSupport();
                        if (!player.Relics.Contains(obtained)) throw new InvalidOperationException("The obtained relic is no longer in the player's inventory.");
                        completed++;
                    }
                    break;
                case "relic-remove":
                    if (RelicSnapshot(player) != request.Snapshot) throw new InvalidOperationException("Relic inventory changed before execution.");
                    var relics = ResolveIndices(player.Relics, request);
                    foreach (var relic in relics)
                    {
                        ValidateRun(player, request);
                        if (!player.Relics.Contains(relic)) throw new InvalidOperationException("A selected relic is no longer owned.");
                        await RelicCmd.Remove(relic);
                        RequireConsoleSupport();
                        completed++;
                    }
                    break;
                case "card-add":
                    RequireSupportedPile(request.Pile);
                    if (request.Quantity < 1) throw new InvalidOperationException("Card quantity must be positive.");
                    foreach (string id in request.Models)
                    for (int i = 0; i < request.Quantity; i++)
                    {
                        ValidateRun(player, request);
                        var pile = GetCards(player, request.Pile);
                        if (request.Pile == PileType.Hand && pile.Count >= CardPile.MaxCardsInHand)
                            throw new InvalidOperationException("The hand is full.");
                        var model = ModelDb.GetById<CardModel>(ModelId.Deserialize(id));
                        if (request.Upgraded && !model.IsUpgradable)
                            throw new InvalidOperationException("The selected card cannot be upgraded: " + model.Id);
                        ICardScope scope = request.Pile == PileType.Deck ? player.RunState :
                            CombatManager.Instance.DebugOnlyGetState() ?? throw new InvalidOperationException("The active combat state is unavailable.");
                        var card = scope.CreateCard(model, player);
                        if (request.Upgraded)
                        {
                            CardCmd.Upgrade(card);
                            if (!card.IsUpgraded) throw new InvalidOperationException("The game did not apply the requested upgrade.");
                        }
                        var addReceipt = await CardPileCmd.Add(card, request.Pile);
                        RequireConsoleSupport();
                        if (!addReceipt.success) throw new InvalidOperationException("The game prevented adding card " + model.Id);
                        if (addReceipt.cardAdded.Pile?.Type != request.Pile)
                            throw new InvalidOperationException($"Card {model.Id} was moved to {addReceipt.cardAdded.Pile?.Type}; requested {request.Pile}.");
                        completed++;
                    }
                    break;
                case "card-remove":
                    if (CardSnapshot(player, request.Pile) != request.Snapshot) throw new InvalidOperationException("Card inventory changed before execution.");
                    var cards = ResolveIndices(GetCards(player, request.Pile), request);
                    if (request.Pile == PileType.Hand && (request.CombatIds.Length != cards.Length || cards.Where((c, i) => NetCombatCard.FromModel(c).CombatCardIndex != request.CombatIds[i]).Any()))
                        throw new InvalidOperationException("Selected combat card identities changed.");
                    foreach (var card in cards)
                    {
                        ValidateRun(player, request);
                        if (!GetCards(player, request.Pile).Contains(card)) throw new InvalidOperationException("A selected card left the chosen pile.");
                        if (request.Pile == PileType.Deck) await CardPileCmd.RemoveFromDeck(card);
                        else await CardPileCmd.RemoveFromCombat(card);
                        RequireConsoleSupport();
                        completed++;
                    }
                    break;
                case "potion-add":
                    foreach (string id in request.Models)
                    {
                        ValidateRun(player, request);
                        var potion = ModelDb.GetById<PotionModel>(ModelId.Deserialize(id)).ToMutable();
                        var receipt = await PotionCmd.TryToProcure(potion, player);
                        RequireConsoleSupport();
                        if (!receipt.success) throw new InvalidOperationException("Potion was not obtained: " + receipt.failureReason);
                        completed++;
                    }
                    break;
                case "potion-remove":
                case "potion-replace":
                    if (request.Slot < 0 || request.Slot >= player.PotionSlots.Count || PotionSnapshot(player, request.Slot) != request.Snapshot)
                        throw new InvalidOperationException("Potion slot changed before execution.");
                    var original = player.PotionSlots[request.Slot];
                    PotionModel? replacement = null;
                    if (request.Operation == "potion-replace")
                    {
                        replacement = ModelDb.GetById<PotionModel>(ModelId.Deserialize(request.Models.Single())).ToMutable();
                        if (!Hook.ShouldProcurePotion(player.RunState, player.Creature.CombatState, replacement, player))
                            throw new InvalidOperationException("The game currently prevents obtaining this potion; the original potion was retained.");
                    }
                    else if (original == null) throw new InvalidOperationException("The selected potion slot is empty.");
                    if (original != null)
                    {
                        await PotionCmd.Discard(original);
                        RequireConsoleSupport();
                    }
                    if (replacement != null)
                    {
                        ValidateRun(player, request);
                        var receipt = await PotionCmd.TryToProcure(replacement, player, request.Slot);
                        RequireConsoleSupport();
                        if (!receipt.success) throw new InvalidOperationException("Original potion was discarded, but replacement failed: " + receipt.failureReason);
                    }
                    completed++;
                    break;
                default: throw new ArgumentException("Unknown RT2 action: " + request.Operation);
            }
            RuntimeLog.Info($"inRunConsole;request={request.RequestId};action={request.Operation};player={player.NetId};completed={completed};success=true");
        }
        catch (Exception ex)
        {
            RuntimeLog.Fault($"inRunConsole;request={request.RequestId};action={request.Operation};player={player.NetId};completed={completed}", ex);
            throw new InvalidOperationException($"{request.Operation}: completed {completed}; {ex.Message}", ex);
        }
    }

    private static T[] ResolveIndices<T>(IReadOnlyList<T> inventory, ConsoleActionRequest request) where T : AbstractModel
    {
        if (request.Indices.Length != request.Models.Length || request.Indices.Distinct().Count() != request.Indices.Length)
            throw new InvalidOperationException("Invalid exact inventory selection.");
        return request.Indices.Select((index, i) => index >= 0 && index < inventory.Count && inventory[index].Id.ToString() == request.Models[i]
            ? inventory[index] : throw new InvalidOperationException("Selected inventory instance changed.")).ToArray();
    }

    private static ConsoleActionResult Failure(string stage, Exception exception)
    {
        RuntimeLog.Fault("inRunConsole;stage=" + stage, exception);
        return new(false, exception.Message);
    }
}

internal sealed class ConsoleActionRequest
{
    public string RequestId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Seed { get; set; } = "";
    public int Floor { get; set; }
    public bool InCombat { get; set; }
    public string Location { get; set; } = "";
    public string[] Models { get; set; } = [];
    public int[] Indices { get; set; } = [];
    public uint[] CombatIds { get; set; } = [];
    public string Snapshot { get; set; } = "";
    public PileType Pile { get; set; }
    public int Quantity { get; set; } = 1;
    public bool Upgraded { get; set; }
    public int Slot { get; set; }
    public int GoldDelta { get; set; }
}
