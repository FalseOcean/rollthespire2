using System.Text.Json;
using System.Text.Json.Serialization;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>Portable inputs only. Never stores compiled plans or live game objects.</summary>
public sealed record ProductionSearchReplayContext(int SchemaVersion, string Provenance, SearchContext Context);
public sealed record ProductionSearchReplayWorkload(int SchemaVersion, SearchQuery Query, SearchRunOptions Options);

public static class ProductionSearchReplay
{
    public const int SchemaVersion = 1;
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();
    private static JsonSerializerOptions CompactJsonOptions { get; } = new(JsonOptions) { WriteIndented = false };

    internal static string SerializeWorkload(ExactSearchExecutionRequest request) => JsonSerializer.Serialize(
        new ProductionSearchReplayWorkload(SchemaVersion, request.CompiledSearch.Query,
            request.RunOptions with { StartSeed = request.CanonicalStartSeed, ScanCount = request.ScanCount }), CompactJsonOptions);

    // One complete typed Query per attempted session, including automatic cursor segments.
    // Existing five-item UI history is not an audit archive. No live game objects are logged.
    internal static void LogSessionWorkload(ExactSearchExecutionRequest request, string sessionId)
    {
        try
        {
            string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(request.SnapshotFingerprint)));
            Bootstrap.RuntimeLog.Info($"productionSearchWorkload=true;phase=CompiledBeforeSessionStart;sessionId={sessionId};" +
                $"game={request.Detection.DisplayVersion};character={request.CharacterKey.Serialized};ascension={request.Ascension};profile={request.ProfileId};" +
                $"snapshotFingerprintSha256={fingerprint};workloadJson={SerializeWorkload(request)}");
        }
        catch (Exception ex) { Bootstrap.RuntimeLog.Warn($"productionSearchWorkloadFailed=true;sessionId={sessionId};issue={ex.GetType().Name}:{ex.Message};searchContinues=true"); }
    }

    public static void InitializeHostOnMainThread()
    {
        Bootstrap.RuntimeLog.InitializeOnMainThread(hostConsole: true);
        Search.FamilyExecution.FamilyDeviceProfileFoundation.CaptureAvailabilityOnMainThread();
        var loaded = Bootstrap.AssemblyEvidence.Capture();
        Bootstrap.RuntimeLog.Info($"runtimeBinary=true;rewriteAssemblyPath={loaded.AssemblyPath};rewriteAssemblySha256={loaded.Sha256};build={typeof(ProductionSearchReplay).Assembly.GetName().Version}");
        Bootstrap.RuntimeLog.Info("standaloneProductionHostInitialized=true");
        Search.Runtime.SearchPerformanceProfileFoundation.ObserveGpuIdentity(
            Godot.RenderingServer.GetVideoAdapterName(), Godot.RenderingServer.GetCurrentRenderingDriverName());
        Search.FamilyExecution.GpuCostCalibration.InitializeOnMainThread();
        Search.FamilyExecution.CpuCostCalibration.InitializeOnMainThread();
        Search.Predictability.SearchPredictabilityVerificationStore.InitializeOnMainThread();
    }

    public static void FlushHostOnMainThread()
    {
        Bootstrap.RuntimeLog.PumpOnMainThread();
        Search.FamilyExecution.GpuCostCalibration.TryFlushPendingOnMainThread();
        Search.FamilyExecution.CpuCostCalibration.TryFlushPendingOnMainThread();
        Search.Predictability.SearchPredictabilityVerificationStore.TryFlushPendingOnMainThread();
        Bootstrap.OperationalFileLog.Flush();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ModelKeyConverter());
        return options;
    }

    public static void Write<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        // Refuse an export which cannot be losslessly reconstructed by this build.
        T copy = JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new JsonException("ReplayNull");
        if (JsonSerializer.Serialize(copy, JsonOptions) != json)
            throw new JsonException("ReplayRoundTripMismatch:" + typeof(T).Name);
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new JsonException("ReplayNull:" + path);

    /// <summary>Opt-in capture at the real UI compiler boundary; failure never alters Search.</summary>
    internal static void ExportIfRequested(SearchContext context, SearchQuery query)
    {
        string? directory = Environment.GetEnvironmentVariable("RT2_PRODUCTION_REPLAY_EXPORT_DIR");
        string request = Path.Combine(Godot.OS.GetUserDataDir(), "RolltheSpire2", "production-replay-export.request");
        try
        {
            bool oneShot = string.IsNullOrWhiteSpace(directory) && File.Exists(request);
            if (oneShot) directory = File.ReadAllText(request).Trim();
            if (string.IsNullOrWhiteSpace(directory)) return;
            Write(Path.Combine(directory, "context.json"), new ProductionSearchReplayContext(SchemaVersion,
                "GameRuntimeCapture", context));
            Write(Path.Combine(directory, "workload.json"), new ProductionSearchReplayWorkload(SchemaVersion,
                query, new SearchRunOptions("000000000000", 1 << 24, int.MaxValue, 4)));
            ExportRuntimeContexts(directory, context);
            Bootstrap.RuntimeLog.Info("productionReplayExport=true;directory=" + Path.GetFullPath(directory));
            if (oneShot) File.Delete(request);
        }
        catch (Exception ex) { Bootstrap.RuntimeLog.Warn("productionReplayExportFailed=" + ex.Message); }
    }

    private static void ExportRuntimeContexts(string directory, SearchContext source)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var catalog = RuntimeCharacterCatalogCapture.Capture();
        Write(Path.Combine(directory, "character-catalog.json"), catalog);
        var runtimeAuthority = RuntimeAuthority.RuntimeAuthoritySnapshotCapture.Capture(source.Detection.DisplayVersion);
        Write(Path.Combine(directory, "runtime-authority.json"), runtimeAuthority);
        Bootstrap.RuntimeLog.Info($"productionReplayEnvironment=true;fingerprint={runtimeAuthority.Fingerprint.OverallSemanticHash};complete={runtimeAuthority.Fingerprint.Complete};issues={string.Join('|', runtimeAuthority.CaptureIssues)}");
        // EffectiveCharacters deliberately has a presentation fallback. It is not
        // authority for enumerating a complete portable runtime capture.
        if (!catalog.IdentityCaptureComplete)
            throw new InvalidOperationException("ReplayCharacterCatalogIncomplete:" + catalog.EvidenceCode);
        var profile = RuntimeProfileRegistry.Select(source.Detection);
        if (profile.ProfileId != source.ProfileId)
            throw new InvalidOperationException("ReplayCaptureProfileMismatch");
        string probeSeed = ProfileSeedGenerator.CreateProbeSeed(profile);
        var entries = new List<ReplayContextCaptureEntry>();
        int characterIndex = -1;
        foreach (ModelKey key in catalog.CharactersInSourceOrder)
        {
            characterIndex++;
            for (int ascension = SeedPredictionInputLimits.MinimumAscension;
                 ascension <= SeedPredictionInputLimits.MaximumAscension; ascension++)
            {
                string relative = $"contexts/character-{characterIndex:D2}-a{ascension:D2}.json";
                try
                {
                    var authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
                        profile, probeSeed, CharacterIdentity.FromKey(key), ascension, source.Detection.DisplayVersion,
                        playersCount: 1, playerSlotIndex: 0, predictionGameMode: WorldGameMode.Singleplayer,
                        predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
                    var context = source with { CharacterKey = key, Ascension = ascension, Authority = authority };
                    Write(Path.Combine(directory, relative), new ProductionSearchReplayContext(SchemaVersion,
                        "GameRuntimeCapture", context));
                    var issues = new List<string>();
                    if (!SourceAuthorityRules.SupportsExactIdentity(authority.SourceAuthority) ||
                        authority.Completeness != SnapshotCompleteness.Complete ||
                        authority.ResolutionStatus != IdentityResolutionStatus.Exact)
                        issues.Add($"CharacterUnlock:{authority.SourceAuthority}/{authority.Completeness}/{authority.ResolutionStatus}");
                    if (authority.EffectAuthority is not { Completeness: SnapshotCompleteness.Complete } ||
                        !SourceAuthorityRules.SupportsExactIdentity(authority.EffectAuthority.SourceAuthority))
                        issues.Add("EffectAuthority:" + (authority.EffectAuthority?.CaptureDiagnosticCode ?? "Missing"));
                    if (authority.WorldAuthority is not { Completeness: SnapshotCompleteness.Complete } ||
                        !SourceAuthorityRules.SupportsExactIdentity(authority.WorldAuthority.SourceAuthority))
                        issues.Add("WorldAuthority:" + (authority.WorldAuthority?.CaptureDiagnosticCode ?? "Missing"));
                    entries.Add(new(key, ascension, relative, issues.ToArray()));
                    Bootstrap.RuntimeLog.Info($"productionReplayContextCaptured=true;character={key.Serialized};ascension={ascension};dependencies={string.Join('|', issues)}");
                }
                catch (Exception ex)
                {
                    entries.Add(new(key, ascension, null, [ex.GetType().Name + ":" + ex.Message]));
                    Bootstrap.RuntimeLog.Warn($"productionReplayContextFailed=true;character={key.Serialized};ascension={ascension};issue={ex.Message}");
                }
            }
        }
        Write(Path.Combine(directory, "contexts-manifest.json"), entries);
        Bootstrap.RuntimeLog.Info($"productionReplayContextsComplete=true;expected={entries.Count};written={entries.Count(e => e.Path is not null)};withDependencies={entries.Count(e => e.Dependencies.Length != 0)};probeSeed={probeSeed};queryAuthorityValidation=ProductionCompiler");
    }

    private sealed record ReplayContextCaptureEntry(ModelKey Character, int Ascension, string? Path, string[] Dependencies);

    private sealed class ModelKeyConverter : JsonConverter<ModelKey>
    {
        public override ModelKey Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            string text = reader.GetString() ?? throw new JsonException("NullModelKey");
            if (text == ":") return default;
            return ModelKey.TryParseExact(text, out var key) ? key : throw new JsonException("InvalidModelKey:" + text);
        }
        public override void Write(Utf8JsonWriter writer, ModelKey value, JsonSerializerOptions options) => writer.WriteStringValue(value.Serialized);
        public override ModelKey ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => Read(ref reader, type, options);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, ModelKey value, JsonSerializerOptions options) => writer.WritePropertyName(value.Serialized);
    }
}
