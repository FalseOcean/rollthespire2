using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class TransformationAggregateQueryProbability
{
    internal static JointSelectivityResult Compose(SearchSelectivityInput input,Func<SearchSelectivityInput,JointSelectivityResult> estimateOther)
    {
        var compiled=input.CompiledSearch;
        // Immutable numerical donor inputs only; no session, GPU owner, pricing
        // or execution is created by probability analysis.
        var request=new ExactSearchExecutionRequest(compiled,new("000000000000",1,1,1),"000000000000",1,
            CompiledSearchEvaluationProjector.Project(compiled).Evaluation,input.CombatRewardRoutePolicy,compiled.SemanticFingerprint);
        var numerical = new TransformationAggregateNumericalPlan(request);
        var probability=TransformationAggregateProbability.Build(numerical);
        var query = compiled.NormalizedQuery;
        var owned = query.StructuredOpeningEffects.Where(c => numerical.DrawGroups.Any(g => numerical.OwnsGroup(g.Hash,c.SourceRelicKey))).ToArray();
        if (TransformationAggregateCondition.HasSharedNeow(request.Evaluation) && owned.Length > 0)
        {
            if (TransformationAggregateProbability.WithNeowConditions(numerical,owned) is { } sameDrawJoint)
            {
                probability = probability with { QueryHitProbability=sameDrawJoint, Evidence=probability.Evidence+";AdditionalN=SameDrawJoint" };
                query = query with { StructuredOpeningEffects=query.StructuredOpeningEffects.Except(owned).ToArray() };
                compiled = SearchCompiler.Compile(query,compiled.Context);
                request = request with { Evaluation=CompiledSearchEvaluationProjector.Project(compiled).Evaluation };
            }
        }
        // A fixed Leafy/NewLeaf Bones transaction draws its final curse after the
        // owned transforms. Under the named-draw model its conditional pool is uniform;
        // the Bones identity and grant pair have already been paid by T.
        var finalConditions=query.StructuredOpeningEffects.Where(c => c.SourceRelicKey==Core.Identity.BaseGameModelKeys.Relics.NeowsBones &&
            c.Scope==NeowStructuredEffectScope.FinalCurse && c.Kind==NeowStructuredConditionKind.ExactSingle).ToArray();
        bool hasFinal=finalConditions.Length>0 || query.LegacyNeow.RequiredFinalCurse.HasValue || query.LegacyNeow.BannedFinalCurses.Count>0;
        if (hasFinal && numerical.Condition.IsBones && probability.QueryHitProbability is { } aggregateMass)
        {
            var withoutFinal=query with { StructuredOpeningEffects=query.StructuredOpeningEffects.Except(finalConditions).ToArray(),
                LegacyNeow=query.LegacyNeow with {RequiredFinalCurse=null,BannedFinalCurses=[]} };
            var clean=SearchCompiler.Compile(withoutFinal,compiled.Context);
            if(TransformationAggregateCondition.SharedNeowIdentityOnly(CompiledSearchEvaluationProjector.Project(clean).Evaluation) &&
                input.Authority.EffectAuthority is {CursePoolExact:true,GeneratedCursePool.Count:>0} effects)
            {
                var targets=finalConditions.SelectMany(c=>c.OutputKeys).Concat(query.LegacyNeow.RequiredFinalCurse is {} curse ? new[]{curse}:[]).ToArray();
                double mass=effects.GeneratedCursePool.Count(key=>targets.All(t=>t==key) && !query.LegacyNeow.BannedFinalCurses.Contains(key))/(double)effects.GeneratedCursePool.Count;
                probability=probability with { QueryHitProbability=aggregateMass*mass,Evidence=probability.Evidence+";FinalCurse=ConditionalUniformPoolNamedDrawModel" };
                compiled=clean;
                request=request with {Evaluation=CompiledSearchEvaluationProjector.Project(clean).Evaluation};
            }
        }
        bool shared = TransformationAggregateCondition.HasSharedNeow(request.Evaluation);
        bool identityOnly = TransformationAggregateCondition.SharedNeowIdentityOnly(request.Evaluation);
        var otherQuery = compiled.NormalizedQuery with { TransformationAggregate = null };
        // For the fixed Bones Leafy+NewLeaf + C closure, removing the already-paid
        // identity here does not approximate a different reward distribution: both
        // transforms leave Rewards untouched, Bones contributes a fixed shuffle
        // prefix, and C starts with the same rarity/potion state. Fast/Exact still
        // replay that prefix; this is only the existing distribution factorization.
        if (identityOnly) otherQuery = otherQuery with { OpeningRoute = null, OpeningRouteRelicRequirement = null, LegacyNeow = LegacyNeowSemanticConstraints.Empty };
        var rest=SearchCompiler.Compile(otherQuery,compiled.Context);
        var baseline=estimateOther(SearchSelectivityInput.From(rest));
        if(probability.QueryHitProbability is not { } p)
            return JointSelectivityResult.Partial("Probability.TransformationAggregate.Unknown", probability.Evidence, "No empirical fallback", baseline.KnownComponents, baseline.UnknownComponents.Append("T.TransformationAggregate.AuthorityUnclosed").ToArray());
        var assumptions=new[]{probability.Evidence,"CrossFamilyRelation=AssumedIndependentUnlessStructuralDependencyRegistered;NotFiniteSeedCount"};
        var component=new JointSelectivityComponent("transformation-aggregate", "Transformation aggregate", "Authored raw transformation sources",p,
            "T.LocalCountDistribution.20260914.v1",JointSelectivityCombinationMethod.IndependentProduct,SearchSelectivityDependencyClass.AssumedIndependent,
            true,p>0,probability.Evidence,assumptions);
        var components=baseline.KnownComponents.Append(component).ToArray();
        if (shared && !identityOnly)
            return JointSelectivityResult.Partial("Probability.TransformationAggregate.SharedNeowJointUnknown",
                "Additional N predicates may constrain T's same transformation outcomes", "Shared opening counted once; no independent product", components,
                baseline.UnknownComponents.Append("T+N.AdditionalPredicateJoint").ToArray(), assumptions: assumptions);
        if(baseline.Probability is not { } other)
            return JointSelectivityResult.Partial("Probability.TransformationAggregate.Partial",probability.Evidence, "Other authored predicate unresolved", components,baseline.UnknownComponents,assumptions:assumptions);
        double joint=p*other;
        return JointSelectivityResult.Exact(joint,JointSelectivityCombinationMethod.IndependentProduct,
            "Probability.TransformationAggregate.ModeledJoint",probability.Evidence,
            "N finite identity mass × local replacement distributions; aggregate once × independent other authored domains",
            components:components,assumptions:baseline.Assumptions.Concat(assumptions).ToArray(),dependencyCoverage:"ModeledIndependence")
            with { Confidence=SearchSelectivityConfidence.Medium, ExactlyImpossible=joint==0 };
    }
}
