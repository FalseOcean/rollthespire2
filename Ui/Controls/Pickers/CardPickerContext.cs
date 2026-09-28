using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Controls.Pickers;

/// <summary>
/// Main-thread-only presentation metadata for a card picker invocation. The
/// legal candidate set is supplied by production authority; this context only
/// exposes filter dimensions that are already present in that set.
/// </summary>
internal sealed record CardPickerCandidateMetadata(
    ModelKey CardKey,
    IReadOnlyList<ModelKey> CharacterKeys,
    EffectCardRarity Rarity,
    EffectCardType CardType)
{
    // Missing character provenance does not establish colorless membership.
    public bool IsColorlessPoolMember { get; init; }
}

internal sealed record CardPickerContext(
    IReadOnlyList<ModelKey> AllowedCardModelKeys,
    IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> CandidateMetadata,
    bool AllowCharacterFilter,
    string SourceId)
{
    public IReadOnlyList<ModelKey> AvailableCharacterKeys => AllowedCardModelKeys
        .Where(CandidateMetadata.ContainsKey)
        .SelectMany(key => CandidateMetadata[key].CharacterKeys)
        .Where(key => key.IsValid)
        .Distinct(ModelKeyComparer.Instance)
        .OrderBy(key => key.Serialized, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<EffectCardRarity> AvailableRarities => AllowedCardModelKeys
        .Where(key => CandidateMetadata.ContainsKey(key))
        .Select(key => CandidateMetadata[key].Rarity)
        .Where(rarity => rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
        .Distinct()
        .OrderBy(rarity => rarity)
        .ToArray();

    public IReadOnlyList<EffectCardType> AvailableTypes => AllowedCardModelKeys
        .Where(key => CandidateMetadata.ContainsKey(key))
        .Select(key => CandidateMetadata[key].CardType)
        .Where(type => type is EffectCardType.Attack or EffectCardType.Skill or EffectCardType.Power)
        .Distinct()
        .OrderBy(type => type)
        .ToArray();

    public bool IsAllowed(ModelKey key) =>
        key.IsValid && AllowedCardModelKeys.Contains(key, ModelKeyComparer.Instance);

    public bool IsColorlessPoolMember(ModelKey key) =>
        TryGet(key, out CardPickerCandidateMetadata metadata) && metadata.IsColorlessPoolMember &&
        metadata.Rarity is not (EffectCardRarity.Curse or EffectCardRarity.Ancient);

    public bool TryGet(ModelKey key, out CardPickerCandidateMetadata metadata)
    {
        if (!IsAllowed(key))
        {
            metadata = null!;
            return false;
        }

        bool found = CandidateMetadata.TryGetValue(key, out CardPickerCandidateMetadata? resolved);
        metadata = resolved!;
        return found;
    }
}
