using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// Query-only projection. Each Family owns its own instance; no candidate state or
// mutable cross-Family cache is shared. Numerical probability solvers are donors.
internal sealed class NeowFamilyProjections
{
    private readonly ExactSearchExecutionRequest _request;
    private readonly NeowSearchFilter _n, _r, _identity;
    private readonly string _id;
    private readonly bool _capsule;
    private readonly bool _correlated;
    private readonly bool _ambiguousRouteUnion;
    private readonly double _prefixWork, _tailWork;
    internal FamilyAnalyticalCostProjection Full { get; }

    internal NeowFamilyProjections(ExactSearchExecutionRequest request, NeowReplayPlan plan, bool capsule, RelicFamilyPool? pool, int physicalCapacity = FamilyCpuExecution.Capacity)
    {
        _request = request; _capsule = capsule; _id = capsule ? "R.Relic" : "N.Neow";
        _n = capsule ? NeowReplayPlan.Compile(request, false).Filter : plan.Filter;
        _r = capsule ? plan.Filter : NeowReplayPlan.NeowFilter(request, true);
        _r = _r with { RelicSequenceConditions = request.Evaluation.RelicSequenceConditions, RelicShopSequenceConditions = request.Evaluation.RelicShopSequenceConditions };
        _correlated = NeowReplayPlan.HasCapsule(request);
        _ambiguousRouteUnion = _correlated && plan.Bones && request.Evaluation.RequiredBonesAcquisitionOrder.Count != 2 &&
            (_n.StructuredNeowEffects.Count > 0 || _n.RequiredFinalCurse.HasValue || _n.BannedFinalCurses.Count > 0);
        _plan = plan;
        _pool = pool;
        _identity = Identity(_capsule ? _r : _n);
        double identityBound = plan.Authority.EligibleCurseRelicIds.Max(IdentityWork);
        double routeBound = capsule || plan.StructuredConditions.Length > 0 || plan.HasFinalCurseFastProjection
            ? Routes().Select(route => RouteWork(route.First, route.Second, fullPath: true)).DefaultIfEmpty(0).Max() + FinalCurseWork : 0;
        int routeCount = plan.Bones && plan.First == Beta110FastRelicCatalog.InvalidId ? 2 : 1;
        _prefixWork = capsule && pool is not null ? BagWork(pool) : identityBound;
        _tailWork = (capsule ? identityBound + SequenceProbeWork() : 0) + routeCount * routeBound;
        Full = new FamilyAnalyticalCostProjection(_id, physicalCapacity,
            [new(FamilyAnalyticalOperation.InvocationBoundary, FamilyAnalyticalWorkVariability.Constant, 1, 0, 0, 0, "CpuInvocation"),
             new(FamilyAnalyticalOperation.ObservationWrite, FamilyAnalyticalWorkVariability.Path, 0, _prefixWork + _tailWork, 0, 0, "CpuDonorPrimitiveLedger.FullPathEnvelope"),
             new(FamilyAnalyticalOperation.CompactInputOrdinal, FamilyAnalyticalWorkVariability.Path, 0, 0, 1, 0, "ABI1Input"),
             new(FamilyAnalyticalOperation.OutputCompaction, FamilyAnalyticalWorkVariability.Path, 0, 0, 0, 1, "OrderedCpuAppend")],
            "CpuDonorPrimitiveLedger.v2;FullPathEnvelope;RngDrawsAndObservationsAndPredicateCalls;NotCpuInstructionCount");
    }

    internal FamilySurvivalProjection Survival(IReadOnlySet<string> passed)
    {
        bool conditioned = _correlated && passed.Contains(_capsule ? "N.Neow" : "R.Relic");
        if (_ambiguousRouteUnion && conditioned)
            return FamilySurvivalProjection.Unresolved(_id, "IndependentUnpinnedRouteUnionsRequireIntersectionProbability_NotSameRouteJoint");
        NeowSearchFilter own = _capsule ? _r : _n;
        if (!conditioned) return Projection(Probability(own), "UnconditionalOwnedPredicateAndReplayPrerequisites");
        double? denominator = Probability(_capsule ? _n : _r);
        double? joint = Probability(Union(_n, _r));
        if (!denominator.HasValue || !joint.HasValue)
            return FamilySurvivalProjection.Unresolved(_id, "ConditionalCoverageJointOrDenominatorUnavailable");
        if (denominator == 0) return FamilySurvivalProjection.Resolved(_id, 0, "EmptyPassedCoveragePopulation");
        if (joint > denominator + 1e-12) return FamilySurvivalProjection.Unresolved(_id, "ConditionalJointExceedsParent");
        return Projection(Math.Clamp(joint.Value / denominator.Value, 0, 1), "P(OwnAndPassedCoverage)/P(PassedCoverage)");
    }

    internal FamilyExpectedFilteringCostProjection Expected(IReadOnlySet<string> passed)
    {
        FamilySurvivalProjection survival = Survival(passed);
        bool conditional = _correlated && passed.Contains(_capsule ? "N.Neow" : "R.Relic");
        NeowSearchFilter parent = conditional ? (_capsule ? _n : _r) : NeowSearchFilter.Empty;
        double? denominator = conditional ? Probability(parent) : 1d;
        var segments = new List<FamilyExpectedFilteringCostSegment>();
        bool unresolved = !survival.IsResolved || !denominator.HasValue;
        double Reach(NeowSearchFilter prefix)
        {
            if (denominator == 0) return 0;
            double? numerator = Probability(Merge(prefix, parent));
            if (!numerator.HasValue || !denominator.HasValue || numerator > denominator + 1e-12)
            { unresolved = true; return 0; }
            return Math.Clamp(numerator.Value / denominator.Value, 0, 1);
        }
        void Add(string name, FamilyAnalyticalOperation operation, double work, double reach)
        { if (work > 0) segments.Add(new(name, operation, work, reach)); }
        NeowSearchFilter prefix = NeowSearchFilter.Empty;
        if (_capsule)
        {
            Add("R.InitialBagReplay", FamilyAnalyticalOperation.RngAdvance, BagWork(_pool!), 1);
            foreach (var condition in _r.RelicSequenceConditions.Where(c => !c.IsEmpty))
            {
                Add("R.SequencePredicate", FamilyAnalyticalOperation.PredicateProbe, 1, Reach(prefix));
                prefix = prefix with { RelicSequenceConditions = prefix.RelicSequenceConditions.Append(condition).ToArray() };
            }
            foreach (var condition in _r.RelicShopSequenceConditions.Where(c => !c.IsEmpty))
            {
                Add("R.ShopSequencePredicate", FamilyAnalyticalOperation.PredicateProbe, 1, Reach(prefix));
                prefix = prefix with { RelicShopSequenceConditions = prefix.RelicShopSequenceConditions.Append(condition).ToArray() };
            }
        }
        // Identity observation is completed before the local gate. Its number of
        // shuffle draws depends on the eligible curse, so integrate that finite
        // branch instead of substituting a hard-coded average draw count.
        foreach (byte curse in _plan.Authority.EligibleCurseRelicIds)
        {
            NeowSearchFilter branch = prefix with { NeowRelics = new ModelKeySetFilter([], [Beta110FastRelicCatalog.KeyOf(curse)], []) };
            Add("IdentityReplay." + curse, FamilyAnalyticalOperation.RngAdvance, IdentityWork(curse), Reach(branch));
        }
        if (_capsule || _plan.StructuredConditions.Length > 0 || _plan.HasFinalCurseFastProjection)
        {
            foreach (var route in Routes())
            {
                NeowSearchFilter gate = Merge(prefix, _identity);
                if (_plan.Bones) gate = gate with { RequiredBonesCombination = gate.RequiredBonesCombination
                    .Concat(new[] { Beta110FastRelicCatalog.KeyOf(route.First), Beta110FastRelicCatalog.KeyOf(route.Second) }).Distinct().ToArray() };
                Add("RouteReplay." + route.First + "." + route.Second, FamilyAnalyticalOperation.ObservationWrite,
                    RouteWork(route.First, route.Second), Reach(gate));
                if (!_capsule && _plan.HasFinalCurseFastProjection)
                {
                    NeowSearchFilter localPassed = Merge(gate, _n with {
                        RequiredFinalCurse = null, BannedFinalCurses = [],
                        StructuredNeowEffects = _n.StructuredNeowEffects.Where(c => c.Scope != NeowStructuredEffectScope.FinalCurse).ToArray() });
                    if (_plan.Bones) localPassed = localPassed with { RequiredBonesAcquisitionOrder =
                        new[] { Beta110FastRelicCatalog.KeyOf(route.First), Beta110FastRelicCatalog.KeyOf(route.Second) } };
                    Add("FinalCurse." + route.First + "." + route.Second, FamilyAnalyticalOperation.PredicateProbe, FinalCurseWork, Reach(localPassed));
                }
            }
        }
        Add("CanonicalAbi1Append", FamilyAnalyticalOperation.OutputCompaction, 1, survival.SurvivalProbability ?? 0);
        return new(_id, Full.WorkUnitsPerInput + 1, unresolved ? null : segments.Sum(s => s.ExpectedContribution),
            survival.SurvivalProbability, 1, unresolved ? [] : segments,
            unresolved ? "ConditionalReachOrPrimitiveBranchProbabilityUnavailable" :
            "CpuDonorPrimitiveLedger.v2;FiniteCurseAndBonesBranches;SequenceCheckpointReach;PassedCoverage=" + string.Join(',', passed));
    }

    private FamilySurvivalProjection Projection(double? probability, string evidence) => probability.HasValue
        ? FamilySurvivalProjection.Resolved(_id, probability.Value, evidence)
        : FamilySurvivalProjection.Unresolved(_id, evidence + ":DonorProbabilityUnresolved");
    private double? Probability(NeowSearchFilter filter)
    {
        // Family projections belong to one owner, including multiplayer slots.
        // Keep that context; only the outer Search compiler accepts a whole table.
        CompiledSearch compiled = SearchCompiler.CompilePlayer(LegacySearchQueryAdapter.FromFilter(filter), _request.CompiledSearch.Context);
        lock (_probabilities)
        {
            if (_probabilities.TryGetValue(compiled.SemanticFingerprint, out double? cached)) return cached;
            double? value = ProbabilityCore(filter);
            _probabilities.Add(compiled.SemanticFingerprint, value);
            return value;
        }
    }

    private double? ProbabilityCore(NeowSearchFilter filter)
    {
        // Structured donors price the selected parent, but do not incorporate
        // arbitrary additional top-level Any/All/Ban rows. Replace that parent
        // mass with the complete finite identity conjunction; never multiply the
        // extra rows as independent facts (including the curse-cost branches).
        double identity = IdentityProbability(Identity(filter));
        if (identity == 0) return 0;
        bool output = filter.StructuredNeowEffects.Count > 0 || filter.RequiredFinalCurse.HasValue ||
            filter.BannedFinalCurses.Count > 0 || !filter.CapsuleContainedRelics.IsEmpty || filter.RequireWhetstone || filter.RequireWarPaint;
        NeowSearchFilter donor;
        double parent = 1;
        if (output)
        {
            var required = filter.RequiredBonesCombination.Concat(filter.BonesRelics.All)
                .Concat(filter.RequiredBonesAcquisitionOrder).Distinct().ToArray();
            bool bones = IsBones(filter);
            // An unspecified partner filtered by Any/Ban requires another finite
            // output mixture. Keep it explicit instead of using an unfiltered donor.
            if (bones && required.Concat(filter.RequiredBonesAcquisitionOrder).Distinct().Count() < 2 &&
                (filter.BonesRelics.Any.Count > 0 || filter.BonesRelics.Ban.Count > 0)) return null;
            donor = filter with { NeowRelics = ModelKeySetFilter.Empty, BonesRelics = ModelKeySetFilter.Empty,
                RequireSmallCapsule = false, RequireLargeCapsule = false, RequiredBonesCombination = required };
            parent = IdentityProbability(Identity(donor));
        }
        else donor = NeowSearchFilter.Empty with { RelicSequenceConditions = filter.RelicSequenceConditions,
            RelicShopSequenceConditions = filter.RelicShopSequenceConditions };
        if (parent == 0) return null;
        var compiled = SearchCompiler.CompilePlayer(LegacySearchQueryAdapter.FromFilter(donor), _request.CompiledSearch.Context);
        var estimate = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(compiled));
        if (!estimate.JointlyPriced || !estimate.Probability.HasValue) return null;
        double conditional = estimate.Probability.Value / parent;
        return conditional <= 1 + 1e-12 ? identity * Math.Clamp(conditional, 0, 1) : null;
    }

    private static bool IsBones(NeowSearchFilter filter) => filter.NeowRoute?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones ||
        filter.RequireNeowsBones || !filter.BonesRelics.IsEmpty || filter.RequiredBonesCombination.Count > 0 || filter.RequiredBonesAcquisitionOrder.Count > 0;

    private double IdentityProbability(NeowSearchFilter filter) => IdentityProbability(_plan, filter);
    internal static double IdentityProbability(NeowReplayPlan plan, NeowSearchFilter filter)
    {
        ulong Mask(IEnumerable<ModelKey> keys)
        {
            if (!NeowNumericCompilation.TryBuildMask(keys, out ulong mask)) throw new InvalidOperationException("NeowProbabilityUnmappedIdentity");
            return mask;
        }
        bool bones = IsBones(filter);
        ulong any = Mask(filter.NeowRelics.Any), all = Mask(filter.NeowRelics.All), ban = Mask(filter.NeowRelics.Ban);
        if (filter.NeowRoute is not null) all |= Mask([filter.NeowRoute.RouteRelicKey]);
        if (bones) all |= Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.NeowsBones);
        ulong capsules = Mask(new[] { filter.RequireSmallCapsule ? BaseGameModelKeys.Relics.SmallCapsule : default,
            filter.RequireLargeCapsule ? BaseGameModelKeys.Relics.LargeCapsule : default }.Where(k => k.IsValid));
        if (!bones) all |= capsules;
        bool Fits(ulong observed, ulong a, ulong required, ulong excluded) =>
            (a == 0 || (observed & a) != 0) && (observed & required) == required && (observed & excluded) == 0;
        double boneMass = 1;
        if (bones)
        {
            ulong ba = Mask(filter.BonesRelics.Any), bb = Mask(filter.BonesRelics.Ban);
            ulong br = Mask(filter.BonesRelics.All.Concat(filter.RequiredBonesCombination).Concat(filter.RequiredBonesAcquisitionOrder)) | capsules;
            byte[] pool = plan.Authority.BonesEligibleRelicIds;
            int accepted = 0;
            for (int i = 0; i < pool.Length; i++)
            for (int j = i + 1; j < pool.Length; j++)
                if (Fits(Beta110FastRelicCatalog.Bit(pool[i]) | Beta110FastRelicCatalog.Bit(pool[j]), ba, br, bb)) accepted++;
            boneMass = pool.Length < 2 ? 0 : accepted * 2d / (pool.Length * (pool.Length - 1));
        }
        if (boneMass == 0) return 0;
        double mass = 0;
        foreach (byte curse in plan.Authority.EligibleCurseRelicIds)
        {
            int variants = curse == Beta110FastRelicCatalog.LargeCapsule ? 4 : 8;
            for (int variant = 0; variant < variants; variant++)
            {
                var positive = new List<byte>();
                foreach (byte source in NeowFamilyReplay.Positives)
                    if (NeowLocalOperators.IsPositiveAllowed(source, curse, plan.Authority)) positive.Add(source);
                positive.Add((variant & 1) == 0 ? Beta110FastRelicCatalog.NutritiousOyster : Beta110FastRelicCatalog.StoneHumidifier);
                positive.Add((variant & 2) == 0 ? Beta110FastRelicCatalog.NeowsTalisman : Beta110FastRelicCatalog.Pomander);
                if (variants == 8) positive.Add((variant & 4) == 0 ? Beta110FastRelicCatalog.LavaRock : Beta110FastRelicCatalog.SmallCapsule);
                int accepted = 0;
                for (int i = 0; i < positive.Count; i++)
                for (int j = i + 1; j < positive.Count; j++)
                    if (Fits(Beta110FastRelicCatalog.Bit(curse) | Beta110FastRelicCatalog.Bit(positive[i]) | Beta110FastRelicCatalog.Bit(positive[j]), any, all, ban)) accepted++;
                mass += accepted * 2d / (positive.Count * (positive.Count - 1) * variants * plan.Authority.EligibleCurseRelicIds.Length);
            }
        }
        return Math.Clamp(mass * boneMass, 0, 1);
    }
    private static NeowSearchFilter Union(NeowSearchFilter n, NeowSearchFilter r) => n with
    {
        StructuredNeowEffects = n.StructuredNeowEffects.Concat(r.StructuredNeowEffects).ToArray(),
        CapsuleContainedRelics = r.CapsuleContainedRelics, RequireWhetstone = r.RequireWhetstone, RequireWarPaint = r.RequireWarPaint,
        RelicSequenceConditions = r.RelicSequenceConditions, RelicShopSequenceConditions = r.RelicShopSequenceConditions
    };
    private readonly NeowReplayPlan _plan;
    private readonly RelicFamilyPool? _pool;
    private double FinalCurseWork => _plan.HasFinalCurseFastProjection ? 1 + _plan.RequiredFinalCurseIds.Length + _plan.BannedFinalCurseIds.Length : 0;
    private readonly Dictionary<string, double?> _probabilities = new(StringComparer.Ordinal);

    private static NeowSearchFilter Identity(NeowSearchFilter filter) => filter with
    {
        RequireNeowsBones = filter.RequireNeowsBones || filter.RequiredFinalCurse.HasValue || filter.BannedFinalCurses.Count > 0,
        StructuredNeowEffects = [], RequiredFinalCurse = null, BannedFinalCurses = [],
        CapsuleContainedRelics = ModelKeySetFilter.Empty, RequireWhetstone = false, RequireWarPaint = false,
        RelicSequenceConditions = [], RelicShopSequenceConditions = [],
        BonesRelics = filter.NeowRoute?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones
            ? new ModelKeySetFilter(filter.BonesRelics.Any, filter.BonesRelics.All.Concat(filter.StructuredNeowEffects
                .Select(c => c.SourceRelicKey).Where(k => k != BaseGameModelKeys.Relics.NeowsBones)).Distinct().ToArray(), filter.BonesRelics.Ban)
            : filter.BonesRelics
    };
    private static NeowSearchFilter Merge(NeowSearchFilter left, NeowSearchFilter right) => left with
    {
        NeowRoute = left.NeowRoute ?? right.NeowRoute,
        NeowRelics = new ModelKeySetFilter(left.NeowRelics.Any.Count > 0 ? left.NeowRelics.Any : right.NeowRelics.Any,
            left.NeowRelics.All.Concat(right.NeowRelics.All).Distinct().ToArray(), left.NeowRelics.Ban.Concat(right.NeowRelics.Ban).Distinct().ToArray()),
        BonesRelics = new ModelKeySetFilter(left.BonesRelics.Any.Count > 0 ? left.BonesRelics.Any : right.BonesRelics.Any,
            left.BonesRelics.All.Concat(right.BonesRelics.All).Distinct().ToArray(), left.BonesRelics.Ban.Concat(right.BonesRelics.Ban).Distinct().ToArray()),
        RequireNeowsBones = left.RequireNeowsBones || right.RequireNeowsBones,
        RequireSmallCapsule = left.RequireSmallCapsule || right.RequireSmallCapsule,
        RequireLargeCapsule = left.RequireLargeCapsule || right.RequireLargeCapsule,
        RequiredBonesCombination = left.RequiredBonesCombination.Concat(right.RequiredBonesCombination).Distinct().ToArray(),
        RequiredBonesAcquisitionOrder = left.RequiredBonesAcquisitionOrder.Count > 0 ? left.RequiredBonesAcquisitionOrder : right.RequiredBonesAcquisitionOrder,
        StructuredNeowEffects = left.StructuredNeowEffects.Concat(right.StructuredNeowEffects).Distinct().ToArray(),
        RequiredFinalCurse = left.RequiredFinalCurse ?? right.RequiredFinalCurse,
        BannedFinalCurses = left.BannedFinalCurses.Concat(right.BannedFinalCurses).Distinct().ToArray(),
        CapsuleContainedRelics = left.CapsuleContainedRelics.IsEmpty ? right.CapsuleContainedRelics : left.CapsuleContainedRelics,
        RequireWhetstone = left.RequireWhetstone || right.RequireWhetstone, RequireWarPaint = left.RequireWarPaint || right.RequireWarPaint,
        RelicSequenceConditions = left.RelicSequenceConditions.Concat(right.RelicSequenceConditions).Distinct().ToArray(),
        RelicShopSequenceConditions = left.RelicShopSequenceConditions.Concat(right.RelicShopSequenceConditions).Distinct().ToArray()
    };
    private IEnumerable<(byte First, byte Second)> Routes()
    {
        if (!_plan.Bones) { yield return (_plan.Selected, Beta110FastRelicCatalog.InvalidId); yield break; }
        if (_plan.First != Beta110FastRelicCatalog.InvalidId && _plan.Second != Beta110FastRelicCatalog.InvalidId)
        { yield return (_plan.First, _plan.Second); yield break; }
        byte[] pool = _plan.Authority.BonesEligibleRelicIds;
        ulong required = _plan.BonesAll;
        foreach (var key in _identity.BonesRelics.All.Concat(_identity.RequiredBonesCombination))
            if (Beta110FastRelicCatalog.TryGetId(key, out byte id)) required |= Beta110FastRelicCatalog.Bit(id);
        for (int a = 0; a < pool.Length; a++)
        for (int b = a + 1; b < pool.Length; b++)
        {
            ulong pair = Beta110FastRelicCatalog.Bit(pool[a]) | Beta110FastRelicCatalog.Bit(pool[b]);
            if ((pair & required) != required) continue;
            yield return (pool[a], pool[b]); yield return (pool[b], pool[a]);
        }
    }
    private double IdentityWork(byte curse)
    {
        int binary = curse == Beta110FastRelicCatalog.LargeCapsule ? 2 : 3;
        int positiveCount = binary;
        foreach (byte id in NeowFamilyReplay.Positives)
            if (NeowLocalOperators.IsPositiveAllowed(id, curse, _plan.Authority)) positiveCount++;
        // Root, two RNG initializations (the captured Rewards value is always
        // constructed), 14 source eligibility probes, draws, observations, masks.
        return (_capsule ? 0 : 1) + 2 + 14 + (1 + binary + positiveCount - 1) + 3 + 5 +
            (_plan.Bones ? _plan.Authority.BonesEligibleRelicIds.Length +
                Math.Max(0, _plan.Authority.BonesEligibleRelicIds.Length - 1) + 2 + 3 +
                (_plan.First != Beta110FastRelicCatalog.InvalidId ? 1 : 0) : 0);
    }
    private static double BagWork(RelicFamilyPool pool) => 2 + pool.BucketCount +
        pool.BucketLengths.Sum(length => Math.Max(0, length - 1)) +
        Enumerable.Range(0, pool.BucketCount).Where(i => pool.BucketScopes[i] != 0).Sum(i => pool.BucketLengths[i]);
    private double SequenceProbeWork() => _r.RelicSequenceConditions.Count + _r.RelicShopSequenceConditions.Count;
    private double RouteWork(byte first, byte second, bool fullPath = false)
    {
        bool rewards = _capsule || (_plan.EnabledDomains & ~(Beta110FastDomain.FinalCurse | Beta110FastDomain.NewLeafTransform |
            Beta110FastDomain.LeafyPoulticeTransforms | Beta110FastDomain.PhialHolsterPotions)) != 0;
        bool niche = _capsule || _plan.HasFinalCurseFastProjection || (_plan.EnabledDomains & Beta110FastDomain.NewLeafTransform) != 0 || rewards;
        bool transforms = !_capsule && (_plan.EnabledDomains & Beta110FastDomain.LeafyPoulticeTransforms) != 0;
        bool potions = !_capsule && (_plan.EnabledDomains & Beta110FastDomain.PhialHolsterPotions) != 0;
        double work = 4 + _plan.StructuredConditions.Length; // stream init / source-presence probes
        foreach (byte source in new[] { first, second })
        {
            if (source == Beta110FastRelicCatalog.InvalidId) continue;
            work++; // operator dispatch
            double draws = source switch
            {
                Beta110FastRelicCatalog.Kaleidoscope when niche || rewards => 2 * Math.Max(0, _plan.Authority.EffectCatalog.OtherCharacterPools.Length - 1) + (rewards ? 18 : 0),
                Beta110FastRelicCatalog.LeafyPoultice when transforms => 2,
                Beta110FastRelicCatalog.ArcaneScroll when rewards => 1,
                Beta110FastRelicCatalog.HeftyTablet when rewards => 3,
                Beta110FastRelicCatalog.LeadPaperweight when rewards => 6,
                Beta110FastRelicCatalog.LostCoffer when rewards => 11,
                Beta110FastRelicCatalog.PhialHolster when potions => 4,
                Beta110FastRelicCatalog.NewLeaf when niche => 1,
                Beta110FastRelicCatalog.ScrollBoxes when rewards => _plan.Authority.UsesDefectScrollBoxesRule ? 2 + 6 * (fullPath ? 1 : .99) : 6,
                Beta110FastRelicCatalog.LargeCapsule when rewards => 2,
                Beta110FastRelicCatalog.SmallCapsule when rewards => 1,
                _ => 0
            };
            // Numerical donor operators are observation primitives, rather than
            // a second micro-instruction cost model. Their source RNG draws and
            // per-query predicate calls are counted separately with unit weights.
            work += draws + 1 + _plan.StructuredConditions.Count(c => c.SourceRelicId == source);
        }
        work += _plan.StructuredConditions.Length;
        // Final Curse is reached only after local predicates pass. It is priced
        // separately by Expected; no survival-independent charge is invented.
        return work;
    }
}
