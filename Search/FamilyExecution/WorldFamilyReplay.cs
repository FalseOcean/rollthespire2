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
    internal string PricingSignature => $"act={Plan.MaxRequiredAct};stage={Plan.RequiredFarthestStage};second={Plan.RequiresSecondBoss};" +
        string.Join('|', Plan.Acts.Select(a => $"a{a.Act}:eventPool={a.EventCandidates.Length}:eventPred={a.EventPredicates.Length}:" +
            $"h={a.EventPredicates.Select(p => (int)p.RangeValue).DefaultIfEmpty(0).Max()}:boss={a.HasBossPredicate}:ancient={a.HasAncientPredicate}"));
    private static readonly ulong SelectionHash = XxHash64.Hash("act_selection"u8, 0);
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
        return request.CompiledSearch.NormalizedQuery.VariantBossBranches.Count > 0 ||
            f.BossFilters.Any(p => !p.IsEmpty) || f.BossOrdinalFilters.Any(p => !p.IsEmpty) ||
            f.AncientIdentityFilters.Any(p => !p.IsEmpty) || f.AncientBranchConditions.Any(p => p.IsValid) ||
            f.EventSequenceConditions.Any(p => !p.IsEmpty);
    }

    internal WorldFamilyReplay(ExactSearchExecutionRequest request) : this(request, Compile(request)) { }

    internal static Beta110WorldFastPlan Compile(ExactSearchExecutionRequest request)
    {
        var filter = Filter(request);
        var branches = request.CompiledSearch.NormalizedQuery.VariantBossBranches;
        bool variantOnly = branches.Count > 0 && branches.All(b => b.FirstBoss.IsEmpty && b.SecondBoss.IsEmpty) &&
            !filter.BossFilters.Any(p => !p.IsEmpty) && !filter.BossOrdinalFilters.Any(p => !p.IsEmpty) &&
            !filter.AncientIdentityFilters.Any(p => !p.IsEmpty) && filter.AncientBranchConditions.Count == 0 &&
            !filter.EventSequenceConditions.Any(p => !p.IsEmpty);
        return variantOnly ? CompileVariant(request) : Beta110WorldFastPlanCompiler.CompileForFamily(request, filter, branches);
    }

    internal WorldFamilyReplay(ExactSearchExecutionRequest request, Beta110WorldFastPlan plan)
    {
        Plan = plan;
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
        var generation = request.Authority.WorldAuthority!.Beta109Generation!;
        if (!(generation.RelicInitializationExact && generation.SharedRelicPoolOrderExact &&
            generation.CharacterRelicPoolOrderExact && generation.RelicRarityAuthorityExact && generation.PlayerRelicPoolCompositionExact &&
            generation.SharedRelicBuckets.Count > 0 && generation.PlayerRelicBuckets.Count > 0 &&
            generation.SharedRelicBuckets.Concat(generation.PlayerRelicBuckets).All(b => b.OrderExact) &&
            (!generation.IsMultiplayer || generation.HasExactFixedParty)))
            throw new InvalidOperationException("W.RootRelicInitializationAuthorityIncomplete");
    }

    private static Beta110WorldFastPlan CompileVariant(ExactSearchExecutionRequest request)
    {
        var g = request.Authority.WorldAuthority?.Beta109Generation;
        if (g is null || !g.CanReconstructSelectedActs || g.Profile != request.ProfileId)
            throw new InvalidOperationException("W.VariantAuthorityIncomplete");
        var groups = g.ActSelectionGroups.OrderBy(a => a.Act).ToArray();
        var keys = groups.SelectMany(a => a.EligibleActsInSourceOrder).Concat(
            g.Act1OverrideResolvedKey is { } key ? [key] : []).Distinct().ToArray();
        ushort Id(RolltheSpire2.Core.Identity.ModelKey key) => checked((ushort)Array.IndexOf(keys,key));
        var branches = request.CompiledSearch.NormalizedQuery.VariantBossBranches;
        var acts = keys.Select(key =>
        {
            int act = g.Act1OverrideResolvedKey == key ? 1 : groups.First(a => a.EligibleActsInSourceOrder.Contains(key)).Act;
            return new WorldFastActPlan(act,Id(key),0,0,0,[],0,[],[],[],[],[],[],[],[],[],[],[],[],[],[],[],[])
            { FamilyVariantAllowed = !branches.Any(b => b.Act == act) || branches.Any(b => b.Act == act && b.VariantKey == key) };
        }).ToArray();
        return Beta110WorldFastPlan.Disabled("") with
        {
            Enabled=true, Acts=acts, ActPlanIndexByDenseId=Enumerable.Range(0,keys.Length).Select(i=>checked((short)i)).ToArray(),
            ActSelectionGroups=groups.Select(a=>new WorldFastActSelectionGroup(a.Act,
                a.SelectionMode==RolltheSpire2.Core.World.Snapshots.Beta109ActSelectionMode.DeterministicFirst?(byte)1:(byte)0,
                a.EligibleActsInSourceOrder.Select(Id).ToArray())).ToArray(),
            Act1OverrideId=g.Act1OverrideResolvedKey is { } selected ? Id(selected) : ushort.MaxValue,
            RequiredFarthestStage="ActVariant", Fingerprint=request.CompiledSearch.SemanticFingerprint
        };
    }

    internal bool Matches(ulong root)
    {
        // All variant branches are checked before any UpFront work, including
        // variants in later Acts than the last non-variant observation.
        var selection = new Beta110FastRng(unchecked(root + SelectionHash));
        for (int i = 0; i < Plan.ActSelectionGroups.Length; i++)
        {
            var group = Plan.ActSelectionGroups[i];
            ushort id = group.EligibleActIds[group.SelectionMode == 1 ? 0 : selection.NextInt(group.EligibleActIds.Length)];
            if (i == 0 && Plan.Act1OverrideId != Beta110WorldFastPlan.InvalidDenseId) id = Plan.Act1OverrideId;
            int index = id < Plan.ActPlanIndexByDenseId.Length ? Plan.ActPlanIndexByDenseId[id] : -1;
            if (index < 0) throw new InvalidOperationException("W.ActSelectionCatalogMismatch");
            if (!Plan.Acts[index].FamilyVariantAllowed) return false;
        }
        if (Plan.MaxRequiredAct == 0) return true;
        var rng = new Beta110FastRng(unchecked(root + UpFrontHash));
        foreach (uint length in BucketLengths) rng.ConsumeUnstableShuffle(checked((int)length));
        return Beta110WorldFastStage.EvaluateForFamily(root, rng.CaptureCheckpoint(), Plan).Decision !=
            Beta110FastDecision.CandidateReject;
    }
}
