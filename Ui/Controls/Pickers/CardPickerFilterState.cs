using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Controls.Pickers;

// Shared presentation-only state extracted from RelicPickerPanel's card filters.
internal sealed class CardPickerFilterState
{
    public HashSet<ModelKey> Characters { get; } = new(ModelKeyComparer.Instance);
    public HashSet<EffectCardRarity> Rarities { get; } = new();
    public HashSet<EffectCardType> Types { get; } = new();
    public bool AllCharacters { get; set; } = true;

    public void SelectAllCharacters()
    {
        AllCharacters = true;
        Characters.Clear();
    }

    public void ToggleCharacter(ModelKey character)
    {
        bool alreadySelected = !AllCharacters && Characters.Count == 1 && Characters.Contains(character);
        Characters.Clear();
        AllCharacters = alreadySelected;
        if (!alreadySelected) Characters.Add(character);
    }

    public void ToggleRarity(EffectCardRarity rarity)
    {
        bool alreadySelected = Rarities.Count == 1 && Rarities.Contains(rarity);
        Rarities.Clear();
        if (!alreadySelected) Rarities.Add(rarity);
    }

    public void ToggleType(EffectCardType type)
    {
        bool alreadySelected = Types.Count == 1 && Types.Contains(type);
        Types.Clear();
        if (!alreadySelected) Types.Add(type);
    }

    public bool Matches(CardPickerContext context, ModelKey key)
    {
        if (!context.IsAllowed(key)) return false;
        if (!context.TryGet(key, out CardPickerCandidateMetadata metadata))
            return Characters.Count == 0 && Rarities.Count == 0 && Types.Count == 0;
        bool characterMatches = AllCharacters || Characters.Count == 0 || metadata.CharacterKeys.Any(Characters.Contains);
        bool rarityMatches = Rarities.Count == 0 || Rarities.Contains(metadata.Rarity);
        bool typeMatches = Types.Count == 0 || Types.Contains(metadata.CardType);
        return characterMatches && rarityMatches && typeMatches;
    }
}
