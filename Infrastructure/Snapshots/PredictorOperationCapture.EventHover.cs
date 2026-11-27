using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    internal sealed record EventHoverSource(EventModel Event, NDeckTransformSelectScreen? Selector, bool LegacyRewardHooks);
    private sealed record EventHoverPool(CardPoolModel Model, CardModel[] Cards);
    private sealed record EventHoverInputs(string Facts, Player Player, PredictorOperationEventCapture.EventHoverPage Page,
        EventHoverPool[] Pools, IReadOnlyList<CardModel> Originals, ImmutableArray<long> Selected, int CardsRequired,
        ImmutableArray<PredictorRelic> Relics, CardModel[] ReservedCards, ImmutableArray<PredictorPotion?> Potions,
        ImmutableArray<PredictorRunModifier> Modifiers);
    private sealed record EventHoverEntry(PredictorOperationSnapshot Snapshot, string Facts);
    private static readonly ConditionalWeakTable<EventModel, EventHoverEntry> EventHoverEntries = new();
    // Older supported builds have the multiplayer check on the unlocked pool,
    // while later builds also apply a card-level run-mode filter.
    private static readonly Func<CardModel, IRunState, bool>? NativeCardAllowed =
        typeof(CardModel).GetMethod("IsAllowedInRun", [typeof(IRunState)])?.CreateDelegate<Func<CardModel, IRunState, bool>>();

    internal static bool TryCaptureEventHover(RunState run, string version, NDeckTransformSelectScreen? selector,
        out EventHoverSource? source, out PredictorOperationSnapshot? snapshot, out string? stamp, CaptureTiming timing)
    {
        source = null; snapshot = null; stamp = null;
        if (run.CurrentRoom is not EventRoom room) return false;
        var current = new EventHoverSource(room.LocalMutableEvent, selector,
            RuntimeVersionCompatibility.Resolve(version).SemanticProfileId != RngSemanticProfileId.Modern113);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long observed = Stopwatch.GetTimestamp();
        if (ObserveEventHover(run, current) is not { } inputs) return false;
        timing.NativeStampMs = Stopwatch.GetElapsedTime(observed).TotalMilliseconds;
        if (EventHoverEntries.TryGetValue(current.Event, out var previous) &&
            previous.Snapshot.Context.GameVersion == version && previous.Facts == inputs.Facts)
        {
            snapshot = previous.Snapshot;
            timing.SnapshotReuses++;
        }
        else
        {
            snapshot = CaptureEventHover(run, version, current, inputs, previous?.Snapshot, timing);
            observed = Stopwatch.GetTimestamp();
            var verified = ObserveEventHover(run, current);
            timing.NativeStampMs += Stopwatch.GetElapsedTime(observed).TotalMilliseconds;
            if (verified is null || verified.Facts != inputs.Facts || !verified.Selected.SequenceEqual(inputs.Selected)) return false;
            EventHoverEntries.Remove(current.Event);
            EventHoverEntries.Add(current.Event, new(snapshot, inputs.Facts));
        }
        stamp = EventHoverStamp(inputs);
        var eventScene = (PredictorOperationEventScene)snapshot.Scene;
        snapshot = snapshot with { Fingerprint = stamp, SelectedCardIds = inputs.Selected,
            Scene = eventScene with { SelectedCardIds = inputs.Selected } };
        timing.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        source = current;
        return true;
    }

    internal static string? EventHoverObservationStamp(RunState run, EventHoverSource source, CaptureTiming? timing = null)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        string? stamp = ObserveEventHover(run, source) is { } inputs ? EventHoverStamp(inputs) : null;
        if (timing != null) timing.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return stamp;
    }

    private static string EventHoverStamp(EventHoverInputs inputs) =>
        inputs.Selected.IsEmpty ? inputs.Facts : Hash(new { inputs.Facts, inputs.Selected });

    private static EventHoverInputs? ObserveEventHover(RunState run, EventHoverSource source)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var ev = source.Event;
        if (run.CurrentRoom is not EventRoom room || !ReferenceEquals(room.LocalMutableEvent, ev) || ev.IsFinished ||
            LocalContext.GetMe(run) is not { } player || !ReferenceEquals(ev.Owner, player) || HasPendingNativeObtain(player) ||
            ReadNullable(ev, "_currentOptions") is not IReadOnlyList<EventOption> options) return null;

        bool transform = source.Selector != null;
        var scope = PredictorOperationEventCapture.ReadHoverOptions(ev, options, transform);
        if (scope is null) return null;
        var top = NOverlayStack.Instance?.Peek();
        if (source.Selector == null ? top != null : !ReferenceEquals(top, source.Selector) || !SelectionIsPending(source.Selector))
            return null;

        IReadOnlyList<CardModel> originals = source.Selector == null ? [] : Read<IReadOnlyList<CardModel>>(source.Selector, "_cards");
        var prefs = source.Selector == null ? default : Read<CardSelectorPrefs>(source.Selector, "_prefs");
        if (transform)
        {
            var deck = player.Deck.Cards.ToHashSet(ReferenceEqualityComparer.Instance);
            if (prefs.MinSelect < 1 || prefs.MinSelect != prefs.MaxSelect || prefs.MinSelect > originals.Count ||
                originals.Any(card => !deck.Contains(card))) return null;
        }
        var selected = source.Selector == null ? [] : Read<IEnumerable<CardModel>>(source.Selector, "_selectedCards").Select(Id).ToImmutableArray();
        var pools = transform ? originals.Select(TransformPool).Distinct().ToArray() : [player.Character.CardPool];
        var requestedFaces = scope.Offers.Select(offer => (Enum.Parse<CardType>(offer.Option.CardType!.Value.ToString()),
            Enum.Parse<CardRarity>(offer.Rarity.ToString()))).ToHashSet();
        var memberships = pools.Select(pool => new EventHoverPool(pool,
            pool.GetUnlockedCards(player.UnlockState, ((IRunState)run).CardMultiplayerConstraint)
                .Where(card => (transform || requestedFaces.Contains((card.Type, card.Rarity))) &&
                    (NativeCardAllowed == null || NativeCardAllowed(card, run))).ToArray())).ToArray();
        // Keep the reference for definition replacement and observe the same
        // public fields the catalog reads. A patch can change these in place.
        var members = memberships.SelectMany(pool => pool.Cards).Concat(originals)
            .DistinctBy(ContentId).Select(card => new
            {
                Ref = ContentId(card), Key = Key(card), card.Rarity, card.Type, card.CurrentUpgradeLevel,
                card.MaxUpgradeLevel, card.GainsBlock, card.MultiplayerConstraint, card.CanBeGeneratedInCombat,
                Cost = card.EnergyCost.Canonical, card.EnergyCost.CostsX, card.Keywords,
                LocalKeywords = card.GetKeywordsWithSources(KeywordSources.Local), card.Tags,
                Pool = ContentId(TransformPool(card)), card.IsCanonical, card.IsMock
            }).ToArray();
        var reserved = new List<CardModel>();
        long CardId(CardModel card)
        {
            reserved.Add(card);
            return Id(card);
        }
        var relics = player.Relics.Select(relic => relic.Id.Entry == "PAELS_TOOTH"
            ? new PredictorRelic(Id(relic), Key(relic), relic.FloorAddedToDeck, relic.IsMelted)
            : Relic(relic, EmptyEventHoverCatalog, CardId)).ToImmutableArray();
        var potions = player.PotionSlots.Select(potion => potion == null ? null :
            new PredictorPotion(Id(potion), Key(potion))).ToImmutableArray();
        var modifiers = run.Modifiers.Select(modifier => new PredictorRunModifier(Key(modifier),
            modifier is CharacterCards cc ? Key(cc.CharacterModel) : null)).ToImmutableArray();
        string facts = Hash(new
        {
            Run = Id(run), Room = Id(room), Event = Id(ev), run.CurrentActIndex, run.TotalFloor, run.GameMode, run.AscensionLevel,
            Player = Id(player), Character = ContentId(player.Character), Players = run.Players.Select(Id).ToArray(),
            Scope = scope, EventRng = transform ? ev.Rng.ToSerializable() : null,
            Rewards = transform ? null : player.PlayerRng.Rewards.ToSerializable(),
            Niche = !transform && source.LegacyRewardHooks && relics.Any(relic => !relic.Melted && relic.Key.Entry == "WING_CHARM")
                ? run.Rng.Niche.ToSerializable() : null,
            Deck = source.Selector == null ? null : player.Deck.Cards.Select(card => Card(card, EmptyEventHoverCatalog)).ToArray(),
            Relics = relics, Modifiers = modifiers, Potions = potions,
            player.Gold, player.Creature.CurrentHp, player.Creature.MaxHp,
            Selector = source.Selector == null ? 0 : Id(source.Selector), Completion = source.Selector == null ? 0 : Id(ReadNullable(source.Selector, "_completionSource")!),
            Candidates = originals.Select(Id).ToArray(), prefs.MinSelect, prefs.MaxSelect, prefs.Cancelable, prefs.RequireManualConfirmation,
            Pools = memberships.Select(pool => new { Pool = ContentId(pool.Model), Cards = pool.Cards.Select(ContentId).ToArray() }).ToArray(),
            Members = members
        });
        return new(facts, player, scope, memberships, originals, selected, prefs.MinSelect, relics, reserved.ToArray(), potions, modifiers);
    }

    private static PredictorOperationSnapshot CaptureEventHover(RunState run, string version, EventHoverSource source,
        EventHoverInputs input, PredictorOperationSnapshot? previous, CaptureTiming timing)
    {
        timing.ScopedEventCaptures++;
        long started = Stopwatch.GetTimestamp();
        var player = input.Player;
        bool transform = source.Selector != null;
        IReadOnlyList<CardModel> deck = transform ? player.Deck.Cards : [];
        var levels = new CatalogLevels(previous, timing);
        var models = input.Pools.SelectMany(pool => pool.Cards).Concat(input.Originals)
            .Select(card => card.Id).Distinct().Select(ModelDb.GetById<CardModel>).ToArray();
        var catalog = new PredictorCatalog(transform ? [] : input.Pools[0].Cards.Select((card, index) =>
            CrystalSphereCatalogCapture.CardSnapshot(card, index, input.Pools[0].Model)).ToImmutableArray(), [])
        {
            CardDefinitions = models.Select(card => CompleteCardDefinition(
                CrystalSphereCatalogCapture.CaptureCardDefinition(card, levels.Capture), card)).ToImmutableArray(),
            TransformationPools = transform ? input.Pools.Select(pool =>
                new PredictorCardPool(Key(pool.Model), pool.Cards.Select(Key).ToImmutableArray())).ToImmutableArray() : [],
            ShopGainsBlock = input.Relics.Any(relic => !relic.Melted && relic.Key.Entry == "FRESNEL_LENS")
                ? models.Where(card => card.Id.Entry is not ("SOVEREIGN_BLADE" or "MAD_SCIENCE") && card.GainsBlock)
                    .Select(Key).ToImmutableArray() : []
        };
        timing.CatalogMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var opaqueModels = models.Cast<AbstractModel>().Concat(deck).Concat(player.Relics).Concat(run.Modifiers)
            .Concat(deck.Select(card => card.Enchantment).OfType<EnchantmentModel>()).Append(player.Character)
            .Where(model => model.GetType().Assembly != typeof(CardModel).Assembly).Select(Key).Distinct().ToImmutableArray();
        // The digest represents the observed, ordered unlocked membership of these pools.
        var context = new PredictorContext(run.Rng.StringSeed, version, Key(player.Character), run.AscensionLevel,
            input.Facts, input.Facts, [], [], [], 0)
        {
            Catalog = catalog, Modifiers = input.Modifiers,
            Operation = new(player.NetId, run.Players.ToList().IndexOf(player), run.Players.Count, opaqueModels)
        };
        var cards = deck.Select(card => Card(card, catalog)).ToImmutableArray();
        var removed = input.ReservedCards.DistinctBy(Id).Where(card => !deck.Contains(card)).Select(card => Card(card, catalog) with { Removed = true });
        var scene = new PredictorOperationEventScene(Key(source.Event), CrystalSphereLiveCapture.RngState(PredictorStream.Niche, source.Event.Rng),
            input.Page.Offers.Select(offer => offer.Option).ToImmutableArray(), new(player.Creature.CurrentHp, player.Creature.MaxHp))
        {
            Branch = input.Page.Branch, AwaitingCards = transform, CardsRequired = input.CardsRequired,
            CardCandidates = input.Originals.Select(Id).ToImmutableArray(), SelectedCardIds = input.Selected,
            FuturePotionTypes = input.Page.Offers.ToImmutableDictionary(offer => offer.Option.PotionId!.Value, offer => offer.Option.CardType!.Value),
            FuturePotionRarities = input.Page.Offers.ToImmutableDictionary(offer => offer.Option.PotionId!.Value, offer => offer.Rarity),
            ConveyorDish = input.Page.Conveyor?.Dish, ConveyorLastDish = input.Page.Conveyor?.Last,
            ConveyorGrabs = input.Page.Conveyor?.Grabs ?? 0, ConveyorHadGrab = input.Page.Conveyor != null
        };
        // The shared simulator requires its stream table and empty bags even when this scene
        // can only settle card replacement or construct one prefiltered reward.
        var state = new PredictorState(NextInstanceId, new(run.CurrentActIndex + 1, run.TotalFloor, "event-hover"), player.Gold,
            cards.AddRange(removed), cards.Select(card => card.Id).ToImmutableArray(), input.Relics, input.Potions,
            CaptureStreams(run, player, context), new(0, 0), PredictorRelicBag.Empty(false), PredictorRelicBag.Empty(true));
        var snapshot = new PredictorOperationSnapshot(input.Facts, context, state, scene, player.Creature.CurrentHp, player.Creature.MaxHp);
        levels.Remember(snapshot);
        return snapshot;
    }

    private static readonly PredictorCatalog EmptyEventHoverCatalog = new([], []);

    private static CardPoolModel TransformPool(CardModel card) =>
        card.Type == CardType.Quest || card.Rarity is CardRarity.Event or CardRarity.Ancient or CardRarity.Token
            ? ModelDb.CardPool<ColorlessCardPool>() : card.Pool;
}
