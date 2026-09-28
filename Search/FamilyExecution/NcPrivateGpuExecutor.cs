using System.Diagnostics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed class NcPrivateGpuExecutor : IDisposable
{
    private readonly GpuCostSamples _logTotals = new();

    private readonly PrivateOrdinalBuffer _ordinals;
    private readonly CombatRewardGpuExecutor _c;
    private readonly NeowFamilyGpuExecutor _n;
    private readonly CombatRewardGpuExecutor? _referenceC;
    private readonly NeowFamilyGpuExecutor? _referenceN;
    private readonly bool _nFirst;
    private bool _disposed;
    internal string Device => _c.Device;
    internal double SetupMs { get; }
    internal NcPrivateGpuExecutor(RenderingDevice rd, CombatRewardGpuPlan c, NeowFamilyGpuPlan n, uint tag, bool verify, bool nFirst=false)
    {
        var timer=Stopwatch.StartNew(); _nFirst=nFirst;
        try
        {
            _ordinals=new(rd);
            _c=nFirst?new(rd,c,privateInput:_ordinals):new(rd,c,privateOutput:_ordinals);
            _n=nFirst?new(rd,n,tag,privateOutput:_ordinals):new(rd,n,tag,privateInput:_ordinals);
            if(verify)
            {
                _referenceC=new(rd,c);_referenceN=new(rd,n,tag);
            }
            SetupMs=timer.Elapsed.TotalMilliseconds;
        }
        catch {PrivateOrdinalBuffer.DisposeAll(_referenceN, _referenceC, _n, _c, _ordinals);throw;}
    }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input,CancellationToken token,out double canonicalMs)
    {
        if(!input.IsDense)throw new InvalidOperationException("NC.PrivateDenseOnly");
        _ordinals.CheckPopulation(input.Batch, input.Count);
        var timer=Stopwatch.StartNew();
        int count; FamilyCandidateSet result; CombatRewardGpuMetrics cm; NeowFamilyGpuMetrics nm;
        if (_nFirst)
        {
            count = _n.ExecutePrivateOutput(input, token, out nm);
            _ordinals.CheckPopulation(input.Batch, count);
            result = _c.ExecutePrivateInput(input.Batch, count, token, out cm);
        }
        else
        {
            count = _c.ExecutePrivate(input, token, out cm);
            _ordinals.CheckPopulation(input.Batch, count);
            result = _n.ExecutePrivate(input.Batch, count, token, out nm);
        }
        canonicalMs=timer.Elapsed.TotalMilliseconds;
        if(_referenceC is not null)
        {
            var expectedFirst=_nFirst?_referenceN!.Execute(input,token,out _):_referenceC.Execute(input,token,out _);
            _ordinals.VerifyPopulation(input.Batch,count,expectedFirst);
            var expected=_nFirst?_referenceC.Execute(expectedFirst,token,out _):_referenceN!.Execute(expectedFirst,token,out _);
            if(!expected.EnumerateLogicalOrdinals().SequenceEqual(result.EnumerateLogicalOrdinals()))throw new InvalidDataException("NC.FullFinalPopulationMismatch");
        }
        _logTotals.Record("NC|N|orderNFirst="+_nFirst+"|identity=SelectedPlan;capacityScope=PrivateBatch",PrivateOrdinalBuffer.Capacity,_nFirst?input.Count:count,_nFirst?count:result.Count,nm.DispatchSyncMs,nm.CanonicalMs,nm.ReadbackMs,nm.ReadbackBytes);
        _logTotals.Record("NC|C|orderNFirst="+_nFirst+"|identity=SelectedPlan;capacityScope=PrivateBatch",PrivateOrdinalBuffer.Capacity,_nFirst?count:input.Count,_nFirst?result.Count:count,cm.DispatchSyncMs,cm.CanonicalMs,cm.ReadbackMs,cm.ReadbackBytes);
        if(Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"ncPrivateBatch=true;nFirst={_nFirst};input={input.Count};intermediate={count};output={result.Count};cCanonicalMs={cm.CanonicalMs};cDispatches={cm.Dispatches};cUploadMs={cm.UploadMs};cDispatchSyncMs={cm.DispatchSyncMs};cHeaderMs={cm.ReadbackMs};cReadbackBytes={cm.ReadbackBytes};nCanonicalMs={nm.CanonicalMs};nDispatchSyncMs={nm.DispatchSyncMs};nReadbackMs={nm.ReadbackMs};nReadbackBytes={nm.ReadbackBytes};canonicalMs={canonicalMs};intermediatePayloadBytes=0;retainedBytes={PrivateOrdinalBuffer.Capacity*4L};fullPopulationParity={_referenceC is not null}");
        return result;
    }
    public void Dispose(){if (_disposed) return; _ordinals.CheckCount(0); _disposed=true; _logTotals.WriteSummary(calibration:false); PrivateOrdinalBuffer.DisposeAll(_referenceN, _referenceC, _n, _c, _ordinals);}
}
