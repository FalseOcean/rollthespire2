using Expression = System.Linq.Expressions.Expression;
using Environment = System.Environment;
using RolltheSpire2.Compatibility;
using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Focused checks in the existing Production host, using the real N executors and CPU reference.
internal static class DirectNeowChecks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static void Run(ExactSearchExecutionRequest request, string output)
    {
        if (OS.GetCmdlineUserArgs().Contains("--gpu-resource-failure-checks", StringComparer.Ordinal))
        {
            RunResourceFailureChecks(output);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--n-initialization-checks", StringComparer.Ordinal))
        {
            RunInitializationChecks(request, output);
            return;
        }
        var assembly=typeof(FamilyCandidateSet).Assembly;
        Type T(string name)=>assembly.GetType("RolltheSpire2.Search.FamilyExecution."+name,true)!;
        object Get(object obj,string name)=>obj.GetType().GetProperty(name,Flags)?.GetValue(obj)
            ?? obj.GetType().GetField(name,Flags)!.GetValue(obj)!;
        var compile=T("NeowReplayPlan").GetMethod("Compile",Flags)!;
        object replay=compile.Invoke(null,[request,false])!;
        var root=Expression.Parameter(typeof(ulong));
        var cpu=Expression.Lambda<Func<ulong,bool>>(Expression.Call(T("NeowFamilyReplay").GetMethod("Matches",Flags)!,root,
            Expression.Constant(replay,replay.GetType())),root).Compile();
        var rows=new List<object>();
        var format=(Func<ulong,string>)assembly.GetType("RolltheSpire2.Search.FamilyExecution.Beta110SeedCodec",true)!
            .GetMethod("FormatOrdinal",Flags)!.CreateDelegate(typeof(Func<ulong,string>));
        if (Convert.ToByte(Get(replay,"Selected"))==5)
        {
            // Bounded CPU reference observations, NOT GPU branch timings or pricing.
            // Keep all stream prerequisites when observing one predicate at a time.
            var observe=T("NeowFamilyReplay").GetMethod("Observe",Flags)!;
            var observation=Expression.Lambda<Func<ulong,object>>(Expression.Convert(Expression.Call(observe,root,
                Expression.Constant(replay,replay.GetType())),typeof(object)),root).Compile();
            var route=T("NeowFamilyReplay").GetMethod("Route",Flags)!;
            var conditionsProperty=replay.GetType().GetProperty("StructuredConditions",Flags)!;
            var conditions=(Array)conditionsProperty.GetValue(replay)!;
            var probes=new List<object>{replay};
            for(int i=0;i<conditions.Length;i++)
            {
                object copy=typeof(object).GetMethod("MemberwiseClone",Flags)!.Invoke(replay,null)!;
                Array one=Array.CreateInstance(conditions.GetType().GetElementType()!,1);one.SetValue(conditions.GetValue(i),0);
                conditionsProperty.SetValue(copy,one);probes.Add(copy);
            }
            long pairs=0;long[] forward=new long[probes.Count],reverse=new long[probes.Count],both=new long[probes.Count];
            const int sample=1<<20;
            for(ulong i=0;i<sample;i++)
            {
                ulong hash=Beta111Profile.Instance.ComputeRootSeed(format(i));object obs=observation(hash);
                if(!(bool)Get(obs,"IdentityPass")) continue;
                pairs++;object a=Get(obs,"BonesFirst"),b=Get(obs,"BonesSecond"),rewards=Get(obs,"RewardsAfterBones");
                for(int j=0;j<probes.Count;j++)
                {
                    bool f=(bool)route.Invoke(null,[hash,probes[j],a,b,rewards])!;
                    bool r=(bool)route.Invoke(null,[hash,probes[j],b,a,rewards])!;
                    if(f)forward[j]++;if(r)reverse[j]++;if(f&&r)both[j]++;
                }
            }
            File.WriteAllText(Path.Combine(output,"bones-route-reference.json"),JsonSerializer.Serialize(new {
                sample,pairs,forward,reverse,both,order="offer/reverse (diagnostic includes both even for pinned query)",
                columns="complete predicate, then each authored predicate; original stream prerequisites retained",
                timingAuthority=false },new JsonSerializerOptions{WriteIndented=true}));
        }
        var baseline=new Dictionary<string,ulong[]>();
        int witnessWindows=0;
        var inputs=new List<(string Name,FamilyCandidateSet Input)>();
        inputs.Add(("full",FamilyCandidateSet.Dense(new SearchBatch(1UL<<24,1<<24))));
        inputs.Add(("cpu",FamilyCandidateSet.Dense(new SearchBatch(12345,1<<19))));
        inputs.Add(("tail",FamilyCandidateSet.Dense(new SearchBatch((1UL<<24)-31,67))));
        inputs.Add(("zero",FamilyCandidateSet.FromAbi1(new SearchBatch(0,67),Array.Empty<ulong>())));
        inputs.Add(("compact",FamilyCandidateSet.FromAbi1(new SearchBatch(12345,1<<19),Enumerable.Range(0,1<<17).Select(i=>(ulong)i*3).ToArray())));
        // Compare complete GPU populations across the workload's whole closed interval.
        // CPU checks remain bounded, with exact witness neighborhoods added below.
        for(ulong start=0;start<(ulong)request.ScanCount;start+=1UL<<24)
            inputs.Add(("interval"+start,FamilyCandidateSet.Dense(new SearchBatch(start,(int)Math.Min(1UL<<24,(ulong)request.ScanCount-start)))));
        var expectedCpu=inputs.Where(x=>x.Name is "cpu" or "compact" or "tail" or "zero").ToDictionary(x=>x.Name,
            x=>x.Input.EnumerateLogicalOrdinals().Where(i=>cpu(Beta111Profile.Instance.ComputeRootSeed(format(x.Input.Batch.BatchBase+i)))).ToArray());
        if(Convert.ToByte(Get(replay,"Selected"))==14)
        {
            var hits=expectedCpu["cpu"].ToHashSet();
            int group1Only=0,group1OnlyKept=0,sameGroupOnly=0,sameGroupOnlyKept=0;
            var authority=Get(replay,"Authority");
            int slot=Convert.ToInt32(Get(authority,"PlayerSlotIndex"));
            float rare=Convert.ToInt32(Get(authority,"Ascension"))>=7 ? .0149f : .03f;
            foreach(ulong ordinal in inputs[1].Input.EnumerateLogicalOrdinals())
            {
                ulong hash=Beta111Profile.Instance.ComputeRootSeed(format(inputs[1].Input.Batch.BatchBase+ordinal));
                var rng=new Xoshiro256StarStar(Beta111Profile.Instance.DeriveNamedStreamSeed(hash+(ulong)slot,"rewards"));
                int first=0,second=0;
                for(int i=0;i<6;i++) { if(rng.NextFloat()<rare) {if(i<3)first++;else second++;} rng.Advance(2); }
                if(first==0 && second>0) { group1Only++; if(hits.Contains(ordinal))group1OnlyKept++; }
                if((first>=2 && second==0)||(second>=2 && first==0)) { sameGroupOnly++; if(hits.Contains(ordinal))sameGroupOnlyKept++; }
            }
            if(group1Only==0 || sameGroupOnly==0) throw new Exception("KaleidoscopeBoundaryPopulationMissing");
            var condition=((Array)Get(replay,"StructuredConditions")).GetValue(0)!;
            int count=Convert.ToInt32(Get(condition,"TargetCount"));
            var rarity=(byte[])Get(Get(authority,"EffectCatalog"),"CardRarityByDenseId");
            bool twoRare=count==2 && rarity[Convert.ToInt32(Get(condition,"Target0"))]==3 && rarity[Convert.ToInt32(Get(condition,"Target1"))]==3;
            if(twoRare && (group1OnlyKept!=0 || sameGroupOnlyKept!=0)) throw new Exception("KaleidoscopeCrossGroupReferenceMismatch");
            if(count==1 && group1OnlyKept==0) throw new Exception("KaleidoscopeGroup1OnlyWitnessMissing");
            File.WriteAllText(Path.Combine(output,"kaleidoscope-boundaries.json"),JsonSerializer.Serialize(new{group1Only,group1OnlyKept,sameGroupOnly,sameGroupOnlyKept}));
        }
        string? previous=Environment.GetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT");
        string? previousBones=Environment.GetEnvironmentVariable("RT2_N_BONES_EXPERIMENT");
        using var rd=RenderingServer.CreateLocalRenderingDevice() ?? throw new Exception("DirectNDeviceMissing");
        try
        {
            string[] modes=Convert.ToByte(Get(replay,"Selected")) switch {
                5 => ["baseline","early-accept","rare-local","pair-local","default"],
                16 => ["baseline","lost-direct","lost-local","lost-replay","lost-continue","lost-potion-first","default"],
                15 => ["baseline","lead-direct","lead-local","lead-first","default"],
                10 => ["baseline","arcane-direct","arcane-first","arcane-index-local","arcane-index-first","default"],
                20 => ["baseline","phial-direct","phial-local","phial-first","default"],
                2 => ["baseline","default"],
                19 => ["baseline","new-replay","new-pass","default"],
                22 => ["baseline","scroll-first","scroll-local","default"],
                _ => ["baseline","identity-direct","replay","continue","both","local","default"] };
            foreach(string mode in modes)
            {
                bool bones=Convert.ToByte(Get(replay,"Selected"))==5;
                Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT",bones ? "baseline" : mode);
                if(bones) Environment.SetEnvironmentVariable("RT2_N_BONES_EXPERIMENT",mode=="default" ? null : mode);
                object?[] packed=[replay,null,""];
                if(!(bool)T("NeowFamilyGpuPlan").GetMethod("TryCreate",Flags)!.Invoke(null,packed)!) throw new Exception("DirectNPlan:"+packed[2]);
                var plan=packed[1]!;
                using var executor=(IDisposable)T("NeowFamilyGpuExecutor").GetConstructors(Flags).Single().Invoke([rd,plan,73u,null,null,null,0,0,null,null,null]);
                var execute=executor.GetType().GetMethod("Execute",Flags)!;
                for(int inputIndex=0;inputIndex<inputs.Count;inputIndex++)
                {
                    var item=inputs[inputIndex];
                    object?[] values=[item.Input,CancellationToken.None,null];
                    var actual=(FamilyCandidateSet)execute.Invoke(executor,values)!;
                    ulong[] ordinals=actual.EnumerateLogicalOrdinals().ToArray();
                    if(mode=="baseline") baseline[item.Name]=ordinals;
                    if(mode=="baseline" && (item.Name=="full" || item.Name.StartsWith("interval",StringComparison.Ordinal)) && witnessWindows<8)
                    {
                        foreach(ulong ordinal in ordinals.Take(8-witnessWindows))
                        {
                            ulong absolute=item.Input.Batch.BatchBase+ordinal;
                            string name="witness"+witnessWindows++;
                            var witness=FamilyCandidateSet.Dense(new SearchBatch(absolute-Math.Min(absolute,4UL),9));
                            inputs.Add((name,witness));
                            expectedCpu[name]=witness.EnumerateLogicalOrdinals().Where(i=>cpu(Beta111Profile.Instance.ComputeRootSeed(format(witness.Batch.BatchBase+i)))).ToArray();
                        }
                    }
                    if(!ordinals.SequenceEqual(baseline[item.Name])) throw new Exception("DirectNGpuParity:"+mode+":"+item.Name);
                    if(expectedCpu.TryGetValue(item.Name,out var expected) && !ordinals.SequenceEqual(expected)) throw new Exception("DirectNCpuParity:"+mode+":"+item.Name);
                    rows.Add(new{mode,selected=Get(plan,"DirectNestedMode"),shape=item.Name,input=item.Input.Count,output=ordinals.Length,parity=true,metrics=values[2]});
                }
                using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
                try { execute.Invoke(executor,[inputs[2].Input,cancelled.Token,null]); throw new Exception("CancelledNExecuted"); }
                catch(TargetInvocationException ex) when(ex.InnerException is OperationCanceledException) { }
                object?[] again=[inputs[2].Input,CancellationToken.None,null];
                if(!((FamilyCandidateSet)execute.Invoke(executor,again)!).EnumerateLogicalOrdinals().SequenceEqual(baseline["tail"])) throw new Exception("CancelReuseParity");
            }
        }
        finally {
            Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT",previous);
            Environment.SetEnvironmentVariable("RT2_N_BONES_EXPERIMENT",previousBones);
        }
        File.WriteAllText(Path.Combine(output,"direct-n-checks.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("DirectNeowChecks=PASS;fullGpuPopulation=true;cpuDenseCompact=true;tail=true;zero=true;cancelReuse=true;checks="+rows.Count);
    }

    // Native failure boundaries only. This does not simulate device loss, OOM,
    // or the reporters' driver/compiler failure, and does not rerun N fixtures.
    private static void RunResourceFailureChecks(string output)
    {
        Type utility = typeof(FamilyCandidateSet).Assembly.GetType(
            "RolltheSpire2.Search.FamilyExecution.FamilyGpuComputeUtility", true)!;
        object? Invoke(string method, params object?[] args) => utility.GetMethod(method, Flags)!.Invoke(null, args);
        var failures = new List<string>();
        void ExpectFailure(string operation, Action action)
        {
            try { action(); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException failure &&
                failure.Message.StartsWith("FamilyGpu." + operation + "Failed:", StringComparison.Ordinal))
            {
                failures.Add(operation);
                Console.WriteLine("GPU_RESOURCE_EXPECTED_FAILURE operation=" + operation);
                return;
            }
            throw new InvalidDataException("GpuResourceFailure.NotRejectedAtCreation:" + operation);
        }
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("GpuResourceFailure.DeviceMissing");
        ExpectFailure("ShaderCompile", () => Invoke("CompileShader", rd,
            "#version 450\nthis_is_not_valid_glsl", "NativeFailureChecks.BadShader"));
        ExpectFailure("ComputePipeline", () => Invoke("CreateComputePipeline", rd,
            default(Rid), "NativeFailureChecks.EmptyShader"));
        var owned = new List<Rid>();
        Rid Own(object? value) { var rid = (Rid)value!; owned.Add(rid); return rid; }
        try
        {
            Rid shader = Own(Invoke("CompileShader", rd,
                "#version 450\nlayout(local_size_x=1) in;\nlayout(set=0,binding=0,std430) buffer Data { uint values[]; } data;\nvoid main(){ data.values[0]=42u; }",
                "NativeFailureChecks.ValidControl"));
            ExpectFailure("UniformSet", () => Invoke("CreateUniformSet", rd, shader,
                new Rid[] { default }, "NativeFailureChecks.EmptyBuffer"));
            Rid pipeline = Own(Invoke("CreateComputePipeline", rd, shader, "NativeFailureChecks.ValidControl"));
            Rid buffer = Own(Invoke("CreateStorageBuffer", rd, new byte[4], "NativeFailureChecks.ControlOutput"));
            Rid uniforms = Own(Invoke("CreateUniformSet", rd, shader, new[] { buffer }, "NativeFailureChecks.ValidControl"));
            long list = rd.ComputeListBegin();
            try
            {
                rd.ComputeListBindComputePipeline(list, pipeline);
                rd.ComputeListBindUniformSet(list, uniforms, 0);
                rd.ComputeListDispatch(list, 1, 1, 1);
            }
            finally { rd.ComputeListEnd(); }
            Invoke("SubmitAndSync", rd, "NativeFailureChecks.ValidControl");
            byte[] data = rd.BufferGetData(buffer);
            if (data.Length != 4 || BitConverter.ToUInt32(data) != 42u)
                throw new InvalidDataException("GpuResourceFailure.ValidControlFailed");
        }
        finally { Invoke("FreeAll", rd, owned); }
        File.WriteAllText(Path.Combine(output, "gpu-resource-failure-checks.json"), JsonSerializer.Serialize(new
        {
            expectedNativeFailures = failures, validControlValue = 42,
            invalidHandlesRejectedBeforeDispatch = true, simulatesDriverLossOrOutOfMemory = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("GpuResourceFailureChecks=PASS;expectedFailures=3;validControl=42;invalidHandlesNotSubmitted=true");
    }

    private static void RunInitializationChecks(ExactSearchExecutionRequest request, string output)
    {
        var assembly = typeof(FamilyCandidateSet).Assembly;
        Type T(string name) => assembly.GetType("RolltheSpire2.Search.FamilyExecution." + name, true)!;
        object replay = T("NeowReplayPlan").GetMethod("Compile", Flags)!.Invoke(null, [request, false])!;
        object?[] packed = [replay, null, ""];
        if (!(bool)T("NeowFamilyGpuPlan").GetMethod("TryCreate", Flags)!.Invoke(null, packed)!)
            throw new InvalidOperationException("NInitialization.PlanUnavailable:" + packed[2]);
        object plan = packed[1]!;
        if (!(bool)plan.GetType().GetProperty("UsesStagedDense", Flags)!.GetValue(plan)!)
            throw new InvalidOperationException("NInitialization.RequiresStagedWorkload");
        var root = Expression.Parameter(typeof(ulong));
        var cpu = Expression.Lambda<Func<ulong, bool>>(Expression.Call(T("NeowFamilyReplay").GetMethod("Matches", Flags)!,
            root, Expression.Constant(replay, replay.GetType())), root).Compile();
        var format = (Func<ulong, string>)T("Beta110SeedCodec").GetMethod("FormatOrdinal", Flags)!
            .CreateDelegate(typeof(Func<ulong, string>));
        const int sample = 65539;
        var batch = new SearchBatch(12345, sample);
        var dense = FamilyCandidateSet.Dense(batch);
        var compact = FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, sample)
            .Where(i => i % 3 == 1 || i == sample - 1).Select(i => (ulong)i).ToArray());
        var empty = FamilyCandidateSet.Dense(new SearchBatch(batch.BatchBase, 0));
        var expected = dense.EnumerateLogicalOrdinals().Where(i =>
            cpu(Beta111Profile.Instance.ComputeRootSeed(format(batch.BatchBase + i)))).ToHashSet();
        Type executorType = T("NeowFamilyGpuExecutor");
        var execute = executorType.GetMethod("Execute", Flags)!;
        var setupProperty = executorType.GetProperty("SetupMs", Flags)!;
        var pipelineField = executorType.GetField("_pipeline", Flags)!;
        var stagesField = executorType.GetField("_stages", Flags)!;
        var stagedBuffersField = executorType.GetField("_stagedBuffers", Flags)!;
        var rows = new List<object>();
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("NInitialization.DeviceMissing");
        foreach (bool compactFirst in new[] { true, false })
        {
            string sequence = compactFirst ? "CompactDenseCompact" : "DenseCompact";
            using var executor = (IDisposable)executorType.GetConstructors(Flags).Single()
                .Invoke([rd, plan, 73u, null, null, null, 0, 0, null, null, null]);
            double Setup() => (double)setupProperty.GetValue(executor)!;
            void CheckState(bool ordinary, int stages)
            {
                int actualStages = ((System.Collections.ICollection)stagesField.GetValue(executor)!).Count;
                if (((Rid)pipelineField.GetValue(executor)!).IsValid != ordinary || actualStages != stages ||
                    (stagedBuffersField.GetValue(executor) is not null) != (stages != 0))
                    throw new InvalidOperationException("NInitialization.UnexpectedModules:" + sequence);
            }
            void CheckInput(string step, FamilyCandidateSet input, bool ordinary, int stages, bool setupGrows)
            {
                double before = Setup();
                object?[] args = [input, CancellationToken.None, null];
                var actual = (FamilyCandidateSet)execute.Invoke(executor, args)!;
                ulong[] truth = input.EnumerateLogicalOrdinals().Where(expected.Contains).ToArray();
                if (!actual.EnumerateLogicalOrdinals().SequenceEqual(truth))
                    throw new InvalidDataException("NInitialization.CpuPopulationMismatch:" + sequence + ":" + step);
                CheckState(ordinary, stages);
                double after = Setup();
                if (setupGrows ? after <= before : after != before)
                    throw new InvalidDataException("NInitialization.SetupAccounting:" + sequence + ":" + step);
                rows.Add(new { sequence, step, input = input.Count, survivors = actual.Count, ordinary, stages,
                    setupMs = after, setupAddedMs = after - before, parity = true, metrics = args[2] });
            }
            CheckState(false, 0);
            CheckInput("EmptyBeforeInitialization", empty, false, 0, false);
            double beforeCancellation = Setup();
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try
                {
                    execute.Invoke(executor, [dense, cancelled.Token, null]);
                    throw new InvalidOperationException("NInitialization.CancellationIgnored");
                }
                catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { }
            }
            CheckState(false, 0);
            if (Setup() != beforeCancellation) throw new InvalidDataException("NInitialization.CancelledSetup");
            if (compactFirst)
            {
                CheckInput("CompactFirst", compact, true, 0, true);
                CheckInput("DenseAfterCompact", dense, true, 3, true);
                CheckInput("CompactReuse", compact, true, 3, false);
            }
            else
            {
                CheckInput("DenseFirst", dense, false, 3, true);
                CheckInput("CompactAfterDense", compact, true, 3, true);
            }
            CheckInput("EmptyAfterInitialization", empty, true, 3, false);
        }
        File.WriteAllText(Path.Combine(output, "n-initialization-checks.json"), JsonSerializer.Serialize(new
        {
            sample, batchBase = batch.BatchBase, denseSurvivors = expected.Count, compactPopulation = compact.Count,
            fullCpuReference = true, cancelledBeforeInitialization = true, rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"NeowInitializationChecks=PASS;sample={sample};cpuSurvivors={expected.Count};compactDenseCompact=true;denseCompact=true;empty=true;cancelNoInit=true;setupSeparated=true;checks={rows.Count}");
    }
}
