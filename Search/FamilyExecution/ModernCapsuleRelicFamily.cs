using System.Diagnostics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class ModernCapsuleRelicFamily
{
    internal const string Obligation = "R.Capsule";
    private const string Revision = "R.Relic.Cpu.ModernCapsuleHeads.20261114.v1";

    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu, GpuNumericalVariant numericalVariant, bool includeOrdinary = true)
    {
        body = null;
        if (model.Authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113 ||
            !NeowReplayPlan.HasCapsule(model)) return false;
        NeowReplayPlan route;
        try { route = NeowReplayPlan.Compile(model, true); }
        catch (InvalidOperationException error) when (error.Message is "NeowFamilyReplayAuthorityUnavailable" or "NeowFamilyUnmappedIdentity" or
            "RFamilyUnboundCapsuleRoute_ExactOnly" or "RFamilyCapsulePredicateUnsupported_ExactOnly")
        {
            return false;
        }
        if (ModernCapsuleRelicPlan.TryCompile(model, route) is not { } plan) return false;
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        int slot = model.Authority.PlayerSlotIndex;
        var preparation = new PreparedFilter(RelicFamily.Id)
        {
            Coverage = [Obligation],
            Obligations = [new(Obligation, slot)],
            Remainders = plan.HasUnresolvedPickup ? [Obligation + ":UnresolvedPickupContinuationRetainedForExact"] : [],
            AnalyticalCost = new(RelicFamily.Id, CpuFilterExecution.Capacity, [], "ModernCapsuleHeadWorkUnpriced"),
            Survival = FamilySurvivalProjection.Unresolved(RelicFamily.Id, "ModernCapsuleSelectionProbabilityUnpriced")
        };
        var capsule = new FilterBodyDescription(preparation, Revision, profile.ProfileId)
        {
            InputIdentity = model.SnapshotFingerprint,
            PlayersCount = model.Authority.PlayersCount,
            CpuWorkers = workers,
            WindowCapacity = CpuFilterExecution.Capacity,
            CpuChunkCapacity = CpuFilterExecution.Capacity,
            Cost = new() { PartyChild = new(AllowsHistoricalEnvelope: false) },
            PerformanceProjection = compact => CpuFilterExecution.Describe(RelicFamily.Id, Revision,
                workers, CpuFilterExecution.Capacity, true, false, compact),
            CreateExecution = (self, _) => new CpuFilterExecution(self, profile, plan.Matches, numericalRoots: true)
        };
        RelicPreparation? ordinary = null;
        if (includeOrdinary) RelicFamily.TryPrepare(model, preferences, out ordinary, preferGpu, numericalVariant);
        var cpu = ordinary is null ? capsule : Compose(ordinary.Body.UsesGpu ? ordinary.Body.CpuAlternatives.Single() : ordinary.Body, capsule);
        if (preferGpu && plan.PackGpu(numericalVariant) is { } gpu)
            body = PrepareGpu(model, profile, ordinary?.Plan, plan, gpu, cpu);
        else
            body = ordinary?.Body.UsesGpu == true ? Compose(ordinary.Body, capsule) : cpu;
        return true;
    }

    private static FilterBodyDescription PrepareGpu(SearchModel model, IRuntimeProfile profile, RelicFamilyPlan? ordinary,
        ModernCapsuleRelicPlan plan, ModernCapsuleGpuPlan gpu, FilterBodyDescription cpu)
    {
        var sequence = ordinary ?? new RelicFamilyPlan(plan.Pool, [], [], [], new byte[4], new byte[4], [],
            new int[4], new byte[4], 0, false);
        gpu = gpu.WithOrdinarySequence(sequence);
        string identity = model.SnapshotFingerprint;
        bool native = gpu.NumericalVariant == GpuNumericalVariant.NativeFloat64;
        return new(cpu.Preparation, gpu.Revision(false), profile.ProfileId)
        {
            UsesGpu = true,
            NumericRequirements = GpuIntegerNumerics.Requirements(gpu.NumericalVariant),
            WindowCapacity = RelicFamilyGpuExecutor.Capacity,
            PreferredRootWindowSize = RelicFamilyGpuExecutor.Capacity,
            PlayersCount = cpu.PlayersCount,
            CpuWorkers = cpu.CpuWorkers,
            InputIdentity = identity,
            CpuAlternatives = [cpu],
            Cost = new()
            {
                PartyChild = new(AllowsHistoricalEnvelope: false),
                Quote = (_, geometry, _) => RelicWorkEstimate.ModernShopFirstCapsule(sequence, gpu, geometry)
            },
            PerformanceProjection = compact => new(RelicFamily.Id, gpu.Revision(compact),
                "ModernCapsule.EligibleLaneHeads", 1, "IndependentAbi1;PublicWorkUnpriced", true),
            CreateExecution = (self, _) => new GpuFilterExecution<RelicFamilyGpuExecutor>(self,
                device => RelicFamilyGpuExecutor.Create(device, identity, sequence, gpu),
                (resource, input, token) =>
                {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalAbi1ReadyMs, metrics.CommandMs + metrics.SubmitMs + metrics.SyncMs,
                        metrics.ReadbackMs, metrics.ReadbackBytes);
                },
                resource => resource.Dispose(), Reference(ordinary, plan), profile,
                resource => (resource.DeviceName, resource.SetupMs), 256, "ModernCapsuleGpuFault.Dispose"),
            BindPrivate = native ? (device, input, output) =>
                RelicFamilyGpuExecutor.Create(device, identity, sequence, gpu, input, output).BindPrivateSerial() : null
        };
    }

    private static Func<ulong, bool> Reference(RelicFamilyPlan? ordinary, ModernCapsuleRelicPlan capsule)
    {
        if (ordinary is null) return capsule.Matches;
        if (RelicShopCpuPlan.TryCompile(ordinary) is { } shop) return root => shop.Matches(root) && capsule.Matches(root);
        var relics = RelicCpuPlan.TryCompile(ordinary) ?? throw new InvalidOperationException("R.ModernCapsuleOrdinaryMatcherMissing");
        return root => relics.Matches(root) && capsule.Matches(root);
    }

    private static FilterBodyDescription Compose(FilterBodyDescription ordinary, FilterBodyDescription capsule)
    {
        var preparation = capsule.Preparation with
        {
            Coverage = [..ordinary.Coverage, ..capsule.Coverage],
            Obligations = [..ordinary.Preparation.EffectiveObligations, ..capsule.Preparation.EffectiveObligations]
        };
        string revision = ordinary.PhysicalRevision + "+" + Revision;
        return new(preparation, revision, ordinary.ProfileId)
        {
            UsesGpu = ordinary.UsesGpu,
            NumericRequirements = ordinary.NumericRequirements,
            WindowCapacity = ordinary.WindowCapacity,
            PreferredRootWindowSize = ordinary.PreferredRootWindowSize,
            CpuChunkCapacity = capsule.CpuChunkCapacity,
            CpuWorkers = capsule.CpuWorkers,
            PlayersCount = capsule.PlayersCount,
            InputIdentity = capsule.InputIdentity,
            CpuAlternatives = ordinary.CpuAlternatives.Select(cpu => Compose(cpu, capsule)).ToArray(),
            PhysicalBodies = [ordinary, capsule],
            Cost = capsule.Cost,
            PerformanceProjection = compact => new(RelicFamily.Id,
                ordinary.ResolveConditionPerformance(compact).PhysicalImplementationRevision + "+" +
                capsule.ResolveConditionPerformance(true).PhysicalImplementationRevision,
                "ModernCapsuleWithInitialRelicSequence", 1, "OrderedPublicAbi1BetweenOrdinaryRelicsAndCapsule",
                ordinary.UsesGpu),
            CreateExecution = (self, services) => new Execution(self, ordinary, capsule, services)
        };
    }

    private sealed class Execution : IFilterExecution
    {
        public FilterBodyDescription Body { get; }
        private readonly IFilterExecution _ordinary, _capsule;
        public (string Device, double SetupMs) ResourceInfo => _ordinary.ResourceInfo;
        public bool ExecutionFaulted => _ordinary.ExecutionFaulted || _capsule.ExecutionFaulted;

        internal Execution(FilterBodyDescription body, FilterBodyDescription ordinary, FilterBodyDescription capsule,
            FilterRuntimeServices services)
        {
            Body = body;
            var passed = services.CoverageFor(body);
            services.BindCoverage(ordinary, passed);
            services.BindCoverage(capsule, passed);
            _ordinary = ordinary.CreateExecution!(ordinary, services);
            _capsule = capsule.CreateExecution!(capsule, services);
        }

        public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            try
            {
                var ordinary = await _ordinary.FilterAsync(services, input, token).ConfigureAwait(false);
                var output = ordinary.Count == 0 ? ordinary :
                    await _capsule.FilterAsync(services, ordinary, token).ConfigureAwait(false);
                services.Costs.ObserveCompleted(Body, input, output.Count, watch.Elapsed.TotalMilliseconds,
                    passedCoverage: services.CoverageFor(Body));
                return output;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Child timings have their own identity. Invalidate this whole
                // public invocation too, without replacing the original failure.
                services.Costs.Invalidate(Body);
                throw;
            }
        }

        public async ValueTask DisposeAsync(FilterRuntimeServices services)
        {
            try { await _capsule.DisposeAsync(services).ConfigureAwait(false); }
            finally { await _ordinary.DisposeAsync(services).ConfigureAwait(false); }
        }
    }
}
