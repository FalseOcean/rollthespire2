using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class TransformationEditorPrototype : Control
{
    private readonly WorkspacePalette _p = WorkspacePalette.Canonical;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("transformation-prototype");
    private readonly Action<IReadOnlyList<ModelKey>, Action<ModelKey>> _pickCards;
    private readonly Control _body = new();
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private SeatDraft _draft = new();
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private IReadOnlyList<TransformationPrototypeSource> _sources = [];
    private IReadOnlyList<ModelKey> _cards = [];
    private int _seat;
    private bool _guideExpanded;

    private sealed class SeatDraft
    {
        public HashSet<string> TakenOver { get; } = new(StringComparer.Ordinal);
        public int Objective;
        public int ResultCount = 3;
        public int RareCount = 1;
        public List<ModelKey> Cards { get; } = [];
    }

    public TransformationEditorPrototype(Action<IReadOnlyList<ModelKey>, Action<ModelKey>> pickCards)
    {
        _pickCards = pickCards;
        AddChild(_body);
    }
    public event Action? GuideRequested;
    public event Action<int, string>? TakeoverChanged;
    public event Action<string>? SourcePageRequested;
    public event Action<TransformationPrototypeSource>? SourceEditorRequested;

    public bool IsTakenOver(int seat, string sourceId) =>
        _drafts.TryGetValue(seat, out SeatDraft? draft) && draft.TakenOver.Contains(sourceId);

    public void Refresh(string language, IUiTextProvider text, int seat,
        IReadOnlyList<TransformationPrototypeSource> sources, IReadOnlyList<ModelKey> cards, bool render = true)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        _seat = seat;
        if (!_drafts.TryGetValue(seat, out SeatDraft? draft)) _drafts[seat] = draft = new SeatDraft();
        _draft = draft;
        _sources = sources;
        _cards = cards.Distinct(ModelKeyComparer.Instance).ToArray();
        HashSet<string> available = sources.Select(source => source.Id).ToHashSet(StringComparer.Ordinal);
        int removed = _draft.TakenOver.RemoveWhere(id => !available.Contains(id));
        if (removed > 0 && _draft.TakenOver.Count == 0) { _draft.Cards.Clear(); _draft.RareCount = 1; }
        _draft.Cards.RemoveAll(card => !_cards.Contains(card, ModelKeyComparer.Instance));
        _draft.ResultCount = _sources.Where(s => _draft.TakenOver.Contains(s.Id)).Sum(s => s.ResultCount);
        if (render) Render();
    }

    private void Render()
    {
        Clear(_body);
        _body.Size = Size;
        var guide = SearchEditorGuide.Build(_body, _p, Size.X - 20, _text.Get("query.transform.guide.title"),
            _text.Get("query.transform.guide.summary"),
            [_text.Get("query.transform.guide.usage"), _text.Get("query.transform.guide.boundary")],
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

        if (_sources.Count == 0)
        {
            Text(content, _text.Get("query.transform.empty.title"), 0, 12, width, 22);
            Text(content, _text.Get("query.transform.empty.help"), 0, 52, width, 16, true);
            Button(content, _text.Get("query.domain.neow"), 0, 104, 160, () => SourcePageRequested?.Invoke("neow"), false, 44);
            Button(content, _text.Get("query.domain.events"), 172, 104, 160, () => SourcePageRequested?.Invoke("events"), false, 44);
            content.CustomMinimumSize = new(width, 168);
            return;
        }
        Text(content, _text.Get("query.transform.sources.title"), 0, 0, width, 18);
        Text(content, _text.Get("query.transform.sources.hint"), 0, 32, width, 14, true);
        float y = RenderSources(content, 70);
        if (_draft.TakenOver.Count == 0)
        {
            Text(content, _text.Get("query.transform.none_managed"), 0, y + 14, width, 16, true);
            content.CustomMinimumSize = new(width, y + 60);
            return;
        }
        AddLine(content, y + 8, width - 12);
        y += 28;
        Text(content, _text.Get("query.transform.objective"), 0, y, width, 18);
        y += 36;
        string[] objectives = ["query.transform.objective.rare", "query.transform.objective.cards", "query.transform.objective.cards_rare"];
        float[] objectiveWidths = [154, 154, 236];
        float x = 0;
        for (int index = 0; index < objectives.Length; index++)
        {
            int captured = index;
            Button button = Button(content, _text.Get(objectives[index]), x, y, objectiveWidths[index],
                () => { _draft.Objective = captured; Render(); }, _draft.Objective == index, 40);
            button.AddThemeFontSizeOverride("font_size", 14);
            x += objectiveWidths[index] + 12;
        }
        y += 60;
        Text(content, _text.Get("query.transform.result_count"), 0, y, 150, 15, true);
        Text(content, _draft.ResultCount.ToString(), 0, y + 26, 150, 20);
        if (_draft.Objective == 0)
        {
            Text(content, _text.Get("query.transform.rare_count"), 174, y, 150, 15, true);
            int[] counts = Enumerable.Range(1, Math.Max(1, _draft.ResultCount)).Append(_draft.RareCount).Distinct().Order().ToArray();
            Options(content, counts.Select(value => value.ToString()).ToArray(),
                Array.IndexOf(counts, _draft.RareCount), 174, y + 24, 150,
                selected => { _draft.RareCount = counts[selected]; Render(); });
            y += 84;
        }
        else
        {
            y += 76;
            Text(content, _text.Get("query.transform.target_cards"), 0, y, width, 16, true);
            y += 30;
            int shown = _draft.Objective == 2 ? Math.Min(1, _draft.Cards.Count) : _draft.Cards.Count;
            for (int index = 0; index < shown; index++)
            {
                int slot = index;
                content.AddChild(new WorkspaceResultTile(_p, _icons, _names, _text, _draft.Cards[index],
                    GameContentKind.Card, () => PickCard(slot),
                    () => { _draft.Cards.RemoveAt(slot); Render(); })
                { Position = new(index * (WorkspaceResultTile.TileWidth + 12), y) });
            }
            if (shown < (_draft.Objective == 2 ? 1 : _draft.ResultCount))
            {
                int slot = shown;
                content.AddChild(new WorkspaceResultTile(_p, _icons, _names, _text, null,
                    GameContentKind.Card, () => PickCard(slot), () => { })
                { Position = new(shown * (WorkspaceResultTile.TileWidth + 12), y) });
            }
            y += WorkspaceResultTile.TileHeight + 14;
            if (_draft.Objective == 2)
            {
                Text(content, _text.Format("query.transform.remaining_rare", _draft.ResultCount - 1), 0, y, width, 16, true);
                y += 38;
            }
        }
        content.CustomMinimumSize = new(width, y + 18);
    }

    private void PickCard(int slot)
    {
        if (_cards.Count == 0) return;
        _pickCards(_cards, card =>
        {
            if (!_cards.Contains(card, ModelKeyComparer.Instance)) return;
            if (slot < _draft.Cards.Count) _draft.Cards[slot] = card;
            else _draft.Cards.Add(card);
            Render();
        });
    }

    internal bool SetSourceTakenOver(string sourceId, bool enabled)
    {
        if (!_sources.Any(source => source.Id == sourceId)) return false;
        bool changed = enabled ? _draft.TakenOver.Add(sourceId) : _draft.TakenOver.Remove(sourceId);
        if (!changed) return true;
        if (_draft.TakenOver.Count == 0) { _draft.Cards.Clear(); _draft.RareCount = 1; }
        _draft.ResultCount = _sources.Where(s => _draft.TakenOver.Contains(s.Id)).Sum(s => s.ResultCount);
        TakeoverChanged?.Invoke(_seat, sourceId);
        Render();
        return true;
    }

    private float RenderSources(Control content, float y)
    {
        float width = Size.X - 28;
        foreach (TransformationPrototypeSource source in _sources)
        {
            bool takenOver = _draft.TakenOver.Contains(source.Id);
            string name = SourceName(source);
            var button = Button(content, (takenOver ? "✓  " : "+  ") + name, 0, y, width - 152,
                () => SetSourceTakenOver(source.Id, !takenOver), takenOver, 42);
            button.TooltipText = _text.Get(takenOver ? "query.transform.sources.release" : "query.transform.sources.takeover");
            Button(content, _text.Get("query.transform.sources.edit"), width - 140, y, 140,
                () => SourceEditorRequested?.Invoke(source), false, 42);
            y += 54;
        }
        return y;
    }

    private string SourceName(TransformationPrototypeSource source)
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

    private OptionButton Options(Control parent, string[] values, int selected, float x, float y, float width, Action<int> change)
    {
        var option = new OptionButton { Position = new(x, y), Size = new(width, 40), FitToLongestItem = false, ClipText = true };
        foreach (string value in values) option.AddItem(value);
        option.Select(selected);
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
        parent.AddChild(new ColorRect { Position = new(0, y), Size = new(width, 1), Color = color, MouseFilter = MouseFilterEnum.Ignore });
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
}
