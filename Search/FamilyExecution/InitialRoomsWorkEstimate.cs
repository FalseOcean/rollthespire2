using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Additional initial-room work only. The wrapper separately composes its owned
// ordinary calculation exactly once; this is not a cross-family chain quote.
internal static class InitialRoomsWorkEstimate
{
    internal static FamilyPhysicalQuote? Local(string familyId, InitialRoomsPlan plan,
        FamilyPhysicalQuoteRequest geometry, bool gpu, int workers)
    {
        var shape = plan.DescribePricingShape();
        double targets = shape.Targets.Count;
        double draws = shape.TreasureCovered ? shape.TreasureStart + shape.TreasureCount - 1 : 0;
        double updates = shape.SharedShuffleDraws * targets;
        double ns = gpu ? .12 + .04 * shape.UnknownPrefixCount : 30 + 12 * shape.UnknownPrefixCount;
        if (shape.TreasureCovered)
            ns += gpu ? .85 + .00254 * shape.SharedShuffleDraws + .0101 * updates + .025 * draws :
                120 + .93 * shape.SharedShuffleDraws + 1.31 * updates + 12 * draws;
        string basis = FormattableString.Invariant($"I.TrackedSharedFront.20261020;Coefficients=ExistingSRootAndRShuffleTrackedReferences;UnknownPrefix={shape.UnknownPrefixCount};TreasureDraws={draws:G9};SharedShuffle={shape.SharedShuffleDraws};TrackedUpdates={updates:G9};Targets={targets:G9};TreasureOrder={shape.TreasureOrder};RuntimeSharedBagCounts;UncommittedSupportedPlanOnly;NumericalVariant={plan.NumericalVariant};NoNewMeasurement");
        return gpu ? FamilyCoarsePricing.Gpu(familyId, "InitialRooms.Local", ns, InitialRoomsPlan.Capacity, geometry, basis) :
            FamilyCoarsePricing.Cpu(familyId, "InitialRooms.Local", ns, workers, geometry, basis);
    }
}
