using Godot;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RolltheSpire2.Search.FamilyExecution;
public partial class Main : Node
{
 public override void _Ready(){try{if(OS.GetCmdlineUserArgs().Contains("--variant-opening-executor"))RunVariantOpening();else Run();GetTree().Quit();}catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);}}
 void Run(){
  using var rd=RenderingServer.CreateLocalRenderingDevice();
  var asm=typeof(FamilyCandidateSet).Assembly;
  var pt=asm.GetType("RolltheSpire2.Search.FamilyExecution.WorldFamilyGpuPlan")!;
  var et=asm.GetType("RolltheSpire2.Search.FamilyExecution.WorldFamilyGpuExecutor")!;
  var ctor=et.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single(c=>c.GetParameters().Length==4&&c.GetParameters()[1].ParameterType==pt);
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

 // Opt-in reuse of this existing executor runner. The small forced transport
 // windows change no V numerical constants and keep the whole-population oracle.
 void RunVariantOpening(){
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  var asm=typeof(FamilyCandidateSet).Assembly;
  var et=asm.GetType("RolltheSpire2.Search.FamilyExecution.WorldFamilyGpuExecutor",true)!;
  var bt=asm.GetType("RolltheSpire2.Search.FamilyExecution.PrivateOrdinalBuffer",true)!;
  var nt=asm.GetType("RolltheSpire2.Search.FamilyExecution.GpuNumericalVariant",true)!;
  var raw=et.GetConstructors(flags).Single(c=>c.GetParameters().Length==10&&c.GetParameters()[1].ParameterType==typeof(uint[][]));
  var bufferCtor=bt.GetConstructors(flags).Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType==typeof(RenderingDevice));
  object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,flags)!.Invoke(target,args);
  Rid Buffer(object target)=>(Rid)bt.GetProperty("Buffer",flags)!.GetValue(target)!;
  string dir="D:/STS2Modding/RolltheSpire2/.build-evidence/v-gpu";
  string[] arguments=OS.GetCmdlineUserArgs();int pathIndex=Array.IndexOf(arguments,"--variant-fixtures");
  if(pathIndex>=0)dir=System.IO.Path.GetFullPath(arguments[pathIndex+1]);
  using var doc=JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir,"fixtures.json")));
  using var rd=RenderingServer.CreateLocalRenderingDevice()??throw new InvalidOperationException("V executor GPU unavailable");
  int checks=0;
  void Check(bool condition,string label){checks++;if(!condition)throw new InvalidDataException("V.Executor:"+label);}
  void Reject(Action action,Type expected,string label){
   try{action();}catch(TargetInvocationException exception) when(expected.IsInstanceOfType(exception.InnerException)){checks++;return;}
   throw new InvalidDataException("V.Executor.MissingRejection:"+label);
  }
  // Both local short-circuit orders in native; IntegerRne uses its public path.
  foreach(string name in new[]{"CombinedUnknownFirstNativeFloat64","CombinedVariantFirstNativeFloat64","CombinedUnknownFirstIntegerRne"}){
   var f=doc.RootElement.EnumerateArray().First(item=>item.GetProperty("name").GetString()==name&&item.GetProperty("batchMeta")[4].GetUInt32()==0);
   uint[][] packed=f.GetProperty("buffers").EnumerateArray().Select(array=>array.EnumerateArray().Select(value=>value.GetUInt32()).ToArray()).ToArray();
   uint[] meta=f.GetProperty("batchMeta").EnumerateArray().Select(value=>value.GetUInt32()).ToArray();
   ulong start=((ulong)meta[1]<<32)|meta[0];
   ulong[] oracle=f.GetProperty("expected").EnumerateArray().Select(value=>value.GetUInt64()).ToArray();
   string source=System.IO.File.ReadAllText(System.IO.Path.Combine(dir,f.GetProperty("shader").GetString()!));
   bool integer=name.EndsWith("IntegerRne",StringComparison.Ordinal);
   object numerical=Enum.Parse(nt,integer?"IntegerRne":"NativeFloat64");
   IDisposable New(object? input=null,object? output=null)=>(IDisposable)raw.Invoke(new object?[]{rd,packed,64,4,0,numerical,source,"V.VariantOpening",input is null?null:Buffer(input),output});
   void Population(FamilyCandidateSet actual,ulong[] expected,string label)=>Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected),name+":"+label);
   void Metrics(object metrics,int count,bool resident){
    int Read(string field)=>(int)metrics.GetType().GetProperty(field)!.GetValue(metrics)!;
    int windows=(count+63)/64,groups=(windows+3)/4;
    Check(Read("Dispatches")==windows&&Read("SubmissionGroups")==groups&&
     Read("HeaderReadCalls")== (resident&&count>0?1:groups),name+":WindowAndHeaderCounts");
    if(resident)Check(Read("PayloadReadCalls")==0,name+":NoResidentPayloadReadback");
   }
   FamilyCandidateSet Public(IDisposable executor,FamilyCandidateSet input){
    object?[] call={input,CancellationToken.None,null};
    var result=(FamilyCandidateSet)et.GetMethod("Execute",flags)!.Invoke(executor,call)!;
    Metrics(call[2]!,input.Count,false);return result;
   }
   using(var executor=New()){
    foreach(int count in new[]{257,67,0})Population(Public(executor,FamilyCandidateSet.Dense(new SearchBatch(start,count))),oracle.Where(value=>value<(ulong)count).ToArray(),"PublicDense"+count);
    var batch=new SearchBatch(start,1025);var ids=Enumerable.Range(0,257).Where(i=>i%3==0).Select(i=>(ulong)i).ToArray();
    Population(Public(executor,FamilyCandidateSet.FromAbi1(batch,ids)),oracle.Where(ids.Contains).ToArray(),"PublicCompact");
   }
   if(integer)continue; // Family binding admits resident ports only for Native.
   using var incoming=(IDisposable)bufferCtor.Invoke(new object?[]{rd});
   using var outgoing=(IDisposable)bufferCtor.Invoke(new object?[]{rd});
   void Upload(int count,bool corrupt=false){
    uint[] ids=Enumerable.Range(0,count).Reverse().Select(i=>(uint)i).ToArray();
    if(corrupt)ids[0]=1025;
    byte[] data=new byte[ids.Length*sizeof(uint)];System.Buffer.BlockCopy(ids,0,data,0,data.Length);
    if(data.Length>0&&rd.BufferUpdate(Buffer(incoming),0,(uint)data.Length,data)!=Error.Ok)throw new InvalidOperationException("V.Executor.Upload");
   }
   var compactBatch=new SearchBatch(start,1025);
   Upload(257);
   using(var terminal=New(incoming)){
    object?[] call={compactBatch,257,CancellationToken.None,null};
    var actual=(FamilyCandidateSet)et.GetMethod("ExecutePrivate",flags)!.Invoke(terminal,call)!;
    Metrics(call[3]!,257,false);Population(actual,oracle.Where(value=>value<257).ToArray(),"PrivateInputPublicOutput");
   }
   foreach(bool privateInput in new[]{false,true}){
    using var resident=New(privateInput?incoming:null,outgoing);
    foreach(int count in new[]{257,67,0}){
     Upload(count);
     var batch=privateInput?compactBatch:new SearchBatch(start,count);
     object?[] call={batch,count,CancellationToken.None,null};
     int output=(int)et.GetMethod("ExecutePrivateStage",flags)!.Invoke(resident,call)!;
     Metrics(call[3]!,count,true);
     Population((FamilyCandidateSet)Call(outgoing,"ReadForVerification",batch,output)!,oracle.Where(value=>value<(ulong)count).ToArray(),"Resident"+privateInput+":"+count);
    }
    using var cancellation=new CancellationTokenSource();cancellation.Cancel();
    Reject(()=>Call(resident,"ExecutePrivateStage",privateInput?compactBatch:new SearchBatch(start,67),67,cancellation.Token,null),typeof(OperationCanceledException),"ResidentCancellation");
    Upload(67);
    object?[] reuse={privateInput?compactBatch:new SearchBatch(start,67),67,CancellationToken.None,null};
    int reused=(int)et.GetMethod("ExecutePrivateStage",flags)!.Invoke(resident,reuse)!;
    Population((FamilyCandidateSet)Call(outgoing,"ReadForVerification",reuse[0],reused)!,oracle.Where(value=>value<67).ToArray(),"AfterCancellation");
    if(privateInput){
     Upload(67,true);
     Reject(()=>Call(resident,"ExecutePrivateStage",compactBatch,67,CancellationToken.None,null),typeof(InvalidDataException),"PrivateOrdinalRangeFault");
     Upload(67);
     object?[] clean={compactBatch,67,CancellationToken.None,null};
     int n=(int)et.GetMethod("ExecutePrivateStage",flags)!.Invoke(resident,clean)!;
     Population((FamilyCandidateSet)Call(outgoing,"ReadForVerification",compactBatch,n)!,oracle.Where(value=>value<67).ToArray(),"AfterStickyFaultReset");
    }
   }
   Reject(()=>{using var alias=New(incoming,incoming);},typeof(ArgumentException),"PrivateAlias");
  }
  GD.Print($"V_EXECUTOR_SUMMARY checks={checks};failures=0;forcedCapacity=64;K=4;counts=257,67,0;windows=5;groups=2;NativePrivate=true;IntegerRnePublicOnly=true;unorderedInput=true;reset=true;ABI1=true");
 }
}
