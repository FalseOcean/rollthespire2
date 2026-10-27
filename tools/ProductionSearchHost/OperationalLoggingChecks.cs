using System.Reflection;
using RolltheSpire2.Infrastructure.Snapshots;


internal static class OperationalLoggingChecks
{
    internal static async Task RunAsync(bool simulateIoFailure)
    {
        int checks=0;
        void Check(bool pass,string name) { checks++; if(!pass)throw new InvalidOperationException(name); }
        using var scope=Log.SearchScope("logging-check-session");
        Log.Info("loggingCheckDefault=true");
        Log.Detail("loggingCheckDetail=true");
        await Task.WhenAll(Enumerable.Range(0,4).Select(i=>Task.Run(()=> {
            for(int n=0;n<20;n++) Log.TryBackgroundInfo($"loggingCheckWorker=true;worker={i};n={n}");
        })));
        Log.Flush();
        string text=ReadLiveLog();
        Check(text.Contains("loggingCheckDefault=true;logSession=logging-check-session"),"correlated default file evidence");
        Check(text.Contains("loggingCheckDetail=true")==Log.DetailEnabled,"detail opt-in persisted");
        Check(text.Split('\n').Count(l=>l.Contains("loggingCheckWorker=true") && l.Contains("logSession=logging-check-session"))==80,"worker events complete and correlated");
        Exception fault;
        try { throw new InvalidOperationException("OuterFixture",new IOException("InnerFixture")); }
        catch(Exception ex){fault=ex;}
        Log.Fault("searchFault=true;phase=LoggingFixture;plan=fixture;safeCursor=123",fault);
        Log.Flush();
        text=ReadLiveLog();
        Check(text.Contains("InnerFixture") && text.Contains("exceptionJson=") && text.Contains("OperationalLoggingChecks"),"full inner exception and stack persisted");
        if(simulateIoFailure)
        {
            // Test-only injection in the existing sink, never a production path override.
            var field=Log.Store.GetField("_writer",BindingFlags.Static|BindingFlags.NonPublic)!;
            var original=field.GetValue(null);
            var broken=new StreamWriter(new BrokenStream());
            field.SetValue(null,broken);
            try {
                Check(!Log.TryBackgroundWarning("loggingInjectedIoFailure=true"),"file failure isolated");
                Log.TryBackgroundWarning("loggingInjectedIoFailure=true");
                Check(Log.LastFailureCode.Contains("OperationalLog"),"failure observable");
            } finally {field.SetValue(null,original); try {broken.Dispose();} catch {}}
        }
        Log.Info($"loggingChecks=PASS;checks={checks};detail={Log.DetailEnabled};ioFailure={simulateIoFailure}");
        Log.Flush();
    }
    internal static RolltheSpire2.Search.Runtime.ProductionSearchSession CreateExactFaultSession(RolltheSpire2.Search.Contracts.ExactSearchExecutionRequest request, bool cpuFault = false)
    {
        // Reuse the current session's existing evaluator injection seam; no Product switch.
        using var scope=Log.SearchScope("logging-exact-fault-check");
        ProductionSearchReplay.LogSessionWorkload(request,"logging-exact-fault-check");
        var plan=RolltheSpire2.Search.FamilyExecution.FamilyExecutionCoordinator.Plan(request, gpuAvailable: cpuFault ? false : null);
        var eta=RolltheSpire2.Search.FamilyExecution.FamilySearchEtaProjectorV1.Project(request,plan);
        if(cpuFault)
        {
            var families=plan.OrderedFamilies;
            var cpu=families.First(f=>f.GetType().Name=="FamilyCpuExecution");
            cpu.GetType().GetField("_matches",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(cpu,
                (Func<ulong,bool>)(_=>throw new InvalidOperationException("CpuSelectedFaultFixture")));
            return new(request,plan,eta,65536,64,null,"",failOnPhysicalRecovery:true);
        }
        return new(request,plan,eta,16777216,64,null,"",failOnPhysicalRecovery:true,
            exactEvaluator:(_,_)=>throw new InvalidOperationException("LoggingExactFaultFixture"));
    }
    private static string ReadLiveLog()
    { using var reader=new StreamReader(new FileStream(Log.CurrentLogPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)); return reader.ReadToEnd(); }
    private static class Log
    {
        private static readonly Type Facade=typeof(ProductionSearchReplay).Assembly.GetType("RolltheSpire2.Bootstrap.RuntimeLog",true)!;
        internal static readonly Type Store=typeof(ProductionSearchReplay).Assembly.GetType("RolltheSpire2.Bootstrap.OperationalFileLog",true)!;
        private static object? Call(string method,params object?[] args)=>Facade.GetMethod(method,BindingFlags.Public|BindingFlags.Static)!.Invoke(null,args);
        internal static IDisposable SearchScope(string id)=>(IDisposable)Call("SearchScope",id)!;
        internal static string CurrentLogPath=>(string)Facade.GetProperty("CurrentLogPath")!.GetValue(null)!;
        internal static string LastFailureCode=>(string)Facade.GetProperty("LastFailureCode")!.GetValue(null)!;
        internal static bool DetailEnabled=>(bool)Facade.GetField("DetailEnabled")!.GetValue(null)!;
        internal static void Info(string s)=>Call("Info",s);
        internal static void Detail(string s)=>Call("Detail",s);
        internal static bool TryBackgroundInfo(string s)=>(bool)Call("TryBackgroundInfo",s)!;
        internal static bool TryBackgroundWarning(string s)=>(bool)Call("TryBackgroundWarning",s)!;
        internal static void Fault(string s,Exception ex)=>Call("Fault",s,ex);
        internal static void Flush()=>Store.GetMethod("Flush")!.Invoke(null,null);
    }
    private sealed class BrokenStream:MemoryStream
    {
        public override void Write(byte[] b,int o,int c)=>throw new IOException("Injected logging failure");
        public override void Write(ReadOnlySpan<byte> b)=>throw new IOException("Injected logging failure");
        public override void Flush()=>throw new IOException("Injected logging failure");
    }
}
