using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Shell;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class RelicSequenceSearchEditor : Control
{
    private ScrollContainer? _editorScroll;
    private readonly ModRuntimeSnapshot _runtime;
    private readonly ObjectPickerPanel _picker;
    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("relic-sequence-prototype");
    private readonly Control _body = new();
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private readonly Dictionary<int, SeatDraft> _partyDrafts = [];
    private SeatDraft _draft = new();
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private bool _english;
    private bool _guideExpanded;
    private bool _party;
    private bool _treasurePage;

    private sealed class BuilderDraft
    {
        public int RangeValue = 3;
        public SearchSequenceRangeMode RangeMode = SearchSequenceRangeMode.FirstN;
        public RelicSequenceUiMatchMode MatchMode = RelicSequenceUiMatchMode.Appears;
        public bool Advanced;
    }

    private sealed class SeatDraft
    {
        public string Context = string.Empty;
        public string Problem = string.Empty;
        public RelicSequenceSearchUiCatalog? Catalog;
        public BuilderDraft Builder { get; } = new();
        public List<RelicSequenceUiCondition> Conditions { get; } = [];
        public int TreasureStart = 1;
        public int TreasureCount = 1;
        public TreasureRelicOrderMode TreasureOrder = TreasureRelicOrderMode.Ordered;
        public ModelKey?[] TreasureSlots = [null, null];
    }

    public RelicSequenceSearchEditor(ModRuntimeSnapshot runtime, ObjectPickerPanel picker)
    {
        _runtime = runtime;
        _picker = picker;
        AddChild(_body);
    }

    public event Action? GuideRequested;

    public void Refresh(string language, IUiTextProvider text, ModelKey character, int ascension,
        int players, int seat, SerializableUnlockState? unlocks = null, bool render = true, EditorRefreshContext? sharedContext = null)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        _english = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        _party = players > 1;
        var drafts = players > 1 ? _partyDrafts : _drafts;
        foreach (int removedSeat in drafts.Keys.Where(index => index >= players).ToArray()) drafts.Remove(removedSeat);
        if (!drafts.TryGetValue(seat, out var draft)) drafts[seat] = draft = new SeatDraft();
        _draft = draft;

        string unlockKey = sharedContext?.Key ?? (players == 1
            ? System.Text.Json.JsonSerializer.Serialize(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks));
        string context = $"{character}/{ascension}/{players}/{seat}/{unlockKey}";
        if (_draft.Context != context)
        {
            _draft.Context = context;
            _draft.Catalog = null;
            _draft.Problem = string.Empty;
            try
            {
                if (players > 1 && unlocks is null)
                    _draft.Problem = _text.Get("query.relic.context.read_unlocks_required");
                else
                {
                    UnlockState resolved = sharedContext is not null ? UnlockState.FromSerializable(sharedContext.Unlocks) : players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    _draft.Catalog = sharedContext is not null
                        ? RelicSequenceSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, sharedContext.Authority.WorldAuthority)
                        : CaptureCatalog(character, ascension, players, seat, resolved);
                    if (!_draft.Catalog.CatalogAvailable)
                        _draft.Problem = _text.Get("query.relic.context.catalog_unavailable");
                }
            }
            catch (Exception exception)
            {
                _draft.Catalog = null;
                _draft.Problem = _text.Get("query.relic.context.catalog_unavailable");
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception, "EditorCatalogCaptureFailed", "RelicSequenceEditorPrototype", "PickerCatalog", "issues.capture_failed", Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning);
            }
        }
        if (render) Render();
    }

    private RelicSequenceSearchUiCatalog CaptureCatalog(ModelKey character, int ascension,
        int players, int seat, UnlockState unlocks)
    {
        string seed = RolltheSpire2.Compatibility.ProfileSeedGenerator.CreateProbeSeed(_runtime.Profile);
        var effects = ReflectionNeowEffectSnapshotAdapter.Capture(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, _runtime.Profile.ProfileId,
            _runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), unlocks);
        var world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, noRunModifiers: true,
            effects.EffectAuthority, gameVersion: _runtime.Detection.DisplayVersion);
        return RelicSequenceSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, world);
    }

    private void Render()
    {
        int scrollTop = _editorScroll is not null && GodotObject.IsInstanceValid(_editorScroll) ? _editorScroll.ScrollVertical : 0;
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
        float tabsHeight = _party ? 0 : 56;
        if (!_party)
        {
            Button(content, _text.Get("query.relic.tab.sequence"), 16, 0, 170,
                () => { _treasurePage = false; Render(); }, !_treasurePage).Name = "RelicSequenceTab";
            Button(content, _text.Get("query.relic.tab.treasure"), 198, 0, 210,
                () => { _treasurePage = true; Render(); }, _treasurePage).Name = "TreasureRelicTab";
        }
        if (!_party && _treasurePage) RenderTreasure(content, width, tabsHeight);
        else
        {
            var sequence = new Control { Position = new(0, tabsHeight), Size = new(width, 1) };
            content.AddChild(sequence);
            float builderHeight = RenderBuilder(sequence, width);
            RenderConditions(sequence, width, builderHeight);
            content.CustomMinimumSize = new(width, tabsHeight + sequence.CustomMinimumSize.Y);
        }
        _editorScroll = scroll;
        if (scrollTop > 0) scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scrollTop);
    }

    private VBoxContainer FamilyGuide(Control host, float width) =>
        SearchEditorGuide.Build(host, _p, width, _text.Get(!_party && _treasurePage ? "query.relic.tab.treasure" : "query.relic.guide.title"),
            _text.Get(!_party && _treasurePage ? "query.treasure.guide.summary" : "query.relic.guide.summary"),
            [_text.Get(!_party && _treasurePage ? "query.treasure.guide.usage" : "query.relic.guide.usage")],
            _guideExpanded, () => { _guideExpanded = !_guideExpanded; Render(); }, () => GuideRequested?.Invoke());

    private float RenderBuilder(Control content, float width)
    {
        bool hasProblem = !string.IsNullOrWhiteSpace(_draft.Problem);
        const float bottom = 72;
        var builder = new Control { Name = "RelicConditionBuilder", Size = new(width, bottom + (hasProblem ? 40 : 0)) };
        content.AddChild(builder);
        RenderBuilderPosition(builder, 16, 16);
        var choose = Button(builder, _text.Get("query.relic.choose_button"), 380, 16, 174, OpenPicker, false, 40);
        choose.Name = "RelicBuilderChoose";
        choose.AccessibilityName = _text.Get("query.relic.choose");
        choose.AddThemeFontSizeOverride("font_size", _english ? 15 : 16);
        choose.Disabled = _draft.Catalog is null || !_draft.Catalog.CatalogAvailable;
        var advanced = Button(builder, _text.Get("query.relic.advanced"), 566, 16, 128,
            ToggleAdvanced, _draft.Builder.Advanced, 40);
        advanced.Name = "RelicSequenceAdvanced";
        advanced.AddThemeFontSizeOverride("font_size", _english ? 15 : 16);
        if (hasProblem) Text(builder, _draft.Problem, 16, bottom, width - 32, 16, true);
        return builder.Size.Y + 16;
    }

    private void RenderConditions(Control content, float width, float top)
    {
        Text(content, _text.Format("query.relic.conditions", _draft.Conditions.Count), 16, top, width - 32, 17);
        float y = top + 36;
        if (_draft.Conditions.Count == 0)
        {
            Text(content, _text.Get("query.relic.conditions.empty"), 16, y, width - 32, 16, true);
            y += 36;
        }
        for (int index = 0; index < _draft.Conditions.Count; index++)
        {
            RelicSequenceUiCondition condition = _draft.Conditions[index];
            string name = _names.Resolve(condition.RelicKey, GameContentKind.Relic);
            var row = new Control { Name = "RelicCondition" + index, Position = new(0, y), Size = new(width, 76) };
            content.AddChild(row);
            row.AddChild(new TextureRect
            {
                Position = new(16, 16), Size = new(44, 44),
                Texture = _icons.Resolve(condition.RelicKey, GameContentKind.Relic, IconVariant.RelicLarge).Texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore
            });
            var relicName = Text(row, name, 72, 8, width - 140, 18);
            MainPanelTheme.SetColor(relicName, "font_color", _p.Selected);
            relicName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            Text(row, ConditionText(condition), 72, 40, width - 140, 14, true);
            var remove = Button(row, "×", width - 52, 20, 36, () => RemoveCondition(condition.Id), false, 36);
            remove.AccessibilityName = _text.Format("common.remove_named", name);
            Color divider = _p.Color(_p.Line);
            divider.A *= .35f;
            row.AddChild(new ColorRect { Position = new(16, 75), Size = new(width - 32, 1), Color = divider,
                MouseFilter = MouseFilterEnum.Ignore });
            y += row.Size.Y + 8;
        }
        content.CustomMinimumSize = new(width, y);
    }

    private void RenderBuilderPosition(Control host, float left, float top)
    {
        var group = new Control { Name = "RelicBuilderPosition", Position = new(left, top), Size = new(352, 40) };
        host.AddChild(group);
        if (_draft.Builder.Advanced)
        {
            var mode = BuilderOptions(group,
                [_text.Get("query.relic.position.first_n"), _text.Get("query.relic.position.exact_n")],
                _draft.Builder.RangeMode == SearchSequenceRangeMode.ExactSlot ? 1 : 0, 0, 0, 104,
                index => { _draft.Builder.RangeMode = index == 1 ? SearchSequenceRangeMode.ExactSlot : SearchSequenceRangeMode.FirstN; Render(); });
            mode.Name = "RelicBuilderRangeMode";
            var polarity = BuilderOptions(group,
                [_text.Get("query.relic.polarity.include"), _text.Get("query.relic.polarity.exclude")],
                _draft.Builder.MatchMode == RelicSequenceUiMatchMode.Excluded ? 1 : 0, 248, 0, 104,
                index => { _draft.Builder.MatchMode = index == 1 ? RelicSequenceUiMatchMode.Excluded : RelicSequenceUiMatchMode.Appears; Render(); });
            polarity.Name = "RelicBuilderPolarity";
        }
        else
        {
            var range = Text(group, _text.Get(_draft.Builder.RangeMode == SearchSequenceRangeMode.FirstN
                ? "query.relic.position.first_n" : "query.relic.position.exact_n"), 0, 0, 104, 16, true);
            range.Name = "RelicBuilderRangeMode";
            range.Size = new(104, 40);
            range.VerticalAlignment = VerticalAlignment.Center;
            var match = Text(group, _text.Get(_draft.Builder.MatchMode == RelicSequenceUiMatchMode.Appears
                ? "query.relic.polarity.include" : "query.relic.polarity.exclude"), 248, 0, 104, 16, true);
            match.Name = "RelicBuilderPolarity";
            match.Size = new(104, 40);
            match.VerticalAlignment = VerticalAlignment.Center;
        }
        var decrease = Button(group, "−", 116, 0, 36, () => SetRangeValue(_draft.Builder.RangeValue - 1), false, 40);
        decrease.Name = "RelicBuilderPositionDecrease";
        decrease.TooltipText = string.Empty;
        decrease.AccessibilityName = _text.Get("query.relic.range.decrease");
        decrease.Disabled = _draft.Builder.RangeValue <= 1;
        var value = Text(group, _draft.Builder.RangeValue.ToString(), 158, 0, 36, 18);
        value.Name = "RelicBuilderPositionValue";
        value.Size = new(36, 40);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        var increase = Button(group, "+", 200, 0, 36, () => SetRangeValue(_draft.Builder.RangeValue + 1), false, 40);
        increase.Name = "RelicBuilderPositionIncrease";
        increase.TooltipText = string.Empty;
        increase.AccessibilityName = _text.Get("query.relic.range.increase");
        increase.Disabled = _draft.Builder.RangeValue >= SeedPredictionInputLimits.MaximumRelicSequencePreviewCount;
    }

    private OptionButton BuilderOptions(Control parent, string[] values, int selected, float x, float y,
        float width, Action<int> changed)
    {
        var control = new OptionButton { FitToLongestItem = false, ClipText = true };
        using var donor = _p.CompactButton("", 40, 16);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            control.AddThemeStyleboxOverride(state, donor.GetThemeStylebox(state));
        control.AddThemeColorOverride("font_color", _p.Color(_p.Text));
        control.AddThemeFontSizeOverride("font_size", 16);
        foreach (string value in values) control.AddItem(value);
        control.Select(selected);
        control.Position = new(x, y);
        control.Size = new(width, 40);
        control.ItemSelected += index => changed((int)index);
        parent.AddChild(control);
        return control;
    }

    private void OpenPicker()
    {
        if (_draft.Catalog is null || !_draft.Catalog.CatalogAvailable) return;
        ModelKey[] candidates = new[] { RelicSequenceKind.Common, RelicSequenceKind.Uncommon, RelicSequenceKind.Rare, RelicSequenceKind.Shop }
            .SelectMany(_draft.Catalog.CandidatesFor)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        _picker.Open(new(candidates, CommitSelectedRelic, _names, _text, _icons,
            key => _draft.Catalog.Categories.GetValueOrDefault(key).ToString()));
    }

    private void CommitSelectedRelic(ModelKey relic)
    {
        if (_draft.Catalog is null ||
            _draft.Builder.RangeValue is < 1 or > SeedPredictionInputLimits.MaximumRelicSequencePreviewCount ||
            !_draft.Catalog.TryResolveLane(relic, out RelicSequenceKind lane) ||
            lane is not (RelicSequenceKind.Common or RelicSequenceKind.Uncommon or RelicSequenceKind.Rare or RelicSequenceKind.Shop)) return;
        _draft.Conditions.Add(new RelicSequenceUiCondition(Guid.NewGuid(), lane,
            _draft.Builder.RangeMode, _draft.Builder.RangeValue, relic, _draft.Builder.MatchMode));
        Render();
    }

    private void RemoveCondition(Guid id)
    {
        if (_draft.Conditions.RemoveAll(condition => condition.Id == id) > 0) Render();
    }

    private void ToggleAdvanced()
    {
        _draft.Builder.Advanced = !_draft.Builder.Advanced;
        Render();
    }

    private void SetRangeValue(int value)
    {
        int next = Math.Clamp(value, 1, SeedPredictionInputLimits.MaximumRelicSequencePreviewCount);
        if (next == _draft.Builder.RangeValue) return;
        _draft.Builder.RangeValue = next;
        Render();
    }

    internal IReadOnlyList<RelicSequenceSearchCondition> BuildConditions() => _draft.Conditions
        .Select(condition => new RelicSequenceSearchCondition(condition.Lane, condition.RangeMode,
            condition.RangeValue, condition.MatchMode == RelicSequenceUiMatchMode.Appears
                ? new ModelKeySetFilter([condition.RelicKey], [], [])
                : new ModelKeySetFilter([], [], [condition.RelicKey])))
        .ToArray();

    private string ConditionText(RelicSequenceUiCondition condition)
    {
        string key = (condition.RangeMode, condition.MatchMode) switch
        {
            (SearchSequenceRangeMode.FirstN, RelicSequenceUiMatchMode.Appears) => "query.relic.condition.first_n.include",
            (SearchSequenceRangeMode.FirstN, _) => "query.relic.condition.first_n.exclude",
            (SearchSequenceRangeMode.ExactSlot, RelicSequenceUiMatchMode.Appears) => "query.relic.condition.exact_n.include",
            _ => "query.relic.condition.exact_n.exclude"
        };
        return _text.Format(key, LaneText(condition.Lane), condition.RangeValue);
    }

    private string LaneText(RelicSequenceKind lane) => _text.Get(lane switch
    {
        RelicSequenceKind.Common => "query.relic.sequence.common",
        RelicSequenceKind.Uncommon => "query.relic.sequence.uncommon",
        RelicSequenceKind.Rare => "query.relic.sequence.rare",
        RelicSequenceKind.Shop => "query.relic.sequence.shop",
        _ => "query.relic.sequence.common"
    });

    private Button Button(Control parent, string title, float x, float y, float width, Action action,
        bool selected, float height = 40)
    {
        var button = _p.CompactButton(title, height, height < 32 ? 16 : 18);
        _p.SetModeActive(button, selected);
        button.Position = new(x, y);
        button.Size = new(width, height);
        button.CustomMinimumSize = new(0, height);
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
