using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Events;

/// <summary>
/// Source-audited event operations. Callers own event entry, the current local RNG,
/// alternative checkpoints and settlement; these rules never derive or reset a seed.
/// Numeric search backends compile these ordered facts rather than invoking this per seed.
/// </summary>
public static class Beta111EventRules
{
    public const int FakeMerchantOfferCount = 6;
    public const int ColorfulOfferCount = 3;
    public const int TrialCaseCount = 3;
    public const int TinkerOfferCount = 2;
    public const int TinkerRidersPerType = 3;

    public static IReadOnlyList<string> TinkerTypes { get; } = new[] { "ATTACK", "SKILL", "POWER" };
    public static IReadOnlyList<string> TinkerRiders { get; } = new[]
    {
        "SAPPING", "VIOLENCE", "CHOKING", "ENERGIZED", "WISDOM", "CHAOS",
        "EXPERTISE", "CURIOUS", "IMPROVEMENT"
    };

    private static readonly ModelKey[] AuthoredDolls =
    [
        new("RELIC", "DAUGHTER_OF_THE_WIND"), new("RELIC", "MR_STRUGGLES"), new("RELIC", "BING_BONG")
    ];

    public static ModelKey TrashHeapGrab(Xoshiro256StarStar rng) =>
        Beta111EventResultCatalog.TrashHeapGrabCards[rng.NextInt(Beta111EventResultCatalog.TrashHeapGrabCards.Count)];

    public static ModelKey TrashHeapDive(Xoshiro256StarStar rng) =>
        Beta111EventResultCatalog.TrashHeapDiveRelics[rng.NextInt(Beta111EventResultCatalog.TrashHeapDiveRelics.Count)];

    public static ModelKey[] FakeMerchantInventory(Xoshiro256StarStar rng)
    {
        var pool = Beta111EventResultCatalog.FakeMerchantRelics.ToArray();
        rng.UnstableShuffle(pool);
        return pool.Take(FakeMerchantOfferCount).ToArray();
    }

    public static ModelKey[] ColorfulCandidates(ModelKey owner, IEnumerable<ModelKey> unlockedCharacters)
    {
        var unlocked = new HashSet<ModelKey>(unlockedCharacters, ModelKeyComparer.Instance);
        return Beta111EventResultCatalog.ColorfulCharacterOrder
            .Where(key => key != owner && unlocked.Contains(key)).ToArray();
    }

    public static ModelKey[] ColorfulOffers(Xoshiro256StarStar rng, ModelKey owner,
        IEnumerable<ModelKey> unlockedCharacters)
    {
        var candidates = ColorfulCandidates(owner, unlockedCharacters).ToList();
        while (candidates.Count > ColorfulOfferCount)
            candidates.RemoveAt(rng.NextInt(candidates.Count));
        return candidates.ToArray();
    }

    // Trial's cosmetic entrant number uses Rng.Chaotic, not this event-local stream.
    public static int TrialCase(Xoshiro256StarStar rng) => rng.NextInt(TrialCaseCount);

    public static int[] TinkerTypeOffers(Xoshiro256StarStar rng)
    {
        int[] types = [0, 1, 2];
        rng.UnstableShuffle(types);
        return types.Take(TinkerOfferCount).ToArray();
    }

    // IDs follow the authored type-major order used by search's numeric ABI.
    public static int[] TinkerRiderOffers(Xoshiro256StarStar rng, int type)
    {
        if (type < 0 || type >= TinkerTypes.Count) throw new ArgumentOutOfRangeException(nameof(type));
        int[] riders = [type * 3, type * 3 + 1, type * 3 + 2];
        rng.UnstableShuffle(riders);
        return riders.Take(TinkerOfferCount).ToArray();
    }

    public static ModelKey RandomDoll(Xoshiro256StarStar rng) => AuthoredDolls[rng.NextInt(AuthoredDolls.Length)];

    public static ModelKey[] DollOffers(Xoshiro256StarStar rng, int count)
    {
        // Native StableShuffle sorts by RelicModel.CompareTo/ModelId before shuffling.
        var sorted = AuthoredDolls.OrderBy(key => key.Entry, StringComparer.Ordinal).ToArray();
        rng.UnstableShuffle(sorted);
        return sorted.Take(count).ToArray();
    }

    // Preserve NextFloat(min,max)'s double range arithmetic and float-to-decimal
    // conversion. Consumers truncate only after their own gold settlement hooks.
    public static decimal JungleGold(Xoshiro256StarStar rng, decimal baseAmount) =>
        baseAmount + (decimal)(float)(rng.NextDouble() * 30d - 15d);

    public static int DenseVegetationGold(Xoshiro256StarStar rng) => 61 + rng.NextInt(39);
    public static int LostWispGold(Xoshiro256StarStar rng) => 45 + rng.NextInt(31);
    public static int LuminousChoirGold(Xoshiro256StarStar rng) => 149 - rng.NextInt(50);
    public static int CrystalSphereGold(Xoshiro256StarStar rng) => 51 + rng.NextInt(49);
    public static int SunkenTreasurySmallGold(Xoshiro256StarStar rng) => 52 + rng.NextInt(16);
    public static int SunkenTreasuryLargeGold(Xoshiro256StarStar rng) => 303 + rng.NextInt(61);
    public static int SunkenStatueGold(Xoshiro256StarStar rng) => 101 + rng.NextInt(21);
    public static int ThisOrThatGold(Xoshiro256StarStar rng) => 41 + rng.NextInt(28);
    public static int WhisperingHollowGold(Xoshiro256StarStar rng) => 26 + rng.NextInt(19);
}
