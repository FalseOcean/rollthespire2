using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record CombatRewardPreparation(CombatRewardReplay Replay, CombatRewardGpuPlan? GpuPlan, FilterBodyDescription Body);

// C prepares its authored/neutral opening and continuous reward observation.
// Numerical replay is shared input; selected CPU/GPU lifetimes are independent.
internal static partial class CombatRewardFamily
{
    internal const string Id = "C.CombatReward";
    private static readonly IReadOnlySet<string> EmptyCoverage = new HashSet<string>(StringComparer.Ordinal);
    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        bool accepted = TryPrepare(model, preferences, out var prepared, preferGpu, numericalVariant);
        body = prepared?.Body;
        return accepted;
    }
    internal static bool TryPrepare(SearchModel model, FilterExecutionPreferences preferences,
        out CombatRewardPreparation? prepared, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        prepared = null;
        if (model.Authority.PlayersCount != 1 || !model.Evaluation.NormalCombatRewardConditions.Any(c => !c.IsEmpty)) return false;
        if (!TryCreateReplay(model, preferences, 0, out var replay)) return false;
        prepared = Prepare(model, preferences, replay!, preferGpu, numericalVariant);
        return true;
    }
    internal static bool TryCreateParty(SearchModel model, FilterExecutionPreferences preferences,
        int? precedingNicheDraws, int? precedingPotionDraws, out FilterBodyDescription? body, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        bool accepted = TryPrepareParty(model, preferences, precedingNicheDraws, precedingPotionDraws,
            out var prepared, preferGpu, numericalVariant);
        body = prepared?.Body;
        return accepted;
    }
    internal static bool TryPrepareParty(SearchModel model, FilterExecutionPreferences preferences,
        int? precedingNicheDraws, int? precedingPotionDraws, out CombatRewardPreparation? prepared, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        prepared = null;
        if (model.ProfileId != RuntimeProfileId.Beta111 || !model.Authority.CanUseCurrentModel ||
            model.Authority.PlayersCount is < 2 or > 4 || model.Authority.PlayerSlotIndex < 0 ||
            model.Authority.PlayerSlotIndex >= model.Authority.PlayersCount ||
            !model.Evaluation.NormalCombatRewardConditions.Any(c => !c.IsEmpty)) return false;
        if (model.CompiledSearch.NormalizedQuery.OpeningRoute is { } route &&
            (!route.IsValid || !Beta110FastRelicCatalog.TryGetId(route.RouteRelicKey, out _))) return false;
        // Combat potions use personal Rewards. The shared opening-potion arrival
        // is deliberately not substituted for this independent stream.
        if (!TryCreateReplay(model, preferences, precedingNicheDraws, out var replay)) return false;
        prepared = Prepare(model, preferences, replay!, preferGpu, numericalVariant);
        return true;
    }
    private static bool TryCreateReplay(SearchModel model, FilterExecutionPreferences preferences,
        int? precedingNicheDraws, out CombatRewardReplay? replay)
    {
        replay = null;
        if (!CombatRewardReplay.TryCompile(model, out var catalog, out var plan, out string issue))
        {
            ReportUnavailable(issue, preferences.SkipExactValidation);
            return false;
        }
        replay = new(model, precedingNicheDraws, catalog, plan);
        if (replay.ConservativeOpeningReason.StartsWith("OpeningConsumptionAuthorityUnavailable:", StringComparison.Ordinal) ||
            replay.ConservativeOpeningReason == "BonesShufflePoolUnavailable")
        {
            ReportUnavailable(replay.ConservativeOpeningReason, preferences.SkipExactValidation);
            replay = null;
            return false;
        }
        return true;
    }
    private static void ReportUnavailable(string cause, bool skipExact)
    {
        RuntimeLog.TryBackgroundInfo($"combatRewardFamilyUnavailable=true;reason={cause};remainingQueryOwner=ProductionExact;replacementCompleteness=NotClaimed");
        if (RuntimeLog.SessionId.Length > 0)
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("CombatRewardFilterUnavailable",
                nameof(CombatRewardFamily), "Filter.Construction", "issues.filter_incomplete",
                $"cause={cause};predicatesRetained=true;finalValidationSkipped={skipExact};matchClaimed=false");
    }
    private static CombatRewardPreparation Prepare(SearchModel model, FilterExecutionPreferences preferences,
        CombatRewardReplay replay, bool preferGpu, GpuNumericalVariant numericalVariant)
    {
        CombatRewardGpuPlan? gpu = null;
        string failure = "", gpuIssue = "CpuReferenceSelected";
        if (preferGpu)
        {
            try { gpu = CombatRewardGpuPlan.TryCreate(model, replay, out gpuIssue) is { } compiled
                ? compiled with { NumericalVariant = numericalVariant } : null; }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { gpuIssue = "GpuPlanConstructionFailed"; failure = error.ToString(); }
        }
        var projections = new CombatRewardFamilyModels(model, replay, gpu is null ? CpuFilterExecution.Capacity : CombatRewardGpuExecutor.Capacity);
        var issues = new List<FilterPreparationIssue>();
        if (failure.Length > 0) issues.Add(new("GpuPreparation", gpuIssue, failure));
        if (replay.ConservativelyKeeps) issues.Add(new("Filtering", replay.ConservativeOpeningReason));
        string observedRemainder = replay.UsesObservedOpening && replay.ObservedOpening!.HasUnresolvedEffects
            ? "UnresolvedObservedOpeningEffectsRetainedForExact" : "";
        if (observedRemainder.Length > 0) issues.Add(new("Filtering", observedRemainder));
        string neutralRemainder = replay.NeutralWitness?.Remainder ?? "";
        if (neutralRemainder.Length > 0) issues.Add(new("Filtering", neutralRemainder));
        var preparation = new PreparedFilter(Id) {
            AnalyticalCost = projections.Analytical, Survival = projections.Survival,
            ExpectedFilteringCost = projections.Expected(EmptyCoverage), HasConditionalProjections = true,
            SurvivalResolver = projections.ResolveSurvival, ExpectedCostResolver = projections.Expected,
            Obligations = [new(Id, model.Authority.PlayerSlotIndex)], Issues = issues.ToArray(),
            Remainders = replay.ConservativelyKeeps ? [Id + ":" + replay.ConservativeOpeningReason] :
                new[] { observedRemainder, neutralRemainder }.Where(cause => cause.Length > 0)
                    .Select(cause => Id + ":" + cause).ToArray()
        };
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var pricing = CombatRewardPricingInput.From(model, replay);
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        string identity = model.SnapshotFingerprint;
        var ordinary = PrepareCpu(preparation, replay, gpu, pricing, profile, identity, workers, false);
        var cpu = replay.CanUsePotionPrefix ? PrepareCpu(preparation, replay, gpu, pricing, profile, identity, workers, true) : ordinary;
        if (gpu is null) return new(replay, null, cpu);
        var gpuPlan = gpu;
        const string hotRemainder = "ObservedOpeningOutsideHotStructureRetainedForExact";
        var gpuPreparation = gpu.UsesObservedOpeningHotLoop ? preparation with {
            Issues = [..preparation.Issues, new("Filtering", hotRemainder)],
            Remainders = [..preparation.Remainders, Id + ":" + hotRemainder]
        } : preparation;
        var body = new FilterBodyDescription(gpuPreparation, gpu.Revision(false), profile.ProfileId) {
            UsesGpu = true, NumericRequirements = gpu.NumericRequirements,
            InputIdentity = identity, PlayersCount = model.Authority.PlayersCount, CpuWorkers = preferences.WorkerCount,
            WindowCapacity = CombatRewardGpuExecutor.Capacity,
            CpuAlternatives = replay.CanUsePotionPrefix ? [cpu, ordinary] : [ordinary],
            PerformanceProjection = compact => new(Id, gpuPlan.Revision(compact), "C.Neutral.20260907.v1", 1,
                "NoAcceptedWithinPathCurve;CanonicalAbi1Ready", true),
            Cost = new() {
                PartyChild = new(AllowsHistoricalEnvelope: gpuPlan.NumericalVariant != GpuNumericalVariant.IntegerRne && !replay.Plan.NeedsCompleteMatchingReference && !replay.UsesQueryLiteralPartyOpening && !replay.UsesObservedOpening && !replay.UsesNeutralWitness, NumericalRevision: gpuPlan.NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne.20261025.v1" : "", CapacityRevision: gpuPlan.BagCapacityRevision, CompleteCombatMatching: replay.Plan.NeedsCompleteMatchingReference, QueryLiteralCombatOpening: replay.UsesQueryLiteralPartyOpening),
                MinimumComparableInput = _ => 1,
                CanonicalScope = pricing.Authority.PlayersCount == 1,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeCombat(replay, gpuPlan),
                Quote = (selected, geometry, _) => FamilyGpuReadback.Reference(selected,
                    FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                    CombatRewardPhysicalPricing.Quote(pricing, replay, gpuPlan, geometry) ?? CombatRewardWorkEstimate.Gpu(replay, gpuPlan, geometry))
            },
            CreateExecution = (selected, _) => new GpuFilterExecution<CombatRewardGpuExecutor>(selected,
                device => new(device, gpuPlan), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalMs, metrics.DispatchSyncMs, metrics.ReadbackMs, metrics.ReadbackBytes);
                }, resource => resource.Dispose(), gpuPlan.UsesObservedOpeningHotLoop ? replay.MatchesObservedHotReference : replay.MatchesReference,
                profile, resource => (resource.Device, resource.SetupMs), 4096),
            BindPrivate = gpuPlan.NumericalVariant != GpuNumericalVariant.NativeFloat64 ? null : (device, input, output) => {
                var resource = new CombatRewardGpuExecutor(device, gpuPlan, privateInput: input, privateOutput: output);
                return new(resource, resource.Device, resource.SetupMs, input, output, (batch, count, token) => {
                    FamilyCandidateSet? filtered = null; CombatRewardGpuMetrics metrics;
                    if (output is not null) {
                        if (input is null) resource.ExecutePrivate(FamilyCandidateSet.Dense(batch), token, out metrics);
                        else resource.ExecutePrivateStage(batch, count, token, out metrics);
                    }
                    else filtered = input is null ? resource.Execute(FamilyCandidateSet.Dense(batch), token, out metrics) :
                        resource.ExecutePrivateInput(batch, count, token, out metrics);
                    return new(metrics.Output, filtered, metrics.DispatchSyncMs, metrics.ReadbackMs,
                        metrics.ReadbackBytes, metrics.CanonicalMs, metrics.Dispatches);
                });
            }
        };
        return new(replay, gpuPlan, body);
    }
}
