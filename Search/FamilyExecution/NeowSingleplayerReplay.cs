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
    internal static ReadOnlySpan<byte> Positives => [
        Beta110FastRelicCatalog.ArcaneScroll, Beta110FastRelicCatalog.BoomingConch, Beta110FastRelicCatalog.FishingRod,
        Beta110FastRelicCatalog.GoldenPearl, Beta110FastRelicCatalog.Kaleidoscope, Beta110FastRelicCatalog.LeadPaperweight,
        Beta110FastRelicCatalog.LostCoffer, Beta110FastRelicCatalog.NeowsTorment,
        Beta110FastRelicCatalog.NewLeaf, Beta110FastRelicCatalog.PhialHolster, Beta110FastRelicCatalog.PreciseScissors,
        Beta110FastRelicCatalog.ScrollBoxes, Beta110FastRelicCatalog.WingedBoots];

    internal static NeowReplayObservation Observe(ulong root, NeowReplayPlan plan)
    {
        var authority = plan.Authority;
        byte curse = ObserveCurse(root, plan, out var rng);
        Span<byte> positive = stackalloc byte[17];
        int count = 0;
        foreach (byte id in Positives) if (NeowSingleplayerOperators.IsPositiveAllowed(id, curse, authority)) positive[count++] = id;
        if (curse != Beta110FastRelicCatalog.LargeCapsule)
            positive[count++] = rng.NextBool() ? Beta110FastRelicCatalog.LavaRock : Beta110FastRelicCatalog.SmallCapsule;
        positive[count++] = rng.NextBool() ? Beta110FastRelicCatalog.NutritiousOyster : Beta110FastRelicCatalog.StoneHumidifier;
        positive[count++] = rng.NextBool() ? Beta110FastRelicCatalog.NeowsTalisman : Beta110FastRelicCatalog.Pomander;
        rng.UnstableShuffle(positive[..count]);
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
            rewards.UnstableShuffle(pool);
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
        bool keep = false;
        foreach (var route in Routes(plan, observation))
            keep |= Route(root, plan, route.First, route.Second, observation.RewardsAfterBones);
        return keep;
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
        int firstAdvance = plan.AuthoredUpgrades?.Advance(first, 255) ?? 0;
        int secondAdvance = plan.AuthoredUpgrades?.Advance(second, first) ?? 0;
        // No party arrivals or speculative upgrade enumeration in solo execution.
        return firstAdvance < 0 || secondAdvance < 0 ||
            RouteAtArrival(root, plan, first, second, afterBones, firstAdvance, secondAdvance);
    }

    private static bool RouteAtArrival(ulong root, NeowReplayPlan plan, byte first, byte second,
        Beta110FastRng afterBones, int firstAdvance, int secondAdvance)
    {
        var rewards = plan.Bones ? afterBones
            : new Beta110FastRng(unchecked(root + RewardsHash));
        var niche = new Beta110FastRng(unchecked(root + NicheHash));
        var transformations = new Beta110FastRng(unchecked(root + TransformationsHash));
        var potions = new Beta110FastRng(unchecked(root + PotionHash));
        Span<byte> matched = stackalloc byte[plan.StructuredConditions.Length]; matched.Clear();
        bool rewardsNeeded = (plan.EnabledDomains & ~(Beta110FastDomain.FinalCurse | Beta110FastDomain.NewLeafTransform |
            Beta110FastDomain.LeafyPoulticeTransforms | Beta110FastDomain.PhialHolsterPotions)) != 0;
        bool nicheNeeded = plan.HasFinalCurseFastProjection || (plan.EnabledDomains & Beta110FastDomain.NewLeafTransform) != 0 || rewardsNeeded;
        foreach (byte relic in new[] { first, second })
        {
            if (relic == Beta110FastRelicCatalog.InvalidId) continue;
            if (!NeowSingleplayerOperators.ExecuteRelic(relic, true, plan, plan.Authority.EffectCatalog, rewardsNeeded, nicheNeeded,
                (plan.EnabledDomains & Beta110FastDomain.LeafyPoulticeTransforms) != 0,
                (plan.EnabledDomains & Beta110FastDomain.PhialHolsterPotions) != 0,
                ref rewards, ref niche, ref transformations, ref potions, matched))
                throw new InvalidOperationException("NFamilyContinuationAuthorityUnavailable");
            int advance = relic == first ? firstAdvance : secondAdvance;
            for (int i = 0; i < advance; i++) niche.NextInt(2);
        }
        bool allMatched = true;
        foreach (byte match in matched) allMatched &= match != 0;
        if (!allMatched) return false;
        if (plan.HasFinalCurseFastProjection)
        {
            var pool = plan.Authority.EffectCatalog.GeneratedCurseIds;
            ushort curse = pool[niche.NextInt(pool.Length)];
            bool curseMatches = true;
            foreach (ushort target in plan.RequiredFinalCurseIds) curseMatches &= target == curse;
            foreach (ushort target in plan.BannedFinalCurseIds) curseMatches &= target != curse;
            return curseMatches;
        }
        return true;
    }
    private static ulong Bit(byte id) => Beta110FastRelicCatalog.Bit(id);
}
