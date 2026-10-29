using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.FamilyExecution;

// Immutable W inputs only. Relic initialization is a draw-count prerequisite,
// not a dependency on R execution or R-owned observations.
internal sealed class WorldFamilyReplay
{
    internal Beta110WorldFastPlan Plan { get; }
    internal uint[] BucketLengths { get; }
    internal bool VariantFilterMigrated { get; }
    internal string PricingSignature => $"act={Plan.MaxRequiredAct};stage={Plan.RequiredFarthestStage};second={Plan.RequiresSecondBoss};" +
        string.Join('|', Plan.Acts.Select(a => $"a{a.Act}:eventPool={a.EventCandidates.Length}:eventPred={a.EventPredicates.Length}:" +
            $"h={a.EventPredicates.Select(p => (int)p.RangeValue).DefaultIfEmpty(0).Max()}:boss={a.HasBossPredicate}:ancient={a.HasAncientPredicate}"));
    private static readonly ulong UpFrontHash = XxHash64.Hash("up_front"u8, 0);

    internal static NeowSearchFilter Filter(ExactSearchExecutionRequest request)
    {
        var q = request.CompiledSearch.NormalizedQuery;
        return NeowSearchFilter.Empty with
        {
            BossFilters = q.LegacyWorld.BossFilters,
            BossOrdinalFilters = q.LegacyWorld.BossOrdinalFilters,
            AncientIdentityFilters = q.LegacyWorld.AncientIdentityFilters,
            AncientBranchConditions = q.AncientBranches.Select(b => b with { OptionAny = [], SeaGlassTargetAny = [] }).ToArray(),
            EventSequenceConditions = q.EventSequenceConstraints
        };
    }

    internal static bool HasConditions(ExactSearchExecutionRequest request)
    {
        var f = Filter(request);
        return request.CompiledSearch.NormalizedQuery.VariantBossBranches.Any(b => !b.FirstBoss.IsEmpty || !b.SecondBoss.IsEmpty) ||
            f.BossFilters.Any(p => !p.IsEmpty) || f.BossOrdinalFilters.Any(p => !p.IsEmpty) ||
            f.AncientIdentityFilters.Any(p => !p.IsEmpty) || f.AncientBranchConditions.Any(p => p.IsValid) ||
            f.EventSequenceConditions.Any(p => !p.IsEmpty);
    }

    internal WorldFamilyReplay(ExactSearchExecutionRequest request) : this(request, Compile(request)) { }

    internal static Beta110WorldFastPlan Compile(ExactSearchExecutionRequest request)
    {
        var filter = Filter(request);
        var branches = request.CompiledSearch.NormalizedQuery.VariantBossBranches;
        if (!HasConditions(request)) return Beta110WorldFastPlan.Disabled("W.NoResidualWorldPredicates");
        var plan = Beta110WorldFastPlanCompiler.CompileForFamily(request, filter, branches);
        // V owns the necessary variant restriction. W still selects each real
        // variant and applies its own associated Boss rows and content pools.
        // Outside the authored variant set those rows are vacuous; V/Exact
        // retains the excluded-variant obligation.
        if (plan.Enabled) plan = plan with
        { Acts = plan.Acts.Select(act => act with { FamilyVariantAllowed = true }).ToArray() };
        if (plan.Enabled && plan.MaxRequiredAct > 0 && !HasRelicAuthority(request))
            return Beta110WorldFastPlan.Disabled("W.RootRelicInitializationAuthorityIncomplete");
        return plan;
    }

    internal WorldFamilyReplay(ExactSearchExecutionRequest request, Beta110WorldFastPlan plan)
    {
        Plan = plan;
        VariantFilterMigrated = request.CompiledSearch.NormalizedQuery.VariantBossBranches.Count > 0;
        if (!Plan.Enabled) throw new InvalidOperationException("W.Compilation:" + Plan.DisableReason);
        var finalActs = Plan.Acts.Where(a => a.Act == Plan.MaxRequiredAct).ToArray();
        Plan = Plan with { RequiredFarthestStage = Plan.MaxRequiredAct == 0 ? "ActVariant" :
            Plan.RequiresSecondBoss ? "SecondBoss" : finalActs.Any(a => a.HasAncientIdentityPredicate) ? "AncientIdentity" :
            finalActs.Any(a => a.HasBossPredicate) ? "BossIdentity" : "EventEffectiveCandidate" };
        var generation = request.Authority.WorldAuthority!.Beta109Generation!;
        if (Plan.MaxRequiredAct > 0) ValidateRelicAuthority(request);
        BucketLengths = Plan.MaxRequiredAct == 0 ? [] : generation.SharedRelicBuckets.Concat(generation.IsMultiplayer
                ? generation.PartyRelicBuckets.SelectMany(buckets => buckets) : generation.PlayerRelicBuckets)
            .Select(b => checked((uint)b.OrderedRelics.Count)).ToArray();
    }

    internal static void ValidateRelicAuthority(ExactSearchExecutionRequest request)
    {
        if (!HasRelicAuthority(request))
            throw new InvalidOperationException("W.RootRelicInitializationAuthorityIncomplete");
    }

    private static bool HasRelicAuthority(ExactSearchExecutionRequest request)
    {
        var generation = request.Authority.WorldAuthority?.Beta109Generation;
        return generation is not null && generation.RelicInitializationExact && generation.SharedRelicPoolOrderExact &&
            generation.CharacterRelicPoolOrderExact && generation.RelicRarityAuthorityExact && generation.PlayerRelicPoolCompositionExact &&
            generation.SharedRelicBuckets.Count > 0 && generation.PlayerRelicBuckets.Count > 0 &&
            generation.SharedRelicBuckets.Concat(generation.PlayerRelicBuckets).All(b => b.OrderExact) &&
            (!generation.IsMultiplayer || generation.HasExactFixedParty);
    }

    internal bool Matches(ulong root)
    {
        if (Plan.MaxRequiredAct == 0) return true;
        var rng = new Beta110FastRng(unchecked(root + UpFrontHash));
        foreach (uint length in BucketLengths) rng.ConsumeUnstableShuffle(checked((int)length));
        return Beta110WorldFastStage.EvaluateForFamily(root, rng.CaptureCheckpoint(), Plan).Decision !=
            Beta110FastDecision.CandidateReject;
    }
}
