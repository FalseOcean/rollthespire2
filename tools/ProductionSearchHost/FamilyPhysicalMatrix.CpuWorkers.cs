using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

internal static partial class FamilyPhysicalMatrix
{
    // Host-only controlled trial: change one registered realization, preserving
    // stage order, obligations, window size, Exact budget and session ownership.
    internal static ProductionSearchSession StartCpuWorkerSession(ExactSearchExecutionRequest request,
        string selection, bool cpuOnly)
    {
        string[] parts = selection.Split(':');
        if (!cpuOnly || request.CompiledSearch.Context.Party is not null || parts.Length != 2 ||
            parts[0].Length != 1 || !int.TryParse(parts[1], out int workers) || workers < 1 || workers > request.WorkerCount)
            throw new ArgumentException("CPU worker trial requires solo CPU Only and <Family letter>:<workers within budget>");
        var owners = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable:false);
        var baseline = FamilyExecutionCoordinator.Plan(request, gpuAvailable:false);
        var stages = baseline.OrderedFamilies.ToArray();
        int index = Array.FindIndex(stages, stage => stage.FamilyId.StartsWith(parts[0] + ".", StringComparison.Ordinal));
        if (index < 0 || stages[index] is not FamilyCpuExecution current)
            throw new InvalidOperationException("CPU worker trial requires an existing FamilyCpuExecution stage");
        string body = current.Condition(false).PhysicalImplementationRevision.Split(".RootMode", StringSplitOptions.None)[0];
        var owner = owners.Single(f => f.FamilyId == stages[index].FamilyId);
        var candidate = owner.CpuRealizations.OfType<FamilyCpuExecution>().SingleOrDefault(c => c.Workers == workers &&
            c.Condition(false).PhysicalImplementationRevision.StartsWith(body + ".RootMode", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Requested CPU body/worker configuration is not registered");
        stages[index] = candidate;
        int window = FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, baseline.OrderedFamilies);
        if (FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, stages) != window)
            throw new InvalidOperationException("CPU worker trial changed the observation window");
        var replacement = FamilyPlanner.InOrder(stages);
        // Keep policy/remainders from the Production plan, but do not present the
        // overridden allocation's old price as an estimate. The normal session
        // may collect calibration evidence in the launcher's isolated store.
        var plan = baseline with
        {
            PlanId = replacement.PlanId,
            Stages = replacement.Stages,
            RankingAuthority = FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
            DecisionEvidence = ["HostOnlyCpuWorkerTrial;OrderAndBodyPreserved;NoRepricing"],
            ExpectedFamilyPipelineMsPerRoot = null,
            AllowSmallCandidateExact = false
        };
        Console.WriteLine($"cpuThreadingOverride=true;family={owner.FamilyId};workers={workers};exactBudget={request.WorkerCount};window={window};orderUnchanged=true;revision={candidate.Condition(false).PhysicalImplementationRevision};noRepricing=true");
        ProductionSearchReplay.LogSessionWorkload(request, "cpu-threading-trial");
        return new(request, plan, FamilySearchEtaProjectorV1.Project(request, plan), window, 64, null, "", failOnPhysicalRecovery:true);
    }
}
