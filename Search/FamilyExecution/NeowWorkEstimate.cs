using System.Numerics;

namespace RolltheSpire2.Search.FamilyExecution;

// Cost only: no candidate evaluation, RNG, joint-family work or new probability
// solver. The old eight N numerical classes and 33/42/210ns CPU envelopes are
// reference anchors, not measurements of the current device/optimization.
internal static class NeowWorkEstimate
{
    internal readonly record struct Work(double Draws, double PoolVisits, double Comparisons, int Routes);

    internal static Work Describe(NeowReplayPlan p)
    {
        if (p.StructuredConditions.Length == 0 && !p.HasFinalCurseFastProjection) return default;
        bool rewards = (p.EnabledDomains & ~(Beta110FastDomain.FinalCurse | Beta110FastDomain.NewLeafTransform |
            Beta110FastDomain.LeafyPoulticeTransforms | Beta110FastDomain.PhialHolsterPotions)) != 0;
        bool niche = rewards || p.HasFinalCurseFastProjection || (p.EnabledDomains & Beta110FastDomain.NewLeafTransform) != 0;
        bool transform = (p.EnabledDomains & Beta110FastDomain.LeafyPoulticeTransforms) != 0;
        bool potion = (p.EnabledDomains & Beta110FastDomain.PhialHolsterPotions) != 0;
        var sources = p.Bones ? p.First != byte.MaxValue && p.Second != byte.MaxValue
            ? new[] { p.First, p.Second } : p.Authority.BonesEligibleRelicIds
            : new[] { p.Selected };
        // An unpinned Bones route executes two acquired sources, not the whole
        // eligible pool. Bound by the two heaviest possible local operations.
        var work = sources.Select(id => Source(id, p.Authority.EffectCatalog, rewards, niche, transform, potion))
            .OrderByDescending(w => w.Draws + w.Visits / 16).Take(p.Bones ? 2 : 1).ToArray();
        double advances = p.AuthoredUpgrades is { } upgrades
            ? upgrades.Advances.Select(a => (double)Math.Max(0, a)).DefaultIfEmpty().Max() * (p.Bones ? 2 : 1) : 0;
        return new(work.Sum(w => w.Draws) + advances + Math.Max(0, p.SharedNicheDraws) + Math.Max(0, p.SharedPotionDraws) +
                (p.HasFinalCurseFastProjection ? 1 : 0),
            work.Sum(w => w.Visits), p.StructuredConditions.Sum(row => (double)Math.Max(1, (int)row.TargetCount)) +
                p.RequiredFinalCurseIds.Length + p.BannedFinalCurseIds.Length,
            p.Bones && (p.First == byte.MaxValue || p.Second == byte.MaxValue) ? 2 : 1);
    }

    // The reference R replay executes a non-Capsule pickup to reach the same
    // Rewards state even when that pickup has no authored output predicate.
    // Reuse numerical-operation weights only: no N identity/filter charge.
    internal static Work CapsuleCompanion(NeowReplayPlan p)
    {
        if (!p.Bones) return default;
        var sources = p.First != byte.MaxValue && p.Second != byte.MaxValue
            ? new[] { p.First, p.Second }
            : p.Authority.BonesEligibleRelicIds.Where(id => (p.BonesBan & Beta110FastRelicCatalog.Bit(id)) == 0 &&
                (BitOperations.PopCount(p.BonesAll) != 2 || (p.BonesAll & Beta110FastRelicCatalog.Bit(id)) != 0));
        // One of the two actual pickups must be the observed Capsule. For an
        // unpinned companion use one heaviest eligible source, not the whole pool.
        var work = sources.Where(id => id is not (Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule))
            .Select(id => Source(id, p.Authority.EffectCatalog, true, true, false, false))
            .OrderByDescending(w => w.Draws + w.Visits / 16).FirstOrDefault();
        return new(work.Draws, work.Visits, 0, p.First == byte.MaxValue ? 2 : 1);
    }

    // Only repeated unused-selection/shuffle walks scale with pool size.
    // Arcane, NewLeaf and Leafy with an empty used set remain direct indexes.
    private static (double Draws, double Visits) Source(byte id, Beta110FastEffectCatalog c,
        bool rewards, bool niche, bool transform, bool potion) => id switch {
        Beta110FastRelicCatalog.SmallCapsule when rewards => (1, 0),
        Beta110FastRelicCatalog.LargeCapsule when rewards => (2, 0),
        Beta110FastRelicCatalog.ArcaneScroll when rewards => (1, 0),
        // Three normal cards each consume rarity, selection and upgrade rolls;
        // the two selections with nonempty used sets count/select their pools.
        Beta110FastRelicCatalog.MassiveScroll when rewards => (9, 4d * c.MultiplayerRewardPool.TotalCount),
        Beta110FastRelicCatalog.HeftyTablet when rewards => (3, 4d * c.CharacterRewardPool.Rare.Length),
        Beta110FastRelicCatalog.LeadPaperweight when rewards => (6, 2d * c.ColorlessRewardPool.TotalCount),
        Beta110FastRelicCatalog.LostCoffer when rewards => (11, 4d * c.CharacterRewardPool.TotalCount),
        Beta110FastRelicCatalog.Kaleidoscope when rewards => (18 + 2d * Math.Max(0, c.OtherCharacterPools.Length - 1),
            c.OtherCharacterPools.Sum(pool => 2d * pool.TotalCount) + 2d * c.OtherCharacterPools.Length),
        Beta110FastRelicCatalog.Kaleidoscope when niche => (2d * Math.Max(0, c.OtherCharacterPools.Length - 1), 0),
        Beta110FastRelicCatalog.ScrollBoxes when rewards => (8, 8d * c.CharacterRewardPool.Common.Length + 2d * c.CharacterRewardPool.Uncommon.Length),
        Beta110FastRelicCatalog.LeafyPoultice when transform => (2, 0),
        Beta110FastRelicCatalog.NewLeaf when niche => (1, 0),
        Beta110FastRelicCatalog.PhialHolster when potion => (4, 2d * c.PotionPool.TotalCount),
        _ => (0, 0)
    };

    internal static FamilyPhysicalQuote? Gpu(NeowFamily family, Work work, FamilyPhysicalQuoteRequest g)
    {
        if (family.Coverage.Count != 1 || family.GpuPlan is not { } gpu) return null;
        var p = family.PricingReplayPlan;
        int bones = p.Authority.BonesEligibleRelicIds.Length;
        int curses = p.Authority.EligibleCurseRelicIds.Length;
        bool local = work.Routes > 0;
        bool pair = !gpu.IsMultiplayer && p.Bones && gpu.Meta.Length > 67 && gpu.Meta[67] == 1;
        bool resolved = !gpu.IsMultiplayer && curses is > 0 and <= 32 && gpu.Meta.Length > 69 &&
            BitOperations.PopCount(gpu.Meta[68] | gpu.Meta[69]) >= curses;
        string kind;
        double ns;
        if (p.Bones)
        {
            kind = pair ? "TrackedPair" : "BonesPoolReplay";
            ns = pair ? (g.CompactInput ? 1 : .14) + .003 * Math.Max(0, bones - 28)
                : (g.CompactInput ? 1.6 : .30) + .02 * Math.Max(0, bones - 1);
        }
        else
        {
            kind = resolved ? "FirstDraw" : "PositivePool";
            ns = resolved ? g.CompactInput ? .68 : .28 : g.CompactInput ? 1.6 : 1.1;
        }
        // These anchors already contain the identity prefix: never add it twice.
        if (!p.Bones && local)
        {
            int anchor = gpu.UsesLeafyPreGate ? 2 : gpu.DirectNestedMode switch {
                3 or 40 => 0, 6 or 7 => 3, 21 or 31 => 4, 11 => 5, 22 => 6, 42 => 7,
                10 => p.StructuredConditions.All(c => c.Kind == Beta110FastStructuredConditionKind.LostCofferPotion) ? 7 : 6,
                _ => -1
            };
            if (anchor >= 0)
            {
                var a = NeowPhysicalPricing.ReferenceWork(anchor);
                kind = a.Shape[2..];
                ns = g.CompactInput ? a.CompactNs : a.DenseNs;
                // Only size-dependent walks receive an increment. Direct-index
                // transforms must not become slower merely for a larger mod pool.
                double referenceVisits = p.StructuredConditions.Select(c => c.SourceRelicId).Distinct().Sum(id => id switch {
                    Beta110FastRelicCatalog.Kaleidoscope => 648d,
                    Beta110FastRelicCatalog.LostCoffer => 320d,
                    Beta110FastRelicCatalog.LeadPaperweight => 106d,
                    Beta110FastRelicCatalog.PhialHolster => 96d,
                    Beta110FastRelicCatalog.ScrollBoxes => 230d,
                    Beta110FastRelicCatalog.HeftyTablet => 100d,
                    _ => 0d
                });
                ns += .002 * Math.Max(0, work.PoolVisits - referenceVisits);
            }
            else ns += .10 * work.Routes * work.Draws + .002 * work.Routes * work.PoolVisits + .015 * work.Comparisons;
        }
        else if (local)
        {
            double entry = curses > 0 ? 1d / curses : 1;
            if (pair && bones > 1) entry *= 2d / (bones * (bones - 1d));
            double activeWarp = 1 - Math.Pow(1 - Math.Clamp(entry, 0, 1), 32);
            ns += activeWarp * work.Routes * (.10 * work.Draws + .002 * work.PoolVisits + .015 * work.Comparisons);
        }
        var options = family.WorkEstimateOptimizations(g.CompactInput);
        if (options.LeafyLocalPrefix) ns = Math.Max(.12, ns - .04); // amortized shared prefix hash; estimated operation saving
        if (options.CurseFirst && !g.CompactInput) ns *= .9; // bounded first-gate saving, not a measured variant ratio
        double integerFactor = family.UsesCompatibilityGpu ? 1.5 : 1;
        ns *= integerFactor; // estimated arithmetic work, not FP64 measurement identity reuse
        int representedPlayers = gpu.IsMultiplayer ? Math.Max(1, p.Authority.PlayersCount) : 1;
        ns *= representedPlayers; // packed multi-player observations belong to this N body
        return FamilyCoarsePricing.Gpu(family.FamilyId, "N." + kind, ns, NeowFamilyGpuExecutor.Capacity, g,
            $"N.WorkEstimate.20261028.v1;Anchors=N.WorkShape.20260910.v2;OwnCoverageOnly;Draws={work.Draws:R};PoolVisits={work.PoolVisits:R};Comparisons={work.Comparisons:R};Routes={work.Routes};" +
            $"NumericalVariant={gpu.NumericalVariant};OptimizationReference={options.Revision};IntegerArithmeticFactorEstimated={integerFactor};RepresentedPlayers={representedPlayers};NoJointPrice;NoNewMeasurement",
            setupMilliseconds: gpu.UsesStagedDense ? 4200 : 2400, publicTransportClass: "LargeResidentAppend32");
    }

    internal static FamilyPhysicalQuote? Cpu(NeowFamily family, Work work, FamilyPhysicalQuoteRequest g, int workers,
        NeowCpuPreGate? gate = null, bool fixedPair = false, bool directLocal = false, NeowCpuFourWayPreGate? fourWay = null,
        int capacity = 65536)
    {
        if (family.Coverage.Count != 1) return null;
        var p = family.PricingReplayPlan;
        double identity = 210 + (p.Bones ? 3d * Math.Max(0, p.Authority.BonesEligibleRelicIds.Length - 1) : 0);
        double local = work.Routes * (12 * work.Draws + .3 * work.PoolVisits + 2 * work.Comparisons);
        string kind = "Replay";
        double ns = identity + local;
        if (fixedPair) { kind = "TrackedPair"; ns = 42 + .5 * Math.Max(0, p.Authority.BonesEligibleRelicIds.Length - 28); }
        else if (gate is not null)
        {
            kind = gate.IdentityOnly ? "FirstDraw" : "IdentityPreGate";
            ns = gate.IdentityOnly ? 33 : 33 + Math.Clamp(gate.GateSurvival, 0, 1) *
                ((gate.DirectIdentityComplete ? 0 : identity) + local);
        }
        else if (directLocal)
        {
            kind = "LocalFirst";
            double reach = 1;
            if (p.StructuredConditions is [var row] && row.TargetCount == 1)
            {
                var c = p.Authority.EffectCatalog;
                ushort[] pool = row.Kind == Beta110FastStructuredConditionKind.NewLeafTransform ? c.NewLeafTransformPool :
                    row.Kind == Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard ? c.CharacterRewardPool.Rare : [];
                if (pool.Length > 0) reach = (double)pool.Count(id => id == row.Target0) / pool.Length;
            }
            ns = 33 + local + reach * identity;
        }
        if (!g.CompactInput && fourWay is { Enabled: true })
        {
            double reach = (double)BitOperations.PopCount(fourWay.AllowedIndexes) / fourWay.Bound;
            ns = 18 + reach * ns;
            kind += ".FourWay";
        }
        if (g.CompactInput) ns += 17;
        return FamilyCoarsePricing.Cpu(family.FamilyId, "N." + kind, ns, workers, g,
            $"N.WorkEstimate.20261028.v1;CpuAnchors=33,42,210ns;Draws={work.Draws:R};PoolVisits={work.PoolVisits:R};Comparisons={work.Comparisons:R};Routes={work.Routes};" +
            "OwnFirstGateReachOnly;UnknownLocalReachUsesFullWork;FourWay18nsEstimated;NoJointPrice;NoNewMeasurement", capacity);
    }
}
