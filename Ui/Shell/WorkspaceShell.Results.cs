using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private void InitializeResultsEntry()
    {
        _references!.OpenResultRequested += OpenSearchResult;
    }

    private void OpenSearchResult(SearchResultAction action)
    {
        if (action.Live is null) { OpenSavedSearchResult(action.Result); return; }
        if (action.Draft is { Players.Count: > 0 }) ShowPartyResult(action);
        else ShowSingleResult(action);
    }

    private void OpenSavedSearchResult(PersistedSearchResult result)
    {
        if (result.Context is not { } context) return;
        try
        {
            if (_predictor is null) CreatePredictor();
            _predictorController!.OpenSavedSearchResult(result);
            SelectTask(Workspace.Analysis);
            if (_predictorController.ReopenedWithoutHistoricalOpening) _predictor!.ShowLibraryReceipt(HistoricalOpeningNotice());
        }
        catch (Exception ex) { _references?.ShowLibraryReceipt(SeedLibraryIssue(ex)); }
    }

    private string HistoricalOpeningNotice() => _languageCode == "zh"
        ? "旧记录的开局选择在当前规则下不可用。相关席位已显示当前开局选项。"
        : "The saved opening is unavailable under current rules. Showing current offers for the affected seats.";
}
