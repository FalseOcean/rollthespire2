from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


s_shader = read("Search/FamilyExecution/Shaders/MerchantShopColorless.comp.glsl")
r_shader = read("Search/FamilyExecution/Shaders/RelicFamily.comp.glsl")
s_family = read("Search/FamilyExecution/MerchantShopColorlessFamily.cs")
foundation = read("Search/FamilyExecution/FamilyPerformanceFoundation.cs")
cost = read("Search/FamilyExecution/FamilyAnalyticalCost.cs")

require("void consume_rng()" in s_shader, "S consume-only helper missing")
for transition in (
    "uint64_t temporary = s1 << 17u;",
    "s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= temporary; s3 = rt2_rotate_left45_split32(s3);",
):
    require(s_shader.count(transition) == 2,
            f"S next_u64 and consume_rng transition must remain identical: {transition}")

require("next_double" not in s_shader, "S still materializes discarded double results")
require("float64" not in s_shader and "double(" not in s_shader,
        "S still requires FP64 for discarded RNG burns")
require("for (uint call = 0u; call < 12u; ++call) consume_rng();" in s_shader,
        "S pre-colorless burn count changed")
between_draws = s_shader.split("uncommon[ordinal - 1u] = next_int(uncommon_pool_count);", 1)[1].split(
    "rare[ordinal - 1u] = next_int(rare_pool_count);", 1)[0]
require(between_draws.count("consume_rng();") == 1 and "next_u64(" not in between_draws and "next_int(" not in between_draws,
        "S colorless price burn/order changed")
require("for (uint call = 0u; call < 13u; ++call) consume_rng();" in s_shader,
        "S post-colorless burn count changed")
require(s_shader.count("next_int(") == 3, "S must retain two next_int calls plus its helper definition")

require("next_double" not in r_shader, "R unexpectedly gained an S-only next_double helper")
require("rngBurnImplementation=StateAdvanceOnly26PerMerchant" in s_family,
        "S consume-only runtime diagnostic missing")
require("maximumMerchantOrdinal=" in s_family,
        "S runtime diagnostics cannot distinguish replay horizon")
require(re.search(r'"S\.MerchantShopColorless\.Gpu\.v\d+\.[^"\n]*ConsumeOnly\.HistoricalNextInt"', foundation) is not None,
        "S consume-only physical revision missing")
require("R.Relic.Gpu.v7.TrackedPositions.Base34Carry8.ProcessedWorkgroup.CompactFullDecode.HistoricalNextInt" in foundation,
        "R physical revision no longer preserves the accepted pre-Lab4 identity")

require("26 consumed NextUInt64 draws per Merchant" in cost,
        "S analytical cost no longer describes semantic RNG consumption")
require("FamilyAnalyticalOperation.RngAdvance" in cost,
        "S analytical RNG consumption operation was removed")

print("family S consume-only lab3 structure: PASS")
