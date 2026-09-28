using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Controls.Pickers;

namespace RolltheSpire2.Ui.Pages.Search.Shop;

internal sealed record ShopColorlessSearchUiCatalog(
    RuntimeProfileId ProfileId,
    IReadOnlyList<ModelKey> UncommonCandidates,
    IReadOnlyList<ModelKey> RareCandidates,
    IReadOnlyDictionary<ModelKey, RelicPickerCategory> Categories,
    bool AuthorityExact,
    string EvidenceCode)
{
    // Authoring can display captured identities without claiming that a Mod's
    // merchant hooks or inventory RNG consumption are supported by Exact.
    public bool CatalogAvailable => UncommonCandidates.Count > 0 || RareCandidates.Count > 0;

    public IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> CardPickerMetadata { get; init; } =
        new Dictionary<ModelKey, CardPickerCandidateMetadata>(ModelKeyComparer.Instance);

    public CardPickerContext CreateCardPickerContext(IReadOnlyList<ModelKey> allowed, string sourceId) => new(
        allowed,
        CardPickerMetadata,
        AllowCharacterFilter: false,
        SourceId: sourceId);

    public static ShopColorlessSearchUiCatalog Empty(RuntimeProfileId profileId, string evidenceCode) => new(
        profileId,
        Array.Empty<ModelKey>(),
        Array.Empty<ModelKey>(),
        new Dictionary<ModelKey, RelicPickerCategory>(ModelKeyComparer.Instance),
        false,
        evidenceCode);

    public static ShopColorlessSearchUiCatalog FromAuthority(
        RuntimeProfileId profileId,
        NeowEffectAuthoritySnapshot? authority)
    {
        if (authority is null)
            return Empty(profileId, "shop-colorless-ui-effect-authority-missing");

        IReadOnlyList<NeowEffectCardSnapshot> ordered = authority.MerchantColorlessOrderedPool ??
            authority.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>();
        ModelKey[] uncommon = ordered
            .Where(card => card.Rarity == EffectCardRarity.Uncommon)
            .OrderBy(card => card.PoolOrder)
            .Select(card => card.CardKey)
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        ModelKey[] rare = ordered
            .Where(card => card.Rarity == EffectCardRarity.Rare)
            .OrderBy(card => card.PoolOrder)
            .Select(card => card.CardKey)
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();

        Dictionary<ModelKey, RelicPickerCategory> categories = uncommon
            .Select(key => (Key: key, Category: RelicPickerCategory.Uncommon))
            .Concat(rare.Select(key => (Key: key, Category: RelicPickerCategory.Rare)))
            .GroupBy(item => item.Key, ModelKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.First().Category, ModelKeyComparer.Instance);
        Dictionary<ModelKey, CardPickerCandidateMetadata> metadata = ordered
            .Where(card => uncommon.Contains(card.CardKey, ModelKeyComparer.Instance) ||
                           rare.Contains(card.CardKey, ModelKeyComparer.Instance))
            .GroupBy(card => card.CardKey, ModelKeyComparer.Instance)
            .ToDictionary(
                group => group.Key,
                group => new CardPickerCandidateMetadata(
                    group.Key,
                    Array.Empty<ModelKey>(),
                    group.First().Rarity,
                    group.First().CardType)
                {
                    IsColorlessPoolMember = true
                },
                ModelKeyComparer.Instance);

        bool exact = profileId == RuntimeProfileId.Beta111 &&
                     authority.HasExactMerchantColorlessV1Inputs &&
                     uncommon.Length > 0 && rare.Length > 0;
        return new ShopColorlessSearchUiCatalog(
            profileId,
            uncommon,
            rare,
            categories,
            exact,
            exact ? "shop-colorless-ui-beta111-runtime-authority-exact" : "shop-colorless-ui-authority-partial")
        {
            CardPickerMetadata = metadata
        };
    }
}
