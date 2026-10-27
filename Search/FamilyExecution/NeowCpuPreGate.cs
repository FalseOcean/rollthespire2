using System.Numerics;

namespace RolltheSpire2.Search.FamilyExecution;

// N-private necessary identity checks. Remaining local replay uses the unchanged
// operators, including real pickup/continuation and the existing lossy boundary.
internal sealed class NeowCpuPreGate
{
    private readonly NeowReplayPlan _plan;
    private readonly NeowIdentityCpuPlan? _pair;
    private readonly ulong _allowedCurses;
    internal bool IdentityOnly { get; }
    internal bool DirectIdentityComplete { get; }
    internal bool FixedPair => _pair is not null;
    internal double GateSurvival { get; }
    // First NEOW draw only. This mask is necessary even when the scalar gate
    // also proves a Bones pair or runs subsequent local predicates.
    internal ulong FirstDrawAllowedIndexes
    {
        get
        {
            ulong indexes = 0;
            for (int i = 0; i < _plan.Authority.EligibleCurseRelicIds.Length; i++)
                if ((_allowedCurses & Beta110FastRelicCatalog.Bit(_plan.Authority.EligibleCurseRelicIds[i])) != 0)
                    indexes |= 1UL << i;
            return indexes;
        }
    }
    // Donor e89849e: the fixed direct two-draw operator only. Other replay
    // bodies retain their own configuration boundary.
    internal bool BoundedLeafyWorkers => !IdentityOnly && !FixedPair && !_plan.Bones && !_plan.RequireBones &&
        _plan.Selected == Beta110FastRelicCatalog.LeafyPoultice &&
        _plan.StructuredConditions is [{ Kind: Beta110FastStructuredConditionKind.LeafyPoulticeTransforms, TargetCount: 2 } condition] &&
        condition.SourceRelicId == _plan.Selected && _plan.ExactOnly.Length == 0 && !_plan.HasFinalCurseFastProjection &&
        _plan.EnabledDomains == Beta110FastDomain.LeafyPoulticeTransforms &&
        _plan.Authority.PlayersCount == 1 && _plan.Authority.AllCharacterCardPoolsUnlocked &&
        _plan.Authority.ScrollBoxesAllowed && _plan.Authority.EligibleCurseRelicIds.Length == 10 &&
        _plan.Authority.EffectCatalog.LeafyTransformAuthorityExact &&
        (_plan.AuthoredUpgrades?.Advance(_plan.Selected, 255) ?? 0) == 0;
    private NeowCpuPreGate(NeowReplayPlan plan, NeowIdentityCpuPlan? pair, ulong allowed, bool identityOnly,
        bool directIdentityComplete)
    {
        _plan = plan; _pair = pair; _allowedCurses = allowed; IdentityOnly = identityOnly;
        DirectIdentityComplete = directIdentityComplete;
        GateSurvival = (double)BitOperations.PopCount(allowed) / plan.Authority.EligibleCurseRelicIds.Length;
        if (pair is not null) GateSurvival *= 2d / (plan.Authority.BonesEligibleRelicIds.Length * (plan.Authority.BonesEligibleRelicIds.Length - 1));
    }

    internal static NeowCpuPreGate? TryCompile(NeowReplayPlan p)
    {
        if (!p.DirectNestedVanilla111 || !p.Authority.IdentityAuthorityExact || p.Authority.EligibleCurseRelicIds.Length == 0) return null;
        ulong curses = 0;
        foreach (byte id in p.Authority.EligibleCurseRelicIds)
        {
            // Prove curse observations cannot also be supplied by a positive slot.
            if (NeowFamilyReplay.Positives.Contains(id) || id is Beta110FastRelicCatalog.LavaRock or
                Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.NutritiousOyster or
                Beta110FastRelicCatalog.StoneHumidifier or Beta110FastRelicCatalog.NeowsTalisman or Beta110FastRelicCatalog.Pomander) return null;
            curses |= Beta110FastRelicCatalog.Bit(id);
        }
        ulong allowed = curses;
        ulong selected = p.Selected == Beta110FastRelicCatalog.InvalidId ? 0 : Beta110FastRelicCatalog.Bit(p.Selected);
        if ((selected & curses) != 0) allowed &= selected;
        if (p.Bones || p.RequireBones) allowed &= Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.NeowsBones);
        ulong all = p.TopAll & curses;
        if (all != 0) allowed &= BitOperations.PopCount(all) == 1 ? all : 0;
        if (p.TopAny != 0 && (p.TopAny & ~curses) == 0) allowed &= p.TopAny;
        allowed &= ~p.TopBan;

        var pair = NeowIdentityCpuPlan.TryCompile(p with { StructuredConditions = [],
            EnabledDomains = Beta110FastDomain.None, ExactOnly = [] });
        if (pair is null && allowed == curses) return null;
        bool noInnerRequirement = (p.BonesAny | p.BonesAll | p.BonesBan) == 0 &&
            p.First == Beta110FastRelicCatalog.InvalidId && p.Second == Beta110FastRelicCatalog.InvalidId;
        bool identityOnly = noInnerRequirement && p.StructuredConditions.Length == 0 &&
            p.ExactOnly.Length == 0 && p.EnabledDomains == Beta110FastDomain.None &&
            ((p.TopAny | p.TopAll | p.TopBan | selected) & ~curses) == 0;
        // The accepted curse is the actual selected direct route, and all top
        // obligations refer only to this mutually exclusive curse slot. Locals
        // still run their original operators; no positive offer is their input.
        // FinalCurse/Bones and Exact remainders retain full observation replay.
        bool directIdentityComplete = !p.Bones && !p.RequireBones && noInnerRequirement &&
            p.Selected != Beta110FastRelicCatalog.InvalidId && (selected & curses) != 0 &&
            ((p.TopAny | p.TopAll | p.TopBan) & ~curses) == 0 &&
            !p.HasFinalCurseFastProjection && p.ExactOnly.Length == 0;
        if (!identityOnly && pair is null && BitOperations.PopCount(allowed) * 2 > p.Authority.EligibleCurseRelicIds.Length)
            return null; // loose necessary checks do not justify replaying the prefix
        return new(p, pair, allowed, identityOnly, directIdentityComplete);
    }

    internal bool PassesIdentity(ulong root) => _pair is not null ? _pair.Matches(root) :
            (_allowedCurses & Beta110FastRelicCatalog.Bit(NeowFamilyReplay.ObserveCurse(root, _plan, out _))) != 0;

    internal bool Matches(ulong root)
    {
        // Pure curse predicates are completely decided by the first observation.
        if (!PassesIdentity(root)) return false;
        if (IdentityOnly) return true;
        if (DirectIdentityComplete)
        {
            // A non-Bones Route ignores afterBones and initializes its required
            // streams from this root. Preserve the original identity-only return
            // when no local condition participates; otherwise keep every local
            // predicate, authored burn and continuation fault in the pure Route.
            return _plan.StructuredConditions.Length == 0 || NeowFamilyReplay.Route(root, _plan,
                _plan.Selected, Beta110FastRelicCatalog.InvalidId, default);
        }
        return NeowFamilyReplay.Matches(root, _plan);
    }
}
