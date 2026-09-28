using System.Diagnostics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;


// Reuse the single-player private ordinal transport. No RNG or transaction
// state crosses stages; the only public output is sorted ABI1 at the end.
internal sealed class FamilyPrivateGpuChain : IDisposable
{
    private readonly RenderingDevice _rd;
    private readonly FamilyGpuComputeUtility.ShaderReuse _modules;
    private readonly IFamilyInvocation[] _families;
    private readonly PrivateOrdinalBuffer[] _buffers;
    private readonly FamilyPrivateGpuExecution[] _executors;
    private PrivateOrdinalBuffer? _incoming;
    private FamilyPrivateGpuExecution? _compactFirst;
    private bool _disposed;
    internal string Device { get; }
    internal double SetupMs { get; private set; }
    internal long ReadbackBytes { get; private set; }
    internal double DispatchMs { get; private set; }

    internal FamilyPrivateGpuChain(RenderingDevice rd, IFamilyInvocation[] families)
    {
        if (families.Length < 2 || families.Any(f => !f.CanBindPrivateSerial))
            throw new ArgumentException("FamilyParty.PrivateChainNotAdmitted", nameof(families));
        _rd = rd; _families = families; Device = rd.GetDeviceName();
        _modules = new(rd);
        _buffers = new PrivateOrdinalBuffer[Math.Min(2, families.Length - 1)];
        _executors = new FamilyPrivateGpuExecution[families.Length];
        var timer = Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < _buffers.Length; i++) _buffers[i] = new(rd);
            using var reuse = _modules.Activate();
            for (int i = 0; i < families.Length; i++)
                _executors[i] = families[i].BindPrivateSerial(rd,
                    i == 0 ? null : _buffers[(i - 1) % 2], i == families.Length - 1 ? null : _buffers[i % 2]);
            SetupMs = timer.Elapsed.TotalMilliseconds;
        }
        catch (Exception failure)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, Dispose, "FamilyParty.PrivateChain");
            throw;
        }
    }

    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        _buffers[0].CheckPopulation(input.Batch, input.Count);
        if (!input.IsDense)
        {
            if (_incoming is null)
            {
                var timer = Stopwatch.StartNew();
                _incoming = new(_rd);
                using var reuse = _modules.Activate();
                _compactFirst = _families[0].BindPrivateSerial(_rd, _incoming, _buffers[0]);
                SetupMs += timer.Elapsed.TotalMilliseconds;
            }
            _incoming.CheckPopulation(input.Batch, input.Count);
            FamilyGpuComputeUtility.Update(_rd, _incoming.Buffer,
                input.ExportAbi1().ToArray().Select(i => checked((uint)i)).ToArray(), "FamilyParty.PrivateInput");
        }
        int count = input.Count;
        FamilyCandidateSet? result = null;
        for (int i = 0; i < _executors.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (count == 0) return FamilyCandidateSet.FromSortedAbi1(input.Batch, []);
            var executor = i == 0 && !input.IsDense ? _compactFirst! : _executors[i];
            var output = executor.Execute(input.Batch, count, token);
            count = output.Count; result = output.PublicOutput;
            ReadbackBytes += output.ReadbackBytes; DispatchMs += output.DispatchSyncMs;
        }
        return result ?? throw new InvalidDataException("FamilyParty.PrivateFinalOutputMissing");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PrivateOrdinalBuffer.DisposeAll(_executors.AsEnumerable().Reverse().Cast<IDisposable?>()
            .Concat(new IDisposable?[] { _compactFirst, _incoming }).Concat(_buffers).Append(_modules).ToArray());
    }
}
