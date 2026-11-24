using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

// Condition the complete query on the same Act choices before combining W/A.
internal static class WorldVariantQueryProbability
{
    internal static bool Needed(SearchSelectivityInput input)
    {
        var q=input.CompiledSearch.NormalizedQuery;
        if(q.VariantBossBranches.Count>0) return true;
        return q.AncientBranches.Count>0 && (q.EventSequenceConstraints.Count>0 || q.LegacyWorld.BossFilters.Count>0 || q.LegacyWorld.BossOrdinalFilters.Count>0) &&
            input.Authority.WorldAuthority?.Beta109Generation?.ActSelectionGroups.Any(g=>g.EligibleActsInSourceOrder.Count>1)==true;
    }
    internal static JointSelectivityResult Compose(SearchSelectivityInput input, Func<SearchSelectivityInput,JointSelectivityResult> estimate)
    {
        var q=input.CompiledSearch.NormalizedQuery;
        var world=input.Authority.WorldAuthority;
        var generation=world?.Beta109Generation;
        JointSelectivityResult Unknown(string code) => JointSelectivityResult.Unpriced("Probability.WorldVariant."+code,
            "Variant-conditioned query could not be priced.","No missing variant mass substituted.");
        if(generation is null || !generation.ActSelectionAuthorityExact) return Unknown("AuthorityMissing");
        if(q.VariantBossBranches.GroupBy(b=>(b.Act,b.VariantKey)).Any(g=>g.Count()>1)) return Unknown("RepeatedVariantAlternatives");
        var groups=generation.ActSelectionGroups.OrderBy(g=>g.Act).ToArray();
        if(groups.Any(g=>!g.EligibilityAndOrderExact || g.EligibleActsInSourceOrder.Count==0)) return Unknown("PriorMissing");
        if(q.VariantBossBranches.Any(b=>!groups.Any(g=>g.Act==b.Act))) return Unknown("ActMissing");
        var priors=groups.Select(g=>WorldProbabilityEstimator.ResolveVariantPriors(generation,g)).ToArray();
        if(priors.Any(p=>p.Count==0 || Math.Abs(p.Sum(x=>x.Prior)-1)>1e-10)) return Unknown("PriorNotNormalized");
        if(priors.Aggregate(1L,(n,p)=>Math.Min(1025,n*p.Count))>1024) return Unknown("MixtureBudget");
        var selected=new ModelKey[groups.Length]; var traces=new List<JointSelectivityBranchTrace>();
        var assumptions=new HashSet<string>(); var unknown=new List<string>(); double total=0;
        void Visit(int index,double weight)
        {
            if(index<groups.Length) { foreach(var (key,p) in priors[index]) {selected[index]=key;Visit(index+1,weight*p);} return; }
            var rows=new List<ActOrdinalModelKeySetFilter>(q.LegacyWorld.BossOrdinalFilters);
            foreach(var act in q.VariantBossBranches.GroupBy(b=>b.Act))
            {
                int slot=Array.FindIndex(groups,g=>g.Act==act.Key);
                var branch=act.SingleOrDefault(b=>b.VariantKey==selected[slot]);
                if(branch is null) return;
                if(!branch.FirstBoss.IsEmpty) rows.Add(new(act.Key,1,branch.FirstBoss));
                if(branch.IncludesSecondBoss && !branch.SecondBoss.IsEmpty) rows.Add(new(act.Key,2,branch.SecondBoss));
            }
            var conditioned=generation with { SelectedActs=selected.ToArray(),SelectedActsExact=true,
                ActSelectionGroups=groups.Select((g,i)=>g with {EligibleActsInSourceOrder=[selected[i]],SelectionMode=Beta109ActSelectionMode.DeterministicFirst}).ToArray() };
            var authority=input.Authority.WithWorldAuthority(world! with {Beta109Generation=conditioned});
            var child=q with {VariantBossBranches=[],LegacyWorld=q.LegacyWorld with {BossOrdinalFilters=rows}};
            var result=estimate(SearchSelectivityInput.From(SearchCompiler.Compile(child,input.CompiledSearch.Context with {Authority=authority})));
            string label=string.Join(",",selected.Select(k=>k.Entry));
            if(result.Probability is {} probability) total+=weight*probability; else unknown.Add(label+":"+result.EvidenceCode);
            assumptions.UnionWith(result.Assumptions);
            traces.Add(new(label,"Act selection",weight,true,result.Probability,result.ExactlyImpossible,result.EvidenceCode,result.KnownComponents,result.Assumptions));
        }
        Visit(0,1);
        assumptions.Add("ActVariantPriorPaidOnceAcrossWholeQuery;WAndAncientShareSameVariant");
        assumptions.Add("VariantBranchesAreAlternatives;BossAndAncientOptionsRemainParentScoped");
        // Preserve variant-invariant factors for family attribution. A marginal over
        // dependent variant branches is not an independently multiplicative factor.
        var components = traces.Count == 0 ? Array.Empty<JointSelectivityComponent>() : traces[0].Components
            .Where(c => traces.All(t => t.Components.Any(x => x.Id == c.Id && x.Probability == c.Probability))).ToArray();
        if(unknown.Count>0) return JointSelectivityResult.Partial("Probability.WorldVariant.Partial","A conditional query is unpriced.",
            "Sum of variant priors times complete conditional queries.",components,unknown,branches:traces,assumptions:assumptions.ToArray());
        return JointSelectivityResult.Exact(total,JointSelectivityCombinationMethod.FiniteMixture,
            "Probability.WorldVariant.QueryMixture","Finite Act mixture; conditional query assumptions retained.",
            "Sum P(Act variants) × P(full query | same Act variants)",components:components,branches:traces,assumptions:assumptions.ToArray())
            with {Confidence=SearchSelectivityConfidence.Medium};
    }
}
