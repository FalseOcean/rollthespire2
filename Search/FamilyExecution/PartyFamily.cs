using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// A physical specialization of one existing Family. Each slot retains its
// immutable request; only ordered batch-local ordinals leave this invocation.
internal sealed class PartyFamily : IFamilyInvocation
{
    bool IFamilyInvocation.ExecutionFaulted => _stages.Any(stage => stage.Family.ExecutionFaulted);
    private readonly ExactSearchExecutionRequest _request;
    private readonly GpuCostSnapshot? _costs;
    private readonly (ExactSearchExecutionRequest Request, IFamilyInvocation Family)[] _stages;
    private FamilyPrivateGpuChain? _chain;
    private bool _compact;
    private int _batches;
    private long _inputs, _outputs, _liveInputs;
    private double _ms, _liveMs;
    private static readonly IReadOnlySet<string> NoPassedPersonalCoverage = new HashSet<string>(StringComparer.Ordinal);
    private bool _smallConfiguredShopCpu;
    private (ExactSearchExecutionRequest Request, IFamilyInvocation Family)[]? _executionStages;
    private bool UsesPrivate => _stages.Length > 1 && _stages.All(s => s.Family.CanBindPrivateSerial);

    internal PartyFamily(ExactSearchExecutionRequest request,
        (ExactSearchExecutionRequest Request, IFamilyInvocation Family)[] stages)
    {
        _request = request; _stages = stages;
        if (stages.Length == 0 || stages.Any(s => s.Family.FamilyId != stages[0].Family.FamilyId))
            throw new ArgumentException("Party.MixedFamilyInvocation");
        _costs = FamilyAuxiliaryEvidence.Read<GpuCostSnapshot?>(FamilyId, "PersonalUnitCostSnapshot",
            GpuCostCalibration.Capture, null, request.SnapshotFingerprint);
        FamilyAuxiliaryEvidence.Read(FamilyId, "ReadyDiagnostic", () =>
        {
            RuntimeLog.TryBackgroundInfo($"partyCostShape=true;family={FamilyId};stages={stages.Length};" +
                $"private={UsesPrivate};workPerInput={string.Join(',', stages.Select(s => s.Family.AnalyticalCost.WorkUnitsPerInput.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)))};" +
                $"capacity={string.Join(',', stages.Select(s => s.Family.AnalyticalCost.InputCapacityPerInvocation))};" +
                $"worldBagEntries={string.Join(',', stages.Select(s => s.Family is WorldFamily w ? w.Replay.BucketLengths.Sum(x => (long)x) : 0))};" +
                $"revisions={string.Join(',', stages.Select(s => s.Family.ConditionPerformance.PhysicalImplementationRevision))}");
            return true;
        }, false, request.SnapshotFingerprint);
    }
    GpuNumericRequirements IFamilyInvocation.GpuNumericRequirements => _stages.Aggregate(GpuNumericRequirements.None, (requirements, stage) => requirements | stage.Family.GpuNumericRequirements);
    public string FamilyId => _stages[0].Family.FamilyId;
    internal IEnumerable<(int Slot, IFamilyInvocation Family)> PersonalStages =>
        _stages.Select(s => (s.Request.Authority.PlayerSlotIndex, s.Family));
    private PartyFamily? ConfiguredCpuAlternative()
    {
        if (_stages.Any(s => !s.Family.ConditionPerformance.UsesGpu)) return null;
        var children = new List<(ExactSearchExecutionRequest Request, IFamilyInvocation Family)>();
        foreach (var stage in _stages)
        {
            var cpu = FamilyCpuExecution.ConfiguredRealizations(stage.Family).OfType<FamilyCpuExecution>()
                .FirstOrDefault(c => c.Workers == FamilyCpuExecution.ConfiguredWorkers(stage.Request));
            if (cpu is null) return null;
            children.Add((stage.Request, cpu));
        }
        // The measured complete wrapper has one requested worker configuration.
        if (children.Select(s => ((FamilyCpuExecution)s.Family).Workers).Distinct().Count() != 1) return null;
        return new PartyFamily(_request, children.ToArray())
            { _smallConfiguredShopCpu = FamilyId == "S.MerchantShopColorless" };
    }
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations =>
        ConfiguredCpuAlternative() is { } alternative ? [alternative] : [];
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, FamilyCpuExecution.Capacity, [], "PartyFamilyAnalyticalWorkUnavailable;PhysicalQuoteBounded");
    public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry) =>
        QuotePhysicalWork(geometry, NoPassedPersonalCoverage);
    internal FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry, IReadOnlySet<string> passedCoverage)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput ||
            geometry.MeanInputPopulation <= 0) return null;
        int window = ConditionPerformance.UsesGpu ? FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize : FamilyCpuExecution.Capacity;
        var price = QuoteUnit(geometry.MeanInputPopulation, geometry.CompactInput,
            passedCoverage, geometry.ComputationOnly, geometry.CostSnapshot);
        var quote = price is null ? null : new FamilyPhysicalQuote(FamilyId,
            geometry.ComputationOnly ? "PartyFamilyComputationEnvelope" : "PartyFamilyReferenceEnvelope",
            price.Milliseconds * 1e6 / geometry.MeanInputPopulation, window, price.SetupMilliseconds,
            price.Evidence + (geometry.ComputationOnly ? ";FamilyComputationBoundary" : ";CompleteCanonicalAbi1;PlannerRankingReference"), OutputElementBytes: 8)
        { LocalCostSource = geometry.ComputationOnly ? "ComponentComposition" : "Condition",
            OutputAlreadyOrdered = true, PublicTransportClass = geometry.ComputationOnly
            ? (ConditionPerformance.UsesGpu ? "DeviceFamilyWork" : "CpuOrderedAbi1") : "CompleteCanonicalAbi1" };
        return geometry.ComputationOnly ? quote : FamilyGpuReadback.Reference(this, quote);
    }
    public FamilySurvivalProjection Survival => ResolveSurvival(new HashSet<string>(StringComparer.Ordinal));
    public bool HasConditionalProjections => _stages.Any(s => s.Family.HasConditionalProjections);
    public FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passedCoverage)
    {
        if (FamilyId == "W.World" && _stages.Length == 1)
            return _stages[0].Family.ResolveSurvival(passedCoverage);
        if (FamilyId is "W.World" or "E.EventResult")
            return PartyStageSurvival.Project(_request, FamilyId);
        double probability = 1;
        foreach (var (_, family) in _stages)
        {
            FamilySurvivalProjection stage = family.ResolveSurvival(passedCoverage);
            if (stage.SurvivalProbability is not double value)
            {
                return FamilySurvivalProjection.Unresolved(FamilyId,
                    $"PartyStageProbabilityUnavailable:{stage.Evidence};KnownPlayerProduct={probability:G17};NoUnresolvedPlayerSubstitution");
            }
            probability *= value;
        }
        return FamilySurvivalProjection.Resolved(FamilyId, probability,
            "PersonalStageSieveProductApproximation;SharedFactsNotDuplicated;" +
            "CrossPlayerPersonalConditionsAssumedIndependent");
    }
    public FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
    private (ExactSearchExecutionRequest Request, IFamilyInvocation Family)[] OrderPersonalStages(
        bool firstCompact, IReadOnlySet<string> passedCoverage)
    {
        var remaining = _stages.ToList();
        var ordered = new List<(ExactSearchExecutionRequest Request, IFamilyInvocation Family)>(_stages.Length);
        while (remaining.Count > 0)
        {
            bool compact = firstCompact || ordered.Count > 0;
            double? Hardware(IFamilyInvocation family) => FamilyAuxiliaryEvidence.Read<double?>(FamilyId,
                "PersonalUnitHardwareCost", () =>
                {
                    return FamilyPerformanceProjectionMath.PerInputMilliseconds(
                        GpuCostSnapshot.ComputationReference(_costs, family, family.ResolveConditionPerformance(compact), compact,
                            passedCoverage, referencePopulation: family is FamilyCpuExecution cpu ? cpu.WindowCapacity : null));
                }, null, _request.SnapshotFingerprint);
            double? Probability(IFamilyInvocation family) => FamilyAuxiliaryEvidence.Read<double?>(FamilyId,
                "PersonalUnitSurvival", () => family.ResolveSurvival(passedCoverage).SurvivalProbability,
                null, _request.SnapshotFingerprint);
            double? Analytical(IFamilyInvocation family) => FamilyAuxiliaryEvidence.Read<double?>(FamilyId,
                "PersonalUnitWork", () =>
                {
                    var work = family.ResolveExpectedFilteringCost(passedCoverage);
                    return work?.ExpectedFilteringWorkUnitsPerInput + (compact ? work?.CompactInputWorkUnitsPerInput : 0);
                }, null, _request.SnapshotFingerprint);
            var selected = GreedyUnitOrder.Select(remaining, stage => Hardware(stage.Family),
                stage => Probability(stage.Family), stage => Analytical(stage.Family));
            ordered.Add(selected);
            remaining.Remove(selected);
            // Each child belongs to a different immutable player request. Passing
            // this Family for one slot never conditions another slot's probability.
        }
        return ordered.ToArray();
    }

    internal FamilyUnitCost? QuoteUnit(double inputs, bool compact, IReadOnlySet<string> passedCoverage,
        bool computationOnly = false, GpuCostSnapshot? costs = null)
    {
        // The retained wrapper envelope predates these integer numerical bodies.
        // One personal stage (and the shared W stage) still executes publicly,
        // so the multi-stage private-chain restriction cannot enforce this scope.
        // Keep that execution legal without promoting NativeFloat64 timings.
        bool legacyBody = !UsesUnmeasuredIntegerGpuBody() && !NeedsCompleteCombatMatchingReference() && !NeedsQueryLiteralCombatOpeningReference();
        // Runtime replays independent immutable player requests; the local order
        // uses the same unconditional player facts in pricing and execution.
        // Upstream whole-table coverage may refine expected work below without
        // becoming a cross-player same-Family pass or hidden execution context.
        IFamilyInvocation[] children = OrderPersonalStages(compact, NoPassedPersonalCoverage).Select(s => s.Family).ToArray();
        bool gpu = children.All(child => child.ConditionPerformance.UsesGpu);
        bool cpuOnly = children.All(child => !child.ConditionPerformance.UsesGpu);
        if (!computationOnly && legacyBody && (gpu || cpuOnly))
        {
            double weightedWork = 0, reach = 1;
            foreach (IFamilyInvocation child in children)
            {
                double units = child switch
                {
                    AncientOptionFamily ancient => Math.Max(1,
                        ancient.Plan.OptionPlans.Sum(p => 1 + Math.Max(0, p.MaxScratchCount - 3) * .1)),
                    EventResultFamily events => Math.Max(1, events.Plan.Conditions.Sum(c =>
                        c.Kind is EventResultConditionKind.MorphicGroveGroupInitialBasicsContains ? 5 :
                        c.Kind > EventResultConditionKind.MorphicGroveGroupInitialBasicsContains ? 3 : 1)),
                    WorldFamily world => WorldWork(world.Replay.Plan.MaxRequiredAct,
                        world.Replay.Plan.Acts.Sum(a => a.EventPredicates.Length)),
                    FamilyCpuExecution when FamilyId == "W.World" => WorldWork(
                        _request.CompiledSearch.NormalizedQuery.EventSequenceConstraints.Select(c => c.Act)
                            .Concat(_request.CompiledSearch.NormalizedQuery.VariantBossBranches.Select(c => c.Act))
                            .Concat(_request.CompiledSearch.NormalizedQuery.LegacyWorld.BossFilters.Select(c => c.Act))
                            .Concat(_request.CompiledSearch.NormalizedQuery.LegacyWorld.AncientIdentityFilters.Select(c => c.Act))
                            .DefaultIfEmpty(2).Max(),
                        _request.CompiledSearch.NormalizedQuery.EventSequenceConstraints.Count),
                    _ => Math.Max(1, child.AnalyticalCost.WorkUnitsPerInput)
                };
                weightedWork += reach * units;
                reach *= child.ResolveSurvival(passedCoverage).SurvivalProbability ?? 1;
            }
            double outputRate = ResolveSurvival(passedCoverage).SurvivalProbability ?? 1;
            int workers = children[0] is FamilyCpuExecution cpuChild ? cpuChild.Workers : 1;
            var measured = PartyMeasuredCost.Price(FamilyId, inputs, children.Length, gpu,
                UsesPrivate, children.Min(c => c.AnalyticalCost.InputCapacityPerInvocation),
                weightedWork, outputRate, workers, smallConfiguredShopCpu: _smallConfiguredShopCpu,
                cpuStages: !gpu && FamilyId == "R.Relic" && children.All(child => child is FamilyCpuExecution)
                    ? children.Cast<FamilyCpuExecution>().Select(child =>
                        (child.Condition(compact).PhysicalImplementationRevision, child.WindowCapacity)).ToArray()
                    : null);
            if (measured is { } local)
            {
                bool changedShopBody = gpu && FamilyId == "S.MerchantShopColorless" && _stages.Any(s =>
                    MerchantShopColorlessGpuExecutor.PartyEarlyRejectionRevision(s.Request.Evaluation, s.Request.Authority.PlayersCount).Length != 0);
                string evidence = changedShopBody ? local.Evidence.Replace("LocalMeasuredGPU;", "HistoricalGpuReferenceEnvelope;", StringComparison.Ordinal) +
                    ";CurrentPartyPrefixBody=20261016.v1;HistoricalCoefficientsAppliedAsCoarseEnvelope;NotCurrentBodyMeasurement;NoSameRunFitting" : local.Evidence;
                string combatBody = gpu ? CombatBagCapacityRevision() : "";
                if (combatBody.Length != 0) evidence = evidence.Replace("LocalMeasuredGPU;", "HistoricalGpuReferenceEnvelope;", StringComparison.Ordinal) +
                    ";CurrentPartyCombatBody=" + combatBody + ";HistoricalCoefficientsAppliedAsCoarseEnvelope;NotCurrentBodyMeasurement;NoSameRunFitting";
                string relicBody = gpu ? RelicBagCapacityRevision() : "";
                if (relicBody.Length != 0) evidence = evidence.Replace("LocalMeasuredGPU;", "HistoricalGpuReferenceEnvelope;", StringComparison.Ordinal) +
                    ";CurrentPartyRelicBody=" + relicBody + ";HistoricalCoefficientsAppliedAsCoarseEnvelope;NotCurrentBodyMeasurement;NoSameRunFitting";
                evidence = evidence.Replace("LocalMeasuredGPU;", "HistoricalGpuReferenceEnvelope;", StringComparison.Ordinal)
                    .Replace("LocalMeasuredCPU;", "HistoricalCpuReferenceEnvelope;", StringComparison.Ordinal) +
                    ";PriceQuality=Estimate;HistoricalWrapperCoefficients;NotCurrentWrapperMeasurement";
                return new(local.Ms, local.SetupMs,
                    evidence + $";WeightedSourceWork={weightedWork:G9};InnerUnknownPass=ConservativeOne;SingleFamilyWrapper");
            }
        }
        return EstimateUnit(inputs, compact, children, computationOnly, UsesPrivate, costs);

        double WorldWork(int act, int events)
        {
            var generation = _request.CompiledSearch.Context.Party?.World.Beta109Generation;
            double bagEntries = generation is null ? 606 : generation.SharedRelicBuckets.Sum(b => b.OrderedRelics.Count) +
                generation.PartyRelicBuckets.SelectMany(b => b).Sum(b => b.OrderedRelics.Count);
            return Math.Max(.25, bagEntries / 606d) *
                (1 + Math.Max(0, act - 2) * .5 + events * .25);
        }
    }

    private static FamilyUnitCost? EstimateUnit(double inputs, bool compact, IReadOnlyList<IFamilyInvocation> children,
        bool computationOnly, bool privateEdges, GpuCostSnapshot? costs)
    {
        if (!double.IsFinite(inputs) || inputs is <= 0 or > PrivateOrdinalBuffer.Capacity) return null;
        double milliseconds = 0, reach = 1;
        double? setup = 0;
        var evidence = new List<string>();
        for (int index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (reach == 0) break;
            bool privateRole = computationOnly && privateEdges;
            var geometry = new FamilyPhysicalQuoteRequest(compact || index > 0,
                privateRole && index < children.Count - 1, inputs * reach, PrivateInput: privateRole && (compact || index > 0))
                { ComputationOnly = computationOnly, CostSnapshot = costs };
            var quote = computationOnly ? FamilyPhysicalQuote.Computation(child, geometry, costs, NoPassedPersonalCoverage) :
                child is FamilyCpuExecution cpu ? cpu.Quote(geometry, NoPassedPersonalCoverage) : child.QuotePhysicalWork(geometry);
            if (quote is null) return null;
            if (computationOnly && costs is null) quote = FamilyCoarsePricing.LocalComputation(child, quote, geometry);
            double? survival = child.ResolveSurvival(NoPassedPersonalCoverage).SurvivalProbability;
            double bound = survival is >= 0 and <= 1 ? survival.Value : 1;
            var time = computationOnly ? FamilyCoarsePricing.ComputationMilliseconds(quote, geometry)
                : FamilyCoarsePricing.PublicMilliseconds(quote, geometry, bound);
            if (time is null) return null;
            milliseconds += time.Value;
            setup = setup.HasValue && quote.SetupMilliseconds.HasValue ? setup + quote.SetupMilliseconds : null;
            evidence.Add($"Child{index}={quote.Shape};InputReach={reach:G17};OwnOutputEnvelope={bound:G17};" + quote.Evidence);
            reach *= bound;
        }
        return new(milliseconds, setup, FamilyCoarsePricing.Revision +
            ";PriceQuality=Estimate;SingleFamilyWrapper;WeightedOwnChildren;UnknownChildReachIsCostUpperBoundOnly;" +
            "PersonalStagesUseTheirOwnUnconditionalS;" +
            (computationOnly ? "ComputationOnly;NumericalAndDeviceEmission;NoSubmitOrHostReadback;NoCrossFamilyHandoffCost;"
                : "PublicCallEnvelopeAlsoBoundsPrivate;NoCrossFamilyConnectionQuote;") + string.Join('|', evidence));
    }
    private bool NeedsCompleteCombatMatchingReference() => FamilyId == "C.CombatReward" &&
        _stages.Any(s => (s.Family is FamilyCpuExecution cpu ? cpu.RealizationOwner : s.Family)
            is CombatRewardFamily { Replay.Plan.NeedsCompleteMatchingReference: true });
    private bool NeedsQueryLiteralCombatOpeningReference() => FamilyId == "C.CombatReward" &&
        _stages.Any(s => (s.Family is FamilyCpuExecution cpu ? cpu.RealizationOwner : s.Family)
            is CombatRewardFamily { Replay.UsesQueryLiteralPartyOpening: true });
    private bool UsesUnmeasuredIntegerGpuBody() => _stages.Any(stage => stage.Family switch
    {
        CombatRewardFamily { GpuPlan.NumericalVariant: GpuNumericalVariant.IntegerRne } => true,
        CapsuleRelicFamily { GpuPlan.NumericalVariant: GpuNumericalVariant.IntegerRne } => true,
        WorldFamily { GpuPlan.NumericalVariant: GpuNumericalVariant.IntegerRne } => true,
        AncientOptionFamily { Plan.NumericalVariant: GpuNumericalVariant.IntegerRne, Plan.GpuSupported: true } => true,
        EventResultFamily { Plan.NumericalVariant: GpuNumericalVariant.IntegerRne, Plan.GpuSupported: true } => true,
        _ => false
    });
    private string CombatBagCapacityRevision() => FamilyId == "C.CombatReward"
        ? string.Concat(_stages.Select(s => s.Family is CombatRewardFamily c ? c.GpuPlan?.BagCapacityRevision ?? "" : "")) : "";
    private string RelicBagCapacityRevision() => FamilyId == "R.Relic"
        ? string.Concat(_stages.Select(s => s.Family is CapsuleRelicFamily r ? r.GpuBagCapacityRevision : "")) : "";
    public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact)
    {
        bool usesGpu = _stages.Any(s => s.Family.ConditionPerformance.UsesGpu);
        string numericalRevision = usesGpu && FamilyId == "S.MerchantShopColorless" && _stages.Any(s =>
            MerchantShopColorlessGpuExecutor.PartyEarlyRejectionRevision(s.Request.Evaluation, s.Request.Authority.PlayersCount).Length != 0)
            ? FamilyPhysicalImplementationRevisions.MerchantShopColorlessPartyPrefix : "";
        bool unmeasuredInteger = UsesUnmeasuredIntegerGpuBody();
        if (unmeasuredInteger) numericalRevision += ".IntegerRne.20261025.v1";
        string transportRevision = UsesPrivate && compact ? ".DirectCompactCast.20261016.v1" : "";
        string combatBodyRevision = usesGpu ? CombatBagCapacityRevision() : "";
        string matchingRevision = NeedsCompleteCombatMatchingReference() ? ".CompletePositiveMatching.20261026.v1" : "";
        string openingRevision = NeedsQueryLiteralCombatOpeningReference() ? ".PartyQueryLiteralOpening.20261027.v1" : "";
        string relicBodyRevision = usesGpu ? RelicBagCapacityRevision() : "";
        return new(FamilyId,
            FamilyId + (usesGpu ? ".Party.20260922.v1." : ".Party.20261013.v2.") +
                (UsesPrivate ? "PrivateOrdinal" : "Public") + (compact ? ".CompactAbi1" : ".Dense") + numericalRevision + transportRevision + combatBodyRevision + matchingRevision + openingRevision + relicBodyRevision + ".GreedyPlayers.20261026.v1" +
                (usesGpu ? GpuRngRotation.Revision : ""),
            "PartyFamilyConjunction", 1, "OneFamily;PersonalAndSharedPredicates;WholeTableExactFinal;BoundedLocalPhysicalQuote" +
                (unmeasuredInteger ? ";GpuNumericalVariant=IntegerRne;PhysicalQuoteUnavailable=UnmeasuredIntegerRneWrapper;ExecutionRemainsAvailable" : "") +
                (combatBodyRevision.Length != 0 ? ";CurrentCombatBagCapacityBody;HistoricalGpuCoefficientsRemainCoarseReference;NotCurrentBodyCalibration" : "") +
                (matchingRevision.Length != 0 ? ";CompleteCombatMatchingBody;NoHistoricalMatchingQuote" : "") +
                (openingRevision.Length != 0 ? ";PartyQueryLiteralOpeningBody;NoHistoricalOpeningQuote" : "") +
                (relicBodyRevision.Length != 0 ? ";CurrentRelicBagCapacityBody;HistoricalGpuCoefficientsRemainCoarseReference;NotCurrentBodyCalibration" : ""),
            usesGpu: usesGpu);
    }

    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window,
        FamilyCandidateSet input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (window.ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint || input.Batch != window.Batch)
            throw new InvalidOperationException("Party.FamilyObservationMismatch");
        if (input.Count == 0) return input;
        _compact = !input.IsDense;
        if (_executionStages is null)
        {
            _executionStages = OrderPersonalStages(_compact, NoPassedPersonalCoverage);
            RuntimeLog.TryBackgroundInfo($"partyPersonalOrder=true;family={FamilyId};policy={FamilyPlanner.SelectionPolicyId};" +
                $"inputMode={(_compact ? "CompactAbi1" : "Dense")};slots={string.Join(',', _executionStages.Select(s => s.Request.Authority.PlayerSlotIndex))};" +
                "sameFamilyPassNotSharedAcrossPlayers=true;sharedContextsRetained=true");
        }
        var timer = Stopwatch.StartNew();
        FamilyCandidateSet output = input;
        if (UsesPrivate)
        {
            _chain ??= await context.ExecuteGpuAsync(rd => new FamilyPrivateGpuChain(rd, _executionStages.Select(s => s.Family).ToArray()), token).ConfigureAwait(false);
            output = await context.ExecuteGpuAsync(_ => _chain.Execute(input, token, context.SmallCandidateExact), token).ConfigureAwait(false);
        }
        else for (int i = 0; i < _executionStages.Length; i++)
        {
            if (output.Count == 0 || context.SmallCandidateExact is not null && SmallCandidateExactPolicy.ShouldHandoff(output.Count))
            {
                context.SmallCandidateExact?.Observe(
                    "Party." + FamilyId + ".Slots=" + string.Join(',', _executionStages.Take(i).Select(s => s.Request.Authority.PlayerSlotIndex)),
                    _executionStages.Skip(i).Select(s => FamilyId + ".Slot=" + s.Request.Authority.PlayerSlotIndex).ToArray(), output.Count);
                if (output.IsDense)
                    output = FamilyCandidateSet.FromSortedAbi1(output.Batch, output.EnumerateLogicalOrdinals().ToArray());
                break;
            }
            var stage = _executionStages[i];
            output = await stage.Family.InvokeAsync(context, new(window.Batch, stage.Request), output, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        double elapsed = timer.Elapsed.TotalMilliseconds;
        _inputs += input.Count; _outputs += output.Count; _ms += elapsed;
        if (_batches++ > 0) { Interlocked.Add(ref _liveInputs, input.Count); _liveMs += elapsed; }
        return output;
    }
    public FamilyPerformanceObservation CapturePerformanceObservation() => new(ResolveConditionPerformance(_compact),
        _chain?.Device ?? _stages[0].Family.CapturePerformanceObservation().DeviceName,
        _chain?.SetupMs ?? _stages.Sum(s => s.Family.CapturePerformanceObservation().SetupMs), 0, 0, _batches, Math.Max(0, _batches - 1),
        _inputs, _outputs, _ms, false, "PartyFamily;CanonicalAbi1Observation;BoundedLocalQuote");
    public FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => _batches < 2 ? null :
        new(FamilyId, ResolveConditionPerformance(_compact).PhysicalImplementationRevision, Interlocked.Read(ref _liveInputs), Volatile.Read(ref _liveMs), _batches - 1);
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if (_chain is not null)
            await context.ExecuteGpuAsync(_ => { _chain.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false);
        foreach (var stage in _stages) await stage.Family.DisposeAsync(context).ConfigureAwait(false);
        RuntimeLog.TryBackgroundInfo($"partyFamilySummary=true;family={FamilyId};stages={_stages.Length};private={UsesPrivate};inputs={_inputs};outputs={_outputs};wallMs={_ms};intermediatePayloadReadbackBytes={(UsesPrivate ? 0 : -1)}");
    }
}
