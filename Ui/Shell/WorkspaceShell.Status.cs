using Godot;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.SaveStatus;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private SaveStatusPage? _statusPage;

    private void OpenStatus()
    {
        if (_statusPage is null)
        {
            _statusPage = new SaveStatusPage(_runtime) { Name = "RuntimeStatusPage" };
            _content.AddChild(_statusPage);
            _statusPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        // The launcher refreshes save/environment authority before opening RT2.
        // Read that snapshot and current device evidence without creating compute work.
        _statusPage?.ApplyLocalization(JsonUiTextProvider.Create(_languageCode),
            RuntimeGameContentNameResolver.Create(_languageCode));
    }
}
