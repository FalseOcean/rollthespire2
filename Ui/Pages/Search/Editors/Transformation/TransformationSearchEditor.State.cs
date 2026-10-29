using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class TransformationSearchEditor
{
    internal IReadOnlyList<ModelKey> RemoveUnavailableNeowTargetsAfterCharacterChange(IReadOnlyList<ModelKey> candidates)
    {
        var active = ActiveTakeovers().ToArray();
        // Event transformations can use different source cards and pools. A
        // Neow-only proof cannot rule out their contribution to a joint target.
        if (active.Length == 0 || active.Any(id => !id.StartsWith("N.", StringComparison.Ordinal))) return [];
        var known = MegaCrit.Sts2.Core.Models.ModelDb.AllCards.Select(model =>
            new ModelKey(model.Id.Category, model.Id.Entry)).ToHashSet(ModelKeyComparer.Instance);
        var removed = _draft.Cards.Where(key => known.Contains(key) && !candidates.Contains(key, ModelKeyComparer.Instance)).ToArray();
        _draft.Cards.RemoveAll(key => removed.Contains(key, ModelKeyComparer.Instance));
        return removed;
    }

    internal Action PreserveConditionsForUndo()
    {
        var saved = _drafts.ToDictionary(p => p.Key, p =>
        {
            var copy = new SeatDraft { Objective = p.Value.Objective, ResultCount = p.Value.ResultCount, RareCount = p.Value.RareCount };
            copy.TakenOver.UnionWith(p.Value.TakenOver); copy.Cards.AddRange(p.Value.Cards);
            foreach (var source in p.Value.KnownSources) copy.KnownSources.Add(source.Key, source.Value);
            return copy;
        });
        return () => { _drafts.Clear(); foreach (var pair in saved) _drafts[pair.Key] = pair.Value; };
    }

    internal bool HasConditions() => _drafts.Values.Any(d => d.TakenOver.Count > 0 || d.Cards.Count > 0);
    internal Action ClearConditions() => WorkbenchConditionReset.Swap(_drafts, _ => new SeatDraft());

    internal int AuthoredConditionCount() => ActiveTakeovers().Any() &&
        (_draft.Objective == 0 ? _draft.RareCount > 0 : _draft.Cards.Count > 0) ? 1 : 0;

    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }

    internal TransformationEditorStateSnapshot ExportEditorState() => new(_draft.TakenOver.Order(StringComparer.Ordinal).ToArray(),
        _draft.Objective, _draft.RareCount, _draft.Cards.ToArray(), _draft.KnownSources.Values
            .Select(source => new TransformationSourceSnapshot(source.Id, source.Identity, source.ResultCount, source.OtherRelic)).ToArray());
    internal void ImportEditorState(TransformationEditorStateSnapshot saved)
    {
        _draft.TakenOver.Clear(); _draft.TakenOver.UnionWith(saved.TakenOver);
        _draft.Objective = saved.Objective; _draft.RareCount = saved.RareCount;
        _draft.Cards.Clear(); _draft.Cards.AddRange(saved.Cards);
        _draft.KnownSources.Clear();
        foreach (var source in saved.KnownSources ?? [])
            _draft.KnownSources[source.Id] = new(source.Id, source.Identity, source.ResultCount, source.OtherRelic);
    }
}
