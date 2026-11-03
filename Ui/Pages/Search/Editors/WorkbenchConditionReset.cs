namespace RolltheSpire2.Ui.Pages.Search.Editors;

// Swap UI drafts instead of compiling a Query: incomplete edits must be clearable
// too. Detached drafts retain hidden mode choices for the single-step undo.
internal static class WorkbenchConditionReset
{
    internal static Action Swap<T>(Dictionary<int, T> drafts, Func<T, T> empty, int? onlySlot = null,
        Action<T, T>? replaceActive = null) where T : class
    {
        var saved = drafts.Where(p => onlySlot is null || p.Key == onlySlot).ToArray();
        foreach (var (slot, value) in saved)
        {
            var cleared = empty(value);
            drafts[slot] = cleared;
            replaceActive?.Invoke(value, cleared);
        }
        return () => Restore(drafts, saved, onlySlot, replaceActive);
    }

    internal static Action Preserve<T>(Dictionary<int, T> drafts, Func<T, T> copy,
        Action<T, T> replaceActive) where T : class
    {
        var saved = drafts.Select(pair => new KeyValuePair<int, T>(pair.Key, copy(pair.Value))).ToArray();
        return () => Restore(drafts, saved, null, replaceActive);
    }

    private static void Restore<T>(Dictionary<int, T> drafts, KeyValuePair<int, T>[] saved, int? onlySlot,
        Action<T, T>? replaceActive) where T : class
    {
        var current = drafts.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (onlySlot is null) drafts.Clear();
        foreach (var (slot, value) in saved)
        {
            drafts[slot] = value;
            if (current.TryGetValue(slot, out var replaced)) replaceActive?.Invoke(replaced, value);
        }
    }
}
