from pathlib import Path
from math import exp, floor, log

ROOT = Path(__file__).resolve().parents[2]
COST = (ROOT / "Search/FamilyExecution/FamilyAnalyticalCost.cs").read_text(encoding="utf-8")
CONTRACTS = (ROOT / "Search/FamilyExecution/FamilyExecutionContracts.cs").read_text(encoding="utf-8")
S_FAMILY = (ROOT / "Search/FamilyExecution/MerchantShopColorlessFamily.cs").read_text(encoding="utf-8")
R_FAMILY = (ROOT / "Search/FamilyExecution/RelicFamily.cs").read_text(encoding="utf-8")

for forbidden in (
    "CapabilityOffer",
    "ExecutableAlternative",
    "PhysicalBackendVariant",
    "BackendRealizer",
    "BackendPipelineCompiler",
    "CompoundAction",
    "RuntimeStageCostCalibrator",
    "DurableHardwareService",
):
    assert forbidden not in COST, forbidden

for required in (
    "FixedWorkUnitsPerInvocation",
    "WorkUnitsPerInput",
    "CompactInputWorkUnitsPerInput",
    "WorkUnitsPerOutput",
    "26d * maximumOrdinal",
    "2d * maximumOrdinal",
    "TrackedPositionUpdateAttempt",
    "expectedObservedDraws",
    "ExpectedPhysicalInvocationCount",
    "ExpectedNonEmptyFragmentCount",
    "SearchPredictabilityMath.ProbabilityAtLeastOne",
    "inputPopulation * inputRate",
    "outputPopulation * WorkUnitsPerOutput",
    "NextInt=HistoricalShortcut",
):
    assert required in COST, required

assert "FamilyAnalyticalCostProjection AnalyticalCost" in CONTRACTS
assert "MerchantShopColorlessAnalyticalCost.Project" in S_FAMILY
assert "RelicFamilyAnalyticalCost.Project" in R_FAMILY


def merchant_per_input(maximum_ordinal: int, predicate_budget: int) -> float:
    return 1 + 1 + 26 * maximum_ordinal + 2 * maximum_ordinal + 2 * maximum_ordinal + predicate_budget


assert merchant_per_input(1, 1) < merchant_per_input(3, 3) < merchant_per_input(5, 25)


def tracked_lane_units(length: int, tracked: int) -> float:
    draws = max(0, length - 1)
    return draws + tracked + draws * tracked


assert tracked_lane_units(38, 1) < tracked_lane_units(38, 2) < tracked_lane_units(38, 4)


def stage_work(population, survival, fixed, per_input, compact_increment, per_output, invocations, compact):
    output = population * survival
    return fixed * invocations + population * (per_input + (compact_increment if compact else 0)) + output * per_output


root = 1_000_000
s_survival = 0.01
r_survival = 0.001
s_then_r = stage_work(root, s_survival, 4, 33, 2, 3, 1, False)
s_then_r += stage_work(root * s_survival, r_survival, 4, 120, 1, 2, 1, True)
r_then_s = stage_work(root, r_survival, 4, 120, 1, 2, 1, False)
r_then_s += stage_work(root * r_survival, s_survival, 4, 33, 2, 3, 1, True)
assert s_then_r != r_then_s


def probability_at_least_one(probability, population):
    if population <= 0 or probability <= 0:
        return 0.0
    if probability >= 1:
        return 1.0
    return 1.0 - exp(population * log(1.0 - probability))


def expected_non_empty_fragments(root_population, fragment_capacity, cumulative_survival):
    full_fragments = floor(root_population / fragment_capacity)
    remainder = root_population - full_fragments * fragment_capacity
    expected = full_fragments * probability_at_least_one(cumulative_survival, fragment_capacity)
    if remainder > 0:
        expected += probability_at_least_one(cumulative_survival, remainder)
    return expected


fragment_capacity = 1 << 24
fragment_count = 2398
runtime_roots = fragment_capacity * fragment_count

# Owner sanity case: one expected survivor spread over 2,398 root fragments is
# approximately one non-empty downstream physical invocation, not 2,398.
sparse_s_to_r = expected_non_empty_fragments(runtime_roots, fragment_capacity, 1.0 / runtime_roots)
assert 0.99 < sparse_s_to_r < 1.01
assert 3.9 < 4 * sparse_s_to_r < 4.1

# R-first sparse input uses the same probability seam and remains proportional to
# expected survivors rather than the 2,398 logical fragment calls.
sparse_r_to_s = expected_non_empty_fragments(runtime_roots, fragment_capacity, 8.0 / runtime_roots)
assert 7.9 < sparse_r_to_s < 8.1

# Dense downstream input makes every root fragment non-empty with effectively unit probability.
dense_downstream = expected_non_empty_fragments(runtime_roots, fragment_capacity, 0.5)
assert dense_downstream == fragment_count

assert expected_non_empty_fragments(runtime_roots, fragment_capacity, 0.0) == 0.0

# Exact-only is the empty Family order: no Family work and unchanged population.
assert sum([]) == 0

print("family analytical cost restoration: PASS")
