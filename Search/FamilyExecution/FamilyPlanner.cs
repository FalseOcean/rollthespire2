using System.Globalization;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum FamilyPlannerRankingAuthority
{
    HardwareExpectedPlanTime,
    HardwareDenseEntryTimePerRejectedCandidate,
    AnalyticalExpectedPlanWork,
    RegistrationOrderFallback,
    UnpricedPhysicalTrial,
    OfflineAllocationWorkShape
}

internal sealed record FamilyExecutionPlanStage(
    IFamilyInvocation Family,
    IReadOnlyList<string> Coverage,
    bool CompactAbi1Input,
    double ExpectedInputFraction,
    double? Survival,
    double? RejectionProbability,
    double? ExpectedFilteringWorkUnitsPerInput,
    FamilyConditionPerformanceProjection ConditionPerformance,
    FamilyPerformanceReference Performance,
    double? PerInputTimeMs,
    double? ExpectedStageTimeMsPerRoot,
    double? TimePerRejectedCandidateMs,
    double? WorkPerRejectedCandidate,
    int RegistrationIndex);

internal sealed record FamilyExecutionPlan(
    string PlanId,
    string SelectionPolicyId,
    FamilyPlannerRankingAuthority RankingAuthority,
    IReadOnlyList<FamilyExecutionPlanStage> Stages,
    double? ExpectedFamilyPipelineMsPerRoot,
    IReadOnlyList<string> DecisionEvidence,
    IReadOnlyList<string> MissingEvidence)
{
    internal int BoundedPricingOrderCount { get; init; }
    // A registered complete allocation may own a population-dependent offline quote.
    // It closes over immutable modeled facts, never a live invocation or observation.
    // Null preserves Unknown; this is not a generic composition or execution object.
    internal Func<double, double?>? EstimateCanonicalMilliseconds { get; init; }
    internal double? EstimatedSetupMilliseconds { get; init; }
    // Allocation-specific TerminalFastCandidateRate, not QueryHitProbability.
    // StageSurvival lives in the selected stage projections; stronger Composites
    // may have a different terminal rate for the same logical Coverage.
    internal double? EstimatedTerminalSurvival { get; init; }
    internal string CompleteQuoteEvidence { get; init; } = "Unavailable";
    internal IReadOnlyList<IFamilyInvocation> OrderedFamilies => Stages.Select(stage => stage.Family).ToArray();

    internal string FormatSummary()
    {
        string stages = Stages.Count == 0
            ? "DirectProductionExact"
            : string.Join('|', Stages.Select((stage, index) =>
                $"{index + 1}:{stage.Family.FamilyId}" +
                $"[coverage={string.Join(',', stage.Coverage)}" +
                $",inputMode={(stage.CompactAbi1Input ? "CompactAbi1" : "Dense")}" +
                $",expectedInputFraction={Maybe(stage.ExpectedInputFraction)}" +
                $",survival={Maybe(stage.Survival)}" +
                $",rejection={Maybe(stage.RejectionProbability)}" +
                $",physicalRevision={San(stage.Performance.PhysicalImplementationRevision)}" +
                $",performance={Maybe(Positive(stage.Performance.CurrentQueryReference))}" +
                $",performanceSource={stage.Performance.EvidenceSource}" +
                $",perInputMs={Maybe(stage.PerInputTimeMs)}" +
                $",expectedStageMsPerRoot={Maybe(stage.ExpectedStageTimeMsPerRoot)}" +
                $",timePerRejectedCandidateMs={Maybe(stage.TimePerRejectedCandidateMs)}" +
                $",expectedFilteringWorkPerInput={Maybe(stage.ExpectedFilteringWorkUnitsPerInput)}]"));
        return $"planId={PlanId};selectionPolicy={SelectionPolicyId};rankingAuthority={RankingAuthority};" +
               $"expectedFamilyPipelineMsPerRoot={Maybe(ExpectedFamilyPipelineMsPerRoot)};stages={stages};" +
               $"completeQuote={San(CompleteQuoteEvidence)};estimatedSetupMs={Maybe(EstimatedSetupMilliseconds)};" +
               $"estimatedTerminalSurvival={Maybe(EstimatedTerminalSurvival)};" +
               $"decisionEvidence={(DecisionEvidence.Count == 0 ? "none" : string.Join('|', DecisionEvidence.Select(San)))};" +
               $"missingEvidence={(MissingEvidence.Count == 0 ? "none" : string.Join('|', MissingEvidence.Select(San)))};" +
               $"firstCandidateEvaluation=true;permutationEnumeration={(BoundedPricingOrderCount > 0 ? "bounded" : "false")};boundedPricingOrders={BoundedPricingOrderCount};searchAdmissionAffected=false";
    }

    private static double? Positive(double value) => value > 0d && double.IsFinite(value) ? value : null;
    private static string Maybe(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? value.Value.ToString("G17", CultureInfo.InvariantCulture)
        : "unavailable";
    private static string San(string value) => string.IsNullOrWhiteSpace(value)
        ? "none"
        : value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}

/// <summary>
/// Prices the actual Dense entry or downstream Compact physical implementation.
/// Every possible first Family is evaluated, while each remaining independent compact
/// tail is sorted by its exchange-optimal time/rejection ratio. No permutations are enumerated.
/// </summary>
internal static class FamilyPlanner
{
    internal const string SelectionPolicyId = "FamilyPlanner.ExpectedPhysicalPlanTime.20260907.v3";

    internal static FamilyExecutionPlan InOrder(IReadOnlyList<IFamilyInvocation> families) =>
        BuildPlan(BuildCandidates(families), FamilyPlannerRankingAuthority.OfflineAllocationWorkShape,
            ["RegisteredEquivalentAllocation"]);

    // Two explicit complete allocations; no set-cover search or new topology.
    // Never compare plans that omit or add logical Coverage.
    internal static FamilyExecutionPlan SelectEquivalentAllocations(FamilyExecutionPlan composite, FamilyExecutionPlan baseline)
    {
        var coverage = composite.Stages.SelectMany(s => s.Coverage).ToHashSet(StringComparer.Ordinal);
        if (!coverage.SetEquals(baseline.Stages.SelectMany(s => s.Coverage)))
            throw new InvalidOperationException("PhysicalAllocationCoverageMismatch");
        bool priced = composite.ExpectedFamilyPipelineMsPerRoot.HasValue && baseline.ExpectedFamilyPipelineMsPerRoot.HasValue;
        var selected = priced && baseline.ExpectedFamilyPipelineMsPerRoot < composite.ExpectedFamilyPipelineMsPerRoot ? baseline : composite;
        string evidence = priced ? "EquivalentCoverageAllocationByPhysicalTime" : "EquivalentCoverageResidentComposite_UnmeasuredPhysicalTrial";
        return selected with { DecisionEvidence = selected.DecisionEvidence.Concat(new[] {
            evidence, "CompositePredictionMsPerRoot=" + Maybe(composite.ExpectedFamilyPipelineMsPerRoot),
            "BaselinePredictionMsPerRoot=" + Maybe(baseline.ExpectedFamilyPipelineMsPerRoot) }).ToArray() };
    }

    private sealed record Candidate(
        IFamilyInvocation Family,
        string[] Coverage,
        double? Survival,
        double? Rejection,
        double? DenseWorkPerInput,
        double? CompactWorkPerInput,
        FamilyConditionPerformanceProjection DenseCondition,
        FamilyConditionPerformanceProjection CompactCondition,
        FamilyPerformanceReference DensePerformance,
        FamilyPerformanceReference CompactPerformance,
        double? DensePerInputMs,
        double? CompactPerInputMs,
        int RegistrationIndex);

    internal static FamilyExecutionPlan Plan(IReadOnlyList<IFamilyInvocation> registeredFamilies)
    {
        ArgumentNullException.ThrowIfNull(registeredFamilies);
        if (registeredFamilies.Count == 0)
        {
            return new FamilyExecutionPlan(
                "FamilyPlan.v2.DirectProductionExact", SelectionPolicyId,
                FamilyPlannerRankingAuthority.RegistrationOrderFallback,
                [], 0d, ["NoFamilyCoverageRequired"], []);
        }

        Candidate[] candidates = BuildCandidates(registeredFamilies);
        if (registeredFamilies.Any(family => family.HasConditionalProjections))
            return PlanConditional(candidates);
        var decision = new List<string>();
        FamilyPlannerRankingAuthority authority;
        Candidate[] ordered;

        if (TrySelectCompleteHardwarePlan(candidates, out ordered, out double hardwareMsPerRoot))
        {
            authority = FamilyPlannerRankingAuthority.HardwareExpectedPlanTime;
            decision.Add("CompletePlanExpectedPhysicalMsPerRoot=" + F(hardwareMsPerRoot));
        }
        else if (candidates.All(candidate => Score(candidate.DensePerInputMs, candidate.Rejection).HasValue))
        {
            authority = FamilyPlannerRankingAuthority.HardwareDenseEntryTimePerRejectedCandidate;
            Candidate first = candidates
                .OrderBy(candidate => Score(candidate.DensePerInputMs, candidate.Rejection))
                .ThenBy(candidate => candidate.RegistrationIndex)
                .First();
            ordered = [first, .. OrderCompactTail(candidates.Where(candidate => candidate != first).ToArray())];
            decision.Add("CompleteCompactPlanEvidenceUnavailable_DenseEntryDecision");
            decision.AddRange(candidates.Select(candidate =>
                $"DenseEntryScore:{candidate.Family.FamilyId}={Maybe(Score(candidate.DensePerInputMs, candidate.Rejection))}"));
        }
        else if (TrySelectCompleteAnalyticalPlan(candidates, out ordered, out double workPerRoot))
        {
            authority = FamilyPlannerRankingAuthority.AnalyticalExpectedPlanWork;
            decision.Add("CompletePlanExpectedAnalyticalWorkPerRoot=" + F(workPerRoot));
        }
        else if (TrySelectWorldEventEntry(candidates, out ordered, out string entryEvidence, out bool coldTrial))
        {
            authority = coldTrial ? FamilyPlannerRankingAuthority.UnpricedPhysicalTrial
                : FamilyPlannerRankingAuthority.HardwareExpectedPlanTime;
            decision.Add(entryEvidence);
        }
        else
        {
            authority = FamilyPlannerRankingAuthority.RegistrationOrderFallback;
            ordered = candidates.OrderBy(candidate => candidate.RegistrationIndex).ToArray();
            decision.Add("IncompleteSurvivalCostAndHardwareEvidence_RegistrationOrderPreserved");
        }

        return BuildPlan(ordered, authority, decision);
    }

    // Narrow onboarding escape from the W -> sparse E evidence deadlock. This
    // does not change N/R conditional ordering or general incomplete plans.
    private static bool TrySelectWorldEventEntry(Candidate[] candidates, out Candidate[] ordered,
        out string evidence, out bool coldTrial)
    {
        ordered = []; evidence = ""; coldTrial = false;
        if (candidates.Length != 2) return false;
        var w = candidates.SingleOrDefault(c => c.Family.FamilyId == "W.World");
        var e = candidates.SingleOrDefault(c => c.Family.FamilyId == "E.EventResult");
        if (w is null || e is null || !w.DenseCondition.UsesGpu || !e.DenseCondition.UsesGpu) return false;
        if (!ChooseWorldEventEntry(e.Survival, e.DensePerInputMs, w.DensePerInputMs,
                w.CompactPerInputMs, out evidence, out coldTrial)) return false;
        ordered = [e, w];
        return true;
    }

    internal static bool ChooseWorldEventEntry(double? eventSurvival, double? eventDenseMs,
        double? worldDenseMs, double? worldCompactMs, out string evidence, out bool coldTrial)
    {
        evidence = ""; coldTrial = false;
        if (eventSurvival is not (>= 0d and < 1d) || worldDenseMs is not (> 0d)) return false;
        if (!eventDenseMs.HasValue)
        {
            coldTrial = true;
            evidence = "WorldEventColdDenseTrial;EFirstUnpriced_NotPredictedFaster;" +
                "CollectOwnDenseAndWorldCompactEvidence;NotSurvivalLearning;ReplanNextSession";
            return true;
        }
        if (eventDenseMs is not (> 0d) || (eventSurvival > 0d && worldCompactMs is not (> 0d))) return false;
        // E -> W's filtering time does not require W's terminal survival.
        // W -> E costs AT LEAST W Dense, for every unknown W survival in [0,1].
        // Only choose E on a strict bound; overlapping costs retain the fallback.
        double eFirst = eventDenseMs.Value + (eventSurvival == 0d ? 0d : eventSurvival.Value * worldCompactMs!.Value);
        if (!double.IsFinite(eFirst) || eFirst >= worldDenseMs.Value) return false;
        evidence = "WorldEventEFirstCostBound;EFirstMsPerRoot=" + F(eFirst) +
            ";WFirstLowerBoundMsPerRoot=" + F(worldDenseMs.Value) +
            ";WorldSurvivalUnchanged;NoDenseCompactEvidenceSubstitution";
        return true;
    }

    private static Candidate[] BuildCandidates(IReadOnlyList<IFamilyInvocation> registeredFamilies)
    {
        var costSnapshot = GpuCostCalibration.Capture();
        var candidates = new Candidate[registeredFamilies.Count];
        var covered = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < registeredFamilies.Count; index++)
        {
            IFamilyInvocation family = registeredFamilies[index] ??
                throw new ArgumentException("Registered Family cannot be null.", nameof(registeredFamilies));
            string[] coverage = family.Coverage
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            if (coverage.Length == 0) throw new InvalidOperationException($"FamilyCoverageEmpty:{family.FamilyId}");
            foreach (string domain in coverage)
            {
                if (!covered.Add(domain))
                    throw new InvalidOperationException($"FamilyCoverageOverlapRequiresExplicitAlternativeSelection:{domain}");
            }

            double? survival = family.Survival.SurvivalProbability;
            double? rejection = survival.HasValue ? 1d - survival.Value : null;
            FamilyExpectedFilteringCostProjection? expectedCost = family.ExpectedFilteringCost;
            double? denseWork = expectedCost?.ExpectedFilteringWorkUnitsPerInput;
            double? compactWork = denseWork.HasValue
                ? denseWork.Value + expectedCost!.CompactInputWorkUnitsPerInput
                : null;
            FamilyConditionPerformanceProjection denseCondition = family.ResolveConditionPerformance(false);
            FamilyConditionPerformanceProjection compactCondition = family.ResolveConditionPerformance(true);
            FamilyPerformanceReference densePerformance = costSnapshot.Reference(family, denseCondition, false);
            FamilyPerformanceReference compactPerformance = costSnapshot.Reference(family, compactCondition, true);
            candidates[index] = new Candidate(
                family, coverage, survival, rejection, denseWork, compactWork,
                denseCondition, compactCondition, densePerformance, compactPerformance,
                FamilyPerformanceProjectionMath.PerInputMilliseconds(densePerformance),
                FamilyPerformanceProjectionMath.PerInputMilliseconds(compactPerformance), index);
        }
        return candidates;
    }

    // A small subset DP over Coverage, not a new execution topology. S/R's
    // accepted independent fast ordering path above remains unchanged.
    private static FamilyExecutionPlan PlanConditional(Candidate[] candidates)
    {
        if (candidates.Length > 8)
            return BuildPlan(candidates, FamilyPlannerRankingAuthority.RegistrationOrderFallback,
                ["ConditionalCoveragePlanningBoundExceeded_RegistrationOrderPreserved"]);
        int complete = (1 << candidates.Length) - 1;
        IReadOnlySet<string> Coverage(int mask) => candidates
            .Where((_, index) => (mask & (1 << index)) != 0)
            .SelectMany(candidate => candidate.Coverage).ToHashSet(StringComparer.Ordinal);
        foreach (bool hardware in new[] { true, false })
        {
            var memo = new Dictionary<int, (double Cost, int Next)>();
            double Solve(int mask)
            {
                if (mask == complete) return 0d;
                if (memo.TryGetValue(mask, out var cached)) return cached.Cost;
                double best = double.PositiveInfinity;
                int next = -1;
                IReadOnlySet<string> passed = Coverage(mask);
                for (int index = 0; index < candidates.Length; index++)
                {
                    if ((mask & (1 << index)) != 0) continue;
                    Candidate candidate = candidates[index];
                    double? survival = candidate.Family.ResolveSurvival(passed).SurvivalProbability;
                    FamilyExpectedFilteringCostProjection? expected = candidate.Family.ResolveExpectedFilteringCost(passed);
                    double? work = expected?.ExpectedFilteringWorkUnitsPerInput;
                    double? cost = hardware
                        ? (mask == 0 ? candidate.DensePerInputMs : candidate.CompactPerInputMs)
                        : work + (mask == 0 ? 0d : expected?.CompactInputWorkUnitsPerInput);
                    if (!survival.HasValue || !cost.HasValue) continue;
                    double tail = Solve(mask | (1 << index));
                    if (!double.IsFinite(tail)) continue;
                    double total = cost.Value + survival.Value * tail;
                    if (total < best) { best = total; next = index; }
                }
                memo[mask] = (best, next);
                return best;
            }
            if (!double.IsFinite(Solve(0))) continue;
            var order = new List<Candidate>(candidates.Length);
            for (int mask = 0; mask != complete;)
            {
                int next = memo[mask].Next;
                order.Add(candidates[next]);
                mask |= 1 << next;
            }
            return BuildPlan(order.ToArray(), hardware
                ? FamilyPlannerRankingAuthority.HardwareExpectedPlanTime
                : FamilyPlannerRankingAuthority.AnalyticalExpectedPlanWork,
                ["ConditionalCoverageSubsetPricing;IndependentReplayDoesNotImplyProbabilisticIndependence"]);
        }
        return BuildPlan(candidates, FamilyPlannerRankingAuthority.RegistrationOrderFallback,
            ["ConditionalProjectionOrPerformanceUnavailable_RegistrationOrderPreserved"]);
    }

    private static bool TrySelectCompleteHardwarePlan(
        Candidate[] candidates,
        out Candidate[] ordered,
        out double expectedMsPerRoot)
    {
        ordered = [];
        expectedMsPerRoot = 0d;
        if (candidates.Any(candidate => !candidate.Survival.HasValue || !candidate.DensePerInputMs.HasValue ||
                                        !candidate.CompactPerInputMs.HasValue))
            return false;

        Candidate[]? best = null;
        double bestTime = double.PositiveInfinity;
        foreach (Candidate first in candidates)
        {
            Candidate[] tail = candidates.Where(candidate => candidate != first)
                .OrderBy(candidate => Score(candidate.CompactPerInputMs, candidate.Rejection))
                .ThenBy(candidate => candidate.RegistrationIndex)
                .ToArray();
            Candidate[] plan = [first, .. tail];
            double time = ExpectedPlanValue(
                plan,
                candidate => candidate.DensePerInputMs!.Value,
                candidate => candidate.CompactPerInputMs!.Value);
            if (time < bestTime)
            {
                bestTime = time;
                best = plan;
            }
        }
        ordered = best!;
        expectedMsPerRoot = bestTime;
        return true;
    }

    private static bool TrySelectCompleteAnalyticalPlan(
        Candidate[] candidates,
        out Candidate[] ordered,
        out double expectedWorkPerRoot)
    {
        ordered = [];
        expectedWorkPerRoot = 0d;
        if (candidates.Any(candidate => !candidate.Survival.HasValue || !candidate.DenseWorkPerInput.HasValue ||
                                        !candidate.CompactWorkPerInput.HasValue))
            return false;

        Candidate[]? best = null;
        double bestWork = double.PositiveInfinity;
        foreach (Candidate first in candidates)
        {
            Candidate[] tail = candidates.Where(candidate => candidate != first)
                .OrderBy(candidate => Score(candidate.CompactWorkPerInput, candidate.Rejection))
                .ThenBy(candidate => candidate.RegistrationIndex)
                .ToArray();
            Candidate[] plan = [first, .. tail];
            double work = ExpectedPlanValue(
                plan,
                candidate => candidate.DenseWorkPerInput!.Value,
                candidate => candidate.CompactWorkPerInput!.Value);
            if (work < bestWork)
            {
                bestWork = work;
                best = plan;
            }
        }
        ordered = best!;
        expectedWorkPerRoot = bestWork;
        return true;
    }

    private static Candidate[] OrderCompactTail(Candidate[] tail)
    {
        if (tail.All(candidate => Score(candidate.CompactPerInputMs, candidate.Rejection).HasValue))
            return tail.OrderBy(candidate => Score(candidate.CompactPerInputMs, candidate.Rejection))
                .ThenBy(candidate => candidate.RegistrationIndex).ToArray();
        if (tail.All(candidate => Score(candidate.CompactWorkPerInput, candidate.Rejection).HasValue))
            return tail.OrderBy(candidate => Score(candidate.CompactWorkPerInput, candidate.Rejection))
                .ThenBy(candidate => candidate.RegistrationIndex).ToArray();
        return tail.OrderBy(candidate => candidate.RegistrationIndex).ToArray();
    }

    private static FamilyExecutionPlan BuildPlan(
        Candidate[] ordered,
        FamilyPlannerRankingAuthority authority,
        IReadOnlyList<string> decision)
    {
        var missing = new List<string>();
        var stages = new FamilyExecutionPlanStage[ordered.Length];
        double inputFraction = 1d;
        double expectedPipelineMsPerRoot = 0d;
        bool completeTime = true;
        var passedCoverage = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < ordered.Length; index++)
        {
            Candidate candidate = ordered[index];
            FamilySurvivalProjection survival = candidate.Family.ResolveSurvival(passedCoverage);
            FamilyExpectedFilteringCostProjection? expected = candidate.Family.ResolveExpectedFilteringCost(passedCoverage);
            candidate = candidate with
            {
                Survival = survival.SurvivalProbability,
                Rejection = survival.SurvivalProbability.HasValue ? 1d - survival.SurvivalProbability.Value : null,
                DenseWorkPerInput = expected?.ExpectedFilteringWorkUnitsPerInput,
                CompactWorkPerInput = expected?.ExpectedFilteringWorkUnitsPerInput + expected?.CompactInputWorkUnitsPerInput
            };
            bool compact = index > 0;
            FamilyConditionPerformanceProjection condition = compact ? candidate.CompactCondition : candidate.DenseCondition;
            FamilyPerformanceReference performance = compact ? candidate.CompactPerformance : candidate.DensePerformance;
            double? perInputMs = compact ? candidate.CompactPerInputMs : candidate.DensePerInputMs;
            double? expectedWork = compact ? candidate.CompactWorkPerInput : candidate.DenseWorkPerInput;
            double? expectedStageMs = perInputMs.HasValue && double.IsFinite(inputFraction)
                ? inputFraction * perInputMs.Value
                : null;
            if (expectedStageMs.HasValue) expectedPipelineMsPerRoot += expectedStageMs.Value;
            else completeTime = false;

            if (!candidate.Survival.HasValue) missing.Add($"FamilySurvivalUnavailable:{candidate.Family.FamilyId}");
            if (!perInputMs.HasValue)
                missing.Add($"FamilyHardwarePerformanceUnavailable:{candidate.Family.FamilyId}:{(compact ? "CompactAbi1" : "Dense")}");
            if (!expectedWork.HasValue) missing.Add($"FamilyExpectedFilteringCostUnavailable:{candidate.Family.FamilyId}");
            stages[index] = new FamilyExecutionPlanStage(
                candidate.Family, candidate.Coverage, compact, inputFraction,
                candidate.Survival, candidate.Rejection, expectedWork, condition, performance,
                perInputMs, expectedStageMs, Score(perInputMs, candidate.Rejection),
                Score(expectedWork, candidate.Rejection), candidate.RegistrationIndex);
            if (candidate.Survival.HasValue) inputFraction *= candidate.Survival.Value;
            else inputFraction = double.NaN;
            passedCoverage.UnionWith(candidate.Coverage);
        }

        string planId = "FamilyPlan.v2." + string.Join('>', stages.Select(stage =>
            stage.Family.FamilyId + "@" + stage.Performance.PhysicalImplementationRevision));
        return new FamilyExecutionPlan(
            planId, SelectionPolicyId, authority, stages,
            completeTime ? expectedPipelineMsPerRoot : null,
            decision,
            missing.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    private static double ExpectedPlanValue(
        Candidate[] plan,
        Func<Candidate, double> firstSelector,
        Func<Candidate, double> compactSelector)
    {
        double inputFraction = 1d;
        double total = 0d;
        for (int index = 0; index < plan.Length; index++)
        {
            Candidate candidate = plan[index];
            total += inputFraction * (index == 0 ? firstSelector(candidate) : compactSelector(candidate));
            inputFraction *= candidate.Survival!.Value;
        }
        return total;
    }

    private static double? Score(double? cost, double? rejection) =>
        cost.HasValue && rejection is > 0d
            ? cost.Value / rejection.Value
            : rejection == 0d && cost.HasValue ? double.PositiveInfinity : null;

    private static string Maybe(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? F(value.Value)
        : "unavailable";
    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
