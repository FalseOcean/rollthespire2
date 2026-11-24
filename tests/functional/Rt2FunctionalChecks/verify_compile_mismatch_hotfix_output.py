from pathlib import Path
import argparse

parser = argparse.ArgumentParser()
parser.add_argument("log", type=Path)
args = parser.parse_args()
text = args.log.read_text(encoding="utf-8", errors="replace")

required = {
    "beta110FastCompileMismatchDiagnostics=true;remainingMismatchCount=0;": "remaining compile mismatch",
    "scenario=HeftyTabletRareOffer;": "exact mismatch scenario",
    "legacyCharacterRarePoolCount=2;": "legacy rare count",
    "compilerRequiredRarePoolCount=3;": "compiler gate",
    "correctedCharacterRarePoolCount=3;": "corrected rare count",
    "classification=ReferenceFixtureAuthorityInsufficient;": "root-cause classification",
    "productionCompilerChanged=false;": "production compiler unchanged",
    "fixturePassed=true;": "root-cause fixture",
    "beta110HeftyTabletPositiveFixture=true;compiled=true;enteredExact=true;exactMatch=true;": "positive Hefty fixture",
    "beta110NeowFastRngSurfaceC1=true;compileMismatch=0;": "C1 compile parity",
    "searchFastExecutionPlannerParity=true;window=1048576;candidateMismatch=0;routeMaskMismatch=0;": "main 2^20 parity",
    "searchFastRouteAuditMatrix=true;": "route matrix fixture",
    "candidateMismatch=0;routeMaskMismatch=0;": "route candidate parity",
    "beta110FastSearchC0ParityPassed=true;": "final gate",
}

missing = [name for token, name in required.items() if token not in text]
if missing:
    raise SystemExit("Planner compile-mismatch hotfix validation failed: " + ", ".join(missing))

print("SearchFastCompileMismatchRuntimeValidationPassed=true")
print("CompileMismatch=0")
print("MainContinuousParityWindow=1048576")
print("CandidateMismatch=0")
print("RouteMaskMismatch=0")
print("HeftyPositiveFixture=true")
print("FinalC0Gate=true")
