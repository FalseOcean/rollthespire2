using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class WorldFamily
{
    // Physical specialization of the existing Family; ABI1 remains ordinals only.
    internal sealed class PartyInitial : IFamilyInvocation
    {
        private readonly FamilyCpuExecution _cpu;
        internal PartyInitial(ExactSearchExecutionRequest request)
        { _cpu = new(request, FamilyId, "W.World.PartyInitial.Cpu.v1", workers: request.WorkerCount, numericalRoots: true); }
        private bool Matches(ulong root)
        {
            // Conservative pass-through: shared W is reconstructed once at Production Exact.
            // No SP World fast plan is substituted for uncaptured MP physical inputs.
            return true;
        }
        public string FamilyId => "W.World";
        public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, FamilyCpuExecution.Capacity, [], "PartyInitialCostUncalibrated");
        public FamilySurvivalProjection Survival => FamilySurvivalProjection.Unresolved(FamilyId, "PartyCorrelationUnknown");
        public FamilyConditionPerformanceProjection ConditionPerformance => _cpu.Condition(false);
        public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact) => _cpu.Condition(compact);
        IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations => [this];
        public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window, FamilyCandidateSet input, CancellationToken token) =>
            new(_cpu.Execute(window, input, token, Matches));
        public FamilyPerformanceObservation CapturePerformanceObservation() => _cpu.Observation();
        public FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => _cpu.Live();
        public ValueTask DisposeAsync(FamilyExecutionContext context) { _cpu.WriteSummary(); return ValueTask.CompletedTask; }
    }
}
