namespace RolltheSpire2.Ui.Pages.Search.Editors;

// Swap UI drafts instead of compiling a Query: incomplete edits must be clearable
// too. Detached drafts retain hidden mode choices for the single-step undo.
internal static class WorkbenchConditionReset
{
    internal static Action Swap<T>(Dictionary<int, T> drafts, Func<T, T> empty, int? onlySlot = null) where T : class
    {
        var saved = drafts.Where(p => onlySlot is null || p.Key == onlySlot).ToArray();
        foreach (var (slot, value) in saved) drafts[slot] = empty(value);
        return () =>
        {
            if (onlySlot is null) drafts.Clear();
            foreach (var (slot, value) in saved) drafts[slot] = value;
        };
    }
}
