from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


audit = read("Search/FamilyExecution/FamilyPipelineBoundaryAudit.cs")
runtime = read("Search/FamilyExecution/FamilyExecutionRuntime.cs")
s_executor = read("Search/FamilyExecution/MerchantShopColorlessGpuExecutor.cs")
r_executor = read("Search/FamilyExecution/RelicFamilyGpuExecutor.cs")
foundation = read("Search/FamilyExecution/FamilyPerformanceFoundation.cs")

for field in (
    "headerWaitReadbackMs",
    "payloadReadbackMs",
    "sourceHostMaterializeMs",
    "sourceTypedAllocationMs",
    "sourceDecodePayloadMs",
    "sourceSortMs",
    "sourceValidationCandidateSetMs",
    "sourceHostAllocatedBytes",
    "ownerHandoffMs",
    "targetHostPackMs",
    "hostPackAllocateMs",
    "hostPackConvertValidateMs",
    "targetUploadStagingMs",
    "targetBufferUpdateMs",
    "estimatedTemporaryBytes",
    "targetUploadMs",
    "targetDispatchBoundaryMs",
    "boundaryTotalMs",
):
    require(field in audit, f"boundary audit field missing: {field}")

require("RT2_FAMILY_PIPELINE_BOUNDARY_AUDIT" in audit, "dedicated opt-in flag missing")
require("hardwarePerformanceEvidence=false" in audit, "Hardware evidence exclusion missing")
require("FamilyPipelineBoundaryAudit.TryCreate()" in runtime, "session-scoped audit observer missing")
require("HeaderReadbackMs" in s_executor and "PayloadReadbackMs" in s_executor,
        "S header/payload readback split missing")
require("HostMaterializeMs" in s_executor, "S host materialization timing missing")
require("TypedAllocationMs" in s_executor and "DecodePayloadMs" in s_executor and "SortMs" in s_executor,
        "S materialization decomposition missing")
require("CompactPackMs" in r_executor and "CompactUploadMs" in r_executor and
        "CompactUploadStagingMs" in r_executor and "CompactBufferUpdateMs" in r_executor,
        "R compact preparation/upload split missing")

for forbidden in ("ObserveSearchPeak", "SubmitObservation", "ConditionPerformanceFactor", "AnalyticalCost"):
    require(forbidden not in audit, f"boundary audit leaked into authority: {forbidden}")

require("CompactFullDecode" in foundation, "final R FullDecode revision changed")
print("family-pipeline-boundary-audit: PASS")
