using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class RelicPhysicalPricing
{
    internal static FamilyPhysicalQuote? QuoteMixedModel(ExactSearchExecutionRequest request, RelicFamilyPlan plan,
        FamilyExpectedFilteringCostProjection expected, FamilyPhysicalQuoteRequest g)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(g) || !FamilyPhysicalQuote.HasReferenceBackend() ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.Authority.AllCharacterCardPoolsUnlocked != true ||
            plan.AlwaysReject || !expected.IsResolved || plan.Predicates.Length is < 1 or > 5 ||
            plan.Predicates.Any(p=>p.Lane>2) || plan.TrackedInitialPositions.Length is < 1 or > 12 ||
            plan.ShopPredicates is not [{ Count: >= 1 and <= 5 } shop] || plan.LastRequiredBucket != 9 ||
            !plan.Pool.BucketLengths.SequenceEqual(new[]{30,25,35,25,1,2,32,26,38,26})) return null;
        if (Enumerable.Range(plan.Pool.BucketOffsets[9], plan.Pool.BucketLengths[9]).Any(i=>(plan.Pool.EntryFlags[i]&1)==0))
            return null;
        double Sum(FamilyAnalyticalOperation op) => expected.Segments.Where(s=>s.Operation==op).Sum(s=>s.ExpectedContribution);
        double shopReach = expected.Segments.Single(s=>s.Name=="R.Shop.PartialPermutation.LocalState").ReachProbability;
        double prefix = expected.Segments.Single(s=>s.Name=="R.Shop.PartialPermutation.NextInt").ExpectedContribution;
        double rng = g.CompactInput ? .00085 : .00254;
        double root = g.CompactInput ? 1.55 : .98;
        double tracked = g.CompactInput ? .01175 : .0101;
        double shopBase = g.CompactInput ? 2.03 : 1.65;
        double prefixCost = g.CompactInput ? .05 : .049;
        double lookup = g.CompactInput ? .024 : .019;
        // Ordinary and Shop run one shared upfront traversal. Recover only the
        // Shop-local initialization part of the existing Shop reference; never
        // add a second root/hash/shared shuffle envelope.
        double sharedDraws = plan.Pool.BucketLengths.Take(9).Sum(n=>n-1);
        double shopLocalBase = shopBase - root - rng * sharedDraws;
        double probes = shop.OrderMode == 0 ? 0 : shop.Count * shop.TargetIds.Count(id=>id!=ushort.MaxValue);
        double ns = root + rng * Sum(FamilyAnalyticalOperation.HistoricalNextInt) +
            tracked * Sum(FamilyAnalyticalOperation.TrackedPositionUpdateAttempt) +
            shopLocalBase * shopReach + (prefixCost-rng) * prefix + lookup * probes * shopReach;
        string work = string.Join('_', plan.Predicates.Select(p => $"{p.Lane}-{p.RangeMode}-{p.RangeValue}-{p.AnyCount}-{p.AllCount}-{p.BanCount}"));
        return new("R.Relic", $"R.MixedTrackedShop.20260913.v1.T{plan.TrackedInitialPositions.Length}.{work}.Shop{shop.Count}-{shop.OrderMode}-{shop.TargetIds.Count(id=>id!=ushort.MaxValue)}", ns, RelicFamilyGpuExecutor.Capacity, 390,
            "Model=ConservativeCoarse;ExistingOrdinaryAndShopCoefficients;OneSharedTraversal;" +
            "ModeledLedgerReach;NoDoubleHashOrSharedShuffle;OrderedUnorderedShop1..5;Tracked1..12;" +
            "NumericalEmissionOnly;InvocationAndEdgesSeparate;NoObservedPopulation");
    }
}
