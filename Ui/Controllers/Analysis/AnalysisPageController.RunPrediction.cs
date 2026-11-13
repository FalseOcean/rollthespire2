using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Controllers;

internal sealed partial class AnalysisPageController
{
    private SeedLibraryContext? _capturedRunInformation;
    private string _capturedRunInformationSeed = "";

    // Session-only choices for an expanded live roster. They never become an
    // offline configuration, and are reused only after the shell matches the run.
    internal (string Seed, SeedLibraryContext Context)? CurrentRunPredictionContext =>
        _capturedRunInformation is not null && _partyDraft is { } party
            ? (_capturedRunInformationSeed, PartyContext(party)) : null;
}
