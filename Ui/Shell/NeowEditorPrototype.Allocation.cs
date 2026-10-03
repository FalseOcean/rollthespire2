using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Pages.Search.Neow;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class NeowEditorPrototype
{
    private string _allocationNotice = "";

    private static bool IsTransformSlot(string id) =>
        id.StartsWith(BaseGameModelKeys.Relics.LeafyPoultice + "/", StringComparison.Ordinal) ||
        id.StartsWith(BaseGameModelKeys.Relics.NewLeaf + "/", StringComparison.Ordinal);

    private void SetCapsuleAllocation(bool joint)
    {
        if (!HasCapsulePair || joint == _draft.JointCapsules) return;
        if (TransferAllocation(joint, transforms: false)) _draft.JointCapsules = joint;
    }

    private bool TransferAllocation(bool joint, bool transforms)
    {
        if (_catalog is null) return false;
        var sources = transforms
            ? new[] { BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf }
            : new[] { BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule };
        var slots = sources.SelectMany(source => NeowEffectCardRegistry.Get(source).Components.SelectMany(component =>
            Enumerable.Range(0, component.SlotCount).Select(i =>
                (Id: $"{source}/{component.ComponentId}/{i}", Pool: _catalog.Candidates(component.CandidatePool))))).ToArray();
        var combined = transforms ? _draft.TransformTargets : _draft.CapsuleTargets;
        var separate = slots.Select(s => _slots.GetValueOrDefault(s.Id)).ToArray();
        var active = (joint ? separate : combined).Where(k => k.HasValue).Select(k => k!.Value).ToArray();
        IReadOnlyList<ModelKey> commonPool = transforms ? JointTransformPool : _catalog.OrdinaryRelics;
        var pools = joint ? slots.Select(_ => commonPool).ToArray() : slots.Select(s => s.Pool).ToArray();
        var assigned = NeowTargetAllocation.Reconcile(joint ? combined : separate, active, pools);
        if (assigned is null)
        {
            // Some modded pools cannot represent a joint multiset in individual
            // source slots. Keep the current mode/targets intact instead of dropping one.
            _allocationNotice = "query.neow.notice.allocation_unavailable";
            _noticeSeconds = 5;
            return false;
        }
        _allocationNotice = "";
        if (joint) Array.Copy(assigned, combined, assigned.Length);
        else for (int i = 0; i < slots.Length; i++)
        {
            _slots.Remove(slots[i].Id);
            if (assigned[i] is { } key) _slots[slots[i].Id] = key;
        }
        return true;
    }
}
