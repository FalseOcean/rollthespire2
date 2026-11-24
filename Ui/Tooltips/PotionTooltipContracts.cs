using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Tooltips;

internal sealed record PotionTooltipSnapshot(
    ModelKey PotionKey,
    string Title,
    string Description,
    bool HasOfficialDescription,
    string EvidenceCode);

internal interface IPotionTooltipResolver
{
    PotionTooltipSnapshot Resolve(ModelKey potionKey, string fallbackTitle);
}
