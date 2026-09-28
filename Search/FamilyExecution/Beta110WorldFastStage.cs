using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum WorldFastPredicateScope : byte
{
    BossAct,
    BossOrdinal,
    AncientIdentity,
    AncientBranchIdentity,
    EventStaticCandidate
}

internal readonly record struct WorldFastDenseSetPredicate(
    ushort[] Any,
    ushort[] All,
    ushort[] Ban,
    bool AlwaysReject)
{
    public bool IsEmpty => Any.Length == 0 && All.Length == 0 && Ban.Length == 0;
}

internal readonly record struct WorldFastEventCandidate(
    ushort EventId,
    byte Source,
    bool StaticEligible);

internal readonly record struct WorldFastEventPredicate(
    byte RangeMode,
    uint RangeValue,
    byte SourceFilter,
    WorldFastDenseSetPredicate Keys);

internal readonly record struct WorldFamilyBossBranch(WorldFastDenseSetPredicate First, WorldFastDenseSetPredicate Second);

internal readonly record struct WorldFastEncounterEntry(
    ushort EncounterId,
    int SourceOrdinal,
    int ReferenceIdentityId,
    int ConflictOffset,
    ushort ConflictCount);

internal sealed record WorldFastActPlan(
    int Act,
    ushort ActId,
    int WeakEncounterSlots,
    int RegularEncounterSlots,
    int EliteEncounterSlots,
    WorldFastEventCandidate[] EventCandidates,
    int EligibleEventCount,
    WorldFastEncounterEntry[] WeakEncounters,
    WorldFastEncounterEntry[] RegularEncounters,
    WorldFastEncounterEntry[] EliteEncounters,
    int[] EncounterConflictSourceOrdinals,
    ushort[] Bosses,
    ushort[] Ancients,
    WorldFastDenseSetPredicate[] BossActPredicates,
    WorldFastDenseSetPredicate[] BossOrdinalOnePredicates,
    WorldFastDenseSetPredicate[] BossOrdinalTwoPredicates,
    WorldFastDenseSetPredicate[] AncientIdentityPredicates,
    WorldFastDenseSetPredicate[] AncientBranchIdentityPredicates,
    WorldFastAncientOptionPlan[] AncientOptionPlans,
    WorldFastAncientBranchOptionPredicate[] AncientBranchOptionPredicates,
    WorldFastDenseSetPredicate[] AncientOptionPredicates,
    WorldFastDenseSetPredicate[] SeaGlassTargetPredicates,
    WorldFastEventPredicate[] EventPredicates)
{
    internal bool FamilyVariantAllowed { get; init; } = true;
    internal WorldFamilyBossBranch[] FamilyBossBranches { get; init; } = [];
    internal ushort FamilyFirstBossOverride { get; init; } = ushort.MaxValue;
    internal bool FamilyBossBranchesConjunctive { get; init; }
    public bool HasEventPredicate => EventPredicates.Length > 0;
    public bool HasBossPredicate =>
        BossActPredicates.Length > 0 ||
        BossOrdinalOnePredicates.Length > 0 ||
        BossOrdinalTwoPredicates.Length > 0 || FamilyBossBranches.Any(b => !b.First.IsEmpty || b.First.AlwaysReject || !b.Second.IsEmpty || b.Second.AlwaysReject);
    public bool HasAncientIdentityPredicate =>
        AncientIdentityPredicates.Length > 0 || AncientBranchIdentityPredicates.Length > 0;
    public bool HasAncientOptionPredicate
    {
        get
        {
            if (AncientOptionPredicates.Length > 0) return true;
            foreach (WorldFastAncientBranchOptionPredicate predicate in AncientBranchOptionPredicates)
                if (predicate.OptionFastReject) return true;
            return false;
        }
    }
    public bool HasSeaGlassPredicate
    {
        get
        {
            if (SeaGlassTargetPredicates.Length > 0) return true;
            foreach (WorldFastAncientBranchOptionPredicate predicate in AncientBranchOptionPredicates)
                if (predicate.SeaGlassFastReject) return true;
            return false;
        }
    }
    public bool HasAncientPredicate =>
        HasAncientIdentityPredicate || HasAncientOptionPredicate || HasSeaGlassPredicate;
}

internal readonly record struct WorldFastActSelectionGroup(
    int Act,
    byte SelectionMode,
    ushort[] EligibleActIds);

/// <summary>
/// Immutable numeric World plan. It contains only primitive arrays and dense IDs;
/// no live game model, Godot object, delegate, event execution or mutable runtime
/// object crosses into the worker.
/// </summary>
internal sealed record Beta110WorldFastPlan(
    bool Enabled,
    bool AlwaysRejectCandidate,
    WorldFastActSelectionGroup[] ActSelectionGroups,
    ushort Act1OverrideId,
    short[] ActPlanIndexByDenseId,
    WorldFastActPlan[] Acts,
    ushort[] SharedAncients,
    int Ascension,
    int MaxRequiredAct,
    bool RequiresSecondBoss,
    int PredicateCount,
    int EventPredicateCount,
    int BossPredicateCount,
    int AncientIdentityPredicateCount,
    int AncientOptionPredicateCount,
    int SeaGlassPredicateCount,
    int AncientOptionExactOnlyPredicateCount,
    int SeaGlassExactOnlyPredicateCount,
    int MaxEventCandidateCount,
    int MaxEncounterPoolCount,
    string RequiredFarthestStage,
    string AuthorityFingerprint,
    string AncientOptionInputFingerprint,
    string DeckPresetFingerprint,
    string Fingerprint,
    string DisableReason)
{
    public const ushort InvalidDenseId = ushort.MaxValue;

    public static Beta110WorldFastPlan Disabled(
        string reason,
        int ancientOptionExactOnlyPredicateCount = 0,
        int seaGlassExactOnlyPredicateCount = 0,
        string authorityFingerprint = "",
        string ancientOptionInputFingerprint = "",
        string deckPresetFingerprint = "") => new(
        false,
        false,
        Array.Empty<WorldFastActSelectionGroup>(),
        InvalidDenseId,
        Array.Empty<short>(),
        Array.Empty<WorldFastActPlan>(),
        Array.Empty<ushort>(),
        0,
        0,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        ancientOptionExactOnlyPredicateCount,
        seaGlassExactOnlyPredicateCount,
        0,
        0,
        ancientOptionExactOnlyPredicateCount > 0 || seaGlassExactOnlyPredicateCount > 0
            ? "ProductionExact"
            : "None",
        authorityFingerprint,
        ancientOptionInputFingerprint,
        deckPresetFingerprint,
        string.Empty,
        reason);

    public bool HasEventFast => EventPredicateCount > 0;
    public bool HasBossFast => BossPredicateCount > 0;
    public bool HasAncientIdentityFast => AncientIdentityPredicateCount > 0;
    public bool HasAncientOptionFast => AncientOptionPredicateCount > 0;
    public bool HasSeaGlassFast => SeaGlassPredicateCount > 0;
    public bool HasAncientFast => HasAncientIdentityFast || HasAncientOptionFast || HasSeaGlassFast;
    [Obsolete("Use the dimension-specific Ancient predicate counts.")]
    public int AncientPredicateCount => AncientIdentityPredicateCount + AncientOptionPredicateCount + SeaGlassPredicateCount;
}

internal readonly record struct WorldFastStageTelemetry(
    bool Evaluated,
    bool Rejected,
    int StageInput,
    int StageOutput,
    int SelectedActCount,
    int EventShuffleCalls,
    int EncounterDrawCalls,
    int BossDrawCalls,
    int AncientDrawCalls,
    int StaticCandidatesVisited,
    int EventStageInput,
    int EventStageOutput,
    int BossStageInput,
    int BossStageOutput,
    int AncientIdentityStageInput,
    int AncientIdentityStageOutput,
    int AncientOptionStageInput,
    int AncientOptionStageOutput,
    int SeaGlassStageInput,
    int SeaGlassStageOutput)
{
    public static WorldFastStageTelemetry None => default;
    public double ConditionalSurvival => StageInput == 0 ? 0d : (double)StageOutput / StageInput;
}

internal enum WorldFastStageDiagnosticCode : byte
{
    None,
    PlanDisabled,
    SharedPrefixUnavailable,
    ActSelectionUnavailable,
    WorldContinuationUnavailable,
    EventPredicateRejected,
    BossPredicateRejected,
    AncientPredicateRejected,
    AncientOptionPredicateRejected,
    SeaGlassPredicateRejected
}

internal readonly record struct Beta110WorldFastStageResult(
    Beta110FastDecision Decision,
    WorldFastStageTelemetry Telemetry,
    WorldFastStageDiagnosticCode DiagnosticCode)
{
    public static Beta110WorldFastStageResult NotEvaluated => new(
        Beta110FastDecision.CandidateKeep,
        WorldFastStageTelemetry.None,
        WorldFastStageDiagnosticCode.PlanDisabled);
}

internal static class Beta110WorldFastPlanCompiler
{
    private const int MaximumDenseEntries = ushort.MaxValue - 1;
    private const int MaximumActSelectionGroups = 8;
    private const int MaximumActPlans = 64;
    private const int MaximumEventCandidates = 512;
    private const int MaximumEncounterEntriesPerPool = 512;
    private const int MaximumSharedAncients = 512;

    internal static Beta110WorldFastPlan CompileForFamily(ExactSearchExecutionRequest request, NeowSearchFilter filter,
        IReadOnlyList<RolltheSpire2.Search.Semantics.VariantScopedBossBranch> branches) => CompileCore(
        filter, request.Authority, request.ProfileId, request.Ascension, request.SnapshotFingerprint, true, branches);

    private static Beta110WorldFastPlan CompileCore(NeowSearchFilter filter, RuntimeContextAuthoritySnapshot authority,
        RuntimeProfileId profile, int ascension, string snapshotFingerprint, bool family,
        IReadOnlyList<RolltheSpire2.Search.Semantics.VariantScopedBossBranch> branches)
    {
        bool hasBossRequest = filter.BossFilters.Any(item => !item.IsEmpty) ||
                              filter.BossOrdinalFilters.Any(item => !item.IsEmpty);
        int requestedAncientOptionPredicateCount = filter.AncientOptionFilters.Count(item => !item.IsEmpty) +
                                                    filter.AncientBranchConditions.Count(item => item.OptionAny.Count > 0);
        int requestedSeaGlassPredicateCount = filter.AncientSeaGlassTargetFilters.Count(item => !item.IsEmpty) +
                                              filter.AncientBranchConditions.Count(item => item.SeaGlassTargetAny.Count > 0);
        bool hasAncientOptionRequest = requestedAncientOptionPredicateCount > 0;
        bool hasSeaGlassRequest = requestedSeaGlassPredicateCount > 0;
        bool hasAncientIdentityRequest = filter.AncientIdentityFilters.Any(item => !item.IsEmpty) ||
                                         filter.AncientBranchConditions.Any(item => item.IsValid) ||
                                         hasAncientOptionRequest || hasSeaGlassRequest;
        bool hasEventRequest = filter.EventSequenceConditions.Any(item => !item.IsEmpty);
        if (!hasBossRequest && !hasAncientIdentityRequest && !hasAncientOptionRequest && !hasSeaGlassRequest && !hasEventRequest && branches.Count == 0)
            return Beta110WorldFastPlan.Disabled("NoWorldFastPredicates");
        if (!family && filter.EventSequenceConditions.Any(item => !item.IsEmpty && item.RangeValue > byte.MaxValue))
            hasEventRequest = false;

        WorldAuthoritySnapshot? world = authority.WorldAuthority;
        Beta109WorldGenerationSnapshot? generation = world?.Beta109Generation;
        if (world is null || generation is null ||
            !RuntimeProfilePolicies.UsesBeta110SharedAlgorithms(profile) ||
            world.CapturedProfileId != profile ||
            generation.Profile != profile)
        {
            return Beta110WorldFastPlan.Disabled("MissingModernWorldAuthority");
        }

        bool exactFoundation =
            (SourceAuthorityRules.SupportsExactIdentity(world.SourceAuthority) || authority.UsesBestEffortModel) &&
            (world.Completeness == SnapshotCompleteness.Complete || authority.UsesBestEffortModel) &&
            generation.HasExactWorldFastPlanInputs &&
            generation.CanReconstructSelectedActs &&
            (generation.WorldGenerationHooksExact || authority.UsesBestEffortModel) &&
            generation.DirectSourceAudited &&
            (generation.IsVanilla || authority.UsesBestEffortModel) &&
            ((generation.GameMode == WorldGameMode.Singleplayer && !generation.IsMultiplayer && generation.PlayerCount == 1) ||
                family && generation.HasExactFixedParty);
        if (!exactFoundation)
            return Beta110WorldFastPlan.Disabled("WorldNumericAuthorityIncomplete");
        // Family consumes exact numerical inputs; historical fixture-promotion
        // flags are not an execution Gate. Tutorial override is real semantics.
        if (family && ((hasBossRequest || branches.Any(b => !b.FirstBoss.IsEmpty || !b.SecondBoss.IsEmpty)) &&
            generation.TutorialBossOverrideWillApply && !(generation.IsMultiplayer && generation.PartyBossDiscoveryExact) ||
            hasEventRequest && (EventStaticEligibilityCatalog.ProjectionPrecision(profile) != PredictionPrecision.Exact || !generation.EventAuthority.HasExactFilteringAuthority)))
            return Beta110WorldFastPlan.Disabled("WRequestedAuthorityIncomplete");
        if (!family && hasBossRequest && !generation.CanReplayBossIdentityForFastSearch)
            hasBossRequest = false;
        if (!family && hasAncientIdentityRequest && !generation.AllowsAncientIdentityFastSearch)
        {
            hasAncientIdentityRequest = false;
            hasAncientOptionRequest = false;
            hasSeaGlassRequest = false;
        }
        if (hasEventRequest &&
            (EventStaticEligibilityCatalog.ProjectionPrecision(profile) != PredictionPrecision.Exact ||
             !generation.EventAuthority.HasExactFilteringAuthority))
        {
            hasEventRequest = false;
        }
        if (!hasBossRequest && !hasAncientIdentityRequest && !hasAncientOptionRequest && !hasSeaGlassRequest && !hasEventRequest && branches.Count == 0)
            return Beta110WorldFastPlan.Disabled(
                "RequestedWorldPredicatesRemainExactOnly",
                requestedAncientOptionPredicateCount,
                requestedSeaGlassPredicateCount,
                authorityFingerprint: Fingerprint(new[]
                {
                    generation.SnapshotFingerprint,
                    filter.AncientOptionConditions.Fingerprint,
                    "RequestedWorldPredicatesRemainExactOnly"
                }),
                ancientOptionInputFingerprint: Fingerprint(new[]
                {
                    generation.CatalogFingerprint,
                    generation.UnlockFingerprint,
                    generation.LobbyPlayers[0].CharacterKey.Serialized,
                    ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    filter.AncientOptionConditions.Fingerprint
                }),
                deckPresetFingerprint: filter.AncientOptionConditions.Fingerprint);

        if (generation.ActSelectionGroups.Count == 0 || generation.OrderedActCatalog.Count == 0)
            return Beta110WorldFastPlan.Disabled("WorldNumericPlanAuthorityEmpty");
        if (generation.ActSelectionGroups.Count > MaximumActSelectionGroups ||
            generation.OrderedActCatalog.Count > MaximumActPlans ||
            generation.SharedAncients.Count > MaximumSharedAncients)
        {
            return Beta110WorldFastPlan.Disabled("WorldNumericPlanCapacityExceeded");
        }

        var preparedEvents = new Dictionary<ModelKey, EventPoolSequenceProjector.PreparedAct>(ModelKeyComparer.Instance);
        foreach (Beta109ActGenerationSnapshot act in generation.OrderedActCatalog)
        {
            bool needEventMaterialization = hasEventRequest &&
                filter.EventSequenceConditions.Any(condition => !condition.IsEmpty && condition.Act == act.Act);
            if (!needEventMaterialization) continue;
            if (!EventPoolSequenceProjector.TryPrepare(
                    act.Act,
                    act.ActKey,
                    act.EventRngConsumptionExact,
                    act.OrderedRawEvents,
                    generation.EventAuthority.OrderedSharedEventsRaw,
                    generation.EventAuthority,
                    act.OrderedEligibleEvents,
                    out EventPoolSequenceProjector.PreparedAct? prepared,
                    out string issue) || prepared is null)
            {
                return Beta110WorldFastPlan.Disabled("EventStaticCandidateAuthority:" + issue);
            }
            if (prepared.Candidates.Count > MaximumEventCandidates)
                return Beta110WorldFastPlan.Disabled("EventCandidateCapacityExceeded");
            preparedEvents[act.ActKey] = prepared;
        }

        var denseByKey = new Dictionary<ModelKey, ushort>(ModelKeyComparer.Instance);
        ushort Add(ModelKey key)
        {
            if (denseByKey.TryGetValue(key, out ushort existing)) return existing;
            if (denseByKey.Count >= MaximumDenseEntries)
                throw new InvalidOperationException("WorldDenseCatalogCapacityExceeded");
            ushort created = checked((ushort)denseByKey.Count);
            denseByKey.Add(key, created);
            return created;
        }

        try
        {
            foreach (Beta109ActSelectionGroupSnapshot group in generation.ActSelectionGroups)
                foreach (ModelKey key in group.EligibleActsInSourceOrder) Add(key);
            if (generation.Act1OverrideResolvedKey is ModelKey overrideKey && overrideKey.IsValid) Add(overrideKey);
            foreach (ModelKey key in generation.SharedAncients) Add(key);
            foreach (ModelKey key in generation.PartyBossDiscoveryOverrides.Values) Add(key);
            foreach (Beta109ActGenerationSnapshot act in generation.OrderedActCatalog)
            {
                Add(act.ActKey);
                foreach (ModelKey key in act.OrderedEligibleEvents) Add(key);
                foreach (Beta109EncounterEntrySnapshot entry in act.OrderedGenerateAllEncounters) Add(entry.EncounterKey);
                foreach (ModelKey key in act.Bosses) Add(key);
                foreach (ModelKey key in act.OrderedAncients) Add(key);
            }
        }
        catch (InvalidOperationException ex)
        {
            return Beta110WorldFastPlan.Disabled(ex.Message);
        }

        WorldFastDenseSetPredicate CompileSet(ModelKeySetFilter keys)
        {
            var any = new List<ushort>();
            var all = new List<ushort>();
            var ban = new List<ushort>();
            bool alwaysReject = false;
            foreach (ModelKey key in keys.Any.Distinct(ModelKeyComparer.Instance))
                if (denseByKey.TryGetValue(key, out ushort id)) any.Add(id);
            if (keys.Any.Count > 0 && any.Count == 0) alwaysReject = true;
            foreach (ModelKey key in keys.All.Distinct(ModelKeyComparer.Instance))
            {
                if (denseByKey.TryGetValue(key, out ushort id)) all.Add(id);
                else alwaysReject = true;
            }
            foreach (ModelKey key in keys.Ban.Distinct(ModelKeyComparer.Instance))
                if (denseByKey.TryGetValue(key, out ushort id)) ban.Add(id);
            return new WorldFastDenseSetPredicate(any.ToArray(), all.ToArray(), ban.ToArray(), alwaysReject);
        }

        var actPlans = new List<WorldFastActPlan>(generation.OrderedActCatalog.Count);
        int maxEvent = 0;
        int maxEncounter = 0;
        bool planAlwaysReject = false;
        int eventPredicateCount = 0;
        int bossPredicateCount = 0;
        int ancientIdentityPredicateCount = 0;
        int ancientOptionPredicateCount = 0;
        int seaGlassPredicateCount = 0;
        int ancientOptionExactOnlyPredicateCount = 0;
        int seaGlassExactOnlyPredicateCount = 0;
        int maxRequiredAct = 0;
        bool requiresSecondBoss = false;

        foreach (Beta109ActGenerationSnapshot act in generation.OrderedActCatalog)
        {
            // The game still consumes the boss draw before replacing its result.
            // Keep the original pool for that draw and the Act 3 second-boss pool.
            ushort firstBossOverride = family && generation.IsMultiplayer &&
                generation.PartyBossDiscoveryOverrides.TryGetValue(act.ActKey.Serialized, out var replacement)
                ? Add(replacement) : ushort.MaxValue;
            ModelKey[] possibleAncients = act.OrderedAncients
                .Concat(generation.SharedAncients)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            WorldFastAncientOptionCompilation optionCompilation =
                Beta110AncientOptionFastPlanCompiler.Compile(
                    generation,
                    filter,
                    act.Act,
                    possibleAncients,
                    Add,
                    denseByKey);

            WorldFastDenseSetPredicate[] bossAct = hasBossRequest
                ? filter.BossFilters.Where(item => !item.IsEmpty && item.Act == act.Act)
                    .Select(item => CompileSet(item.Keys)).ToArray()
                : Array.Empty<WorldFastDenseSetPredicate>();
            WorldFastDenseSetPredicate[] bossOne = hasBossRequest
                ? filter.BossOrdinalFilters.Where(item => !item.IsEmpty && item.Act == act.Act && item.Ordinal == 1)
                    .Select(item => CompileSet(item.Keys)).ToArray()
                : Array.Empty<WorldFastDenseSetPredicate>();
            WorldFastDenseSetPredicate[] bossTwo = hasBossRequest && ascension >= 10 && act.Act == 3
                ? filter.BossOrdinalFilters.Where(item => !item.IsEmpty && item.Act == act.Act && item.Ordinal == 2)
                    .Select(item => CompileSet(item.Keys)).ToArray()
                : Array.Empty<WorldFastDenseSetPredicate>();
            WorldFastDenseSetPredicate[] ancientIdentity = hasAncientIdentityRequest
                ? filter.AncientIdentityFilters.Where(item => !item.IsEmpty && item.Act == act.Act)
                    .Select(item => CompileSet(item.Keys)).ToArray()
                : Array.Empty<WorldFastDenseSetPredicate>();
            WorldFastDenseSetPredicate[] ancientBranches = hasAncientIdentityRequest
                ? CompileAncientBranchPredicates(filter, act.Act, denseByKey)
                : Array.Empty<WorldFastDenseSetPredicate>();
            WorldFastEventPredicate[] eventPredicates = hasEventRequest
                ? filter.EventSequenceConditions.Where(item => !item.IsEmpty && item.Act == act.Act)
                    .Select(item => new WorldFastEventPredicate(
                        item.RangeMode == SearchSequenceRangeMode.FirstN ? (byte)0 : (byte)1,
                        checked((uint)item.RangeValue),
                        item.Source.HasValue ? (byte)item.Source.Value : byte.MaxValue,
                        CompileSet(item.Keys)))
                    .ToArray()
                : Array.Empty<WorldFastEventPredicate>();

            if (bossAct.Length + bossOne.Length + bossTwo.Length > 0)
            {
                maxRequiredAct = Math.Max(maxRequiredAct, act.Act);
                bossPredicateCount += bossAct.Length + bossOne.Length + bossTwo.Length;
            }
            if (ancientIdentity.Length + ancientBranches.Length > 0)
            {
                maxRequiredAct = Math.Max(maxRequiredAct, act.Act);
                ancientIdentityPredicateCount += ancientIdentity.Length + ancientBranches.Length;
            }
            if (optionCompilation.AncientOptionPredicateCount > 0 ||
                optionCompilation.SeaGlassPredicateCount > 0)
            {
                maxRequiredAct = Math.Max(maxRequiredAct, act.Act);
                ancientOptionPredicateCount += optionCompilation.AncientOptionPredicateCount;
                seaGlassPredicateCount += optionCompilation.SeaGlassPredicateCount;
            }
            ancientOptionExactOnlyPredicateCount += optionCompilation.AncientOptionExactOnlyPredicateCount;
            seaGlassExactOnlyPredicateCount += optionCompilation.SeaGlassExactOnlyPredicateCount;
            if (eventPredicates.Length > 0)
            {
                maxRequiredAct = Math.Max(maxRequiredAct, act.Act);
                eventPredicateCount += eventPredicates.Length;
            }
            if (ascension >= 10 && act.Act == 3 &&
                (bossAct.Length > 0 || bossTwo.Length > 0))
            {
                requiresSecondBoss = true;
            }

            planAlwaysReject |= bossAct.Any(item => item.AlwaysReject) ||
                                bossOne.Any(item => item.AlwaysReject) ||
                                bossTwo.Any(item => item.AlwaysReject) ||
                                ancientIdentity.Any(item => item.AlwaysReject) ||
                                ancientBranches.Any(item => item.AlwaysReject) ||
                                optionCompilation.AlwaysReject ||
                                eventPredicates.Any(item => item.Keys.AlwaysReject);

            WorldFastEventCandidate[] eventCandidates = Array.Empty<WorldFastEventCandidate>();
            if (preparedEvents.TryGetValue(act.ActKey, out EventPoolSequenceProjector.PreparedAct? prepared))
            {
                eventCandidates = prepared.Candidates.Select(candidate => new WorldFastEventCandidate(
                    Add(candidate.EventKey),
                    (byte)candidate.Source,
                    !EventStaticEligibilityCatalog.Evaluate(
                        candidate.EventKey,
                        act.Act,
                        profile,
                        candidate.Source, family ? new EventImmutableEligibilityContext(generation.GameMode, generation.ModeFactsExact, generation.PlayerCount, generation.ModeFactsExact && generation.IsMultiplayerExact, generation.EventAuthority.CharacterCardPoolCount, generation.EventAuthority.CharacterCardPoolCountExact) : null).ShouldReject))
                    .ToArray();
            }
            maxEvent = Math.Max(maxEvent, Math.Max(eventCandidates.Length, act.OrderedEligibleEvents.Count));

            // One flattened conflict table per act is required because offsets must
            // remain valid across weak/regular/elite arrays. Compile in one pass.
            var allConflicts = new List<int>();
            WorldFastEncounterEntry[] CompilePool(IReadOnlyList<Beta109EncounterEntrySnapshot> source)
            {
                var entries = new WorldFastEncounterEntry[source.Count];
                for (int index = 0; index < source.Count; index++)
                {
                    Beta109EncounterEntrySnapshot item = source[index];
                    int offset = allConflicts.Count;
                    allConflicts.AddRange(item.ExactTagConflictSourceOrdinals);
                    entries[index] = new WorldFastEncounterEntry(
                        Add(item.EncounterKey),
                        item.SourceOrdinal,
                        item.ReferenceIdentityId,
                        offset,
                        checked((ushort)(allConflicts.Count - offset)));
                }
                return entries;
            }

            WorldFastEncounterEntry[] weak = CompilePool(act.WeakEncounters);
            WorldFastEncounterEntry[] regular = CompilePool(act.RegularEncounters);
            WorldFastEncounterEntry[] elite = CompilePool(act.EliteEncounters);
            if (weak.Length > MaximumEncounterEntriesPerPool ||
                regular.Length > MaximumEncounterEntriesPerPool ||
                elite.Length > MaximumEncounterEntriesPerPool)
                return Beta110WorldFastPlan.Disabled("EncounterPoolCapacityExceeded");
            maxEncounter = Math.Max(maxEncounter, Math.Max(weak.Length, Math.Max(regular.Length, elite.Length)));

            actPlans.Add(new WorldFastActPlan(
                act.Act,
                Add(act.ActKey),
                act.WeakEncounterSlots,
                Math.Max(0, act.TotalNormalRooms - act.WeakEncounterSlots),
                act.EliteEncounterSlots,
                eventCandidates,
                act.OrderedEligibleEvents.Count,
                weak,
                regular,
                elite,
                allConflicts.ToArray(),
                act.Bosses.Select(Add).ToArray(),
                act.OrderedAncients.Select(Add).ToArray(),
                bossAct,
                bossOne,
                bossTwo,
                ancientIdentity,
                ancientBranches,
                optionCompilation.Plans,
                optionCompilation.BranchPredicates,
                optionCompilation.LegacyOptionPredicates,
                optionCompilation.LegacySeaGlassPredicates,
                eventPredicates)
            {
                FamilyVariantAllowed = !family || !branches.Any(b => b.Act == act.Act) || branches.Any(b => b.Act == act.Act && b.VariantKey == act.ActKey),
                FamilyFirstBossOverride = firstBossOverride,
                FamilyBossBranchesConjunctive = family && generation.IsMultiplayer,
                FamilyBossBranches = branches.Where(b => b.Act == act.Act && b.VariantKey == act.ActKey).Select(b => new WorldFamilyBossBranch(CompileSet(b.FirstBoss), CompileSet(b.SecondBoss))).ToArray()
            });
            if (branches.Any(b => b.Act == act.Act && (!b.FirstBoss.IsEmpty || !b.SecondBoss.IsEmpty)))
            {
                maxRequiredAct = Math.Max(maxRequiredAct, act.Act);
                bossPredicateCount++;
                requiresSecondBoss |= ascension >= 10 && act.Act == 3 && branches.Any(b => b.Act == act.Act && !b.SecondBoss.IsEmpty);
            }
        }

        if (eventPredicateCount + bossPredicateCount + ancientIdentityPredicateCount +
            ancientOptionPredicateCount + seaGlassPredicateCount == 0 && branches.Count == 0)
            return Beta110WorldFastPlan.Disabled(
                "NoAuthorityCompleteWorldPredicate",
                ancientOptionExactOnlyPredicateCount,
                seaGlassExactOnlyPredicateCount,
                authorityFingerprint: Fingerprint(new[]
                {
                    generation.SnapshotFingerprint,
                    filter.AncientOptionConditions.Fingerprint,
                    "NoAuthorityCompleteWorldPredicate"
                }),
                ancientOptionInputFingerprint: Fingerprint(new[]
                {
                    generation.CatalogFingerprint,
                    generation.UnlockFingerprint,
                    generation.LobbyPlayers[0].CharacterKey.Serialized,
                    ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    filter.AncientOptionConditions.Fingerprint
                }),
                deckPresetFingerprint: filter.AncientOptionConditions.Fingerprint);

        var selectionGroups = generation.ActSelectionGroups
            .OrderBy(group => group.Act)
            .Select(group => new WorldFastActSelectionGroup(
                group.Act,
                group.SelectionMode switch
                {
                    Beta109ActSelectionMode.RandomNextItem => (byte)0,
                    Beta109ActSelectionMode.DeterministicFirst => (byte)1,
                    _ => byte.MaxValue
                },
                group.EligibleActsInSourceOrder.Select(Add).ToArray()))
            .ToArray();
        if (selectionGroups.Any(group => group.SelectionMode == byte.MaxValue || group.EligibleActIds.Length == 0))
            return Beta110WorldFastPlan.Disabled("ActSelectionModeUnsupported");

        short[] planByDense = Enumerable.Repeat((short)-1, denseByKey.Count).ToArray();
        for (int index = 0; index < actPlans.Count; index++)
            planByDense[actPlans[index].ActId] = checked((short)index);
        if (selectionGroups.SelectMany(group => group.EligibleActIds).Any(id => id >= planByDense.Length || planByDense[id] < 0))
            return Beta110WorldFastPlan.Disabled("ActSelectionCatalogAlignmentMismatch");

        ushort overrideId = generation.Act1OverrideResolvedKey is ModelKey act1Override && act1Override.IsValid
            ? Add(act1Override)
            : Beta110WorldFastPlan.InvalidDenseId;
        if (overrideId != Beta110WorldFastPlan.InvalidDenseId &&
            (overrideId >= planByDense.Length || planByDense[overrideId] < 0))
            return Beta110WorldFastPlan.Disabled("Act1OverrideCatalogAlignmentMismatch");

        string fingerprint = Fingerprint(new[]
        {
            snapshotFingerprint,
            generation.SnapshotFingerprint,
            generation.GenerationRuleFingerprint,
            EventStaticEligibilityCatalog.CatalogVersion,
            ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
            maxRequiredAct.ToString(System.Globalization.CultureInfo.InvariantCulture),
            requiresSecondBoss.ToString(),
            eventPredicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bossPredicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ancientIdentityPredicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ancientOptionPredicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            seaGlassPredicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ancientOptionExactOnlyPredicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            seaGlassExactOnlyPredicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            filter.AncientOptionConditions.Fingerprint,
            string.Join(';', selectionGroups.Select(group => $"{group.Act}:{group.SelectionMode}:{string.Join(',', group.EligibleActIds)}")),
            string.Join(';', actPlans.Select(act => $"{act.Act}:{act.ActId}:{act.EligibleEventCount}:{act.WeakEncounters.Length}:{act.RegularEncounters.Length}:{act.EliteEncounters.Length}:{act.Bosses.Length}:{act.Ancients.Length}"))
        });

        string worldAuthorityFingerprint = Fingerprint(new[]
        {
            generation.SnapshotFingerprint,
            filter.AncientOptionConditions.Fingerprint,
            string.Join(';', actPlans.SelectMany(item => item.AncientOptionPlans)
                .Select(item => item.AuthorityFingerprint))
        });
        string ancientOptionInputFingerprint = Fingerprint(new[]
        {
            generation.CatalogFingerprint,
            generation.UnlockFingerprint,
            generation.GenerationRuleFingerprint,
            generation.LobbyPlayers[0].CharacterKey.Serialized,
            ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
            filter.AncientOptionConditions.Fingerprint,
            string.Join(';', actPlans.SelectMany(item => item.AncientOptionPlans)
                .Select(item => item.AuthorityFingerprint))
        });

        return new Beta110WorldFastPlan(
            true,
            family ? false : planAlwaysReject,
            selectionGroups,
            overrideId,
            planByDense,
            actPlans.ToArray(),
            generation.SharedAncients.Select(Add).ToArray(),
            ascension,
            maxRequiredAct,
            requiresSecondBoss,
            eventPredicateCount + bossPredicateCount + ancientIdentityPredicateCount +
                ancientOptionPredicateCount + seaGlassPredicateCount,
            eventPredicateCount,
            bossPredicateCount,
            ancientIdentityPredicateCount,
            ancientOptionPredicateCount,
            seaGlassPredicateCount,
            ancientOptionExactOnlyPredicateCount,
            seaGlassExactOnlyPredicateCount,
            maxEvent,
            maxEncounter,
            DetermineRequiredFarthestStage(
                requiresSecondBoss,
                maxRequiredAct,
                eventPredicateCount,
                bossPredicateCount,
                ancientIdentityPredicateCount,
                ancientOptionPredicateCount,
                seaGlassPredicateCount),
            worldAuthorityFingerprint,
            ancientOptionInputFingerprint,
            filter.AncientOptionConditions.Fingerprint,
            fingerprint,
            string.Empty);
    }

    private static WorldFastDenseSetPredicate[] CompileAncientBranchPredicates(
        NeowSearchFilter filter,
        int act,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey)
    {
        AncientSearchBranchCondition[] branches = filter.AncientBranchConditions
            .Where(item => item.IsValid && item.Act == act)
            .ToArray();
        if (branches.Length == 0) return Array.Empty<WorldFastDenseSetPredicate>();
        ushort[] ids = branches.Select(item => item.AncientKey)
            .Distinct(ModelKeyComparer.Instance)
            .Where(denseByKey.ContainsKey)
            .Select(key => denseByKey[key])
            .ToArray();
        return new[]
        {
            new WorldFastDenseSetPredicate(ids, Array.Empty<ushort>(), Array.Empty<ushort>(), ids.Length == 0)
        };
    }

    private static string DetermineRequiredFarthestStage(
        bool requiresSecondBoss,
        int maxRequiredAct,
        int eventCount,
        int bossCount,
        int ancientIdentityCount,
        int ancientOptionCount,
        int seaGlassCount)
    {
        if (requiresSecondBoss) return "Act3SecondBoss";
        if (seaGlassCount > 0) return "SeaGlassTarget";
        if (ancientOptionCount > 0) return "AncientOptions";
        if (ancientIdentityCount > 0) return "AncientIdentity";
        if (bossCount > 0) return "BossIdentity";
        if (eventCount > 0) return "EventStaticCandidate";
        return maxRequiredAct > 0 ? "WorldContinuation" : "None";
    }

    private static string Fingerprint(IEnumerable<string> values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values))))
            .ToLowerInvariant();
}

internal static class Beta110WorldFastStage
{
    private static readonly ulong ActSelectionStreamHash =
        RolltheSpire2.Core.Seed.XxHash64.Hash("act_selection"u8, 0UL);
    private const int OpeningAncientCursorOffset = 1;

    public static Beta110WorldFastStageResult Evaluate(
        ulong rootHash,
        UpFrontRngCheckpoint checkpoint,
        Beta110WorldFastPlan plan) => EvaluateCore(rootHash, checkpoint, plan, false);

    internal static Beta110WorldFastStageResult EvaluateForFamily(ulong rootHash, UpFrontRngCheckpoint checkpoint,
        Beta110WorldFastPlan plan) => EvaluateCore(rootHash, checkpoint, plan, true);

    private static Beta110WorldFastStageResult EvaluateCore(ulong rootHash, UpFrontRngCheckpoint checkpoint,
        Beta110WorldFastPlan plan, bool family)
    {
        if (!plan.Enabled) return Beta110WorldFastStageResult.NotEvaluated;

        int selectedActCount = 0;
        int eventShuffleCalls = 0;
        int encounterDrawCalls = 0;
        int bossDrawCalls = 0;
        int ancientDrawCalls = 0;
        int staticCandidatesVisited = 0;
        int eventStageInput = 0;
        int eventStageOutput = 0;
        int bossStageInput = 0;
        int bossStageOutput = 0;
        int ancientIdentityStageInput = 0;
        int ancientIdentityStageOutput = 0;
        int ancientOptionStageInput = 0;
        int ancientOptionStageOutput = 0;
        int seaGlassStageInput = 0;
        int seaGlassStageOutput = 0;

        WorldFastStageTelemetry Telemetry(bool rejected) => new(
            true,
            rejected,
            1,
            rejected ? 0 : 1,
            selectedActCount,
            eventShuffleCalls,
            encounterDrawCalls,
            bossDrawCalls,
            ancientDrawCalls,
            staticCandidatesVisited,
            eventStageInput,
            eventStageOutput,
            bossStageInput,
            bossStageOutput,
            ancientIdentityStageInput,
            ancientIdentityStageOutput,
            ancientOptionStageInput,
            ancientOptionStageOutput,
            seaGlassStageInput,
            seaGlassStageOutput);

        Beta110WorldFastStageResult Keep(WorldFastStageDiagnosticCode code = WorldFastStageDiagnosticCode.None) =>
            family && code != WorldFastStageDiagnosticCode.None ? throw new InvalidOperationException("W.Numeric:" + code) :
            new(Beta110FastDecision.CandidateKeep, Telemetry(false), code);
        Beta110WorldFastStageResult RejectCurrent(WorldFastStageDiagnosticCode code) =>
            new(Beta110FastDecision.CandidateReject, Telemetry(true), code);

        if (plan.AlwaysRejectCandidate)
            return RejectCurrent(WorldFastStageDiagnosticCode.WorldContinuationUnavailable);
        if (checkpoint.IsZero)
            return Keep(WorldFastStageDiagnosticCode.SharedPrefixUnavailable);

        Span<ushort> selectedActs = stackalloc ushort[plan.ActSelectionGroups.Length];
        var actSelection = new Beta110FastRng(unchecked(rootHash + ActSelectionStreamHash));
        for (int index = 0; index < plan.ActSelectionGroups.Length; index++)
        {
            WorldFastActSelectionGroup group = plan.ActSelectionGroups[index];
            selectedActs[index] = group.SelectionMode == 1
                ? group.EligibleActIds[0]
                : group.EligibleActIds[actSelection.NextInt(group.EligibleActIds.Length)];
        }
        if (selectedActs.Length > 0 && plan.Act1OverrideId != Beta110WorldFastPlan.InvalidDenseId)
            selectedActs[0] = plan.Act1OverrideId;
        selectedActCount = selectedActs.Length;

        var upFront = new Beta110FastRng(checkpoint);
        Span<ushort> sharedRemaining = plan.SharedAncients.Length == 0
            ? Span<ushort>.Empty
            : stackalloc ushort[plan.SharedAncients.Length];
        plan.SharedAncients.AsSpan().CopyTo(sharedRemaining);
        upFront.UnstableShuffle(sharedRemaining);

        Span<ushort> assignedShared = plan.SharedAncients.Length == 0
            ? Span<ushort>.Empty
            : stackalloc ushort[plan.SharedAncients.Length];
        Span<int> assignedOffsets = stackalloc int[selectedActs.Length];
        Span<int> assignedCounts = stackalloc int[selectedActs.Length];
        if (family) { assignedOffsets.Clear(); assignedCounts.Clear(); }
        int remainingShared = sharedRemaining.Length;
        int assignedWrite = 0;
        for (int selectedIndex = 1; selectedIndex < selectedActs.Length; selectedIndex++)
        {
            int count = upFront.NextInt(remainingShared + 1);
            assignedOffsets[selectedIndex] = assignedWrite;
            assignedCounts[selectedIndex] = count;
            if (count <= 0) continue;
            sharedRemaining[..count].CopyTo(assignedShared.Slice(assignedWrite, count));
            assignedWrite += count;
            sharedRemaining.Slice(count, remainingShared - count).CopyTo(sharedRemaining);
            remainingShared -= count;
        }

        Span<ushort> firstBossBySelectedAct = stackalloc ushort[selectedActs.Length];
        firstBossBySelectedAct.Fill(Beta110WorldFastPlan.InvalidDenseId);
        Span<WorldFastEventCandidate> eventScratch = plan.MaxEventCandidateCount == 0
            ? Span<WorldFastEventCandidate>.Empty
            : stackalloc WorldFastEventCandidate[plan.MaxEventCandidateCount];
        Span<ushort> singleIdentity = stackalloc ushort[1];

        for (int selectedIndex = 0; selectedIndex < selectedActs.Length; selectedIndex++)
        {
            ushort selectedActId = selectedActs[selectedIndex];
            if (selectedActId >= plan.ActPlanIndexByDenseId.Length)
                return Keep(WorldFastStageDiagnosticCode.ActSelectionUnavailable);
            short actPlanIndex = plan.ActPlanIndexByDenseId[selectedActId];
            if (actPlanIndex < 0 || actPlanIndex >= plan.Acts.Length)
                return Keep(WorldFastStageDiagnosticCode.ActSelectionUnavailable);
            WorldFastActPlan act = plan.Acts[actPlanIndex];
            if (family && !act.FamilyVariantAllowed) return RejectCurrent(WorldFastStageDiagnosticCode.BossPredicateRejected);
            if (!plan.RequiresSecondBoss && act.Act > plan.MaxRequiredAct) break;

            if (act.HasEventPredicate)
            {
                eventStageInput++;
                Span<WorldFastEventCandidate> events = eventScratch[..act.EventCandidates.Length];
                act.EventCandidates.AsSpan().CopyTo(events);
                upFront.UnstableShuffle(events);
                eventShuffleCalls += Math.Max(0, events.Length - 1);
                if (family && act.Ancients.Length + assignedCounts[selectedIndex] == 0)
                    throw new InvalidOperationException("W.OpeningAncientCursorAuthorityMissing");
                if (!EvaluateEventPredicates(events, act.EventPredicates, ref staticCandidatesVisited, family))
                    return RejectCurrent(WorldFastStageDiagnosticCode.EventPredicateRejected);
                eventStageOutput++;
            }
            else
            {
                upFront.ConsumeUnstableShuffle(act.EligibleEventCount);
                eventShuffleCalls += Math.Max(0, act.EligibleEventCount - 1);
            }

            bool needBeyondEvents = act.Act < plan.MaxRequiredAct || act.HasBossPredicate ||
                                    act.HasAncientPredicate || plan.RequiresSecondBoss;
            if (!needBeyondEvents) continue;

            WorldFastEncounterEntry? previousNormal = null;
            ConsumeEncounterQueue(act.WeakEncounters, act.WeakEncounterSlots, act.EncounterConflictSourceOrdinals,
                ref upFront, ref previousNormal, ref encounterDrawCalls);
            ConsumeEncounterQueue(act.RegularEncounters, act.RegularEncounterSlots, act.EncounterConflictSourceOrdinals,
                ref upFront, ref previousNormal, ref encounterDrawCalls);
            WorldFastEncounterEntry? previousElite = null;
            ConsumeEncounterQueue(act.EliteEncounters, act.EliteEncounterSlots, act.EncounterConflictSourceOrdinals,
                ref upFront, ref previousElite, ref encounterDrawCalls);

            if (act.Bosses.Length == 0)
                return Keep(WorldFastStageDiagnosticCode.WorldContinuationUnavailable);
            ushort firstBoss = act.Bosses[upFront.NextInt(act.Bosses.Length)];
            if (family && act.FamilyFirstBossOverride != ushort.MaxValue) firstBoss = act.FamilyFirstBossOverride;
            bossDrawCalls++;
            firstBossBySelectedAct[selectedIndex] = firstBoss;
            if (family && act.FamilyBossBranches.Length > 0 &&
                !(act.FamilyBossBranchesConjunctive ? act.FamilyBossBranches.All(b => Matches(new[] { firstBoss }, b.First)) :
                    act.FamilyBossBranches.Any(b => Matches(new[] { firstBoss }, b.First))))
                return RejectCurrent(WorldFastStageDiagnosticCode.BossPredicateRejected);
            if (act.HasBossPredicate)
            {
                bossStageInput++;
                singleIdentity[0] = firstBoss;
                if (!MatchesAll(singleIdentity, act.BossOrdinalOnePredicates) ||
                    (!(plan.Ascension >= 10 && act.Act == 3 && act.BossActPredicates.Length > 0) &&
                     !MatchesAll(singleIdentity, act.BossActPredicates)))
                {
                    return RejectCurrent(WorldFastStageDiagnosticCode.BossPredicateRejected);
                }
                bossStageOutput++;
            }

            bool needAncient = act.Act < plan.MaxRequiredAct || act.HasAncientPredicate || plan.RequiresSecondBoss;
            if (!needAncient) continue;
            int ancientPoolCount = act.Ancients.Length + assignedCounts[selectedIndex];
            if (ancientPoolCount <= 0)
                return Keep(WorldFastStageDiagnosticCode.WorldContinuationUnavailable);
            int ancientIndex = upFront.NextInt(ancientPoolCount);
            ancientDrawCalls++;
            ushort ancient = ancientIndex < act.Ancients.Length
                ? act.Ancients[ancientIndex]
                : assignedShared[assignedOffsets[selectedIndex] + ancientIndex - act.Ancients.Length];

            if (act.HasAncientIdentityPredicate)
            {
                ancientIdentityStageInput++;
                singleIdentity[0] = ancient;
                if (!MatchesAll(singleIdentity, act.AncientIdentityPredicates) ||
                    !MatchesAll(singleIdentity, act.AncientBranchIdentityPredicates))
                {
                    return RejectCurrent(WorldFastStageDiagnosticCode.AncientPredicateRejected);
                }
                ancientIdentityStageOutput++;
            }

            if (family) continue; // identity only; A owns options and SeaGlass
            WorldFastAncientOptionEvaluationResult optionResult =
                Beta110AncientOptionFastStage.Evaluate(rootHash, ancient, act);
            if (optionResult.OptionStageEvaluated)
            {
                ancientOptionStageInput++;
                if (optionResult.OptionStagePassed) ancientOptionStageOutput++;
            }
            if (optionResult.SeaGlassStageEvaluated)
            {
                seaGlassStageInput++;
                if (optionResult.SeaGlassStagePassed) seaGlassStageOutput++;
            }
            if (optionResult.Evaluation == WorldFastAncientOptionEvaluation.Reject)
            {
                return RejectCurrent(optionResult.SeaGlassStageEvaluated && !optionResult.SeaGlassStagePassed
                    ? WorldFastStageDiagnosticCode.SeaGlassPredicateRejected
                    : WorldFastStageDiagnosticCode.AncientOptionPredicateRejected);
            }
        }

        if (plan.RequiresSecondBoss && selectedActs.Length > 0)
        {
            int finalIndex = selectedActs.Length - 1;
            ushort finalActId = selectedActs[finalIndex];
            if (finalActId >= plan.ActPlanIndexByDenseId.Length)
                return Keep(WorldFastStageDiagnosticCode.WorldContinuationUnavailable);
            short finalPlanIndex = plan.ActPlanIndexByDenseId[finalActId];
            if (finalPlanIndex < 0)
                return Keep(WorldFastStageDiagnosticCode.WorldContinuationUnavailable);
            WorldFastActPlan finalAct = plan.Acts[finalPlanIndex];
            ushort firstBoss = firstBossBySelectedAct[finalIndex];
            Span<ushort> secondPool = stackalloc ushort[finalAct.Bosses.Length];
            int secondCount = 0;
            foreach (ushort boss in finalAct.Bosses)
                if (boss != firstBoss) secondPool[secondCount++] = boss;
            if (secondCount <= 0)
                return Keep(WorldFastStageDiagnosticCode.WorldContinuationUnavailable);
            ushort secondBoss = secondPool[upFront.NextInt(secondCount)];
            if (family && finalAct.FamilyBossBranches.Length > 0 &&
                !(finalAct.FamilyBossBranchesConjunctive ? finalAct.FamilyBossBranches.All(b => Matches(new[] { firstBoss }, b.First) && Matches(new[] { secondBoss }, b.Second)) :
                    finalAct.FamilyBossBranches.Any(b => Matches(new[] { firstBoss }, b.First) && Matches(new[] { secondBoss }, b.Second))))
                return RejectCurrent(WorldFastStageDiagnosticCode.BossPredicateRejected);
            bossDrawCalls++;
            bossStageInput++;
            if (!MatchesAll(stackalloc ushort[] { secondBoss }, finalAct.BossOrdinalTwoPredicates) ||
                !MatchesAll(stackalloc ushort[] { firstBoss, secondBoss }, finalAct.BossActPredicates))
            {
                return RejectCurrent(WorldFastStageDiagnosticCode.BossPredicateRejected);
            }
            bossStageOutput++;
        }

        return Keep();
    }

    private static bool EvaluateEventPredicates(
        Span<WorldFastEventCandidate> shuffled,
        ReadOnlySpan<WorldFastEventPredicate> predicates,
        ref int visited, bool family = false)
    {
        int effectiveCount = 0;
        int horizon = 0;
        foreach (var predicate in predicates) horizon = Math.Max(horizon, checked((int)predicate.RangeValue));
        int capacity = family ? Math.Min(horizon, shuffled.Length) : shuffled.Length;
        Span<ushort> effectiveIds = stackalloc ushort[capacity];
        Span<byte> effectiveSources = stackalloc byte[capacity];
        for (int rawIndex = OpeningAncientCursorOffset; rawIndex < shuffled.Length; rawIndex++)
        {
            if (family && effectiveCount >= horizon) break;
            WorldFastEventCandidate candidate = shuffled[rawIndex];
            visited++;
            if (!candidate.StaticEligible) continue;
            bool duplicate = false;
            for (int index = 0; !family && index < effectiveCount; index++)
            {
                if (effectiveIds[index] != candidate.EventId) continue;
                duplicate = true;
                break;
            }
            if (duplicate) continue;
            effectiveIds[effectiveCount] = candidate.EventId;
            effectiveSources[effectiveCount] = candidate.Source;
            effectiveCount++;
        }

        Span<ushort> range = effectiveCount == 0 ? Span<ushort>.Empty : stackalloc ushort[effectiveCount];
        foreach (WorldFastEventPredicate predicate in predicates)
        {
            int rangeCount = 0;
            int ordinalLimit = checked((int)predicate.RangeValue);
            for (int index = 0; index < effectiveCount; index++)
            {
                int ordinal = index + 1;
                bool ordinalIncluded = predicate.RangeMode == 0 ? ordinal <= ordinalLimit : ordinal == ordinalLimit;
                if (!ordinalIncluded) continue;
                if (predicate.SourceFilter != byte.MaxValue && effectiveSources[index] != predicate.SourceFilter) continue;
                range[rangeCount++] = effectiveIds[index];
            }
            if (predicate.RangeMode == 1 && rangeCount == 0) return false;
            if (!Matches(range[..rangeCount], predicate.Keys)) return false;
        }
        return true;
    }

    private static void ConsumeEncounterQueue(
        ReadOnlySpan<WorldFastEncounterEntry> source,
        int slots,
        ReadOnlySpan<int> conflicts,
        ref Beta110FastRng rng,
        ref WorldFastEncounterEntry? previous,
        ref int drawCalls)
    {
        if (slots <= 0) return;
        Span<int> bag = source.Length == 0 ? Span<int>.Empty : stackalloc int[source.Length];
        int count = 0;
        for (int slot = 0; slot < slots; slot++)
        {
            if (count == 0 && source.Length > 0)
            {
                for (int index = 0; index < source.Length; index++) bag[index] = index;
                count = source.Length;
            }

            int selectedBagIndex = -1;
            bool hasEligible = false;
            for (int index = 0; index < count; index++)
            {
                if (!previous.HasValue || DoesNotRepeat(source[bag[index]], previous.Value, conflicts))
                {
                    hasEligible = true;
                    break;
                }
            }
            if (hasEligible)
            {
                while (true)
                {
                    int candidateIndex = (int)(rng.NextDouble() * count);
                    drawCalls++;
                    WorldFastEncounterEntry candidate = source[bag[candidateIndex]];
                    if (!previous.HasValue || DoesNotRepeat(candidate, previous.Value, conflicts))
                    {
                        selectedBagIndex = candidateIndex;
                        break;
                    }
                }
            }
            else
            {
                // Source GrabBag performs the no-predicate fallback. It consumes one
                // NextDouble even when the bag is empty.
                double sample = rng.NextDouble();
                drawCalls++;
                if (count > 0) selectedBagIndex = (int)(sample * count);
            }

            if (selectedBagIndex < 0) continue;
            WorldFastEncounterEntry selected = source[bag[selectedBagIndex]];
            for (int index = selectedBagIndex; index < count - 1; index++) bag[index] = bag[index + 1];
            count--;
            previous = selected;
        }
    }

    private static bool DoesNotRepeat(
        WorldFastEncounterEntry candidate,
        WorldFastEncounterEntry previous,
        ReadOnlySpan<int> conflicts)
    {
        if (candidate.ReferenceIdentityId >= 0 &&
            previous.ReferenceIdentityId >= 0 &&
            candidate.ReferenceIdentityId == previous.ReferenceIdentityId)
            return false;
        for (int index = 0; index < candidate.ConflictCount; index++)
            if (conflicts[candidate.ConflictOffset + index] == previous.SourceOrdinal) return false;
        return true;
    }

    private static bool MatchesAll(ReadOnlySpan<ushort> values, ReadOnlySpan<WorldFastDenseSetPredicate> predicates)
    {
        foreach (WorldFastDenseSetPredicate predicate in predicates)
            if (!Matches(values, predicate)) return false;
        return true;
    }

    private static bool Matches(ReadOnlySpan<ushort> values, WorldFastDenseSetPredicate predicate)
    {
        if (predicate.AlwaysReject) return false;
        if (predicate.Any.Length > 0)
        {
            bool found = false;
            foreach (ushort target in predicate.Any)
            {
                if (!values.Contains(target)) continue;
                found = true;
                break;
            }
            if (!found) return false;
        }
        foreach (ushort target in predicate.All)
            if (!values.Contains(target)) return false;
        foreach (ushort target in predicate.Ban)
            if (values.Contains(target)) return false;
        return true;
    }

}
