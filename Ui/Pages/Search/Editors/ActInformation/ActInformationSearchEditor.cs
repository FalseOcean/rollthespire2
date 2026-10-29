using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
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

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class ActInformationSearchEditor : Control
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("act-information-editor");
    private readonly SearchWorkspace _persistence;
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
        public HashSet<ModelKey>[] SelectedSecondBosses { get; } = NewIdentitySets();
        public bool[] IncludesSecondBoss { get; } = new bool[3];
        public bool MapMode = true;
        // Retained for existing workbench UI-state persistence; map properties are now always visible.
        public bool AdvancedProperties;
        public int Scope;
        public Dictionary<int, List<PropertyCondition>> Properties { get; } = [];
        public Dictionary<int, RouteCondition> Routes { get; } = [];
        public bool[] DrawingModes { get; } = new bool[4];
        public Dictionary<int, List<MapDrawingNodeSnapshot>> Drawings { get; } = [];
        public Dictionary<int, MapMetricKind> DailyMetrics { get; } = [];
    }

    public ActInformationSearchEditor(ModRuntimeSnapshot runtime, SearchWorkspace persistence)
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
        WorldAuthoritySnapshot? partyWorld = null, EditorRefreshContext? sharedContext = null)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        foreach (int removed in _drafts.Keys.Where(index => index >= players).ToArray()) _drafts.Remove(removed);
        int draftKey = players > 1 ? -1 : seat;
        if (!_drafts.TryGetValue(draftKey, out SeatDraft? draft)) _drafts[draftKey] = draft = new SeatDraft();
        _draft = draft;
        _draft.Players = players;
        _draft.Ascension = ascension;
        partyWorld = sharedContext?.Authority.WorldAuthority ?? partyWorld;
        string unlockKey = sharedContext?.Key ?? (players == 1
            ? System.Text.Json.JsonSerializer.Serialize(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks));
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
                    UnlockState resolved = sharedContext is not null ? UnlockState.FromSerializable(sharedContext.Unlocks) : players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    string seed = RolltheSpire2.Compatibility.ProfileSeedGenerator.CreateProbeSeed(_runtime.Profile);
                    WorldAuthoritySnapshot? world = partyWorld;
                    if (world is null && sharedContext is null)
                    {
                        var effects = ReflectionNeowEffectSnapshotAdapter.Capture(_runtime.Profile, seed,
                            CharacterIdentity.FromKey(character), ascension, players, seat, _runtime.Profile.ProfileId,
                            _runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), resolved);
                        world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(_runtime.Profile, seed,
                            CharacterIdentity.FromKey(character), ascension, players, seat, true, effects.EffectAuthority,
                            WorldGameMode.Unknown, gameVersion: _runtime.Detection.DisplayVersion, explicitUnlockState: resolved);
                    }
                    _draft.Catalog = BossMapSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, ascension, world);
                }
                RevalidateMapCapabilities();
            }
            catch (Exception exception)
            {
                _draft.Catalog = BossMapSearchUiCatalog.Empty(_runtime.Profile.ProfileId, "act-info-catalog-unavailable");
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception, "EditorCatalogCaptureFailed", "ActInformationEditorPrototype", "PickerCatalog", "issues.capture_failed", Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning);
            }
        }
        if (render) Render();
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
        const float inset = 16, choicesLeft = 120, tileWidth = 104, tileHeight = 76, tileGap = 8;
        int columns = Math.Max(1, (int)((width - choicesLeft - inset + tileGap) / (tileWidth + tileGap)));
        float y = top;
        for (int act = 1; act <= 3; act++)
        {
            int actIndex = act - 1;
            BossMapActSectionDefinition? section = Section(act);
            ModelKey[] variants = section?.Variants.Select(value => value.ActKey).Distinct(ModelKeyComparer.Instance).ToArray() ?? [];
            ModelKey[] bosses = BossesForAct(act).Concat(_draft.SelectedBosses[actIndex])
                .Distinct(ModelKeyComparer.Instance).ToArray();
            bool offersSecondBoss = section?.ShowSecondBoss == true;
            bool showSecondBoss = offersSecondBoss || _draft.IncludesSecondBoss[actIndex] ||
                _draft.SelectedSecondBosses[actIndex].Count > 0;
            bool secondPresenceOnly = _draft.IncludesSecondBoss[actIndex] && _draft.SelectedSecondBosses[actIndex].Count == 0;
            float secondHeader = secondPresenceOnly || !offersSecondBoss ? 34 : 0;
            ModelKey[] secondBosses = (offersSecondBoss ? BossesForAct(act) : [])
                .Concat(_draft.SelectedSecondBosses[actIndex]).Distinct(ModelKeyComparer.Instance).ToArray();
            const float variantWidth = 208, variantLeft = choicesLeft + 112;
            int variantColumns = Math.Max(1, (int)((width - variantLeft - inset + 12) / (variantWidth + 12)));
            int variantRows = Math.Max(1, (variants.Length + variantColumns - 1) / variantColumns);
            float bossTop = y + (variants.Length == 1 ? 44 : 10 + variantRows * 44);
            int bossRows = Math.Max(1, (bosses.Length + columns - 1) / columns);
            float sectionHeight = bossTop - y + bossRows * (tileHeight + tileGap) + 2;
            float secondTop = y + sectionHeight + 8;
            if (showSecondBoss)
                sectionHeight += secondHeader + 8 + Math.Max(1, (secondBosses.Length + columns - 1) / columns) * (tileHeight + tileGap);
            var sectionPanel = new Panel { Name = "BossConditionGroup" + act, Position = new(0, y),
                Size = new(width, sectionHeight), MouseFilter = MouseFilterEnum.Ignore };
            sectionPanel.AddThemeStyleboxOverride("panel", _p.ContentBox());
            content.AddChild(sectionPanel);
            Text(content, _text.Format("query.act_info.act", act), inset, y + 10, 92, 20);
            Text(content, _text.Get("query.act_info.variant"), choicesLeft, y + 12, 100, 15, true);
            HashSet<ModelKey> selectedVariants = _draft.SelectedVariants[actIndex];
            if (variants.Length == 1)
                Text(content, _names.Resolve(variants[0], GameContentKind.Act), variantLeft, y + 12, width - variantLeft - inset, 16);
            else
                for (int index = 0; index < variants.Length; index++)
                {
                    ModelKey key = variants[index];
                    IdentityButton(content, key, GameContentKind.Act, IconVariant.Small,
                        variantLeft + index % variantColumns * (variantWidth + 12), y + 6 + index / variantColumns * 44, variantWidth,
                        selectedVariants.Contains(key), () =>
                        {
                            ToggleSelected(selectedVariants, key);
                            if (selectedVariants.Count == 0 && _draft.SelectedSecondBosses[actIndex].Count == 0)
                                _draft.IncludesSecondBoss[actIndex] = false;
                            Render();
                        });
                }
            Text(content, _text.Get(showSecondBoss ? "query.act_info.boss.first" : "query.act_info.boss"), inset, bossTop + 14, 92, showSecondBoss ? 14 : 16, true);
            HashSet<ModelKey> selectedBosses = _draft.SelectedBosses[actIndex];
            for (int index = 0; index < bosses.Length; index++)
            {
                ModelKey key = bosses[index];
                BossIdentityButton(content, key, choicesLeft + index % columns * (tileWidth + tileGap),
                    bossTop + index / columns * (tileHeight + tileGap), tileWidth, tileHeight, selectedBosses.Contains(key),
                    () => { ToggleSelected(selectedBosses, key); Render(); }).Name = $"FirstBoss{act}_{index}";
            }
            if (showSecondBoss)
            {
                Text(content, _text.Get("query.act_info.boss.second"), inset, secondTop + secondHeader + 14, 92, 14, true);
                if (secondPresenceOnly)
                {
                    // Older branches may require a second Boss without naming
                    // one. Keep that declaration visible and removable; an empty
                    // unscoped ordinal cannot represent the same requirement.
                    var required = new CheckButton
                    {
                        Name = "RequireSecondBoss" + act,
                        Text = _text.Get(offersSecondBoss ? "query.act_info.boss.second_required" : "query.act_info.boss.second_retained"),
                        Position = new(choicesLeft, secondTop), Size = new(width - choicesLeft - inset, 30), ButtonPressed = true
                    };
                    required.Toggled += enabled =>
                    {
                        _draft.IncludesSecondBoss[actIndex] = enabled;
                        Render();
                    };
                    content.AddChild(required);
                }
                else if (!offersSecondBoss)
                {
                    Text(content, _text.Get("query.act_info.boss.second_retained"), choicesLeft, secondTop,
                        width - choicesLeft - inset, 14, true);
                }
                HashSet<ModelKey> selectedSecond = _draft.SelectedSecondBosses[actIndex];
                for (int index = 0; index < secondBosses.Length; index++)
                {
                    ModelKey key = secondBosses[index];
                    BossIdentityButton(content, key, choicesLeft + index % columns * (tileWidth + tileGap),
                        secondTop + secondHeader + index / columns * (tileHeight + tileGap), tileWidth, tileHeight,
                        selectedSecond.Contains(key), () =>
                        {
                            ToggleSelected(selectedSecond, key);
                            _draft.IncludesSecondBoss[actIndex] = selectedSecond.Count > 0;
                            Render();
                        }, showTooltip: false).Name = $"SecondBoss{act}_{index}";
                }
            }
            y += sectionHeight + 8;
        }
        return y - 8;
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

    private void IdentityButton(Control parent, ModelKey key, GameContentKind kind, IconVariant variant,
        float x, float y, float width, bool selected, Action action)
    {
        Button button = Button(parent, string.Empty, x, y, width, action, selected, 38);
        _p.ApplyCandidateSurface(button, selected);
        SearchTooltip.Set(button, _names.Resolve(key, kind));
        Texture2D? texture = _icons.Resolve(key, kind, variant).Texture;
        if (texture is not null)
            button.AddChild(new TextureRect
            {
                Position = new(6, 4), Size = new(30, 30), Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore
            });
        Label name = Text(button, _names.Resolve(key, kind), 42, 5, width - 48, 16);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.MouseFilter = MouseFilterEnum.Ignore;
    }

    private Button BossIdentityButton(Control parent, ModelKey key, float x, float y, float width, float height,
        bool selected, Action action, bool showTooltip = true)
    {
        Button button = Button(parent, string.Empty, x, y, width, action, selected, height);
        _p.ApplyCandidateSurface(button, selected);
        if (showTooltip) SearchTooltip.Set(button, _names.Resolve(key, GameContentKind.Encounter));
        Label name = Text(button, _names.Resolve(key, GameContentKind.Encounter), 6, 36, width - 12, 15);
        name.Size = new(width - 12, height - 38);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.MaxLinesVisible = 2;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.MouseFilter = MouseFilterEnum.Ignore;
        Texture2D? texture = _icons.Resolve(key, GameContentKind.Encounter, IconVariant.WorldCompendiumBossIcon).Texture;
        if (texture is null) return button;
        button.AddChild(new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = texture, Position = new((width - 30) / 2, 4), Size = new(30, 30),
            MouseFilter = MouseFilterEnum.Ignore
        });
        return button;
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
        panel.AddThemeStyleboxOverride("panel", _p.ContentBox());
        Label label = _p.Label(_toastMessage, 15);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        panel.AddChild(label);
        _body.AddChild(panel);
    }

    private Button ModeButton(Control parent, string title, float x, float y, float width, Action action, bool active, float height = 40)
    {
        var button = Button(parent, title, x, y, width, action, false, height);
        _p.SetModeActive(button, active);
        return button;
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
        parent.AddChild(new ColorRect { Position = new(x + 8, y), Size = new(Math.Max(1, width - 16), 1), Color = color, MouseFilter = MouseFilterEnum.Ignore });
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
}
