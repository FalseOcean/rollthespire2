using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private SearchCandidate? _partyExpected;
    private WorkbenchSearchDraft? _partyResultDraft;

    private void ShowSingleResult(SearchCandidate candidate)
    {
        _partyExpected = null; _partyResultDraft = null;
        SelectTask(Workspace.Analysis);
        if (_predictor is null || _predictorController is null) return;
        _predictorController.SetPartyDraft(null);
        _predictorController.ClearSeedLibraryOverrides();
        _predictorController.ImportSearchOpening(candidate, _references?.ResultDraft, _references?.ResultSeedContext);

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

    private void ShowPartyResult(SearchCandidate candidate, WorkbenchSearchDraft draft)
    {
        _partyExpected = candidate;
        _partyResultDraft = draft;
        SelectTask(Workspace.Analysis);
    }
}
