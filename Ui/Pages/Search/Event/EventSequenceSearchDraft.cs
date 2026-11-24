using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Pages.Search.Event;

internal enum EventSequenceUiMatchMode
{
    Appears,
    Excluded
}

internal sealed record EventSequenceUiCondition(
    Guid Id,
    int Act,
    SearchSequenceRangeMode RangeMode,
    int RangeValue,
    ModelKey EventKey,
    EventSequenceUiMatchMode MatchMode)
{
    public string Serialize()
    {
        string range = RangeMode == SearchSequenceRangeMode.FirstN ? "FIRST" : "SLOT";
        string mode = MatchMode == EventSequenceUiMatchMode.Appears ? "ANY" : "BAN";
        return $"A{Act} ANY {range} {RangeValue} {mode} {EventKey.Serialized}";
    }
}
