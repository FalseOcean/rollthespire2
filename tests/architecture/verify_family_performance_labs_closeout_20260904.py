from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


r_shader = read("Search/FamilyExecution/Shaders/RelicFamily.comp.glsl")
r_family = read("Search/FamilyExecution/RelicFamily.cs")
r_executor = read("Search/FamilyExecution/RelicFamilyGpuExecutor.cs")
foundation = read("Search/FamilyExecution/FamilyPerformanceFoundation.cs")

require("advance_seed12_packed(seed_first8, seed_tail4)" in r_shader,
        "accepted dense Base34 Carry8 path missing")
require("record_processed_workgroup();" in r_shader and
        "atomicAdd(output_header.values[4u], admitted);" in r_shader,
        "accepted workgroup processed aggregation missing")
require("process_candidate(input_index);" in r_shader,
        "compact FullDecode path missing")
for rejected in (
    "add_seed12_packed_delta",
    "seed_ascii_digit",
    "previous_logical",
    "CompactOrderedStrideCarry8",
):
    require(rejected not in r_shader, f"rejected stride-carry shader code remains: {rejected}")

require("candidateDecode=DensePackedBase34Carry8_CompactFullDecode" in r_family,
        "R runtime identity does not report final decode path")
for telemetry in (
    "CompactGaps",
    "meanOrdinalDelta",
    "medianOrdinalDelta",
    "p90OrdinalDelta",
    "maxOrdinalDelta",
    "captureCompactGapQuantiles",
    "PackCompactOrdinals",
):
    require(telemetry not in r_family and telemetry not in r_executor,
            f"Lab #4-only telemetry remains: {telemetry}")

require("R.Relic.Gpu.v7.TrackedPositions.Base34Carry8.ProcessedWorkgroup.CompactFullDecode.HistoricalNextInt" in foundation,
        "final R physical revision missing")
require("RelicDensePerformance" in r_family and "RelicCompactPerformance" in r_family,
        "Dense and Compact R Hardware observations must use isolated identities")
require("CanonicalAbi1ReadyMs" in r_family and "CanonicalAbi1ReadyMs" in r_executor,
        "R Hardware observation must include the canonical ABI1-ready boundary")

print("family-performance-labs-closeout: PASS")
