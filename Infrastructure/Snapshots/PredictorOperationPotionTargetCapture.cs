using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

// Detached generation inputs only: no scene, combat serialization, use or hooks.
internal static class PredictorOperationPotionTargetCapture
{
    internal static PredictorContext Capture(RunState run, ulong targetPlayerId, string version)
        => CaptureContext(run, Target(run, targetPlayerId), version, null, melody: true);

    internal static PredictorOperationPotionObservation CaptureLive(RunState run, ulong targetPlayerId, string version,
        PredictorOperationPotionSource source, PredictorOperationPotionObservation? previous = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var target = Target(run, targetPlayerId);
        RequireSource(run, target, source);
        bool cards = source.Potion.Key.Entry != "ENTROPIC_BREW";
        bool melody = source.Potion.Key.Entry == "BOTTLED_SONG" || target.PotionSlots.Any(p => p?.Id.Entry == "BOTTLED_SONG");
        string before = Stamp(run, target, cards, melody);
        var context = CaptureContext(run, target, version, previous?.Context, cards, verify: false, melody: melody);
        var stream = context.NonCombatStream(PredictorStream.CombatPotionGeneration);
        var cursor = PotionGeneration(run, target, stream).ToSerializable();
        var inventory = target.PotionSlots.Select(p => p == null ? null : new PredictorPotion(Id(p), Key(p))).ToImmutableArray();
        bool blocked = AcquisitionBlocked(target);
        if (Stamp(run, target, cards, melody) != before) throw new InvalidOperationException("OperationPotionTargetSnapshotChanged");
        RequireSource(run, target, source);
        return new(context, source, inventory,
            new(stream, cursor.state0, cursor.state1, cursor.state2, cursor.state3, cursor.counter),
            CombatManager.Instance.IsInProgress, blocked, before);
    }

    internal static string ObservationStamp(RunState run, ulong targetPlayerId) => Stamp(run, Target(run, targetPlayerId));
    internal static string ObservationStamp(RunState run, ulong targetPlayerId, PredictorOperationPotionSource source)
    {
        var target = Target(run, targetPlayerId);
        RequireSource(run, target, source);
        return Stamp(run, target, source.Potion.Key.Entry != "ENTROPIC_BREW",
            source.Potion.Key.Entry == "BOTTLED_SONG" || target.PotionSlots.Any(p => p?.Id.Entry == "BOTTLED_SONG"));
    }
    internal static string ObservationStamp(RunState run) => Stamp(run,
        LocalContext.GetMe(run) ?? throw new InvalidOperationException("OperationLocalPlayerMissing"));

    private static Player Target(RunState run, ulong targetPlayerId)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ArgumentNullException.ThrowIfNull(run);
        return run.Players.SingleOrDefault(p => p.NetId == targetPlayerId)
            ?? throw new ArgumentException("OperationPotionTargetNotInRun", nameof(targetPlayerId));
    }

    private static void RequireSource(RunState run, Player target, PredictorOperationPotionSource source)
    {
        if (source == null || source.Potion == null || !Supported(source.Potion.Key) ||
            source.HeldSlot.HasValue == source.RewardId.HasValue)
            throw new ArgumentException("OperationPotionSourceInvalid", nameof(source));
        if (source.HeldSlot is { } slot)
        {
            if (slot < 0 || slot >= target.PotionSlots.Count || target.PotionSlots[slot] is not { } bottle ||
                Id(bottle) != source.Potion.Id || Key(bottle) != source.Potion.Key)
                throw new ArgumentException("PotionPreviewInstanceMismatch", nameof(source));
        }
        else
        {
            var set = PredictorOperationCapture.CurrentNativeRewards(run, target, NRun.Instance);
            var matches = set == null ? Array.Empty<PotionReward>() : PredictorOperationCapture.NativeRewardLeaves(set.Rewards)
                .OfType<PotionReward>().Where(r => Id(r) == source.RewardId && ReferenceEquals(r.Player, target) &&
                    !r.SuccessfullySelected && r.Potion is { } bottle && Id(bottle) == source.Potion.Id &&
                    Key(bottle) == source.Potion.Key).ToArray();
            if (matches.Length != 1) throw new ArgumentException("PotionPreviewRewardMismatch", nameof(source));
        }
    }

    internal static bool Supported(ModelKey potion) => PredictorOperationPotionQueries.Supports(potion) ||
        potion.Category == "POTION" && potion.Entry == "ENTROPIC_BREW";

    private static bool AcquisitionBlocked(Player target) => target.IsActiveForHooks &&
        target.Relics.Any(r => r.GetType() == typeof(Sozu) && !r.IsMelted);
    private static CardModel[] CharacterCards(RunState run, Player target) => target.Character.CardPool
        .GetUnlockedCards(target.UnlockState, ((IRunState)run).CardMultiplayerConstraint).ToArray();
    private static CardModel[] ColorlessCards(RunState run, Player target) => ModelDb.CardPool<ColorlessCardPool>()
        .GetUnlockedCards(target.UnlockState, ((IRunState)run).CardMultiplayerConstraint).ToArray();

    internal static bool IsMelodyTag(CardTag tag) => tag.ToString() is "Wah" or "Cho" or "Ki";

    internal static CardModel[] MelodyCards(Player target, Func<CardPoolModel, IEnumerable<CardModel>> pool) =>
        target.UnlockState.CharacterCardPools.SelectMany(pool)
            .Where(card => card.Tags.Any(IsMelodyTag)).ToArray();

    internal static ImmutableArray<string> MelodyTags(CardModel card) =>
        card.Tags.Where(IsMelodyTag).Select(tag => tag.ToString()).Order(StringComparer.Ordinal).ToImmutableArray();

    private static PredictorContext CaptureContext(RunState run, Player target, string version, PredictorContext? previous,
        bool cards = true, bool verify = true, bool melody = false)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("OperationPotionVersionMissing", nameof(version));
        string? before = verify ? Stamp(run, target, cards) : null;
        var unlocks = target.UnlockState.ToSerializable();
        var own = cards ? CharacterCards(run, target) : [];
        var colorless = cards ? ColorlessCards(run, target) : [];
        var levels = new CatalogLevels(previous);
        var melodies = cards && melody ? MelodyCards(target, pool =>
            pool.GetUnlockedCards(target.UnlockState, ((IRunState)run).CardMultiplayerConstraint)) : [];
        var models = own.Concat(colorless).Concat(melodies).DistinctBy(c => c.Id).ToArray();
        var definitions = models.Select(c =>
        {
            bool opaque = c.GetType().Assembly != typeof(CardModel).Assembly;
            var type = Enum.TryParse<EffectCardType>(c.Type.ToString(), out var t) ? t : EffectCardType.Other;
            var prototype = new PredictorCard(0, Key(c), type, c.CurrentUpgradeLevel, c.MaxUpgradeLevel, c.IsRemovable, 0);
            // Never execute custom upgrade implementations.
            var capturedLevels = opaque
                ? Enumerable.Range(0, c.MaxUpgradeLevel + 1).Select(level => new PredictorCardLevelMetadata(level,
                    c.EnergyCost.Canonical, c.EnergyCost.CostsX, c.Keywords.Contains(CardKeyword.Unplayable),
                    c.Keywords.Contains(CardKeyword.Exhaust), c.Keywords.Contains(CardKeyword.Innate),
                    c.Keywords.Contains(CardKeyword.Retain), c.Keywords.Contains(CardKeyword.Eternal))).ToImmutableArray()
                : levels.Capture(c);
            return new PredictorCardDefinition(prototype, c.Rarity.ToString(), Key(c.Pool), c.Type == CardType.Quest,
                c.Tags.Contains(CardTag.Strike), c.Tags.Contains(CardTag.Defend))
            {
                Unplayable = c.Keywords.Contains(CardKeyword.Unplayable), CostsX = c.EnergyCost.CostsX,
                HasLocalExhaust = c.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Exhaust),
                CanonicalCost = c.EnergyCost.Canonical, Levels = capturedLevels
            };
        }).ToImmutableArray();
        var catalog = new PredictorCatalog([], []) { CardDefinitions = definitions };
        ImmutableArray<PredictorOperationPotionCard> Copy(IEnumerable<CardModel> cards) => cards.Select(c =>
            new PredictorOperationPotionCard(Card(c, catalog) with { Id = 0 }, c.Rarity.ToString(), c.CanBeGeneratedInCombat,
                c.MultiplayerConstraint.ToString()) { MelodyTags = MelodyTags(c) }).ToImmutableArray();
        var cursor = run.Rng.CombatCardGeneration.ToSerializable();
        var opaqueModels = models.Where(c => c.GetType().Assembly != typeof(CardModel).Assembly).Select(Key)
            .Concat(target.Character.GetType().Assembly != typeof(CardModel).Assembly ? [Key(target.Character)] : [])
            .Distinct().ToImmutableArray();
        var context = new PredictorContext(run.Rng.StringSeed, version, Key(target.Character), run.AscensionLevel,
            Hash(catalog), Hash(unlocks), unlocks.UnlockedEpochs.ToImmutableArray(),
            unlocks.EncountersSeen.Select(Key).ToImmutableArray(), run.Acts.Select(Key).ToImmutableArray(), unlocks.NumberOfRuns)
        {
            Catalog = cards ? catalog : null,
            PotionPool = cards ? [] : PotionFactory.GetPotionOptions(target).Select(p => new PredictorPotionOption(Key(p),
                Enum.Parse<PredictorPotionRarity>(p.Rarity.ToString()), p.CanBeGeneratedInCombat)).ToImmutableArray(),
            Operation = new(target.NetId, run.Players.ToList().IndexOf(target), run.Players.Count, opaqueModels)
            {
                CombatCardRng = cards ? new(cursor.state0, cursor.state1, cursor.state2, cursor.state3, cursor.counter) : null,
                CombatCardPools = cards ? new(Copy(own), Copy(colorless)) { MelodyCards = Copy(melodies) } : null
            }
        };
        if (verify && Stamp(run, target, cards) != before) throw new InvalidOperationException("OperationPotionTargetSnapshotChanged");
        levels.Remember(context);
        return context;
    }

    private static string Stamp(RunState run, Player target, bool cards = true, bool melody = false)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        // Observe current source order/metadata on every poll. Only immutable
        // native upgrade definitions are reused, never a prediction or RNG.
        object Cards(IEnumerable<CardModel> cards) => cards.Select(c => new
        {
            Identity = Id(c), Key = Key(c), c.Type, c.Rarity, c.CanBeGeneratedInCombat, c.MultiplayerConstraint,
            c.CurrentUpgradeLevel, c.MaxUpgradeLevel, c.IsRemovable, c.EnergyCost.Canonical, c.EnergyCost.CostsX,
            Keywords = c.GetKeywordsWithSources(KeywordSources.Local).ToArray(), Tags = c.Tags.ToArray(),
            // The same copied fields used by the identity query remain fresh
            // for mutable/mock pool entries too (local cost/enchantment, etc.).
            Card = Card(c, new PredictorCatalog([], [])) with { Id = 0 }
        }).ToArray();
        return Hash(new
        {
            Run = Id(run), run.Rng.StringSeed, run.AscensionLevel,
            Players = run.Players.Select(p => p.NetId).ToArray(), Target = target.NetId,
            Character = new { Key = Key(target.Character), Identity = Id(target.Character) },
            Unlocks = target.UnlockState.ToSerializable(),
            CardRng = cards ? run.Rng.CombatCardGeneration.ToSerializable() : null,
            PotionRng = !cards ? run.Rng.CombatPotionGeneration.ToSerializable() : null,
            NonCombatPotionRng = !cards ? PredictorOperationCapture.TryNonCombatGeneration(target)?.ToSerializable() : null,
            CharacterCards = cards ? Cards(CharacterCards(run, target)) : null,
            ColorlessCards = cards ? Cards(ColorlessCards(run, target)) : null,
            MelodyCards = cards && melody ? Cards(MelodyCards(target, pool =>
                pool.GetUnlockedCards(target.UnlockState, ((IRunState)run).CardMultiplayerConstraint))) : null,
            Potions = !cards ? PotionFactory.GetPotionOptions(target).Select(p => new
                { Identity = Id(p), Key = Key(p), p.Rarity, p.CanBeGeneratedInCombat }).ToArray() : null,
            Inventory = target.PotionSlots.Select(p => p == null ? null : new PredictorPotion(Id(p), Key(p))).ToArray(),
            InCombat = CombatManager.Instance.IsInProgress, HasSozu = AcquisitionBlocked(target)
        });
    }

    private static Rng PotionGeneration(RunState run, Player target, PredictorStream stream) =>
        stream == PredictorStream.NonCombatGeneration ? PredictorOperationCapture.NonCombatGeneration(target) : run.Rng.CombatPotionGeneration;

    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true }))));
}
