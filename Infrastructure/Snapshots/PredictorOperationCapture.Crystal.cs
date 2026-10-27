using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    private static PredictorOperationScene CaptureCrystal(RunState run, EventModel ev, Node? root)
    {
        var owner = ev.Owner ?? throw new InvalidOperationException("OperationCrystalOwnerMissing");
        var game = root == null ? null : CrystalSphereLiveCapture.FindGame(root);
        if (game == null)
        {
            var options = ReadNullable(ev, "_currentOptions") as IReadOnlyList<EventOption>;
            if (ev.IsFinished || options == null || options.Count == 0 || options.Any(o => o.WasChosen))
                throw new InvalidOperationException("OperationCrystalSceneSettling");
            if (ReadNullable(ev, "_dynamicVars") == null) throw new InvalidOperationException("OperationCrystalVarsMissing");
            int cost = ev.DynamicVars["UncoverFutureCost"].IntValue;
            return new PredictorOperationEventScene(Key(ev), CrystalSphereLiveCapture.RngState(PredictorStream.Niche, ev.Rng),
                [new("UNCOVER_FUTURE", GoldCost: cost), new("PAYMENT_PLAN")],
                new(owner.Creature.CurrentHp, owner.Creature.MaxHp)) { GoldAmount = cost };
        }
        if (!ReferenceEquals(Read<MegaCrit.Sts2.Core.Entities.Players.Player>(game, "_owner"), owner))
            throw new InvalidOperationException("OperationCrystalOwnerMismatch");
        if (game.IsFinished || CrystalSphereLiveCapture.IsClickPending(game))
            throw new InvalidOperationException("OperationCrystalSceneSettling");
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
                _ => throw new NotSupportedException("OperationCrystalItemNotCaptured:" + info.type)
            };
            bool placed = game.cells.Cast<CrystalSphereCell>().Any(c => ReferenceEquals(c.Item, item));
            var callbacks = ReadNullable(item, "Revealed") as Delegate;
            int subscriptions = callbacks?.GetInvocationList().Count(d => ReferenceEquals(d.Target, game)) ?? 0;
            return new PredictorCrystalItem(kind, item.Size.X, item.Size.Y, placed ? item.Position.X : -1,
                placed ? item.Position.Y : -1, subscriptions, revealed.Contains(i));
        }).ToImmutableArray();
        return new PredictorOperationCrystalScene(CrystalSphereLiveCapture.RngState(PredictorStream.Rewards, game.Rng),
            game.DivinationCount, game.CrystalSphereTool == CrystalSphereMinigame.CrystalSphereToolType.Small
                ? PredictorCrystalTool.Small : PredictorCrystalTool.Big, game.PlacedAllItems,
            game.cells.Cast<CrystalSphereCell>().Select(c => c.IsHidden).ToImmutableArray(),
            game.cells.Cast<CrystalSphereCell>().Select(c => c.Item == null ? -1 : items.IndexOf(c.Item)).ToImmutableArray(), entries, revealed);
    }
}
