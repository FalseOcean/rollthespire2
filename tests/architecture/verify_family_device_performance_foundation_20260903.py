from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def read(path):
    return (ROOT / path).read_text(encoding="utf-8")
def require(condition, message):
    if not condition:
        raise AssertionError(message)

# Current replacement for the retired 20260903 durable Family/bootstrap authority.
# Numerical behavior is exercised by --gpu-cost-calibration C# checks.
calibration = read("Search/FamilyExecution/GpuCostCalibration.cs")
session = read("Search/Runtime/ProductionSearchSession.cs")
contracts = read("Search/FamilyExecution/FamilyExecutionContracts.cs")
shell = read("Ui/Shell/AppShell.cs")
foundation = read("Search/FamilyExecution/FamilyPerformanceFoundation.cs")
require("FamilyPerformanceAuthorityV1" not in foundation + session + shell,
        "obsolete durable Family authority must not return")
for token in ("GpuLocalPeak", "SampleActiveMs", "SampleInputs", "ObservedAtUtc",
              "PhysicalImplementationRevision", "PrivateInput", "PrivateOutput"):
    require(token in calibration, "missing calibration identity/evidence: " + token)
require("window.Ms < 1000" in calibration and "!window.Warmed" in calibration,
        "require completed one-second windows after cold exclusion")
require("verification ||" in calibration and "inputs % quote.WindowCapacity" in calibration,
        "exclude verification and partial capacity observations")
require("throughput > peak.PeakThroughput" in calibration and "peak.PeakThroughput > old.PeakThroughput" in calibration,
        "local peak must only rise")
require("CostPeaks" in contracts and "GpuCostCalibration.Submit(_families.SelectMany(f => f.CostPeaks))" in session,
        "completed invocation evidence must reach existing session cleanup")
require("!physicalRecoveryContaminated && (SearchRunState)Volatile.Read(ref _state) != SearchRunState.Faulted" in session,
        "faulted/recovered sessions must not publish calibration")
for method in ("InitializeOnMainThread", "TryFlushPendingOnMainThread"):
    require("GpuCostCalibration." + method + "()" in shell, "existing main-thread persistence hook missing")
require("File.WriteAllText" not in session, "session worker must not persist")
require("reference(p.Key) / p.PeakThroughput" in calibration and "values.Length == 0 ? 1" in calibration,
        "derive current ratios with reference-default one")
require("record Document(int SchemaVersion, List<GpuLocalPeak> Peaks)" in calibration,
        "persist raw peaks, not a permanent ratio")
print("family GPU reference cost calibration foundation: PASS")
