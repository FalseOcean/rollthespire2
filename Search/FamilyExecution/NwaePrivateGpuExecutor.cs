using System.Diagnostics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// Bounded N/A/E/W serial allocation, each body used at most once. Two ordinal
// allocations; no RNG/root/route state crosses any of these private stages.
internal sealed class NwaePrivateGpuExecutor : IDisposable
{
    private readonly GpuCostSamples _logTotals = new();

    private readonly string _order;
    private readonly PrivateOrdinalBuffer[] _ordinals = [];
    private readonly NeowFamilyGpuExecutor? _n, _referenceN;
    private readonly AncientOptionFamilyGpuExecutor? _a, _referenceA;
    private readonly EventResultFamilyGpuExecutor? _e, _referenceE;
    private readonly WorldFamilyGpuExecutor? _w, _referenceW;
    private readonly bool _verify;
    private bool _disposed;
    internal string Device { get; }
    internal double SetupMs { get; }
    internal NwaePrivateGpuExecutor(RenderingDevice rd, NeowFamilyGpuPlan? n, AncientOptionFamilyPlan? a,
        EventResultFamilyPlan? e, WorldFamilyGpuPlan w, string order, bool verify)
    {
        _order = order; _verify = verify;
        var timer = Stopwatch.StartNew();
        try
        {
            if (order.Length < 2 || order.Length > 4 || !order.Contains('W') || order.Any(c => !"NAEW".Contains(c)) || order.Distinct().Count() != order.Length)
                throw new InvalidOperationException("NWAE.PrivateOrder");
            // Assign each allocation immediately so partial construction can release it.
            _ordinals = new PrivateOrdinalBuffer[Math.Min(2, order.Length-1)];
            for (int i=0;i<_ordinals.Length;i++) _ordinals[i]=new(rd);
            for (int i=0;i<order.Length;i++)
            {
                var input = i == 0 ? null : _ordinals[(i-1)%2];
                var output = i == order.Length-1 ? null : _ordinals[i%2];
                switch (order[i])
                {
                    case 'N': _n=new(rd,n ?? throw new InvalidOperationException("NWAE.MissingN"),42,privateInput:input,privateOutput:output); break;
                    case 'A': _a=new(rd,a ?? throw new InvalidOperationException("NWAE.MissingA"),input,output); break;
                    case 'E': _e=new(rd,e ?? throw new InvalidOperationException("NWAE.MissingE"),input,output); break;
                    case 'W': _w=new(rd,w,input?.Buffer,output); break;
                    default: throw new InvalidOperationException("NWAE.UnknownStage");
                }
            }
            if (verify)
            {
                if (n is not null) _referenceN=new(rd,n,42);
                if (a is not null) _referenceA=new(rd,a);
                if (e is not null) _referenceE=new(rd,e);
                _referenceW=new(rd,w);
            }
            Device=rd.GetDeviceName(); SetupMs=timer.Elapsed.TotalMilliseconds;
        }
        catch { Release(); throw; }
    }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, out double canonicalMs)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if (!input.IsDense) throw new InvalidOperationException("NWAE.DenseEntryRequired");
        _ordinals[0].CheckPopulation(input.Batch,input.Count);
        token.ThrowIfCancellationRequested();
        int count=input.Count; var expected=input; double validationMs=0;
        var timer=Stopwatch.StartNew();
        for (int i=0;i<_order.Length-1;i++)
        {
            int incoming=count; double canonical, dispatch, read; int dispatches;
            switch (_order[i])
            {
                case 'N': count=_n!.ExecutePrivateStage(input.Batch,count,token,out var nm); canonical=nm.CanonicalMs; dispatch=nm.DispatchSyncMs; read=nm.ReadbackMs; dispatches=nm.Dispatches; break;
                case 'A': count=_a!.ExecutePrivateStage(input.Batch,count,token,out var am); canonical=am.CanonicalMs; dispatch=am.DispatchSyncMs; read=am.ReadbackMs; dispatches=am.Dispatches; break;
                case 'E': count=_e!.ExecutePrivateStage(input.Batch,count,token,out var em); canonical=em.CanonicalMs; dispatch=em.DispatchSyncMs; read=em.ReadbackMs; dispatches=em.Dispatches; break;
                case 'W': count=_w!.ExecutePrivateStage(input.Batch,count,token,out var wm); canonical=wm.CanonicalMs; dispatch=wm.DispatchSyncMs; read=wm.HeaderReadbackMs; dispatches=wm.Dispatches; break;
                default: throw new InvalidOperationException("NWAE.UnknownStage");
            }
            _ordinals[i%2].CheckPopulation(input.Batch,count);
            if (count>incoming) throw new InvalidDataException("NWAE.OutputExceedsInput");
            if (_verify)
            {
                double start=timer.Elapsed.TotalMilliseconds;
                expected=_order[i] switch {
                    'N'=>_referenceN!.Execute(expected,token,out _),
                    'A'=>_referenceA!.Execute(expected,token,out _),
                    'E'=>_referenceE!.Execute(expected,token,out _),
                    'W'=>_referenceW!.Execute(expected,token,out _),
                    _=>throw new InvalidOperationException("NWAE.ReferenceStage") };
                _ordinals[i%2].VerifyPopulation(input.Batch,count,expected);
                validationMs+=timer.Elapsed.TotalMilliseconds-start;
            }
            _logTotals.Record("NWAE|"+_order+"|"+_order[i]+"|PrivateOutput|identity=SelectedPlan;capacityScope=PrivateBatch",PrivateOrdinalBuffer.Capacity,incoming,count,dispatch,canonical,read,-1);
            if(Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"nwaeStage=true;order={_order};stage={_order[i]};input={incoming};output={count};dispatches={(incoming == 0 ? 0 : dispatches)};dispatchSyncMs={dispatch};headerMs={read};canonicalMs={canonical};payloadBytes=0;boundary=PrivateOrdinalCountReady;fullPopulationParity={_verify}");
        }
        double terminalDispatch, terminalCanonical;
        FamilyCandidateSet final;
        switch (_order[^1])
        {
            case 'N': final=_n!.ExecutePrivate(input.Batch,count,token,out var nm);terminalDispatch=nm.DispatchSyncMs;terminalCanonical=nm.CanonicalMs;break;
            case 'A': final=_a!.ExecutePrivate(input.Batch,count,token,out var am);terminalDispatch=am.DispatchSyncMs;terminalCanonical=am.CanonicalMs;break;
            case 'E': final=_e!.ExecutePrivate(input.Batch,count,token,out var em);terminalDispatch=em.DispatchSyncMs;terminalCanonical=em.CanonicalMs;break;
            case 'W': final=_w!.ExecutePrivate(input.Batch,count,token,out var wm);terminalDispatch=wm.DispatchSyncMs;terminalCanonical=wm.CanonicalMs;break;
            default: throw new InvalidOperationException("NWAE.Terminal");
        }
        canonicalMs=timer.Elapsed.TotalMilliseconds-validationMs;
        if (_verify)
        {
            var expectedFinal=_order[^1] switch {
                'N'=>_referenceN!.Execute(expected,token,out _),
                'A'=>_referenceA!.Execute(expected,token,out _),
                'E'=>_referenceE!.Execute(expected,token,out _),
                'W'=>_referenceW!.Execute(expected,token,out _),
                _=>throw new InvalidOperationException("NWAE.ReferenceTerminal") };
            if(!expectedFinal.EnumerateLogicalOrdinals().SequenceEqual(final.EnumerateLogicalOrdinals()))
                throw new InvalidDataException("NWAE.FinalPopulationMismatch");
        }
        _logTotals.Record("NWAE|"+_order+"|"+_order[^1]+"|PublicOutput|identity=SelectedPlan;capacityScope=PrivateBatch",PrivateOrdinalBuffer.Capacity,count,final.Count,terminalDispatch,terminalCanonical,-1,-1);
        if(Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"nwaePrivateBatch=true;order={_order};batchBase={input.Batch.BatchBase};input={input.Count};wInput={(_order[^1]=='W'?count:-1)};output={final.Count};terminal={_order[^1]};terminalInput={count};terminalDispatchSyncMs={terminalDispatch};terminalCanonicalMs={terminalCanonical};wDispatchSyncMs={(_order[^1]=='W'?terminalDispatch:0)};wCanonicalMs={(_order[^1]=='W'?terminalCanonical:0)};canonicalMs={canonicalMs};retainedBytes={_ordinals.Length*PrivateOrdinalBuffer.Capacity*4L};intermediatePayloadBytes=0;fullPopulationParity={_verify}");
        return final;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _ordinals[0].CheckCount(0); _disposed=true; _logTotals.WriteSummary(calibration:false); Release();
    }
    private void Release() => PrivateOrdinalBuffer.DisposeAll(
        _referenceW,_referenceE,_referenceA,_referenceN,_w,_e,_a,_n,
        _ordinals.Length>0?_ordinals[0]:null,_ordinals.Length>1?_ordinals[1]:null);
}
