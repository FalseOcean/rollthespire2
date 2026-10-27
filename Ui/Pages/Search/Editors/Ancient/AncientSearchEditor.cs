using RolltheSpire2.Ui.Shell;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Ancient;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class AncientSearchEditor : Control
{
    private readonly SearchEditorLayout _presentation = new();
    private readonly ModRuntimeSnapshot _runtime;
    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("ancient-prototype");
    private readonly Control _body = new();
    private readonly AnchoredTooltipHost _eligibilityTooltip = new(new RuntimeRelicTooltipResolver(),
        new RuntimePotionTooltipResolver(), new RuntimeCardTooltipResolver());
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private readonly Dictionary<int, SeatDraft> _partyDrafts = [];
    private readonly Dictionary<int, List<ModelKey>>[] _partyIdentities =
    [
        new() { [2] = [], [3] = [] },
        new() { [2] = [], [3] = [] }
    ];
    private bool _partyAdvanced;
    private SeatDraft _draft = new();
    private bool _multiplayer;
    private int _seat;
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private bool _guideExpanded;
    private List<ModelKey> SharedAncients(int act) => _partyIdentities[_partyAdvanced ? 1 : 0][act];

    private sealed class AncientDraft(ModelKey ancient)
    {
        public ModelKey Ancient = ancient;
        public List<ModelKey> Options { get; } = [];
        public ModelKey? SeaGlassTarget;
    }

    private sealed class ModeDraft
    {
        public bool NeowEnabled;
        public int SelectedAct = 1;
        public List<ModelKey> NeowOptions { get; } = [];
        public Dictionary<int, List<AncientDraft>> Acts { get; } = new() { [2] = [], [3] = [] };
    }

    private sealed class SeatDraft
    {
        public string Context = string.Empty;
        public string Problem = string.Empty;
        public AncientSearchUiCatalog? Catalog;
        public NeowSearchUiCatalog? NeowCatalog;
        public int Mode;
        public int LastAdvancedMode = 2;
        public ModeDraft[] Modes { get; } = [new(), new(), new()];
        public ModeDraft Current => Modes[Mode];
        public bool Advanced
        {
            get => Mode != 0;
            set
            {
                if (value) Mode = LastAdvancedMode;
                else if (Mode != 0) { LastAdvancedMode = Mode; Mode = 0; }
            }
        }
        public bool OfferedTogether => Mode == 2;
        public bool RequireAll => Mode == 2;
        public bool EligibilityExpanded;
        public bool AdvancedExpanded;
        public bool NeowEnabled { get => Current.NeowEnabled; set => Current.NeowEnabled = value; }
        public int SelectedAct { get => Current.SelectedAct; set => Current.SelectedAct = value; }
        public AncientOptionConditionProfile Eligibility = AncientOptionConditionProfile.BroadDefault;
        public List<ModelKey> NeowOptions => Current.NeowOptions;
        public Dictionary<int, List<AncientDraft>> Acts => Current.Acts;
    }

    public AncientSearchEditor(ModRuntimeSnapshot runtime)
    {
        _runtime = runtime;
        AddChild(_body);
        AddChild(_eligibilityTooltip);
        VisibilityChanged += () => { if (!IsVisibleInTree()) DismissEligibilityTooltip(); };
    }

    public void AttachOverlay(Control shell)
    {
        DismissEligibilityTooltip();
        _eligibilityTooltip.Reparent(shell);
        _eligibilityTooltip.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _ExitTree() => DismissEligibilityTooltip();

    private void DismissEligibilityTooltip(Control? anchor = null)
    {
        if (GodotObject.IsInstanceValid(_eligibilityTooltip)) _eligibilityTooltip.Dismiss(anchor);
    }

    public event Action? GuideRequested;

    public AncientOptionConditionProfile OptionConditions => _draft.Eligibility;

    public void Refresh(string language, IUiTextProvider text, ModelKey character, int ascension,
        int players, int seat, SerializableUnlockState? unlocks = null, bool render = true,
        Core.Authority.RuntimeContextAuthoritySnapshot? partyAuthority = null, EditorRefreshContext? sharedContext = null)
    {
        DismissEligibilityTooltip();
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        _multiplayer = players > 1;
        _seat = seat;
        var drafts = players > 1 ? _partyDrafts : _drafts;
        foreach (int removedSeat in drafts.Keys.Where(index => index >= players).ToArray()) drafts.Remove(removedSeat);
        if (!drafts.TryGetValue(seat, out var draft)) drafts[seat] = draft = new SeatDraft();
        _draft = draft;
        if (_multiplayer) _draft.Advanced = _partyAdvanced;
        partyAuthority = sharedContext?.Authority ?? partyAuthority;
        string unlockKey = sharedContext?.Key ?? (players == 1
            ? System.Text.Json.JsonSerializer.Serialize(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks));
        string context = $"{character}/{ascension}/{players}/{seat}/{unlockKey}/{partyAuthority?.WorldSnapshotFingerprint}";
        if (_draft.Context != context)
        {
            _draft.Context = context;
            _draft.Catalog = null;
            _draft.NeowCatalog = null;
            _draft.Problem = string.Empty;
            try
            {
                if (players > 1 && unlocks is null)
                    _draft.Problem = _text.Get("query.ancient.context.read_unlocks_required");
                else
                {
                    UnlockState resolved = sharedContext is not null ? UnlockState.FromSerializable(sharedContext.Unlocks) : players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    if (partyAuthority is not null)
                    {
                        _draft.Catalog = AncientSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, character, partyAuthority.WorldAuthority);
                        _draft.NeowCatalog = NeowSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, character, partyAuthority.EffectAuthority);
                    }
                    else CaptureCatalogs(character, ascension, players, seat, resolved);
                    if (_draft.Catalog is null || _draft.Catalog.Sections.Count == 0)
                        _draft.Problem = _text.Get("query.ancient.context.catalog_unavailable");
                }
            }
            catch (Exception exception)
            {
                _draft.Problem = _text.Get("query.ancient.context.catalog_unavailable");
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception, "EditorCatalogCaptureFailed", "AncientEditorPrototype", "PickerCatalog", "issues.capture_failed", Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning);
            }
        }
        if (render) Render();
    }

    private void CaptureCatalogs(ModelKey character, int ascension, int players, int seat, UnlockState unlocks)
    {
        string seed = RolltheSpire2.Compatibility.ProfileSeedGenerator.CreateProbeSeed(_runtime.Profile);
        var effects = ReflectionNeowEffectSnapshotAdapter.Capture(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, _runtime.Profile.ProfileId,
            _runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), unlocks);
        var world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, noRunModifiers: true,
            effects.EffectAuthority, gameVersion: _runtime.Detection.DisplayVersion);
        _draft.Catalog = AncientSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, character, world);
        _draft.NeowCatalog = NeowSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, character, effects.EffectAuthority);
    }

    private void Render()
    {
        _presentation.Begin();
        DismissEligibilityTooltip();
        Clear(_body);
        _body.Size = Size;
        var guide = FamilyGuide(_body, Size.X - 20);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        void Layout()
        {
            float top = guide.Size.Y + 12;
            scroll.Position = new(0, top);
            scroll.Size = new(Size.X, Math.Max(0, Size.Y - top));
        }
        guide.Resized += Layout;
        Layout();
        _body.AddChild(scroll);
        var content = new Control { CustomMinimumSize = new(Size.X - 20, 1) };
        scroll.AddChild(content);
        float width = Size.X - 28;
        var settings = new Control { Name = "AncientMatchingSettings", Size = new(width, 1) };
        content.AddChild(settings);
        float eligibilityBottom = RenderEligibilityConfiguration(settings, 0);
        const float disclosureWidth = 112, modeGap = 8;
        var advanced = ModeButton(settings, _text.Get("query.ancient.advanced"), width - disclosureWidth - 8, 0, disclosureWidth,
            ToggleAdvancedDisclosure, _draft.AdvancedExpanded, 36);
        advanced.Name = "AncientAdvancedDisclosure";
        advanced.AddThemeFontSizeOverride("font_size", 14);
        float toolbarBottom = eligibilityBottom;
        if (_draft.AdvancedExpanded)
        {
            const float basicWidth = 144, anyWidth = 172, groupWidth = 148;
            float modeLeft = width - disclosureWidth - 16 - (basicWidth + anyWidth + groupWidth + modeGap * 2);
            float modeTop = 0;
            if (modeLeft < 188)
            {
                modeLeft = width - 8 - (basicWidth + anyWidth + groupWidth + modeGap * 2);
                modeTop = eligibilityBottom + 8;
            }
            var basic = ModeButton(settings, _text.Get("query.ancient.mode.default"), modeLeft, modeTop, basicWidth,
                () => SelectMode(0), _draft.Mode == 0, 36);
            basic.Name = "AncientModeBasic";
            var any = ModeButton(settings, _text.Get("query.ancient.mode.option_any"), modeLeft + basicWidth + modeGap, modeTop, anyWidth,
                () => SelectMode(1), _draft.Mode == 1, 36);
            any.Name = "AncientModeAny";
            var group = ModeButton(settings, _text.Get("query.ancient.mode.option_group"), modeLeft + basicWidth + anyWidth + modeGap * 2, modeTop, groupWidth,
                () => SelectMode(2), _draft.Mode == 2, 36);
            group.Name = "AncientModeGroup";
            group.Disabled = _multiplayer && _partyIdentities[1].Any(pair => pair.Value.Count > 1 &&
                _draft.Modes[2].Acts[pair.Key].Any(row => pair.Value.Contains(row.Ancient, ModelKeyComparer.Instance) && row.Options.Count > 0));
            if (group.Disabled) SearchTooltip.Set(group, _text.Get("query.ancient.party.group_requires_one"));
            basic.AddThemeFontSizeOverride("font_size", 14);
            any.AddThemeFontSizeOverride("font_size", 14);
            group.AddThemeFontSizeOverride("font_size", 14);
            toolbarBottom = Math.Max(toolbarBottom, modeTop + 36);
        }
        float hintTop = toolbarBottom + 8;
        string hintKey = _draft.Mode switch
        {
            1 => "query.ancient.mode.any_hint",
            2 => "query.ancient.mode.group_hint",
            _ => "query.ancient.mode.basic_hint"
        };
        var hint = Text(settings, _text.Get(hintKey), 8, hintTop, width - 16, 14, true);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.Size = new(width - 16, Math.Max(24, hint.GetMinimumSize().Y));
        settings.Size = new(width, hintTop + hint.Size.Y + 4);
        float editorTop = settings.Size.Y + 8;
        if (!string.IsNullOrWhiteSpace(_draft.Problem))
        {
            Text(content, _draft.Problem, 16, editorTop, width - 32, 15, true);
            editorTop += 40;
        }
        float bottom = _draft.Mode == 0
            ? RenderSimple(content, width, editorTop)
            : _draft.AdvancedExpanded
                ? RenderAdvanced(content, width, editorTop)
                : RenderAdvancedSummary(content, width, editorTop);
        content.CustomMinimumSize = new(width, bottom + 4);
        _presentation.Finish();
    }

    private float RenderEligibilityConfiguration(Control content, float top)
    {
        var toggle = ModeButton(content, _text.Get("query.ancient.eligibility.edit"), 8, top, 164,
            () => { _draft.EligibilityExpanded = !_draft.EligibilityExpanded; Render(); },
            _draft.EligibilityExpanded, 36);
        toggle.AddThemeFontSizeOverride("font_size", 14);
        SearchTooltip.Set(toggle, _text.Get("query.ancient.eligibility.hint"));
        if (!_draft.EligibilityExpanded) return top + 36;

        IReadOnlyList<ModelKey> options = AncientOptionConditionProfile.AuthoredOptionKeys;
        for (int index = 0; index < options.Count; index++)
        {
            ModelKey option = options[index];
            _draft.Eligibility.TryGetOptionEligibility(option, out bool eligible);
            Button choice = AddRelicChoice(content, option, index, 8, top + 44, options.Count, eligible,
                () => ToggleEligibility(option, eligible), dimmed: !eligible, queryCondition: false);
            SearchTooltip.Set(choice, string.Empty);
            choice.MouseEntered += () => _eligibilityTooltip.ShowStructuredText(choice,
                _names.Resolve(option, GameContentKind.Relic),
                _text.Get(eligible ? "query.ancient.eligibility.met" : "query.ancient.eligibility.not_met"),
                _text.Get("query.ancient.eligibility." + option.Entry));
            choice.MouseExited += () => DismissEligibilityTooltip(choice);
            choice.TreeExiting += () => DismissEligibilityTooltip(choice);
        }
        return top + 88;
    }

    private VBoxContainer FamilyGuide(Control host, float width)
    {
        string[] details = _multiplayer
            ? [_text.Get("query.ancient.guide.usage"), _text.Get("query.ancient.guide.multiplayer"),
                _text.Get("query.ancient.guide.premise")]
            : [_text.Get("query.ancient.guide.usage"), _text.Get("query.ancient.guide.premise")];
        return SearchEditorGuide.Build(host, _p, width,
            _text.Get("query.ancient.guide.title"), _text.Get("query.ancient.guide.summary"),
            details, _guideExpanded,
            () => { _guideExpanded = !_guideExpanded; Render(); },
            () => GuideRequested?.Invoke());
    }

    private void ToggleAdvancedDisclosure()
    {
        _draft.AdvancedExpanded = !_draft.AdvancedExpanded;
        Render();
    }

    private float RenderAdvancedSummary(Control content, float width, float top)
    {
        var panel = new Panel { Name = "AncientAdvancedSummary", Position = new(0, top), Size = new(width, 1) };
        panel.AddThemeStyleboxOverride("panel", _p.ContentBox());
        content.AddChild(panel);
        string mode = _text.Get(_draft.Mode == 2 ? "query.ancient.mode.option_group" : "query.ancient.mode.option_any");
        Text(panel, _text.Format("query.ancient.summary.title", mode), 16, 16, width - 260, 18);
        var edit = ModeButton(panel, _text.Get("query.ancient.summary.edit"), width - 232, 16, 216,
            () => { _draft.AdvancedExpanded = true; Render(); }, false, 36);
        edit.Name = "AncientSummaryEdit";
        edit.AddThemeFontSizeOverride("font_size", 14);
        float y = 64;
        bool any = false;
        if (_draft.NeowEnabled)
            AddRow(1, new ModelKey(BaseGameModelKeys.Categories.Event, "NEOW"), _draft.NeowOptions, null);
        foreach (int act in new[] { 2, 3 })
        {
            IEnumerable<ModelKey> identities = _multiplayer
                ? SharedAncients(act)
                : _draft.Acts[act].Select(row => row.Ancient);
            foreach (ModelKey ancient in identities)
            {
                AncientDraft? row = _draft.Acts[act].FirstOrDefault(item => item.Ancient == ancient);
                AddRow(act, ancient, row?.Options ?? [], row?.SeaGlassTarget);
            }
        }
        if (!any)
        {
            Text(panel, _text.Get("query.ancient.summary.empty"), 16, y, width - 32, 16, true);
            y += 32;
        }
        panel.Size = new(width, y + 16);
        return top + panel.Size.Y;

        void AddRow(int act, ModelKey ancient, IReadOnlyList<ModelKey> options, ModelKey? seaGlassTarget)
        {
            if (any) y += 12;
            any = true;
            Text(panel, _text.Format("query.ancient.act", act) + " · " + _names.Resolve(ancient, GameContentKind.Ancient),
                16, y, width - 32, 16);
            y += 32;
            string names = options.Count == 0 ? _text.Get("common.any")
                : string.Join(" · ", options.Select(key => _names.Resolve(key, GameContentKind.Relic)));
            var offered = Text(panel, _text.Format("query.ancient.summary.options", names), 16, y, width - 32, 15, true);
            offered.Name = "AncientSummaryOfferedOptions";
            offered.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            float height = Math.Max(28, offered.GetMinimumSize().Y);
            offered.Size = new(width - 32, height);
            y += height;
            if (seaGlassTarget is { } target)
            {
                Text(panel, _text.Format("query.ancient.summary.target", _names.Resolve(target, GameContentKind.Character)),
                    16, y, width - 32, 14, true);
                y += 28;
            }
        }
    }

    private float RenderSimple(Control content, float width, float top)
    {
        float y = top;
        foreach (int act in new[] { 2, 3 })
            y = RenderAct(content, act, width, y, false, 1, true);
        return y;
    }

    private float RenderAdvanced(Control content, float width, float top)
    {
        const float tabWidth = 120, tabGap = 16;
        for (int index = 0; index < 3; index++)
        {
            int act = index + 1;
            string title = _text.Format("query.ancient.act", act);
            ModeButton(content, title, 16 + index * (tabWidth + tabGap), top, tabWidth,
                () => { _draft.SelectedAct = act; Render(); }, _draft.SelectedAct == act, 32);
        }
        float editorTop = top + 40;
        _presentation.Section(content, top, editorTop);
        return _draft.SelectedAct == 1
            ? RenderNeowAct(content, width, editorTop, _draft.OfferedTogether ? 3 : int.MaxValue)
            : RenderAct(content, _draft.SelectedAct, width, editorTop, !_draft.OfferedTogether, _draft.OfferedTogether ? 3 : int.MaxValue, false);
    }

    private float RenderNeowAct(Control content, float width, float y, int optionLimit)
    {
        const float inset = 16;
        Text(content, _text.Get("query.ancient.act1.neow"), inset, y, width - inset * 2, 20);
        const float leftWidth = 104, gutter = 16;
        float rightLeft = inset + leftWidth + gutter;
        float rightWidth = width - rightLeft - inset;
        Text(content, _text.Get("layout.sources"), inset, y + 26, leftWidth, 13, true);
        Text(content, _text.Get("query.ancient.offered_options"), rightLeft, y + 26, rightWidth, 13, true);
        float rowTop = y + 48;
        float leftHeight = AddNeowIdentity(content, inset, rowTop);
        float rightHeight = _draft.NeowEnabled
            ? AddNeowStrip(content, rightLeft, rowTop, rightWidth, optionLimit)
            : 0;
        if (!_draft.NeowEnabled)
            Text(content, _text.Get("common.any"), rightLeft, rowTop + 14, rightWidth, 16, true);
        float rowHeight = Math.Max(leftHeight, Math.Max(62, rightHeight));
        float bottom = rowTop + rowHeight + 8;
        _presentation.Section(content, y, bottom);
        return bottom;
    }

    private float RenderAct(Control content, int act, float width, float y, bool multipleAncients,
        int optionLimit, bool simpleGrid)
    {
        const float inset = 16;
        float leftWidth = simpleGrid ? 228 : 196;
        const float gutter = 24;
        float rightLeft = inset + leftWidth + gutter;
        float rightWidth = width - rightLeft - inset;
        Text(content, _text.Format("query.ancient.act", act), inset, y, width - inset * 2, 20);
        Text(content, _text.Get(_multiplayer ? "query.ancient.party.identity" : "layout.sources"), inset, y + 26, leftWidth, 13, true);
        Text(content, _multiplayer ? _text.Format("query.ancient.party.options", _seat + 1) : _text.Get("query.ancient.offered_options"),
            rightLeft, y + 26, rightWidth, 13, true);
        float rowTop = y + 48;
        float leftHeight = AddAncientGrid(content, act, inset, rowTop, multipleAncients, simpleGrid);
        float rightHeight = 0;
        IEnumerable<AncientDraft> visibleRows = _multiplayer
            ? SharedAncients(act).Select(key => GetOrAddAncientRow(_draft.Current, act, key))
            : _draft.Acts[act];
        foreach (AncientDraft row in visibleRows)
        {
            if (rightHeight > 0)
            {
                rightHeight += 12;
            }
            rightHeight += AddOptionStrip(content, act, row, rightLeft, rowTop + rightHeight, rightWidth,
                optionLimit, simpleGrid);
        }
        if (rightHeight == 0)
            Text(content, _text.Get("common.any"), rightLeft, rowTop + 14, rightWidth, 16, true);
        float rowHeight = Math.Max(leftHeight, Math.Max(62, rightHeight));
        float bottom = rowTop + rowHeight + 8;
        _presentation.Section(content, y, bottom);
        return bottom;
    }

    private float AddAncientGrid(Control content, int act, float x, float y, bool multipleAncients,
        bool simpleGrid)
    {
        IReadOnlyList<ModelKey> candidates = AncientCandidates(act);
        float tileWidth = simpleGrid ? 104 : 88;
        const float tileHeight = 72;
        const float imageSize = 40, labelY = 46, imageY = 4;
        int fontSize = simpleGrid ? 13 : 12;
        const float columnGap = 8, rowGap = 8;
        for (int index = 0; index < candidates.Count; index++)
        {
            ModelKey key = candidates[index];
            bool selected = _multiplayer
                ? SharedAncients(act).Contains(key, ModelKeyComparer.Instance)
                : _draft.Acts[act].Any(row => row.Ancient == key);
            float itemX = x + index % 2 * (tileWidth + columnGap);
            float itemY = y + index / 2 * (tileHeight + rowGap);
            var button = Button(content, string.Empty, itemX, itemY, tileWidth,
                () => ToggleAncient(act, key, multipleAncients), selected, tileHeight);
            _p.ApplyCandidateSurface(button, selected);
            var label = Text(button, _names.Resolve(key, GameContentKind.Ancient), 4, labelY, tileWidth - 8, fontSize);
            label.Size = new(tileWidth - 8, 24);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.MouseFilter = MouseFilterEnum.Ignore;
            AddPicture(button, key, GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon,
                (tileWidth - imageSize) / 2, imageY, imageSize);
        }
        int rows = Math.Max(1, (int)Math.Ceiling(candidates.Count / 2d));
        return rows * (tileHeight + rowGap) - rowGap;
    }

    private float AddOptionStrip(Control content, int act, AncientDraft row, float x, float y, float width,
        int optionLimit, bool simpleGrid)
    {
        float height = AddOptionGrid(content, act, row, x, y, width, optionLimit, simpleGrid);
        if (row.Options.Any(IsSeaGlass))
            height += 8 + AddSeaGlassTargetSelector(content, row, x, y + height + 8, width);
        return height;
    }

    private float AddOptionGrid(Control content, int act, AncientDraft row, float x, float y, float width,
        int optionLimit, bool simpleGrid)
    {
        IReadOnlyList<ModelKey> candidates = OptionCandidates(act, row.Ancient);
        const float columnPitch = 152, rowPitch = 56;
        int columns = Math.Max(1, (int)((width + 8) / columnPitch));
        for (int index = 0; index < candidates.Count; index++)
        {
            ModelKey key = candidates[index];
            bool selected = row.Options.Contains(key, ModelKeyComparer.Instance);
            bool eligible = IsOptionEligible(key);
            bool disabled = !selected && (!eligible || row.Options.Count >= optionLimit ||
                _draft.Advanced && _draft.OfferedTogether && (!CanOfferTogether(act, row, key) ||
                    _multiplayer && SharedAncients(act).Count > 1));
            AddRelicChoice(content, key, index, x, y, columns, selected,
                () => ToggleOption(act, row, key, optionLimit),
                disabled, simpleGrid, true, !eligible);
        }
        int rows = Math.Max(1, (int)Math.Ceiling(candidates.Count / (double)columns));
        return rows * rowPitch - 8;
    }

    private float AddNeowIdentity(Control content, float x, float y)
    {
        ModelKey key = new(BaseGameModelKeys.Categories.Event, "NEOW");
        var button = ModeButton(content, string.Empty, x, y, 88, ToggleNeow, _draft.NeowEnabled, 72);
        _p.ApplyCandidateSurface(button, _draft.NeowEnabled);
        var label = Text(button, _names.Resolve(key, GameContentKind.Ancient), 4, 46, 80, 12);
        label.Size = new(80, 24);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.MouseFilter = MouseFilterEnum.Ignore;
        AddPicture(button, key, GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon, 24, 4, 40);
        return 72;
    }

    private float AddNeowStrip(Control content, float x, float y, float width, int optionLimit)
    {
        IReadOnlyList<ModelKey> candidates = NeowOptionCandidates();
        const float columnPitch = 152, rowPitch = 56;
        int columns = Math.Max(1, (int)((width + 8) / columnPitch));
        for (int index = 0; index < candidates.Count; index++)
        {
            ModelKey key = candidates[index];
            bool selected = _draft.NeowOptions.Contains(key, ModelKeyComparer.Instance);
            bool disabled = !selected && (_draft.NeowOptions.Count >= optionLimit ||
                _draft.OfferedTogether && !CanNeowOfferTogether(_draft.NeowOptions.Append(key)));
            AddRelicChoice(content, key, index, x, y, columns, selected, () =>
            {
                if (selected) _draft.NeowOptions.Remove(key);
                else if (_draft.NeowOptions.Count < optionLimit &&
                         (!_draft.OfferedTogether || CanNeowOfferTogether(_draft.NeowOptions.Append(key))))
                    _draft.NeowOptions.Add(key);
                Render();
            }, disabled, comfortable: true);
        }
        int rows = Math.Max(1, (int)Math.Ceiling(candidates.Count / (double)columns));
        return rows * rowPitch - 8;
    }

    private Button AddRelicChoice(Control content, ModelKey key, int index, float x, float y, int columns,
        bool selected, Action action, bool disabled = false, bool large = false, bool comfortable = false,
        bool dimmed = false, bool queryCondition = true)
    {
        float width = large || comfortable ? 144 : 44;
        float height = large || comfortable ? 48 : 44;
        const float gap = 8;
        float imageSize = large || comfortable ? 32 : 28;
        var button = queryCondition
            ? Button(content, string.Empty, x + index % columns * (width + gap), y + index / columns * (height + gap), width, action, selected, height)
            : ModeButton(content, string.Empty, x + index % columns * (width + gap), y + index / columns * (height + gap), width, action, selected, height);
        button.Disabled = disabled;
        if (queryCondition) _p.ApplyCandidateSurface(button, selected);
        SearchTooltip.Set(button, _names.Resolve(key, GameContentKind.Relic));
        button.Modulate = dimmed ? new Color(1f, 1f, 1f, .38f) : Colors.White;
        if (large || comfortable)
        {
            var label = Text(button, _names.Resolve(key, GameContentKind.Relic), 46, 5, width - 52, 12);
            label.Size = new(width - 52, height - 10);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.MouseFilter = MouseFilterEnum.Ignore;
            AddPicture(button, key, GameContentKind.Relic, IconVariant.RelicLarge,
                6, (height - imageSize) / 2, imageSize);
        }
        else
        {
            float imageInset = (width - imageSize) / 2;
            AddPicture(button, key, GameContentKind.Relic, IconVariant.RelicLarge, imageInset, imageInset, imageSize);
        }
        return button;
    }

    private float AddSeaGlassTargetSelector(Control content, AncientDraft row, float x, float y, float width)
    {
        Text(content, _text.Get("query.ancient.sea_glass.target_character"), x, y, width, 13);
        IReadOnlyList<ModelKey> candidates = SeaGlassTargetCandidates();
        const float tileWidth = 72, tileHeight = 58, gap = 8;
        for (int index = 0; index < candidates.Count; index++)
        {
            ModelKey key = candidates[index];
            bool selected = row.SeaGlassTarget == key;
            var button = Button(content, string.Empty, x + index * (tileWidth + gap), y + 22, tileWidth,
                () =>
                {
                    row.SeaGlassTarget = selected ? null : key;
                    Render();
                }, selected, tileHeight);
            var label = Text(button, _names.Resolve(key, GameContentKind.Character), 3, 2, tileWidth - 6, 11);
            label.Size = new(tileWidth - 6, 17);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.MouseFilter = MouseFilterEnum.Ignore;
            AddPicture(button, key, GameContentKind.Character, IconVariant.CharacterPortrait, 20, 21, 32);
        }
        return 22 + tileHeight;
    }

    private void ToggleAncient(int act, ModelKey key, bool multipleAncients)
    {
        if (_multiplayer)
        {
            List<ModelKey> identities = SharedAncients(act);
            if (identities.Contains(key, ModelKeyComparer.Instance)) identities.Remove(key);
            else if (multipleAncients)
            {
                if (_partyDrafts.Values.Any(draft => draft.Mode == 2 && draft.Modes[2].Acts[act]
                    .Any(row => (identities.Contains(row.Ancient, ModelKeyComparer.Instance) || row.Ancient == key) &&
                        row.Options.Count > 0)))
                    return;
                identities.Add(key);
            }
            else { identities.Clear(); identities.Add(key); }
            GetOrAddAncientRow(_draft.Current, act, key);
            Render();
            return;
        }
        List<AncientDraft> rows = _draft.Acts[act];
        int index = rows.FindIndex(row => row.Ancient == key);
        if (multipleAncients)
        {
            if (index >= 0) rows.RemoveAt(index); else rows.Add(new AncientDraft(key));
        }
        else
        {
            if (index >= 0) rows.Clear();
            else { rows.Clear(); rows.Add(new AncientDraft(key)); }
        }
        Render();
    }

    private static AncientDraft GetOrAddAncientRow(ModeDraft mode, int act, ModelKey key)
    {
        List<AncientDraft> rows = mode.Acts[act];
        AncientDraft? row = rows.FirstOrDefault(item => item.Ancient == key);
        if (row is not null) return row;
        row = new AncientDraft(key);
        rows.Add(row);
        return row;
    }

    private void ToggleOption(int act, AncientDraft row, ModelKey key, int optionLimit)
    {
        bool selected = row.Options.Contains(key, ModelKeyComparer.Instance);
        if (!selected && !IsOptionEligible(key)) return;
        if (!selected && _multiplayer && _draft.OfferedTogether && SharedAncients(act).Count > 1) return;
        if (!selected && _draft.Advanced && _draft.OfferedTogether && !CanOfferTogether(act, row, key)) return;
        if (optionLimit > 1)
        {
            if (selected) row.Options.Remove(key);
            else if (row.Options.Count < optionLimit) row.Options.Add(key);
        }
        else
        {
            row.Options.Clear();
            if (!selected) row.Options.Add(key);
        }
        if (!row.Options.Any(IsSeaGlass)) row.SeaGlassTarget = null;
        Render();
    }

    private void ToggleEligibility(ModelKey option, bool currentlyEligible)
    {
        _draft.Eligibility = _draft.Eligibility.WithOptionEligibility(option, !currentlyEligible);
        if (currentlyEligible)
            foreach (ModeDraft mode in _draft.Modes)
            foreach (List<AncientDraft> rows in mode.Acts.Values)
            foreach (AncientDraft row in rows)
            {
                row.Options.RemoveAll(key => key == option);
                if (!row.Options.Any(IsSeaGlass)) row.SeaGlassTarget = null;
            }
        if (_draft.Catalog is not null && _draft.NeowCatalog is not null)
            RevalidateOfferedSelections(_draft.Modes[2]);
        Render();
    }

    private void ToggleNeow()
    {
        _draft.NeowEnabled = !_draft.NeowEnabled;
        if (!_draft.NeowEnabled) _draft.NeowOptions.Clear();
        Render();
    }

    private void SelectMode(int mode)
    {
        if (_multiplayer)
        {
            if (mode == 0)
            {
                _partyAdvanced = false;
                foreach (SeatDraft draft in _partyDrafts.Values) draft.Advanced = false;
            }
            else
            {
                if (!_partyAdvanced)
                {
                    _partyAdvanced = true;
                    foreach (SeatDraft draft in _partyDrafts.Values) draft.Advanced = true;
                }
                _draft.Mode = mode;
                _draft.LastAdvancedMode = mode;
            }
            Render();
            return;
        }
        if (_draft.Mode == mode) return;
        _draft.Mode = mode;
        if (mode != 0) _draft.LastAdvancedMode = mode;
        Render();
    }

    private bool CanOfferTogether(int act, AncientDraft row, ModelKey candidate)
    {
        if (!IsOptionEligible(candidate) || row.Options.Any(option => !IsOptionEligible(option))) return false;
        AncientRowDefinition? definition = _draft.Catalog?.Sections
            .FirstOrDefault(section => section.Act == act)?.Rows
            .FirstOrDefault(item => item.AncientKey == row.Ancient);
        return definition is not null && definition.CanOfferTogether(row.Options.Append(candidate));
    }

    private bool IsOptionEligible(ModelKey option) =>
        !_draft.Eligibility.TryGetOptionEligibility(option, out bool eligible) || eligible;

    private bool CanNeowOfferTogether(IEnumerable<ModelKey> targets) => _runtime.Profile.ProfileId switch
    {
        RuntimeProfileId.Stable107 => Stable107NeowIdentityAnalyzer.CanCoOffer(targets),
        RuntimeProfileId.Beta109 or RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 =>
            ModernNeowIdentityPredictor.CanCoOffer(targets),
        _ => false
    };

    private void RevalidateOfferedSelections(ModeDraft mode)
    {
        var retainedNeow = new List<ModelKey>(3);
        foreach (ModelKey option in mode.NeowOptions)
            if (retainedNeow.Count < 3 && CanNeowOfferTogether(retainedNeow.Append(option))) retainedNeow.Add(option);
        mode.NeowOptions.Clear();
        mode.NeowOptions.AddRange(retainedNeow);

        foreach ((int act, List<AncientDraft> rows) in mode.Acts)
        foreach (AncientDraft row in rows)
        {
            AncientRowDefinition? definition = _draft.Catalog?.Sections
                .FirstOrDefault(section => section.Act == act)?.Rows
                .FirstOrDefault(item => item.AncientKey == row.Ancient);
            var retained = new List<ModelKey>(3);
            if (definition is not null)
                foreach (ModelKey option in row.Options)
                    if (retained.Count < 3 && definition.CanOfferTogether(retained.Append(option))) retained.Add(option);
            row.Options.Clear();
            row.Options.AddRange(retained);
            if (!row.Options.Any(IsSeaGlass)) row.SeaGlassTarget = null;
        }
    }

    private IReadOnlyList<ModelKey> AncientCandidates(int act) => _draft.Catalog?.Sections
        .FirstOrDefault(section => section.Act == act)?.Rows
        .Where(row => row.IdentityAvailable).Select(row => row.AncientKey).Distinct(ModelKeyComparer.Instance).ToArray()
        ?? Array.Empty<ModelKey>();

    private IReadOnlyList<ModelKey> OptionCandidates(int act, ModelKey ancient) => _draft.Catalog?.Sections
        .FirstOrDefault(section => section.Act == act)?.Rows.FirstOrDefault(row => row.AncientKey == ancient)?.Options
        .Where(option => option.IsAvailable).Select(option => option.OptionKey).Distinct(ModelKeyComparer.Instance).ToArray()
        ?? Array.Empty<ModelKey>();

    private IReadOnlyList<ModelKey> NeowOptionCandidates() => _draft.NeowCatalog?.RouteRelics
        .Distinct(ModelKeyComparer.Instance).ToArray() ?? Array.Empty<ModelKey>();

    private IReadOnlyList<ModelKey> SeaGlassTargetCandidates() => _draft.Catalog?.SeaGlassTargetCharacters
        .Where(key => key.IsValid && key.Category == BaseGameModelKeys.Categories.Character)
        .Distinct(ModelKeyComparer.Instance).ToArray() ?? Array.Empty<ModelKey>();

    private static bool IsSeaGlass(ModelKey key) =>
        string.Equals(key.Entry, "SEA_GLASS", StringComparison.Ordinal);

    private void AddPicture(Control parent, ModelKey key, GameContentKind kind, IconVariant variant,
        float x, float y, float size)
    {
        Texture2D? texture = _icons.Resolve(key, kind, variant).Texture;
        if (texture is null) return;
        parent.AddChild(new TextureRect
        {
            Position = new(x, y), Size = new(size, size), Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        });
    }

    private Button ModeButton(Control parent, string title, float x, float y, float width, Action action, bool active, float height = 40)
    {
        var button = Button(parent, title, x, y, width, action, false, height);
        _p.SetModeActive(button, active);
        return button;
    }

    private Button Button(Control parent, string title, float x, float y, float width, Action action,
        bool selected, float height = 40)
    {
        var button = _p.CompactButton(title, height, height < 32 ? 16 : 18, selected);
        button.Position = new(x, y);
        button.Size = new(width, height);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
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

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
}
