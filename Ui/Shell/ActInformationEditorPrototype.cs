using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.BossMap;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class ActInformationEditorPrototype : Control
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly WorkspacePalette _p = WorkspacePalette.Canonical;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("act-information-editor");
    private readonly SearchWorkspacePersistence _persistence;
    private readonly Control _body = new();
    private ScrollContainer? _mapScroll;
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private SeatDraft _draft = new();
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private bool _identityGuideExpanded;
    private bool _mapGuideExpanded = true;
    private string _toastMessage = string.Empty;
    private ulong _toastRevision;

    private sealed record PropertyCondition(MapMetricKind Metric, MapMetricComparison Comparison, int Value);
    private sealed record RouteCondition(MapMetricKind Metric, int Value);

    private sealed class SeatDraft
    {
        public string Context = string.Empty;
        public int Players = 1;
        public int Ascension;
        public BossMapSearchUiCatalog Catalog = BossMapSearchUiCatalog.Empty(RuntimeProfileId.Unsupported, "act-info-uninitialized");
        public HashSet<ModelKey>[] SelectedVariants { get; } = NewIdentitySets();
        public HashSet<ModelKey>[] SelectedBosses { get; } = NewIdentitySets();
        public bool MapMode = true;
        // Retained for existing workbench UI-state persistence; map properties are now always visible.
        public bool AdvancedProperties;
        public int Scope;
        public Dictionary<int, List<PropertyCondition>> Properties { get; } = [];
        public Dictionary<int, RouteCondition> Routes { get; } = [];
        public MapMetricKind PropertyMetric = MapMetricKind.GuaranteedElite;
        public int PropertyOptionIndex;
        public MapMetricKind RouteMetric = MapMetricKind.ReachableMaxElite;
        public int RouteValue = 5;
    }

    public ActInformationEditorPrototype(ModRuntimeSnapshot runtime, SearchWorkspacePersistence persistence)
    {
        _runtime = runtime;
        _persistence = persistence;
        _mapGuideExpanded = persistence.Preferences.ActInformationMapGuideExpanded
            ?? persistence.Preferences.ActInformationGuideExpanded;
        AddChild(_body);
    }

    public event Action? GuideRequested;

    public void SelectPage(bool map)
    {
        if (_draft.MapMode == map) return;
        _draft.MapMode = map;
        Render();
    }

    public void Refresh(string language, IUiTextProvider text, ModelKey character, int ascension,
        int players, int seat, SerializableUnlockState? unlocks = null, bool render = true,
        WorldAuthoritySnapshot? partyWorld = null)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        foreach (int removed in _drafts.Keys.Where(index => index >= players).ToArray()) _drafts.Remove(removed);
        int draftKey = players > 1 ? -1 : seat;
        if (!_drafts.TryGetValue(draftKey, out SeatDraft? draft)) _drafts[draftKey] = draft = new SeatDraft();
        _draft = draft;
        _draft.Players = players;
        _draft.Ascension = ascension;
        string unlockKey = players == 1
            ? System.Text.Json.JsonSerializer.Serialize(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks);
        string context = $"{character}/{ascension}/{players}/{seat}/{unlockKey}/{partyWorld?.SnapshotFingerprint}";
        if (_draft.Context != context)
        {
            _draft.Context = context;
            try
            {
                if (players > 1 && unlocks is null)
                    _draft.Catalog = BossMapSearchUiCatalog.Empty(_runtime.Profile.ProfileId, "act-info-unlocks-unread");
                else
                {
                    UnlockState resolved = players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    string seed = new(_runtime.Profile.SeedAlphabet[0], _runtime.Profile.SeedLength);
                    var effects = ReflectionNeowEffectSnapshotAdapter.Capture(_runtime.Profile, seed,
                        CharacterIdentity.FromKey(character), ascension, players, seat, _runtime.Profile.ProfileId,
                        _runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), resolved);
                    WorldAuthoritySnapshot world = partyWorld ?? ReflectionNeowEffectSnapshotAdapter.CaptureWorld(_runtime.Profile, seed,
                        CharacterIdentity.FromKey(character), ascension, players, seat, true, effects.EffectAuthority,
                        WorldGameMode.Unknown, gameVersion: _runtime.Detection.DisplayVersion, explicitUnlockState: resolved);
                    _draft.Catalog = BossMapSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, ascension, world);
                }
                RevalidateIdentity();
                RevalidateMapCapabilities();
            }
            catch (Exception exception)
            {
                _draft.Catalog = BossMapSearchUiCatalog.Empty(_runtime.Profile.ProfileId, "act-info-catalog-unavailable");
                RuntimeLog.Warn("actInformationCatalog=" + exception.Message);
            }
        }
        if (render) Render();
    }

    private void RevalidateIdentity()
    {
        for (int act = 1; act <= 3; act++)
        {
            BossMapActSectionDefinition? section = Section(act);
            ModelKey[] variants = section?.Variants.Select(value => value.ActKey).ToArray() ?? [];
            ModelKey[] bosses = section?.Variants.SelectMany(value => value.Bosses).Distinct(ModelKeyComparer.Instance).ToArray() ?? [];
            _draft.SelectedVariants[act - 1].RemoveWhere(key => !variants.Contains(key, ModelKeyComparer.Instance));
            if (variants.Distinct(ModelKeyComparer.Instance).Take(2).Count() <= 1)
                _draft.SelectedVariants[act - 1].Clear();
            _draft.SelectedBosses[act - 1].RemoveWhere(key => !bosses.Contains(key, ModelKeyComparer.Instance));
        }
    }

    private static HashSet<ModelKey>[] NewIdentitySets() => Enumerable.Range(0, 3)
        .Select(_ => new HashSet<ModelKey>(ModelKeyComparer.Instance)).ToArray();

    private BossMapActSectionDefinition? Section(int act) => _draft.Catalog.Sections.FirstOrDefault(value => value.Act == act);

    private void Render()
    {
        int scrollTop = _draft.MapMode && _mapScroll is not null && GodotObject.IsInstanceValid(_mapScroll)
            ? _mapScroll.ScrollVertical : 0;
        Clear(_body);
        _body.Size = Size;
        VBoxContainer guide = FamilyGuide(_body, Size.X - 20);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        void Layout()
        {
            float top = guide.Size.Y + 10;
            scroll.Position = new(0, top);
            scroll.Size = new(Size.X, Math.Max(0, Size.Y - top));
        }
        guide.Resized += Layout;
        Layout();
        _body.AddChild(scroll);
        float width = Size.X - 28;
        var content = new Control { CustomMinimumSize = new(width, 1) };
        scroll.AddChild(content);
        float y = _draft.MapMode ? RenderMap(content, width, 0) : RenderIdentity(content, width, 0);
        content.CustomMinimumSize = new(width, y + 16);
        _mapScroll = _draft.MapMode ? scroll : null;
        if (scrollTop > 0) scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scrollTop);
        if (!string.IsNullOrEmpty(_toastMessage)) RenderToast();
    }

    private VBoxContainer FamilyGuide(Control host, float width)
    {
        bool expanded = _draft.MapMode ? _mapGuideExpanded : _identityGuideExpanded;
        string titleKey = _draft.MapMode ? "query.map.guide.title" : "query.act_info.identity.guide.title";
        string summaryKey = _draft.MapMode ? "query.map.guide.summary" : "query.act_info.identity.guide.summary";
        string usageKey = _draft.MapMode ? "query.map.guide.usage" : "query.act_info.identity.guide.usage";
        string boundaryKey = _draft.MapMode ? "query.map.guide.boundary" : "query.act_info.identity.guide.boundary";
        return SearchEditorGuide.Build(host, _p, width,
            _text.Get(titleKey), _text.Get(summaryKey), [_text.Get(usageKey), _text.Get(boundaryKey)], expanded,
            () =>
            {
                if (_draft.MapMode)
                {
                    _mapGuideExpanded = !_mapGuideExpanded;
                    _persistence.SetActInformationMapGuideExpanded(_mapGuideExpanded);
                }
                else
                {
                    _identityGuideExpanded = !_identityGuideExpanded;
                }
                Render();
            }, () => GuideRequested?.Invoke());
    }

    private float RenderIdentity(Control content, float width, float top)
    {
        const float choicesLeft = 104, tileWidth = 104, tileHeight = 84, tileGap = 10;
        int columns = Math.Max(1, (int)((width - choicesLeft + tileGap) / (tileWidth + tileGap)));
        float y = top;
        for (int act = 1; act <= 3; act++)
        {
            int actIndex = act - 1;
            BossMapActSectionDefinition? section = Section(act);
            ModelKey[] variants = section?.Variants.Select(value => value.ActKey).Distinct(ModelKeyComparer.Instance).ToArray() ?? [];
            ModelKey[] bosses = BossesForAct(act);
            int bossRows = Math.Max(1, (int)Math.Ceiling(bosses.Length / (double)columns));
            float sectionHeight = 76 + bossRows * (tileHeight + tileGap);
            var sectionPanel = new Panel
            {
                Position = new(0, y), Size = new(width, sectionHeight), MouseFilter = MouseFilterEnum.Ignore
            };
            sectionPanel.AddThemeStyleboxOverride("panel", _p.Box(_p.Canvas, _p.Line, 1));
            content.AddChild(sectionPanel);
            Label actTitle = Text(content, _text.Format("query.act_info.act", act), 12, y + 13, 92, 20);
            actTitle.AddThemeColorOverride("font_color", _p.Color(_p.Selected));
            Text(content, _text.Get("query.act_info.variant"), choicesLeft, y + 17, 126, 15, true);
            HashSet<ModelKey> selectedVariants = _draft.SelectedVariants[actIndex];
            if (variants.Length == 1)
            {
                Text(content, _names.Resolve(variants[0], GameContentKind.Act), choicesLeft + 132, y + 17, width - choicesLeft - 144, 16);
            }
            else
            {
                for (int index = 0; index < variants.Length; index++)
                {
                    ModelKey key = variants[index];
                    IdentityButton(content, key, GameContentKind.Act, IconVariant.Small,
                        choicesLeft + 132 + index * 218, y + 8, 208,
                        selectedVariants.Contains(key), () => { ToggleSelected(selectedVariants, key); Render(); });
                }
            }
            AddLine(content, 12, y + 65, width - 24);
            Text(content, _text.Get("query.act_info.boss"), 12, y + 88, 88, 16, true);
            HashSet<ModelKey> selectedBosses = _draft.SelectedBosses[actIndex];
            for (int index = 0; index < bosses.Length; index++)
            {
                ModelKey key = bosses[index];
                BossIdentityButton(content, key, choicesLeft + index % columns * (tileWidth + tileGap),
                    y + 76 + index / columns * (tileHeight + tileGap), tileWidth, tileHeight, selectedBosses.Contains(key),
                    () => { ToggleSelected(selectedBosses, key); Render(); });
            }
            y += sectionHeight + 14;
        }
        return y - 14;
    }

    private ModelKey[] BossesForAct(int act)
    {
        BossMapActSectionDefinition? section = Section(act);
        if (section is null) return [];
        return section.Variants.SelectMany(value => value.Bosses).Distinct(ModelKeyComparer.Instance).ToArray();
    }

    private static void ToggleSelected(HashSet<ModelKey> selected, ModelKey key)
    {
        if (!selected.Add(key)) selected.Remove(key);
    }

    private float RenderMap(Control content, float width, float top)
    {
        Text(content, _text.Get("query.map.scope.current_label"), 0, top, width, 14, true);
        string[] scopes = ["query.map.scope.total", "query.map.scope.act1", "query.map.scope.act2", "query.map.scope.act3"];
        float scopeWidth = (width - 24) / scopes.Length;
        for (int index = 0; index < scopes.Length; index++)
        {
            int captured = index;
            int count = (_draft.Routes.ContainsKey(index) ? 1 : 0) +
                (_draft.Properties.GetValueOrDefault(index)?.Count ?? 0);
            Button scope = Button(content, string.Empty, index * (scopeWidth + 8), top + 28, scopeWidth,
                () => SetMapScope(captured), _draft.Scope == index, 66);
            scope.Name = $"MapScope{index}";
            Label name = Text(scope, _text.Get(scopes[index]), 12, 7, scopeWidth - 24, 18);
            name.MouseFilter = MouseFilterEnum.Ignore;
            Label summary = Text(scope, count == 0 ? _text.Get("query.map.scope.empty") : _text.Format("query.map.scope.count", count),
                12, 35, scopeWidth - 24, 14, true);
            summary.MouseFilter = MouseFilterEnum.Ignore;
        }

        float y = RenderRoute(content, width, top + 114);
        if (_draft.Scope == 0 && (_draft.Properties.GetValueOrDefault(0)?.Count ?? 0) == 0) return y + 16;
        AddLine(content, 0, y + 6, width);
        return RenderMapProperties(content, width, y + 26);
    }

    private float RenderMapProperties(Control content, float width, float top)
    {
        Text(content, _text.Get("query.map.property.filter"), 0, top, width, 18);
        MapHint(content, _text.Get("query.map.property.hint"), 0, top + 30, width);
        float y = top + 76;
        int scope = _draft.Scope;
        List<PropertyCondition> scopeConditions = PropertiesForScope(scope);
        float valueLeft = width * .49f;
        float valueWidth = width - valueLeft - 54;
        if (scopeConditions.Count == 0)
        {
            Text(content, _text.Get("query.map.property.empty"), 0, y, width, 14, true);
            y += 34;
        }
        for (int row = 0; row < scopeConditions.Count; row++)
        {
            int conditionIndex = row;
            PropertyCondition condition = scopeConditions[row];
            PropertyCondition captured = condition;
            Label name = Text(content, PropertyLabel(condition.Metric), 0, y + 5, valueLeft - 16, 16);
            name.TooltipText = name.Text;
            MapMetricOption[] options = PropertyOptions(scope, condition);
            int selected = Array.FindIndex(options, option => !option.IsAny &&
                option.Comparison == condition.Comparison && option.Value == condition.Value);
            OptionButton threshold = Options(content, options.Select(OptionLabel).ToArray(), selected, valueLeft, y, valueWidth, index =>
            {
                List<PropertyCondition> conditions = PropertiesForScope(scope);
                if (conditionIndex >= conditions.Count || conditions[conditionIndex] != captured) return;
                MapMetricOption option = options[index];
                if (option.IsAny) conditions.RemoveAt(conditionIndex);
                else conditions[conditionIndex] = new(captured.Metric, option.Comparison, option.Value);
                Render();
            });
            threshold.Name = $"MapPropertyValue{row}";
            Button remove = Button(content, "×", width - 40, y, 40, () =>
            {
                List<PropertyCondition> conditions = PropertiesForScope(scope);
                if (conditionIndex < conditions.Count && conditions[conditionIndex] == captured)
                    conditions.RemoveAt(conditionIndex);
                Render();
            }, false, 38);
            remove.Name = $"RemoveMapProperty{row}";
            remove.TooltipText = _text.Format("common.remove_named", PropertyLabel(condition.Metric));
            y += 48;
        }
        // Imported conditions remain visible and removable, even if their scope no longer offers a builder.
        if (scope == 0) return y + 8;

        MapMetricKind[] metrics = PropertyMetrics(scope)
            .Where(metric => scopeConditions.All(condition => condition.Metric != metric)).ToArray();
        if (metrics.Length == 0)
        {
            Text(content, _text.Get("query.map.property.all_added"), 0, y + 6, width, 14, true);
            return y + 42;
        }
        if (!metrics.Contains(_draft.PropertyMetric))
        {
            _draft.PropertyMetric = metrics[0];
            _draft.PropertyOptionIndex = 0;
        }
        if (scopeConditions.Count > 0)
        {
            AddLine(content, 0, y + 3, width);
            y += 20;
        }
        float metricWidth = width * .45f;
        float optionLeft = metricWidth + 12;
        float optionWidth = width - optionLeft - 154;
        Text(content, _text.Get("query.map.property.add"), 0, y, metricWidth, 14, true);
        Text(content, _text.Get("query.map.route.quantity"), optionLeft, y, optionWidth, 14, true);
        float controlsTop = y + 28;
        OptionButton metricPicker = Options(content, metrics.Select(PropertyLabel).ToArray(), Array.IndexOf(metrics, _draft.PropertyMetric),
            0, controlsTop, metricWidth, index =>
            {
                _draft.PropertyMetric = metrics[index];
                _draft.PropertyOptionIndex = 0;
                Render();
            });
        metricPicker.Name = "MapPropertyMetric";
        MapMetricKind selectedMetric = _draft.PropertyMetric;
        MapMetricOption[] builderOptions = Descriptor(scope, selectedMetric).Options.ToArray();
        _draft.PropertyOptionIndex = Math.Clamp(_draft.PropertyOptionIndex, 0, builderOptions.Length - 1);
        OptionButton optionPicker = Options(content, builderOptions.Select(OptionLabel).ToArray(), _draft.PropertyOptionIndex,
            optionLeft, controlsTop, optionWidth, index => { _draft.PropertyOptionIndex = index; Render(); });
        optionPicker.Name = "MapPropertyValue";
        MapMetricOption selectedOption = builderOptions[_draft.PropertyOptionIndex];
        Button add = Button(content, _text.Get("query.map.property.confirm"), width - 142, controlsTop, 142, () =>
        {
            if (selectedOption.IsAny) return;
            List<PropertyCondition> conditions = PropertiesForScope(scope);
            if (conditions.Any(condition => condition.Metric == selectedMetric)) return;
            conditions.Add(new(selectedMetric, selectedOption.Comparison, selectedOption.Value));
            ResetPropertyBuilderSelection();
            Render();
        }, false, 38);
        add.Name = "AddMapProperty";
        add.Disabled = selectedOption.IsAny;
        add.AddThemeFontSizeOverride("font_size", 15);
        return controlsTop + 52;
    }

    private MapMetricOption[] PropertyOptions(int scope, PropertyCondition condition)
    {
        MapMetricOption[] options = MapMetricCapabilityCatalog.Find(_draft.Players, _draft.Ascension, scope, condition.Metric)
            ?.Options.ToArray() ?? [MapMetricOption.Any];
        if (!options.Any(option => !option.IsAny && option.Comparison == condition.Comparison && option.Value == condition.Value))
            options = options.Append(new MapMetricOption(false, condition.Comparison, condition.Value)).ToArray();
        return options;
    }

    private void SetMapScope(int scope)
    {
        if (_mapScroll is not null && GodotObject.IsInstanceValid(_mapScroll)) _mapScroll.ScrollVertical = 0;
        _draft.Scope = scope;
        ResetPropertyBuilderSelection();
        LoadRouteEditor();
        Render();
    }

    private float RenderRoute(Control content, float width, float top)
    {
        int scope = _draft.Scope;
        Text(content, _text.Get("query.map.route"), 0, top, width, 18);
        MapHint(content, _text.Get(scope == 0 ? "query.map.route.hint.total" : "query.map.route.hint.act"),
            0, top + 30, width);
        bool blockedByTotal = scope > 0 && _draft.Routes.ContainsKey(0);
        bool blockedByAct = scope == 0 && _draft.Routes.Keys.Any(value => value > 0);
        if (blockedByTotal || blockedByAct)
        {
            string key = blockedByTotal ? "query.map.route.total_active" : "query.map.route.act_active";
            Text(content, _text.Get(key), 0, top + 82, width - 198, 15, true);
            int activeScope = blockedByTotal ? 0 : _draft.Routes.Keys.Where(value => value > 0).Min();
            Button manage = Button(content, _text.Get("query.map.route.manage"), width - 190, top + 76, 190,
                () => SetMapScope(activeScope), false, 38);
            manage.Name = "ManageMapRoute";
            manage.AddThemeFontSizeOverride("font_size", 15);
            return top + 132;
        }
        RouteCondition? active = _draft.Routes.GetValueOrDefault(scope);
        MapMetricKind[] metrics = RouteMetrics(scope);
        if (metrics.Length == 0) return top + 76;
        if (active is not null) _draft.RouteMetric = active.Metric;
        if (!metrics.Contains(_draft.RouteMetric)) _draft.RouteMetric = metrics[0];
        float nodeWidth = width * .45f;
        float valueLeft = nodeWidth + 12;
        float valueWidth = width - valueLeft - 54;
        Text(content, _text.Get("query.map.route.node_type"), 0, top + 76, nodeWidth, 14, true);
        Text(content, _text.Get("query.map.route.target"), valueLeft, top + 76, valueWidth, 14, true);
        float controlsTop = top + 104;
        OptionButton nodePicker = Options(content, metrics.Select(metric => NodeName(NodeKind(metric))).ToArray(),
            Array.IndexOf(metrics, _draft.RouteMetric), 0, controlsTop, nodeWidth, index =>
            {
                _draft.RouteMetric = metrics[index];
                // Changing the node is explicit; keep the authored threshold instead of replacing it with 1.
                if (active is not null) SetRoute(_draft.RouteMetric, active.Value);
                Render();
            });
        nodePicker.Name = "MapRouteNode";
        MapMetricOption[] routeOptions = RouteOptions(scope, _draft.RouteMetric);
        int selected = active is null ? 0 : Array.FindIndex(routeOptions,
            option => !option.IsAny && option.Value == active.Value);
        if (selected < 0 && active is not null)
        {
            routeOptions = routeOptions.Append(new MapMetricOption(false, MapMetricComparison.AtLeast, active.Value)).ToArray();
            selected = routeOptions.Length - 1;
        }
        OptionButton targetPicker = Options(content, routeOptions.Select(OptionLabel).ToArray(), Math.Max(0, selected), valueLeft, controlsTop, valueWidth, index =>
        {
            MapMetricOption option = routeOptions[index];
            if (option.IsAny) _draft.Routes.Remove(scope);
            else SetRoute(_draft.RouteMetric, option.Value);
            Render();
        });
        targetPicker.Name = "MapRouteTarget";
        Button remove = Button(content, "×", width - 40, controlsTop, 40,
            () => { _draft.Routes.Remove(scope); Render(); }, false, 38);
        remove.Name = "RemoveMapRoute";
        remove.Disabled = active is null;
        remove.TooltipText = _text.Format("common.remove_named", _text.Get("query.map.route"));
        return controlsTop + 52;
    }

    private void MapHint(Control content, string value, float x, float y, float width)
    {
        Label hint = Text(content, value, x, y, width, 14, true);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.Size = new(width, 42);
        hint.MaxLinesVisible = 2;
        hint.TooltipText = value;
    }

    private void LoadRouteEditor()
    {
        if (!_draft.Routes.TryGetValue(_draft.Scope, out RouteCondition? route)) return;
        _draft.RouteMetric = route.Metric;
        _draft.RouteValue = route.Value;
    }

    private void SetRoute(MapMetricKind metric, int value)
    {
        bool cleared;
        if (_draft.Scope == 0)
        {
            cleared = _draft.Routes.Keys.Any(scope => scope > 0);
            _draft.Routes.Remove(1); _draft.Routes.Remove(2); _draft.Routes.Remove(3);
        }
        else
        {
            cleared = _draft.Routes.Remove(0);
        }
        _draft.RouteMetric = metric;
        _draft.RouteValue = value;
        _draft.Routes[_draft.Scope] = new RouteCondition(metric, value);
        if (cleared) ShowToast(_text.Get("query.map.route.scope_toast"));
    }

    private List<PropertyCondition> PropertiesForScope(int scope)
    {
        if (!_draft.Properties.TryGetValue(scope, out List<PropertyCondition>? values))
            _draft.Properties[scope] = values = [];
        return values;
    }

    private void RevalidateMapCapabilities()
    {
        // Observed extrema only supply browsing suggestions, never legality or hard bounds.
        // Canonical validation owns supported scopes/metrics; do not discard a saved threshold
        // merely because a newer/older observational catalog does not list it.
        ResetPropertyBuilderSelection();
        LoadRouteEditor();
    }

    private void ResetPropertyBuilderSelection()
    {
        MapMetricKind[] metrics = PropertyMetrics(_draft.Scope);
        if (metrics.Length > 0) _draft.PropertyMetric = metrics[0];
        _draft.PropertyOptionIndex = 0;
    }

    private MapMetricDescriptor Descriptor(int scope, MapMetricKind metric) =>
        MapMetricCapabilityCatalog.Find(_draft.Players, _draft.Ascension, scope, metric)
        ?? throw new InvalidOperationException($"Missing map metric descriptor for players={_draft.Players}, scope={scope}, metric={metric}.");

    private MapMetricKind[] PropertyMetrics(int scope)
    {
        MapMetricKind[] ordered =
        [
            MapMetricKind.GuaranteedMonster, MapMetricKind.GuaranteedElite,
            MapMetricKind.GuaranteedRest, MapMetricKind.GuaranteedUnknown,
            MapMetricKind.ReachableMaxMonster, MapMetricKind.ReachableMaxElite,
            MapMetricKind.ReachableMaxRest, MapMetricKind.ReachableMaxUnknown,
            MapMetricKind.ForcedMonsterPrefix
        ];
        return ordered.Where(metric => MapMetricCapabilityCatalog.Find(_draft.Players, _draft.Ascension, scope, metric) is not null).ToArray();
    }

    private MapMetricKind[] RouteMetrics(int scope)
    {
        MapMetricKind[] ordered = [MapMetricKind.ReachableMaxElite, MapMetricKind.ReachableMaxRest,
            MapMetricKind.ReachableMaxUnknown];
        return ordered.Where(metric => MapMetricCapabilityCatalog.Find(_draft.Players, _draft.Ascension, scope, metric) is not null).ToArray();
    }

    private MapMetricOption[] RouteOptions(int scope, MapMetricKind metric) => Descriptor(scope, metric).Options
        .Where(option => option.IsAny || option.Comparison == MapMetricComparison.AtLeast)
        .ToArray();

    private string OptionLabel(MapMetricOption option) => option.IsAny
        ? _text.Get("query.map.option.any")
        : _text.Format(option.Comparison == MapMetricComparison.AtLeast
            ? "query.map.option.at_least"
            : "query.map.option.at_most", option.Value);

    private string PropertyLabel(MapMetricKind metric) => _text.Get(metric switch
    {
        MapMetricKind.GuaranteedMonster => "query.map.property.monster",
        MapMetricKind.GuaranteedElite => "query.map.property.elite",
        MapMetricKind.GuaranteedRest => "query.map.property.rest",
        MapMetricKind.GuaranteedUnknown => "query.map.property.unknown",
        MapMetricKind.ForcedMonsterPrefix => "query.map.property.prefix",
        MapMetricKind.ReachableMaxMonster => "query.map.property.max_monster",
        MapMetricKind.ReachableMaxElite => "query.map.property.max_elite",
        MapMetricKind.ReachableMaxRest => "query.map.property.max_rest",
        _ => "query.map.property.max_unknown"
    });

    private static int NodeKind(MapMetricKind metric) => metric switch
    {
        MapMetricKind.GuaranteedMonster or MapMetricKind.ReachableMaxMonster => 0,
        MapMetricKind.GuaranteedElite or MapMetricKind.ReachableMaxElite => 1,
        MapMetricKind.GuaranteedRest or MapMetricKind.ReachableMaxRest => 2,
        _ => 4
    };

    private string NodeName(int kind) => _text.Get(kind switch
    {
        0 => "query.map.node.monster", 1 => "query.map.node.elite", 2 => "query.map.node.rest",
        3 => "query.map.node.shop", _ => "query.map.node.unknown"
    });

    private void IdentityButton(Control parent, ModelKey key, GameContentKind kind, IconVariant variant,
        float x, float y, float width, bool selected, Action action)
    {
        Button button = Button(parent, string.Empty, x, y, width, action, selected, 50);
        button.TooltipText = _names.Resolve(key, kind);
        Texture2D? texture = _icons.Resolve(key, kind, variant).Texture;
        if (texture is not null)
            button.AddChild(new TextureRect
            {
                Position = new(6, 5), Size = new(40, 40), Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore
            });
        Label name = Text(button, button.TooltipText, 50, 10, width - 56, 16);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.MouseFilter = MouseFilterEnum.Ignore;
    }

    private void BossIdentityButton(Control parent, ModelKey key, float x, float y, float width, float height,
        bool selected, Action action)
    {
        Button button = Button(parent, string.Empty, x, y, width, action, selected, height);
        button.TooltipText = _names.Resolve(key, GameContentKind.Encounter);
        Label name = Text(button, button.TooltipText, 6, 44, width - 12, 15);
        name.Size = new(width - 12, height - 48);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.MaxLinesVisible = 2;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.MouseFilter = MouseFilterEnum.Ignore;
        Texture2D? texture = _icons.Resolve(key, GameContentKind.Encounter, IconVariant.WorldCompendiumBossIcon).Texture;
        if (texture is null) return;
        button.AddChild(new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = texture, Position = new((width - 36) / 2, 6), Size = new(36, 36),
            MouseFilter = MouseFilterEnum.Ignore
        });
    }

    private void ShowToast(string message)
    {
        _toastMessage = message;
        ulong revision = ++_toastRevision;
        SceneTreeTimer timer = GetTree().CreateTimer(2.5);
        timer.Timeout += () =>
        {
            if (revision != _toastRevision) return;
            _toastMessage = string.Empty;
            if (IsInsideTree()) Render();
        };
    }

    private void RenderToast()
    {
        var panel = new PanelContainer
        {
            Position = new(Math.Max(0, Size.X - 430), Math.Max(0, Size.Y - 66)),
            Size = new(410, 44),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = UiZLayers.ToastModal
        };
        panel.AddThemeStyleboxOverride("panel", _p.Box(_p.Surface, _p.Selected, 1));
        Label label = _p.Label(_toastMessage, 15);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        panel.AddChild(label);
        _body.AddChild(panel);
    }

    private Button Button(Control parent, string title, float x, float y, float width, Action action, bool selected, float height)
    {
        var button = _p.CompactButton(title, height, height < 32 ? 16 : 18, selected);
        button.Position = new(x, y);
        button.Size = new(width, height);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private OptionButton Options(Control parent, string[] values, int selected, float x, float y, float width, Action<int> change)
    {
        var option = new OptionButton
        {
            Position = new(x, y),
            Size = new(width, 38),
            FitToLongestItem = false,
            ClipText = true
        };
        foreach (string value in values) option.AddItem(value);
        option.Select(Math.Clamp(selected, 0, Math.Max(0, values.Length - 1)));
        option.AddThemeFontSizeOverride("font_size", 15);
        option.AddThemeColorOverride("font_color", _p.Color(_p.Text));
        option.AddThemeStyleboxOverride("normal", _p.Box(_p.Surface));
        option.AddThemeStyleboxOverride("hover", _p.Box(_p.Hover));
        option.AddThemeStyleboxOverride("pressed", _p.Box(_p.Line));
        option.AddThemeStyleboxOverride("focus", _p.FocusRing());
        option.ItemSelected += index => change((int)index);
        parent.AddChild(option);
        return option;
    }

    private Label Text(Control parent, string value, float x, float y, float width, int size, bool secondary = false)
    {
        var label = _p.Label(value, size, secondary);
        label.Position = new(x, y);
        label.Size = new(width, 28);
        label.ClipText = true;
        parent.AddChild(label);
        return label;
    }

    private void AddLine(Control parent, float x, float y, float width)
    {
        Color color = _p.Color(_p.Line); color.A *= .45f;
        parent.AddChild(new ColorRect { Position = new(x, y), Size = new(width, 1), Color = color, MouseFilter = MouseFilterEnum.Ignore });
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
}
