from __future__ import annotations

from itertools import product
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EST = ROOT / "Search/Selectivity/NeowStructuredEffectProbabilityEstimator.cs"
text = EST.read_text(encoding="utf-8")

checks = {
    "old nested blanket deferral removed": "Probability.NeowStructured.NestedSourcePending" not in text,
    "old scroll blanket deferral removed": "Probability.NeowStructured.ScrollBoxesDPDeferred" not in text,
    "bones nested authority present": "Probability.Authority.BonesNestedStructuredConditional" in text,
    "bones nested shared fact": "Nested source identities are SharedFacts with RequiredBonesCombination/RequiredBonesAcquisitionOrder" in text,
    "bones nested capsule shared-bag authority implemented": "Probability.Authority.BonesNestedStructuredWithCapsuleSharedBag" in text and "BonesNestedCapsuleSharedBagDeferred" not in text,
    "scroll exact helper present": "TryEstimateScrollBoxesCondition" in text,
    "scroll runtime pools": "CommonPool={common.Length};UncommonPool={uncommon.Length}" in text,
    "scroll cross bundle no repeat": "crossBundleNoRepeat=true" in text,
    "triple claw exact": "1d - 0.99d * 0.99d" in text,
    "defect normal opportunity": "2d * 0.99d" in text,
}

failed = [name for name, ok in checks.items() if not ok]
if failed:
    raise SystemExit("FAILED static checks: " + ", ".join(failed))


def formula(c_pool: int, u_pool: int, target_c: int, target_u: int, defect: bool) -> float:
    common_mass = {0: 1.0, 1: 2.0 / c_pool, 2: 2.0 / (c_pool * (c_pool - 1))}[target_c]
    uncommon_mass = {0: 1.0, 1: 1.0 / u_pool}[target_u]
    return (1.98 if defect else 2.0) * common_mass * uncommon_mass


def brute(c_pool: int, u_pool: int, target_c: int, target_u: int, defect: bool) -> float:
    common = tuple(range(c_pool))
    uncommon = tuple(range(u_pool))
    special_branches = ((False, 0.99), (True, 0.01)) if defect else ((False, 1.0),)
    hit_mass = 0.0

    def step(bundle: int, used_c: frozenset[int], used_u: frozenset[int], mass: float, hit: bool) -> None:
        nonlocal hit_mass
        if bundle == 2:
            if hit:
                hit_mass += mass
            return
        for special, special_mass in special_branches:
            if special:
                step(bundle + 1, used_c, used_u, mass * special_mass, hit)
                continue
            avail_c = [value for value in common if value not in used_c]
            avail_u = [value for value in uncommon if value not in used_u]
            for first in avail_c:
                for second in avail_c:
                    if second == first:
                        continue
                    for unc in avail_u:
                        branch = mass * special_mass / len(avail_c) / (len(avail_c) - 1) / len(avail_u)
                        bundle_hit = all(value in (first, second) for value in range(target_c))
                        bundle_hit = bundle_hit and all(value == unc for value in range(target_u))
                        step(
                            bundle + 1,
                            used_c | frozenset((first, second)),
                            used_u | frozenset((unc,)),
                            branch,
                            hit or bundle_hit,
                        )

    step(0, frozenset(), frozenset(), 1.0, False)
    return hit_mass


for c_pool, u_pool in ((5, 3), (7, 4)):
    for target_c, target_u in ((1, 0), (0, 1), (2, 0), (1, 1), (2, 1)):
        for defect in (False, True):
            expected = formula(c_pool, u_pool, target_c, target_u, defect)
            actual = brute(c_pool, u_pool, target_c, target_u, defect)
            if abs(expected - actual) > 1e-10:
                raise SystemExit(
                    f"FAILED ScrollBoxes oracle C={c_pool} U={u_pool} "
                    f"tc={target_c} tu={target_u} defect={defect}: "
                    f"formula={expected} brute={actual}"
                )

print(f"probabilityNestedNeowScrollBoxesHotfixValidation=Passed;checks={len(checks)};scrollOracleCases=20")
