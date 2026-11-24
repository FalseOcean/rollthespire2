using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalReachability
{
    private readonly Dictionary<string,PredictorCrystalGoldSearch> _goldQueries=[];
    internal Action<Exception,string>? GoldFailureObserved {get;set;}
    private CrystalDiscoveryProgress _goldProgress;
    internal CrystalDiscoveryProgress GoldProgress {get {lock(_sync) return _goldProgress;}}

    // Money guidance can start before candidate/enchantment compilation. The
    // same source, ordered selection, curse policy and reroll scope stay frozen
    // for the session; a changed selection gets its own resumable frontier.
    internal CrystalOptionProjection AdvanceGold(ImmutableArray<CrystalRewardOption> selected,TimeSpan budget,
        CancellationToken token=default)
    {
        lock(_sync)
        {
            token.ThrowIfCancellationRequested();
            string key=SelectionKey(selected);
            if(!_goldQueries.TryGetValue(key,out var query))
                _goldQueries.Add(key,query=new(source,selected,avoidCurse,includeRerolls,GoldFailureObserved));
            if(_goldQueries.TryGetValue(SelectionKey([]),out var unconditional))
                query.UseUnconditionalUpperBound(unconditional);
            if(_table!=null) query.UseRewardTable(_table);
            var projection=query.Advance(budget,PeekKnown(selected),token);
            _goldProgress=query.Progress;
            return PublishKnown(key,projection,selected);
        }
    }
}
