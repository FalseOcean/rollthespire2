from __future__ import annotations

from pathlib import Path
import argparse


def read_console_log(path: Path) -> str:
    data = path.read_bytes()
    if data.startswith(b"\xff\xfe") or (data[:200].count(b"\x00") > 20):
        return data.decode("utf-16", errors="replace")
    if data.startswith(b"\xef\xbb\xbf"):
        return data.decode("utf-8-sig", errors="replace")
    return data.decode("utf-8", errors="replace")


def parse_record(text: str, prefix: str) -> dict[str, str]:
    line = next((line.strip() for line in text.splitlines() if line.startswith(prefix)), "")
    if not line:
        raise SystemExit(f"Missing output record: {prefix}")
    values: dict[str, str] = {}
    for part in line.split(";"):
        if "=" not in part:
            continue
        key, value = part.split("=", 1)
        values[key] = value
    return values


def integer(values: dict[str, str], key: str) -> int:
    try:
        return int(values[key])
    except (KeyError, ValueError) as exc:
        raise SystemExit(f"Missing or invalid integer field: {key}") from exc


parser = argparse.ArgumentParser()
parser.add_argument("log", type=Path)
args = parser.parse_args()
text = read_console_log(args.log)

planner = parse_record(text, "searchFastExecutionPlannerParity=true;")
route = parse_record(text, "searchFastRouteAuditMatrix=true;")
compile_diag = parse_record(text, "beta110FastCompileMismatchDiagnostics=true;")
fast_exact = parse_record(text, "beta110FastExactParity=true;")
lossy_final = parse_record(text, "beta110UnifiedLossyFinalSoundness=true;")
golden_route = parse_record(text, "beta110FastScenarioParity=true;scenario=RequiredGoldenPearlRoute;")
final_gate = parse_record(text, "beta110FastSearchC0ParityPassed=")

failures: list[str] = []
if planner.get("window") != "1048576": failures.append("main parity window")
if planner.get("candidateMismatch") != "0": failures.append("main candidate mismatch")
if planner.get("routeMaskMismatch") != "0": failures.append("main route-mask mismatch")
if planner.get("earlyRejectionObserved") != "true": failures.append("early rejection")
if compile_diag.get("remainingMismatchCount") != "0": failures.append("compile mismatch")
if fast_exact.get("falseReject") != "0": failures.append("global Fast false reject")
if fast_exact.get("searchRangeMismatch") != "0": failures.append("global Fast/Exact search-range mismatch")
if lossy_final.get("fastFalseNegative") != "0": failures.append("lossy Fast false negative")
if golden_route.get("exactMatches") != golden_route.get("fastMatches"):
    failures.append("top-level Neow identity parity (RequiredGoldenPearlRoute)")
if route.get("matrixAbi") != "2": failures.append("route matrix ABI")
if route.get("targetedPassed") != "true": failures.append("targeted route fixtures")
if route.get("candidateMismatch") != "0": failures.append("route candidate mismatch")
if route.get("routeMaskMismatch") != "0": failures.append("route-mask mismatch")
if route.get("continuousParityPassed") != "true": failures.append("continuous route parity")
if integer(route, "ignoredCompanionSeeds") <= 0: failures.append("ignored companion samples")
if integer(route, "ignoredCompanionComputations") >= integer(route, "ignoredCompanionReferenceComputations"):
    failures.append("ignored companion work reduction")
if integer(route, "dependencyDualOrderedSeeds") <= 0: failures.append("dependency closure DualOrdered samples")
if integer(route, "zeroActiveSourceRejectSeeds") <= 0: failures.append("zero-active source rejection samples")
if final_gate.get("beta110FastSearchC0ParityPassed") != "true": failures.append("final C0 gate")

if failures:
    raise SystemExit("Non-blocking ignored-effect validation failed: " + ", ".join(failures))

print("SearchFastNonBlockingIgnoredEffectsRuntimeValidationPassed=true")
print("RouteMatrixAbi=2")
print("MainContinuousParityWindow=1048576")
print("CandidateMismatch=0")
print("RouteMaskMismatch=0")
print(f"IgnoredCompanionSeeds={integer(route, 'ignoredCompanionSeeds')}")
print(f"DependencyDualOrderedSeeds={integer(route, 'dependencyDualOrderedSeeds')}")
print(f"ZeroActiveSourceRejectSeeds={integer(route, 'zeroActiveSourceRejectSeeds')}")
print("FinalC0Gate=true")
