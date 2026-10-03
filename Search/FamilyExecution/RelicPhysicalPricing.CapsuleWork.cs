using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class RelicPhysicalPricing
{
    internal static FamilyPhysicalQuote? QuoteCapsule(ExactSearchExecutionRequest request,
        RelicFullGpuPlan? plan, FamilyPhysicalQuoteRequest geometry)
    {
        if (QuoteSourceCapsules(request, plan, geometry) is { } measured) return measured;
        if (plan is null || !FamilyPhysicalQuote.AdmittedRequest(geometry) || !FamilyPhysicalQuote.HasReferenceBackend() ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.Authority.AllCharacterCardPoolsUnlocked != true ||
            plan.UsesBonesKBoundary || plan.SequencePlan.AlwaysReject ||
            !plan.SequencePlan.Pool.BucketLengths.SequenceEqual(new[] {30,25,35,25,1,2,32,26,38,26})) return null;

        var sequence = plan.SequencePlan;
        var meta = plan.CapsuleMetadata;
        int targets = (int)meta[3];
        if (targets is < 1 or > 3 || sequence.TrackedInitialPositions.Length > 12 ||
            sequence.Predicates.Length > 5 || sequence.ShopPredicates.Any(p => p.Count > 5)) return null;
        var lengths = sequence.Pool.BucketLengths;
        double shuffle = 0, tracked = 0;
        int last = 0;
        for (int t = 0; t < targets; t++)
        {
            int bucket = (int)meta[4 + t * 3];
            last = Math.Max(last, bucket);
            tracked += lengths[bucket] - 1;
        }
        shuffle += lengths.Take(last + 1).Sum(n => n - 1);
        bool hasSequence = sequence.Predicates.Length + sequence.ShopPredicates.Length > 0;
        if (hasSequence)
        {
            shuffle += lengths.Take(sequence.LastRequiredBucket + 1).Sum(n => n - 1);
            for (int bucket = 0; bucket < lengths.Length; bucket++)
                if (sequence.Pool.BucketScopes[bucket] == 1 && sequence.Pool.BucketKinds[bucket] is >= 1 and <= 3)
                    tracked += sequence.TrackedCountsByLane[sequence.Pool.BucketKinds[bucket] - 1] * (lengths[bucket] - 1);
        }
        bool bones = meta[0] != RelicFullGpuPlan.DirectArrival;
        if (bones) { shuffle += meta[24] - 1; tracked += 2 * (meta[24] - 1); }
        // Same R-owned NextInt/tracked-swap loops as OrdinaryWork. Full-path
        // envelope deliberately does not assume independent sequence/rarity gates.
        // The positive-entry replay envelope covers direct Small and fixed Bones;
        // Large uses its cheaper curse entry. Hash/emission charged only once.
        double entry = bones || meta[1] == Beta110FastRelicCatalog.SmallCapsule ? 1.6 : .28;
        double ns = entry + (geometry.CompactInput ? 1.55 + .00085 * shuffle + .01175 * tracked :
            .98 + .00254 * shuffle + .0101 * tracked);
        string partition = !plan.SourceConstrainedCapsules ? "" : (meta[16] & 16u) != 0 ? ".LargeOnly" : targets == 1 ? ".SmallOnly" : ".BothSources";
        return new("R.Relic", $"R.Rfull.WorkEnvelope.20260913.v1.Arrival{meta[0]}.T{targets}{partition}.Sequence{hasSequence}",
            ns, RelicFamilyGpuExecutor.Capacity, 750,
            "CapsuleFoundation.20260913;Model=ConservativeCoarse;FullOwnedReplayEnvelope;" +
            "ExistingRShuffleTrackedCoefficients;EntryReplay;NoAssumedGateIndependence;" +
            "NumericalEmissionOnly;InvocationAndTransportSeparate;NoQueryIdentity");
    }
}
