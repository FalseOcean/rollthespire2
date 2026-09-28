using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.FamilyExecution;

// Offline numerical + device emission rates for the existing allocation comparators.
// N_COST_CONSOLIDATION_20260909.md / Corpus/n-cost-v1. Not canonical throughput,
// not ConditionPerformance normalization, and never learned from current survivors.
internal static class NeowPhysicalPricing
{
    // Existing offline constructor quotes, split by the modules actually bound.
    // The 2400ms full donor has the same source as the omitted staged-producer
    // module (DB36DC...E66E); 4200ms included that donor plus Dense stages.
    // This is lifecycle subtraction, not a new numerical/transport fit.
    internal static double StagedSetupMilliseconds(FamilyPhysicalQuoteRequest request) =>
        request.PrivateInput ? 2400 : request.PrivateOutput && !request.CompactInput ? 4200 - 2400 : 4200;

    internal static FamilyPhysicalQuote? Quote(NeowFamily family, FamilyPhysicalQuoteRequest request)
    {
        var p = family.PricingReplayPlan;
        var gpu = family.GpuPlan;
        if (family.PricingComposite is { UsesBonesCheckpoint: true, UsesBonesArcane: false } grouped &&
            p.DirectNestedVanilla111 && p.Authority.PlayersCount == 1 && p.Authority.AllCharacterCardPoolsUnlocked &&
            p.Authority.BonesEligibleRelicIds.Length == 28 && p.Authority.EligibleCurseRelicIds.Length == 10 &&
            FamilyPhysicalQuote.AdmittedRequest(request) && FamilyPhysicalQuote.HasReferenceBackend())
        {
            var m=grouped.Metadata;
            int targets=(int)m[3];
            double tracked=0; int last=0;
            for(int t=0;t<targets;t++){int bucket=(int)m[4+3*t];last=Math.Max(last,bucket);tracked+=m[106+bucket]-1;}
            double shuffle=Enumerable.Range(0,last+1).Sum(b=>(double)m[106+b]-1);
            double pairReach=2d/(10*28*27);
            double nested=3 + .00254*shuffle + .01175*tracked;
            return new(family.FamilyId,$"Nr.GroupedThreeDraw.20260913.v1.T{targets}.B{last}",
                (request.CompactInput?1.6:.14)+pairReach*nested,NeowFamilyGpuExecutor.Capacity,
                StagedSetupMilliseconds(request),
                "Model=ConservativeCoarse;ExistingBonesIdentityReferencePlusModeledPairReachTimesFullTargetWork;NoRarityGateDiscount;NoObservedPopulation",
                PublicTransportClass:"LargeResidentAppend32");
        }
        if (FamilyPhysicalQuote.AdmittedRequest(request) && FamilyPhysicalQuote.HasReferenceBackend() &&
            family.Coverage.Count == 1 && gpu is { UsesStagedDense: true, DirectNestedMode: 0 } &&
            p.DirectNestedVanilla111 && p.Authority.PlayersCount == 1 && p.Authority.AllCharacterCardPoolsUnlocked &&
            p.Authority.Ascension == 10 && p.Authority.BonesEligibleRelicIds.Length == 28 &&
            p.Authority.EligibleCurseRelicIds.Length == 10 && p.Bones && gpu.Meta[67] == 1 &&
            p.StructuredConditions.Length == 0 && (p.EnabledDomains & ~Beta110FastDomain.FinalCurse) == 0 &&
            p.BonesAny == 0 && p.BonesBan == 0)
            return new(family.FamilyId, "N.FixedPair28." + (p.HasFinalCurseFastProjection ? "FinalCurse" : "Identity"),
                request.CompactInput ? 1.0 : .14, NeowFamilyGpuExecutor.Capacity, StagedSetupMilliseconds(request),
                "ClassicProductMatrix.20260910.BonesFixedPair;WholeN;NumericalEmission;NoPublicMaterialization;Setup=BoundModules.20260911;FullDonor2400;AllModules4200",
                PublicTransportClass: "LargeResidentAppend32");
        if (!FamilyPhysicalQuote.AdmittedRequest(request) || !FamilyPhysicalQuote.HasReferenceBackend() ||
            family.Coverage.Count != 1 || !TryResolve(family, out var work, out string shape)) return QuoteBones(family, request) ?? QuoteDirectEnvelope(family, request);
        return new(family.FamilyId, shape,
            request.CompactInput ? work.CompactNs : work.DenseNs, work.Capacity,
            family.GpuPlan!.DirectNestedMode == 0 ? 2400 : 120,
            Revision + ";NumericalAndDeviceEmission;NoPublicMaterialization;OfflineMeasured",
            PublicTransportClass: "LargeResidentAppend32");
    }

    internal const string Revision = "N.WorkShape.20260910.v2";
    // DenseCarry8 and Compact were measured separately. The shared .2 ms/window
    // control allowance is subtracted before rounding these numerical coefficients.
    // v2 refreshes TwoTransform Dense and SelectivePositive Dense/Compact from
    // physical-composition-v1 long-window dispatch evidence; no numerical change.
    private static readonly (string Shape, double DenseNs, double CompactNs)[] Rows = [
        ("N.FirstDrawOrBoundedProjection", .28, .68),
        ("N.PositivePoolIdentity", 1.10, 1.60),
        ("N.TwoTransformPreGate", .20, .62),
        ("N.ScalarPredicateFirst", .37, .60),
        ("N.SelectivePositiveReplay", .62, 1.00),
        ("N.ThreeSlotProjectionReplay", .63, .96),
        ("N.LocalMultiPick", 1.20, 1.46),
        ("N.LocalRequestedSide", .92, 1.20)
    ];

    internal static bool TryResolve(NeowFamily family, out PrivateOrdinalAllocationPricing.Work work, out string shape)
    {
        work = default; shape = "Unknown";
        var gpu = family.GpuPlan;
        var p = family.PricingReplayPlan;
        bool identityOnly = p.EnabledDomains == Beta110FastDomain.None && p.StructuredConditions.Length == 0;
        // Identity masks use the same body without a route-form Selected value.
        // The actual first-draw/positive-pool split below still determines work.
        // No Dense-to-Compact or non-Bones-to-Bones extrapolation. Other supported
        // environments retain their existing pricing/execution, not a Search gate.
        if (gpu is null || gpu.UsesStagedDense || p.Bones || p.RequireBones || p.HasFinalCurseFastProjection ||
            !p.DirectNestedVanilla111 || p.Authority.PlayersCount != 1 || !p.Authority.AllCharacterCardPoolsUnlocked ||
            (!identityOnly && p.Authority.Ascension != 10) || (p.Selected == 255 && !identityOnly)) return false;
        uint allCurseOrdinals = (1u << p.Authority.EligibleCurseRelicIds.Length) - 1u;
        bool firstDrawResolved = ((gpu.Meta[68] | gpu.Meta[69]) & allCurseOrdinals) == allCurseOrdinals;
        int row = gpu.UsesLeafyPreGate ? 2 : gpu.DirectNestedMode switch {
            3 or 40 => 0,
            6 or 7 => 3,
            21 or 31 => 4,
            11 => 5,
            22 => 6,
            42 => 7,
            10 => p.StructuredConditions.All(c => c.Kind == Beta110FastStructuredConditionKind.LostCofferPotion) ? 7 : 6,
            0 when identityOnly => firstDrawResolved ? 0 : 1,
            _ => -1
        };
        if (row < 0) return false; // Unmeasured generic operators/parked controls stay unknown.
        var quote = Rows[row]; shape = quote.Shape;
        work = new(quote.DenseNs, quote.CompactNs, NeowFamilyGpuExecutor.Capacity);
        return true;
    }

    private static FamilyPhysicalQuote? QuoteDirectEnvelope(NeowFamily family, FamilyPhysicalQuoteRequest g)
    {
        var p=family.PricingReplayPlan;var gpu=family.GpuPlan;
        if(!FamilyPhysicalQuote.AdmittedRequest(g)||!FamilyPhysicalQuote.HasReferenceBackend()||family.Coverage.Count!=1||
            gpu is not {UsesStagedDense:false,DirectNestedMode:0}||p.Bones||p.RequireBones||p.HasFinalCurseFastProjection||
            !p.DirectNestedVanilla111||p.Authority.PlayersCount!=1||!p.Authority.AllCharacterCardPoolsUnlocked||p.Authority.Ascension!=10||
            p.Selected==255||p.ExactOnly.Length!=0||p.StructuredConditions.Length is <1 or >2||g.MeanInputPopulation<1048576)
            return null;
        var expected=family.ExpectedFilteringCost;
        if(!expected.IsResolved)return null;
        double local=expected.Segments.Where(s=>s.Name.StartsWith("RouteReplay.",StringComparison.Ordinal)).Sum(s=>s.ExpectedContribution);
        if(local is <0 or >5)return null;
        // Generic local donor still owns Entry replay. Start from the measured
        // positive-pool envelope, then budget conditional local work; do not
        // borrow a selective specialized pre-gate's unusually cheap coefficient.
        return new(family.FamilyId,"N.DirectDonorEnvelope.20260913.v1",(g.CompactInput?1.6:1.1)+.15*local,
            NeowFamilyGpuExecutor.Capacity,2400,"PricingHoleSweep.20260913;Model=ConservativeCoarse;GenericDirectDonor;ConditionalLocalWork<=5;NumericalDeviceEmission;MinInput=1048576",
            PublicTransportClass:"LargeResidentAppend32");
    }

    private static FamilyPhysicalQuote? QuoteBones(NeowFamily family, FamilyPhysicalQuoteRequest g)
    {
        var p = family.PricingReplayPlan; var gpu = family.GpuPlan;
        if (!FamilyPhysicalQuote.AdmittedRequest(g) || !FamilyPhysicalQuote.HasReferenceBackend() ||
            family.Coverage.Count != 1 || gpu is not { UsesStagedDense: true } ||
            gpu.DirectNestedMode is not (0 or 102) || gpu.Meta[67] != 1 ||
            !p.Bones || !p.DirectNestedVanilla111 || p.Authority.PlayersCount != 1 ||
            !p.Authority.AllCharacterCardPoolsUnlocked || p.Authority.Ascension != 10 ||
            p.Authority.BonesEligibleRelicIds.Length != 28 || p.Authority.EligibleCurseRelicIds.Length != 10 ||
            p.ExactOnly.Length != 0 || p.StructuredConditions.Length > 4 || g.MeanInputPopulation < 1048576)
            return null;
        // Fixed-pair identity admits about 1/378 of the Bones entries. Local work
        // stays behind that checkpoint (Leafy can reject even earlier). Use a
        // deliberately coarse upper band, not the scalar CPU primitive ledger.
        // Owner K + Pomander .131-.136ns/root; KP full .132ns/root; Compact KP
        // .945ns/input. Extra authored operators/order remain inside this budget.
        int local = p.StructuredConditions.Length + (p.HasFinalCurseFastProjection ? 1 : 0);
        double ns = g.CompactInput ? 1.2 + .15 * Math.Max(0, local - 1) : .18 + .015 * Math.Max(0, local - 1);
        if (g.CompactInput && gpu.UsesLeafyPreGate) ns = .75 + .02 * Math.Max(0, local - 1);
        return new(family.FamilyId, "N.FixedPairNestedEnvelope.20260913.v1", ns,
            NeowFamilyGpuExecutor.Capacity, StagedSetupMilliseconds(g),
            "PricingHoleSweep.20260913;Model=ConservativeCoarse;FixedPair28;LocalOperators0..4;" +
            "AnyOrPinnedPickup;LeafyOrNormalCheckpoint;MinInput=1048576;NumericalDeviceEmission;HostEdgesExcluded",
            PublicTransportClass: "LargeResidentAppend32");
    }

    internal static FamilyPhysicalQuote? QuoteCpuPreGate(NeowFamily family, NeowCpuPreGate gate, FamilyPhysicalQuoteRequest g)
    {
        var p = family.PricingReplayPlan;
        if (!FamilyPhysicalQuote.AdmittedRequest(g) || g.PrivateInput || g.PrivateOutput || g.CompactInput ||
            g.MeanInputPopulation < 1024 || p.Authority.EligibleCurseRelicIds.Length != 10) return null;
        double? ns;
        string shape;
        if (gate.IdentityOnly) { ns = 33; shape = "CurseOnly"; }
        else if (gate.FixedPair && p.Authority.BonesEligibleRelicIds.Length == 28 &&
            p.StructuredConditions.Length <= 2 && p.ExactOnly.Length == 0 && !p.HasFinalCurseFastProjection)
        { ns = 42; shape = "FixedPairLocal"; }
        else
        {
            var identity = QuoteCpuIdentity(family, g);
            if (identity is not null)
            { ns = 33 + identity.NanosecondsPerInput * gate.GateSurvival; shape = "CurseThenIdentity"; }
            else
            {
            // Old full-replay local quote includes an unconditional identity
            // prefix plus identity-conditioned route work. Only scale the former.
            var full = QuoteCpuLocal(family, g);
            if (full is null) return null;
            ns = 33 + 200 * gate.GateSurvival + Math.Max(0, full.NanosecondsPerInput - 200);
            shape = "CurseLocal";
            }
        }
        return new("N.Neow", "N.IdentityPreGate.20260913.v1.P1." + shape, ns.Value, 65536, null,
            "CpuNumericalClosure.20260913;Model=BoundedCoarse;Workers=1;MinInput=1024;" +
            "CpuCanonicalAbi1;metric=ns/ActualStageInput", OutputElementBytes: 8,
            OutputAlreadyOrdered: true, PublicTransportClass: "CpuOrderedAbi1");
    }

    internal static FamilyPhysicalQuote? QuoteCpuIdentity(NeowFamily family, FamilyPhysicalQuoteRequest geometry)
    {
        var p=family.PricingReplayPlan;
        if(!FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput ||
            geometry.CompactInput || geometry.MeanInputPopulation<4096 || !p.DirectNestedVanilla111 ||
            p.Bones || p.RequireBones || p.StructuredConditions.Length!=0 || p.ExactOnly.Length!=0 ||
            p.EnabledDomains!=Beta110FastDomain.None || p.HasFinalCurseFastProjection ||
            p.Authority.PlayersCount!=1 || !p.Authority.AllCharacterCardPoolsUnlocked || !p.Authority.ScrollBoxesAllowed ||
            p.Authority.EligibleCurseRelicIds.Length!=10 || family.Survival.SurvivalProbability is not (>0 and <=.25)) return null;
        // CPU Observe always builds/shuffles the positive pool, including Curse
        // identity queries. Do not reuse GPU's cheap first-draw coefficient here.
        return new("N.Neow","N.IdentityFullPositiveReplay.20260913.v1.P1",210,65536,null,
            "FamilyCostClosure.20260913;MeasuredBoundedClass;CpuCanonicalAbi1;metric=ns/ActualStageInput;IdentityMasks;Survival<=.25;"+
            "MinInput=4096;NoNestedOrBones;NoAscensionOrCharacterNameKey;MeasuredGpuEightClassesUnchanged",
            OutputElementBytes:8,OutputAlreadyOrdered:true,PublicTransportClass:"CpuOrderedAbi1");
    }

    internal static FamilyPhysicalQuote? QuoteCpuLocal(NeowFamily family, FamilyPhysicalQuoteRequest geometry)
    {
        var p=family.PricingReplayPlan;
        if(!FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput ||
            geometry.CompactInput || geometry.MeanInputPopulation<4096 || !p.DirectNestedVanilla111 ||
            p.Bones || p.RequireBones || p.ExactOnly.Length!=0 || p.HasFinalCurseFastProjection ||
            p.Authority.PlayersCount!=1 || !p.Authority.AllCharacterCardPoolsUnlocked ||
            p.Authority.Ascension!=10 || p.StructuredConditions is not [var condition] ||
            condition.TargetCount>2 || condition.SourceRelicId!=p.Selected ||
            condition.Kind is not (Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets or
                Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard or Beta110FastStructuredConditionKind.LeafyPoulticeTransforms or
                Beta110FastStructuredConditionKind.NewLeafTransform or Beta110FastStructuredConditionKind.LostCofferCardOffer or
                Beta110FastStructuredConditionKind.LostCofferPotion or Beta110FastStructuredConditionKind.PhialHolsterPotions or
                Beta110FastStructuredConditionKind.LeadPaperweightColorlessOffer)) return null;
        var expected=family.ExpectedFilteringCost;
        if(!expected.IsResolved) return null;
        double route=expected.Segments.Where(s=>s.Name.StartsWith("RouteReplay.",StringComparison.Ordinal)).Sum(s=>s.ExpectedContribution);
        if(route is <1 or >4.5) return null;
        // CPU-specific mapping of the existing modeled identity-conditioned route
        // work. ScrollBoxes failed this mapping's operator holdout and is excluded.
        return new("N.Neow","N.DirectLocalWork.20260913.v1.P1",200+18*route,65536,null,
            "FamilyCostClosure.20260913;Model=BoundedCoarse;metric=ns/ActualStageInput;ExpectedRouteWork1..4.5;SingleDirectOperator;"+
            "CpuCanonicalAbi1;MinInput=4096;NoBonesFinalCurseOrScrollBoxes;ObservedSurvivalUsed=false",
            OutputElementBytes:8,OutputAlreadyOrdered:true,PublicTransportClass:"CpuOrderedAbi1");
    }
}
