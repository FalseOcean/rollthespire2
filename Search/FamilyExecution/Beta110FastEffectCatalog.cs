using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class Beta110FastDenseId
{
    public const ushort Invalid = ushort.MaxValue;
}

internal readonly record struct Beta110FastRewardRelicCapability(
    bool ContinuationSupported,
    bool InfluenceSupported,
    Beta110CombatRewardInfluenceFlags InfluenceFlags,
    byte AdditionalCardRewardCount,
    short FixedGoldAmount)
{
    public bool Exact => ContinuationSupported && InfluenceSupported;
}

internal readonly record struct Beta110FastOrdinaryRelic(
    ushort DenseId,
    EffectRelicRarity Rarity,
    NestedRelicEffectKind NestedEffectKind,
    Beta110FastRewardRelicCapability RewardCapability);

internal sealed record Beta110FastRelicBucket(ushort[] RelicIndexes);

internal sealed record Beta110FastCardPool(
    ushort[] Common,
    ushort[] Uncommon,
    ushort[] Rare)
{
    public int TotalCount => Common.Length + Uncommon.Length + Rare.Length;
}

internal sealed record Beta110FastPotionPool(
    ushort[] Common,
    ushort[] Uncommon,
    ushort[] Rare,
    ushort[] AllAllowed)
{
    public int TotalCount => AllAllowed.Length;
    public bool HasEveryRarity => Common.Length > 0 && Uncommon.Length > 0 && Rare.Length > 0;
    public bool HasAtLeastPerRarity(int count) =>
        Common.Length >= count && Uncommon.Length >= count && Rare.Length >= count;
}

/// <summary>
/// Immutable, data-driven structural-effect authority compiled on the main-thread
/// Search plan path. Dense IDs are never UI indexes and are never localized names.
/// The hot kernel reads only these arrays and scalar IDs.
/// </summary>
internal sealed record Beta110FastEffectCatalog(
    ModelKey[] DenseKeys,
    Beta110FastOrdinaryRelic[] OrdinaryRelics,
    int[] SharedRelicConsumeShuffleLengths,
    Beta110FastRelicBucket[] PlayerRelicBuckets,
    Beta110FastCardPool CharacterRewardPool,
    Beta110FastCardPool ColorlessRewardPool,
    Beta110FastCardPool[] OtherCharacterPools,
    ushort[] LeafyStrikeTransformPool,
    ushort[] LeafyDefendTransformPool,
    ushort[] NewLeafTransformPool,
    Beta110FastPotionPool PotionPool,
    Beta110FastCardPool CombatRewardCardPool,
    Beta110FastCardPool CombatRewardPowerPool,
    Beta110FastPotionPool CombatRewardPotionPool,
    Beta110FastRewardRelicCapability[] TopLevelRewardCapabilities,
    byte[] CardRarityByDenseId,
    ushort ClawId,
    ushort WhetstoneId,
    ushort WarPaintId,
    ushort CapsuleCircletId,
    ushort[] GeneratedCurseIds,
    bool RelicBagAuthorityExact,
    bool CharacterRewardAuthorityExact,
    bool CharacterCardAuthorityExact,
    bool ColorlessRewardAuthorityExact,
    bool OtherCharacterCardAuthorityExact,
    bool LeafyTransformAuthorityExact,
    bool NewLeafTransformAuthorityExact,
    bool PotionAuthorityExact,
    bool CombatRewardCardAuthorityExact,
    bool CombatRewardPotionAuthorityExact,
    bool CursePoolAuthorityExact,
    string Fingerprint)
{
    public bool TryGetDenseId(ModelKey key, out ushort id)
    {
        for (int index = 0; index < DenseKeys.Length; index++)
        {
            if (DenseKeys[index] == key)
            {
                id = checked((ushort)index);
                return true;
            }
        }
        id = Beta110FastDenseId.Invalid;
        return false;
    }

    public ModelKey KeyOf(ushort id) => id < DenseKeys.Length ? DenseKeys[id] : default;
}

internal static class Beta110FastEffectCatalogCompiler
{
    private const int MaximumDenseKeys = ushort.MaxValue;
    internal const int MaximumRelicBagEntries = 512;
    private const int MaximumOtherCharacterPools = 256;

    public static Beta110FastEffectCatalog Compile(
        NeowEffectAuthoritySnapshot? authority,
        IEnumerable<ModelKey> requestedKeys)
    {
        var dense = new DenseKeyBuilder();
        foreach (ModelKey key in requestedKeys.Where(key => key.IsValid)) dense.GetOrAdd(key);

        IReadOnlyList<NeowEffectRelicSnapshot> shared = authority?.SharedRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>();
        IReadOnlyList<NeowEffectRelicSnapshot> character = authority?.CharacterRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>();
        foreach (NeowEffectRelicSnapshot relic in shared.Concat(character)) dense.GetOrAdd(relic.RelicKey);
        foreach (NeowEffectCardSnapshot card in authority?.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>()) dense.GetOrAdd(card.CardKey);
        foreach (NeowEffectCardSnapshot card in authority?.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>()) dense.GetOrAdd(card.CardKey);
        foreach (NeowEffectCardSnapshot card in authority?.TransformPool ?? Array.Empty<NeowEffectCardSnapshot>()) dense.GetOrAdd(card.CardKey);
        foreach (NeowEffectPotionSnapshot potion in authority?.PotionPool ?? Array.Empty<NeowEffectPotionSnapshot>()) dense.GetOrAdd(potion.PotionKey);
        foreach (CharacterCardPoolSnapshot pool in authority?.OtherCharacterPools ?? Array.Empty<CharacterCardPoolSnapshot>())
        {
            foreach (NeowEffectCardSnapshot card in pool.Cards) dense.GetOrAdd(card.CardKey);
        }
        foreach (ModelKey curse in authority?.GeneratedCursePool ?? Array.Empty<ModelKey>()) dense.GetOrAdd(curse);
        if (authority?.ClawKey is { } claw && claw.IsValid) dense.GetOrAdd(claw);
        dense.GetOrAdd(BaseGameModelKeys.Cards.Claw);
        dense.GetOrAdd(BaseGameModelKeys.OrdinaryRelics.Whetstone);
        dense.GetOrAdd(BaseGameModelKeys.OrdinaryRelics.WarPaint);
        dense.GetOrAdd(BaseGameModelKeys.OrdinaryRelics.Circlet);

        int[] consumeLengths = GroupInSourceRarityOrder(shared)
            .Select(group => group.Count())
            .ToArray();

        var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        var playerRelics = new List<NeowEffectRelicSnapshot>();
        foreach (NeowEffectRelicSnapshot relic in shared.Concat(character))
        {
            if (relic.Rarity is not (EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare or EffectRelicRarity.Shop))
                continue;
            if (seen.Add(relic.RelicKey)) playerRelics.Add(relic);
        }

        Beta110FastOrdinaryRelic[] ordinary = playerRelics
            .Select(relic => new Beta110FastOrdinaryRelic(
                dense.GetOrAdd(relic.RelicKey),
                relic.Rarity,
                relic.NestedEffectKind,
                BuildRewardRelicCapability(RuntimeProfileId.Beta110, relic.RelicKey, nestedAcquisition: true)))
            .ToArray();
        Beta110FastRelicBucket[] relicBuckets = GroupInSourceRarityOrder(playerRelics)
            .Select(group => new Beta110FastRelicBucket(group
                .Select(relic => checked((ushort)playerRelics.FindIndex(candidate => candidate.RelicKey == relic.RelicKey)))
                .ToArray()))
            .ToArray();

        Beta110FastCardPool characterPool = BuildCardPool(
            authority?.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>(), dense);
        Beta110FastCardPool colorlessPool = BuildCardPool(
            authority?.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>(), dense);
        Beta110FastCardPool[] otherPools = (authority?.OtherCharacterPools ?? Array.Empty<CharacterCardPoolSnapshot>())
            .OrderBy(pool => pool.PoolOrder)
            .Select(pool => BuildCardPool(pool.Cards, dense))
            .ToArray();

        (ushort[] leafyStrikePool, ushort[] leafyDefendPool, ushort[] newLeafPool,
            bool leafyTransformExact, bool newLeafTransformExact) = BuildTransformPools(authority, dense);
        Beta110FastPotionPool potionPool = BuildPotionPool(
            authority?.PotionPool ?? Array.Empty<NeowEffectPotionSnapshot>(), dense);
        Beta110FastCardPool combatRewardCardPool = BuildCombatRewardCardPool(
            authority?.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>(), dense,
            requiredType: null);
        Beta110FastCardPool combatRewardPowerPool = BuildCombatRewardCardPool(
            authority?.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>(), dense,
            EffectCardType.Power);
        Beta110FastPotionPool combatRewardPotionPool = BuildCombatRewardPotionPool(
            authority?.PotionPool ?? Array.Empty<NeowEffectPotionSnapshot>(), dense);
        Beta110FastRewardRelicCapability[] topLevelRewardCapabilities = Enumerable.Range(0, 30)
            .Select(index => BuildRewardRelicCapability(
                RuntimeProfileId.Beta110,
                Beta110FastRelicCatalog.KeyOf(checked((byte)index)),
                nestedAcquisition: false))
            .ToArray();

        ushort clawId = authority?.ClawKey is { } clawKey && dense.TryGet(clawKey, out ushort capturedClaw)
            ? capturedClaw
            : dense.GetOrAdd(BaseGameModelKeys.Cards.Claw);
        ushort whetstoneId = dense.GetOrAdd(BaseGameModelKeys.OrdinaryRelics.Whetstone);
        ushort warPaintId = dense.GetOrAdd(BaseGameModelKeys.OrdinaryRelics.WarPaint);
        ushort capsuleCircletId = dense.GetOrAdd(BaseGameModelKeys.OrdinaryRelics.Circlet);
        ushort[] generatedCurseIds = (authority?.GeneratedCursePool ?? Array.Empty<ModelKey>())
            .Select(dense.GetOrAdd)
            .ToArray();

        ModelKey[] keys = dense.ToArray();
        bool keyCountSupported = !dense.Overflowed && keys.Length <= MaximumDenseKeys;
        var cardRarityByDenseId = new byte[keys.Length];
        foreach (NeowEffectCardSnapshot card in (authority?.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>())
                     .Concat(authority?.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>())
                     .Concat(authority?.TransformPool ?? Array.Empty<NeowEffectCardSnapshot>())
                     .Concat((authority?.OtherCharacterPools ?? Array.Empty<CharacterCardPoolSnapshot>()).SelectMany(pool => pool.Cards)))
        {
            if (!dense.TryGet(card.CardKey, out ushort id) || id >= cardRarityByDenseId.Length) continue;
            cardRarityByDenseId[id] = card.Rarity switch
            {
                EffectCardRarity.Common => 1,
                EffectCardRarity.Uncommon => 2,
                EffectCardRarity.Rare => 3,
                _ => cardRarityByDenseId[id]
            };
        }

        bool relicExact = authority?.HasExactRelicBagSourcePools == true &&
                          ordinary.Length is > 0 and <= MaximumRelicBagEntries &&
                          keyCountSupported;
        bool characterRewardExact = authority?.HasExactCharacterRewardPool == true &&
                                    characterPool.TotalCount > 0 &&
                                    keyCountSupported;
        bool characterExact = characterRewardExact &&
                              characterPool.Common.Length >= 4 &&
                              characterPool.Uncommon.Length >= 2;
        bool colorlessExact = authority?.HasExactColorlessRewardPool == true &&
                              colorlessPool.TotalCount > 0 &&
                              keyCountSupported;
        bool otherExact = authority?.HasExactOtherCharacterPools == true &&
                          otherPools.Length is >= 3 and <= MaximumOtherCharacterPools &&
                          otherPools.All(pool => pool.TotalCount > 0) &&
                          keyCountSupported;
        leafyTransformExact &= keyCountSupported;
        newLeafTransformExact &= keyCountSupported;
        bool potionExact = authority?.HasExactPotions == true &&
                           potionPool.HasEveryRarity &&
                           keyCountSupported;
        bool combatRewardCardExact = authority?.HasExactCharacterRewardPool == true &&
                                     combatRewardCardPool.TotalCount > 0 &&
                                     combatRewardPowerPool.TotalCount > 0 &&
                                     (authority?.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>())
                                         .Where(card => !card.IsMultiplayerOnly &&
                                                        card.EligibleForPostCombatRewardByPoolMembership &&
                                                        card.IsUnlockedInCapturedPool &&
                                                        card.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
                                         .All(card => card.CatalogProfileId == authority!.CapturedProfileId &&
                                                      RuntimeProfilePolicies.UsesBeta110SharedAlgorithms(card.CatalogProfileId) &&
                                                      !string.IsNullOrWhiteSpace(card.EligibilityAuthority)) &&
                                     keyCountSupported;
        bool combatRewardPotionExact = authority?.HasExactPotions == true &&
                                       combatRewardPotionPool.TotalCount > 0 &&
                                       keyCountSupported;
        bool curseExact = authority?.HasExactBonesPools == true &&
                          generatedCurseIds.Length > 0 &&
                          generatedCurseIds.All(id => id != Beta110FastDenseId.Invalid) &&
                          keyCountSupported;

        string fingerprint = Fingerprint(new[]
        {
            authority?.CatalogFingerprint ?? string.Empty,
            authority?.UnlockFingerprint ?? string.Empty,
            authority?.RelicBagFingerprint ?? string.Empty,
            relicExact.ToString(),
            characterRewardExact.ToString(),
            characterExact.ToString(),
            colorlessExact.ToString(),
            otherExact.ToString(),
            leafyTransformExact.ToString(),
            newLeafTransformExact.ToString(),
            potionExact.ToString(),
            combatRewardCardExact.ToString(),
            combatRewardPotionExact.ToString(),
            curseExact.ToString(),
            string.Join(",", generatedCurseIds),
            string.Join(",", keys.Select(key => key.Serialized)),
            string.Join(",", consumeLengths),
            string.Join(";", ordinary.Select((relic, index) => $"{index}:{relic.DenseId}:{relic.Rarity}:{relic.NestedEffectKind}")),
            string.Join(";", relicBuckets.Select(bucket => string.Join(",", bucket.RelicIndexes))),
            CardPoolDescriptor(characterPool),
            CardPoolDescriptor(colorlessPool),
            string.Join(";", otherPools.Select(CardPoolDescriptor)),
            "leafy-strike=" + string.Join(',', leafyStrikePool),
            "leafy-defend=" + string.Join(',', leafyDefendPool),
            "new-leaf-policy=" + NewLeafNormalizedSourcePolicy.PolicyId,
            "new-leaf=" + string.Join(',', newLeafPool),
            PotionPoolDescriptor(potionPool),
            "capsule-circlet=" + capsuleCircletId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "combat-reward-card=" + CardPoolDescriptor(combatRewardCardPool),
            "combat-reward-power=" + CardPoolDescriptor(combatRewardPowerPool),
            "combat-reward-potion=" + PotionPoolDescriptor(combatRewardPotionPool),
            "combat-reward-top-impact=" + string.Join(';', topLevelRewardCapabilities.Select((value, index) =>
                $"{index}:{value.ContinuationSupported}:{value.InfluenceSupported}:{value.InfluenceFlags}:{value.AdditionalCardRewardCount}:{value.FixedGoldAmount}"))
        });

        return new Beta110FastEffectCatalog(
            keys,
            ordinary,
            consumeLengths,
            relicBuckets,
            characterPool,
            colorlessPool,
            otherPools,
            leafyStrikePool,
            leafyDefendPool,
            newLeafPool,
            potionPool,
            combatRewardCardPool,
            combatRewardPowerPool,
            combatRewardPotionPool,
            topLevelRewardCapabilities,
            cardRarityByDenseId,
            clawId,
            whetstoneId,
            warPaintId,
            capsuleCircletId,
            generatedCurseIds,
            relicExact,
            characterRewardExact,
            characterExact,
            colorlessExact,
            otherExact,
            leafyTransformExact,
            newLeafTransformExact,
            potionExact,
            combatRewardCardExact,
            combatRewardPotionExact,
            curseExact,
            fingerprint);
    }

    private static (ushort[] LeafyStrike, ushort[] LeafyDefend, ushort[] NewLeaf, bool LeafyExact, bool NewLeafExact)
        BuildTransformPools(NeowEffectAuthoritySnapshot? authority, DenseKeyBuilder dense)
    {
        if (authority?.HasExactDeck != true || authority.HasExactTransformPool != true ||
            authority.OrderedDeck is null || authority.TransformPool is null)
        {
            return (Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<ushort>(), false, false);
        }

        NeowEffectCardSnapshot? strike = authority.OrderedDeck
            .Where(card => card.IsBasic && card.IsStrike)
            .OrderBy(card => card.PoolOrder)
            .FirstOrDefault();
        NeowEffectCardSnapshot? defend = authority.OrderedDeck
            .Where(card => card.IsBasic && card.IsDefend)
            .OrderBy(card => card.PoolOrder)
            .FirstOrDefault();
        ushort[] strikePool = strike is null ? Array.Empty<ushort>() : BuildTransformPool(authority.TransformPool, strike, dense);
        ushort[] defendPool = defend is null ? Array.Empty<ushort>() : BuildTransformPool(authority.TransformPool, defend, dense);
        bool leafyExact = strike is not null && defend is not null && strikePool.Length > 0 && defendPool.Length > 0;

        NeowEffectCardSnapshot? normalizedSource = NewLeafNormalizedSourcePolicy.Select(
            authority.OrderedDeck,
            authority.CharacterStrikeKey);
        if (normalizedSource is null)
            return (strikePool, defendPool, Array.Empty<ushort>(), leafyExact, false);

        ushort[] normalizedPool = NewLeafNormalizedSourcePolicy
            .BuildTransformCandidates(authority.TransformPool, normalizedSource)
            .Select(card => dense.GetOrAdd(card.CardKey))
            .ToArray();
        bool normalizedExact = normalizedPool.Length > 0;
        return (strikePool, defendPool, normalizedPool, leafyExact, normalizedExact);
    }

    private static ushort[] BuildTransformPool(
        IReadOnlyList<NeowEffectCardSnapshot> source,
        NeowEffectCardSnapshot target,
        DenseKeyBuilder dense) => source
        .Where(card => string.Equals(card.PoolId, target.PoolId, StringComparison.Ordinal))
        .Where(card => card.CardKey != target.CardKey)
        .Where(card => card.Rarity is not EffectCardRarity.Basic and not EffectCardRarity.Ancient)
        .OrderBy(card => card.PoolOrder)
        .Select(card => dense.GetOrAdd(card.CardKey))
        .ToArray();

    private static Beta110FastPotionPool BuildPotionPool(
        IEnumerable<NeowEffectPotionSnapshot> source,
        DenseKeyBuilder dense)
    {
        NeowEffectPotionSnapshot[] potions = source
            .OrderBy(potion => potion.PoolOrder)
            .ToArray();
        return new Beta110FastPotionPool(
            potions.Where(potion => potion.Rarity == EffectPotionRarity.Common).Select(potion => dense.GetOrAdd(potion.PotionKey)).ToArray(),
            potions.Where(potion => potion.Rarity == EffectPotionRarity.Uncommon).Select(potion => dense.GetOrAdd(potion.PotionKey)).ToArray(),
            potions.Where(potion => potion.Rarity == EffectPotionRarity.Rare).Select(potion => dense.GetOrAdd(potion.PotionKey)).ToArray(),
            potions.Select(potion => dense.GetOrAdd(potion.PotionKey)).ToArray());
    }

    private static Beta110FastCardPool BuildCardPool(
        IEnumerable<NeowEffectCardSnapshot> source,
        DenseKeyBuilder dense)
    {
        NeowEffectCardSnapshot[] cards = source
            .Where(card => card.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
            .OrderBy(card => card.PoolOrder)
            .ToArray();
        return new Beta110FastCardPool(
            cards.Where(card => card.Rarity == EffectCardRarity.Common).Select(card => dense.GetOrAdd(card.CardKey)).ToArray(),
            cards.Where(card => card.Rarity == EffectCardRarity.Uncommon).Select(card => dense.GetOrAdd(card.CardKey)).ToArray(),
            cards.Where(card => card.Rarity == EffectCardRarity.Rare).Select(card => dense.GetOrAdd(card.CardKey)).ToArray());
    }


    private static Beta110FastCardPool BuildCombatRewardCardPool(
        IEnumerable<NeowEffectCardSnapshot> source,
        DenseKeyBuilder dense,
        EffectCardType? requiredType)
    {
        NeowEffectCardSnapshot[] cards = source
            .Where(card => !card.IsMultiplayerOnly)
            .Where(card => card.EligibleForPostCombatRewardByPoolMembership)
            .Where(card => card.IsUnlockedInCapturedPool)
            .Where(card => card.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
            .Where(card => requiredType is null || card.CardType == requiredType.Value)
            .OrderBy(card => card.PoolOrder)
            .ToArray();
        return new Beta110FastCardPool(
            cards.Where(card => card.Rarity == EffectCardRarity.Common).Select(card => dense.GetOrAdd(card.CardKey)).ToArray(),
            cards.Where(card => card.Rarity == EffectCardRarity.Uncommon).Select(card => dense.GetOrAdd(card.CardKey)).ToArray(),
            cards.Where(card => card.Rarity == EffectCardRarity.Rare).Select(card => dense.GetOrAdd(card.CardKey)).ToArray());
    }

    private static Beta110FastPotionPool BuildCombatRewardPotionPool(
        IEnumerable<NeowEffectPotionSnapshot> source,
        DenseKeyBuilder dense)
    {
        NeowEffectPotionSnapshot[] potions = source
            .Where(potion => !potion.IsMultiplayerOnly)
            .Where(potion => potion.Rarity is EffectPotionRarity.Common or EffectPotionRarity.Uncommon or EffectPotionRarity.Rare)
            .OrderBy(potion => potion.PoolOrder)
            .ToArray();
        return new Beta110FastPotionPool(
            potions.Where(potion => potion.Rarity == EffectPotionRarity.Common).Select(potion => dense.GetOrAdd(potion.PotionKey)).ToArray(),
            potions.Where(potion => potion.Rarity == EffectPotionRarity.Uncommon).Select(potion => dense.GetOrAdd(potion.PotionKey)).ToArray(),
            potions.Where(potion => potion.Rarity == EffectPotionRarity.Rare).Select(potion => dense.GetOrAdd(potion.PotionKey)).ToArray(),
            potions.Select(potion => dense.GetOrAdd(potion.PotionKey)).ToArray());
    }

    private static Beta110FastRewardRelicCapability BuildRewardRelicCapability(
        RuntimeProfileId profileId,
        ModelKey relicKey,
        bool nestedAcquisition)
    {
        if (!VanillaRelicRewardEffects.TryGet(profileId, relicKey, out VanillaRelicRewardEffect capability))
        {
            return new Beta110FastRewardRelicCapability(
                false, false,
                Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation |
                Beta110CombatRewardInfluenceFlags.UnknownRewardImpact,
                0, 0);
        }

        bool continuationSupported = capability.NestedOnObtainPreservesRewardContinuation ||
                                     (!nestedAcquisition && IsTopLevelRewardsContinuationHandledByFast(relicKey));
        Beta110CombatRewardInfluenceFlags flags = Beta110CombatRewardInfluenceFlags.None;
        byte additionalRewards = 0;
        short fixedGold = 0;
        bool influenceSupported;
        if (OpeningCombatRewardImpactAdapterRegistry.TryResolve(profileId, relicKey, out OpeningCombatRewardImpactAdapterDescriptor descriptor))
        {
            influenceSupported = true;
            foreach (OpeningCombatRewardImpactOperation operation in descriptor.Operations)
            {
                switch (operation.Kind)
                {
                    case OpeningCombatRewardImpactOperationKind.ForcePotionReward:
                        flags |= Beta110CombatRewardInfluenceFlags.ForcePotionReward;
                        break;
                    case OpeningCombatRewardImpactOperationKind.AddCardReward:
                        additionalRewards = checked((byte)Math.Min(byte.MaxValue,
                            additionalRewards + Math.Max(0, operation.Amount)));
                        flags |= Beta110CombatRewardInfluenceFlags.PrayerWheelExtraReward;
                        break;
                    case OpeningCombatRewardImpactOperationKind.AddFixedGoldReward:
                        fixedGold = checked((short)Math.Clamp(fixedGold + Math.Max(0, operation.Amount), 0, short.MaxValue));
                        flags |= Beta110CombatRewardInfluenceFlags.AmethystAubergineFixedGold;
                        break;
                    case OpeningCombatRewardImpactOperationKind.AddPowerCardEveryOtherCombat:
                        flags |= Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard;
                        break;
                    case OpeningCombatRewardImpactOperationKind.ForceUpgradeCardType:
                    case OpeningCombatRewardImpactOperationKind.UpgradeNextCardRewards:
                        flags |= Beta110CombatRewardInfluenceFlags.DeterministicUpgradeOnly;
                        break;
                    case OpeningCombatRewardImpactOperationKind.EnchantFirstCardRewardWithGlam:
                        flags |= Beta110CombatRewardInfluenceFlags.DeterministicEnchantmentOnly;
                        break;
                    default:
                        influenceSupported = false;
                        flags |= Beta110CombatRewardInfluenceFlags.UnknownRewardImpact;
                        break;
                }
            }
        }
        else
        {
            influenceSupported = capability.IsHeldNeutralForNormalCombatReward;
            if (!influenceSupported)
                flags |= Beta110CombatRewardInfluenceFlags.UnknownRewardImpact;
        }
        if (!continuationSupported)
            flags |= Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation;
        return new Beta110FastRewardRelicCapability(
            continuationSupported,
            influenceSupported,
            flags,
            additionalRewards,
            fixedGold);
    }


    private static bool IsTopLevelRewardsContinuationHandledByFast(ModelKey relicKey) =>
        relicKey == BaseGameModelKeys.Relics.HeftyTablet ||
        relicKey == BaseGameModelKeys.Relics.LargeCapsule ||
        relicKey == BaseGameModelKeys.Relics.ArcaneScroll ||
        relicKey == BaseGameModelKeys.Relics.Kaleidoscope ||
        relicKey == BaseGameModelKeys.Relics.LeadPaperweight ||
        relicKey == BaseGameModelKeys.Relics.LostCoffer ||
        relicKey == BaseGameModelKeys.Relics.ScrollBoxes ||
        relicKey == BaseGameModelKeys.Relics.SmallCapsule;

    private static IEnumerable<IGrouping<string, NeowEffectRelicSnapshot>> GroupInSourceRarityOrder(
        IEnumerable<NeowEffectRelicSnapshot> source) => source.GroupBy(
        relic => string.IsNullOrWhiteSpace(relic.RarityCode) ? relic.Rarity.ToString() : relic.RarityCode,
        StringComparer.Ordinal);

    private static string CardPoolDescriptor(Beta110FastCardPool pool) =>
        $"c={string.Join(',', pool.Common)}|u={string.Join(',', pool.Uncommon)}|r={string.Join(',', pool.Rare)}";

    private static string PotionPoolDescriptor(Beta110FastPotionPool pool) =>
        $"c={string.Join(',', pool.Common)}|u={string.Join(',', pool.Uncommon)}|r={string.Join(',', pool.Rare)}|a={string.Join(',', pool.AllAllowed)}";

    private static string Fingerprint(IEnumerable<string> values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values)))).ToLowerInvariant();

    private sealed class DenseKeyBuilder
    {
        private readonly List<ModelKey> _keys = new();
        private readonly Dictionary<ModelKey, ushort> _ids = new(ModelKeyComparer.Instance);

        public bool Overflowed { get; private set; }

        public ushort GetOrAdd(ModelKey key)
        {
            if (!key.IsValid) return Beta110FastDenseId.Invalid;
            if (_ids.TryGetValue(key, out ushort existing)) return existing;
            if (_keys.Count >= MaximumDenseKeys)
            {
                Overflowed = true;
                return Beta110FastDenseId.Invalid;
            }
            ushort id = checked((ushort)_keys.Count);
            _keys.Add(key);
            _ids.Add(key, id);
            return id;
        }

        public bool TryGet(ModelKey key, out ushort id) => _ids.TryGetValue(key, out id);
        public ModelKey[] ToArray() => _keys.ToArray();
    }
}
