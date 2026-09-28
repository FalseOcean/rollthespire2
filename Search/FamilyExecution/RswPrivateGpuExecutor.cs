using System.Diagnostics;
using Godot;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Only the six permutations of R/S/W. Existing bodies and two ordinal buffers.
internal sealed class RswPrivateGpuExecutor : IDisposable
{
    private readonly GpuCostSamples _logTotals = new();

    private readonly string _order;
    private readonly PrivateOrdinalBuffer[] _ordinals = new PrivateOrdinalBuffer[2];
    private readonly RelicFamilyGpuExecutor? _r, _referenceR;
    private readonly MerchantShopColorlessGpuExecutor? _s, _referenceS;
    private readonly WorldFamilyGpuExecutor? _w, _referenceW;
    private readonly bool _verify;
    private bool _disposed;
    internal string Device { get; }
    internal double SetupMs { get; }
    internal RswPrivateGpuExecutor(RenderingDevice rd, ExactSearchExecutionRequest request, RelicFamilyPlan r,
        WorldFamilyGpuPlan w, string order, bool verify)
    {
        if (order is not ("RSW" or "RWS" or "SRW" or "SWR" or "WRS" or "WSR")) throw new ArgumentException("RSW.Order");
        _order = order; _verify = verify;
        var timer = Stopwatch.StartNew();
        try
        {
            _ordinals[0] = new(rd); _ordinals[1] = new(rd);
            for (int i = 0; i < 3; i++)
            {
                var input = i == 0 ? null : _ordinals[i-1];
                var output = i == 2 ? null : _ordinals[i];
                switch (order[i])
                {
                    case 'R': _r = RelicFamilyGpuExecutor.Create(rd, request, r, input, output); break;
                    case 'S': _s = MerchantShopColorlessGpuExecutor.Create(rd, request, MerchantShopColorlessCompactionPath.AtomicAppendHostSort, input, output); break;
                    case 'W': _w = new(rd, w, input?.Buffer, output); break;
                }
            }
            SetupMs = timer.Elapsed.TotalMilliseconds; Device = rd.GetDeviceName();
            if (verify)
            {
                _referenceR = RelicFamilyGpuExecutor.Create(rd, request, r);
                _referenceS = MerchantShopColorlessGpuExecutor.Create(rd, request, MerchantShopColorlessCompactionPath.AtomicAppendHostSort);
                _referenceW = new(rd, w);
            }
        }
        catch { Release(); throw; }
    }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, out double canonicalMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!input.IsDense) throw new InvalidOperationException("RSW.DenseEntryRequired");
        _ordinals[0].CheckPopulation(input.Batch, input.Count);
        token.ThrowIfCancellationRequested();
        int count = input.Count; var expected = input; double verificationMs = 0;
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 2; i++)
        {
            int incoming = count; double dispatch, header, canonical; long headerBytes;
            switch (_order[i])
            {
                case 'R': count = _r!.ExecutePrivateStage(input.Batch, count, token, out var rm); dispatch=rm.CommandMs+rm.SubmitMs+rm.SyncMs;header=rm.ReadbackMs;headerBytes=rm.ReadbackBytes;canonical=rm.CanonicalAbi1ReadyMs;break;
                case 'S': count = _s!.ExecutePrivateStage(input.Batch, count, token, out var sm); dispatch=sm.CommandMs+sm.SubmitMs+sm.SyncMs;header=sm.ReadbackMs;headerBytes=sm.ReadbackBytes;canonical=sm.CanonicalAbi1ReadyMs;break;
                case 'W': count = _w!.ExecutePrivateStage(input.Batch, count, token, out var wm); dispatch=wm.DispatchSyncMs;header=wm.HeaderReadbackMs;headerBytes=wm.HeaderReadBytes;canonical=wm.CanonicalMs;break;
                default: throw new InvalidOperationException("RSW.Stage");
            }
            _ordinals[i].CheckPopulation(input.Batch, count);
            if (count > incoming) throw new InvalidDataException("RSW.ExpandedPopulation");
            if (_verify)
            {
                double start = timer.Elapsed.TotalMilliseconds;
                expected = Reference(_order[i], expected, token);
                _ordinals[i].VerifyPopulation(input.Batch, count, expected);
                verificationMs += timer.Elapsed.TotalMilliseconds - start;
            }
            _logTotals.Record("RSW|"+_order+"|"+_order[i]+"|PrivateOutput|identity=SelectedPlan;capacityScope=PrivateBatch",PrivateOrdinalBuffer.Capacity,incoming,count,dispatch,canonical,header,headerBytes);
            if(Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"rswStage=true;order={_order};stage={_order[i]};input={incoming};output={count};dispatchSyncMs={dispatch};headerMs={header};headerBytes={headerBytes};canonicalMs={canonical};payloadBytes=0;fullPopulationParity={_verify}");
        }
        var result = _order[2] switch
        {
            'R' => _r!.ExecutePrivate(input.Batch, count, token, out _),
            'S' => _s!.ExecutePrivate(input.Batch, count, token, out _),
            'W' => _w!.ExecutePrivate(input.Batch, count, token, out _),
            _ => throw new InvalidOperationException("RSW.Terminal")
        };
        canonicalMs = timer.Elapsed.TotalMilliseconds - verificationMs;
        if (_verify && !Reference(_order[2], expected, token).EnumerateLogicalOrdinals().SequenceEqual(result.EnumerateLogicalOrdinals()))
            throw new InvalidDataException("RSW.TerminalParity");
        _logTotals.Record("RSW|"+_order+"|CompleteAllocation|identity=SelectedPlan;capacityScope=PrivateBatch",PrivateOrdinalBuffer.Capacity,input.Count,result.Count,-1,canonicalMs,-1,-1);
        if(Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"rswPrivateBatch=true;order={_order};input={input.Count};terminalInput={count};output={result.Count};canonicalMs={canonicalMs};intermediatePayloadBytes=0;fullPopulationParity={_verify};recovery=None");
        return result;
    }
    private FamilyCandidateSet Reference(char stage, FamilyCandidateSet input, CancellationToken token) => stage switch
    {
        'R' => _referenceR!.Execute(input, token, out _),
        'S' => _referenceS!.Execute(input, token, out _),
        'W' => _referenceW!.Execute(input, token, out _),
        _ => throw new InvalidOperationException("RSW.Reference")
    };
    public void Dispose() { if (_disposed) return; _ordinals[0].CheckCount(0); _disposed=true; _logTotals.WriteSummary(calibration:false); Release(); }
    private void Release() => PrivateOrdinalBuffer.DisposeAll(_referenceW,_referenceS,_referenceR,_w,_s,_r,_ordinals[1],_ordinals[0]);
}
