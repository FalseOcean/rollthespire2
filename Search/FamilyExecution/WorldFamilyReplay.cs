using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
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

    internal static NeowSearchFilter Filter(SearchModel request)
    {
        var q = request.CompiledSearch.NormalizedQuery;
        return NeowSearchFilter.Empty with
        {
            EncounterQueues = q.EncounterQueues,
            BossFilters = q.LegacyWorld.BossFilters,
            BossOrdinalFilters = q.LegacyWorld.BossOrdinalFilters,
            AncientIdentityFilters = q.LegacyWorld.AncientIdentityFilters,
            AncientBranchConditions = q.AncientBranches.Select(b => b with { OptionAny = [], SeaGlassTargetAny = [] }).ToArray(),
            EventSequenceConditions = q.EventSequenceConstraints
        };
    }

    internal static bool HasConditions(SearchModel request)
    {
        var f = Filter(request);
        return request.CompiledSearch.NormalizedQuery.VariantBossBranches.Any(b => !b.FirstBoss.IsEmpty || !b.SecondBoss.IsEmpty) ||
            f.EncounterQueues.Count > 0 || f.BossFilters.Any(p => !p.IsEmpty) || f.BossOrdinalFilters.Any(p => !p.IsEmpty) ||
            f.AncientIdentityFilters.Any(p => !p.IsEmpty) || f.AncientBranchConditions.Any(p => p.IsValid) ||
            f.EventSequenceConditions.Any(p => !p.IsEmpty);
    }

    internal WorldFamilyReplay(SearchModel request) : this(request, Compile(request)) { }

    private static IEnumerable<Beta109ActGenerationSnapshot> ReachableEncounterActs(SearchModel request)
    {
        var generation = request.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null) yield break;
        var branches = request.CompiledSearch.NormalizedQuery.VariantBossBranches;
        foreach (var act in generation.OrderedActCatalog)
        {
            var group = generation.ActSelectionGroups.FirstOrDefault(g => g.Act == act.Act);
            if (act.Act == 1 && generation.Act1OverrideResolvedKey is { } first)
            { if (act.ActKey != first) continue; }
            else if (group is not null && group.EligibleActsInSourceOrder.Count > 0)
            {
                var eligible = group.SelectionMode == Beta109ActSelectionMode.DeterministicFirst
                    ? group.EligibleActsInSourceOrder.Take(1) : group.EligibleActsInSourceOrder;
                if (!eligible.Contains(act.ActKey)) continue;
            }
            if (branches.Any(b => b.Act == act.Act) && !branches.Any(b => b.Act == act.Act && b.VariantKey == act.ActKey)) continue;
            yield return act;
        }
    }

    internal static string[] EncounterRemainders(SearchModel request)
    {
        if (request.CompiledSearch.NormalizedQuery.EncounterQueues.Count == 0) return [];
        var world = request.Authority.WorldAuthority;
        var generation = world?.Beta109Generation;
        if (generation is null) return [];
        return ReachableEncounterActs(request).Where(act => request.CompiledSearch.NormalizedQuery.EncounterQueues.Any(q => q.Act == act.Act) &&
            act.ActKey.Entry == "OVERGROWTH" && (!generation.TestModeFactExact || generation.TestModeIsOff) &&
            (world!.EncounterNumberOfRuns is null || world.EncounterNumberOfRuns == 0 &&
                (generation.Profile != Compatibility.RuntimeProfileId.Beta111 || !generation.TestModeFactExact || !generation.ModeFactsExact ||
                 generation.GameMode != WorldGameMode.Singleplayer)))
            .Select(act => $"EncounterQueue:Act{act.Act}:{act.ActKey.Serialized}:EncounterDiscoveryContextMissing:ProductionExactOnly")
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool HasActualWorldCheck(SearchModel request, Beta110WorldFastPlan plan)
    {
        var generation = request.Authority.WorldAuthority!.Beta109Generation!;
        var reachable = ReachableEncounterActs(request).Select(a => (a.Act, a.ActKey)).ToHashSet();
        for (int index = 0; index < generation.OrderedActCatalog.Count; index++)
        {
            var source = generation.OrderedActCatalog[index];
            if (!reachable.Contains((source.Act, source.ActKey))) continue;
            var act = plan.Acts[index];
            if (act.HasEventPredicate || act.HasBossPredicate || act.HasAncientPredicate ||
                act.HasEncounterPredicate && act.EncounterDiscoveryMode != 2) return true;
        }
        return false;
    }

    internal static Beta110WorldFastPlan Compile(SearchModel request)
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
        if (plan.Enabled && filter.EncounterQueues.Count > 0 && !HasActualWorldCheck(request, plan))
            return Beta110WorldFastPlan.Disabled("W.EncounterDiscoveryContextMissing.NoParticipatingWorldCheck");
        if (plan.Enabled && plan.MaxRequiredAct > 0 && !HasRelicAuthority(request))
            return Beta110WorldFastPlan.Disabled("W.RootRelicInitializationAuthorityIncomplete");
        return plan;
    }

    internal WorldFamilyReplay(SearchModel request, Beta110WorldFastPlan plan)
    {
        Plan = plan;
        VariantFilterMigrated = request.CompiledSearch.NormalizedQuery.VariantBossBranches.Count > 0;
        if (!Plan.Enabled) throw new InvalidOperationException("W.Compilation:" + Plan.DisableReason);
        var finalActs = Plan.Acts.Where(a => a.Act == Plan.MaxRequiredAct).ToArray();
        Plan = Plan with { RequiredFarthestStage = Plan.MaxRequiredAct == 0 ? "ActVariant" :
            Plan.RequiresSecondBoss ? "SecondBoss" : finalActs.Any(a => a.HasAncientIdentityPredicate) ? "AncientIdentity" :
            finalActs.Any(a => a.HasBossPredicate) ? "BossIdentity" : finalActs.Any(a => a.HasEncounterPredicate) ? "EncounterQueue" : "EventEffectiveCandidate" };
        var generation = request.Authority.WorldAuthority!.Beta109Generation!;
        if (Plan.MaxRequiredAct > 0) ValidateRelicAuthority(request);
        BucketLengths = Plan.MaxRequiredAct == 0 ? [] : generation.SharedRelicBuckets.Concat(generation.IsMultiplayer
                ? generation.PartyRelicBuckets.SelectMany(buckets => buckets) : generation.PlayerRelicBuckets)
            .Select(b => checked((uint)b.OrderedRelics.Count)).ToArray();
    }

    internal static void ValidateRelicAuthority(SearchModel request)
    {
        if (!HasRelicAuthority(request))
            throw new InvalidOperationException("W.RootRelicInitializationAuthorityIncomplete");
    }

    private static bool HasRelicAuthority(SearchModel request)
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
        foreach (uint length in BucketLengths)
            if (Plan.UsesChildShuffle) rng.ConsumeShuffleSeed();
            else rng.ConsumeUnstableShuffle(checked((int)length));
        return Beta110WorldFastStage.EvaluateForFamily(root, rng.CaptureCheckpoint(), Plan).Decision !=
            Beta110FastDecision.CandidateReject;
    }
}
