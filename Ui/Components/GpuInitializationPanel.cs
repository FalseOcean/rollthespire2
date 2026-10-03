using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>Creation evidence and an explicit, bounded device probe; all UI updates run on the main thread.</summary>
internal sealed partial class GpuInitializationPanel : VBoxContainer
{
    private readonly IUiTextProvider _text;
    private readonly Func<bool> _searchBusy;
    private readonly bool _compact;
    private readonly Label _state, _detail;
    private readonly Button _retry;
    private long _revision = -1;
    private Task<string>? _displayedRetry;
    private bool _displayedCompleted;

    internal GpuInitializationPanel(string language, Func<bool> searchBusy, bool compact = false)
    {
        Name = "GpuInitializationPanel";
        _text = JsonUiTextProvider.Create(language);
        _searchBusy = searchBusy;
        _compact = compact;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
        _state = Ui1Theme.Label("", Ui1TextRole.Meta, true);
        _state.Name = "GpuInitializationState";
        _detail = Ui1Theme.Label("", Ui1TextRole.Meta, true);
        _detail.Name = "GpuInitializationDetails";
        _state.AutowrapMode = _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _retry = new Button { Name = "RetryGpuInitialization", Text = _text.Get("ui1.devices.retry"),
            CustomMinimumSize = new(0, 38), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        Ui1Theme.ApplyButton(_retry, Ui1ButtonRole.Secondary);
        _retry.Pressed += Retry;
        AddChild(_state); AddChild(_detail); AddChild(_retry);
        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    private void Retry()
    {
        if (_searchBusy() || !FamilyDeviceProfileFoundation.Initialization.Capture().CanRetry) return;
        // Re-capture identity, not compute availability. A same-environment refresh
        // must not clear a failure; only this explicit probe may try creation again.
        FamilyDeviceProfileFoundation.CaptureAvailabilityOnMainThread();
        _ = FamilyDeviceProfileFoundation.RetryInitializationAsync();
        Refresh();
    }

    private void Refresh()
    {
        var state = FamilyDeviceProfileFoundation.Initialization.Capture();
        var retry = FamilyDeviceProfileFoundation.RetryTask;
        bool pending = retry is { IsCompleted: false };
        _retry.Disabled = _searchBusy() || !state.CanRetry || pending;
        if (_revision == state.Revision && _displayedRetry == retry && _displayedCompleted == (retry?.IsCompleted ?? false)) return;
        _revision = state.Revision; _displayedRetry = retry; _displayedCompleted = retry?.IsCompleted ?? false;
        bool failed = state.State == FamilyGpuInitializationState.Failed;
        string retryError = retry is { IsCompletedSuccessfully: true } ? retry.Result : "";
        Visible = !_compact || failed || pending || retryError.Length > 0;
        string key = !state.MainDeviceAvailable ? "ui1.devices.unavailable" : state.State switch
        {
            FamilyGpuInitializationState.Creating => "ui1.devices.creating",
            FamilyGpuInitializationState.Available => "ui1.devices.observed",
            FamilyGpuInitializationState.Failed => "ui1.devices.creation_failed",
            _ => "ui1.devices.unverified"
        };
        _state.Text = _text.Get(key);
        _state.AddThemeColorOverride("font_color", failed ? Ui1Theme.Palette.Warning : Ui1Theme.Palette.TextMuted);
        var details = new List<string>();
        if (failed)
            details.Add(_text.Get("ui1.devices.creation_failed_help"));
        else if (!_compact)
            details.Add(_text.Get("ui1.devices.creation_scope"));
        if (state.LastFailure is { } failure && (failed || !_compact))
        {
            details.Add(_text.Format(failed ? "ui1.devices.failure_details" : "ui1.devices.previous_failure_details",
                failure.Stage, failure.ExceptionType + ": " + failure.Reason));
        }
        if (retryError.Length > 0 && !failed)
            details.Add(_text.Format("ui1.devices.retry_error", retryError));
        _detail.Text = string.Join("\n", details);
        _detail.Visible = details.Count > 0;
        _retry.Text = _text.Get(pending ? "ui1.devices.creating" : "ui1.devices.retry");
        _retry.Visible = state.MainDeviceAvailable;
    }
}
