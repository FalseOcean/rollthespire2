using System.Diagnostics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record CrystalLanguageCompilationProfile(long FinishCalls,long FinishTicks,long FinishCacheHits,long SuffixCalls,
    long CanCoverCalls,long CanCoverTicks,long CanCoverRejected,long CanCoverCacheHits,int CanCoverCacheEntries,long StopwatchFrequency);

internal sealed partial class PredictorCrystalOffsetBound
{
    // One compilation, explicitly enabled by a host. No production observer,
    // budgets, decisions or recursive-inclusive timing depend on this object.
    internal sealed class CompilationTimer
    {
        private long _finishCalls,_finishTicks,_canCoverCalls,_canCoverTicks;
        internal long FinishCacheHits,SuffixCalls,CanCoverRejected,CanCoverCacheHits;
        internal int CanCoverCacheEntries;
        internal Scope MeasureFinish()=>new(this,true);
        internal Scope MeasureCanCover()=>new(this,false);
        internal CrystalLanguageCompilationProfile Snapshot()=>new(_finishCalls,_finishTicks,FinishCacheHits,SuffixCalls,
            _canCoverCalls,_canCoverTicks,CanCoverRejected,CanCoverCacheHits,CanCoverCacheEntries,Stopwatch.Frequency);
        internal readonly struct Scope : IDisposable
        {
            private readonly CompilationTimer _owner;
            private readonly bool _finish;
            private readonly long _started;
            internal Scope(CompilationTimer owner,bool finish)
            {
                _owner=owner;_finish=finish;_started=Stopwatch.GetTimestamp();
                if(finish) owner._finishCalls++;else owner._canCoverCalls++;
            }
            public void Dispose()
            {
                long elapsed=Stopwatch.GetTimestamp()-_started;
                if(_finish) _owner._finishTicks+=elapsed;else _owner._canCoverTicks+=elapsed;
            }
        }
    }
}
