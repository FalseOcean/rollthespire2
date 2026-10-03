namespace RolltheSpire2.Ui.Persistence;

internal sealed record UiColorPreferences
{
    public string Scheme { get; init; } = "ink";
    public string Background { get; init; } = "080B14";
    public string Panel { get; init; } = "131B30";
    public string Accent { get; init; } = "C69A43";
    public string Text { get; init; } = "EEEAE1";

    public UiColorPreferences Normalize() => this with
    {
        Scheme = Scheme is "ink" or "stars" or "custom" ? Scheme : "ink",
        Background = Hex(Background, "080B14"), Panel = Hex(Panel, "131B30"),
        Accent = Hex(Accent, "C69A43"), Text = Hex(Text, "EEEAE1")
    };

    private static string Hex(string? value, string fallback)
    {
        string candidate = (value ?? "").Trim().TrimStart('#');
        return candidate.Length == 6 && candidate.All(Uri.IsHexDigit) ? candidate.ToUpperInvariant() : fallback;
    }
}
