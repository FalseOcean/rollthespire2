using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Pages.Search.Relic;

internal enum RelicSequenceUiMatchMode
{
    Appears,
    Excluded
}

internal sealed record RelicSequenceUiCondition(
    Guid Id,
    RelicSequenceKind Lane,
    SearchSequenceRangeMode RangeMode,
    int RangeValue,
    ModelKey RelicKey,
    RelicSequenceUiMatchMode MatchMode)
{
    public string Serialize()
    {
        string lane = Lane.ToString().ToUpperInvariant();
        string range = RangeMode == SearchSequenceRangeMode.FirstN ? "FIRST" : "SLOT";
        string mode = MatchMode == RelicSequenceUiMatchMode.Appears ? "ANY" : "BAN";
        return $"{lane} {range} {RangeValue} {mode} {RelicKey.Serialized}";
    }
}
