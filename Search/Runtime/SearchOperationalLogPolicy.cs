namespace RolltheSpire2.Search.Runtime;

/// <summary>
/// Search logging policy. Production summary is always emitted. Historical planner
/// comparisons and high-cardinality decomposition are opt-in observers and must never
/// create Production facts or affect plan selection/runtime execution.
/// </summary>
internal static class SearchOperationalLogPolicy
{
    private static readonly bool EnvironmentDeveloperDiagnosticsEnabled = Enabled("RT2_SEARCH_DEVELOPER_DIAGNOSTICS");
    private static int _runtimeDeveloperDiagnosticsEnabled;

    public static bool DeveloperDiagnosticsEnabled =>
        EnvironmentDeveloperDiagnosticsEnabled ||
        System.Threading.Volatile.Read(ref _runtimeDeveloperDiagnosticsEnabled) != 0;

    public static bool RuntimeDeveloperDiagnosticsEnabled =>
        System.Threading.Volatile.Read(ref _runtimeDeveloperDiagnosticsEnabled) != 0;

    public static bool VerboseTraceEnabled { get; } = Enabled("RT2_SEARCH_VERBOSE_TRACE");

    public static void SetRuntimeDeveloperDiagnosticsEnabled(bool enabled) =>
        System.Threading.Volatile.Write(ref _runtimeDeveloperDiagnosticsEnabled, enabled ? 1 : 0);

    private static bool Enabled(string name) =>
        string.Equals(System.Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);
}
