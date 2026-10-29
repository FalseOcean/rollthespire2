using Godot;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private OptionButton? _mapLossyBatchSize;

    private void BuildMapLossySettings(VBoxContainer advanced)
    {
        var map = SettingsSection(advanced, "settings.map_lossy.title", "settings.map_lossy.help");
        map.AddChild(SettingsLabel("settings.map_lossy.batch", 17));
        var actions = new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation", 12);
        actions.AddThemeConstantOverride("v_separation", 8);
        map.AddChild(actions);
        _mapLossyBatchSize = new OptionButton { Name = "SettingsMapLossyGpuBatchSize", CustomMinimumSize = new(200, 44) };
        _mapLossyBatchSize.AddThemeFontSizeOverride("font_size", 18);
        foreach (int batch in MapLossyOptions.SupportedGpuBatchSizes) _mapLossyBatchSize.AddItem(batch.ToString(System.Globalization.CultureInfo.InvariantCulture), batch);
        actions.AddChild(_mapLossyBatchSize);
        _mapLossyBatchSize.ItemSelected += index =>
        {
            if (_bindingSettings) return;
            _persistence!.SetMapLossyGpuBatchSize(MapLossyOptions.SupportedGpuBatchSizes[(int)index]);
            _references?.SyncSearchPreferences();
            RefreshSettings();
        };
        var reset = SettingsButton("settings.map_lossy.reset", () =>
        {
            _persistence!.ResetMapLossyPreferences();
            _references?.SyncSearchPreferences();
            RefreshSettings();
        });
        reset.Name = "SettingsMapLossyReset";
        actions.AddChild(reset);
    }

    private void RefreshMapLossySettings(SearchWorkspace persistence)
    {
        if (_mapLossyBatchSize is null) return;
        _mapLossyBatchSize.Select(_mapLossyBatchSize.GetItemIndex(persistence.Preferences.MapLossyGpuBatchSize));
    }
}
