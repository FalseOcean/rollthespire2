using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Predictability;

namespace RolltheSpire2.Search.FamilyExecution;

public static partial class FamilyExecutionCoordinator
{
    private delegate bool CreateFamily(ExactSearchExecutionRequest request, out IFamilyInvocation? family);

    private static FamilyExecutionPlan PlanParty(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered)
    {
        FamilyExecutionPlan plan = FamilyPlanner.Plan(registered);
        GpuCostSnapshot gpuCosts = GpuCostCalibration.Capture();
        int rootWindow = ResolveExecutionWindowSize(request, plan.OrderedFamilies);
        var fractions = new double[plan.OrderedFamilies.Count];
        var passedForPrice = new IReadOnlySet<string>[plan.OrderedFamilies.Count];
        double terminal = 1;
        var passedCoverage = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < plan.OrderedFamilies.Count; index++)
        {
            IFamilyInvocation family = plan.OrderedFamilies[index];
            fractions[index] = terminal;
            passedForPrice[index] = passedCoverage.ToHashSet(StringComparer.Ordinal);
            if (family.ResolveSurvival(passedCoverage).SurvivalProbability is not double survival)
                return plan;
            terminal *= survival;
            passedCoverage.UnionWith(family.Coverage);
        }
        AutomaticPrivateSerialPricing.PricedOrder? Stage(int index, double inputs)
        {
            IFamilyInvocation family = plan.OrderedFamilies[index];
            return family switch
            {
                PartyFamily party => party.Price(inputs, index > 0, passedForPrice[index], gpuCosts, rootWindow),
                NeowFamily.PartyInitial neow => neow.Price(inputs, index > 0, gpuCosts, rootWindow),
                _ => null
            };
        }
        double? Fragment(double count)
        {
            double ms = 0;
            for (int index = 0; index < fractions.Length; index++)
            {
                double reached = SearchPredictabilityMath.ProbabilityAtLeastOne(fractions[index], count);
                if (reached <= 0) continue;
                double conditionalInputs = count * fractions[index] / reached;
                var stage = Stage(index, conditionalInputs);
                if (stage is null) return null;
                ms += reached * stage.CanonicalMs;
            }
            return ms;
        }
        (double Ms, double? SetupMs, double Terminal)? Price(double roots)
        {
            if (!double.IsFinite(roots) || roots < 0) return null;
            double full = Math.Floor(roots / rootWindow);
            double remainder = roots - full * rootWindow;
            double? fullMs = full > 0 ? Fragment(rootWindow) : 0;
            double? remainderMs = remainder > 0 ? Fragment(remainder) : 0;
            if (!fullMs.HasValue || !remainderMs.HasValue) return null;
            double milliseconds = full * fullMs.Value + remainderMs.Value;
            double? setup = 0;
            for (int index = 0; index < plan.OrderedFamilies.Count; index++)
            {
                double reached = SearchPredictabilityMath.ProbabilityAtLeastOne(fractions[index], roots);
                if (reached <= 0) continue;
                double windowReach = SearchPredictabilityMath.ProbabilityAtLeastOne(fractions[index],
                    Math.Min(roots, rootWindow));
                double conditionalInputs = Math.Min(roots, rootWindow) * fractions[index] / windowReach;
                var stage = Stage(index, conditionalInputs);
                if (stage is null) return null;
                setup = setup.HasValue && stage.SetupMs.HasValue ? setup + reached * stage.SetupMs : null;
            }
            return (milliseconds, setup, terminal);
        }
        var reference = Price(Math.Min(request.ScanCount, rootWindow));
        if (reference is null) return plan;
        return plan with
        {
            EstimateCanonicalMilliseconds = roots => Price(roots)?.Ms,
            EstimatedSetupMilliseconds = reference.Value.SetupMs,
            EstimatedTerminalSurvival = reference.Value.Terminal,
            MissingEvidence = plan.MissingEvidence.Where(e =>
                !e.StartsWith("FamilyHardwarePerformanceUnavailable:", StringComparison.Ordinal) &&
                !e.StartsWith("FamilyExpectedFilteringCostUnavailable:", StringComparison.Ordinal)).ToArray(),
            CompleteQuoteEvidence = "PartyFamilyOwnedStages;ActualFamilyMajorExecution;" +
                "PrivateIntermediateOrdinalsStayOnDevice;ConservativeUnknownInnerPassBoundOne;" +
                "RootWindow=" + rootWindow
        };
    }

    private static IReadOnlyList<IFamilyInvocation> CreatePartyFamilies(ExactSearchExecutionRequest request, bool gpu)
    {
        var result = new List<IFamilyInvocation>();
        if (request.CompiledSearch.NormalizedQuery.Players.Any(NeowFamily.PartyInitial.HasConditions))
            result.Add(new NeowFamily.PartyInitial(request, gpu));
        var children = request.CompiledSearch.PlayerSearches.Select(p => ExactSearchExecutionRequestFactory.ForPlayer(request, p)).ToArray();
        Add(MerchantShopColorlessFamily.TryCreate);
        Add(CreateRelic);
        var arrivals = new Dictionary<int, (int? Niche, int? Potions)>();
        bool hasCombat = children.Any(c => c.CompiledSearch.NormalizedQuery.HasCombatRewardConstraints);
        int? niche = 0, potions = 0;
        foreach (var child in children.Take(request.CompiledSearch.Context.Party!.Players.Count))
        {
            arrivals[child.Authority.PlayerSlotIndex] = (niche, potions);
            if (!hasCombat || child.CompiledSearch.NormalizedQuery.OpeningRoute is null) continue;
            try
            {
                var advance = NeowFamily.PartyInitial.SharedAdvance(child, NeowReplayPlan.Compile(child, false));
                niche = niche.HasValue && advance.Niche.HasValue ? niche.Value + advance.Niche.Value : null;
                potions = potions.HasValue && advance.Potions.HasValue ? potions.Value + advance.Potions.Value : null;
            }
            catch (InvalidOperationException ex)
            {
                // Missing numerical prefix authority is not a query rejection.
                // Only downstream consumers of these shared streams are tainted.
                niche = potions = null;
                RuntimeLog.TryBackgroundInfo($"partyOpeningPrefixUnresolved=true;slot={child.Authority.PlayerSlotIndex};reason={ex.Message};owner=WholeTableExact");
            }
        }
        Add(CreateCombat);
        Add(WorldFamily.TryCreate, sharedOnly: true);
        Add(AncientOptionFamily.TryCreate);
        Add(EventResultFamily.TryCreate);
        // M continues at whole-table Exact. The single-player bounded map sieve
        // is not a multiplayer map authority; no map GPU is introduced here.
        RuntimeLog.TryBackgroundInfo($"partyFamilyRegistration=true;familyMajor=true;families={string.Join(',', result.Select(f => f.FamilyId))};gpuAvailable={gpu};cost=FamilyOwnedStageProjection");
        return result;

        bool CreateRelic(ExactSearchExecutionRequest child, out IFamilyInvocation? family) =>
            NeowReplayPlan.HasCapsule(child) ? CapsuleRelicFamily.TryCreate(child, out family) : RelicFamily.TryCreate(child, out family);

        bool CreateCombat(ExactSearchExecutionRequest child, out IFamilyInvocation? family)
        {
            var arrival = arrivals[child.Authority.PlayerSlotIndex];
            bool admitted = CombatRewardFamily.TryCreateParty(child, arrival.Niche, arrival.Potions, out family);
            if (!admitted && child.CompiledSearch.NormalizedQuery.HasCombatRewardConstraints)
                RuntimeLog.TryBackgroundInfo($"partyCombatRemainder=true;slot={child.Authority.PlayerSlotIndex};reason=UnsupportedProfileContextOrOpeningIdentity;owner=WholeTableExact");
            return admitted;
        }

        void Add(CreateFamily create, bool sharedOnly = false)
        {
            var stages = new List<(ExactSearchExecutionRequest Request, IFamilyInvocation Family)>();
            foreach (var child in sharedOnly ? children.TakeLast(1) : children)
            {
                if (!create(child, out var family) || family is null) continue;
                if (!gpu)
                {
                    var alternatives = family.CpuRealizations.ToArray();
                    var cpu = alternatives.FirstOrDefault();
                    if (cpu is FamilyCpuExecution first)
                    {
                        string body = first.Condition(false).PhysicalImplementationRevision.Split(".RootMode", StringSplitOptions.None)[0];
                        cpu = alternatives.OfType<FamilyCpuExecution>()
                            .Where(c => c.Workers <= child.WorkerCount && c.Condition(false).PhysicalImplementationRevision
                                .StartsWith(body + ".RootMode", StringComparison.Ordinal))
                            .OrderByDescending(c => c.Workers).FirstOrDefault() ?? first;
                    }
                    if (cpu is not null) family = cpu;
                    else if (family.ConditionPerformance.UsesGpu)
                        throw new InvalidOperationException("Party.CpuRealizationMissing:" + family.FamilyId);
                }
                stages.Add((child, family));
            }
            if (stages.Count > 0) result.Add(new PartyFamily(request, stages.ToArray()));
        }
    }

    // A physical specialization of one existing Family. Each slot retains its
    // immutable request; only ordered batch-local ordinals leave this invocation.
    private sealed class PartyFamily : IFamilyInvocation
    {
        private readonly ExactSearchExecutionRequest _request;
        private readonly (ExactSearchExecutionRequest Request, IFamilyInvocation Family)[] _stages;
        private FamilyPrivateGpuChain? _chain;
        private bool _compact;
        private int _batches;
        private long _inputs, _outputs, _liveInputs;
        private double _ms, _liveMs;
        private bool UsesPrivate => _stages.Length > 1 && _stages.All(s => s.Family.CanBindPrivateSerial);

        internal PartyFamily(ExactSearchExecutionRequest request,
            (ExactSearchExecutionRequest Request, IFamilyInvocation Family)[] stages)
        {
            _request = request; _stages = stages;
            if (stages.Length == 0 || stages.Any(s => s.Family.FamilyId != stages[0].Family.FamilyId))
                throw new ArgumentException("Party.MixedFamilyInvocation");
            RuntimeLog.TryBackgroundInfo($"partyCostShape=true;family={FamilyId};stages={stages.Length};" +
                $"private={UsesPrivate};workPerInput={string.Join(',', stages.Select(s => s.Family.AnalyticalCost.WorkUnitsPerInput.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)))};" +
                $"capacity={string.Join(',', stages.Select(s => s.Family.AnalyticalCost.InputCapacityPerInvocation))};" +
                $"worldBagEntries={string.Join(',', stages.Select(s => s.Family is WorldFamily w ? w.Replay.BucketLengths.Sum(x => (long)x) : 0))};" +
                $"revisions={string.Join(',', stages.Select(s => s.Family.ConditionPerformance.PhysicalImplementationRevision))}");
        }
        public string FamilyId => _stages[0].Family.FamilyId;
        public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, FamilyCpuExecution.Capacity, [], "PartyFamilyAnalyticalWorkUnavailable;PhysicalQuoteBounded");
        public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry)
        {
            if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput ||
                geometry.MeanInputPopulation <= 0) return null;
            int window = ConditionPerformance.UsesGpu ? FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize : FamilyCpuExecution.Capacity;
            var price = Price(geometry.MeanInputPopulation, geometry.CompactInput,
                new HashSet<string>(StringComparer.Ordinal), GpuCostCalibration.Capture(), window);
            return price is null ? null : new(FamilyId, "PartyFamilyMeasuredEnvelope",
                price.CanonicalMs * 1e6 / geometry.MeanInputPopulation, window, price.SetupMs,
                string.Join(';', price.Evidence) + ";CompleteCanonicalAbi1;PlannerRankingReference")
            { LocalCostSource = "Condition", OutputAlreadyOrdered = true, PublicTransportClass = "CompleteCanonicalAbi1" };
        }
        public FamilySurvivalProjection Survival => ResolveSurvival(new HashSet<string>(StringComparer.Ordinal));
        public bool HasConditionalProjections => _stages.Any(s => s.Family.HasConditionalProjections);
        public FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passedCoverage)
        {
            if (FamilyId is "W.World" or "E.EventResult")
                return PartyStageSurvival.Project(_request, FamilyId);
            double probability = 1;
            foreach (var (_, family) in _stages)
            {
                FamilySurvivalProjection stage = family.ResolveSurvival(passedCoverage);
                if (stage.SurvivalProbability is not double value)
                {
                    if (FamilyId == "C.CombatReward")
                        return FamilySurvivalProjection.Resolved(FamilyId, 1,
                            "PartyC.ConservativeFastPassUpperEnvelope;ConditionalOpeningOrUnorderedNecessaryPredicates;" +
                            "NotWholeQueryHitProbability;" + stage.Evidence);
                    return FamilySurvivalProjection.Unresolved(FamilyId,
                        $"PartyStageProbabilityUnavailable:{stage.Evidence}");
                }
                probability *= value;
            }
            return FamilySurvivalProjection.Resolved(FamilyId, probability,
                "PersonalStageSieveProductApproximation;SharedFactsNotDuplicated;" +
                "CrossPlayerPersonalConditionsAssumedIndependent");
        }
        public FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
        internal AutomaticPrivateSerialPricing.PricedOrder? Price(double roots, bool compact,
            IReadOnlySet<string> passedCoverage, GpuCostSnapshot gpuCosts, int rootWindow)
        {
            IFamilyInvocation[] children = _stages.Select(s => s.Family).ToArray();
            bool gpu = children.All(child => child.ConditionPerformance.UsesGpu);
            bool cpuOnly = children.All(child => !child.ConditionPerformance.UsesGpu);
            if (gpu || cpuOnly)
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
                var measured = PartyMeasuredCost.Price(FamilyId, roots, children.Length, gpu,
                    UsesPrivate, children.Min(c => c.AnalyticalCost.InputCapacityPerInvocation),
                    weightedWork, outputRate, workers);
                if (measured is { } local)
                    return new(children, UsesPrivate, local.Ms, outputRate, local.SetupMs,
                        [local.Evidence, $"WeightedSourceWork={weightedWork:G9};InnerUnknownPass=ConservativeOne"]);
            }
            FamilyPhysicalQuote? Quote(IFamilyInvocation child, FamilyPhysicalQuoteRequest geometry)
            {
                if (child is FamilyCpuExecution cpu) return cpu.Quote(geometry, passedCoverage);
                return child.QuotePhysicalWork(geometry) is { } raw
                    ? gpuCosts.Local(child, raw, geometry) : null;
            }
            double? Survival(IFamilyInvocation child, IReadOnlySet<string> _)
            {
                // Unknown inner pass rates retain all input for the following
                // physical stage. This bounds work without inventing selectivity.
                if (FamilyId is "W.World" or "E.EventResult") return 1;
                return child.ResolveSurvival(passedCoverage).SurvivalProbability ?? 1;
            }
            return AutomaticPrivateSerialPricing.Price(roots, children, UsesPrivate,
                Quote, Survival, firstCompactInput: compact, rootWindowOverride: rootWindow,
                initialSetupMs: 0);

            double WorldWork(int act, int events)
            {
                var generation = _request.CompiledSearch.Context.Party?.World.Beta109Generation;
                double bagEntries = generation is null ? 606 : generation.SharedRelicBuckets.Sum(b => b.OrderedRelics.Count) +
                    generation.PartyRelicBuckets.SelectMany(b => b).Sum(b => b.OrderedRelics.Count);
                return Math.Max(.25, bagEntries / 606d) *
                    (1 + Math.Max(0, act - 2) * .5 + events * .25);
            }
        }
        public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact) => new(FamilyId,
            FamilyId + ".Party.20260922.v1." + (UsesPrivate ? "PrivateOrdinal" : "Public") + (compact ? ".CompactAbi1" : ".Dense"),
            "PartyFamilyConjunction", 1, "OneFamily;PersonalAndSharedPredicates;WholeTableExactFinal;BoundedLocalPhysicalQuote",
            usesGpu: _stages.Any(s => s.Family.ConditionPerformance.UsesGpu));

        public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window,
            FamilyCandidateSet input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (window.ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint || input.Batch != window.Batch)
                throw new InvalidOperationException("Party.FamilyObservationMismatch");
            if (input.Count == 0) return input;
            _compact = !input.IsDense;
            var timer = Stopwatch.StartNew();
            FamilyCandidateSet output = input;
            if (UsesPrivate)
            {
                _chain ??= await context.ExecuteGpuAsync(rd => new FamilyPrivateGpuChain(rd, _stages.Select(s => s.Family).ToArray()), token).ConfigureAwait(false);
                output = await context.ExecuteGpuAsync(_ => _chain.Execute(input, token), token).ConfigureAwait(false);
            }
            else foreach (var stage in _stages)
            {
                if (output.Count == 0) break;
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
}
