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

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class RelicSequenceEditorPrototype : Control
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly NeowEditorPrototype _pickerHost;
    private readonly WorkspacePalette _p = WorkspacePalette.Canonical;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("relic-sequence-prototype");
    private readonly Control _body = new();
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private readonly Dictionary<int, SeatDraft> _partyDrafts = [];
    private SeatDraft _draft = new();
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private bool _english;
    private bool _guideExpanded;

    private sealed class BuilderDraft
    {
        public int RangeValue = SeedPredictionInputLimits.DefaultRelicSequencePreviewCount;
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
    }

    public RelicSequenceEditorPrototype(ModRuntimeSnapshot runtime, NeowEditorPrototype pickerHost)
    {
        _runtime = runtime;
        _pickerHost = pickerHost;
        AddChild(_body);
    }

    public event Action? GuideRequested;

    public void Refresh(string language, IUiTextProvider text, ModelKey character, int ascension,
        int players, int seat, SerializableUnlockState? unlocks = null, bool render = true)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        _english = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var drafts = players > 1 ? _partyDrafts : _drafts;
        foreach (int removedSeat in drafts.Keys.Where(index => index >= players).ToArray()) drafts.Remove(removedSeat);
        if (!drafts.TryGetValue(seat, out var draft)) drafts[seat] = draft = new SeatDraft();
        _draft = draft;

        string unlockKey = players == 1
            ? System.Text.Json.JsonSerializer.Serialize(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks);
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
                    UnlockState resolved = players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    _draft.Catalog = CaptureCatalog(character, ascension, players, seat, resolved);
                    if (!_draft.Catalog.CatalogAvailable)
                        _draft.Problem = _text.Get("query.relic.context.catalog_unavailable");
                }
            }
            catch (Exception exception)
            {
                _draft.Catalog = null;
                _draft.Problem = _text.Get("query.relic.context.catalog_unavailable");
                RuntimeLog.Warn("relicSequencePrototypeCatalog=" + exception.Message);
            }
            Revalidate();
        }
        if (render) Render();
    }

    private RelicSequenceSearchUiCatalog CaptureCatalog(ModelKey character, int ascension,
        int players, int seat, UnlockState unlocks)
    {
        string seed = new(_runtime.Profile.SeedAlphabet[0], _runtime.Profile.SeedLength);
        var effects = ReflectionNeowEffectSnapshotAdapter.Capture(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, _runtime.Profile.ProfileId,
            _runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), unlocks);
        var world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, noRunModifiers: true,
            effects.EffectAuthority, gameVersion: _runtime.Detection.DisplayVersion);
        return RelicSequenceSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, world);
    }

    private void Revalidate()
    {
        if (_draft.Catalog is null || !_draft.Catalog.CatalogAvailable) return;
        if (_draft.Catalog.LaneMappingExact)
            _draft.Conditions.RemoveAll(condition =>
                !_draft.Catalog.TryResolveLane(condition.RelicKey, out RelicSequenceKind lane) ||
                lane != condition.Lane || lane == RelicSequenceKind.Shop);
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
        float contentHeight = 208 + Math.Max(1, _draft.Conditions.Count) * 66;
        var content = new Control { CustomMinimumSize = new(Size.X - 20, contentHeight) };
        scroll.AddChild(content);
        RenderBuilder(content, Size.X - 28);
        RenderConditions(content, Size.X - 28);
    }

    private VBoxContainer FamilyGuide(Control host, float width) =>
        SearchEditorGuide.Build(host, _p, width, _text.Get("query.relic.guide.title"),
            _text.Get("query.relic.guide.summary"),
            [_text.Get("query.relic.guide.usage")],
            _guideExpanded, () => { _guideExpanded = !_guideExpanded; Render(); }, () => GuideRequested?.Invoke());

    private void RenderBuilder(Control content, float width)
    {
        Text(content, _text.Get("query.relic.add_condition"), 0, 0, 240, 20);
        var advanced = Button(content, _text.Get("query.relic.advanced"), width - 166, 0, 154,
            ToggleAdvanced, _draft.Builder.Advanced, 40);
        advanced.AddThemeFontSizeOverride("font_size", _english ? 15 : 16);

        if (!string.IsNullOrWhiteSpace(_draft.Problem))
            Text(content, _draft.Problem, 420, 2, width - 420, 17, true);

        if (_draft.Builder.Advanced)
        {
            var position = Button(content,
                _text.Get(_draft.Builder.RangeMode == SearchSequenceRangeMode.FirstN
                    ? "query.relic.position.first_n" : "query.relic.position.exact_n"),
                0, 56, 146, ToggleRangeMode,
                _draft.Builder.RangeMode == SearchSequenceRangeMode.ExactSlot, 48);
            position.AddThemeFontSizeOverride("font_size", 16);
        }
        else
            Text(content, _text.Get("query.relic.simple.first_n_include"), 0, 66, 180, 17, true);

        float stepperLeft = _draft.Builder.Advanced ? 158 : 190;
        var decrease = Button(content, "−", stepperLeft, 56, 48, () => SetRangeValue(_draft.Builder.RangeValue - 1), false, 48);
        decrease.AccessibilityName = _text.Get("query.relic.range.decrease");
        decrease.Disabled = _draft.Builder.RangeValue <= 1;
        var number = Text(content, _draft.Builder.RangeValue.ToString(), stepperLeft + 56, 66, 38, 20);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        var increase = Button(content, "+", stepperLeft + 102, 56, 48, () => SetRangeValue(_draft.Builder.RangeValue + 1), false, 48);
        increase.AccessibilityName = _text.Get("query.relic.range.increase");
        increase.Disabled = _draft.Builder.RangeValue >= SeedPredictionInputLimits.MaximumRelicSequencePreviewCount;

        if (_draft.Builder.Advanced)
        {
            var polarity = Button(content,
                _text.Get(_draft.Builder.MatchMode == RelicSequenceUiMatchMode.Appears
                    ? "query.relic.polarity.include" : "query.relic.polarity.exclude"),
                320, 56, 160, TogglePolarity,
                _draft.Builder.MatchMode == RelicSequenceUiMatchMode.Excluded, 48);
            polarity.AddThemeFontSizeOverride("font_size", 16);
        }

        var choose = Button(content, _text.Get("query.relic.choose_button"),
            _draft.Builder.Advanced ? 492 : 350, 56, 132, OpenPicker, false, 48);
        choose.AccessibilityName = _text.Get("query.relic.choose");
        choose.AddThemeFontSizeOverride("font_size", _english ? 15 : 16);
        choose.Disabled = _draft.Catalog is null || !_draft.Catalog.CatalogAvailable;

        Color divider = _p.Color(_p.Line);
        divider.A *= .55f;
        content.AddChild(new ColorRect { Position = new(0, 128), Size = new(width - 12, 1), Color = divider, MouseFilter = MouseFilterEnum.Ignore });
    }

    private void RenderConditions(Control content, float width)
    {
        Text(content, _text.Format("query.relic.conditions", _draft.Conditions.Count), 0, 150, width, 19);
        if (_draft.Conditions.Count == 0)
        {
            Text(content, _text.Get("query.relic.conditions.empty"), 0, 188, width, 17, true);
            return;
        }

        for (int index = 0; index < _draft.Conditions.Count; index++)
        {
            RelicSequenceUiCondition condition = _draft.Conditions[index];
            float y = 184 + index * 66;
            Text(content, ConditionText(condition), 0, y + 15, 322, 17);
            AddSmallRelic(content, condition.RelicKey, 338, y + 5);
            string name = _names.Resolve(condition.RelicKey, GameContentKind.Relic);
            var nameLabel = Text(content, name, 392, y + 15, width - 446, 18);
            nameLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            nameLabel.TooltipText = name;
            AddRemoveButton(content, width - 42, y + 12, name, () => RemoveCondition(condition.Id));
            Color line = _p.Color(_p.Line);
            line.A *= .4f;
            content.AddChild(new ColorRect { Position = new(0, y + 59), Size = new(width - 12, 1), Color = line, MouseFilter = MouseFilterEnum.Ignore });
        }
    }

    private void OpenPicker()
    {
        if (_draft.Catalog is null || !_draft.Catalog.CatalogAvailable) return;
        ModelKey[] candidates = new[] { RelicSequenceKind.Common, RelicSequenceKind.Uncommon, RelicSequenceKind.Rare }
            .SelectMany(_draft.Catalog.CandidatesFor)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        _pickerHost.PickExternalObjects(candidates, CommitSelectedRelic);
    }

    private void CommitSelectedRelic(ModelKey relic)
    {
        if (_draft.Catalog is null ||
            _draft.Builder.RangeValue is < 1 or > SeedPredictionInputLimits.MaximumRelicSequencePreviewCount ||
            !_draft.Catalog.TryResolveLane(relic, out RelicSequenceKind lane) ||
            lane is not (RelicSequenceKind.Common or RelicSequenceKind.Uncommon or RelicSequenceKind.Rare)) return;
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
        if (!_draft.Builder.Advanced)
        {
            _draft.Builder.RangeMode = SearchSequenceRangeMode.FirstN;
            _draft.Builder.MatchMode = RelicSequenceUiMatchMode.Appears;
        }
        Render();
    }

    private void ToggleRangeMode()
    {
        _draft.Builder.RangeMode = _draft.Builder.RangeMode == SearchSequenceRangeMode.FirstN
            ? SearchSequenceRangeMode.ExactSlot : SearchSequenceRangeMode.FirstN;
        Render();
    }

    private void TogglePolarity()
    {
        _draft.Builder.MatchMode = _draft.Builder.MatchMode == RelicSequenceUiMatchMode.Appears
            ? RelicSequenceUiMatchMode.Excluded : RelicSequenceUiMatchMode.Appears;
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
        _ => "query.relic.sequence.common"
    });

    private void AddSmallRelic(Control parent, ModelKey key, float x, float y)
    {
        var icon = new TextureRect
        {
            Position = new(x, y),
            Size = new(44, 44),
            Texture = _icons.Resolve(key, GameContentKind.Relic, IconVariant.Small).Texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        parent.AddChild(icon);
    }

    private void AddRemoveButton(Control parent, float x, float y, string name, Action removeAction)
    {
        var host = new Control { Position = new(x, y), Size = new(30, 30), CustomMinimumSize = new(30, 30), ClipContents = true };
        parent.AddChild(host);
        var remove = new Button { Text = "×", Size = new(30, 30), CustomMinimumSize = new(30, 30), ClipText = true };
        remove.AccessibilityName = _text.Format("common.remove_named", name);
        remove.AddThemeFontSizeOverride("font_size", 15);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var box = _p.Box(state == "hover" ? _p.Hover : state == "pressed" ? _p.Line : _p.Surface,
                _p.Line, state == "hover" ? 1 : 0);
            box.ContentMarginLeft = box.ContentMarginRight = box.ContentMarginTop = box.ContentMarginBottom = 0;
            remove.AddThemeStyleboxOverride(state, box);
        }
        remove.AddThemeStyleboxOverride("focus", _p.FocusRing());
        remove.Pressed += removeAction;
        host.AddChild(remove);
    }

    private Button Button(Control parent, string title, float x, float y, float width, Action action,
        bool selected, float height = 40)
    {
        var button = _p.CompactButton(title, height, height < 32 ? 16 : 18, selected);
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
