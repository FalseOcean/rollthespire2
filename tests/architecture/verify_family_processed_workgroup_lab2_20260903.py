from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


s_shader = read("Search/FamilyExecution/Shaders/MerchantShopColorless.comp.glsl")
r_shader = read("Search/FamilyExecution/Shaders/RelicFamily.comp.glsl")
s_executor = read("Search/FamilyExecution/MerchantShopColorlessGpuExecutor.cs")
r_executor = read("Search/FamilyExecution/RelicFamilyGpuExecutor.cs")
s_family = read("Search/FamilyExecution/MerchantShopColorlessFamily.cs")
r_family = read("Search/FamilyExecution/RelicFamily.cs")
foundation = read("Search/FamilyExecution/FamilyPerformanceFoundation.cs")

for name, shader in (("S", s_shader), ("R", r_shader)):
    require("const uint CANDIDATES_PER_WORKGROUP = 64u * SEEDS_PER_INVOCATION;" in shader,
            f"{name} workgroup admitted-count geometry missing")
    require("void record_processed_workgroup()" in shader,
            f"{name} processed aggregation helper missing")
    require("gl_LocalInvocationIndex != 0u" in shader,
            f"{name} processed counter must have one workgroup leader")
    require("gl_WorkGroupID.x * CANDIDATES_PER_WORKGROUP" in shader,
            f"{name} processed counter must derive its admitted range from dispatch geometry")
    require("min(CANDIDATES_PER_WORKGROUP, input_count - first_input)" in shader,
            f"{name} final partial workgroup is not counted exactly")
    require(shader.count("atomicAdd(output_header.values[4u], admitted)") == 1,
            f"{name} must perform exactly one processed atomic per admitted workgroup")
    require("atomicAdd(output_header.values[4u], 1u)" not in shader,
            f"{name} still contains per-candidate processed atomics")
    require(shader.index("record_processed_workgroup();") < shader.index("first_input = gl_GlobalInvocationID.x"),
            f"{name} processed semantics moved behind candidate filtering")

for name, executor in (("S", s_executor), ("R", r_executor)):
    require("ProcessedCandidates" in executor, f"{name} observed processed metric missing")
    require("processed !=" in executor and "input" in executor,
            f"{name} full-dispatch fail-closed validation missing")
    require("return processed;" in executor, f"{name} validated observed count is not exposed")
    require("InputCandidates" in executor and "PhysicalMs" in executor,
            f"{name} admitted-input throughput denominator changed")

require("processedCounting=GpuWorkgroupAggregate512" in s_family,
        "S processed counting mode diagnostic missing")
require("processedCounting=GpuWorkgroupAggregate512" in r_family,
        "R processed counting mode diagnostic missing")
require("gpuProcessedCandidates" in s_family, "S processed session diagnostic missing")
require("processedCandidates" in r_family, "R processed session diagnostic missing")

require("S.MerchantShopColorless.Gpu.v7.Base34Carry8.ProcessedWorkgroup.ConsumeOnly.HistoricalNextInt" in foundation,
        "S physical revision was not isolated")
require("R.Relic.Gpu.v7.TrackedPositions.Base34Carry8.ProcessedWorkgroup.CompactFullDecode.HistoricalNextInt" in foundation,
        "R physical revision was not isolated")

print("family processed workgroup lab2 structure: PASS")
