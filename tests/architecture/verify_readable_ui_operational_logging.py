"""Current logging responsibility checks; runtime behavior lives in Host --logging-checks."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
def read(p):return (root/p).read_text(encoding="utf-8-sig")
def check(p,m):
    if not p:raise AssertionError(m)
f=read("Bootstrap/RuntimeLog.cs");s=read("Bootstrap/OperationalFileLog.cs")
check('RT2_REWRITE_DETAIL_LOG' in f, 'detail compatibility switch')
check('GD.' not in f[f.index('private static bool Write('):], 'ordinary writer must not fan out to Godot')
check('TryBackgroundDetail' in f and 'Write("DETAIL", message)' in f, 'detail uses canonical file path')
check('GD.PushWarning' in f and 'GD.PrintErr' in f and 'Full diagnostics:' in f, 'severity-aware discovery summary')
check('ConcurrentQueue' in f and 'CurrentManagedThreadId != _mainThread' in f, 'worker discovery delivered on main thread')
check('File.AppendAllText' not in s and 'StreamWriter' in s, 'buffered existing sink')
check('MaxRetainedLogs = 10' in s and 'ProcessExit' in s and 'FlushIfDue' in s, 'retention and flush lifetime')
check('RuntimeLog.Info($"rewriteAssemblySha256=' in read("Bootstrap/ModRuntime.cs"), 'default loaded binary evidence')
check('RuntimeLog.Detail($"MATCH seed=' in read("Ui/Controllers/SearchPageController.cs"), 'result trace bounded in default')
check('physicalExecutionSummary=true' in read("Search/FamilyExecution/GpuCostCalibration.cs"), 'default physical totals retained')
check('gpuCostObservation=true' in read("Search/FamilyExecution/GpuCostCalibration.cs"), 'default best-window evidence retained')
check('LogExecutionEvidence' in read("Search/Runtime/ProductionSearchSession.cs"), 'fault/cancel terminal evidence')
print("operational logging policy: PASS")
