using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class CombatRewardPhysicalPricing
{
    // Retained Y4 NC comparative control, not a point quote for automatic serial.
    // GenericStreaming's historical bound is not promoted to calibrated coverage.
    internal static PrivateOrdinalAllocationPricing.Work LegacyNcWork(CombatRewardGpuPlan plan) =>
        plan.UsesHotLoop ? new(.8, 1.2, CombatRewardGpuExecutor.Capacity) : new(8, 8, CombatRewardGpuExecutor.Capacity);
    internal static FamilyPhysicalQuote? Quote(ExactSearchExecutionRequest request, CombatRewardReplay replay,
        CombatRewardGpuPlan? gpu, FamilyPhysicalQuoteRequest geometry) =>
        QuoteMeasured(request,replay,gpu,geometry) ?? QuoteNeutralModel(request,replay,gpu,geometry,cpu:false) ??
        QuoteGenericCardModel(request,replay,gpu,geometry);

    private static FamilyPhysicalQuote? QuoteGenericCardModel(ExactSearchExecutionRequest request, CombatRewardReplay replay,
        CombatRewardGpuPlan? gpu, FamilyPhysicalQuoteRequest g)
    {
        var p = replay.Plan; var pool = replay.Catalog.CharacterRewardPool;
        int? draws = p.OpeningConsumption.HasReplay
            ? CombatRewardProbabilityEstimator.FixedAuthoredPrefixDrawCount(request,p) : 0;
        if (gpu is not { UsesHotLoop: false, Routes: >= 1 and <= 2 } ||
            !FamilyPhysicalQuote.AdmittedRequest(g) || !FamilyPhysicalQuote.HasReferenceBackend() ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.Ascension != 10 || draws is not (0 or 1 or 3) ||
            !CombatRewardProbabilityEstimator.HasModeledRewardImpact(p.ExplicitContext) || p.MaximumBattleOrdinal is < 1 or > 3 ||
            p.PredicateCount is < 1 or > 3 || pool.TotalCount == 0 ||
            (!request.Authority.UsesBestEffortModel && (pool.Common.Length != 20 || pool.Uncommon.Length != 35 || pool.Rare.Length != 25)) ||
            p.Predicates.Any(row=>row.CardAny.Length+row.CardAll.Length+row.CardBan.Length is < 1 or > 4 ||
                row.HasPotionDropPredicate || row.HasPotionIdentityPredicate || row.HasGoldPredicate)) return null;
        // Reuse measured GenericStreaming (two-route) horizon anchors, retaining
        // one root reconstruction and scaling only repeated route generation.
        // Predicate forms share the same generated rewards; bounded comparisons
        // are a small part of this envelope. No HotLoop number is used.
        double anchor = p.MaximumBattleOrdinal switch { 1=>4.5, 2=>7.2, _=>11.8 };
        const double sharedRoot = 1.6;
        // Conservative additional-generation envelope, not a new measured rate.
        // Extra groups retain their own dedup set; Candy generates one extra
        // power card on battle two. WBS reserves a full potion selection on
        // every battle without charging another root or authored prefix.
        bool candy = (p.ExplicitContext.InfluenceFlags & Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard) != 0;
        bool wbs = (p.ExplicitContext.InfluenceFlags & Beta110CombatRewardInfluenceFlags.ForcePotionReward) != 0;
        double generationScale = 1 + p.ExplicitContext.AdditionalCardRewardCount +
            (candy && p.MaximumBattleOrdinal >= 2 ? 1d / (3 * p.MaximumBattleOrdinal) : 0) +
            (wbs ? 2d * replay.Catalog.PotionPool.TotalCount / (3 * pool.TotalCount) : 0);
        if (request.Authority.UsesBestEffortModel) generationScale *= Math.Max(1, pool.TotalCount / 80d);
        double ns = sharedRoot + (anchor-sharedRoot) * gpu.Routes / 2d * generationScale + .4 * draws.Value;
        string impactShape = p.ExplicitContext.ChangesCurrentFastObservables
            ? $".ImpactV1.Extra{p.ExplicitContext.AdditionalCardRewardCount}.Candy{candy}.Wbs{wbs}" : "";
        if (request.Authority.UsesBestEffortModel) impactShape += $".RuntimePool{pool.TotalCount}.BestEffort";
        return new("C.CombatReward", $"C.GenericCardWork.20260913.v1.R{gpu.Routes}.H{p.MaximumBattleOrdinal}.Prefix{draws}{impactShape}",
            ns, CombatRewardGpuExecutor.Capacity, 1000,
            "Model=ConservativeCoarse;GenericStreamingMeasuredHorizonAnchors;OneRootPlusRouteWork;" +
            "CardAnyAllBan1..4;OrderedOrIndependentExistential;PrefixDraws0/1/FixedDualCapsule3;NoHotLoopAlias;" +
            $"NumericalEmissionOnly;InvocationAndEdgesSeparate;ModeledGenerationScale={generationScale:R};ExtraGroupsCandyWbsFullWorkEnvelope");
    }

    private static FamilyPhysicalQuote? QuoteMeasured(ExactSearchExecutionRequest request, CombatRewardReplay replay,
        CombatRewardGpuPlan? gpu, FamilyPhysicalQuoteRequest geometry)
    {
        var authored = replay.Plan;
        var pool = replay.Catalog.CharacterRewardPool;
        int? prefixDraws = CombatRewardProbabilityEstimator.FixedAuthoredPrefixDrawCount(request, authored);
        if (gpu is { UsesHotLoop: false, Routes: 2 } &&
            FamilyPhysicalQuote.AdmittedRequest(geometry) && FamilyPhysicalQuote.HasReferenceBackend() &&
            request.Ascension == 10 && (prefixDraws == 0 || prefixDraws == 1 && authored.MaximumBattleOrdinal == 1) &&
            !authored.ExplicitContext.ChangesCurrentFastObservables &&
            pool.Common.Length == 20 && pool.Uncommon.Length == 35 && pool.Rare.Length == 25 &&
            authored.MaximumBattleOrdinal is >= 1 and <= 3 && authored.PredicateCount is >= 1 and <= 2 &&
            authored.Predicates.All(row => row.CardAny.Length == 1 && row.CardAll.Length == 0 && row.CardBan.Length == 0 &&
                !row.HasPotionDropPredicate && !row.HasPotionIdentityPredicate && !row.HasGoldPredicate))
            return new("C.CombatReward", $"C.GenericStreaming.FixedPrefixDraws{prefixDraws}.Routes2.Card.H{authored.MaximumBattleOrdinal}",
                prefixDraws == 1 ? geometry.CompactInput ? 4.9 : 4.6 : authored.MaximumBattleOrdinal switch { 1 => 4.5, 2 => 7.2, _ => 11.8 },
                CombatRewardGpuExecutor.Capacity, 1000,
                "ClassicProductMatrix.20260910.AuthoredCard;CompiledRoutesHorizonPool;NoPublicMaterialization");
        if (gpu is not { UsesHotLoop: true, Routes: 1 } || !FamilyPhysicalQuote.AdmittedRequest(geometry) ||
            !FamilyPhysicalQuote.HasReferenceBackend() || request.ProfileId != Compatibility.RuntimeProfileId.Beta111 ||
            request.Authority.CanUseCurrentModel != true || request.Authority.PlayersCount != 1 || request.Ascension != 10) return null;
        var p = replay.Plan;
        if (p.OpeningConsumption.HasReplay || p.ExplicitContext.InfluenceFlags != Beta110CombatRewardInfluenceFlags.None ||
            p.MaximumBattleOrdinal is < 1 or > 3 || p.PredicateCount != p.MaximumBattleOrdinal ||
            p.Predicates.Where((row, i) => row.BattleOrdinal != i + 1).Any()) return null;
        bool cards = p.Predicates.All(row => row.CardAny.Length + row.CardAll.Length == 1 && row.CardBan.Length == 0 &&
            !row.HasPotionDropPredicate && !row.HasPotionIdentityPredicate && !row.HasGoldPredicate);
        bool potions = p.MaximumBattleOrdinal == 3 && p.Predicates.All(row => !row.HasCardPredicate &&
            row.PotionAny.Length + row.PotionAll.Length == 1 && row.PotionBan.Length == 0 && !row.HasGoldPredicate);
        if (!cards && !potions) return null;
        double ns = potions ? .88 : p.MaximumBattleOrdinal == 1 ? .80 : .92;
        return new("C.CombatReward", $"C.NeutralHotLoop.{(cards ? "OrderedSingleCard" : "OrderedSinglePotion")}.H{p.MaximumBattleOrdinal}",
            ns, CombatRewardGpuExecutor.Capacity, 120,
            "FamilyMatrix.20260910.C1-C3;HotLoopNumericalDeviceEmission;GenericAuthoredUnpriced");
    }

    internal static FamilyPhysicalQuote? QuoteCpuPotionPrefix(ExactSearchExecutionRequest request, CombatRewardReplay replay, FamilyPhysicalQuoteRequest g)
    {
        var p = replay.Plan;
        var pool = replay.Catalog.CombatRewardPotionPool;
        if (!replay.CanUsePotionPrefix || !FamilyPhysicalQuote.AdmittedRequest(g) || g.PrivateInput || g.PrivateOutput ||
            g.CompactInput || g.MeanInputPopulation < 1024 || request.Ascension != 10 ||
            p.OpeningConsumption.HasReplay || p.ExplicitContext.ChangesCurrentFastObservables ||
            p.ExplicitContext.InfluenceFlags != Beta110CombatRewardInfluenceFlags.None ||
            pool.Common.Length != 16 || pool.Uncommon.Length != 16 || pool.Rare.Length != 16 ||
            p.Predicates.Length is < 1 or > 3 || p.Predicates.Any(row => row.HasGoldPredicate)) return null;
        var rows = p.Predicates.OrderBy(row => row.BattleOrdinal).ToArray();
        int first = rows[0].BattleOrdinal, horizon = p.MaximumBattleOrdinal;
        if (first is < 1 or > 3 || horizon is < 1 or > 3) return null;
        double? ns = null; string shape = "";
        if (rows.All(row => row.PotionRequirement == NormalCombatPotionRequirement.MustDrop &&
            row.PotionAny.Length + row.PotionAll.Length == 1 && row.PotionBan.Length == 0))
        { ns = 60 + 330 * (first - 1); shape = "Specific.First" + first; }
        else if (rows.Length == horizon && rows.Select(row => (int)row.BattleOrdinal).SequenceEqual(Enumerable.Range(1,horizon)) &&
            rows.All(row => !row.HasPotionIdentityPredicate && row.PotionRequirement == rows[0].PotionRequirement))
        {
            // Empirical horizon buckets retain the different pity-prefix work;
            // no new probability estimator or observed same-run reach is used.
            ns = rows[0].PotionRequirement switch {
                NormalCombatPotionRequirement.MustDrop => new double[]{60,190,230}[horizon-1],
                NormalCombatPotionRequirement.MustNotDrop => new double[]{60,260,360}[horizon-1],
                _ => null
            };
            shape = rows[0].PotionRequirement + ".H" + horizon;
        }
        return ns is null ? null : new("C.CombatReward", "C.PotionPrefix.20260913.v1.P1." + shape, ns.Value,
            65536, null, "CpuNumericalClosure.20260913;Model=BoundedCoarse;NeutralPotionPrefix;Workers=1;" +
            "MinInput=1024;CpuCanonicalAbi1;metric=ns/ActualStageInput", OutputElementBytes:8,
            OutputAlreadyOrdered:true,PublicTransportClass:"CpuOrderedAbi1");
    }

    internal static FamilyPhysicalQuote? QuoteNeutralModel(ExactSearchExecutionRequest request, CombatRewardReplay replay,
        CombatRewardGpuPlan? gpu, FamilyPhysicalQuoteRequest geometry, bool cpu)
    {
        var p=replay.Plan;var pool=replay.Catalog.CharacterRewardPool;
        if(!FamilyPhysicalQuote.AdmittedRequest(geometry) || request.ProfileId!=Compatibility.RuntimeProfileId.Beta111 ||
            !request.Authority.CanUseCurrentModel || request.Authority.PlayersCount!=1 || request.Ascension!=10 ||
            replay.RouteCount!=1 || p.OpeningConsumption.HasReplay || p.ExplicitContext.ChangesCurrentFastObservables ||
            p.ExplicitContext.InfluenceFlags!=Beta110CombatRewardInfluenceFlags.None ||
            p.MaximumBattleOrdinal is <1 or >3 || p.PredicateCount is <1 or >3 ||
            pool.Common.Length!=20 || pool.Uncommon.Length!=35 || pool.Rare.Length!=25 ||
            p.Predicates.Any(row=>row.CardAny.Length+row.CardAll.Length+row.CardBan.Length is <1 or >4 ||
                row.HasPotionDropPredicate || row.HasPotionIdentityPredicate || row.HasGoldPredicate)) return null;
        if(cpu ? geometry.PrivateInput || geometry.PrivateOutput || geometry.CompactInput || geometry.MeanInputPopulation<4096 :
            !FamilyPhysicalQuote.HasReferenceBackend() || gpu is not {UsesHotLoop:true,Routes:1} || geometry.MeanInputPopulation<1048576) return null;
        // CPU generates the whole reward horizon before matching. GPU has a large
        // root/body floor and bounded early-return variation, not H times H1 cost.
        double ns=cpu ? 60+505*p.MaximumBattleOrdinal : .8+.1*(p.MaximumBattleOrdinal-1);
        int extraTargets = p.Predicates.Sum(row => Math.Max(0, row.CardAny.Length + row.CardAll.Length + row.CardBan.Length - 1));
        ns += extraTargets * (cpu ? 8 : .04); // Same reward draw, extra comparisons only.
        return new("C.CombatReward","C.NeutralCardHorizon.20260913.v1.H"+p.MaximumBattleOrdinal+(cpu?".P1":".Gpu"),
            ns,cpu?65536:CombatRewardGpuExecutor.Capacity,cpu?null:120,
            "FamilyCostClosure.20260913;Model=BoundedCoarse;metric=ns/ActualStageInput;NeutralCardHorizon1..3;TargetsPerRow1..4;"+
            "MeasuredSpecializationFirst;FixedRewardPoolGeometry;NoOpeningOrInfluence;"+
            (extraTargets > 0 ? "ConservativeCoarse;ExtraTargetComparisons;" : "") +
            (cpu?"CpuCanonicalAbi1;MinInput=4096":"NumericalDeviceEmission;MinInput=1048576;HostEdgesExcluded"),
            OutputElementBytes:cpu?8:4,OutputAlreadyOrdered:cpu,PublicTransportClass:cpu?"CpuOrderedAbi1":"Counted32");
    }
}
