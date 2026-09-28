using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Controls.Pickers;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

internal enum NeowCandidatePoolKind
{
    CharacterCards,
    RareCharacterCards,
    CommonCharacterCards,
    UncommonCharacterCards,
    ColorlessCards,
    OtherCharacterCards,
    TransformCards,
    NewLeafTransformCards,
    OrdinaryRelics,
    BonesNeowRelics,
    Potions,
    Curses
}

internal sealed record NeowSearchUiCatalog(
    RuntimeProfileId ProfileId,
    ModelKey CharacterKey,
    IReadOnlyList<ModelKey> RouteRelics,
    IReadOnlyList<ModelKey> CharacterCards,
    IReadOnlyList<ModelKey> RareCharacterCards,
    IReadOnlyList<ModelKey> CommonCharacterCards,
    IReadOnlyList<ModelKey> UncommonCharacterCards,
    IReadOnlyList<ModelKey> ColorlessCards,
    IReadOnlyList<ModelKey> OtherCharacterCards,
    IReadOnlyList<ModelKey> TransformCards,
    IReadOnlyList<ModelKey> NewLeafTransformCards,
    IReadOnlyList<ModelKey> OrdinaryRelics,
    IReadOnlyDictionary<ModelKey, EffectRelicRarity> OrdinaryRelicRarities,
    IReadOnlyList<ModelKey> BonesNeowRelics,
    IReadOnlyList<ModelKey> Potions,
    IReadOnlyDictionary<ModelKey, EffectPotionRarity> PotionRarities,
    IReadOnlyList<ModelKey> Curses,
    ModelKey? ClawKey,
    bool EffectAuthorityExact,
    string EvidenceCode)
{
    public IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> CardPickerMetadata { get; init; } =
        new Dictionary<ModelKey, CardPickerCandidateMetadata>(ModelKeyComparer.Instance);

    public CardPickerContext CreateCardPickerContext(
        IReadOnlyList<ModelKey> allowed,
        bool allowCharacterFilter,
        string sourceId) => new(
            allowed,
            CardPickerMetadata,
            allowCharacterFilter,
            sourceId);

    public static NeowSearchUiCatalog Empty(RuntimeProfileId profileId, ModelKey characterKey, string evidenceCode) =>
        new(
            profileId,
            characterKey,
            ApplicableSinglePlayerRoutes(profileId),
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            new Dictionary<ModelKey, EffectRelicRarity>(ModelKeyComparer.Instance),
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            new Dictionary<ModelKey, EffectPotionRarity>(ModelKeyComparer.Instance),
            Array.Empty<ModelKey>(),
            null,
            false,
            evidenceCode);

    public static NeowSearchUiCatalog FromAuthority(
        RuntimeProfileId profileId,
        ModelKey characterKey,
        NeowEffectAuthoritySnapshot? authority)
    {
        if (authority is null)
        {
            return Empty(profileId, characterKey, "neow-ui-effect-authority-missing");
        }

        static ModelKey[] Keys(IEnumerable<ModelKey> keys) => keys
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .OrderBy(key => key.Serialized, StringComparer.Ordinal)
            .ToArray();

        // Picker presentation is allowed to degrade to captured Partial authority.
        // Mod hooks can make a pool non-Exact without making every captured
        // vanilla entry disappear. Exactness remains a Search/Probability
        // authority fact; it must not be used as an all-or-nothing UI visibility
        // gate for an otherwise available read-only snapshot.
        IReadOnlyList<NeowEffectCardSnapshot> character =
            authority.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>();
        IReadOnlyList<NeowEffectCardSnapshot> colorless =
            authority.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>();

        // These three picker pools represent outputs produced through
        // NeowRewardGenerator.CreateCard. That production path can only select
        // Common/Uncommon/Rare cards. Keep TransformCards separate because its
        // legal domain intentionally includes starter/basic cards.
        NeowEffectCardSnapshot[] characterRewardCandidates = character
            .Where(IsRegularRewardRarity)
            .ToArray();
        NeowEffectCardSnapshot[] colorlessRewardCandidates = colorless
            .Where(IsRegularRewardRarity)
            .ToArray();
        ModelKey[] otherCards = Keys(
            (authority.OtherCharacterPools ?? Array.Empty<CharacterCardPoolSnapshot>())
                .SelectMany(pool => pool.Cards)
                .Where(IsRegularRewardRarity)
                .Select(card => card.CardKey));
        // Leafy Poultice uses the ordinary transform path. Production transform
        // semantics exclude Basic and Ancient rarity cards, so the authoring picker
        // must not expose those impossible outputs either.
        ModelKey[] transforms = Keys(
            (authority.TransformPool ?? Array.Empty<NeowEffectCardSnapshot>())
                .Where(card => card.Rarity is not EffectCardRarity.Basic and not EffectCardRarity.Ancient)
                .Select(card => card.CardKey));
        NeowEffectCardSnapshot? newLeafSource = authority.HasExactDeck && authority.OrderedDeck is not null
            ? NewLeafNormalizedSourcePolicy.Select(authority.OrderedDeck, authority.CharacterStrikeKey)
            : null;
        ModelKey[] newLeafTransforms = authority.TransformPool is not null && newLeafSource is not null
            ? Keys(NewLeafNormalizedSourcePolicy
                .BuildTransformCandidates(authority.TransformPool, newLeafSource)
                .Select(card => card.CardKey))
            : Array.Empty<ModelKey>();
        IReadOnlyList<NeowEffectRelicSnapshot> ordinaryRelicSnapshots =
            (authority.OrderedRelicBag ?? Array.Empty<NeowEffectRelicSnapshot>())
                .Where(relic => relic.Rarity != EffectRelicRarity.Shop)
                .ToArray();
        ModelKey[] ordinaryRelics = Keys(ordinaryRelicSnapshots.Select(relic => relic.RelicKey));
        Dictionary<ModelKey, EffectRelicRarity> ordinaryRelicRarities = ordinaryRelicSnapshots
            .Where(relic => relic.RelicKey.IsValid)
            .GroupBy(relic => relic.RelicKey, ModelKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.First().Rarity, ModelKeyComparer.Instance);
        // UI discovery consumes captured runtime identities even when behavioral
        // authority is Partial. Search/Probability still consult the snapshot's
        // exactness flags before claiming semantic authority.
        ModelKey[] bonesRelics = Keys(authority.BonesEligibleRelics ?? Array.Empty<ModelKey>());
        IReadOnlyList<NeowEffectPotionSnapshot> potionSnapshots =
            authority.PotionPool ?? Array.Empty<NeowEffectPotionSnapshot>();
        ModelKey[] potions = Keys(potionSnapshots.Select(potion => potion.PotionKey));
        Dictionary<ModelKey, EffectPotionRarity> potionRarities = potionSnapshots
            .Where(potion => potion.PotionKey.IsValid)
            .GroupBy(potion => potion.PotionKey, ModelKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.First().Rarity, ModelKeyComparer.Instance);
        ModelKey[] curses = Keys(authority.GeneratedCursePool ?? Array.Empty<ModelKey>());

        return new NeowSearchUiCatalog(
            profileId,
            characterKey,
            ApplicableSinglePlayerRoutes(profileId),
            Keys(characterRewardCandidates.Select(card => card.CardKey)),
            Keys(characterRewardCandidates.Where(card => card.Rarity == EffectCardRarity.Rare).Select(card => card.CardKey)),
            Keys(characterRewardCandidates.Where(card => card.Rarity == EffectCardRarity.Common).Select(card => card.CardKey)),
            Keys(characterRewardCandidates.Where(card => card.Rarity == EffectCardRarity.Uncommon).Select(card => card.CardKey)),
            Keys(colorlessRewardCandidates.Select(card => card.CardKey)),
            otherCards,
            transforms,
            newLeafTransforms,
            ordinaryRelics,
            ordinaryRelicRarities,
            bonesRelics,
            potions,
            potionRarities,
            curses,
            authority.HasExactCharacterRewardPool ? authority.ClawKey : null,
            authority.HasExactFoundation,
            authority.SnapshotFingerprint)
        {
            CardPickerMetadata = BuildCardPickerMetadata(authority, characterKey)
        };
    }

    public EffectRelicRarity RelicRarity(ModelKey key) =>
        OrdinaryRelicRarities.TryGetValue(key, out EffectRelicRarity rarity)
            ? rarity
            : EffectRelicRarity.Other;

    public EffectPotionRarity PotionRarity(ModelKey key) =>
        PotionRarities.TryGetValue(key, out EffectPotionRarity rarity)
            ? rarity
            : EffectPotionRarity.Other;

    public IReadOnlyList<ModelKey> Candidates(NeowCandidatePoolKind kind) => kind switch
    {
        NeowCandidatePoolKind.CharacterCards => CharacterCards,
        NeowCandidatePoolKind.RareCharacterCards => RareCharacterCards,
        NeowCandidatePoolKind.CommonCharacterCards => CommonCharacterCards,
        NeowCandidatePoolKind.UncommonCharacterCards => UncommonCharacterCards,
        NeowCandidatePoolKind.ColorlessCards => ColorlessCards,
        NeowCandidatePoolKind.OtherCharacterCards => OtherCharacterCards,
        NeowCandidatePoolKind.TransformCards => TransformCards,
        NeowCandidatePoolKind.NewLeafTransformCards => NewLeafTransformCards,
        NeowCandidatePoolKind.OrdinaryRelics => OrdinaryRelics,
        NeowCandidatePoolKind.BonesNeowRelics => BonesNeowRelics,
        NeowCandidatePoolKind.Potions => Potions,
        NeowCandidatePoolKind.Curses => Curses,
        _ => Array.Empty<ModelKey>()
    };



    private static bool IsRegularRewardRarity(NeowEffectCardSnapshot card) =>
        card.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare;

    private static IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> BuildCardPickerMetadata(
        NeowEffectAuthoritySnapshot authority,
        ModelKey currentCharacter)
    {
        var cards = new Dictionary<ModelKey, (NeowEffectCardSnapshot Card, HashSet<ModelKey> Characters)>(ModelKeyComparer.Instance);

        static void Add(
            Dictionary<ModelKey, (NeowEffectCardSnapshot Card, HashSet<ModelKey> Characters)> output,
            IEnumerable<NeowEffectCardSnapshot> source,
            ModelKey? character)
        {
            foreach (NeowEffectCardSnapshot card in source.Where(card => card.CardKey.IsValid))
            {
                if (!output.TryGetValue(card.CardKey, out var existing))
                {
                    existing = (card, new HashSet<ModelKey>(ModelKeyComparer.Instance));
                }
                if (character.HasValue && character.Value.IsValid) existing.Characters.Add(character.Value);
                output[card.CardKey] = existing;
            }
        }

        Add(cards, authority.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>(), currentCharacter);
        Add(cards, authority.TransformPool ?? Array.Empty<NeowEffectCardSnapshot>(), currentCharacter);
        Add(cards, authority.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>(), null);
        foreach (CharacterCardPoolSnapshot pool in authority.OtherCharacterPools ?? Array.Empty<CharacterCardPoolSnapshot>())
        {
            Add(cards, pool.Cards, pool.CharacterKey);
        }

        return cards.ToDictionary(
            pair => pair.Key,
            pair => new CardPickerCandidateMetadata(
                pair.Key,
                pair.Value.Characters.OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray(),
                pair.Value.Card.Rarity,
                pair.Value.Card.CardType),
            ModelKeyComparer.Instance);
    }

    private static IReadOnlyList<ModelKey> ApplicableSinglePlayerRoutes(RuntimeProfileId profileId) =>
        NeowEffectCoverageRegistry.GetApplicableKeys(profileId)
            .Where(key => !NeowEffectCoverageRegistry.TryGet(key, out NeowEffectCoverageEntry entry) ||
                          entry.Family != NeowEffectFamily.MultiplayerOnly)
            .ToArray();
}
