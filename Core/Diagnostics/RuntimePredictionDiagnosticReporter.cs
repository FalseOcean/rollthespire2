using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Diagnostics;

/// <summary>
/// Pure projection from immutable request/snapshot/document DTOs to diagnostic events.
/// It performs no I/O and never reads live game or Godot objects.
/// </summary>
public static class RuntimePredictionDiagnosticReporter
{
    private static readonly string[] UpFrontStages =
    {
        "RunStart",
        "RelicBags",
        "SharedAncients",
        "Events",
        "Encounters",
        "Rooms",
        "AncientIdentity",
        "BossSelection",
        "SecondBoss"
    };

    public static void EmitAnalysis(
        IRuntimePredictionDiagnosticSink? sink,
        SeedPredictionRequest request,
        SeedPredictionDocument document,
        string detectedGameVersion,
        bool forceTrace = false,
        string source = "SingleSeedAnalysis")
    {
        if (sink is null || (!sink.Enabled && !forceTrace)) return;

        string requestId = request.RequestId.Serialized;
        string profile = document.ProfileId.ToString();
        string seed = document.CanonicalSeed.Length > 0 ? document.CanonicalSeed : request.OriginalSeed;
        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            requestId,
            profile,
            seed,
            RuntimePredictionDiagnosticLevel.Info,
            "Request",
            source,
            RuntimePredictionDiagnosticStatus.Started,
            details: Details(
                ("originalSeed", request.OriginalSeed),
                ("canonicalSeed", document.CanonicalSeed),
                ("gameVersion", detectedGameVersion),
                ("characterModelKey", request.Character.CharacterKey.Serialized),
                ("ascension", request.Ascension),
                ("playersCount", request.PlayersCount),
                ("playerSlotIndex", request.PlayerSlotIndex),
                ("predictionGameMode", request.Authority.PredictionGameMode.ToString()),
                ("predictionGameModeAuthority", request.Authority.PredictionGameModeAuthority.ToString()),
                ("ancientConditionTezcataraBasicStrike", request.AncientOptionConditions.TezcataraHasBasicStrike),
                ("ancientConditionNonupeipeSwiftAtLeast4", request.AncientOptionConditions.NonupeipeSwiftEnchantableAtLeast4),
                ("ancientConditionTanxInstinctAtLeast3", request.AncientOptionConditions.TanxInstinctEnchantableAtLeast3),
                ("ancientConditionPaelGoopyDefendAtLeast3", request.AncientOptionConditions.PaelGoopyDefendCardsAtLeast3),
                ("ancientConditionPaelNoEventPet", request.AncientOptionConditions.PaelAllowLegionNoEventPet),
                ("ancientConditionPaelRemovableAtLeast5", request.AncientOptionConditions.PaelRemovableCardsAtLeast5),
                ("ancientConditionOrobasArchaicTooth", request.AncientOptionConditions.OrobasArchaicToothConditionMet),
                ("ancientConditionOrobasTouch", request.AncientOptionConditions.OrobasTouchOfOrobasConditionMet),
                ("ancientConditionDarvPandorasBox", request.AncientOptionConditions.DarvAllowPandorasBoxRelicSet),
                ("includeDiagnostics", request.IncludeDiagnostics))));

        EmitRuntimeCapture(sink, request, document, forceTrace);
        EmitActSelection(sink, request, document, forceTrace);
        EmitUpFrontStages(sink, request, document, forceTrace);
        EmitProviderResults(sink, request, document, forceTrace);
        EmitFirstFailure(sink, request, document, forceTrace);

        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            requestId,
            profile,
            seed,
            RuntimePredictionDiagnosticLevel.Info,
            "Request",
            source,
            RuntimePredictionDiagnosticStatus.Completed,
            document.OverallStatus is SeedPredictionOverallStatus.Completed or SeedPredictionOverallStatus.CompletedWithWarnings
                ? string.Empty
                : document.OverallStatus.ToString(),
            Details(
                ("overallStatus", document.OverallStatus.ToString()),
                ("warningCodes", document.Warnings.Select(item => item.Code.ToString()).ToArray()),
                ("diagnosticFileFailure", sink.LastFailureCode))));
    }

    public static RuntimePredictionDiagnosticEvent SearchSessionStarted(
        string sessionId,
        string profile,
        string seed,
        RuntimeContextAuthoritySnapshot authority,
        long scanCount,
        int targetMatches,
        int workers) => RuntimePredictionDiagnosticEvent.Create(
            sessionId,
            profile,
            seed,
            RuntimePredictionDiagnosticLevel.Info,
            "Search",
            "SessionStart",
            RuntimePredictionDiagnosticStatus.Started,
            details: Details(
                ("scanCount", scanCount),
                ("targetMatchCount", targetMatches),
                ("workerCount", workers),
                ("snapshotAuthority", authority.WorldAuthority?.SourceAuthority.ToString() ?? "Missing"),
                ("snapshotCompleteness", authority.WorldAuthority?.Completeness.ToString() ?? "Missing"),
                ("worldSnapshotFingerprint", authority.WorldSnapshotFingerprint),
                ("predictionGameMode", authority.PredictionGameMode.ToString()),
                ("predictionGameModeAuthority", authority.PredictionGameModeAuthority.ToString())));

    public static RuntimePredictionDiagnosticEvent SearchSessionCompleted(
        string sessionId,
        string profile,
        string seed,
        SearchDiagnosticSummarySnapshot summary,
        long scanned,
        int matched,
        string runState,
        double elapsedSeconds) => RuntimePredictionDiagnosticEvent.Create(
            sessionId,
            profile,
            seed,
            RuntimePredictionDiagnosticLevel.Info,
            "Search",
            "SessionEnd",
            RuntimePredictionDiagnosticStatus.Completed,
            summary.FirstFailureCode,
            Details(
                ("runState", runState),
                ("scanned", scanned),
                ("matched", matched),
                ("elapsedSeconds", elapsedSeconds),
                ("dispositionCounts", summary.DispositionCounts),
                ("failClosedReasonCounts", summary.FailureReasonCounts),
                ("representativeSeeds", summary.RepresentativeSeeds),
                ("firstFailureCode", summary.FirstFailureCode),
                ("firstFailureStage", MapFailureStage(summary.FirstFailureCode)),
                ("firstFailureSeed", summary.FirstFailureSeed)));

    private static void EmitRuntimeCapture(
        IRuntimePredictionDiagnosticSink sink,
        SeedPredictionRequest request,
        SeedPredictionDocument document,
        bool forceTrace)
    {
        WorldAuthoritySnapshot? world = request.Authority.WorldAuthority;
        Beta109WorldGenerationSnapshot? modern = ProjectModern(request, document);
        string reason = FirstReason(world?.CaptureDiagnosticCode);
        RuntimePredictionDiagnosticStatus status = world is null
            ? RuntimePredictionDiagnosticStatus.Failed
            : world.Completeness == SnapshotCompleteness.Complete
                ? RuntimePredictionDiagnosticStatus.Succeeded
                : RuntimePredictionDiagnosticStatus.Partial;
        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            request.RequestId.Serialized,
            document.ProfileId.ToString(),
            document.CanonicalSeed,
            world is null ? RuntimePredictionDiagnosticLevel.Error : RuntimePredictionDiagnosticLevel.Info,
            "RuntimeCapture",
            "CaptureWorldAuthority",
            status,
            reason,
            Details(
                ("gameVersion", request.Authority.GameVersion),
                ("profile", request.Authority.ProfileId.ToString()),
                ("worldAuthority", world?.SourceAuthority.ToString() ?? "Missing"),
                ("worldCompleteness", world?.Completeness.ToString() ?? "Missing"),
                ("captureReasonChain", SplitReasons(world?.CaptureDiagnosticCode)),
                ("catalogFingerprint", world?.CatalogFingerprint ?? string.Empty),
                ("snapshotFingerprint", world?.SnapshotFingerprint ?? string.Empty),
                ("unlockFingerprint", modern?.UnlockFingerprint ?? string.Empty),
                ("generationRuleFingerprint", modern?.GenerationRuleFingerprint ?? string.Empty),
                ("requestedGameMode", modern?.RequestedGameMode.ToString() ?? request.Authority.PredictionGameMode.ToString()),
                ("requestedGameModeExact", modern?.RequestedGameModeExact ?? false),
                ("gameModeEvidence", modern?.GameModeEvidenceCode ?? string.Empty),
                ("isMultiplayer", modern?.IsMultiplayer),
                ("isMultiplayerExact", modern?.IsMultiplayerExact),
                ("testModeIsOff", modern?.TestModeIsOff),
                ("testModeFactExact", modern?.TestModeFactExact),
                ("act1Override", modern?.Act1OverrideRaw ?? string.Empty),
                ("act1OverrideExact", modern?.Act1OverrideExact),
                ("sharedEventCount", modern?.SharedEvents.Count ?? 0),
                ("sharedEventCatalogExact", modern?.SharedEventCatalogExact),
                ("sharedEventRawOrderExact", modern?.EventAuthority.SharedRawOrderExact),
                ("eventEpochMembershipExact", modern?.EventAuthority.EpochMembershipExact),
                ("eventEpochRevealFactsExact", modern?.EventAuthority.EpochRevealFactsExact),
                ("eventEpochs", modern?.EventAuthority.EpochsInFilterOrder.Select(epoch => new Dictionary<string, object?>
                {
                    ["epochId"] = epoch.EpochId,
                    ["isRevealed"] = epoch.IsRevealed,
                    ["membershipExact"] = epoch.MembershipExact,
                    ["revealFactExact"] = epoch.RevealFactExact,
                    ["memberModelKeys"] = epoch.OrderedMemberKeys.Select(key => key.Serialized).ToArray()
                }).ToArray() ?? Array.Empty<Dictionary<string, object?>>()),
                ("allSharedAncientCount", modern?.AllSharedAncients.Count ?? 0),
                ("unlockedSharedAncientCount", modern?.SharedAncients.Count ?? 0),
                ("sharedAncientCatalogExact", modern?.SharedAncientCatalogExact),
                ("relicInitializationExact", modern?.RelicInitializationExact),
                ("sharedRelicPoolOrderExact", modern?.SharedRelicPoolOrderExact),
                ("characterRelicPoolOrderExact", modern?.CharacterRelicPoolOrderExact),
                ("relicRarityAuthorityExact", modern?.RelicRarityAuthorityExact),
                ("relicShopEligibilityAuthorityExact", modern?.RelicShopEligibilityAuthorityExact),
                ("playerRelicPoolCompositionExact", modern?.PlayerRelicPoolCompositionExact),
                ("relicAuthorityEvidenceCode", modern?.RelicAuthorityEvidenceCode ?? string.Empty),
                ("sharedRelicBucketCounts", BucketCounts(modern?.SharedRelicBuckets)),
                ("playerRelicBucketCounts", BucketCounts(modern?.PlayerRelicBuckets)),
                ("sharedRelicBucketOrder", modern?.SharedRelicBuckets.Select(bucket => bucket.BucketId).ToArray() ?? Array.Empty<string>()),
                ("playerRelicBucketOrder", modern?.PlayerRelicBuckets.Select(bucket => bucket.BucketId).ToArray() ?? Array.Empty<string>()),
                ("orderedActCatalogCount", modern?.OrderedActCatalog.Count ?? 0),
                ("eventAuthorityExact", modern is not null &&
                    modern.EventAuthority.HasExactFilteringAuthority &&
                    modern.OrderedActCatalog.All(act => act.EventRngConsumptionExact)),
                ("encounterAuthorityExact", modern is not null &&
                    SelectedActs(modern).Count == modern.SelectedActs.Count &&
                    SelectedActs(modern).All(HasExactEncounterAuthority)),
                ("roomShapeAuthorityExact", modern?.OrderedActCatalog.All(act => act.RoomShapeExact)),
                ("unlockAuthorityExact", modern?.UnlockFactsExact),
                ("resolvedMembers", new Dictionary<string, object?>
                {
                    ["actCatalog"] = modern?.ActSelectionGroups.Count > 0 ? "ModelDb.ActsByIndex" : "missing",
                    ["predictionMode"] = modern?.GameModeEvidenceCode ?? "missing",
                    ["sharedRelicPool"] = "ModelDb.SharedRelicPool.GetUnlockedRelics",
                    ["characterRelicPool"] = "CharacterModel.RelicPool.GetUnlockedRelics",
                    ["relicBagGrouping"] = "Stable107 donor RelicGrabBag.Populate first-seen rarity order",
                    ["sharedAncients"] = "ModelDb.AllSharedAncients + UnlockState.SharedAncients",
                    ["sharedEvents"] = "ModelDb.AllSharedEvents",
                    ["actEvents"] = "ActModel.AllEvents",
                    ["eventEpochMembership"] = "Event1Epoch/2/3.Events",
                    ["eventEpochReveal"] = "UnlockState.IsEpochRevealed<T>()",
                    ["encounterCatalog"] = "Known vanilla ActModel.GenerateAllEncounters",
                    ["encounterClassification"] = "EncounterModel.RoomType + IsWeak",
                    ["encounterTagComparer"] = "EncounterModel.SharesTagsWith pairwise DTO matrix",
                    ["encounterWeightRule"] = "ActModel.GenerateRooms literal 1.0",
                    ["encounterRetryShape"] = "GrabBag full-bag rejection + fallback"
                }))));

        if (modern is null) return;
        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            request.RequestId.Serialized,
            document.ProfileId.ToString(),
            document.CanonicalSeed,
            RuntimePredictionDiagnosticLevel.Trace,
            "RuntimeCapture",
            "CaptureActsByIndex",
            modern.ActSelectionAuthorityExact
                ? RuntimePredictionDiagnosticStatus.Succeeded
                : RuntimePredictionDiagnosticStatus.Partial,
            modern.ActSelectionAuthorityExact ? string.Empty : "MissingActsByIndexOrPredicateAuthority",
            Details(
                ("actsByIndexFound", modern.ActSelectionGroups.Count > 0),
                ("fallbackCount", world?.ActGroups?.SelectMany(group => group.Acts).Count() ?? 0),
                ("groupCount", modern.ActSelectionGroups.Count),
                ("groups", modern.ActSelectionGroups.Select(group => new Dictionary<string, object?>
                {
                    ["act"] = group.Act,
                    ["selectionMode"] = group.SelectionMode.ToString(),
                    ["eligibilityAndOrderExact"] = group.EligibilityAndOrderExact,
                    ["eligibleModelKeys"] = group.EligibleActsInSourceOrder.Select(key => key.Serialized).ToArray(),
                    ["candidateCount"] = group.Candidates.Count,
                    ["candidates"] = group.Candidates.Select(candidate => new Dictionary<string, object?>
                    {
                        ["ordinal"] = candidate.CandidateOrdinal,
                        ["modelKey"] = candidate.ActKey.Serialized,
                        ["isDefault"] = candidate.IsDefault,
                        ["isUnlocked"] = candidate.IsUnlocked,
                        ["discovered"] = candidate.DiscoveredInSingleplayer,
                        ["deterministicFirstEligible"] = candidate.DeterministicFirstEligible,
                        ["predicateExact"] = candidate.PredicateExact,
                        ["evidenceCode"] = candidate.EvidenceCode
                    }).ToArray()
                }).ToArray()))));

        string eventReason = !modern.EventAuthority.SharedRawOrderExact
            ? "MissingSharedEventCatalog"
            : !modern.EventAuthority.EpochMembershipExact
                ? "MissingEventEpochMembership"
                : !modern.EventAuthority.EpochRevealFactsExact
                    ? "MissingEventEpochAuthority"
                    : modern.OrderedActCatalog.Any(act => !act.EligibleEventOrderExact)
                        ? "MissingEventEligibleOrder"
                        : string.Empty;
        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            request.RequestId.Serialized,
            document.ProfileId.ToString(),
            document.CanonicalSeed,
            eventReason.Length == 0 ? RuntimePredictionDiagnosticLevel.Info : RuntimePredictionDiagnosticLevel.Warning,
            "RuntimeCapture",
            "CaptureEventAuthority",
            eventReason.Length == 0
                ? RuntimePredictionDiagnosticStatus.Succeeded
                : RuntimePredictionDiagnosticStatus.Failed,
            eventReason,
            Details(
                ("sharedRawCount", modern.EventAuthority.OrderedSharedEventsRaw.Count),
                ("sharedRawOrderExact", modern.EventAuthority.SharedRawOrderExact),
                ("epochFilterOrderExact", modern.EventAuthority.EpochFilterOrderExact),
                ("epochMembershipExact", modern.EventAuthority.EpochMembershipExact),
                ("epochRevealFactsExact", modern.EventAuthority.EpochRevealFactsExact),
                ("evidenceCode", modern.EventAuthority.EvidenceCode),
                ("epochs", modern.EventAuthority.EpochsInFilterOrder.Select(epoch => new Dictionary<string, object?>
                {
                    ["epochId"] = epoch.EpochId,
                    ["isRevealed"] = epoch.IsRevealed,
                    ["membershipExact"] = epoch.MembershipExact,
                    ["revealFactExact"] = epoch.RevealFactExact,
                    ["members"] = epoch.OrderedMemberKeys.Select(key => key.Serialized).ToArray()
                }).ToArray()),
                ("acts", modern.OrderedActCatalog.Select(act => new Dictionary<string, object?>
                {
                    ["act"] = act.Act,
                    ["actKey"] = act.ActKey.Serialized,
                    ["rawEventCount"] = act.OrderedRawEvents.Count,
                    ["eligibleEventCount"] = act.OrderedEligibleEvents.Count,
                    ["rawOrderExact"] = act.RawEventCatalogOrderExact,
                    ["eligibleOrderExact"] = act.EligibleEventOrderExact,
                    ["eligibleEvents"] = act.OrderedEligibleEvents.Select(key => key.Serialized).ToArray(),
                    ["expectedNextIntCalls"] = Math.Max(0, act.OrderedEligibleEvents.Count - 1)
                }).ToArray()))));

        string encounterReason = DetermineEncounterAuthorityIssue(modern);
        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            request.RequestId.Serialized,
            document.ProfileId.ToString(),
            document.CanonicalSeed,
            encounterReason.Length == 0 ? RuntimePredictionDiagnosticLevel.Info : RuntimePredictionDiagnosticLevel.Warning,
            "RuntimeCapture",
            "CaptureEncounterAuthority",
            encounterReason.Length == 0
                ? RuntimePredictionDiagnosticStatus.Succeeded
                : RuntimePredictionDiagnosticStatus.Failed,
            encounterReason,
            Details(
                ("selectedActs", modern.SelectedActs.Select(key => key.Serialized).ToArray()),
                ("selectedRawCatalogCount", SelectedActs(modern).Sum(act => act.OrderedGenerateAllEncounters.Count)),
                ("allCapturedRawCatalogCount", modern.OrderedActCatalog.Sum(act => act.OrderedGenerateAllEncounters.Count)),
                ("acts", modern.OrderedActCatalog.Select(act => new Dictionary<string, object?>
                {
                    ["act"] = act.Act,
                    ["actKey"] = act.ActKey.Serialized,
                    ["selected"] = modern.SelectedActs.Contains(act.ActKey, ModelKeyComparer.Instance),
                    ["rawCount"] = act.OrderedGenerateAllEncounters.Count,
                    ["weakCount"] = act.WeakEncounters.Count,
                    ["regularCount"] = act.RegularEncounters.Count,
                    ["eliteCount"] = act.EliteEncounters.Count,
                    ["bossCount"] = act.Bosses.Count,
                    ["weakSlots"] = act.WeakEncounterSlots,
                    ["regularSlots"] = Math.Max(0, act.TotalNormalRooms - act.WeakEncounterSlots),
                    ["eliteSlots"] = act.EliteEncounterSlots,
                    ["orderExact"] = act.GenerateAllEncountersOrderExact,
                    ["classificationExact"] = act.EncounterClassificationExact,
                    ["tagIdentityExact"] = act.EncounterTagIdentityExact,
                    ["tagComparerExact"] = act.EncounterTagComparerExact,
                    ["referenceIdentityExact"] = act.EncounterReferenceIdentityExact,
                    ["weightRuleExact"] = act.EncounterWeightModelExact,
                    ["retryShapeExact"] = act.EncounterRetryShapeExact,
                    ["roomShapeExact"] = act.RoomShapeExact,
                    ["officialVanillaCatalog"] = act.EncounterCatalogOfficialVanilla,
                    ["evidenceCode"] = act.EncounterAuthorityEvidenceCode,
                    ["entries"] = act.OrderedGenerateAllEncounters.Select(entry => new Dictionary<string, object?>
                    {
                        ["sourceOrdinal"] = entry.SourceOrdinal,
                        ["referenceIdentityId"] = entry.ReferenceIdentityId,
                        ["modelKey"] = entry.EncounterKey.Serialized,
                        ["roomType"] = entry.RoomType.ToString(),
                        ["isWeak"] = entry.IsWeak,
                        ["tags"] = entry.Tags.ToArray(),
                        ["tagConflictSourceOrdinals"] = entry.ExactTagConflictSourceOrdinals.ToArray()
                    }).ToArray()
                }).ToArray()))));

        string optionContextReason = modern.AncientEventContexts.Count == 0
            ? "MissingEventContext"
            : modern.AncientEventContexts.Any(context => context.Catalog is null)
                ? "MissingAncientOptionCatalog"
                : modern.AncientEventContexts.Any(context => !context.EventRngRootExact)
                    ? "MissingAncientOptionEventRoot"
                    : modern.AncientEventContexts.Any(context => context.HookDecision == Beta109HookDecision.Unknown)
                        ? "UnknownAncientHook"
                        : string.Empty;
        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            request.RequestId.Serialized,
            document.ProfileId.ToString(),
            document.CanonicalSeed,
            optionContextReason.Length == 0 ? RuntimePredictionDiagnosticLevel.Info : RuntimePredictionDiagnosticLevel.Warning,
            "RuntimeCapture",
            "CaptureAncientOptionAuthority",
            optionContextReason.Length == 0
                ? RuntimePredictionDiagnosticStatus.Succeeded
                : RuntimePredictionDiagnosticStatus.Partial,
            optionContextReason,
            Details(
                ("contextCount", modern.AncientEventContexts.Count),
                ("contexts", modern.AncientEventContexts.Select(context => new Dictionary<string, object?>
                {
                    ["act"] = context.Act,
                    ["ancientKey"] = context.AncientKey.Serialized,
                    ["eventIdEntry"] = context.EventIdEntry,
                    ["runtimeTypeName"] = context.RuntimeTypeName,
                    ["isShared"] = context.IsShared,
                    ["currentActIndex"] = context.CurrentActIndex,
                    ["playerSlot"] = context.PlayerSlot,
                    ["eventContextExact"] = context.EventContextExact,
                    ["dynamicFactsExact"] = context.DynamicFactsExact,
                    ["modifierFactsExact"] = context.ModifierFactsExact,
                    ["unlockedCharacterSourceOrderExact"] = context.UnlockedCharacterSourceOrderExact,
                    ["unlockedCharactersInSourceOrder"] = context.UnlockedCharacters
                        .Select(key => key.Serialized)
                        .ToArray(),
                    ["hookDecision"] = context.HookDecision.ToString(),
                    ["eventRngRootHex"] = context.EventRngRootExact ? $"0x{context.EventRngRoot:X16}" : string.Empty,
                    ["eventRngRootExact"] = context.EventRngRootExact,
                    ["eventRngFormulaVersion"] = context.EventRngFormulaVersion,
                    ["projectionPolicy"] = context.ProjectionPolicy,
                    ["authorityEvidenceCode"] = context.AuthorityEvidenceCode,
                    ["catalogExact"] = context.Catalog?.CatalogExact ?? false,
                    ["mutableInstanceProjectionExact"] = context.Catalog?.MutableInstanceProjectionExact ?? false,
                    ["catalogFingerprint"] = context.Catalog?.CatalogFingerprint ?? string.Empty,
                    ["pools"] = context.Catalog?.Pools.OrderBy(pool => pool.SourceOrdinal).Select(pool => new Dictionary<string, object?>
                    {
                        ["poolId"] = pool.PoolId,
                        ["sourceOrdinal"] = pool.SourceOrdinal,
                        ["count"] = pool.OrderedOptions.Count,
                        ["orderExact"] = pool.OrderExact,
                        ["instanceProjectionExact"] = pool.InstanceProjectionExact,
                        ["filterPredicateId"] = pool.FilterPredicateId,
                        ["filterResultExact"] = pool.FilterResultExact,
                        ["dynamicProjection"] = pool.IsDynamicProjection,
                        ["orderedOptions"] = pool.OrderedOptions.Select(key => key.Serialized).ToArray()
                    }).ToArray() ?? Array.Empty<Dictionary<string, object?>>()
                }).ToArray()))));
    }

    private static void EmitActSelection(
        IRuntimePredictionDiagnosticSink sink,
        SeedPredictionRequest request,
        SeedPredictionDocument document,
        bool forceTrace)
    {
        Beta109WorldGenerationSnapshot? modern = ProjectModern(request, document);
        if (modern is null) return;
        string failure = modern.SelectedActs.Count > 0
            ? string.Empty
            : FirstMatchingReason(modern.CaptureDiagnosticCode,
                "MissingActCatalog",
                "MissingActsByIndexOrder",
                "MissingUnlockState",
                "MissingDiscoveredActs",
                "MissingMultiplayerMode",
                "MissingTestMode",
                "MissingAct1Override",
                "MissingLobbyPlayerAuthority",
                "ActSelectionGroupIncomplete",
                "MissingSelectedActs");
        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            request.RequestId.Serialized,
            document.ProfileId.ToString(),
            document.CanonicalSeed,
            modern.SelectedActs.Count > 0 ? RuntimePredictionDiagnosticLevel.Info : RuntimePredictionDiagnosticLevel.Warning,
            "ActSelection",
            "ReconstructSelectedActs",
            modern.SelectedActs.Count > 0 && modern.SelectedActsExact
                ? RuntimePredictionDiagnosticStatus.Succeeded
                : RuntimePredictionDiagnosticStatus.Failed,
            failure,
            Details(
                ("actSelectionRootHex", modern.ActSelectionRootExact ? modern.ActSelectionRoot.ToString("X16") : string.Empty),
                ("runSeedRootHex", modern.RunSeedRootExact ? modern.RunSeedRoot.ToString("X16") : string.Empty),
                ("rootValuesEqual", modern.ActSelectionRootExact && modern.RunSeedRootExact && modern.ActSelectionRoot == modern.RunSeedRoot),
                ("streamDomainsIndependent", true),
                ("canReconstructSelectedActs", modern.CanReconstructSelectedActs),
                ("selectedActsExact", modern.SelectedActsExact),
                ("selectedActProvenance", modern.SelectedActProvenance.ToString()),
                ("selectedActs", modern.SelectedActs.Select(key => key.Serialized).ToArray()),
                ("groupOrder", modern.ActSelectionGroups.OrderBy(group => group.Act).Select(group => group.Act).ToArray()),
                ("selectionModes", modern.ActSelectionGroups.OrderBy(group => group.Act).Select(group => group.SelectionMode.ToString()).ToArray()),
                ("estimatedRngDrawOrdinals", EstimatedActSelectionDraws(modern)))));
    }

    private static void EmitUpFrontStages(
        IRuntimePredictionDiagnosticSink sink,
        SeedPredictionRequest request,
        SeedPredictionDocument document,
        bool forceTrace)
    {
        Beta109WorldGenerationSnapshot? modern = ProjectModern(request, document);
        if (modern is null) return;
        PredictionSection? boss = document.Sections.FirstOrDefault(section => section.Kind == PredictionSectionKind.BossIdentity);
        PredictionSection? ancient = document.Sections.FirstOrDefault(section => section.Kind == PredictionSectionKind.AncientIdentityAndOptions);
        string failure = FirstNonEmpty(boss?.IssueCode, ancient?.IssueCode, FirstReason(modern.CaptureDiagnosticCode));
        string failureStage = MapFailureStage(failure);
        IReadOnlyList<WorldRngTraceEntry> trace = CollectUpFrontTrace(document);
        bool detailed = forceTrace || sink.Verbosity >= RuntimePredictionDiagnosticLevel.Trace;
        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            request.RequestId.Serialized,
            document.ProfileId.ToString(),
            document.CanonicalSeed,
            failure.Length == 0 ? RuntimePredictionDiagnosticLevel.Info : RuntimePredictionDiagnosticLevel.Warning,
            "UpFrontReplay",
            "Summary",
            failure.Length == 0
                ? RuntimePredictionDiagnosticStatus.Succeeded
                : RuntimePredictionDiagnosticStatus.Partial,
            failure,
            Details(
                ("prefixKind", modern.UpFrontPrefix.Kind.ToString()),
                ("priorInputsExact", modern.UpFrontPrefix.PriorInputsExact),
                ("firstFailureStage", failureStage),
                ("traceEntryCount", trace.Count),
                ("detailedTraceEnabled", detailed))));

        foreach (string stage in UpFrontStages)
        {
            WorldRngTraceEntry[] stageTrace = trace.Where(item => MapTraceStage(item.SourceStage) == stage).ToArray();
            bool failed = string.Equals(stage, failureStage, StringComparison.Ordinal);
            bool reached = stageTrace.Length > 0;
            RuntimePredictionDiagnosticStatus status = failed
                ? RuntimePredictionDiagnosticStatus.Failed
                : reached
                    ? RuntimePredictionDiagnosticStatus.Succeeded
                    : RuntimePredictionDiagnosticStatus.Skipped;
            if (!detailed && !failed)
            {
                continue;
            }
            RuntimePredictionDiagnosticLevel level = failed
                ? RuntimePredictionDiagnosticLevel.Warning
                : RuntimePredictionDiagnosticLevel.Trace;
            (int before, int after) = CursorRange(stageTrace);
            Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
                request.RequestId.Serialized,
                document.ProfileId.ToString(),
                document.CanonicalSeed,
                level,
                "UpFrontReplay",
                stage,
                status,
                failed ? failure : string.Empty,
                Details(
                    ("inputAuthority", StageAuthority(modern, stage)),
                    ("poolOrCount", StagePoolCount(modern, stage)),
                    ("rngCallShapes", stageTrace.Select(item => item.ConsumptionShape.ToString()).Distinct(StringComparer.Ordinal).ToArray()),
                    ("replayCursorBefore", before),
                    ("replayCursorAfter", after),
                    ("traceEntryCount", stageTrace.Length),
                    ("trace", stageTrace.Select(item => new Dictionary<string, object?>
                    {
                        ["sequence"] = item.Sequence,
                        ["sourceStage"] = item.SourceStage,
                        ["operation"] = item.Operation,
                        ["callCountAfter"] = item.CallCountAfter,
                        ["consumptionShape"] = item.ConsumptionShape.ToString(),
                        ["bound"] = item.Bound,
                        ["intResult"] = item.IntResult,
                        ["doubleResult"] = item.DoubleResult,
                        ["selectedKey"] = item.SelectedKey?.Serialized
                    }).ToArray()))));
        }
    }

    private static void EmitProviderResults(
        IRuntimePredictionDiagnosticSink sink,
        SeedPredictionRequest request,
        SeedPredictionDocument document,
        bool forceTrace)
    {
        PredictionSection? boss = document.Sections.FirstOrDefault(section => section.Kind == PredictionSectionKind.BossIdentity);
        PredictionSection? ancient = document.Sections.FirstOrDefault(section => section.Kind == PredictionSectionKind.AncientIdentityAndOptions);
        if (boss is not null)
        {
            Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
                request.RequestId.Serialized,
                document.ProfileId.ToString(),
                document.CanonicalSeed,
                boss.DomainStatus == SeedDomainEvaluationStatus.Evaluated
                    ? RuntimePredictionDiagnosticLevel.Info
                    : RuntimePredictionDiagnosticLevel.Warning,
                "Provider",
                "Boss",
                ProviderStatus(boss.DomainStatus),
                boss.IssueCode,
                Details(
                    ("domainStatus", boss.DomainStatus.ToString()),
                    ("precision", boss.Bosses.Select(item => item.IdentityPrecision.ToString()).Distinct().ToArray()),
                    ("authority", boss.Bosses.Select(item => item.Authority.ToString()).Distinct().ToArray()),
                    ("completeness", boss.Bosses.Select(item => item.Completeness.ToString()).Distinct().ToArray()),
                    ("bosses", boss.Bosses.Select(item => new Dictionary<string, object?>
                    {
                        ["act"] = item.Act,
                        ["ordinal"] = item.Ordinal,
                        ["modelKey"] = item.BossKey.Serialized,
                        ["rngStream"] = item.RngStream,
                        ["rngCallCount"] = item.RngCallCount,
                        ["authorityFingerprint"] = item.AuthorityFingerprint
                    }).ToArray()))));
        }
        if (ancient is not null)
        {
            Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
                request.RequestId.Serialized,
                document.ProfileId.ToString(),
                document.CanonicalSeed,
                ancient.DomainStatus == SeedDomainEvaluationStatus.Evaluated
                    ? RuntimePredictionDiagnosticLevel.Info
                    : RuntimePredictionDiagnosticLevel.Warning,
                "Provider",
                "Ancient",
                ProviderStatus(ancient.DomainStatus),
                ancient.IssueCode,
                Details(
                    ("domainStatus", ancient.DomainStatus.ToString()),
                    ("identityPrecision", ancient.Ancients.Select(item => item.IdentityPrecision.ToString()).Distinct().ToArray()),
                    ("optionPrecision", ancient.Ancients.Select(item => item.OptionPrecision.ToString()).Distinct().ToArray()),
                    ("authority", ancient.Ancients.Select(item => item.Authority.ToString()).Distinct().ToArray()),
                    ("completeness", ancient.Ancients.Select(item => item.Completeness.ToString()).Distinct().ToArray()),
                    ("ancients", ancient.Ancients.Select(item => new Dictionary<string, object?>
                    {
                        ["act"] = item.Act,
                        ["modelKey"] = item.AncientKey.Serialized,
                        ["identityRngStream"] = item.IdentityRngStream,
                        ["optionRngStream"] = item.OptionRngStream,
                        ["optionsEvaluationStatus"] = item.OptionsEvaluationStatus.ToString(),
                        ["optionIssueCode"] = item.OptionIssueCode,
                        ["orderedOptions"] = item.Options.OrderBy(option => option.Ordinal).Select(option => new Dictionary<string, object?>
                        {
                            ["ordinal"] = option.Ordinal,
                            ["modelKey"] = option.OptionKey.Serialized,
                            ["variantId"] = option.VariantId,
                            ["characterTargetKey"] = option.CharacterTarget?.CharacterKey?.Serialized,
                            ["characterTargetPrecision"] = option.CharacterTarget?.Precision.ToString(),
                            ["characterTargetEvidenceCode"] = option.CharacterTarget?.EvidenceCode.ToString(),
                            ["characterTargetIssueCode"] = option.CharacterTarget?.IssueCode,
                            ["visible"] = option.IsVisible,
                            ["selectable"] = option.IsSelectable,
                            ["locked"] = option.IsLocked,
                            ["appearancePrecision"] = option.AppearancePrecision.ToString(),
                            ["selectabilityPrecision"] = option.SelectabilityPrecision.ToString()
                        }).ToArray(),
                        ["authorityFingerprint"] = item.AuthorityFingerprint
                    }).ToArray()))));
        }
    }

    private static void EmitFirstFailure(
        IRuntimePredictionDiagnosticSink sink,
        SeedPredictionRequest request,
        SeedPredictionDocument document,
        bool forceTrace)
    {
        string[] reasonChain = FullReasonChain(request, document);
        string first = reasonChain.FirstOrDefault() ?? string.Empty;
        Write(sink, forceTrace, RuntimePredictionDiagnosticEvent.Create(
            request.RequestId.Serialized,
            document.ProfileId.ToString(),
            document.CanonicalSeed,
            first.Length == 0 ? RuntimePredictionDiagnosticLevel.Info : RuntimePredictionDiagnosticLevel.Warning,
            "Summary",
            "FirstFailure",
            first.Length == 0
                ? RuntimePredictionDiagnosticStatus.Succeeded
                : RuntimePredictionDiagnosticStatus.Failed,
            first,
            Details(
                ("firstFailureStage", MapFailureStage(first)),
                ("reasonChain", reasonChain),
                ("searchDefiniteMatchAllowed", DefiniteMatchAllowed(document)),
                ("worldAuthorityFingerprint", request.Authority.WorldSnapshotFingerprint))));
    }

    private static bool DefiniteMatchAllowed(SeedPredictionDocument document)
    {
        PredictionSection? boss = document.Sections.FirstOrDefault(section => section.Kind == PredictionSectionKind.BossIdentity);
        PredictionSection? ancient = document.Sections.FirstOrDefault(section => section.Kind == PredictionSectionKind.AncientIdentityAndOptions);
        bool bossOkay = boss is null || (boss.DomainStatus == SeedDomainEvaluationStatus.Evaluated &&
                                         boss.Bosses.All(item => item.IdentityPrecision == PredictionPrecision.Exact));
        bool ancientOkay = ancient is null || (ancient.DomainStatus == SeedDomainEvaluationStatus.Evaluated &&
                                                ancient.Ancients.All(item => item.IdentityPrecision == PredictionPrecision.Exact &&
                                                                 item.OptionPrecision == PredictionPrecision.Exact));
        return bossOkay && ancientOkay;
    }

    private static string[] FullReasonChain(SeedPredictionRequest request, SeedPredictionDocument document)
    {
        IEnumerable<string> capture = SplitReasons(request.Authority.WorldAuthority?.CaptureDiagnosticCode);
        IEnumerable<string> sections = document.Sections
            .Where(section => section.Kind is PredictionSectionKind.BossIdentity or PredictionSectionKind.AncientIdentityAndOptions)
            .Select(section => section.IssueCode)
            .Where(code => !string.IsNullOrWhiteSpace(code));
        IEnumerable<string> diagnostics = document.Diagnostics
            .Where(item => item.Code.Contains("precision-issue", StringComparison.Ordinal) ||
                           item.Code.Contains("world-modern", StringComparison.Ordinal) &&
                           item.Code.Contains("-capture", StringComparison.Ordinal))
            .SelectMany(item => SplitReasons(item.Value));
        return sections.Concat(diagnostics).Concat(capture)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static Beta109WorldGenerationSnapshot? ProjectModern(
        SeedPredictionRequest request,
        SeedPredictionDocument document)
    {
        Beta109WorldGenerationSnapshot? source = request.Authority.WorldAuthority?.Beta109Generation;
        if (source is null || !RuntimeProfilePolicies.IsModernCore(document.ProfileId) ||
            string.IsNullOrWhiteSpace(document.CanonicalSeed))
        {
            return source;
        }
        try
        {
            return request.TrustedRootHashInput is { } trusted
                ? Beta109WorldSnapshotProjector.ProjectForRootHash(source, trusted.RootHash, trusted.SeedIdentity)
                : Beta109WorldSnapshotProjector.ProjectForSeed(source, document.CanonicalSeed);
        }
        catch
        {
            return source;
        }
    }

    private static IReadOnlyDictionary<string, object?> BucketCounts(
        IReadOnlyList<Beta109RelicBucketSnapshot>? buckets) =>
        (buckets ?? Array.Empty<Beta109RelicBucketSnapshot>())
            .ToDictionary(bucket => bucket.BucketId, bucket => (object?)bucket.OrderedRelics.Count, StringComparer.Ordinal);

    private static int[] EstimatedActSelectionDraws(Beta109WorldGenerationSnapshot snapshot)
    {
        int ordinal = 0;
        var output = new List<int>();
        foreach (Beta109ActSelectionGroupSnapshot group in snapshot.ActSelectionGroups.OrderBy(group => group.Act))
        {
            if (group.SelectionMode == Beta109ActSelectionMode.RandomNextItem)
            {
                output.Add(ordinal++);
            }
            else
            {
                output.Add(-1);
            }
        }
        return output.ToArray();
    }

    private static IReadOnlyList<WorldRngTraceEntry> CollectUpFrontTrace(SeedPredictionDocument document)
    {
        IEnumerable<WorldRngTraceEntry> bosses = document.Sections
            .SelectMany(section => section.Bosses)
            .SelectMany(item => item.RngTrace);
        IEnumerable<WorldRngTraceEntry> ancients = document.Sections
            .SelectMany(section => section.Ancients)
            .SelectMany(item => item.IdentityRngTrace);
        return bosses.Concat(ancients)
            .Where(item => string.Equals(item.StreamDomain, "up_front", StringComparison.Ordinal))
            .GroupBy(item => (item.Sequence, item.SourceStage, item.CallCountAfter))
            .Select(group => group.First())
            .OrderBy(item => item.Sequence)
            .ToArray();
    }

    private static (int Before, int After) CursorRange(IReadOnlyList<WorldRngTraceEntry> trace)
    {
        if (trace.Count == 0) return (-1, -1);
        int firstAfter = trace.Min(item => item.CallCountAfter);
        int lastAfter = trace.Max(item => item.CallCountAfter);
        return (Math.Max(0, firstAfter - 1), lastAfter);
    }

    private static string StageAuthority(Beta109WorldGenerationSnapshot snapshot, string stage) => stage switch
    {
        "RunStart" => snapshot.UpFrontPrefix.PriorInputsExact ? "Exact" : "Missing",
        "RelicBags" => snapshot.RelicInitializationExact ? "Exact" : "Missing",
        "SharedAncients" => snapshot.SharedAncientCatalogExact ? "Exact" : "Missing",
        "Events" => snapshot.EventAuthority.HasExactFilteringAuthority &&
                    snapshot.OrderedActCatalog.All(act => act.EventRngConsumptionExact)
            ? "Exact" : "Missing",
        "Encounters" => SelectedActs(snapshot).All(HasExactEncounterAuthority)
            ? "Exact" : "Missing",
        "Rooms" => snapshot.OrderedActCatalog.All(act => act.RoomShapeExact) ? "Exact" : "Missing",
        "AncientIdentity" => snapshot.AllSharedAncientCatalogExact && snapshot.SharedAncientCatalogExact
            ? "Exact" : "Missing",
        "BossSelection" or "SecondBoss" => snapshot.OrderedActCatalog.All(act => act.Bosses.Count > 0 && act.CatalogOrderExact)
            ? "Exact" : "Missing",
        _ => "Unknown"
    };

    private static int StagePoolCount(Beta109WorldGenerationSnapshot snapshot, string stage) => stage switch
    {
        "RelicBags" => snapshot.SharedRelicBuckets.Sum(bucket => bucket.OrderedRelics.Count) +
                       snapshot.PlayerRelicBuckets.Sum(bucket => bucket.OrderedRelics.Count),
        "SharedAncients" => snapshot.SharedAncients.Count,
        "Events" => snapshot.OrderedActCatalog.Sum(act => act.OrderedEligibleEvents.Count),
        "Encounters" => SelectedActs(snapshot).Sum(act => act.OrderedGenerateAllEncounters.Count),
        "Rooms" => snapshot.OrderedActCatalog.Sum(act => act.TotalNormalRooms),
        "AncientIdentity" => snapshot.OrderedActCatalog.Sum(act => act.OrderedAncients.Count) + snapshot.SharedAncients.Count,
        "BossSelection" or "SecondBoss" => snapshot.OrderedActCatalog.Sum(act => act.Bosses.Count),
        _ => 0
    };

    private static IReadOnlyList<Beta109ActGenerationSnapshot> SelectedActs(
        Beta109WorldGenerationSnapshot snapshot) =>
        snapshot.SelectedActs
            .Select(key => snapshot.OrderedActCatalog.FirstOrDefault(act => act.ActKey == key))
            .Where(act => act is not null)
            .Cast<Beta109ActGenerationSnapshot>()
            .ToArray();

    private static bool HasExactEncounterAuthority(Beta109ActGenerationSnapshot act) =>
        act.EncounterCatalogOfficialVanilla &&
        act.GenerateAllEncountersOrderExact &&
        act.EncounterClassificationExact &&
        act.EncounterTagIdentityExact &&
        act.EncounterTagComparerExact &&
        act.EncounterReferenceIdentityExact &&
        act.EncounterWeightModelExact &&
        act.EncounterRetryShapeExact &&
        act.RoomShapeExact;

    private static string DetermineEncounterAuthorityIssue(Beta109WorldGenerationSnapshot snapshot)
    {
        IReadOnlyList<Beta109ActGenerationSnapshot> acts = SelectedActs(snapshot);
        if (acts.Count != snapshot.SelectedActs.Count) return "MissingEncounterCatalog";
        if (acts.Any(act => !act.EncounterCatalogOfficialVanilla)) return "UnsupportedModEncounter";
        if (acts.Any(act => string.Equals(
                act.EncounterAuthorityEvidenceCode,
                "VanillaEncounterSourceShapeMismatch",
                StringComparison.Ordinal)))
            return "EncounterCatalogShapeMismatch";
        if (acts.Any(act => !act.GenerateAllEncountersOrderExact)) return "MissingGenerateAllEncountersOrder";
        if (acts.Any(act => !act.EncounterClassificationExact)) return "MissingEncounterClassificationAuthority";
        if (acts.Any(act => !act.EncounterTagIdentityExact)) return "MissingEncounterTagAuthority";
        if (acts.Any(act => !act.EncounterTagComparerExact)) return "MissingEncounterTagComparer";
        if (acts.Any(act => !act.EncounterReferenceIdentityExact)) return "MissingEncounterReferenceIdentity";
        if (acts.Any(act => !act.EncounterWeightModelExact)) return "MissingEncounterWeightAuthority";
        if (acts.Any(act => !act.EncounterRetryShapeExact)) return "MissingEncounterRetryCallShape";
        if (acts.Any(act => !act.RoomShapeExact)) return "MissingRoomShapeAuthority";
        return string.Empty;
    }

    private static string MapTraceStage(string sourceStage)
    {
        if (sourceStage.StartsWith("initialize:", StringComparison.Ordinal)) return "RelicBags";
        if (sourceStage.StartsWith("shared-ancient:", StringComparison.Ordinal)) return "SharedAncients";
        if (sourceStage.Contains(":event-shuffle", StringComparison.Ordinal)) return "Events";
        if (sourceStage.Contains(":weak", StringComparison.Ordinal) ||
            sourceStage.Contains(":regular", StringComparison.Ordinal) ||
            sourceStage.Contains(":elite", StringComparison.Ordinal)) return "Encounters";
        if (sourceStage.Contains(":ancient:identity", StringComparison.Ordinal)) return "AncientIdentity";
        if (sourceStage.Contains(":boss:second", StringComparison.Ordinal)) return "SecondBoss";
        if (sourceStage.Contains(":boss:first", StringComparison.Ordinal)) return "BossSelection";
        return "RunStart";
    }

    private static string MapFailureStage(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode)) return string.Empty;
        if (reasonCode.Contains("Act", StringComparison.Ordinal) || reasonCode.Contains("SelectedActs", StringComparison.Ordinal))
            return "ActSelection";
        if (reasonCode.Contains("Relic", StringComparison.Ordinal)) return "RelicBags";
        if (reasonCode.Contains("SharedAncient", StringComparison.Ordinal)) return "SharedAncients";
        if (reasonCode.Contains("Event", StringComparison.Ordinal)) return "Events";
        if (reasonCode.Contains("Encounter", StringComparison.Ordinal)) return "Encounters";
        if (reasonCode.Contains("Room", StringComparison.Ordinal)) return "Rooms";
        if (reasonCode.Contains("Ancient", StringComparison.Ordinal)) return "AncientIdentity";
        if (reasonCode.Contains("SecondBoss", StringComparison.Ordinal)) return "SecondBoss";
        if (reasonCode.Contains("Boss", StringComparison.Ordinal)) return "BossSelection";
        if (reasonCode.Contains("UpFront", StringComparison.Ordinal) ||
            reasonCode.Contains("RunSeed", StringComparison.Ordinal) ||
            reasonCode.Contains("Mode", StringComparison.Ordinal) ||
            reasonCode.Contains("Hook", StringComparison.Ordinal) ||
            reasonCode.Contains("Fixture", StringComparison.Ordinal)) return "RunStart";
        return "Provider";
    }

    private static RuntimePredictionDiagnosticStatus ProviderStatus(SeedDomainEvaluationStatus status) => status switch
    {
        SeedDomainEvaluationStatus.Evaluated => RuntimePredictionDiagnosticStatus.Succeeded,
        SeedDomainEvaluationStatus.Unsupported => RuntimePredictionDiagnosticStatus.Failed,
        _ => RuntimePredictionDiagnosticStatus.Partial
    };

    private static string FirstMatchingReason(string chain, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (chain.Contains(candidate, StringComparison.Ordinal)) return candidate;
        }
        return FirstReason(chain);
    }

    private static string FirstReason(string? chain) => SplitReasons(chain).FirstOrDefault() ?? string.Empty;

    private static string[] SplitReasons(string? chain) => string.IsNullOrWhiteSpace(chain)
        ? Array.Empty<string>()
        : chain.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static IReadOnlyDictionary<string, object?> Details(params (string Key, object? Value)[] items) =>
        items.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static void Write(
        IRuntimePredictionDiagnosticSink sink,
        bool force,
        RuntimePredictionDiagnosticEvent diagnosticEvent) => sink.TryWrite(diagnosticEvent, force);
}
