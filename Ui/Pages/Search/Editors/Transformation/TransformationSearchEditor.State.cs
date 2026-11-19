using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class TransformationSearchEditor
{
    internal IReadOnlyList<ModelKey> RemoveUnavailableNeowTargetsAfterCharacterChange(IReadOnlyList<ModelKey> candidates)
    {
        var known = MegaCrit.Sts2.Core.Models.ModelDb.AllCards.Select(model => new ModelKey(model.Id.Category, model.Id.Entry))
            .ToHashSet(ModelKeyComparer.Instance);
        var removed = new List<ModelKey>();
        foreach (var group in new[] { _draft })
        {
            var active = ActiveTakeovers(group).ToArray();
            // The Neow candidate proof does not rule out event or Ancient cards.
            if (active.Length == 0 || active.Any(id => !id.StartsWith("N.", StringComparison.Ordinal))) continue;
            group.Cards.RemoveAll(key =>
            {
                if (!known.Contains(key) || candidates.Contains(key, ModelKeyComparer.Instance)) return false;
                removed.Add(key);
                return true;
            });
        }
        return removed;
    }

    private static void CopyGroup(GroupDraft source, GroupDraft target)
    {
        target.TakenOver.Clear();
        target.TakenOver.UnionWith(source.TakenOver);
        target.Objective = source.Objective;
        target.RareCount = source.RareCount;
        target.CardsRareCount = source.CardsRareCount;
        target.Cards.Clear();
        target.Cards.AddRange(source.Cards);
    }

    internal Action PreserveConditionsForUndo()
    {
        return WorkbenchConditionReset.Preserve(_drafts, draft =>
        {
            var copy = new SeatDraft { PandoraCountOverride = draft.PandoraCountOverride,
                ExplicitRequirements = draft.ExplicitRequirements, SingleCombination = draft.SingleCombination };
            CopyGroup(draft, copy);
            foreach (var group in draft.AdditionalGroups)
            {
                var clone = new GroupDraft();
                CopyGroup(group, clone);
                copy.AdditionalGroups.Add(clone);
            }
            foreach (var source in draft.KnownSources) copy.KnownSources.Add(source.Key, source.Value);
            return copy;
        }, (previous, current) => { if (ReferenceEquals(_draft, previous)) _draft = current; });
    }

    internal bool HasConditions() => _drafts.Values.Any(draft => draft.TakenOver.Count > 0 || draft.Cards.Count > 0 ||
        draft.CardsRareCount > 0 || draft.AdditionalGroups.Count > 0 || draft.PandoraCountOverride.HasValue);

    internal Action ClearConditions() => WorkbenchConditionReset.Swap(_drafts, _ => new SeatDraft(),
        replaceActive: (previous, current) => { if (ReferenceEquals(_draft, previous)) _draft = current; });

    internal int AuthoredConditionCount() => Groups.Count(group => ActiveTakeovers(group).Any() && HasResultTarget(group));

    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }

    internal TransformationEditorStateSnapshot ExportEditorState() => new(_draft.TakenOver.Order(StringComparer.Ordinal).ToArray(),
        _draft.Objective, _draft.RareCount, _draft.Cards.ToArray(), _draft.KnownSources.Values.Select(source =>
            new TransformationSourceSnapshot(source.Id, source.Identity, source.ResultCount, source.OtherRelic) { AncientAct = source.AncientAct }).ToArray())
    {
        CardsRareCount = _draft.CardsRareCount,
        PandoraCountOverride = _draft.PandoraCountOverride,
        ExplicitRequirements = _draft.ExplicitRequirements,
        SingleCombination = _draft.SingleCombination,
        AdditionalGroups = _draft.AdditionalGroups.Count == 0 ? null : _draft.AdditionalGroups.Select(group =>
            new TransformationResultGroupSnapshot(group.TakenOver.Order(StringComparer.Ordinal).ToArray(),
                group.Objective, group.RareCount, group.Cards.ToArray()) { CardsRareCount = group.CardsRareCount }).ToArray()
    };

    internal void ImportEditorState(TransformationEditorStateSnapshot saved)
    {
        LoadGroup(_draft, saved.TakenOver, saved.Objective, saved.RareCount, saved.CardsRareCount, saved.Cards);
        _draft.AdditionalGroups.Clear();
        foreach (var group in saved.AdditionalGroups ?? [])
        {
            var restored = new GroupDraft();
            LoadGroup(restored, group.TakenOver, group.Objective, group.RareCount, group.CardsRareCount, group.Cards);
            _draft.AdditionalGroups.Add(restored);
        }
        if (saved.PandoraCountOverride is < 0) throw new InvalidOperationException("integration.load_shape");
        _draft.PandoraCountOverride = saved.PandoraCountOverride;
        _draft.ExplicitRequirements = saved.ExplicitRequirements;
        _draft.SingleCombination = saved.SingleCombination;
        _draft.KnownSources.Clear();
        foreach (var source in saved.KnownSources ?? [])
            _draft.KnownSources[source.Id] = new(source.Id, source.Identity, source.ResultCount, source.OtherRelic) { AncientAct = source.AncientAct };
    }

    internal void MigrateLegacyRequirements(int ancientAct, Func<int, int, bool> movePandoraCount,
        Func<TransformationSearchSource, IReadOnlyList<ModelKey>, bool> moveNamedTargets)
    {
        if (_draft.SingleCombination) return;
        if (_draft.PandoraCountOverride is { } count)
        {
            int act = ancientAct > 0 ? ancientAct : _draft.KnownSources.GetValueOrDefault(TransformationSearchSource.PandoraId)?.AncientAct ?? 0;
            if (movePandoraCount(act, count)) _draft.PandoraCountOverride = null;
        }

        foreach (var group in Groups.ToArray())
        {
            if (group is not { Objective: 1, CardsRareCount: 0, TakenOver.Count: 1, Cards.Count: > 0 }) continue;
            string id = group.TakenOver.Single();
            if (Groups.Any(other => !ReferenceEquals(group, other) && other.TakenOver.Contains(id))) continue;
            var source = _sources.FirstOrDefault(candidate => candidate.Id == id);
            if (source is null || !moveNamedTargets(source, group.Cards)) continue;
            if (ReferenceEquals(group, _draft)) CopyGroup(new GroupDraft(), _draft);
            else _draft.AdditionalGroups.Remove(group);
        }
        _draft.SingleCombination = true;
    }

    private static void LoadGroup(GroupDraft target, IReadOnlyList<string> sources, int objective, int rareCount,
        int cardsRareCount, IReadOnlyList<ModelKey> cards)
    {
        if (sources is null || cards is null || objective is < 0 or > 2 || rareCount < 0 || cardsRareCount < 0)
            throw new InvalidOperationException("integration.load_shape");
        target.TakenOver.Clear();
        target.TakenOver.UnionWith(sources);
        target.Objective = objective;
        target.RareCount = rareCount;
        target.CardsRareCount = cardsRareCount;
        target.Cards.Clear();
        target.Cards.AddRange(cards);
    }
}
