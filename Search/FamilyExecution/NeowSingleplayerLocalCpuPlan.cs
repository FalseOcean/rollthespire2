using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

// Closed direct solo predicates. Compilation proves the entire local operation
// cannot fault before moving it ahead of the independent initial-offer check.
// Production Exact retains result truth; no state escapes this invocation.
internal sealed class NeowSingleplayerLocalCpuPlan
{
    private enum Operation { Index, LostPotion, Kaleidoscope }
    private readonly Operation _operation;
    private readonly NeowReplayPlan _plan;
    private readonly ushort[] _pool;
    private readonly ushort _target;
    private readonly ulong _streamHash;
    internal string Revision => _operation switch {
        Operation.Index => "N.Neow.Cpu.DirectLocalFirst.20261015.v1",
        Operation.LostPotion => "N.Neow.Cpu.LostPotionLocalFirst.20261015.v1",
        _ => "N.Neow.Cpu.DirectMinimalObservation.20261015.v1"
    };
    private NeowSingleplayerLocalCpuPlan(NeowReplayPlan plan, ushort[] pool, ushort target, ulong streamHash)
    { _plan = plan; _pool = pool; _target = target; _streamHash = streamHash; }
    private NeowSingleplayerLocalCpuPlan(NeowReplayPlan plan, Operation operation)
    { _plan = plan; _pool = []; _target = plan.StructuredConditions[0].Target0; _operation = operation; }

    internal static NeowSingleplayerLocalCpuPlan? TryCompile(NeowReplayPlan plan)
    {
        var a = plan.Authority;
        if (a.PlayersCount != 1 || a.PlayerSlotIndex != 0 || !plan.DirectNestedVanilla111 ||
            !a.IdentityAuthorityExact || a.EligibleCurseRelicIds.Length == 0 || !a.AllCharacterCardPoolsUnlocked ||
            plan.Bones || plan.RequireBones || plan.HasFinalCurseFastProjection || plan.ExactOnly.Length != 0 ||
            plan.StructuredConditions is not [var condition] || condition.SourceRelicId != plan.Selected)
            return null;
        var c = a.EffectCatalog;
        if (plan.Selected == Beta110FastRelicCatalog.LostCoffer && condition.TargetCount == 1 &&
            condition.Kind == Beta110FastStructuredConditionKind.LostCofferPotion &&
            plan.EnabledDomains == Beta110FastDomain.LostCofferOffer && c.CharacterRewardAuthorityExact && c.PotionAuthorityExact &&
            ThreeDistinctValid(c.CharacterRewardPool.Common) && ThreeDistinctValid(c.CharacterRewardPool.Uncommon) &&
            ThreeDistinctValid(c.CharacterRewardPool.Rare) && c.PotionPool.HasEveryRarity &&
            Valid(c.PotionPool.Common) && Valid(c.PotionPool.Uncommon) && Valid(c.PotionPool.Rare))
            return new(plan, Operation.LostPotion);
        if (plan.Selected == Beta110FastRelicCatalog.Kaleidoscope && condition.TargetCount == 2 && !condition.OrderedKaleidoscope &&
            condition.Kind == Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets &&
            plan.EnabledDomains == Beta110FastDomain.KaleidoscopeOffers && c.OtherCharacterCardAuthorityExact &&
            c.OtherCharacterPools.Length is >= 3 and <= 16 && c.OtherCharacterPools.All(p => p.TotalCount > 0 &&
                Valid(p.Common) && Valid(p.Uncommon) && Valid(p.Rare)))
            return new(plan, Operation.Kaleidoscope);
        if (condition.TargetCount != 1) return null;
        ushort[] pool;
        ulong hash;
        if (plan.Selected == Beta110FastRelicCatalog.ArcaneScroll &&
            condition.Kind == Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard &&
            plan.EnabledDomains == Beta110FastDomain.ArcaneScrollOffer && c.CharacterRewardAuthorityExact)
        { pool = c.CharacterRewardPool.Rare; hash = NeowSingleplayerReplay.RewardsHash; }
        else if (plan.Selected == Beta110FastRelicCatalog.NewLeaf &&
            condition.Kind == Beta110FastStructuredConditionKind.NewLeafTransform &&
            plan.EnabledDomains == Beta110FastDomain.NewLeafTransform && c.NewLeafTransformAuthorityExact)
        { pool = c.NewLeafTransformPool; hash = XxHash64.Hash("niche"u8, 0); }
        else return null;
        // The original empty-used draw is exactly one index in the original
        // array, including its duplicate weight. Invalid outputs retain replay.
        if (pool.Length == 0 || pool.Contains(Beta110FastDenseId.Invalid)) return null;
        return new(plan, pool, condition.Target0, hash);
    }

    private static bool Valid(ushort[] pool) => !pool.Contains(Beta110FastDenseId.Invalid);
    private static bool ThreeDistinctValid(ushort[] pool) => Valid(pool) && pool.Distinct().Take(3).Count() == 3;

    // Three successful normal-card draws each consume rarity, original weighted
    // index, and upgrade. The compile proof prevents every exhaustion/fallback
    // failure; only their unobserved identities are omitted.
    internal static void BurnLostCofferCards(ref Beta110FastRng rewards)
    {
        for (int draw = 0; draw < 9; draw++) _ = rewards.NextFloat();
    }

    internal bool Matches(ulong root)
    {
        if (_operation == Operation.LostPotion)
        {
            // Compilation proves both this local and the delayed identity cannot
            // fault: nonempty curse IDs, fixed positives (10..13 admitted plus
            // 2..3 variants fit the 17-byte span), and no Bones pool indexing.
            // The original weighted card/potion draws are independent of NEOW.
            var rewards = new Beta110FastRng(unchecked(root + NeowSingleplayerReplay.RewardsHash));
            BurnLostCofferCards(ref rewards);
            if (NeowLocalOperators.RollPotion(_plan.Authority.EffectCatalog.PotionPool, [], ref rewards) != _target) return false;
            return NeowSingleplayerReplay.Observe(root, _plan).IdentityPass;
        }
        if (_operation == Operation.Kaleidoscope)
        {
            // K retains N04's offer-first ordering; no new fault proof for the
            // complete two-group operation is claimed by the Lost-only change.
            if (!NeowSingleplayerReplay.Observe(root, _plan).IdentityPass) return false;
            var rewards = new Beta110FastRng(unchecked(root + NeowSingleplayerReplay.RewardsHash));
            var niche = new Beta110FastRng(unchecked(root + XxHash64.Hash("niche"u8, 0)));
            Span<ushort> offers = stackalloc ushort[6];
            var catalog = _plan.Authority.EffectCatalog;
            if (!NeowLocalOperators.GenerateKaleidoscopeOffers(_plan.Authority.Ascension, catalog, ref rewards, ref niche, offers[..3], groups: 1))
                throw new InvalidOperationException("NFamilyContinuationAuthorityUnavailable");
            ushort other = _plan.StructuredConditions[0].Target1;
            bool first0 = offers[..3].Contains(_target), first1 = offers[..3].Contains(other);
            if (!first0 && !first1) return false;
            if (!NeowLocalOperators.GenerateKaleidoscopeOffers(_plan.Authority.Ascension, catalog, ref rewards, ref niche, offers[3..], groups: 1))
                throw new InvalidOperationException("NFamilyContinuationAuthorityUnavailable");
            return first0 && offers[3..].Contains(other) || first1 && offers[3..].Contains(_target);
        }
        var rng = new Beta110FastRng(unchecked(root + _streamHash));
        if (_pool[rng.NextInt(_pool.Length)] != _target) return false;
        return NeowSingleplayerReplay.Observe(root, _plan).IdentityPass;
    }
}
