using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class NeowSearchEditor
{
    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal SearchQuery ExportPartyConditions(int slot)
    {
        if (!_partyCatalogDrafts.TryGetValue(slot, out var draft)) return SearchQuery.Empty;
        var active = _draft;
        try { _draft = draft; return ExportQuery(); }
        finally { _draft = active; }
    }

    internal SearchQuery ExportQuery()
    {
        if (_source is null) return SearchQuery.Empty;
        SearchQuery query = _source == BaseGameModelKeys.Relics.NeowsBones ? BuildBonesQuery()
            : SearchQuery.Empty with { OpeningRoute = new(_source.Value) };
        var effects = query.StructuredOpeningEffects.ToList();
        IEnumerable<ModelKey> sources = _source == BaseGameModelKeys.Relics.NeowsBones ? _bones : [_source.Value];
        foreach (var source in sources)
        {
            if (source == BaseGameModelKeys.Relics.Kaleidoscope || source == BaseGameModelKeys.Relics.ScrollBoxes)
            {
                effects.RemoveAll(c => c.SourceRelicKey == source);
                var advanced = AdvancedSourceCondition(source);
                if (advanced is not null) effects.Add(advanced);
                else if (source == BaseGameModelKeys.Relics.ScrollBoxes)
                {
                    var cards = Enumerable.Range(0, 3).Select(i => _slots.GetValueOrDefault($"{source}/bundle/{i}"))
                        .Where(k => k.HasValue).Select(k => k!.Value).ToArray();
                    if (cards.Length > 0) effects.Add(new(source, NeowStructuredConditionKind.StructuredCardComposition,
                        NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, cards));
                }
                continue;
            }
            if (_source == BaseGameModelKeys.Relics.NeowsBones) continue;
            if (TManagesTransform() && source is var transformSource &&
                (transformSource == BaseGameModelKeys.Relics.LeafyPoultice || transformSource == BaseGameModelKeys.Relics.NewLeaf)) continue;
            foreach (var component in NeowEffectCardRegistry.Get(source).Components)
            {
                var targets = Enumerable.Range(0, component.SlotCount)
                    .Select(i => _slots.GetValueOrDefault($"{source}/{component.ComponentId}/{i}"))
                    .Where(k => k.HasValue).Select(k => k!.Value).ToArray();
                if (targets.Length > 0) effects.Add(new(source, component.ConditionKind, component.Scope,
                    component.OutputKind, targets, AllowDuplicateOutputs: component.AllowDuplicateOutputs));
            }
        }
        return query with { StructuredOpeningEffects = effects.ToArray() };
    }

    internal void ImportQuery(SearchQuery query)
    {
        _draft.Source = query.OpeningRoute?.RouteRelicKey;
        _bones.Clear(); _slots.Clear(); Array.Clear(_draft.TransformTargets); Array.Clear(_draft.CapsuleTargets);
        _draft.JointTransforms = _draft.JointCapsules = false;
        _draft.Curse = null; _draft.ScrollOffer = NeowSpecialOfferKind.None;
        _draft.KaleidoscopeOrder = KaleidoscopeGroupOrderMode.AnyOrder;
        _bones.AddRange(query.OpeningRouteRelicRequirement?.RequiredRelicKeys ?? []);
        _ordered = query.OpeningRouteRelicRequirement?.OrderMode == BonesRouteOrderMode.ExactOrder;
        foreach (var effect in query.StructuredOpeningEffects)
        {
            if (effect.Scope == NeowStructuredEffectScope.FinalCurse) { _draft.Curse = effect.OutputKeys.Single(); continue; }
            if (effect.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset)
            { _draft.JointCapsules = true; for (int i=0;i<effect.OutputKeys.Count;i++) _draft.CapsuleTargets[i]=effect.OutputKeys[i]; continue; }
            string component;
            IReadOnlyList<ModelKey?> values = effect.OutputKeys.Select(k => (ModelKey?)k).ToArray();
            if (effect.SourceRelicKey == BaseGameModelKeys.Relics.ScrollBoxes)
            { _draft.ScrollOffer = effect.SpecialOffer; component = "bundle"; }
            else if (effect.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope)
            {
                component = "independent-offers"; _draft.KaleidoscopeOrder = effect.KaleidoscopeGroupOrder;
                if (effect.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder) values = effect.KaleidoscopePositionalSlots;
            }
            else component = NeowEffectCardRegistry.Get(effect.SourceRelicKey).Components.Single(c =>
                c.Scope == effect.Scope && c.OutputKind == effect.OutputKind && c.ConditionKind == effect.Kind).ComponentId;
            for (int i = 0; i < values.Count; i++) if (values[i].HasValue) _slots[$"{effect.SourceRelicKey}/{component}/{i}"] = values[i];
        }
        if (query.TransformationAggregate is { Opening: TransformationOpening.BonesLeafyNewLeaf,
            Predicate: TransformationAggregatePredicate.ContainsMultiset, UsesEvents: false } joint)
        {
            _draft.JointTransforms = true;
            for (int i = 0; i < joint.TargetMultiset.Count; i++) _draft.TransformTargets[i] = joint.TargetMultiset[i];
        }
        InitializeAllocationDraftsFromQuery();
        _draft.Context = ""; // Next refresh binds legality against freshly captured context.
    }
}
