using Godot;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Ui.Icons;

internal interface ICardPickerFilterIconProvider
{
    Texture2D? ResolveRarity(EffectCardRarity rarity);
    Texture2D? ResolveType(EffectCardType type);
}
