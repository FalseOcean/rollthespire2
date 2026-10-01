using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

// Detached values shared with the Crystal Sphere donor. These are copied
// values, never wrappers around a game Player/RunState or Search conditions.
internal sealed record PredictorContext(
    string Seed, string GameVersion, ModelKey Character, int Ascension,
    string CatalogFingerprint, string UnlockFingerprint,
    ImmutableArray<string> RevealedEpochs, ImmutableArray<ModelKey> SeenEncounters,
    ImmutableArray<ModelKey> DiscoveredActs, int NumberOfRuns,
    int? SealOfGoldFixedDeduction = null)
{
    // Captured character pool followed by shared unlocked pool, in source order.
    // Empty while this dependency has not yet been initialized, never "all unlocked".
    public ImmutableArray<PredictorPotionOption> PotionPool { get; init; } = [];
    public PredictorCatalog? Catalog { get; init; }
    public ImmutableArray<ModelKey> OpeningEligibleRelics { get; init; } = [];
    public RolltheSpire2.Core.World.Snapshots.Beta109WorldGenerationSnapshot? AncientAuthority { get; init; }
    public ImmutableArray<PredictorRunModifier> Modifiers { get; init; } = [];
    public int? AccountWongoPoints { get; init; }
    internal bool HasRunModifier(string entry) => Modifiers.Any(m => m.Key.Entry == entry);
}

internal sealed record PredictorRunModifier(ModelKey Key, ModelKey? Character = null);
// Nominal health commands for UI/local-health facts. There is intentionally no
// persistent current/max HP simulation; requested healing can be capped by HP.
internal sealed record PredictorHealthEffect(ModelKey SourceKey, long SourceInstanceId, int MaxHpDelta, int HealRequested);

internal enum PredictorPotionRarity { Common, Uncommon, Rare, Event }
internal sealed record PredictorPotionOption(ModelKey Key, PredictorPotionRarity Rarity, bool CanBeGeneratedInCombat = true);

internal enum PredictorStream { UpFront, Rewards, Transformations, Shops, Niche, CombatPotionGeneration, UnknownMapPoint, TreasureRoomRelics }

internal sealed record PredictorStreamState(PredictorStream Stream, ulong S0, ulong S1, ulong S2, ulong S3, int Calls)
{
    internal Xoshiro256StarStar Restore() => Xoshiro256StarStar.FromState(S0, S1, S2, S3, Calls);
    internal static PredictorStreamState Capture(PredictorStream stream, Xoshiro256StarStar rng) =>
        new(stream, rng.State.S0, rng.State.S1, rng.State.S2, rng.State.S3, rng.CallCount);
}

internal enum PredictorMadScienceRider { None, Sapping, Violence, Choking, Energized, Wisdom, Chaos, Expertise, Curious, Improvement }

internal sealed record PredictorCard(
    long Id, ModelKey Key, EffectCardType Type, int UpgradeLevel, int MaxUpgradeLevel,
    bool Removable, int FloorAdded, ModelKey? Enchantment = null, int EnchantmentAmount = 0,
    int IncreasedBlock = 0, int IncreasedDamage = 0, bool Removed = false)
{
    public EffectCardType TinkerTimeType { get; init; } = EffectCardType.Skill;
    public PredictorMadScienceRider TinkerTimeRider { get; init; }
    public int RoomsEntered { get; init; }
    public int CombatsSeen { get; init; }
    public int? PermanentCostOverride { get; init; }
    public bool HasEternalKeyword { get; init; }
    public bool HasExhaustKeyword { get; init; }
    public bool HasInnateKeyword { get; init; }
    public bool HasRetainKeyword { get; init; }
    public int SpoilsActIndex { get; init; } = -1;
    public RolltheSpire2.Core.Prediction.Maps.MapPosition? SpoilsCoord { get; init; }
}

internal sealed record PredictorRelic(long Id, ModelKey Key, int FloorAdded, bool Melted = false)
{
    public int CombatRewardsSeen { get; init; }
    public int CombatsFinished { get; init; }
    public bool GaveRelic { get; init; }
    public bool LavaRockTriggered { get; init; }
    public int RewardsSacrificed { get; init; }
    public bool IsWax { get; init; }
    public int ToyBoxCombatsSeen { get; init; }
    public int FishingRodCombatsSeen { get; init; }
    public int SwordElitesDefeated { get; init; }
    public int BookOfFiveRingsCardsAdded { get; init; }
    public ImmutableArray<long> BingBongCardsToSkip { get; init; } = [];
    public int FurCoatAct { get; init; } = -1;
    public bool FurCoatCoordsSet { get; init; }
    public ImmutableArray<RolltheSpire2.Core.Prediction.Maps.MapPosition> FurCoatQuestCoords { get; init; } = [];
    public int RewardUpgradeUses { get; init; }
    public bool SilkenUsed { get; init; }
    public bool TookDamageThisCombat { get; init; }
    public int TimesLifted { get; init; }
    public int TimesUsed { get; init; }
    public int GoldenPathAct { get; init; } = -1;
    public bool MawBankSpent { get; init; }
    public bool LizardTailUsed { get; init; }
    public int TreasureRoomsEntered { get; init; }
    public int CombatsLeft { get; init; } = Key.Entry == "EMBER_TEA" ? 5 : Key.Entry is "BONE_TEA" or "TEA_OF_DISCOURTESY" ? 1 : 0;
    public bool GainEnergyInNextCombat { get; init; }
    public int KindleCount { get; init; }
    public ImmutableArray<PredictorCard> StoredCards { get; init; } = [];
    public ModelKey? CharacterTarget { get; init; }
    public long? StarterCardId { get; init; }
    public long? StarterRelicId { get; init; }
    public string? VariantId { get; init; }
    public ModelKey? PreparedCard { get; init; }
}
internal sealed record PredictorPotion(long Id, ModelKey Key);
internal sealed record PredictorOdds(float CardRarityOffset, float PotionDropChance);
internal sealed record PredictorPosition(int Act, int Floor, string NodeId);
internal enum PredictorRunOutcome { Victory, Death, Abandoned }

internal sealed record PredictorState(
    long NextInstanceId, PredictorPosition Position, int GoldBudget,
    ImmutableArray<PredictorCard> Cards, ImmutableArray<long> Deck,
    ImmutableArray<PredictorRelic> Relics, ImmutableArray<PredictorPotion?> PotionSlots,
    ImmutableArray<PredictorStreamState> Streams, PredictorOdds Odds,
    PredictorRelicBag PersonalBag, PredictorRelicBag SharedBag)
{
    public ImmutableArray<ModelKey> OpeningOptions { get; init; } = [];
    public bool OpeningCompleted { get; init; }
    public bool StartedWithNeow { get; init; } = true;
    public int CombatCount { get; init; }
    public bool CurrentRoomIsCombat { get; init; }
    // Vintage removes these CardReward objects from the visible list without
    // unsubscribing their Player.RelicObtained handlers. They remain live.
    public ImmutableArray<PredictorReward> DetachedCardRewards { get; init; } = [];
    public int CardShopRemovalsUsed { get; init; }
    public int WongoPoints { get; init; }
    public ImmutableArray<PredictorHealthEffect> CurrentHealthEffects { get; init; } = [];
    public ImmutableArray<long> HoarderCardsToSkip { get; init; } = [];
    public PredictorRunOutcome? Outcome { get; init; }
    public ImmutableArray<ModelKey> VisitedEvents { get; init; } = [];
    internal PredictorCard Card(long id) => Cards.Single(c => c.Id == id);
    internal PredictorState WithCard(PredictorCard card)
    {
        int index = Cards.FindIndex(c => c.Id == card.Id);
        if (index < 0) throw new InvalidOperationException("PredictorCardMissing");
        return this with { Cards = Cards.SetItem(index, card) };
    }

    internal (int Value, PredictorState State) Draw(PredictorStream stream, int minimum, int maximumExclusive)
    {
        if (maximumExclusive <= minimum) throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
        var cursor = Streams.Single(s => s.Stream == stream);
        var rng = cursor.Restore();
        int value = checked(minimum + rng.NextInt(checked(maximumExclusive - minimum)));
        return (value, WithRng(stream, rng));
    }

    internal PredictorState WithRng(PredictorStream stream, Xoshiro256StarStar rng) =>
        this with { Streams = Streams.SetItem(Streams.FindIndex(s => s.Stream == stream), PredictorStreamState.Capture(stream, rng)) };

    internal void Validate()
    {
        if (Position == null || Odds == null || PersonalBag == null || SharedBag == null ||
            OpeningOptions.IsDefault || CombatCount < 0 || WongoPoints < 0 || CardShopRemovalsUsed < 0 ||
            VisitedEvents.IsDefault || DetachedCardRewards.IsDefault ||
            HoarderCardsToSkip.IsDefault || CurrentHealthEffects.IsDefault ||
            NextInstanceId < 1 || GoldBudget < 0 || Position.Act < 0 || Position.Floor < 0 ||
            string.IsNullOrWhiteSpace(Position.NodeId) || PotionSlots.IsDefault || Cards.IsDefault ||
            Deck.IsDefault || Relics.IsDefault || Streams.IsDefault ||
            Cards.Any(c => c == null) || Relics.Any(r => r == null) || Streams.Any(s => s == null) ||
            !float.IsFinite(Odds.CardRarityOffset) || !float.IsFinite(Odds.PotionDropChance))
            throw new InvalidDataException("PredictorStateInvalid");
        long[] ids = Cards.Select(c => c.Id).Concat(Relics.Select(r => r.Id))
            .Concat(PotionSlots.OfType<PredictorPotion>().Select(p => p.Id)).ToArray();
        if (ids.Any(id => id < 1 || id >= NextInstanceId) || ids.Distinct().Count() != ids.Length ||
            Cards.Any(c => c.Key.Category != "CARD" || !c.Key.IsValid || !Enum.IsDefined(c.Type) || c.UpgradeLevel < 0 || c.MaxUpgradeLevel < c.UpgradeLevel) ||
            Relics.Any(r => r.Key.Category != "RELIC" || !r.Key.IsValid || r.CombatRewardsSeen < 0 || r.CombatsFinished < 0 || r.RewardUpgradeUses < 0 || r.TimesLifted < 0) || PotionSlots.OfType<PredictorPotion>().Any(p => p.Key.Category != "POTION" || !p.Key.IsValid) ||
            Deck.Distinct().Count() != Deck.Length || Deck.Any(id => !Cards.Any(c => c.Id == id && !c.Removed)))
            throw new InvalidDataException("PredictorInstanceInvariant");
        if (Streams.Length != Enum.GetValues<PredictorStream>().Length ||
            Streams.Select(s => s.Stream).Distinct().Count() != Streams.Length ||
            Streams.Any(s => !Enum.IsDefined(s.Stream) || s.Calls < 0 || (s.S0 | s.S1 | s.S2 | s.S3) == 0))
            throw new InvalidDataException("PredictorRngInvariant");
        PersonalBag.Validate();
        SharedBag.Validate();
        if (DetachedCardRewards.Any(r => r == null || r.Kind != PredictorRewardKind.Card || r.Cards.IsDefault ||
            r.SubscriptionOrder < 0 || r.SubscriptionOrder >= NextInstanceId || r.Status != PredictorRewardStatus.Open))
            throw new InvalidDataException("PredictorDetachedRewardInvariant");
        bool InvalidCardReferences(ImmutableArray<long> references) => references.IsDefault ||
            references.Distinct().Count() != references.Length || references.Any(id => !Cards.Any(c => c.Id == id));
        if (InvalidCardReferences(HoarderCardsToSkip) || Relics.Any(r => InvalidCardReferences(r.BingBongCardsToSkip) ||
            r.FishingRodCombatsSeen < 0 || r.SwordElitesDefeated < 0 || r.ToyBoxCombatsSeen < 0) ||
            CurrentHealthEffects.Any(h => h == null || !h.SourceKey.IsValid || h.SourceInstanceId < 0 || h.HealRequested < 0))
            throw new InvalidDataException("PredictorSourceLifetimeInvariant");
    }
}

internal static class PredictorSeedState
{
    // Creates the pre-initialization state. World generation, bag population,
    // modifiers and starting-relic obtains MUST precede an Opening checkpoint.
    internal static PredictorState Create(PredictorContext context, IEnumerable<PredictorCard> startingDeck,
        IEnumerable<ModelKey> startingRelics, int initialGold, int potionCapacity, PredictorOdds odds)
    {
        ValidateContext(context);
        if (potionCapacity < 0) throw new ArgumentOutOfRangeException(nameof(potionCapacity));
        long id = 1;
        var cards = startingDeck.Select(c => c with { Id = id++, Removed = false }).ToImmutableArray();
        var relics = startingRelics.Select(key => new PredictorRelic(id++, key, 1)).ToImmutableArray();
        ulong root = Beta111Profile.Instance.ComputeRootSeed(context.Seed);
        var streams = Enum.GetValues<PredictorStream>().Select(stream => PredictorStreamState.Capture(stream,
            new Xoshiro256StarStar(Beta111Profile.Instance.DeriveNamedStreamSeed(root, Name(stream))))).ToImmutableArray();
        var result = new PredictorState(id, new(0, 0, "seed-initialization"), initialGold, cards,
            cards.Select(c => c.Id).ToImmutableArray(), relics,
            Enumerable.Repeat<PredictorPotion?>(null, potionCapacity).ToImmutableArray(), streams, odds,
            PredictorRelicBag.Empty(false), PredictorRelicBag.Empty(true));
        result.Validate();
        return result;
    }

    internal static void ValidateContext(PredictorContext context)
    {
        if (context == null || !Beta111Profile.Instance.TryCanonicalizeSeed(context.Seed, out string canonical, out _) || canonical != context.Seed ||
            context.GameVersion != "0.111.0" || !context.Character.IsValid || context.Ascension is < 0 or > 10 ||
            string.IsNullOrWhiteSpace(context.CatalogFingerprint) || string.IsNullOrWhiteSpace(context.UnlockFingerprint) ||
            context.RevealedEpochs.IsDefault || context.SeenEncounters.IsDefault || context.DiscoveredActs.IsDefault || context.Modifiers.IsDefault ||
            context.AccountWongoPoints < 0 || context.Modifiers.Any(m => m == null || !m.Key.IsValid || m.Key.Category != "MODIFIER" || !KnownModifier(m.Key.Entry) ||
                m.Key.Entry == "CHARACTER_CARDS" && (m.Character == null || !m.Character.Value.IsValid)) ||
            context.PotionPool.IsDefault || context.PotionPool.Any(p => p == null || p.Key.Category != "POTION" || !p.Key.IsValid || !Enum.IsDefined(p.Rarity)) ||
            context.NumberOfRuns < 0 || context.SealOfGoldFixedDeduction < 0)
            throw new InvalidDataException("PredictorContextInvalid");
    }

    // Validate a frozen vanilla identity, not combinations of modifiers. Unknown
    // caller keys must not silently act as modifiers with no effects.
    private static bool KnownModifier(string entry) => entry is
        "ALL_STAR" or "BIG_GAME_HUNTER" or "CHARACTER_CARDS" or "CURSED_RUN" or
        "DEADLY_EVENTS" or "DRAFT" or "FLIGHT" or "HOARDER" or "INSANITY" or "MIDAS" or
        "MURDEROUS" or "NIGHT_TERRORS" or "SEALED_DECK" or "SPECIALIZED" or "TERMINAL" or "VINTAGE";

    private static string Name(PredictorStream stream) => stream switch
    {
        PredictorStream.UpFront => "up_front", PredictorStream.Rewards => "rewards",
        PredictorStream.Transformations => "transformations", PredictorStream.Shops => "shops",
        PredictorStream.Niche => "niche", PredictorStream.CombatPotionGeneration => "combat_potion_generation",
        PredictorStream.UnknownMapPoint => "unknown_map_point", PredictorStream.TreasureRoomRelics => "treasure_room_relics",
        _ => throw new ArgumentOutOfRangeException(nameof(stream))
    };
}

internal static class PredictorArray
{
    internal static int FindIndex<T>(this ImmutableArray<T> values, Func<T, bool> predicate)
    {
        for (int i = 0; i < values.Length; i++) if (predicate(values[i])) return i;
        return -1;
    }
}
