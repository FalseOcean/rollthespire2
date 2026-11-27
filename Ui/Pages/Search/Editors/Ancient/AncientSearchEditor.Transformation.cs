using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class AncientSearchEditor
{
    private sealed class AncientTransformationDraft
    {
        public List<ModelKey> Cards { get; } = [];
        public int? PandoraCountOverride;
    }

    internal event Action? TransformationContextChanged;
    internal event Action<string>? TransformationRequested;
    internal Func<int, string, bool>? TransformTakenOver { get; set; }
    internal Func<TransformationSearchSource, int?>? AutomaticPandoraCount { get; set; }
    internal Action<Action<ModelKey>>? PickTransformationTarget { get; set; }

    private IEnumerable<(int Act, ModelKey Ancient, ModelKey Option)> DeclaredTransformationSelections() =>
        _multiplayer ? [] : _draft.Acts.SelectMany(pair => pair.Value
            .Where(row => row.Ancient.Entry == "DARV")
            .SelectMany(row => row.Options.Where(option => TransformationSearchSource.AncientSourceId(option) is not null)
                .Select(option => (pair.Key, row.Ancient, option))));

    internal IEnumerable<string> DeclaredTransformationSourceIds() => DeclaredTransformationSelections()
        .Select(selection => TransformationSearchSource.AncientSourceId(selection.Option)!).Distinct(StringComparer.Ordinal);

    internal IEnumerable<TransformationSearchSource> TransformationSources() => DeclaredTransformationSelections()
        .GroupBy(selection => selection.Option, ModelKeyComparer.Instance)
        .Where(group => group.Select(selection => selection.Act).Distinct().Count() == 1)
        .SelectMany(group => group.Where(IsTransformationAvailable).Take(1))
        .Select(selection => DescribeTransformation(selection.Act, selection.Option));

    private bool IsTransformationAvailable((int Act, ModelKey Ancient, ModelKey Option) selection) =>
        IsOptionEligible(selection.Option) && OptionCandidates(selection.Act, selection.Ancient).Contains(selection.Option, ModelKeyComparer.Instance);

    private TransformationSearchSource DescribeTransformation(int act, ModelKey option)
    {
        var state = _draft.Transformations.GetValueOrDefault((act, option));
        return new(TransformationSearchSource.AncientSourceId(option)!, option, option.Entry == "ASTROLABE" ? 3 : 0)
        {
            AncientAct = act,
            HasIndependentResult = state?.Cards.Count > 0,
            PandoraCountOverride = state?.PandoraCountOverride
        };
    }

    internal IReadOnlyList<AncientTransformationResult> ExportTransformationResults() => DeclaredTransformationSelections()
        .DistinctBy(selection => (selection.Act, selection.Option))
        .Select(selection => (Selection: selection, Source: DescribeTransformation(selection.Act, selection.Option)))
        .Where(entry => entry.Source.HasIndependentResult && TransformTakenOver?.Invoke(_seat, entry.Source.Id) != true)
        .Select(entry => new AncientTransformationResult(entry.Source,
            _draft.Transformations[(entry.Selection.Act, entry.Selection.Option)].Cards.ToArray())
            { IsAvailable = IsTransformationAvailable(entry.Selection) })
        .ToArray();

    internal bool TryRestoreTransformationTargets(string sourceId, int act, IReadOnlyList<ModelKey> cards)
    {
        var selected = DeclaredTransformationSelections().Where(selection => selection.Act == act &&
            TransformationSearchSource.AncientSourceId(selection.Option) == sourceId).Select(selection => selection.Option).Distinct().ToArray();
        if (selected.Length != 1) return false;
        var state = TransformationDraft(act, selected[0]);
        if (state.Cards.Count > 0) return SameTransformationTargets(state.Cards, cards);
        state.Cards.AddRange(cards);
        return true;
    }

    internal bool TryRestorePandoraCount(int act, int count)
    {
        if (act is not (2 or 3)) return false;
        var state = TransformationDraft(act, new("RELIC", "PANDORAS_BOX"));
        if (state.PandoraCountOverride is { } current && current != count) return false;
        state.PandoraCountOverride = count;
        return true;
    }

    internal static bool SameTransformationTargets(IEnumerable<ModelKey> first, IEnumerable<ModelKey> second) =>
        first.OrderBy(key => key.Serialized, StringComparer.Ordinal).SequenceEqual(second.OrderBy(key => key.Serialized, StringComparer.Ordinal));

    private AncientTransformationDraft TransformationDraft(int act, ModelKey option)
    {
        if (!_draft.Transformations.TryGetValue((act, option), out var state))
            _draft.Transformations[(act, option)] = state = new();
        return state;
    }

    private static bool HasTransformationDraft(AncientTransformationDraft draft) => draft.Cards.Count > 0 || draft.PandoraCountOverride.HasValue;

    private static IReadOnlyList<AncientTransformationEditorSnapshot>? ExportTransformationEditorState(SeatDraft draft)
    {
        var entries = draft.Transformations.Where(entry => HasTransformationDraft(entry.Value)).OrderBy(entry => entry.Key.Act)
            .ThenBy(entry => entry.Key.Option.Serialized, StringComparer.Ordinal)
            .Select(entry => new AncientTransformationEditorSnapshot(entry.Key.Act, entry.Key.Option, entry.Value.Cards.ToArray())
                { PandoraCountOverride = entry.Value.PandoraCountOverride }).ToArray();
        return entries.Length > 0 ? entries : null;
    }

    private static void ImportTransformationEditorState(SeatDraft draft, IReadOnlyList<AncientTransformationEditorSnapshot>? saved)
    {
        draft.Transformations.Clear();
        foreach (var entry in saved ?? [])
        {
            if (entry is null || entry.Act is not (2 or 3) || entry.Option.Category != "RELIC" ||
                TransformationSearchSource.AncientSourceId(entry.Option) is not { } source || entry.Cards is null ||
                entry.Cards.Any(card => !card.IsValid || card.Category != "CARD") || entry.PandoraCountOverride is < 0 ||
                entry.PandoraCountOverride.HasValue && source != TransformationSearchSource.PandoraId ||
                draft.Transformations.ContainsKey((entry.Act, entry.Option)))
                throw new InvalidOperationException("integration.load_shape");
            var state = new AncientTransformationDraft { PandoraCountOverride = entry.PandoraCountOverride };
            state.Cards.AddRange(entry.Cards);
            draft.Transformations.Add((entry.Act, entry.Option), state);
        }
    }

    internal void FocusTransformationSource(int act)
    {
        if (act is not (2 or 3)) return;
        _draft.SelectedAct = act;
        Render();
    }

    private float AddTransformationSourceActions(Control content, int act, AncientDraft row, float x, float y, float width)
    {
        var comboSources = TransformationSources().Select(source => source.Id).ToHashSet(StringComparer.Ordinal);
        var selected = DeclaredTransformationSelections().Where(selection => selection.Act == act &&
            selection.Ancient == row.Ancient && IsTransformationAvailable(selection));
        foreach (var selection in selected)
        {
            var source = DescribeTransformation(act, selection.Option);
            var name = _names.Resolve(source.Identity, GameContentKind.Relic);
            bool managed = TransformTakenOver?.Invoke(_seat, source.Id) == true;
            bool conflicting = DeclaredTransformationSelections().Any(selection =>
                TransformationSearchSource.AncientSourceId(selection.Option) is { } other && other != source.Id &&
                (TransformTakenOver?.Invoke(_seat, other) == true ||
                    _draft.Transformations.GetValueOrDefault((selection.Act, selection.Option))?.Cards.Count > 0));
            if (managed || comboSources.Contains(source.Id))
            {
                var title = _text.Format(managed ? "query.transform.sources.open_ancient" : "query.transform.sources.takeover_ancient", name);
                var button = ModeButton(content, title, x, y + 8, width, () => TransformationRequested?.Invoke(source.Id), managed, 34);
                button.Name = "AncientTransformation_" + source.Identity.Entry;
                button.AddThemeFontSizeOverride("font_size", 15);
                button.Disabled = conflicting && !managed;
                y += 48;
            }
            else
            {
                Text(content, name, x, y + 8, width, 15);
                y += 36;
            }
            y = RenderTransformationTargets(content, source, x, y, width, managed, conflicting);
        }
        return y;
    }

    private float RenderTransformationTargets(Control host, TransformationSearchSource source, float x, float y, float width,
        bool managed, bool conflicting)
    {
        AncientTransformationDraft draft = TransformationDraft(source.AncientAct, source.Identity);
        bool pandora = source.Id == TransformationSearchSource.PandoraId;
        int? automatic = pandora ? AutomaticPandoraCount?.Invoke(source) : null;
        int? Capacity() => pandora ? draft.PandoraCountOverride ?? automatic : source.ResultCount;
        Action updateCapacity = () => { };
        if (pandora)
        {
            Text(host, _text.Get("query.transform.pandora.count"), x, y, width, 15);
            var quantity = new MapQuantityInput(_p, Capacity(), true, compact: true)
            { Name = "TransformationPandoraCount", Position = new(x, y + 28) };
            host.AddChild(quantity);
            quantity.ResetSize();
            string restoreTitle = automatic.HasValue ? _text.Format("query.transform.pandora.restore_count", automatic.Value)
                : _text.Get("query.transform.pandora.restore");
            var restore = Button(host, restoreTitle, x, y + 28, 166, () =>
            {
                draft.PandoraCountOverride = null;
                RenderAfterAncientEdit();
            }, false, 38);
            restore.Name = "TransformationPandoraRestoreAutomatic";
            restore.AddThemeFontSizeOverride("font_size", 15);
            var status = Text(host, _text.Get(automatic.HasValue ? "query.transform.pandora.automatic" :
                "query.transform.pandora.automatic_unavailable"), x, y, width, 14, true);
            status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            status.ClipText = false;
            float nextX = quantity.GetRect().End.X + 12;
            bool stacked = width - (nextX - x) < restore.Size.X || !automatic.HasValue;
            restore.Position = stacked ? new(x, y + 72) : new(nextX, y + 28);
            status.Position = restore.Position + new Vector2(0, 5);
            status.Size = new(stacked ? width : width - (nextX - x), 28);
            void UpdateCountStatus()
            {
                restore.Visible = draft.PandoraCountOverride.HasValue;
                status.Visible = !restore.Visible;
            }
            UpdateCountStatus();
            quantity.ValueChanged += count =>
            {
                draft.PandoraCountOverride = count;
                UpdateCountStatus();
                updateCapacity();
                TransformationContextChanged?.Invoke();
            };
            y = restore.Position.Y + Math.Max(38, status.GetMinimumSize().Y + 5) + 12;
        }

        if (conflicting || !managed && draft.Cards.Count > 0)
        {
            var premise = Text(host, _text.Get(conflicting ? "query.transform.ancient.exclusive" : source.AncientPremiseKey),
                x, y, width, 14, true);
            premise.Name = "AncientTransformationPremise";
            premise.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            premise.ClipText = false;
            float height = Math.Max(24, premise.GetMinimumSize().Y);
            premise.Size = new(width, height);
            y += height + 6;
        }
        if (managed && draft.Cards.Count == 0 || conflicting && draft.Cards.Count == 0) return y;
        if (managed)
        {
            Text(host, _text.Get("query.transform.ancient.retained_targets"), x, y, width, 15, true);
            string cards = string.Join(" · ", TransformationSearchEditor.DescribeCardCopies(draft.Cards, _names));
            var summary = Text(host, cards, x, y + 28, width, 15, true);
            summary.Name = "AncientTransformationDraft_" + source.Identity.Entry;
            summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            summary.ClipText = false;
            summary.Size = new(width, Math.Max(28, summary.GetMinimumSize().Y));
            return summary.Position.Y + summary.Size.Y + 8;
        }
        Text(host, _text.Get("query.transform.target_cards"), x, y, width, 15);
        y += 28;
        int columns = Math.Max(1, (int)((width + 8) / (WorkspaceResultTile.CompactWidth + 8)));
        Vector2 PositionFor(int index) => new(x + index % columns * (WorkspaceResultTile.CompactWidth + 8),
            y + index / columns * (WorkspaceResultTile.CompactHeight + 8));
        void Choose(int slot)
        {
            PickTransformationTarget?.Invoke(card =>
            {
                if (slot < draft.Cards.Count) draft.Cards[slot] = card;
                else if (draft.Cards.Count < Capacity()) draft.Cards.Add(card);
                RenderAfterAncientEdit();
            });
        }
        for (int slot = 0; slot < draft.Cards.Count; slot++)
        {
            int index = slot;
            host.AddChild(new WorkspaceResultTile(_p, _icons, _names, _text, draft.Cards[slot], GameContentKind.Card,
                () => Choose(index), () => { draft.Cards.RemoveAt(index); RenderAfterAncientEdit(); },
                compact: true) { Position = PositionFor(slot) });
        }
        int displayed = draft.Cards.Count;
        if (!conflicting && (pandora || draft.Cards.Count < Capacity()))
        {
            var add = new WorkspaceResultTile(_p, _icons, _names, _text, null, GameContentKind.Card,
                () => Choose(draft.Cards.Count), () => { }, compact: true)
            { Name = "AncientTransformationAddTarget_" + source.Identity.Entry, Position = PositionFor(displayed++) };
            host.AddChild(add);
            updateCapacity = () => add.Visible = draft.Cards.Count < Capacity();
            updateCapacity();
        }
        return y + (displayed + columns - 1) / columns * (WorkspaceResultTile.CompactHeight + 8) + 8;
    }

    private void RenderAfterAncientEdit()
    {
        TransformationContextChanged?.Invoke();
        Render();
    }
}
