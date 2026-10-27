using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.Workspace;

internal sealed partial class SearchWorkspace
{
    private void InitializeFeaturePreferences() => InRunFeaturePreferences.Initialize(_directory, _preferences);
    internal void SyncFeaturePreferences() => InitializeFeaturePreferences();

    public void SetOperationPredictionEnabled(bool enabled)
    {
        InitializeFeaturePreferences();
        _preferences.OperationPredictionEnabled = enabled;
        InRunFeaturePreferences.SetOperation(_directory, enabled);
        MarkPreferencesDirty(); FlushPreferences();
    }

    public void SetMapPredictionEnabled(bool enabled)
    {
        InitializeFeaturePreferences();
        _preferences.MapPredictionEnabled = enabled;
        InRunFeaturePreferences.SetMap(_directory, enabled);
        MarkPreferencesDirty(); FlushPreferences();
    }

    internal void SetOperationPredictionMode(string id, OperationPredictionMode mode)
    {
        InitializeFeaturePreferences();
        InRunFeaturePreferences.SetOperationMode(_directory, id, mode);
        MarkPreferencesDirty(); FlushPreferences();
    }

    internal void SetOperationPredictionMode(OperationPredictionCategory category, OperationPredictionMode mode)
    {
        InitializeFeaturePreferences();
        InRunFeaturePreferences.SetOperationMode(_directory, category, mode);
        MarkPreferencesDirty(); FlushPreferences();
    }
}
