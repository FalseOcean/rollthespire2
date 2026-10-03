namespace RolltheSpire2.Ui.Persistence;

// Curated descriptions are presentation, never a claim of Search/Exact validation.
internal sealed record DeveloperSeedText
{
    public string Zh { get; init; } = "";
    public string En { get; init; } = "";
    public string Resolve(string language) => (language == "en"
        ? (string.IsNullOrWhiteSpace(En) ? Zh : En)
        : (string.IsNullOrWhiteSpace(Zh) ? En : Zh))?.Trim() ?? "";
}

internal sealed record DeveloperSeedDetails
{
    public DeveloperSeedText Title { get; init; } = new();
    public DeveloperSeedText Description { get; init; } = new();
    public DeveloperSeedText Instructions { get; init; } = new();
    public IReadOnlyList<SearchPresetVisualIconRef> Icons { get; init; } = [];

    internal void Validate()
    {
        if (Title is null || Description is null || Instructions is null || Icons is null)
            throw new InvalidDataException("SeedLibraryDeveloperDetailsInvalid");
        if (Icons.Any(icon => icon is null || icon.Kind != SearchPresetVisualIconRef.RelicKind || !icon.TryGetModelKey(out _)))
            throw new InvalidDataException("SeedLibraryDeveloperIconInvalid");
    }
}
