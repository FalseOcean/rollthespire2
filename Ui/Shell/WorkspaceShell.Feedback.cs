using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Diagnostics;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private const string FeedbackEmail = "rollthespire2@outlook.com";
    private readonly Button _feedback = MakeButton();
    private Control _feedbackPage = null!;
    private Button _feedbackExport = null!, _feedbackFolder = null!, _feedbackCopy = null!;
    private Button _feedbackIssue = null!, _feedbackCopyIssue = null!;
    private Label _feedbackStatus = null!, _feedbackState = null!, _feedbackFileName = null!, _feedbackWarnings = null!, _feedbackReceipt = null!;
    private Control _feedbackArtifact = null!, _feedbackNotice = null!, _feedbackDetails = null!;
    private Button _feedbackDetailsToggle = null!;
    private LineEdit _feedbackPath = null!;
    private readonly List<(Label Label, string Key)> _feedbackLabels = [];
    private readonly List<(Button Button, string Key)> _feedbackButtons = [];
    private Task<FeedbackBundleResult>? _feedbackTask;
    private FeedbackBundleResult? _feedbackResult;
    private string _feedbackMessage = "", _feedbackError = "";
    private string _feedbackAfterExport = "";

    private void StartFeedbackExport()
    {
        if (_feedbackTask is not null) return;
        _feedbackMessage = _feedbackError = "";
        try
        {
            FeedbackBundleRequest request = CaptureFeedbackRequest();
            _feedbackResult = null;
            _feedbackTask = Task.Run(() => FeedbackBundle.Export(request));
        }
        catch (Exception ex) { _feedbackAfterExport = ""; RecordFeedbackFailure(ex); }
        RefreshFeedback();
    }

    // Capture game metadata/UI on the main thread; never compile, predict or touch RunState.
    private FeedbackBundleRequest CaptureFeedbackRequest()
    {
        var warnings = new List<string>();
        var environment = new Dictionary<string, object?> { ["CapturedUtc"] = DateTime.UtcNow,
            ["Rt2Build"] = typeof(WorkspaceShell).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            ["Rt2LoadedAssemblySha256"] = _runtime.AssemblyEvidence.IsExact ? _runtime.AssemblyEvidence.Sha256 : null,
            ["Rt2HashIssue"] = _runtime.AssemblyEvidence.FailureReason,
            ["GameVersion"] = _runtime.Detection.DisplayVersion, ["Profile"] = _runtime.Profile.ProfileId.ToString(),
            ["OS"] = RuntimeInformation.OSDescription, ["Architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["DotNet"] = RuntimeInformation.FrameworkDescription, ["SearchRunning"] = _references?.HasActiveSearch == true };
        void Capture(string name, Func<object?> capture)
        {
            try { environment[name] = capture(); }
            catch (Exception ex) { warnings.Add(name + ": " + ex.GetType().Name); }
        }
        Capture("Godot", () => Engine.GetVersionInfo()["string"].AsString());
        Capture("RenderingDriver", () => RenderingServer.GetCurrentRenderingDriverName());
        Capture("GPU", () => RenderingServer.GetVideoAdapterName());
        Capture("GpuInitialization", () => Search.FamilyExecution.FamilyDeviceProfileFoundation.Initialization.Capture());
        Capture("SearchDiagnostics", () => _references?.CaptureSearchDiagnostics());
        Capture("Mods", () => MegaCrit.Sts2.Core.Modding.ModManager.Mods.Select(m => new {
            Id = m.manifest?.id, Name = m.manifest?.name, Version = m.manifest?.version,
            State = m.state.ToString(), Source = m.modSource.ToString() }).ToArray());
        string? editor = null;
        try { editor = JsonSerializer.Serialize(_references?.CaptureFeedbackDraft()); }
        catch (Exception ex) { warnings.Add("CurrentEditor: " + ex.GetType().Name); }
        string godotLog = "";
        try
        {
            godotLog = ProjectSettings.GlobalizePath(ProjectSettings.GetSettingWithOverride("debug/file_logging/log_path").AsString());
            bool fileLogging = ProjectSettings.GetSettingWithOverride("debug/file_logging/enable_file_logging").AsBool();
            environment["GodotFileLoggingEnabled"] = fileLogging;
            if (!fileLogging)
                warnings.Add("Godot file logging is disabled; files may be from an earlier run.");
        }
        catch (Exception ex) { warnings.Add("GodotLogPath: " + ex.GetType().Name); }
        OperationalFileLog.Flush();
        if (RuntimeLog.LastFailureCode.Length > 0) warnings.Add(RuntimeLog.LastFailureCode);
        string userData = OS.GetUserDataDir();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        paths[System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)] = "<USER_HOME>";
        paths[userData] = "<GAME_USER_DATA>";
        return new(Path.Combine(userData, "RolltheSpire2", "feedback"), RuntimeLog.LogDirectory,
            RuntimeLog.CurrentLogPath, godotLog, JsonSerializer.Serialize(environment, new JsonSerializerOptions { WriteIndented = true }),
            editor, paths, warnings);
    }

    // Polling keeps every Godot operation on the main thread, including after a tab switch.
    private void PollFeedbackExport()
    {
        if (_feedbackTask is not { IsCompleted: true } task) return;
        _feedbackTask = null;
        string action = _feedbackAfterExport; _feedbackAfterExport = "";
        try
        {
            _feedbackResult = task.GetAwaiter().GetResult(); OpenFeedbackFolder();
            if (action.Length > 0) RequestFeedbackIssue(action);
        }
        catch (Exception ex) { RecordFeedbackFailure(ex); }
        RefreshFeedback();
    }

    private void RecordFeedbackFailure(Exception ex)
    {
        _feedbackMessage = "feedback.failed"; _feedbackError = ex.GetType().Name;
        RuntimeLog.Warn("feedbackExportFailed=" + ex.GetType().Name);
    }
    private void OpenFeedbackFolder()
    {
        if (_feedbackResult is null) return;
        string directory = Path.GetDirectoryName(_feedbackResult.Path)!;
        OpenFeedbackUrl(new Uri(Path.GetFullPath(directory) + Path.DirectorySeparatorChar).AbsoluteUri);
    }
    private void OpenFeedbackUrl(string url)
    {
        _feedbackMessage = TryOpenFeedbackUrl(url) ? "" : "feedback.open_failed";
        RefreshFeedback();
    }

    private static bool TryOpenFeedbackUrl(string url)
    {
        try { return OS.ShellOpen(url) == Error.Ok; }
        catch { return false; }
    }

    private void RequestFeedbackIssue(string action)
    {
        if (_feedbackTask is not null) return;
        if (_feedbackResult is not { } result)
        {
            _feedbackAfterExport = action;
            StartFeedbackExport();
            return;
        }
        // The result owns both languages from the exported snapshot. Never recapture the editor here.
        FeedbackIssueText issue = _languageCode == "zh" ? result.ChineseIssue : result.EnglishIssue;
        _feedbackError = "";
        if (action == "copy")
        {
            DisplayServer.ClipboardSet(issue.Body);
            _feedbackMessage = "feedback.issue_copied"; RefreshFeedback();
            return;
        }
        string? url = FeedbackIssueTemplate.PrefillUrl(issue);
        if (url is not null && TryOpenFeedbackUrl(url))
            _feedbackMessage = "feedback.issue_opened";
        else
        {
            DisplayServer.ClipboardSet(issue.Body);
            // Also covers launchers that reject a URL below our bound. Never truncate the report.
            url = FeedbackIssueTemplate.NewIssueUrl + "?title=" + Uri.EscapeDataString(issue.Title);
            _feedbackMessage = TryOpenFeedbackUrl(url) ? "feedback.issue_paste" : "feedback.issue_open_failed";
        }
        RefreshFeedback();
    }
}
