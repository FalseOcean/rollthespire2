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

    public static RuntimeAuthorityEnvironmentSnapshot Current => Volatile.Read(ref _current);

    public static RuntimeAuthorityEnvironmentSnapshot CaptureOnMainThread(ModRuntimeSnapshot runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        RuntimeSnapshotThreadGuard.RequireMainThread();

        RuntimeAuthoritySnapshot authority = RuntimeAuthoritySnapshotCapture.Capture(runtime.Detection.DisplayVersion);
        RuntimeAuthorityInterpretation interpretation = RuntimeAuthorityInterpreter.Interpret(authority);
        string candidatePath = RuntimeAuthorityBaselineCandidateWriter.TryWrite(authority);
        var environment = new RuntimeAuthorityEnvironmentSnapshot(authority, interpretation, candidatePath);
        Volatile.Write(ref _current, environment);
        Log(environment, runtime);
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
    public static string TryWrite(RuntimeAuthoritySnapshot snapshot)
    {
        try
        {
            string directory = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "authority");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "runtime_authority_baseline_candidate.json");
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
    }
}
