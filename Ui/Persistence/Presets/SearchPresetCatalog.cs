namespace RolltheSpire2.Ui.Persistence;

internal sealed class SearchPresetCatalog
{
    private readonly BuiltInSearchPresetProvider _builtIn;
    private readonly TemporarySearchPresetStore _temporary;
    private readonly SearchPresetStore _user;

    public SearchPresetCatalog(string stateDirectory)
    {
        _builtIn = new BuiltInSearchPresetProvider();
        _temporary = new TemporarySearchPresetStore(stateDirectory);
        _user = new SearchPresetStore(stateDirectory);
    }

    private long _cachedRevision = -1;
    private string _cachedCulture = "";
    private IReadOnlyList<SearchPresetDefinition> _all = [];
    internal long Revision => _user.Revision;

    public IReadOnlyList<SearchPresetDefinition> GetAll()
    {
        string culture = System.Globalization.CultureInfo.CurrentCulture.Name;
        if (_cachedRevision == Revision && _cachedCulture == culture) return _all;
        _all = Array.AsReadOnly(_builtIn.GetPresets()
            .Concat(_temporary.GetPresets()).Concat(_user.GetPresets())
            .OrderBy(preset => preset.Source)
            .ThenByDescending(preset => preset.Source == SearchPresetSource.Temporary ? preset.CreatedAtUtc : DateTimeOffset.MinValue)
            .ThenBy(preset => preset.Title, StringComparer.CurrentCultureIgnoreCase).ToArray());
        _cachedRevision = Revision; _cachedCulture = culture;
        return _all;
    }

    public IReadOnlyList<string> LoadIssues => _temporary.LoadIssues.Concat(_user.LoadIssues).ToArray();

    public SearchPresetDefinition SaveUserPreset(
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons,
        SearchPresetCapture capture) =>
        _user.Save(title, description, visualIcons, capture);

    public SearchPresetDefinition SaveUserPresetFromExisting(
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons,
        SearchPresetDefinition source) =>
        _user.SaveFromExisting(title, description, visualIcons, source);

    public SearchPresetDefinition UpdateUserMetadata(
        string id,
        string title,
        string description,
        IReadOnlyList<SearchPresetVisualIconRef> visualIcons) =>
        _user.UpdateMetadata(id, title, description, visualIcons);

    public bool DeleteUserPreset(string id) => _user.Delete(id);
    public SearchPresetDefinition UpdateUserEnvironment(string id, string fingerprint) => _user.UpdateEnvironment(id, fingerprint);

    public bool TryGetUserByName(string name, out SearchPresetDefinition preset)
    {
        string normalized = (name ?? string.Empty).Trim();
        preset = _user.GetPresets().FirstOrDefault(candidate =>
            string.Equals(candidate.Title, normalized, StringComparison.CurrentCultureIgnoreCase))!;
        return preset is not null;
    }

    public bool TryGet(string id, out SearchPresetDefinition preset)
    {
        preset = GetAll().FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.Ordinal))!;
        return preset is not null;
    }
}
