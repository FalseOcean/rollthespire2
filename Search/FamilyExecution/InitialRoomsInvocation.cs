using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// Preparation composes two domain descriptions. The selected runtime owns the
// child executions; probability and complete-call prices never read their state.
internal static class InitialRoomsInvocation
{
    internal const string TreasureCoverage = "R.Relic.InitialTreasure";
    internal const string UnknownCoverage = "W.World.InitialUnknown";

    internal static FilterBodyDescription Prepare(SearchModel model, FilterExecutionPreferences preferences,
        string familyId, InitialRoomsPlan plan, FilterBodyDescription? ordinary, bool preferGpu,
        int? workers = null)
    {
        if (familyId is not ("R.Relic" or "W.World") || ordinary is not null && ordinary.FamilyId != familyId ||
            familyId == "R.Relic" && (!plan.CoversTreasure || plan.CoversUnknownPrefix) ||
            familyId == "W.World" && (!plan.CoversUnknownPrefix || plan.CoversTreasure))
            throw new ArgumentException("InitialRooms.OwnerProjectionMismatch", nameof(plan));
        int count = workers ?? Math.Clamp(preferences.WorkerCount, 1, 32);
        if (!preferGpu && ordinary is { UsesGpu: true })
            ordinary = ordinary.CpuAlternatives.FirstOrDefault(body => body.CpuWorkers == count) ??
                throw new InvalidOperationException("InitialRooms.OrdinaryCpuRealizationUnavailable:" + familyId);
        var probabilityInput = SearchSelectivityInput.From(model);
        var probability = plan.CoversTreasure ? TreasureRelicProbability.Estimate(probabilityInput) :
            FirstActUnknownEventProbability.Estimate(probabilityInput);
        double? localProbability = probability.Probability;
        string localEvidence = probability.EvidenceCode;
        var child = ordinary;
        FamilySurvivalProjection Survival(IReadOnlySet<string> passed)
        {
            if (localProbability is not double local)
                return FamilySurvivalProjection.Unresolved(familyId, "InitialRooms.BaseProbabilityUnavailable;" + localEvidence);
            double prior = 1;
            if (child is not null)
            {
                var observed = child.ResolveSurvival(passed);
                if (observed.SurvivalProbability is not double value)
                    return FamilySurvivalProjection.Unresolved(familyId, "InitialRooms.OrdinaryProbabilityUnavailable;" + observed.Evidence);
                prior = value;
            }
            return FamilySurvivalProjection.Resolved(familyId, prior * local,
                "InitialRooms.DeclaredConditions;OrdinaryAndInitialConditionsAssumedIndependent;" + localEvidence);
        }
        string revision = familyId + (plan.CoversTreasure ? ".InitialTreasure" : ".InitialUnknown") +
            (child is null ? ".Only" : ".WithOrdinary") + ".Public.DeclaredUnknownJuzu.20261029.v2" +
            (preferGpu && plan.NumericalVariant != GpuNumericalVariant.NativeFloat64 ? ".IntegerRne.20261025.v1" : "");
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var localObligations = new List<FilterObligation>();
        if (plan.CoversTreasure) localObligations.Add(new(TreasureCoverage));
        else
        {
            var query = model.CompiledSearch.NormalizedQuery;
            if (query.FirstActUnknownEventPrefixCount > 0)
                localObligations.Add(new(UnknownCoverage, ThroughOrdinal: plan.UnknownPrefixCount));
            localObligations.AddRange(query.Players.Where(p => p.Conditions.FirstActUnknownEventPrefixCount > 0)
                .Select(p => new FilterObligation(UnknownCoverage, p.Slot, plan.UnknownPrefixCount)));
        }
        var prepared = new PreparedFilter(familyId)
        {
            Coverage = (child?.Coverage ?? []).Append(plan.CoversTreasure ? TreasureCoverage : UnknownCoverage)
                .Distinct(StringComparer.Ordinal).ToArray(),
            Obligations = (child?.Preparation.EffectiveObligations ?? []).Concat(localObligations).ToArray(),
            AnalyticalCost = new(familyId, InitialRoomsPlan.Capacity, [], "InitialRoomsAnalyticalWorkUnmodeled"),
            Survival = Survival(new HashSet<string>(StringComparer.Ordinal)),
            HasConditionalProjections = child?.HasConditionalProjections == true, SurvivalResolver = Survival
        };
        var local = Local(prepared, revision, plan, profile, count, preferGpu,
            model.Authority.PlayersCount, model.SnapshotFingerprint);
        var cpus = new List<FilterBodyDescription>();
        if (preferGpu)
        {
            var children = child is null ? new FilterBodyDescription?[] { null } :
                child.UsesGpu ? child.CpuAlternatives.Cast<FilterBodyDescription?>().ToArray() : [child];
            foreach (var cpu in children)
                cpus.Add(Prepare(model, preferences, familyId, plan, cpu, false, cpu?.CpuWorkers ?? count));
        }
        return new(prepared, revision, model.ProfileId)
        {
            UsesGpu = preferGpu || child?.UsesGpu == true,
            WindowCapacity = preferGpu ? InitialRoomsPlan.Capacity : 65536,
            CpuWorkers = count, PlayersCount = model.Authority.PlayersCount, InputIdentity = model.SnapshotFingerprint,
            NumericRequirements = (preferGpu ? plan.NumericRequirements : GpuNumericRequirements.None) |
                (child?.NumericRequirements ?? GpuNumericRequirements.None),
            CpuAlternatives = cpus,
            PhysicalBodies = child is null ? [local] : [child, local],
            PerformanceProjection = compact => new(familyId, revision + (preferGpu ? ".Gpu" : ".Cpu.P" + count) +
                (compact ? ".CompactAbi1" : ".Dense") + ".CanonicalAbi1Ready" + (preferGpu ? GpuRngRotation.Revision : ""),
                "InitialRooms.Neutral.20261024.v1", 1,
                "CompletePublicCall;FiniteReferenceWhenAvailable;ProductionExactFinal", preferGpu),
            Cost = new()
            {
                CanonicalScope = true,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeInitialRooms(familyId, plan),
                OrderedChildBodies = child is null ? null : (compact, roots) => FamilyCanonicalCostShape.DescribeChildBody(child, compact, roots),
                Quote = (self, geometry, _) => Quote(self, child, plan, localProbability, geometry, preferGpu, count)
            },
            CreateExecution = (self, services) => new Execution(self, child, local, services)
        };
    }

    private static FilterBodyDescription Local(PreparedFilter owner, string revision, InitialRoomsPlan plan,
        IRuntimeProfile profile, int workers, bool gpu, int players, string identity)
    {
        var prepared = new PreparedFilter(owner.FamilyId) { Coverage = [plan.CoversTreasure ? TreasureCoverage : UnknownCoverage] };
        return new(prepared, revision + ".LocalCalculation", profile.ProfileId)
        {
            UsesGpu = gpu, WindowCapacity = gpu ? InitialRoomsPlan.Capacity : 65536,
            CpuWorkers = workers, PlayersCount = players, InputIdentity = identity,
            NumericRequirements = gpu ? plan.NumericRequirements : GpuNumericRequirements.None,
            PerformanceProjection = compact => gpu ? new(owner.FamilyId, revision + ".LocalCalculation.Gpu" +
                (compact ? ".CompactAbi1" : ".Dense") + GpuRngRotation.Revision, "InitialRooms.Local", 1, "LocalWorkOnly", true) :
                CpuFilterExecution.Describe(owner.FamilyId, revision + ".Cpu", workers, 65536, true, false, compact),
            Cost = new() { Quote = (_, geometry, passed) => InitialRoomsWorkEstimate.Local(owner.FamilyId, plan, geometry, gpu, workers) },
            CreateExecution = gpu ? (self, services) => new GpuFilterExecution<InitialRoomsGpuExecutor>(self,
                device => new(device, plan), (resource, input, token) =>
                {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalMs, metrics.DispatchSyncMs, metrics.ReadbackMs, metrics.ReadbackBytes);
                }, resource => resource.Dispose(), plan.Reference, profile,
                resource => (resource.Device, resource.SetupMs), paritySample: 256) :
                (self, services) => new CpuFilterExecution(self, profile, plan.Matches, numericalRoots: true)
        };
    }

    private static FamilyPhysicalQuote? Quote(FilterBodyDescription self, FilterBodyDescription? ordinary,
        InitialRoomsPlan plan, double? probability, FamilyPhysicalQuoteRequest geometry, bool gpu, int workers)
    {
        if (FamilyCanonicalMeasuredCost.TryDenseQuote(self, geometry) is { } measured) return measured;
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput || geometry.MeanInputPopulation <= 0)
            return null;
        double inputs = geometry.MeanInputPopulation, priorPass = 1, priorMs = 0;
        FamilyPhysicalQuote? prior = null;
        if (ordinary is not null)
        {
            priorPass = ordinary.Survival.SurvivalProbability is { } p && double.IsFinite(p) && p is >= 0 and <= 1 ? p : 1;
            prior = geometry.ComputationOnly ? FamilyPhysicalQuote.Computation(ordinary, geometry, geometry.CostSnapshot) : ordinary.QuotePhysicalWork(geometry);
            bool fallback = prior is null;
            if (prior is null)
            {
                double work = Math.Max(1, ordinary.AnalyticalCost.WorkUnitsPerInput);
                prior = ordinary.UsesGpu ? FamilyCoarsePricing.Gpu(self.FamilyId, "InitialRooms.OrdinaryWorkEnvelope", .98 + .012 * work,
                    ordinary.AnalyticalCost.InputCapacityPerInvocation, geometry, "OrdinaryOwnerReferenceUnavailable;SourceWorkEnvelope;NoZeroCostSubstitution") :
                    FamilyCoarsePricing.Cpu(self.FamilyId, "InitialRooms.OrdinaryWorkEnvelope", 243 + work,
                        ordinary.CpuWorkers, geometry, "OrdinaryOwnerReferenceUnavailable;SourceWorkEnvelope;NoZeroCostSubstitution");
            }
            if (prior is null) return null;
            if (geometry.ComputationOnly && (fallback || geometry.CostSnapshot is null)) prior = FamilyCoarsePricing.LocalComputation(ordinary, prior, geometry);
            double? value = geometry.ComputationOnly ? FamilyCoarsePricing.ComputationMilliseconds(prior, geometry) :
                FamilyCoarsePricing.PublicMilliseconds(prior, geometry, priorPass);
            if (value is null) return null;
            priorMs = value.Value;
        }
        var localGeometry = geometry with { CompactInput = geometry.CompactInput || ordinary is not null, MeanInputPopulation = inputs * priorPass };
        var local = InitialRoomsWorkEstimate.Local(self.FamilyId, plan, localGeometry, gpu, workers);
        double localPass = probability is { } q && double.IsFinite(q) && q is >= 0 and <= 1 ? q : 1;
        if (local is null) return null;
        if (geometry.ComputationOnly) local = FamilyCoarsePricing.LocalComputation(self, local, localGeometry);
        double? localTime = geometry.ComputationOnly ? FamilyCoarsePricing.ComputationMilliseconds(local, localGeometry) :
            FamilyCoarsePricing.PublicMilliseconds(local, localGeometry, localPass);
        if (localTime is not { } localMs) return null;
        return new(self.FamilyId, geometry.ComputationOnly ? "InitialRooms.OwnedComputationWorkEstimate.20261028.v1" :
            "InitialRooms.OwnedPublicWorkEstimate.20261028.v1", (priorMs + localMs) * 1e6 / inputs,
            gpu ? InitialRoomsPlan.Capacity : 65536, null,
            FamilyCoarsePricing.Revision + ";PriceQuality=Estimate;Model=ReusableCoarse;FamilyLocal;" +
            (geometry.ComputationOnly ? "ComputationOnly;NumericalAndDeviceEmission;NoSubmitOrHostReadback;NoCrossFamilyHandoffCost;" : "CompleteWithinFamilyPublicInvoke;") +
            "OrdinaryOnceThenLocalOnModeledSurvivors;" + $"OrdinaryPass={priorPass:G17};LocalPass={localPass:G17};UnknownPass=ConservativeOne;" +
            "AlreadyCompleteOrdinaryHasNoSecondOutputCharge;NoCrossFamilyConnectionOrExactCost;OrdinaryEvidence=" +
            (prior?.Evidence ?? "None") + ";LocalEvidence=" + local.Evidence,
            OutputElementBytes: 8, OutputAlreadyOrdered: true,
            PublicTransportClass: geometry.ComputationOnly ? gpu ? "DeviceFamilyWork" : "CpuOrderedAbi1" : "CompleteCanonicalAbi1")
        { LocalCostSource = geometry.ComputationOnly ? "ComponentComposition" : "ReferenceEstimate", ReferencePeakThroughput = 0 };
    }

    private sealed class Execution : IFilterExecution
    {
        public FilterBodyDescription Body { get; }
        private readonly IFilterExecution? _ordinary;
        private readonly IFilterExecution _local;
        private bool _faulted;
        private (string Device, double SetupMs) _lastResource = ("", 0);
        public (string Device, double SetupMs) ResourceInfo
        {
            get
            {
                var local = _local.ResourceInfo;
                var ordinary = _ordinary?.ResourceInfo ?? (Device: "", SetupMs: 0d);
                string device = local.Device.Length == 0 ? ordinary.Device :
                    ordinary.Device.Length == 0 || ordinary.Device == local.Device ? local.Device : ordinary.Device + ";" + local.Device;
                if (device.Length != 0 || local.SetupMs + ordinary.SetupMs > 0)
                    _lastResource = (device, local.SetupMs + ordinary.SetupMs);
                return _lastResource;
            }
        }
        public bool ExecutionFaulted => _faulted || _local.ExecutionFaulted || _ordinary?.ExecutionFaulted == true;
        internal Execution(FilterBodyDescription body, FilterBodyDescription? ordinary,
            FilterBodyDescription local, FilterRuntimeServices services)
        {
            Body = body;
            var passed = services.CoverageFor(body);
            if (ordinary is not null) services.BindCoverage(ordinary, passed);
            services.BindCoverage(local, passed);
            _ordinary = ordinary is null ? null : ordinary.CreateExecution is { } create
                ? create(ordinary, services) : throw new InvalidOperationException("InitialRooms.OrdinaryExecutionUnavailable:" + body.FamilyId);
            _local = local.CreateExecution!(local, services);
        }
        public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ExecutionFaulted) throw new InvalidOperationException(Body.FamilyId + ".InitialRooms.InvocationFaulted");
            var timer = Stopwatch.StartNew();
            try
            {
                var current = _ordinary is null ? input : await _ordinary.FilterAsync(services, input, token).ConfigureAwait(false);
                var output = await _local.FilterAsync(services, current, token).ConfigureAwait(false);
                services.Costs.ObserveCompleted(Body, input, output.Count, timer.Elapsed.TotalMilliseconds, passedCoverage: services.CoverageFor(Body));
                return output;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                _faulted = true;
                services.Costs.Invalidate(Body);
                RuntimeLog.TryBackgroundWarning("initialRoomsOwnerFault=true;family=" + Body.FamilyId + ";recovery=None;exception=" + RuntimeLog.SafeJson(error.ToString()));
                throw;
            }
        }
        public async ValueTask DisposeAsync(FilterRuntimeServices services)
        {
            _ = ResourceInfo;
            try { await _local.DisposeAsync(services).ConfigureAwait(false); }
            catch (Exception error) { services.RecordGpuCleanupFailure(error, Body.FamilyId, "InitialRooms.Dispose", services.CorrelationId); }
            if (_ordinary is not null) await _ordinary.DisposeAsync(services).ConfigureAwait(false);
        }
    }
}
