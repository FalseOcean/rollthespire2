using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.FamilyExecution;

// CPU specialization of Reference × Local Ratio. Exact and GPU evidence never enter here.
internal sealed record CpuLocalPeak(string Device, string Key, string Shape, string Revision, int Workers,
    long Inputs, double CanonicalMs, DateTimeOffset ObservedAtUtc, bool CompactInput=false, int RootBatches=0, double? ModeledInput=null)
{
    internal double NanosecondsPerInput => CanonicalMs * 1e6 / Inputs;
}

internal sealed class CpuCostSnapshot
{
    private readonly Dictionary<string,double> _conditions = new();
    private readonly Dictionary<int,double> _globals = new();
    internal CpuCostSnapshot(IEnumerable<CpuLocalPeak> peaks)
    {
        var rows=peaks.Where(CpuCostCalibration.Valid)
            .Select(p=>(Peak:p,Reference:FamilyCpuReferenceCost.ReferenceNs(p.Shape)))
            .Where(x=>x.Reference is >0)
            .GroupBy(x=>x.Peak.Key).Select(g=>g.MinBy(x=>x.Peak.NanosecondsPerInput)).ToArray();
        foreach(var row in rows) _conditions[row.Peak.Key]=row.Peak.NanosecondsPerInput/row.Reference!.Value;
        foreach(var group in rows.GroupBy(r=>r.Peak.Workers))
        {
            var ratios=group.Select(r=>_conditions[r.Peak.Key]).Order().ToArray();
            _globals[group.Key]=ratios.Length%2==1?ratios[ratios.Length/2]:(ratios[ratios.Length/2-1]+ratios[ratios.Length/2])/2;
        }
    }
    internal (double Ratio,string Source) Resolve(string key,int workers) =>
        _conditions.TryGetValue(key,out double condition)?(condition,"Condition"):
        _globals.TryGetValue(workers,out double global)?(global,"Global"):(1,"ReferenceDefault");
    internal FamilyPhysicalQuote Local(FamilyPhysicalQuote raw,string revision,int workers,bool compact)
    {
        var (ratio,source)=Resolve(CpuCostCalibration.Key(raw.Shape,revision,workers,compact),workers);
        return raw with { NanosecondsPerInput=raw.NanosecondsPerInput*ratio,
            FixedWindowMilliseconds=raw.FixedWindowMilliseconds*ratio,
            LocalExecutorCostRatio=ratio,LocalCostSource=source,
            Evidence=raw.Evidence+$";ExecutionClass=CPU;WorkerConfig=P{workers};LocalCpuRatio={ratio:G17};LocalCostSource={source};sameRunFrozen=true" };
    }
}

internal static class CpuCostCalibration
{
    internal const string Boundary="CpuCanonical.Full65536.FirstExcluded.20260912.v1";
    private sealed record Document(int SchemaVersion,List<CpuLocalPeak> Peaks);
    private static readonly object Sync=new();
    private static readonly Dictionary<(string Device,string Key),CpuLocalPeak> Peaks=new();
    private static bool _loaded,_dirty;
    internal static string StorePath => Environment.GetEnvironmentVariable("RT2_CPU_COST_EVIDENCE_PATH") ??
        Path.Combine(Path.GetDirectoryName(OperationalFileLog.LogDirectory)!,"cache","cpu_cost_peaks_v1.json");
    internal static string DeviceKey()
    {
        var d=SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        return string.IsNullOrWhiteSpace(d.CpuIdentity)?"":d.RuntimeTarget+"|"+d.CpuIdentity;
    }
    internal static string Key(string shape,string revision,int workers,bool compact=false) =>
        $"{shape}|{revision}|P{workers}|{(compact?"Compact":"Dense")}|{Boundary}";
    internal static bool Valid(CpuLocalPeak p) => p is not null && p.Workers is >=1 and <=32 &&
        !string.IsNullOrWhiteSpace(p.Device) && !string.IsNullOrWhiteSpace(p.Shape) && !string.IsNullOrWhiteSpace(p.Revision) &&
        p.NanosecondsPerInput>0 && double.IsFinite(p.NanosecondsPerInput) && p.Inputs>=262144 && p.CanonicalMs>=50 && double.IsFinite(p.CanonicalMs) &&
        p.Key==Key(p.Shape,p.Revision,p.Workers,p.CompactInput) && p.Revision.Contains($".P{p.Workers}.",StringComparison.Ordinal) &&
        p.Revision == FamilyCpuReferenceCost.Revision(p.Shape)+$".RootMode2.P{p.Workers}.B65536.{(p.CompactInput?"CompactAbi1":"Dense")}.CanonicalAbi1Ready" &&
        (!p.CompactInput || (p.RootBatches>0 && p.ModeledInput is >0 &&
            p.ModeledInput == (p.Shape.StartsWith("R.DirectSmallC.",StringComparison.Ordinal)?65536d/17:p.Shape.StartsWith("R.DirectLargeUR.",StringComparison.Ordinal)?65536d/10:0) &&
            Math.Abs(p.Inputs/(p.RootBatches*p.ModeledInput.Value)-1)<=.05)) &&
        p.Shape.EndsWith($".P{p.Workers}",StringComparison.Ordinal) && FamilyCpuReferenceCost.ReferenceNs(p.Shape) is >0;
    internal static void InitializeOnMainThread()
    {
        lock(Sync)
        {
            if(_loaded)return;
            try
            {
                if(File.Exists(StorePath) && JsonSerializer.Deserialize<Document>(File.ReadAllText(StorePath)) is {SchemaVersion:1} d)
                    foreach(var p in d.Peaks.Where(Valid))
                        if(!Peaks.TryGetValue((p.Device,p.Key),out var old)||p.NanosecondsPerInput<old.NanosecondsPerInput)Peaks[(p.Device,p.Key)]=p;
            }
            catch(Exception ex){RuntimeLog.Warn("cpuCostStoreLoadFailed="+ex.GetType().Name);}
            _loaded=true;
            RuntimeLog.Info($"cpuCostStore=true;entries={Peaks.Count};path={StorePath};rawMeasurements=true;dedicatedBenchmark=false");
        }
    }
    internal static CpuCostSnapshot Capture()
    { lock(Sync)return new(Peaks.Values.Where(p=>p.Device==DeviceKey()).ToArray()); }
    internal static (int Workers, double Ratio, int Samples)[] DisplaySummary()
    {
        lock (Sync)
        {
            var peaks = Peaks.Values.Where(p => p.Device == DeviceKey() && Valid(p)).ToArray();
            var snapshot = new CpuCostSnapshot(peaks);
            return peaks.GroupBy(p => p.Workers).OrderBy(g => g.Key)
                .Select(g => (g.Key, snapshot.Resolve("", g.Key).Ratio, g.Count())).ToArray();
        }
    }
    internal static void Submit(IEnumerable<CpuLocalPeak> samples)
    {
        lock(Sync)
        {
            if(!_loaded)return;
            foreach(var p in samples.Where(Valid))
            {
                var key=(p.Device,p.Key);
                bool updated=!Peaks.TryGetValue(key,out var old)||p.NanosecondsPerInput<old.NanosecondsPerInput;
                if(updated){Peaks[key]=p;_dirty=true;}
                RuntimeLog.TryBackgroundInfo("cpuCostPeakDecision=true;decisionJson="+RuntimeLog.SafeJson(new{p.Key,p.Workers,p.Inputs,p.CanonicalMs,observedNs=p.NanosecondsPerInput,previousNs=old?.NanosecondsPerInput,updated,sameRunRepricing=false}));
            }
        }
    }
    internal static void TryFlushPendingOnMainThread()
    {
        lock(Sync)
        {
            if(!_loaded||!_dirty)return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath+".tmp",JsonSerializer.Serialize(new Document(1,Peaks.Values.ToList()),new JsonSerializerOptions{WriteIndented=true}));
                File.Move(StorePath+".tmp",StorePath,true);_dirty=false;
                RuntimeLog.Info("cpuCostStoreFlush=true;diskIoThread=GodotMain");
            }
            catch(Exception ex){RuntimeLog.Warn("cpuCostStoreFlushFailed="+ex.GetType().Name);}
        }
    }
}
