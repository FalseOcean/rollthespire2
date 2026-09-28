using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class WorldPhysicalPricing
{
    internal static PrivateOrdinalAllocationPricing.Work LegacyEwWork(WorldFamilyGpuPlan plan) =>
        new(6.8, 8.5, plan.Capacity * plan.GroupingK);
    internal static PrivateSerialChainPricing.Node LegacyDirectCapsuleTail(WorldFamilyReplay replay,
        WorldFamilyGpuPlan plan, double survival)
    {
        bool act2 = replay.Plan.MaxRequiredAct == 2;
        return new('W', act2 ? "W.Progression.Act2.K4" : "W.Progression.Act3.K4",
            act2 ? 11.5 : 6.8, act2 ? 6 : 12, plan.Capacity * plan.GroupingK, survival, 190);
    }
    internal static FamilyPhysicalQuote? Quote(WorldFamilyReplay replay, WorldFamilyGpuPlan? plan,
        FamilyPhysicalQuoteRequest geometry)
    {
        if (plan is null ||
            !FamilyPhysicalQuote.AdmittedRequest(geometry) || !FamilyPhysicalQuote.HasReferenceBackend()) return null;
        var p = replay.Plan;
        int events = p.Acts.Sum(a => a.EventPredicates.Length);
        int ancients = p.Acts.Count(a => a.HasAncientPredicate);
        int bosses = p.Acts.Count(a => a.HasBossPredicate);
        bool h3 = p.Acts.All(a => a.EventPredicates.All(c => c.RangeValue == 3));
        string counts = string.Join(',', p.Acts.Select(a => a.EventPredicates.Length));
        double? ns = null; string shape = "";
        if (plan.VariantOnly && plan.ScratchWords == 0) { ns = .62; shape = "VariantOnly"; }
        else if (plan.ScratchWords == 12 && events == 0 && ancients + bosses == 1)
        {
            if (p.MaxRequiredAct == 2 && !p.RequiresSecondBoss) { ns = 7.0; shape = "Act2.SingleIdentity"; }
            else if (p.MaxRequiredAct == 3 && (ancients == 1 || p.RequiresSecondBoss))
            { ns = geometry.CompactInput ? 9.85 : 9.5; shape = "Act3.SingleIdentity"; }
        }
        else if (p.MaxRequiredAct == 1 && plan.ScratchWords == 31 && ancients + bosses == 0 &&
            h3 && p.Acts.All(a => a.EventPredicates.Length <= 1))
        { ns = 7.0; shape = "Act1.EventPrefix3"; }
        else if (p.MaxRequiredAct == 3 && plan.ScratchWords == 43 && h3)
        {
            if (counts is "1,1,2,0" or "1,1,3,0" && ancients == 2 && bosses == 0)
            { ns = 9.2; shape = "Act3.EventAndTwoAncient"; }
            else if (counts == "3,3,1,2" && ancients == 0 && bosses == 2 && p.RequiresSecondBoss)
            { ns = 7.5; shape = "Act3.EventAndTwoBoss"; }
        }
        bool coarse = false;
        if (ns is null && plan.GroupingK == 4 &&
            p.MaxRequiredAct is >= 1 and <= 3 && events <= 12 &&
            p.Acts.All(a => a.EventPredicates.All(c => c.RangeValue is >= 1 and <= 3)))
        {
            // Same progression/shuffle body. Constraints change rejection reach,
            // not generation authority. Invocation floors are charged separately,
            // including rare Compact singleton calls; a low mean population does
            // not change the owned progression loops. Existing scratch-class envelope;
            // do not price a new scratch layout or deeper event replay this way.
            if (plan.ScratchWords == 12) { ns = 10; shape = "IdentityMixCoarse"; }
            else if (plan.ScratchWords is >= 31 and <= 43) { ns = 12; shape = "EventMixCoarse"; }
            coarse = ns.HasValue;
        }
        if (ns is null) return null;
        return new("W.World", "W.Progression." + shape + $".S{plan.ScratchWords}.K{plan.GroupingK}",
            ns.Value, plan.Capacity * plan.GroupingK, 190,
            coarse ? "CapsuleFoundation.20260913;Model=ConservativeCoarse;ExistingScratchClass;EventPrefix<=3;InvocationFloorSeparate;NumericalDeviceEmission" :
            "FamilyMatrix.20260910.WorldGeometry;NumericalDeviceEmission;MeasuredSpecialization");
    }

    internal static FamilyPhysicalQuote? QuoteCpu(ExactSearchExecutionRequest request, WorldFamilyReplay replay,
        WorldFamilyGpuPlan? plan, FamilyPhysicalQuoteRequest g)
    {
        if (plan is null || !FamilyPhysicalQuote.AdmittedRequest(g) || g.CompactInput || g.PrivateInput ||
            g.PrivateOutput || g.MeanInputPopulation < 4096 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.ProfileId != Compatibility.RuntimeProfileId.Beta111) return null;
        var p = replay.Plan;
        double? ns = null;
        string shape = "";
        if (plan.VariantOnly && p.ActSelectionGroups.Length <= 4 &&
            p.ActSelectionGroups.All(a => a.EligibleActIds.Length is >= 1 and <= 2))
        { ns = 55; shape = "VariantOnly"; }
        else if (plan.ScratchWords == 12 && p.Acts.All(a => a.FamilyVariantAllowed && a.EventPredicates.Length == 0) &&
            p.Acts.Sum(a => (a.HasBossPredicate ? 1 : 0) + (a.HasAncientIdentityPredicate ? 1 : 0)) == 1 &&
            replay.BucketLengths.SequenceEqual(new uint[]{30,25,35,25,1,2,32,26,38,26}))
        {
            if (p.MaxRequiredAct == 2 && !p.RequiresSecondBoss) { ns = 2700; shape = "Act2.SingleIdentity"; }
            else if (p.MaxRequiredAct == 3) { ns = 3750; shape = "Act3.SingleIdentity"; }
        }
        // Event-prefix / constrained-variant reach is a different work class.
        // Do not linearly extend the identity buckets into those combinations.
        return ns is null ? null : new("W.World", "W.Progression.20260913.v1.P1." + shape, ns.Value,
            65536, null, "FamilyCostClosure.20260913;Model=BoundedCoarse;Workers=1;MinInput=4096;" +
            "CpuCanonicalAbi1;metric=ns/ActualStageInput", OutputElementBytes: 8,
            OutputAlreadyOrdered: true, PublicTransportClass: "CpuOrderedAbi1");
    }

    internal static double? ProjectOwnedSurvival(ExactSearchExecutionRequest request, double ancientConditional)
    {
        if (!(ancientConditional > 0 && ancientConditional <= 1)) return null;
        var world = SearchSelectivityEstimator.EstimateStage(SearchSelectivityInput.From(request), SearchSelectivityDomain.WorldEvent);
        if (!world.IsPriced || world.Probability is not double probability) return null;
        double owned = probability / ancientConditional;
        return owned is >= 0 and <= 1 && double.IsFinite(owned) ? owned : null;
    }

    internal static FamilySurvivalProjection ResolveSurvival(ExactSearchExecutionRequest request)
    {
        var e = request.Evaluation;
        bool hasOptions = e.AncientBranchConditions.Any(r => r.OptionAny.Count > 0 || r.SeaGlassTargetAny.Count > 0) ||
            e.AncientOptionFilters.Any(f => !f.IsEmpty) || e.AncientSeaGlassTargetFilters.Any(f => !f.IsEmpty);
        double? factor = hasOptions ? AncientOptionPhysicalPricing.ResolveSurvival(request).SurvivalProbability : 1;
        double? owned = factor is double a ? ProjectOwnedSurvival(request, a) : null;
        return owned is double p
            ? FamilySurvivalProjection.Resolved("W.World", p, "W.OwnedWorldProjection;AncientOptionFactorRemovedOnce;ModeledNotObserved")
            : FamilySurvivalProjection.Unresolved("W.World", "W.OwnedWorldProbabilityUnknown");
    }
}
