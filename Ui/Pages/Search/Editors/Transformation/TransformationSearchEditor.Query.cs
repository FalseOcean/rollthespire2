using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class TransformationSearchEditor
{
    internal TransformationAggregateCondition? ExportCondition(SearchQuery q)
    {
        if (_draft.TakenOver.Count == 0)
        {
            return q.TransformationAggregate;
        }
        string[] missing = _draft.TakenOver.Where(id => !_sources.Any(source => source.Id == id)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("TransformationDeclaredSourcesUnavailable:" + string.Join(",", missing));
        var opening = _draft.TakenOver.Contains("N.LeafyPoultice") ? TransformationOpening.LeafyPoultice :
            _draft.TakenOver.Contains("N.NewLeaf") ? TransformationOpening.NewLeaf :
            _draft.TakenOver.Contains("N.BonesLeafyNewLeaf") ? TransformationOpening.BonesLeafyNewLeaf :
            _draft.TakenOver.Contains("N.BonesLeafyOther") ? TransformationOpening.BonesLeafyOther : TransformationOpening.None;
        var order = opening is not (TransformationOpening.BonesLeafyNewLeaf or TransformationOpening.BonesLeafyOther) || q.OpeningRouteRelicRequirement?.OrderMode != BonesRouteOrderMode.ExactOrder
            ? TransformationPickupOrder.Any : q.OpeningRouteRelicRequirement.RequiredRelicKeys[0] == BaseGameModelKeys.Relics.LeafyPoultice
                ? (opening==TransformationOpening.BonesLeafyOther ? TransformationPickupOrder.LeafyThenCompanion : TransformationPickupOrder.LeafyThenNewLeaf) :
                (opening==TransformationOpening.BonesLeafyOther ? TransformationPickupOrder.CompanionThenLeafy : TransformationPickupOrder.NewLeafThenLeafy);
        var result = new TransformationAggregateCondition(opening, order, _draft.TakenOver.Contains("E.MORPHIC_GROVE"),
            _draft.TakenOver.Contains("E.AROMA_OF_CHAOS"), _draft.TakenOver.Contains("E.WHISPERING_HOLLOW"),
            _draft.Objective switch { 0 => TransformationAggregatePredicate.RareCountAtLeast,
                2 => TransformationAggregatePredicate.ContainsMultisetAndRemainingRare, _ => TransformationAggregatePredicate.ContainsMultiset },
            _draft.Objective == 0 ? _draft.RareCount : 0, _draft.Objective == 0 ? [] : _draft.Cards.ToArray()) { CompanionRelic=opening==TransformationOpening.BonesLeafyOther ? q.OpeningRouteRelicRequirement?.RequiredRelicKeys.FirstOrDefault(k=>k!=BaseGameModelKeys.Relics.LeafyPoultice) : null, Symbiote = _draft.TakenOver.Contains("E.SYMBIOTE"), TrialNondescript = _draft.TakenOver.Contains("E.TRIAL") };
        // ResultCount is display capacity, never a user-created RNG opportunity.
        if ((_draft.Objective == 0 ? result.MinimumRareCount : result.TargetMultiset.Count) is < 1 ||
            (_draft.Objective == 0 ? result.MinimumRareCount : result.TargetMultiset.Count) > result.OpportunityCount)
            throw new InvalidOperationException("integration.transform.capacity");
        return result;
    }
    internal void ImportQuery(SearchQuery q)
    {
        _draft.TakenOver.Clear(); _draft.Cards.Clear(); _draft.KnownSources.Clear();
        _draft.Objective = 0; _draft.RareCount = 1;
        if(q.TransformationAggregate is not { } t) return;
        // This existing bounded predicate is also authored by N's joint-three-slot editor.
        // Restore that presentation without duplicating the predicate in T's local state.
        if(t.Opening==TransformationOpening.BonesLeafyNewLeaf && !t.UsesEvents && t.Predicate==TransformationAggregatePredicate.ContainsMultiset) return;
        if(t.UsesNeow) _draft.TakenOver.Add(t.Opening switch { TransformationOpening.LeafyPoultice=>"N.LeafyPoultice",TransformationOpening.NewLeaf=>"N.NewLeaf",TransformationOpening.BonesLeafyOther=>"N.BonesLeafyOther",_=>"N.BonesLeafyNewLeaf" });
        if(t.MorphicGrove) _draft.TakenOver.Add("E.MORPHIC_GROVE");
        if(t.AromaOfChaos) _draft.TakenOver.Add("E.AROMA_OF_CHAOS");
        if(t.WhisperingHollow) _draft.TakenOver.Add("E.WHISPERING_HOLLOW");
        if(t.Symbiote) _draft.TakenOver.Add("E.SYMBIOTE");
        if(t.TrialNondescript) _draft.TakenOver.Add("E.TRIAL");
        _draft.Objective=t.Predicate switch { TransformationAggregatePredicate.ContainsMultiset=>1,
            TransformationAggregatePredicate.ContainsMultisetAndRemainingRare=>2, _=>0 };
        _draft.Cards.AddRange(t.TargetMultiset); _draft.RareCount=t.MinimumRareCount;
        _draft.ResultCount=t.OpportunityCount;
    }
}
