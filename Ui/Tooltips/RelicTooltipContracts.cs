using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Tooltips;

internal sealed record RelicTooltipSnapshot(
    ModelKey RelicKey,
    string Title,
    string Description,
    bool HasOfficialDescription,
    string EvidenceCode);

internal interface IRelicTooltipResolver
{
    RelicTooltipSnapshot Resolve(ModelKey relicKey, string fallbackTitle);
}
