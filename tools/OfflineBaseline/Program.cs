using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// 載入指定正式組件，不初始化遊戲、Godot、GPU 或玩家目錄。
var cli = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i < args.Length; i += 2)
{
    if (!args[i].StartsWith("--") || i + 1 == args.Length) throw new ArgumentException("Arguments must be --name value pairs.");
    cli.Add(args[i][2..], args[i + 1]);
}
string Required(string name) => cli.TryGetValue(name, out string? value) ? Path.GetFullPath(value) : throw new ArgumentException("Missing --" + name);
string assemblyPath = Required("assembly"), outputPath = Required("output");
string? referenceDir = cli.TryGetValue("reference-dir", out var rd) ? Path.GetFullPath(rd) : null;
int count = cli.TryGetValue("count", out var countText) ? int.Parse(countText) : 64;
if (count is < 1 or > 100000) throw new ArgumentOutOfRangeException("count", "Use a bounded count of 1..100000.");
var report = new Dictionary<string, object?>
{
    ["SchemaVersion"] = 1,
    ["AssemblyPath"] = assemblyPath,
    ["AssemblySha256"] = Hash(File.ReadAllBytes(assemblyPath)),
    ["Runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    ["CreatedUtc"] = DateTimeOffset.UtcNow,
    ["Scope"] = "Offline production CPU code only; no GPU, native Godot initialization, game launch, or user-data writes."
};
var jsonOut = new JsonSerializerOptions { WriteIndented = true };
try
{
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        foreach (string directory in new[] { Path.GetDirectoryName(assemblyPath)!, referenceDir }.OfType<string>())
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
        }
        return null;
    };
    Assembly production = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    report["AssemblyInformationalVersion"] = production.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    report["AssemblyMetadata"] = production.GetCustomAttributes<AssemblyMetadataAttribute>()
        .ToDictionary(value => value.Key, value => value.Value);
    report["NeowSmallReadbackCompiledEnabled"] = production.GetType("RolltheSpire2.Search.FamilyExecution.NeowReadbackTransport")?
        .GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
    report["NeowCombinedReadbackCompiledEnabled"] = production.GetType("RolltheSpire2.Search.FamilyExecution.NeowCombinedReadbackTransport")?
        .GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
    report["NeowMediumReadbackCompiledEnabled"] = production.GetType("RolltheSpire2.Search.FamilyExecution.NeowMediumReadbackTransport")?
        .GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
    Type T(string name) => production.GetType("RolltheSpire2." + name, throwOnError: true)!;
    MethodInfo Static(string type, string method, int parameterCount) => T(type).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .Single(m => m.Name == method && m.GetParameters().Length == parameterCount);
    object Call(string type, string method, params object?[] values) => Invoke(Static(type, method, values.Length), values);
    var vectorWatch = Stopwatch.StartNew();
    object vectors = Call("Core.Verification.SeedRngVectorVerifier", "RunAll");
    vectorWatch.Stop();
    report["RngVectors"] = JsonSerializer.SerializeToElement(vectors, vectors.GetType());
    report["RngVectorsColdMilliseconds"] = vectorWatch.Elapsed.TotalMilliseconds;
    if (!(bool)Get(vectors, "Passed")) throw new InvalidOperationException("Production RNG vectors failed.");
    report["AssemblyVersion"] = production.GetName().Version?.ToString();

    if (cli.TryGetValue("workload", out var workloadArgument))
    {
        string workloadPath = Path.GetFullPath(workloadArgument);
        Type replayType = T("Infrastructure.Snapshots.ProductionSearchReplay");
        var replayOptions = (JsonSerializerOptions)replayType.GetProperty("JsonOptions")!.GetValue(null)!;
        object Read(string type, string path) => Invoke(replayType.GetMethod("Read")!.MakeGenericMethod(T(type)), [path]);
        object workload = Read("Infrastructure.Snapshots.ProductionSearchReplayWorkload", workloadPath);
        if ((int)Get(workload, "SchemaVersion") != 1) throw new InvalidOperationException("Unsupported workload schema.");
        string typedJson = JsonSerializer.Serialize(workload, workload.GetType(), replayOptions);
        object second = JsonSerializer.Deserialize(typedJson, workload.GetType(), replayOptions)!;
        bool roundTrip = JsonSerializer.Serialize(second, second.GetType(), replayOptions) == typedJson;
        if (!roundTrip) throw new InvalidOperationException("Production replay workload round-trip mismatch.");
        report["Workload"] = new { Path = workloadPath, FileSha256 = Hash(File.ReadAllBytes(workloadPath)), TypedJsonSha256 = HashText(typedJson), RoundTripPassed = roundTrip };
        object query = Get(workload, "Query"), options = Get(workload, "Options");
        object normalized = Call("Search.Semantics.SearchQueryNormalizer", "Normalize", query);
        report["QueryNormalization"] = new
        {
            Status = Get(normalized, "Status").ToString(),
            NormalizedQuerySha256 = HashText(JsonSerializer.Serialize(Get(normalized, "NormalizedQuery"), T("Search.Semantics.SearchQuery"), replayOptions))
        };

        string? contextPath = cli.TryGetValue("context", out var contextArgument) ? Path.GetFullPath(contextArgument) : null;
        string? expectedSemanticFingerprint = null;
        if (contextPath == null && cli.ContainsKey("import-runtime-log"))
        {
            string sourceLog = Required("import-runtime-log"), releasePath = Required("release-info");
            byte[] sourceLogBytes = ReadShared(sourceLog);
            string[] lines = Encoding.UTF8.GetString(sourceLogBytes).Split('\n').Select(s => s.TrimEnd('\r')).ToArray();
            string suppliedWorkload = CompactJson(File.ReadAllText(workloadPath));
            int sourceIndex = Array.FindLastIndex(lines, line => line.Contains("productionSearchWorkload=true") &&
                line.Contains(";workloadJson=") && CompactJson(line[(line.IndexOf(";workloadJson=", StringComparison.Ordinal) + 14)..]) == suppliedWorkload);
            if (sourceIndex < 0) throw new InvalidOperationException("No exact workload record in supplied runtime log.");
            int fingerprintIndex = Array.FindIndex(lines, sourceIndex + 1, line => line.Contains("workbenchProductionStart=true;terminal=ProductionExact;fingerprint="));
            int nextWorkload = Array.FindIndex(lines, sourceIndex + 1, line => line.Contains("productionSearchWorkload=true"));
            if (fingerprintIndex < 0 || nextWorkload >= 0 && fingerprintIndex > nextWorkload) throw new InvalidOperationException("Matching production semantic fingerprint missing.");
            expectedSemanticFingerprint = lines[fingerprintIndex].Split(";fingerprint=", 2)[1];
            var fields = expectedSemanticFingerprint.Split('|').Select(x => x.Split('=', 2)).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
            var root = JsonNode.Parse(File.ReadAllText(workloadPath))!;
            var authorities = root["Query"]!["EventResultConditions"]!.AsArray()
                .Select(c => c?["MorphicGroveScenario"]?["Authority"]).OfType<JsonObject>().ToArray();
            if (authorities.Length == 0) throw new InvalidOperationException("Logged workload contains no runtime authority; supply a genuine captured --context.");
            JsonObject authority = authorities[0];
            foreach (string required in new[] { "UnlockSnapshotFingerprint", "CatalogFingerprint", "EffectSnapshotFingerprint", "WorldSnapshotFingerprint" })
            {
                string fingerprintKey = required switch { "UnlockSnapshotFingerprint" => "unlock", "CatalogFingerprint" => "catalog", "EffectSnapshotFingerprint" => "effect", _ => "world" };
                if (authority[required]?.GetValue<string>() != fields[fingerprintKey]) throw new InvalidOperationException("Logged authority fingerprint mismatch: " + required);
            }
            if (authority["SourceAuthority"]?.GetValue<string>() != "OfficialRuntimeExact" || authority["Completeness"]?.GetValue<string>() != "Complete" || authority["ResolutionStatus"]?.GetValue<string>() != "Exact")
                throw new InvalidOperationException("Logged authority is not an exact, complete runtime capture.");
            if (authority["Character"]?["CharacterKey"]?.GetValue<string>() != fields["character"] || authority["Ascension"]!.GetValue<int>().ToString() != fields["ascension"] ||
                authority["GameVersion"]?.GetValue<string>() != fields["game"] || authority["ProfileId"]?.GetValue<string>() != fields["profile"])
                throw new InvalidOperationException("Logged authority identity does not match the production fingerprint.");
            object detection = Call("Compatibility.GameVersionDetector", "DetectFromCandidates", new object?[] { new[] { releasePath } });
            if (!(bool)Get(detection, "IsExact") || (string)Get(detection, "NormalizedVersion") != fields["game"])
                throw new InvalidOperationException("Local release metadata does not confirm the logged exact game version.");
            string[] assumptionBits = fields["ancientAssumptions"].Split(':');
            string[] assumptionNames = ["TezcataraHasBasicStrike", "NonupeipeSwiftEnchantableAtLeast4", "TanxInstinctEnchantableAtLeast3", "PaelGoopyDefendCardsAtLeast3", "PaelAllowLegionNoEventPet", "PaelRemovableCardsAtLeast5", "OrobasArchaicToothConditionMet", "OrobasTouchOfOrobasConditionMet", "DarvAllowPandorasBoxRelicSet"];
            if (assumptionBits.Length != assumptionNames.Length || assumptionBits.Any(b => b is not ("0" or "1"))) throw new InvalidOperationException("Logged ancient assumptions incomplete.");
            var assumptions = new JsonObject();
            for (int i = 0; i < assumptionNames.Length; i++) assumptions[assumptionNames[i]] = assumptionBits[i] == "1";
            var contextJson = new JsonObject
            {
                ["SchemaVersion"] = 1,
                ["Provenance"] = "ReconstructedFromLoggedExactAuthorityAndSemanticFingerprint;DetectionFromConfirmedLocalReleaseMetadata",
                ["Context"] = new JsonObject
                {
                    ["ProfileId"] = fields["profile"], ["CharacterKey"] = fields["character"], ["Ascension"] = int.Parse(fields["ascension"]),
                    ["Authority"] = authority.DeepClone(), ["Detection"] = JsonSerializer.SerializeToNode(detection, detection.GetType(), replayOptions),
                    ["EvaluationAssumptions"] = new JsonObject { ["AncientEligibilityAssumptions"] = assumptions }
                }
            };
            contextPath = Path.ChangeExtension(outputPath, ".context.json");
            Directory.CreateDirectory(Path.GetDirectoryName(contextPath)!);
            File.WriteAllText(contextPath, contextJson.ToJsonString(jsonOut));
            report["ContextImportEvidence"] = new
            {
                LogPath = sourceLog, LogSha256 = Hash(sourceLogBytes), WorkloadLine = sourceIndex + 1,
                SemanticFingerprintLine = fingerprintIndex + 1, AuthorityField = "Query.EventResultConditions[0].MorphicGroveScenario.Authority",
                SourceAuthority = "OfficialRuntimeExact", Completeness = "Complete", ResolutionStatus = "Exact",
                ReleaseInfoPath = releasePath, ReleaseInfoSha256 = Hash(File.ReadAllBytes(releasePath)),
                AncientAssumptions = fields["ancientAssumptions"], ExpectedSemanticFingerprintSha256 = HashText(expectedSemanticFingerprint),
                Description = "Authority copied unchanged from workload; identity, four authority fingerprints, assumptions and complete compiled semantic fingerprint must match the real production log. Detection uses production DetectFromCandidates with local release_info. No authority/catalog is synthesized."
            };
        }
        if (contextPath == null)
            report["CpuExact"] = new { Status = "SkippedMissingCapturedSearchContext", Reason = "The workload alone does not establish the full SearchContext. Supply --context or the explicitly verified --import-runtime-log and --release-info inputs." };
        else
        {
            object replayContext = Read("Infrastructure.Snapshots.ProductionSearchReplayContext", contextPath);
            object context = Get(replayContext, "Context");
            object compiled = Call("Search.Semantics.SearchCompiler", "Compile", query, context, null);
            string semanticFingerprint = (string)Get(compiled, "SemanticFingerprint");
            if (expectedSemanticFingerprint != null && expectedSemanticFingerprint != semanticFingerprint) throw new InvalidOperationException("Reconstructed context failed full semantic fingerprint equality; CPU Exact refused.");
            report["Context"] = new { Path = contextPath, FileSha256 = Hash(File.ReadAllBytes(contextPath)), Provenance = Get(replayContext, "Provenance"), SemanticFingerprintSha256 = HashText(semanticFingerprint), MatchesRuntimeFingerprint = expectedSemanticFingerprint == null ? (bool?)null : true };
            object compileResult = Call("Search.Compilation.ExactSearchExecutionRequestFactory", "Compile", compiled, options);
            if (!(bool)Get(compileResult, "Success"))
                report["CpuExact"] = new { Status = "RejectedByProductionCompiler", Disposition = Get(compileResult, "Disposition").ToString(), Issue = Get(compileResult, "Issue") };
            else
            {
                object plan = Get(compileResult, "Plan");
                object profile = Invoke(T("Compatibility.RuntimeProfileRegistry").GetMethod("Select", [T("Compatibility.GameVersionDetection")])!, [Get(context, "Detection")]);
                var parse = Static("Search.FamilyExecution.VisibleSeedCandidateCodec", "TryParseOrdinal", 5);
                object?[] parseArgs = [profile, Get(options, "StartSeed"), 0UL, null, null];
                if (!(bool)Invoke(parse, parseArgs)) throw new InvalidOperationException("Production ordinal parsing rejected start seed: " + parseArgs[4]);
                ulong start = (ulong)parseArgs[2]!;
                var format = Static("Search.FamilyExecution.VisibleSeedCandidateCodec", "FormatOrdinal", 2);
                var input = Static("Core.Prediction.TrustedRootHashInput", "FromCanonicalSeed", 2);
                var evaluate = T("Search.Runtime.ProductionExactSearchEvaluator").GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(m => m.Name == "Evaluate" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.Name == "ExactSearchExecutionRequest");
                Func<object, object, object> eval = FastCall(evaluate), bind = FastCall(input), formatSeed = FastCall(format);
                string[] seeds = cli.TryGetValue("seeds", out var seedFile) ? File.ReadAllLines(seedFile).Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith('#')).ToArray() :
                    Enumerable.Range(0, count).Select(i => (string)formatSeed(profile, checked(start + (ulong)i))).ToArray();
                if (seeds.Length is < 1 or > 100000) throw new ArgumentOutOfRangeException("seeds");
                foreach (string seed in seeds)
                {
                    object?[] validation = [profile, seed, 0UL, null, null];
                    if (!(bool)Invoke(parse, validation) || (string)validation[3]! != seed)
                        throw new InvalidOperationException("Seed list must contain valid canonical seeds: " + seed);
                }
                // 用同一計畫暖機一次，再逐一獨立驗證指定種子。
                _ = eval(plan, bind(profile, seeds[0]));
                long evaluationTicks = 0;
                var rows = new List<object>();
                foreach (string seed in seeds)
                {
                    object trusted = bind(profile, seed);
                    long before = Stopwatch.GetTimestamp();
                    object result = eval(plan, trusted);
                    evaluationTicks += Stopwatch.GetTimestamp() - before;
                    object evaluation = Get(result, "Evaluation");
                    rows.Add(new { Seed = seed, RootHash = ((ulong)Get(trusted, "RootHash")).ToString("X16"), Disposition = Get(evaluation, "Disposition").ToString(), IsMatch = (bool)Get(result, "IsMatch"), FailureCode = Get(result, "FailureCode"), EvaluationSha256 = HashText(JsonSerializer.Serialize(evaluation, evaluation.GetType(), replayOptions)) });
                }
                string rowsJson = JsonSerializer.Serialize(rows);
                double milliseconds = evaluationTicks * 1000.0 / Stopwatch.Frequency;
                report["CpuExact"] = new { Status = "Completed", Count = rows.Count, WarmupCount = 1, EvaluationMilliseconds = milliseconds, EvaluatedSeedsPerSecond = rows.Count * 1000.0 / milliseconds, ResultsSha256 = HashText(rowsJson), Results = rows,
                    TimingScope = "ProductionExactSearchEvaluator only, using compiled reflection delegates. One warmup excluded. No GPU sieve, no seed formatting/hash setup, no result serialization; not end-to-end search throughput." };
            }
        }
    }
    report["Succeeded"] = true;
}
catch (Exception error)
{
    report["Succeeded"] = false;
    report["Error"] = error.ToString();
    Environment.ExitCode = 1;
}
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(report, jsonOut));
Console.WriteLine(JsonSerializer.Serialize(new { Output = outputPath, Succeeded = report.GetValueOrDefault("Succeeded"), AssemblySha256 = report["AssemblySha256"], RngVectorsPassed = report.GetValueOrDefault("RngVectors") is JsonElement rng && rng.GetProperty("Passed").GetBoolean(), CpuExact = report.GetValueOrDefault("CpuExact") is null ? "NotRequested" : "SeeReport" }));

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
static byte[] ReadShared(string path)
{
    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var copy = new MemoryStream();
    input.CopyTo(copy);
    return copy.ToArray();
}
static string CompactJson(string text) => JsonSerializer.Serialize(JsonDocument.Parse(text).RootElement);
static object Get(object value, string property) => value.GetType().GetProperty(property)!.GetValue(value)!;
static object Invoke(MethodInfo method, object?[] values)
{
    try { return method.Invoke(null, values)!; }
    catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
}
static Func<object, object, object> FastCall(MethodInfo method)
{
    var left = Expression.Parameter(typeof(object)); var right = Expression.Parameter(typeof(object));
    ParameterInfo[] parameters = method.GetParameters();
    return Expression.Lambda<Func<object, object, object>>(Expression.Convert(Expression.Call(method,
        Expression.Convert(left, parameters[0].ParameterType), Expression.Convert(right, parameters[1].ParameterType)), typeof(object)), left, right).Compile();
}
