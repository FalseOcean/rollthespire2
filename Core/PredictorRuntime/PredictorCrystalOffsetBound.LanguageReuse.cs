using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalOffsetBound
{
    // Completed GoalLanguage and root-slot facts. Reward-domain absence joins the existing
    // all-orders Bound facts. Joint geometric absence is kept separate: it must
    // not be presented as absence from the relaxed reward domain in Check().
    private readonly List<ImmutableArray<CrystalRewardOption>> _languageGeometryNegatives=[];
    private readonly HashSet<string> _languageOrderedNegatives=[];
    private bool _languageRewardFactsPublished;
    private static string OrderedLanguageKey(ImmutableArray<CrystalRewardOption> goals)=>
        string.Join('|',goals.Select(g=>g.CacheKey));

    internal CrystalLanguageNegativeKind KnownLanguageNegative(ImmutableArray<CrystalRewardOption> goals)
    {
        lock(_sync)
        {
            if(_negativeSets.Any(prior=>prior.All(goals.Contains))) return CrystalLanguageNegativeKind.RewardDomain;
            if(_languageGeometryNegatives.Any(prior=>prior.All(goals.Contains))) return CrystalLanguageNegativeKind.Geometry;
            return _languageOrderedNegatives.Contains(OrderedLanguageKey(goals))
                ?CrystalLanguageNegativeKind.OrderedQuery:CrystalLanguageNegativeKind.None;
        }
    }

    internal void RememberLanguageNegative(ImmutableArray<CrystalRewardOption> goals,CrystalLanguageNegativeKind kind)
    {
        lock(_sync)
        {
            if(kind==CrystalLanguageNegativeKind.OrderedQuery)
            {
                // A complete query with failed replay endpoints proves only
                // this exact ordered take contract. No permutation/subset reuse.
                if(_languageOrderedNegatives.Count<2048) _languageOrderedNegatives.Add(OrderedLanguageKey(goals));
                return;
            }
            if(kind!=CrystalLanguageNegativeKind.RewardDomain && kind!=CrystalLanguageNegativeKind.Geometry) return;
            var facts=kind==CrystalLanguageNegativeKind.RewardDomain?_negativeSets:_languageGeometryNegatives;
            goals=goals.Distinct().ToImmutableArray();
            // Only complete all-orders absence reaches this branch: either no
            // accepting reward word, or no accepting geometric endpoint before
            // ANY replay. A failed prefix/replay/cutoff never reaches it.
            if(goals.IsEmpty || facts.Any(prior=>prior.All(goals.Contains))) return;
            facts.RemoveAll(prior=>goals.All(prior.Contains));
            if(facts.Count<2048) facts.Add(goals);
            if(kind==CrystalLanguageNegativeKind.RewardDomain) Volatile.Write(ref _languageRewardFactsPublished,true);
        }
    }
}
