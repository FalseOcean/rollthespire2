using System.Text.Json;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Ui.Persistence;

internal sealed class TemporarySearchPresetStore
{
    private const int Capacity = 5;
    private const string FileName = "temporary_search_presets.json";
    private readonly string _sourcePath;
    private readonly JsonSerializerOptions _json;
    private readonly List<SearchPresetDefinition> _entries = new();
    private readonly List<string> _loadIssues = new();

    public TemporarySearchPresetStore(string stateDirectory)
    {
        string current = Path.Combine(stateDirectory, "temporary_search_presets.format4.json");
        _sourcePath = File.Exists(current) ? current : Path.Combine(stateDirectory, FileName);
        _json = SearchPresetStore.CreateJsonOptions();
        LoadFailSoft();
    }

    public IReadOnlyList<SearchPresetDefinition> GetPresets() =>
        _entries.OrderByDescending(entry => entry.CreatedAtUtc).ToArray();
    public IReadOnlyList<string> LoadIssues => _loadIssues.ToArray();

    private void LoadFailSoft()
    {
        _entries.Clear();
        _loadIssues.Clear();
        if (!File.Exists(_sourcePath)) return;
        try
        {
            string jsonText = File.ReadAllText(_sourcePath);
            List<SearchPresetStore.PersistedSearchPreset>? persisted =
                JsonSerializer.Deserialize<List<SearchPresetStore.PersistedSearchPreset>>(jsonText, _json);
            if (persisted is null) throw new InvalidDataException("TemporaryPresetDocumentNull");
            // Read every persisted entry before limiting the visible history, so
            // unreadable tail assets remain reported alongside the retained rows.
            using var original = JsonDocument.Parse(jsonText);
            int sourceIndex = 0;
            foreach (SearchPresetStore.PersistedSearchPreset item in persisted)
            {
                try
                {
                    SearchPresetDefinition entry = SearchPresetStore.Normalize(item, SearchPresetSource.Temporary, _json, original.RootElement[sourceIndex++].GetRawText());
                    if (!entry.QueryResolved)
                    {
                        _loadIssues.Add(SearchPresetStore.LoadIssue(Path.GetFileName(_sourcePath), "PresetQueryShapeUnresolved"));
                    }
                    if (_entries.Count < Capacity) _entries.Add(entry);
                }
                catch (Exception ex)
                {
                    _loadIssues.Add(SearchPresetStore.LoadIssue(Path.GetFileName(_sourcePath), SearchPresetStore.LoadIssueReason(ex)));
                    RuntimeLog.Warn(
                        $"temporarySearchPresetEntrySkipped=true;failSoft=true;issue={ex.GetType().Name}:{SearchPresetStore.Compact(ex.Message)}");
                }
            }
            _entries.Sort((left, right) => right.CreatedAtUtc.CompareTo(left.CreatedAtUtc));
            if (_entries.Count > Capacity)
                _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
        catch (Exception ex)
        {
            _loadIssues.Add(SearchPresetStore.LoadIssue(Path.GetFileName(_sourcePath), SearchPresetStore.LoadIssueReason(ex)));
            RuntimeLog.Warn(
                $"temporarySearchPresetStoreLoadFailed=true;failSoft=true;issue={ex.GetType().Name}:{SearchPresetStore.Compact(ex.Message)}");
        }
    }
}
