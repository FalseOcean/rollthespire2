using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class QueryWorkbenchFrame
{
    private SeedLibraryContext? _resultSeedContext;
    internal WorkbenchSearchDraft? ResultDraft { get; private set; }
    internal SeedLibraryContext? ResultSeedContext => _resultSeedContext;
    internal event Action<string, SeedLibraryContext>? FavoriteSeedRequested;
    internal void ShowLibraryReceipt(string message) => ReceiptText(message);

    private void InstallResultContext(WorkbenchSearchDraft draft)
    {
        ResultDraft = draft;
        _runningPartyDraft = draft.Players.Count > 0 ? draft : null;
        try { _resultSeedContext = SeedLibraryContextCapture.ForSearch(draft, _runtime); }
        catch (Exception ex)
        {
            _resultSeedContext = null;
            RuntimeLog.Warn("seedLibraryResultContextUnavailable=" + ex.Message);
        }
    }

    private Button FavoriteResultButton(Search.Contracts.SearchCandidate result, SeedLibraryContext? context)
    {
        var button = _p.Button(_language == "zh" ? "收藏" : "Favorite");
        button.CustomMinimumSize = new(72, 32); button.AddThemeFontSizeOverride("font_size", 14);
        button.Disabled = context is null || result.IsUnverified;
        if (result.IsUnverified) button.TooltipText = _language == "zh" ? "先校验候选，再保存已确认结果。" : "Validate this candidate before saving a confirmed result.";
        if (context is null) button.TooltipText = _language == "zh" ? "此结果缺少可恢复的上下文。" : "This result has no restorable context.";
        button.Pressed += () =>
        {
            try { FavoriteSeedRequested?.Invoke(result.Seed, SeedLibraryContextCapture.WithWitness(context!, result)); }
            catch (Exception ex) { ReceiptText(ex.Message); }
        };
        return button;
    }
}
