using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// T owns its aggregate distribution. The identity term is N's finite offer/pair
// solver; local replacement draws use the existing named-stream independence
// Product model. This is not an exact count of the finite root-hash space.
internal sealed record TransformationAggregateProbability(double? QueryHitProbability,
    double? StageSurvival, double IdentityProbability, double ExpectedGpuDraws,
    IReadOnlyList<double[]> LocalRareDistributions, string Evidence)
{
    // Reuse the query model's SAME-DRAW intersection, including authored transform
    // targets and the final curse. Do not multiply two overlapping marginals.
    // Strip unrelated families so this is an N/T intersection, not full-query mass.
    internal static double? SharedNeowJoint(RolltheSpire2.Search.Contracts.ExactSearchExecutionRequest request,
        TransformationAggregateNumericalPlan numerical)
    {
        if (!TransformationAggregateCondition.HasSharedNeow(request.Evaluation) || !numerical.ProbabilityClosed ||
            numerical.Neow is null) return null;
        var source = request.CompiledSearch.NormalizedQuery;
        var query = LegacySearchQueryAdapter.FromFilter(NeowReplayPlan.NeowFilter(request, false)) with {
            OpeningRoute = source.OpeningRoute,
            OpeningRouteRelicRequirement = source.OpeningRouteRelicRequirement,
            TransformationAggregate = source.TransformationAggregate
        };
        var compiled = SearchCompiler.CompilePlayer(query, request.CompiledSearch.Context);
        var joint = RolltheSpire2.Search.Selectivity.JointSelectivityEstimator.EstimateQuery(
            RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(compiled));
        return joint.JointlyPriced ? joint.Probability : null;
    }

    internal static double? Conditional(double? joint, double? parent) =>
        joint is >= 0 and <= 1 && parent is >= 0 and <= 1 && joint <= parent + 1e-12
            ? parent == 0 ? 0 : Math.Clamp(joint.Value / parent.Value, 0, 1) : null;

    internal static TransformationAggregateProbability Build(TransformationAggregateNumericalPlan plan)
    {
        const string policy = "T.LocalWithReplacementDistributions;NFiniteOfferAndUnorderedPair;NamedStreamsModeledIndependent;NoEmpiricalFit;BonesRoutesSameClosedOutputs_NotDoubleMass";
        double identity = plan.Neow is { } n ? NeowFamilyProjections.IdentityProbability(n, n.Filter) : 1;
        var groups = plan.DrawGroups;
        if (!plan.ProbabilityClosed)
            return new(null, null, identity, plan.Condition.OpportunityCount, [], "T.ObservationAuthorityUnclosed");
        var local = groups.Select(g => RareDistribution(g.Pools.Select(pool => pool.Count(i => plan.IsRare(i)) / (double)pool.Length))).ToArray();
        var c = plan.Condition;
        double[] states = new double[1 << c.OpportunityCount]; states[0] = 1;
        double expectedDraws = 0; int consumed = 0;
        foreach (var group in groups)
        foreach (var pool in group.Pools)
        {
            int remaining = c.OpportunityCount - consumed;
            expectedDraws += states.Select((p,s) => Score(s) + remaining >= Goal() ? p : 0).Sum();
            var next = new double[states.Length];
            if (c.Predicate == TransformationAggregatePredicate.RareCountAtLeast)
            {
                double rare = pool.Count(i => plan.IsRare(i)) / (double)pool.Length;
                for (int s = 0; s <= consumed; s++) { next[s] += states[s] * (1-rare); next[s+1] += states[s] * rare; }
            }
            else
            {
                var masses = pool.GroupBy(id => (Bits: plan.TargetBits(id), CanRemain: !c.RequiresRareRemainder || plan.IsRare(id)))
                    .Select(g => (g.Key.Bits, g.Key.CanRemain, Mass:g.Count()/(double)pool.Length)).ToArray();
                for (int s = 0; s < states.Length; s++) if (states[s] != 0)
                    foreach (var m in masses)
                    {
                        uint available = m.Bits & ~(uint)s;
                        if (available == 0 && !m.CanRemain) continue;
                        int target = available == 0 ? s : s | (1 << System.Numerics.BitOperations.TrailingZeroCount(available));
                        next[target] += states[s] * m.Mass;
                    }
            }
            states = next; consumed++;
        }
        double conditional = states.Select((p,s) => Score(s) >= Goal() ? p : 0).Sum();
        double probability = Math.Clamp(identity * plan.EventGateProbability * conditional, 0, 1);
        return new(probability, plan.Closed ? probability : null, identity, expectedDraws, local, policy +
            (c.RequiresRareRemainder ? ";TargetMultisetAndRemainingRare=SameDrawJoint" : "") +
            (c.TrialNondescript ? ";TrialNondescriptCaseMass=1/3;CaseDrawPrecedesTransforms" : "") +
            ";UnspecifiedCompanionInfluenceIgnored=OwnerApproved;RuntimeContinuationProofUnchanged" +
            (plan.Closed ? "" : ";RuntimeSurvivalUnmodeled=ConservativeUnknownOutputs"));
        int Score(int state) => c.Predicate == TransformationAggregatePredicate.RareCountAtLeast ? state : System.Numerics.BitOperations.PopCount((uint)state);
        int Goal() => c.Predicate == TransformationAggregatePredicate.RareCountAtLeast ? c.MinimumRareCount : c.TargetMultiset.Count;
    }
    // Product probability for extra N predicates over the SAME owned transform draws.
    // GPU T survival stays at the aggregate-only mass; no additional physical filtering is claimed.
    internal static double? WithNeowConditions(TransformationAggregateNumericalPlan plan,
        IReadOnlyList<RolltheSpire2.Search.Contracts.NeowStructuredEffectSearchCondition> conditions)
    {
        if (!plan.ProbabilityClosed) return null;
        var c = plan.Condition;
        var states = new Dictionary<int,double> { [0] = 1 };
        foreach (var group in plan.DrawGroups)
        {
            var relevant = conditions.Where(x => plan.OwnsGroup(group.Hash,x.SourceRelicKey)).ToArray();
            if (relevant.Any(x => x.Scope != RolltheSpire2.Search.Contracts.NeowStructuredEffectScope.TransformResults ||
                x.OutputKind != RolltheSpire2.Search.Contracts.NeowStructuredOutputKind.Card ||
                x.Kind is not (RolltheSpire2.Search.Contracts.NeowStructuredConditionKind.ExactSingle or RolltheSpire2.Search.Contracts.NeowStructuredConditionKind.ExactUnorderedPair))) return null;
            var required = relevant.Select(x => x.OutputKeys.Select(plan.LocalId).ToArray()).ToArray();
            if (required.Any(keys => keys.Length > group.Pools.Length)) return 0;
            bool Accept(int[] draws) => required.All(keys => keys.Length switch {
                1 => draws.Contains(keys[0]),
                2 => draws.Length == 2 && (draws[0]==keys[0] && draws[1]==keys[1] || draws[0]==keys[1] && draws[1]==keys[0]),
                _ => false });
            var next = new Dictionary<int,double>();
            var draws = new int[group.Pools.Length];
            void Visit(int index, double mass)
            {
                if(index < draws.Length)
                {
                    foreach(var bucket in group.Pools[index].GroupBy(id=>id))
                    { draws[index]=bucket.Key; Visit(index+1,mass*bucket.Count()/group.Pools[index].Length); }
                    return;
                }
                if(!Accept(draws)) return;
                foreach(var (state, weight) in states)
                {
                    int target=state;
                    foreach(int id in draws)
                    {
                        if(c.Predicate==TransformationAggregatePredicate.RareCountAtLeast) target += plan.IsRare(id)?1:0;
                        else { uint available=plan.TargetBits(id)&~(uint)target;
                            if(available==0 && c.RequiresRareRemainder && !plan.IsRare(id)) { target=-1; break; }
                            if(available!=0) target |= 1<<System.Numerics.BitOperations.TrailingZeroCount(available); }
                    }
                    if(target<0) continue;
                    next[target]=next.GetValueOrDefault(target)+weight*mass;
                }
            }
            Visit(0,1); states=next;
        }
        double local=states.Where(x=>c.Predicate==TransformationAggregatePredicate.RareCountAtLeast ? x.Key>=c.MinimumRareCount :
            System.Numerics.BitOperations.PopCount((uint)x.Key)>=c.TargetMultiset.Count).Sum(x=>x.Value);
        return local * plan.EventGateProbability * (plan.Neow is { } n ? NeowFamilyProjections.IdentityProbability(n,n.Filter) : 1);
    }

    private static double[] RareDistribution(IEnumerable<double> draws)
    {
        double[] distribution = [1];
        foreach (double p in draws)
        {
            var next = new double[distribution.Length+1];
            for(int i=0;i<distribution.Length;i++) { next[i]+=distribution[i]*(1-p); next[i+1]+=distribution[i]*p; }
            distribution=next;
        }
        return distribution;
    }
}
