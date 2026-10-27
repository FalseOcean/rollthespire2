using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Existing Production Family bindings only: no shader substitutions, synthetic
// predicates, new numerical body, or same-run price updates. Family-local timing
// decomposition is distinct from whole Product allocation E2E evidence.
internal static partial class FamilyPhysicalMatrix
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int RootCapacity = 1 << 24;
    private static object? Get(object value, string name) => value.GetType().GetProperty(name, Instance)!.GetValue(value);
    private static object? Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, Instance)!.Invoke(value, args);
    private static string Digest(FamilyCandidateSet set) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(set.ExportAbi1().Span)));
    internal static void Run(ExactSearchExecutionRequest request, string filter, string output)
    {
        if (filter == "mp-c-reuse")
        {
            RunPartyCombatReuseChecks(request, output);
            return;
        }
        if (filter == "mp-c-reuse-bench")
        {
            RunPartyCombatReuseBench(request, output);
            return;
        }
        if (filter == "mp-c-reuse-bench-small")
        {
            RunPartyCombatReuseBench(request, output, 1 << 16);
            return;
        }
        if (filter == "mp-r-capacity")
        {
            RunPartyRelicCapacityChecks(request, output);
            return;
        }
        if (filter == "mp-r-capacity-wrapper")
        {
            RunPartyRelicCapacityWrapperChecks(request, output);
            return;
        }
        if (filter == "mp-s1-check")
        {
            RunPartyMerchantChecks(request, output);
            return;
        }
        if (filter == "mp-s-order")
        {
            RunPartyMerchantOrderChecks(request, output);
            return;
        }
        if (filter == "mp-s-order-holdout")
        {
            RunPartyMerchantOrderHoldoutChecks(request, output);
            return;
        }
        if (filter == "mp-cost")
        {
            RunPartyCostChecks(request, output);
            return;
        }
        if (filter == "mp-party-order-price")
        {
            RunPartyOrderPricingProbe(request, output);
            return;
        }
        if (filter == "mp-party-order-price-holdout")
        {
            RunPartyOrderPricingProbe(request, output, (1UL << 38) + 987654321UL);
            return;
        }
        if (filter == "mp-rare-control")
        {
            ExportPartyRareControl(request, output);
            return;
        }
        if (filter == "mp-c-e2e-fixture")
        {
            ExportPartyCombatE2eFixture(request, output);
            return;
        }
        if (filter == "mp-cost-fixture")
        {
            ExportPartyCostFixture(request, output);
            return;
        }
        if (filter is "s-identity-repro" or "s-identity-check")
        {
            RunMerchantIdentityChecks(request, output, requireParity: filter == "s-identity-check");
            return;
        }
        if (filter == "reliability-sr")
        {
            RunSrReliability(request, output);
            return;
        }
        FamilyPricingInventory.Run(request, output);
        var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request);
        var bind = typeof(IFamilyInvocation).GetMethod("BindPrivateSerial", Instance)!;
        var admitted = typeof(IFamilyInvocation).GetProperty("CanBindPrivateSerial", Instance)!;
        var bufferType = typeof(FamilyCandidateSet).Assembly.GetType("RolltheSpire2.Search.FamilyExecution.PrivateOrdinalBuffer", true)!;
        object Buffer(RenderingDevice rd) => Activator.CreateInstance(bufferType, Instance, null, [rd], null)!;
        const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        ulong first = 0; foreach (char c in request.CanonicalStartSeed) first = checked(first * 34 + (uint)alphabet.IndexOf(c));
        int max = (int)Math.Min(RootCapacity, request.ScanCount);
        int[] populations = new[] { 64, 512, 1024, 2048, 4096, 4438, 8192, 16384, 65536, 262144, 1 << 20, 1 << 22, RootCapacity, max }.Where(n => n > 0 && n <= max).Distinct().Order().ToArray();
        var rows = new List<object>();
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("Matrix.DeviceUnavailable");
        foreach (var family in families.Where(f => filter == "all" || filter.Contains(f.FamilyId[0])))
        {
            if (!(bool)admitted.GetValue(family)!) { rows.Add(new { family.FamilyId, Status = "PrivateBindingNotAdmitted" }); continue; }
            var fingerprints = new Dictionary<(bool Compact, int Count), string>();
            bool transformation = family.FamilyId == "T.TransformationAggregate";
            bool cpuParity = transformation || family.FamilyId == "R.Relic";
            var cpu = cpuParity ? ((IEnumerable<IFamilyInvocation>)typeof(IFamilyInvocation).GetProperty("CpuRealizations", Instance)!.GetValue(family)!).First() : null;
            var cpuContext = new FamilyExecutionContext(requiresGpu: false);
            if (transformation)
            {
                using var alias = (IDisposable)Buffer(rd);
                try { using var invalid = (IDisposable)bind.Invoke(family,[rd,alias,alias])!; throw new InvalidDataException("T.AliasAdmitted"); }
                catch(TargetInvocationException ex) when(ex.InnerException is ArgumentException e && e.Message=="T.PrivateBuffersAlias") { }
            }
            foreach (bool compact in new[] { false, true })
            foreach (bool privateOutput in new[] { false, true })
            {
                object? inputBuffer = null, outputBuffer = null, execution = null;
                try
                {
                    if (compact) inputBuffer = Buffer(rd);
                    if (privateOutput) outputBuffer = Buffer(rd);
                    execution = bind.Invoke(family, [rd, inputBuffer, outputBuffer])!;
                    if (transformation)
                    {
                        var empty = Call(execution, "Execute", new SearchBatch(first, 0), 0, CancellationToken.None)!;
                        if ((int)Get(empty, "Count")! != 0) throw new InvalidDataException("T.Zero");
                        using var canceled = new CancellationTokenSource(); canceled.Cancel();
                        try { Call(execution, "Execute", new SearchBatch(first, 67), 67, canceled.Token); throw new InvalidDataException("T.CancelMissing"); }
                        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { }
                    }
                    foreach (int population in populations)
                    {
                        var batch = new SearchBatch(first, compact ? RootCapacity : population);
                        if (compact)
                        {
                            // Unique, reverse-ordered spread across one batch; private
                            // Compact must not rely on public ordering or dense ranks.
                            var ordinals = new uint[population];
                            for (int i = 0; i < population; i++) ordinals[i] = (uint)((ulong)(population - 1 - i) * RootCapacity / (uint)population);
                            byte[] bytes = MemoryMarshal.AsBytes(ordinals.AsSpan()).ToArray();
                            rd.BufferUpdate((Rid)Get(inputBuffer!, "Buffer")!, 0, (uint)bytes.Length, bytes);
                        }
                        for (int round = -1; round < 3; round++)
                        {
                            var result = Call(execution, "Execute", batch, population, CancellationToken.None)!;
                            int count = (int)Get(result, "Count")!;
                            if (round < 0) continue;
                            string? digest = null;
                            if (round == 2)
                            {
                                var materialized = (FamilyCandidateSet?)(Get(result, "PublicOutput") ??
                                    Call(outputBuffer!, "ReadForVerification", batch, count));
                                digest = Digest(materialized!);
                                if (cpuParity && !fingerprints.ContainsKey((compact, population)))
                                {
                                    var cpuInput = compact ? FamilyCandidateSet.FromAbi1(batch,
                                        Enumerable.Range(0,population).Select(i=>(ulong)i*(ulong)RootCapacity/(uint)population).ToArray()) : FamilyCandidateSet.Dense(batch);
                                    for(int cpuRound=-1;cpuRound<3;cpuRound++)
                                    {
                                        var watch=System.Diagnostics.Stopwatch.StartNew();
                                        var cpuResult=cpu!.InvokeAsync(cpuContext,new(batch,request),cpuInput,default).GetAwaiter().GetResult();
                                        double ms=watch.Elapsed.TotalMilliseconds;
                                        if(Digest(cpuResult)!=digest)throw new InvalidDataException(family.FamilyId+".FullCpuGpuPopulationMismatch");
                                        if(cpuRound>=0) rows.Add(new { family.FamilyId, Cpu=true, CompactInput=compact, Input=population, Output=count, Round=cpuRound, CanonicalMs=ms, Digest=digest });
                                    }
                                }
                                if (fingerprints.TryGetValue((compact, population), out var expected) && expected != digest)
                                    throw new InvalidDataException("Matrix.PublicPrivatePopulationMismatch:" + family.FamilyId);
                                fingerprints[(compact, population)] = digest;
                            }
                            rows.Add(new {
                                family.FamilyId, CompactInput = compact, PrivateOutput = privateOutput, Input = population, Output = count,
                                Round = round, SetupMs = Get(execution, "SetupMs"), DispatchSyncMs = Get(result, "DispatchSyncMs"),
                                ReportedReadbackMs = Get(result, "ReportedReadbackMs"), ReadbackBytes = Get(result, "ReadbackBytes"),
                                PhysicalReadyMs = Get(result, "CanonicalMs"), Dispatches = Get(result, "Dispatches"), Digest = digest,
                                Boundary = privateOutput ? "PrivateOrdinalCountReady" : "CanonicalAbi1Ready",
                                Quote = family.QuotePhysicalWork(new(compact, privateOutput, population, compact)),
                                VerificationOutsideTiming = true
                            });
                        }
                    }
                    if (transformation && compact)
                    {
                        rd.BufferUpdate((Rid)Get(inputBuffer!, "Buffer")!, 0, 4, BitConverter.GetBytes(uint.MaxValue));
                        try { Call(execution,"Execute",new SearchBatch(first,3),1,CancellationToken.None); throw new InvalidDataException("T.BadOrdinalDidNotFault"); }
                        catch(TargetInvocationException ex) when(ex.InnerException is InvalidDataException e && e.Message.Contains("HeaderFault")) { }
                    }
                }
                finally
                {
                    // All three lifetimes must close even if one release faults.
                    var failures = new List<Exception>();
                    foreach (var resource in new[] { execution, outputBuffer, inputBuffer })
                        try { (resource as IDisposable)?.Dispose(); } catch (Exception ex) { failures.Add(ex); }
                    if (failures.Count > 0) throw new AggregateException("Matrix.CleanupFailed", failures);
                }
            }
            cpu?.DisposeAsync(cpuContext).GetAwaiter().GetResult();
            cpuContext.DisposeAsync().GetAwaiter().GetResult();
        }
        File.WriteAllText(Path.Combine(output, "family-physical-matrix.json"), JsonSerializer.Serialize(new {
            Source = "ProductionCompiledFamilyBindings", Device = rd.GetDeviceName(), Context = request.SnapshotFingerprint,
            SameRunRepricing = false, ControlledUnorderedCompact = true, Populations = populations, Rows = rows
        }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine($"familyPhysicalMatrix=PASS;rows={rows.Count};numericalBodiesChanged=0;publicPrivatePopulationParity=true");
    }
}
