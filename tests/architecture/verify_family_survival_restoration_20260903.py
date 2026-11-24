from fractions import Fraction
from math import expm1, log1p
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SURVIVAL = (ROOT / "Search/FamilyExecution/FamilySurvival.cs").read_text(encoding="utf-8")
FOLD = (ROOT / "Search/FamilyExecution/FamilyAnalyticalCost.cs").read_text(encoding="utf-8")
SHOP = (ROOT / "Search/Selectivity/ShopSequenceProbabilityEstimator.cs").read_text(encoding="utf-8")
MATH = (ROOT / "Search/Predictability/SearchPredictabilityMath.cs").read_text(encoding="utf-8")
SESSION = (ROOT / "Search/Runtime/ProductionSearchSession.cs").read_text(encoding="utf-8")

for forbidden in (
    "RuntimeObserved",
    "candidatesPerSecond",
    "HardwareEvidence",
    "Bayesian",
    "MonteCarlo",
):
    assert forbidden not in SURVIVAL, forbidden

for required in (
    "FamilySurvivalProjection",
    "TrySolveColorlessConjunction",
    "SearchSelectivityEstimator.EstimateStage",
    "SearchSelectivityInput.From(request)",
    "Probability.Authority.MerchantColorlessFamilyExact",
):
    assert required in SURVIVAL, required

assert "ExpectedPhysicalInvocationCount" in FOLD
assert "IReadOnlyList<FamilySurvivalProjection>" in FOLD
assert "Family Cost and Survival projection order does not match" in FOLD
assert "familySurvivalProjection=true" in SESSION
assert "runtimeObservationUsed=false" in SESSION
assert "double.LogP1(-probability)" in MATH
assert "-double.ExpM1(logMiss)" in MATH

# S replacement draws: one positional fact, two independent U/R facts, and two
# targets sharing one two-draw sequence. Duplicate observation of the same fact
# is not multiplied twice.
uncommon = 32
rare = 21
assert Fraction(1, uncommon) == Fraction(1, 32)
assert Fraction(1, uncommon) * Fraction(1, rare) == Fraction(1, 672)
assert Fraction(2, uncommon * uncommon) == Fraction(1, 512)
assert Fraction(1, uncommon) != Fraction(1, uncommon * uncommon)

# R uniform permutations: one exact position and two distinct identities in the
# first two positions. Adding the second constraint cannot increase Survival.
relic_lane = 38
r_one = Fraction(1, relic_lane)
r_pair = Fraction(2, relic_lane * (relic_lane - 1))
assert r_pair < r_one

# Stable non-empty probability remains non-zero well below 1 - p subtraction precision.
p = 1e-20
stable_non_empty = -expm1(16_777_216 * log1p(-p))
assert stable_non_empty > 0


def expected_invocations(roots, fragment_size, cumulative_survival):
    fragments, tail = divmod(roots, fragment_size)
    value = fragments * -expm1(fragment_size * log1p(-cumulative_survival))
    if tail:
        value += -expm1(tail * log1p(-cumulative_survival))
    return value


def family_work(roots, input_population, output_population, invocations, fixed, per_input, compact, per_output):
    return invocations * fixed + input_population * (per_input + compact) + output_population * per_output


# Cost + resolved Survival complete both explicit orders without choosing one.
roots = 10 * (1 << 24)
s_survival = float(Fraction(1, 672))
r_survival = float(Fraction(1, 703))
s_first = family_work(roots, roots, roots * s_survival, 10, 4, 34, 0, 3)
s_then_r = s_first + family_work(
    roots,
    roots * s_survival,
    roots * s_survival * r_survival,
    expected_invocations(roots, 1 << 24, s_survival),
    4,
    120,
    1,
    2,
)
r_first = family_work(roots, roots, roots * r_survival, 10, 4, 120, 0, 2)
r_then_s = r_first + family_work(
    roots,
    roots * r_survival,
    roots * r_survival * s_survival,
    expected_invocations(roots, 1 << 24, r_survival),
    4,
    34,
    2,
    3,
)
assert s_then_r != r_then_s

for required in (
    "orderedByPosition",
    "unorderedConstraints",
    "ColorlessOrderedPositionConflict",
    "ColorlessJointMultiplicityImpossible",
):
    assert required in SHOP, required

print("family survival restoration: PASS")
