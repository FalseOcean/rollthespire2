namespace RolltheSpire2.Ui.Persistence;

// Read-only migration shape for retired appearance fields in user_preferences.json.
// SearchWorkspace clears this value on load; it is never applied or saved again.
internal sealed record UiColorPreferences
{
    public string Scheme { get; init; } = "ink";
    public string Background { get; init; } = "070D1A";
    public string Panel { get; init; } = "0B1224";
    public string Accent { get; init; } = "C7B07B";
    public string Text { get; init; } = "E5EDF8";
}
