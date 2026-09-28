using System.Numerics;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

// N-owned identity-only realization. No continuation is exported: R replays its
// own prerequisites. Required pair identities are compiled, never relic-name cases.
internal sealed class NeowIdentityCpuPlan
{
    private static readonly ulong EventHash = XxHash64.Hash("NEOW"u8, 0);
    private readonly NeowReplayPlan _plan;
    private readonly int _first, _second;
    private readonly bool _impossible;
    private NeowIdentityCpuPlan(NeowReplayPlan plan, int first, int second, bool impossible)
    { _plan = plan; _first = first; _second = second; _impossible = impossible; }

    internal static NeowIdentityCpuPlan? TryCompile(NeowReplayPlan plan)
    {
        ulong bones = Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.NeowsBones);
        if (!plan.DirectNestedVanilla111 || !plan.Bones || plan.Selected != Beta110FastRelicCatalog.NeowsBones ||
            plan.StructuredConditions.Length != 0 || plan.HasFinalCurseFastProjection || plan.ExactOnly.Length != 0 ||
            (plan.TopAny & ~bones) != 0 || (plan.TopAll & ~bones) != 0 || plan.TopBan != 0 ||
            BitOperations.PopCount(plan.BonesAll) != 2 ||
            NeowFamilyReplay.Positives.Contains(Beta110FastRelicCatalog.NeowsBones)) return null;
        // In the audited vanilla identity realization, Bones can only occupy
        // the curse offer. The three appended positive alternatives are not Bones.
        var pool = plan.Authority.BonesEligibleRelicIds;
        if (!plan.Authority.IdentityAuthorityExact || !plan.Authority.BonesAuthorityExact || pool.Length < 2 ||
            pool.Distinct().Count() != pool.Length) return null;
        int first = -1, second = -1;
        for (int i = 0; i < pool.Length; i++)
            if ((Beta110FastRelicCatalog.Bit(pool[i]) & plan.BonesAll) != 0)
            { if (first < 0) first = i; else second = i; }
        bool impossible = first < 0 || second < 0 ||
            (plan.BonesAny != 0 && (plan.BonesAny & plan.BonesAll) == 0) || (plan.BonesBan & plan.BonesAll) != 0;
        if (plan.First != Beta110FastRelicCatalog.InvalidId && plan.Second != Beta110FastRelicCatalog.InvalidId)
            impossible |= (Beta110FastRelicCatalog.Bit(plan.First) | Beta110FastRelicCatalog.Bit(plan.Second)) != plan.BonesAll;
        return new(plan, first, second, impossible);
    }

    internal bool Matches(ulong root)
    {
        if (_impossible) return false;
        var authority = _plan.Authority;
        ulong playerRoot = unchecked(root + (ulong)authority.PlayerSlotIndex);
        var identity = new Beta110FastRng(unchecked(playerRoot + EventHash));
        if (authority.EligibleCurseRelicIds[identity.NextInt(authority.EligibleCurseRelicIds.Length)] != Beta110FastRelicCatalog.NeowsBones)
            return false;
        var rewards = new Beta110FastRng(unchecked(playerRoot + NeowFamilyReplay.RewardsHash));
        int a = _first, b = _second;
        for (int count = authority.BonesEligibleRelicIds.Length; count > 2; count--)
        {
            int selected = rewards.NextInt(count), tail = count - 1;
            // A target moved to the finalized suffix can never return to top two.
            if (a == selected || b == selected) return false;
            if (a == tail) a = selected;
            if (b == tail) b = selected;
        }
        // Last swap only orders the required pair. N owns source membership;
        // authored acquisition order is replayed independently by its consumers.
        return true;
    }
}
