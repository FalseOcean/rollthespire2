using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private void ShowSingleResult(SearchResultAction action)
    {
        var candidate = action.Live!.Candidate;
        SelectTask(Workspace.Analysis);
        if (_predictor is null || _predictorController is null) return;
        _predictorController.SetPartyDraft(null);
        _predictorController.ClearSeedLibraryOverrides();
        _predictorController.ImportSearchOpening(candidate, action.Draft, action.Context, action.Association);

        string route = candidate.Witnesses.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.OpeningRouteId))
            ?.OpeningRouteId ?? string.Empty;
        int? choiceSlot = null;
        const string prefix = "choice.";
        if (route.StartsWith(prefix, StringComparison.Ordinal))
        {
            int separator = route.IndexOf('.', prefix.Length);
            if (separator > prefix.Length && separator + 1 < route.Length &&
                int.TryParse(route[prefix.Length..separator], out int parsedSlot) && parsedSlot >= 0)
            {
                choiceSlot = parsedSlot;
                route = route[(separator + 1)..];
            }
        }

        _predictor.SetPredictorContext(candidate.CharacterKey, candidate.Ascension, 1, 0,
            candidate.PredictionRequest.AncientOptionConditions, route, choiceSlot,
            string.Empty, notify: false);
        _predictor.SetUnlockState(candidate.PredictionRequest.Authority.AllCharacterCardPoolsUnlocked);
        _predictor.SetSeedText(candidate.Seed, commit: true);
        _predictorController.AnalyzeCurrentDraft();
    }

    private void ShowPartyResult(SearchResultAction action)
    {
        var candidate = action.Live!.Candidate;
        SelectTask(Workspace.Analysis);
        _predictorController?.SetPartyDraft(action.Draft!, candidate.Seed, candidate.Document?.Party);
        _predictorController?.ImportSearchOpening(candidate, action.Draft, action.Context, action.Association);
    }
}
