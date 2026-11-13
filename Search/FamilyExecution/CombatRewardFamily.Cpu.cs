using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class CombatRewardFamily
{
    private static FilterBodyDescription PrepareCpu(PreparedFilter preparation, CombatRewardReplay replay,
        CombatRewardGpuPlan? gpu, CombatRewardPricingInput pricing, IRuntimeProfile profile,
        string identity, int workers, bool potionPrefix)
    {
        var p = replay.Plan; var potions = replay.Catalog.CombatRewardPotionPool;
        bool priced = !replay.UsesChildShuffle && !replay.UsesNeutralWitness && pricing.Authority.PlayersCount == 1 && potions.Common.Length == 16 && potions.Uncommon.Length == 16 &&
            potions.Rare.Length == 16 && !p.OpeningConsumption.HasReplay && p.ExplicitContext.InfluenceFlags == Beta110CombatRewardInfluenceFlags.None &&
            p.MaximumBattleOrdinal == 3 && p.PredicateCount == 3 &&
            p.Predicates.Select(row => (int)row.BattleOrdinal).SequenceEqual(new[] { 1, 2, 3 }) &&
            p.Predicates.All(row => row.PotionRequirement == NormalCombatPotionRequirement.MustDrop &&
                row.PotionAny.Concat(row.PotionAll).All(id => potions.Common.Contains(id)) && !row.HasCardPredicate &&
                row.PotionAny.Length + row.PotionAll.Length == 1 && row.PotionBan.Length == 0 && !row.HasGoldPredicate);
        string revision = potionPrefix ? "C.CombatReward.Cpu.PotionPrefix.20261013.v2" +
            (replay.UsesObservedOpening ? CombatRewardObservedOpening.Revision : "") +
            (replay.UsesNeutralWitness ? CombatRewardNeutralWitness.Revision : "") + replay.RuleRevision : replay.CpuRevision;
        bool numericalRoots = profile.ProfileId == RuntimeProfileId.Beta111;
        var costs = FamilyAuxiliaryEvidence.Read<CpuCostSnapshot?>(Id, "CpuCostSnapshot", CpuCostCalibration.Capture, null);
        FamilyPhysicalQuote? Raw(FamilyPhysicalQuoteRequest geometry) => potionPrefix
            ? (workers == 1 && !replay.UsesChildShuffle ? CombatRewardPhysicalPricing.QuoteCpuPotionPrefix(pricing, replay, geometry) : null) ??
                CombatRewardWorkEstimate.Cpu(replay, workers, geometry, potionPrefix: true)
            : (priced ? FamilyCpuReferenceCost.Quote(pricing.Authority, geometry, Id, "C.ThreeOrderedCommonPotions", workers,
                workers == 1 ? "169.972-170.826" : "36.462-37.286") : null) ??
                (workers == 1 && !replay.UsesChildShuffle ? CombatRewardPhysicalPricing.QuoteSixBattleModel(pricing, replay, gpu, geometry, cpu: true) ??
                    CombatRewardPhysicalPricing.QuoteNeutralModel(pricing, replay, gpu, geometry, cpu: true) : null) ??
                CombatRewardWorkEstimate.Cpu(replay, workers, geometry);
        return new(preparation, revision, profile.ProfileId) {
            InputIdentity = identity, PlayersCount = pricing.Authority.PlayersCount,
            WindowCapacity = 65536, CpuChunkCapacity = CpuFilterExecution.Capacity, CpuWorkers = workers,
            PerformanceProjection = compact => CpuFilterExecution.Describe(Id, revision, workers, 65536, numericalRoots, false, compact),
            Cost = new() {
                PartyChild = new(AllowsHistoricalEnvelope: !replay.UsesChildShuffle && !replay.Plan.NeedsCompleteMatchingReference && !replay.UsesQueryLiteralPartyOpening && !replay.UsesObservedOpening && !replay.UsesNeutralWitness, CompleteCombatMatching: replay.Plan.NeedsCompleteMatchingReference, QueryLiteralCombatOpening: replay.UsesQueryLiteralPartyOpening),
                CanonicalScope = pricing.Authority.PlayersCount == 1 && !replay.UsesChildShuffle,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeCombat(replay, null), CalibrationQuote = Raw,
                Quote = (selected, geometry, _) => FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                    (Raw(geometry) is { } raw ? costs?.Local(raw,
                        selected.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision, workers, geometry.CompactInput) ?? raw : null)
            },
            CreateExecution = (selected, _) => new CpuFilterExecution(selected, profile,
                potionPrefix ? replay.MatchesPotionPrefix : replay.Matches, numericalRoots)
        };
    }
}
