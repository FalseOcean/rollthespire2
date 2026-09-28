using RolltheSpire2.Infrastructure.Snapshots;
using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Ancient;
using RolltheSpire2.Ui.Pages.Search.BossMap;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Pages.Search.Shop;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Controllers;

internal sealed class SearchPageController
{
    private readonly SearchPage _page;
    private readonly ModRuntimeSnapshot _runtime;
    private readonly RuntimePredictionSettings _predictionSettings;
    private readonly Action<GlobalStatusKind, string, string> _setGlobalStatus;
    private readonly Action<SearchCandidate> _openCandidate;
    private readonly Action<PersistedSearchResult> _openPersistedCandidate;
    private readonly Action<string> _showReceipt;
    private readonly IRuntimePredictionDiagnosticSink _diagnosticSink;
    private readonly SearchWorkspacePersistence _persistence;
    private readonly SearchPresetCatalog _presets;
    private IProductionSearchSession? _session;
    private string _diagnosticSessionId = string.Empty;
    private string _diagnosticStartSeed = string.Empty;
    private string _activeQueryFingerprint = string.Empty;
    private bool _activeUsesPersistentCursor;
    private bool _panelCloseCancellationRequested;
    private ulong _cycleStartOrdinal;
    private long _completedSegmentScanned;
    private int _completedSegmentMatches;
    private int _cycleTargetMatches;
    private bool _wrappedThisCycle;
    private SearchDraft? _cycleDraft;
    private SearchRunDraft? _cycleRunDraft;
    private bool _persistedWorkspaceRestored;
    private bool _restoringPersistedWorkspace;
    private bool _discardActiveSessionCandidates;
    private bool _presetLoadCancellationRequested;
    private PendingPresetLoad? _pendingPresetLoad;

    public SearchPageController(
        SearchPage page,
        ModRuntimeSnapshot runtime,
        RuntimePredictionSettings predictionSettings,
        IRuntimePredictionDiagnosticSink diagnosticSink,
        SearchWorkspacePersistence persistence,
        Action<GlobalStatusKind, string, string> setGlobalStatus,
        Action<SearchCandidate> openCandidate,
        Action<PersistedSearchResult> openPersistedCandidate,
        Action<string> showReceipt)
    {
        _page = page;
        _runtime = runtime;
        _predictionSettings = predictionSettings;
        _diagnosticSink = diagnosticSink ?? throw new ArgumentNullException(nameof(diagnosticSink));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _presets = _persistence.Presets;
        _setGlobalStatus = setGlobalStatus;
        _openCandidate = openCandidate;
        _openPersistedCandidate = openPersistedCandidate;
        _showReceipt = showReceipt ?? throw new ArgumentNullException(nameof(showReceipt));

        _page.BindRuntime(runtime);

        _page.StartRequested += Start;
        _page.StopRequested += Stop;
        _page.PollRequested += Poll;
        _page.CandidateOpenRequested += _openCandidate;
        _page.PersistedCandidateOpenRequested += _openPersistedCandidate;
        _page.ContextDraftChanged += RefreshSearchContext;
        _page.QueryDraftChanged += RefreshQueryDraft;
        _page.PresetWorkspaceRequested += ShowPresetLibrary;
        _page.PresetSaveConfirmed += SavePreset;
        _page.PresetLoadConfirmed += LoadPreset;
        _page.PresetDeleteRequested += DeletePreset;
    }

    /// <summary>
    /// Restore persisted Search business state only after the page has received
    /// localization/content-name dependencies. Several picker/editor surfaces,
    /// especially structured Neow effect editors, are materialized by
    /// ApplyLocalization; restoring earlier silently loses their values.
    /// </summary>
    internal bool HasActiveSearchSession => _session is not null;

    /// <summary>
    /// Developer-diagnostic semantic capture. Reuses the current Search page Draft and
    /// canonical SearchCompiler path on the main thread, but does not create a SearchSession
    /// or alter persistent cursor/run options.
    /// </summary>
    internal bool TryGetCurrentCompiledSearchForDeveloperDiagnostic(out CompiledSearch? compiled, out string issue)
    {
        compiled = null;
        issue = string.Empty;
        if (_session is not null)
        {
            issue = "SearchSessionCurrentlyRunning";
            return false;
        }

        try
        {
            SearchDraft draft = _page.CurrentDraft;
            if (!TryCompileDraftSemantics(draft, out compiled, out _, out issue) || compiled is null)
                return false;
            SearchFeasibilityResult feasibility = SearchFeasibilityAnalyzer.Analyze(compiled);
            if (feasibility.IsImpossible)
            {
                issue = _page.FormatSearchImpossibility(feasibility.Proof);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            issue = "CurrentSearchCompileFailed:" + ex.GetType().Name + ":" + ex.Message;
            compiled = null;
            return false;
        }
    }

    internal void EnsurePersistedWorkspaceRestored()
    {
        if (_persistedWorkspaceRestored)
            return;

        _restoringPersistedWorkspace = true;
        try
        {
            RestorePersistedWorkspace();
        }
        catch (Exception ex)
        {
            // Persistence/restore is fail-soft. A malformed or temporarily
            // unavailable UI/catalog projection must not block opening Search.
            RuntimeLog.Warn($"searchWorkspaceRestoreFailed=true;failSoft=true;issue={ex.GetType().Name}:{ex.Message}");
        }
        finally
        {
            _restoringPersistedWorkspace = false;
            _persistedWorkspaceRestored = true;
        }
        RefreshProbabilityPresentation();
        RefreshResultsStaleness();
    }

    private void RestorePersistedWorkspace()
    {
        SearchWorkspaceDocument workspace = _persistence.Workspace;
        ModelKey character = BaseGameModelKeys.Characters.Silent;
        if (!string.IsNullOrWhiteSpace(workspace.CharacterKey) &&
            ModelKey.TryParseExact(workspace.CharacterKey, out ModelKey restoredCharacter) &&
            restoredCharacter.IsValid)
        {
            character = restoredCharacter;
        }
        int ascension = Math.Clamp(
            workspace.Ascension,
            SeedPredictionInputLimits.MinimumAscension,
            SeedPredictionInputLimits.MaximumAscension);
        _page.RestoreContext(character, ascension, notify: false);
        character = _page.CharacterKey;
        RefreshNeowUiCatalog();
        _page.RestoreWorkspace(character, ascension, workspace.Draft, workspace.RunDraft);
        _page.RestorePersistedResults(_persistence.GetPersistedResults());
        RuntimeLog.Info(
            $"searchWorkspaceRestored=true;hasDraft={(workspace.Draft is not null).ToString().ToLowerInvariant()};" +
            $"resultCount={workspace.Results.Count};character={character.Serialized};ascension={ascension};" +
            $"cursor={_persistence.DescribeCursor()}");
    }

    internal void RefreshPersistedResults()
    {
        if (_session is not null) return;
        _page.RestorePersistedResults(_persistence.GetPersistedResults());
        RefreshResultsStaleness();
    }



    private void ShowPresetLibrary() =>
        _page.ShowPresetLibrary(_presets.GetAll());

    private void SavePreset(SearchPresetSaveCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        try
        {
            SearchPresetDefinition saved;
            SearchPresetMetadataDraft metadata = commit.Metadata;
            switch (commit.Kind)
            {
                case SearchPresetSaveIntentKind.CreateCurrentQuery:
                {
                    SearchPresetCapture capture = CaptureCurrentPresetSnapshot();
                    saved = _presets.SaveUserPreset(
                        metadata.Title,
                        metadata.Description,
                        metadata.VisualIcons,
                        capture);
                    break;
                }
                case SearchPresetSaveIntentKind.CreateFromExistingSnapshot:
                {
                    if (!_presets.TryGet(commit.SourcePresetId, out SearchPresetDefinition source))
                        throw new KeyNotFoundException("PresetSourceMissing:" + commit.SourcePresetId);
                    saved = _presets.SaveUserPresetFromExisting(
                        metadata.Title,
                        metadata.Description,
                        metadata.VisualIcons,
                        source);
                    break;
                }
                case SearchPresetSaveIntentKind.EditMetadata:
                    saved = _presets.UpdateUserMetadata(
                        commit.SourcePresetId,
                        metadata.Title,
                        metadata.Description,
                        metadata.VisualIcons);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(commit.Kind));
            }

            RuntimeLog.Info(
                $"searchPresetSaved=true;id={saved.Id};title={saved.Title};source={saved.Source};intent={commit.Kind};" +
                $"character={saved.CharacterKey.Serialized};ascension={saved.Ascension};conditionCount={saved.ConditionCount};" +
                $"visualIconCount={saved.VisualIcons.Count};savedProbabilityStatus={saved.SavedProbability.Status};" +
                $"payload=Character+Ascension+SearchDraft;runControlsExcluded=true;unlockContextExcluded=true");
            _page.RefreshPresetLibrary(_presets.GetAll());
            _showReceipt(_page.Format(Ui1TextKey.SearchPresetReceiptSaved, saved.Title));
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "SearchPresetTitleAlreadyExists", StringComparison.Ordinal))
        {
            RuntimeLog.Warn("searchPresetSaveFailed=true;failSoft=true;reason=TitleConflict");
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(
                $"searchPresetSaveFailed=true;failSoft=true;issue={ex.GetType().Name}:{ex.Message}");
        }
    }

    private SearchPresetCapture CaptureCurrentPresetSnapshot()
    {
        SearchDraft draft = _page.CurrentDraft;
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
        SearchProbabilityQuickView probability = SearchProbabilityQuickView.Unavailable;
        SearchPresetProvenance provenance = BuildPresetProvenance(capturedAtUtc);

        try
        {
            if (TryCompileDraftSemanticsForContext(
                    draft,
                    _page.CharacterKey,
                    _page.Ascension,
                    out CompiledSearch? compiled,
                    out RuntimeContextAuthoritySnapshot _,
                    out _) && compiled is not null)
            {
                probability = SearchProbabilityPresentationBuilder.Build(compiled);
            }
        }
        catch (Exception ex)
        {
            // Probability/provenance are historical metadata only and can never block
            // saving the player's authored Query snapshot.
            RuntimeLog.Info($"searchPresetSavedAnalysisUnavailable=true;issue={ex.GetType().Name}:{ex.Message}");
        }

        return new SearchPresetCapture(
            _page.CharacterKey,
            _page.Ascension,
            draft,
            _page.CurrentEnabledConditionCount,
            SearchPresetProbabilitySnapshot.From(probability, capturedAtUtc),
            provenance,
            capturedAtUtc);
    }

    private void LoadPreset(string id)
    {
        try
        {
            if (!_presets.TryGet(id, out SearchPresetDefinition preset))
            {
                RuntimeLog.Warn($"searchPresetLoadSkipped=true;reason=MissingPreset;id={id}");
                return;
            }

            SearchPresetLoadResolution resolution = SearchPresetCompatibilityResolver.Resolve(
                preset,
                RuntimeAuthorityEnvironment.Current.Authority);
            SearchPresetCompatibilityAssessment compatibility = SearchPresetCompatibilityResolver.Assess(
                preset,
                RuntimeAuthorityEnvironment.Current);
            if (!resolution.CanLoad || resolution.Draft is null)
            {
                _showReceipt(_page.Format(Ui1TextKey.SearchPresetReceiptLoadUnavailable, preset.Title));
                RuntimeLog.Warn(
                    $"searchPresetLoadSkipped=true;reason={resolution.Issue};id={id};rawPreserved=true;" +
                    $"authoredConditions={resolution.AuthoredConditionCount};unresolvedRefs={resolution.Unresolved.Count};" +
                    $"semanticCompatibility={compatibility.SemanticCompatibility}");
                return;
            }

            var pending = new PendingPresetLoad(preset, resolution, compatibility);
            if (_session is not null)
            {
                _pendingPresetLoad = pending;
                _discardActiveSessionCandidates = true;
                _presetLoadCancellationRequested = true;
                _session.Cancel();
                _setGlobalStatus(
                    GlobalStatusKind.Warning,
                    _page.Localize(Ui1TextKey.SearchStatusStopping),
                    string.Empty);
                RuntimeLog.Info(
                    $"searchPresetLoadCancellationRequested=true;id={preset.Id};discardOldSessionCandidates=true;" +
                    $"deferredCommitUntilSessionTerminal=true;mainThreadWait=false");
                return;
            }

            CommitPresetLoad(pending, cancelledActiveSearch: false);
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(
                $"searchPresetLoadFailed=true;failSoft=true;id={id};issue={ex.GetType().Name}:{ex.Message}");
        }
    }

    private void CommitPresetLoad(PendingPresetLoad pending, bool cancelledActiveSearch)
    {
        SearchPresetDefinition preset = pending.Preset;
        SearchPresetLoadResolution resolution = pending.Resolution;
        SearchPresetCompatibilityAssessment compatibility = pending.Compatibility;
        if (resolution.Draft is null)
            return;

        SearchRunDraft runBefore = _page.CurrentRunDraft;
        _page.ReplaceAuthoredState(preset.CharacterKey, preset.Ascension, resolution.Draft);
        SearchRunDraft runAfter = _page.CurrentRunDraft;
        PersistWorkspaceSnapshot();
        RefreshProbabilityPresentation();
        RefreshResultsStaleness();

        if (resolution.Kind == SearchPresetLoadResolutionKind.Partial)
        {
            _showReceipt(_page.Format(
                Ui1TextKey.SearchPresetReceiptPartiallyLoaded,
                preset.Title,
                resolution.LoadedConditionCount,
                resolution.AuthoredConditionCount,
                resolution.UnresolvedConditionCount));
        }
        else
        {
            _showReceipt(_page.Format(Ui1TextKey.SearchPresetReceiptLoaded, preset.Title));
        }

        RuntimeLog.Info(
            $"searchPresetLoaded=true;id={preset.Id};title={preset.Title};source={preset.Source};replaceNotMerge=true;" +
            $"loadKind={resolution.Kind};authoredConditions={resolution.AuthoredConditionCount};" +
            $"loadedConditions={resolution.LoadedConditionCount};unresolvedConditions={resolution.UnresolvedConditionCount};" +
            $"unresolvedRefs={resolution.Unresolved.Count};semanticCompatibility={compatibility.SemanticCompatibility};" +
            $"sameGameVersion={compatibility.SameGameVersion.ToString().ToLowerInvariant()};" +
            $"cancelledActiveSearch={cancelledActiveSearch.ToString().ToLowerInvariant()};resultsPreserved=true;resultsStalenessReevaluated=true;" +
            $"character={preset.CharacterKey.Serialized};ascension={preset.Ascension};" +
            $"runControlsUnchanged={(runBefore == runAfter).ToString().ToLowerInvariant()};unlockContextUnchanged=true;rawPresetPreserved=true");
        foreach (SearchPresetUnresolvedReference unresolved in resolution.Unresolved.Take(12))
        {
            RuntimeLog.Warn(
                $"searchPresetUnresolvedReference=true;id={preset.Id};path={unresolved.Path};" +
                $"identity={unresolved.StableIdentity};reason={unresolved.Reason}");
        }
    }

    private void DeletePreset(string id)
    {
        try
        {
            string title = _presets.TryGet(id, out SearchPresetDefinition existing)
                ? existing.Title
                : id;
            bool deleted = _presets.DeleteUserPreset(id);
            RuntimeLog.Info($"searchPresetDeleted={deleted.ToString().ToLowerInvariant()};id={id}");
            if (deleted)
            {
                _page.RefreshPresetLibrary(_presets.GetAll());
                _showReceipt(_page.Format(Ui1TextKey.SearchPresetReceiptDeleted, title));
            }
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(
                $"searchPresetDeleteFailed=true;failSoft=true;id={id};issue={ex.GetType().Name}:{ex.Message}");
        }
    }

    private SearchPresetProvenance BuildPresetProvenance(DateTimeOffset capturedAtUtc)
    {
        RuntimeAuthorityEnvironmentSnapshot environment = RuntimeAuthorityEnvironment.Current;
        RuntimeAuthoritySnapshot authority = environment.Authority;
        RuntimeAuthorityInterpretation interpretation = environment.Interpretation;
        string gameVersion = string.IsNullOrWhiteSpace(authority.GameVersion)
            ? _runtime.Detection.DisplayVersion
            : authority.GameVersion;
        string unlock = interpretation.VanillaUnlockStatus switch
        {
            VanillaUnlockOverallStatus.Full => SearchPresetUnlockKinds.Full,
            VanillaUnlockOverallStatus.Partial => SearchPresetUnlockKinds.Partial,
            _ => SearchPresetUnlockKinds.Unknown
        };
        return new SearchPresetProvenance(
            gameVersion,
            authority.FingerprintSchemaVersion,
            authority.Fingerprint.OverallSemanticHash ?? string.Empty,
            interpretation.EnvironmentStatus.ToString(),
            interpretation.MatchedBaselineGameVersion ?? string.Empty,
            interpretation.ComparisonBaselineGameVersion ?? string.Empty,
            interpretation.EnvironmentReason ?? string.Empty,
            unlock,
            capturedAtUtc);
    }

    private void RecordSuccessfulRunTemporaryPreset(
        SearchDraft draft,
        CompiledSearch compiledSearch)
    {
        try
        {
            DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
            SearchProbabilityQuickView probability;
            try
            {
                probability = SearchProbabilityPresentationBuilder.Build(compiledSearch);
            }
            catch (Exception ex)
            {
                probability = SearchProbabilityQuickView.Unavailable;
                RuntimeLog.Info($"temporarySearchPresetProbabilityUnavailable=true;issue={ex.GetType().Name}:{ex.Message}");
            }

            var capture = new SearchPresetCapture(
                _page.CharacterKey,
                _page.Ascension,
                draft,
                _page.CurrentEnabledConditionCount,
                SearchPresetProbabilitySnapshot.From(probability, capturedAtUtc),
                BuildPresetProvenance(capturedAtUtc),
                capturedAtUtc);
            SearchPresetDefinition temporary = _presets.RecordTemporary(capture);
            _page.RefreshPresetLibrary(_presets.GetAll());
            RuntimeLog.Info(
                $"temporarySearchPresetRecorded=true;id={temporary.Id};conditionCount={temporary.ConditionCount};" +
                $"capacity=5;fifo=true;dedupe=false;persistent=true");
        }
        catch (Exception ex)
        {
            // Search-session creation is already successful. Lightweight recent-history
            // persistence must never roll back or fail the live Search.
            RuntimeLog.Warn(
                $"temporarySearchPresetRecordFailed=true;failSoft=true;searchContinues=true;issue={ex.GetType().Name}:{ex.Message}");
        }
    }

    private void RefreshSearchContext()
    {
        if (!_persistedWorkspaceRestored || _restoringPersistedWorkspace)
            return;
        RefreshNeowUiCatalog();
        PersistWorkspaceSnapshot();
        RefreshProbabilityPresentation();
        RefreshResultsStaleness();
    }

    private void RefreshQueryDraft()
    {
        if (!_persistedWorkspaceRestored || _restoringPersistedWorkspace)
            return;
        PersistWorkspaceSnapshot();
        RefreshProbabilityPresentation();
        RefreshResultsStaleness();
    }

    private void RefreshResultsStaleness()
    {
        if (!_persistedWorkspaceRestored || _restoringPersistedWorkspace)
            return;
        if (_page.PersistedResultCount == 0)
        {
            _page.SetResultsStale(false);
            return;
        }
        if (_session is not null)
            return;

        string resultsFingerprint = _persistence.Workspace.QueryFingerprint ?? string.Empty;
        bool stale = true;
        try
        {
            SearchDraft draft = _page.CurrentDraft;
            if (TryCompileDraftSemantics(draft, out CompiledSearch? compiled, out _, out _) && compiled is not null)
            {
                stale = string.IsNullOrWhiteSpace(resultsFingerprint) ||
                    !string.Equals(compiled.SemanticFingerprint, resultsFingerprint, StringComparison.Ordinal);
            }
        }
        catch
        {
            stale = true;
        }
        _page.SetResultsStale(stale);
    }

    private void RefreshProbabilityPresentation()
    {
        if (_session is not null)
            return;

        try
        {
            SearchDraft draft = _page.CurrentDraft;
            if (!TryCompileDraftSemantics(draft, out CompiledSearch? compiled, out _, out _) || compiled is null)
            {
                _page.BindProbabilityQuickView(SearchProbabilityQuickView.Unavailable);
                _page.BindEtaQuickView(SearchEtaQuickView.Unavailable(_page.CurrentRunDraft.TargetMatchCount));
                return;
            }

            // Rarity stays CompiledSearch-derived. ETA is a separate presentation-only
            // preview that asks the current Production Planner/Analytical Cost authority
            // for the selected physical plan without starting a Search workload.
            _page.BindProbabilityQuickView(SearchProbabilityPresentationBuilder.Build(compiled));
            RefreshEtaPresentation(compiled);
        }
        catch (Exception ex)
        {
            RuntimeLog.Info($"searchProbabilityPresentationUnavailable={ex.GetType().Name}:{ex.Message}");
            _page.BindProbabilityQuickView(SearchProbabilityQuickView.Unavailable);
            _page.BindEtaQuickView(SearchEtaQuickView.Unavailable(_page.CurrentRunDraft.TargetMatchCount));
        }
    }

    private void RefreshEtaPresentation(CompiledSearch compiled)
    {
        try
        {
            SearchRunDraft runDraft = _page.CurrentRunDraft;
            ExactSearchExecutionCompileResult execution = ExactSearchExecutionRequestFactory.Compile(compiled, ToRunOptions(runDraft));
            if (!execution.Success || execution.Plan is null)
            {
                _page.BindEtaQuickView(SearchEtaQuickView.Unavailable(runDraft.TargetMatchCount));
                return;
            }

            FamilyExecutionPlan familyPlan = FamilyExecutionCoordinator.Plan(execution.Plan,
                _predictionSettings.NormalizeWorkshopSearchModePreference() == Beta110RelicComputeBackendPreference.Cpu ? false : null);
            FamilySearchEtaProjectionV1 projection = FamilySearchEtaProjectorV1.Project(execution.Plan, familyPlan);
            _page.BindEtaQuickView(SearchEtaPresentationBuilder.Build(projection));
        }
        catch (Exception ex)
        {
            // ETA is presentation-only. Preview failure must never participate in Search admission.
            RuntimeLog.Info($"searchEtaPreviewUnavailable=true;issue={ex.GetType().Name}:{ex.Message}");
            _page.BindEtaQuickView(SearchEtaQuickView.Unavailable(_page.CurrentRunDraft.TargetMatchCount));
        }
    }

    private static string EtaLogNumber(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("R") : "unknown";

    private void BindAndLogSelectedEta(ProductionSearchSession session)
    {
        try
        {
            FamilySearchEtaProjectionV1 projection = session.EtaProjection;
            _page.BindEtaQuickView(SearchEtaPresentationBuilder.Build(projection));
            string missing = projection.MissingEvidence.Count == 0
                ? "none"
                : string.Join("|", projection.MissingEvidence.Select(item => item.Replace(';', ',').Replace('\n', ' ')));
            RuntimeLog.Info(
                "searchEtaProjection=true;phase=SelectedProductionPlan;" +
                $"status={projection.Status};effectiveAcceptedResultSurvivalRate={EtaLogNumber(projection.AcceptedResultProbability)};" +
                $"acceptedResultSurvivalEvidence={projection.AcceptedResultProbabilityEvidence};" +
                $"selectedPlan={projection.Plan.PlanId};selectionPolicy={projection.Plan.SelectionPolicyId};" +
                $"firstResultMeanMs={EtaLogNumber(projection.FirstResultMeanMs)};firstResultP99Ms={EtaLogNumber(projection.FirstResultP99Ms)};" +
                $"targetCount={projection.TargetCount};targetMeanMs={EtaLogNumber(projection.TargetMeanMs)};" +
                $"playerFirstSearchMeanMs={EtaLogNumber(projection.FirstResultSearchMeanMs)};playerFirstSearchP99Ms={EtaLogNumber(projection.FirstResultSearchP99Ms)};playerTargetSearchMeanMs={EtaLogNumber(projection.TargetSearchMeanMs)};playerGpuColdSetupIncluded=false;" +
                $"firstMeanRoots={(projection.FirstMeanRootHorizon?.ToString() ?? "unknown")};firstP99Roots={(projection.FirstP99RootHorizon?.ToString() ?? "unknown")};targetMeanRoots={(projection.TargetMeanRootHorizon?.ToString() ?? "unknown")};" +
                $"fixedScanRangeMs={EtaLogNumber(projection.FixedScanRangeMs)};" +
                $"expectedMatchesWithinScanRange={EtaLogNumber(projection.ExpectedMatchesWithinScanRange)};" +
                $"targetProbabilityWithinScanRange={EtaLogNumber(projection.TargetProbabilityWithinScanRange)};" +
                $"familyPipelineMs={EtaLogNumber(projection.TargetFamilyPipelineMs)};exactTailMs={EtaLogNumber(projection.TargetExactTailMs)};" +
                $"missingEvidence={missing};acceptedResultRootModel=QueryWideProbabilityAuthority;" +
                "etaAuthority=FamilyPlan+FamilySurvival+SelectedPhysicalPerformance+ExactTimingOnly;" +
                $"steadyStateComposition=BoundedWindowPipelineFillDrain;startupIncluded={projection.Plan.EstimatedSetupMilliseconds.HasValue};searchAdmissionAffected=false");
        }
        catch (Exception ex)
        {
            RuntimeLog.Info($"searchEtaProjection=true;phase=SelectedProductionPlan;status=Unavailable;missingEvidence=ProjectionException:{ex.GetType().Name}:{ex.Message};searchAdmissionAffected=false");
            _page.BindEtaQuickView(SearchEtaQuickView.Unavailable(_page.CurrentRunDraft.TargetMatchCount));
        }
    }

    private void RefreshNeowUiCatalog()
    {
        var stopwatch = Stopwatch.StartNew();
        RuntimeLog.Ui(
            $"Search UI catalog refresh started: profile={_runtime.Profile.ProfileId}; " +
            $"character={_page.CharacterKey.Serialized}; ascension={_page.Ascension}");
        try
        {
            CharacterIdentity character = CharacterIdentity.FromKey(_page.CharacterKey);
            // Candidate catalogs are seed-independent. Use one canonical probe
            // seed so partially edited Search seed text can never invalidate the
            // main-thread UI authority capture.
            string seed = new string(_runtime.Profile.SeedAlphabet[0], _runtime.Profile.SeedLength);
            RuntimeContextAuthoritySnapshot authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
                _runtime.Profile,
                seed,
                character,
                _page.Ascension,
                _runtime.Detection.DisplayVersion,
                playersCount: 1,
                playerSlotIndex: 0,
                predictionGameMode: WorldGameMode.Singleplayer,
                predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
            if (_runtime.Profile.ProfileId == RuntimeProfileId.Beta111)
            {
                Beta111CompatibilityAuthorityProbe.Result beta111Probe =
                    Beta111CompatibilityAuthorityProbe.Evaluate(authority);
                RuntimeLog.Info(beta111Probe.ToLogFields());
            }
            NeowSearchUiCatalog neowCatalog = NeowSearchUiCatalog.FromAuthority(
                _runtime.Profile.ProfileId,
                _page.CharacterKey,
                authority.EffectAuthority);
            AncientSearchUiCatalog ancientCatalog = AncientSearchUiCatalog.FromAuthority(
                _runtime.Profile.ProfileId,
                _page.CharacterKey,
                authority.WorldAuthority);
            BossMapSearchUiCatalog bossMapCatalog = BossMapSearchUiCatalog.FromAuthority(
                _runtime.Profile.ProfileId,
                _page.Ascension,
                authority.WorldAuthority);
            RelicSequenceSearchUiCatalog relicSequenceCatalog = RelicSequenceSearchUiCatalog.FromAuthority(
                _runtime.Profile.ProfileId,
                authority.WorldAuthority);
            EventSequenceSearchUiCatalog eventSequenceCatalog = EventSequenceSearchUiCatalog.FromAuthority(
                _runtime.Profile.ProfileId,
                authority.WorldAuthority);
            if (_runtime.Profile.ProfileId == RuntimeProfileId.Beta111)
            {
                try { eventSequenceCatalog = eventSequenceCatalog with {
                    MorphicGroveScenario = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredInitialBasics(authority) }; }
                catch (Exception ex) { RuntimeLog.Warn("morphicGroveUiPoolUnavailable=" + ex.Message); }
            }
            CombatRewardSearchUiCatalog combatRewardCatalog = CombatRewardSearchUiCatalog.FromAuthority(
                _runtime.Profile.ProfileId,
                authority.EffectAuthority, characterKey: _page.CharacterKey);
            ShopColorlessSearchUiCatalog shopColorlessCatalog = ShopColorlessSearchUiCatalog.FromAuthority(
                _runtime.Profile.ProfileId,
                authority.EffectAuthority);
            ModelKey[] presetVisualRelics = neowCatalog.RouteRelics
                .Concat(neowCatalog.OrdinaryRelics)
                .Concat(neowCatalog.BonesNeowRelics)
                .Concat(relicSequenceCatalog.AllCandidates)
                .Concat(authority.WorldAuthority?.AncientOptionRelicCatalog ?? Array.Empty<ModelKey>())
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .OrderBy(key => key.Serialized, StringComparer.Ordinal)
                .ToArray();

            NeowEffectAuthoritySnapshot? effectAuthority = authority.EffectAuthority;
            if (effectAuthority is not null)
            {
                RuntimeLog.Info(
                    $"searchCardCatalogAuthority=true;character={_page.CharacterKey.Serialized};" +
                    $"characterRewardPoolCount={effectAuthority.CharacterRewardPool?.Count ?? 0};" +
                    $"characterRewardPoolExact={effectAuthority.HasExactCharacterRewardPool.ToString().ToLowerInvariant()};" +
                    $"characterRewardIdentityPoolExact={effectAuthority.HasExactCharacterRewardIdentityPool.ToString().ToLowerInvariant()};" +
                    $"colorlessRewardPoolCount={effectAuthority.ColorlessRewardPool?.Count ?? 0};" +
                    $"colorlessRewardPoolExact={effectAuthority.HasExactColorlessRewardPool.ToString().ToLowerInvariant()};" +
                    $"otherCharacterPoolCount={effectAuthority.OtherCharacterPools?.Count ?? 0};" +
                    $"otherCharacterPoolsExact={effectAuthority.HasExactOtherCharacterPools.ToString().ToLowerInvariant()};" +
                    $"transformPoolCount={effectAuthority.TransformPool?.Count ?? 0};" +
                    $"transformPoolExact={effectAuthority.HasExactTransformPool.ToString().ToLowerInvariant()};" +
                    $"combatRewardUiCandidates={combatRewardCatalog.CardCandidates.Count};" +
                    $"neowCharacterCards={neowCatalog.CharacterCards.Count};otherCharacterCards={neowCatalog.OtherCharacterCards.Count};" +
                    $"colorlessCards={neowCatalog.ColorlessCards.Count};transformCards={neowCatalog.TransformCards.Count};");
            }

            WorldAuthoritySnapshot? world = authority.WorldAuthority;
            Beta109WorldGenerationSnapshot? modernWorld = world?.Beta109Generation;
            RuntimeLog.Info(
                $"searchDomainAuthority=true;character={_page.CharacterKey.Serialized};" +
                $"characterKnownVanilla={authority.Character.IsKnownVanilla.ToString().ToLowerInvariant()};" +
                $"vanillaNeowCatalogExact={(authority.VanillaNeowCatalogExact?.ToString().ToLowerInvariant() ?? "unknown")};" +
                $"modernNeowIdentityInputsExact={authority.HasExactModernNeowIdentityInputs.ToString().ToLowerInvariant()};" +
                $"beta111NeowIdentityAuthorityExact={authority.IsBeta111NeowIdentityAuthorityExact.ToString().ToLowerInvariant()};" +
                $"worldPresent={(world is not null).ToString().ToLowerInvariant()};" +
                $"worldGenerationHooksExact={(modernWorld?.WorldGenerationHooksExact.ToString().ToLowerInvariant() ?? "unknown")};" +
                $"broadNoUnknownHooks={(modernWorld?.NoUnknownHooksOrModifiers.ToString().ToLowerInvariant() ?? "unknown")};" +
                $"worldReplayInputsExact={(modernWorld?.HasExactReplayInputs.ToString().ToLowerInvariant() ?? "unknown")};");
            if (modernWorld is not null)
            {
                RuntimeLog.Info(
                    $"searchRelicCatalogAuthority=true;character={_page.CharacterKey.Serialized};" +
                    $"playerRelicBucketCount={modernWorld.PlayerRelicBuckets.Count};" +
                    $"playerRelicCapturedCount={modernWorld.PlayerRelicBuckets.Sum(bucket => bucket.OrderedRelics.Count)};" +
                    $"relicRarityAuthorityExact={modernWorld.RelicRarityAuthorityExact.ToString().ToLowerInvariant()};" +
                    $"relicShopEligibilityAuthorityExact={modernWorld.RelicShopEligibilityAuthorityExact.ToString().ToLowerInvariant()};" +
                    $"playerRelicPoolCompositionExact={modernWorld.PlayerRelicPoolCompositionExact.ToString().ToLowerInvariant()};" +
                    $"relicSequenceUiCandidates={relicSequenceCatalog.AllCandidates.Count};" +
                    $"relicSequenceUiLaneMappingExact={relicSequenceCatalog.LaneMappingExact.ToString().ToLowerInvariant()};" +
                    $"relicSequenceUiEvidence={relicSequenceCatalog.EvidenceCode};");
            }
            int ancientContextCount = modernWorld?.AncientEventContexts.Count ?? 0;
            int actAncientCount = modernWorld is not null
                ? modernWorld.OrderedActCatalog.Sum(act => act.OrderedAncients.Count)
                : world?.ActGroups?.SelectMany(group => group.Acts).Sum(act => act.Ancients.Count) ?? 0;
            int sharedAncientCount = modernWorld?.SharedAncients.Count ?? world?.SharedAncients?.Count ?? 0;
            int optionRelicCount = world?.AncientOptionRelicCatalog?.Count ?? 0;
            bool optionRelicCatalogExact = world?.AncientOptionRelicCatalogExact ?? false;
            RuntimeLog.Ui(
                $"Ancient UI authority captured: worldPresent={(world is not null).ToString().ToLowerInvariant()}; " +
                $"completeness={world?.Completeness.ToString() ?? "missing"}; " +
                $"actAncients={actAncientCount}; sharedAncients={sharedAncientCount}; " +
                $"eventContexts={ancientContextCount}; optionRelics={optionRelicCount}; " +
                $"optionRelicCatalogExact={optionRelicCatalogExact.ToString().ToLowerInvariant()}");
            if (world is not null && ancientContextCount == 0 && RuntimeProfilePolicies.IsModernCore(_runtime.Profile.ProfileId))
            {
                RuntimeLog.Warn(
                    $"Ancient UI authority has no modern event contexts: capture={world.CaptureDiagnosticCode}");
            }

            _page.BindNeowUiCatalog(neowCatalog);
            _page.BindAncientUiCatalog(ancientCatalog);
            _page.BindBossMapUiCatalog(bossMapCatalog);
            _page.BindRelicSequenceUiCatalog(relicSequenceCatalog);
            _page.BindCombatRewardUiCatalog(combatRewardCatalog);
            _page.BindEventSequenceUiCatalog(eventSequenceCatalog);
            _page.BindShopColorlessUiCatalog(shopColorlessCatalog);
            _page.BindPresetVisualRelicCatalog(presetVisualRelics);

            int ancientRows = ancientCatalog.Sections.Sum(section => section.Rows.Count);
            int ancientOptions = ancientCatalog.Sections
                .SelectMany(section => section.Rows)
                .Sum(row => row.Options.Count);
            int pendingRows = ancientCatalog.Sections
                .SelectMany(section => section.Rows)
                .Count(row => !row.IdentityAvailable || row.Options.Count == 0);
            RuntimeLog.Ui(
                $"Search UI catalog refresh completed: neowRoutes={neowCatalog.RouteRelics.Count}; " +
                $"ancientSections={ancientCatalog.Sections.Count}; ancientRows={ancientRows}; " +
                $"ancientOptions={ancientOptions}; pendingAncientRows={pendingRows}; " +
                $"bossMapSections={bossMapCatalog.Sections.Count}; " +
                $"bossMapVariants={bossMapCatalog.Sections.Sum(section => section.Variants.Count)}; " +
                $"relicSequenceCandidates={relicSequenceCatalog.AllCandidates.Count}; " +
                $"combatRewardCards={combatRewardCatalog.CardCandidates.Count}; " +
                $"combatRewardPotions={combatRewardCatalog.PotionCandidates.Count}; " +
                $"eventSequenceCandidates={eventSequenceCatalog.CandidatesByAct.Values.Sum(items => items.Count)}; " +
                $"shopColorlessU={shopColorlessCatalog.UncommonCandidates.Count};shopColorlessR={shopColorlessCatalog.RareCandidates.Count}; " +
                $"took={stopwatch.Elapsed.TotalMilliseconds:0.0}ms");
        }
        catch (Exception ex)
        {
            _page.BindNeowUiCatalog(NeowSearchUiCatalog.Empty(
                _runtime.Profile.ProfileId,
                _page.CharacterKey,
                "neow-ui-catalog-capture-failed:" + ex.GetType().Name));
            _page.BindAncientUiCatalog(AncientSearchUiCatalog.Empty(
                _runtime.Profile.ProfileId,
                _page.CharacterKey,
                "ancient-ui-catalog-capture-failed:" + ex.GetType().Name));
            _page.BindBossMapUiCatalog(BossMapSearchUiCatalog.Empty(
                _runtime.Profile.ProfileId,
                "boss-map-ui-catalog-capture-failed:" + ex.GetType().Name));
            _page.BindRelicSequenceUiCatalog(RelicSequenceSearchUiCatalog.Empty(
                _runtime.Profile.ProfileId,
                "relic-sequence-ui-catalog-capture-failed:" + ex.GetType().Name));
            _page.BindCombatRewardUiCatalog(CombatRewardSearchUiCatalog.Empty(
                _runtime.Profile.ProfileId,
                "combat-reward-ui-catalog-capture-failed:" + ex.GetType().Name));
            _page.BindEventSequenceUiCatalog(EventSequenceSearchUiCatalog.Empty(
                _runtime.Profile.ProfileId,
                "event-sequence-ui-catalog-capture-failed:" + ex.GetType().Name));
            _page.BindShopColorlessUiCatalog(ShopColorlessSearchUiCatalog.Empty(
                _runtime.Profile.ProfileId,
                "shop-colorless-ui-catalog-capture-failed:" + ex.GetType().Name));
            RuntimeLog.Error($"Search UI catalog refresh failed after {stopwatch.Elapsed.TotalMilliseconds:0.0}ms: {ex.GetType().Name}: {ex.Message}");
        }
    }


    private ExactSearchExecutionCompileResult CompileExactDraft(
        SearchDraft draft,
        SearchRunDraft runDraft,
        out ExactSearchEvaluationProjection evaluation,
        out RuntimeContextAuthoritySnapshot authority)
    {
        if (!TryCompileDraftSemantics(draft, out CompiledSearch? compiled, out authority, out string semanticIssue) || compiled is null)
        {
            evaluation = ExactSearchEvaluationProjection.Empty;
            return ExactSearchExecutionCompileResult.Rejected(SearchDisposition.Unsupported, semanticIssue);
        }

        SearchFeasibilityResult feasibility = SearchFeasibilityAnalyzer.Analyze(compiled);
        if (feasibility.IsImpossible)
        {
            evaluation = ExactSearchEvaluationProjection.Empty;
            return ExactSearchExecutionCompileResult.Rejected(
                SearchDisposition.NoMatch,
                _page.FormatSearchImpossibility(feasibility.Proof));
        }

        ExactSearchExecutionCompileResult execution = ExactSearchExecutionRequestFactory.Compile(
            compiled,
            ToRunOptions(runDraft));
        evaluation = execution.Plan?.Evaluation ?? ExactSearchEvaluationProjection.Empty;
        return execution;
    }

    private static SearchRunOptions ToRunOptions(SearchRunDraft runDraft)
    {
        ArgumentNullException.ThrowIfNull(runDraft);
        return new SearchRunOptions(
            runDraft.StartSeed,
            runDraft.ScanCount,
            runDraft.TargetMatchCount,
            runDraft.WorkerCount,
            IncludeDiagnostics: true);
    }

    private bool TryCompileDraftSemantics(
        SearchDraft draft,
        out CompiledSearch? compiled,
        out RuntimeContextAuthoritySnapshot authority,
        out string issue) =>
        TryCompileDraftSemanticsForContext(draft, _page.CharacterKey, _page.Ascension, out compiled, out authority, out issue);

    private bool TryCompileDraftSemanticsForContext(
        SearchDraft draft,
        ModelKey characterKey,
        int ascension,
        out CompiledSearch? compiled,
        out RuntimeContextAuthoritySnapshot authority,
        out string issue)
    {
        compiled = CompileAuthoredDraft(draft, _runtime, characterKey, ascension, out authority);
        issue = string.Empty;
        if (compiled.Status == QueryNormalizationStatus.Impossible)
            issue = "SearchQueryImpossible:" + string.Join(",", compiled.Normalization.Diagnostics);
        return true;
    }

    // Same existing authoring adapter for the old page and the new workbench.
    // Captures read-only authority; no Search session, cursor or persistence mutation.
    internal static CompiledSearch CompileAuthoredDraft(SearchDraft draft, ModRuntimeSnapshot runtime,
        ModelKey characterKey, int ascension, out RuntimeContextAuthoritySnapshot authority)
    {
        BuildSearchQuery(draft, out SearchQuery query, out AncientOptionConditionProfile ancientAssumptions);
        query = query with { TransformationAggregate = draft.TransformationAggregate };
        if ((draft.MorphicGroveContainsCard ?? draft.MorphicGroveSecondCard) is { } card)
            query = query with { EventResultConditions = query.EventResultConditions.Append(new EventResultSearchCondition(
                EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, card) {
                MorphicGroveSecondCard = draft.MorphicGroveContainsCard.HasValue ? draft.MorphicGroveSecondCard : null
            }).ToArray() };
        return CompileAuthoredQuery(query, ancientAssumptions, runtime, characterKey, ascension, out authority);
    }

    // Shared compilation seam; neither caller needs the legacy SearchPage UI.
    internal static CompiledSearch CompileAuthoredQuery(SearchQuery query, AncientOptionConditionProfile ancientAssumptions,
        ModRuntimeSnapshot runtime, ModelKey characterKey, int ascension, out RuntimeContextAuthoritySnapshot authority)
    {
        CharacterIdentity character = CharacterIdentity.FromKey(characterKey);
        string contextAuthoritySeed = new string(runtime.Profile.SeedAlphabet[0], runtime.Profile.SeedLength);
        authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
            runtime.Profile, contextAuthoritySeed, character, ascension, runtime.Detection.DisplayVersion,
            playersCount: 1, playerSlotIndex: 0,
            predictionGameMode: WorldGameMode.Singleplayer,
            predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
        SearchContext context = SearchContextFactory.From(
            runtime.Profile.ProfileId, characterKey, ascension, authority, runtime.Detection, ancientAssumptions);
        var capturedAuthority = authority;
        query = query with { EventResultConditions = query.EventResultConditions.Select(c =>
            EventResultTransformSemantics.IsTransform(c.Kind)
                ? c with { MorphicGroveScenario = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredEventResult(capturedAuthority, c.Kind) }
                : c).ToArray() };
        if (query.TransformationAggregate is { } aggregate)
            query = query with { TransformationAggregate = aggregate with { EventScenario = aggregate.UsesEvents
                ? CaptureAggregateEventPremises(authority, aggregate) : null } };
        RolltheSpire2.Infrastructure.Snapshots.ProductionSearchReplay.ExportIfRequested(context, query);
        return SearchCompiler.Compile(query, context);
    }

    private static RolltheSpire2.Core.Prediction.MorphicGroveScenario CaptureAggregateEventPremises(
        RuntimeContextAuthoritySnapshot authority, TransformationAggregateCondition aggregate)
    {
        var source = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredBasics(authority, aggregate.MorphicGrove || aggregate.TrialNondescript ? 2 : 1);
        return new(source.Authority, source.Premises with { EventOccurrenceBasis =
            "Authored transformation events: " + (aggregate.MorphicGrove ? "Morphic Grove / Group; " : "") +
            (aggregate.AromaOfChaos ? "Aroma of Chaos / LetGo; " : "") + (aggregate.WhisperingHollow ? "Whispering Hollow / Hug; " : "") +
            (aggregate.Symbiote ? "Symbiote / transform; " : "") + (aggregate.TrialNondescript ? "Trial / require Nondescript case and two transforms; " : "") +
            "legal initial Strike/Defend targets and unchanged local pool/RNG premises at each entry; occurrence and deck history not proven." }, source.Targets);
    }

    private void Start(SearchDraft draft, SearchRunDraft runDraft)
    {
        RuntimeLog.Info($"searchStartup=true;phase=SearchRequested;processId={Environment.ProcessId}");
        PersistWorkspaceSnapshot();
        StopAndReleasePrevious();
        _page.BindSearchAuthorityWarnings(Array.Empty<string>());
        _panelCloseCancellationRequested = false;
        _completedSegmentScanned = 0L;
        _completedSegmentMatches = 0;
        _wrappedThisCycle = false;
        _cycleTargetMatches = Math.Max(1, runDraft.TargetMatchCount);

        SearchRunDraft effectiveRunDraft = runDraft with
        {
            StartSeed = _persistence.CurrentNextCursorSeed,
            ScanCount = long.MaxValue
        };
        _cycleDraft = draft;
        _cycleRunDraft = effectiveRunDraft;
        _activeUsesPersistentCursor = true;
        if (_activeUsesPersistentCursor &&
            Beta110SeedCodec.TryParseOrdinal(effectiveRunDraft.StartSeed, out ulong cycleStart, out _, out _))
        {
            _cycleStartOrdinal = cycleStart;
        }
        else
        {
            _cycleStartOrdinal = 0UL;
        }

        if (!TryCompileAndStartSegment(draft, effectiveRunDraft, clearExistingResults: true))
        {
            _activeUsesPersistentCursor = false;
            _cycleDraft = null;
            _cycleRunDraft = null;
            return;
        }
    }

    private bool TryCompileAndStartSegment(
        SearchDraft draft,
        SearchRunDraft runDraft,
        bool clearExistingResults)
    {
        IProductionSearchSession? createdSession = null;
        long compileStarted=System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            ExactSearchExecutionCompileResult compiled = CompileExactDraft(
                draft,
                runDraft,
                out ExactSearchEvaluationProjection evaluation,
                out RuntimeContextAuthoritySnapshot authority);
            if (!compiled.Success || compiled.Plan is null)
            {
                _page.ShowError(compiled.Issue);
                _setGlobalStatus(GlobalStatusKind.Warning, _page.Localize(Ui1TextKey.SearchStatusUnavailable), compiled.Issue);
                RuntimeLog.Error($"rewriteR3SearchCompileRejected={compiled.Disposition}:{compiled.Issue}");
                string rejectedSessionId = Guid.NewGuid().ToString("N");
                _diagnosticSink.TryWrite(RuntimePredictionDiagnosticEvent.Create(
                    rejectedSessionId,
                    _runtime.Profile.ProfileId.ToString(),
                    runDraft.StartSeed,
                    RuntimePredictionDiagnosticLevel.Warning,
                    "Search",
                    "Compile",
                    RuntimePredictionDiagnosticStatus.Failed,
                    compiled.Issue,
                    new Dictionary<string, object?>
                    {
                        ["disposition"] = compiled.Disposition.ToString(),
                        ["requiresWorldDomain"] = evaluation.RequiresWorldDomain,
                        ["worldSnapshotFingerprint"] = authority.WorldSnapshotFingerprint
                    }));
                return false;
            }

            RuntimeLog.Info($"searchStartup=true;phase=QueryCompiled;elapsedMs={System.Diagnostics.Stopwatch.GetElapsedTime(compileStarted).TotalMilliseconds:F4};processId={Environment.ProcessId}");
            string diagnosticSessionId = Guid.NewGuid().ToString("N");
            string diagnosticStartSeed = compiled.Plan.CanonicalStartSeed;
            using var logScope = RuntimeLog.SearchScope(diagnosticSessionId);
            _diagnosticSink.TryWrite(RuntimePredictionDiagnosticReporter.SearchSessionStarted(
                diagnosticSessionId,
                compiled.Plan.ProfileId.ToString(),
                compiled.Plan.CanonicalStartSeed,
                compiled.Plan.Authority,
                compiled.Plan.ScanCount,
                compiled.Plan.TargetMatchCount,
                compiled.Plan.WorkerCount));

            ProductionSearchSession familySession = FamilyExecutionCoordinator.Start(
                compiled.Plan,
                queueCapacity: 64,
                diagnosticSink: _diagnosticSink,
                gpuAvailable: _predictionSettings.NormalizeWorkshopSearchModePreference() == Beta110RelicComputeBackendPreference.Cpu ? false : null);
            createdSession = familySession;
            BindAndLogSelectedEta(familySession);
            _page.BindSearchAuthorityWarnings(Array.Empty<string>());

            // Product contract: old results survive compile/session-start failure and are
            // cleared only after a real SearchSession has been constructed successfully.
            string queryFingerprint = compiled.Plan.CompiledSearch.SemanticFingerprint;
            if (clearExistingResults)
            {
                _page.ClearResults();
                _persistence.ResetWorkspaceForSuccessfulStart(queryFingerprint);
            }

            _session = createdSession;
            _diagnosticSessionId = diagnosticSessionId;
            _diagnosticStartSeed = diagnosticStartSeed;
            _activeQueryFingerprint = queryFingerprint;
            _page.RefreshGpuBackendControls();
            _page.SetRunning(true);
            _setGlobalStatus(GlobalStatusKind.Busy, _page.Localize(Ui1TextKey.SearchStatusSearching), compiled.Plan.CanonicalStartSeed);
            if (clearExistingResults)
                RecordSuccessfulRunTemporaryPreset(draft, compiled.Plan.CompiledSearch);

            if (_runtime.IsCompatibilityFallback)
            {
                RuntimeLog.Warn(
                    $"Search compatibility fallback: detected={_runtime.Detection.DisplayVersion}; " +
                    $"using={_runtime.Compatibility.ReferenceVersion}; profile={compiled.Plan.ProfileId}; " +
                    $"plan={compiled.Plan.SnapshotFingerprint}");
            }
            else if (_runtime.IsPendingRuntimeValidation)
            {
                RuntimeLog.Warn(
                    $"Search runtime validation pending; failClosed=true; " +
                    $"detected={_runtime.Detection.DisplayVersion}; profile={compiled.Plan.ProfileId}; " +
                    $"plan={compiled.Plan.SnapshotFingerprint}");
            }

            RuntimeLog.Info(
                $"rewriteR3SearchStarted=true;profile={compiled.Plan.ProfileId};start={compiled.Plan.CanonicalStartSeed};" +
                $"count={compiled.Plan.ScanCount};workers={compiled.Plan.WorkerCount};" +
                $"customStartSeedOverride={(!_activeUsesPersistentCursor).ToString().ToLowerInvariant()};" +
                $"persistentCursor={_activeUsesPersistentCursor.ToString().ToLowerInvariant()};" +
                $"persistentNext={_persistence.CurrentNextCursorSeed};" +
                $"executionSpine=FamilyExecution;familyIds={string.Join(",", familySession.FamilyIds)};" +
                "terminal=ProductionExact;legacyFallback=false");
            createdSession = null; // controller now owns the live session
            return true;
        }
        catch (Exception ex)
        {
            _page.ShowError(_page.Localize(Ui1TextKey.SearchStatusFailed));
            _setGlobalStatus(GlobalStatusKind.Error, _page.Localize(Ui1TextKey.SearchStatusFailed), runDraft.StartSeed);
            RuntimeLog.Error($"rewriteR3SearchStartFailed={ex.GetType().Name}:{ex.Message}");
            _diagnosticSink.TryWrite(RuntimePredictionDiagnosticEvent.Create(
                Guid.NewGuid().ToString("N"),
                _runtime.Profile.ProfileId.ToString(),
                runDraft.StartSeed,
                RuntimePredictionDiagnosticLevel.Error,
                "Search",
                "SessionStart",
                RuntimePredictionDiagnosticStatus.Failed,
                "SearchInitializationFailed:" + ex.GetType().Name,
                new Dictionary<string, object?>
                {
                    ["message"] = ex.Message
                }));
            return false;
        }
    }

    private void Stop()
    {
        _session?.Cancel();
        _setGlobalStatus(GlobalStatusKind.Warning, _page.Localize(Ui1TextKey.SearchStatusStopping), string.Empty);
    }

    internal bool HasActiveSearch => _session is not null;

    internal void CancelForPanelClose()
    {
        _pendingPresetLoad = null;
        _presetLoadCancellationRequested = false;
        _discardActiveSessionCandidates = false;
        PersistWorkspaceSnapshot();
        CheckpointCursorFromSession(_session, flush: true, allowWrapCommit: false);
        IProductionSearchSession? session = _session;
        if (session is not null)
        {
            _panelCloseCancellationRequested = true;
            session.Cancel();
            _page.SetRunning(false);
            RuntimeLog.Info("searchPanelCloseCancellationRequested=true;reason=PanelClosed;mainThreadWait=false");
        }
        _persistence.FlushWorkspace();
    }

    internal void CancelForAppShutdown()
    {
        _pendingPresetLoad = null;
        _presetLoadCancellationRequested = false;
        _discardActiveSessionCandidates = false;
        PersistWorkspaceSnapshot();
        CheckpointCursorFromSession(_session, flush: true, allowWrapCommit: false);
        IProductionSearchSession? session = _session;
        if (session is null) return;
        _panelCloseCancellationRequested = true;
        session.Cancel();
        RuntimeLog.Info("searchShutdownCancellationRequested=true;reason=AppShellExit");
    }

    private void Poll()
    {
        IProductionSearchSession? session = _session;
        if (session is null)
        {
            _page.RefreshGpuBackendControls();
            return;
        }

        int drained = 0;
        while (drained < 16 && session.TryReadCandidate(out SearchCandidate? candidate))
        {
            if (candidate is not null)
                ConsumeCandidate(candidate);
            drained++;
        }

        CheckpointCursorFromSession(session, flush: false, allowWrapCommit: false);
        SearchProgressSnapshot segmentProgress = session.GetProgress();
        bool completed = session.Completion.IsCompleted;
        if (completed)
        {
            segmentProgress = session.GetProgress();
            // A terminal session may still have more than the normal per-frame drain
            // budget buffered. Drain it before detaching the session so accepted Exact
            // results are never stranded in the channel.
            while (session.TryReadCandidate(out SearchCandidate? finalCandidate))
            {
                if (finalCandidate is not null)
                    ConsumeCandidate(finalCandidate);
            }
        }
        // A terminal receipt can precede asynchronous family/owner cleanup. Keep
        // the session attached (and Search unavailable) until it has fully ended.
        if (segmentProgress.State != SearchRunState.Running && !completed) return;
        SearchProgressSnapshot displayProgress = AggregateProgress(segmentProgress);
        if (_panelCloseCancellationRequested)
            _page.SetRunning(false);
        else
            _page.UpdateProgress(displayProgress);
        if (segmentProgress.State == SearchRunState.Running)
        {
            // SearchPage projects UI-only live ETA from this same completed-root
            // rolling speed. The selected session's Cost/probability stays frozen.
            if (!_panelCloseCancellationRequested)
            {
                _setGlobalStatus(
                    GlobalStatusKind.Busy,
                    _page.Localize(Ui1TextKey.SearchStatusSearching),
                    $"{displayProgress.ScannedCount:N0} · {displayProgress.MatchCount:N0}/{displayProgress.TargetMatchCount:N0}");
            }
            return;
        }

        SearchDiagnosticSummarySnapshot diagnosticSummary = session.GetDiagnosticSummary();
        _diagnosticSink.TryWrite(RuntimePredictionDiagnosticReporter.SearchSessionCompleted(
            _diagnosticSessionId,
            _runtime.Profile.ProfileId.ToString(),
            _diagnosticStartSeed,
            diagnosticSummary,
            segmentProgress.ScannedCount,
            segmentProgress.MatchCount,
            segmentProgress.State.ToString(),
            segmentProgress.ElapsedSeconds));
        {
            RuntimeLog.Info(
                $"searchPublicationState=true;logSession={_diagnosticSessionId};state={segmentProgress.State};publishedMatches={segmentProgress.MatchCount};scope=UIReceipt");
        }

        // The terminal state is a safe cursor transaction boundary. For GPU paths
        // the session intentionally updates its safe ordinal only from backend
        // terminal/resume authority, never from pre-Exact progress callbacks.
        CheckpointCursorFromSession(session, flush: true, allowWrapCommit: false);

        if (TryStartWrapContinuation(session, segmentProgress))
            return;

        CheckpointCursorFromSession(session, flush: true, allowWrapCommit: true);
        SearchProgressSnapshot finalProgress = AggregateProgress(segmentProgress);
        GlobalStatusKind kind = segmentProgress.State switch
        {
            SearchRunState.Completed => GlobalStatusKind.Idle,
            SearchRunState.Cancelled => GlobalStatusKind.Warning,
            _ => GlobalStatusKind.Error
        };
        string statusText = segmentProgress.State switch
        {
            SearchRunState.Completed => _page.Localize(Ui1TextKey.SearchStatusCompleted),
            SearchRunState.Cancelled => _page.Localize(Ui1TextKey.SearchStatusCancelled),
            _ => _page.Localize(Ui1TextKey.SearchStatusFailed)
        };
        _setGlobalStatus(kind, statusText, $"{finalProgress.ScannedCount:N0} · {finalProgress.MatchCount:N0}");
        _page.UpdateProgress(finalProgress);
        _persistence.FlushWorkspace();
        _persistence.FlushCursor();

        if (ReferenceEquals(_session, session))
        {
            _session = null;
            _ = session.DisposeAsync();

            // Stage-scoped durable evidence may have become usable during this run,
            // even when the enclosing Search was cancelled. Re-preview ETA only after
            // the real session is detached so the UI shows what the next Production
            // planning cycle can price. This remains presentation-only and cannot gate Search.
            if (!_panelCloseCancellationRequested && !_presetLoadCancellationRequested)
                RefreshProbabilityPresentation();
        }
        RuntimeLog.Info(
            $"persistentSearchCycleFinished=true;state={segmentProgress.State};scanned={finalProgress.ScannedCount};" +
            $"matches={finalProgress.MatchCount};target={_cycleTargetMatches};wrapped={_wrappedThisCycle.ToString().ToLowerInvariant()};" +
            $"cursor={_persistence.DescribeCursor()}");
        if (_presetLoadCancellationRequested)
        {
            PendingPresetLoad? pending = _pendingPresetLoad;
            _pendingPresetLoad = null;
            _presetLoadCancellationRequested = false;
            _discardActiveSessionCandidates = false;
            RuntimeLog.Info(
                $"searchPresetLoadCancellationCompleted=true;oldSessionDisposed=true;pendingCommit={(pending is not null).ToString().ToLowerInvariant()}");
            if (pending is not null)
                CommitPresetLoad(pending, cancelledActiveSearch: true);
        }
        ResetCycleState();
    }

    private void ConsumeCandidate(SearchCandidate candidate)
    {
        if (_discardActiveSessionCandidates)
            return;
        _page.AddCandidate(candidate, _activeQueryFingerprint);
        _persistence.AppendResult(candidate, _activeQueryFingerprint);
        RuntimeLog.Detail($"MATCH seed={candidate.Seed}");
        if (SearchOperationalLogPolicy.VerboseTraceEnabled)
        {
            RuntimeLog.Info(
                $"rewriteR3SearchCandidate={candidate.Seed};profile={candidate.ProfileId};routes={string.Join("|", candidate.MatchedRouteIds)}");
        }
    }

    private bool TryStartWrapContinuation(IProductionSearchSession completedSession, SearchProgressSnapshot progress)
    {
        if (!_activeUsesPersistentCursor || _panelCloseCancellationRequested || _presetLoadCancellationRequested || _wrappedThisCycle ||
            progress.State != SearchRunState.Completed || _cycleDraft is null || _cycleRunDraft is null)
            return false;

        int totalMatches = _completedSegmentMatches + progress.MatchCount;
        if (totalMatches >= _cycleTargetMatches || _cycleStartOrdinal == 0UL)
            return false;
        if (!completedSession.TryGetSafeNextOrdinal(out ulong safe) || safe < Beta110SeedCodec.SpaceSize)
            return false;

        _completedSegmentScanned = checked(_completedSegmentScanned + progress.ScannedCount);
        _completedSegmentMatches = totalMatches;
        _persistence.CommitEndOfSpaceWrap();
        _persistence.FlushCursor();
        _wrappedThisCycle = true;

        if (ReferenceEquals(_session, completedSession))
        {
            _session = null;
        }
        _ = completedSession.DisposeAsync();

        int remainingMatches = Math.Max(1, _cycleTargetMatches - _completedSegmentMatches);
        long secondSegmentCount = checked((long)_cycleStartOrdinal);
        SearchRunDraft continuationRun = _cycleRunDraft with
        {
            StartSeed = Beta110SeedCodec.FormatOrdinal(0UL),
            ScanCount = secondSegmentCount,
            TargetMatchCount = remainingMatches
        };
        RuntimeLog.Info(
            $"persistentSearchWrapContinuation=true;segmentAEnd={Beta110SeedCodec.SpaceSize};" +
            $"segmentBStart=0;segmentBCount={secondSegmentCount};remainingMatches={remainingMatches};" +
            $"cycleStopOrdinal={_cycleStartOrdinal}");
        if (TryCompileAndStartSegment(_cycleDraft, continuationRun, clearExistingResults: false))
            return true;

        // Fail-soft: Segment A results remain visible/persisted even if the wrap
        // continuation cannot be constructed for an unexpected authority reason.
        _persistence.FlushWorkspace();
        return false;
    }

    private SearchProgressSnapshot AggregateProgress(SearchProgressSnapshot progress)
    {
        if (_completedSegmentScanned == 0L && _completedSegmentMatches == 0)
            return progress with { TargetMatchCount = _cycleTargetMatches > 0 ? _cycleTargetMatches : progress.TargetMatchCount };
        return progress with
        {
            ScannedCount = checked(_completedSegmentScanned + progress.ScannedCount),
            MatchCount = checked(_completedSegmentMatches + progress.MatchCount),
            TargetMatchCount = _cycleTargetMatches
        };
    }

    private void CheckpointCursorFromSession(IProductionSearchSession? session, bool flush, bool allowWrapCommit)
    {
        if (!_activeUsesPersistentCursor || session is null)
            return;
        if (!session.TryGetSafeNextOrdinal(out ulong safe))
            return;
        if (safe < Beta110SeedCodec.SpaceSize)
        {
            _persistence.ObserveSafeNextCursor(safe);
        }
        else if (allowWrapCommit && safe == Beta110SeedCodec.SpaceSize && !_wrappedThisCycle)
        {
            _persistence.CommitEndOfSpaceWrap();
            _wrappedThisCycle = true;
        }
        if (flush)
            _persistence.FlushCursor();
    }

    private void PersistWorkspaceSnapshot()
    {
        try
        {
            SearchDraft stableDraft = _page.CurrentDraft;
            SearchRunDraft stableRunDraft = _page.CurrentRunDraft with
            {
                StartSeed = _persistence.CurrentNextCursorSeed,
                ScanCount = long.MaxValue
            };
            _persistence.SaveWorkspaceDraft(_page.CharacterKey, _page.Ascension, stableDraft, stableRunDraft);
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn($"searchWorkspaceDraftCaptureFailed=true;failSoft=true;issue={ex.GetType().Name}:{ex.Message}");
        }
    }

    private void ResetCycleState()
    {
        _activeUsesPersistentCursor = false;
        _panelCloseCancellationRequested = false;
        _cycleStartOrdinal = 0UL;
        _completedSegmentScanned = 0L;
        _completedSegmentMatches = 0;
        _cycleTargetMatches = 0;
        _wrappedThisCycle = false;
        _cycleDraft = null;
        _cycleRunDraft = null;
        _activeQueryFingerprint = string.Empty;
    }

    private static string ProductionAncientOptionAuthority(
        Beta109WorldGenerationSnapshot? generation,
        NeowSearchFilter filter,
        bool runtimeAuthorityExact)
    {
        bool requested = filter.AncientOptionFilters.Any(item => !item.IsEmpty) ||
                         filter.AncientBranchConditions.Any(item => item.OptionAny.Count > 0);
        if (!requested) return "NotEvaluatedByPolicy";
        if (generation is null) return "AuthorityMissing";
        IEnumerable<ModelKey> branchEntries = filter.AncientBranchConditions
            .Where(item => item.OptionAny.Count > 0)
            .Select(item => item.AncientKey);
        IEnumerable<ModelKey> legacyEntries = generation.AncientEventContexts
            .Where(item => filter.AncientOptionFilters.Any(condition => !condition.IsEmpty && condition.Act == item.Act))
            .Select(item => item.AncientKey);
        ModelKey[] entries = branchEntries
            .Concat(legacyEntries)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        return entries.Length > 0 && entries.All(entry =>
            HasExactAncientOptionAuthority(
                generation,
                entry,
                runtimeAuthorityExact,
                usePerSeedProjectedAuthority: true))
            ? "Exact"
            : "AuthorityMissing";
    }

    private static string ProductionSeaGlassAuthority(
        Beta109WorldGenerationSnapshot? generation,
        NeowSearchFilter filter,
        bool runtimeAuthorityExact)
    {
        bool requested = filter.AncientSeaGlassTargetFilters.Any(item => !item.IsEmpty) ||
                         filter.AncientBranchConditions.Any(item => item.SeaGlassTargetAny.Count > 0);
        if (!requested) return "NotEvaluatedByPolicy";
        if (generation is null || !generation.HasExactOrobasSeaGlassTargetAuthority)
            return "AuthorityMissing";
        Beta109AncientEventContextSnapshot? context = generation.AncientEventContexts.FirstOrDefault(item =>
            string.Equals(NormalizeAuthorityEntry(item.AncientKey.Entry), "OROBAS", StringComparison.Ordinal));
        return context is not null &&
               context.UnlockedCharacterSourceOrderExact &&
               context.UnlockedCharacters.Any(key =>
                   key.IsValid &&
                   key.Category == BaseGameModelKeys.Categories.Character &&
                   key != context.CharacterKey) &&
               HasExactAncientOptionAuthority(
                   generation,
                   context.AncientKey,
                   runtimeAuthorityExact,
                   usePerSeedProjectedAuthority: true)
            ? "Exact"
            : "AuthorityMissing";
    }

    private static bool HasExactAncientOptionAuthority(
        Beta109WorldGenerationSnapshot generation,
        ModelKey ancientKey,
        bool runtimeAuthorityExact,
        bool usePerSeedProjectedAuthority = false)
    {
        bool generatorAuthority = usePerSeedProjectedAuthority
            ? generation.AllowsAncientOptionFastSearch(ancientKey.Entry)
            : generation.AllowsAncientOptionProductionExact(ancientKey.Entry);
        if (!runtimeAuthorityExact || !generatorAuthority)
        {
            return false;
        }

        string normalized = NormalizeAuthorityEntry(ancientKey.Entry);
        bool simpleConditionAuthority = normalized is "PAEL" or "OROBAS" or "TEZCATARA" or
            "NONUPEIPE" or "TANX" or "DARV";
        foreach (Beta109AncientEventContextSnapshot context in generation.AncientEventContexts)
        {
            if (context.AncientKey != ancientKey ||
                !context.EventContextExact ||
                !context.EventRngRootExact ||
                !context.ModifierFactsExact ||
                context.HookDecision == Beta109HookDecision.Unknown)
            {
                continue;
            }

            if (context.HookDecision == Beta109HookDecision.Deny)
            {
                if (!context.DynamicFactsExact || context.Catalog is null) continue;
                if (context.Catalog.Pool("wrapper.proceed").Count == 1) return true;
                continue;
            }

            if (!context.DynamicFactsExact && !simpleConditionAuthority) continue;
            if (context.Catalog is null ||
                !context.Catalog.CatalogExact ||
                context.Catalog.Capability != ExpectedAncientOptionCapability(normalized) ||
                context.Catalog.Pools.Any(pool =>
                    pool.SourceOrdinal < 0 || !pool.OrderExact || !pool.FilterResultExact))
            {
                continue;
            }
            return true;
        }
        return false;
    }

    private static Beta109AncientOptionSupport ExpectedAncientOptionCapability(string normalized) => normalized switch
    {
        "DARV" or "OROBAS" => Beta109AncientOptionSupport.PureInstanceProjection,
        "PAEL" or "TEZCATARA" or "NONUPEIPE" or "TANX" => Beta109AncientOptionSupport.DeckFactPredicate,
        "VAKUU" => Beta109AncientOptionSupport.PureShuffleTake,
        "NEOW" => Beta109AncientOptionSupport.PureBranch,
        _ => Beta109AncientOptionSupport.Unknown
    };

    private static string NormalizeAuthorityEntry(string entry) =>
        new(entry.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private void StopAndReleasePrevious()
    {
        if (_session is null)
        {
            return;
        }
        IProductionSearchSession previous = _session;
        CheckpointCursorFromSession(previous, flush: true, allowWrapCommit: false);
        _session = null;
        previous.Cancel();
        _ = previous.DisposeAsync();
    }

    private static void BuildSearchQuery(
        SearchDraft draft,
        out SearchQuery query,
        out AncientOptionConditionProfile ancientAssumptions)
    {
        NeowRouteSearchCondition? openingRoute = draft.NeowRouteDraft.RouteRelicKey is { } routeKey
            ? new NeowRouteSearchCondition(routeKey)
            : null;
        OpeningRouteRelicRequirement? openingRouteRelics =
            openingRoute is { IsValid: true } route && draft.NeowRouteDraft.RequiredBonesRelics.Count > 0
                ? new OpeningRouteRelicRequirement(
                    route.RouteRelicKey,
                    draft.NeowRouteDraft.RequiredBonesRelics.ToArray(),
                    draft.NeowRouteDraft.BonesOrderMode)
                : null;

        IReadOnlyList<VariantScopedBossBranch> variantBranches = draft.BossMapDraft.CatalogBound
            ? BuildVariantScopedBossBranches(draft.BossMapDraft)
            : Array.Empty<VariantScopedBossBranch>();

        IReadOnlyList<ActModelKeySetFilter> legacyBossFilters = draft.BossMapDraft.CatalogBound
            ? Array.Empty<ActModelKeySetFilter>()
            : BuildActFilter(
                draft.BossAct,
                draft.BossAny,
                draft.BossAll,
                draft.BossBan,
                BaseGameModelKeys.Categories.Encounter);
        IReadOnlyList<ActOrdinalModelKeySetFilter> legacyBossOrdinalFilters = draft.BossMapDraft.CatalogBound
            ? BuildBossOrdinalFilters(draft.BossMapDraft)
            : BuildActOrdinalFilter(
                draft.BossAct,
                draft.BossOrdinal,
                draft.BossOrdinalAny,
                draft.BossOrdinalAll,
                draft.BossOrdinalBan,
                BaseGameModelKeys.Categories.Encounter);

        LegacyWorldSemanticConstraints legacyWorld = draft.BossMapDraft.CatalogBound
            ? LegacyWorldSemanticConstraints.Empty
            : new LegacyWorldSemanticConstraints(
                legacyBossFilters,
                legacyBossOrdinalFilters,
                Array.Empty<ActModelKeySetFilter>(),
                Array.Empty<ActModelKeySetFilter>(),
                Array.Empty<ActModelKeySetFilter>());

        AncientSearchBranchCondition[] ancientBranches = draft.AncientMatrixDraft.Rows
            .Where(row => row.IsActive)
            .Select(row => new AncientSearchBranchCondition(
                row.Act,
                row.AncientKey,
                row.SelectedOptionKeys,
                row.SelectedOptionKeys.Any(key => string.Equals(key.Entry, "SEA_GLASS", StringComparison.Ordinal))
                    ? row.SeaGlassTargetKeys
                    : Array.Empty<ModelKey>()))
            .ToArray();

        IReadOnlyList<RelicSequenceSearchCondition> relicSequence =
            draft.RelicSequenceDraft.Count > 0 || string.IsNullOrWhiteSpace(draft.RelicSequenceConditions)
                ? draft.RelicSequenceDraft
                : ParseRelicSequenceConditions(draft.RelicSequenceConditions);
        IReadOnlyList<EventSequenceSearchCondition> eventSequence =
            draft.EventSequenceDraft.Count > 0 || string.IsNullOrWhiteSpace(draft.EventSequenceConditions)
                ? draft.EventSequenceDraft
                : ParseEventSequenceConditions(draft.EventSequenceConditions);

        CombatCardRewardSequenceSearchCondition? cardRewardSequence = null;
        if (draft.CombatRewardDraft.Cards.HasAnyValue)
        {
            ModelKey?[] cardSlots = draft.CombatRewardDraft.Cards.Slots
                .Take(Math.Clamp(draft.CombatRewardDraft.Cards.Count, 1, 3))
                .ToArray();
            cardRewardSequence = new CombatCardRewardSequenceSearchCondition(
                draft.CombatRewardDraft.Cards.Count,
                draft.CombatRewardDraft.Cards.OrderMode,
                cardSlots);
        }

        CombatPotionRewardSequenceSearchCondition? potionRewardSequence = null;
        if (draft.CombatRewardDraft.Potions.HasAnyValue)
        {
            CombatPotionRewardSlotSearchCondition[] potionSlots = draft.CombatRewardDraft.Potions.Slots
                .Take(Math.Clamp(draft.CombatRewardDraft.Potions.Count, 1, 3))
                .Select(slot => new CombatPotionRewardSlotSearchCondition(
                    slot.Requirement ?? CombatPotionSlotRequirement.Neutral,
                    slot.Requirement == CombatPotionSlotRequirement.DropSpecific ? slot.PotionKey : null))
                .ToArray();
            potionRewardSequence = new CombatPotionRewardSequenceSearchCondition(
                draft.CombatRewardDraft.Potions.Count,
                draft.CombatRewardDraft.Potions.OrderMode,
                potionSlots);
        }

        ancientAssumptions = new AncientOptionConditionProfile(
            TezcataraHasBasicStrike: draft.TezcataraHasBasicStrike,
            NonupeipeSwiftEnchantableAtLeast4: draft.NonupeipeSwiftEnchantableAtLeast4,
            TanxInstinctEnchantableAtLeast3: draft.TanxInstinctEnchantableAtLeast3,
            PaelGoopyDefendCardsAtLeast3: draft.PaelGoopyDefendCardsAtLeast3,
            PaelAllowLegionNoEventPet: draft.PaelAllowLegionNoEventPet,
            PaelRemovableCardsAtLeast5: draft.PaelRemovableCardsAtLeast5,
            OrobasArchaicToothConditionMet: draft.OrobasArchaicToothConditionMet,
            OrobasTouchOfOrobasConditionMet: draft.OrobasTouchOfOrobasConditionMet,
            DarvAllowPandorasBoxRelicSet: draft.DarvAllowPandorasBoxRelicSet);

        query = new SearchQuery(
            openingRoute,
            openingRouteRelics,
            draft.NeowRouteDraft.EffectConditions,
            variantBranches,
            ancientBranches,
            relicSequence,
            eventSequence,
            Array.Empty<NormalCombatRewardSearchCondition>(),
            LegacyNeowSemanticConstraints.Empty,
            legacyWorld)
        {
            CombatCardRewards = cardRewardSequence,
            CombatPotionRewards = potionRewardSequence,
            EventResultConditions = draft.EventResultDraft,
            MerchantColorlessConditions = draft.MerchantColorlessDraft,
            MerchantColorlessSequenceConditions = draft.MerchantColorlessSequenceDraft,
            RelicShopSequenceConditions = draft.RelicShopSequenceDraft
        };
    }

    private static IReadOnlyList<VariantScopedBossBranch> BuildVariantScopedBossBranches(
        BossMapSearchDraft draft)
    {
        var output = new List<VariantScopedBossBranch>();
        foreach (IGrouping<int, BossMapVariantSearchDraft> actRows in draft.Rows
                     .GroupBy(row => row.Act)
                     .OrderBy(group => group.Key))
        {
            BossMapVariantSearchDraft[] rows = actRows.ToArray();
            bool selectableVariants = rows.Any(row => row.HasSelectableVariant);
            BossMapVariantSearchDraft[] activeRows = selectableVariants
                ? rows.Where(row => row.IsVariantActive).ToArray()
                : rows;

            foreach (BossMapVariantSearchDraft row in activeRows)
            {
                bool hasFirstBossConstraint = row.FirstBossAny.Count > 0;
                bool hasSecondBossConstraint =
                    row.Act == 3 &&
                    draft.IncludeSecondAct3Boss &&
                    row.SecondBossAny.Count > 0;
                bool hasExplicitVariantConstraint = selectableVariants && row.IsVariantActive;

                // A non-selectable Variant is runtime/UI context, not player Query
                // truth by itself. Preserve Variant-only semantics only when the
                // UI actually offered a Variant choice and the player activated it.
                // Otherwise emit the parent branch only when a Boss child predicate
                // needs that Variant attribution. This keeps Canonical active domains
                // aligned with the Legacy execution filter for empty/Neow-only queries.
                if (!hasExplicitVariantConstraint &&
                    !hasFirstBossConstraint &&
                    !hasSecondBossConstraint)
                {
                    continue;
                }

                ModelKeySetFilter first = hasFirstBossConstraint
                    ? new ModelKeySetFilter(row.FirstBossAny, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())
                    : ModelKeySetFilter.Empty;
                ModelKeySetFilter second = hasSecondBossConstraint
                    ? new ModelKeySetFilter(row.SecondBossAny, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())
                    : ModelKeySetFilter.Empty;

                output.Add(new VariantScopedBossBranch(
                    row.Act,
                    row.ActKey,
                    first,
                    second,
                    row.Act == 3 && draft.IncludeSecondAct3Boss));
            }
        }
        return output;
    }

    private static IReadOnlyList<ActOrdinalModelKeySetFilter> BuildBossOrdinalFilters(
        BossMapSearchDraft draft)
    {
        var output = new List<ActOrdinalModelKeySetFilter>();
        foreach (IGrouping<int, BossMapVariantSearchDraft> actRows in draft.Rows
                     .GroupBy(row => row.Act)
                     .OrderBy(group => group.Key))
        {
            BossMapVariantSearchDraft[] rows = actRows.ToArray();
            bool selectableVariants = rows.Any(row => row.HasSelectableVariant);
            BossMapVariantSearchDraft[] activeRows = selectableVariants
                ? rows.Where(row => row.IsVariantActive).ToArray()
                : rows;
            if (activeRows.Length == 0)
            {
                continue;
            }

            ModelKey[] first = activeRows
                .SelectMany(row => selectableVariants && row.FirstBossAny.Count == 0
                    ? row.AllBossKeys
                    : row.FirstBossAny)
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            if (first.Length > 0)
            {
                output.Add(new ActOrdinalModelKeySetFilter(
                    actRows.Key,
                    1,
                    new ModelKeySetFilter(first, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())));
            }

            if (actRows.Key != 3 || !draft.IncludeSecondAct3Boss)
            {
                continue;
            }

            ModelKey[] second = activeRows
                .SelectMany(row => row.SecondBossAny)
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            if (second.Length > 0)
            {
                output.Add(new ActOrdinalModelKeySetFilter(
                    actRows.Key,
                    2,
                    new ModelKeySetFilter(second, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())));
            }
        }
        return output;
    }

    private static ModelKeySetFilter BuildKeySet(string any, string all, string ban, string? category) => new(
        ParseKeys(any, category),
        ParseKeys(all, category),
        ParseKeys(ban, category));

    private static IReadOnlyList<ActModelKeySetFilter> BuildActFilter(
        int act,
        string any,
        string all,
        string ban,
        string? category)
    {
        ModelKeySetFilter keys = BuildKeySet(any, all, ban, category);
        return keys.IsEmpty
            ? Array.Empty<ActModelKeySetFilter>()
            : new[] { new ActModelKeySetFilter(act, keys) };
    }

    private static IReadOnlyList<ActOrdinalModelKeySetFilter> BuildActOrdinalFilter(
        int act,
        int ordinal,
        string any,
        string all,
        string ban,
        string? category)
    {
        ModelKeySetFilter keys = BuildKeySet(any, all, ban, category);
        if (keys.IsEmpty)
        {
            return Array.Empty<ActOrdinalModelKeySetFilter>();
        }
        if (ordinal <= 0)
        {
            throw new InvalidOperationException("An exact Boss ordinal is required when an ordinal Boss filter is enabled.");
        }
        return new[] { new ActOrdinalModelKeySetFilter(act, ordinal, keys) };
    }

    private static IReadOnlyList<RelicSequenceSearchCondition> ParseRelicSequenceConditions(string text)
    {
        var output = new List<RelicSequenceSearchCondition>();
        foreach (string condition in SplitConditions(text))
        {
            string[] parts = condition.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 5 ||
                !Enum.TryParse(parts[0], ignoreCase: true, out RelicSequenceKind lane) ||
                !TryParseRangeMode(parts[1], out SearchSequenceRangeMode rangeMode) ||
                !int.TryParse(parts[2], out int rangeValue) || rangeValue <= 0 ||
                !TryParseSetMode(parts[3], out string setMode))
            {
                throw new InvalidOperationException(
                    "Invalid relic sequence condition. Expected: COMMON FIRST 5 ANY RELIC:KEY or RARE SLOT 2 BAN RELIC:KEY.");
            }
            string keysText = string.Join(' ', parts.Skip(4));
            ModelKeySetFilter keys = BuildSingleModeKeySet(setMode, keysText, BaseGameModelKeys.Categories.Relic);
            output.Add(new RelicSequenceSearchCondition(lane, rangeMode, rangeValue, keys));
        }
        return output;
    }

    private static IReadOnlyList<EventSequenceSearchCondition> ParseEventSequenceConditions(string text)
    {
        var output = new List<EventSequenceSearchCondition>();
        foreach (string condition in SplitConditions(text))
        {
            string[] parts = condition.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 6 || !TryParseAct(parts[0], out int act) ||
                !TryParseEventSource(parts[1], out EventPoolSourceKind? source) ||
                !TryParseRangeMode(parts[2], out SearchSequenceRangeMode rangeMode) ||
                !int.TryParse(parts[3], out int rangeValue) || rangeValue <= 0 ||
                !TryParseSetMode(parts[4], out string setMode))
            {
                throw new InvalidOperationException(
                    "Invalid event sequence condition. Expected: A2 SHARED FIRST 5 ANY EVENT:KEY or A3 ANY SLOT 1 BAN EVENT:KEY.");
            }
            string keysText = string.Join(' ', parts.Skip(5));
            ModelKeySetFilter keys = BuildSingleModeKeySet(setMode, keysText, BaseGameModelKeys.Categories.Event);
            output.Add(new EventSequenceSearchCondition(act, source, rangeMode, rangeValue, keys));
        }
        return output;
    }

    private static IEnumerable<string> SplitConditions(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryParseAct(string token, out int act)
    {
        string value = token.Trim().ToUpperInvariant();
        if (value.StartsWith('A')) value = value[1..];
        return int.TryParse(value, out act) && act is >= 1 and <= 3;
    }

    private static bool TryParseEventSource(string token, out EventPoolSourceKind? source)
    {
        switch (token.Trim().ToUpperInvariant())
        {
            case "ANY": source = null; return true;
            case "SHARED": source = EventPoolSourceKind.Shared; return true;
            case "LOCAL":
            case "ACTLOCAL":
            case "ACT-LOCAL": source = EventPoolSourceKind.ActLocal; return true;
            default: source = null; return false;
        }
    }

    private static bool TryParseRangeMode(string token, out SearchSequenceRangeMode mode)
    {
        switch (token.Trim().ToUpperInvariant())
        {
            case "FIRST":
            case "FIRSTN": mode = SearchSequenceRangeMode.FirstN; return true;
            case "SLOT":
            case "EXACT": mode = SearchSequenceRangeMode.ExactSlot; return true;
            default: mode = default; return false;
        }
    }

    private static bool TryParseSetMode(string token, out string mode)
    {
        mode = token.Trim().ToUpperInvariant();
        return mode is "ANY" or "ALL" or "BAN";
    }

    private static ModelKeySetFilter BuildSingleModeKeySet(string mode, string keysText, string category)
    {
        IReadOnlyList<ModelKey> keys = ParseKeys(keysText, category);
        return mode switch
        {
            "ANY" => new ModelKeySetFilter(keys, Array.Empty<ModelKey>(), Array.Empty<ModelKey>()),
            "ALL" => new ModelKeySetFilter(Array.Empty<ModelKey>(), keys, Array.Empty<ModelKey>()),
            "BAN" => new ModelKeySetFilter(Array.Empty<ModelKey>(), Array.Empty<ModelKey>(), keys),
            _ => throw new InvalidOperationException("Unknown set mode: " + mode)
        };
    }

    private static NormalCombatPotionRequirement ParsePotionRequirement(int selected) => selected switch
    {
        0 => NormalCombatPotionRequirement.Any,
        1 => NormalCombatPotionRequirement.MustDrop,
        2 => NormalCombatPotionRequirement.MustNotDrop,
        _ => throw new InvalidOperationException("Invalid potion requirement selection.")
    };

    private static int? ParseOptionalNonNegativeInt(string text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!int.TryParse(text.Trim(), out int value) || value < 0)
        {
            throw new InvalidOperationException($"Invalid {fieldName}: {text}");
        }
        return value;
    }

    private static ModelKey? ParseOptionalKey(string text, string? category)
    {
        IReadOnlyList<ModelKey> values = ParseKeys(text, category);
        if (values.Count > 1)
        {
            throw new InvalidOperationException("Only one exact ModelKey is allowed in this field.");
        }
        return values.Count == 1 ? values[0] : null;
    }

    private static IReadOnlyList<ModelKey> ParseOrderedKeys(string text, string category)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<ModelKey>();
        var output = new List<ModelKey>();
        foreach (string token in text.Split(new[] { '>', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            output.Add(ParseExactKey(token, category));
        }
        return output;
    }

    private static IReadOnlyList<ModelKey> ParseKeys(string text, string? category)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<ModelKey>();
        }

        var output = new List<ModelKey>();
        foreach (string token in text.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            output.Add(ParseExactKey(token, category));
        }
        return output.Distinct(ModelKeyComparer.Instance).ToArray();
    }

    private static ModelKey ParseExactKey(string token, string? category)
    {
        string exact = token.Contains(':', StringComparison.Ordinal)
            ? token.Trim().ToUpperInvariant()
            : category is not null
                ? category + ":" + token.Trim().ToUpperInvariant()
                : throw new InvalidOperationException("An exact CATEGORY:ID ModelKey is required: " + token);
        if (!ModelKey.TryParseExact(exact, out ModelKey key) ||
            (category is not null && !string.Equals(key.Category, category, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Invalid exact ModelKey: " + token);
        }
        return key;
    }
    private sealed record PendingPresetLoad(
        SearchPresetDefinition Preset,
        SearchPresetLoadResolution Resolution,
        SearchPresetCompatibilityAssessment Compatibility);

}
