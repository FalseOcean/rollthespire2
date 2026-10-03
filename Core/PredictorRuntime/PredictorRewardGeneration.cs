using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorCombatKind { Monster, Elite, Boss }
internal enum PredictorCardOdds { Regular, Elite, Boss, Uniform }
internal enum PredictorRewardStage { Construct, PopulateOriginal, Modify, ModifyLate, PopulateAdded, AfterModifying, Sort, BeforeOffering }
internal sealed record PredictorRewardPlan(PredictorRewardKind Kind, int MinGold = 0, int MaxGold = 0,
    bool PrefilledGold = false, int CardCount = 3, PredictorCardOdds Odds = PredictorCardOdds.Regular,
    bool EncounterSource = false, bool IsFromCombat = false, bool Colorless = false,
    EffectCardRarity? Rarity = null, bool NoUpgradeRoll = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    ImmutableArray<ModelKey> CardPoolOverride = default, bool NoCardPoolModifications = false,
    ModelKey? PrefilledPotionKey = null, ModelKey? PrefilledRelicKey = null, ModelKey? PrefilledCardKey = null,
    bool ForceRarityOddsChange = false, bool NoRarityModification = false, bool UseEventRng = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    ImmutableArray<ModelKey> PrefilledCardKeys = default, PredictorRewardPlan? ManualRerollRecipe = null,
    long? PrefilledCardInstanceId = null, bool WasGoldStolenBack = false);
internal sealed record PredictorGeneratedRewards(PredictorState State, ImmutableArray<PredictorReward> Rewards,
    ImmutableArray<PredictorRewardStage> Stages);

internal static class PredictorRewardGeneration
{
    internal static PredictorGeneratedRewards Generate(PredictorContext context, PredictorState entry,
        IEnumerable<PredictorRewardPlan> originals, PredictorCombatKind? room = null, CancellationToken cancellationToken = default,
        Xoshiro256StarStar? eventRng = null)
    {
        var state = entry;
        var rewards = new List<PredictorReward>();
        var added = new List<PredictorRewardPlan>();
        var stages = ImmutableArray.CreateBuilder<PredictorRewardStage>();
        stages.Add(PredictorRewardStage.Construct);
        stages.Add(PredictorRewardStage.PopulateOriginal);
        foreach (var plan in originals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rewards.Add(Populate(context, ref state, plan, original: true, rngOverride: plan.UseEventRng
                ? eventRng ?? throw new InvalidOperationException("PredictorEventRewardRngMissing") : null));
        }
        stages.Add(PredictorRewardStage.Modify);
        var modified = new List<long>();
        // Concrete source handlers, in actual relic instance order. Added objects
        // remain plans until BOTH normal and late ModifyRewards have finished.
        foreach (var relic in state.Relics.Where(r => !r.Melted))
        {
            switch (relic.Key.Entry)
            {
                case "PRAYER_WHEEL" when room == PredictorCombatKind.Monster:
                    added.Add(new(PredictorRewardKind.Card, EncounterSource: true)); break;
                case "WHITE_STAR" when room == PredictorCombatKind.Elite:
                    added.Add(new(PredictorRewardKind.Card, Odds: PredictorCardOdds.Boss, EncounterSource: true)); break;
                case "BLACK_STAR" when room == PredictorCombatKind.Elite:
                    added.Add(new(PredictorRewardKind.Relic)); break;
                case "LAVA_ROCK" when room == PredictorCombatKind.Boss && state.Position.Act == 1 && !relic.LavaRockTriggered:
                    added.AddRange(Enumerable.Repeat(new PredictorRewardPlan(PredictorRewardKind.Relic), 2));
                    int lavaIndex = state.Relics.FindIndex(r => r.Id == relic.Id);
                    state = state with { Relics = state.Relics.SetItem(lavaIndex, relic with { LavaRockTriggered = true }) };
                    break;
                case "AMETHYST_AUBERGINE" when room != null && !(room == PredictorCombatKind.Boss && state.Position.Act == 3):
                    added.Add(new(PredictorRewardKind.Gold, 15, 15, PrefilledGold: true)); break;
                case "WONGOS_MYSTERY_TICKET" when room != null && relic.CombatsFinished >= 5 && !relic.GaveRelic:
                    added.AddRange(Enumerable.Repeat(new PredictorRewardPlan(PredictorRewardKind.Relic), 3));
                    modified.Add(relic.Id); break;
            }
        }
        stages.Add(PredictorRewardStage.ModifyLate);
        bool reroll = state.Relics.Any(r => !r.Melted && r.Key.Entry == "DRIFTWOOD");
        if (reroll)
            for (int i = 0; i < rewards.Count; i++)
                if (rewards[i].Kind == PredictorRewardKind.Card) rewards[i] = rewards[i] with { CanReroll = true };
        // Late mutations see the already populated originals and the as-yet
        // unpopulated normal additions. Midas must not reroll an original Gold;
        // Vintage must not undo its removed CardReward's earlier RNG/hooks.
        var pending = added.Select(p => (Plan: p, Subscription: p.Kind == PredictorRewardKind.Card ? ReserveSubscription(ref state) : 0L)).ToList();
        foreach (var modifier in context.Modifiers)
        {
            if (modifier.Key.Entry == "MIDAS")
            {
                for (int i = 0; i < rewards.Count; i++)
                    if (rewards[i].Kind == PredictorRewardKind.Gold)
                        rewards[i] = rewards[i] with { GoldAmount = checked(rewards[i].GoldAmount * 2) };
                for (int i = 0; i < pending.Count; i++)
                    if (pending[i].Plan.Kind == PredictorRewardKind.Gold)
                    {
                        int amount = pending[i].Plan.PrefilledGold ? checked(pending[i].Plan.MinGold * 2) : 0;
                        pending[i] = (new(PredictorRewardKind.Gold, amount, amount, PrefilledGold: true), 0);
                    }
            }
            if (modifier.Key.Entry == "VINTAGE" && room == PredictorCombatKind.Monster)
            {
                for (int i = 0; i < rewards.Count; i++)
                    if (rewards[i].Kind == PredictorRewardKind.Card)
                    {
                        state = state with { DetachedCardRewards = state.DetachedCardRewards.Add(rewards[i]) };
                        // Keep the replacement at the removed object's position.
                        rewards[i] = new(0, PredictorRewardKind.Relic, PredictorRewardOrigin.Combat, default, []);
                    }
                for (int i = 0; i < pending.Count; i++)
                    if (pending[i].Plan.Kind == PredictorRewardKind.Card)
                    {
                        state = state with { DetachedCardRewards = state.DetachedCardRewards.Add(new(0,
                            PredictorRewardKind.Card, PredictorRewardOrigin.Combat, default, []) { SubscriptionOrder = pending[i].Subscription }) };
                        pending[i] = (new(PredictorRewardKind.Relic), 0);
                    }
            }
        }
        stages.Add(PredictorRewardStage.PopulateAdded);
        for (int i = 0; i < rewards.Count; i++)
            if (rewards[i].Kind == PredictorRewardKind.Relic && !rewards[i].Key.IsValid)
                rewards[i] = Populate(context, ref state, new(PredictorRewardKind.Relic), original: false);
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var populated = Populate(context, ref state, item.Plan, original: false, item.Subscription);
            rewards.Add(populated with { CanReroll = reroll && populated.Kind == PredictorRewardKind.Card });
        }
        stages.Add(PredictorRewardStage.AfterModifying);
        foreach (long id in modified)
        {
            int index = state.Relics.FindIndex(r => r.Id == id);
            state = state with { Relics = state.Relics.SetItem(index, state.Relics[index] with { GaveRelic = true }) };
        }
        stages.Add(PredictorRewardStage.Sort);
        static int Order(PredictorReward r) => r.Kind switch { PredictorRewardKind.Gold => 1, PredictorRewardKind.Potion => 2, PredictorRewardKind.Relic => 3, PredictorRewardKind.SpecialCard => 4, PredictorRewardKind.RemoveCard => 7, _ => 5 };
        rewards.Sort((a,b) => Order(a).CompareTo(Order(b)));
        stages.Add(PredictorRewardStage.BeforeOffering);
        if (room != null && rewards.Any(r => r.Kind == PredictorRewardKind.Card))
            state = state with { Relics = state.Relics.Select(r => !r.Melted && r.Key.Entry == "LASTING_CANDY"
                ? r with { CombatRewardsSeen = r.CombatRewardsSeen + 1 } : r).ToImmutableArray() };
        return new(state, rewards.ToImmutableArray(), stages.ToImmutable());
    }

    private static long ReserveSubscription(ref PredictorState state)
    {
        long id = state.NextInstanceId;
        state = state with { NextInstanceId = checked(id + 1) };
        return id;
    }

    private static PredictorReward Populate(PredictorContext context, ref PredictorState state, PredictorRewardPlan plan, bool original, long subscription = 0,
        Xoshiro256StarStar? rngOverride = null)
    {
        var reward = new PredictorReward(0, plan.Kind, PredictorRewardOrigin.Event, default, []);
        switch (plan.Kind)
        {
            case PredictorRewardKind.Gold:
                int amount = plan.MinGold;
                if (original || !plan.PrefilledGold)
                {
                    if (rngOverride == null) (amount, state) = state.Draw(PredictorStream.Rewards, plan.MinGold, checked(plan.MaxGold + 1));
                    else amount = checked(plan.MinGold + rngOverride.NextInt(checked(plan.MaxGold - plan.MinGold + 1)));
                }
                return reward with { GoldAmount = amount, WasGoldStolenBack = plan.WasGoldStolenBack };
            case PredictorRewardKind.Potion:
                return reward with { Key = plan.PrefilledPotionKey ?? GeneratePotions(context, ref state, 1, PredictorStream.Rewards, rngOverride: rngOverride)[0] };
            case PredictorRewardKind.Relic:
                return reward with { Key = plan.PrefilledRelicKey ?? PullRelic(context, ref state, rngOverride) };
            case PredictorRewardKind.SpecialCard:
                if (plan.PrefilledCardInstanceId is { } existingId)
                {
                    if (state.Deck.Contains(existingId) || state.Card(existingId).Removed)
                        throw new ArgumentException("PredictorReturnedCardInstanceInvalid");
                    return reward with { Cards = [state.Card(existingId)], RelicSubscriptionActive = false, PreserveCardInstance = true };
                }
                if (plan.PrefilledCardKey is not { } cardKey) throw new ArgumentException("PredictorSpecialCardMissing");
                return reward with { Cards = [PredictorCardChanges.Definition(context, cardKey).Prototype], RelicSubscriptionActive = false };
            case PredictorRewardKind.RemoveCard:
                // CardRemovalReward.Populate is empty. OnSelect opens a
                // cancelable single-card Deck removal choice.
                return reward with { RelicSubscriptionActive = false };
            default:
                if (!plan.PrefilledCardKeys.IsDefault)
                {
                    var manual = plan.PrefilledCardKeys.Select(k => PredictorCardChanges.Definition(context, k).Prototype).ToList();
                    return reward with { Cards = ModifyManualCardRewardOptions(context, ref state, manual), ManuallySetCards = true,
                        RelicSubscriptionActive = false, RerollRecipe = plan.ManualRerollRecipe };
                }
                if (subscription == 0) subscription = ReserveSubscription(ref state);
                return reward with { Cards = GenerateCards(context, ref state, plan, isCardReward: true, rngOverride), SubscriptionOrder = subscription, RerollRecipe = plan };
        }
    }

    internal static ImmutableArray<ModelKey> GeneratePotions(PredictorContext context, ref PredictorState state, int count, PredictorStream stream, bool inCombat = false,
        Xoshiro256StarStar? rngOverride = null)
    {
        var pool = context.PotionPool.Where(p => !inCombat || p.CanBeGeneratedInCombat).ToList();
        var rng = rngOverride ?? state.Streams.Single(s => s.Stream == stream).Restore();
        var result = ImmutableArray.CreateBuilder<ModelKey>();
        for (int i = 0; i < count; i++)
        {
            float roll = rng.NextFloat();
            var rarity = roll <= .1f ? PredictorPotionRarity.Rare : roll <= .35f ? PredictorPotionRarity.Uncommon : PredictorPotionRarity.Common;
            var bucket = pool.Where(p => p.Rarity == rarity).ToArray();
            if (bucket.Length == 0) throw new InvalidOperationException("PredictorPotionRarityDepleted");
            var item = bucket[rng.NextInt(bucket.Length)];
            result.Add(item.Key);
            pool.Remove(item);
        }
        if (rngOverride == null) state = state.WithRng(stream, rng);
        return result.ToImmutable();
    }

    internal static ModelKey PullRelic(PredictorContext context, ref PredictorState state, Xoshiro256StarStar? rngOverride = null)
    {
        var rng = rngOverride ?? state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        float roll = rng.NextFloat();
        string rarity = roll < .5f ? "Common" : roll < .83f ? "Uncommon" : "Rare";
        var entry = state;
        var pull = state.PersonalBag.Pull(rarity, false, key => RelicAllowed(context, entry, key), _ => true);
        var key = pull.Key ?? new ModelKey("RELIC", "CIRCLET");
        if (rngOverride == null) state = state.WithRng(PredictorStream.Rewards, rng);
        state = state with { PersonalBag = pull.Bag, SharedBag = state.SharedBag.Remove(key) };
        return key;
    }

    internal static bool RelicAllowed(PredictorContext context, PredictorState state, ModelKey key)
    {
        if (context.Crystal is { } crystal) return crystal.EligibleRelics.Contains(key);
        if (key.Entry == "MASSIVE_SCROLL") return false;
        if (key.Entry == "LASTING_CANDY" && context.Character.Entry == "IRONCLAD" && context.NumberOfRuns == 0) return false;
        return key.Entry is not ("AMETHYST_AUBERGINE" or "BOOK_OF_FIVE_RINGS" or "BOWLER_HAT" or "DRAGON_FRUIT" or
            "FROZEN_EGG" or "GIRYA" or "JUZU_BRACELET" or "LASTING_CANDY" or "LUCKY_FYSH" or "MEAL_TICKET" or
            "MOLTEN_EGG" or "OLD_COIN" or "PLANISPHERE" or "SHOVEL" or "TOXIC_EGG" or "WHITE_BEAST_STATUE" or "WHITE_STAR") || state.Position.Floor < 41;
    }

    internal static ImmutableArray<NeowEffectCardSnapshot> ResolveCardPool(PredictorContext context,
        PredictorState state, ref PredictorRewardPlan plan, bool isCardReward)
    {
        var catalog = context.Catalog ?? throw new InvalidOperationException("PredictorCardCatalogMissing");
        var pool = plan.Colorless ? catalog.ColorlessCards : catalog.CharacterCards;
        if (!plan.CardPoolOverride.IsDefault)
        {
            var cardsByKey = catalog.AllCharacterCards.Concat(catalog.CharacterCards).Concat(catalog.ColorlessCards)
                .DistinctBy(c => c.CardKey).ToDictionary(c => c.CardKey);
            pool = plan.CardPoolOverride.Select(k => cardsByKey[k]).ToImmutableArray();
        }
        bool hasColorless = plan.Colorless;
        if (isCardReward && !plan.NoCardPoolModifications)
            foreach (var relic in state.Relics.Where(r => !r.Melted))
            {
                if (relic.Key.Entry == "DINGY_RUG" && !hasColorless)
                { pool = pool.AddRange(catalog.ColorlessCards); hasColorless = true; }
                if (relic.Key.Entry == "PRISMATIC_GEM" && !plan.Colorless)
                {
                    if (catalog.AllCharacterCards.IsDefaultOrEmpty) throw new InvalidOperationException("PredictorUnlockedCharacterPoolsMissing");
                    // Vanilla unions unlocked pools first, then the existing
                    // pools. Keep an existing pool even if absent from unlocks.
                    pool = catalog.AllCharacterCards.Concat(pool).DistinctBy(c => c.CardKey).ToImmutableArray();
                }
            }
        if (isCardReward && !plan.NoCardPoolModifications)
            foreach (var modifier in context.Modifiers)
            {
                if (modifier.Key.Entry == "CHARACTER_CARDS")
                {
                    var keys = catalog.CharacterPools.Single(p => p.Character == modifier.Character).Cards.ToHashSet();
                    pool = pool.Concat(catalog.AllCharacterCards.Where(c => keys.Contains(c.CardKey))).DistinctBy(c => c.CardKey).ToImmutableArray();
                }
                if (modifier.Key.Entry == "BIG_GAME_HUNTER" && plan.EncounterSource && plan.Odds == PredictorCardOdds.Elite && !plan.NoRarityModification)
                {
                    plan = plan with { Odds = PredictorCardOdds.Uniform, Rarity = EffectCardRarity.Rare };
                    if (!pool.Any(c => c.Rarity == EffectCardRarity.Rare)) pool = catalog.CharacterCards;
                }
            }
        var rarity = plan.Rarity;
        pool = pool.Where(c => (context.Crystal?.PlayerCount > 1 || !c.IsMultiplayerOnly) && c.IsUnlockedInCapturedPool &&
            (rarity == null || c.Rarity == rarity)).ToImmutableArray();
        return pool;
    }

    internal static ImmutableArray<PredictorCard> GenerateCards(PredictorContext context, ref PredictorState state,
        PredictorRewardPlan plan, bool isCardReward, Xoshiro256StarStar? rngOverride = null)
    {
        var pool = ResolveCardPool(context, state, ref plan, isCardReward);
        var cards = new List<PredictorCard>();
        for (int i = 0; i < plan.CardCount; i++)
            cards.Add(GenerateCard(context, ref state, plan, pool.Where(c => cards.All(t => t.Key != c.CardKey)).ToArray(), rngOverride));
        {
            foreach (var relic in state.Relics.Where(r => !r.Melted))
                if (isCardReward && relic.Key.Entry == "LASTING_CANDY" && plan.EncounterSource && plan.IsFromCombat && relic.CombatRewardsSeen % 2 == 1)
                {
                    var power = pool.Where(c => c.CardType == EffectCardType.Power && cards.All(t => t.Key != c.CardKey)).ToArray();
                    if (power.Length == 0) power = pool.Where(c => c.CardType == EffectCardType.Power).ToArray();
                    if (power.Length > 0) cards.Add(GenerateCard(context, ref state, plan with { EncounterSource = false }, power, rngOverride));
                }
        }
        return ModifyManualCardRewardOptions(context, ref state, cards, isCardReward);
    }

    internal static ImmutableArray<PredictorCard> ModifyManualCardRewardOptions(PredictorContext context,
        ref PredictorState state, IEnumerable<PredictorCard> input, bool isCardReward = true)
    {
            var cards = input.ToList();
            var after = new List<long>();
            foreach (var relic in state.Relics.Where(r => !r.Melted))
            {
                cards = cards.Select(c => UpgradeWithEgg(c, relic.Key.Entry)).ToList();
                if (isCardReward && relic.Key.Entry == "SILVER_CRUCIBLE" && relic.RewardUpgradeUses < 3)
                {
                    cards = cards.Select(c => c.UpgradeLevel < c.MaxUpgradeLevel ? c with { UpgradeLevel = c.UpgradeLevel + 1 } : c).ToList();
                    after.Add(relic.Id);
                }
                if (isCardReward && relic.Key.Entry == "SILKEN_TRESS" && !relic.SilkenUsed)
                {
                    cards = cards.Select(c => c.Enchantment == null && c.Type is EffectCardType.Attack or EffectCardType.Skill or EffectCardType.Power
                        ? c with { Enchantment = new ModelKey("ENCHANTMENT", "GLAM"), EnchantmentAmount = 1 } : c).ToList();
                    after.Add(relic.Id);
                }
                (state, cards) = PredictorCardRewardRelicHooks.ApplyLate(context, state, cards, relic.Key, cloneOnChange: false, relicId: relic.Id);
            }
            foreach (long id in after)
            {
                int i = state.Relics.FindIndex(r => r.Id == id);
                var relic = state.Relics[i];
                state = state with { Relics = state.Relics.SetItem(i, relic.Key.Entry == "SILVER_CRUCIBLE"
                    ? relic with { RewardUpgradeUses = relic.RewardUpgradeUses + 1 } : relic with { SilkenUsed = true }) };
            }
        return cards.Select(c => PredictorCardLevelEffects.Reconcile(context, c)).ToImmutableArray();
    }

    internal static PredictorCard GenerateCard(PredictorContext context, ref PredictorState state,
        PredictorRewardPlan plan, IReadOnlyList<NeowEffectCardSnapshot> pool, Xoshiro256StarStar? rngOverride = null)
    {
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        NeowEffectCardSnapshot[] choices;
        if (plan.Odds == PredictorCardOdds.Uniform)
            choices = pool.Where(c => c.Rarity is not (EffectCardRarity.Basic or EffectCardRarity.Ancient)).ToArray();
        else
        {
            float baseRare = plan.Odds switch { PredictorCardOdds.Boss => 1f, PredictorCardOdds.Elite => context.Ascension >= 7 ? .05f : .1f, _ => context.Ascension >= 7 ? .0149f : .03f };
            float uncommon = plan.Odds switch { PredictorCardOdds.Boss => 0f, PredictorCardOdds.Elite => .4f, _ => .37f };
            float offset = (plan.EncounterSource || plan.ForceRarityOddsChange) && plan.Odds != PredictorCardOdds.Boss ? state.Odds.CardRarityOffset : 0;
            float roll = rng.NextFloat();
            var rolled = roll < baseRare + offset ? EffectCardRarity.Rare : roll < baseRare + offset + uncommon ? EffectCardRarity.Uncommon : EffectCardRarity.Common;
            if (plan.EncounterSource || plan.ForceRarityOddsChange)
                state = state with { Odds = state.Odds with { CardRarityOffset = rolled == EffectCardRarity.Rare ? -.05f : Math.Min(.4f, state.Odds.CardRarityOffset + (context.Ascension >= 7 ? .005f : .01f)) } };
            var rarity = rolled;
            do
            {
                choices = pool.Where(c => c.Rarity == rarity).ToArray();
                if (choices.Length > 0) break;
                rarity = rarity switch { EffectCardRarity.Common => EffectCardRarity.Uncommon, EffectCardRarity.Uncommon => EffectCardRarity.Rare, _ => EffectCardRarity.Common };
            } while (rarity != rolled);
        }
        if (choices.Length == 0) throw new InvalidOperationException("PredictorCardPoolDepleted");
        var identityRng = rngOverride ?? rng;
        var selected = choices[identityRng.NextInt(choices.Length)];
        var card = PredictorCardChanges.Definition(context, selected.CardKey).Prototype;
        if (!plan.NoUpgradeRoll)
        {
            decimal upgrade = (decimal)identityRng.NextFloat();
            decimal chance = selected.Rarity == EffectCardRarity.Rare ? 0m : Math.Max(0, state.Position.Act - 1) * (context.Ascension >= 7 ? .125m : .25m);
            if (selected.CanUpgrade && upgrade <= chance && card.UpgradeLevel < card.MaxUpgradeLevel)
                card = card with { UpgradeLevel = card.UpgradeLevel + 1 };
        }
        state = state.WithRng(PredictorStream.Rewards, rng);
        return PredictorCardLevelEffects.Reconcile(context, card);
    }

    internal static PredictorCard UpgradeWithEgg(PredictorCard card, string relic) =>
        card.UpgradeLevel < card.MaxUpgradeLevel && (relic == "MOLTEN_EGG" && card.Type == EffectCardType.Attack ||
            relic == "TOXIC_EGG" && card.Type == EffectCardType.Skill || relic == "FROZEN_EGG" && card.Type == EffectCardType.Power)
        ? card with { UpgradeLevel = card.UpgradeLevel + 1 } : card;

    // Deck insertion is a different hook from reward creation/open-reward
    // notifications. Vanilla refuses already upgraded cards here.
    internal static PredictorCard UpgradeOnDeckAddWithEgg(PredictorCard card, string relic) =>
        card.UpgradeLevel == 0 ? UpgradeWithEgg(card, relic) : card;
}
