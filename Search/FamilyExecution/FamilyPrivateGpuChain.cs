using System.Diagnostics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;


// Reuse the single-player private ordinal transport. No RNG or transaction
// state crosses stages; the only public output is sorted ABI1 at the end.
internal sealed partial class FamilyPrivateGpuChain : IDisposable
{
    private readonly RenderingDevice _rd;
    private readonly FamilyGpuComputeUtility.ShaderReuse _modules;
    private readonly IFamilyInvocation[] _families;
    private readonly NeowFamilyGpuExecutor? _denseScratchDonor;
    private readonly PrivateOrdinalBuffer?[] _buffers;
    private readonly FamilyPrivateGpuExecution?[] _executors;
    private readonly int _ownerThread = System.Environment.CurrentManagedThreadId;
    private PrivateOrdinalBuffer? _incoming;
    private FamilyPrivateGpuExecution? _compactFirst;
    private bool _disposed;
    internal string Device { get; }
    internal double SetupMs { get; private set; }
    internal long ReadbackBytes { get; private set; }
    internal double DispatchMs { get; private set; }

    internal FamilyPrivateGpuChain(RenderingDevice rd, IFamilyInvocation[] families,
        NeowFamilyGpuExecutor? denseScratchDonor = null)
    {
        if (families.Length < 2 || families.Any(f => !f.CanBindPrivateSerial))
            throw new ArgumentException("FamilyParty.PrivateChainNotAdmitted", nameof(families));
        _rd = rd; _families = families; _denseScratchDonor = denseScratchDonor; Device = rd.GetDeviceName();
        _modules = new(rd);
        _buffers = new PrivateOrdinalBuffer?[Math.Min(2, families.Length - 1)];
        _executors = new FamilyPrivateGpuExecution?[families.Length];
    }

    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token,
        SmallCandidateExactPolicy? smallCandidateExact = null, FamilyPrivateGpuOutcome? preparedPrefix = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CheckOwner();
        token.ThrowIfCancellationRequested();
        if (input.Batch.BatchCandidateCount > PrivateOrdinalBuffer.Capacity)
            throw new InvalidDataException("FamilyParty.PrivateInputOutsideCapacity");
        if (preparedPrefix is not null && (!input.IsDense || !CanGroupDensePrefix))
            throw new InvalidOperationException("FamilyParty.PreparedPrefixRoleMismatch");
        int count = input.Count;
        FamilyCandidateSet? result = null, smallOutput = null;
        for (int i = 0; i < _executors.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (count == 0 || smallCandidateExact is not null && SmallCandidateExactPolicy.ShouldHandoff(count))
            {
                var handoff = count == 0 ? FamilyCandidateSet.FromSortedAbi1(input.Batch, []) :
                    i == 0 ? input.IsDense
                        ? FamilyCandidateSet.FromSortedAbi1(input.Batch, input.EnumerateLogicalOrdinals().ToArray())
                        : input
                    : smallOutput ?? (_buffers[(i - 1) % 2] ?? throw new InvalidDataException("FamilyParty.PrivateHandoffBufferMissing"))
                        .ReadAbi1ForExact(input.Batch, count);
                token.ThrowIfCancellationRequested();
                if (i > 0 && count > 0 && smallOutput is null) ReadbackBytes += checked(count * 4L);
                smallCandidateExact?.Observe("Party:" + string.Join(',', _families.Take(i).Select(f => f.FamilyId)),
                    _families.Skip(i).Select(f => f.FamilyId).ToArray(), count);
                return handoff;
            }
            var executor = EnsureExecutor(i, i == 0 && !input.IsDense);
            if (i == 0 && !input.IsDense)
            {
                _incoming!.CheckPopulation(input.Batch, count);
                FamilyGpuComputeUtility.UpdateBytes(_rd, _incoming.Buffer,
                    FamilyGpuComputeUtility.PackAbi1UInt32(input), "FamilyParty.PrivateInput");
            }
            var output = i == 0 && preparedPrefix is { } prefix
                ? executor.CompletePrepared(input.Batch, count, prefix) : executor.Execute(input.Batch, count, token);
            count = output.Count; result = output.PublicOutput; smallOutput = output.SmallOutput;
            // Prepared prefix transport was already charged by its group,
            // including a slot discarded after another root reaches the target.
            if (i != 0 || preparedPrefix is null) ReadbackBytes += output.ReadbackBytes;
            DispatchMs += output.DispatchSyncMs;
        }
        return result ?? throw new InvalidDataException("FamilyParty.PrivateFinalOutputMissing");
    }

    private FamilyPrivateGpuExecution EnsureExecutor(int index, bool compactFirst)
    {
        if (compactFirst && _compactFirst is { } compact) return compact;
        if (!compactFirst && _executors[index] is { } existing) return existing;
        long started = Stopwatch.GetTimestamp();
        try
        {
            PrivateOrdinalBuffer? incoming = index == 0
                ? compactFirst ? (_incoming ??= new(_rd)) : null
                : _buffers[(index - 1) % 2] ?? throw new InvalidDataException("FamilyParty.PrivateInputBufferMissing");
            PrivateOrdinalBuffer? outgoing = index == _families.Length - 1 ? null :
                (_buffers[index % 2] ??= new(_rd));
            using var reuse = _modules.Activate();
            var executor = index == 0 && !compactFirst && _denseScratchDonor is not null && _families[index] is NeowFamily neow
                ? neow.BindNeowPrivate(_rd, incoming, outgoing, _denseScratchDonor)
                : _families[index].BindPrivateSerial(_rd, incoming, outgoing);
            if (compactFirst) _compactFirst = executor;
            else _executors[index] = executor;
            return executor;
        }
        finally { SetupMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

    private void CheckOwner()
    {
        if (_ownerThread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("FamilyParty.PrivateOwnerMismatch");
    }

    public void Dispose()
    {
        if (_disposed) return;
        CheckOwner();
        _disposed = true;
        PrivateOrdinalBuffer.DisposeAll(_executors.AsEnumerable().Reverse().Cast<IDisposable?>()
            .Concat(new IDisposable?[] { _compactFirst, _incoming }).Concat(_buffers).Append(_modules).ToArray());
    }
}
