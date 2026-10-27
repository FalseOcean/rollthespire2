using Godot;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RolltheSpire2.Search.FamilyExecution;
public partial class Main : Node
{
 public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);}}
 void Run(){
  using var rd=RenderingServer.CreateLocalRenderingDevice();
  var asm=typeof(FamilyCandidateSet).Assembly;
  var pt=asm.GetType("RolltheSpire2.Search.FamilyExecution.WorldFamilyGpuPlan")!;
  var et=asm.GetType("RolltheSpire2.Search.FamilyExecution.WorldFamilyGpuExecutor")!;
  var ctor=et.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single();
  var exec=et.GetMethod("Execute",BindingFlags.Instance|BindingFlags.NonPublic)!;
  using var doc=JsonDocument.Parse(System.IO.File.ReadAllText("D:/STS2Modding/RolltheSpire2/.build-evidence/w-gpu/fixtures.json"));
  int checks=0,zeroOutputs=0,nonzeroOutputs=0;
  object Plan(JsonElement f,bool bad=false,int k=1,int capacity=64){
   var p=RuntimeHelpers.GetUninitializedObject(pt);
   uint[][] buffers=f.GetProperty("buffers").EnumerateArray().Select(a=>a.EnumerateArray().Select(x=>x.GetUInt32()).ToArray()).ToArray();
   if(bad)Array.Fill(buffers[5],uint.MaxValue);
   void Set(string n,object v)=>pt.GetField("<"+n+">k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(p,v);
   Set("Buffers",buffers);Set("ScratchWords",f.GetProperty("scratchWords").GetInt32());
   Set("VariantOnly",buffers[2][6]==0);Set("Capacity",capacity);Set("GroupingK",k);return p;
  }
  FamilyCandidateSet Execute(IDisposable e,FamilyCandidateSet input,CancellationToken token=default){object[] a={input,token,null!};var result=(FamilyCandidateSet)exec.Invoke(e,a)!;
   if(input.Count>0){
    object metrics=a[2];int Read(string n)=>(int)metrics.GetType().GetProperty(n)!.GetValue(metrics)!;
    int cap=(int)et.GetProperty("Capacity",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(e)!;
    int k=(int)et.GetProperty("GroupingK",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(e)!;
    int windows=(input.Count+cap-1)/cap,groups=(windows+k-1)/k;
    if(Read("Dispatches")!=windows||Read("SubmissionGroups")!=groups||Read("HeaderReadCalls")!=groups)throw new Exception("Grouping counts");
   }
   if(input.Count>0){if(result.Count==0)zeroOutputs++;else nonzeroOutputs++;}return result;}
  foreach(int k in new[]{1,4}) foreach(var f in doc.RootElement.EnumerateArray()){
   using var e=(IDisposable)ctor.Invoke(new object?[]{rd,Plan(f,k:k),null,null});
   var m=f.GetProperty("batchMeta").EnumerateArray().Select(x=>x.GetUInt32()).ToArray();
   var batch=new SearchBatch(((ulong)m[1]<<32)|m[0],(int)m[2]);
   var input=m[4]==0?FamilyCandidateSet.Dense(batch):FamilyCandidateSet.FromAbi1(batch,f.GetProperty("ordinals").EnumerateArray().Select(x=>x.GetUInt64()).ToArray());
   var output=Execute(e,input);
   if(!output.ExportAbi1().ToArray().SequenceEqual(f.GetProperty("expected").EnumerateArray().Select(x=>x.GetUInt64())))throw new Exception("Executor parity "+f.GetProperty("name"));
   foreach(int size in new[]{1,65,129,193,257}.Where(n=>n<=input.Count)){
    var prefix=input.IsDense?FamilyCandidateSet.Dense(new SearchBatch(batch.BatchBase,size)):
      FamilyCandidateSet.FromAbi1(batch,input.ExportAbi1().ToArray().Take(size).ToArray());
    var expected=f.GetProperty("expected").EnumerateArray().Select(x=>x.GetUInt64()).Where(i=>prefix.EnumerateLogicalOrdinals().Contains(i));
    if(!Execute(e,prefix).ExportAbi1().ToArray().SequenceEqual(expected))throw new Exception($"Prefix/tail parity K={k} size={size}");
    checks++;
   }
   if(Execute(e,FamilyCandidateSet.FromAbi1(batch,[])).Count!=0)throw new Exception("Zero input");
   using var cts=new CancellationTokenSource();cts.Cancel();
   try{Execute(e,input,cts.Token);throw new Exception("Missing cancellation");}
   catch(TargetInvocationException ex) when(ex.InnerException is OperationCanceledException){checks++;}
   checks+=2;
  }
  var first=doc.RootElement[0];
  foreach(int k in new[]{1,4}) using(var e=(IDisposable)ctor.Invoke(new object?[]{rd,Plan(first,true,k),null,null})){
   try{Execute(e,FamilyCandidateSet.Dense(new SearchBatch(0,67)));throw new Exception("Missing numeric fault");}
   catch(TargetInvocationException ex) when(ex.InnerException is System.IO.InvalidDataException){checks++;GD.Print("W_EXECUTOR_FAULT_PASS "+ex.InnerException.Message);}
  }
  foreach(int k in new[]{1,4}) using(var e=(IDisposable)ctor.Invoke(new object?[]{rd,Plan(first,k:k,capacity:195072),null,null})){
   // Warm pipeline, then cancel a real multi-group invocation. No result may escape.
   Execute(e,FamilyCandidateSet.Dense(new SearchBatch(0,67)));
   using var cts=new CancellationTokenSource();long requested=0;
   using var registration=cts.Token.Register(()=>Interlocked.Exchange(ref requested,System.Diagnostics.Stopwatch.GetTimestamp()));
   cts.CancelAfter(10);
   try{Execute(e,FamilyCandidateSet.Dense(new SearchBatch(0,1<<24)),cts.Token);throw new Exception("Missing in-flight cancellation");}
   catch(TargetInvocationException ex) when(ex.InnerException is OperationCanceledException){
    double ms=System.Diagnostics.Stopwatch.GetElapsedTime(Volatile.Read(ref requested)).TotalMilliseconds;
    GD.Print($"W_CANCEL_PASS K={k};cancelLatencyMs={ms};resultPublished=False");checks++;
   }
   // The same executor must be reusable after draining cancellation.
   if(Execute(e,FamilyCandidateSet.Dense(new SearchBatch(0,67))).Count!=67)throw new Exception("Post-cancel reuse");
   checks++;
  }
  foreach(int k in new[]{1,4}){
   var e=(IDisposable)ctor.Invoke(new object?[]{rd,Plan(first,k:k),null,null});
   e.Dispose();
   if(((System.Collections.ICollection)et.GetField("_owned",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(e)!).Count!=0)throw new Exception("Owned resources retained");
   try{Execute(e,FamilyCandidateSet.Dense(new SearchBatch(0,1)));throw new Exception("Execute after dispose");}
   catch(TargetInvocationException ex) when(ex.InnerException is ObjectDisposedException){checks++;}
  }
  if(zeroOutputs==0||nonzeroOutputs==0)throw new Exception("Output coverage missing");
  GD.Print($"W_OUTPUT_COVERAGE zero={zeroOutputs};nonzero={nonzeroOutputs}");
  GD.Print("W_EXECUTOR_SUMMARY checks="+checks+" failures=0; K=1,4; forcedCapacity=64; tailGroups=1,2,3; fullGroup=4; counts=true; multichunk=true; zeroInput=true; cancellation=true; numericFault=true");
 }
}
