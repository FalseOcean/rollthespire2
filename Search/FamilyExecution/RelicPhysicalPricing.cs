using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class RelicPhysicalPricing
{
    internal static FamilyPhysicalQuote? QuoteSourceCapsules(ExactSearchExecutionRequest request, RelicFullGpuPlan? plan,
        FamilyPhysicalQuoteRequest geometry)
    {
        // Old reference constrained both sources. A blank side can admit more heavy work;
        // use the owned full-work envelope rather than aliasing that rarity-band measurement.
        if (plan is not { SourceConstrainedCapsules: true } || !FamilyPhysicalQuote.AdmittedRequest(geometry) ||
            !FamilyPhysicalQuote.HasReferenceBackend() || request.Authority.PlayersCount != 1 ||
            !request.Authority.CanUseCurrentModel || plan.SequencePlan.Predicates.Length != 0 || plan.SequencePlan.ShopPredicates.Length != 0 ||
            plan.CapsuleMetadata[24] != 28 || (plan.CapsuleMetadata[16] & 16u) != 0 || plan.CapsuleMetadata[3] < 2 ||
            !plan.SequencePlan.Pool.BucketLengths.SequenceEqual(new[] { 30, 25, 35, 25, 1, 2, 32, 26, 38, 26 })) return null;
        return new("R.Relic", "R.SourceConstrainedSmallLarge.ThreeDraw.Targets" + plan.CapsuleMetadata[3],
            geometry.CompactInput ? 1.65 : 1.4, RelicFamilyGpuExecutor.Capacity, 750,
            "ClassicProductMatrix.20260910.SourceCapsules;MeasuredRarityBand;ArrivalRarityTrackedRank;NoPublicMaterialization");
    }
    internal static FamilyPhysicalQuote? Quote(ExactSearchExecutionRequest request, RelicFamilyPlan plan,
        FamilyPhysicalQuoteRequest geometry, FamilyExpectedFilteringCostProjection expected) =>
        QuoteMeasured(request, plan, geometry) ?? QuoteOrdinaryModel(request, plan, expected, geometry, cpu: false) ??
        QuoteShopModel(request, plan, geometry, cpu: false) ?? QuoteMixedModel(request, plan, expected, geometry);

    private static FamilyPhysicalQuote? QuoteMeasured(ExactSearchExecutionRequest request, RelicFamilyPlan plan,
        FamilyPhysicalQuoteRequest geometry)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || !FamilyPhysicalQuote.HasReferenceBackend() ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || request.Authority.PlayersCount != 1 ||
            request.Authority.CanUseCurrentModel != true || plan.AlwaysReject ||
            !plan.Pool.BucketLengths.SequenceEqual(new[] { 30, 25, 35, 25, 1, 2, 32, 26, 38, 26 })) return null;
        if (plan.Predicates.Length == 0 && plan.LastRequiredBucket == 9 &&
            plan.ShopPredicates is [{ Count: 1, TargetIds.Length: 1 }])
            return new("R.Relic", "R.ShopSingle.FullLaneReplay", geometry.CompactInput ? 2.0 : 1.65,
                RelicFamilyGpuExecutor.Capacity, 390, "FamilyMatrix.20260910.RShopSingle;DistinctFromTrackedPositionBody");
        if (plan.ShopPredicates.Length != 0 || plan.Predicates is not [{ RangeMode: 0, BanCount: 0 } p]) return null;
        if (p.Lane == 3 && p.RangeValue == 1 && p.AnyCount + p.AllCount == 1 &&
            plan.TrackedInitialPositions.Length == 0 && plan.LastRequiredBucket == 9)
            return new("R.Relic", "R.LegacyShopSingle.FullLaneReplay", geometry.CompactInput ? 2.0 : 1.7,
                RelicFamilyGpuExecutor.Capacity, 550, "ClassicProductMatrix.20260910.LegacyShop;FullLaneNotTrackedPosition");
        if (p.Lane is 1 or 2 && p.RangeValue == 1 && p.AnyCount + p.AllCount == 1 &&
            plan.TrackedInitialPositions.Length == 1 && plan.LastRequiredBucket == (p.Lane == 1 ? 6 : 8))
            return new("R.Relic", p.Lane == 1 ? "R.UncommonTrackedSingle" : "R.RareTrackedSingle",
                geometry.CompactInput ? p.Lane == 1 ? 1.85 : 2.0 : 1.75, RelicFamilyGpuExecutor.Capacity,
                390, "FamilyMatrix.20260910.RUncommon-RRare;NumericalDeviceEmission;NoPublicSort");
        if (p.Lane != 0 || plan.LastRequiredBucket != 7) return null;
        int tracked = plan.TrackedInitialPositions.Length;
        if (tracked is < 1 or > 3 || p.RangeValue != tracked ||
            (tracked == 1 ? p.AnyCount + p.AllCount != 1 : p.AllCount != tracked || p.AnyCount != 0)) return null;
        // Up-front tracked-position body, not a sparse-output assumption. Other
        // lanes, Shop, exclusions, and Capsule arrivals retain separate evidence.
        double ns = geometry.CompactInput ? tracked switch { 1 => 1.95, 2 => 2.15, _ => 2.50 }
            : tracked switch { 1 => 1.55, 2 => 1.80, _ => 1.95 };
        return new("R.Relic", $"R.CommonTrackedPrefix.T{tracked}", ns, RelicFamilyGpuExecutor.Capacity,
            390, "FamilyMatrix.20260910.RCommon1-3;TrackedPositions;NumericalDeviceEmission;NoPublicSort");
    }
}
