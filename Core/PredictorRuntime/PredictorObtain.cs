using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorObtainReturn { Opening, Reward, RewardAlternative, Shop, Event, Treasure, Rest, RestHeal, EventRestHeal, Ancient, InternalLargeCapsule, InternalTouch }
internal sealed record PredictorObtainCompletion(PredictorObtainReturn Destination, long SourceId);
internal sealed record ChooseScrollBoxBundle(int BundleIndex) : PredictorInput;
internal sealed record ChooseObtainCard(long? CardId) : PredictorInput;
internal sealed record ChooseObtainDeckCards(ImmutableArray<long> CardIds) : PredictorInput;
internal sealed record ChooseObtainManyCards(ImmutableArray<long> CardIds) : PredictorInput;
internal enum PredictorObtainDeckOperation { Remove, Upgrade, Enchant, Astrolabe, PaelsTooth, Claws, Clone }

// One frame per suspended AfterObtained. Frames are transaction residue: the
// completed node checkpoint contains only the resulting state and inputs.
internal sealed record PredictorObtainFrame(
    ModelKey Relic, PredictorObtainReturn Destination, long SourceId,
    ImmutableArray<PredictorReward> ParentRewards, PredictorPhase ParentPhase,
    int Stage, bool Mandatory);

internal sealed partial class PredictorRun
{
    private ImmutableArray<PredictorObtainFrame> _obtainFrames = [];
    private ImmutableArray<PredictorObtainCompletion> _completedObtains = [];
    private PredictorObtainCompletion? _newLeafObtain;
    private PredictorObtainCompletion? _touchPending;
    private ImmutableArray<ImmutableArray<PredictorCard>> _scrollBoxBundles = [];
    private PredictorObtainCompletion? _leafyPending;
    private PredictorObtainCompletion? _fragrantPending;
    private PredictorObtainCompletion? _wafflePending;
    private PredictorObtainCompletion? _fakeWafflePending;
    private PredictorObtainCompletion? _serePending;
    private PredictorObtainCompletion? _precariousPending;
    private PredictorObtainCompletion? _obtainCardPending;
    private ModelKey _obtainCardSource;
    private ImmutableArray<PredictorCard> _obtainCardChoices = [];
    internal ImmutableArray<PredictorCard> ObtainCardChoices => _obtainCardChoices;
    private PredictorObtainCompletion? _obtainDeckPending;
    private ModelKey _obtainDeckSource;
    private PredictorObtainDeckOperation _obtainDeckOperation;
    private int _obtainDeckCount;
    private long _obtainDeckRelicId;
    private ImmutableArray<long> _obtainDeckCandidateOrder = [];
    internal ImmutableArray<long> ObtainDeckCandidates => _obtainDeckPending == null ? [] :
        _obtainDeckCandidateOrder.Length > 0 ? _obtainDeckCandidateOrder : Working.Deck.Where(id =>
        _obtainDeckOperation switch
        {
            PredictorObtainDeckOperation.Remove => Working.Card(id).Removable,
            PredictorObtainDeckOperation.Upgrade => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel,
            PredictorObtainDeckOperation.Enchant => PredictorObtainSources.CanEnchant(Context, Working.Card(id)) &&
                (_obtainDeckSource.Entry != "TRI_BOOMERANG" || Working.Card(id).Type == EffectCardType.Attack) &&
                (_obtainDeckSource.Entry != "ELECTRIC_SHRYMP" || Working.Card(id).Type == EffectCardType.Skill) &&
                (_obtainDeckSource.Entry is not ("GNARLED_HAMMER" or "PUNCH_DAGGER") || Working.Card(id).Type == EffectCardType.Attack) &&
                (_obtainDeckSource.Entry != "ROYAL_STAMP" || Working.Card(id).Type is EffectCardType.Attack or EffectCardType.Skill),
            PredictorObtainDeckOperation.Astrolabe => Working.Card(id).Removable &&
                !PredictorCardChanges.Definition(Context, Working.Card(id).Key).IsQuest,
            PredictorObtainDeckOperation.PaelsTooth => Working.Card(id).Removable &&
                Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel,
            PredictorObtainDeckOperation.Claws => Working.Card(id).Removable &&
                !PredictorCardChanges.Definition(Context, Working.Card(id).Key).IsQuest,
            PredictorObtainDeckOperation.Clone => !PredictorCardChanges.Definition(Context, Working.Card(id).Key).IsQuest,
            _ => false
        }).ToImmutableArray();
    private PredictorObtainCompletion? _obtainManyPending;
    private ImmutableArray<PredictorCard> _obtainManyChoices = [];
    internal ImmutableArray<PredictorCard> ObtainManyCardChoices => _obtainManyChoices;
    internal ImmutableArray<PredictorReward> CompletedChildRewards { get; private set; } = [];
    internal ImmutableArray<ImmutableArray<PredictorCard>> ScrollBoxBundles => _scrollBoxBundles;
    internal bool HasObtainChild => _obtainFrames.Length > 0 && Phase == PredictorPhase.Rewards;
    internal PredictorObtainCompletion? ConsumeCompletedObtain()
    {
        if (_completedObtains.Length == 0) return null;
        var completed = _completedObtains[0];
        _completedObtains = _completedObtains.RemoveAt(0);
        return completed;
    }

    // Returns true if this Obtain and its AfterObtained completed synchronously.
    // A false result exposes concrete child choices through the current Rewards.
    internal bool BeginObtainRelic(ModelKey key, long? existingInstanceId,
        PredictorObtainReturn destination, long sourceId, CancellationToken token = default,
        PredictorRelic? prepared = null, int? insertIndex = null)
    {
        token.ThrowIfCancellationRequested();
        if (key.Category != "RELIC" || !key.IsValid) throw new ArgumentException("PredictorObtainKeyInvalid");
        if (destination is PredictorObtainReturn.Opening or PredictorObtainReturn.Ancient)
            BeginAncientOutcomeObservation(key);
        var state = PredictorSettlementEffects.AddRelic(Working, key, existingInstanceId, Context);
        long obtainedId = state.Relics[^1].Id;
        if (prepared != null)
        {
            if (prepared.Key != key || prepared.Id is < 0) throw new ArgumentException("PredictorPreparedRelicMismatch");
            var inserted = state.Relics[^1];
            state = state with { Relics = state.Relics.SetItem(state.Relics.Length - 1,
                inserted with { CharacterTarget = prepared.CharacterTarget, StarterCardId = prepared.StarterCardId,
                    StarterRelicId = prepared.StarterRelicId, VariantId = prepared.VariantId,
                    PreparedCard = prepared.PreparedCard, IsWax = prepared.IsWax }) };
        }
        if (destination == PredictorObtainReturn.Reward && Rewards.Any(r => r.Id == sourceId && r.IsWaxRelic))
            state = state with { Relics = state.Relics.SetItem(state.Relics.Length - 1,
                state.Relics[^1] with { IsWax = true }) };
        if (insertIndex is int index)
        {
            if (index < 0 || index >= state.Relics.Length) throw new ArgumentOutOfRangeException(nameof(insertIndex));
            var inserted = state.Relics[^1];
            state = state with { Relics = state.Relics.RemoveAt(state.Relics.Length - 1).Insert(index, inserted) };
        }
        Working = state;
        // RelicObtained notification precedes bag removal and AfterObtained.
        NotifyOpenCardRewards(key, obtainedId);
        if (Context.Operation?.IgnoresBehavior(key) == true || Context.Crystal?.UnmodeledSources.Contains(key) == true)
        {
            // Preserve the actual object/bag bookkeeping; only its additional
            // behavior is outside the explicit operation/Crystal premise.
            // Crystal permits only the captured, diagnosed unmodeled identities;
            // native relics retain their implemented lifecycle and known hooks.
            Working = Working with { PersonalBag = Working.PersonalBag.Remove(key), SharedBag = Working.SharedBag.Remove(key) };
            return true;
        }
        var beforeAfterRelic = Working;
        if (ApplyLiveDeckRandomObtainBoundary(key, destination, sourceId)) return true;
        _preview.Partial?.Synchronize(Working);
        Working = PredictorSettlementEffects.AfterRelicAdded(Working, key);
        if (key.Entry is "WHETSTONE" or "WAR_PAINT")
        {
            foreach (long cardId in Working.Deck)
            {
                var card = Working.Card(cardId);
                if (card.UpgradeLevel != beforeAfterRelic.Card(cardId).UpgradeLevel)
                    Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, card));
            }
            ObserveOperationRandomUpgrade(obtainedId, key, beforeAfterRelic);
        }
        if (_preview.Partial != null && key.Entry is "WHETSTONE" or "WAR_PAINT")
            Working = _preview.Partial.ProjectUpgrade(beforeAfterRelic, Working, key);
        switch (key.Entry)
        {
            case "NEOWS_TALISMAN":
                Working = PredictorCardChanges.NeowsTalisman(Context, Working);
                break;
            case "CURSED_PEARL":
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "GREED"));
                Working = PredictorSettlementEffects.TakeGold(Working, 333);
                break;
            case "OLD_COIN":
                Working = PredictorSettlementEffects.TakeGold(Working, 300);
                break;
            case "NUTRITIOUS_OYSTER":
                Accept(new("NutritiousOysterGainMaxHp", Working.Relics[^1].Id, [], null, null)
                { EventHealthConsequences = [new(11, 11)] });
                break;
            case "BIG_MUSHROOM":
                Accept(new("BigMushroomGainMaxHp", obtainedId, [], null, null)
                { EventHealthConsequences = [new(20, 20)] });
                break;
            case "MANGO":
            case "PEAR":
            case "STRAWBERRY":
                int maxHpGain = key.Entry switch { "MANGO" => 14, "PEAR" => 10, _ => 7 };
                Accept(new(key.Entry + "GainMaxHp", obtainedId, [], null, null)
                { EventHealthConsequences = [new(maxHpGain, maxHpGain)] });
                break;
            case "LEES_WAFFLE":
                _wafflePending = new(destination, sourceId);
                Phase = PredictorPhase.ObtainHealth;
                _requestId++;
                return false;
            case "FAKE_LEES_WAFFLE":
                _fakeWafflePending = new(destination, sourceId);
                Phase = PredictorPhase.ObtainHealth;
                _requestId++;
                return false;
            case "FAKE_MANGO":
                Accept(new("FakeMangoGainMaxHp", obtainedId, [], null, null)
                { EventHealthConsequences = [new(3, 3)] });
                break;
            case "LOOMING_FRUIT":
                Accept(new("LoomingFruitGainMaxHp", obtainedId, [], null, null)
                { EventHealthConsequences = [new(31, 31)] });
                break;
            case "FRAGRANT_MUSHROOM":
                _fragrantPending = new(destination, sourceId);
                Phase = PredictorPhase.ObtainHealth;
                _requestId++;
                return false;
            case "SAND_CASTLE":
                var sand = PredictorObtainSources.FragrantMushroom(Context, Working, 6);
                Working = sand.State;
                Accept(new("SandCastleUpgrade", obtainedId, sand.CardIds, null, null));
                break;
            case "SERE_TALON":
                _serePending = new(destination, sourceId);
                Phase = PredictorPhase.ObtainHealth;
                _requestId++;
                return false;
            case "DOWSING_ROD":
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "DOWSING"));
                break;
            case "NEOWS_TORMENT":
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "NEOWS_FURY"));
                break;
            case "BLOOD_SOAKED_ROSE":
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "ENTHRALLED"));
                break;
            case "JEWELRY_BOX":
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "APOTHEOSIS"));
                break;
            case "STORYBOOK":
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "BRIGHTEST_FLAME"));
                break;
            case "TANXS_WHISTLE":
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "WHISTLE"));
                break;
            case "PAELS_HORN":
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "RELAX"));
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "RELAX"));
                break;
            case "SIGNET_RING":
                Working = PredictorSettlementEffects.TakeGold(Working, 888);
                break;
            case "PUMPKIN_CANDLE":
                var candleIndex = Working.Relics.FindIndex(r => r.Id == (existingInstanceId ?? Working.Relics[^1].Id));
                var candle = Working.Relics[candleIndex];
                Working = Working with { Relics = Working.Relics.SetItem(candleIndex,
                    candle with { KindleCount = candle.KindleCount + 5 }) };
                break;
            case "GOLDEN_COMPASS":
                ApplyGoldenCompassObtained(existingInstanceId ?? Working.Relics[^1].Id);
                break;
            case "FUR_COAT":
                ApplyFurCoatObtained(obtainedId);
                break;
            case "BYRDPIP":
                ulong rootSeed = RolltheSpire2.Compatibility.Beta111Profile.Instance.ComputeRootSeed(Context.Seed);
                ulong petHash = XxHash64.HashUtf8(key.Entry, 0UL);
                var petRng = new Xoshiro256StarStar(unchecked(rootSeed + petHash));
                int skin = petRng.NextInt(4) + 1;
                int petIndex = Working.Relics.FindIndex(r => r.Id == (existingInstanceId ?? Working.Relics[^1].Id));
                Working = Working with { Relics = Working.Relics.SetItem(petIndex,
                    Working.Relics[petIndex] with { VariantId = "version" + skin }) };
                var eggs = Working.Deck.Where(id => Working.Card(id).Key.Entry == "BYRDONIS_EGG").ToImmutableArray();
                var eggChanges = ImmutableArray.CreateBuilder<PredictorTransformation>();
                foreach (long egg in eggs)
                {
                    Working = PredictorCardChanges.TransformTo(Context, Working, egg, new ModelKey("CARD", "BYRD_SWOOP"));
                    eggChanges.Add(new(egg, Working.Deck[^1]));
                }
                Accept(new("ByrdpipHatch", Working.Relics[petIndex].Id,
                    eggChanges.Select(c => c.ReplacementId).ToImmutableArray(), null, null)
                { Transformations = eggChanges.ToImmutable() });
                break;
            case "PAELS_LEGION":
                ulong legionRoot = RolltheSpire2.Compatibility.Beta111Profile.Instance.ComputeRootSeed(Context.Seed);
                ulong legionHash = XxHash64.HashUtf8(key.Entry, 0UL);
                var legionRng = new Xoshiro256StarStar(unchecked(legionRoot + legionHash));
                string[] legionSkins = ["eyes", "horns", "spikes", "wings"];
                int legionIndex = Working.Relics.FindIndex(r => r.Id == obtainedId);
                Working = Working with { Relics = Working.Relics.SetItem(legionIndex,
                    Working.Relics[legionIndex] with { VariantId = legionSkins[legionRng.NextInt(legionSkins.Length)] }) };
                break;
            case "BEAUTIFUL_BRACELET":
                Working = PredictorObtainSources.BeautifulBracelet(Context, Working);
                break;
            case "PAELS_CLAW":
                Working = PredictorObtainSources.EnchantDeckSnapshot(Context, Working,
                    new ModelKey("ENCHANTMENT", "GOOPY"), 1,
                    c => PredictorCardChanges.Definition(Context, c.Key).IsDefend);
                break;
            case "NUTRITIOUS_SOUP":
                Working = PredictorObtainSources.EnchantDeckSnapshot(Context, Working,
                    new ModelKey("ENCHANTMENT", "TEZCATARAS_EMBER"), 1,
                    c => PredictorCardChanges.Definition(Context, c.Key) is { Rarity: "Basic", IsStrike: true });
                break;
            case "TRI_BOOMERANG":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Enchant, 3, destination, sourceId);
            case "ELECTRIC_SHRYMP":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Enchant, 1, destination, sourceId);
            case "PAELS_GROWTH":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Enchant, 1, destination, sourceId);
            case "GNARLED_HAMMER":
            case "KIFUDA":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Enchant, 3, destination, sourceId);
            case "PUNCH_DAGGER":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Enchant, 1, destination, sourceId);
            case "ROYAL_STAMP":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Enchant, 1, destination, sourceId);
            case "DOLLYS_MIRROR":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Clone, 1, destination, sourceId);
            case "PAELS_TOOTH":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.PaelsTooth, 5, destination, sourceId);
            case "ASTROLABE":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Astrolabe, 3, destination, sourceId);
            case "CLAWS":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Claws, 6, destination, sourceId);
            case "PANDORAS_BOX":
                var basicTargets = Working.Deck.Where(id => Working.Card(id).Removable &&
                    PredictorCardChanges.Definition(Context, Working.Card(id).Key) is { Rarity: "Basic" } d &&
                    (d.IsStrike || d.IsDefend)).ToImmutableArray();
                var pandora = PredictorCardChanges.Transform(Context, Working, basicTargets, PredictorStream.Niche, token);
                Working = pandora.State;
                Accept(new("PandorasBoxTransform", Working.Relics[^1].Id,
                    pandora.Changes.Select(c => c.ReplacementId).ToImmutableArray(), null, null)
                { Transformations = pandora.Changes });
                break;
            case "DISTINGUISHED_CAPE":
                var availableCurses = (Context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing"))
                    .UnlockedCurseCards.ToList();
                if (availableCurses.Count < 2) throw new InvalidOperationException("PredictorCapeCursePoolDepleted");
                var capeCardIds = ImmutableArray.CreateBuilder<long>();
                for (int i = 0; i < 2; i++)
                {
                    var draw = Working.Draw(PredictorStream.Niche, 0, availableCurses.Count);
                    Working = draw.State;
                    var curse = availableCurses[draw.Value];
                    availableCurses.RemoveAt(draw.Value);
                    capeCardIds.Add(Working.NextInstanceId);
                    Working = PredictorObtainSources.AddCard(Context, Working, curse);
                }
                for (int i = 0; i < 3; i++)
                {
                    capeCardIds.Add(Working.NextInstanceId);
                    Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "APPARITION"));
                }
                Accept(new("DistinguishedCapeCards", obtainedId, capeCardIds.ToImmutable(), null, null));
                break;
            case "NEOWS_SACRIFICE":
                int freeSlot = Working.PotionSlots.FindIndex(p => p == null);
                long potionId = Working.NextInstanceId;
                Working = Working with { NextInstanceId = potionId + 1 };
                if (freeSlot >= 0 && !PredictorSettlementEffects.Has(Working, "SOZU"))
                    Working = Working with { PotionSlots = Working.PotionSlots.SetItem(freeSlot,
                        new(potionId, new("POTION", "AMBERGRIS"))) };
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "GUILTY"));
                break;
            case "DUSTY_TOME":
                var chosenAncient = Working.Relics[^1].PreparedCard
                    ?? throw new InvalidOperationException("PredictorDustyTomeSetupMissing");
                var ancientCard = PredictorCardChanges.Definition(Context, chosenAncient).Prototype;
                if (ancientCard.MaxUpgradeLevel < 1) throw new InvalidOperationException("PredictorDustyTomeCardCannotUpgrade");
                Working = PredictorObtainSources.AddCard(Context, Working, ancientCard with { UpgradeLevel = 1 });
                Accept(new("DustyTomeCard", Working.Relics[^1].Id, [Working.Deck[^1]], null, null));
                break;
            case "ARCHAIC_TOOTH":
                var transcended = PredictorObtainSources.ArchaicTooth(Context, Working);
                Working = transcended.State;
                Accept(new("ArchaicToothTransform", Working.Relics[^1].Id, [transcended.Change.ReplacementId], null, null)
                { Transformations = [transcended.Change] });
                break;
            case "TOUCH_OF_OROBAS":
                var target = PredictorObtainSources.TouchTarget(Working);
                var originalIndex = Working.Relics.FindIndex(r => r.Id == target.Id);
                Working = Working with { Relics = Working.Relics.RemoveAt(originalIndex) };
                Accept(new("TouchOfOrobasRemoveStarter", target.Id, [], null, null));
                _touchPending = new(destination, sourceId);
                if (!BeginObtainRelic(target.Upgrade, null, PredictorObtainReturn.InternalTouch, sourceId,
                    token, insertIndex: originalIndex)) return false;
                _touchPending = null;
                break;
            case "SEA_GLASS":
                var targetCharacter = Working.Relics[^1].CharacterTarget
                    ?? throw new InvalidOperationException("PredictorSeaGlassCharacterSetupMissing");
                var glass = PredictorObtainSources.SeaGlass(Context, Working, targetCharacter);
                Working = glass.State;
                _obtainManyChoices = glass.Cards;
                _obtainManyPending = new(destination, sourceId);
                Phase = PredictorPhase.ObtainChooseManyCards;
                _requestId++;
                return false;
            case "ALCHEMICAL_COFFER":
                var coffer = PredictorPotionEffects.Coffer(Context, Working, existingInstanceId ?? Working.Relics[^1].Id);
                Working = coffer.State;
                Accept(new("AlchemicalCoffer", Working.Relics[^1].Id, [], null, null) { PotionAttempts = coffer.Attempts });
                break;
            case "NEW_LEAF":
                _newLeafObtain = new(destination, sourceId);
                BeginNewLeaf(destination == PredictorObtainReturn.Reward ? sourceId : null, token);
                return false;
            case "ARCANE_SCROLL":
                state = Working;
                var arcane = PredictorRewardGeneration.GenerateCards(Context, ref state,
                    new(PredictorRewardKind.Card, CardCount: 1, Odds: PredictorCardOdds.Uniform,
                        Rarity: EffectCardRarity.Rare, NoUpgradeRoll: true), isCardReward: false);
                Working = PredictorObtainSources.AddCard(Context, state, arcane[0]);
                break;
            case "PHIAL_HOLSTER":
                Working = Working with { PotionSlots = Working.PotionSlots.Add(null) };
                state = Working;
                var generated = PredictorRewardGeneration.GeneratePotions(Context, ref state, 2, PredictorStream.CombatPotionGeneration);
                Working = state;
                var holsterGenerated = ImmutableArray.CreateBuilder<PredictorPotion>();
                foreach (var potion in generated)
                {
                    int slot = Working.PotionSlots.FindIndex(p => p == null);
                    long id = Working.NextInstanceId;
                    Working = Working with { NextInstanceId = id + 1 };
                    holsterGenerated.Add(new(id, potion));
                    if (slot >= 0 && !PredictorSettlementEffects.Has(Working, "SOZU"))
                        Working = Working with { PotionSlots = Working.PotionSlots.SetItem(slot, new(id, potion)) };
                }
                if (Context.Operation != null || _ancientOutcomeBefore?.Position.NodeId == Working.Position.NodeId)
                    Accept(new("PhialHolster", Working.Relics[^1].Id, [], null, null)
                    { GeneratedPotions = holsterGenerated.ToImmutable() });
                break;
            case "LOST_COFFER":
                PushChild(key, destination, sourceId, false,
                    PredictorRewardGeneration.Generate(Context, Working,
                        [new(PredictorRewardKind.Card), new(PredictorRewardKind.Potion)], cancellationToken: token));
                return false;
            case "CAULDRON":
                PushChild(key, destination, sourceId, false,
                    PredictorRewardGeneration.Generate(Context, Working,
                        Enumerable.Repeat(new PredictorRewardPlan(PredictorRewardKind.Potion), 5), cancellationToken: token));
                return false;
            case "SMALL_CAPSULE":
                PushChild(key, destination, sourceId, false,
                    PredictorRewardGeneration.Generate(Context, Working, [new(PredictorRewardKind.Relic)], cancellationToken: token));
                return false;
            case "NEOWS_BONES":
                PushChild(key, destination, sourceId, true, PredictorObtainSources.Bones(Context, Working, token));
                return false;
            case "LARGE_CAPSULE":
                return BeginLargeCapsule(destination, sourceId, token);
            case "CALLING_BELL":
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "CURSE_OF_THE_BELL"));
                PushChild(key, destination, sourceId, false, PredictorObtainSources.CallingBell(Context, Working, token));
                return false;
            case "KALEIDOSCOPE":
                PushChild(key, destination, sourceId, false, PredictorObtainSources.Kaleidoscope(Context, Working, token));
                return false;
            case "TOY_BOX":
                var wax = PredictorRewardGeneration.Generate(Context, Working,
                    Enumerable.Repeat(new PredictorRewardPlan(PredictorRewardKind.Relic), 5), cancellationToken: token);
                PushChild(key, destination, sourceId, false,
                    wax with { Rewards = wax.Rewards.Select(r => r with { IsWaxRelic = true }).ToImmutableArray() });
                return false;
            case "ORRERY":
                PushChild(key, destination, sourceId, false,
                    PredictorRewardGeneration.Generate(Context, Working,
                        Enumerable.Repeat(new PredictorRewardPlan(PredictorRewardKind.Card), 5), cancellationToken: token));
                return false;
            case "GLASS_EYE":
                var rarities = new[] { EffectCardRarity.Common, EffectCardRarity.Common,
                    EffectCardRarity.Uncommon, EffectCardRarity.Uncommon, EffectCardRarity.Rare };
                PushChild(key, destination, sourceId, false,
                    PredictorRewardGeneration.Generate(Context, Working,
                        rarities.Select(r => new PredictorRewardPlan(PredictorRewardKind.Card,
                            Odds: PredictorCardOdds.Uniform, Rarity: r, NoUpgradeRoll: true)), cancellationToken: token));
                return false;
            case "LEAD_PAPERWEIGHT":
            case "HEFTY_TABLET":
            case "MASSIVE_SCROLL":
                var choice = PredictorObtainSources.GenerateObtainCardChoices(Context, Working, key.Entry);
                Working = choice.State;
                _obtainCardChoices = choice.Cards;
                _obtainCardSource = key;
                _obtainCardPending = new(destination, sourceId);
                Phase = PredictorPhase.ObtainChooseCard;
                _requestId++;
                return false;
            case "PRECISE_SCISSORS":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Remove, 1, destination, sourceId);
            case "BIIIG_HUG":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Remove, 4, destination, sourceId);
            case "PRECARIOUS_SHEARS":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Remove, 2, destination, sourceId);
            case "EMPTY_CAGE":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Remove, 2, destination, sourceId);
            case "PRESERVED_FOG":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Remove, 3, destination, sourceId);
            case "POMANDER":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Upgrade, 1, destination, sourceId);
            case "YUMMY_COOKIE":
                return BeginObtainDeckChoice(key, PredictorObtainDeckOperation.Upgrade, 4, destination, sourceId);
            case "LEAFY_POULTICE":
                _leafyPending = new(destination, sourceId);
                Phase = PredictorPhase.ObtainHealth;
                _requestId++;
                return false;
            case "SCROLL_BOXES":
                var bundles = PredictorObtainSources.ScrollBoxes(Context, Working);
                Working = bundles.State;
                _scrollBoxBundles = bundles.Bundles;
                _newLeafObtain = new(destination, sourceId);
                if (destination is PredictorObtainReturn.Opening or PredictorObtainReturn.Ancient)
                    RecordAncientBundleObservation();
                Phase = PredictorPhase.ChooseBundle;
                _requestId++;
                return false;
        }
        Working.Validate();
        return true;
    }

    private void NotifyOpenCardRewards(ModelKey key, long relicId)
    {
        var subscriptions = new List<(long Order, int Natural, int Location, int Frame, int Index)>();
        int natural = 0;
        for (int index = 0; index < Working.DetachedCardRewards.Length; index++)
            subscriptions.Add((Working.DetachedCardRewards[index].SubscriptionOrder, natural++, 0, -1, index));
        for (int frameIndex = 0; frameIndex < _obtainFrames.Length; frameIndex++)
            for (int index = 0; index < _obtainFrames[frameIndex].ParentRewards.Length; index++)
                subscriptions.Add((_obtainFrames[frameIndex].ParentRewards[index].SubscriptionOrder,
                    natural++, 1, frameIndex, index));
        for (int index = 0; index < Rewards.Length; index++)
            subscriptions.Add((Rewards[index].SubscriptionOrder, natural++, 2, -1, index));
        // Positive orders are actual CardReward constructor ordinals. Zero is
        // reserved for older controlled offers and retains their natural order.
        foreach (var subscription in subscriptions.OrderBy(s => s.Order).ThenBy(s => s.Natural))
        {
            if (subscription.Location == 0)
            {
                var rewards = Working.DetachedCardRewards;
                var updated = NotifyOpenCardRewards([rewards[subscription.Index]], key, relicId)[0];
                Working = Working with { DetachedCardRewards = rewards.SetItem(subscription.Index, updated) };
            }
            else if (subscription.Location == 1)
            {
                var frame = _obtainFrames[subscription.Frame];
                var updated = NotifyOpenCardRewards([frame.ParentRewards[subscription.Index]], key, relicId)[0];
                _obtainFrames = _obtainFrames.SetItem(subscription.Frame, frame with
                { ParentRewards = frame.ParentRewards.SetItem(subscription.Index, updated) });
            }
            else Rewards = Rewards.SetItem(subscription.Index,
                NotifyOpenCardRewards([Rewards[subscription.Index]], key, relicId)[0]);
        }
    }

    private ImmutableArray<PredictorReward> NotifyOpenCardRewards(ImmutableArray<PredictorReward> rewards, ModelKey key, long relicId)
    {
        for (int i = 0; i < rewards.Length; i++)
        {
            var reward = rewards[i];
            if (reward.Kind != PredictorRewardKind.Card || reward.Status != PredictorRewardStatus.Open || !reward.RelicSubscriptionActive) continue;
            int modifierIndex = Working.Relics.FindIndex(r => r.Id == relicId && r.Key == key &&
                (key.Entry == "SILVER_CRUCIBLE" && r.RewardUpgradeUses < 3 ||
                 key.Entry == "SILKEN_TRESS" && !r.SilkenUsed));
            bool silverAvailable = key.Entry == "SILVER_CRUCIBLE" && modifierIndex >= 0;
            bool silkenAvailable = key.Entry == "SILKEN_TRESS" && modifierIndex >= 0;
            var changed = ImmutableArray.CreateBuilder<PredictorCard>();
            foreach (var candidate in reward.Cards)
            {
                var next = PredictorRewardGeneration.UpgradeWithEgg(candidate, key.Entry);
                if (silverAvailable && candidate.UpgradeLevel < candidate.MaxUpgradeLevel)
                    next = candidate with { UpgradeLevel = candidate.UpgradeLevel + 1 };
                if (silkenAvailable && PredictorObtainSources.CanEnchant(Context, candidate))
                    next = candidate with { Enchantment = new("ENCHANTMENT", "GLAM"), EnchantmentAmount = 1 };
                next = PredictorCardLevelEffects.Reconcile(Context, next);
                if (next != candidate)
                {
                    next = next with { Id = Working.NextInstanceId };
                    Working = Working with { NextInstanceId = Working.NextInstanceId + 1 };
                }
                changed.Add(next);
            }
            var wingCharmEligibility = new List<ImmutableArray<int>>();
            var late = PredictorCardRewardRelicHooks.ApplyLate(Context, Working, changed.ToList(), key,
                cloneOnChange: true, relicId: relicId, wingCharmEligibility: wingCharmEligibility);
            Working = late.State;
            rewards = rewards.SetItem(i, reward with { Cards = late.Cards.ToImmutableArray(),
                CardRewardNicheScope = PredictorCardRewardNicheScope.From(wingCharmEligibility, reward.CardRewardNicheScope) });
        }
        return rewards;
    }

    private void PushChild(ModelKey relic, PredictorObtainReturn destination, long sourceId,
        bool mandatory, PredictorGeneratedRewards generated)
    {
        _obtainFrames = _obtainFrames.Add(new(relic, destination, sourceId, Rewards, Phase, 0, mandatory));
        Working = generated.State;
        PublishChildRewards(generated.Rewards, mandatory ? PredictorRewardOrigin.BonesInternal :
            relic.Entry == "SMALL_CAPSULE" ? PredictorRewardOrigin.SmallCapsule : PredictorRewardOrigin.Event);
        GenerationStages = generated.Stages;
    }

    internal bool BeginRestHealRewards(IEnumerable<PredictorRewardPlan> plans, long sourceId,
        CancellationToken token = default)
    {
        var array = plans.ToArray();
        if (array.Length == 0) return true;
        if (Phase != PredictorPhase.RestSite) throw new InvalidOperationException("PredictorRestHealRewardPhaseInvalid");
        PushChild(new("RELIC", "REST_HEAL_REWARDS"), PredictorObtainReturn.RestHeal,
            sourceId, false, PredictorRewardGeneration.Generate(Context, Working, array, cancellationToken: token));
        return false;
    }

    internal bool BeginMimicRestHealRewards(IEnumerable<PredictorRewardPlan> plans, long sourceId,
        CancellationToken token = default)
    {
        var array = plans.ToArray();
        if (array.Length == 0) return true;
        if (Phase != PredictorPhase.EventChoice) throw new InvalidOperationException("PredictorMimicHealRewardPhaseInvalid");
        PushChild(new("RELIC", "MIMIC_REST_HEAL_REWARDS"), PredictorObtainReturn.EventRestHeal,
            sourceId, false, PredictorRewardGeneration.Generate(Context, Working, array, cancellationToken: token));
        return false;
    }

    private void PublishChildRewards(ImmutableArray<PredictorReward> offers, PredictorRewardOrigin origin)
    {
        var children = ImmutableArray.CreateBuilder<PredictorReward>();
        foreach (var offer in offers)
        {
            long rewardId = Working.NextInstanceId;
            Working = Working with { NextInstanceId = rewardId + 1 };
            long? instanceId = null;
            if (offer.Kind is PredictorRewardKind.Relic or PredictorRewardKind.Potion)
            {
                instanceId = Working.NextInstanceId;
                Working = Working with { NextInstanceId = instanceId.Value + 1 };
            }
            var cards = ImmutableArray.CreateBuilder<PredictorCard>();
            foreach (var card in offer.Cards)
            {
                cards.Add(card with { Id = Working.NextInstanceId });
                Working = Working with { NextInstanceId = Working.NextInstanceId + 1 };
            }
            children.Add(offer with { Id = rewardId, Origin = origin, Cards = cards.ToImmutable(), ContentInstanceId = instanceId,
                RelicSubscriptionActive = offer.Kind == PredictorRewardKind.Card && !offer.ManuallySetCards });
        }
        Rewards = children.ToImmutable();
        Phase = PredictorPhase.Rewards;
        _requestId++;
    }

    private bool BeginLargeCapsule(PredictorObtainReturn destination, long sourceId, CancellationToken token)
    {
        _obtainFrames = _obtainFrames.Add(new(new("RELIC", "LARGE_CAPSULE"), destination, sourceId, Rewards, Phase, 0, true));
        bool completed = ContinueLargeCapsule(token);
        if (completed && _completedObtains.Length > 0 &&
            _completedObtains[^1] == new PredictorObtainCompletion(destination, sourceId))
            _completedObtains = _completedObtains.RemoveAt(_completedObtains.Length - 1);
        return completed;
    }

    private bool ContinueLargeCapsule(CancellationToken token)
    {
        while (_obtainFrames.Length > 0 && _obtainFrames[^1].Relic.Entry == "LARGE_CAPSULE")
        {
            var frame = _obtainFrames[^1];
            if (frame.Stage == 2)
            {
                Working = PredictorObtainSources.AddBasicStrikeDefend(Context, Working);
                RestoreParentFrame();
                return true;
            }
            var state = Working;
            var key = PredictorRewardGeneration.PullRelic(Context, ref state);
            Working = state;
            _obtainFrames = _obtainFrames.SetItem(_obtainFrames.Length - 1, frame with { Stage = frame.Stage + 1 });
            if (!BeginObtainRelic(key, null, PredictorObtainReturn.InternalLargeCapsule, frame.SourceId, token)) return false;
        }
        return true;
    }

    // Called only after the child reward action has become Taken. Mandatory
    // Bones returns after both children, then its final curse is generated.
    internal bool ContinueObtainChildIfComplete(CancellationToken token = default)
    {
        if (_obtainFrames.Length == 0 || Rewards.Any(r => r.Status == PredictorRewardStatus.Open)) return false;
        CompleteChildFrame(token);
        return true;
    }

    internal PredictorInputResult? TryLeaveObtainChild(CancellationToken token = default)
    {
        if (_obtainFrames.Length == 0 || Phase != PredictorPhase.Rewards) return null;
        if (_cardSelection != null) return PredictorInputResult.Invalid;
        if (_obtainFrames[^1].Mandatory && Rewards.Any(r => r.Status == PredictorRewardStatus.Open)) return PredictorInputResult.Invalid;
        Rewards = Rewards.Select(r => r.Status == PredictorRewardStatus.Open ? r with
        { Status = PredictorRewardStatus.Skipped, RelicSubscriptionActive = false } : r).ToImmutableArray();
        Accept(new("LeaveObtainChild", 0, [], null, null));
        CompleteChildFrame(token);
        return PredictorInputResult.Accepted;
    }

    private void CompleteChildFrame(CancellationToken token)
    {
        if (_obtainFrames.Length == 0) return;
        var frame = _obtainFrames[^1];
        if (frame.Relic.Entry == "NEOWS_BONES") Working = PredictorObtainSources.BonesCurse(Context, Working);
        RestoreParentFrame();
        if (_obtainFrames.Length > 0 && _obtainFrames[^1].Relic.Entry == "LARGE_CAPSULE") ContinueLargeCapsule(token);
    }

    private void RestoreParentFrame()
    {
        var frame = _obtainFrames[^1];
        CompletedChildRewards = CompletedChildRewards.AddRange(Rewards);
        _obtainFrames = _obtainFrames.RemoveAt(_obtainFrames.Length - 1);
        Rewards = frame.ParentRewards;
        Phase = frame.ParentPhase;
        _requestId++;
        if (frame.Destination == PredictorObtainReturn.InternalTouch)
            CompleteObtainDestination(new(frame.Destination, frame.SourceId));
        else if (frame.Destination != PredictorObtainReturn.InternalLargeCapsule)
            _completedObtains = _completedObtains.Add(new(frame.Destination, frame.SourceId));
    }

    private void CompleteObtainDestination(PredictorObtainCompletion completion)
    {
        if (completion.Destination == PredictorObtainReturn.InternalTouch)
        {
            var parent = _touchPending ?? throw new InvalidOperationException("PredictorTouchParentMissing");
            _touchPending = null;
            CompleteObtainDestination(parent);
        }
        else if (completion.Destination == PredictorObtainReturn.Reward)
            MarkTaken(completion.SourceId);
        else
            _completedObtains = _completedObtains.Add(completion);
    }

    // A death or abandon ends the current transaction without running pending
    // AfterObtained continuations. Retain every suspended parent's offers in
    // the terminal node record, including rewards not yet taken.
    internal void AbortObtainFrames()
    {
        foreach (var frame in _obtainFrames)
            CompletedChildRewards = CompletedChildRewards.AddRange(frame.ParentRewards.Select(r =>
                r.Status == PredictorRewardStatus.Open ? r with
                { Status = PredictorRewardStatus.Aborted, RelicSubscriptionActive = false } : r));
        _obtainFrames = [];
        _completedObtains = [];
    }

    internal void ResetObtain()
    {
        _obtainFrames = [];
        _completedObtains = [];
        _newLeafObtain = null;
        _touchPending = null;
        _scrollBoxBundles = [];
        _leafyPending = null;
        _fragrantPending = null;
        _wafflePending = null;
        _fakeWafflePending = null;
        _serePending = null;
        _precariousPending = null;
        _obtainCardPending = null;
        _obtainCardSource = default;
        _obtainCardChoices = [];
        _obtainDeckPending = null;
        _obtainDeckCount = 0;
        _obtainDeckRelicId = 0;
        _obtainDeckCandidateOrder = [];
        _obtainDeckSource = default;
        _obtainManyPending = null;
        _obtainManyChoices = [];
        CompletedChildRewards = [];
    }

    internal void ClearCompletedChildRewards() => CompletedChildRewards = [];


    // FinishNewLeaf calls this after its selected transform and Reward mark.
    // Opening and Reward retain the existing local completion path.
    internal void TryResumeNewLeafObtain()
    {
        if (_newLeafObtain is not { } completion) return;
        _newLeafObtain = null;
        if (completion.Destination == PredictorObtainReturn.InternalTouch)
            CompleteObtainDestination(completion);
        else if (completion.Destination is not (PredictorObtainReturn.Opening or PredictorObtainReturn.Reward))
            _completedObtains = _completedObtains.Add(completion);
    }

    internal PredictorInputResult SubmitScrollBoxBundle(ChooseScrollBoxBundle input)
    {
        if (Phase != PredictorPhase.ChooseBundle || input.BundleIndex is < 0 or > 1 || _scrollBoxBundles.Length != 2)
            return PredictorInputResult.Invalid;
        var ids = ImmutableArray.CreateBuilder<long>();
        foreach (var candidate in _scrollBoxBundles[input.BundleIndex])
        {
            Working = PredictorObtainSources.AddExistingCard(Context, Working, candidate);
            ids.Add(Working.Deck[^1]);
        }
        _scrollBoxBundles = [];
        Phase = PredictorPhase.Rewards;
        Accept(new("ChooseScrollBoxBundle", input.BundleIndex, ids.ToImmutable(), null, null));
        var completion = _newLeafObtain;
        _newLeafObtain = null;
        if (completion != null) CompleteObtainDestination(completion);
        ContinueObtainChildIfComplete();
        return PredictorInputResult.Accepted;
    }

    internal PredictorInputResult SubmitLeafyHealth(ProvideEventHealthFact input, CancellationToken token)
    {
        if (_wafflePending != null) return SubmitWaffleHealth(input);
        if (_fakeWafflePending != null) return SubmitFakeWaffleHealth(input);
        if (_fragrantPending != null) return SubmitFragrantHealth(input);
        if (_serePending != null) return SubmitSereHealth(input);
        if (_precariousPending != null) return SubmitPrecariousHealth(input);
        if (Phase != PredictorPhase.ObtainHealth || _leafyPending is not { } pending || input.Fact is not { } fact ||
            fact.MaxHp < 1 || fact.CurrentHp < 1 || fact.CurrentHp > fact.MaxHp) return PredictorInputResult.Invalid;
        int rawMax = fact.MaxHp - 12;
        int newMax = Math.Max(1, rawMax);
        int rawDamage = Math.Max(0, fact.CurrentHp - rawMax);
        int damage = PredictorHealthEffects.ModifyOutOfCombatHpLoss(Working, rawDamage);
        int afterDamage = fact.CurrentHp - damage;
        int newCurrent = Math.Min(afterDamage, newMax);
        long consumedId = 0;
        if (afterDamage <= 0)
        {
            // LoseMaxHp damages against the unclamped raw max. Fairy is the
            // normal-pass preventer; Tail is only consulted in the late pass.
            int fairySlot = Working.PotionSlots.FindIndex(p => p?.Key.Entry == "FAIRY_IN_A_BOTTLE");
            if (fairySlot >= 0)
            {
                consumedId = Working.PotionSlots[fairySlot]!.Id;
                Working = Working with { PotionSlots = Working.PotionSlots.SetItem(fairySlot, null) };
            }
            else
            {
                int tailIndex = Working.Relics.FindIndex(r => !r.Melted && r.Key.Entry == "LIZARD_TAIL" && !r.LizardTailUsed);
                if (tailIndex < 0)
                {
                    Accept(new("LeafyPoulticeLethalDamage", pending.SourceId, [], null, null)
                    { EventHealthConsequences = [new(-fact.CurrentHp, 0)] });
                    _leafyPending = null;
                    CompleteRunDeath("LeafyPoultice");
                    return PredictorInputResult.Accepted;
                }
                var tail = Working.Relics[tailIndex];
                consumedId = tail.Id;
                Working = Working with { Relics = Working.Relics.SetItem(tailIndex, tail with { LizardTailUsed = true }) };
            }
            newCurrent = 1; // Preventer heals before SetMaxHp clamps to 1.
        }
        Accept(new("LeafyPoulticeLoseMaxHp", pending.SourceId, [], null, null)
        { EventHealthConsequences = [new(newCurrent - fact.CurrentHp, newMax - fact.MaxHp)] });
        if (consumedId != 0) Accept(new("LeafyPoulticeDeathPrevented", consumedId, [], null, null));
        var changed = PredictorObtainSources.Leafy(Context, Working, token);
        Working = changed.State;
        Accept(new("LeafyPoulticeTransform", pending.SourceId,
            changed.Changes.Select(c => c.ReplacementId).ToImmutableArray(), null, null)
        { Transformations = changed.Changes });
        _leafyPending = null;
        Phase = PredictorPhase.Rewards;
        CompleteObtainDestination(pending);
        ContinueObtainChildIfComplete(token);
        return PredictorInputResult.Accepted;
    }

    private PredictorInputResult SubmitWaffleHealth(ProvideEventHealthFact input)
    {
        if (Phase != PredictorPhase.ObtainHealth || _wafflePending is not { } pending || input.Fact is not { } fact ||
            fact.MaxHp < 1 || fact.CurrentHp < 1 || fact.CurrentHp > fact.MaxHp) return PredictorInputResult.Invalid;
        // GainMaxHp heals by the max-HP increase, then Waffle heals the rest.
        Accept(new("LeesWaffleFullHeal", pending.SourceId, [], null, null)
        { EventHealthConsequences = [new(fact.MaxHp + 7 - fact.CurrentHp, 7)] });
        _wafflePending = null;
        Phase = PredictorPhase.Rewards;
        CompleteObtainDestination(pending);
        ContinueObtainChildIfComplete();
        return PredictorInputResult.Accepted;
    }

    private PredictorInputResult SubmitFakeWaffleHealth(ProvideEventHealthFact input)
    {
        if (Phase != PredictorPhase.ObtainHealth || _fakeWafflePending is not { } pending || input.Fact is not { } fact ||
            fact.MaxHp < 1 || fact.CurrentHp < 1 || fact.CurrentHp > fact.MaxHp) return PredictorInputResult.Invalid;
        int requested = (int)(fact.MaxHp * .1m);
        int healed = Math.Min(requested, fact.MaxHp - fact.CurrentHp);
        Accept(new("FakeLeesWaffleHeal", pending.SourceId, [], null, null)
        { EventHealthConsequences = [new(healed, 0)] });
        _fakeWafflePending = null;
        Phase = PredictorPhase.Rewards;
        CompleteObtainDestination(pending);
        ContinueObtainChildIfComplete();
        return PredictorInputResult.Accepted;
    }

    private PredictorInputResult SubmitFragrantHealth(ProvideEventHealthFact input)
    {
        if (Phase != PredictorPhase.ObtainHealth || _fragrantPending is not { } pending || input.Fact is not { } fact ||
            fact.MaxHp < 1 || fact.CurrentHp < 1 || fact.CurrentHp > fact.MaxHp) return PredictorInputResult.Invalid;
        int afterHp = fact.CurrentHp - PredictorHealthEffects.ModifyOutOfCombatHpLoss(Working, 15);
        long consumedId = 0;
        if (afterHp <= 0)
        {
            int fairySlot = Working.PotionSlots.FindIndex(p => p?.Key.Entry == "FAIRY_IN_A_BOTTLE");
            if (fairySlot >= 0)
            {
                consumedId = Working.PotionSlots[fairySlot]!.Id;
                Working = Working with { PotionSlots = Working.PotionSlots.SetItem(fairySlot, null) };
                afterHp = Math.Max(1, (int)(fact.MaxHp * .3m));
            }
            else
            {
                int tailIndex = Working.Relics.FindIndex(r => !r.Melted && r.Key.Entry == "LIZARD_TAIL" && !r.LizardTailUsed);
                if (tailIndex < 0)
                {
                    Accept(new("FragrantMushroomLethalDamage", pending.SourceId, [], null, null)
                    { EventHealthConsequences = [new(-fact.CurrentHp, 0)] });
                    _fragrantPending = null;
                    CompleteRunDeath("FragrantMushroom");
                    return PredictorInputResult.Accepted;
                }
                var tail = Working.Relics[tailIndex];
                consumedId = tail.Id;
                Working = Working with { Relics = Working.Relics.SetItem(tailIndex, tail with { LizardTailUsed = true }) };
                afterHp = Math.Max(1, fact.MaxHp / 2);
            }
        }
        Accept(new("FragrantMushroomDamage", pending.SourceId, [], null, null)
        { EventHealthConsequences = [new(afterHp - fact.CurrentHp, 0)] });
        if (consumedId != 0) Accept(new("FragrantMushroomDeathPrevented", consumedId, [], null, null));
        var upgraded = PredictorObtainSources.FragrantMushroom(Context, Working);
        Working = upgraded.State;
        Accept(new("FragrantMushroomUpgrade", pending.SourceId, upgraded.CardIds, null, null));
        _fragrantPending = null;
        Phase = PredictorPhase.Rewards;
        CompleteObtainDestination(pending);
        ContinueObtainChildIfComplete();
        return PredictorInputResult.Accepted;
    }

    private PredictorInputResult SubmitSereHealth(ProvideEventHealthFact input)
    {
        if (Phase != PredictorPhase.ObtainHealth || _serePending is not { } pending || input.Fact is not { } fact ||
            fact.MaxHp < 1 || fact.CurrentHp < 1 || fact.CurrentHp > fact.MaxHp) return PredictorInputResult.Invalid;
        int rawMax = fact.MaxHp - 9;
        int maxHp = Math.Max(1, rawMax);
        int rawDamage = Math.Max(0, fact.CurrentHp - rawMax);
        int damage = PredictorHealthEffects.ModifyOutOfCombatHpLoss(Working, rawDamage);
        int afterDamage = fact.CurrentHp - damage;
        int currentHp = Math.Min(afterDamage, maxHp);
        long consumedId = 0;
        if (afterDamage <= 0)
        {
            int fairySlot = Working.PotionSlots.FindIndex(p => p?.Key.Entry == "FAIRY_IN_A_BOTTLE");
            if (fairySlot >= 0)
            {
                consumedId = Working.PotionSlots[fairySlot]!.Id;
                Working = Working with { PotionSlots = Working.PotionSlots.SetItem(fairySlot, null) };
            }
            else
            {
                int tailIndex = Working.Relics.FindIndex(r => !r.Melted && r.Key.Entry == "LIZARD_TAIL" && !r.LizardTailUsed);
                if (tailIndex < 0)
                {
                    Accept(new("SereTalonLethalMaxHpLoss", pending.SourceId, [], null, null)
                    { EventHealthConsequences = [new(-fact.CurrentHp, 0)] });
                    _serePending = null;
                    CompleteRunDeath("SereTalon");
                    return PredictorInputResult.Accepted;
                }
                var tail = Working.Relics[tailIndex];
                consumedId = tail.Id;
                Working = Working with { Relics = Working.Relics.SetItem(tailIndex, tail with { LizardTailUsed = true }) };
            }
            currentHp = 1;
        }
        Accept(new("SereTalonLoseMaxHp", pending.SourceId, [], null, null)
        { EventHealthConsequences = [new(currentHp - fact.CurrentHp, maxHp - fact.MaxHp)] });
        if (consumedId != 0) Accept(new("SereTalonDeathPrevented", consumedId, [], null, null));
        var wishes = ImmutableArray.CreateBuilder<long>();
        for (int i = 0; i < 3; i++)
        {
            wishes.Add(Working.NextInstanceId);
            Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "WISH"));
        }
        Accept(new("SereTalonWishes", pending.SourceId, wishes.ToImmutable(), null, null));
        _serePending = null;
        Phase = PredictorPhase.Rewards;
        CompleteObtainDestination(pending);
        ContinueObtainChildIfComplete();
        return PredictorInputResult.Accepted;
    }

    private PredictorInputResult SubmitPrecariousHealth(ProvideEventHealthFact input)
    {
        if (Phase != PredictorPhase.ObtainHealth || _precariousPending is not { } pending || input.Fact is not { } fact ||
            fact.MaxHp < 1 || fact.CurrentHp < 1 || fact.CurrentHp > fact.MaxHp) return PredictorInputResult.Invalid;
        int afterHp = fact.CurrentHp - PredictorHealthEffects.ModifyOutOfCombatHpLoss(Working, 16);
        long consumedId = 0;
        if (afterHp <= 0)
        {
            int fairySlot = Working.PotionSlots.FindIndex(p => p?.Key.Entry == "FAIRY_IN_A_BOTTLE");
            if (fairySlot >= 0)
            {
                consumedId = Working.PotionSlots[fairySlot]!.Id;
                Working = Working with { PotionSlots = Working.PotionSlots.SetItem(fairySlot, null) };
                afterHp = Math.Max(1, (int)(fact.MaxHp * .3m));
            }
            else
            {
                int tailIndex = Working.Relics.FindIndex(r => !r.Melted && r.Key.Entry == "LIZARD_TAIL" && !r.LizardTailUsed);
                if (tailIndex < 0)
                {
                    Accept(new("PrecariousShearsLethalDamage", pending.SourceId, [], null, null)
                    { EventHealthConsequences = [new(-fact.CurrentHp, 0)] });
                    _precariousPending = null;
                    CompleteRunDeath("PrecariousShears");
                    return PredictorInputResult.Accepted;
                }
                var tail = Working.Relics[tailIndex];
                consumedId = tail.Id;
                Working = Working with { Relics = Working.Relics.SetItem(tailIndex, tail with { LizardTailUsed = true }) };
                afterHp = Math.Max(1, fact.MaxHp / 2);
            }
        }
        Accept(new("PrecariousShearsDamage", pending.SourceId, [], null, null)
        { EventHealthConsequences = [new(afterHp - fact.CurrentHp, 0)] });
        if (consumedId != 0) Accept(new("PrecariousShearsDeathPrevented", consumedId, [], null, null));
        _precariousPending = null;
        Phase = PredictorPhase.Rewards;
        CompleteObtainDestination(pending);
        ContinueObtainChildIfComplete();
        return PredictorInputResult.Accepted;
    }

    internal PredictorInputResult SubmitObtainCard(ChooseObtainCard input)
    {
        if (Phase != PredictorPhase.ObtainChooseCard || _obtainCardPending is not { } pending ||
            input.CardId is long id && !_obtainCardChoices.Any(c => c.Id == id)) return PredictorInputResult.Invalid;
        if (input.CardId is long selectedId)
            Working = PredictorObtainSources.AddExistingCard(Context, Working, _obtainCardChoices.Single(c => c.Id == selectedId));
        if (_obtainCardSource.Entry == "HEFTY_TABLET")
            Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "INJURY"));
        Accept(new("ChooseObtainCard", pending.SourceId,
            input.CardId is long picked ? [picked] : [], null, null));
        _obtainCardPending = null;
        _obtainCardChoices = [];
        _obtainCardSource = default;
        Phase = PredictorPhase.Rewards;
        CompleteObtainDestination(pending);
        ContinueObtainChildIfComplete();
        return PredictorInputResult.Accepted;
    }

    private bool BeginObtainDeckChoice(ModelKey source, PredictorObtainDeckOperation operation, int count,
        PredictorObtainReturn destination, long sourceId)
    {
        _obtainDeckSource = source;
        _obtainDeckOperation = operation;
        _obtainDeckCount = count;
        _obtainDeckRelicId = Working.Relics[^1].Id;
        _obtainDeckPending = new(destination, sourceId);
        var candidates = ObtainDeckCandidates;
        if (source.Entry == "ROYAL_STAMP")
        {
            var royal = PredictorObtainSources.RoyalStampCandidates(Working, candidates);
            Working = royal.State;
            _obtainDeckCandidateOrder = royal.Candidates;
            candidates = _obtainDeckCandidateOrder;
        }
        bool optional = source.Entry is "GNARLED_HAMMER" or "KIFUDA";
        bool toothPopulationKnown = source.Entry != "PAELS_TOOTH" || _preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults ||
            _preview.Partial == null || LivePaelsToothPopulation().OrderedCandidatesKnown;
        if (toothPopulationKnown && (candidates.Length == 0 || (!optional && candidates.Length <= count)) && operation != PredictorObtainDeckOperation.Claws)
        {
            ApplyObtainDeckChoice(candidates);
            bool thenHealth = source.Entry == "PRECARIOUS_SHEARS";
            if (thenHealth)
            {
                _precariousPending = _obtainDeckPending;
                Phase = PredictorPhase.ObtainHealth;
                _requestId++;
            }
            _obtainDeckPending = null;
            _obtainDeckSource = default;
            _obtainDeckCandidateOrder = [];
            return !thenHealth;
        }
        Phase = PredictorPhase.ObtainDeckCards;
        _requestId++;
        return false;
    }

    internal PredictorInputResult SubmitObtainDeckCards(ChooseObtainDeckCards input)
    {
        if (Phase != PredictorPhase.ObtainDeckCards || _obtainDeckPending is not { } pending ||
            input.CardIds.IsDefault || (_obtainDeckOperation == PredictorObtainDeckOperation.Claws ||
                _obtainDeckSource.Entry is "GNARLED_HAMMER" or "KIFUDA"
                ? input.CardIds.Length > _obtainDeckCount : input.CardIds.Length != _obtainDeckCount) ||
            input.CardIds.Distinct().Count() != input.CardIds.Length ||
            input.CardIds.Any(id => !ObtainDeckCandidates.Contains(id))) return PredictorInputResult.Invalid;
        ApplyObtainDeckChoice(input.CardIds);
        bool thenHealth = _obtainDeckSource.Entry == "PRECARIOUS_SHEARS";
        if (thenHealth) _precariousPending = pending;
        _obtainDeckPending = null;
        _obtainDeckSource = default;
        _obtainDeckCandidateOrder = [];
        if (thenHealth)
        {
            Phase = PredictorPhase.ObtainHealth;
            _requestId++;
            return PredictorInputResult.Accepted;
        }
        Phase = PredictorPhase.Rewards;
        CompleteObtainDestination(pending);
        ContinueObtainChildIfComplete();
        return PredictorInputResult.Accepted;
    }

    internal PredictorInputResult SubmitObtainManyCards(ChooseObtainManyCards input)
    {
        if (Phase != PredictorPhase.ObtainChooseManyCards || _obtainManyPending is not { } pending ||
            input.CardIds.IsDefault || input.CardIds.Distinct().Count() != input.CardIds.Length ||
            input.CardIds.Any(id => !_obtainManyChoices.Any(c => c.Id == id))) return PredictorInputResult.Invalid;
        foreach (long id in input.CardIds)
            Working = PredictorObtainSources.AddExistingCard(Context, Working, _obtainManyChoices.Single(c => c.Id == id));
        Accept(new("SeaGlassChooseCards", pending.SourceId, input.CardIds, null, null));
        _obtainManyPending = null;
        _obtainManyChoices = [];
        Phase = PredictorPhase.Rewards;
        CompleteObtainDestination(pending);
        ContinueObtainChildIfComplete();
        return PredictorInputResult.Accepted;
    }

    private void ApplyObtainDeckChoice(ImmutableArray<long> ids)
    {
        if (_obtainDeckOperation == PredictorObtainDeckOperation.Clone)
        {
            foreach (long id in ids)
                Working = PredictorObtainSources.CloneCardToDeck(Context, Working, Working.Card(id));
            Accept(new("DollysMirrorClone", _obtainDeckPending!.SourceId,
                ids.Length == 0 ? [] : [Working.Deck[^1]], null, null));
            return;
        }
        if (_obtainDeckOperation == PredictorObtainDeckOperation.Claws)
        {
            var replacements = new List<(int Index, long OriginalId, PredictorCard Card)>();
            foreach (long id in ids)
            {
                var original = Working.Card(id);
                var maul = PredictorObtainSources.MaulFromOriginal(Context, original) with
                { Id = Working.NextInstanceId, FloorAdded = Working.Position.Floor };
                Working = Working with { NextInstanceId = Working.NextInstanceId + 1,
                    Cards = Working.Cards.Add(maul) };
                int index = Working.Deck.IndexOf(id);
                RemoveCards([id]);
                replacements.Add((index, id, maul));
            }
            replacements.Sort((a, b) => a.Index.CompareTo(b.Index));
            var changes = ImmutableArray.CreateBuilder<PredictorTransformation>();
            foreach (var replacement in replacements)
            {
                int insertion = Working.Deck.Length;
                Working = PredictorObtainSources.AddExistingCard(Context, Working, replacement.Card);
                changes.Add(new(replacement.OriginalId, Working.Deck[insertion]));
            }
            Accept(new("ClawsTransform", _obtainDeckPending!.SourceId, ids, null, null)
            { Transformations = changes.ToImmutable() });
            return;
        }
        if (_obtainDeckOperation == PredictorObtainDeckOperation.Astrolabe)
        {
            var changes = ImmutableArray.CreateBuilder<PredictorTransformation>();
            foreach (long id in ids)
            {
                var changed = PredictorCardChanges.Transform(Context, Working, [id], PredictorStream.Niche,
                    upgradeReplacementBeforeAdd: true);
                Working = changed.State;
                changes.AddRange(changed.Changes);
            }
            Accept(new("AstrolabeTransform", _obtainDeckPending!.SourceId, ids, null, null)
            { Transformations = changes.ToImmutable() });
            return;
        }
        if (_obtainDeckOperation == PredictorObtainDeckOperation.PaelsTooth)
        {
            var ordered = ids.Select(Working.Card).OrderBy(c => c.Key.Entry, StringComparer.Ordinal).ToArray();
            var index = Working.Relics.FindIndex(r => r.Id == _obtainDeckRelicId);
            var relic = Working.Relics[index];
            Working = Working with { Relics = Working.Relics.SetItem(index,
                relic with { StoredCards = ordered.ToImmutableArray() }) };
            foreach (var card in ordered)
                RemoveCards([card.Id]);
            Accept(new("PaelsToothStore", _obtainDeckPending!.SourceId,
                ordered.Select(c => c.Id).ToImmutableArray(), null, null));
            return;
        }
        foreach (long id in ids)
        {
            var card = Working.Card(id);
            if (_obtainDeckOperation == PredictorObtainDeckOperation.Remove)
                RemoveCards([id]);
            else if (_obtainDeckOperation == PredictorObtainDeckOperation.Upgrade)
                Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                    card with { UpgradeLevel = card.UpgradeLevel + 1 }));
            else
            {
                var entry = _obtainDeckSource.Entry switch
                { "TRI_BOOMERANG" => "INSTINCT", "ELECTRIC_SHRYMP" => "IMBUED",
                  "GNARLED_HAMMER" => "SHARP", "KIFUDA" => "ADROIT",
                  "PUNCH_DAGGER" => "MOMENTUM", "ROYAL_STAMP" => "ROYALLY_APPROVED", _ => "CLONE" };
                Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, card with
                {
                    Enchantment = new ModelKey("ENCHANTMENT", entry),
                    EnchantmentAmount = _obtainDeckSource.Entry switch
                    { "PAELS_GROWTH" => 4, "GNARLED_HAMMER" or "KIFUDA" => 3,
                      "PUNCH_DAGGER" => 5, _ => 1 },
                    HasInnateKeyword = card.HasInnateKeyword || entry == "ROYALLY_APPROVED",
                    HasRetainKeyword = card.HasRetainKeyword || entry == "ROYALLY_APPROVED"
                }));
            }
        }
        if (_obtainDeckSource.Entry == "PRESERVED_FOG")
            Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "FOLLY"));
        Accept(new(_obtainDeckSource.Entry + _obtainDeckOperation,
            _obtainDeckPending!.SourceId, ids, null, null));
    }
}

internal static partial class PredictorObtainSources
{
    internal static (PredictorState State, ImmutableArray<long> Candidates) RoyalStampCandidates(
        PredictorState state, ImmutableArray<long> eligible)
    {
        var shuffled = eligible.ToList();
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Niche).Restore();
        rng.UnstableShuffle(shuffled);
        return (state.WithRng(PredictorStream.Niche, rng), shuffled.ToImmutableArray());
    }

    internal static PredictorCard MaulFromOriginal(PredictorContext context, PredictorCard original)
    {
        var maul = PredictorCardChanges.Definition(context, new ModelKey("CARD", "MAUL")).Prototype;
        if (original.UpgradeLevel > 0 && maul.MaxUpgradeLevel > 0)
            maul = maul with { UpgradeLevel = 1 };
        if (original.Enchantment is not { } enchantment)
            return PredictorCardLevelEffects.Reconcile(context, maul);
        bool compatible = enchantment.Entry switch
        {
            "GOOPY" or "IMBUED" or "NIMBLE" or "SOULS_POWER" or "SPIRAL" => false,
            "ADROIT" or "CLONE" or "CORRUPTED" or "GLAM" or "INKY" or "INSTINCT" or
            "MOMENTUM" or "PERFECT_FIT" or "ROYALLY_APPROVED" or "SHARP" or "SLITHER" or
            "SLUMBERING_ESSENCE" or "SOWN" or "STEADY" or "SWIFT" or
            "TEZCATARAS_EMBER" or "VIGOROUS" => true,
            _ => throw new NotImplementedException("PredictorClawsEnchantmentEligibilityPending:" + enchantment)
        };
        if (!compatible) return PredictorCardLevelEffects.Reconcile(context, maul);
        maul = maul with { Enchantment = enchantment, EnchantmentAmount = original.EnchantmentAmount };
        if (enchantment.Entry == "TEZCATARAS_EMBER") maul = maul with
        { PermanentCostOverride = 0, HasEternalKeyword = true, Removable = false };
        return PredictorCardLevelEffects.Reconcile(context, maul);
    }

    internal static (PredictorState State, ImmutableArray<long> CardIds) FragrantMushroom(
        PredictorContext context, PredictorState entry, int count = 2)
    {
        var candidates = entry.Deck.Select(entry.Card)
            .Where(c => c.UpgradeLevel < c.MaxUpgradeLevel).ToList();
        candidates.Sort((a, b) =>
        {
            int category = string.Compare(a.Key.Category, b.Key.Category, StringComparison.Ordinal);
            if (category != 0) return category;
            int key = string.Compare(a.Key.Entry, b.Key.Entry, StringComparison.Ordinal);
            return key != 0 ? key : a.UpgradeLevel.CompareTo(b.UpgradeLevel);
        });
        var rng = entry.Streams.Single(s => s.Stream == PredictorStream.Niche).Restore();
        rng.UnstableShuffle(candidates);
        var state = entry.WithRng(PredictorStream.Niche, rng);
        var selected = candidates.Take(count).ToArray();
        foreach (var card in selected) state = state.WithCard(PredictorCardLevelEffects.Reconcile(context,
            card with { UpgradeLevel = card.UpgradeLevel + 1 }));
        return (state, selected.Select(c => c.Id).ToImmutableArray());
    }

    internal static PredictorGeneratedRewards Bones(PredictorContext context, PredictorState state, CancellationToken token)
    {
        if (context.OpeningEligibleRelics.IsDefaultOrEmpty) throw new InvalidOperationException("PredictorBonesEligibleRelicsMissing");
        var options = context.OpeningEligibleRelics.Where(k => k.Entry != "NEOWS_BONES").ToList();
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        rng.UnstableShuffle(options);
        state = state.WithRng(PredictorStream.Rewards, rng);
        if (options.Count < 2) throw new InvalidOperationException("PredictorBonesRelicsDepleted");
        return new(state, options.Take(2).Select(k => new PredictorReward(0, PredictorRewardKind.Relic,
            PredictorRewardOrigin.BonesInternal, k, [])).ToImmutableArray(), []);
    }

    internal static PredictorState BonesCurse(PredictorContext context, PredictorState state)
    {
        var curses = (context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing"))
            .UnlockedCurseCards.ToArray();
        if (curses.Length == 0) throw new InvalidOperationException("PredictorBonesCursePoolMissing");
        var drawn = state.Draw(PredictorStream.Niche, 0, curses.Length);
        return AddCard(context, drawn.State, curses[drawn.Value]);
    }

    internal static PredictorGeneratedRewards CallingBell(PredictorContext context, PredictorState state, CancellationToken token)
    {
        var keys = ImmutableArray.CreateBuilder<ModelKey>();
        foreach (string rarity in new[] { "Common", "Uncommon", "Rare" })
        {
            var entry = state;
            var pull = state.PersonalBag.Pull(rarity, false,
                key => PredictorRewardGeneration.RelicAllowed(context, entry, key), _ => true);
            var key = pull.Key ?? new ModelKey("RELIC", "CIRCLET");
            state = state with { PersonalBag = pull.Bag, SharedBag = state.SharedBag.Remove(key) };
            keys.Add(key);
        }
        return new(state, keys.Select(k => new PredictorReward(0, PredictorRewardKind.Relic,
            PredictorRewardOrigin.Event, k, [])).ToImmutableArray(), []);
    }

    internal static PredictorGeneratedRewards Kaleidoscope(PredictorContext context, PredictorState state, CancellationToken token)
    {
        var catalog = context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        var ownPool = catalog.CharacterCards.FirstOrDefault()?.PoolId;
        var pools = catalog.AllCharacterCards.Where(c => c.IsUnlockedInCapturedPool && c.PoolId != ownPool)
            .GroupBy(c => c.PoolId).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.ToImmutableArray()).ToList();
        if (pools.Count < 3) throw new InvalidOperationException("PredictorKaleidoscopePoolsMissing");
        var generated = new List<PredictorCard[]>();
        for (int n = 0; n < 2; n++)
        {
            token.ThrowIfCancellationRequested();
            var order = Enumerable.Range(0, pools.Count).ToList();
            var rng = state.Streams.Single(s => s.Stream == PredictorStream.Niche).Restore();
            rng.UnstableShuffle(order);
            state = state.WithRng(PredictorStream.Niche, rng);
            var cards = ImmutableArray.CreateBuilder<PredictorCard>();
            foreach (int pool in order.Take(3))
            {
                var options = pools[pool].Where(c => context.Operation?.PlayerCount > 1 || !c.IsMultiplayerOnly).ToArray();
                var rawCard = PredictorRewardGeneration.GenerateCard(context, ref state,
                    new(PredictorRewardKind.Card, Odds: PredictorCardOdds.Regular), options);
                cards.Add(PredictorRewardGeneration.ModifyManualCardRewardOptions(context, ref state, [rawCard], isCardReward: false)[0]);
            }
            generated.Add(cards.ToArray());
        }
        // Both explicit card lists are generated before OfferCustom populates
        // either CardReward and runs its modifier callbacks.
        var rewards = ImmutableArray.CreateBuilder<PredictorReward>();
        foreach (var raw in generated)
        {
            var modified = PredictorRewardGeneration.ModifyManualCardRewardOptions(context, ref state, raw, true, out var nicheScope);
            rewards.Add(new(0, PredictorRewardKind.Card, PredictorRewardOrigin.Event, default, modified)
            {
                ManuallySetCards = true,
                CardRewardNicheScope = nicheScope,
                CanReroll = PredictorSettlementEffects.Has(state, "DRIFTWOOD"),
                // Vanilla deliberately stores an empty pool, not the original
                // three character pools. Preserve its depletion behavior.
                RerollRecipe = new(PredictorRewardKind.Card, CardCount: raw.Length,
                    NoUpgradeRoll: true, CardPoolOverride: []),
                RerollIsCardReward = false,
                RelicSubscriptionActive = false
            });
        }
        return new(state, rewards.ToImmutable(), []);
    }

    internal static (PredictorState State, ImmutableArray<PredictorCard> Cards) GenerateObtainCardChoices(
        PredictorContext context, PredictorState entry, string source)
    {
        var catalog = context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        bool tablet = source == "HEFTY_TABLET";
        bool massive = source == "MASSIVE_SCROLL";
        if (massive && context.Operation?.PlayerCount is not > 1)
            throw new InvalidOperationException("PredictorMassiveScrollMultiplayerRequired");
        var pool = massive ? catalog.CharacterCards.Concat(catalog.ColorlessCards).Where(c => c.IsMultiplayerOnly).ToArray()
            : tablet ? catalog.CharacterCards.Where(c => c.Rarity == EffectCardRarity.Rare).ToArray()
            : catalog.ColorlessCards.ToArray();
        var state = entry;
        var choices = ImmutableArray.CreateBuilder<PredictorCard>();
        for (int i = 0; i < (tablet || massive ? 3 : 2); i++)
        {
            var available = pool.Where(c => c.IsUnlockedInCapturedPool && choices.All(p => p.Key != c.CardKey)).ToArray();
            var prototype = PredictorRewardGeneration.GenerateCard(context, ref state,
                new(PredictorRewardKind.Card, Odds: tablet ? PredictorCardOdds.Uniform : PredictorCardOdds.Regular,
                    Rarity: tablet ? EffectCardRarity.Rare : null, NoUpgradeRoll: tablet), available);
            foreach (var relic in state.Relics.Where(r => !r.Melted))
            {
                prototype = PredictorRewardGeneration.UpgradeWithEgg(prototype, relic.Key.Entry);
                if (relic.Key.Entry == "FRESNEL_LENS") prototype = ApplyCreationLens(context, prototype);
            }
            var card = PredictorCardLevelEffects.Reconcile(context,
                prototype with { Id = state.NextInstanceId, FloorAdded = state.Position.Floor });
            state = state with { NextInstanceId = state.NextInstanceId + 1, Cards = state.Cards.Add(card) };
            choices.Add(card);
        }
        return (state, choices.ToImmutable());
    }

    internal static PredictorState AddBasicStrikeDefend(PredictorContext context, PredictorState state)
    {
        var catalog = context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        foreach (bool strike in new[] { true, false })
        {
            var key = (strike ? catalog.CharacterStrikeKey : catalog.CharacterDefendKey)
                ?? throw new InvalidOperationException("PredictorCharacterBasicCardMissing");
            state = AddCard(context, state, key);
        }
        return state;
    }

    internal static (PredictorState State, PredictorTransformation Change) ArchaicTooth(
        PredictorContext context, PredictorState state)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BASH"] = "BREAK", ["NEUTRALIZE"] = "SUPPRESS", ["UNLEASH"] = "PROTECTOR",
            ["FALLING_STAR"] = "METEOR_SHOWER", ["DUALCAST"] = "QUADCAST"
        };
        // AfterObtained looks up the first qualifying CURRENT Deck instance.
        var original = state.Deck.Select(state.Card).FirstOrDefault(c => replacements.ContainsKey(c.Key.Entry))
            ?? throw new InvalidOperationException("PredictorArchaicStarterMissingAtObtain");
        var replacement = ArchaicToothReplacement(context, original) with { FloorAdded = state.Position.Floor };
        state = state with { Deck = state.Deck.Remove(original.Id) };
        state = state.WithCard(original with { Removed = true });
        long newId = state.NextInstanceId;
        state = AddCard(context, state, replacement);
        return (state, new(original.Id, newId));
    }

    internal static PredictorCard ArchaicToothReplacement(PredictorContext context, PredictorCard original)
    {
        var target = original.Key.Entry switch
        {
            "BASH" => "BREAK", "NEUTRALIZE" => "SUPPRESS", "UNLEASH" => "PROTECTOR",
            "FALLING_STAR" => "METEOR_SHOWER", "DUALCAST" => "QUADCAST",
            _ => throw new InvalidOperationException("PredictorArchaicStarterMissingAtObtain")
        };
        var prototype = PredictorCardChanges.Definition(context, new ModelKey("CARD", target)).Prototype;
        var replacement = prototype with { UpgradeLevel = original.UpgradeLevel,
            Enchantment = original.Enchantment, EnchantmentAmount = original.EnchantmentAmount,
            FloorAdded = original.FloorAdded };
        if (replacement.Enchantment?.Entry == "TEZCATARAS_EMBER") replacement = replacement with
        { PermanentCostOverride = 0, Removable = false };
        if (replacement.UpgradeLevel > replacement.MaxUpgradeLevel)
            throw new InvalidOperationException("PredictorArchaicUpgradeInvalid");
        return PredictorCardLevelEffects.Reconcile(context, replacement);
    }

    internal static (long Id, ModelKey Upgrade) TouchTarget(PredictorState state)
    {
        var upgrades = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BURNING_BLOOD"] = "BLACK_BLOOD", ["RING_OF_THE_SNAKE"] = "RING_OF_THE_DRAKE",
            ["DIVINE_RIGHT"] = "DIVINE_DESTINY", ["BOUND_PHYLACTERY"] = "PHYLACTERY_UNBOUND",
            ["CRACKED_CORE"] = "INFUSED_CORE"
        };
        var touch = state.Relics.Last(r => r.Key.Entry == "TOUCH_OF_OROBAS");
        PredictorRelic? starter = touch.StarterRelicId is long bound
            ? state.Relics.FirstOrDefault(r => r.Id == bound)
            : state.Relics.FirstOrDefault(r => upgrades.ContainsKey(r.Key.Entry));
        if (starter == null) throw new InvalidOperationException("PredictorTouchStarterMissingAtObtain");
        return (starter.Id, TouchUpgrade(starter.Key));
    }

    internal static ModelKey TouchUpgrade(ModelKey starter) => new("RELIC", starter.Entry switch
    {
        "BURNING_BLOOD" => "BLACK_BLOOD", "RING_OF_THE_SNAKE" => "RING_OF_THE_DRAKE",
        "DIVINE_RIGHT" => "DIVINE_DESTINY", "BOUND_PHYLACTERY" => "PHYLACTERY_UNBOUND",
        "CRACKED_CORE" => "INFUSED_CORE", _ => "CIRCLET"
    });

    internal static (PredictorState State, ImmutableArray<PredictorTransformation> Changes) Leafy(
        PredictorContext context, PredictorState state, CancellationToken token)
    {
        var defs = state.Deck.Select(state.Card).Where(c => PredictorCardChanges.Definition(context, c.Key).Rarity == "Basic").ToArray();
        var strike = defs.FirstOrDefault(c => PredictorCardChanges.Definition(context, c.Key).IsStrike);
        var defend = defs.FirstOrDefault(c => PredictorCardChanges.Definition(context, c.Key).IsDefend);
        var ids = new[] { strike?.Id, defend?.Id }.OfType<long>().ToImmutableArray();
        return PredictorCardChanges.Transform(context, state, ids, PredictorStream.Transformations, token);
    }

    internal static (PredictorState State, ImmutableArray<ImmutableArray<PredictorCard>> Bundles) ScrollBoxes(
        PredictorContext context, PredictorState entry)
    {
        var catalog = context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        var commons = catalog.CharacterCards.Where(c => c.IsUnlockedInCapturedPool && c.Rarity == EffectCardRarity.Common).ToArray();
        var uncommons = catalog.CharacterCards.Where(c => c.IsUnlockedInCapturedPool && c.Rarity == EffectCardRarity.Uncommon).ToArray();
        if (commons.Length < 4 || uncommons.Length < 2) throw new InvalidOperationException("PredictorScrollBoxesPoolIneligible");
        var used = new HashSet<ModelKey>();
        var state = entry;
        var bundles = ImmutableArray.CreateBuilder<ImmutableArray<PredictorCard>>();
        for (int n = 0; n < 2; n++)
        {
            var keys = new List<ModelKey>();
            bool clawBundle = false;
            if (context.Character.Entry == "DEFECT")
            {
                var claw = state.Draw(PredictorStream.Rewards, 0, 100);
                state = claw.State;
                if (claw.Value < 1)
                {
                    keys.AddRange(Enumerable.Repeat(new ModelKey("CARD", "CLAW"), 3));
                    clawBundle = true;
                }
            }
            if (keys.Count == 0)
            {
                for (int i = 0; i < 2; i++)
                {
                    var choices = commons.Where(c => !used.Contains(c.CardKey)).ToArray();
                    var draw = state.Draw(PredictorStream.Rewards, 0, choices.Length);
                    state = draw.State;
                    var key = choices[draw.Value].CardKey;
                    keys.Add(key);
                    used.Add(key);
                }
                var uncommonOptions = uncommons.Where(c => !used.Contains(c.CardKey)).ToArray();
                var uncommon = state.Draw(PredictorStream.Rewards, 0, uncommonOptions.Length);
                state = uncommon.State;
                var uncommonKey = uncommonOptions[uncommon.Value].CardKey;
                keys.Add(uncommonKey);
                used.Add(uncommonKey);
            }
            var cards = ImmutableArray.CreateBuilder<PredictorCard>();
            foreach (var key in keys)
            {
                var card = PredictorCardChanges.Definition(context, key).Prototype with
                { Id = state.NextInstanceId, FloorAdded = state.Position.Floor };
                if (!clawBundle)
                    foreach (var relic in state.Relics.Where(r => !r.Melted))
                    {
                        card = PredictorRewardGeneration.UpgradeWithEgg(card, relic.Key.Entry);
                        if (relic.Key.Entry == "FRESNEL_LENS") card = ApplyCreationLens(context, card);
                    }
                state = state with { NextInstanceId = state.NextInstanceId + 1, Cards = state.Cards.Add(card) };
                cards.Add(card);
            }
            bundles.Add(cards.ToImmutable());
        }
        return (state, bundles.ToImmutable());
    }

    internal static (PredictorState State, ImmutableArray<PredictorCard> Cards) SeaGlass(
        PredictorContext context, PredictorState entry, ModelKey targetCharacter)
    {
        var catalog = context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        var characterPool = catalog.CharacterPools.SingleOrDefault(p => p.Character == targetCharacter)
            ?? throw new InvalidOperationException("PredictorSeaGlassTargetPoolMissing");
        var definitions = catalog.CardDefinitions.ToDictionary(d => d.Prototype.Key);
        var state = entry;
        var all = ImmutableArray.CreateBuilder<PredictorCard>();
        foreach (string rarity in new[] { "Common", "Uncommon", "Rare" })
        {
            var remaining = characterPool.Cards.Where(k => definitions.TryGetValue(k, out var d) && d.Rarity == rarity).ToList();
            if (remaining.Count < 5) throw new InvalidOperationException("PredictorSeaGlassPoolDepleted:" + rarity);
            for (int n = 0; n < 5; n++)
            {
                var draw = state.Draw(PredictorStream.Rewards, 0, remaining.Count);
                state = draw.State;
                var key = remaining[draw.Value];
                remaining.RemoveAt(draw.Value);
                var card = definitions[key].Prototype with { Id = state.NextInstanceId, FloorAdded = state.Position.Floor };
                foreach (var relic in state.Relics.Where(r => !r.Melted))
                {
                    card = PredictorRewardGeneration.UpgradeWithEgg(card, relic.Key.Entry);
                    if (relic.Key.Entry == "FRESNEL_LENS") card = ApplyCreationLens(context, card);
                }
                state = state with { NextInstanceId = state.NextInstanceId + 1, Cards = state.Cards.Add(card) };
                all.Add(card);
            }
        }
        return (state, all.ToImmutable());
    }

    private static PredictorCard ApplyCreationLens(PredictorContext context, PredictorCard card) =>
        CanEnchant(context, card) && GainsBlockForFresnel(context, card)
            ? PredictorCardLevelEffects.Reconcile(context,
                card with { Enchantment = new("ENCHANTMENT", "NIMBLE"), EnchantmentAmount = 2 }) : card;

    internal static PredictorState BeautifulBracelet(PredictorContext context, PredictorState state)
    {
        var eligible = state.Deck.Select(state.Card).Where(c => CanEnchant(context, c)).ToList();
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Niche).Restore();
        rng.UnstableShuffle(eligible);
        state = state.WithRng(PredictorStream.Niche, rng);
        foreach (var card in eligible.Take(4))
            state = state.WithCard(PredictorCardLevelEffects.Reconcile(context,
                card with { Enchantment = new("ENCHANTMENT", "SWIFT"), EnchantmentAmount = 2 }));
        return state;
    }

    internal static PredictorState EnchantDeckSnapshot(PredictorContext context, PredictorState state,
        ModelKey enchantment, int amount, Func<PredictorCard, bool> sourceFilter)
    {
        var snapshot = state.Deck.Select(state.Card).ToArray();
        foreach (var card in snapshot)
            if (sourceFilter(card) && CanEnchant(context, card))
            {
                var enchanted = card with { Enchantment = enchantment, EnchantmentAmount = amount };
                if (enchantment.Entry == "TEZCATARAS_EMBER") enchanted = enchanted with
                { PermanentCostOverride = 0, HasEternalKeyword = true, Removable = false };
                if (enchantment.Entry == "GOOPY") enchanted = enchanted with { HasExhaustKeyword = true };
                if (enchantment.Entry is "STEADY" or "ROYALLY_APPROVED") enchanted = enchanted with
                { HasRetainKeyword = true, HasInnateKeyword = enchanted.HasInnateKeyword || enchantment.Entry == "ROYALLY_APPROVED" };
                state = state.WithCard(PredictorCardLevelEffects.Reconcile(context, enchanted));
            }
        return state;
    }

}
