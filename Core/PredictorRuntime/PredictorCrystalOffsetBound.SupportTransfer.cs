using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalOffsetBound
{
    // Opt-in experiment only. Each entry contains DIFFERENT card options whose
    // complete support over every frozen card kind is contained in this option.
    // The map belongs to this Bound/table; no seed/global/continuation key.
    private Dictionary<CrystalRewardOption,ImmutableArray<CrystalRewardOption>>? _supportSubsets;
    private long _supportMapLookups,_supportTransferCalls,_supportTransferProbes;
    internal long SupportMapLookups=>Interlocked.Read(ref _supportMapLookups);
    internal long SupportTransferCalls=>Interlocked.Read(ref _supportTransferCalls);
    internal long SupportTransferProbes=>Interlocked.Read(ref _supportTransferProbes);
    internal int SupportTransferEntries {get {lock(_sync) return _supportSubsets?.Values.Sum(v=>v.Length)??0;}}
    internal bool CardSupportEmpty(CrystalRewardOption option)=>_source.Items.Where(i=>i.X>=0 &&
        i.Kind.StartsWith("CARD_",StringComparison.Ordinal)).All(i=>_table.CardOffsets(i.Kind,option).IsZero);

    internal ImmutableArray<CrystalRewardOption> ContainedCardSupports(CrystalRewardOption carrier)
    {
        lock(_sync)
        {
            // Scalar offset inclusion loses the Niche/creation-stage coordinate.
            // It cannot transfer a negative proved for one exact enchantment.
            if(carrier.Kind!=PredictorRewardKind.Card || carrier.Enchantment!=null) return [];
            _supportMapLookups++;
            if(_supportSubsets==null)
            {
                var kinds=_source.Items.Where(i=>i.X>=0 && i.Kind.StartsWith("CARD_",StringComparison.Ordinal))
                    .Select(i=>i.Kind).Distinct(StringComparer.Ordinal).ToArray();
                var cards=_table.Candidates.Where(c=>c.Kind==PredictorRewardKind.Card).ToArray();
                var vectors=cards.ToDictionary(c=>c,c=>kinds.Select(k=>_table.CardOffsets(k,c)).ToArray());
                _supportSubsets=[];
                foreach(var c in cards)
                {
                    var upper=vectors[c];
                    _supportSubsets.Add(c,cards.Where(d=>d!=c && vectors[d].Any(bits=>!bits.IsZero) &&
                        vectors[d].Where((bits,i)=>!(bits&~upper[i]).IsZero).Any()==false)
                        .ToImmutableArray());
                }
            }
            return _supportSubsets.GetValueOrDefault(carrier,[]);
        }
    }

    // Replacing D by C preserves the same injective one-pick slot assignment:
    // for every slot kind and initial/reroll offset, D support implies C support.
    // Thus every relaxed physical word accepted for S+D is accepted for S+C.
    // Only complete RewardDomain/Geometry absence can transfer. Ordered take
    // failure and all incomplete work remain outside this implication.
    internal ImmutableArray<CrystalRewardOption> TransferSupportDenials(ImmutableArray<CrystalRewardOption> selected,
        CrystalRewardOption carrier)
    {
        lock(_sync)
        {
            _supportTransferCalls++;
            if(carrier.Kind!=PredictorRewardKind.Card || selected.Contains(carrier)) return [];
            var kind=KnownLanguageNegative(selected.Add(carrier));
            if(kind!=CrystalLanguageNegativeKind.RewardDomain && kind!=CrystalLanguageNegativeKind.Geometry) return [];
            var additions=ImmutableArray.CreateBuilder<CrystalRewardOption>();
            foreach(var d in ContainedCardSupports(carrier))
            {
                _supportTransferProbes++;
                if(!selected.Contains(d) && KnownLanguageNegative(selected.Add(d))==CrystalLanguageNegativeKind.None) additions.Add(d);
            }
            var derived=additions.ToImmutable();
            foreach(var d in derived) RememberLanguageNegative(selected.Add(d),kind);
            return derived;
        }
    }
}
