using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private void InitializeResultsEntry()
    {
        _references!.OpenSavedResultRequested += OpenSavedSearchResult;
    }

    private void OpenSavedSearchResult(PersistedSearchResult result)
    {
        if (result.Context is not { } context) return;
        try
        {
            if (_predictor is null) CreatePredictor();
            _predictorController!.OpenPredictionContext(result.Seed, context);
            _partyExpected = null; _partyResultDraft = null;
            SelectTask(Workspace.Analysis);
            _predictor!.SetSearchOrigin(result.Seed, result.IsUnverified);
        }
        catch (Exception ex) { _references?.ShowLibraryReceipt(SeedLibraryIssue(ex)); }
    }
}
