using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Ui.Workspace;

// Both page controls edit this one instance. Only the event owner exports it.
internal sealed class UnknownRoomPrefixEditorState
{
    internal int EventCount;
    internal int MonsterCount;
    internal bool HasConditions => EventCount > 0 || MonsterCount > 0;
    internal bool HasConflict => EventCount > 0 && MonsterCount > 0;
    internal int Selection => HasConflict ? 7 : MonsterCount > 0 ? MonsterCount + 3 : EventCount;
    internal void Select(int value)
    {
        if (value == 7) return; // Retain imported contradictions until the player explicitly edits them.
        EventCount = value is >= 1 and <= 3 ? value : 0;
        MonsterCount = value is >= 4 and <= 6 ? value - 3 : 0;
    }
    internal string[] Options(IUiTextProvider text) => Enumerable.Range(0, 4)
        .Select(i => text.Get("query.event.unknown.option_" + i))
        .Concat(Enumerable.Range(1, 3).Select(i => text.Get("query.unknown.monster.option_" + i)))
        .Concat(HasConflict ? new[] { text.Get("query.unknown.prefix.conflict") } : []).ToArray();
}
