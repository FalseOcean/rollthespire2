using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;

namespace RolltheSpire2.Bootstrap;

/// <summary>
/// Shared immutable Runtime Authority & Environment Identity snapshot. Capture occurs only
/// on the bound Godot main thread. All consumers read copied RT2-owned data.
/// </summary>
internal static class RuntimeAuthorityEnvironment
{
    private static RuntimeAuthorityEnvironmentSnapshot _current =
        RuntimeAuthorityEnvironmentSnapshot.Unavailable(string.Empty, "RuntimeAuthorityNotCaptured");

    private static string _interpretationKey = string.Empty;

    public static RuntimeAuthorityEnvironmentSnapshot Current => Volatile.Read(ref _current);

    public static RuntimeAuthorityEnvironmentSnapshot CaptureOnMainThread(ModRuntimeSnapshot runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        RuntimeSnapshotThreadGuard.RequireMainThread();

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        RuntimeAuthoritySnapshot authority = RuntimeAuthoritySnapshotCapture.Capture(runtime.Detection.DisplayVersion);
        long captured = System.Diagnostics.Stopwatch.GetTimestamp();
        return PublishVerifiedObservation(runtime, authority, captureMilliseconds:
            System.Diagnostics.Stopwatch.GetElapsedTime(started, captured).TotalMilliseconds);
    }

    // The caller owns freshness: menu candidates reach this boundary only after
    // a new full same-frame native observation compares equal to their frozen inputs.
    // Formal search capture continues to use CaptureOnMainThread's independent read.
    public static RuntimeAuthorityEnvironmentSnapshot PublishVerifiedObservation(
        ModRuntimeSnapshot runtime, RuntimeAuthoritySnapshot authority,
        RuntimeAuthorityInterpretation? preparedInterpretation = null, double captureMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(authority);
        RuntimeSnapshotThreadGuard.RequireMainThread();
        long captured = System.Diagnostics.Stopwatch.GetTimestamp();
        // A fresh native observation is mandatory: dynamic Mod definitions and
        // account progress have no reliable common mutation revision. Reuse only
        // the pure interpretation/log projection after comparing all of its inputs.
        string interpretationKey = JsonSerializer.Serialize(new {
            authority.GameVersion, authority.FingerprintSchemaVersion, authority.Fingerprint,
            authority.CurrentUnlockState, authority.CaptureIssues });
        bool alreadyLogged = authority.Fingerprint.Complete && _interpretationKey == interpretationKey;
        bool reusedInterpretation = preparedInterpretation is not null || alreadyLogged;
        RuntimeAuthorityInterpretation interpretation = preparedInterpretation ?? (reusedInterpretation
            ? Current.Interpretation : RuntimeAuthorityInterpreter.Interpret(authority));
        _interpretationKey = authority.Fingerprint.Complete ? interpretationKey : string.Empty;
        long interpreted = System.Diagnostics.Stopwatch.GetTimestamp();
        string candidatePath = RuntimeAuthorityBaselineCandidateWriter.TryWrite(authority);
        long exported = System.Diagnostics.Stopwatch.GetTimestamp();
        var environment = new RuntimeAuthorityEnvironmentSnapshot(authority, interpretation, candidatePath);
        Volatile.Write(ref _current, environment);
        if (alreadyLogged)
            RuntimeLog.Info($"runtimeAuthorityObservation=true;interpretationReused=true;overall={authority.Fingerprint.OverallSemanticHash};freshNativeCapture=true");
        else Log(environment, runtime);
        RuntimeLog.Detail(FormattableString.Invariant($"runtimeAuthorityTiming=true;captureMs={captureMilliseconds:F3};interpretMs={System.Diagnostics.Stopwatch.GetElapsedTime(captured, interpreted).TotalMilliseconds:F3};exportMs={System.Diagnostics.Stopwatch.GetElapsedTime(interpreted, exported).TotalMilliseconds:F3}"));
        return environment;
    }

    private static void Log(RuntimeAuthorityEnvironmentSnapshot environment, ModRuntimeSnapshot runtime)
    {
        RuntimeAuthoritySnapshot authority = environment.Authority;
        RuntimeAuthorityInterpretation interpretation = environment.Interpretation;
        RuntimeLog.Info(
            $"runtimeAuthoritySnapshot=true;gameVersion={authority.GameVersion};" +
            $"schema={authority.FingerprintSchemaVersion};complete={authority.Fingerprint.Complete.ToString().ToLowerInvariant()};" +
            $"overall={DisplayHash(authority.Fingerprint.OverallSemanticHash)};" +
            $"environmentStatus={interpretation.EnvironmentStatus};matchedBaseline={interpretation.MatchedBaselineGameVersion};" +
            $"environmentReason={interpretation.EnvironmentReason};vanillaUnlock={interpretation.VanillaUnlockStatus};" +
            $"compatibilityProfile={runtime.Profile.ProfileId};compatibilityFallback={runtime.IsCompatibilityFallback.ToString().ToLowerInvariant()}");

        RuntimeLog.Info("runtimeAuthorityDomains=true;domainsJson=" + RuntimeLog.SafeJson(authority.SemanticUniverse.Select(domain => new { domain.Domain, domain.Count, complete=domain.AuthorityComplete, hash=DisplayHash(authority.Fingerprint.GetDomainHash(domain.Domain)), evidence=Compact(domain.EvidenceCode) })));
        RuntimeLog.Info("runtimeAuthorityUnlockDomains=true;domainsJson=" + RuntimeLog.SafeJson(authority.UnlockAuthority.Select(domain => {
            var current=authority.FindCurrentUnlockDomain(domain.Domain);
            var vanilla=interpretation.VanillaUnlockCoverage.FirstOrDefault(item=>string.Equals(item.Domain,domain.Domain,StringComparison.Ordinal));
            return new { domain.Domain, runtimeUniverse=domain.Count,currentUnlocked=current?.Count,runtimeUniverseComplete=domain.AuthorityComplete,currentComplete=current?.StateComplete,vanillaUnlocked=vanilla?.Unlocked,vanillaUniverse=vanilla?.VanillaUniverse,authorityEvidence=Compact(domain.EvidenceCode),currentEvidence=Compact(current?.EvidenceCode ?? "CurrentUnlockStateMissing") };
        })));
        foreach (RuntimeSemanticDomainSnapshot domain in authority.SemanticUniverse)
        {
            RuntimeLog.Detail(
                $"runtimeAuthorityDomain={domain.Domain};count={domain.Count};complete={domain.AuthorityComplete.ToString().ToLowerInvariant()};" +
                $"sha256={DisplayHash(authority.Fingerprint.GetDomainHash(domain.Domain))};evidence={Compact(domain.EvidenceCode)}");
        }
        RuntimeLog.Info(
            $"runtimeAuthorityUnlockUniverse=true;sha256={DisplayHash(authority.Fingerprint.UnlockUniverseHash)};" +
            $"domainCount={authority.UnlockAuthority.Count}");
        foreach (RuntimeUnlockAuthorityDomainSnapshot domain in authority.UnlockAuthority)
        {
            RuntimeCurrentUnlockDomainSnapshot? current = authority.FindCurrentUnlockDomain(domain.Domain);
            VanillaUnlockDomainCoverage? vanilla = interpretation.VanillaUnlockCoverage.FirstOrDefault(item =>
                string.Equals(item.Domain, domain.Domain, StringComparison.Ordinal));
            RuntimeLog.Detail(
                $"runtimeAuthorityUnlockDomain={domain.Domain};runtimeUniverse={domain.Count};" +
                $"currentUnlocked={(current?.Count.ToString() ?? "unknown")};runtimeUniverseComplete={domain.AuthorityComplete.ToString().ToLowerInvariant()};" +
                $"currentComplete={(current?.StateComplete.ToString().ToLowerInvariant() ?? "false")};" +
                $"vanillaUnlocked={(vanilla?.Unlocked.ToString() ?? "unknown")};vanillaUniverse={(vanilla?.VanillaUniverse.ToString() ?? "unknown")};" +
                $"authorityEvidence={Compact(domain.EvidenceCode)};currentEvidence={Compact(current?.EvidenceCode ?? "CurrentUnlockStateMissing")}");
        }
        if (authority.CaptureIssues.Count > 0)
            RuntimeLog.Warn("runtimeAuthorityIssues=" + string.Join("|", authority.CaptureIssues.Select(Compact)));
        if (!string.IsNullOrWhiteSpace(environment.BaselineCandidatePath))
            RuntimeLog.Info("runtimeAuthorityBaselineCandidatePath=" + environment.BaselineCandidatePath);
    }

    private static string DisplayHash(string value) => string.IsNullOrWhiteSpace(value) ? "unavailable" : value;
    private static string Compact(string value)
    {
        string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();
        return normalized.Length <= 220 ? normalized : normalized[..220];
    }
}

internal static class RuntimeAuthorityBaselineCandidateWriter
{
    // Capture still reads current facts on every entry. Only this auxiliary
    // export is reused; its timestamp remains the time those bytes were written.
    // One successful export is retained, never a failed write or native objects.
    private static BaselineCandidateExport? _lastExport;
    private static string? _lastPath;
    private static long _lastLength;
    private static DateTime _lastWriteTimeUtc;

    public static string TryWrite(RuntimeAuthoritySnapshot snapshot)
    {
        try
        {
            string directory = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "authority");
            string path = Path.Combine(directory, "runtime_authority_baseline_candidate.json");
            if (path == _lastPath && _lastExport?.Matches(snapshot) == true)
            {
                var existing = new FileInfo(path);
                if (existing.Exists && existing.Length == _lastLength && existing.LastWriteTimeUtc == _lastWriteTimeUtc)
                    return path;
            }
            Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            var export = new BaselineCandidateExport
            {
                GameVersion = snapshot.GameVersion,
                FingerprintSchemaVersion = snapshot.FingerprintSchemaVersion,
                DomainHashes = snapshot.Fingerprint.DomainHashes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                UnlockUniverseHash = snapshot.Fingerprint.UnlockUniverseHash,
                OverallSemanticHash = snapshot.Fingerprint.OverallSemanticHash,
                FingerprintComplete = snapshot.Fingerprint.Complete,
                RuntimeUnlockUniverse = snapshot.UnlockAuthority.ToDictionary(
                    item => item.Domain,
                    item => item.UnlockUniverse.ToArray(),
                    StringComparer.Ordinal),
                CapturedAtUtc = snapshot.CapturedAtUtc,
                CaptureIssues = snapshot.CaptureIssues.ToArray()
            };
            string json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            var written = new FileInfo(path);
            _lastExport = export;
            _lastPath = path;
            _lastLength = written.Length;
            _lastWriteTimeUtc = written.LastWriteTimeUtc;
            return path;
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn($"runtimeAuthorityBaselineCandidateWriteFailed=true;issue={ex.GetType().Name}:{ex.Message}");
            return string.Empty;
        }
    }

    private sealed class BaselineCandidateExport
    {
        public string GameVersion { get; set; } = string.Empty;
        public int FingerprintSchemaVersion { get; set; }
        public Dictionary<string, string> DomainHashes { get; set; } = new(StringComparer.Ordinal);
        public string UnlockUniverseHash { get; set; } = string.Empty;
        public string OverallSemanticHash { get; set; } = string.Empty;
        public bool FingerprintComplete { get; set; }
        public Dictionary<string, string[]> RuntimeUnlockUniverse { get; set; } = new(StringComparer.Ordinal);
        public DateTimeOffset CapturedAtUtc { get; set; }
        public string[] CaptureIssues { get; set; } = Array.Empty<string>();

        internal bool Matches(RuntimeAuthoritySnapshot snapshot) =>
            GameVersion == snapshot.GameVersion && FingerprintSchemaVersion == snapshot.FingerprintSchemaVersion &&
            UnlockUniverseHash == snapshot.Fingerprint.UnlockUniverseHash && OverallSemanticHash == snapshot.Fingerprint.OverallSemanticHash &&
            FingerprintComplete == snapshot.Fingerprint.Complete && DomainHashes.Count == snapshot.Fingerprint.DomainHashes.Count &&
            DomainHashes.All(pair => snapshot.Fingerprint.DomainHashes.TryGetValue(pair.Key, out var hash) && hash == pair.Value) &&
            RuntimeUnlockUniverse.Count == snapshot.UnlockAuthority.Count && snapshot.UnlockAuthority.All(domain =>
                RuntimeUnlockUniverse.TryGetValue(domain.Domain, out var universe) && universe.SequenceEqual(domain.UnlockUniverse)) &&
            CaptureIssues.SequenceEqual(snapshot.CaptureIssues);
    }
}
