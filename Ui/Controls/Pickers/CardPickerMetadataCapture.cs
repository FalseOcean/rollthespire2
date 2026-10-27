using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Ui.Controls.Pickers;

// Copies filter provenance from a caller-owned captured authority. It does not
// choose legal candidates; each source supplies its existing candidate list.
internal static class CardPickerMetadataCapture
{
    public static IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> FromAuthority(
        NeowEffectAuthoritySnapshot authority,
        ModelKey currentCharacter)
    {
        var cards = new Dictionary<ModelKey, (NeowEffectCardSnapshot Card, HashSet<ModelKey> Characters)>(ModelKeyComparer.Instance);
        HashSet<ModelKey> colorlessCards = (authority.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>())
            .Select(card => card.CardKey).ToHashSet(ModelKeyComparer.Instance);

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
                pair.Value.Card.CardType)
            {
                IsColorlessPoolMember = colorlessCards.Contains(pair.Key)
            },
            ModelKeyComparer.Instance);
    }

}
