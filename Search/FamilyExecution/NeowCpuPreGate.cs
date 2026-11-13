using System.Numerics;
using RolltheSpire2.Core.Seed;

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
        if (!p.AuditedIdentityReplay || !p.Authority.IdentityAuthorityExact || p.Authority.EligibleCurseRelicIds.Length == 0) return null;
        ulong curses = 0;
        foreach (byte id in p.Authority.EligibleCurseRelicIds)
        {
            // Prove curse observations cannot also be supplied by a positive slot.
            if (NeowIdentityOfferFacts.IsPositiveCandidate(id)) return null;
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

// RT2's existing finalized-position algorithm, applied to one required positive
// offer. Each admitted pool contains the target at most once. This is private
// CPU identity computation; no offer identities or RNG state cross ABI1.
internal sealed class NeowCpuPositiveIdentityPlan
{
    internal const string EnvironmentVariable = "RT2_N_CPU_IDENTITY";
    internal const string Revision = ".SinglePositivePosition.20261030.v1";
    private readonly NeowReplayPlan _plan;
    private readonly byte _appendChoice, _optionalAppendSuppressedBy;
    private readonly bool _appendTargetWhenTrue;
    private readonly int[] _fixedCounts, _positions;
    private readonly bool[] _acceptedCurses;

    private NeowCpuPositiveIdentityPlan(NeowReplayPlan plan, byte target, int[] counts, int[] positions, bool[] accepted,
        NeowIdentityBinaryChoice[] choices)
    {
        _plan = plan; _fixedCounts = counts; _positions = positions; _acceptedCurses = accepted;
        _optionalAppendSuppressedBy = choices[0].SuppressedByCurse;
        _appendChoice = 255;
        for (int i = 0; i < choices.Length; i++)
            if (choices[i].TrueCandidate == target || choices[i].FalseCandidate == target)
            {
                _appendChoice = checked((byte)i);
                _appendTargetWhenTrue = choices[i].TrueCandidate == target;
            }
    }

    internal static bool Capture() => Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim().ToLowerInvariant() switch
    {
        null or "" or "default" or "track" => true,
        "off" => false,
        var value => throw new InvalidOperationException("UnknownNCpuIdentity:" + value)
    };

    internal static NeowCpuPositiveIdentityPlan? TryCompile(NeowReplayPlan plan, bool enabled)
    {
        var a = plan.Authority;
        if (!enabled || !plan.AuditedIdentityReplay || a.PlayersCount != 1 || a.PlayerSlotIndex != 0 ||
            !a.IdentityAuthorityExact || a.EligibleCurseRelicIds.Length == 0 || a.EligibleCurseRelicIds.Any(id => id >= 10) ||
            a.EligibleCurseRelicIds.Distinct().Count() != a.EligibleCurseRelicIds.Length ||
            plan.Bones || plan.RequireBones || plan.TopBan != 0 ||
            (plan.BonesAny | plan.BonesAll | plan.BonesBan) != 0 || plan.First != 255 || plan.Second != 255)
            return null;
        const ulong curses = (1UL << 10) - 1;
        ulong selected = plan.Selected == 255 ? 0 : Beta110FastRelicCatalog.Bit(plan.Selected);
        ulong required = (plan.TopAny | plan.TopAll | selected) & ~curses;
        if (BitOperations.PopCount(required) != 1 ||
            ((plan.TopAll | selected) & required) == 0 && plan.TopAny != required) return null;
        byte target = checked((byte)BitOperations.TrailingZeroCount(required));
        var positives = NeowIdentityOfferFacts.For(a.PlayersCount, true, true).AdmittedFixedCandidates.ToArray();
        var choices = NeowIdentityOfferFacts.BinaryChoices.ToArray();
        // Retain this mature three-append hot loop only when preparation proves
        // its shape. Other future rules keep the complete identity replay.
        if (choices.Length != 3 || choices[0].SuppressedByCurse == 255 ||
            choices[1].SuppressedByCurse != 255 || choices[2].SuppressedByCurse != 255) return null;
        byte[] variants = choices
            .SelectMany(choice => new[] { choice.TrueCandidate, choice.FalseCandidate }).ToArray();
        // These are the very arrays used by the original solo offer generator.
        // Prove unique membership before replacing its full identity shuffle.
        if (positives.Concat(variants).Any(id => id < 10 || id >= 64) ||
            positives.Concat(variants).Distinct().Count() != positives.Length + variants.Length ||
            !positives.Contains(target) && !variants.Contains(target)) return null;
        var counts = new int[10];
        var positions = Enumerable.Repeat(-1, 10).ToArray();
        var accepted = new bool[10];
        foreach (byte curse in a.EligibleCurseRelicIds)
        {
            var append = a.Identity.ForCurse(curse).AppendChoices;
            int firstChoice = curse == choices[0].SuppressedByCurse ? 1 : 0;
            if (!append.SequenceEqual(choices.AsSpan(firstChoice))) return null;
            ulong top = Beta110FastRelicCatalog.Bit(curse) | required;
            accepted[curse] = (plan.TopAny == 0 || (top & plan.TopAny) != 0) &&
                (top & plan.TopAll) == plan.TopAll && (selected == 0 || (top & selected) != 0);
            foreach (byte id in a.Identity.ForCurse(curse).FixedCandidates)
            {
                if (id == target) positions[curse] = counts[curse];
                counts[curse]++;
            }
        }
        return new(plan, target, counts, positions, accepted, choices);
    }

    internal bool PassesIdentity(ulong root) => ObserveIdentity(root, out _, out _, out _);

    // Successful paths consume every original variant draw and shuffle draw,
    // including the final two-element swap. Tests compare its complete RNG state.
    internal bool ObserveIdentity(ulong root, out byte curse, out int position, out UpFrontRngCheckpoint afterIdentity)
    {
        curse = NeowSingleplayerReplay.ObserveCurse(root, _plan, out var rng);
        afterIdentity = default;
        position = _positions[curse];
        if (!_acceptedCurses[curse]) return false;
        int count = _fixedCounts[curse];
        // A fixed-pool target cannot be supplied by an append (uniqueness was
        // proved during preparation). Burn its unobserved choices without
        // constructing identities; appended targets observe only their own flip.
        if (_appendChoice == 255)
        {
            if (curse != _optionalAppendSuppressedBy) { _ = rng.NextBool(); count++; }
            _ = rng.NextBool(); _ = rng.NextBool(); count += 2;
        }
        else
        {
            if (curse != _optionalAppendSuppressedBy)
            {
                if (rng.NextBool() == _appendTargetWhenTrue && _appendChoice == 0) position = count;
                count++;
            }
            if (rng.NextBool() == _appendTargetWhenTrue && _appendChoice == 1) position = count;
            count++;
            if (rng.NextBool() == _appendTargetWhenTrue && _appendChoice == 2) position = count;
            count++;
        }
        if (position < 0) return false;
        var shuffle = _plan.Authority.UsesChildShuffle ? rng.ForkShuffle() : rng;
        for (int remaining = count; remaining > 1; remaining--)
        {
            int selected = shuffle.NextInt(remaining), tail = remaining - 1;
            if (position == selected) position = tail;
            else if (position == tail) position = selected;
            if (remaining > 2 && position == tail) return false;
        }
        afterIdentity = (_plan.Authority.UsesChildShuffle ? rng : shuffle).CaptureCheckpoint();
        return true;
    }

    internal bool Matches(ulong root)
    {
        if (!PassesIdentity(root)) return false;
        if (_plan.StructuredConditions.Length == 0 && !_plan.HasFinalCurseFastProjection) return true;
        // A direct route starts its original named streams from this root;
        // only Bones consumes RewardsAfterBones, and Bones is outside this body.
        return NeowSingleplayerReplay.Route(root, _plan, _plan.Selected, Beta110FastRelicCatalog.InvalidId, default);
    }
}
