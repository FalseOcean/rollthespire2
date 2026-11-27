using Godot;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class SeedLibraryCanvas
{
    internal Func<QueryHistoryEntry, string, CancellationToken, Task<Dictionary<string, PersistedSearchResult>>>? RecheckHistory { get; set; }
    private Task<Dictionary<string, PersistedSearchResult>>? _recheckTask;
    private readonly CancellationTokenSource _recheckCancel = new();
    private string _recheckId = "";
    private Dictionary<string, PersistedSearchResult>? _rechecked;

    private void AddRecheck(Control actions, QueryHistoryEntry query, bool canUse)
    {
        var button = AddAction(actions, T("重载并校验已有结果", "Reload and check saved seeds"), () => BeginRecheck(query));
        button.Name = "RecheckHistory"; RequireIdle(button, !canUse || _recheckTask != null);
        if (_recheckId != query.Id) return;
        if (_recheckTask != null)
            _detail.AddChild(Text(T("正在按当前条件校验已有种子…", "Checking saved seeds against current conditions…"), 17, true));
        else if (_rechecked is { } rows)
        {
            int hits = rows.Values.Count(r => r.ValidationDisposition == SearchDisposition.Match);
            int misses = rows.Values.Count(r => r.ValidationDisposition == SearchDisposition.NoMatch);
            int unknown = rows.Count - hits - misses;
            _detail.AddChild(Text(T($"本次校验 · 命中 {hits} · 不符 {misses} · 未确认 {unknown}",
                $"Current check · {hits} matched · {misses} ruled out · {unknown} unconfirmed"), 17, true));
        }
    }

    private void BeginRecheck(QueryHistoryEntry query)
    {
        if (_recheckTask != null || RecheckHistory == null) return;
        _recheckId = query.Id; _rechecked = null;
        try
        {
            _recheckTask = RecheckHistory(query, QueryTitle(query), _recheckCancel.Token);
            _historyResults = true;
            SelectDetail();
        }
        catch (Exception ex) { SelectDetail(); ShowIssue(ex.Message); }
    }

    private void PollRecheck()
    {
        if (_recheckTask is not { IsCompleted: true } task) return;
        _recheckTask = null;
        if (_section == LibrarySection.History) SelectDetail();
        try { _rechecked = task.GetAwaiter().GetResult(); }
        catch (Exception ex) { ShowIssue(ex.Message); }
    }

    private string CurrentCheckStatus(PersistedSearchResult result) => result.ValidationDisposition switch
    {
        SearchDisposition.Match => T("当前命中", "Matches now"),
        SearchDisposition.NoMatch => T("当前不符", "Ruled out now"),
        _ => T("当前未确认", "Unconfirmed now")
    };
}
