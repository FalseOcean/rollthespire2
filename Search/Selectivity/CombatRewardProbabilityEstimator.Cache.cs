using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class CombatRewardProbabilityEstimator
{
    // Only the inner, pure DP result is shared. Route compatibility, authority
    // checks and probability evidence are rebuilt for every query above this layer.
    private const int ProfileProbabilityCacheCapacity = 32;
    private const int MaximumProfileProbabilityKeyBytes = 32 * 1024;
    private static readonly object ProfileProbabilityCacheGate = new();
    private static readonly Dictionary<string, double> ProfileProbabilityCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> ProfileProbabilityCacheOrder = new();
    private static long _profileProbabilityComputations;

    internal static (long Computations, int Entries) ProfileProbabilityCacheDiagnostics
    {
        get
        {
            lock (ProfileProbabilityCacheGate)
                return (Interlocked.Read(ref _profileProbabilityComputations), ProfileProbabilityCache.Count);
        }
    }

    private static string? ProfileProbabilityCacheKey(int ascension, CardPoolModel cards,
        PotionPoolModel potions, MatcherContext matcher, RewardInfluenceProfile profile)
    {
        // Length-prefixed binary values preserve exact mod identities and sequence
        // multiplicity, without hash-only collisions or mutable snapshot references.
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(ascension);
        writer.Write(profile.PrayerWheel);
        writer.Write(profile.WhiteBeastStatue);
        writer.Write(profile.LastingCandy);
        writer.Write(profile.AmethystAubergine);

        writer.Write(cards.Entries.Length);
        foreach (CardPoolEntry entry in cards.Entries)
        {
            Key(entry.Key);
            writer.Write((int)entry.Rarity);
            writer.Write((int)entry.Type);
            writer.Write(entry.TargetIndex);
        }
        Integers(cards.TotalByRarity);
        Integers(cards.PowerByRarity);
        Indexes(cards.TargetIndexByKey);
        writer.Write(cards.TargetCount);

        Integers(potions.TotalByRarity);
        writer.Write(potions.TargetInfoByKey.Count);
        foreach (var pair in potions.TargetInfoByKey)
        {
            Key(pair.Key);
            writer.Write(pair.Value.Rarity);
            writer.Write(pair.Value.TargetIndex);
        }
        writer.Write(potions.TargetCount);

        writer.Write(matcher.Conditions.Length);
        foreach (NormalCombatRewardSearchCondition condition in matcher.Conditions)
        {
            writer.Write(condition.BattleOrdinal);
            Filter(condition.Cards);
            writer.Write((int)condition.PotionRequirement);
            Filter(condition.Potions);
            OptionalInteger(condition.MinimumGold);
            OptionalInteger(condition.MaximumGold);
        }
        writer.Write(matcher.ModernCards.Length);
        foreach (ModernCardConstraint constraint in matcher.ModernCards)
        {
            Key(constraint.Target);
            OptionalInteger(constraint.OrderedBattleOrdinal);
        }
        writer.Write(matcher.ModernPotions.Length);
        foreach (ModernPotionConstraint constraint in matcher.ModernPotions)
        {
            writer.Write((int)constraint.Slot.Requirement);
            writer.Write(constraint.Slot.PotionKey.HasValue);
            if (constraint.Slot.PotionKey is { } key) Key(key);
            OptionalInteger(constraint.OrderedBattleOrdinal);
        }
        Indexes(matcher.CardTargetIndex);
        Indexes(matcher.PotionTargetIndex);
        writer.Write(matcher.AnyConditionBitByIndex.Count);
        foreach (var pair in matcher.AnyConditionBitByIndex)
        {
            writer.Write(pair.Key);
            writer.Write(pair.Value);
        }
        byte[] anyMask = matcher.RequiredAnyConditionMask.ToByteArray();
        writer.Write(anyMask.Length);
        writer.Write(anyMask);
        writer.Write(matcher.MaximumBattleOrdinal);
        writer.Write(matcher.UsesCards);
        writer.Write(matcher.UsesPotions);
        writer.Write(matcher.UsesGold);
        writer.Write(matcher.ModernCardUnorderedCount);
        writer.Write(matcher.ModernPotionUnorderedCount);
        writer.Write(matcher.ModernCardWindowCount);
        writer.Write(matcher.ModernPotionWindowCount);
        writer.Flush();

        // Unusually large mod pools remain correctly priced without retaining
        // an unbounded cache key. Both key size and entry count bound memory.
        return buffer.Length <= MaximumProfileProbabilityKeyBytes
            ? Convert.ToBase64String(buffer.GetBuffer(), 0, checked((int)buffer.Length))
            : null;

        void Key(ModelKey key)
        {
            Text(key.Category);
            Text(key.Entry);
        }
        void Text(string text)
        {
            writer.Write(text.Length);
            foreach (char value in text) writer.Write((ushort)value);
        }
        void Keys(IReadOnlyList<ModelKey> keys)
        {
            writer.Write(keys.Count);
            foreach (ModelKey key in keys) Key(key);
        }
        void Integers(int[] values)
        {
            writer.Write(values.Length);
            foreach (int value in values) writer.Write(value);
        }
        void Indexes(IReadOnlyDictionary<ModelKey, int> indexes)
        {
            writer.Write(indexes.Count);
            foreach (var pair in indexes)
            {
                Key(pair.Key);
                writer.Write(pair.Value);
            }
        }
        void Filter(ModelKeySetFilter filter)
        {
            Keys(filter.Any);
            Keys(filter.All);
            Keys(filter.Ban);
        }
        void OptionalInteger(int? value)
        {
            writer.Write(value.HasValue);
            if (value.HasValue) writer.Write(value.Value);
        }
    }
}
