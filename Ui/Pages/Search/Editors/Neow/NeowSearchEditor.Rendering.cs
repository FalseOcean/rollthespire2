using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class NeowSearchEditor
{
    private void RenderEditor()
    {
        _presentation.Begin();
        Clear(_body); _body.Size = Size;
        if (_removed > 0 || _allocationNotice.Length > 0)
        {
            _notice = Text(_body, _allocationNotice.Length > 0 ? _text.Get(_allocationNotice) :
                _text.Format("query.neow.notice.removed_invalid", _removed), 4, Size.Y - 32, Size.X - 8, 17);
            _notice.ZIndex = 10;
        }
        var guide = FamilyGuide(_body, Size.X - 20);
        var panes = Source(_body, Size.X - 20, guide);
        _presentation.Finish();
        AlignSourcePanels(panes.Identity, panes.Results);
    }

    private VBoxContainer FamilyGuide(Control host, float width) =>
        SearchEditorGuide.Build(host, _p, width, _text.Get("query.neow.guide.title"),
            _text.Get("query.neow.guide.summary"),
            [_text.Get("query.neow.guide.usage"), _text.Get("query.neow.guide.pickup_order"),
                _players == 1 ? _text.Get("query.neow.guide.choices") : string.Empty,
                _text.Get("query.neow.guide.transform_scope")],
            _guideExpanded, () => { _guideExpanded = !_guideExpanded; RenderEditor(); }, () => GuideRequested?.Invoke());

    // Retained research surface; not reachable from the current N editor.
    private void RenderResultOnlyPrototype()
    {
        var scroll = new ScrollContainer { Position = new(0, 58), Size = new(Size.X, Size.Y - 58), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.AddChild(scroll);
        var content = new Control { CustomMinimumSize = new(Size.X - 20, 1) }; scroll.AddChild(content);
        float width = Size.X - 28;
        if (_catalog is null) { Text(content, _problem, 0, 12, width); return; }
        float bottom = Results(content, width);
        content.CustomMinimumSize = new(width, bottom + 20);
    }

    private static void AlignSourcePanels(Control identity, Control results)
    {
        var sourcePanel = identity.GetChildren().OfType<Panel>().Single();
        var resultPanel = results.GetChildren().OfType<Panel>().Single();
        float height = Math.Max(sourcePanel.Size.Y, resultPanel.Size.Y);
        sourcePanel.Size = new(sourcePanel.Size.X, height);
        resultPanel.Size = new(resultPanel.Size.X, height);
        float bottom = Math.Max(sourcePanel.Position.Y, resultPanel.Position.Y) + height + 4;
        identity.CustomMinimumSize = new(identity.CustomMinimumSize.X, bottom);
        results.CustomMinimumSize = new(results.CustomMinimumSize.X, bottom);
    }

    private (Control Identity, Control Results) Source(Control host, float width, Control guide)
    {
        const float identityWidth = 276, gap = 20;
        const int identityColumns = 5;
        float resultsWidth = width - identityWidth - gap - 12;
        Control Pane(float x, float paneWidth)
        {
            var scroll = new ScrollContainer { Name = x == 0 ? "NeowSources" : "NeowResults",
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            void LayoutPane()
            {
                float top = guide.Size.Y + 8;
                scroll.Position = new(x, top);
                scroll.Size = new(paneWidth + 12, Math.Max(0, Size.Y - top));
            }
            guide.Resized += LayoutPane;
            LayoutPane();
            host.AddChild(scroll);
            var content = new Control { CustomMinimumSize = new(paneWidth, 1) };
            scroll.AddChild(content); return content;
        }
        var identity = Pane(0, identityWidth);
        var results = Pane(identityWidth + gap, resultsWidth);
        _presentation.Section(identity, 0, float.MaxValue / 4, _text.Get("layout.sources"));
        _presentation.Section(results, 0, float.MaxValue / 4, _text.Get("query.neow.results.benefits"));
        var bonesKey = BaseGameModelKeys.Relics.NeowsBones;
        var bonesButton = Button(identity, "", 8, 0, identityWidth - 16,
            ToggleBonesMode, _source == bonesKey, 48);
        bonesButton.Disabled = _source != bonesKey && !IdentityAllowed(bonesKey);
        bonesButton.Modulate = bonesButton.Disabled ? new Color(1, 1, 1, .52f) : Colors.White;
        Picture(bonesButton, bonesKey, 10, 5, 38);
        Text(bonesButton, NameOf(bonesKey), 60, 10, identityWidth - 88, 18);
        float y = 56;
        bool HasRandomResult(ModelKey key) => NeowEffectCardRegistry.Get(key).HasEffectCard || key == Relic("MASSIVE_SCROLL");
        var ordinary = Sources.Where(k => k != bonesKey).ToArray();
        foreach (bool random in new[] { true, false })
        {
            string heading = random && _source == bonesKey
                ? _text.Format("query.neow.bones.inner_relics", _bones.Count)
                : _text.Get(random ? "query.neow.source.random_results" : "common.other");
            Text(identity, heading, 8, y, identityWidth - 16, 15, true);
            y += 24;
            var keys = ordinary.Where(k => HasRandomResult(k) == random).ToArray();
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                bool selectedIdentity = _source == bonesKey ? _bones.Contains(key) : _source == key;
                var b = Button(identity, "", 8 + i % identityColumns * 52, y + i / identityColumns * 52, 48,
                    () => OnSourceRelicPressed(key), selectedIdentity, 48);
                b.Disabled = !selectedIdentity && (!IdentityAllowed(key) || (_source == bonesKey && _bones.Count >= 2));
                b.Modulate = b.Disabled ? new Color(1f, 1f, 1f, 0.52f) : Colors.White;
                Picture(b, key, 4, 4, 40);
                SearchTooltip.Set(b, NameOf(key)); b.AccessibilityName = NameOf(key);
            }
            y += (int)Math.Ceiling(keys.Length / (double)identityColumns) * 52 + (random ? 8 : -4);
        }
        identity.CustomMinimumSize = new(identityWidth, y);
        if (_catalog is null)
        {
            Text(results, _problem, 0, 0, resultsWidth, 18, true);
            results.CustomMinimumSize = new(resultsWidth, CurrentSourceConflicts(results, 40, resultsWidth)); return (identity, results);
        }
        float end = 0;
        if (_source == bonesKey)
        {
            Picture(results, bonesKey, 8, 4, 40);
            Text(results, NameOf(bonesKey), 60, 9, resultsWidth - 330, 22);
            var order = ModeButton(results, _text.Get(_ordered ? "query.neow.pickup_order.ordered" : "query.neow.pickup_order.any"),
                resultsWidth - 256, 0, 196, () => SetBonesOrder(!_ordered), _ordered);
            order.Disabled = _bones.Count != 2;
            SearchTooltip.Set(order, _text.Get(_ordered ? "query.neow.pickup_order.ordered_tooltip" : "query.neow.pickup_order.any_tooltip"));
            var swap = Button(results, "⇄", resultsWidth - 48, 0, 40, SwapBonesOrder);
            swap.AccessibilityName = _text.Get("common.swap");
            SearchTooltip.Set(swap, swap.AccessibilityName);
            swap.Disabled = !_ordered || _bones.Count != 2;

            const float siblingGap = 20;
            float childWidth = (resultsWidth - siblingGap) / 2;
            bool pairMode = HasTransformPair || HasCapsulePair;
            bool joint = HasTransformPair ? _draft.JointTransforms : HasCapsulePair && _draft.JointCapsules;
            bool alignOrderRows = !joint && _bones.Count == 2 && _bones.Any(child =>
                NeowEffectCardRegistry.Get(child).Components.Any(component => component.SupportsOrderSelection));
            float childTop = pairMode ? 104 : 52;
            float outputTop = childTop + 68;
            if (pairMode)
            {
                Text(results, _text.Get("query.neow.results.allocation"), 8, 62, 96, 16, true);
                float modeWidth = (resultsWidth - 132) / 2;
                void SelectAllocation(bool value)
                {
                    if (HasTransformPair) SetTransformAllocation(value);
                    else SetCapsuleAllocation(value);
                    RenderAfterEdit();
                }
                var separate = ModeButton(results, _text.Get("query.neow.results.separate"), 104, 52, modeWidth, () => SelectAllocation(false), !joint);
                SearchTooltip.Set(separate, _text.Get("query.neow.results.separate_tooltip"));
                var combined = ModeButton(results, _text.Get("query.neow.results.combined"), 116 + modeWidth, 52, modeWidth, () => SelectAllocation(true), joint);
                SearchTooltip.Set(combined, _text.Get("query.neow.results.combined_tooltip"));
                combined.Disabled = HasTransformPair && _players != 1;
            }
            end = outputTop;
            for (int i = 0; i < 2; i++)
            {
                float x = i * (childWidth + siblingGap);
                Text(results, _text.Format("query.neow.results.benefit", i + 1), x + 8, childTop, childWidth - 16, 14, true);
                if (i >= _bones.Count)
                {
                    Text(results, _language == "zh" ? "不限 · 从左侧选择" : "Any · choose on the left", x + 8, childTop + 30, childWidth - 16, 16, true);
                    continue;
                }
                var child = _bones[i];
                if (!joint) ResultSourceHeader(results, child, x + 8, childTop + 26, childWidth - 16, 40, (_ordered ? $"{i + 1} · " : ""));
                else
                {
                    Picture(results, child, x + 8, childTop + 26, 40);
                    Text(results, (_ordered ? $"{i + 1} · " : "") + NameOf(child), x + 60, childTop + 32, childWidth - 68, 18);
                }
                if (!joint) end = Math.Max(end, Outputs(results, child, x + 8, outputTop, childWidth - 16, alignOrderRows));
            }
            if (joint)
            {
                bool transforms = HasTransformPair;
                var targets = transforms ? _draft.TransformTargets : _draft.CapsuleTargets;
                Text(results, _text.Get(transforms ? "query.neow.results.combined_cards" : "query.neow.results.combined_relics"), 8, outputTop, resultsWidth - 16, 18, true);
                if (transforms && TManagesTransform())
                {
                    Text(results, _text.Get("query.transform.managed"), 8, outputTop + 34, resultsWidth - 16, 16, true);
                    end = outputTop + 76;
                }
                else
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int slot = i;
                        ResultObject(results, targets[i], transforms ? NeowStructuredOutputKind.Card : NeowStructuredOutputKind.Relic,
                            8 + i * (ResultObjectWidth + 12), outputTop + 32,
                            () => Pick(transforms ? JointTransformPool : CapsuleCandidates(slot), k => { targets[slot] = k; RenderAfterEdit(); }),
                            () => { targets[slot] = null; RenderAfterEdit(); });
                    }
                    end = outputTop + 32 + ResultObjectHeight;
                }
            }
            end += 12;
            Text(results, _text.Get("query.neow.results.final_curse"), 8, end, resultsWidth - 16, 18, true);
            var curse = new WorkspaceResultTile(_p, _icons, _names, _text, _bonesFinalCurse, GameContentKind.Card,
                () => Pick(_catalog!.Curses, k => { _bonesFinalCurse = k; RenderAfterEdit(); }),
                () => { _bonesFinalCurse = null; RenderAfterEdit(); }, compact: true, compactWidth: 160)
                { Position = new(8, end + 28) };
            results.AddChild(curse);
            end += 28 + 64 + 8;
        }
        else if (_source is { } source)
        {
            ResultSourceHeader(results, source, 8, end, resultsWidth - 16, 48);
            end = Outputs(results, source, 8, end + 56, resultsWidth - 16) + 8;
        }
        else
        {
            var hint = _p.Label(_text.Get("query.neow.results.empty"), 17, true);
            hint.Position = new(16, 24); hint.Size = new(resultsWidth - 32, 80);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; results.AddChild(hint);
            end = 120;
        }
        end = CurrentSourceConflicts(results, end + 8, resultsWidth);
        results.CustomMinimumSize = new(resultsWidth, Math.Max(1, end));
        return (identity, results);
    }

    private void ResultSourceHeader(Control parent, ModelKey source, float x, float y, float width, float iconSize, string prefix = "")
    {
        var header = Button(parent, "", x, y, width, () =>
        {
            foreach (string id in _slots.Keys.Where(id => id.StartsWith(source + "/", StringComparison.Ordinal)).ToArray()) _slots.Remove(id);
            if (source == BaseGameModelKeys.Relics.ScrollBoxes) _draft.ScrollOffer = NeowSpecialOfferKind.None;
            RenderAfterEdit();
        }, height: iconSize);
        header.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        SearchTooltip.Set(header, _text.Get("query.neow.results.clear_nested"));
        header.AccessibilityName = NameOf(source) + " · " + _text.Get("query.neow.results.clear_nested");
        Picture(header, source, 0, 0, iconSize);
        Text(header, prefix + NameOf(source), iconSize + 12, (iconSize - 28) / 2, width - iconSize - 12, iconSize >= 40 ? 22 : 18);
    }

    private float Outputs(Control parent, ModelKey source, float x, float y, float width,
        bool alignOrderRow = false)
    {
        bool suspendedTransform = _source == BaseGameModelKeys.Relics.NeowsBones && _draft.JointTransforms && !HasTransformPair &&
            (source == BaseGameModelKeys.Relics.LeafyPoultice || source == BaseGameModelKeys.Relics.NewLeaf);
        bool suspendedCapsule = _source == BaseGameModelKeys.Relics.NeowsBones && _draft.JointCapsules && !HasCapsulePair && IsCapsule(source);
        if (suspendedTransform || suspendedCapsule)
        {
            var affected = _p.Label(_text.Get("query.neow.results.combined_source_changed"), 15, true);
            affected.Position = new(x, y); affected.Size = new(width, 54);
            affected.AutowrapMode = TextServer.AutowrapMode.WordSmart; parent.AddChild(affected);
            ModeButton(parent, _text.Get("query.neow.results.separate"), x, y + 58, width,
                () => { if (suspendedTransform) SetTransformAllocation(false); else SetCapsuleAllocation(false); RenderAfterEdit(); }, false);
            return y + 106;
        }
        var components = NeowEffectCardRegistry.Get(source).Components;
        // A sibling with an order selector uses this row. Reserve the same space
        // before the other sibling's content so their labels and result slots align.
        if (alignOrderRow && !components.Any(component => component.SupportsOrderSelection)) y += 48;
        // Exact Kaleidoscope groups have a caption above each slot. Its sibling
        // needs the same caption space without acquiring group/order semantics.
        float siblingCaptionHeight = alignOrderRow && _draft.KaleidoscopeOrder == KaleidoscopeGroupOrderMode.ExactOrder ? 26 : 0;
        if (source is { } && (source == BaseGameModelKeys.Relics.LeafyPoultice || source == BaseGameModelKeys.Relics.NewLeaf)
            && TManagesTransform())
        {
            Text(parent, _text.Get("query.event.results.transform_targets"), x, y, width, 18, true);
            Text(parent, _text.Get("query.transform.managed"), x, y + 32, width, 16, true);
            return y + 78;
        }
        int columns = Math.Max(1, (int)((width + 12) / (ResultObjectWidth + 12)));
        if (source == BaseGameModelKeys.Relics.ScrollBoxes)
        {
            if (_catalog!.CharacterKey == BaseGameModelKeys.Characters.Defect)
            {
                bool triple = _draft.ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw;
                float modeWidth = (width - 12) / 2;
                ModeButton(parent, _text.Get("query.neow.scroll.normal_bundle"), x, y, modeWidth,
                    () => { _draft.ScrollOffer = NeowSpecialOfferKind.None; RenderAfterEdit(); }, !triple);
                Button(parent, _text.Get("query.neow.scroll.triple_claw"), x + modeWidth + 12, y, modeWidth,
                    () => { _draft.ScrollOffer = NeowSpecialOfferKind.ScrollBoxesTripleClaw; RenderAfterEdit(); }, triple);
                y += 48;
                if (triple)
                {
                    var claw = new ModelKey("CARD", "CLAW");
                    for (int i = 0; i < 3; i++)
                    {
                        var view = new Panel { Position = new(x + i % columns * (ResultObjectWidth + 12), y + i / columns * (ResultObjectHeight + 12)),
                            Size = new(ResultObjectWidth, ResultObjectHeight) };
                        parent.AddChild(view);
                        view.AddThemeStyleboxOverride("panel", _p.Box(_p.Surface));
                        ResultContents(view, claw, NeowStructuredOutputKind.Card);
                    }
                    return y + (int)Math.Ceiling(3d / columns) * (ResultObjectHeight + 12);
                }
            }
            Text(parent, _text.Get("object.cards"), x, y, width, 18, true); y += 32 + siblingCaptionHeight;
            for (int i = 0; i < 3; i++)
                OutputSlot(parent, source, "bundle", i, ScrollBundlePool,
                    x + i % columns * (ResultObjectWidth + 12), y + i / columns * (ResultObjectHeight + 12), ResultObjectWidth);
            y += (int)Math.Ceiling(3d / columns) * (ResultObjectHeight + 12);
            return y;
        }
        if (components.Count == 0) return y;
        foreach (var c in components)
        {
            bool ordered = c.SupportsOrderSelection && _draft.KaleidoscopeOrder == KaleidoscopeGroupOrderMode.ExactOrder;
            if (c.SupportsOrderSelection)
            {
                var order = ModeButton(parent, _text.Get(ordered ? "query.neow.generation_order.exact" : "query.neow.generation_order.any"), x, y, width - 52,
                    () => { _draft.KaleidoscopeOrder = ordered ? KaleidoscopeGroupOrderMode.AnyOrder : KaleidoscopeGroupOrderMode.ExactOrder; RenderAfterEdit(); }, ordered);
                SearchTooltip.Set(order, _text.Get(ordered ? "query.neow.generation_order.exact_tooltip" : "query.neow.generation_order.any_tooltip"));
                var swap = Button(parent, "⇄", x + width - 40, y, 40,
                    () => { SwapKaleidoscopeTargets(); RenderAfterEdit(); });
                swap.AccessibilityName = _text.Get("common.swap");
                SearchTooltip.Set(swap, swap.AccessibilityName);
                swap.Disabled = !ordered;
                y += 48;
            }
            string title = c.OutputKind.ToString() switch { "Relic" => _text.Get("object.relics"), "Potion" => _text.Get("object.potions"), _ => _text.Get("object.cards") };
            if (source == BaseGameModelKeys.Relics.NewLeaf) title = _text.Get("query.neow.results.new_leaf");
            var heading = Text(parent, title, x, y, width, 18, true);
            if (source == BaseGameModelKeys.Relics.NewLeaf)
                SearchTooltip.Set(heading, _text.Get("query.neow.results.new_leaf_tooltip"));
            else if (c.SupportsOrderSelection)
                SearchTooltip.Set(heading, _text.Get("query.neow.results.offer_groups_tooltip"));
            else if (c.Unordered)
                SearchTooltip.Set(heading, _text.Get("query.neow.results.unordered_tooltip"));
            heading.MouseFilter = MouseFilterEnum.Pass;
            y += 32;
            bool objectView = c.OutputKind is NeowStructuredOutputKind.Card or NeowStructuredOutputKind.Relic or NeowStructuredOutputKind.Potion;
            int slotColumns = objectView ? columns : width >= 440 ? 2 : 1;
            float slotWidth = objectView ? ResultObjectWidth : (width - (slotColumns - 1) * 12) / slotColumns;
            float captionHeight = ordered ? 26 : siblingCaptionHeight;
            float rowHeight = (objectView ? ResultObjectHeight + 12 : 68) + captionHeight;
            for (int i = 0; i < c.SlotCount; i++)
            {
                float top = y + i / slotColumns * rowHeight;
                if (ordered) Text(parent, _text.Format("query.neow.results.group", i + 1), x + i % slotColumns * (slotWidth + 12), top, slotWidth, 16, true);
                OutputSlot(parent, source, c.ComponentId, i, _catalog!.Candidates(c.CandidatePool),
                    x + i % slotColumns * (slotWidth + 12), top + captionHeight, slotWidth);
            }
            y += (int)Math.Ceiling(c.SlotCount / (double)slotColumns) * rowHeight;
        }
        return y;
    }
    // Inactive mode/source drafts stay stored, but only a conflict in the
    // currently selected source is rendered here.
    private float CurrentSourceConflicts(Control parent, float y, float width)
    {
        ModelKey[] activeSources = _source == BaseGameModelKeys.Relics.NeowsBones ? _bones.ToArray()
            : _source is { } selected ? [selected] : [];
        bool managed = TManagesTransform();
        var conflicts = new List<(string Caption, ModelKey[] Targets)>();
        foreach (var group in _slots.Where(s => s.Value.HasValue).GroupBy(s => s.Key.Split('/')[0]))
        {
            if (!ModelKey.TryParseExact(group.Key, out var source)) continue;
            bool transform = IsTransformSlot(group.First().Key);
            bool sourceManaged = activeSources.Contains(source) && transform && managed;
            bool dormant = !activeSources.Contains(source) || sourceManaged ||
                _source == BaseGameModelKeys.Relics.NeowsBones && (transform && _draft.JointTransforms || IsCapsule(source) && _draft.JointCapsules) ||
                source == BaseGameModelKeys.Relics.ScrollBoxes && _draft.ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw;
            if (!dormant && _catalog is not null)
            {
                var unavailable = group.Where(slot =>
                {
                    string component = slot.Key.Split('/')[1];
                    var definition = NeowEffectCardRegistry.Get(source).Components.FirstOrDefault(c => c.ComponentId == component);
                    IReadOnlyList<ModelKey> pool = definition is null && source == BaseGameModelKeys.Relics.ScrollBoxes ? ScrollBundlePool
                        : definition is null ? [] : _catalog.Candidates(definition.CandidatePool);
                    return !pool.Contains(slot.Value!.Value);
                }).Select(s => s.Value!.Value).ToArray();
                if (unavailable.Length > 0) conflicts.Add((_text.Format("query.neow.saved.pool_changed", NameOf(source)), unavailable));
            }
        }
        void Combined(bool transforms)
        {
            var targets = transforms ? _draft.TransformTargets : _draft.CapsuleTargets;
            bool changed = _source == BaseGameModelKeys.Relics.NeowsBones && (transforms
                ? _draft.JointTransforms && !HasTransformPair : _draft.JointCapsules && !HasCapsulePair);
            if (!changed || !targets.Any(k => k.HasValue)) return;
            string sources = transforms ? NameOf(BaseGameModelKeys.Relics.LeafyPoultice) + " + " + NameOf(BaseGameModelKeys.Relics.NewLeaf)
                : NameOf(BaseGameModelKeys.Relics.SmallCapsule) + " + " + NameOf(BaseGameModelKeys.Relics.LargeCapsule);
            sources += " · " + _text.Get("query.neow.results.combined");
            conflicts.Add((_text.Format("query.neow.saved.source_changed", sources),
                targets.Where(k => k.HasValue).Select(k => k!.Value).ToArray()));
        }
        Combined(true); Combined(false);
        foreach (var requirement in conflicts)
        {
            var caption = _p.Label(requirement.Caption, 14, true);
            caption.Name = "NeowSourceConflict";
            caption.Position = new(8, y); caption.Size = new(width - 16, 42);
            caption.AutowrapMode = TextServer.AutowrapMode.WordSmart; parent.AddChild(caption); y += 44;
            foreach (var key in requirement.Targets)
            {
                Picture(parent, key, 8, y + 2, 24);
                Text(parent, NameOf(key), 44, y, width - 52, 15, true); y += 28;
            }
            y += 8;
        }
        return y;
    }
    private float OutputSlot(Control p, ModelKey source, string component, int index, IReadOnlyList<ModelKey> pool, float x, float y, float width)
    {
        string id = $"{source}/{component}/{index}"; _slots.TryGetValue(id, out var chosen);
        var kind = NeowEffectCardRegistry.Get(source).Components.FirstOrDefault(c => c.ComponentId == component)?.OutputKind
            ?? NeowStructuredOutputKind.Card;
        if (kind is NeowStructuredOutputKind.Card or NeowStructuredOutputKind.Relic or NeowStructuredOutputKind.Potion)
        {
            ResultObject(p, chosen, kind, x, y,
                () => Pick(SlotCandidates(source, component, index, pool), k => { _slots[id] = k; RenderAfterEdit(); }),
                () => { _slots.Remove(id); RenderAfterEdit(); });
            return y + ResultObjectHeight + 12;
        }
        ObjectSlot(p, chosen, _text.Get("common.add_any"), x, y, Math.Min(width, 600),
            () => Pick(SlotCandidates(source, component, index, pool), k => { _slots[id] = k; RenderAfterEdit(); }), () => { _slots.Remove(id); RenderAfterEdit(); });
        return y + 68;
    }

    private float Results(Control content, float width)
    {
        float y = 0;
        const float slotWidth = 124, slotHeight = 76, gap = 12;
        int columns = Math.Max(1, (int)((width + gap) / (slotWidth + gap)));
        foreach (string kind in new[] { "CARD", "RELIC", "POTION" })
        {
            string title = kind switch { "CARD" => _text.Get("object.cards"), "RELIC" => _text.Get("object.relics"), _ => _text.Get("object.potions") };
            Text(content, title, 0, y, width, 20); y += 30;
            var selected = _targets.Where(t => t.Kind == kind).ToArray();
            for (int i = 0; i <= selected.Length; i++)
            {
                float x = i % columns * (slotWidth + gap), top = y + i / columns * (slotHeight + gap);
                if (i == selected.Length)
                {
                    var empty = Button(content, "+", x, top, slotWidth,
                        () => Pick(Pool(kind), key => { _targets.Add(new Target(kind, key)); RenderAfterEdit(); }), height: slotHeight);
                    empty.AccessibilityName = _text.Format("picker.choose_kind", title);
                    continue;
                }
                var target = selected[i];
                var slot = Button(content, "", x, top, slotWidth,
                    () => Pick(Pool(kind), key => { target.Key = key; RenderAfterEdit(); }), height: slotHeight);
                Picture(slot, target.Key, 12, 2, 46);
                Text(slot, NameOf(target.Key), 8, 48, slotWidth - 16, 16);
                slot.AccessibilityName = NameOf(target.Key);
                Button(content, "×", x + slotWidth - 36, top + 2, 32,
                    () => { _targets.Remove(target); RenderAfterEdit(); }, height: 30);
            }
            y += (selected.Length / columns + 1) * (slotHeight + gap) + 8;
        }

        // Deliberately a UI illustration, never an Exact/Query feasibility assertion.
        var preview = PreviewSources();
        Text(content, _text.Format("query.neow.result_only.possible_sources", preview.Count), 0, y, 300, 21);
        Text(content, _text.Get("query.neow.result_only.disclaimer"), 320, y + 2, width - 320, 16, true);
        y += 40;
        if (_bans.Count > 0)
        {
            Text(content, _text.Format("query.neow.result_only.excluded_sources", _bans.Count), 0, y + 7, 240, 17, true);
            Button(content, _text.Get("query.neow.result_only.reset_restrictions"), 254, y, 204,
                () => { _bans.Clear(); RenderAfterEdit(); });
            y += 52;
        }
        var sources = Sources.Where(preview.Contains).ToArray();
        const int sourceColumns = 10;
        float stride = width / sourceColumns;
        for (int i = 0; i < sources.Length; i++)
        {
            var key = sources[i];
            var b = Button(content, "", i % sourceColumns * stride, y + i / sourceColumns * 64, stride - 10,
                () => { _bans.Add(key); RenderAfterEdit(); }, height: 54);
            Picture(b, key, (stride - 52) / 2, 5, 42);
            b.AccessibilityName = _text.Format("query.neow.result_only.exclude_accessibility", NameOf(key));
            SearchTooltip.Set(b, _text.Format("query.neow.result_only.exclude_tooltip", NameOf(key)));
        }
        if (sources.Length == 0)
        { Text(content, _text.Get("query.neow.result_only.no_sources"), 0, y, width, 17, true); y += 36; }
        return y + (int)Math.Ceiling(sources.Length / (double)sourceColumns) * 64;
    }

    // Capacity/pool sketch for visual contraction only. No seed, RNG, offer correlation,
    // sampling uniqueness, hooks or multiplayer continuation is modeled here.
    // A source may participate as either the direct entry or a Bones sibling.
}
