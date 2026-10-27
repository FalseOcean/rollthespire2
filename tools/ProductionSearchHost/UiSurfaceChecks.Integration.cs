using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using System.Reflection;
using Godot;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

internal static partial class UiSurfaceChecks
{
    private static async Task RunIntegrationChecks(WorkspaceShell shell, SeedPredictionRequest request, SeedPredictionDocument document)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(object obj, string name) => obj.GetType().GetField(name, flags)!.GetValue(obj)!;
        void Set(object obj, string name, object? value) => obj.GetType().GetField(name, flags)!.SetValue(obj, value);
        object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, flags)!.Invoke(obj, args);
        void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException("IntegratedUi:" + message); }
        var workbench = (QueryWorkbenchFrame)Field(shell, "_references");
        RunLiveEtaChecks(workbench);
        var persistence = (SearchWorkspace)Field(shell, "_persistence");
        var candidate = new SearchCandidate(document.CanonicalSeed, request.Authority.ProfileId,
            request.Character.CharacterKey, request.Ascension, "ui-integration", request, document,
            request.Authority, [], [], []);
        var runtime = (ModRuntimeSnapshot)Field(workbench, "_runtime");
        var draft = new WorkbenchSearchDraft(request.Character.CharacterKey, request.Ascension, SearchQuery.Empty, request.AncientOptionConditions);
        var plan = ExactSearchExecutionRequestFactory.Compile(draft.Compile(runtime, out _), new(candidate.Seed, 1, 1, 1)).Plan!;
        string fingerprint = plan.CompiledSearch.SemanticFingerprint;
        var prepared = persistence.PrepareSearchStart(draft, plan, SeedLibraryContextCapture.ForSearch(draft, runtime));
        var session = new FinishingUiSession(candidate);
        Set(workbench, "_showResults", true);
        Set(workbench, "_receiptRemaining", 0d);
        persistence.Runtime.InstallStartedSession(session, fingerprint, prepared, shell.GetTree());
        persistence.Runtime.Tick();
        Call(workbench, "PollProduction", 0d);
        Require(!workbench.HasActiveSearch && session.Delivered == 1, "final candidate must be drained after Completion");
        Require(persistence.DisplayResults.Count(r => r.QueryFingerprint == fingerprint) == 1 &&
            persistence.Workspace.Results.Count(r => r.QueryFingerprint == fingerprint) == 1,
            "published result must reach its batch and persistence exactly once");
        Set(workbench, "_receiptRemaining", 2d);
        var progress = (SearchProgressSnapshot)Field(workbench, "_lastProgress");
        Require(progress.State == SearchRunState.Faulted && progress.FailureCode == "FixtureCleanupFault" &&
            ((Label)Field(workbench, "_status")).Text.Contains("FixtureCleanupFault"), "final cleanup fault must remain visible");
        Call(workbench, "PollProduction", 6d);
        Require(((Label)Field(workbench, "_status")).Text.Contains("FixtureCleanupFault"),
            "a previous success notice timer must not clear the new fault");

        // Set the same mode state left by a previous party information page,
        // without dispatching an unrelated multiplayer prediction.
        var controller = (AnalysisPageController)Field(shell, "_predictorController");
        var page = (AnalysisPage)Field(shell, "_predictor");
        Set(controller, "_partyDraft", new WorkbenchSearchDraft(request.Character.CharacterKey, request.Ascension,
            SearchQuery.Empty, request.AncientOptionConditions));
        Set(controller, "_sourcePartyDraftKey", "previous-party-fixture");
        page.ConfigureParty(2);
        Call(shell, "ShowSingleResult", persistence.ResultAction(persistence.DisplayResults.Single(result => result.Seed == candidate.Seed)));
        Require(Field(controller, "_partyDraft") is null && page.CurrentDraft.PlayersCount == 1 && page.CurrentDraft.PlayerSlotIndex == 0,
            "single result navigation must clear the controller's party mode");
        Require(page.LastRequest is { PlayersCount: 1, PlayerSlotIndex: 0 } && page.LastDocument?.Party is null,
            "single result must produce a single-player request and document");

        // The explicit recovery control restores editing; persistence checks cover
        // the original-file backups and locked-write failure matrix independently.
        Set(workbench, "_loadFailed", true);
        Set(workbench, "_showResults", false);
        workbench.Refresh((string)Field(workbench, "_language"),
            (RolltheSpire2.Presentation.Localization.IUiTextProvider)Field(workbench, "_text"));
        var recovery = workbench.FindChild("RecoverWorkbenchDraft", true, false) as Button;
        Require(recovery is not null && ((Button)Field(workbench, "_start")).Disabled, "failed restore needs a reachable recovery action");
        recovery!.EmitSignal(Button.SignalName.Pressed);
        Require(!(bool)Field(workbench, "_loadFailed") && workbench.FindChild("RecoverWorkbenchDraft", true, false) is null,
            "explicit recovery must leave the editor unlocked");
        Require(persistence.RecentQueries.SelectMany(history => history.Results).Count(r => r.QueryFingerprint == fingerprint) == 1,
            "new draft must preserve previous result history");
        Call(workbench, "OpenPresets");
        var library = (SeedLibraryCanvas)Field(shell, "_seedLibrary");
        for (int i = 0; i < 1200 && (!library.IsVisibleInTree() || !library.IsPresentationReady); i++)
            await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(library.IsVisibleInTree() && library.IsPresentationReady && !library.HasPresentationFailure,
            "Library must finish presentation without failure before its modal flow is inspected");
        Call(workbench, "OpenPresetSave", RolltheSpire2.Ui.Components.SearchPresetSaveIntentKind.CreateCurrentQuery, "");
        var save = (RolltheSpire2.Ui.Components.SearchPresetSaveTransactionOverlay)Field(workbench, "_presetSave");
        var confirmation = (RolltheSpire2.Ui.Components.SearchConfirmationTransactionOverlay)Field(workbench, "_presetConfirm");
        int commits = 0;
        void Escape() => shell.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Call(workbench, "ConfirmPreset", "Overwrite", "fixture", (Action)(() => commits++));
        Escape();
        Require(!confirmation.IsOpen && save.IsOpen && library.IsVisibleInTree() && Field(workbench, "_presetCommit") is null && commits == 0,
            "Escape cancels the top overwrite transaction without closing its parent save editor");
        Escape();
        Require(!save.IsOpen && library.IsVisibleInTree(), "second Escape returns from save to its library");
        Call(workbench, "ConfirmPreset", "Delete", "fixture", (Action)(() => commits++));
        Escape();
        Require(!confirmation.IsOpen && library.IsVisibleInTree() && Field(workbench, "_presetCommit") is null && commits == 0,
            "Escape cancels deletion and keeps the library mounted");
        Require(library.Section == SeedLibraryCanvas.LibrarySection.Presets, "Library is a persistent workspace, not an extra modal layer");
        GD.Print("PRESET_MODAL_ESCAPE_PASS overwrite=true;delete=true;parentsPreserved=true;noCommit=true");
        GD.Print("INTEGRATED_UI_PASS finalDrain=true;cleanupFault=true;singleAfterParty=true;draftRecovery=true;historyPreserved=true");
    }

    private sealed class FinishingUiSession(SearchCandidate candidate) : IProductionSearchSession
    {
        private bool _finished, _read;
        public int Delivered { get; private set; }
        public Task Completion { get { _finished = true; return Task.CompletedTask; } }
        public void Cancel() { }
        public bool TryReadCandidate(out SearchCandidate? value)
        {
            value = _finished && !_read ? candidate : null;
            if (value is null) return false;
            _read = true; Delivered++; return true;
        }
        public SearchProgressSnapshot GetProgress() => new(_finished ? SearchRunState.Faulted : SearchRunState.Running,
            _finished ? 1 : 0, _finished ? 1 : 0, 1, 1, 1, 1, SearchDisposition.Match,
            _finished ? "FixtureCleanupFault" : "", false);
        public SearchDiagnosticSummarySnapshot GetDiagnosticSummary() => new(
            new Dictionary<string, long>(), new Dictionary<string, long>(), new Dictionary<string, string>(), "", "");
        public ulong GetSafeNextOrdinal() => _finished ? 1UL : 0;
        public bool TryGetSafeNextOrdinal(out ulong nextOrdinal) { nextOrdinal = GetSafeNextOrdinal(); return true; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
