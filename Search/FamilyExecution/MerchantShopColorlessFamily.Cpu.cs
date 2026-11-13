using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class MerchantShopColorlessFamily
{
    private static FilterBodyDescription PrepareCpu(PreparedFilter preparation, MerchantShopColorlessPreparation inputs,
        MerchantShopColorlessCompactionDecision decision, IRuntimeProfile profile, string identity, int workers)
    {
        var plan = inputs.CpuPlan;
        bool sequence = inputs.Evaluation.MerchantColorlessSequenceConditions.Count != 0;
        bool slotWorkers = !sequence && inputs.Authority.UncommonPool.Count == 32 && plan is not null &&
            inputs.Evaluation.MerchantColorlessConditions.Count == 3 &&
            inputs.Evaluation.MerchantColorlessConditions.All(c => c.Slot == MerchantColorlessSlot.Uncommon) &&
            inputs.Evaluation.MerchantColorlessConditions.Select(c => c.MerchantOrdinal).Order().SequenceEqual(new[] { 1, 2, 3 });
        bool priced = slotWorkers && !inputs.Authority.UsesModern113 && inputs.PricingAuthority.AdmitsCpuReference(true);
        string revision = plan is null ? "S.MerchantShopColorless.Cpu.Reference.20260912.v1" :
            sequence ? "S.MerchantShopColorless.Cpu.Sequence.20261013.v2" : "S.MerchantShopColorless.Cpu.Slot.20261013.v2";
        revision += inputs.Authority.RuleRevision;
        bool numericalRoots = profile.ProfileId == RuntimeProfileId.Beta111;
        var costs = FamilyAuxiliaryEvidence.Read<CpuCostSnapshot?>(Id, "CpuCostSnapshot", CpuCostCalibration.Capture, null);
        FamilyPhysicalQuote? Raw(FamilyPhysicalQuoteRequest geometry) =>
            (priced && workers is 1 or 16 ? FamilyCpuReferenceCost.QuoteAdmitted(geometry, Id, "S.ThreeUncommonSlots", workers,
                workers == 1 ? "23.197-23.656" : "4.976-5.592") : null) ??
            MerchantShopColorlessWorkEstimate.Quote(inputs, decision, preparation.ExpectedFilteringCost!, geometry,
                cpu: true, workers: workers, compiledMatcher: plan is not null);
        return new(preparation, revision, profile.ProfileId) {
            InputIdentity = identity, PlayersCount = inputs.Authority.PlayersCount,
            WindowCapacity = 65536, CpuChunkCapacity = CpuFilterExecution.Capacity, CpuWorkers = workers,
            PerformanceProjection = compact => CpuFilterExecution.Describe(Id, revision, workers, 65536, numericalRoots, false, compact),
            Cost = new() {
                CanonicalScope = inputs.Authority.PlayersCount == 1 && !inputs.Authority.UsesModern113,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeShop(inputs.Evaluation, inputs.Authority),
                CalibrationQuote = Raw,
                Quote = (selected, geometry, _) => FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                    (Raw(geometry) is { } raw ? costs?.Local(raw,
                        selected.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision, workers, geometry.CompactInput) ?? raw : null)
            },
            CreateExecution = (selected, _) => new CpuFilterExecution(selected, profile,
                plan is null ? inputs.Reference : plan.Matches, numericalRoots)
        };
    }
}
