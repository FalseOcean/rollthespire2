from pathlib import Path
from typing import Dict, List, Set


ROOT = Path(__file__).resolve().parents[2]
PHASE1 = (ROOT / "Search/FamilyExecution/Shaders/MerchantShopColorless.comp.glsl").read_text(encoding="utf-8")
SCATTER = (ROOT / "Search/FamilyExecution/Shaders/MerchantShopColorlessStableScatter.comp.glsl").read_text(encoding="utf-8")
EXECUTOR = (ROOT / "Search/FamilyExecution/MerchantShopColorlessGpuExecutor.cs").read_text(encoding="utf-8")
FAMILY = (ROOT / "Search/FamilyExecution/MerchantShopColorlessFamily.cs").read_text(encoding="utf-8")
POLICY = (ROOT / "Search/FamilyExecution/MerchantShopColorlessCompactionPolicy.cs").read_text(encoding="utf-8")
FOUNDATION = (ROOT / "Search/FamilyExecution/FamilyPerformanceFoundation.cs").read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def stable_compact(count: int, kept: Set[int]) -> List[int]:
    workgroup_size = 64
    carry = 8
    candidates_per_group = workgroup_size * carry
    groups = (count + candidates_per_group - 1) // candidates_per_group
    masks: Dict[int, int] = {}
    block_counts = [0] * groups
    for ordinal in kept:
        require(0 <= ordinal < count, "fixture ordinal outside input")
        invocation = ordinal // carry
        masks[invocation] = masks.get(invocation, 0) | (1 << (ordinal % carry))
        block_counts[ordinal // candidates_per_group] += 1

    offsets = [0] * groups
    total = 0
    for block, block_count in enumerate(block_counts):
        offsets[block] = total
        total += block_count

    output = [-1] * total
    for block in range(groups):
        local_rank = 0
        first_invocation = block * workgroup_size
        for local_invocation in range(workgroup_size):
            invocation = first_invocation + local_invocation
            mask = masks.get(invocation, 0)
            for bit in range(carry):
                if mask & (1 << bit):
                    output[offsets[block] + local_rank] = invocation * carry + bit
                    local_rank += 1
        require(local_rank == block_counts[block], "block count and scatter disagree")
    return output


def check_case(name: str, count: int, kept: Set[int]) -> None:
    actual = stable_compact(count, kept)
    expected = sorted(kept)
    require(actual == expected, f"{name}: stable output differs from sorted baseline")
    require(all(a < b for a, b in zip(actual, actual[1:])), f"{name}: output is not strict")


require('StableThreshold = 0.01d' in POLICY, "Production threshold mismatch")
require("resolved >= StableThreshold" in POLICY, "1% boundary must select Stable")
require("AnalyticalFinalSurvival" in POLICY and "SurvivalProbability" in POLICY,
        "selector must consume analytical Final Survival")
require("RT2_S_COMPACTION_MODE" in POLICY and "ForceAtomic" in POLICY and "ForceStable" in POLICY,
        "developer Auto/ForceAtomic/ForceStable override contract missing")
require("RT2_S_STABLE_ORDERED_COMPACTION" in POLICY, "legacy force-Stable audit switch missing")
require("MerchantShopColorlessStableOrderedCompaction" in FOUNDATION and
        "MerchantShopColorlessAtomicAppendHostSort" in FOUNDATION,
        "Atomic and Stable physical revisions must be isolated")
require("_compactionDecision.PhysicalImplementationRevision" in FAMILY,
        "selected physical revision must reach Condition Performance evidence identity")
require("StableKeepMaskBytes = MaximumInvocationCount * sizeof(uint)" in EXECUTOR, "mask budget changed")
require("StableBlockCountBytes = MaximumWorkgroupCount * sizeof(uint)" in EXECUTOR, "count budget changed")
require("StableBlockOffsetBytes = MaximumWorkgroupCount * sizeof(uint)" in EXECUTOR, "offset budget changed")
require("keep_mask |= 1u << offset" in PHASE1, "Carry8 mask mapping missing")
require("block_counts.values[gl_WorkGroupID.x] = block_count" in PHASE1, "per-WG count missing")
require("total != checked((uint)survivorCount)" in EXECUTOR, "prefix/header total check missing")
require("Array.Sort(survivors)" in EXECUTOR, "Production baseline sort missing")
require("Stable scatter is constructive" in EXECUTOR, "stable retained validation seam missing")
require("evaluate_merchant_colorless" not in SCATTER, "scatter must not replay Merchant semantics")
require("root_hash" not in SCATTER and "rng_" not in SCATTER, "scatter must not decode/replay RNG")

check_case("zero", 1024, set())
check_case("one", 1024, {517})
check_case("carry8-boundary", 33, {0, 7, 8, 15, 16, 31, 32})
check_case("workgroup-boundary", 1025, {0, 510, 511, 512, 513, 1024})
check_case("partial-final-range", 777, {1, 255, 511, 512, 776})
check_case("sparse", 100_003, set(range(3, 100_003, 997)))
check_case("medium", 100_003, set(range(7, 100_003, 101)))
check_case("high", 100_003, set(range(11, 100_003, 23)))
check_case("full-2^24", 1 << 24, {0, 7, 8, 511, 512, (1 << 24) - 2, (1 << 24) - 1})

print("S stable ordered compaction structure and fixed-geometry proof fixtures: PASS")
