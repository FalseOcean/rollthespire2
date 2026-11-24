using System.Diagnostics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record CrystalLanguageCompilationProfile(long FinishCalls,long FinishTicks,long FinishCacheHits,long SuffixCalls,
    long CanCoverCalls,long CanCoverTicks,long CanCoverRejected,long CanCoverCacheHits,int CanCoverCacheEntries,long StopwatchFrequency)
{
    public long SummaryCalls { get; init; }
    public long SummaryTicks { get; init; }
    public long CanStillMatchCalls { get; init; }
    public long CanStillMatchTicks { get; init; }
    public long CanStillMatchCacheHits { get; init; }
    public int CanStillMatchCacheEntries { get; init; }
    public long NodeCalls { get; init; }
    public long NodeTicks { get; init; }
    public long CoreCalls { get; init; }
    public long CoreCacheHits { get; init; }
}

internal sealed partial class PredictorCrystalOffsetBound
{
    // One compilation, explicitly enabled by a host. No production observer,
    // budgets, decisions or recursive-inclusive timing depend on this object.
    internal sealed class CompilationTimer
    {
        private long _finishCalls,_finishTicks,_canCoverCalls,_canCoverTicks;
        private long _summaryCalls,_summaryTicks,_canStillMatchCalls,_canStillMatchTicks,_nodeCalls,_nodeTicks;
        internal long FinishCacheHits,SuffixCalls,CanCoverRejected,CanCoverCacheHits;
        internal long CoreCalls,CoreCacheHits;
        internal long CanStillMatchCacheHits;
        internal int CanStillMatchCacheEntries;
        internal int CanCoverCacheEntries;
        internal Scope MeasureFinish()=>new(this,Measurement.Finish);
        internal Scope MeasureCanCover()=>new(this,Measurement.CanCover);
        internal Scope MeasureSummary()=>new(this,Measurement.Summary);
        internal Scope MeasureCanStillMatch()=>new(this,Measurement.CanStillMatch);
        internal Scope MeasureNode()=>new(this,Measurement.Node);
        internal CrystalLanguageCompilationProfile Snapshot()=>new(_finishCalls,_finishTicks,FinishCacheHits,SuffixCalls,
            _canCoverCalls,_canCoverTicks,CanCoverRejected,CanCoverCacheHits,CanCoverCacheEntries,Stopwatch.Frequency)
        {
            SummaryCalls=_summaryCalls,SummaryTicks=_summaryTicks,
            CanStillMatchCalls=_canStillMatchCalls,CanStillMatchTicks=_canStillMatchTicks,
            CanStillMatchCacheHits=CanStillMatchCacheHits,CanStillMatchCacheEntries=CanStillMatchCacheEntries,
            NodeCalls=_nodeCalls,NodeTicks=_nodeTicks,CoreCalls=CoreCalls,CoreCacheHits=CoreCacheHits
        };
        internal enum Measurement { Finish,CanCover,Summary,CanStillMatch,Node }
        internal readonly struct Scope : IDisposable
        {
            private readonly CompilationTimer _owner;
            private readonly Measurement _measurement;
            private readonly long _started;
            internal Scope(CompilationTimer owner,Measurement measurement)
            {
                _owner=owner;_measurement=measurement;_started=Stopwatch.GetTimestamp();
                switch(measurement)
                {
                    case Measurement.Finish:owner._finishCalls++;break;
                    case Measurement.CanCover:owner._canCoverCalls++;break;
                    case Measurement.Summary:owner._summaryCalls++;break;
                    case Measurement.CanStillMatch:owner._canStillMatchCalls++;break;
                    case Measurement.Node:owner._nodeCalls++;break;
                }
            }
            public void Dispose()
            {
                long elapsed=Stopwatch.GetTimestamp()-_started;
                switch(_measurement)
                {
                    case Measurement.Finish:_owner._finishTicks+=elapsed;break;
                    case Measurement.CanCover:_owner._canCoverTicks+=elapsed;break;
                    case Measurement.Summary:_owner._summaryTicks+=elapsed;break;
                    case Measurement.CanStillMatch:_owner._canStillMatchTicks+=elapsed;break;
                    case Measurement.Node:_owner._nodeTicks+=elapsed;break;
                }
            }
        }
    }
}
