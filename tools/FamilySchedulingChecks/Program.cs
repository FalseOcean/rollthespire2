using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

var cli = new Dictionary<string, string>(StringComparer.Ordinal);
for (int index = 0; index < args.Length; index += 2)
{
    if (!args[index].StartsWith("--") || index + 1 >= args.Length)
        throw new ArgumentException("Use --name value pairs.");
    cli.Add(args[index][2..], args[index + 1]);
}
string Required(string name) => cli.TryGetValue(name, out string? value)
    ? Path.GetFullPath(value) : throw new ArgumentException("Missing --" + name);
string assemblyPath = Required("assembly"), outputPath = Required("output");
string referenceDirectory = Required("reference-dir");
int checks = 0;
var passed = new List<string>();
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
object? Invoke(MethodInfo method, object? target, params object?[] values)
{
    try { return method.Invoke(target, values); }
    catch (TargetInvocationException ex) when (ex.InnerException is not null)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
    }
}

var report = new Dictionary<string, object?>
{
    ["Scope"] = "Production assembly scheduling boundaries and CPU routing only; no Godot native initialization or GPU execution.",
    ["AssemblySha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assemblyPath)))
};
try
{
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        foreach (string directory in new[] { Path.GetDirectoryName(assemblyPath)!, referenceDirectory })
        {
            string file = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
        }
        return null;
    };
    Assembly production = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    Type T(string name) => production.GetType("RolltheSpire2." + name, true)!;
    MethodInfo Static(string type, string name, int count) => T(type).GetMethods(All)
        .Single(method => method.Name == name && method.GetParameters().Length == count);
    object Get(object value, string name) => value.GetType().GetProperty(name, All)!.GetValue(value)!;

    // 直接調用交付組件，避免測試另外實作一套 fallback。
    Type unsupportedType = T("Search.FamilyExecution.FamilyBatchNotSupportedException");
    Exception Unsupported() => (Exception)Activator.CreateInstance(unsupportedType, "test", null)!;
    MethodInfo run = Static("Search.FamilyExecution.FamilyEpochFallback", "RunAsync", 2)
        .MakeGenericMethod(typeof(string[]));
    Task<string[]> Run(Func<Task<string[]>> epoch, Func<Task<string[]>> fallback) =>
        (Task<string[]>)Invoke(run, null, epoch, fallback)!;
    string[] expected = ["window-1:empty", "window-2:7", "window-3:empty", "window-4:9"];
    int epochCalls = 0, fallbackCalls = 0;
    string[] normal = await Run(() => { epochCalls++; return Task.FromResult(expected); },
        () => { fallbackCalls++; return Task.FromResult(Array.Empty<string>()); });
    Check(ReferenceEquals(normal, expected) && epochCalls == 1 && fallbackCalls == 0,
        "Successful epoch must return once without fallback.");
    string[] recovered = await Run(() => { epochCalls++; throw Unsupported(); },
        () => { fallbackCalls++; return Task.FromResult(expected); });
    Check(ReferenceEquals(recovered, expected) && epochCalls == 2 && fallbackCalls == 1,
        "Unsupported tail must preserve complete ordered output and run fallback once.");
    string[] delayedUnsupported = await Run(() => Task.FromException<string[]>(Unsupported()),
        () => { fallbackCalls++; return Task.FromResult(expected); });
    Check(ReferenceEquals(delayedUnsupported, expected) && fallbackCalls == 2,
        "Asynchronous admission rejection must also run one fallback.");
    passed.Add("EpochSuccessAndUnsupportedTail");

    foreach (Exception failure in new Exception[] { new InvalidDataException("native-read-failure"),
                 new OperationCanceledException(new CancellationToken(true)) })
    {
        bool propagated = false;
        int before = fallbackCalls;
        try
        {
            await Run(() => Task.FromException<string[]>(failure),
                () => { fallbackCalls++; return Task.FromResult(expected); });
        }
        catch (Exception ex) when (ReferenceEquals(ex, failure)) { propagated = true; }
        Check(propagated && fallbackCalls == before,
            "Execution faults and cancellation must never retry a submitted group.");
    }
    Exception fallbackFailure = new InvalidOperationException("fallback-failed");
    bool fallbackPropagated = false;
    try { await Run(() => Task.FromException<string[]>(Unsupported()), () => Task.FromException<string[]>(fallbackFailure)); }
    catch (Exception ex) when (ReferenceEquals(ex, fallbackFailure)) { fallbackPropagated = true; }
    Check(fallbackPropagated, "Fallback failure must propagate without another retry.");
    passed.Add("EpochFaultCancellationAndFallbackFailure");

    Type candidates = T("Search.FamilyExecution.FamilyCandidateSet");
    Type batchType = T("Search.FamilyExecution.SearchBatch");
    object batch = Activator.CreateInstance(batchType, 0UL, 64)!;
    object Compact(int count) => Invoke(candidates.GetMethod("FromAbi1", All)!, null,
        batch, Enumerable.Range(0, count).Select(index => (ulong)index).ToArray())!;
    MethodInfo shouldOffload = Static("Search.FamilyExecution.TinyInputCpuOffload", "ShouldOffload", 1);
    bool tinyCompiled = (bool)T("Search.FamilyExecution.TinyInputCpuOffload").GetField("Compiled", All)!.GetRawConstantValue()!;
    report["TinyCpuCompiled"] = tinyCompiled;
    Check((int)T("Search.FamilyExecution.TinyInputCpuOffload").GetField("ThresholdInputs", All)!.GetRawConstantValue()! ==
        (tinyCompiled ? 32 : 0), "Compiled routing threshold must match the feature flag.");
    foreach (int count in new[] { 0, 1, 32, 33 })
        Check((bool)Invoke(shouldOffload, null, Compact(count))! == (tinyCompiled && count is > 0 and <= 32),
            "Production compact routing boundary: " + count);
    object dense = Invoke(candidates.GetMethod("Dense", All)!, null, batch)!;
    Check(!(bool)Invoke(shouldOffload, null, dense)!, "Dense windows must retain their GPU route.");
    passed.Add("TinyCompactRoutingBoundaries");

    // 開啟實驗分流時，另以實際 family 驗證 CPU 結果及 GPU parity。
    if (tinyCompiled)
    {
    object Read(string name, string path) => Invoke(T("Infrastructure.Snapshots.ProductionSearchReplay")
        .GetMethod("Read", All)!.MakeGenericMethod(T(name)), null, path)!;
    object context = Get(Read("Infrastructure.Snapshots.ProductionSearchReplayContext", Required("context")), "Context");
    object workload = Read("Infrastructure.Snapshots.ProductionSearchReplayWorkload", Required("workload"));
    object compiled = Invoke(Static("Search.Semantics.SearchCompiler", "Compile", 3), null,
        Get(workload, "Query"), context, null)!;
    object compilation = Invoke(Static("Search.Compilation.ExactSearchExecutionRequestFactory", "Compile", 2),
        null, compiled, Get(workload, "Options"))!;
    Check((bool)Get(compilation, "Success"), "Captured query must compile.");
    object plan = Get(compilation, "Plan");
    var families = ((IEnumerable)Invoke(Static("Search.FamilyExecution.FamilyExecutionCoordinator",
        "CreateRegisteredFamilies", 2), null, plan, true)!).Cast<object>().ToArray();
    object executionContext = Activator.CreateInstance(T("Search.FamilyExecution.FamilyExecutionContext"),
        All, null, [false, false], null)!;
    object window = Activator.CreateInstance(T("Search.FamilyExecution.FamilyObservationWindow"), batch, plan)!;
    ulong[] Ordinals(object value) => ((IEnumerable)Invoke(value.GetType().GetMethod("EnumerateLogicalOrdinals", All)!, value)!)
        .Cast<ulong>().ToArray();
    foreach (string familyName in new[] { "CombatRewardFamily", "WorldFamily" })
    {
        object family = families.Single(value => value.GetType().Name == familyName);
        Type type = family.GetType();
        object? Field(string name) => type.GetField(name, All)!.GetValue(family);
        Check(Field("_gpuPlan") is not null, familyName + " must have a GPU plan for this regression.");
        object compact = Compact(32);
        object cpu = Field("_cpu")!;
        object replay = Get(family, "Replay");
        MethodInfo matches = replay.GetType().GetMethods(All).Single(method => method.Name == "Matches" &&
            method.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(ulong));
        Delegate predicate = matches.CreateDelegate(typeof(Func<ulong, bool>), replay);
        object reference = Invoke(cpu.GetType().GetMethod("Execute", All)!, cpu, window, compact,
            CancellationToken.None, predicate)!;
        object valueTask = Invoke(type.GetMethod("InvokeAsync", All)!, family,
            executionContext, window, compact, CancellationToken.None)!;
        Task task = (Task)Invoke(valueTask.GetType().GetMethod("AsTask")!, valueTask)!;
        await task;
        object result = Get(task, "Result");
        Check(Ordinals(reference).SequenceEqual(Ordinals(result)), familyName + " offload must match its CPU reference.");
        Check(Field("_gpu") is null && !(bool)Field("_parity")!, familyName + " CPU offload must leave GPU parity unverified.");
        Check((int)Field("_cpuOffloadBatches")! == 1 && (int)Field("_batches")! == 0,
            familyName + " CPU and GPU counters must stay separate.");
        passed.Add(familyName + "CpuResultsAndGpuParityOwnership");
    }
    }
    report["Success"] = true;
}
catch (Exception ex)
{
    report["Success"] = false;
    report["Failure"] = ex.ToString();
    Environment.ExitCode = 1;
}
report["Checks"] = checks;
report["Cases"] = passed;
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));
