using System.Reflection;
using System.Text.Json;

namespace RolltheSpire2.Compatibility;

public sealed record GameVersionDetection(
    string RawVersion,
    string NormalizedVersion,
    string Branch,
    string Evidence,
    bool IsExact,
    string FailureReason)
{
    public string DisplayVersion => string.IsNullOrWhiteSpace(NormalizedVersion) ? "unknown" : NormalizedVersion;

    public static GameVersionDetection NotDetected { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        "not-initialized",
        false,
        "Version detection has not run.");
}

public static class GameVersionDetector
{
    public static GameVersionDetection Detect()
    {
        IReadOnlyList<string> candidates = BuildCandidatePaths();
        return DetectFromCandidates(candidates);
    }

    public static GameVersionDetection DetectFromCandidates(IEnumerable<string> candidatePaths)
    {
        var attempted = new List<string>();
        foreach (string candidate in candidatePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch (Exception ex)
            {
                attempted.Add($"invalid:{candidate}:{ex.GetType().Name}");
                continue;
            }

            attempted.Add(fullPath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                using FileStream stream = File.OpenRead(fullPath);
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;
                string rawVersion = ReadString(root, "version");
                string branch = ReadString(root, "branch");
                if (string.IsNullOrWhiteSpace(rawVersion))
                {
                    continue;
                }

                string normalized = NormalizeVersion(rawVersion);
                return new GameVersionDetection(
                    rawVersion.Trim(),
                    normalized,
                    branch,
                    $"release_info.json:{fullPath};branch={branch}",
                    true,
                    string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                attempted.Add($"read-failed:{fullPath}:{ex.GetType().Name}");
            }
        }

        return new GameVersionDetection(
            string.Empty,
            string.Empty,
            string.Empty,
            "release_info.json-not-found;attempted=" + string.Join("|", attempted),
            false,
            "No readable release_info.json with a version field was found. Unsupported profile selected.");
    }

    public static string NormalizeVersion(string version)
    {
        string value = (version ?? string.Empty).Trim();
        if (value.StartsWith('v'))
        {
            value = value[1..];
        }

        int suffixIndex = value.IndexOfAny(new[] { '-', '+' });
        if (suffixIndex > 0)
        {
            value = value[..suffixIndex];
        }

        return value;
    }

    private static IReadOnlyList<string> BuildCandidatePaths()
    {
        var candidates = new List<string>();
        string? explicitPath = Environment.GetEnvironmentVariable("STS2_RELEASE_INFO_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        Assembly? gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
        AddNearAssembly(candidates, gameAssembly);
        AddNearAssembly(candidates, typeof(GameVersionDetector).Assembly);
        AddNearDirectory(candidates, AppContext.BaseDirectory);
        AddNearDirectory(candidates, Environment.CurrentDirectory);
        return candidates;
    }

    private static void AddNearAssembly(ICollection<string> candidates, Assembly? assembly)
    {
        if (assembly is null || string.IsNullOrWhiteSpace(assembly.Location))
        {
            return;
        }

        AddNearDirectory(candidates, Path.GetDirectoryName(assembly.Location));
    }

    private static void AddNearDirectory(ICollection<string> candidates, string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return;
        }

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(startDirectory);
        }
        catch
        {
            return;
        }

        for (int depth = 0; depth < 7 && current is not null; depth++, current = current.Parent)
        {
            candidates.Add(Path.Combine(current.FullName, "release_info.json"));
            candidates.Add(Path.Combine(current.FullName, "SlayTheSpire2_Data", "release_info.json"));
        }
    }

    private static string ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
