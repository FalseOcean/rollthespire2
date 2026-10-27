using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class NeowSearchEditor
{
    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }

    internal bool HasConditions(bool party) => (party ? _partyCatalogDrafts : _drafts).Values.Any(d => d.Count > 0) ||
        party && _partyOffers.Values.Any(keys => keys.Count > 0);

    internal Action ClearConditions(bool party, int? slot = null)
    {
        var restore = WorkbenchConditionReset.Swap(party ? _partyCatalogDrafts : _drafts, _ => new SeatDraft(), slot);
        if (!party) return restore;
        var offers = WorkbenchConditionReset.Swap(_partyOffers, _ => new HashSet<Core.Identity.ModelKey>(), slot);
        var modes = _partyModes.Where(p => slot is null || p.Key == slot).ToArray(); var choices = _partyChoices.Where(p => slot is null || p.Key == slot).ToArray();
        foreach (var pair in modes) _partyModes.Remove(pair.Key); foreach (var pair in choices) _partyChoices.Remove(pair.Key);
        return () =>
        {
            restore(); offers(); foreach (var pair in modes) _partyModes.Remove(pair.Key); foreach (var pair in choices) _partyChoices.Remove(pair.Key);
            foreach (var (slot, mode) in modes) _partyModes[slot] = mode;
            foreach (var (slot, value) in choices) _partyChoices[slot] = value;
        };
    }

    internal int AuthoredConditionCount()
    {
        if (_draft.Source is not { } source) return 0;
        int count = 1; // Opening identity.
        bool bones = source == BaseGameModelKeys.Relics.NeowsBones;
        ModelKey[] roots = bones ? _draft.Bones.ToArray() : [source];
        if (bones && roots.Length > 0) count++; // Nested relics and their pickup order form one requirement.

        var resultGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string slot, ModelKey? value) in _draft.Slots)
        {
            if (!value.HasValue || !roots.Any(root => slot.StartsWith(root + "/", StringComparison.Ordinal))) continue;
            if (TManagesTransform() && IsTransformSlot(slot)) continue;
            if (bones && _draft.JointTransforms && IsTransformSlot(slot)) continue;
            if (bones && _draft.JointCapsules && (slot.StartsWith(BaseGameModelKeys.Relics.SmallCapsule + "/", StringComparison.Ordinal) ||
                slot.StartsWith(BaseGameModelKeys.Relics.LargeCapsule + "/", StringComparison.Ordinal))) continue;
            int ordinal = slot.LastIndexOf('/');
            if (ordinal > 0) resultGroups.Add(slot[..ordinal]);
        }
        count += resultGroups.Count;
        if (HasCapsulePair && _draft.JointCapsules && _draft.CapsuleTargets.Any(key => key.HasValue)) count++;
        if (HasTransformPair && _draft.JointTransforms && _draft.TransformTargets.Any(key => key.HasValue) && !TManagesTransform()) count++;
        if (bones && _draft.Curse.HasValue) count++;
        if (!bones && source == BaseGameModelKeys.Relics.ScrollBoxes &&
            _draft.ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw) count++;
        return count;
    }

    private static NeowEditorStateSnapshot CaptureEditorState(SeatDraft draft) => new(draft.Source, draft.Bones.ToArray(), draft.Ordered,
        draft.KaleidoscopeOrder, draft.ScrollOffer, draft.JointTransforms, draft.JointCapsules,
        draft.TransformTargets.ToArray(), draft.CapsuleTargets.ToArray(), draft.Curse,
        new Dictionary<string, ModelKey?>(draft.Slots), draft.TransformSeparateInitialized,
        draft.TransformJointInitialized, draft.CapsuleSeparateInitialized, draft.CapsuleJointInitialized);
    internal NeowEditorStateSnapshot ExportEditorState() => CaptureEditorState(_draft);
    internal NeowEditorStateSnapshot? ExportPartyEditorState(int seat) => _partyCatalogDrafts.TryGetValue(seat, out var draft)
        ? CaptureEditorState(draft) : null;
    internal void ImportEditorState(NeowEditorStateSnapshot saved)
    {
        if (saved.Bones.Count > 2 || saved.TransformTargets.Count != 3 || saved.CapsuleTargets.Count != 3)
            throw new InvalidOperationException("integration.load_shape");
        _draft.Source = saved.Source; _bones.Clear(); _bones.AddRange(saved.Bones); _ordered = saved.Ordered;
        _draft.KaleidoscopeOrder = saved.KaleidoscopeOrder; _draft.ScrollOffer = saved.ScrollOffer;
        _draft.JointTransforms = saved.JointTransforms; _draft.JointCapsules = saved.JointCapsules;
        for (int i = 0; i < 3; i++)
        { _draft.TransformTargets[i] = saved.TransformTargets[i]; _draft.CapsuleTargets[i] = saved.CapsuleTargets[i]; }
        _draft.Curse = saved.Curse; _slots.Clear();
        foreach (var slot in saved.Slots) _slots.Add(slot.Key, slot.Value);
        _draft.TransformSeparateInitialized = saved.TransformSeparateInitialized;
        _draft.TransformJointInitialized = saved.TransformJointInitialized;
        _draft.CapsuleSeparateInitialized = saved.CapsuleSeparateInitialized;
        _draft.CapsuleJointInitialized = saved.CapsuleJointInitialized;
        _draft.Context = "";
    }
    // Legacy active-query imports contain no inactive-mode drafts. Seed only the
    // mode they actually author; the first switch can reuse the existing allocation solver.
    private void InitializeAllocationDraftsFromQuery()
    {
        _draft.TransformSeparateInitialized = !_draft.JointTransforms;
        _draft.TransformJointInitialized = _draft.JointTransforms;
        _draft.CapsuleSeparateInitialized = !_draft.JointCapsules;
        _draft.CapsuleJointInitialized = _draft.JointCapsules;
    }
}
