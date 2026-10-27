from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


contracts = read("Search/FamilyExecution/FamilyExecutionContracts.cs")
planner = read("Search/FamilyExecution/FamilyPlanner.cs")
eta = read("Search/FamilyExecution/FamilySearchEta.cs")
session = read("Search/Runtime/ProductionSearchSession.cs")
controller = read("Ui/Controllers/SearchPageController.cs")
s_family = read("Search/FamilyExecution/MerchantShopColorlessFamily.cs")
s_policy = read("Search/FamilyExecution/MerchantShopColorlessCompactionPolicy.cs")
performance = read("Search/FamilyExecution/FamilyPerformanceFoundation.cs")

# Planner consumes Family-owned projections and the selected physical-performance reference.
require("IReadOnlyList<string> Coverage => [FamilyId]" in contracts,
        "Family/fused-Family Coverage seam missing")
require("family.ResolveConditionPerformance(false)" in planner and
        "family.ResolveConditionPerformance(true)" in planner,
        "Planner must resolve Dense and Compact physical performance independently")
require("costSnapshot.Reference(family, denseCondition, false)" in planner and
        "costSnapshot.Reference(family, compactCondition, true)" in planner,
        "Planner must consume both physical implementation performance references")
require("family.Survival.SurvivalProbability" in planner,
        "Planner must consume Family Survival")
require("expectedCost?.ExpectedFilteringWorkUnitsPerInput" in planner,
        "Planner must consume existing Expected Filtering Cost")
require("FamilyPerformanceProjectionMath.PerInputMilliseconds" in planner and
        "1000d / candidatesPerSecond" in performance,
        "Planner per-input time must use the shared physical-throughput projection")
require("cost.Value / rejection.Value" in planner,
        "Planner must rank by time per rejected candidate")
require("TrySelectCompleteHardwarePlan" in planner and "ExpectedPlanValue" in planner,
        "Planner must compare complete expected physical plans")
require("HardwareDenseEntryTimePerRejectedCandidate" in planner,
        "Planner must degrade to Dense entry evidence when Compact evidence is incomplete")
require("GetPermutations" not in planner and "Permutation(" not in planner,
        "Planner must not enumerate Family permutations")

# S keeps ownership of the physical selector and the frozen one-percent boundary.
require("resolved >= StableThreshold" in s_policy,
        "S analytical Survival selector boundary moved out of S")
require("StableThreshold = 0.01d" in s_policy,
        "S Stable/Atomic threshold changed")
require("FamilyPlanner" not in s_family and "FamilyPlanner" not in s_policy,
        "S must not depend on Planner")

# ETA rarity is Query-wide. Family Survival only folds downstream population.
require("JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request))" in eta,
        "ETA must consume Query-wide probability authority")
require("double output = population * survival" in eta,
        "ETA must fold Family Survival into downstream population")
require("FamilyPerformanceProjectionMath.ExpectedMilliseconds(population, reference.Value)" in eta,
        "ETA Family wall time must share Planner's expected input / selected throughput projection")
require("stage.Performance.PhysicalImplementationRevision" in eta and
        "FamilyPerformanceEvidenceSource.Live" in eta,
        "live ETA must refine only the selected physical implementation")
require("TryGetExactTiming(exactFamily)" in eta and
        "AverageExactMsPerAttempt / Math.Max(1, request.WorkerCount)" in eta,
        "ETA must use timing-only Production Exact evidence")
require("Math.Max(familyPipelineMs, exactTailMs.Value)" in eta,
        "steady-state Family/Exact pipeline must compose by max, not sum")
require("plan.EstimatedSetupMilliseconds ?? 0d" in eta and "runtimeSurvivalLearning=false" in eta,
        "Complete allocation ETA includes separately quoted setup without runtime rarity learning")
require("request.ScanCount" in eta and "FixedScanRangeMs" in eta,
        "fixed scan-range degradation missing")

# Production preview and execution must use the same Family-world decision.
require("FamilyExecutionCoordinator.Plan(execution.Plan" in controller,
        "Search preview must use Family Planner")
require("FamilySearchEtaProjectorV1.Project(execution.Plan, familyPlan)" in controller,
        "Search preview must use Family-world ETA")
require("FamilyExecutionPlan executionPlan = Plan(plan, gpuAvailable)" in read("Search/FamilyExecution/FamilyExecutionCoordinator.cs"),
        "Production start must run the Planner decision")
require("_families = executionPlan.OrderedFamilies" in session,
        "runtime Family order must come from the selected plan")
require("stage.ConditionPerformance.FormatSummary()" in session and
        "stage.Performance.FormatSummary()" in session,
        "runtime diagnostics must report the selected position-specific performance")
require("SubmitExactTimingSample" in session and "acceptanceIgnored=true" in session,
        "Family runtime must publish timing-only Exact evidence")
require("GetEtaProjection(bool allowLivePerformance = true)" in session and
        "GetEtaProjection(bool allowLivePerformance = true) => _etaProjection" in session and
        "GetLiveEtaEvidenceRevision() => 0" in session,
        "Family session must freeze ETA evidence; same-run timings are diagnostics only")
require("familySearchEtaLive=true" in controller and "familyOrderChanged=false" in controller,
        "live ETA diagnostics must state that planning authorities are unchanged")

s_survival = 0.1467848
r_survival = 2.269e-9
s_score = (1000.0 / 558_000_000.0) / (1.0 - s_survival)
r_score = (1000.0 / 807_600_000.0) / (1.0 - r_survival)
require(r_score < s_score,
        "corrected Dense-entry evidence must prefer R -> S for the accepted counterexample")

# Missing evidence affects estimate/ranking quality, never Search admission.
require("RegistrationOrderFallback" in planner,
        "missing Planner evidence needs a runnable deterministic fallback")
require(planner.count("searchAdmissionAffected=false") == 1 and
        eta.count("searchAdmissionAffected=false") == 1,
        "Planner/ETA evidence must not become a Search Gate")

print("family planner and Family-world ETA: PASS")

require(controller.count("NormalizeWorkshopSearchModePreference() == Beta110RelicComputeBackendPreference.Cpu") >= 2,
        "Search and ETA must both honor the existing CPU Only preference")
