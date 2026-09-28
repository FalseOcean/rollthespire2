using Godot;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Ui.Icons;

/// <summary>Loads only audited fixed picker textures through Godot's resource cache.</summary>
internal sealed class CardPickerFilterIconProvider : ICardPickerFilterIconProvider
{
    private const string CommonPath = "res://images/ui/reward_screen/reward_icon_card.png";
    private const string UncommonPath = "res://images/ui/reward_screen/reward_icon_uncommon.png";
    private const string RarePath = "res://images/ui/reward_screen/reward_icon_rare.png";
    private const string AttackPath = "res://images/packed/card_library/type_sort_attack.png";
    private const string SkillPath = "res://images/packed/card_library/type_sort_skill.png";
    private const string PowerPath = "res://images/packed/card_library/type_sort_power.png";

    private readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.Ordinal);

    public Texture2D? ResolveRarity(EffectCardRarity rarity) => rarity switch
    {
        EffectCardRarity.Common => Load(CommonPath),
        EffectCardRarity.Uncommon => Load(UncommonPath),
        EffectCardRarity.Rare => Load(RarePath),
        _ => null
    };

    public Texture2D? ResolveType(EffectCardType type) => type switch
    {
        EffectCardType.Attack => Load(AttackPath),
        EffectCardType.Skill => Load(SkillPath),
        EffectCardType.Power => Load(PowerPath),
        _ => null
    };

    private Texture2D? Load(string path)
    {
        if (_cache.TryGetValue(path, out Texture2D? texture)) return texture;
        try
        {
            texture = ResourceLoader.Exists(path)
                ? ResourceLoader.Load<Texture2D>(path, null, ResourceLoader.CacheMode.Reuse)
                : null;
        }
        catch
        {
            texture = null;
        }
        _cache[path] = texture;
        return texture;
    }
}
