using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Controls.Pickers;

namespace RolltheSpire2.Ui.Pages.Search.CombatReward;

internal sealed record CombatRewardSearchUiCatalog(
    RuntimeProfileId ProfileId,
    IReadOnlyList<ModelKey> CardCandidates,
    IReadOnlyList<ModelKey> PotionCandidates,
    IReadOnlyDictionary<ModelKey, RelicPickerCategory> CardCategories,
    IReadOnlyDictionary<ModelKey, RelicPickerCategory> PotionCategories,
    bool CardCatalogAvailable,
    bool PotionCatalogAvailable,
    string EvidenceCode)
{
    public IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> CardPickerMetadata { get; init; } =
        new Dictionary<ModelKey, CardPickerCandidateMetadata>(ModelKeyComparer.Instance);

    public CardPickerContext CreateCardPickerContext(string sourceId) => new(
        CardCandidates,
        CardPickerMetadata,
        AllowCharacterFilter: false,
        SourceId: sourceId);

    public static CombatRewardSearchUiCatalog Empty(
        RuntimeProfileId profileId,
        string evidenceCode) => new(
            profileId,
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            new Dictionary<ModelKey, RelicPickerCategory>(ModelKeyComparer.Instance),
            new Dictionary<ModelKey, RelicPickerCategory>(ModelKeyComparer.Instance),
            false,
            false,
            evidenceCode);

    public static CombatRewardSearchUiCatalog FromAuthority(
        RuntimeProfileId profileId,
        NeowEffectAuthoritySnapshot? authority)
    {
        if (authority is null)
        {
            return Empty(profileId, "combat-reward-ui-effect-authority-missing");
        }

        ModelKey[] cards = (authority.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>())
            .Where(card => !card.IsMultiplayerOnly)
            .Where(card => card.EligibleForPostCombatRewardByPoolMembership)
            .Where(card => card.IsUnlockedInCapturedPool)
            .Where(card => card.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
            .OrderBy(card => card.PoolOrder)
            .Select(card => card.CardKey)
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();

        ModelKey[] potions = (authority.PotionPool ?? Array.Empty<NeowEffectPotionSnapshot>())
            .Where(potion => !potion.IsMultiplayerOnly)
            .Where(potion => potion.Rarity is EffectPotionRarity.Common or EffectPotionRarity.Uncommon or EffectPotionRarity.Rare)
            .OrderBy(potion => potion.PoolOrder)
            .Select(potion => potion.PotionKey)
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();

        Dictionary<ModelKey, RelicPickerCategory> cardCategories =
            (authority.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>())
            .Where(card => cards.Contains(card.CardKey, ModelKeyComparer.Instance))
            .GroupBy(card => card.CardKey, ModelKeyComparer.Instance)
            .ToDictionary(
                group => group.Key,
                group => CardCategory(group.First().Rarity),
                ModelKeyComparer.Instance);

        Dictionary<ModelKey, RelicPickerCategory> potionCategories =
            (authority.PotionPool ?? Array.Empty<NeowEffectPotionSnapshot>())
            .Where(potion => potions.Contains(potion.PotionKey, ModelKeyComparer.Instance))
            .GroupBy(potion => potion.PotionKey, ModelKeyComparer.Instance)
            .ToDictionary(
                group => group.Key,
                group => PotionCategory(group.First().Rarity),
                ModelKeyComparer.Instance);

        bool cardAvailable = authority.CharacterRewardPool is not null && cards.Length > 0;
        bool potionAvailable = authority.PotionPool is not null && potions.Length > 0;
        string precision = authority.CharacterRewardPoolExact && authority.PotionPoolExact
            ? "exact"
            : "partial";

        return new CombatRewardSearchUiCatalog(
            profileId,
            cards,
            potions,
            cardCategories,
            potionCategories,
            cardAvailable,
            potionAvailable,
            $"combat-reward-ui-runtime-catalog:{precision}")
        {
            CardPickerMetadata = (authority.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>())
                .Where(card => cards.Contains(card.CardKey, ModelKeyComparer.Instance))
                .GroupBy(card => card.CardKey, ModelKeyComparer.Instance)
                .ToDictionary(
                    group => group.Key,
                    group => new CardPickerCandidateMetadata(
                        group.Key,
                        Array.Empty<ModelKey>(),
                        group.First().Rarity,
                        group.First().CardType),
                    ModelKeyComparer.Instance)
        };
    }

    private static RelicPickerCategory CardCategory(EffectCardRarity rarity) => rarity switch
    {
        EffectCardRarity.Common => RelicPickerCategory.Common,
        EffectCardRarity.Uncommon => RelicPickerCategory.Uncommon,
        EffectCardRarity.Rare => RelicPickerCategory.Rare,
        _ => RelicPickerCategory.Other
    };

    private static RelicPickerCategory PotionCategory(EffectPotionRarity rarity) => rarity switch
    {
        EffectPotionRarity.Common => RelicPickerCategory.Common,
        EffectPotionRarity.Uncommon => RelicPickerCategory.Uncommon,
        EffectPotionRarity.Rare => RelicPickerCategory.Rare,
        _ => RelicPickerCategory.Other
    };
}
