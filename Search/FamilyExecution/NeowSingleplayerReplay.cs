using RolltheSpire2.Core.Seed;
namespace RolltheSpire2.Search.FamilyExecution;

// Independent Singleplayer N CPU core. Query semantics remain owned by Production Exact.
internal static class NeowSingleplayerReplay
{
    internal static readonly ulong RewardsHash = XxHash64.Hash("rewards"u8, 0);
    private static readonly ulong EventHash = XxHash64.Hash("NEOW"u8, 0);
    private static readonly ulong NicheHash = XxHash64.Hash("niche"u8, 0);
    private static readonly ulong TransformationsHash = XxHash64.Hash("transformations"u8, 0);
    private static readonly ulong PotionHash = XxHash64.Hash("combat_potion_generation"u8, 0);
    internal static NeowReplayObservation Observe(ulong root, NeowReplayPlan plan)
    {
        var authority = plan.Authority;
        byte curse = ObserveCurse(root, plan, out var rng);
        Span<byte> positive = stackalloc byte[17];
        var identity = authority.Identity.ForCurse(curse);
        identity.FixedCandidates.CopyTo(positive);
        int count = identity.FixedCandidates.Length;
        foreach (var choice in identity.AppendChoices)
            positive[count++] = rng.NextBool() ? choice.TrueCandidate : choice.FalseCandidate;
        rng.UnstableShuffle(positive[..count], authority.RngSemantics);
        ulong top = Bit(positive[0]) | Bit(positive[1]) | Bit(curse);
        bool pass = (plan.TopAny == 0 || (top & plan.TopAny) != 0) & ((top & plan.TopAll) == plan.TopAll) &
            ((top & plan.TopBan) == 0) & (plan.Selected == Beta110FastRelicCatalog.InvalidId || (top & Bit(plan.Selected)) != 0) &
            (!(plan.Bones || plan.RequireBones) || (top & Bit(Beta110FastRelicCatalog.NeowsBones)) != 0);
        byte first = Beta110FastRelicCatalog.InvalidId, second = first;
        var rewards = new Beta110FastRng(unchecked(root + RewardsHash));
        // One replay even when this observation is used as R's prerequisite.
        if (plan.Bones)
        {
            Span<byte> pool = stackalloc byte[authority.BonesEligibleRelicIds.Length];
            authority.BonesEligibleRelicIds.CopyTo(pool);
            if (authority.UsesChildShuffle)
            {
                var nonCombat = new Beta110FastRng(unchecked(root + NeowFamilyReplay.NonCombatHash));
                nonCombat.UnstableShuffle(pool, authority.RngSemantics);
            }
            else rewards.UnstableShuffle(pool);
            first = pool[0]; second = pool[1];
            ulong pair = Bit(first) | Bit(second);
            pass &= (plan.BonesAny == 0 || (pair & plan.BonesAny) != 0) & ((pair & plan.BonesAll) == plan.BonesAll) & ((pair & plan.BonesBan) == 0);
            if (plan.First != Beta110FastRelicCatalog.InvalidId && plan.Second != Beta110FastRelicCatalog.InvalidId)
                pass &= pair == (Bit(plan.First) | Bit(plan.Second));
        }
        return new(positive[0], positive[1], curse, first, second, rewards, pass);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static byte ObserveCurse(ulong root, NeowReplayPlan plan, out Beta110FastRng rng)
    {
        var a = plan.Authority;
        rng = new Beta110FastRng(unchecked(root + EventHash));
        return a.EligibleCurseRelicIds[rng.NextInt(a.EligibleCurseRelicIds.Length)];
    }

    internal static bool Matches(ulong root, NeowReplayPlan plan)
    {
        NeowReplayObservation observation = Observe(root, plan);
        if (!observation.IdentityPass) return false;
        if (plan.StructuredConditions.Length == 0 && !plan.HasFinalCurseFastProjection) return true;
        if (!plan.Bones) return Route(root, plan, plan.Selected, Beta110FastRelicCatalog.InvalidId, observation.RewardsAfterBones);
        if (plan.First != Beta110FastRelicCatalog.InvalidId && plan.Second != Beta110FastRelicCatalog.InvalidId)
            return Route(root, plan, plan.First, plan.Second, observation.RewardsAfterBones);
        // Solo keeps its existing first-success route semantics.
        return Route(root, plan, observation.BonesFirst, observation.BonesSecond, observation.RewardsAfterBones) ||
            Route(root, plan, observation.BonesSecond, observation.BonesFirst, observation.RewardsAfterBones);
    }

    internal static IEnumerable<(byte First, byte Second)> Routes(NeowReplayPlan plan, NeowReplayObservation observation)
    {
        if (!plan.Bones) { yield return (plan.Selected, Beta110FastRelicCatalog.InvalidId); yield break; }
        byte a = observation.BonesFirst, b = observation.BonesSecond;
        if (plan.First != Beta110FastRelicCatalog.InvalidId && plan.Second != Beta110FastRelicCatalog.InvalidId)
        {
            if ((a == plan.First && b == plan.Second) || (b == plan.First && a == plan.Second)) yield return (plan.First, plan.Second);
            yield break;
        }
        yield return (a, b);
        yield return (b, a);
    }

    internal static bool Route(ulong root, NeowReplayPlan plan, byte first, byte second, Beta110FastRng afterBones)
    {
        if (!NeowLocalOperators.RequiredStructuredSourcesPresent(plan.StructuredConditions, first, second)) return false;
        if (plan.CapsuleNicheContinuation is { } continuation)
        {
            int[] firstAdvances = NeowCapsuleNicheContinuation.ObservedAfterFirst(plan,second)
                ? continuation.Advances(first,255) : NeowCapsuleNicheContinuation.Zero;
            int[] secondAdvances = plan.HasFinalCurseFastProjection
                ? continuation.Advances(second,first) : NeowCapsuleNicheContinuation.Zero;
            foreach(int da in firstAdvances)
            foreach(int db in secondAdvances)
                if(RouteAtArrival(root,plan,first,second,afterBones,da,db)) return true;
            return false;
        }
        int firstAdvance = plan.AuthoredUpgrades?.Advance(first, 255) ?? 0;
        int secondAdvance = plan.AuthoredUpgrades?.Advance(second, first) ?? 0;
        // The retained authored-only donor is used when no N-only continuation
        // is required (and by the explicit frozen legacy comparison).
        return firstAdvance < 0 || secondAdvance < 0 ||
            RouteAtArrival(root, plan, first, second, afterBones, firstAdvance, secondAdvance);
    }

    internal static bool RouteAtArrival(ulong root, NeowReplayPlan plan, byte first, byte second,
        Beta110FastRng afterBones, int firstAdvance, int secondAdvance)
    {
        bool rewardsNeeded = plan.NeedsRewards;
        bool nicheNeeded = plan.NeedsNicheOrKaleidoscopePools;
        bool transformationsNeeded = plan.NeedsTransformations;
        bool potionsNeeded = plan.NeedsPotions;
        // Unconsumed route-local streams need no SplitMix setup. Preserve every
        // authored burn and the observation's Rewards continuation unchanged.
        var rewards = plan.Bones ? afterBones
            : rewardsNeeded ? new Beta110FastRng(unchecked(root + RewardsHash)) : default;
        var niche = nicheNeeded || firstAdvance > 0 || secondAdvance > 0
            ? new Beta110FastRng(unchecked(root + NicheHash)) : default;
        var transformations = transformationsNeeded ? new Beta110FastRng(unchecked(root + TransformationsHash)) : default;
        var potions = potionsNeeded ? new Beta110FastRng(unchecked(root + PotionHash)) : default;
        var nonCombat = plan.Authority.UsesChildShuffle ? NeowFamilyReplay.NonCombatRng(root, plan) : default;
        Span<byte> matched = stackalloc byte[plan.StructuredConditions.Length]; matched.Clear();
        bool nicheUnknown = false;
        for (int pickup = 0; pickup < 2; pickup++)
        {
            byte relic = pickup == 0 ? first : second;
            if (relic == Beta110FastRelicCatalog.InvalidId) continue;
            if (nicheUnknown && (plan.Authority.UsesChildShuffle
                    ? relic is Beta110FastRelicCatalog.Kaleidoscope or Beta110FastRelicCatalog.PhialHolster
                    : NeowCapsuleNicheContinuation.ReadsNiche(relic)))
            {
                // Keep dependent identities for Exact while retaining K's fixed
                // Rewards consumption and the independent checks.
                if(relic==Beta110FastRelicCatalog.Kaleidoscope && rewardsNeeded)
                    for(int i=0;i<18;i++) rewards.NextFloat();
                for(int i=0;i<plan.StructuredConditions.Length;i++)
                    if(plan.StructuredConditions[i].SourceRelicId==relic) matched[i]=1;
            }
            else
            {
                if (!NeowSingleplayerOperators.ExecuteRelic(relic, true, plan, plan.Authority.EffectCatalog, rewardsNeeded, nicheNeeded,
                    transformationsNeeded, potionsNeeded,
                    ref rewards, ref niche, ref transformations, ref potions, ref nonCombat, matched))
                    throw new InvalidOperationException("NFamilyContinuationAuthorityUnavailable");
            }
            int advance = relic == first ? firstAdvance : secondAdvance;
            if(advance<0 || NeowCapsuleNonCombatContinuation.IsUnknown(plan, relic)) nicheUnknown=true;
            else for (int i = 0; i < advance; i++) niche.NextInt(2);
        }
        bool allMatched = true;
        foreach (byte match in matched) allMatched &= match != 0;
        if (!allMatched) return false;
        if (plan.HasFinalCurseFastProjection)
        {
            if(nicheUnknown) return true;
            var pool = plan.Authority.EffectCatalog.GeneratedCurseIds;
            ushort curse = pool[(plan.Authority.UsesChildShuffle ? ref nonCombat : ref niche).NextInt(pool.Length)];
            bool curseMatches = true;
            foreach (ushort target in plan.RequiredFinalCurseIds) curseMatches &= target == curse;
            foreach (ushort target in plan.BannedFinalCurseIds) curseMatches &= target != curse;
            return curseMatches;
        }
        return true;
    }
    private static ulong Bit(byte id) => Beta110FastRelicCatalog.Bit(id);
}
