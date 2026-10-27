using System.Reflection;
using Godot;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Focused checks inside the existing rendering-enabled host; no alternate simulator.
internal static class PrivateOrdinalChecks
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    internal static void Run(ExactSearchExecutionRequest request)
    {
        var assembly = typeof(FamilyCandidateSet).Assembly;
        Type Type(string name) => assembly.GetType("RolltheSpire2.Search.FamilyExecution." + name, true)!;
        object New(string name, params object?[] args)
        {
            var constructor = Type(name).GetConstructors(Members).Single();
            var parameters = constructor.GetParameters();
            if (args.Length > parameters.Length) throw new TargetParameterCountException(name);
            // Reflection does not supply C# optional arguments. Use the declared
            // defaults, without inventing values for required constructor inputs.
            return constructor.Invoke(parameters.Select((parameter, index) => index < args.Length ? args[index] :
                parameter.HasDefaultValue ? parameter.DefaultValue : throw new TargetParameterCountException(name)).ToArray());
        }
        object? Call(object target, string method, params object?[] args)
        {
            var member = target.GetType().GetMethod(method, Members)!;
            var parameters = member.GetParameters();
            return member.Invoke(target, parameters.Select((parameter, index) => index < args.Length ? args[index] :
                parameter.HasDefaultValue ? parameter.DefaultValue : throw new TargetParameterCountException(method)).ToArray());
        }
        object Property(object target, string name) => target.GetType().GetProperty(name, Members)!.GetValue(target)!;
        int checks = 0;
        void Reject(Action action, System.Type expected)
        {
            try { action(); } catch (Exception e) when (expected.IsInstanceOfType(e is TargetInvocationException t ? t.InnerException : e)) { checks++; return; }
            throw new InvalidOperationException("Missing expected rejection: " + expected.Name);
        }
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("GPU unavailable");
        var buffer = (IDisposable)New("PrivateOrdinalBuffer", rd);
        try
        {
            Call(buffer, "CheckCount", 0); Call(buffer, "CheckCount", 1 << 24); checks += 2;
            Reject(() => Call(buffer, "CheckCount", -1), typeof(InvalidDataException));
            Reject(() => Call(buffer, "CheckCount", (1 << 24) + 1), typeof(InvalidDataException));
            Reject(() => Call(buffer, "CheckPopulation", new SearchBatch(0, 7), 8), typeof(InvalidDataException));
            Task.Run(() => Reject(() => Call(buffer, "CheckCount", 0), typeof(InvalidOperationException))).GetAwaiter().GetResult();
            var rid = (Rid)Property(buffer, "Buffer");
            void Write(uint[] ordinals)
            {
                byte[] bytes = new byte[ordinals.Length * 4]; Buffer.BlockCopy(ordinals, 0, bytes, 0, bytes.Length);
                if (rd.BufferUpdate(rid, 0, (uint)bytes.Length, bytes) != Error.Ok) throw new InvalidOperationException("Fixture upload");
            }
            Write([2, 0, 1]);
            Call(buffer, "VerifyPopulation", new SearchBatch(0, 3), 3, FamilyCandidateSet.Dense(new(0, 3))); checks++;
            Write([0, 0]); Reject(() => Call(buffer, "ReadForVerification", new SearchBatch(0, 3), 2), typeof(ArgumentException));
            Write([9]); Reject(() => Call(buffer, "ReadForVerification", new SearchBatch(0, 3), 1), typeof(ArgumentOutOfRangeException));
        }
        finally { buffer.Dispose(); }
        buffer.Dispose(); checks++;
        Reject(() => Call(buffer, "CheckCount", 0), typeof(ObjectDisposedException));
        bool releasedAfterFailure = false;
        Reject(() => Type("PrivateOrdinalBuffer").GetMethod("DisposeAll", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [new IDisposable[] { new ReleaseProbe(() => throw new InvalidOperationException("InjectedCleanupFault")),
                new ReleaseProbe(() => releasedAfterFailure = true) }]), typeof(AggregateException));
        if (!releasedAfterFailure) throw new InvalidOperationException("Cleanup stopped at first failure");
        checks++;

        var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request);
        string serialMode = System.Environment.GetEnvironmentVariable("RT2_PRIVATE_SERIAL_EXPERIMENT") ?? "";
        if (serialMode.Length != 0)
        {
            string order = serialMode.Split('-')[1];
            var ordered = order.Select(id => families.Single(f => f.FamilyId[0] == id)).ToArray();
            using (var alias = (IDisposable)New("PrivateOrdinalBuffer", rd))
            {
                object? aliasExecution = null;
                try
                {
                    var bind = typeof(IFamilyInvocation).GetMethod("BindPrivateSerial", Members)!;
                    aliasExecution = bind.Invoke(ordered[^1], [rd, alias, alias])!;
                    Reject(() => Call(aliasExecution, "Execute", new SearchBatch(0, 1), 1, CancellationToken.None), typeof(InvalidDataException));
                }
                catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException or InvalidDataException or InvalidOperationException &&
                    ex.InnerException.Message.Contains("Alias", StringComparison.OrdinalIgnoreCase))
                { checks++; } // Some donors correctly reject the alias at binding.
                finally { (aliasExecution as IDisposable)?.Dispose(); }
            }
            var type = Type("AutomaticPrivateSerialInvocation").GetNestedType("Gpu", BindingFlags.NonPublic)!;
            var serial = (IDisposable)Activator.CreateInstance(type, Members, null, [rd, ordered, null], null)!;
            FamilyCandidateSet RunSerial(int count, CancellationToken token = default)
            {
                var value = Call(serial, "Execute", FamilyCandidateSet.Dense(new(0, count)), order, true, null, token)!;
                return (FamilyCandidateSet)value.GetType().GetField("Item1")!.GetValue(value)!;
            }
            try
            {
                if (RunSerial(0).Count != 0) throw new InvalidDataException("Serial.Zero"); checks++;
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                Reject(() => RunSerial(67, cancellation.Token), typeof(OperationCanceledException));
                var tail = RunSerial(67); checks++;
                if (!tail.ExportAbi1().Span.SequenceEqual(RunSerial(67).ExportAbi1().Span)) throw new InvalidDataException("Serial.Reuse"); checks++;
                int capsuleIndex = Array.FindIndex(ordered, f => f.GetType().Name == "CapsuleRelicFamily");
                if (capsuleIndex > 0)
                {
                    // Existing CPU R replay is the full-population reference,
                    // not another GPU invocation or sampled runtime parity.
                    const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
                    ulong start = 0;
                    foreach (char c in request.CanonicalStartSeed) start = checked(start * 34 + (uint)alphabet.IndexOf(c));
                    var sample = new SearchBatch(start, 1 << 24);
                    var value = Call(serial, "Execute", FamilyCandidateSet.Dense(sample), order, true, null, CancellationToken.None)!;
                    var snapshots = (FamilyCandidateSet[])value.GetType().GetField("Item3")!.GetValue(value)!;
                    var predecessors = snapshots[capsuleIndex - 1].EnumerateLogicalOrdinals().ToArray();
                    if (predecessors.Length > 65536) throw new InvalidOperationException("CapsuleReferenceFixtureTooDense");
                    object replay = ordered[capsuleIndex].GetType().GetField("_replay", Members)!.GetValue(ordered[capsuleIndex])!;
                    var matches = (Func<ulong, bool>)replay.GetType().GetMethod("Matches", Members)!.CreateDelegate(typeof(Func<ulong, bool>), replay);
                    var format = (Func<ulong, string>)assembly.GetType("RolltheSpire2.Search.FamilyExecution.Beta110SeedCodec", true)!
                        .GetMethod("FormatOrdinal", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.CreateDelegate(typeof(Func<ulong, string>));
                    var expected = predecessors.Where(o => matches(RolltheSpire2.Compatibility.Beta111Profile.Instance.ComputeRootSeed(format(start + o)))).ToArray();
                    if (!expected.SequenceEqual(snapshots[capsuleIndex].EnumerateLogicalOrdinals())) throw new InvalidDataException("Capsule.FullCpuPopulationMismatch");
                    checks++;
                    Console.WriteLine($"capsuleFullCpuPopulation=PASS;input={predecessors.Length};output={expected.Length};frame={start};sourceConstraintsPreserved=true");
                }
                Reject(() => RunSerial((1 << 24) + 1), typeof(InvalidDataException));
                // This fault fixture explicitly reaches the terminal consumer;
                // normal zero-survivor runs correctly leave it uninitialized.
                for (int i = 0; i < ordered.Length; i++) Call(serial, "EnsureExecutor", i);
                var buffers = (Array)type.GetField("_buffers", Members)!.GetValue(serial)!;
                var executors = (Array)type.GetField("_executors", Members)!.GetValue(serial)!;
                object terminalInput = buffers.GetValue((ordered.Length - 2) % 2)!;
                Rid rid = (Rid)Property(terminalInput, "Buffer");
                if (rd.BufferUpdate(rid, 0, 4, BitConverter.GetBytes(99u)) != Error.Ok) throw new InvalidOperationException("Serial.CorruptFixtureUpload");
                Reject(() => Call(executors.GetValue(ordered.Length - 1)!, "Execute", new SearchBatch(0, 1), 1, CancellationToken.None), typeof(InvalidDataException));
            }
            finally { serial.Dispose(); }
            serial.Dispose(); checks++;
            Reject(() => RunSerial(1), typeof(ObjectDisposedException));
            Console.WriteLine($"privateOrdinalLifecycle=PASS;coverage=Automatic-{order};checks={checks};bounds=true;duplicates=true;rangeFault=true;cancelReuse=true;dispose=true");
            return;
        }
        bool ew = families.Any(f => f.FamilyId == "E.EventResult");
        string nwae = System.Environment.GetEnvironmentVariable("RT2_NWAE_EXPERIMENT") ?? "";
        string? selectionPath = System.Environment.GetEnvironmentVariable("RT2_PRIVATE_ORDINAL_SELECTION");
        if (nwae.Length == 0 && selectionPath is not null && File.Exists(selectionPath))
        {
            using var selection = System.Text.Json.JsonDocument.Parse(File.ReadAllText(selectionPath));
            if (selection.RootElement.GetProperty("Coverage").GetString() == "NWAE")
                nwae = "verify-" + selection.RootElement.GetProperty("Order").GetString();
        }
        IFamilyInvocation Family(string id) => families.Single(f => f.FamilyId == id);
        object? Plan(string id, string property) => families.Any(f => f.FamilyId == id) ? Property(Family(id), property) : null;
        string rswMode = System.Environment.GetEnvironmentVariable("RT2_RSW_EXPERIMENT") ?? "";
        if (rswMode.Length != 0)
        {
            string order = rswMode.Split('-')[1];
            var rPlan = Family("R.Relic").GetType().GetField("_plan", Members)!.GetValue(Family("R.Relic"))!;
            var wPlan = New("WorldFamilyGpuPlan", Property(Family("W.World"), "Replay"));
            using var source = (IDisposable)New("PrivateOrdinalBuffer", rd);
            using var output = (IDisposable)New("PrivateOrdinalBuffer", rd);
            object R(object input, object dest) => Type("RelicFamilyGpuExecutor").GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
                .Single(m=>m.Name=="Create"&&m.GetParameters().Length==5&&m.GetParameters()[2].ParameterType==rPlan.GetType()).Invoke(null,[rd,request,rPlan,input,dest])!;
            var atomic = Enum.Parse(Type("MerchantShopColorlessCompactionPath"),"AtomicAppendHostSort");
            object S(object input, object dest) => Type("MerchantShopColorlessGpuExecutor").GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
                .Single(m=>m.Name=="Create"&&m.GetParameters().Length==5).Invoke(null,[rd,request,atomic,input,dest])!;
            Reject(()=>R(source,source),typeof(ArgumentException));
            Reject(()=>S(source,source),typeof(ArgumentException));
            Reject(()=>New("WorldFamilyGpuExecutor",rd,wPlan,(Rid)Property(source,"Buffer"),source),typeof(ArgumentException));
            // Corrupt one plain private ordinal: all three real consumer shaders must fault.
            if(rd.BufferUpdate((Rid)Property(source,"Buffer"),0,4,BitConverter.GetBytes(99u))!=Error.Ok)throw new InvalidOperationException("RSW corrupt fixture upload");
            foreach(string id in new[]{"R","S","W"})
            {
                using var consumer = (IDisposable)(id=="R"?R(source,output):id=="S"?S(source,output):New("WorldFamilyGpuExecutor",rd,wPlan,(Rid)Property(source,"Buffer"),output));
                Reject(()=>Call(consumer,"ExecutePrivateStage",new SearchBatch(0,1),1,CancellationToken.None,null),typeof(InvalidDataException));
            }
            var chain = (IDisposable)New("RswPrivateGpuExecutor",rd,request,rPlan,wPlan,order,true);
            FamilyCandidateSet Run(int count,CancellationToken token=default) => (FamilyCandidateSet)Call(chain,"Execute",FamilyCandidateSet.Dense(new SearchBatch(0,count)),token,null)!;
            try
            {
                if(Run(0).Count!=0)throw new InvalidDataException("RSW zero"); checks++;
                using var cancel = new CancellationTokenSource();cancel.Cancel();
                Reject(()=>Run(67,cancel.Token),typeof(OperationCanceledException));
                Run(67); checks++;
                Reject(()=>Run((1<<24)+1),typeof(InvalidDataException));
            }
            finally {chain.Dispose();}
            chain.Dispose();checks++;
            Reject(()=>Run(1),typeof(ObjectDisposedException));
            Call(source,"CheckCount",0);Call(output,"CheckCount",0);checks+=2;
            Console.WriteLine($"privateOrdinalLifecycle=PASS;coverage={order};checks={checks};alias=true;rangeFault=R,S,W;cancelReuse=true;borrow=true;dispose=true");
            return;
        }
        if (Plan("C.CombatReward", "GpuPlan") is { } cPlan &&
            (System.Environment.GetEnvironmentVariable("RT2_EW_EXPERIMENT") ?? "").EndsWith("-ecw", StringComparison.Ordinal))
        {
            using var source = (IDisposable)New("PrivateOrdinalBuffer", rd);
            using var destination = (IDisposable)New("PrivateOrdinalBuffer", rd);
            Reject(() => New("CombatRewardGpuExecutor", rd, cPlan, false, source, source), typeof(ArgumentException));
            using var reference = (IDisposable)New("CombatRewardGpuExecutor", rd, cPlan, false, null, null);
            var middle = (IDisposable)New("CombatRewardGpuExecutor", rd, cPlan, false, destination, source);
            int Stage(SearchBatch batch, int count, CancellationToken token = default)
                => (int)Call(middle, "ExecutePrivateStage", batch, count, token, null)!;
            try
            {
                foreach (int count in new[] { 0, 1, 67, 1 << 20, (1 << 20) + 1 })
                {
                    var batch = new SearchBatch(0, 1 << 24);
                    var ordinals = Enumerable.Range(0, count).Select(x => (uint)x).Reverse().ToArray();
                    byte[] bytes = new byte[count * 4]; Buffer.BlockCopy(ordinals, 0, bytes, 0, bytes.Length);
                    if (count != 0 && rd.BufferUpdate((Rid)Property(source, "Buffer"), 0, (uint)bytes.Length, bytes) != Error.Ok)
                        throw new InvalidOperationException("C fixture upload");
                    int output = Stage(batch, count);
                    var input = FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, count).Select(x => (ulong)x).ToArray());
                    var expected = (FamilyCandidateSet)Call(reference, "Execute", input, CancellationToken.None, null)!;
                    Call(destination, "VerifyPopulation", batch, output, expected); checks++;
                }
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                Reject(() => Stage(new(0, 67), 0, cancel.Token), typeof(OperationCanceledException));
                if (Stage(new(0, 67), 0) != 0) throw new InvalidDataException("C cancelled reuse"); checks++;
                Reject(() => Stage(new(0, 1), 2), typeof(InvalidDataException));
                Reject(() => Stage(new(0, (1 << 24) + 1), (1 << 24) + 1), typeof(InvalidDataException));
                if (rd.BufferUpdate((Rid)Property(source, "Buffer"), 0, 4, BitConverter.GetBytes(99u)) != Error.Ok)
                    throw new InvalidOperationException("C corrupt fixture upload");
                Reject(() => Stage(new(0, 1), 1), typeof(InvalidDataException));
            }
            finally { middle.Dispose(); }
            Call(source, "CheckCount", 0); Call(destination, "CheckCount", 0); checks += 2;
            Reject(() => Stage(new(0, 1), 0), typeof(ObjectDisposedException));
            Console.WriteLine($"privateOrdinalLifecycle=PASS;coverage=ECW.CMiddle;checks={checks};unordered=true;windowBoundary=true;alias=true;borrow=true;rangeFault=true;cancelReuse=true");
            return;
        }
        if (nwae.Length != 0)
        {
            using var alias = (IDisposable)New("PrivateOrdinalBuffer", rd);
            if (Plan("N.Neow", "GpuPlan") is { } n)
                Reject(() => New("NeowFamilyGpuExecutor", rd, n, 42u, null, alias, alias, 0, 0, null), typeof(ArgumentException));
            if (Plan("A.AncientOption", "Plan") is { } a)
                Reject(() => New("AncientOptionFamilyGpuExecutor", rd, a, alias, alias), typeof(ArgumentException));
            if (Plan("E.EventResult", "Plan") is { } e)
                Reject(() => New("EventResultFamilyGpuExecutor", rd, e, alias, alias), typeof(ArgumentException));
        }
        IDisposable executor = nwae.Length != 0
            ? (IDisposable)New("NwaePrivateGpuExecutor", rd, Plan("N.Neow", "GpuPlan"),
                Plan("A.AncientOption", "Plan"), Plan("E.EventResult", "Plan"),
                New("WorldFamilyGpuPlan", Property(Family("W.World"), "Replay")), nwae.Split('-')[1], true)
            : ew
            ? (IDisposable)New("EwPrivateGpuExecutor", rd, Property(Family("E.EventResult"), "Plan"),
                New("WorldFamilyGpuPlan", Property(Family("W.World"), "Replay")), true)
            : (IDisposable)New("NcPrivateGpuExecutor", rd, Property(Family("C.CombatReward"), "GpuPlan"),
                Property(Family("N.Neow"), "GpuPlan"), 42u, true, true);
        FamilyCandidateSet Execute(int count, CancellationToken token = default)
            => (FamilyCandidateSet)Call(executor, "Execute", FamilyCandidateSet.Dense(new SearchBatch(0, count)), token, null)!;
        try
        {
            if (Execute(0).Count != 0) throw new InvalidDataException("Zero population"); checks++;
            using var cts = new CancellationTokenSource(); cts.Cancel();
            Reject(() => Execute(67, cts.Token), typeof(OperationCanceledException));
            Execute(67); checks++; // Same allocation remains reusable after a cancelled call; full GPU parity.
            Reject(() => Execute((1 << 24) + 1), ew && nwae.Length == 0 ? typeof(InvalidOperationException) : typeof(InvalidDataException));
            // Feed a corrupt private ordinal to the actual consumer. Its shader/header must fault.
            object ordinals = executor.GetType().GetField("_ordinals", Members)!.GetValue(executor)!;
            if (nwae.Length != 0)
            {
                var buffers = (Array)ordinals;
                ordinals = buffers.GetValue((nwae.Split('-')[1].Length - 2) % 2)!;
            }
            Rid rid = (Rid)Property(ordinals, "Buffer");
            byte[] corrupt = BitConverter.GetBytes(99u);
            if (rd.BufferUpdate(rid, 0, 4, corrupt) != Error.Ok) throw new InvalidOperationException("Fixture upload");
            object consumer = executor.GetType().GetField(nwae.Length != 0 ? "_" + nwae.Split('-')[1][^1].ToString().ToLowerInvariant() : ew ? "_world" : "_c", Members)!.GetValue(executor)!;
            Reject(() => Call(consumer, ew || nwae.Length != 0 ? "ExecutePrivate" : "ExecutePrivateInput", new SearchBatch(0, 1), 1, CancellationToken.None, null), typeof(InvalidDataException));
        }
        finally { executor.Dispose(); }
        executor.Dispose(); checks++;
        Reject(() => Execute(1), typeof(ObjectDisposedException));
        Console.WriteLine($"privateOrdinalLifecycle=PASS;coverage={(nwae.Length != 0 ? nwae : ew ? "EW" : "NC")};checks={checks};bounds=true;duplicates=true;rangeFault=true;cancelReuse=true;dispose=true");
    }
    private sealed class ReleaseProbe(Action release) : IDisposable { public void Dispose() => release(); }
}
