using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Offline numerical decomposition, NOT a Production candidate or calibration source.
// Reuses live executors, metadata, dispatch geometry and entire predicate bodies.
// Reflection/pipeline substitution is confined to this explicit host diagnostic.
internal static class Y2RootCostProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type T(string name) => typeof(FamilyCandidateSet).Assembly.GetType("RolltheSpire2.Search.FamilyExecution." + name, true)!;
    private static object New(string name, params object?[] args) => T(name).GetConstructors(Instance).Single().Invoke(args);
    private static object Get(object o, string name) => o.GetType().GetProperty(name, Instance)?.GetValue(o)
        ?? o.GetType().GetField(name, Instance)!.GetValue(o)!;
    private static object? Call(object o, string name, params object?[] args) => o.GetType().GetMethod(name, Instance)!.Invoke(o, args);
    private static object? StaticCall(string type, string name, params object?[] args) => T(type).GetMethods(Static)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(null, args);
    private static string Shader(string suffix) => (string)StaticCall("FamilyGpuComputeUtility", "LoadEmbeddedShader", suffix)!;
    private static string SharedShader(string suffix) => (string)StaticCall("FamilyGpuComputeUtility", "LoadFamilyShaderWithVisibleSeedRootHash", suffix)!;
    private static string Replace(string text, string from, string to)
    {
        if (!text.Contains(from, StringComparison.Ordinal)) throw new InvalidOperationException("Y2 source seam changed: " + from);
        return text.Replace(from, to, StringComparison.Ordinal);
    }
    private static Rid Compile(RenderingDevice rd, string text) => (Rid)StaticCall("FamilyGpuComputeUtility", "CompileShader", rd, text, "Y2.DiagnosticOnly")!;
    private static Rid Uniforms(RenderingDevice rd, Rid shader, Rid[] buffers) =>
        FamilyGpuComputeUtility.CreateUniformSet(rd, shader, buffers, "Y2.DiagnosticOnly");
    private static Rid Buffer(RenderingDevice rd, byte[] bytes) => rd.StorageBufferCreate((uint)Math.Max(4, bytes.Length), bytes);
    private static string Digest(FamilyCandidateSet set) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(set.ExportAbi1().Span)));

    internal static void Run(ExactSearchExecutionRequest request, string kind, string output)
    {
        if (kind == "N-trials") { ShionNeowOptimizationChecks.Run(request, output); return; }
        if (kind == "N-readback") { NeowSmallReadbackChecks.Run(request, output); return; }
        if (kind == "E-readback") { EventReadbackProbe.Run(request, output); return; }
        if (kind == "N-readyT") { NeowReadyStageProbe.Run(request, output); return; }
        const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        ulong start = 0; foreach (char c in request.CanonicalStartSeed) start = checked(start * 34 + (uint)alphabet.IndexOf(c));
        int count = (int)Math.Min(1 << 24, request.ScanCount);
        var batch = new SearchBatch(start, count);
        var input = FamilyCandidateSet.Dense(batch);
        var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true);
        IFamilyInvocation Family(string id) => families.Single(f => f.FamilyId == id);
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("GPU unavailable");
        IDisposable? producer = null, privateBuffer = null;
        IDisposable executor;
        string source, pipelineField = "_pipeline";
        bool compact = kind == "NC";
        uint[] ordinals = [];
        object NPlan() => Get(Family("N.Neow"), "GpuPlan");
        if (kind == "N")
        {
            var plan = NPlan();
            if ((bool)Get(plan, "UsesStagedDense")) throw new InvalidOperationException("Y2 N probe requires non-staged query");
            executor = new NeowFamilyGpuExecutor(rd, (NeowFamilyGpuPlan)plan, 42u);
            source = NeowFamilyGpuExecutor.ShaderSource(leafyPreGate: (bool)Get(plan, "UsesLeafyPreGate"),
                directNestedMode: (int)Get(plan,"DirectNestedMode"));
        }
        else if (kind.StartsWith("S", StringComparison.Ordinal))
        {
            bool stable = kind == "S-stable";
            var authority = RolltheSpire2.Core.Merchant.Beta111MerchantColorlessAuthority.From(request.Authority);
            if (authority.UncommonPool.Distinct(RolltheSpire2.Core.Identity.ModelKeyComparer.Instance).Count() != authority.UncommonPool.Count ||
                authority.RarePool.Distinct(RolltheSpire2.Core.Identity.ModelKeyComparer.Instance).Count() != authority.RarePool.Count)
                throw new InvalidOperationException("Y2 S substitutions require unique pools; use s-identity-check for canonical-map fixtures");
            executor = (IDisposable)StaticCall("MerchantShopColorlessGpuExecutor", "Create", rd, request,
                Enum.Parse(T("MerchantShopColorlessCompactionPath"), stable ? "StableOrderedCompaction" : "AtomicAppendHostSort"))!;
            source = SharedShader("MerchantShopColorless.comp.glsl");
            source = MerchantShopColorlessGpuExecutor.ConfigureEarlyRejection(source, request.Evaluation,
                request.Authority.PlayersCount);
            if (stable) { source = (string)StaticCall("MerchantShopColorlessGpuExecutor", "EnableStableOrderedCompaction", source)!; pipelineField = "_stablePhase1Pipeline"; }
        }
        else if (kind == "R")
        {
            var plan = (RelicFamilyPlan)Get(Family("R.Relic"), "_plan");
            executor = RelicFamilyGpuExecutor.Create(rd, request, plan);
            source = RelicFamilyGpuExecutor.ShaderSource(false, localStateCapacity: plan.LocalStateCapacity);
        }
        else if (kind is "C" or "NC")
        {
            var plan = Get(Family("C.CombatReward"), "GpuPlan");
            if (compact)
            {
                privateBuffer = (IDisposable)New("PrivateOrdinalBuffer", rd);
                producer = new NeowFamilyGpuExecutor(rd, (NeowFamilyGpuPlan)NPlan(), 42u,
                    privateOutput: (PrivateOrdinalBuffer)privateBuffer);
                object?[] args = [input, CancellationToken.None, null];
                int n = (int)Call(producer, "ExecutePrivateOutput", args)!;
                // Preserve the actual unordered resident append order, not a host-sorted substitute.
                ordinals = n == 0 ? [] : MemoryMarshal.Cast<byte,uint>(rd.BufferGetData((Rid)Get(privateBuffer, "Buffer"), 0, checked((uint)n*4))).ToArray();
                if (ordinals.Distinct().Count() != n || ordinals.Any(o => o >= count)) throw new InvalidDataException("Y2 N private population");
                Console.WriteLine($"y2ActualNPopulation={ordinals.Length};modeledN={Family("N.Neow").Survival};producerMetrics={JsonSerializer.Serialize(args[2])}");
            }
            executor = (IDisposable)New("CombatRewardGpuExecutor", rd, plan, false, null, privateBuffer);
            source = (string)Call(plan, "ShaderSource", false)!;
            if (compact) source = Replace(source, "input_ordinals.values[index]", "input_ordinals.values[batch_meta.values[6]+index]");
        }
        else if (kind == "W")
        {
            var plan = New("WorldFamilyGpuPlan", Get(Family("W.World"), "Replay"));
            executor = (IDisposable)New("WorldFamilyGpuExecutor", rd, plan, null, null);
            source = (string)Call(plan, "ShaderSource")!;
        }
        else throw new ArgumentException(kind);

        var owned = new List<Rid>();
        var rows = new List<object>();
        var field = executor.GetType().GetField(pipelineField, Instance)!;
        Rid originalPipeline = (Rid)field.GetValue(executor)!;
        var originalUniforms = kind == "W" ? (Rid[])Get(executor, "_uniforms") : null;
        string uniformField = kind switch { "S-stable" => "_stablePhase1UniformSet", "S" or "R" => "_uniformSet", _ => "_uniforms" };
        object originalUniform = Get(executor, uniformField);
        Rid Add(Rid rid) { owned.Add(rid); return rid; }
        try
        {
            Rid MakePipeline(string text) { Rid shader = Add(Compile(rd, text)); Rid p = Add(rd.ComputePipelineCreate(shader)); if (!rd.ComputePipelineIsValid(p)) throw new InvalidOperationException("Y2 pipeline"); return p; }
            var variants = new List<(string Name, Rid Pipeline, object Uniforms)> { ("current", originalPipeline, originalUniform) };
            bool terminalCardGate = source.Contains("#define RT2_C_TERMINAL_CARD_GATE 1", StringComparison.Ordinal);
            bool merchantEarlyGate = kind.StartsWith("S", StringComparison.Ordinal);
            if (merchantEarlyGate)
                variants.Add(("merchant-early-reference", MakePipeline(source.Replace(
                    "#define RT2_S_EARLY_REJECTION 1", "#define RT2_S_EARLY_REJECTION 0", StringComparison.Ordinal)), originalUniform));
            if (terminalCardGate)
            {
                // Isolate this increment. Previously rejected root/RNE experiments
                // are not repeated as part of the terminal-card comparison.
                variants.Add(("terminal-card-reference", MakePipeline(Replace(source, "#version 450",
                    "#version 450\n#define RT2_C_TERMINAL_CARD_GATE 0")), originalUniform));
            }
            else if (kind is "C" or "NC")
            {
                variants.Add(("next-int-rne", MakePipeline(Replace(source, "#version 450",
                    "#version 450\n#define RT2_NEXT_INT_VARIANT 2")), originalUniform));
                // Independent retained scan reference for the empty-exclusion shortcut.
                // Restore only this helper; keep the live route/predicates/geometry.
                const string poolSignature = "uint cr_select_pool_excluding(uint meta,uint excluded[12],uint excluded_count,inout RngState rng){";
                int poolStart = source.IndexOf(poolSignature, StringComparison.Ordinal);
                int poolEnd = source.IndexOf("bool cr_execute_non_capsule_relic", poolStart, StringComparison.Ordinal);
                if(poolStart<0 || poolEnd<0)throw new InvalidOperationException("Y2 pool selector seam changed");
                const string scanBody = "uint off=pool_meta.values[meta],len=pool_meta.values[meta+1u],available=0u;for(uint i=0u;i<len;++i){uint v=dense_ids.ids[off+i];bool hit=false;for(uint j=0u;j<excluded_count;++j)hit=hit||excluded[j]==v;if(!hit)available++;}if(available==0u)return 0xffffffffu;uint pick=cr_next_int(rng,available);for(uint i=0u;i<len;++i){uint v=dense_ids.ids[off+i];bool hit=false;for(uint j=0u;j<excluded_count;++j)hit=hit||excluded[j]==v;if(hit)continue;if(pick==0u)return v;pick--;}return 0xffffffffu;}\n\n";
                variants.Add(("pool-scan-reference", MakePipeline(source[..poolStart]+poolSignature+scanBody+source[poolEnd..]), originalUniform));
            }
            if (kind == "C" && !terminalCardGate)
            {
                // Narrow experiment: eight lanes generate eight roots each, then the unchanged
                // 64-lane C predicate/compaction body consumes them. No new dispatch or ABI.
                string helper = "uint64_t make_u64(uint lo,uint hi){return cr_make_u64(lo,hi);}\nuint64_t rotl64(uint64_t x,uint n){return cr_rotl64(x,n);}\n"
                    + Shader("VisibleSeedRootHashCommon.glsl") + "\nshared uvec2 y2_carried[64];\n";
                string carry = Replace(source, "void main(){", helper + """

                    void main(){
                        if(gl_LocalInvocationID.x<8u){
                            uint first=gl_WorkGroupID.x*64u+gl_LocalInvocationID.x*8u;
                            uint64_t a;uint b;
                            encode_seed12_packed(cr_make_u64(batch_meta.values[0],batch_meta.values[1])+uint64_t(batch_meta.values[6]+first),a,b);
                            for(uint j=0u;j<8u;j++){
                                uint64_t r=root_hash_for_seed12_packed(a,b);
                                y2_carried[gl_LocalInvocationID.x*8u+j]=uvec2(uint(r),uint(r>>32u));
                                advance_seed12_packed(a,b);
                            }
                        }
                        barrier();
                    """);
                carry = Replace(carry, "cr_hash_seed12(cr_make_u64(batch_meta.values[0],batch_meta.values[1])+uint64_t(ordinal))", "cr_make_u64(y2_carried[lid].x,y2_carried[lid].y)");
                variants.Add(("carry-cooperative8", MakePipeline(carry), originalUniform));
            }
            if (kind is "N" or "R")
            {
                string full = kind == "N"
                    ? Replace(source, "xxhash64_seed12_packed(seed_first8,tail)", "root_hash_for_ordinal(base+uint64_t(ordinal))")
                    : Replace(source, "root_hash_for_seed12_packed(seed_first8, seed_tail4)", "root_hash_for_ordinal(global_ordinal + uint64_t(offset))");
                // The now-dead encode/advance chain is removed by shader DCE. Geometry remains 8 lanes.
                variants.Add(("full-decode", MakePipeline(full), originalUniform));
            }
            int population = compact ? ordinals.Length : count;
            if (population > 0 && !terminalCardGate && (kind is "N" or "C" or "NC" or "W"))
            {
                Rid roots = Add(rd.StorageBufferCreate((uint)Math.Max(8, population * 8), []));
                GenerateRoots(rd, batch, compact ? ordinals : null, roots, rows, owned);
                string rootExpression = kind switch
                {
                    "N" => "batch.values[4u]==0u ? xxhash64_seed12_packed(seed_first8,tail) : root_hash_for_ordinal(base+uint64_t(ordinal))",
                    "W" => "root_hash_for_ordinal(make_u64(batch.v[0],batch.v[1])+uint64_t(ordinal))",
                    _ => "cr_hash_seed12(cr_make_u64(batch_meta.values[0],batch_meta.values[1])+uint64_t(ordinal))"
                };
                int binding = kind == "W" ? 19 : 11;
                string index = kind == "N" ? "first+lane" : kind == "W" ? "batch.v[6]+i" : "batch_meta.values[6]+index";
                string precomputed = Replace(source, rootExpression, $"(uint64_t(y2_roots.values[({index})*2u])|(uint64_t(y2_roots.values[({index})*2u+1u])<<32u))");
                precomputed = Replace(precomputed, "layout(local_size_x", $"layout(set=0,binding={binding},std430) readonly restrict buffer Y2Roots {{uint values[];}} y2_roots;\nlayout(local_size_x");
                Rid shader = Add(Compile(rd, precomputed)), pipeline = Add(rd.ComputePipelineCreate(shader));
                if (!rd.ComputePipelineIsValid(pipeline)) throw new InvalidOperationException("Y2 precomputed pipeline");
                var executorOwned = (List<Rid>)Get(executor, "_owned");
                object uniforms;
                if (kind == "W")
                {
                    int k = originalUniforms!.Length;
                    if (executorOwned.Count != 17 + k * 5 + (k > 1 ? 1 : 0) || executorOwned[16] != originalPipeline)
                        throw new InvalidOperationException("Y2 W descriptor construction changed");
                    var buffers = new Rid[20]; int cursor = 0;
                    for (int i = 0; i < 19; i++) if (i is not (0 or 12 or 16 or 17 or 18)) buffers[i] = executorOwned[cursor++];
                    buffers[12] = executorOwned[cursor]; buffers[19] = roots;
                    var sets = new Rid[originalUniforms!.Length];
                    for (int slot = 0; slot < sets.Length; slot++)
                    {
                        foreach (var (b, f) in new[] { (0,"_batch"),(16,"_header"),(17,"_output"),(18,"_input") }) buffers[b] = ((Rid[])Get(executor, f))[slot];
                        sets[slot] = Add(Uniforms(rd, shader, buffers));
                    }
                    uniforms = sets;
                }
                else
                {
                    if (executorOwned.Count != (compact ? 13 : 14) || executorOwned[^2] != originalPipeline)
                        throw new InvalidOperationException("Y2 N/C descriptor construction changed");
                    // Constructor-owned buffer order; assert named endpoints before GPU execution.
                    int[] map = kind == "N" ? [0,4,5,6,7,8,1,3,9,10,2] : compact ? [0,3,4,5,6,7,8,9,1,2,-1] : [0,4,5,6,7,8,9,10,1,3,2];
                    Rid[] buffers = map.Select(i => i < 0 ? (Rid)Get(privateBuffer!, "Buffer") : executorOwned[i]).Append(roots).ToArray();
                    if (buffers[0] != (Rid)Get(executor,kind=="N"?"_meta":"_batch") || buffers[kind=="N"?6:8] != (Rid)Get(executor,"_header") ||
                        buffers[kind=="N"?7:9] != (Rid)Get(executor,"_output") || buffers[10] != (Rid)Get(executor,"_input"))
                        throw new InvalidOperationException("Y2 descriptor endpoint mismatch");
                    uniforms = Add(Uniforms(rd, shader, buffers));
                }
                variants.Add(("precomputed-root", pipeline, uniforms));
            }
            string? expected = null; int expectedCount = -1;
            // Alternating order; warm samples excluded. Hash/readback validation outside executor timers.
            for (int round = -3; round < 9; round++)
            foreach (var variant in (round % 2 == 0 ? variants.AsEnumerable() : variants.AsEnumerable().Reverse()))
            {
                field.SetValue(executor, variant.Pipeline);
                executor.GetType().GetField(uniformField, Instance)!.SetValue(executor, variant.Uniforms);
                object?[] args = compact ? [batch, population, CancellationToken.None, null] : [input, CancellationToken.None, null];
                var result = (FamilyCandidateSet)Call(executor, compact ? "ExecutePrivateInput" : "Execute", args)!;
                string digest = Digest(result);
                expected ??= digest; if (expectedCount < 0) expectedCount = result.Count;
                if (digest != expected || result.Count != expectedCount) throw new InvalidOperationException("Y2 population mismatch: " + variant.Name);
                if (round >= 0) rows.Add(new { kind, variant = variant.Name, round, input = population, output = result.Count, digest, metrics = args[^1] });
            }
            if (compact)
            {
                FamilyCandidateSet ExecuteCompact((string Name, Rid Pipeline, object Uniforms) v, int n,
                    CancellationToken token = default)
                {
                    field.SetValue(executor, v.Pipeline);
                    executor.GetType().GetField(uniformField, Instance)!.SetValue(executor, v.Uniforms);
                    return (FamilyCandidateSet)Call(executor, "ExecutePrivateInput", batch, n, token, null)!;
                }
                int checks = 0;
                foreach (int n in new[] { 0, Math.Min(1,population), Math.Min(67,population),
                    Math.Min((1 << 20) + 67,population) }.Distinct())
                {
                    string reference = Digest(ExecuteCompact(variants[0],n));
                    foreach(var v in variants)
                    {
                        if(Digest(ExecuteCompact(v,n))!=reference)throw new InvalidDataException("Y2 Compact tail parity:"+v.Name);
                        checks++;
                    }
                }
                using var cts = new CancellationTokenSource(); cts.Cancel();
                foreach(var v in variants)
                {
                    try {ExecuteCompact(v,population,cts.Token);throw new InvalidOperationException("Y2 missed Compact cancellation");}
                    catch(TargetInvocationException e) when(e.InnerException is OperationCanceledException) {checks++;}
                    if(Digest(ExecuteCompact(v,population))!=expected)throw new InvalidDataException("Y2 Compact cancellation reuse");
                    checks++;
                }
                rows.Add(new{kind="validation",checks,compact=true,tail=true,zeroInput=true,preCancelled=true,reuse=true,
                    nonzeroInputOffset=population>(1<<20)});
            }
            else
            {
                FamilyCandidateSet ExecuteAt((string Name, Rid Pipeline, object Uniforms) v, SearchBatch b, CancellationToken token = default)
                {
                    field.SetValue(executor,v.Pipeline); executor.GetType().GetField(uniformField,Instance)!.SetValue(executor,v.Uniforms);
                    return (FamilyCandidateSet)Call(executor,"Execute",FamilyCandidateSet.Dense(b),token,null)!;
                }
                int checks=0;
                foreach(int size in new[]{0,1,67,Math.Min(count,4096)})
                {
                    var b=new SearchBatch(start,size); string reference=Digest(ExecuteAt(variants[0],b));
                    foreach(var v in variants) { if(Digest(ExecuteAt(v,b))!=reference)throw new InvalidDataException("Y2 tail parity:"+v.Name);checks++; }
                }
                using var cts=new CancellationTokenSource();cts.Cancel();
                foreach(var v in variants)
                {
                    try {ExecuteAt(v,batch,cts.Token);throw new InvalidOperationException("Y2 missed cancellation");}
                    catch(TargetInvocationException e) when(e.InnerException is OperationCanceledException) {checks++;}
                    // Same resident executor must still be usable.
                    if(Digest(ExecuteAt(v,batch))!=expected)throw new InvalidDataException("Y2 cancellation reuse");checks++;
                }
                var small=new SearchBatch(start,4096); var referenceFull=ExecuteAt(variants[0],small).ExportAbi1().ToArray();
                foreach(var v in variants.Where(v=>v.Name!="precomputed-root"))
                {
                    var a=ExecuteAt(v,new SearchBatch(start,67)).ExportAbi1().ToArray();
                    var b=ExecuteAt(v,new SearchBatch(start+67,4029)).ExportAbi1().ToArray().Select(x=>x+67);
                    if(!a.Concat(b).SequenceEqual(referenceFull))throw new InvalidDataException("Y2 split/resume parity:"+v.Name);checks++;
                }
                rows.Add(new{kind="validation",checks,tail=true,zeroInput=true,preCancelled=true,reuse=true,splitResume=true});
            }
            Console.WriteLine($"y2Probe=PASS;kind={kind};input={population};output={expectedCount};abi1Sha256={expected};diagnosticOnly=true;productionCalibration=false");
            File.WriteAllText(Path.Combine(output, "y2.json"), JsonSerializer.Serialize(new { kind, batch,
                modeledSurvival = families.Select(f => new { f.FamilyId, f.Coverage, f.Survival }), rows }, ProductionSearchReplay.JsonOptions));
        }
        finally
        {
            field.SetValue(executor, originalPipeline);
            executor.GetType().GetField(uniformField, Instance)!.SetValue(executor, originalUniform);
            for (int i = owned.Count - 1; i >= 0; i--) rd.FreeRid(owned[i]);
            executor.Dispose(); producer?.Dispose(); privateBuffer?.Dispose();
        }
    }

    private static void GenerateRoots(RenderingDevice rd, SearchBatch batch, uint[]? ordinals, Rid roots, List<object> rows, List<Rid> owned)
    {
        Rid Add(Rid rid) { owned.Add(rid); return rid; }
        int count = ordinals?.Length ?? batch.BatchCandidateCount;
        Rid metadata = Add(Buffer(rd, MemoryMarshal.AsBytes(new uint[] { (uint)batch.BatchBase, (uint)(batch.BatchBase >> 32), (uint)count, ordinals is null ? 0u : 1u }.AsSpan()).ToArray()));
        Rid input = Add(Buffer(rd, ordinals is null ? new byte[4] : MemoryMarshal.AsBytes(ordinals.AsSpan()).ToArray()));
        string prefix = "#version 450\n#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require\nlayout(local_size_x=64) in;\n" +
            "layout(set=0,binding=0,std430) readonly buffer M {uint v[];} meta;\nlayout(set=0,binding=1,std430) readonly buffer I {uint v[];} ids;\n" +
            "layout(set=0,binding=2,std430) writeonly buffer O {uint v[];} roots;\nvoid store_root(uint i,uint64_t r){roots.v[i*2u]=uint(r);roots.v[i*2u+1u]=uint(r>>32u);}\n" +
            "uint64_t make_u64(uint lo,uint hi){return uint64_t(lo)|(uint64_t(hi)<<32u);}\nuint64_t rotl64(uint64_t x,uint n){return (x<<n)|(x>>(64u-n));}\n" + Shader("VisibleSeedRootHashCommon.glsl");
        foreach (bool carry in ordinals is null ? new[] { false, true } : new[] { false })
        {
            string body = carry
                ? "uint first=gl_GlobalInvocationID.x*8u;if(first>=meta.v[2])return;uint64_t a;uint b;encode_seed12_packed(base+uint64_t(first),a,b);for(uint j=0u;j<8u&&first+j<meta.v[2];j++){store_root(first+j,root_hash_for_seed12_packed(a,b));advance_seed12_packed(a,b);}"
                : "uint i=gl_GlobalInvocationID.x;if(i>=meta.v[2])return;uint ordinal=meta.v[3]==0u?i:ids.v[i];store_root(i,root_hash_for_ordinal(base+uint64_t(ordinal)));";
            body = body.Replace("gl_GlobalInvocationID.x", "(gl_GlobalInvocationID.x+gl_GlobalInvocationID.y*32768u*64u)", StringComparison.Ordinal);
            Rid shader = Add(Compile(rd, prefix + "\nvoid main(){uint64_t base=uint64_t(meta.v[0])|(uint64_t(meta.v[1])<<32u);" + body + "}"));
            Rid pipeline = Add(rd.ComputePipelineCreate(shader)), uniforms = Add(Uniforms(rd, shader, [metadata, input, roots]));
            if (!rd.ComputePipelineIsValid(pipeline)) throw new InvalidOperationException("Y2 root generator pipeline");
            for (int round = -3; round < 9; round++)
            {
                var watch = Stopwatch.StartNew(); long list = rd.ComputeListBegin();
                rd.ComputeListBindComputePipeline(list, pipeline); rd.ComputeListBindUniformSet(list, uniforms, 0);
                uint groups=(uint)((count+(carry?511:63))/(carry?512:64)), x=Math.Min(groups,32768u);
                rd.ComputeListDispatch(list, x, (groups+x-1)/x, 1);
                rd.ComputeListEnd(); rd.Submit(); rd.Sync(); watch.Stop();
                if (round >= 0) rows.Add(new { kind = "root-generation-plus-store", variant = carry ? "carry" : "full-decode", round, input = count, dispatchSyncMs = watch.Elapsed.TotalMilliseconds, rootBytes = count * 8L });
            }
            // Independent CPU check over deterministic samples, including both ends.
            foreach (int i in new[] { 0, Math.Min(7,count-1), count/2, count-1 }.Distinct())
            {
                ulong ordinal = batch.BatchBase + (ordinals is null ? (uint)i : ordinals[i]);
                const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
                byte[] seed = new byte[12]; for (int p=11;p>=0;p--) {seed[p]=(byte)alphabet[(int)(ordinal%34)];ordinal/=34;}
                ulong actual=BitConverter.ToUInt64(rd.BufferGetData(roots,(uint)(i*8),8));
                if(actual!=XxHash64.Hash(seed,0))throw new InvalidOperationException("Y2 root CPU mismatch");
            }
        }
    }
}
