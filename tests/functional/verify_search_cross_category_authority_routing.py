#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def require(value: bool, code: str) -> None:
    if not value:
        raise AssertionError(code)
    print(f"{code}=true")

projector = read("Search/Runtime/SearchAuthorityProjector.cs")
worker = read("Search/Runtime/SearchExecutionPipeline.cs")
exact = read("Search/Runtime/ProductionExactSearchEvaluator.cs")
authority = read("Core/World/Snapshots/Beta109AcceptedRuntimeValidationAuthority.cs")
controller = read("Ui/Controllers/SearchPageController.cs")
contracts = read("Search/Contracts/SearchContracts.cs")

require("PreserveSeedIndependentEffectFoundation" in projector,
        "seedIndependentEffectFoundationHelperPresent")
require("OrderedRelicBag = null" in projector and "RelicBagExact = false" in projector,
        "onlySeedSpecificRelicBagRemoved")
require("WithEffectAuthority(null)" not in projector and "WithEffectAuthority(null)" not in worker,
        "effectFoundationNotDropped")
require("PreserveSeedIndependentEffectFoundation(plan.Authority)" in exact,
        "emptyAndIdentitySearchKeepFoundation")
require("RequiresNormalCombatRewardDomain" in contracts and "RequiresEffectColdPath" in contracts,
        "rewardStillUsesPerSeedEffectColdPath")
require("AssumedCompatibleLatestKnown" in authority and
        "Beta110ValidationAuthority.BossAncientAccepted" in authority and
        '"boss:" + Beta110BossValidationId' in authority and
        "AcceptedBeta110FixtureIds" in authority,
        "beta110BossAncientAcceptedAuthorityPublished")
require("Search plan:" in controller and "FirstFailureCode" in controller,
        "crossCategoryReadableDiagnosticsPresent")

print("searchCrossCategoryAuthorityRoutingHotfix=true")
