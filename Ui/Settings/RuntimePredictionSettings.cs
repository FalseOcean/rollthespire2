using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Ui.Settings;

/// <summary>
/// Live UI settings; mode/worker preferences are restored by SearchWorkspacePersistence.
/// No session/request/result state. Each search captures current values.
/// </summary>
internal sealed class RuntimePredictionSettings
{
    private int _searchWorkerCount = 4;

    public bool EnableComplexBonesDeckInteractions { get; set; }

    /// <summary>
    /// Main-menu lifetime Search backend preference. This does not change the
    /// immutable Search draft or any filter semantics.
    /// </summary>
    public Beta110RelicComputeBackendPreference RelicComputeBackendPreference { get; set; } =
        Beta110RelicComputeBackendPreference.Auto;

    /// <summary>
    /// Workshop Release UI exposes only Auto / CPU Only. Any historical or
    /// developer-only persisted preference must collapse to Auto before a player
    /// Search starts so the visible selection and execution preference cannot diverge.
    /// </summary>
    public Beta110RelicComputeBackendPreference NormalizeWorkshopSearchModePreference()
    {
        if (RelicComputeBackendPreference is not Beta110RelicComputeBackendPreference.Auto and
            not Beta110RelicComputeBackendPreference.Cpu)
        {
            RelicComputeBackendPreference = Beta110RelicComputeBackendPreference.Auto;
        }

        return RelicComputeBackendPreference;
    }

    public bool EnableDiagnosticLogging { get; set; }

    public RuntimePredictionDiagnosticLevel DiagnosticLogLevel { get; set; } =
        RuntimePredictionDiagnosticLevel.Info;

    public int SearchWorkerCount
    {
        get => _searchWorkerCount;
        set => _searchWorkerCount = Math.Clamp(value, 1, 64);
    }
}
