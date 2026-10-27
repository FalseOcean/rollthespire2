using Godot;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RolltheSpire2.Search.FamilyExecution;
public partial class Main : Node
{
 public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);}}
 void Run(){
  using var rd=RenderingServer.CreateLocalRenderingDevice();var asm=typeof(FamilyCandidateSet).Assembly;
  using var doc=JsonDocument.Parse(System.IO.File.ReadAllText(OS.GetCmdlineUserArgs().First()));
  int checks=0,zeros=0,nonzeros=0;
  foreach(var group in doc.RootElement.EnumerateArray().GroupBy(f=>f.GetProperty("family").GetString())){
   string name=group.Key=="A"?"AncientOptionFamily":group.Key=="T"?"TransformationAggregate":"EventResultFamily";
   var pt=asm.GetType("RolltheSpire2.Search.FamilyExecution."+name+(group.Key=="T"?"GpuPlan":"Plan"))!;
   var et=asm.GetType("RolltheSpire2.Search.FamilyExecution."+name+"GpuExecutor")!;
   var ctor=et.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single();
   var exec=et.GetMethod("Execute",BindingFlags.Instance|BindingFlags.NonPublic)!;
   object Plan(JsonElement f,bool bad=false,int capacity=64){
    var p=RuntimeHelpers.GetUninitializedObject(pt);
    uint[][] buffers=f.GetProperty("buffers").EnumerateArray().Select(a=>a.EnumerateArray().Select(x=>x.GetUInt32()).ToArray()).ToArray();
    if(bad&&group.Key=="A")buffers[2][1]=999u;
    void Set(string n,object v)=>pt.GetField("<"+n+">k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(p,v);
    Set("Buffers",buffers);if(group.Key=="T")Set("FullTarget",f.GetProperty("fullTarget").GetBoolean());else Set("Capacity",capacity);
    if(group.Key=="A")Set("ScratchWords",f.GetProperty("scratchWords").GetInt32());return p;
   }
   FamilyCandidateSet Execute(IDisposable e,FamilyCandidateSet input,CancellationToken token=default){object[] a={input,token,null!};return (FamilyCandidateSet)exec.Invoke(e,a)!;}
   foreach(var f in group){
    using var e=(IDisposable)ctor.Invoke(new object[]{rd,Plan(f),null!,null!});
    var m=f.GetProperty("batchMeta").EnumerateArray().Select(x=>x.GetUInt32()).ToArray();
    var batch=new SearchBatch(((ulong)m[1]<<32)|m[0],(int)m[2]);
    var input=m[4]==0?FamilyCandidateSet.Dense(batch):FamilyCandidateSet.FromAbi1(batch,f.GetProperty("ordinals").EnumerateArray().Select(x=>x.GetUInt64()).ToArray());
    var output=Execute(e,input);if(output.Count==0)zeros++;else nonzeros++;
    if(!output.ExportAbi1().ToArray().SequenceEqual(f.GetProperty("expected").EnumerateArray().Select(x=>x.GetUInt64())))throw new Exception("GPU parity "+f.GetProperty("name")+" mode="+m[4]);
    if(Execute(e,FamilyCandidateSet.FromAbi1(batch,[])).Count!=0)throw new Exception("Zero input");
    using var cts=new CancellationTokenSource();cts.Cancel();
    try{Execute(e,input,cts.Token);throw new Exception("Missing cancellation");}catch(TargetInvocationException x)when(x.InnerException is OperationCanceledException){}
    checks+=3;
   }
   var first=group.First(f=>f.GetProperty("expected").GetArrayLength()>0);
   using(var e=(IDisposable)ctor.Invoke(new object[]{rd,Plan(first),null!,null!})){
    // Deliberately corrupt an upstream ABI1 via reflection, outside public validation.
    // The GPU's range header must fault, never publish a partial result.
    var corrupt=(FamilyCandidateSet)typeof(FamilyCandidateSet).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single()
      .Invoke(new object[]{new SearchBatch(0,67),new ulong[]{999}});
    try{Execute(e,corrupt);throw new Exception("Missing range fault");}
    catch(TargetInvocationException x)when(x.InnerException is System.IO.InvalidDataException){checks++;GD.Print("AE_RANGE_FAULT_PASS family="+name);}
   }
   if(group.Key=="A")using(var e=(IDisposable)ctor.Invoke(new object[]{rd,Plan(first,true),null!,null!})){
    try{Execute(e,FamilyCandidateSet.Dense(new SearchBatch(0,67)));throw new Exception("Missing numeric fault");}
    catch(TargetInvocationException x)when(x.InnerException is System.IO.InvalidDataException){GD.Print("AE_NUMERIC_FAULT_PASS "+x.InnerException.Message);checks++;}
   }
   using(var e=(IDisposable)ctor.Invoke(new object[]{rd,Plan(first,capacity:65536),null!,null!})){
    Execute(e,FamilyCandidateSet.Dense(new SearchBatch(0,67)));
    using var cts=new CancellationTokenSource();cts.CancelAfter(10);
    try{Execute(e,FamilyCandidateSet.Dense(new SearchBatch(0,1<<24)),cts.Token);throw new Exception("Missing inflight cancellation");}
    catch(TargetInvocationException x)when(x.InnerException is OperationCanceledException){GD.Print("AE_CANCEL_PASS family="+name+" resultPublished=False");checks++;}
    Execute(e,FamilyCandidateSet.Dense(new SearchBatch(0,67)));checks++;
   }
   var disposed=(IDisposable)ctor.Invoke(new object[]{rd,Plan(first),null!,null!});disposed.Dispose();
   try{Execute(disposed,FamilyCandidateSet.Dense(new SearchBatch(0,1)));throw new Exception("Missing disposed guard");}
   catch(TargetInvocationException x)when(x.InnerException is ObjectDisposedException){checks++;}
   foreach(var f in group.Where(f=>OS.GetCmdlineUserArgs().Contains("--timing") &&f.GetProperty("name").GetString() is "TrashHeapGrabCard_CALTROPS" or "FakeMerchantOfferedFakeRelic_FAKE_ANCHOR" or "DARV_DUSTY_TOME" or "PAELUnion" or "PAEL_GOLDEN_PEARL").GroupBy(f=>f.GetProperty("name").GetString()).Select(g=>g.First())){
    using var e=(IDisposable)ctor.Invoke(new object[]{rd,Plan(f,capacity:1<<20),null!,null!});
    var input=FamilyCandidateSet.Dense(new SearchBatch(0,1<<20));
    Execute(e,input); // Cold compile/warmup excluded from the following diagnostic.
    double ms=0,read=0,sort=0;long bytes=0,outputs=0;
    for(int j=0;j<3;j++){
     object[] args={input,CancellationToken.None,null!};var output=(FamilyCandidateSet)exec.Invoke(e,args)!;object m=args[2];
     double D(string field)=>Convert.ToDouble(m.GetType().GetProperty(field)!.GetValue(m));
     ms+=D("CanonicalMs");read+=D("ReadbackMs");sort+=D("SortMs");bytes+=(long)D("ReadbackBytes");outputs+=output.Count;
    }
    GD.Print($"AE_TIMING name={f.GetProperty("name")};input={3L*(1<<20)};output={outputs};canonicalMs={ms};readbackMs={read};sortMs={sort};readbackBytes={bytes};localVulkanOnly=True;productionE2E=False");
   }
  }
  GD.Print($"AE_GPU_SUMMARY checks={checks};zero={zeros};nonzero={nonzeros};failures=0;Dense+Compact;AEWindow64;TDefaultWindow;tail;cancel+dispose");
 }
}
