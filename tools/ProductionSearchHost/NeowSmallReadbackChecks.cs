using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Explicit native validation inside the existing Host. Synthetic emitters below
// exercise transport only; they are never labeled numerical N parity or pricing.
internal static class NeowSmallReadbackChecks
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(ExactSearchExecutionRequest request, string output)
    {
        var family = (NeowFamily)FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true)
            .Single(f => f.FamilyId == "N.Neow");
        var plan = family.GpuPlan ?? throw new InvalidOperationException("NReadback.NoGpuPlan");
        if (!family.UsesSmallPrivateOutput || !plan.UsesStagedDense) throw new InvalidOperationException("NReadback.RequiresSoloStagedLeafy");
        const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        ulong start = 0;
        foreach (char c in request.CanonicalStartSeed) start = checked(start * 34 + (uint)alphabet.IndexOf(c));
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("NReadback.NoGpu");
        using var buffer = new PrivateOrdinalBuffer(rd);
        using var gpu = new NeowFamilyGpuExecutor(rd, plan, 42, privateOutput: buffer);
        using var reference = new NeowFamilyGpuExecutor(rd, plan, 43);
        if(!gpu.SupportsDensePair)throw new InvalidOperationException("NReadback.RequiresOrdinaryDensePairScope");
        var matcher = NeowFamilyReplay.Bind(family.PricingReplayPlan);
        var profile = RuntimeProfileRegistry.Select(request.Detection);
        bool cpu(ulong candidate) => matcher(profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, candidate)));
        long candidates = 0, smallWindows = 0, emptyWindows = 0, largeWindows = 0, cpuChecks = 0;
        ulong? positive = null;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int windows = checked((int)((request.ScanCount + PrivateOrdinalBuffer.Capacity - 1) / PrivateOrdinalBuffer.Capacity));
        if (windows > 512) throw new InvalidOperationException("NReadback.Bounded512Windows");
        var elapsed = Stopwatch.StartNew();
        for (int window = 0; window < windows; window++)
        {
            int size = (int)Math.Min(PrivateOrdinalBuffer.Capacity, request.ScanCount - (long)window * PrivateOrdinalBuffer.Capacity);
            var batch = new SearchBatch(start + (ulong)window * PrivateOrdinalBuffer.Capacity, size);
            int count = gpu.ExecutePrivateOutput(FamilyCandidateSet.Dense(batch), CancellationToken.None, out var metrics);
            var full = buffer.ReadForVerification(batch, count);
            var actual = reference.Execute(FamilyCandidateSet.Dense(batch), CancellationToken.None, out _);
            Equal(full, actual, "PublicVsPrivate");
            if (count == 0) emptyWindows++;
            else if (count <= 8) { smallWindows++; Equal(metrics.SmallOutput ?? throw new InvalidDataException("MissingMirror"), full, "MirrorVsFull"); }
            else largeWindows++;
            if ((count == 0 || count > 8) && metrics.SmallOutput is not null) throw new InvalidDataException("TruncatedMirrorConsumed");
            digest.AppendData(BitConverter.GetBytes(batch.BatchBase));
            digest.AppendData(MemoryMarshal.AsBytes(full.ExportAbi1().Span));
            foreach (ulong ordinal in full.EnumerateLogicalOrdinals())
            {
                if (!cpu(batch.GlobalCandidate(ordinal))) throw new InvalidDataException("NReadback.CpuPositiveParity");
                cpuChecks++; positive ??= batch.GlobalCandidate(ordinal);
            }
            var ordinals = full.ExportAbi1().ToArray();
            for (ulong ordinal = 0; ordinal < (ulong)Math.Min(size, 512); ordinal++)
            {
                if (cpu(batch.GlobalCandidate(ordinal)) != (Array.BinarySearch(ordinals, ordinal) >= 0))
                    throw new InvalidDataException("NReadback.CpuBoundedParity");
                cpuChecks++;
            }
            candidates += count;
        }
        if (positive is null) throw new InvalidDataException("NReadback.NoRealPositiveForTails");
        var tails = new List<object>();
        foreach (int size in new[] { 1, 511, 512, 513, 1_000_003 })
        {
            var batch = new SearchBatch(positive.Value, size);
            int count = gpu.ExecutePrivateOutput(FamilyCandidateSet.Dense(batch), CancellationToken.None, out var metrics);
            var full = buffer.ReadForVerification(batch, count);
            Equal(full, reference.Execute(FamilyCandidateSet.Dense(batch), CancellationToken.None, out _), "TailPublicParity");
            if (count is < 1 or > 8) throw new InvalidDataException("NReadback.TailExpectedSmallPositive");
            Equal(metrics.SmallOutput!, full, "TailMirrorParity");
            tails.Add(new { size, count });
        }
        // The next invocation must not inherit a stale mirror, even after cancellation.
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { gpu.ExecutePrivateOutput(FamilyCandidateSet.Dense(new(positive.Value, 513)), canceled.Token, out _); throw new InvalidDataException("NReadback.CancelNotObserved"); }
            catch (OperationCanceledException) { }
        }
        var resumedBatch = new SearchBatch(positive.Value, 513);
        int resumed = gpu.ExecutePrivateOutput(FamilyCandidateSet.Dense(resumedBatch), CancellationToken.None, out var resumedMetrics);
        Equal(resumedMetrics.SmallOutput!, buffer.ReadForVerification(resumedBatch, resumed), "CancelReuse");

        // Compact private input uses original batch-local ordinals, including gaps
        // and an intentionally unordered resident input, without renumbering.
        var compactBatch = new SearchBatch(positive.Value, 513);
        using var incoming = new PrivateOrdinalBuffer(rd);
        uint[] compactInput = [512, 3, 2, 1, 0];
        FamilyGpuComputeUtility.Update(rd, incoming.Buffer, compactInput, "NReadback.CompactFixture");
        using (var compactGpu = new NeowFamilyGpuExecutor(rd, plan, 44, privateInput: incoming, privateOutput: buffer))
        {
            int count = compactGpu.ExecutePrivateStage(compactBatch, compactInput.Length, CancellationToken.None, out var metrics);
            var expected = FamilyCandidateSet.FromAbi1(compactBatch, compactInput.Where(x => cpu(compactBatch.GlobalCandidate(x))).Order().Select(x => (ulong)x).ToArray());
            Equal(metrics.SmallOutput!, expected, "CompactCpuParity");
            Equal(buffer.ReadForVerification(compactBatch, count), expected, "CompactFullParity");
        }
        var protocol = Protocol(rd, plan, buffer);
        var densePairs = DensePairs(rd, plan, positive.Value, cpu);
        var pairProtocol = PairProtocol(rd, plan);
        var result = new { passed = true, device = rd.GetDeviceName(), roots = request.ScanCount, windows, candidates,
            smallWindows, emptyWindows, largeWindows, cpuChecks, abi1Sha256 = Convert.ToHexString(digest.GetHashAndReset()),
            tails, cancelReuse = true, compactPrivateInput = true, protocol, densePairs, pairProtocol,
            validationMilliseconds = elapsed.Elapsed.TotalMilliseconds, timingIsPerformanceSample = false };
        File.WriteAllText(Path.Combine(output, "n-small-readback-checks.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"nSmallReadback=PASS;roots={request.ScanCount};candidates={candidates};cpuChecks={cpuChecks};syntheticProtocolIsNParity=false");
    }

    private static object Protocol(RenderingDevice rd, NeowFamilyGpuPlan plan, PrivateOrdinalBuffer buffer)
    {
        using var gpu = new NeowFamilyGpuExecutor(rd, plan, 45, privateOutput: buffer);
        Rid control=InstallSynthetic(rd,gpu);
        var batch = new SearchBatch(0, 32);
        var counts = new[] { 0, 1, 7, 8, 9 };
        foreach (int emit in counts)
        {
            FamilyGpuComputeUtility.Update(rd, control, [(uint)emit, 0], "NReadback.EmitCount");
            int count = gpu.ExecutePrivateOutput(FamilyCandidateSet.Dense(batch), CancellationToken.None, out var metrics);
            if (count != emit) throw new InvalidDataException("NReadback.SyntheticCount");
            var full = buffer.ReadForVerification(batch, count);
            var expected = FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(32 - emit, emit).Select(x => (ulong)x).ToArray());
            Equal(full, expected, "SyntheticFullOutput");
            if (emit is > 0 and <= 8) Equal(metrics.SmallOutput!, full, "SyntheticMirror");
            else if (metrics.SmallOutput is not null) throw new InvalidDataException("NReadback.SyntheticMirrorScope");
        }
        foreach (uint malformed in new uint[] { 1, 2, 3 })
        {
            FamilyGpuComputeUtility.Update(rd, control, [2, malformed], "NReadback.MalformedTransport");
            try { gpu.ExecutePrivateOutput(FamilyCandidateSet.Dense(batch), CancellationToken.None, out _); }
            catch (Exception ex) when (malformed == 1 && ex is InvalidDataException || malformed == 2 && ex is ArgumentException || malformed == 3 && ex is ArgumentOutOfRangeException) { continue; }
            throw new InvalidDataException("NReadback.MalformedAccepted:" + malformed);
        }
        return new { syntheticTransportOnly = true, counts, invalidMarkerDuplicateAndBoundsRejected = true, dedicatedControlBinding = true };
    }

    // This fixture replaces only the final stage and supplies a test-owned
    // control binding. Dense production input is a valid unused 4-byte binding.
    // Expected populations below are constructed independently of N numerics.
    private static Rid InstallSynthetic(RenderingDevice rd, NeowFamilyGpuExecutor gpu)
    {
        var type=typeof(NeowFamilyGpuExecutor);
        type.GetMethod("EnsureModules",Members)!.Invoke(gpu,[true,false]);
        string source=(string)type.GetMethod("ModuleSource",Members)!.Invoke(gpu,[2,false])!;
        source=source.Replace("void main()","void numerical_main()",StringComparison.Ordinal)+"""

            void main() {
                if(gl_GlobalInvocationID.x!=0u) return;
                for(uint i=0u;i<input_ordinals.values[0u];i++) {
                    uint ordinal=input_ordinals.values[1u]==2u ? batch.values[2u]-1u :
                        input_ordinals.values[1u]==3u ? batch.values[2u] : batch.values[2u]-1u-i;
                    append_ordinal(ordinal);
                }
                if(input_ordinals.values[1u]==1u) header.values[8u]=0u;
                if(input_ordinals.values[1u]==4u) header.values[5u]^=1u;
            }
            """;
        var owned=(List<Rid>)type.GetField("_owned",Members)!.GetValue(gpu)!;
        var stages=(List<(Rid Pipeline,Rid Uniforms)>)type.GetField("_stages",Members)!.GetValue(gpu)!;
        var buffers=(Rid[])((Rid[])type.GetField("_stagedBuffers",Members)!.GetValue(gpu)!).Clone();
        Rid control=FamilyGpuComputeUtility.CreateStorageBuffer(rd,FamilyGpuComputeUtility.ToBytes([0,0]),"NReadback.TestControl");
        owned.Add(control); buffers[10]=control;
        Rid shader=FamilyGpuComputeUtility.CompileShader(rd,source,"NReadback.SyntheticTransportOnly"); owned.Add(shader);
        Rid pipeline=FamilyGpuComputeUtility.CreateComputePipeline(rd,shader,"NReadback.SyntheticTransportOnly"); owned.Add(pipeline);
        Rid uniforms=FamilyGpuComputeUtility.CreateUniformSet(rd,shader,buffers,"NReadback.SyntheticTransportOnly"); owned.Add(uniforms);
        stages[2]=(pipeline,uniforms);
        return control;
    }

    private static object DensePairs(RenderingDevice rd, NeowFamilyGpuPlan nativePlan, ulong positive, Func<ulong,bool> cpu)
    {
        long cpuChecks=0; int publicPairs=0,privatePairs=0;
        var batches=new[]
        {
            (new SearchBatch(positive,513),new SearchBatch(positive+137,1_000_003)),
            (new SearchBatch(positive+137,511),new SearchBatch(positive,1)),
            (new SearchBatch(positive+9,0),new SearchBatch(positive+513,512))
        };
        foreach(var variant in new[] { NeowGpuNumericalVariant.NativeFloat64,NeowGpuNumericalVariant.IntegerUnitFloat })
        {
            var plan=nativePlan with { NumericalVariant=variant };
            using var standalone=new NeowFamilyGpuExecutor(rd,plan,60);
            using var first=new NeowFamilyGpuExecutor(rd,plan,61);
            using (var second=new NeowFamilyGpuExecutor(rd,plan,62,sharedDenseScratch:first))
            {
            var compactBatch=new SearchBatch(positive,513);
            ulong[] compactOrdinals=[0,1,3,512];
            var compact=FamilyCandidateSet.FromAbi1(compactBatch,compactOrdinals);
            var compactExpected=FamilyCandidateSet.FromAbi1(compactBatch,
                compactOrdinals.Where(ordinal=>cpu(compactBatch.GlobalCandidate(ordinal))).ToArray());
            Equal(first.Execute(compact,CancellationToken.None,out _),compactExpected,"PublicCompactBeforeDensePair"+variant);
            foreach(var (leftBatch,rightBatch) in batches)
            {
                var leftExpected=standalone.Execute(FamilyCandidateSet.Dense(leftBatch),CancellationToken.None,out _);
                var rightExpected=standalone.Execute(FamilyCandidateSet.Dense(rightBatch),CancellationToken.None,out _);
                var pair=NeowFamilyGpuExecutor.ExecuteDensePair(first,leftBatch,second,rightBatch,CancellationToken.None);
                AssertSharedScratch(first,second);
                Equal(pair.Left!,leftExpected,"RealPublicPairLeft"+variant);
                Equal(pair.Right!,rightExpected,"RealPublicPairRight"+variant);
                PairMetrics(pair.LeftMetrics,pair.RightMetrics,pair.SharedMs,leftBatch,rightBatch);
                cpuChecks+=CpuPopulation(pair.Left!,cpu);
                cpuChecks+=CpuPopulation(pair.Right!,cpu);
                publicPairs++;
            }
            }
            var reuseBatch=batches[0].Item1;
            Equal(first.Execute(FamilyCandidateSet.Dense(reuseBatch),CancellationToken.None,out _),
                standalone.Execute(FamilyCandidateSet.Dense(reuseBatch),CancellationToken.None,out _),
                "DonorModuleValidAfterBorrowerDisposed"+variant);
        }
        using(var leftBuffer=new PrivateOrdinalBuffer(rd))
        using(var rightBuffer=new PrivateOrdinalBuffer(rd))
        using(var standalone=new NeowFamilyGpuExecutor(rd,nativePlan,63))
        using(var first=new NeowFamilyGpuExecutor(rd,nativePlan,64,privateOutput:leftBuffer))
        using(var second=new NeowFamilyGpuExecutor(rd,nativePlan,65,privateOutput:rightBuffer,sharedDenseScratch:first))
        {
            foreach(var (leftBatch,rightBatch) in batches)
            {
                var leftExpected=standalone.Execute(FamilyCandidateSet.Dense(leftBatch),CancellationToken.None,out _);
                var rightExpected=standalone.Execute(FamilyCandidateSet.Dense(rightBatch),CancellationToken.None,out _);
                var pair=NeowFamilyGpuExecutor.ExecuteDensePair(first,leftBatch,second,rightBatch,CancellationToken.None);
                AssertSharedScratch(first,second);
                if(pair.Left is not null || pair.Right is not null)throw new InvalidDataException("NReadback.PrivatePairReturnedPublicPayload");
                VerifyPrivate(leftBuffer,leftBatch,pair.LeftMetrics,leftExpected,"RealPrivatePairLeft");
                VerifyPrivate(rightBuffer,rightBatch,pair.RightMetrics,rightExpected,"RealPrivatePairRight");
                PairMetrics(pair.LeftMetrics,pair.RightMetrics,pair.SharedMs,leftBatch,rightBatch);
                cpuChecks+=CpuPopulation(leftExpected,cpu);
                cpuChecks+=CpuPopulation(rightExpected,cpu);
                privatePairs++;
            }
        }
        return new { numericalNParity=true,publicPairs,privatePairs,cpuChecks,
            variants=new[] { "NativeFloat64.PublicAndPrivate","IntegerUnitFloat.PublicOnly" },
            standaloneCompletePopulationParity=true,cpuPositiveAndFirst512Recall=true,distinctBasesAndTails=true,
            repeatedSharedScratchReuse=true,sharedImmutableModules=true,donorReuseAfterBorrowerDisposal=true,
            compactToDenseReuse=true,universalRecallClaim=false };
    }

    private static long CpuPopulation(FamilyCandidateSet result,Func<ulong,bool> cpu)
    {
        long checks=0;
        ulong[] ordinals=result.ExportAbi1().ToArray();
        foreach(ulong ordinal in ordinals)
        {
            if(!cpu(result.Batch.GlobalCandidate(ordinal)))throw new InvalidDataException("NReadback.PairCpuPositive");
            checks++;
        }
        for(ulong ordinal=0;ordinal<(ulong)Math.Min(result.Batch.BatchCandidateCount,512);ordinal++)
        {
            if(cpu(result.Batch.GlobalCandidate(ordinal))!=(Array.BinarySearch(ordinals,ordinal)>=0))
                throw new InvalidDataException("NReadback.PairCpuBoundedRecall");
            checks++;
        }
        return checks;
    }

    private static FamilyCandidateSet SyntheticExpected(SearchBatch batch,int emit) =>
        FamilyCandidateSet.FromAbi1(batch,Enumerable.Range(batch.BatchCandidateCount-emit,emit).Select(x=>(ulong)x).ToArray());

    private static void AssertSharedScratch(NeowFamilyGpuExecutor first,NeowFamilyGpuExecutor second)
    {
        var field=typeof(NeowFamilyGpuExecutor).GetField("_stagedBuffers",Members)!;
        var left=(Rid[])field.GetValue(first)!; var right=(Rid[])field.GetValue(second)!;
        if(left[11]!=right[11] || left[12]!=right[12] || left[0]==right[0] || left[6]==right[6] || left[7]==right[7])
            throw new InvalidDataException("NReadback.PairScratchOrSlotAliasing");
        var shaders=typeof(NeowFamilyGpuExecutor).GetField("_stageShaders",Members)!;
        var donorShaders=(Rid[])shaders.GetValue(first)!;
        var borrowedShaders=(Rid[])shaders.GetValue(second)!;
        var borrowerOwned=(List<Rid>)typeof(NeowFamilyGpuExecutor).GetField("_owned",Members)!.GetValue(second)!;
        if(donorShaders.Any(shader=>!shader.IsValid) || !donorShaders.SequenceEqual(borrowedShaders) ||
            donorShaders.Any(borrowerOwned.Contains))
            throw new InvalidDataException("NReadback.PairModuleSharingOrOwnership");
    }

    private static void PairMetrics(NeowFamilyGpuMetrics left,NeowFamilyGpuMetrics right,double sharedMs,SearchBatch leftBatch,SearchBatch rightBatch)
    {
        if(left.Processed!=leftBatch.BatchCandidateCount || right.Processed!=rightBatch.BatchCandidateCount ||
            left.DispatchSyncMs!=0 || right.DispatchSyncMs!=0 || sharedMs<0 ||
            (leftBatch.BatchCandidateCount+rightBatch.BatchCandidateCount>0 && sharedMs<=0))
            throw new InvalidDataException("NReadback.PairMetricIdentityOrDoubleCharge");
    }

    private static void VerifyPrivate(PrivateOrdinalBuffer buffer,SearchBatch batch,NeowFamilyGpuMetrics metrics,FamilyCandidateSet expected,string scope)
    {
        if(metrics.Survivors!=expected.Count)throw new InvalidDataException("NReadback."+scope+"Count");
        Equal(buffer.ReadForVerification(batch,metrics.Survivors),expected,scope+"Full");
        if(expected.Count is >0 and <=8)Equal(metrics.SmallOutput ?? throw new InvalidDataException("NReadback.PairMissingMirror"),expected,scope+"Mirror");
        else if(metrics.SmallOutput is not null)throw new InvalidDataException("NReadback.PairUnexpectedMirror");
    }

    private static object PairProtocol(RenderingDevice rd,NeowFamilyGpuPlan nativePlan)
    {
        int privatePairs=0,publicPairs=0;
        var leftBatch=new SearchBatch(12345,131073);
        var rightBatch=new SearchBatch(54321,196611);
        foreach(var variant in new[] { NeowGpuNumericalVariant.NativeFloat64,NeowGpuNumericalVariant.IntegerUnitFloat })
        {
            var plan=nativePlan with { NumericalVariant=variant };
            using var first=new NeowFamilyGpuExecutor(rd,plan,70);
            using var second=new NeowFamilyGpuExecutor(rd,plan,71,sharedDenseScratch:first);
            Rid leftControl=InstallSynthetic(rd,first),rightControl=InstallSynthetic(rd,second);
            AssertSharedScratch(first,second);
            foreach(var (leftEmit,rightEmit) in new[] { (1024,1025),(65536,65537),(65537,9),(7,0),(0,65537) })
            {
                FamilyGpuComputeUtility.Update(rd,leftControl,[(uint)leftEmit,0],"NReadback.PairLeftEmit");
                FamilyGpuComputeUtility.Update(rd,rightControl,[(uint)rightEmit,0],"NReadback.PairRightEmit");
                var pair=NeowFamilyGpuExecutor.ExecuteDensePair(first,leftBatch,second,rightBatch,CancellationToken.None);
                Equal(pair.Left!,SyntheticExpected(leftBatch,leftEmit),"SyntheticPublicPairLeft"+variant);
                Equal(pair.Right!,SyntheticExpected(rightBatch,rightEmit),"SyntheticPublicPairRight"+variant);
                PairMetrics(pair.LeftMetrics,pair.RightMetrics,pair.SharedMs,leftBatch,rightBatch);
                publicPairs++;
            }
        }
        using(var leftBuffer=new PrivateOrdinalBuffer(rd))
        using(var rightBuffer=new PrivateOrdinalBuffer(rd))
        using(var first=new NeowFamilyGpuExecutor(rd,nativePlan,72,privateOutput:leftBuffer))
        using(var second=new NeowFamilyGpuExecutor(rd,nativePlan,73,privateOutput:rightBuffer,sharedDenseScratch:first))
        {
            var smallLeft=new SearchBatch(13579,32); var smallRight=new SearchBatch(97531,35);
            Rid leftControl=InstallSynthetic(rd,first),rightControl=InstallSynthetic(rd,second);
            AssertSharedScratch(first,second);
            foreach(var (leftEmit,rightEmit) in new[] { (0,1),(8,9),(9,8),(1,0) })
            {
                FamilyGpuComputeUtility.Update(rd,leftControl,[(uint)leftEmit,0],"NReadback.PairLeftEmit");
                FamilyGpuComputeUtility.Update(rd,rightControl,[(uint)rightEmit,0],"NReadback.PairRightEmit");
                var pair=NeowFamilyGpuExecutor.ExecuteDensePair(first,smallLeft,second,smallRight,CancellationToken.None);
                if(pair.Left is not null || pair.Right is not null)throw new InvalidDataException("NReadback.SyntheticPrivatePublicPayload");
                VerifyPrivate(leftBuffer,smallLeft,pair.LeftMetrics,SyntheticExpected(smallLeft,leftEmit),"SyntheticPairLeft");
                VerifyPrivate(rightBuffer,smallRight,pair.RightMetrics,SyntheticExpected(smallRight,rightEmit),"SyntheticPairRight");
                privatePairs++;
            }
            FamilyGpuComputeUtility.Update(rd,leftControl,[8,0],"NReadback.CancelLeft");
            FamilyGpuComputeUtility.Update(rd,rightControl,[9,0],"NReadback.CancelRight");
            using(var canceled=new CancellationTokenSource())
            {
                canceled.Cancel();
                try { NeowFamilyGpuExecutor.ExecuteDensePair(first,smallLeft,second,smallRight,canceled.Token); throw new InvalidDataException("NReadback.PairPreRecordCancelMissing"); }
                catch(OperationCanceledException) { }
            }
            var afterPre=NeowFamilyGpuExecutor.ExecuteDensePair(first,smallLeft,second,smallRight,CancellationToken.None);
            VerifyPrivate(leftBuffer,smallLeft,afterPre.LeftMetrics,SyntheticExpected(smallLeft,8),"PairPreCancelReuseLeft");
            VerifyPrivate(rightBuffer,smallRight,afterPre.RightMetrics,SyntheticExpected(smallRight,9),"PairPreCancelReuseRight");
            using(var canceled=new CancellationTokenSource())
            {
                int callbacks=0;
                Callable callback=Callable.From<byte[]>(_=> { callbacks++; canceled.Cancel(); });
                // This callback can only run on the pair's following Sync. It
                // deterministically cancels after recording, without timer races.
                if(rd.BufferGetDataAsync(leftControl,callback,0,4)!=Error.Ok)throw new InvalidOperationException("NReadback.CancelSentinelSchedule");
                try { NeowFamilyGpuExecutor.ExecuteDensePair(first,smallLeft,second,smallRight,canceled.Token); throw new InvalidDataException("NReadback.PairPostRecordCancelMissing"); }
                catch(OperationCanceledException) { }
                if(callbacks!=1)throw new InvalidDataException("NReadback.CancelSentinelNotDrained");
                GC.KeepAlive(callback);
            }
            var afterPost=NeowFamilyGpuExecutor.ExecuteDensePair(first,smallLeft,second,smallRight,CancellationToken.None);
            VerifyPrivate(leftBuffer,smallLeft,afterPost.LeftMetrics,SyntheticExpected(smallLeft,8),"PairPostCancelReuseLeft");
            VerifyPrivate(rightBuffer,smallRight,afterPost.RightMetrics,SyntheticExpected(smallRight,9),"PairPostCancelReuseRight");
        }
        // A failed second header does not erase a fully collected first root.
        using(var first=new NeowFamilyGpuExecutor(rd,nativePlan,74))
        using(var second=new NeowFamilyGpuExecutor(rd,nativePlan,75,sharedDenseScratch:first))
        {
            Rid leftControl=InstallSynthetic(rd,first),rightControl=InstallSynthetic(rd,second);
            FamilyGpuComputeUtility.Update(rd,leftControl,[8,0],"NReadback.RightFaultLeft");
            FamilyGpuComputeUtility.Update(rd,rightControl,[9,4],"NReadback.RightFaultRight");
            try { NeowFamilyGpuExecutor.ExecuteDensePair(first,leftBatch,second,rightBatch,CancellationToken.None); throw new InvalidDataException("NReadback.RightFaultAccepted"); }
            catch(NeowDensePairRightFailure failure)
            {
                if(failure.RightBatch!=rightBatch || failure.InnerException is not InvalidDataException || failure.Left is null || failure.LeftMetrics.Survivors!=8 || failure.SharedMs<=0)
                    throw new InvalidDataException("NReadback.RightFaultLostContext",failure);
                Equal(failure.Left,SyntheticExpected(leftBatch,8),"RightFaultPreservesLeft");
            }
            try { second.Execute(FamilyCandidateSet.Dense(rightBatch),CancellationToken.None,out _); throw new InvalidDataException("NReadback.RightFaultBindingReused"); }
            catch(InvalidOperationException failure) when(failure.Message=="NFamilyGpuInitializationFaulted") { }
            Equal(first.Execute(FamilyCandidateSet.Dense(leftBatch),CancellationToken.None,out _),SyntheticExpected(leftBatch,8),"RightFaultLeftReuse");
        }
        var nativeRightBoundaryFaults=NativeRightBoundaryFaults(rd,nativePlan);
        return new { syntheticTransportOnly=true,privatePairs,publicPairs,nativeRightBoundaryFaults,
            privateCounts=new[] { 0,1,8,9 },publicPrefixAndTailCounts=new[] { 0,7,9,1024,1025,65536,65537 },
            handAuthoredCompletePopulationOracle=true,privateInlineAndFull=true,sharedScratch=true,
            preRecordCancelReuse=true,postRecordCancelDuringSyncReuse=true,rightCollectFailurePreservesLeft=true,
            syntheticProtocolIsNumericalNParity=false };
    }

    private static object NativeRightBoundaryFaults(RenderingDevice rd,NeowFamilyGpuPlan plan)
    {
        var results=new List<object>();
        var executorType=typeof(NeowFamilyGpuExecutor);
        var preparedType=executorType.GetNestedType("Prepared",BindingFlags.NonPublic)!;
        var pendingField=preparedType.GetField("Pending",Members)!;
        var bankField=executorType.GetField("_readbackBank",Members)!;
        var metaField=executorType.GetField("_meta",Members)!;
        var queueMethod=executorType.GetMethod("QueueReadback",Members)!;
        var pendingType=typeof(FamilyGpuReadback.Pending);
        var bytesField=pendingType.GetField("_bytes",Members)!;
        var callbacksField=pendingType.GetField("_callbacks",Members)!;
        var callbackThreadField=pendingType.GetField("_callbackThread",Members)!;
        var nativeErrorField=pendingType.GetField("_error",Members)!;
        var scheduleFailureField=pendingType.GetField("_queueFailure",Members)!;
        foreach(string stage in new[] { "copy","queue","completion" })
        {
            using var first=new NeowFamilyGpuExecutor(rd,plan,80);
            using var second=new NeowFamilyGpuExecutor(rd,plan,81,sharedDenseScratch:first);
            Rid leftControl=InstallSynthetic(rd,first),rightControl=InstallSynthetic(rd,second);
            AssertSharedScratch(first,second);
            var leftBatch=new SearchBatch(24680,32); var rightBatch=new SearchBatch(86420,35);
            FamilyGpuComputeUtility.Update(rd,leftControl,[8,0],"NReadback.NativeFaultLeft");
            FamilyGpuComputeUtility.Update(rd,rightControl,[9,0],"NReadback.NativeFaultRight");
            FamilyGpuReadback.Pending? originalPending=null;
            Error queueError=Error.Ok,sentinelError=Error.Ok;
            Exception? realScheduleFailure=null;
            Exception? sentinelMutationFailure=null;
            Callable sentinel=default;
            int sentinelCallbacks=0,probes=0;
            Action<int,object> probe=(slot,prepared)=>
            {
                if(slot!=1)return;
                probes++;
                originalPending=(FamilyGpuReadback.Pending)pendingField.GetValue(prepared)!;
                if(stage=="copy")
                {
                    Rid originalBank=(Rid)bankField.GetValue(second)!;
                    try
                    {
                        // The real 32-byte header fits this live meta buffer;
                        // its following prefix does not. QueueReadback must
                        // retain the native BufferCopy error until shared Sync.
                        bankField.SetValue(second,(Rid)metaField.GetValue(second)!);
                        queueMethod.Invoke(second,[prepared]);
                    }
                    finally { bankField.SetValue(second,originalBank); }
                }
                else if(stage=="queue")
                {
                    // This invokes Godot's real async scheduling API with an
                    // invalid RID; it does not manufacture QueueFailure.
                    var rejected=FamilyGpuReadback.Queue(rd,default,0,32,"NReadback.InjectedRightQueue");
                    queueError=(Error)nativeErrorField.GetValue(rejected)!;
                    realScheduleFailure=(Exception?)scheduleFailureField.GetValue(rejected);
                    pendingField.SetValue(prepared,rejected);
                }
                else
                {
                    var pending=originalPending;
                    sentinel=Callable.From<byte[]>(_=>
                    {
                        // Memory-only injection after a real right download.
                        // A non-one callback count remains invalid regardless
                        // of ordering with that download's native callback.
                        try
                        {
                            bytesField.SetValue(pending,new byte[1]);
                            callbacksField.SetValue(pending,7);
                        }
                        catch(Exception failure) { sentinelMutationFailure=failure; }
                        finally { sentinelCallbacks++; }
                    });
                    sentinelError=rd.BufferGetDataAsync(rightControl,sentinel,0,4);
                }
            };
            Console.WriteLine($"nReadbackIntentionalNativeFault={stage};slot=1;expectedNativeErrorDiagnostics={stage is "copy" or "queue"}");
            NeowDensePairRightFailure? observed=null;
            try
            {
                NeowFamilyGpuExecutor.ExecuteDensePair(first,leftBatch,second,rightBatch,CancellationToken.None,probe);
            }
            catch(NeowDensePairRightFailure failure) { observed=failure; }
            if(stage=="completion" && sentinelMutationFailure is not null)
                throw new InvalidDataException("NReadback.CompletionInjectionUnavailable",sentinelMutationFailure);
            if(stage=="completion" && (sentinelError!=Error.Ok || sentinelCallbacks!=1))
                throw new InvalidDataException($"NReadback.CompletionSentinelUnavailable:error={sentinelError};callbacks={sentinelCallbacks}");
            if(stage=="queue" && queueError==Error.Ok && realScheduleFailure is null)
                throw new InvalidDataException("NReadback.NativeQueueInjectionWasAccepted");
            if(probes!=1 || observed is null || observed.Left is null || observed.RightBatch!=rightBatch ||
                observed.LeftMetrics.Processed!=leftBatch.BatchCandidateCount || observed.LeftMetrics.Survivors!=8 || observed.SharedMs<=0)
                throw new InvalidDataException("NReadback.NativeRightFaultMissingOrLostContext:"+stage,observed);
            Equal(observed.Left,SyntheticExpected(leftBatch,8),"NativeRightFaultPreservesLeft:"+stage);
            Exception cause=observed.InnerException ?? throw new InvalidDataException("NReadback.NativeRightFaultCauseLost:"+stage);
            if(stage=="copy")
            {
                if(cause is not InvalidOperationException || !cause.Message.StartsWith($"N.PrefixBankCopy:batch={rightBatch.BatchBase};error=",StringComparison.Ordinal))
                    throw new InvalidDataException("NReadback.NativeCopyCauseChanged",cause);
                // Even though Prepared.Pending was replaced by the injected
                // queue, the original native callback must already be drained.
                _=originalPending!.GetData();
            }
            else if(stage=="queue")
            {
                if(realScheduleFailure is not null ? !ReferenceEquals(cause,realScheduleFailure) :
                    cause is not InvalidOperationException || cause.Message!="NReadback.InjectedRightQueue.ReadbackSchedule:"+queueError)
                    throw new InvalidDataException("NReadback.NativeQueueCauseChanged",cause);
                _=originalPending!.GetData();
            }
            else
            {
                if(sentinelError!=Error.Ok || sentinelCallbacks!=1 || cause is not InvalidDataException ||
                    !cause.Message.StartsWith($"N.PublicBoundary:batch={rightBatch.BatchBase}.ReadbackCompletion:",StringComparison.Ordinal) ||
                    (int)callbacksField.GetValue(originalPending!)! is not (7 or 8) ||
                    (int)callbackThreadField.GetValue(originalPending!)! != System.Environment.CurrentManagedThreadId)
                    throw new InvalidDataException("NReadback.NativeCompletionGuardNotExercised",cause);
            }
            try { second.Execute(FamilyCandidateSet.Dense(rightBatch),CancellationToken.None,out _); throw new InvalidDataException("NReadback.NativeRightFaultBindingReused:"+stage); }
            catch(InvalidOperationException failure) when(failure.Message=="NFamilyGpuInitializationFaulted") { }
            Equal(first.Execute(FamilyCandidateSet.Dense(leftBatch),CancellationToken.None,out _),SyntheticExpected(leftBatch,8),"NativeRightFaultLeftReuse:"+stage);
            GC.KeepAlive(sentinel); GC.KeepAlive(originalPending);
            results.Add(new { stage,rightBatch=rightBatch.BatchBase,actualNativeBoundary=true,leftPreserved=true,
                originalRightCallbackDrained=true,rightBindingUnusable=true,leftReuse=true,
                expectedNativeErrorDiagnostics=stage is "copy" or "queue" });
        }
        return results;
    }

    private static void Equal(FamilyCandidateSet actual, FamilyCandidateSet expected, string scope)
    {
        if (actual.Batch != expected.Batch || !actual.EnumerateLogicalOrdinals().SequenceEqual(expected.EnumerateLogicalOrdinals()))
            throw new InvalidDataException("NReadback." + scope);
    }
}
