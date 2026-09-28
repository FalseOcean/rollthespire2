using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.Controllers;

internal sealed partial class AnalysisPageController
{
    private readonly AnalysisPage _page;
    private readonly ModRuntimeSnapshot _runtime;
    private readonly Action<GlobalStatusKind, string, string> _setGlobalStatus;
    private readonly RuntimePredictionSettings _predictionSettings;
    private readonly IRuntimePredictionDiagnosticSink _diagnosticSink;
    private readonly SearchWorkspacePersistence _persistence;

    public AnalysisPageController(
        AnalysisPage page,
        ModRuntimeSnapshot runtime,
        RuntimePredictionSettings predictionSettings,
        IRuntimePredictionDiagnosticSink diagnosticSink,
        SearchWorkspacePersistence persistence,
        Action<GlobalStatusKind, string, string> setGlobalStatus)
    {
        _page = page;
        _runtime = runtime;
        _predictionSettings = predictionSettings;
        _diagnosticSink = diagnosticSink ?? throw new ArgumentNullException(nameof(diagnosticSink));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _setGlobalStatus = setGlobalStatus;
        _page.AnalyzeRequested += Analyze;
        _page.RandomSeedRequested += GenerateRandomSeed;
        _page.PredictorContextChanged += PersistPredictorContext;
        _page.PartyPlayerSelected += SelectPartyPlayer;
        _page.PartyModeSelected += SelectPartyMode;
        _page.PartyConfigurationRequested += OpenPartyConfiguration;
        _page.OpeningExplicitlySelected += slot =>
        {
            _strictLibrarySeed = "";
            if (_partyDraft is not null) _partyExplicitSelections.Add(slot); else _soloExplicitOpening = true;
        };
    }

    private void GenerateRandomSeed()
    {
        string seed = ProfileSeedGenerator.Generate(_runtime.Profile);
        if (!string.IsNullOrWhiteSpace(seed))
        {
            _page.SetSeedText(seed, commit: true);
            Analyze(_page.CurrentDraft);
        }
    }

    private void PersistPredictorContext(
        AnalysisRequestDraft draft,
        string preferredOpeningRouteId,
        int? preferredOpeningChoiceSlotIndex,
        string preferredRewardRouteGroupId)
    {
        if (_partyDraft is not null && !_bindingPartyDocument)
        {
            _partySelections[draft.PlayerSlotIndex] = (preferredOpeningChoiceSlotIndex, preferredOpeningRouteId);
            // Premise-only updates no longer enter AnalyzeParty. Keep the seat's
            // authored context current for switching players and seed favorites.
            _partyDraft = _partyDraft with { Query = _partyDraft.Query with
            {
                Players = _partyDraft.Query.Players.Select(p => p.Slot == draft.PlayerSlotIndex
                    ? p with { AncientPremises = draft.AncientOptionConditions } : p).ToArray()
            } };
        }
        _persistence.SavePredictorContext(
            draft.RawSeed,
            draft.CharacterKey,
            draft.Ascension,
            draft.PlayersCount,
            draft.PlayerSlotIndex,
            _page.CurrentUnlockState,
            draft.AncientOptionConditions,
            preferredOpeningRouteId,
            preferredOpeningChoiceSlotIndex,
            preferredRewardRouteGroupId);
        PersistPartyConfiguration();
    }

    public void AnalyzeCurrentDraft() => Analyze(_page.CurrentDraft);

    private void Analyze(AnalysisRequestDraft draft)
    {
        _libraryPredictionIssue = "";
        PreparePartySelection(draft);
        PersistPredictorContext(
            draft,
            _page.PreferredOpeningRouteId,
            _page.PreferredOpeningChoiceSlotIndex,
            _page.PreferredRewardRouteGroupId);
        _page.ShowLoading(draft);
        _setGlobalStatus(GlobalStatusKind.Busy, _page.Text(Ui1TextKey.Analyzing), draft.RawSeed.Trim());
        try
        {
            if (_partyDraft is not null) { AnalyzeParty(draft); return; }
            CharacterIdentity character = CharacterIdentity.FromKey(draft.CharacterKey);
            RuntimeContextAuthoritySnapshot authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
                _runtime.Profile,
                draft.RawSeed,
                character,
                draft.Ascension,
                _runtime.Detection.DisplayVersion,
                draft.PlayersCount,
                draft.PlayerSlotIndex,
                predictionGameMode: draft.PlayersCount > 1 ? WorldGameMode.Multiplayer : WorldGameMode.Singleplayer,
                predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest,
                explicitUnlockState: CapturePredictionUnlocks());

            if (!SeedPredictionRequest.TryCreate(
                    draft.RawSeed,
                    character,
                    draft.Ascension,
                    draft.PlayersCount,
                    draft.PlayerSlotIndex,
                    authority,
                    draft.AncientOptionConditions,
                    SeedPredictionDomainSelection.All,
                    SeedPredictionInputLimits.MaximumRelicSequencePreviewCount,
                    includeDiagnostics: true,
                    out SeedPredictionRequest? request,
                    out SeedPredictionRequestError requestError))
            {
                _page.ShowRequestError(requestError);
                _libraryPredictionIssue = requestError.ToString();
                _setGlobalStatus(GlobalStatusKind.Error, _page.Text(Ui1TextKey.AnalysisFailed), draft.RawSeed.Trim());
                RuntimeLog.Error($"ui1AnalysisRequestRejected={requestError}");
                _diagnosticSink.TryWrite(RuntimePredictionDiagnosticEvent.Create(
                    Guid.NewGuid().ToString("N"),
                    _runtime.Profile.ProfileId.ToString(),
                    draft.RawSeed.Trim(),
                    RuntimePredictionDiagnosticLevel.Warning,
                    "Request",
                    "SingleSeedAnalysis",
                    RuntimePredictionDiagnosticStatus.Failed,
                    "AnalysisRequestRejected:" + requestError,
                    new Dictionary<string, object?>
                    {
                        ["characterModelKey"] = draft.CharacterKey.Serialized,
                        ["ascension"] = draft.Ascension,
                        ["playersCount"] = draft.PlayersCount,
                        ["playerSlotIndex"] = draft.PlayerSlotIndex
                    }),
                    false);
                return;
            }

            request = request!.WithComplexBonesDeckInteractions(_predictionSettings.EnableComplexBonesDeckInteractions);
            SeedPredictionDocument document = RuntimeProfileRegistry.Predict(_runtime.Detection, request!);
            if (_strictLibrarySeed == document.CanonicalSeed && _librarySoloSelection is { } savedSelection)
                ValidateLibrarySelection(document.Sections.SelectMany(s => s.NeowChoices).ToArray(), savedSelection.ChoiceSlot, savedSelection.Route);
            var predictionFailures = document.Diagnostics.Where(d => d.Code == PredictionDiagnosticCodes.Exception).Select(d => d.Value).ToArray();
            if (predictionFailures.Length > 0)
                RuntimeLog.FaultEvidence($"predictorFault=true;request={document.Context.RequestId.Serialized};seed={document.CanonicalSeed};profile={document.ProfileId}", string.Join("\n", predictionFailures), "Prediction failed: " + document.CanonicalSeed);
            RuntimePredictionDiagnosticReporter.EmitAnalysis(
                _diagnosticSink,
                request!,
                document,
                _runtime.Detection.DisplayVersion,
                false,
                "SingleSeedAnalysis");
            _page.ShowDocument(request!, document);
            _setGlobalStatus(
                document.OverallStatus is SeedPredictionOverallStatus.Completed or SeedPredictionOverallStatus.CompletedWithWarnings
                    ? GlobalStatusKind.Idle
                    : document.OverallStatus == SeedPredictionOverallStatus.Unsupported
                        ? GlobalStatusKind.Warning
                        : GlobalStatusKind.Error,
                document.OverallStatus is SeedPredictionOverallStatus.Completed or SeedPredictionOverallStatus.CompletedWithWarnings
                    ? _page.Text(Ui1TextKey.AnalysisComplete)
                    : _page.Text(Ui1TextKey.AnalysisFailed),
                document.CanonicalSeed);

            IReadOnlyList<NeowChoiceResult> choices = document.Sections
                .SelectMany(section => section.NeowChoices)
                .ToArray();
            if (_runtime.IsCompatibilityFallback)
            {
                RuntimeLog.Warn(
                    $"Analysis compatibility fallback: detected={_runtime.Detection.DisplayVersion}; " +
                    $"using={_runtime.Compatibility.ReferenceVersion}; profile={document.ProfileId}; " +
                    $"request={document.Context.RequestId.Serialized}");
            }
            else if (_runtime.IsPendingRuntimeValidation)
            {
                RuntimeLog.Warn(
                    $"Analysis runtime validation pending: detected={_runtime.Detection.DisplayVersion}; " +
                    $"profile={document.ProfileId}; request={document.Context.RequestId.Serialized}");
            }
            RuntimeLog.Info(
                $"Analysis completed: seed={document.CanonicalSeed}; profile={document.ProfileId}; " +
                $"status={document.OverallStatus}; neowChoices={choices.Count}; " +
                $"warnings={document.Warnings.Count}; request={document.Context.RequestId.Serialized}");
            RuntimeLog.Detail($"ui1AnalysisRequestId={document.Context.RequestId.Serialized}");
            RuntimeLog.Detail($"ui1AnalysisCanonicalSeed={document.CanonicalSeed}");
            RuntimeLog.Detail($"ui1AnalysisProfile={document.ProfileId}");
            RuntimeLog.Detail($"ui1AnalysisChoiceKeys={string.Join(",", choices.Select(choice => choice.RelicKey.Serialized))}");
            RuntimeLog.Detail($"ui1AnalysisIdentityPrecisions={string.Join(",", choices.Select(choice => choice.IdentityPrecision))}");
            RuntimeLog.Detail($"ui1AnalysisEffectPrecisions={string.Join(",", choices.Select(choice => choice.EffectPrecision))}");
            RuntimeLog.Detail($"ui1AnalysisEffectGroupCounts={string.Join(",", choices.Select(choice => $"{choice.RelicKey.Serialized}:{choice.EffectGroups.Count}"))}");
            RuntimeLog.Detail($"ui1AnalysisEffectEvidence={string.Join(",", choices.Select(choice => $"{choice.RelicKey.Serialized}:{choice.EffectEvidenceCode}"))}");
            RuntimeLog.Detail($"ui1AnalysisEffectWarningCodes={string.Join(",", choices.Select(choice => $"{choice.RelicKey.Serialized}:[{string.Join("|", choice.Warnings.Select(warning => warning.Code))}]"))}");
            PredictionWarning[] suppressedSnapshotWarnings = document.Warnings
                .Where(warning => warning.Code == PredictionWarningCode.EffectSnapshotIncomplete)
                .Where(IsSnapshotWarningSuppressedFromOrdinaryUi)
                .ToArray();
            if (suppressedSnapshotWarnings.Length > 0)
            {
                RuntimeLog.Detail(
                    "analysisWarningSuppressed=" +
                    $"count={suppressedSnapshotWarnings.Length};" +
                    "reason=DiagnosticsOnlyNoPlayerWarningAuthority;" +
                    $"evidence={string.Join("|", suppressedSnapshotWarnings.Select(warning => warning.EvidenceCode.ToString()))}");
            }
            var eventPrediction = document.Sections
                .Select(section => section.EventPoolSequencePrediction)
                .FirstOrDefault(prediction => prediction is not null);
            if (eventPrediction is not null)
            {
                foreach (var act in eventPrediction.Acts.OrderBy(item => item.Act))
                {
                    RuntimeLog.Detail(
                        "EventProjection " +
                        $"act={act.Act};rawCount={act.RawEntries.Count};cursorOffset={act.OpeningAncientCursorOffset};" +
                        $"staticRejected={act.StaticFilteredOutCount};" +
                        $"runtimeDependent={act.Entries.Count(entry => entry.EligibilityKind == EventCandidateEligibilityKind.RuntimeDependent)};" +
                        $"finalCount={act.Entries.Count}");
                }
            }
            foreach (NeowChoiceResult choice in choices.Where(choice => choice.BonesOutcome is not null))
            {
                foreach (BonesAcquisitionRouteResult route in choice.BonesOutcome!.OriginalRoutes)
                {
                    BonesDependencyContinuity? continuity = route.DependencyContinuity;
                    string relicPrecisions = string.Join("|", route.RelicScopedResults.Select(result =>
                        $"{result.SourceRelicKey.Serialized}:{result.Precision}"));
                    RuntimeLog.Detail(
                        "bonesRouteDiagnostic=" +
                        $"routeId={route.RouteId};" +
                        $"acquisitionOrder={string.Join(">", route.AcquisitionOrder.Select(key => key.Serialized))};" +
                        $"routeOverallPrecision={route.Precision};" +
                        $"relicEffectPrecisionBySource={relicPrecisions};" +
                        $"rewardsRngContinuity={continuity?.RewardsRng.Status.ToString() ?? "not-recorded"};" +
                        $"nicheRngContinuity={continuity?.NicheRng.Status.ToString() ?? "not-recorded"};" +
                        $"transformationsRngContinuity={continuity?.TransformationsRng.Status.ToString() ?? "not-recorded"};" +
                        $"shadowDeckContinuity={continuity?.ShadowDeck.Status.ToString() ?? "not-recorded"};" +
                        $"relicBagContinuity={continuity?.RelicBag.Status.ToString() ?? "not-recorded"};" +
                        $"generatedCursePoolAuthority={continuity?.GeneratedCursePool.Status.ToString() ?? "not-recorded"};" +
                        $"unknownHookContinuity={continuity?.UnknownHook.Status.ToString() ?? "not-recorded"};" +
                        $"nestedObtainContinuity={continuity?.NestedObtain.Status.ToString() ?? "not-recorded"};" +
                        $"playerChoiceContinuity={continuity?.PlayerChoice.Status.ToString() ?? "not-recorded"};" +
                        $"finalCurseKey={route.SharedContinuation.FinalCurseKey?.Serialized ?? "none"};" +
                        $"finalCursePrecision={route.SharedContinuation.FinalCursePrecision};" +
                        $"continuationWarningCodes={string.Join("|", continuity?.ContinuationWarningCodes ?? Array.Empty<PredictionWarningCode>())};" +
                        $"continuationEvidenceCodes={string.Join("|", (continuity?.ContinuationEvidenceCodes ?? Array.Empty<EvidenceCode>()).Select(code => code.ToString()))};" +
                        $"continuationFingerprint={route.RelevantContinuationFingerprint}");
                }
            }
            RuntimeLog.Detail($"productionEffectAuthorityPresent={request!.Authority.EffectAuthority is not null}");
            if (request.Authority.EffectAuthority is { } effects)
            {
                RuntimeLog.Detail($"productionEffectAuthoritySource={effects.AuthoritySource}");
                RuntimeLog.Detail($"productionEffectSnapshotCompleteness={effects.Completeness}");
                RuntimeLog.Detail($"productionEffectSnapshotFingerprint={effects.SnapshotFingerprint}");
                RuntimeLog.Detail($"productionEffectSnapshotDeckCount={effects.OrderedDeck?.Count ?? -1}");
                RuntimeLog.Detail($"productionEffectSnapshotCardPoolCount={effects.CharacterRewardPool?.Count ?? -1}");
                RuntimeLog.Detail($"productionEffectSnapshotCharacterPoolUniqueInstanceIds={NeowEffectSnapshotInvariants.CountUniqueCardInstanceIds(effects.CharacterRewardPool)}");
                RuntimeLog.Detail($"productionEffectSnapshotCharacterPoolExact={effects.HasExactCharacterRewardPool.ToString().ToLowerInvariant()}");
                RuntimeLog.Detail($"productionEffectSnapshotColorlessPoolCount={effects.ColorlessRewardPool?.Count ?? -1}");
                RuntimeLog.Detail($"productionEffectSnapshotColorlessPoolUniqueInstanceIds={NeowEffectSnapshotInvariants.CountUniqueCardInstanceIds(effects.ColorlessRewardPool)}");
                RuntimeLog.Detail($"productionEffectSnapshotColorlessPoolExact={effects.HasExactColorlessRewardPool.ToString().ToLowerInvariant()}");
                RuntimeLog.Detail($"productionEffectSnapshotOtherCharacterPoolCardCount={effects.OtherCharacterPools?.Sum(pool => pool.Cards.Count) ?? -1}");
                RuntimeLog.Detail($"productionEffectSnapshotOtherCharacterPoolUniqueInstanceIds={NeowEffectSnapshotInvariants.CountUniqueCardInstanceIds(effects.OtherCharacterPools?.SelectMany(pool => pool.Cards))}");
                RuntimeLog.Detail($"productionEffectSnapshotOtherCharacterPoolsExact={effects.HasExactOtherCharacterPools.ToString().ToLowerInvariant()}");
                RuntimeLog.Detail($"productionEffectSnapshotPotionCount={effects.PotionPool?.Count ?? -1}");
                RuntimeLog.Detail($"productionEffectSnapshotRelicBagCount={effects.OrderedRelicBag?.Count ?? -1}");
                RuntimeLog.Detail($"productionEffectSnapshotWarningCodes={string.Join(",", effects.WarningCodes ?? Array.Empty<PredictionWarningCode>())}");
            }
            if (request.Authority.WorldAuthority is { } world)
            {
                RuntimeLog.Detail($"productionWorldAuthorityProfile={world.CapturedProfileId}");
                RuntimeLog.Detail($"productionWorldAuthoritySource={world.SourceAuthority}");
                RuntimeLog.Detail($"productionWorldSnapshotCompleteness={world.Completeness}");
                RuntimeLog.Detail($"productionWorldSnapshotFingerprint={world.SnapshotFingerprint}");
                RuntimeLog.Detail($"productionWorldCaptureDiagnostic={world.CaptureDiagnosticCode}");
                if (world.Beta109Generation is Beta109WorldGenerationSnapshot modernWorldSource)
                {
                    Beta109WorldGenerationSnapshot modernWorld = Beta109WorldSnapshotProjector.ProjectForSeed(
                        modernWorldSource,
                        document.CanonicalSeed);
                    RuntimeLog.Detail(
                        "modernWorldAuthorityDiagnostic=" +
                        $"selectedActsExact={modernWorld.SelectedActsExact.ToString().ToLowerInvariant()};" +
                        $"selectedActs={string.Join(">", modernWorld.SelectedActs.Select(key => key.Serialized))};" +
                        $"actSelectionAuthorityExact={modernWorld.ActSelectionAuthorityExact.ToString().ToLowerInvariant()};" +
                        $"actSelectionRootExact={modernWorld.ActSelectionRootExact.ToString().ToLowerInvariant()};" +
                        $"runSeedRootExact={modernWorld.RunSeedRootExact.ToString().ToLowerInvariant()};" +
                        $"isMultiplayer={modernWorld.IsMultiplayer.ToString().ToLowerInvariant()};" +
                        $"isMultiplayerExact={modernWorld.IsMultiplayerExact.ToString().ToLowerInvariant()};" +
                        $"testModeFactExact={modernWorld.TestModeFactExact.ToString().ToLowerInvariant()};" +
                        $"act1OverrideExact={modernWorld.Act1OverrideExact.ToString().ToLowerInvariant()};" +
                        $"modeFactsExact={modernWorld.ModeFactsExact.ToString().ToLowerInvariant()};" +
                        $"requestedGameMode={modernWorld.RequestedGameMode};" +
                        $"requestedGameModeExact={modernWorld.RequestedGameModeExact.ToString().ToLowerInvariant()};" +
                        $"gameModeEvidence={modernWorld.GameModeEvidenceCode};" +
                        $"actGroups={modernWorld.ActSelectionGroups.Count};" +
                        $"actGroupAuthority={string.Join(",", modernWorld.ActSelectionGroups.Select(group => $"A{group.Act}:{group.SelectionMode}:{group.EligibilityAndOrderExact}:{group.EligibleActsInSourceOrder.Count}"))};" +
                        $"sharedEventCatalogExact={modernWorld.SharedEventCatalogExact.ToString().ToLowerInvariant()};" +
                        $"sharedAncientCatalogExact={modernWorld.SharedAncientCatalogExact.ToString().ToLowerInvariant()};" +
                        $"relicInitializationExact={modernWorld.RelicInitializationExact.ToString().ToLowerInvariant()};" +
                        $"upFrontPrefixKind={modernWorld.UpFrontPrefix.Kind};" +
                        $"upFrontPriorInputsExact={modernWorld.UpFrontPrefix.PriorInputsExact.ToString().ToLowerInvariant()};" +
                        $"captureDiagnostic={modernWorld.CaptureDiagnosticCode}");
                }
            }
        }
        catch (Exception ex)
        {
            _libraryPredictionIssue = ex.Message;
            _page.ShowUnhandledError();
            _setGlobalStatus(GlobalStatusKind.Error, _page.Text(Ui1TextKey.AnalysisFailed), draft.RawSeed.Trim());
            RuntimeLog.Fault("ui1AnalysisFailedSafely=true", ex);
            _diagnosticSink.TryWrite(RuntimePredictionDiagnosticEvent.Create(
                Guid.NewGuid().ToString("N"),
                _runtime.Profile.ProfileId.ToString(),
                draft.RawSeed.Trim(),
                RuntimePredictionDiagnosticLevel.Error,
                "Request",
                "SingleSeedAnalysis",
                RuntimePredictionDiagnosticStatus.Failed,
                "AnalysisUnhandledFailure:" + ex.GetType().Name,
                new Dictionary<string, object?>
                {
                    ["message"] = ex.Message
                }),
                false);
        }
    }
    private static bool IsSnapshotWarningSuppressedFromOrdinaryUi(
        PredictionWarning warning)
    {
        if (warning.Code != PredictionWarningCode.EffectSnapshotIncomplete)
        {
            return false;
        }

        // EffectSnapshotIncomplete remains an evidence/log diagnostic, but it no
        // longer has ordinary Predictor player-facing warning authority.
        return true;
    }

    private static bool IsSnapshotWarningFullEffectOnly(NeowChoiceResult choice)
    {
        bool productExact =
            choice.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Evaluated &&
            choice.ProductRelevantProjectionPrecision == PredictionPrecision.Exact;
        bool bonesRoutesProductBounded = choice.BonesOutcome is { } bones &&
            bones.OriginalRoutes.Count > 0 &&
            bones.OriginalRoutes.All(route => route.RouteProjectionStatus switch
            {
                RouteProjectionStatus.Exact or RouteProjectionStatus.NotEvaluatedByPolicy => true,
                RouteProjectionStatus.Unknown => route.ProductRelevantProjectionStatus switch
                {
                    ProductRelevantProjectionStatus.NotEvaluatedByPolicy => true,
                    ProductRelevantProjectionStatus.Evaluated =>
                        route.ProductRelevantProjectionPrecision == PredictionPrecision.Exact &&
                        route.SharedContinuation.FinalCursePrecision == PredictionPrecision.Exact,
                    _ => false
                },
                _ => false
            });
        return productExact || bonesRoutesProductBounded;
    }

}
