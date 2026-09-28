using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class RelicPhysicalPricing
{
    internal static FamilyPhysicalQuote? QuoteShopModel(ExactSearchExecutionRequest request,
        RelicFamilyPlan plan, FamilyPhysicalQuoteRequest geometry, bool cpu)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.Authority.AllCharacterCardPoolsUnlocked != true ||
            plan.AlwaysReject || plan.Predicates.Length != 0 || plan.LastRequiredBucket != 9 ||
            plan.ShopPredicates is not [{Count: >= 1 and <= 5} p] ||
            !plan.Pool.BucketLengths.SequenceEqual(new[]{30,25,35,25,1,2,32,26,38,26})) return null;
        if(cpu ? geometry.PrivateInput || geometry.PrivateOutput || geometry.CompactInput || geometry.MeanInputPopulation<4096 :
            !FamilyPhysicalQuote.HasReferenceBackend() || geometry.MeanInputPopulation<262144) return null;
        int offset=plan.Pool.BucketOffsets[9], length=plan.Pool.BucketLengths[9];
        // Validated full-eligible prefix. A changed eligibility pattern is a
        // different amount of shuffle work, not an equivalent character identity.
        if (Enumerable.Range(offset,length).Any(i=>(plan.Pool.EntryFlags[i]&1)==0)) return null;
        var targets=p.TargetIds.Where(id=>id!=ushort.MaxValue).ToArray();
        if(targets.Length==0 || targets.Distinct().Count()!=targets.Length) return null;
        if(cpu)
        {
            // CPU checks Ordered targets while drawing. Unordered cannot reject
            // until the prefix is available. Full eligibility makes prefix work
            // explicit; the small post-first-check tail is covered by this coarse band.
            int prefix=p.OrderMode==0 ? Array.FindIndex(p.TargetIds,id=>id!=ushort.MaxValue)+1 : p.Count;
            return new("R.Relic","R.ShopPrefixWork.20260913.v1.P1",245+15*(prefix-1),65536,null,
                "FamilyCostClosure.20260913;Model=BoundedCoarse;metric=ns/ActualStageInput;Work=FirstRequiredOrderedPrefixOrUnorderedDepth;"+
                "MeasuredSpecializationFirst;Workers=1;Count1..5;MinInput=4096;Boundary=CpuCanonicalAbi1;NoAdditionalOutputEdge",
                OutputElementBytes:8,OutputAlreadyOrdered:true,PublicTransportClass:"CpuOrderedAbi1");
        }
        double unorderedProbes=p.OrderMode==0 ? 0 : p.Count*targets.Length;
        double ns=geometry.CompactInput ? 2.03+.05*p.Count+.024*unorderedProbes :
            1.65+.049*p.Count+.019*unorderedProbes;
        return new("R.Relic","R.ShopPrefixWork.20260913.v1."+(geometry.CompactInput?"Compact":"Dense"),
            ns,RelicFamilyGpuExecutor.Capacity,390,
            "FamilyCostClosure.20260913;Model=BoundedCoarse;metric=ns/ActualStageInput;Work=EligiblePrefix+UnorderedLookup;"+
            "MeasuredSpecializationFirst;Count1..5;DistinctTargets;FullEligibleLane;MinInput=262144;"+
            "NumericalDeviceEmission;SubmitAndHostTransportExcluded;ObservedSurvivalUsed=false");
    }
}
