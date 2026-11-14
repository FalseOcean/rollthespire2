using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

// Closed direct solo predicates. Compilation proves the entire local operation
// cannot fault before moving it ahead of the independent initial-offer check.
// Production Exact retains result truth; no state escapes this invocation.
internal sealed class NeowSingleplayerLocalCpuPlan
{
    internal const string ModernSelection = "RT2_N_CPU_LOCAL";
    private enum Operation { Index, LostPotion, Kaleidoscope, ModernDirect, ModernFixedPair }
    private readonly Operation _operation;
    private readonly NeowReplayPlan _plan;
    private readonly ushort[] _pool;
    private readonly ushort _target;
    private readonly ulong _streamHash;
    private readonly NeowCpuPositiveIdentityPlan? _identity;
    private readonly NeowSoloResultObservation? _observation;
    private readonly NeowIdentityCpuPlan? _trackedPair;
    private readonly (byte First, byte Second, int Rewards, int NonCombat)[] _routes = [];
    internal string Revision => (_operation switch {
        Operation.Index => "N.Neow.Cpu.DirectLocalFirst.20261015.v1",
        Operation.LostPotion => "N.Neow.Cpu.LostPotionLocalFirst.20261015.v1",
        Operation.ModernDirect => "N.Neow.Cpu.Direct." + _observation!.Shape + ".20261114.v1",
        Operation.ModernFixedPair => "N.Neow.Cpu.FixedPair." + (_observation?.Shape ?? "RouteReplay") + ".20261114.v1",
        _ => "N.Neow.Cpu.DirectMinimalObservation.20261015.v1"
    }) + ".LocalPoolAuthority.20261114.v1" + (_identity is null ? "" : NeowCpuPositiveIdentityPlan.Revision);
    private NeowSingleplayerLocalCpuPlan(NeowReplayPlan plan, ushort[] pool, ushort target, ulong streamHash,
        NeowCpuPositiveIdentityPlan? identity)
    { _plan = plan; _pool = pool; _target = target; _streamHash = streamHash; _identity = identity; }
    private NeowSingleplayerLocalCpuPlan(NeowReplayPlan plan, Operation operation, NeowCpuPositiveIdentityPlan? identity)
    { _plan = plan; _pool = []; _target = plan.StructuredConditions[0].Target0; _operation = operation; _identity = identity; }

    private NeowSingleplayerLocalCpuPlan(NeowReplayPlan plan, NeowSoloResultObservation? observation,
        NeowCpuPositiveIdentityPlan? identity, NeowIdentityCpuPlan? trackedPair,
        (byte First, byte Second, int Rewards, int NonCombat)[] routes)
    {
        _plan = plan;
        _pool = [];
        _operation = trackedPair is null ? Operation.ModernDirect : Operation.ModernFixedPair;
        _identity = identity;
        _observation = observation;
        _trackedPair = trackedPair;
        _routes = routes;
    }

    internal static NeowSingleplayerLocalCpuPlan? TryCompile(NeowReplayPlan plan, NeowCpuPositiveIdentityPlan? identity = null)
    {
        var a = plan.Authority;
        if (a.PlayersCount != 1 || a.PlayerSlotIndex != 0 || !plan.AuditedIdentityReplay ||
            !a.IdentityAuthorityExact || a.EligibleCurseRelicIds.Length == 0) return null;
        if (plan.AuditedModern113IdentityReplay && a.UsesChildShuffle)
        {
            if (string.Equals(Environment.GetEnvironmentVariable(ModernSelection), "off", StringComparison.OrdinalIgnoreCase))
                return null;
            var observation = NeowSoloResultObservation.TryCompile(plan);
            if (plan.Bones) return TryCompileModernPair(plan, observation);
            if (!plan.RequireBones && observation is not null && plan.Selected == observation.Source)
                return new(plan, observation, identity, null, []);
        }
        if (plan.Bones || plan.RequireBones || plan.HasFinalCurseFastProjection || plan.ExactOnly.Length != 0 ||
            plan.StructuredConditions is not [var condition] || condition.SourceRelicId != plan.Selected)
            return null;
        var c = a.EffectCatalog;
        if (plan.Selected == Beta110FastRelicCatalog.LostCoffer && condition.TargetCount == 1 &&
            condition.Kind == Beta110FastStructuredConditionKind.LostCofferPotion &&
            plan.EnabledDomains == Beta110FastDomain.LostCofferOffer && c.CharacterRewardAuthorityExact && c.PotionAuthorityExact &&
            ThreeDistinctValid(c.CharacterRewardPool.Common) && ThreeDistinctValid(c.CharacterRewardPool.Uncommon) &&
            ThreeDistinctValid(c.CharacterRewardPool.Rare) && c.PotionPool.HasEveryRarity &&
            Valid(c.PotionPool.Common) && Valid(c.PotionPool.Uncommon) && Valid(c.PotionPool.Rare))
            return new(plan, Operation.LostPotion, identity);
        if (plan.Selected == Beta110FastRelicCatalog.Kaleidoscope && a.AllCharacterCardPoolsUnlocked && condition.TargetCount == 2 && !condition.OrderedKaleidoscope &&
            condition.Kind == Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets &&
            plan.EnabledDomains == Beta110FastDomain.KaleidoscopeOffers && c.OtherCharacterCardAuthorityExact &&
            c.OtherCharacterPools.Length is >= 3 and <= 16 && c.OtherCharacterPools.All(p => p.TotalCount > 0 &&
                Valid(p.Common) && Valid(p.Uncommon) && Valid(p.Rare)))
            return new(plan, Operation.Kaleidoscope, identity);
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
        { pool = c.NewLeafTransformPool; hash = XxHash64.Hash(a.UsesChildShuffle ? "transformations"u8 : "niche"u8, 0); }
        else return null;
        // The original empty-used draw is exactly one index in the original
        // array, including its duplicate weight. Invalid outputs retain replay.
        if (pool.Length == 0 || pool.Contains(Beta110FastDenseId.Invalid)) return null;
        return new(plan, pool, condition.Target0, hash, identity);
    }

    private static NeowSingleplayerLocalCpuPlan? TryCompileModernPair(NeowReplayPlan plan, NeowSoloResultObservation? observation)
    {
        if (plan.StructuredConditions.Length == 0 && !plan.HasFinalCurseFastProjection) return null;
        var identityOnly = plan with { StructuredConditions = [], EnabledDomains = Beta110FastDomain.None, ExactOnly = [] };
        if (NeowIdentityCpuPlan.TryCompile(identityOnly) is not { } trackedPair) return null;
        var sources = plan.Authority.BonesEligibleRelicIds.Where(id =>
            (Beta110FastRelicCatalog.Bit(id) & plan.BonesAll) != 0).ToArray();
        if (sources.Length != 2) return null;
        (byte First, byte Second)[] paths = plan.First != 255 && plan.Second != 255
            ? [(plan.First, plan.Second)] : [(sources[0], sources[1]), (sources[1], sources[0])];
        var routes = new (byte First, byte Second, int Rewards, int NonCombat)[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            var (first, second) = paths[i];
            if (observation is null || !TryProveArrival(plan, observation, first, second, out int rewards, out int nc))
            {
                observation = null;
                for (int j = 0; j < paths.Length; j++) routes[j] = (paths[j].First, paths[j].Second, 0, 0);
                break;
            }
            routes[i] = (first, second, rewards, nc);
        }
        return new(plan, observation, null, trackedPair, routes);
    }

    private static bool TryProveArrival(NeowReplayPlan plan, NeowSoloResultObservation observation,
        byte first, byte second, out int rewards, out int nonCombat)
    {
        rewards = nonCombat = 0;
        byte source = observation.Source;
        if (first != source && second != source) return false;
        byte companion = first == source ? second : first;
        bool precedes = first == companion;
        var catalog = plan.Authority.EffectCatalog;
        // For a terminal result we may omit the rest of this route only when
        // the generic companion's unobserved generation is proved to succeed.
        if (observation.UsesRewards)
        {
            if (!RelicOpeningPrefixPlan.TryProveCompanionRewardsDraws(plan, companion, out int draws)) return false;
            if (precedes) rewards = draws;
        }
        if (companion == Beta110FastRelicCatalog.PhialHolster && plan.NeedsPotions)
        {
            if (!catalog.PotionAuthorityExact || !NeowSoloResultObservation.ValidPotions(catalog.PotionPool, 2)) return false;
            if (precedes && observation.UsesNonCombat) nonCombat = 4;
        }
        else if (companion == Beta110FastRelicCatalog.Kaleidoscope && plan.NeedsNicheOrKaleidoscopePools)
        {
            if (!catalog.OtherCharacterCardAuthorityExact || catalog.OtherCharacterPools.Length < 3) return false;
            if (precedes && observation.UsesNonCombat) nonCombat = 2;
        }
        if (precedes && observation.UsesNonCombat &&
            (companion is NeowCapsuleNonCombatContinuation.Small or NeowCapsuleNonCombatContinuation.Large))
        {
            uint mask = NeowCapsuleNonCombatContinuation.AdvanceMask(plan, companion);
            if (!System.Numerics.BitOperations.IsPow2(mask)) return false;
            nonCombat = System.Numerics.BitOperations.TrailingZeroCount(mask);
        }
        if (observation.UsesTransformations)
        {
            if (companion is Beta110FastRelicCatalog.LeafyPoultice or Beta110FastRelicCatalog.NewLeaf) return false;
            if (precedes && source == Beta110FastRelicCatalog.LeafyPoultice && !plan.LeafyBonesInitialTransformInvariant) return false;
        }
        return true;
    }

    private bool IdentityPass(ulong root) => _identity is null
        ? NeowSingleplayerReplay.Observe(root, _plan).IdentityPass : _identity.PassesIdentity(root);

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
        if (_operation == Operation.ModernDirect)
        {
            var observation = _observation!;
            return observation.LocalFirst
                ? MatchModernAtArrival(root, 0, 0) && IdentityPass(root)
                : IdentityPass(root) && MatchModernAtArrival(root, 0, 0);
        }
        if (_operation == Operation.ModernFixedPair)
        {
            if (!_trackedPair!.Matches(root)) return false;
            var rewards = _observation is null ? new Beta110FastRng(unchecked(root + NeowSingleplayerReplay.RewardsHash)) : default;
            foreach (var route in _routes)
            {
                bool match = _observation is null
                    ? NeowSingleplayerReplay.Route(root, _plan, route.First, route.Second, rewards)
                    : MatchModernAtArrival(root, route.Rewards, route.NonCombat);
                if (match) return true;
            }
            return false;
        }
        if (_operation == Operation.LostPotion)
        {
            // Compilation proves both this local and the delayed identity cannot
            // fault: nonempty curse IDs, fixed positives (10..13 admitted plus
            // 2..3 variants fit the 17-byte span), and no Bones pool indexing.
            // The original weighted card/potion draws are independent of NEOW.
            var rewards = new Beta110FastRng(unchecked(root + NeowSingleplayerReplay.RewardsHash));
            BurnLostCofferCards(ref rewards);
            if (NeowLocalOperators.RollPotion(_plan.Authority.EffectCatalog.PotionPool, [], ref rewards) != _target) return false;
            return IdentityPass(root);
        }
        if (_operation == Operation.Kaleidoscope)
        {
            // K retains N04's offer-first ordering; no new fault proof for the
            // complete two-group operation is claimed by the Lost-only change.
            if (!IdentityPass(root)) return false;
            var rewards = new Beta110FastRng(unchecked(root + NeowSingleplayerReplay.RewardsHash));
            var semanticProfile = _plan.Authority.RngSemantics;
            ulong shuffleHash = _plan.Authority.UsesChildShuffle ? NeowFamilyReplay.NonCombatHash : XxHash64.Hash("niche"u8, 0);
            var shuffle = new Beta110FastRng(unchecked(root + shuffleHash));
            Span<ushort> offers = stackalloc ushort[6];
            var catalog = _plan.Authority.EffectCatalog;
            if (!NeowLocalOperators.GenerateKaleidoscopeOffers(_plan.Authority.Ascension, catalog, ref rewards, ref shuffle,
                    offers[..3], groups: 1, semanticProfile: semanticProfile))
                throw new InvalidOperationException("NFamilyContinuationAuthorityUnavailable");
            ushort other = _plan.StructuredConditions[0].Target1;
            bool first0 = offers[..3].Contains(_target), first1 = offers[..3].Contains(other);
            if (!first0 && !first1) return false;
            if (!NeowLocalOperators.GenerateKaleidoscopeOffers(_plan.Authority.Ascension, catalog, ref rewards, ref shuffle,
                    offers[3..], groups: 1, semanticProfile: semanticProfile))
                throw new InvalidOperationException("NFamilyContinuationAuthorityUnavailable");
            return first0 && offers[3..].Contains(other) || first1 && offers[3..].Contains(_target);
        }
        var rng = new Beta110FastRng(unchecked(root + _streamHash));
        if (_pool[rng.NextInt(_pool.Length)] != _target) return false;
        return IdentityPass(root);
    }

    private bool MatchModernAtArrival(ulong root, int rewardsDraws, int nonCombatDraws)
    {
        var observation = _observation!;
        var rewards = observation.UsesRewards ? new Beta110FastRng(unchecked(root + NeowSingleplayerReplay.RewardsHash)) : default;
        var transforms = observation.UsesTransformations ? new Beta110FastRng(unchecked(root + NeowSoloResultObservation.TransformationsHash)) : default;
        var nonCombat = observation.UsesNonCombat ? NeowFamilyReplay.NonCombatRng(root, _plan) : default;
        RelicOpeningPrefixPlan.AdvanceObservedRewards(rewardsDraws, ref rewards);
        for (int draw = 0; draw < nonCombatDraws; draw++) nonCombat.ConsumeShuffleSeed();
        return observation.Matches(ref rewards, ref transforms, ref nonCombat);
    }
}
