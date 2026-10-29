namespace RolltheSpire2.Search.FamilyExecution;

// The invocation already owns its opening, routes and reward generator. Estimate
// their work, not the spelling/rarity of a requested card or a different Family.
internal static class CombatRewardWorkEstimate
{
    internal readonly record struct PoolWork(double ScanEntries, double AnchorScanEntries);

    internal static PoolWork DescribePoolWork(CombatRewardReplay replay, CombatRewardGpuPlan plan)
    {
        if (replay.ConservativelyKeeps || plan.UsesHotLoop && plan.HotLoop.DirectCardOrdinalSelectionEligible)
            return default;
        var p = replay.Plan;
        var c = replay.Catalog;
        double battles = Math.Max(1, p.MaximumBattleOrdinal) * Math.Max(1, replay.RouteCount);
        double groups = 1 + p.ExplicitContext.AdditionalCardRewardCount;
        // Count + selection are at most two traversals. Sum the rarity pools as
        // a fallback envelope, without guessing rarity probabilities or early exits.
        // Generic's first draw is direct; the non-direct HotLoop scans all three.
        double scansPerGroup = plan.UsesHotLoop ? 6 : 4;
        double scans = battles * groups * scansPerGroup * c.CombatRewardCardPool.TotalCount;
        // The Generic horizon anchors generated one ordinary 20/35/25 group.
        // Subtract that group's scans once. The .5 extra-group term below is
        // generator overhead; extra groups pay their actual scans in full.
        double included = plan.UsesHotLoop ? 0 : battles * 4 * 80;
        if ((p.ExplicitContext.InfluenceFlags & Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard) != 0)
            scans += Math.Max(1, replay.RouteCount) * (p.MaximumBattleOrdinal / 2) *
                3d * c.CombatRewardPowerPool.TotalCount; // full availability + count/select

        // Authored opening uses cr_select_unused even for its first draw.
        // Capsule rarity burns and count-only Kaleidoscope never read card IDs.
        double opening = 0;
        foreach (byte relic in p.OpeningConsumption.OrderedRelicIds)
            opening += relic switch
            {
                Beta110FastRelicCatalog.ArcaneScroll => 2d * c.CharacterRewardPool.Rare.Length,
                Beta110FastRelicCatalog.HeftyTablet => 6d * c.CharacterRewardPool.Rare.Length,
                Beta110FastRelicCatalog.LeadPaperweight => 4d * c.ColorlessRewardPool.TotalCount,
                Beta110FastRelicCatalog.LostCoffer => 6d * c.CharacterRewardPool.TotalCount + 2d * c.PotionPool.TotalCount,
                Beta110FastRelicCatalog.ScrollBoxes => 6d * c.CharacterRewardPool.Common.Length + 4d * c.CharacterRewardPool.Uncommon.Length,
                Beta110FastRelicCatalog.MassiveScroll when plan.Meta[3] > 1 => 6d * c.MultiplayerRewardPool.TotalCount,
                Beta110FastRelicCatalog.Kaleidoscope when !replay.KaleidoscopeCountOnly => 4d * c.OtherCharacterPools.Sum(pool => pool.TotalCount),
                _ => 0
            };
        return new(scans + Math.Max(1, replay.RouteCount) * opening, included);
    }

    private static (int Horizon, int Routes, int Comparisons, int Assignment, int ExtraGroups, int Prefix) Work(CombatRewardReplay replay)
    {
        var p = replay.Plan;
        return (Math.Max(1, p.MaximumBattleOrdinal), Math.Max(1, replay.RouteCount),
            p.Predicates.Sum(row => row.CardAny.Length + row.CardAll.Length + row.CardBan.Length +
                row.PotionAny.Length + row.PotionAll.Length + row.PotionBan.Length + 1),
            p.CardAssignmentTargets.Length * p.CardAssignmentWindow +
                p.PotionAssignmentTargets.Length * p.PotionAssignmentWindow,
            p.ExplicitContext.AdditionalCardRewardCount,
            Math.Max(0, replay.PrecedingNicheDraws ?? 0) + (p.OpeningConsumption.HasReplay ? 1 : 0));
    }

    internal static FamilyPhysicalQuote? Gpu(CombatRewardReplay replay, CombatRewardGpuPlan plan, FamilyPhysicalQuoteRequest g)
    {
        var w = Work(replay);
        var pools = DescribePoolWork(replay, plan);
        // Existing HotLoop H1=.8/H3=1.0 and Generic H1/H2/H3=4.5/7.2/11.8
        // (two routes, shared root=1.6). Extend generation, not target identities.
        double route = w.Horizon switch { 1 => (4.5 - 1.6) / 2, 2 => (7.2 - 1.6) / 2,
            3 => (11.8 - 1.6) / 2, _ => (11.8 - 1.6) / 2 * w.Horizon / 3 };
        double ns = plan.UsesHotLoop ? .8 + .1 * (w.Horizon - 1) : 1.6 + w.Routes * route;
        ns += .04 * Math.Max(0, w.Comparisons - w.Horizon) + .04 * w.Assignment +
            .15 * w.Prefix + .5 * w.ExtraGroups * w.Horizon * w.Routes;
        // Small reusable scan term, not a newly measured curve. Preserve the
        // existing anchors and avoid adding their reference-pool work twice.
        ns += .0025 * (pools.ScanEntries - pools.AnchorScanEntries);
        return FamilyCoarsePricing.Gpu("C.CombatReward", plan.UsesHotLoop ? "C.HotLoop" : "C.Generic",
            ns, CombatRewardGpuExecutor.Capacity, g,
            $"C.NeutralHorizonAndGenericStreamingAnchors;H={w.Horizon};Routes={w.Routes};" +
            $"Comparisons={w.Comparisons};Assignment={w.Assignment};ExtraGroups={w.ExtraGroups};Prefix={w.Prefix};" +
            $"PoolScanEntries={pools.ScanEntries};AnchorPoolScanEntries={pools.AnchorScanEntries};PoolScanEnvelopeDelta;" +
            "NoTargetOrUnusedPoolKey;FullGenerationEnvelope;NoEarlyRejectDiscount", 120);
    }

    internal static FamilyPhysicalQuote? Cpu(CombatRewardReplay replay, int workers, FamilyPhysicalQuoteRequest g,
        bool potionPrefix = false, int capacity = 65536)
    {
        var w = Work(replay);
        double ns;
        if (replay.ConservativelyKeeps) ns = 30;
        else if (potionPrefix)
        {
            int first = replay.Plan.Predicates.Select(p => (int)p.BattleOrdinal).DefaultIfEmpty(1).Min();
            bool identities = replay.Plan.Predicates.Any(p => p.HasPotionIdentityPredicate);
            // Specific first-battle=60, prior full battles=330; scalar H2/H3
            // slow-side anchors are190/360. Retain pity work without a target grid.
            ns = identities ? 60 + 330 * Math.Max(0, first - 1) : 60 + 150 * (w.Horizon - 1);
        }
        else ns = 60 + (w.Horizon > 3 ? 850 : 505) * w.Horizon * w.Routes * (1 + w.ExtraGroups) +
            8 * Math.Max(0, w.Comparisons - w.Horizon) + 8 * w.Assignment + 18 * w.Prefix;
        if (g.CompactInput) ns += 17; // Existing compact root-decoding envelope.
        return FamilyCoarsePricing.Cpu("C.CombatReward", potionPrefix ? "C.PotionPrefix" : "C.FullReward",
            ns, workers, g, $"C.CpuHorizon505AndH4To6_850;PotionPrefix60_330_360;" +
            $"H={w.Horizon};Routes={w.Routes};Comparisons={w.Comparisons};Assignment={w.Assignment};" +
            $"Prefix={w.Prefix};NoTargetOrPoolFingerprintKey;FullGenerationEnvelope", capacity);
    }
}
