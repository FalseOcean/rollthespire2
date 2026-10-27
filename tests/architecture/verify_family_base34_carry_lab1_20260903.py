from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


common = read("Search/FamilyExecution/Shaders/VisibleSeedRootHashCommon.glsl")
s_shader = read("Search/FamilyExecution/Shaders/MerchantShopColorless.comp.glsl")
r_shader = read("Search/FamilyExecution/Shaders/RelicFamily.comp.glsl")
utility = read("Search/FamilyExecution/FamilyGpuComputeUtility.cs")
foundation = read("Search/FamilyExecution/FamilyPerformanceFoundation.cs")

require(common.count("ordinal % uint64_t(34u)") == 11, "packed first decode must extract 11 low digits")
require(common.count("ordinal /= uint64_t(34u)") == 11, "packed first decode must divide only for remaining digits")
for transition in (
    "value == 57u",   # 9 -> A
    "value == 72u",   # H -> J
    "value == 78u",   # N -> P
    "value < 90u",    # P..Y -> next
    "return 48u",     # Z -> 0 + carry
):
    require(transition in common, f"base34 alphabet carry transition missing: {transition}")
for shift in ("24u", "16u", "8u", "0u", "56u", "48u", "40u", "32u"):
    require(shift in common, f"packed carry shift missing: {shift}")
require("root_hash_for_seed12_packed" in common, "packed seed must feed xxHash directly")

for name, shader in (("S", s_shader), ("R", r_shader)):
    require(shader.count("/*__RT2_VISIBLE_SEED_ROOT_HASH__*/") == 1, f"{name} common marker missing")
    require("gl_GlobalInvocationID.x * SEEDS_PER_INVOCATION" in shader, f"{name} invocation mapping changed")
    require("batch_meta.values[4u] == 0u" in shader, f"{name} dense-only carry guard missing")
    require("encode_seed12_packed(global_ordinal" in shader, f"{name} first-candidate decode missing")
    require("advance_seed12_packed(seed_first8, seed_tail4)" in shader, f"{name} carry advance missing")
    require("root_hash_for_ordinal(global_ordinal)" in shader, f"{name} compact full-decode path missing")

require("LoadFamilyShaderWithVisibleSeedRootHash" in utility, "shared Family shader composition seam missing")
require("S.MerchantShopColorless.Gpu.v7.Base34Carry8.ProcessedWorkgroup.ConsumeOnly.HistoricalNextInt" in foundation,
        "S physical implementation revision no longer contains the accepted Base34 carry identity")
require("R.Relic.Gpu.v7.TrackedPositions.Base34Carry8.ProcessedWorkgroup.CompactFullDecode.HistoricalNextInt" in foundation,
        "R physical implementation revision no longer contains the accepted Base34 carry identity")

print("family base34 carry lab1 structure: PASS")
