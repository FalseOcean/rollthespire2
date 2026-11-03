using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record PartyFilterStage(int Slot, FilterBodyDescription Body);

// Immutable one-family party composition. Shared state is established by domain
// preparation; only per-slot descriptors, frozen projections and prices remain.
internal sealed class PartyFamily
{
    private static readonly IReadOnlySet<string> NoPassedPersonalCoverage = new HashSet<string>(StringComparer.Ordinal);
    private readonly PartyFilterStage[] _stages;
    private readonly GpuCostSnapshot? _costs;
    private readonly FamilySurvivalProjection? _sharedSurvival;
    private readonly string _identity;
    private readonly int _workers, _players;
    private readonly RolltheSpire2.Compatibility.RuntimeProfileId _profile;
    private readonly double _worldBagScale;
    private readonly bool _smallConfiguredShopCpu;
    private string FamilyId => _stages[0].Body.FamilyId;
    private bool UsesPrivate => _stages.Length > 1 && _stages.All(s => s.Body.CanBindPrivateSerial);
    private bool UsesGpu => _stages.Any(s => s.Body.UsesGpu);
    private bool NeedsCompleteMatching => _stages.Any(s => s.Body.Cost.PartyChild.CompleteCombatMatching);
    private bool NeedsLiteralOpening => _stages.Any(s => s.Body.Cost.PartyChild.QueryLiteralCombatOpening);
    private bool HistoricalEnvelope => _stages.All(s => s.Body.Cost.PartyChild.AllowsHistoricalEnvelope) &&
        !NeedsCompleteMatching && !NeedsLiteralOpening;
    private string CapacityRevision => string.Concat(_stages.Select(s => s.Body.Cost.PartyChild.CapacityRevision));
    private bool ShopPrefixChanged => _stages.Any(s => s.Body.Cost.PartyChild.ShopPrefixChanged);

    internal static FilterBodyDescription Prepare(SearchModel model, FilterExecutionPreferences preferences,
        IReadOnlyList<PartyFilterStage> stages)
    {
        if (stages.Count == 0 || stages.Any(s => s.Body.FamilyId != stages[0].Body.FamilyId))
            throw new ArgumentException("Party.MixedFamilyInvocation");
        string family = stages[0].Body.FamilyId;
        var shared = family is "W.World" or "E.EventResult" ? PartyStageSurvival.Project(model, family) : null;
        var generation = model.CompiledSearch.Context.Party?.World.Beta109Generation;
        double bagEntries = generation is null ? 606 : generation.SharedRelicBuckets.Sum(b => b.OrderedRelics.Count) +
            generation.PartyRelicBuckets.SelectMany(b => b).Sum(b => b.OrderedRelics.Count);
        var costs = FamilyAuxiliaryEvidence.Read<GpuCostSnapshot?>(family, "PersonalUnitCostSnapshot", GpuCostCalibration.Capture,
            null, model.SnapshotFingerprint);
        return new PartyFamily(stages.ToArray(), costs, shared, model.SnapshotFingerprint,
            Math.Clamp(preferences.WorkerCount, 1, 32), model.Authority.PlayersCount, model.ProfileId,
            Math.Max(.25, bagEntries / 606d)).Describe();
    }
    private PartyFamily(PartyFilterStage[] stages, GpuCostSnapshot? costs, FamilySurvivalProjection? shared,
        string identity, int workers, int players, RolltheSpire2.Compatibility.RuntimeProfileId profile,
        double worldBagScale, bool smallConfiguredShopCpu = false)
    { _stages = stages; _costs = costs; _sharedSurvival = shared; _identity = identity; _workers = workers;
        _players = players; _profile = profile; _worldBagScale = worldBagScale; _smallConfiguredShopCpu = smallConfiguredShopCpu; }

    private FilterBodyDescription Describe()
    {
        var prepared = new PreparedFilter(FamilyId)
        {
            Coverage = _stages.SelectMany(s => s.Body.Coverage).Distinct(StringComparer.Ordinal).ToArray(),
            Obligations = _stages.SelectMany(s => s.Body.Preparation.EffectiveObligations.Select(obligation => obligation with
                { PlayerSlot = obligation.Domain == "W.World" ? null : obligation.PlayerSlot ?? s.Slot })).ToArray(),
            AnalyticalCost = new(FamilyId, CpuFilterExecution.Capacity, [], "PartyFamilyAnalyticalWorkUnavailable;PhysicalQuoteBounded"),
            Survival = ResolveSurvival(NoPassedPersonalCoverage), HasConditionalProjections = _stages.Any(s => s.Body.HasConditionalProjections),
            SurvivalResolver = ResolveSurvival
        };
        var alternatives = new List<FilterBodyDescription>();
        if (_stages.All(s => s.Body.UsesGpu))
        {
            var cpu = _stages.Select(s => new PartyFilterStage(s.Slot,
                s.Body.CpuAlternatives.FirstOrDefault(c => c.CpuWorkers == _workers)!)).ToArray();
            if (cpu.All(s => s.Body is not null)) alternatives.Add(new PartyFamily(cpu, _costs, _sharedSurvival,
                _identity, _workers, _players, _profile, _worldBagScale, FamilyId == "S.MerchantShopColorless").Describe());
        }
        // Execution chooses the first input mode once, using the same frozen
        // unconditional player facts used by the price projection.
        var dense = OrderPersonalStages(false, NoPassedPersonalCoverage);
        var compact = OrderPersonalStages(true, NoPassedPersonalCoverage);
        return new(prepared, Condition(false).PhysicalImplementationRevision, _profile)
        {
            UsesGpu = UsesGpu, CpuWorkers = _workers, PlayersCount = _players, InputIdentity = _identity,
            WindowCapacity = UsesGpu ? FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize : CpuFilterExecution.Capacity,
            PreferredRootWindowSize = UsesGpu ? null : CpuFilterExecution.Capacity,
            NumericRequirements = _stages.Aggregate(GpuNumericRequirements.None, (value, s) => value | s.Body.NumericRequirements),
            CpuAlternatives = alternatives, PhysicalBodies = _stages.Select(s => s.Body).ToArray(),
            PerformanceProjection = Condition,
            Cost = new()
            {
                ComposedPopulationReference = true,
                CompleteCallQuote = (geometry, passed) => QuoteUnit(geometry.MeanInputPopulation, geometry.CompactInput, passed),
                Quote = (self, geometry, passed) => Quote(self, geometry, passed)
            },
            CreateExecution = RuntimeFactory(dense, compact, UsesPrivate)
        };
    }
    private static Func<FilterBodyDescription, FilterRuntimeServices, IFilterExecution> RuntimeFactory(
        PartyFilterStage[] dense, PartyFilterStage[] compact, bool privateEdges) =>
        (body, _) => new PartyFilterExecution(body, dense, compact, privateEdges);

    private FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passed)
    {
        if (FamilyId == "W.World" && _stages.Length == 1) return _stages[0].Body.ResolveSurvival(passed);
        if (_sharedSurvival is not null) return _sharedSurvival;
        double probability = 1;
        foreach (var stage in _stages)
        {
            var current = stage.Body.ResolveSurvival(passed);
            if (current.SurvivalProbability is not double value) return FamilySurvivalProjection.Unresolved(FamilyId,
                $"PartyStageProbabilityUnavailable:{current.Evidence};KnownPlayerProduct={probability:G17};NoUnresolvedPlayerSubstitution");
            probability *= value;
        }
        return FamilySurvivalProjection.Resolved(FamilyId, probability,
            "PersonalStageSieveProductApproximation;SharedFactsNotDuplicated;CrossPlayerPersonalConditionsAssumedIndependent");
    }
    private PartyFilterStage[] OrderPersonalStages(bool firstCompact, IReadOnlySet<string> passed)
    {
        var remaining = _stages.ToList(); var ordered = new List<PartyFilterStage>();
        while (remaining.Count > 0)
        {
            bool compact = firstCompact || ordered.Count > 0;
            double? Hardware(FilterBodyDescription body) => FamilyAuxiliaryEvidence.Read<double?>(FamilyId, "PersonalUnitHardwareCost",
                () => FamilyPerformanceProjectionMath.PerInputMilliseconds(GpuCostSnapshot.ComputationReference(_costs, body,
                    body.ResolveConditionPerformance(compact), compact, passed, referencePopulation: !body.UsesGpu ? body.WindowCapacity : null)), null, _identity);
            double? Probability(FilterBodyDescription body) => FamilyAuxiliaryEvidence.Read<double?>(FamilyId, "PersonalUnitSurvival",
                () => body.ResolveSurvival(passed).SurvivalProbability, null, _identity);
            double? Analytical(FilterBodyDescription body) => FamilyAuxiliaryEvidence.Read<double?>(FamilyId, "PersonalUnitWork", () =>
            {
                var work = body.ResolveExpectedFilteringCost(passed);
                return work?.ExpectedFilteringWorkUnitsPerInput + (compact ? work?.CompactInputWorkUnitsPerInput : 0);
            }, null, _identity);
            var selected = GreedyUnitOrder.Select(remaining, s => Hardware(s.Body), s => Probability(s.Body), s => Analytical(s.Body));
            ordered.Add(selected); remaining.Remove(selected);
        }
        return ordered.ToArray();
    }
    private FamilyPhysicalQuote? Quote(FilterBodyDescription self, FamilyPhysicalQuoteRequest geometry, IReadOnlySet<string> passed)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput || geometry.MeanInputPopulation <= 0) return null;
        var price = QuoteUnit(geometry.MeanInputPopulation, geometry.CompactInput, passed, geometry.ComputationOnly, geometry.CostSnapshot);
        var quote = price is null ? null : new FamilyPhysicalQuote(FamilyId,
            geometry.ComputationOnly ? "PartyFamilyComputationEnvelope" : "PartyFamilyReferenceEnvelope",
            price.Milliseconds * 1e6 / geometry.MeanInputPopulation,
            UsesGpu ? FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize : CpuFilterExecution.Capacity,
            price.SetupMilliseconds, price.Evidence + (geometry.ComputationOnly ? ";FamilyComputationBoundary" : ";CompleteCanonicalAbi1;PlannerRankingReference"),
            OutputElementBytes: 8, OutputAlreadyOrdered: true,
            PublicTransportClass: geometry.ComputationOnly ? UsesGpu ? "DeviceFamilyWork" : "CpuOrderedAbi1" : "CompleteCanonicalAbi1")
        { LocalCostSource = geometry.ComputationOnly ? "ComponentComposition" : "Condition" };
        return geometry.ComputationOnly ? quote : FamilyGpuReadback.Reference(self, quote);
    }
    private FamilyUnitCost? QuoteUnit(double inputs, bool compact, IReadOnlySet<string> passed,
        bool computationOnly = false, GpuCostSnapshot? costs = null)
    {
        var children = OrderPersonalStages(compact, NoPassedPersonalCoverage).Select(s => s.Body).ToArray();
        bool gpu = children.All(c => c.UsesGpu), cpu = children.All(c => !c.UsesGpu);
        if (!computationOnly && HistoricalEnvelope && (gpu || cpu))
        {
            double work = 0, reach = 1;
            foreach (var child in children)
            {
                double units = child.Cost.PartyChild.WorkUnits ?? Math.Max(1, child.AnalyticalCost.WorkUnitsPerInput);
                if (FamilyId == "W.World") units *= _worldBagScale;
                work += reach * units; reach *= child.ResolveSurvival(passed).SurvivalProbability ?? 1;
            }
            double output = ResolveSurvival(passed).SurvivalProbability ?? 1;
            var measured = PartyMeasuredCost.Price(FamilyId, inputs, children.Length, gpu, UsesPrivate,
                children.Min(c => c.AnalyticalCost.InputCapacityPerInvocation), work, output,
                cpu ? children[0].CpuWorkers : 1, smallConfiguredShopCpu: _smallConfiguredShopCpu,
                cpuStages: cpu && FamilyId == "R.Relic" ? children.Select(c =>
                    (c.ResolveConditionPerformance(compact).PhysicalImplementationRevision, c.WindowCapacity)).ToArray() : null);
            if (measured is { } local)
            {
                string evidence = local.Evidence;
                if (gpu && ShopPrefixChanged) evidence += ";CurrentPartyPrefixBody=20261016.v1;HistoricalCoefficientsAppliedAsCoarseEnvelope;NotCurrentBodyMeasurement;NoSameRunFitting";
                if (gpu && CapacityRevision.Length != 0) evidence += ";CurrentPartyCapacityBody=" + CapacityRevision + ";HistoricalCoefficientsAppliedAsCoarseEnvelope;NotCurrentBodyMeasurement;NoSameRunFitting";
                evidence = evidence.Replace("LocalMeasuredGPU;", "HistoricalGpuReferenceEnvelope;", StringComparison.Ordinal)
                    .Replace("LocalMeasuredCPU;", "HistoricalCpuReferenceEnvelope;", StringComparison.Ordinal) +
                    ";PriceQuality=Estimate;HistoricalWrapperCoefficients;NotCurrentWrapperMeasurement";
                return new(local.Ms, local.SetupMs, evidence + $";WeightedSourceWork={work:G9};InnerUnknownPass=ConservativeOne;SingleFamilyWrapper");
            }
        }
        return EstimateUnit(inputs, compact, children, computationOnly, UsesPrivate, costs);
    }
    private static FamilyUnitCost? EstimateUnit(double inputs, bool compact, IReadOnlyList<FilterBodyDescription> children,
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
                child.QuotePhysicalWork(geometry, NoPassedPersonalCoverage);
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

    private FamilyConditionPerformanceProjection Condition(bool compact)
    {
        string numerical = UsesGpu && ShopPrefixChanged ? FamilyPhysicalImplementationRevisions.MerchantShopColorlessPartyPrefix : "";
        bool unmeasuredInteger = _stages.Any(s => s.Body.UsesGpu &&
            s.Body.Cost.PartyChild.NumericalRevision.Contains(".IntegerRne.", StringComparison.Ordinal));
        if (unmeasuredInteger) numerical += ".IntegerRne.20261025.v1";
        return new(FamilyId, FamilyId + (UsesGpu ? ".Party.20260922.v1." : ".Party.20261013.v2.") +
            (UsesPrivate ? "PrivateOrdinal" : "Public") + (compact ? ".CompactAbi1" : ".Dense") + numerical +
            (UsesPrivate && compact ? ".DirectCompactCast.20261016.v1" : "") + (UsesGpu ? CapacityRevision : "") +
            (NeedsCompleteMatching ? ".CompletePositiveMatching.20261026.v1" : "") +
            (NeedsLiteralOpening ? ".PartyQueryLiteralOpening.20261027.v1" : "") + ".GreedyPlayers.20261026.v1" +
            (UsesGpu ? GpuRngRotation.Revision : ""), "PartyFamilyConjunction", 1,
            "OneFamily;PersonalAndSharedPredicates;WholeTableExactFinal;BoundedLocalPhysicalQuote" +
            (unmeasuredInteger ? ";GpuNumericalVariant=IntegerRne;PhysicalQuoteUnavailable=UnmeasuredIntegerRneWrapper;ExecutionRemainsAvailable" : "") +
            (UsesGpu && CapacityRevision.Length != 0 && FamilyId == "C.CombatReward"
                ? ";CurrentCombatBagCapacityBody;HistoricalGpuCoefficientsRemainCoarseReference;NotCurrentBodyCalibration" : "") +
            (NeedsCompleteMatching ? ";CompleteCombatMatchingBody;NoHistoricalMatchingQuote" : "") +
            (NeedsLiteralOpening ? ";PartyQueryLiteralOpeningBody;NoHistoricalOpeningQuote" : "") +
            (UsesGpu && CapacityRevision.Length != 0 && FamilyId == "R.Relic"
                ? ";CurrentRelicBagCapacityBody;HistoricalGpuCoefficientsRemainCoarseReference;NotCurrentBodyCalibration" : ""), UsesGpu);
    }
}

internal sealed class PartyFilterExecution : IFilterExecution
{
    public FilterBodyDescription Body { get; }
    public (string Device, double SetupMs) ResourceInfo
    {
        get
        {
            var current = CaptureResourceInfo();
            return string.IsNullOrEmpty(current.Device) && current.SetupMs == 0 ? _lastResource : current;
        }
    }
    private (string Device, double SetupMs) _lastResource = ("", 0);
    private readonly PartyFilterStage[] _dense, _compact;
    private readonly bool _private;
    private (int Slot, IFilterExecution Execution)[]? _executions;
    private FamilyPrivateGpuChain? _chain;
    public bool ExecutionFaulted => _executions?.Any(s => s.Execution.ExecutionFaulted) == true;
    internal PartyFilterExecution(FilterBodyDescription body, PartyFilterStage[] dense, PartyFilterStage[] compact, bool privateEdges)
    { Body = body; _dense = dense; _compact = compact; _private = privateEdges; }
    private (string Device, double SetupMs) CaptureResourceInfo()
    {
        if (_chain is { } chain) return (chain.Device, chain.SetupMs);
        var children = _executions?.Select(stage => stage.Execution.ResourceInfo).ToArray() ?? [];
        return (children.FirstOrDefault(info => !string.IsNullOrEmpty(info.Device)).Device ?? "",
            children.Sum(info => info.SetupMs));
    }
    public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (input.Count == 0) return input;
        var stages = input.IsDense ? _dense : _compact;
        if (_executions is null)
        {
            var passed = services.CoverageFor(Body);
            _executions = stages.Select(s =>
            {
                services.BindCoverage(s.Body, passed);
                return (s.Slot, (s.Body.CreateExecution ??
                    throw new InvalidOperationException("Party.ChildExecutionUnavailable:" + s.Body.FamilyId))(s.Body, services));
            }).ToArray();
        }
        var timer = Stopwatch.StartNew(); var output = input;
        if (_private)
        {
            _chain ??= await services.ExecuteGpuAsync(device => new FamilyPrivateGpuChain(device,
                _executions.Select(s => s.Execution.Body).ToArray()), token).ConfigureAwait(false);
            output = await services.ExecuteGpuAsync(_ => _chain.Execute(input, token, services.SmallCandidateExact), token).ConfigureAwait(false);
        }
        else for (int i = 0; i < _executions.Length; i++)
        {
            if (output.Count == 0 || services.SmallCandidateExact is not null && SmallCandidateExactPolicy.ShouldHandoff(output.Count))
            {
                services.SmallCandidateExact?.Observe("Party." + Body.FamilyId + ".Slots=" + string.Join(',', _executions.Take(i).Select(s => s.Slot)),
                    _executions.Skip(i).Select(s => Body.FamilyId + ".Slot=" + s.Slot).ToArray(), output.Count);
                if (output.IsDense) output = FamilyCandidateSet.FromSortedAbi1(output.Batch, output.EnumerateLogicalOrdinals().ToArray());
                break;
            }
            output = await _executions[i].Execution.FilterAsync(services, output, token).ConfigureAwait(false);
        }
        services.Costs.ObserveCompleted(Body, input, output.Count, timer.Elapsed.TotalMilliseconds, passedCoverage: services.CoverageFor(Body));
        return output;
    }
    public async ValueTask DisposeAsync(FilterRuntimeServices services)
    {
        _lastResource = CaptureResourceInfo();
        if (_chain is not null) await services.ExecuteGpuAsync(_ => { _chain.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false);
        foreach (var stage in _executions ?? []) await stage.Execution.DisposeAsync(services).ConfigureAwait(false);
    }
}
