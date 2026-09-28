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

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class AncientEditorPrototype : Control
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly WorkspacePalette _p = WorkspacePalette.Canonical;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("ancient-prototype");
    private readonly Control _body = new();
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
        public bool NeowEnabled { get => Current.NeowEnabled; set => Current.NeowEnabled = value; }
        public int SelectedAct { get => Current.SelectedAct; set => Current.SelectedAct = value; }
        public AncientOptionConditionProfile Eligibility = AncientOptionConditionProfile.BroadDefault;
        public List<ModelKey> NeowOptions => Current.NeowOptions;
        public Dictionary<int, List<AncientDraft>> Acts => Current.Acts;
    }

    public AncientEditorPrototype(ModRuntimeSnapshot runtime)
    {
        _runtime = runtime;
        AddChild(_body);
    }

    public event Action? GuideRequested;

    public AncientOptionConditionProfile OptionConditions => _draft.Eligibility;

    public void Refresh(string language, IUiTextProvider text, ModelKey character, int ascension,
        int players, int seat, SerializableUnlockState? unlocks = null, bool render = true,
        Core.Authority.RuntimeContextAuthoritySnapshot? partyAuthority = null)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        _multiplayer = players > 1;
        _seat = seat;
        var drafts = players > 1 ? _partyDrafts : _drafts;
        foreach (int removedSeat in drafts.Keys.Where(index => index >= players).ToArray()) drafts.Remove(removedSeat);
        if (!drafts.TryGetValue(seat, out var draft)) drafts[seat] = draft = new SeatDraft();
        _draft = draft;
        if (_multiplayer) _draft.Advanced = _partyAdvanced;
        string unlockKey = players == 1
            ? System.Text.Json.JsonSerializer.Serialize(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks);
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
                    UnlockState resolved = players == 1
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
                RuntimeLog.Warn("ancientPrototypeCatalog=" + exception.Message);
            }
            Revalidate();
        }
        if (render) Render();
    }

    private void CaptureCatalogs(ModelKey character, int ascension, int players, int seat, UnlockState unlocks)
    {
        string seed = new(_runtime.Profile.SeedAlphabet[0], _runtime.Profile.SeedLength);
        var effects = ReflectionNeowEffectSnapshotAdapter.Capture(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, _runtime.Profile.ProfileId,
            _runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), unlocks);
        var world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, noRunModifiers: true,
            effects.EffectAuthority, gameVersion: _runtime.Detection.DisplayVersion);
        _draft.Catalog = AncientSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, character, world);
        _draft.NeowCatalog = NeowSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, character, effects.EffectAuthority);
    }

    private void Revalidate()
    {
        if (_draft.Catalog is not null)
            foreach (ModeDraft mode in _draft.Modes)
            foreach ((int act, List<AncientDraft> rows) in mode.Acts)
            {
                HashSet<ModelKey> ancients = AncientCandidates(act).ToHashSet(ModelKeyComparer.Instance);
                rows.RemoveAll(row => !ancients.Contains(row.Ancient));
                foreach (AncientDraft row in rows)
                {
                    HashSet<ModelKey> options = OptionCandidates(act, row.Ancient).ToHashSet(ModelKeyComparer.Instance);
                    row.Options.RemoveAll(option => !options.Contains(option));
                    row.Options.RemoveAll(option => !IsOptionEligible(option));
                    if (!row.Options.Any(IsSeaGlass) ||
                        row.SeaGlassTarget is { } target && !SeaGlassTargetCandidates().Contains(target, ModelKeyComparer.Instance))
                        row.SeaGlassTarget = null;
                }
            }
        if (_draft.NeowCatalog is not null)
            foreach (ModeDraft mode in _draft.Modes)
                mode.NeowOptions.RemoveAll(option => !_draft.NeowCatalog.RouteRelics.Contains(option, ModelKeyComparer.Instance));
        if (_draft.Catalog is not null && _draft.NeowCatalog is not null)
            RevalidateOfferedSelections(_draft.Modes[2]);
    }

    private void Render()
    {
        Clear(_body);
        _body.Size = Size;
        var guide = FamilyGuide(_body, Size.X - 20);
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
        var content = new Control { CustomMinimumSize = new(Size.X - 20, 1) };
        scroll.AddChild(content);
        float width = Size.X - 28;
        float sharedBottom = 4;
        float controlsTop = sharedBottom;
        float eligibilityBottom = RenderEligibilityConfiguration(content, controlsTop);
        var advanced = Button(content, _text.Get("query.ancient.advanced"), width - 140, controlsTop, 140,
            () => SelectMode(_draft.Advanced ? 0 : _draft.LastAdvancedMode), _draft.Advanced, 36);
        advanced.AddThemeFontSizeOverride("font_size", 14);
        if (_draft.Advanced)
        {
            var any = Button(content, _text.Get("query.ancient.mode.option_any"), 176, controlsTop, 196,
                () => SelectMode(1), _draft.Mode == 1, 36);
            var group = Button(content, _text.Get("query.ancient.mode.option_group"), 384, controlsTop, 220,
                () => SelectMode(2), _draft.Mode == 2, 36);
            group.Disabled = _multiplayer && _partyIdentities[1].Any(pair => pair.Value.Count > 1 &&
                _draft.Modes[2].Acts[pair.Key].Any(row => pair.Value.Contains(row.Ancient, ModelKeyComparer.Instance) && row.Options.Count > 0));
            if (group.Disabled) group.TooltipText = _text.Get("query.ancient.party.group_requires_one");
            any.AddThemeFontSizeOverride("font_size", 14);
            group.AddThemeFontSizeOverride("font_size", 14);
        }
        string hintKey = _draft.Mode switch
        {
            1 => "query.ancient.mode.any_hint",
            2 => "query.ancient.mode.group_hint",
            _ => "query.ancient.mode.basic_hint"
        };
        Text(content, _text.Get(hintKey), 0, controlsTop + 44, width, 14, true);
        float editorTop = Math.Max(controlsTop + 78, eligibilityBottom + 12);
        if (!string.IsNullOrWhiteSpace(_draft.Problem))
        {
            Text(content, _draft.Problem, 0, editorTop, width, 15, true);
            editorTop += 32;
        }
        float bottom = _draft.Advanced
            ? RenderAdvanced(content, width, editorTop)
            : RenderSimple(content, width, editorTop);
        content.CustomMinimumSize = new(width, bottom + 12);
    }

    private float RenderEligibilityConfiguration(Control content, float top)
    {
        var toggle = Button(content, _text.Get("query.ancient.eligibility.edit"), 0, top, 164,
            () => { _draft.EligibilityExpanded = !_draft.EligibilityExpanded; Render(); },
            _draft.EligibilityExpanded, 36);
        toggle.AddThemeFontSizeOverride("font_size", 14);
        if (!_draft.EligibilityExpanded) return top + 36;

        IReadOnlyList<ModelKey> options = AncientOptionConditionProfile.AuthoredOptionKeys;
        for (int index = 0; index < options.Count; index++)
        {
            ModelKey option = options[index];
            _draft.Eligibility.TryGetOptionEligibility(option, out bool eligible);
            AddRelicChoice(content, option, index, 0, top + 78, options.Count, eligible,
                () => ToggleEligibility(option, eligible), dimmed: !eligible);
        }
        return top + 122;
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

    private float RenderSimple(Control content, float width, float top)
    {
        float y = top;
        foreach (int act in new[] { 2, 3 })
            y = RenderAct(content, act, width, y, false, 1, true);
        return y;
    }

    private float RenderAdvanced(Control content, float width, float top)
    {
        if (_draft.OfferedTogether)
        {
            float y = RenderNeowAct(content, width, top, 3);
            foreach (int act in new[] { 2, 3 })
                y = RenderAct(content, act, width, y, false, 3, false);
            return y;
        }

        const float tabWidth = 120, tabGap = 16;
        for (int index = 0; index < 3; index++)
        {
            int act = index + 1;
            string title = _text.Format("query.ancient.act", act);
            Button(content, title, index * (tabWidth + tabGap), top, tabWidth,
                () => { _draft.SelectedAct = act; Render(); }, _draft.SelectedAct == act, 32);
        }
        float editorTop = top + 50;
        return _draft.SelectedAct == 1
            ? RenderNeowAct(content, width, editorTop, int.MaxValue)
            : RenderAct(content, _draft.SelectedAct, width, editorTop, true, int.MaxValue, false);
    }

    private float RenderNeowAct(Control content, float width, float y, int optionLimit)
    {
        Text(content, _text.Get("query.ancient.act1.neow"), 0, y, width, 20);
        const float leftWidth = 196, gutter = 24;
        float rightLeft = leftWidth + gutter;
        float rightWidth = width - rightLeft - 12;
        float rowTop = y + 28;
        float leftHeight = AddNeowIdentity(content, 0, rowTop);
        float rightHeight = _draft.NeowEnabled
            ? AddNeowStrip(content, rightLeft, rowTop, rightWidth, optionLimit)
            : 0;
        float rowHeight = Math.Max(leftHeight, Math.Max(62, rightHeight));
        AddVerticalLine(content, leftWidth + gutter / 2, rowTop, rowHeight);
        float bottom = rowTop + rowHeight + 12;
        AddLine(content, 0, bottom - 6, width - 12);
        return bottom;
    }

    private float RenderAct(Control content, int act, float width, float y, bool multipleAncients,
        int optionLimit, bool simpleGrid)
    {
        float leftWidth = simpleGrid ? 228 : 196;
        const float gutter = 24;
        float rightLeft = leftWidth + gutter;
        float rightWidth = width - rightLeft - 12;
        Text(content, _text.Format("query.ancient.act", act), 0, y, width, 20);
        if (_multiplayer)
        {
            Text(content, _text.Get("query.ancient.party.identity"), 0, y + 25, leftWidth, 13);
            Text(content, _text.Format("query.ancient.party.options", _seat + 1), rightLeft, y + 25, rightWidth, 13);
        }
        float rowTop = y + (_multiplayer ? 48 : 28);
        float leftHeight = AddAncientGrid(content, act, 0, rowTop, multipleAncients, simpleGrid);
        float rightHeight = 0;
        IEnumerable<AncientDraft> visibleRows = _multiplayer
            ? SharedAncients(act).Select(key => GetOrAddAncientRow(_draft.Current, act, key))
            : _draft.Acts[act];
        foreach (AncientDraft row in visibleRows)
        {
            if (rightHeight > 0)
            {
                AddLine(content, rightLeft, rowTop + rightHeight + 4, rightWidth);
                rightHeight += 14;
            }
            rightHeight += AddOptionStrip(content, act, row, rightLeft, rowTop + rightHeight, rightWidth,
                optionLimit, simpleGrid, multipleAncients);
        }
        float rowHeight = Math.Max(leftHeight, Math.Max(62, rightHeight));
        AddVerticalLine(content, leftWidth + gutter / 2, rowTop, rowHeight);
        float bottom = rowTop + rowHeight + 12;
        AddLine(content, 0, bottom - 6, width - 12);
        return bottom;
    }

    private float AddAncientGrid(Control content, int act, float x, float y, bool multipleAncients,
        bool simpleGrid)
    {
        IReadOnlyList<ModelKey> candidates = AncientCandidates(act);
        float tileWidth = simpleGrid ? 104 : 88;
        float tileHeight = simpleGrid ? 68 : 56;
        float imageSize = simpleGrid ? 40 : 30;
        float labelY = simpleGrid ? 3 : 2;
        float imageY = simpleGrid ? 23 : 22;
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
            var label = Text(button, _names.Resolve(key, GameContentKind.Ancient), 4, labelY, tileWidth - 8, fontSize);
            label.Size = new(tileWidth - 8, 18);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.MouseFilter = MouseFilterEnum.Ignore;
            AddPicture(button, key, GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon,
                (tileWidth - imageSize) / 2, imageY, imageSize);
        }
        int rows = Math.Max(1, (int)Math.Ceiling(candidates.Count / 2d));
        return rows * (tileHeight + rowGap) - rowGap;
    }

    private float AddOptionStrip(Control content, int act, AncientDraft row, float x, float y, float width,
        int optionLimit, bool simpleGrid, bool showSectionName)
    {
        float gridTop = y;
        if (showSectionName)
        {
            Text(content, _names.Resolve(row.Ancient, GameContentKind.Ancient), x, y + 1, width - 154, 15);
            gridTop += 24;
        }
        float gridHeight = AddOptionGrid(content, act, row, x, gridTop, width, optionLimit,
            simpleGrid, showSectionName);
        float height = gridTop - y + gridHeight;
        if (row.Options.Any(IsSeaGlass))
            height += 8 + AddSeaGlassTargetSelector(content, row, x, y + height + 8, width);
        return height;
    }

    private float AddOptionGrid(Control content, int act, AncientDraft row, float x, float y, float width,
        int optionLimit, bool simpleGrid, bool comfortableGrid)
    {
        IReadOnlyList<ModelKey> candidates = OptionCandidates(act, row.Ancient);
        int columns = simpleGrid
            ? Math.Max(1, (int)Math.Ceiling(candidates.Count / 2d))
            : comfortableGrid
                ? Math.Max(1, (int)((width + 8) / 60))
                : Math.Max(1, (int)((width + 8) / 52));
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
                disabled, simpleGrid, comfortableGrid, !eligible);
        }
        int rows = Math.Max(1, (int)Math.Ceiling(candidates.Count / (double)columns));
        float pitch = simpleGrid ? 92 : comfortableGrid ? 60 : 52;
        return rows * pitch - 8;
    }

    private float AddNeowIdentity(Control content, float x, float y)
    {
        ModelKey key = new(BaseGameModelKeys.Categories.Event, "NEOW");
        var button = Button(content, string.Empty, x, y, 88, ToggleNeow, _draft.NeowEnabled, 56);
        var label = Text(button, _names.Resolve(key, GameContentKind.Ancient), 4, 2, 80, 12);
        label.Size = new(80, 18);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        AddPicture(button, key, GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon, 29, 22, 30);
        return 56;
    }

    private float AddNeowStrip(Control content, float x, float y, float width, int optionLimit)
    {
        IReadOnlyList<ModelKey> candidates = NeowOptionCandidates();
        bool comfortableGrid = optionLimit == int.MaxValue;
        float pitch = comfortableGrid ? 60 : 52;
        int columns = Math.Max(1, (int)((width + 8) / pitch));
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
            }, disabled, comfortable: comfortableGrid);
        }
        int rows = Math.Max(1, (int)Math.Ceiling(candidates.Count / (double)columns));
        return rows * pitch - 8;
    }

    private void AddRelicChoice(Control content, ModelKey key, int index, float x, float y, int columns,
        bool selected, Action action, bool disabled = false, bool large = false, bool comfortable = false,
        bool dimmed = false)
    {
        float width = large ? 96 : comfortable ? 52 : 44;
        float height = large ? 84 : width;
        const float gap = 8;
        float imageSize = large ? 50 : comfortable ? 34 : 28;
        var button = Button(content, string.Empty, x + index % columns * (width + gap), y + index / columns * (height + gap), width, action, selected, height);
        button.Disabled = disabled;
        button.Modulate = dimmed ? new Color(1f, 1f, 1f, .38f) : Colors.White;
        if (large)
        {
            var label = Text(button, _names.Resolve(key, GameContentKind.Relic), 4, 3, width - 8, 12);
            label.Size = new(width - 8, 18);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.MouseFilter = MouseFilterEnum.Ignore;
            AddPicture(button, key, GameContentKind.Relic, IconVariant.RelicLarge,
                (width - imageSize) / 2, 27, imageSize);
        }
        else
        {
            float imageInset = (width - imageSize) / 2;
            AddPicture(button, key, GameContentKind.Relic, IconVariant.RelicLarge, imageInset, imageInset, imageSize);
        }
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

    private void AddLine(Control parent, float x, float y, float width)
    {
        Color color = _p.Color(_p.Line);
        color.A *= .45f;
        parent.AddChild(new ColorRect { Position = new(x, y), Size = new(width, 1), Color = color, MouseFilter = MouseFilterEnum.Ignore });
    }

    private void AddVerticalLine(Control parent, float x, float y, float height)
    {
        Color color = _p.Color(_p.Line);
        color.A *= .35f;
        parent.AddChild(new ColorRect { Position = new(x, y), Size = new(1, height), Color = color, MouseFilter = MouseFilterEnum.Ignore });
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
