using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class SeedLibraryCanvas
{
    private string _selectedPreset = "";
    private readonly IGameIconResolver _presetIcons = new ReflectionGameIconResolver("seed-library-presets");
    public event Action<SearchPresetDefinition>? PresetUseRequested;
    public event Action<SearchPresetDefinition>? PresetHistoryRequested;
    public event Action<SearchPresetDefinition?, bool>? PresetEditRequested;
    public event Action<SearchPresetDefinition>? PresetEnvironmentRequested;

    private string PresetContext(SearchPresetDefinition preset)
    {
        string roster = preset.Workbench is { Players.Count: > 0 } workbench
            ? string.Join(" / ", workbench.Players.Select(p => $"P{p.Slot + 1} {_names.Resolve(p.Character, GameContentKind.Character)}"))
            : _names.Resolve(preset.CharacterKey, GameContentKind.Character);
        string mode = preset.IsMultiplayer ? T($"{preset.Workbench!.Players.Count} 人", $"{preset.Workbench!.Players.Count} players") : T("单人", "Solo");
        return $"{mode} · A{preset.Ascension} · {roster}";
    }

    private bool CanUse(SearchPresetDefinition preset, out string issue)
    {
        if (preset.IsWorkbench)
        {
            var resolution = SearchPresetCompatibilityResolver.ResolveWorkbench(preset, RuntimeAuthorityEnvironment.Current.Authority);
            issue = resolution.Unresolved.Count > 0
                ? T("当前缺少：", "Currently unavailable: ") + string.Join("、", resolution.Unresolved.Select(r => r.StableIdentity).Distinct())
                : resolution.CanLoad ? "" : T("当前无法完整读取这些条件，原始内容已保留。", "These conditions cannot be fully read. The original content is preserved.");
            return resolution.CanLoad;
        }
        var legacy = SearchPresetCompatibilityResolver.Resolve(preset, RuntimeAuthorityEnvironment.Current.Authority);
        issue = legacy.Kind == SearchPresetLoadResolutionKind.Full ? "" : T("当前无法完整使用这些条件，原始内容已保留。", "These conditions cannot be used in full. The original content is preserved.");
        return legacy.Kind == SearchPresetLoadResolutionKind.Full;
    }

    private void RenderPresets()
    {
        var presets = _historyStore!.Presets.GetAll().Where(p => p.Source != SearchPresetSource.Temporary && Matches(p.SearchText + " " + PresetContext(p))).ToArray();
        if (!presets.Any(p => p.Id == _selectedPreset))
            _selectedPreset = presets.OrderBy(p => p.Source == SearchPresetSource.User ? 0 : 1).FirstOrDefault()?.Id ?? "";
        foreach (var source in new[] { SearchPresetSource.User, SearchPresetSource.BuiltIn })
        {
            var items = presets.Where(p => p.Source == source).ToArray();
            if (items.Length == 0) continue;
            _entries.AddChild(Text(source == SearchPresetSource.User ? T("我的预设", "My presets") : T("开发者预设", "Developer presets"), 16, true));
            foreach (var preset in items)
                AddListItem(preset.Id, preset.TitleFor(_language), PresetContext(preset), _selectedPreset == preset.Id,
                    () => { _selectedPreset = preset.Id; SelectDetail(); }, preset.VisualIcons);
        }
        if (_historyStore.Presets.LoadIssues.Count > 0) ShowIssue(T("部分预设无法读取，原文件已保留。", "Some presets could not be read; original files are preserved."));
        var selected = presets.FirstOrDefault(p => p.Id == _selectedPreset);
        if (selected is null)
        {
            EmptyDetail(T("可以在筛种页保存当前条件，或从开发者预设开始。", "Save conditions from Search, or start with a developer preset."));
            return;
        }
        var primary = DetailHeading(selected.TitleFor(_language), PresetContext(selected), selected.VisualIcons);
        bool canUse = CanUse(selected, out string issue);
        AddAction(primary, T("查看历史结果", "View search history"), () => PresetHistoryRequested?.Invoke(selected)).Name = "ViewPresetHistory";
        var use = AddAction(primary, T("使用这组预设", "Use this preset"), () => PresetUseRequested?.Invoke(selected), true);
        use.Name = "UseLibraryPreset"; RequireIdle(use, !canUse);
        if (selected.Source == SearchPresetSource.User)
        {
            var management = ManagementActions();
            var update = AddAction(management, T("用当前环境更新", "Update environment"), () => PresetEnvironmentRequested?.Invoke(selected));
            update.Name = "UpdatePresetEnvironment"; RequireIdle(update);
            update.TooltipText = T("更新环境标记，保留原有条件和历史结果。", "Update the environment marker while retaining the conditions and historical results.");
            var edit = AddAction(management, T("修改信息", "Edit information"), () => PresetEditRequested?.Invoke(selected, true)); edit.Name = "EditLibraryPreset"; RequireIdle(edit);
            AddDeleteAction(management, () => ConfirmDeletePreset(selected)).Name = "DeleteLibraryPreset";
        }
        string description = selected.DescriptionFor(_language);
        if (!string.IsNullOrWhiteSpace(description)) _detail.AddChild(Text(description, 19));
        if (!canUse) _detail.AddChild(Text(issue, 17, true));
        if (selected.Source == SearchPresetSource.User) AddEnvironmentNotice(selected.EnvironmentFingerprint);
        AddConditions(selected);
    }

    private void AddPresetIcons(Control parent, IReadOnlyList<SearchPresetVisualIconRef> icons, int size)
    {
        foreach (var icon in icons.Take(3))
        {
            if (!icon.TryGetModelKey(out var key)) continue;
            var descriptor = _presetIcons.Resolve(key, GameContentKind.Relic, IconVariant.Small);
            if (descriptor.Texture is null || descriptor.IsMissing) continue;
            parent.AddChild(new TextureRect { Name = "PresetRelicIcon" + parent.GetChildCount(), Texture = descriptor.Texture,
                CustomMinimumSize = new(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
        }
    }

    private void ConfirmDeletePreset(SearchPresetDefinition preset)
    {
        if (_confirmation is null) return;
        _confirmed = () =>
        {
            if (!_historyStore!.Presets.DeleteUserPreset(preset.Id)) throw new IOException("PresetDeleteFailed");
            _historyStore.TrimQueryHistory(); _historyStore.FlushWorkspace();
            RefreshEntries(); ShowReceipt(T("已删除预设。", "Preset deleted."));
        };
        BeginModal();
        _confirmation.Open(T("删除预设", "Delete preset"), preset.Title, T("删除", "Delete"), T("取消", "Cancel"));
    }
}
