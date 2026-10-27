using Godot;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private PanelContainer? _operationConfiguration;
    private LineEdit? _operationSearch;
    private Label? _operationRfStatus, _operationNoMatches;
    private readonly Dictionary<OperationPredictionCategory, VBoxContainer> _operationGroups = [];
    private readonly Dictionary<string, (OperationPredictionEntry Entry, HBoxContainer Row, Label Name, OptionButton Mode)> _operationEntries = [];
    private string _operationNamesLanguage = "";
    private bool _bindingOperationSettings;

    private void BuildOperationPredictionSettings(VBoxContainer controls)
    {
        var configure = SettingsButton("settings.operation.configure", OpenOperationPredictionConfiguration);
        configure.Name = "SettingsOperationConfigure";
        configure.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        controls.AddChild(configure);
    }

    private void OpenOperationPredictionConfiguration()
    {
        if (_operationConfiguration is null) BuildOperationPredictionConfiguration();
        _operationConfiguration!.Show();
        LocalizeOperationPredictionSettings(_settingsText!);
        RefreshSettings();
        _operationSearch!.GrabFocus();
    }

    private void CloseOperationPredictionConfiguration() => _operationConfiguration?.Hide();

    private void BuildOperationPredictionConfiguration()
    {
        _operationConfiguration = new PanelContainer { Name = "SettingsOperationConfiguration", Visible = false,
            MouseFilter = MouseFilterEnum.Stop };
        var style = _palette.Box(_palette.Canvas, _palette.Line, 1);
        style.ContentMarginLeft = style.ContentMarginRight = 24;
        style.ContentMarginTop = style.ContentMarginBottom = 20;
        _operationConfiguration.AddThemeStyleboxOverride("panel", style);
        _settingsPage.AddChild(_operationConfiguration);
        _operationConfiguration.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 14);
        _operationConfiguration.AddChild(body);
        var heading = new HBoxContainer(); body.AddChild(heading);
        var title = SettingsLabel("settings.operation.configuration.title", 28);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(title);
        var close = SettingsButton("settings.back", CloseOperationPredictionConfiguration);
        close.Name = "SettingsOperationConfigurationBack"; heading.AddChild(close);
        body.AddChild(SettingsLabel("settings.operation.configuration.help", 17, true, wrap: true));
        _operationSearch = new LineEdit { Name = "SettingsOperationSearch", CustomMinimumSize = new(0, 44),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true };
        StyleSettingsInput(_operationSearch); body.AddChild(_operationSearch);
        _operationSearch.TextChanged += _ => FilterOperationPredictionEntries();
        _operationRfStatus = _palette.Label("", 16, true);
        _operationRfStatus.Name = "SettingsOperationRfStatus";
        _operationRfStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_operationRfStatus);
        var scroll = new ScrollContainer { Name = "SettingsOperationEntryScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll);
        var groups = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        groups.AddThemeConstantOverride("separation", 20); scroll.AddChild(groups);
        foreach (var category in Enum.GetValues<OperationPredictionCategory>())
        {
            var group = new VBoxContainer { Name = "SettingsOperationGroup" + category };
            group.AddThemeConstantOverride("separation", 8); groups.AddChild(group); _operationGroups.Add(category, group);
            var groupHeading = new HBoxContainer(); groupHeading.AddThemeConstantOverride("separation", 10); group.AddChild(groupHeading);
            var groupTitle = SettingsLabel("settings.operation.category." + category, 22);
            groupTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; groupHeading.AddChild(groupTitle);
            groupHeading.AddChild(SettingsLabel("settings.operation.batch", 16, true));
            foreach (var mode in Enum.GetValues<OperationPredictionMode>())
            {
                var batch = SettingsButton("settings.operation.mode." + mode, () =>
                {
                    _persistence!.SetOperationPredictionMode(category, mode);
                    RefreshSettings();
                });
                batch.Name = "SettingsOperationBatch" + category + mode;
                groupHeading.AddChild(batch);
            }
            foreach (var entry in OperationPredictionCatalog.ForCategory(category))
            {
                string token = entry.Id.Replace(':', '_').Replace('-', '_');
                var row = new HBoxContainer { Name = "SettingsOperationEntry_" + token };
                row.AddThemeConstantOverride("separation", 16); group.AddChild(row);
                var name = _palette.Label("", 18);
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                name.AutowrapMode = TextServer.AutowrapMode.WordSmart; row.AddChild(name);
                var choice = new OptionButton { Name = "SettingsOperationMode_" + token,
                    CustomMinimumSize = new(220, 40), FitToLongestItem = false };
                choice.AddThemeFontSizeOverride("font_size", 18);
                foreach (var mode in Enum.GetValues<OperationPredictionMode>()) choice.AddItem("", (int)mode);
                row.AddChild(choice);
                _operationEntries.Add(entry.Id, (entry, row, name, choice));
                choice.ItemSelected += index =>
                {
                    if (_bindingOperationSettings) return;
                    _persistence!.SetOperationPredictionMode(entry.Id, (OperationPredictionMode)choice.GetItemId((int)index));
                    RefreshSettings();
                };
            }
        }
        _operationNoMatches = SettingsLabel("settings.operation.search.empty", 18, true);
        _operationNoMatches.Name = "SettingsOperationNoMatches"; groups.AddChild(_operationNoMatches);
    }

    private void LocalizeOperationPredictionSettings(IUiTextProvider text)
    {
        if (_operationConfiguration is null) return;
        _operationSearch!.PlaceholderText = text.Get("settings.operation.search");
        foreach (var item in _operationEntries.Values)
            foreach (var mode in Enum.GetValues<OperationPredictionMode>())
                item.Mode.SetItemText((int)mode, text.Get("settings.operation.mode." + mode));
        if (_operationNamesLanguage != text.LanguageCode)
        {
            var names = RuntimeGameContentNameResolver.Create(text.LanguageCode);
            foreach (var item in _operationEntries.Values)
            {
                string name = item.Entry.Source is { } source ? names.Resolve(source, source.Category switch
                {
                    "RELIC" => GameContentKind.Relic,
                    "EVENT" => GameContentKind.Event,
                    _ => GameContentKind.Potion
                }) : text.Get(item.Entry.NameKey!);
                item.Name.Text = item.Entry.IsSupplement ? text.Format("settings.operation.supplement", name) : name;
            }
            _operationNamesLanguage = text.LanguageCode;
        }
        FilterOperationPredictionEntries();
    }

    private void FilterOperationPredictionEntries()
    {
        string query = _operationSearch?.Text.Trim() ?? "";
        foreach (var item in _operationEntries.Values)
        {
            string category = _settingsText!.Get("settings.operation.category." + item.Entry.Category);
            item.Row.Visible = query.Length == 0 || item.Name.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || category.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var (category, group) in _operationGroups)
            group.Visible = _operationEntries.Values.Any(item => item.Entry.Category == category && item.Row.Visible);
        if (_operationNoMatches is not null) _operationNoMatches.Visible = !_operationGroups.Values.Any(group => group.Visible);
    }

    private void RefreshOperationPredictionSettings(IUiTextProvider text, SearchWorkspace persistence)
    {
        if (_operationConfiguration is not { Visible: true }) return;
        _bindingOperationSettings = true;
        try
        {
            foreach (var item in _operationEntries.Values)
                item.Mode.Select((int)persistence.Preferences.OperationPredictionModes.Get(item.Entry.Id));
        }
        finally { _bindingOperationSettings = false; }
        string status = RandomForeseerCompatibility.CaptureStatus() switch
        {
            RandomForeseerStatus.NotLoaded => "not_loaded",
            RandomForeseerStatus.Known => "known",
            _ => "unknown"
        };
        _operationRfStatus!.Text = text.Get("settings.operation.rf." + status);
    }
}
