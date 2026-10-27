using Godot;
using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    internal static bool TryDirectTransformObservation(PredictorOperationSnapshot snapshot, RunState run, NCardHolder owner,
        out NDeckTransformSelectScreen? selector, out string? stamp)
    {
        selector = TransformSelector(owner); stamp = null;
        if (selector != null && TryDirectTransformObservation(snapshot, run, selector, out stamp)) return true;
        selector = null; return false;
    }

    private static bool TryDirectTransformObservation(PredictorOperationSnapshot snapshot, RunState run,
        NDeckTransformSelectScreen selector, out string? stamp, CaptureTiming? timing = null)
    {
        stamp = null;
        // Only these callers finish the ordinary direct obtain after Astrolabe's
        // await. Composite obtains/events can resume additional work and retain
        // their existing complete publication capture.
        long relicId = snapshot.Scene switch
        {
            PredictorOperationObtainScene { Source.Entry: "ASTROLABE", Kind: PredictorOperationObtainChildKind.Deck,
                Destination: PredictorObtainReturn.Ancient, Parent: PredictorOperationAncientScene } scene => scene.RelicId,
            PredictorOperationObtainScene { Source.Entry: "ASTROLABE", Kind: PredictorOperationObtainChildKind.Deck,
                Destination: PredictorObtainReturn.Reward, Parent: PredictorOperationRewardsScene } scene => scene.RelicId,
            PredictorOperationShopScene { PendingRelic.Choice.Source.Entry: "ASTROLABE" } scene => scene.PendingRelic.Choice.RelicId,
            _ => 0
        };
        if (relicId == 0) return false;
        if (!snapshot.Context.Modifiers.IsEmpty || snapshot.Context.Operation is not { } operation || operation.OpaqueModels.IsDefault ||
            operation.IgnoresBehavior(snapshot.Context.Character) || snapshot.State.Deck.Any(id =>
                operation.IgnoresBehavior(snapshot.State.Card(id).Key) || snapshot.State.Card(id).Enchantment is { } enchantment &&
                operation.IgnoresBehavior(enchantment)) || snapshot.State.Relics.Any(relic => !relic.Melted &&
                (operation.IgnoresBehavior(relic.Key) || relic.Key.Entry is "MOLTEN_EGG" or "TOXIC_EGG" or "FROZEN_EGG" or
                    "FRESNEL_LENS" or "BING_BONG" or "LUCKY_FYSH" or "DARKSTONE_PERIAPT" or "BOOK_OF_FIVE_RINGS"))) return false;
        if (!SelectionSources.TryGetValue(selector, out var source) || source.Parent != null ||
            Id(source.Relic) != relicId) return false;
        stamp = DirectTransformObservationStamp(snapshot, run, selector, timing: timing);
        return stamp != null;
    }

    internal static string? DirectTransformObservationStamp(PredictorOperationSnapshot snapshot, RunState run,
        NDeckTransformSelectScreen selector, bool captured = false, CaptureTiming? timing = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!GodotObject.IsInstanceValid(selector) || !selector.IsInsideTree() || !SelectionIsPending(selector) ||
            !SelectionSources.TryGetValue(selector, out var source) || source.Parent != null || source.Relic.Id.Entry != "ASTROLABE" ||
            source.Task?.IsCompleted != false) return null;
        RequireSuccessfulObservationTask(source.Task);
        var player = LocalContext.GetMe(run) ?? throw new InvalidOperationException("OperationLocalPlayerMissing");
        var catalog = snapshot.Context.Catalog!;
        var state = snapshot.State;
        // Transformation membership/order is a live input even while the
        // picker is suspended. Preserve that evidence without rebuilding every
        // definition/encounter/shop projection. Native canonical definitions
        // are immutable; changed registrations have different reference ids.
        var registered = MegaCrit.Sts2.Core.Models.ModelDb.AllCards.ToArray();
        if (timing != null) timing.ContentObservationReads += registered.Length;
        if (registered.Any(card => !card.IsCanonical || card.IsMock ||
            card.GetType().Assembly != typeof(MegaCrit.Sts2.Core.Models.CardModel).Assembly)) return null;
        var pools = new List<DirectPoolObservation>();
        foreach (var pool in catalog.TransformationPools)
        {
            var native = MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardPoolModel>(
                new(pool.Key.Category, pool.Key.Entry));
            if (!native.IsCanonical || native.GetType().Assembly != typeof(MegaCrit.Sts2.Core.Models.CardModel).Assembly) return null;
            var cards = captured ? pool.Cards : native.GetUnlockedCards(player.UnlockState,
                ((IRunState)run).CardMultiplayerConstraint).Select(Key).ToImmutableArray();
            if (!captured && timing != null) timing.DirectPoolReads++;
            pools.Add(new(pool.Key, ContentId(native), cards));
        }
        var prefs = Read<MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs>(selector, "_prefs");
        var odds = player.PlayerOdds.ToSerializable();
        Node? sceneRoot = MegaCrit.Sts2.Core.Nodes.NGame.Instance;
        sceneRoot ??= selector.GetTree().Root;
        // Native Astrolabe is suspended in CardsSelected: it has not drawn or
        // transformed anything. These are its changing numerical inputs, plus
        // actual boundary/selection identities. Catalog definitions, map/history
        // and account points do not participate in this direct local operation.
        return Hash(new
        {
            Run = Id(run), Room = run.CurrentRoom == null ? 0 : Id(run.CurrentRoom), run.CurrentActIndex, run.TotalFloor,
            Players = run.Players.Select(Id).ToArray(), Player = Id(player), Selector = Id(selector),
            Character = Key(player.Character),
            Content = registered.Select(card => ContentId(card)).ToArray(),
            Pools = pools,
            // Retain the actual parent reward/shop/Ancient continuation and
            // source observation. Reusing definitions never certifies a parent
            // whose generated options, pending task or source has changed.
            Scene = (object)(captured ? snapshot.Scene : CaptureScene(run, player, catalog, sceneRoot)),
            Completion = Id(ReadNullable(selector, "_completionSource")!), Source = Id(source.Relic), SourceTask = Id(source.Task),
            source.ChildrenStarted, source.Ordinal, SourceReward = source.Reward == null ? 0 : Id(source.Reward),
            Modifiers = run.Modifiers.Select(Id).ToArray(),
            // Relic-owned removed cards are represented by the relic DTO; only
            // actual ordered deck cards are selectable transform inputs.
            Cards = captured ? state.Deck.Select(state.Card).ToArray() : player.Deck.Cards.Select(c => Card(c, catalog)).ToArray(),
            Relics = captured ? state.Relics : player.Relics.Select(r => Relic(r, catalog, Id)).ToImmutableArray(),
            Potions = captured ? state.PotionSlots : player.PotionSlots.Select(p => p == null ? null : new PredictorPotion(Id(p), Key(p))).ToImmutableArray(),
            Gold = captured ? state.GoldBudget : player.Gold,
            Hp = captured ? snapshot.CurrentHp : player.Creature.CurrentHp, MaxHp = captured ? snapshot.MaximumHp : player.Creature.MaxHp,
            Rng = captured ? state.Streams : Streams(run, player),
            PersonalBag = captured ? state.PersonalBag : Bag(player.RelicGrabBag), SharedBag = captured ? state.SharedBag : Bag(run.SharedRelicGrabBag),
            Odds = captured ? state.Odds : new PredictorOdds(odds.CardRarityOddsValue, odds.PotionRewardOddsValue),
            Candidates = Read<IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel>>(selector, "_cards").Select(Id).ToArray(),
            Selected = captured ? snapshot.SelectedCardIds.ToArray() : Read<IEnumerable<MegaCrit.Sts2.Core.Models.CardModel>>(selector, "_selectedCards").Select(Id).ToArray(),
            prefs.MinSelect, prefs.MaxSelect, prefs.Cancelable, prefs.RequireManualConfirmation
        });
    }

    private sealed record DirectPoolObservation(RolltheSpire2.Core.Identity.ModelKey Key, long Reference,
        ImmutableArray<RolltheSpire2.Core.Identity.ModelKey> Cards);

    // Reuses the captured catalog; scene/native observation does not rebuild it.
    internal static string ObservationStamp(RunState run, PredictorCatalog catalog, Node? root)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var player = LocalContext.GetMe(run) ?? throw new InvalidOperationException("OperationLocalPlayerMissing");
        return Hash(new { Native = NativeStamp(run, player, root), Scene = (object)CaptureScene(run, player, catalog, root) });
    }

    // A cheap observation check for the active Neow hover, not a replacement for
    // the complete capture at admission/publication. It reuses the same native
    // stamp and current offer DTO; it does not rebuild the content catalog.
    internal static string NeowObservationStamp(RunState run, Neow neow, PredictorCatalog catalog, Node? root)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var player = LocalContext.GetMe(run) ?? throw new InvalidOperationException("OperationLocalPlayerMissing");
        return Hash(new
        {
            Native = NativeStamp(run, player, root),
            Scene = (object)PredictorOperationAncientCapture.Capture(run, neow, catalog, root)
        });
    }
}
