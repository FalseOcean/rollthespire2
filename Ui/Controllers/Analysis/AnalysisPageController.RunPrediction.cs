using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Controllers;

internal sealed partial class AnalysisPageController
{
    private SeedLibraryContext? _capturedRunInformation;
    private string _capturedRunInformationSeed = "";
    private (string Seed, SeedLibraryContext Context)? _liveRunPredictionBinding;
    internal bool HasLiveRunPredictionBinding => _liveRunPredictionBinding.HasValue;

    private void SetLiveRunPredictionBinding(string seed, SeedLibraryContext context, LiveRunPredictionPremises premises)
    {
        _liveRunPredictionBinding = (seed, context);
        _page.SetLiveRunPredictionPremises(premises);
    }

    private void ClearLiveRunPredictionBinding()
    {
        _liveRunPredictionBinding = null;
        _page.SetLiveRunPredictionPremises(null);
    }

    private void DetachLiveRunPredictionWhenInputsChange(AnalysisRequestDraft draft)
    {
        if (_liveRunPredictionBinding is not { } binding) return;
        var context = binding.Context;
        if (draft.RawSeed != binding.Seed || draft.Ascension != context.Ascension ||
            draft.PlayersCount != context.Players.Count || draft.PlayerSlotIndex < 0 || draft.PlayerSlotIndex >= context.Players.Count ||
            context.Players[draft.PlayerSlotIndex].Character != draft.CharacterKey)
            ClearSeedLibraryOverrides();
    }

    // Session-only choices for an expanded live roster. They never become an
    // offline configuration, and are reused only after the shell matches the run.
    internal (string Seed, SeedLibraryContext Context)? CurrentRunPredictionContext =>
        _capturedRunInformation is not null && _partyDraft is { } party
            ? (_capturedRunInformationSeed, PartyContext(party)) : null;
}
