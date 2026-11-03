using System.Runtime.CompilerServices;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;

// Existing Host probes repeat one selected numerical body. Keep that execution
// lifetime explicit here, independently of its immutable preparation and price.
// No query, observation window or replacement numerical oracle is introduced.
internal static class HostFilterBindings
{
    private sealed class Bound(FilterBodyDescription body, FilterRuntimeServices services)
    {
        internal readonly FilterRuntimeServices Services = services;
        internal readonly IFilterExecution Execution = (body.CreateExecution ??
            throw new InvalidOperationException("Host.FilterExecutionFactoryMissing:" + body.FamilyId))(body, services);
    }
    private sealed class Scope
    {
        internal readonly Dictionary<FilterBodyDescription, Bound> Bodies = new(ReferenceEqualityComparer.Instance);
    }
    private static readonly ConditionalWeakTable<FilterRuntimeServices, Scope> Scopes = new();
    private static readonly ConditionalWeakTable<FilterBodyDescription, Bound> Latest = new();
    internal static IFilterExecution Bind(FilterBodyDescription body, FilterRuntimeServices services)
    {
        var scope = Scopes.GetOrCreateValue(services);
        lock (scope.Bodies) {
            if (!scope.Bodies.TryGetValue(body, out var bound)) scope.Bodies.Add(body, bound = new(body, services));
            Latest.AddOrUpdate(body, bound);
            return bound.Execution;
        }
    }
    internal static ValueTask<FamilyCandidateSet> ExecuteAsync(this FilterBodyDescription body,
        FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token) =>
        Bind(body, services).FilterAsync(services, input, token);
    internal static IFilterExecution[] BindAll(IEnumerable<FilterBodyDescription> bodies, FilterRuntimeServices services) =>
        bodies.Select(body => Bind(body, services)).ToArray();
    internal static NeowPreparedBody PrepareNeow(PreparedSearch run) =>
        NeowFamily.TryPrepare(run.Model, FilterExecutionPreferences.From(run.RunOptions), out var prepared) && prepared is not null
            ? prepared : throw new InvalidDataException("Host.NeowPreparationUnavailable");
    internal static (int Slot, FilterBodyDescription Family)[] PartyStages(FilterBodyDescription body) =>
        body.PhysicalBodies.Select(child => (Slot: child.Preparation.Obligations
            .Select(obligation => obligation.PlayerSlot).FirstOrDefault(slot => slot.HasValue) ??
            throw new InvalidDataException("Host.PartyStageSlotMissing:" + child.FamilyId), Family: child)).ToArray();
    internal static (SearchModel Request, FilterBodyDescription Family, int Slot)[] PartyModels(
        PreparedSearch parent, FilterBodyDescription body) => PartyStages(body).Select(stage =>
            (SearchPreparationCompiler.ForPlayer(parent.Model, parent.Model.CompiledSearch.PlayerSearches[stage.Slot]),
                stage.Family, stage.Slot)).ToArray();
    internal static CombatRewardPreparation PartyCombat(PreparedSearch parent, int slot, bool gpu)
    {
        int? niche = 0, potions = 0;
        var prefs = FilterExecutionPreferences.From(parent.RunOptions);
        foreach (var compiled in parent.Model.CompiledSearch.PlayerSearches)
        {
            var child = SearchPreparationCompiler.ForPlayer(parent.Model, compiled);
            if (child.Authority.PlayerSlotIndex == slot)
                return CombatRewardFamily.TryPrepareParty(child, prefs, niche, potions, out var prepared, gpu) && prepared is not null
                    ? prepared : throw new InvalidDataException("Host.PartyCombatPreparationUnavailable");
            if (child.CompiledSearch.NormalizedQuery.OpeningRoute is null) continue;
            var advance = NeowFamily.PartyInitial.SharedAdvance(child, NeowReplayPlan.Compile(child, false));
            niche = niche.HasValue && advance.Niche.HasValue ? niche + advance.Niche : null;
            potions = potions.HasValue && advance.Potions.HasValue ? potions + advance.Potions : null;
        }
        throw new InvalidDataException("Host.PartyCombatSlotMissing");
    }
    internal static ValueTask DisposeAsync(this FilterBodyDescription body, FilterRuntimeServices services) =>
        Scopes.TryGetValue(services, out var scope) && scope.Bodies.TryGetValue(body, out var bound)
            ? bound.Execution.DisposeAsync(services) : ValueTask.CompletedTask;
    internal static bool Faulted(FilterBodyDescription body) => Latest.TryGetValue(body, out var bound) && bound.Execution.ExecutionFaulted;
    internal static (string Device, double SetupMs) ResourceInfo(FilterBodyDescription body) =>
        Latest.TryGetValue(body, out var bound) ? bound.Execution.ResourceInfo : ("", 0);
    internal static FamilyPerformanceObservation CapturePerformanceObservation(this FilterBodyDescription body) =>
        Latest.TryGetValue(body, out var bound)
            ? bound.Services.Costs.CaptureLatest(body, bound.Execution.ResourceInfo.Device, bound.Execution.ResourceInfo.SetupMs)
            : new(body.ConditionPerformance, "", 0, 0, 0, 0, 0, 0, 0, 0, false, "NoActualInvocation");
    internal static FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot(this FilterBodyDescription body) =>
        Latest.TryGetValue(body, out var bound) ? bound.Services.Costs.LiveLatest(body) : null;
    internal static IEnumerable<FilterBodyDescription> ConfiguredCpuBodies(FilterBodyDescription body) =>
        body.CpuAlternatives.Count > 0
            ? body.CpuAlternatives.Where(cpu => cpu.CpuWorkers == Math.Clamp(body.CpuWorkers, 1, 32))
            : body.UsesGpu ? [] : [body];
}
