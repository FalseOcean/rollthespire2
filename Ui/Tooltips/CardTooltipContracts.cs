using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Tooltips;

/// <summary>
/// Pure presentation DTO produced on the Godot main thread from the canonical
/// runtime CardModel. No CardModel, LocString, HoverTip, Texture or Control may
/// cross this contract.
/// </summary>
internal sealed record CardTooltipSnapshot(
    ModelKey CardKey,
    string DisplayName,
    string PoolOrCharacterName,
    string RarityName,
    string CardTypeName,
    string RulesText,
    bool IsRulesTextExact,
    string EvidenceCode,
    string RulesTextFormat);

internal interface ICardTooltipResolver
{
    CardTooltipSnapshot Resolve(ModelKey cardKey, string fallbackTitle);
}
