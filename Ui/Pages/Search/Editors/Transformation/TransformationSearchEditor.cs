using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class TransformationSearchEditor : Control
{
    private readonly SearchEditorLayout _presentation = new();
    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("transformation-prototype");
    private readonly Action<IReadOnlyList<ModelKey>, Action<ModelKey>> _pickCards;
    private readonly Control _body = new();
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private SeatDraft _draft = new();
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private IReadOnlyList<TransformationSearchSource> _sources = [];
    private IReadOnlySet<string>? _declaredSourceIds;
    private IReadOnlyList<ModelKey> _cards = [];
    private IReadOnlyList<ModelKey> _openingRelics = [];
    private int? _startingStrikeDefendCount;
    private int _seat;
    private bool _guideExpanded;

    private class GroupDraft
    {
        public HashSet<string> TakenOver { get; } = new(StringComparer.Ordinal);
        public int Objective;
        public int RareCount = 1;
        public int CardsRareCount;
        public List<ModelKey> Cards { get; } = [];
    }

    private sealed class SeatDraft : GroupDraft
    {
        public List<GroupDraft> AdditionalGroups { get; } = [];
        public Dictionary<string, TransformationSearchSource> KnownSources { get; } = new(StringComparer.Ordinal);
        public int? PandoraCountOverride;
        public bool ExplicitRequirements;
        public bool SingleCombination = true;
    }

    private IEnumerable<GroupDraft> Groups => _draft.AdditionalGroups.Prepend(_draft);
    private IEnumerable<string> TakenOver => Groups.SelectMany(group => group.TakenOver).Distinct(StringComparer.Ordinal);

    public TransformationSearchEditor(Action<IReadOnlyList<ModelKey>, Action<ModelKey>> pickCards)
    {
        _pickCards = pickCards;
        AddChild(_body);
    }
    public event Action? GuideRequested;
    public event Action<int, string>? TakeoverChanged;
    public event Action<string>? SourcePageRequested;
    public event Action<TransformationSearchSource>? SourceEditorRequested;

    public bool IsTakenOver(int seat, string sourceId) =>
        _drafts.TryGetValue(seat, out SeatDraft? draft) &&
        (draft.TakenOver.Contains(sourceId) || draft.AdditionalGroups.Any(group => group.TakenOver.Contains(sourceId)));

    public void Refresh(string language, IUiTextProvider text, int seat,
        IReadOnlyList<TransformationSearchSource> sources, IReadOnlyList<ModelKey> cards, bool render = true,
        IReadOnlySet<string>? declaredSourceIds = null, int? startingStrikeDefendCount = null,
        IReadOnlyList<ModelKey>? openingRelics = null)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        if (!_drafts.TryGetValue(seat, out SeatDraft? draft)) _drafts[seat] = draft = new SeatDraft();
        _seat = seat;
        _draft = draft;
        _sources = sources;
        _declaredSourceIds = declaredSourceIds;
        _startingStrikeDefendCount = startingStrikeDefendCount;
        _openingRelics = openingRelics ?? [];
        foreach (var source in sources) _draft.KnownSources[source.Id] = source;
        _cards = cards.Distinct(ModelKeyComparer.Instance).ToArray();
        if (render) Render();
    }

    private void Render()
    {
        _presentation.Begin();
        Clear(_body);
        _body.Size = Size;
        var guide = SearchEditorGuide.Build(_body, _p, Size.X - 20, _text.Get("query.transform.guide.title"),
            _text.Get("query.transform.guide.summary"),
            [_text.Get("query.transform.guide.usage"), _text.Get("query.transform.guide.boundary"),
                _text.Get("query.transform.guide.targets")],
            _guideExpanded, () => { _guideExpanded = !_guideExpanded; Render(); }, () => GuideRequested?.Invoke());
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

        var group = _draft;
        if (_sources.Count == 0 && !TakenOver.Any())
        {
            Text(content, _text.Get("query.transform.empty.title"), 16, 12, width - 32, 22);
            Text(content, _text.Get("query.transform.empty.help"), 16, 52, width - 32, 16, true);
            Button(content, _text.Get("query.domain.neow"), 16, 104, 160, () => SourcePageRequested?.Invoke("neow"), false, 44);
            Button(content, _text.Get("query.domain.events"), 188, 104, 160, () => SourcePageRequested?.Invoke("events"), false, 44);
            Button(content, _text.Get("query.domain.ancients"), 360, 104, 160, () => SourcePageRequested?.Invoke("ancient"), false, 44);
            float emptyBottom = RenderSavedRequirements(content, width, RenderDormantTargets(content, width, 168, group));
            content.CustomMinimumSize = new(width, emptyBottom);
            _presentation.Section(content, 0, emptyBottom);
            _presentation.Finish();
            return;
        }
        Text(content, _text.Get("query.transform.sources.title"), 16, 0, width - 272, 20);
        var resultCount = Text(content, _text.Get("query.transform.result_count") + " · " +
            (_sources.Any(source => group.TakenOver.Contains(source.Id) && !SourceCount(source).HasValue) ? "—" : ResultCount(group).ToString()),
            width - 240, 4, 224, 15, true);
        resultCount.HorizontalAlignment = HorizontalAlignment.Right;
        Text(content, _text.Get("query.transform.sources.hint"), 16, 32, width - 32, 14, true);
        float y = RenderSources(content, 64, group);
        if (!ActiveTakeovers(group).Any())
        {
            Text(content, _text.Get("query.transform.none_managed"), 16, y + 14, width - 32, 16, true);
            float inactiveBottom = RenderSavedRequirements(content, width, RenderDormantTargets(content, width, y + 54, group));
            content.CustomMinimumSize = new(width, inactiveBottom);
            _presentation.Section(content, 0, inactiveBottom);
            _presentation.Finish();
            return;
        }
        if (HasResultTarget(group) && _sources.FirstOrDefault(source => source.IsAncient && group.TakenOver.Contains(source.Id)) is { } ancient)
            y = RenderAncientPremises(content, width, y, ancient);
        float objectiveTop = y;
        _presentation.Section(content, 0, y);
        Text(content, _text.Get("query.transform.objective"), 16, y, width - 32, 20);
        y += 36;
        string[] objectives = ["query.transform.objective.rare", "query.transform.objective.cards", "query.transform.objective.cards_rare"];
        float x = 16;
        for (int index = 0; index < objectives.Length; index++)
        {
            int captured = index;
            string title = _text.Get(objectives[index]);
            float segmentWidth = Math.Min(width - 32, Math.Max(120,
                Mathf.Ceil(content.GetThemeFont("font", "Button").GetStringSize(title, fontSize: 14).X) + 24));
            if (x > 16 && x + segmentWidth > width - 16) { x = 16; y += 44; }
            Button button = ModeButton(content, title, x, y, segmentWidth,
                () => { group.Objective = captured; Render(); }, group.Objective == index, 36);
            button.Name = "TransformationObjective" + index;
            button.AddThemeFontSizeOverride("font_size", 14);
            SearchTooltip.Set(button, _text.Get(objectives[index] + "_tooltip"));
            x += segmentWidth + 8;
        }
        y += 46;
        if (group.Objective == 0)
        {
            var label = Text(content, _text.Get("query.transform.rare_count"), 16, y + 8, 150, 15, true);
            label.Size = new(150, 30);
            var quantity = new MapQuantityInput(_p, group.RareCount, true)
            { Name = "TransformationRareCount", Position = new(174, y), Size = new(136, 38) };
            quantity.ValueChanged += value => group.RareCount = value;
            content.AddChild(quantity);
            y += 50;
        }
        else
        {
            Text(content, _text.Get("query.transform.target_cards"), 16, y, width - 32, 16, true);
            y += 30;
            int shown = group.Cards.Count;
            int columns = Math.Max(1, (int)((width - 32 + 12) / (WorkspaceResultTile.CompactWidth + 12)));
            Vector2 TilePosition(int index) => new(16 + index % columns * (WorkspaceResultTile.CompactWidth + 12),
                y + index / columns * (WorkspaceResultTile.CompactHeight + 14));
            for (int index = 0; index < shown; index++)
            {
                int slot = index;
                content.AddChild(new WorkspaceResultTile(_p, _icons, _names, _text, group.Cards[index],
                    GameContentKind.Card, () => PickCard(group, slot),
                    () => { group.Cards.RemoveAt(slot); Render(); }, compact: true)
                { Position = TilePosition(index) });
            }
            bool addTarget = shown < ResultCount(group);
            if (addTarget)
            {
                int slot = shown;
                var add = new WorkspaceResultTile(_p, _icons, _names, _text, null,
                    GameContentKind.Card, () => PickCard(group, slot), () => { }, compact: true)
                { Name = "TransformationAddTarget", Position = TilePosition(shown) };
                content.AddChild(add);
            }
            y += Math.Max(1, (shown + (addTarget ? 1 : 0) + columns - 1) / columns) * (WorkspaceResultTile.CompactHeight + 14);
            if (group.Objective == 2)
            {
                Text(content, _text.Format("query.transform.remaining_rare", Math.Max(0, ResultCount(group) - Math.Max(1, shown))),
                    16, y, width - 32, 16, true);
                y += 38;
            }
            else
            {
                Text(content, _text.Get("query.transform.rare_count_optional"), 16, y + 8, width - 182, 15, true);
                var quantity = new MapQuantityInput(_p, group.CardsRareCount, true)
                { Name = "TransformationCardsRareCount", Position = new(width - 152, y), Size = new(136, 38) };
                quantity.ValueChanged += value => group.CardsRareCount = value;
                content.AddChild(quantity);
                y += 50;
            }
        }
        y = RenderSavedRequirements(content, width, y);
        content.CustomMinimumSize = new(width, y + 18);
        _presentation.Section(content, objectiveTop, y + 18);
        _presentation.Finish();
    }

    private void PickCard(GroupDraft group, int slot)
    {
        if (_cards.Count == 0) return;
        _pickCards(_cards, card =>
        {
            if (!ReferenceEquals(group, _draft) || !_cards.Contains(card, ModelKeyComparer.Instance)) return;
            if (slot < group.Cards.Count) group.Cards[slot] = card;
            else if (group.Cards.Count < ResultCount(group)) group.Cards.Add(card);
            Render();
        });
    }

    internal bool OpenSource(string sourceId)
    {
        if (IsTakenOver(_seat, sourceId)) return true;
        return SetSourceTakenOver(sourceId, true);
    }

    internal bool SetSourceTakenOver(string sourceId, bool enabled)
    {
        if (enabled && (!_sources.Any(source => source.Id == sourceId) || ConflictsWithAncientTakeover(sourceId))) return false;
        bool changed = enabled ? _draft.TakenOver.Add(sourceId) : _draft.TakenOver.Remove(sourceId);
        if (!changed) return true;
        TakeoverChanged?.Invoke(_seat, sourceId);
        Render();
        return true;
    }

    private bool ConflictsWithAncientTakeover(string sourceId) => TransformationSearchSource.IsAncientId(sourceId) &&
        (TakenOver.Any(id => id != sourceId && TransformationSearchSource.IsAncientId(id) && IsSourceDeclared(id)) ||
            _sources.Any(source => source.IsAncient && source.Id != sourceId && source.HasIndependentResult));

    private float RenderSavedRequirements(Control host, float width, float y)
    {
        if (_draft.AdditionalGroups.Count == 0) return y;
        Text(host, _text.Get("query.transform.saved.title"), 16, y, width - 32, 18);
        y += 32;
        for (int index = 0; index < _draft.AdditionalGroups.Count; index++)
        {
            var saved = _draft.AdditionalGroups[index];
            var sources = saved.TakenOver.Order(StringComparer.Ordinal).Select(id => SourceName(
                _draft.KnownSources.GetValueOrDefault(id) ?? RetainedSource(id)));
            var caption = Text(host, string.Join(" + ", sources), 16, y, width - 88, 16);
            caption.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            var remove = Button(host, "×", width - 52, y, 36, () =>
            {
                _draft.AdditionalGroups.Remove(saved);
                TakeoverChanged?.Invoke(_seat, saved.TakenOver.FirstOrDefault() ?? string.Empty);
                Render();
            }, false, 34);
            remove.Name = "TransformationRemoveSavedRequirement" + index;
            remove.AccessibilityName = _text.Get("query.transform.saved.remove");
            List<string> targets = DescribeTargets(saved);
            if (saved.Objective == 2) targets.Add(_text.Get("query.transform.objective.cards_rare"));
            if (!ActiveTakeovers(saved).Any()) targets.Add(_text.Get("query.transform.saved.inactive"));
            var label = Text(host, string.Join(" · ", targets), 16, y + 35, width - 32, 15, true);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.Size = new(width - 32, 54);
            y += 94;
        }
        return y;
    }

    private float RenderDormantTargets(Control host, float width, float y, GroupDraft group)
    {
        if (group.Cards.Count == 0 && group.CardsRareCount == 0 && (group.TakenOver.Count == 0 || group.Objective != 0)) return y + 8;
        Text(host, _text.Get("query.transform.retained.inactive"), 16, y, width - 32, 15, true);
        y += 36;
        var label = Text(host, string.Join(" · ", DescribeTargets(group)), 16, y, width - 32, 16);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.Size = new(width - 32, 56);
        return y + 72;
    }

    private List<string> DescribeTargets(GroupDraft group)
    {
        if (group.Objective == 0) return [_text.Format("query.transform.summary.rare_count", group.RareCount)];
        var targets = group.Cards.Select(key => _names.Resolve(key, GameContentKind.Card)).ToList();
        if (group.Objective == 1 && group.CardsRareCount > 0)
            targets.Add(_text.Format("query.transform.summary.rare_count", group.CardsRareCount));
        return targets;
    }

    private float RenderSources(Control content, float y, GroupDraft group)
    {
        float width = Size.X - 28;
        var displayed = _sources.Concat(group.TakenOver.Where(id => !_sources.Any(source => source.Id == id))
            .Select(id => _draft.KnownSources.GetValueOrDefault(id) ?? RetainedSource(id)));
        foreach (TransformationSearchSource source in displayed)
        {
            bool available = _sources.Any(current => current.Id == source.Id);
            bool takenOver = group.TakenOver.Contains(source.Id);
            bool conflicting = !takenOver && ConflictsWithAncientTakeover(source.Id);
            string name = SourceName(source);
            bool participates = takenOver && IsSourceDeclared(source.Id) && HasResultTarget(group);
            var button = ModeButton(content, takenOver ? "✓" : "○", 16, y + 4, 36,
                () => SetSourceTakenOver(source.Id, !takenOver), takenOver, 36);
            button.Name = "TransformationSourceToggle_" + source.Id.Replace('.', '_');
            button.ToggleMode = true;
            button.ButtonPressed = takenOver;
            button.Disabled = conflicting;
            button.AccessibilityName = (takenOver ? "✓  " : "+  ") + name;
            SearchTooltip.Set(button, _text.Get(takenOver ? "query.transform.sources.release" : "query.transform.sources.takeover"));
            GameContentKind kind = source.Id.StartsWith("E.", StringComparison.Ordinal) ? GameContentKind.Event : GameContentKind.Relic;
            content.AddChild(new TextureRect
            {
                Position = new(64, y + 6), Size = new(32, 32), Texture = _icons.Resolve(source.Identity, kind, IconVariant.Small).Texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore
            });
            string origin = source.IsAncient ? "A" : source.Id.StartsWith("E.", StringComparison.Ordinal) ? "E" : "N";
            string stage = source.IsAncient && source.AncientAct > 0 ? _text.Format("query.ancient.act", source.AncientAct) + " · " : "";
            var caption = Text(content, origin + " · " + stage + name, 108, y + 2, width - 352, 16);
            caption.Size = new(width - 352, 40);
            caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            caption.MaxLinesVisible = 2;
            caption.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.AccessibilityName = name;
            caption.MouseFilter = MouseFilterEnum.Ignore;
            if (participates) MainPanelTheme.SetColor(caption, "font_color", _p.Selected);
            string? amount = SourceCount(source)?.ToString();
            var count = Text(content, amount is null ? "—" : "×" + amount, width - 272, y + 8, 64, 16, true);
            count.HorizontalAlignment = HorizontalAlignment.Center;
            count.AccessibilityName = _text.Get("query.transform.result_count") + " · " + (amount ?? "—");
            Button(content, _text.Get("query.transform.sources.edit"), width - 192, y + 4, 176,
                () => { if (available) SourceEditorRequested?.Invoke(source);
                    else SourcePageRequested?.Invoke(source.IsAncient ? "ancient" : source.Id.StartsWith("E.", StringComparison.Ordinal) ? "events" : "neow"); }, false, 36);
            y += 44;
            if (!available || conflicting)
            {
                string issue = conflicting ? "query.transform.ancient.exclusive" : IsSourceDeclared(source.Id)
                    ? "query.transform.retained.source_missing" : "query.transform.retained.source_inactive";
                var conflict = Text(content, _text.Get(issue), 108, y + 4, width - 124, 15, true);
                conflict.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                conflict.ClipText = false;
                conflict.Size = new(width - 124, 40);
                y += 48;
            }
        }
        return y;
    }

    private float RenderAncientPremises(Control content, float width, float y, TransformationSearchSource source)
    {
        var premise = Text(content, _text.Get(source.AncientPremiseKey), 16, y + 6, width - 32, 15, true);
        premise.Name = "TransformationAncientPremise";
        premise.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        premise.ClipText = false;
        float height = Math.Max(24, premise.GetMinimumSize().Y);
        premise.Size = new(width - 32, height);
        return y + height + 12;
    }

    internal static TransformationSearchSource RetainedSource(string id) => id switch
    {
        "N.LeafyPoultice" => new(id, BaseGameModelKeys.Relics.LeafyPoultice, 2),
        "N.NewLeaf" => new(id, BaseGameModelKeys.Relics.NewLeaf, 1),
        "N.BonesLeafyNewLeaf" => new(id, BaseGameModelKeys.Relics.NeowsBones, 3),
        "N.BonesLeafyOther" => new(id, BaseGameModelKeys.Relics.NeowsBones, 2),
        TransformationSearchSource.AstrolabeId => new(id, new("RELIC", "ASTROLABE"), 3),
        TransformationSearchSource.PandoraId => new(id, new("RELIC", "PANDORAS_BOX"), 0),
        _ => new(id, new ModelKey("EVENT", id.StartsWith("E.", StringComparison.Ordinal) ? id[2..] : id),
            id is "E.MORPHIC_GROVE" or "E.TRIAL" ? 2 : 1)
    };

    private string SourceName(TransformationSearchSource source)
    {
        if (source.Id == "N.BonesLeafyNewLeaf")
            return _names.Resolve(BaseGameModelKeys.Relics.NeowsBones, GameContentKind.Relic) + " · " +
                _names.Resolve(BaseGameModelKeys.Relics.LeafyPoultice, GameContentKind.Relic) + " + " +
                _names.Resolve(BaseGameModelKeys.Relics.NewLeaf, GameContentKind.Relic);
        if (source.Id == "N.BonesLeafyOther")
            return _names.Resolve(BaseGameModelKeys.Relics.NeowsBones, GameContentKind.Relic) + " · " +
                _names.Resolve(BaseGameModelKeys.Relics.LeafyPoultice, GameContentKind.Relic) + " + " +
                (source.OtherRelic is { } other ? _names.Resolve(other, GameContentKind.Relic) : string.Empty);
        if (source.Id == "E.TRIAL") return _names.Resolve(source.Identity, GameContentKind.Event) + " · " + _text.Get("query.event.results.case_3") + " ×2";
        return _names.Resolve(source.Identity, source.Id.StartsWith("E.", StringComparison.Ordinal)
            ? GameContentKind.Event : GameContentKind.Relic);
    }

    private Button ModeButton(Control parent, string title, float x, float y, float width, Action action, bool active, float height = 40)
    {
        var button = Button(parent, title, x, y, width, action, false, height);
        _p.SetModeActive(button, active);
        return button;
    }

    private Button Button(Control parent, string title, float x, float y, float width, Action action, bool selected, float height)
    {
        Button button = _p.CompactButton(title, height, height < 32 ? 16 : 18, selected);
        button.Position = new(x, y);
        button.Size = new(width, height);
        button.CustomMinimumSize = new(0, height);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }


    private Label Text(Control parent, string value, float x, float y, float width, int size, bool secondary = false)
    {
        Label label = _p.Label(value, size, secondary);
        label.Position = new(x, y);
        label.Size = new(width, 28);
        label.ClipText = true;
        parent.AddChild(label);
        return label;
    }

    private void AddLine(Control parent, float y, float width)
    {
        Color color = _p.Color(_p.Line);
        color.A *= .45f;
        parent.AddChild(new ColorRect { Position = new(8, y), Size = new(Math.Max(1, width - 16), 1), Color = color, MouseFilter = MouseFilterEnum.Ignore });
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
}
