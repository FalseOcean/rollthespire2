using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;

namespace RolltheSpire2.Ui.Shell;

// Local adapters for the existing editors. No RNG or Family execution lives here.
internal sealed partial class NeowEditorPrototype
{
    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal SearchQuery ExportPartyConditions(int slot)
    {
        if (!_partyCatalogDrafts.TryGetValue(slot, out var draft)) return SearchQuery.Empty;
        var active = _draft;
        try { _draft = draft; return ExportQuery(); }
        finally { _draft = active; }
    }

    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }
    internal SearchQuery ExportQuery()
    {
        if (_source is null) return SearchQuery.Empty;
        if (_catalog is null) throw new InvalidOperationException("integration.context");
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
        _draft.Context = ""; // Next refresh binds legality against freshly captured context.
    }
}

internal sealed partial class RelicSequenceEditorPrototype
{
    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal IReadOnlyList<RelicSequenceSearchCondition> BuildPartyConditions(int slot)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return [];
        var active = _draft;
        try { _draft = draft; return BuildConditions(); }
        finally { _draft = active; }
    }

    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }
    internal bool AdvancedOpen { get => _draft.Builder.Advanced; set => _draft.Builder.Advanced = value; }
    internal void ImportQuery(SearchQuery q)
    {
        _draft.Conditions.Clear();
        foreach (var c in q.RelicSequenceConstraints)
        {
            foreach (var key in c.Keys.Any.Concat(c.Keys.All))
                _draft.Conditions.Add(new(Guid.NewGuid(), c.Lane, c.RangeMode, c.RangeValue, key, RelicSequenceUiMatchMode.Appears));
            foreach (var key in c.Keys.Ban)
                _draft.Conditions.Add(new(Guid.NewGuid(), c.Lane, c.RangeMode, c.RangeValue, key, RelicSequenceUiMatchMode.Excluded));
        }
        _draft.Context = "";
    }
}

internal sealed partial class CombatRewardEditorPrototype
{
    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal CombatCardRewardSequenceSearchCondition? ExportPartyCondition(int slot)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return null;
        var active = _draft;
        try { _draft = draft; return ExportCondition(); }
        finally { _draft = active; }
    }

    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }
    internal CombatCardRewardSequenceSearchCondition? ExportCondition()
    {
        if (!_draft.Slots.Any(k => k.HasValue)) return null;
        if (_draft.BattleRange > 6) throw new InvalidOperationException("integration.prototype.c");
        if (_catalog is null || !_catalog.CardCatalogAvailable) throw new InvalidOperationException("integration.context");
        return new(_draft.BattleRange, _draft.Unordered ? CombatRewardSequenceOrderMode.Unordered : CombatRewardSequenceOrderMode.Ordered,
            _draft.Slots.Take(_draft.BattleRange).ToArray());
    }
    internal void ImportQuery(SearchQuery q)
    {
        Array.Clear(_draft.Slots);
        _draft.BattleRange = q.CombatCardRewards?.Count ?? 3;
        _draft.Unordered = q.CombatCardRewards?.OrderMode == CombatRewardSequenceOrderMode.Unordered;
        if (q.CombatCardRewards is { } c) for (int i = 0; i < c.Count; i++) _draft.Slots[i] = c.Slots[i];
        Array.Fill(_draft.Potions, new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null));
        _draft.PotionRange = q.CombatPotionRewards?.Count ?? 3;
        _draft.PotionsUnordered = q.CombatPotionRewards?.OrderMode == CombatRewardSequenceOrderMode.Unordered;
        if (q.CombatPotionRewards is { } p) for (int i = 0; i < p.Count; i++) _draft.Potions[i] = p.Slots[i];
        _draft.Context = "";
    }
}

internal sealed partial class AncientEditorPrototype
{
    internal PartyAncientEditorStateSnapshot ExportSharedPartyEditorState() => new(_partyAdvanced,
        _partyIdentities[0].OrderBy(pair => pair.Key)
            .Select(pair => new PartyAncientIdentitySnapshot(pair.Key, pair.Value.ToArray())).ToArray(),
        _partyIdentities[1].OrderBy(pair => pair.Key)
            .Select(pair => new PartyAncientIdentitySnapshot(pair.Key, pair.Value.ToArray())).ToArray());

    internal SearchQuery ExportSharedPartyQuery(SearchQuery q) => q with
    {
        LegacyWorld = q.LegacyWorld with
        {
            AncientIdentityFilters = _partyIdentities[_partyAdvanced ? 1 : 0].OrderBy(pair => pair.Key)
                .Where(pair => pair.Value.Count > 0)
                .Select(pair => new ActModelKeySetFilter(pair.Key, new(pair.Value.ToArray(), [], []))).ToArray()
        }
    };

    internal void ImportSharedPartyQuery(SearchQuery q, PartyAncientEditorStateSnapshot? saved = null,
        IReadOnlyList<WorkbenchPlayerDraft>? players = null)
    {
        foreach (Dictionary<int, List<ModelKey>> mode in _partyIdentities)
            foreach (List<ModelKey> identities in mode.Values) identities.Clear();
        if (saved is not null)
        {
            static void Restore(IReadOnlyList<PartyAncientIdentitySnapshot> source,
                Dictionary<int, List<ModelKey>> target, int limit)
            {
                if (source.GroupBy(item => item.Act).Any(group => group.Count() > 1) ||
                    source.Any(item => item.Act is not (2 or 3) || item.Ancients.Count > limit ||
                        item.Ancients.Any(key => !key.IsValid) || item.Ancients.Distinct(ModelKeyComparer.Instance).Count() != item.Ancients.Count))
                    throw new InvalidOperationException("integration.load_shape");
                foreach (PartyAncientIdentitySnapshot item in source) target[item.Act].AddRange(item.Ancients);
            }
            Restore(saved.DefaultIdentities, _partyIdentities[0], 1);
            Restore(saved.AdvancedIdentities, _partyIdentities[1], 4);
            _partyAdvanced = saved.Advanced;
            foreach (int act in new[] { 2, 3 })
            {
                ModelKey[] authored = q.LegacyWorld.AncientIdentityFilters.Where(filter => filter.Act == act)
                    .SelectMany(filter => filter.Keys.Any).ToArray();
                ModelKey[] restored = _partyIdentities[_partyAdvanced ? 1 : 0][act].ToArray();
                if (!authored.ToHashSet(ModelKeyComparer.Instance).SetEquals(restored))
                    throw new InvalidOperationException("integration.load_shape");
            }
        }
        else
        {
            _partyAdvanced = players?.Any(player => player.AncientEditor?.ActiveMode > 0) ?? _partyAdvanced;
            foreach (int act in new[] { 2, 3 })
            {
                for (int mode = 0; mode < 2; mode++)
                {
                    var sets = players?.Select(player =>
                    {
                        AncientEditorStateSnapshot? editor = player.AncientEditor;
                        if (editor is null || editor.Modes.Count != 3) return Array.Empty<ModelKey>();
                        int personalMode = mode == 0 ? 0 : editor.ActiveMode == 0 ? editor.LastAdvancedMode : editor.ActiveMode;
                        return editor.Modes[personalMode].Rows.Where(row => row.Act == act)
                            .Select(row => row.Ancient).Distinct(ModelKeyComparer.Instance).ToArray();
                    }).Where(set => set.Length > 0).ToArray() ?? [];
                    ModelKey[] common = sets.Length == 0 ? [] : sets.Skip(1)
                        .Aggregate(sets[0].AsEnumerable(), (keys, set) => keys.Intersect(set, ModelKeyComparer.Instance))
                        .ToArray();
                    bool activeWithoutSharedFilter = mode == (_partyAdvanced ? 1 : 0) &&
                        !q.LegacyWorld.AncientIdentityFilters.Any(filter => filter.Act == act);
                    if (sets.Length > 0 && common.Length == 0 && activeWithoutSharedFilter)
                        throw new InvalidOperationException("integration.load_shape");
                    _partyIdentities[mode][act].AddRange(common);
                }
                ActModelKeySetFilter? authored = q.LegacyWorld.AncientIdentityFilters.FirstOrDefault(filter => filter.Act == act);
                if (authored is not null)
                {
                    List<ModelKey> current = _partyIdentities[_partyAdvanced ? 1 : 0][act];
                    current.Clear(); current.AddRange(authored.Keys.Any);
                }
            }
        }
        foreach (SeatDraft draft in _partyDrafts.Values) draft.Advanced = _partyAdvanced;
    }

    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal SearchQuery ExportPartyQuery(int slot, SearchQuery q)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return q;
        var active = _draft;
        try { _draft = draft; return ExportQuery(q); }
        finally { _draft = active; }
    }
    internal AncientOptionConditionProfile PartyOptionConditions(int slot) =>
        _partyDrafts.TryGetValue(slot, out var draft) ? draft.Eligibility : AncientOptionConditionProfile.BroadDefault;

    internal AncientEditorStateSnapshot ExportPartyEditorState(int slot)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return ExportEditorState();
        var active = _draft;
        try { _draft = draft; return ExportEditorState(); }
        finally { _draft = active; }
    }

    internal AncientEditorStateSnapshot ExportEditorState() => new(_draft.Mode, _draft.LastAdvancedMode,
        _draft.Modes.Select(mode => new AncientEditorModeSnapshot(
            mode.Acts.SelectMany(pair => pair.Value.Select(row => new AncientEditorRowSnapshot(
                pair.Key, row.Ancient, row.Options.ToArray(), row.SeaGlassTarget))).ToArray(),
            mode.NeowEnabled, mode.NeowOptions.ToArray(), mode.SelectedAct)).ToArray());

    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }
    internal bool AdvancedOpen
    {
        get => _multiplayer ? _partyAdvanced : _draft.Advanced;
        set
        {
            if (!_multiplayer) { _draft.Advanced = value; return; }
            _partyAdvanced = value;
            foreach (SeatDraft draft in _partyDrafts.Values) draft.Advanced = value;
        }
    }
    internal SearchQuery ExportQuery(SearchQuery q)
    {
        var branches = new List<AncientSearchBranchCondition>();
        var all = new List<ActModelKeySetFilter>();
        foreach (int act in new[] { 2, 3 })
        {
            IEnumerable<AncientDraft> rows = _multiplayer
                ? SharedAncients(act).Select(key => GetOrAddAncientRow(_draft.Current, act, key))
                : _draft.Acts[act];
            foreach (AncientDraft row in rows)
            {
                bool together = _draft.RequireAll;
                branches.Add(new(act, row.Ancient, together ? [] : row.Options.ToArray(),
                    row.SeaGlassTarget is { } target ? [target] : []));
                // Sea Glass's target requires its parent option in the modern branch.
                if (together && row.SeaGlassTarget.HasValue)
                    branches[^1] = branches[^1] with { OptionAny = row.Options.Where(k => k.Entry == "SEA_GLASS").ToArray() };
                if (together && row.Options.Count > 0) all.Add(new(act, new([], row.Options.ToArray(), [])));
            }
        }
        ModelKeySetFilter neow = _draft.Mode == 0 || !_draft.NeowEnabled ? ModelKeySetFilter.Empty : _draft.OfferedTogether
            ? new([], _draft.NeowOptions.ToArray(), []) : new(_draft.NeowOptions.ToArray(), [], []);
        return q with { AncientBranches = branches.ToArray(),
            LegacyWorld = q.LegacyWorld with { AncientOptionFilters = all.ToArray() },
            LegacyNeow = q.LegacyNeow with { NeowRelics = neow } };
    }
    internal void ImportQuery(SearchQuery q, AncientOptionConditionProfile premises,
        AncientEditorStateSnapshot? saved = null)
    {
        _draft.Eligibility = premises;
        foreach (ModeDraft mode in _draft.Modes)
        {
            mode.Acts[2].Clear(); mode.Acts[3].Clear(); mode.NeowOptions.Clear();
            mode.NeowEnabled = false; mode.SelectedAct = 1;
        }
        if (saved is not null)
        {
            if (saved.Modes.Count != 3 || saved.ActiveMode is < 0 or > 2 ||
                saved.LastAdvancedMode is < 1 or > 2 || saved.Modes.Any(mode =>
                    mode.SelectedAct is < 1 or > 3 || mode.Rows.Any(row => row.Act is not (2 or 3))))
                throw new InvalidOperationException("integration.load_shape");
            for (int index = 0; index < 3; index++)
            {
                AncientEditorModeSnapshot source = saved.Modes[index];
                ModeDraft target = _draft.Modes[index];
                foreach (AncientEditorRowSnapshot row in source.Rows)
                {
                    var restored = new AncientDraft(row.Ancient) { SeaGlassTarget = row.SeaGlassTarget };
                    restored.Options.AddRange(row.Options);
                    target.Acts[row.Act].Add(restored);
                }
                target.NeowEnabled = source.NeowEnabled;
                target.NeowOptions.AddRange(source.NeowOptions);
                target.SelectedAct = source.SelectedAct;
            }
            _draft.Mode = saved.ActiveMode;
            _draft.LastAdvancedMode = saved.LastAdvancedMode;
        }
        else
        {
            bool together = q.LegacyWorld.AncientOptionFilters.Any(c => c.Keys.All.Count > 0) ||
                            q.LegacyNeow.NeowRelics.All.Count > 0;
            bool advancedAny = q.LegacyNeow.NeowRelics.Any.Count > 0 ||
                               q.AncientBranches.GroupBy(branch => branch.Act).Any(group => group.Count() > 1) ||
                               q.AncientBranches.Any(branch => branch.OptionAny.Count > 1);
            _draft.Mode = together ? 2 : advancedAny ? 1 : 0;
            _draft.LastAdvancedMode = _draft.Mode == 0 ? 2 : _draft.Mode;
            ModeDraft target = _draft.Current;
            foreach (AncientSearchBranchCondition branch in q.AncientBranches)
            {
                var row = new AncientDraft(branch.AncientKey)
                { SeaGlassTarget = branch.SeaGlassTargetAny.Select(key => (ModelKey?)key).FirstOrDefault() };
                row.Options.AddRange(together
                    ? q.LegacyWorld.AncientOptionFilters.Where(filter => filter.Act == branch.Act)
                        .SelectMany(filter => filter.Keys.All).Distinct(ModelKeyComparer.Instance)
                    : branch.OptionAny);
                target.Acts[branch.Act].Add(row);
            }
            target.NeowOptions.AddRange(q.LegacyNeow.NeowRelics.Any.Concat(q.LegacyNeow.NeowRelics.All));
            target.NeowEnabled = target.NeowOptions.Count > 0;
        }
        _draft.Context = "";
    }
}

internal sealed partial class ActInformationEditorPrototype
{
    internal bool AdvancedOpen { get => _draft.AdvancedProperties; set => _draft.AdvancedProperties = value; }
    internal SearchQuery ExportQuery(SearchQuery q)
    {
        var maps = _draft.Routes.Select(kv => new StandardMapSearchCondition(kv.Key,
            Enum.Parse<StandardMapMetric>(kv.Value.Metric.ToString()), StandardMapComparison.AtLeast, kv.Value.Value, true))
            .Concat(_draft.Properties.SelectMany(kv => kv.Value.Select(c => new StandardMapSearchCondition(kv.Key,
                Enum.Parse<StandardMapMetric>(c.Metric.ToString()),Enum.Parse<StandardMapComparison>(c.Comparison.ToString()),c.Value)))).ToArray();
        var branches = new List<VariantScopedBossBranch>();
        for (int act = 1; act <= 3; act++)
        {
            var variants = _draft.SelectedVariants[act - 1]; var bosses = _draft.SelectedBosses[act - 1];
            if (variants.Count == 0 && bosses.Count == 0) continue;
            foreach (var v in Section(act)?.Variants ?? [])
            {
                if (variants.Count > 0 && !variants.Contains(v.ActKey)) continue;
                var keys = bosses.Where(v.Bosses.Contains).OrderBy(k => k.Serialized, StringComparer.Ordinal).ToArray();
                if (bosses.Count > 0 && keys.Length == 0) continue;
                branches.Add(new(act, v.ActKey, new(keys, [], []), ModelKeySetFilter.Empty, false));
            }
        }
        return q with { VariantBossBranches = branches.ToArray(), StandardMaps = maps };
    }
    internal void ImportQuery(SearchQuery q)
    {
        _draft.Routes.Clear(); _draft.Properties.Clear();
        foreach (var c in q.StandardMaps)
        {
            var metric=Enum.Parse<MapMetricKind>(c.Metric.ToString());
            if(c.RouteObjective) _draft.Routes[c.Scope]=new(metric,c.Value);
            else { if(!_draft.Properties.TryGetValue(c.Scope,out var rows)) _draft.Properties[c.Scope]=rows=[];
                rows.Add(new(metric,Enum.Parse<MapMetricComparison>(c.Comparison.ToString()),c.Value)); }
        }
        foreach (var set in _draft.SelectedVariants.Concat(_draft.SelectedBosses)) set.Clear();
        foreach (var b in q.VariantBossBranches)
        { _draft.SelectedVariants[b.Act - 1].Add(b.VariantKey); _draft.SelectedBosses[b.Act - 1].UnionWith(b.FirstBoss.Any); }
        _draft.Context = "";
    }
}

internal sealed partial class ShopEditorPrototype
{
    // Capture stored authoring state without refreshing controls or closing the shared picker.
    internal SearchQuery ExportPartyQuery(int slot, SearchQuery q)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return q;
        var active = _draft;
        try { _draft = draft; return ExportQuery(q); }
        finally { _draft = active; }
    }

    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }
    internal SearchQuery ExportQuery(SearchQuery q)
    {
        var cards = new List<MerchantColorlessSequenceSearchCondition>();
        foreach (var (row, slot) in new[] { (_draft.Uncommon, MerchantColorlessSlot.Uncommon), (_draft.Rare, MerchantColorlessSlot.Rare) })
            if (row.Slots.Any(k => k.HasValue)) cards.Add(new(_draft.ShopRange, row.OrderMode, slot, row.Slots.Take(_draft.ShopRange).ToArray()));
        return q with { MerchantColorlessSequenceConditions = cards.ToArray(), RelicShopSequenceConditions =
            _draft.Relic.Slots.Any(k => k.HasValue) ? [new(_draft.ShopRange, _draft.Relic.OrderMode, _draft.Relic.Slots.Take(_draft.ShopRange).ToArray())] : [] };
    }
    internal void ImportQuery(SearchQuery q)
    {
        Array.Clear(_draft.Relic.Slots); Array.Clear(_draft.Uncommon.Slots); Array.Clear(_draft.Rare.Slots);
        foreach (var c in q.MerchantColorlessSequenceConditions)
        { var row = c.Slot == MerchantColorlessSlot.Rare ? _draft.Rare : _draft.Uncommon; _draft.ShopRange = c.Count; row.OrderMode = c.OrderMode; for(int i=0;i<c.Count;i++) row.Slots[i]=c.Slots[i]; }
        foreach (var c in q.RelicShopSequenceConditions)
        { _draft.ShopRange=c.Count; _draft.Relic.OrderMode=c.OrderMode; for(int i=0;i<c.Count;i++) _draft.Relic.Slots[i]=c.Slots[i]; }
        _draft.Context = "";
    }
}
